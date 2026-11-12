using System.Text.Json;
using NanoCAD.Contracts;

internal static class DrawingViewPreviewModeTests
{
    const string Bridge = "10000000-1111-2222-3333-000000000001";
    const string Document = "10000000-1111-2222-3333-000000000002";
    const string Operation = "10000000-1111-2222-3333-000000000003";
    static void Need(bool condition, string message) { if (!condition) throw new Exception(message); }
    static Dictionary<string, object?> Edit() => new()
    {
        ["operation"] = "set_drawing_view_properties",
        ["layout_handle"] = "000A1",
        ["handle"] = "000B1",
        ["expected_name"] = "Current native projection"
    };
    static JsonElement Write(object edit) => Wire.Element(new
    {
        bridge_instance_id = Bridge,
        document_id = Document,
        expected_revision = "12",
        operation_id = Operation,
        units = "Millimeters",
        operations = new[] { edit }
    });
    static SetDrawingViewPropertiesEdit Parsed(Dictionary<string, object?> edit) =>
        (SetDrawingViewPropertiesEdit)StructureWriteValidation.Parse(Write(edit)).Operations.Single();
    static void Refuse(Action action, string path)
    {
        try { action(); }
        catch (DomainException error)
        {
            Need(error.Error.code == "INVALID_ARGUMENT" && !error.Error.retryable &&
                Wire.Element(error.Error.details ?? throw new Exception("Missing precise field error.")).GetProperty("path").GetString() == path,
                "An invalid preview-mode input lost its exact nonretryable field path.");
            return;
        }
        throw new Exception("An invalid DrawingView preview-mode input was accepted.");
    }
    static JsonElement ObjectAlternative(JsonElement schema) => schema.GetProperty("anyOf").EnumerateArray()
        .Single(item => item.TryGetProperty("type", out var type) && type.GetString() == "object");

    internal static async Task Run(Func<string, Func<Task>, Task> test)
    {
        await test("DrawingView preview accepts either boolean as its sole requested property", () =>
        {
            foreach (bool value in new[] { true, false })
            {
                var operation = Edit(); operation["preview_mode"] = value;
                var edit = Parsed(operation);
                Need(edit.PreviewMode == value && edit.Target.LayoutHandle == "A1" && edit.Target.Handle == "B1" &&
                    edit.Origin == null && edit.ModelScale == null && edit.ShowHiddenLines == null && edit.ShowHatches == null && edit.ShowTangentEdges == null,
                    "A false preview request was treated as absent or changed another stored view property.");
            }
            return Task.CompletedTask;
        });

        await test("omitted DrawingView preview preserves its mode and remains distinct from explicit false in write identity", () =>
        {
            var operation = Edit(); operation["show_hatches"] = false;
            Need(Parsed(operation).PreviewMode == null && Parsed(operation).ShowHatches == false,
                "An omitted preview field acquired a default or changed another explicit display request.");
            var legacy = new SetDrawingViewPropertiesEdit(new("A1", "B1", "Current native projection"), null, null, null, false, null);
            Need(legacy.PreviewMode == null, "The existing constructor changed its preview default or source compatibility.");
            string omitted = StructureWriteValidation.Parse(Write(operation)).Fingerprint();
            operation["preview_mode"] = false; string disabled = StructureWriteValidation.Parse(Write(operation)).Fingerprint();
            operation["preview_mode"] = true; string enabled = StructureWriteValidation.Parse(Write(operation)).Fingerprint();
            Need(new[] { omitted, disabled, enabled }.Distinct(StringComparer.Ordinal).Count() == 3,
                "Different explicit preview intentions were conflated for the same operation identity.");
            return Task.CompletedTask;
        });

        await test("DrawingView preview rejects coercion duplicate fields and misspellings at their exact paths", () =>
        {
            foreach (object? value in new object?[] { null, "false", 0, 1, new object[0], new { value = true } })
            {
                var operation = Edit(); operation["preview_mode"] = value;
                Refuse(() => StructureWriteValidation.Parse(Write(operation)), "$.operations[0].preview_mode");
            }
            var misspelled = Edit(); misspelled["previewMode"] = true;
            Refuse(() => StructureWriteValidation.Parse(Write(misspelled)), "$.operations[0].previewMode");
            using var duplicate = JsonDocument.Parse("{\"operation\":\"set_drawing_view_properties\",\"layout_handle\":\"A1\",\"handle\":\"B1\",\"expected_name\":\"Current native projection\",\"preview_mode\":false,\"preview_mode\":true}");
            Refuse(() => DrawingViewEditValidation.Parse(duplicate.RootElement, "$.operations[3]"), "$.operations[3].preview_mode");
            return Task.CompletedTask;
        });

        await test("published DrawingView schemas expose a typed actual preview flag and permit complete empty crop geometry", () =>
        {
            var input = AuditServerContractSupport.Branch("modify_structure", "set_drawing_view_properties");
            Need(input.GetProperty("properties").GetProperty("preview_mode").GetProperty("type").GetString() == "boolean" &&
                !input.GetProperty("required").EnumerateArray().Any(value => value.GetString() == "preview_mode") &&
                input.GetProperty("anyOf").EnumerateArray().Any(alternative => alternative.GetProperty("required").EnumerateArray()
                    .Select(value => value.GetString()).SequenceEqual(new[] { "preview_mode" })),
                "The published schema requires preview mode, admits coercion, or refuses a standalone boolean update.");
            var data = ObjectAlternative(AuditServerContractSupport.Output("get_drawing_views").GetProperty("properties").GetProperty("data"));
            var view = data.GetProperty("properties").GetProperty("items").GetProperty("items");
            Need(view.GetProperty("properties").GetProperty("preview_mode").GetProperty("type").GetString() == "boolean" &&
                view.GetProperty("required").EnumerateArray().Any(value => value.GetString() == "preview_mode"),
                "The actual native read flag is missing, nullable or untyped in the published output schema.");
            var geometry = ObjectAlternative(data.GetProperty("properties").GetProperty("selected_geometry"));
            var items = geometry.GetProperty("properties").GetProperty("items");
            Need(items.GetProperty("type").GetString() == "array" &&
                (!items.TryGetProperty("minItems", out var minimum) || minimum.GetInt32() == 0) &&
                geometry.GetProperty("properties").GetProperty("geometry_details_complete").GetProperty("type").GetString() == "boolean" &&
                geometry.GetProperty("properties").GetProperty("total_elements").GetProperty("type").GetString() == "integer",
                "A legitimate complete zero-element native crop was forbidden or conflated with unavailable geometry.");
            return Task.CompletedTask;
        });
    }
}
