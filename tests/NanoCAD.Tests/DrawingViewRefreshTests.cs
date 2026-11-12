using System.Text.Json;
using ModelContextProtocol.Protocol;
using NanoCAD.Contracts;
using NanoCAD.McpServer;

internal static class DrawingViewRefreshTests
{
    static void Need(bool value, string message) { if (!value) throw new Exception(message); }
    static Dictionary<string, object?> Target(string layout = "A1", string handle = "B1", string name = "Current view") => new()
    { ["layout_handle"] = layout, ["handle"] = handle, ["expected_name"] = name };
    static Dictionary<string, object?> Refresh(object? targets) => new() { ["operation"] = "refresh_drawing_views", ["targets"] = targets };
    static JsonElement Write(params object[] operations) => Wire.Element(new
    {
        bridge_instance_id = "10000000-1111-2222-3333-000000000001",
        document_id = "10000000-1111-2222-3333-000000000002",
        expected_revision = "12",
        operation_id = "10000000-1111-2222-3333-000000000003",
        units = "Millimeters",
        operations
    });
    static void Refuse(Action action, string path, string code = "INVALID_ARGUMENT")
    {
        try { action(); }
        catch (DomainException error)
        {
            Need(error.Error.code == code && !error.Error.retryable &&
                Wire.Element(error.Error.details ?? throw new Exception("Missing refresh error details.")).GetProperty("path").GetString() == path,
                "Refresh lost its exact target/batch refusal path or code."); return;
        }
        throw new Exception("An invalid explicit view refresh was accepted.");
    }
    internal static async Task Run(Func<string, Func<Task>, Task> test)
    {
        await test("native view refresh normalizes explicit identities and canonicalizes only selection order", () =>
        {
            var first = Refresh(new[] { Target("000a1", "00cB", "Top"), Target("000b2", "00aB", "Front") });
            var parsed = StructureWriteValidation.Parse(Write(first));
            Need(parsed.Operations.Single() is RefreshDrawingViewsEdit { Targets: [{ Handle: "AB", LayoutHandle: "B2", ExpectedName: "Front" }, { Handle: "CB", LayoutHandle: "A1", ExpectedName: "Top" }] },
                "Refresh dropped an exact view/layout/name or failed to normalize the selected set.");
            var reverse = StructureWriteValidation.Parse(Write(Refresh(new[] { Target("B2", "AB", "Front"), Target("A1", "CB", "Top") })));
            Need(parsed.Fingerprint() == reverse.Fingerprint(), "Equivalent refresh selection order changed write identity.");
            var renamed = StructureWriteValidation.Parse(Write(Refresh(new[] { Target("B2", "AB", "Different current name"), Target("A1", "CB", "Top") })));
            Need(parsed.Fingerprint() != renamed.Fingerprint(), "Refresh discarded an expected current name from its write identity.");
            return Task.CompletedTask;
        });
        await test("view refresh routes through structural settings and original validate-only arguments without geometry input", () =>
        {
            var input = Write(Refresh(new[] { Target() })); var parsed = StructureWriteValidation.Parse(input);
            Need(DrawingViewEditValidation.Handles("refresh_drawing_views") && StructureWriteValidation.IsSettingsOnly(parsed.Operations) && WriteRecovery.NotGuaranteed(parsed),
                "Explicit native refresh was misrouted as coordinate creation or lost its native recovery scope.");
            var preview = WritePreviewRequest.Parse(Wire.Element(new WritePreviewRequest("modify_structure", input)));
            Need(preview is StructureWriteArguments { Operations: [RefreshDrawingViewsEdit] } && preview.Fingerprint() == parsed.Fingerprint(),
                "Validate-only routing changed the requested structural refresh into a geometry/property write.");
            var placement = new SetDrawingViewPropertiesEdit(new("A1", "B1", "Current view"), new(10, 20), null, null, null, null);
            Need(!StructureWriteValidation.IsSettingsOnly(new StructureEdit[] { placement }) && placement.PreviewMode == null,
                "Refresh classification changed coordinate-write preflight or the existing omitted preview default.");
            var property = new { operation = "set_drawing_view_properties", layout_handle = "A1", handle = "B1", expected_name = "Current view", preview_mode = false };
            var refresh = Refresh(new[] { Target() });
            foreach (var operations in new[]{new object[]{refresh,property},new object[]{refresh,Refresh(new[]{Target("A1","C1")})},
                new object[]{refresh,new{operation="delete_drawing_views",targets=new[]{Target()}}}})
                Refuse(() => StructureWriteValidation.Parse(Write(operations)), "$.operations", "INVALID_OPERATION_ORDER");
            return Task.CompletedTask;
        });
        await test("view refresh rejects missing ambiguous or noncurrent target shapes with precise field paths", () =>
        {
            var missing = new Dictionary<string, object?> { ["operation"] = "refresh_drawing_views" };
            Refuse(() => StructureWriteValidation.Parse(Write(missing)), "$.operations[0].targets");
            foreach (object? targets in new object?[] { null, 0, Array.Empty<object>() })
                Refuse(() => StructureWriteValidation.Parse(Write(Refresh(targets))), "$.operations[0].targets");
            Refuse(() => StructureWriteValidation.Parse(Write(Refresh(new object[] { "view" }))), "$.operations[0].targets[0]");
            foreach (string key in new[] { "layout_handle", "handle", "expected_name" })
            {
                var target = Target(); target.Remove(key);
                Refuse(() => StructureWriteValidation.Parse(Write(Refresh(new[] { target }))), "$.operations[0].targets[0]." + key);
            }
            foreach (string key in new[] { "layout_handle", "handle" })
            {
                var target = Target(); target[key] = "0";
                Refuse(() => StructureWriteValidation.Parse(Write(Refresh(new[] { target }))), "$.operations[0].targets[0]." + key, "INVALID_HANDLE");
            }
            Refuse(() => StructureWriteValidation.Parse(Write(Refresh(new[] { Target(), Target("B2", "00b1") }))), "$.operations[0].targets");
            var sections = Refresh(new[] { Target() }); sections["section_targets"] = Array.Empty<object>();
            Refuse(() => StructureWriteValidation.Parse(Write(sections)), "$.operations[0].section_targets");
            var propertyTarget = Target(); propertyTarget["preview_mode"] = false;
            Refuse(() => StructureWriteValidation.Parse(Write(Refresh(new[] { propertyTarget }))), "$.operations[0].targets[0].preview_mode");
            return Task.CompletedTask;
        });
        await test("published refresh is a focused documentation variant of modify_structure with exact target fields", async () =>
        {
            var schema = AuditServerContractSupport.Branch("modify_structure", "refresh_drawing_views");
            var properties = schema.GetProperty("properties"); var targets = properties.GetProperty("targets"); var item = targets.GetProperty("items");
            Need(properties.GetProperty("operation").GetProperty("const").GetString() == "refresh_drawing_views" &&
                !schema.GetProperty("additionalProperties").GetBoolean() && targets.GetProperty("minItems").GetInt32() == 1 && !targets.TryGetProperty("maxItems", out _) &&
                !item.GetProperty("additionalProperties").GetBoolean() && item.GetProperty("required").EnumerateArray().Select(value => value.GetString()).ToHashSet()
                    .SetEquals(new[] { "layout_handle", "handle", "expected_name" }) && !properties.TryGetProperty("section_targets", out _),
                "Focused refresh publication changed its exact selection contract or introduced an artificial target ceiling.");
            var variants = AuditServerContractSupport.Input("modify_structure").GetProperty("variants").EnumerateArray().Select(value => value.GetString());
            Need(variants.Contains("refresh_drawing_views") && !ToolCatalog.Tools.Any(tool => tool.Name == "nanocad.refresh_drawing_views"),
                "Refresh is absent from existing-tool discovery or was exposed as a separate endpoint.");
            var result = await new ToolService().CallAsync("get_guidance", new Dictionary<string, JsonElement> { ["topic"] = Wire.Element("commands"), ["section"] = Wire.Element("documentation") }, default);
            Need(result.IsError != true, "Local command-tree publication failed.");
            using var commands = JsonDocument.Parse(string.Join("\n", result.Content.OfType<TextContentBlock>().Select(block => block.Text)));
            Need(commands.RootElement.GetProperty("routes").EnumerateArray().Single(route => route.GetProperty("tool").GetString() == "nanocad.modify_structure")
                .GetProperty("variants").EnumerateArray().Any(value => value.GetString() == "refresh_drawing_views"),
                "Native refresh is missing from the actual documentation command tree.");
        });
    }
}
