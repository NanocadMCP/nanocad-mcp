using System.Reflection;
using System.Reflection.Emit;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using System.Text.Json;
using NanoCAD.Contracts;

internal static class ProjectedViewAssociationReadTests
{
    private static void Need(bool condition, string message)
    { if (!condition) throw new Exception(message); }

    internal static async Task Run(Func<string, Func<Task>, Task> test, Func<string, Func<Task>, Task>? hostTest = null)
    {
        await test("projected view association output distinguishes actual stored anchors from view ownership", () =>
        {
            var output = AuditServerContractSupport.Output("inspect_objects");
            var dimensions = output.GetProperty("$defs").GetProperty("dimension").GetProperty("oneOf");
            foreach (var dimension in dimensions.EnumerateArray())
            {
                var properties = dimension.GetProperty("properties");
                Need(properties.TryGetProperty("association", out _) && properties.TryGetProperty("measurement_unavailable_reason", out _),
                    "The separate native-view observation replaced ordinary association or measurement availability.");
                var optional = properties.GetProperty("view_association").GetProperty("anyOf");
                Need(optional.EnumerateArray().Any(item => item.TryGetProperty("type", out var type) && type.GetString() == "null"),
                    "Cold/non-MCS dimensions must be able to omit native-view information.");
                var view = optional.EnumerateArray().Single(item => item.TryGetProperty("properties", out _)).GetProperty("properties");
                Need(view.GetProperty("stored_anchor_parameters_available").GetProperty("type").GetString() == "boolean" &&
                    view.TryGetProperty("stored_anchor_parameters_unavailable_reason", out _) && view.TryGetProperty("links_complete", out _),
                    "Native anchor availability or incomplete link inventory became an implied success.");
                Need(!view.TryGetProperty("native_reference_items", out _) && view.TryGetProperty("drawing_view_mc_id", out _) &&
                    view.TryGetProperty("dimension_type", out _) && view.TryGetProperty("native_view_snap_indicator", out _) &&
                    view.TryGetProperty("native_associative", out _) && view.TryGetProperty("native_view_classifier", out _) &&
                    view.TryGetProperty("classification", out _) &&
                    !view.TryGetProperty("native_view_snap_count", out _), "Managed reference items or an inferred native count replaced the public stored-anchor observation.");
                var anchor = view.GetProperty("anchors").GetProperty("items").GetProperty("properties");
                foreach (string field in new[] { "slot", "is_null", "source_mc_id", "gs_marker", "parameter", "data", "complex_reference",
                    "viewport_reference", "evaluated_point_wcs", "dimension_snap_point", "dimension_snap_point_scope", "evaluation_unavailable_reason" })
                    Need(anchor.TryGetProperty(field, out _), "An actual stored-anchor field is missing: " + field);
                Need(!anchor.TryGetProperty("original_point_wcs", out _),
                    "GetSnapPoint has no public coordinate-frame guarantee and must not be presented as an original 3D WCS point.");
                Need(anchor.GetProperty("data").GetProperty("anyOf").EnumerateArray().Any(item =>
                    item.TryGetProperty("type", out var type) && type.GetString() == "string"),
                    "Native Int64 anchor data must retain exact text rather than a lossy JSON floating-point value.");
                var link = view.GetProperty("links").GetProperty("items").GetProperty("properties");
                Need(link.TryGetProperty("view_ref", out _) && link.TryGetProperty("owner_document_id", out _) &&
                    link.TryGetProperty("source_refs", out _) && link.TryGetProperty("view_status", out _) &&
                    link.TryGetProperty("native_anchor_count", out _) && !link.TryGetProperty("native_reference_count", out _) &&
                    link.TryGetProperty("source_gs_marker_unavailable_reason", out _) && !link.TryGetProperty("source_edge_key", out _),
                    "Native owner/source facts are missing or an invented edge identity was introduced.");
            }
            return Task.CompletedTask;
        });
        await (hostTest ?? test)("compiled projected view association reader guards cold loading and uses only exact read identities", () =>
        {
            AuditCompiledReader();
            return Task.CompletedTask;
        });
    }

    private static void AuditCompiledReader()
    {
        const string reader = "CadProjectedDimensionAssociationReader";
        using var stream = File.OpenRead("src/NanoCADBridge/bin/Release/net6.0-windows/NanoCADBridge.dll");
        using var pe = new PEReader(stream);
        var metadata = pe.GetMetadataReader();
        var opcodes = typeof(OpCodes).GetFields(BindingFlags.Public | BindingFlags.Static)
            .Select(field => (OpCode)field.GetValue(null)!).ToDictionary(code => unchecked((ushort)code.Value));
        string TypeName(EntityHandle handle)
        {
            if (handle.Kind == HandleKind.TypeDefinition)
            {
                var type = metadata.GetTypeDefinition((TypeDefinitionHandle)handle);
                return metadata.GetString(type.Namespace) + "." + metadata.GetString(type.Name);
            }
            if (handle.Kind == HandleKind.TypeReference)
            {
                var type = metadata.GetTypeReference((TypeReferenceHandle)handle);
                return metadata.GetString(type.Namespace) + "." + metadata.GetString(type.Name);
            }
            return handle.Kind.ToString();
        }
        string Root(TypeDefinitionHandle handle)
        {
            var type = metadata.GetTypeDefinition(handle);
            return type.GetDeclaringType().IsNil ? metadata.GetString(type.Name) : Root(type.GetDeclaringType());
        }
        (string Type, string Method) Target(EntityHandle handle)
        {
            if (handle.Kind == HandleKind.MethodSpecification) return Target(metadata.GetMethodSpecification((MethodSpecificationHandle)handle).Method);
            if (handle.Kind == HandleKind.MethodDefinition)
            {
                var method = metadata.GetMethodDefinition((MethodDefinitionHandle)handle);
                return (TypeName(method.GetDeclaringType()), metadata.GetString(method.Name));
            }
            var member = metadata.GetMemberReference((MemberReferenceHandle)handle);
            return (TypeName(member.Parent), metadata.GetString(member.Name));
        }
        var calls = new HashSet<string>(StringComparer.Ordinal);
        bool sawColdGuard = false, sawLoadedRead = false;
        int opens = 0;
        foreach (var handle in metadata.MethodDefinitions)
        {
            var method = metadata.GetMethodDefinition(handle);
            if (method.RelativeVirtualAddress == 0 || Root(method.GetDeclaringType()) != reader) continue;
            string name = metadata.GetString(method.Name);
            bool cold = name == "TryRead";
            if (cold) sawColdGuard = true;
            if (name == "ReadLoaded")
            {
                Need((method.ImplAttributes & MethodImplAttributes.NoInlining) != 0, "The loaded-runtime path could be inlined into the ordinary cold reader.");
                sawLoadedRead = true;
            }
            var il = pe.GetMethodBody(method.RelativeVirtualAddress).GetILBytes()!;
            OpCode previous = default;
            for (int offset = 0; offset < il.Length;)
            {
                ushort raw = il[offset++];
                if (raw == 0xfe) raw = (ushort)(0xfe00 | il[offset++]);
                var opcode = opcodes[raw];
                if (opcode.OperandType == OperandType.InlineMethod)
                {
                    var target = Target(MetadataTokens.EntityHandle(BitConverter.ToInt32(il, offset)));
                    calls.Add(target.Type + "." + target.Method);
                    bool cad = target.Type.StartsWith("Multicad.", StringComparison.Ordinal) ||
                        target.Type.StartsWith("Teigha.", StringComparison.Ordinal) || target.Type.StartsWith("HostMgd.", StringComparison.Ordinal);
                    Need(!cold || !target.Type.StartsWith("Multicad.", StringComparison.Ordinal), "Cold guard directly references a MultiCAD type.");
                    Need(!cad || !target.Method.StartsWith("set_", StringComparison.Ordinal) &&
                        target.Method is not ("Update" or "SetAssociative" or "Add2Document" or "Commit" or "Clone" or "StartTransaction" or "StartOpenCloseTransaction" or "AddReactor" or "RemoveReactor"),
                        "Association observation reaches a native mutation: " + target);
                    if (target.Type == "Teigha.DatabaseServices.Transaction" && target.Method == "GetObject")
                    { opens++; Need(previous == OpCodes.Ldc_I4_0, "An association dependency was not opened ForRead."); }
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
                offset += size; Need(offset <= il.Length, "Invalid compiled reader IL boundary."); previous = opcode;
            }
        }
        Need(sawColdGuard && sawLoadedRead && opens > 0, "The compiled native reader/cold guard is missing.");
        foreach (string expected in new[]
        {
            "Multicad.McObjectId.GetObject", "NanoCADBridge.NativeModelHistory421.LoadProjectedDimensionAssociation",
            "Multicad.McObjectId.FromHandle", "Multicad.DatabaseServices.McDbObject.get_DocumentID"
        }) Need(calls.Contains(expected), "The compiled reader lost a reviewed actual native identity read: " + expected);
        Need(calls.Any(call => call.EndsWith(".ReadProjectedDimensionAssociation.Invoke", StringComparison.Ordinal)) &&
            !calls.Any(call => call.EndsWith(".get_ReferenceItems", StringComparison.Ordinal) || call.Contains("McReferenceItem.", StringComparison.Ordinal) ||
                call.EndsWith(".McDimension.get_Associative", StringComparison.Ordinal)),
            "The reader must use actual public native stored anchors without a managed Associative gate or ReferenceItems inference.");
        var associationDelegate = metadata.TypeDefinitions.Select(metadata.GetTypeDefinition)
            .Single(type => metadata.GetString(type.Name) == "ReadProjectedDimensionAssociation");
        var invocation = associationDelegate.GetMethods().Select(metadata.GetMethodDefinition)
            .Single(method => metadata.GetString(method.Name) == "Invoke");
        var nativeState = invocation.GetParameters().Select(metadata.GetParameter)
            .Single(parameter => metadata.GetString(parameter.Name) == "state");
        // ECMA native-array descriptor: ARRAY, default element subtype,
        // absent size parameter, five actual V2 state integers, flags zero.
        Need(metadata.GetBlobBytes(nativeState.GetMarshallingDescriptor()).SequenceEqual(new byte[] { 0x2A, 0x50, 0, 5, 0 }),
            "The compiled V2 association delegate does not reserve all five native observation states.");
        File.WriteAllText(Path.Combine(Environment.GetEnvironmentVariable("NANOCAD_MCP_HOME")!, "projected-view-association-read-audit.json"),
            JsonSerializer.Serialize(new
            {
                calls = calls.Order(StringComparer.Ordinal).ToArray(),
                for_read_open_sites = opens,
                scope = "Compiled cold-load guard and direct read-call audit; no CAD or transitive native behavior is asserted."
            }, Wire.Json));
    }
}
