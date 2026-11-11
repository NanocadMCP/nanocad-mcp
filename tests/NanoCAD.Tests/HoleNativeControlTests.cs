using System.Reflection;
using System.Reflection.Emit;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using System.Text.Json;
using NanoCAD.Contracts;
using NanoCAD.Ipc;

internal static class HoleNativeControlTests
{
    private static void Need(bool condition, string message)
    { if (!condition) throw new Exception(message); }

    internal static async Task Run(Func<string, Func<Task>, Task> test, Func<string, Func<Task>, Task>? hostTest = null)
    {
        await test("native Hole effects preserve raw units and actual numeric fields in parameter receipts", () =>
        {
            var change = new ModelParameterChange("{11111111-1111-4111-8111-111111111111}", "{22222222-2222-4222-8222-222222222222}",
                "10", "HoleDriver", 10, 12, "native_variable_units", owner_kind: "feature")
            {
                native_control_effect = new(2, 0, "native_variable_units",
                    [new("diameter", 10, 12), new("depth", 20, 20.5)], "operation_local_native_readback; no_shape_slot_alias")
            };
            var serialized = Wire.Element(change);
            var effect = serialized.GetProperty("native_control_effect");
            Need(serialized.GetProperty("value_unit").GetString() == "native_variable_units" &&
                effect.GetProperty("value_unit").GetString() == "native_variable_units" &&
                effect.GetProperty("native_value_category").GetUInt32() == 2 && effect.GetProperty("native_extent").GetInt32() == 0,
                "The actual native category/extent was converted into an inferred engineering unit or field role.");
            var fields = effect.GetProperty("shape_changes").EnumerateArray().ToArray();
            Need(fields.Length == 2 && fields[0].GetProperty("field").GetString() == "diameter" &&
                fields[0].GetProperty("previous_value").GetDouble() == 10 && fields[0].GetProperty("value").GetDouble() == 12 &&
                fields[1].GetProperty("field").GetString() == "depth" && fields[1].GetProperty("value").GetDouble() == 20.5,
                "The operation-local native shape readback was lost or reduced to a permanent diameter alias.");
            var angular = change with { native_control_effect = new(1, 3, "native_variable_units", [], "operation_local_native_readback") };
            var raw = Wire.Element(angular).GetProperty("native_control_effect");
            Need(raw.GetProperty("native_value_category").GetUInt32() == 1 && raw.GetProperty("native_extent").GetInt32() == 3 &&
                raw.GetProperty("shape_changes").GetArrayLength() == 0 && raw.GetProperty("value_unit").GetString() == "native_variable_units",
                "A reported angular category introduced implicit conversion or fabricated a shape change.");
            Need(!Wire.Element(change with { native_control_effect = null }).TryGetProperty("native_control_effect", out _),
                "Legacy and User receipts acquired an unrequested effect field.");
            return Task.CompletedTask;
        });
        await test("native Hole effects survive exact committed write lookup without another mutation", async () =>
        {
            string bridge = Guid.NewGuid().ToString("D"), document = Guid.NewGuid().ToString("D"), operation = Guid.NewGuid().ToString("D");
            var request = new Request(Wire.Version, Guid.NewGuid().ToString("D"), "set_model_parameter", 8000, Wire.Element(new
            {
                bridge_instance_id = bridge,
                document_id = document,
                operation_id = operation,
                expected_revision = "7",
                units = "Millimeters",
                handle = "53A",
                variable_id = "{11111111-1111-4111-8111-111111111111}",
                expected_expression = "10",
                expression = "12"
            }));
            using var queue = new WriteQueue(Environment.CurrentManagedThreadId);
            var (job, replayed) = queue.Enqueue(request);
            Need(!replayed && queue.Take() == job && job.TryStart(), "The original scalar write did not start once.");
            var change = new ModelParameterChange("{11111111-1111-4111-8111-111111111111}", null, "10", "12", 10, 12, "native_variable_units")
            { native_control_effect = new(2, 0, "native_variable_units", [new("diameter", 10, 12)], "operation_local_native_readback") };
            var data = new ModelParameterResult(operation, "committed", false, new(bridge, document, "53A"), null, change.variable_id,
                "10", "12", 12, "native_variable_units", 42, "not_guaranteed", [change]);
            var result = Result.Ok(request.requestId, data);
            job.BeginCommit(); job.RecordCommit(result); job.Complete(result);
            var (lookup, wasReplay) = queue.Enqueue(request);
            Need(wasReplay && lookup == job && queue.Take() == null, "Historical lookup admitted another Hole mutation.");
            var historical = WriteJob.ForCaller(await lookup.Completion, "hole-effect-replay", true);
            Need(historical.data is ModelParameterResult { replayed: true }, "Replay lost the original parameter receipt type.");
            var observed = Wire.Element(historical.data!).GetProperty("parameter_changes")[0].GetProperty("native_control_effect");
            Need(observed.GetProperty("shape_changes")[0].GetProperty("value").GetDouble() == 12,
                "The committed native effect disappeared from the existing operation ledger.");
        });
        await (hostTest ?? test)("compiled native Hole control verifies real ownership without scalar setters or persistent aliases", () =>
        { AuditCompiledControl(); return Task.CompletedTask; });
    }

    private sealed record Instruction(int Offset, OpCode Code, int? Branch, (string Type, string Method)? Call, string? Text);
    private static void AuditCompiledControl()
    {
        const string control = "ModelHoleNativeControl";
        using var stream = File.OpenRead("src/NanoCADBridge/bin/Release/net6.0-windows/NanoCADBridge.dll");
        using var pe = new PEReader(stream); var metadata = pe.GetMetadataReader();
        var opcodes = typeof(OpCodes).GetFields(BindingFlags.Public | BindingFlags.Static)
            .Select(field => (OpCode)field.GetValue(null)!).ToDictionary(code => unchecked((ushort)code.Value));
        string TypeName(EntityHandle handle)
        {
            if (handle.Kind == HandleKind.TypeDefinition)
            { var type = metadata.GetTypeDefinition((TypeDefinitionHandle)handle); return metadata.GetString(type.Namespace) + "." + metadata.GetString(type.Name); }
            if (handle.Kind == HandleKind.TypeReference)
            { var type = metadata.GetTypeReference((TypeReferenceHandle)handle); return metadata.GetString(type.Namespace) + "." + metadata.GetString(type.Name); }
            return handle.Kind.ToString();
        }
        string Root(TypeDefinitionHandle handle)
        { var type = metadata.GetTypeDefinition(handle); return type.GetDeclaringType().IsNil ? metadata.GetString(type.Name) : Root(type.GetDeclaringType()); }
        (string Type, string Method) Target(EntityHandle handle)
        {
            if (handle.Kind == HandleKind.MethodSpecification) return Target(metadata.GetMethodSpecification((MethodSpecificationHandle)handle).Method);
            if (handle.Kind == HandleKind.MethodDefinition)
            { var method = metadata.GetMethodDefinition((MethodDefinitionHandle)handle); return (TypeName(method.GetDeclaringType()), metadata.GetString(method.Name)); }
            var member = metadata.GetMemberReference((MemberReferenceHandle)handle); return (TypeName(member.Parent), metadata.GetString(member.Name));
        }
        var calls = new HashSet<string>(StringComparer.Ordinal); List<Instruction>? mask = null; int methods = 0;
        foreach (var handle in metadata.TypeDefinitions)
        {
            var type = metadata.GetTypeDefinition(handle);
            if (metadata.GetString(type.Name) != control) continue;
            foreach (var fieldHandle in type.GetFields())
            {
                var field = metadata.GetFieldDefinition(fieldHandle);
                if ((field.Attributes & FieldAttributes.Static) == 0 || (field.Attributes & FieldAttributes.Literal) != 0) continue;
                Need(metadata.GetString(field.Name) == "ShapeNumbers" && (field.Attributes & FieldAttributes.InitOnly) != 0,
                    "The control acquired persistent mutable state or an ID-to-role cache.");
            }
        }
        foreach (var handle in metadata.MethodDefinitions)
        {
            var method = metadata.GetMethodDefinition(handle);
            if (method.RelativeVirtualAddress == 0 || Root(method.GetDeclaringType()) != control) continue;
            methods++; var instructions = new List<Instruction>(); var il = pe.GetMethodBody(method.RelativeVirtualAddress).GetILBytes()!;
            for (int offset = 0; offset < il.Length;)
            {
                int start = offset; ushort raw = il[offset++]; if (raw == 0xfe) raw = (ushort)(0xfe00 | il[offset++]); var code = opcodes[raw];
                int size = code.OperandType switch
                {
                    OperandType.InlineNone => 0,
                    OperandType.ShortInlineBrTarget or OperandType.ShortInlineI or OperandType.ShortInlineVar => 1,
                    OperandType.InlineVar => 2,
                    OperandType.InlineI8 or OperandType.InlineR => 8,
                    OperandType.InlineSwitch => 4 + 4 * BitConverter.ToInt32(il, offset),
                    _ => 4
                };
                (string Type, string Method)? call = null; string? text = null; int? branch = null;
                if (code.OperandType == OperandType.InlineMethod)
                {
                    var target = Target(MetadataTokens.EntityHandle(BitConverter.ToInt32(il, offset))); call = target; calls.Add(target.Type + "." + target.Method);
                    bool native = target.Type.StartsWith("Multicad.", StringComparison.Ordinal) || target.Type.StartsWith("Teigha.", StringComparison.Ordinal);
                    Need(!native || !target.Method.StartsWith("set_", StringComparison.Ordinal) && target.Method is not
                        ("SetVariableExpression" or "SetVariableValue" or "SetFaceThickness" or "Update" or "UpdateAll" or "AddReactor" or "RemoveReactor"),
                        "The control observer/witness reached a scalar setter or a hidden update: " + target);
                }
                if (code.OperandType == OperandType.InlineString)
                {
                    text = metadata.GetUserString(MetadataTokens.UserStringHandle(BitConverter.ToInt32(il, offset) & 0x00ffffff));
                    Need(!text.Contains("hole-native-formula-diagnostic", StringComparison.Ordinal) && !text.Contains("hole-native-formula-copy.dwg", StringComparison.Ordinal),
                        "A diagnostic fixture gate or logger leaked into the production control.");
                }
                if (code.OperandType == OperandType.ShortInlineBrTarget) branch = offset + size + (sbyte)il[offset];
                if (code.OperandType == OperandType.InlineBrTarget) branch = offset + size + BitConverter.ToInt32(il, offset);
                instructions.Add(new(start, code, branch, call, text)); offset += size; Need(offset <= il.Length, "Invalid compiled control IL.");
            }
            if (metadata.GetString(method.Name) == "RemoveWitnessed") mask = instructions;
        }
        Need(methods > 0 && mask != null, "The compiled production Hole control is missing.");
        foreach (string required in new[]{".GetVariableHostObjectId",".GetDependentOn3dObjects",".GetAllVariables",".LoadHoleControlState",
            ".IsValidVarValForObject",".get_HoleType",".get_DefType",".get_TopDoc",".GetAssocParams",".GetPosition"})
            Need(calls.Any(call => call.EndsWith(required, StringComparison.Ordinal)), "The native control lost a required exact owner/mode/validator read: " + required);
        int getter = mask!.FindIndex(instruction => instruction.Call?.Method == "get_NativeHoleEffect");
        Need(getter >= 0, "Effect-gated numeric masking is missing.");
        int condition = mask.FindIndex(getter + 1, instruction => instruction.Code.FlowControl == FlowControl.Cond_Branch);
        Need(condition >= 0 && !mask.Skip(getter + 1).Take(condition - getter - 1).Any(instruction => instruction.Call != null),
            "The no-effect path performs work before checking the witness.");
        var check = mask[condition];
        int nullPath = check.Code == OpCodes.Brtrue || check.Code == OpCodes.Brtrue_S ? condition + 1 :
            check.Code == OpCodes.Brfalse || check.Code == OpCodes.Brfalse_S ? mask.FindIndex(instruction => instruction.Offset == check.Branch) : -1;
        Need(nullPath >= 0, "The no-effect masking guard has no verified null branch.");
        bool returned = false;
        for (int index = nullPath; index < mask.Count; index++)
        {
            Need(mask[index].Call == null, "An unwitnessed control changed or suppressed parameter observations.");
            if (mask[index].Code == OpCodes.Ret) { returned = true; break; }
        }
        Need(returned, "Without a write effect, the complete original Hole parameters must remain compared unchanged.");
        File.WriteAllText(Path.Combine(Environment.GetEnvironmentVariable("NANOCAD_MCP_HOME")!, "hole-native-control-audit.json"),
            JsonSerializer.Serialize(new
            {
                methods,
                calls = calls.Order(StringComparer.Ordinal).ToArray(),
                no_effect_masks_nothing = true,
                scope = "Compiled direct-call and no-effect guard audit; native propagation is established separately."
            }, Wire.Json));
    }
}
