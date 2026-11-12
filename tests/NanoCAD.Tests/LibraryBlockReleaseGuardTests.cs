using System.Reflection;
using System.Reflection.Emit;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;

internal static class LibraryBlockReleaseGuardTests
{
    sealed record Call(int Offset, EntityHandle Handle, string Type, string Method);
    sealed record Body(List<Call> Calls, List<string> Strings);
    static void Need(bool condition, string message) { if (!condition) throw new Exception(message); }
    internal static async Task Run(Func<string, Func<Task>, Task> hostTest)
    {
        await hostTest("compiled production DWG library dispatch reaches native clone and verified readback around commit", () =>
        {
            Audit(previewOnly: false); return Task.CompletedTask;
        });
        await hostTest("compiled DWG library preflight retains complete source validation without resident mutation", () =>
        {
            Audit(previewOnly: true); return Task.CompletedTask;
        });
    }
    static void Audit(bool previewOnly)
    {
        using var file = File.OpenRead("src/NanoCADBridge/bin/Release/net6.0-windows/NanoCADBridge.dll");
        using var pe = new PEReader(file); var metadata = pe.GetMetadataReader();
        var opcodes = typeof(OpCodes).GetFields(BindingFlags.Public | BindingFlags.Static)
            .Select(field => (OpCode)field.GetValue(null)!).ToDictionary(code => unchecked((ushort)code.Value));
        string TypeName(EntityHandle handle)
        {
            if (handle.Kind == HandleKind.TypeSpecification)
            {
                var reader = metadata.GetBlobReader(metadata.GetTypeSpecification((TypeSpecificationHandle)handle).Signature);
                Need(reader.ReadSignatureTypeCode() == SignatureTypeCode.GenericTypeInstance && reader.ReadSignatureTypeCode() == SignatureTypeCode.TypeHandle,
                    "Unexpected compiled generic call target.");
                return TypeName(reader.ReadTypeHandle());
            }
            if (handle.Kind == HandleKind.TypeDefinition)
            {
                var type = metadata.GetTypeDefinition((TypeDefinitionHandle)handle); string name = metadata.GetString(type.Name);
                if (!type.GetDeclaringType().IsNil) return TypeName(type.GetDeclaringType()) + "+" + name;
                string space = metadata.GetString(type.Namespace); return space.Length == 0 ? name : space + "." + name;
            }
            Need(handle.Kind == HandleKind.TypeReference, "Unexpected compiled call type.");
            var reference = metadata.GetTypeReference((TypeReferenceHandle)handle);
            return metadata.GetString(reference.Namespace) + "." + metadata.GetString(reference.Name);
        }
        (string Type, string Method) Target(EntityHandle handle)
        {
            if (handle.Kind == HandleKind.MethodSpecification) return Target(metadata.GetMethodSpecification((MethodSpecificationHandle)handle).Method);
            if (handle.Kind == HandleKind.MethodDefinition)
            { var method = metadata.GetMethodDefinition((MethodDefinitionHandle)handle); return (TypeName(method.GetDeclaringType()), metadata.GetString(method.Name)); }
            var member = metadata.GetMemberReference((MemberReferenceHandle)handle); return (TypeName(member.Parent), metadata.GetString(member.Name));
        }
        Body Read(MethodDefinition method)
        {
            var calls = new List<Call>(); var strings = new List<string>();
            if (method.RelativeVirtualAddress == 0) return new(calls, strings);
            var il = pe.GetMethodBody(method.RelativeVirtualAddress).GetILBytes()!;
            for (int offset = 0; offset < il.Length;)
            {
                int instruction = offset;
                ushort raw = il[offset++]; if (raw == 0xfe) raw = (ushort)(0xfe00 | il[offset++]); var code = opcodes[raw];
                Need(code != OpCodes.Calli, "The library bridge unexpectedly bypasses its managed native API binding.");
                if (code.OperandType == OperandType.InlineMethod)
                {
                    var handle = MetadataTokens.EntityHandle(BitConverter.ToInt32(il, offset)); var target = Target(handle);
                    calls.Add(new(instruction, handle, target.Type, target.Method));
                }
                if (code.OperandType == OperandType.InlineString) strings.Add(metadata.GetUserString(MetadataTokens.UserStringHandle(BitConverter.ToInt32(il, offset) & 0xffffff)));
                offset += code.OperandType switch
                {
                    OperandType.InlineNone => 0,
                    OperandType.ShortInlineBrTarget or OperandType.ShortInlineI or OperandType.ShortInlineVar => 1,
                    OperandType.InlineVar => 2,
                    OperandType.InlineI8 or OperandType.InlineR => 8,
                    OperandType.InlineSwitch => 4 + 4 * BitConverter.ToInt32(il, offset),
                    _ => 4
                };
                Need(offset <= il.Length, "Invalid compiled library IL.");
            }
            return new(calls, strings);
        }
        const string library = "NanoCADBridge.CadLibraryBlockDwg";
        MethodDefinitionHandle Method(string type, string name)
        {
            var owner = metadata.TypeDefinitions.Single(handle => TypeName(handle) == type);
            return metadata.GetTypeDefinition(owner).GetMethods().Single(handle => metadata.GetString(metadata.GetMethodDefinition(handle).Name) == name);
        }
        List<Call> Reachable(MethodDefinitionHandle start)
        {
            var pending = new Queue<MethodDefinitionHandle>(); var visited = new HashSet<MethodDefinitionHandle>(); var calls = new List<Call>();
            pending.Enqueue(start);
            while (pending.TryDequeue(out var next))
            {
                if (!visited.Add(next)) continue;
                foreach (var call in Read(metadata.GetMethodDefinition(next)).Calls)
                {
                    calls.Add(call); var handle = call.Handle;
                    while (handle.Kind == HandleKind.MethodSpecification) handle = metadata.GetMethodSpecification((MethodSpecificationHandle)handle).Method;
                    if (handle.Kind != HandleKind.MethodDefinition) continue;
                    string type = TypeName(metadata.GetMethodDefinition((MethodDefinitionHandle)handle).GetDeclaringType());
                    if (type == library || type.StartsWith(library + "+", StringComparison.Ordinal)) pending.Enqueue((MethodDefinitionHandle)handle);
                }
            }
            return calls;
        }
        static bool ResidentWrite(Call call) =>
            call.Type == "NanoCAD.Ipc.WriteJob" && call.Method is "BeginCommit" or "RecordCommit" ||
            call.Type == "Teigha.DatabaseServices.Database" && call.Method == "WblockCloneObjects" ||
            call.Type == "Teigha.DatabaseServices.Transaction" && call.Method == "Commit" ||
            call.Type == "Teigha.DatabaseServices.DBObject" && call.Method == "Erase" ||
            call.Type == "Teigha.DatabaseServices.BlockTable" && call.Method == "Add" ||
            call.Type == "Teigha.DatabaseServices.BlockTableRecord" && call.Method == "AppendEntity";
        var preview = Read(metadata.GetMethodDefinition(Method(library, "Preflight")));
        Need(preview.Calls.Any(call => call.Type == library + "+Source" && call.Method == ".ctor") &&
            preview.Calls.Any(call => call.Type == library && call.Method == "ResolveTarget") &&
            preview.Calls.Any(call => call.Type == library && call.Method == "ProjectedReceipt"),
            "The compiled read-only preflight lost target, complete source or receipt validation.");
        if (previewOnly)
        {
            var calls = Reachable(Method(library, "Preflight"));
            Need(calls.Any(call => call.Type == "Teigha.DatabaseServices.Database" && call.Method == "ReadDwgFile"),
                "The preview no longer reads the actual source DWG.");
            Need(!calls.Any(ResidentWrite), "Read-only source preflight reaches resident clone, append, erase or commit work.");
            return;
        }
        var execute = Read(metadata.GetMethodDefinition(Method(library, "Execute")));
        Need(!execute.Strings.Contains("native_clone_metadata_preservation_pending") &&
            execute.Calls.Any(call => call.Type == library + "+Source" && call.Method == ".ctor") &&
            execute.Calls.Any(call => call.Type == "Teigha.DatabaseServices.Database" && call.Method == "WblockCloneObjects"),
            "The normal compiled writer still substitutes a quarantine or omits the accepted source-clone body.");
        var readbacks = execute.Calls.Where(call => call.Method.Contains("g__Readback|", StringComparison.Ordinal)).ToArray();
        var begin = execute.Calls.Single(call => call.Type == "NanoCAD.Ipc.WriteJob" && call.Method == "BeginCommit");
        var commit = execute.Calls.Single(call => call.Type == "Teigha.DatabaseServices.Transaction" && call.Method == "Commit");
        var record = execute.Calls.Single(call => call.Type == "NanoCAD.Ipc.WriteJob" && call.Method == "RecordCommit");
        Need(readbacks.Length == 2 && readbacks[0].Offset < begin.Offset && begin.Offset < commit.Offset &&
            commit.Offset < readbacks[1].Offset && readbacks[1].Offset < record.Offset,
            "The compiled native writer must verify before commit and again before recording success.");
        var native = Reachable(Method(library, "Execute"));
        Need(native.Any(call => call.Type == library && call.Method == "SameMetadata") &&
            native.Any(call => call.Type == library + "+Source" && call.Method == "Verify"),
            "The production writer lost mapped-metadata or retained-source verification.");
        var dispatcher = Read(metadata.GetMethodDefinition(Method("NanoCADBridge.CadWriter", "Execute")));
        var dispatch = dispatcher.Calls.Single(call => call.Type == library && call.Method == "Execute");
        var generalTransaction = dispatcher.Calls.Last(call => call.Type == "Teigha.DatabaseServices.TransactionManager" && call.Method == "StartTransaction");
        Need(dispatch.Offset < generalTransaction.Offset && dispatcher.Calls.Any(call => call.Type == library && call.Method == "Handles"),
            "The library request no longer dispatches to its owning writer before the general transaction.");
    }
}
