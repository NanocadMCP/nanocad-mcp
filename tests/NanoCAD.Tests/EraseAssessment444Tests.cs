using System.Text.Json;
using NanoCAD.Contracts;
using NanoCAD.McpServer;

internal static class EraseAssessment444Tests
{
    private const string Bridge = "11111111-1111-4111-8111-111111111111";
    private const string Document = "22222222-2222-4222-8222-222222222222";
    private static void Check(bool value, string message) { if (!value) throw new Exception(message); }
    private static JsonElement Input(object value) => Wire.Element(value);
    private static void Invalid(Action action)
    {
        try { action(); throw new Exception("Invalid erase assessment request was accepted."); }
        catch (DomainException error) { Check(error.Error.code == "INVALID_ARGUMENT", "Unexpected parser error: " + error.Error.code); }
    }

    public static async Task Run(Func<string, Func<Task>, Task> test)
    {
        await test("block444: erase assessment is an explicit inspect-only boolean and remains off by default", () =>
        {
            var plain = Validation.Parse("inspect_objects", Input(new { bridge_instance_id = Bridge, document_id = Document, handles = new[] { "a1" } }));
            Check(!plain.EraseAssessment && plain.Handles!.Single() == "A1", "A default read gained assessment or lost its handle normalization.");
            var selected = Validation.Parse("inspect_objects", Input(new { bridge_instance_id = Bridge, document_id = Document, handles = new[] { "A1", "B2" }, sections = Array.Empty<string>(), erase_assessment = true }));
            Check(selected.EraseAssessment && selected.Handles!.SequenceEqual(new[] { "A1", "B2" }) && selected.DetailSections is { Length: 0 }, "Assessment changed the selected set or required geometry details.");
            var disabled = Validation.Parse("inspect_objects", Input(new { bridge_instance_id = Bridge, document_id = Document, handles = new[] { "A1" }, erase_assessment = false }));
            Check(!disabled.EraseAssessment, "Explicit false activated assessment.");
            return Task.CompletedTask;
        });
        await test("block444: erase assessment rejects truthy values and unknown-reader use", () =>
        {
            foreach (object? value in new object?[] { "true", 1, null, new { target_space = "model" } })
                Invalid(() => Validation.Parse("inspect_objects", Input(new { document_id = Document, handles = new[] { "A1" }, erase_assessment = value })));
            Invalid(() => Validation.Parse("get_document", Input(new { erase_assessment = true })));
            Invalid(() => Validation.Parse("query_objects", Input(new { document_id = Document, erase_assessment = true })));
            return Task.CompletedTask;
        });
        await test("block444: erase assessment retains normalized-selection and duplicate-field validation", () =>
        {
            try
            {
                Validation.Parse("inspect_objects", Input(new { document_id = Document, handles = new[] { "A1", "0a1" }, erase_assessment = true }));
                throw new Exception("Duplicate normalized selection was accepted.");
            }
            catch (DomainException error) { Check(error.Error.code == "INVALID_ARGUMENT", "Unexpected duplicate-selection result."); }
            using var duplicate = JsonDocument.Parse("{\"document_id\":\"" + Document + "\",\"handles\":[\"A1\"],\"erase_assessment\":true,\"erase_assessment\":false}");
            Invalid(() => Validation.Parse("inspect_objects", duplicate.RootElement));
            return Task.CompletedTask;
        });
        await test("block444: preparation remains a projection independent of explicit erase assessment", () =>
        {
            var plain = ReadPreparation433.Extract("inspect_objects", Input(new { document_id = Document, handles = new[] { "A1" },
                prepare_for = new { operation = "modify_entities", variant = "erase" } }));
            Check(!Validation.Parse("inspect_objects", plain.Arguments).EraseAssessment, "Preparation unexpectedly requested native dependency reads.");
            var requested = ReadPreparation433.Extract("inspect_objects", Input(new { document_id = Document, handles = new[] { "A1", "B2" },
                sections = Array.Empty<string>(), erase_assessment = true,
                prepare_for = new { operation = "modify_entities", variant = "erase" } }));
            Check(requested.Selection is { Operation: "modify_entities", Variant: "erase" }, "Existing preparation selection changed.");
            Check(Validation.Parse("inspect_objects", requested.Arguments).EraseAssessment, "Explicit additional inspection was stripped.");
            return Task.CompletedTask;
        });
        await test("block444: published assessment is optional and does not grant permission", () =>
        {
            var inspect = ToolCatalog.Tools.Single(tool => tool.Name == "nanocad.inspect_objects");
            var flag = inspect.InputSchema.GetProperty("properties").GetProperty("erase_assessment");
            Check(flag.GetProperty("type").GetString() == "boolean" && !flag.GetProperty("default").GetBoolean(), "Published opt-in flag changed.");
            Check(!inspect.InputSchema.GetProperty("required").EnumerateArray().Any(value => value.GetString() == "erase_assessment"), "Assessment became mandatory.");
            Check(inspect.Description!.Contains("exact complete selected handle set", StringComparison.Ordinal), "Whole-selected-set scope missing.");
            var output = inspect.OutputSchema!.Value.GetRawText();
            Check(output.Contains("erase_assessment") && output.Contains("not_assessed") && output.Contains("rechecked_at_write"), "Output omits technical admission versus permission.");
            var erase = ReadPreparation433.Extract("inspect_objects", Input(new { document_id = Document, handles = new[] { "A1" },
                prepare_for = new { operation = "modify_entities", variant = "erase" } })).Selection!.Schema.GetRawText();
            Check(erase.Contains("native") && erase.Contains("shared") && erase.Contains("erase_assessment"), "Erase protection or evidence workflow missing.");
            Check(erase.Contains("Hatch") && erase.Contains("unselected boundary"), "Hatch source protection regressed.");
            Check(ServerGuidance.Instructions.Contains("proven-unused", StringComparison.Ordinal) &&
                ServerGuidance.Instructions.Contains("erase_assessment=true", StringComparison.Ordinal),
                "Engineering guidance omits task-owned cleanup and its assessment requirement.");
            return Task.CompletedTask;
        });
    }
}
