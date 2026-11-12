using System.Reflection;
using System.Reflection.Emit;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using System.Text.Json;
using NanoCAD.Contracts;

internal static class DrawingViewCutExclusionTests
{
    sealed record Call(int Offset, string Type, string Method);
    sealed record Body(Call[] Calls, string[] Strings);
    static void Need(bool condition, string message) { if (!condition) throw new Exception(message); }
    static JsonElement ObjectAlternative(JsonElement value) => value.GetProperty("anyOf").EnumerateArray()
        .Single(item => item.TryGetProperty("type", out var type) && type.GetString() == "object");
    internal static async Task Run(Func<string, Func<Task>, Task> test, Func<string, Func<Task>, Task> hostTest)
    {
        await test("cut exclusion retains ordinary view edits Section selection and read-only cut and Break observations", () =>
        {
            var properties = AuditServerContractSupport.Branch("modify_structure", "set_drawing_view_properties").GetProperty("properties");
            var refresh = AuditServerContractSupport.Branch("modify_structure", "refresh_drawing_views").GetProperty("properties");
            var deletion = AuditServerContractSupport.Branch("modify_structure", "delete_drawing_views").GetProperty("properties");
            Need(properties.GetProperty("preview_mode").GetProperty("type").GetString() == "boolean" &&
                refresh.GetProperty("targets").GetProperty("minItems").GetInt32() == 1 &&
                deletion.GetProperty("section_targets").GetProperty("items").GetProperty("required").EnumerateArray()
                    .Select(value => value.GetString()).ToHashSet().SetEquals(new[] { "section_id", "section_token" }),
                "Cut exclusion removed ordinary preview/refresh or explicit independent Plane Section retirement contracts.");
            foreach (var input in new[] { properties, refresh, deletion })
                Need(!input.TryGetProperty("local_cut", out _) && !input.TryGetProperty("outer_cut", out _) && !input.TryGetProperty("cut_inputs", out _),
                    "A writable cut-authoring input was introduced into the retained view operations.");
            var data = ObjectAlternative(AuditServerContractSupport.Output("get_drawing_views").GetProperty("properties").GetProperty("data"));
            var view = data.GetProperty("properties").GetProperty("items").GetProperty("items").GetProperty("properties");
            Need(view.GetProperty("attached_local_cut_ids").GetProperty("anyOf").EnumerateArray().Any(item => item.GetProperty("type").GetString() == "array") &&
                view.GetProperty("attached_local_cut_ids").GetProperty("anyOf").EnumerateArray().Any(item => item.GetProperty("type").GetString() == "null") &&
                view.GetProperty("outer_cut_sketch_id").GetProperty("anyOf").EnumerateArray().Any(item => item.GetProperty("type").GetString() == "string") &&
                view.GetProperty("attached_break_ids").GetProperty("type").GetString() == "array" && view.TryGetProperty("section_cut", out _),
                "Excluding cut writes removed the existing read-only cut/Section/Break observations needed for dependency protection.");
            return Task.CompletedTask;
        });
        await hostTest("compiled cut-view exclusion reads both actual cut links and precedes every native write tracker", () =>
        {
            using var file = File.OpenRead("src/NanoCADBridge/bin/Release/net6.0-windows/NanoCADBridge.dll");
            using var pe = new PEReader(file); var metadata = pe.GetMetadataReader();
            var opcodes = typeof(OpCodes).GetFields(BindingFlags.Public | BindingFlags.Static).Select(field => (OpCode)field.GetValue(null)!)
                .ToDictionary(code => unchecked((ushort)code.Value));
            string TypeName(EntityHandle handle)
            {
                if (handle.Kind == HandleKind.TypeDefinition) { var type = metadata.GetTypeDefinition((TypeDefinitionHandle)handle); return metadata.GetString(type.Namespace) + "." + metadata.GetString(type.Name); }
                if (handle.Kind == HandleKind.TypeReference) { var type = metadata.GetTypeReference((TypeReferenceHandle)handle); return metadata.GetString(type.Namespace) + "." + metadata.GetString(type.Name); }
                return handle.Kind.ToString();
            }
            (string Type, string Method) Target(int token)
            {
                var handle = MetadataTokens.EntityHandle(token);
                if (handle.Kind == HandleKind.MethodSpecification) handle = metadata.GetMethodSpecification((MethodSpecificationHandle)handle).Method;
                if (handle.Kind == HandleKind.MethodDefinition) { var method = metadata.GetMethodDefinition((MethodDefinitionHandle)handle); return (TypeName(method.GetDeclaringType()), metadata.GetString(method.Name)); }
                if (handle.Kind == HandleKind.MemberReference) { var method = metadata.GetMemberReference((MemberReferenceHandle)handle); return (TypeName(method.Parent), metadata.GetString(method.Name)); }
                throw new Exception("Unexpected compiled method operand in native scope guard.");
            }
            Body Read(string name, string type = "NanoCADBridge.CadDrawingViewEditWriter")
            {
                var owner = metadata.TypeDefinitions.Single(handle => TypeName(handle) == type);
                var method = metadata.GetTypeDefinition(owner).GetMethods().Select(handle => metadata.GetMethodDefinition(handle))
                    .Single(method => metadata.GetString(method.Name) == name);
                var il = pe.GetMethodBody(method.RelativeVirtualAddress).GetILBytes() ?? throw new Exception("Compiled scope method has no IL.");
                var calls = new List<Call>(); var strings = new List<string>();
                for (int index = 0; index < il.Length;)
                {
                    int offset = index; ushort value = il[index++]; if (value == 0xFE) value = (ushort)(0xFE00 | il[index++]); var opcode = opcodes[value];
                    int size = opcode.OperandType switch
                    {
                        OperandType.InlineNone => 0,
                        OperandType.ShortInlineBrTarget or OperandType.ShortInlineI or OperandType.ShortInlineVar => 1,
                        OperandType.InlineVar => 2,
                        OperandType.InlineI8 or OperandType.InlineR => 8,
                        OperandType.InlineSwitch => 4 + 4 * BitConverter.ToInt32(il, index),
                        _ => 4
                    };
                    if (opcode.OperandType == OperandType.InlineMethod) { var target = Target(BitConverter.ToInt32(il, index)); calls.Add(new(offset, target.Type, target.Method)); }
                    if (opcode.OperandType == OperandType.InlineString) strings.Add(metadata.GetUserString(MetadataTokens.UserStringHandle(BitConverter.ToInt32(il, index) & 0xFFFFFF)));
                    Need(opcode.OperandType != OperandType.InlineSig, "Scope selection unexpectedly invokes an unmanaged indirect call."); index += size;
                }
                return new(calls.ToArray(), strings.ToArray());
            }
            var guard = Read("RequireSupportedViews");
            Need(guard.Calls.Any(call => call.Method == "get_AttachedLocalCuts") && guard.Calls.Any(call => call.Method == "get_OuterCutSketch") &&
                guard.Strings.Contains("UNSUPPORTED_DRAWING_VIEW_CUT_WRITE") && guard.Strings.Contains("DEPENDENCY_INVENTORY_UNAVAILABLE"),
                "The real compiled guard no longer protects both cut relations or treats an unavailable inventory as empty.");
            Need(!guard.Calls.Any(call => call.Method is "get_Attached2dBreaks" or "get_SectionID" or "get_IsBasedOnSection"),
                "The cut exclusion was broadened to ordinary Section/Break view state.");
            bool Mutation(Call call) => call.Method is "Start" or "TryModify" or "Erase" or "UpdateAll" or "set_GeomChanged" or "set_PosChanged";
            var select = Read("Select"); int scope = select.Calls.Single(call => call.Method == "RequireSupportedViews").Offset;
            Need(!select.Calls.Any(Mutation) && select.Calls.Where(call => call.Method is "Plain" or "Placement" || call.Type == "NanoCADBridge.CadDrawingViewRetirement269" && call.Method == "Prepare")
                .All(call => call.Offset > scope), "A view write/deletion branch precedes the cut-scope guard.");
            var preview = Read("Preflight");
            Need(preview.Calls.Any(call => call.Type == "NanoCADBridge.CadDrawingViewEditWriter" && call.Method == "Select") && !preview.Calls.Any(Mutation), "Read-only validation bypasses scope selection or starts native tracking.");
            var execute = Read("Execute"); int selected = execute.Calls.Single(call => call.Type == "NanoCADBridge.CadDrawingViewEditWriter" && call.Method == "Select").Offset;
            Need(execute.Calls.Any(call => call.Method == "Start") && execute.Calls.Where(call => Mutation(call) || call.Type == "Multicad.McUndoPoint" && call.Method == ".ctor")
                .All(call => call.Offset > selected), "Actual native tracking or mutation begins before selected cut views are refused.");
            var sketchGuard = Read("RequireSupported", "NanoCADBridge.DrawingViewSketchWriteScope282");
            foreach (string getter in new[] { "get_IsLocalCutSketch", "get_IsDrawingViewCutSketch", "get_ViewIdForLocalCut", "get_ViewIdForOuterCut", "get_ParentId" })
                Need(sketchGuard.Calls.Any(call => call.Method == getter), "Selected cut-sketch protection lost an actual native role/parent observation: " + getter);
            Need(sketchGuard.Strings.Contains("UNSUPPORTED_DRAWING_VIEW_CUT_WRITE") && sketchGuard.Calls.Any(call => call.Method == "IdentifiesObjectOfType") &&
                !sketchGuard.Calls.Any(call => call.Method == "get_HostSolidID") && !sketchGuard.Calls.Any(Mutation),
                "The selected-sketch guard lost the user exclusion or narrowed ordinary document sketches by a null HostSolid heuristic.");
            foreach (var target in new[]{
                ("NativeSketchConstraintWriter385","Resolve"),("NativeSketchConstraintRemoval430","Resolve"),
                ("ModelSketchLifecycleWriter362","Resolve"),("ModelSketchParameter411","Capture"),
                ("ModelProfileLifecycleWriter340","Resolve"),("ModelProfileRegionsWriter350","Resolve"),
                ("ModelProfileElementsWriter358","Resolve"),("ModelProfileRebindWriter349","Resolve"),
                ("ModelSketchWriter","Resolve"),("ModelSketchCompositionWriter","Resolve"),
                ("ModelSketchSupportWriter411","Resolve"),("ModelSketchSupportRecovery440","Resolve"),
                ("ModelSketchVisibilityWriter436","Resolve"),("ModelManufacturingWriter","Resolve")})
            {
                var caller = Read(target.Item2, "NanoCADBridge." + target.Item1);
                Need(caller.Calls.Any(call => call.Type == "NanoCADBridge.DrawingViewSketchWriteScope282" && call.Method == "RequireSupported") && !caller.Calls.Any(Mutation),
                    "A selected mutation preflight bypasses cut-sketch scope protection or writes before it: " + target.Item1 + "." + target.Item2);
            }
            var lifecycle = Read("Prepare", "NanoCADBridge.DrawingViewLifecycleWriter345");
            Need(lifecycle.Calls.Any(call => call.Type == "NanoCADBridge.CadDrawingViewEditWriter" && call.Method == "RequireSupportedViews") && !lifecycle.Calls.Any(Mutation),
                "Native view lifecycle selection, including rename, bypasses cut-view protection.");
            return Task.CompletedTask;
        });
    }
}
