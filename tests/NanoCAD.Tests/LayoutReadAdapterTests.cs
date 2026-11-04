using System.Reflection;
using System.Reflection.Emit;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using System.Text.Json;
using NanoCAD.Contracts;

internal static class LayoutReadAdapterTests
{
    private static void Check(bool value, string why) { if (!value) throw new Exception(why); }
    public static async Task Run(Func<string, Func<Task>, Task> test)
    {
        await test("compiled layout reader uses reviewed getters and ForRead dependencies without plot validation or mutations", () => { Audit(); return Task.CompletedTask; });
    }
    private static void Audit()
    {
        using var file = File.OpenRead("src/NanoCADBridge/bin/Release/net6.0-windows/NanoCADBridge.dll");
        using var pe = new PEReader(file); var metadata = pe.GetMetadataReader();
        var opcodes = typeof(OpCodes).GetFields(BindingFlags.Public | BindingFlags.Static).Select(f => (OpCode)f.GetValue(null)!).ToDictionary(c => (ushort)c.Value);
        string TypeName(EntityHandle h) => h.Kind switch
        {
            HandleKind.TypeDefinition => metadata.GetString(metadata.GetTypeDefinition((TypeDefinitionHandle)h).Name),
            HandleKind.TypeReference => metadata.GetString(metadata.GetTypeReference((TypeReferenceHandle)h).Name),
            HandleKind.TypeSpecification => GenericOwner((TypeSpecificationHandle)h),
            _ => h.Kind.ToString()
        };
        string GenericOwner(TypeSpecificationHandle h)
        {
            var signature = metadata.GetBlobReader(metadata.GetTypeSpecification(h).Signature);
            if (signature.ReadSignatureTypeCode() != SignatureTypeCode.GenericTypeInstance ||
                signature.ReadSignatureTypeCode() != SignatureTypeCode.TypeHandle) return HandleKind.TypeSpecification.ToString();
            var owner = signature.ReadTypeHandle();
            if (owner.Kind != HandleKind.TypeReference) return HandleKind.TypeSpecification.ToString();
            var type = metadata.GetTypeReference((TypeReferenceHandle)owner);
            return metadata.GetString(type.Namespace) + "." + metadata.GetString(type.Name);
        }
        string Root(TypeDefinitionHandle h)
        {
            var type = metadata.GetTypeDefinition(h);
            return type.GetDeclaringType().IsNil ? metadata.GetString(type.Name) : Root(type.GetDeclaringType());
        }
        (string Type, string Method) Target(EntityHandle h)
        {
            if (h.Kind == HandleKind.MethodSpecification) return Target(metadata.GetMethodSpecification((MethodSpecificationHandle)h).Method);
            if (h.Kind == HandleKind.MethodDefinition) { var m = metadata.GetMethodDefinition((MethodDefinitionHandle)h); return (TypeName(m.GetDeclaringType()), metadata.GetString(m.Name)); }
            var member = metadata.GetMemberReference((MemberReferenceHandle)h); return (TypeName(member.Parent), metadata.GetString(member.Name));
        }
        var calls = new SortedSet<string>(StringComparer.Ordinal); var callers = new SortedSet<string>(StringComparer.Ordinal);
        int opens = 0, readerMethods = 0;
        foreach (var handle in metadata.MethodDefinitions)
        {
            var method = metadata.GetMethodDefinition(handle); if (method.RelativeVirtualAddress == 0) continue;
            string root = Root(method.GetDeclaringType()); bool reader = root == "LayoutMetadataReader";
            if (reader) readerMethods++;
            var il = pe.GetMethodBody(method.RelativeVirtualAddress).GetILBytes()!; OpCode previous = default;
            for (int offset = 0; offset < il.Length;)
            {
                ushort raw = il[offset++]; if (raw == 0xfe) raw = (ushort)(0xfe00 | il[offset++]); var opcode = opcodes[raw];
                if (opcode.OperandType == OperandType.InlineMethod)
                {
                    var target = Target(MetadataTokens.EntityHandle(BitConverter.ToInt32(il, offset)));
                    if (!reader && target.Type == "LayoutMetadataReader") callers.Add(root);
                    if (reader)
                    {
                        calls.Add(target.Type + "." + target.Method);
                        Check(!target.Method.StartsWith("set_", StringComparison.Ordinal) && !target.Method.StartsWith("Set", StringComparison.Ordinal), "native setter: " + target);
                        Check(target.Method is not ("LoadLineTypeFile" or "Commit" or "Clone" or "CopyFrom" or "LockDocument" or "StartTransaction" or "StartOpenCloseTransaction" or
                            "SendStringToExecute" or "GetArrowId" or "AsArray" or "get_Current" or "DoEvents"), "unexpected mutation/allocation/context: " + target);
                        // Frozen-layer observations accumulate in a managed List;
                        // an Add on any other owner remains a forbidden mutation.
                        Check(target.Method != "Add" || target.Type == "System.Collections.Generic.List`1", "unexpected Add owner: " + target);
                        if (target.Method == "GetObject") { opens++; Check(target.Type == "Transaction" && previous == OpCodes.Ldc_I4_0, "dependency must open ForRead(0)"); }
                    }
                }
                int size = opcode.OperandType switch
                {
                    OperandType.InlineNone => 0,
                    OperandType.ShortInlineBrTarget or OperandType.ShortInlineI or OperandType.ShortInlineVar => 1,
                    OperandType.InlineVar => 2,
                    OperandType.InlineI8 or OperandType.InlineR => 8,
                    OperandType.InlineSwitch => 4 + 4 * BitConverter.ToInt32(il, offset),
                    _ => 4
                };
                offset += size; Check(offset <= il.Length, "invalid IL boundary"); previous = opcode;
            }
        }
        Check(readerMethods > 0 && opens == 2 && callers.SetEquals(new[] {
            "CadLayoutWriter", "CadLayoutEditor", "CadEmptyLayoutDeletionWriter", "CadViewportCreator",
            "CadViewportWriter", "CadViewportClipping358", "CadViewportLifecycleWriter", "StructureReader"
        }), "unexpected reader integration: " + string.Join(", ", callers));
        foreach (string method in new[] { "get_CustomScale", "get_StandardScale", "get_LensLength", "get_FrontClipOn", "get_BackClipOn", "get_FrontClipAtEyeOn", "get_FrontClipDistance", "get_BackClipDistance", "get_NonRectClipOn", "get_NonRectClipEntityId", "GetFrozenLayers" })
            Check(calls.Contains("Viewport." + method), "missing SDK method " + method);
        Check(!calls.Any(c => c.Contains("PlotSettingsValidator") || c.Contains("LayoutManager") || c.Contains("PlotEngine") || c.Contains("get_Thumbnail") || c.Contains("get_LinkedToSheetView") || c.Contains("get_ViewOrthographic")), "unsafe/unimplemented SDK path");
        Check(!calls.Contains("ResultBuffer.op_Equality") && !calls.Contains("Color.op_Equality"), "null check invoked native wrapper equality");
        Check(calls.Contains("LayoutMetadata.Validate") && calls.Contains("IDisposable.Dispose"), "missing bounded conversion/disposal");
        File.WriteAllText(Path.Combine(Environment.GetEnvironmentVariable("NANOCAD_MCP_HOME")!, "layout-read-adapter-audit.json"),
            JsonSerializer.Serialize(new
            {
                reader_methods = readerMethods,
                caller_roots = callers,
                for_read_open_sites = opens,
                calls,
                selected_viewport_methods = 11,
                native_executed = false,
                scope = "PE/IL only; does not prove transitive native behavior or exception safety of SDK internals."
            }, Wire.Json));
    }
}


