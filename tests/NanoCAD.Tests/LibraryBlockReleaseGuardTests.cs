using System.Reflection;
using System.Reflection.Emit;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;

internal static class LibraryBlockReleaseGuardTests
{
    static void Need(bool condition, string message) { if (!condition) throw new Exception(message); }
    internal static async Task Run(Func<string, Func<Task>, Task> hostTest)
    {
        await hostTest("compiled default DWG library writer refuses before any database work and retains read-only preflight", () =>
        {
            Audit(); return Task.CompletedTask;
        });
    }
    static void Audit()
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
        (List<(string Type, string Method)> Calls, List<string> Strings) Read(MethodDefinition method)
        {
            var calls = new List<(string Type, string Method)>(); var strings = new List<string>();
            var il = pe.GetMethodBody(method.RelativeVirtualAddress).GetILBytes()!;
            for (int offset = 0; offset < il.Length;)
            {
                ushort raw = il[offset++]; if (raw == 0xfe) raw = (ushort)(0xfe00 | il[offset++]); var code = opcodes[raw];
                Need(code != OpCodes.Calli, "The default release guard contains an indirect native invocation.");
                if (code.OperandType == OperandType.InlineMethod) calls.Add(Target(MetadataTokens.EntityHandle(BitConverter.ToInt32(il, offset))));
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
                Need(offset <= il.Length, "Invalid compiled release guard IL.");
            }
            return (calls, strings);
        }
        var owner = metadata.TypeDefinitions.Single(handle => TypeName(handle) == "NanoCADBridge.CadLibraryBlockDwg");
        MethodDefinition Method(string name) => metadata.GetMethodDefinition(metadata.GetTypeDefinition(owner).GetMethods()
            .Single(handle => metadata.GetString(metadata.GetMethodDefinition(handle).Name) == name));
        var execute = Read(Method("Execute"));
        Need(execute.Calls.Count(call => call.Type == "NanoCAD.Ipc.WriteJob" && call.Method == "Error") == 1 &&
            execute.Strings.Contains("CAPABILITY_UNAVAILABLE") && execute.Strings.Contains("not_started"),
            "The compiled default writer does not return an explicit not-started capability refusal.");
        Need(execute.Calls.All(call => call.Type == "NanoCAD.Ipc.WriteJob" && call.Method == "Error" ||
            call.Type.StartsWith("<>f__AnonymousType", StringComparison.Ordinal) && call.Method == ".ctor"),
            "The default library writer reaches work beyond constructing its refusal, including potential transaction/clone helpers.");
        var preview = Read(Method("Preflight"));
        Need(preview.Calls.Any(call => call.Type == "NanoCADBridge.CadLibraryBlockDwg+Source" && call.Method == ".ctor") &&
            preview.Calls.Any(call => call.Type == "NanoCADBridge.CadLibraryBlockDwg" && call.Method == "ProjectedReceipt"),
            "The released bridge lost the existing full read-only source preflight.");
    }
}
