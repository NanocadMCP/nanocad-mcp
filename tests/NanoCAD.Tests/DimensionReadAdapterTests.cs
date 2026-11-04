using System.Collections;
using System.Reflection;
using System.Reflection.Emit;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using System.Text.Json;
using NanoCAD.Contracts;

internal static class DimensionReadAdapterTests
{
    private static void Check(bool value, string why) { if (!value) throw new Exception(why); }
    private sealed class PresenceEnumerator(bool present, bool fail = false) : IEnumerator, IDisposable
    {
        public int Moves, Reads; public bool Disposed;
        public object Current { get { Reads++; throw new Exception("Payload must not be read."); } }
        public bool MoveNext() { Moves++; if (fail) throw new IOException("native enumeration fault"); return present; }
        public void Reset() => throw new NotSupportedException();
        public void Dispose() => Disposed = true;
    }
    public static async Task Run(Func<string, Func<Task>, Task> test, Func<string, Func<Task>, Task>? hostTest = null)
    {
        await test("dimension XData presence does not allocate or inspect payload elements", () =>
        {
            foreach (bool present in new[] { false, true })
            {
                var cursor = new PresenceEnumerator(present); int checks = 0;
                Check(DimensionMetadataEnumeration.HasAny(cursor, () => checks++) == present, "presence");
                Check(cursor.Moves == 1 && cursor.Reads == 0 && cursor.Disposed && checks == 2, "bounded enumeration/disposal");
            }
            Check(DimensionMetadataEnumeration.HasAny(new object[] { 1 }.GetEnumerator(), () => { }), "non-disposable IEnumerator supported");
            return Task.CompletedTask;
        });
        await test("dimension XData enumeration disposes on native fault and either cancellation boundary", () =>
        {
            foreach (int cancel in new[] { 1, 2, 0 })
            {
                var cursor = new PresenceEnumerator(true, fail: cancel == 0); int checks = 0;
                try { DimensionMetadataEnumeration.HasAny(cursor, () => { if (++checks == cancel) throw new OperationCanceledException(); }); throw new Exception("Expected fault"); }
                catch (OperationCanceledException) { Check(cancel != 0, "wrong fault"); }
                catch (IOException) { Check(cancel == 0, "wrong fault"); }
                Check(cursor.Disposed && cursor.Reads == 0 && cursor.Moves == (cancel == 1 ? 0 : 1), "resource leak or unbounded enumeration");
            }
            return Task.CompletedTask;
        });
        await (hostTest ?? test)("compiled dimension reader reaches all reviewed getters and only a ForRead dependency open", () =>
        {
            Audit();
            return Task.CompletedTask;
        });
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
            _ => h.Kind.ToString()
        };
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
            string root = Root(method.GetDeclaringType()); bool reader = root == "DimensionStyleMetadataReader";
            if (reader) readerMethods++;
            var il = pe.GetMethodBody(method.RelativeVirtualAddress).GetILBytes()!; OpCode previous = default;
            for (int offset = 0; offset < il.Length;)
            {
                ushort raw = il[offset++]; if (raw == 0xfe) raw = (ushort)(0xfe00 | il[offset++]); var opcode = opcodes[raw];
                if (opcode.OperandType == OperandType.InlineMethod)
                {
                    var target = Target(MetadataTokens.EntityHandle(BitConverter.ToInt32(il, offset)));
                    if (!reader && target.Type == "DimensionStyleMetadataReader") callers.Add(root);
                    if (reader)
                    {
                        calls.Add(target.Type + "." + target.Method);
                        Check(!target.Method.StartsWith("set_", StringComparison.Ordinal) || target.Type == "DimensionVariables", "native setter: " + target);
                        Check(target.Method is not ("Commit" or "Clone" or "CopyFrom" or "LockDocument" or "StartTransaction" or "StartOpenCloseTransaction" or
                            "SendStringToExecute" or "Add" or "GetArrowId" or "AsArray" or "get_Current" or "DoEvents"), "unexpected mutation/allocation/context: " + target);
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
        // Defaults, lifecycle and rename preflight read detached/current styles
        // through the same reader; their mutation code is outside this audit.
        Check(readerMethods > 0 && opens == 1 && callers.SetEquals(new[] {
            "StructureReader", "CadDimensionStyleWriter", "CadDimensionStyleDefaults364",
            "CadStructureLifecycleWriter", "CadUsedDimensionStyleRename"
        }), "unexpected reader integration: " + string.Join(", ", callers));
        using var catalog = JsonDocument.Parse(File.ReadAllText("tests/fixtures/dimension-style-read-catalog.json"));
        foreach (var field in catalog.RootElement.GetProperty("fields").EnumerateArray())
            Check(calls.Contains("DimStyleTableRecord.get_" + field.GetProperty("sdk_property").GetString()), "missing SDK getter " + field);
        foreach (var field in catalog.RootElement.GetProperty("excluded_getters").EnumerateObject())
            Check(!calls.Contains("DimStyleTableRecord.get_" + field.Name), "excluded native getter " + field.Name);
        Check(!calls.Contains("ResultBuffer.op_Equality") && !calls.Contains("Color.op_Equality"), "null check invoked native wrapper equality");
        Check(calls.Contains("DimensionMetadataEnumeration.HasAny") && calls.Contains("DimensionStyleMetadata.Validate") && calls.Contains("IDisposable.Dispose"), "missing bounded conversion/disposal");
        File.WriteAllText(Path.Combine(Environment.GetEnvironmentVariable("NANOCAD_MCP_HOME")!, "dimension-read-adapter-audit.json"),
            JsonSerializer.Serialize(new
            {
                reader_methods = readerMethods,
                caller_roots = callers,
                for_read_open_sites = opens,
                calls,
                selected_getters = 73,
                native_executed = false,
                scope = "PE/IL only; does not prove transitive native behavior or exception safety of SDK internals."
            }, Wire.Json));
    }
}
