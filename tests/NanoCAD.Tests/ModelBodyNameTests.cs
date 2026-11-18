using System.Text.Json;
using NanoCAD.Contracts;
using NanoCAD.McpServer;

internal static class ModelBodyNameTests
{
    private static JsonElement Request(params object[] operations) => Wire.Element(new
    {
        bridge_instance_id = "11111111-1111-1111-1111-111111111111",
        document_id = "22222222-2222-2222-2222-222222222222",
        operation_id = "33333333-3333-3333-3333-333333333333",
        expected_revision = "1",
        units = "Millimeters",
        operations
    });

    private static object Name(string handle, string label) =>
        new { operation = "name_model_body", handles = new[] { handle }, name = label };

    private static void MustReject(params object[] operations)
    {
        try { ModifyValidation.Parse(Request(operations)); }
        catch (DomainException error) when (error.Error.code == "INVALID_ARGUMENT") { return; }
        throw new Exception("Invalid body name input was accepted.");
    }

    public static async Task Run(Func<string, Func<Task>, Task> test)
    {
        await test("model body labels preserve display text and distinguish identity from name", () =>
        {
            const string label = "\u0412\u0442\u0443\u043B\u043A\u0430 \u00D840";
            var edits = ModifyValidation.Parse(Request(
                new { operation = "name_model_body", handles = new[] { "a" }, name = label, expected_name = "" },
                Name("B", label)));
            if (edits.Operations is not [ModelBodyNameEdit first, ModelBodyNameEdit second] ||
                first.Handles.Single() != "A" || second.Handles.Single() != "B" ||
                first.Name != label || second.Name != label || first.ExpectedName != "" ||
                second.ExpectedName != null || !WriteRecovery.NotGuaranteed(edits))
                throw new Exception("Independent display names or the empty expected name were lost.");

            var single = ModifyValidation.Parse(Request(Name("A", label)));
            var expected = ModifyValidation.Parse(Request(new { operation = "name_model_body", handles = new[] { "A" }, name = label, expected_name = "" }));
            if (single.Fingerprint() == expected.Fingerprint())
                throw new Exception("Changing the expected body identity state must change the replay fingerprint.");
            var branch = ToolCatalog.InputContract("modify_entities").GetProperty("properties")
                .GetProperty("operations").GetProperty("items").GetProperty("oneOf").EnumerateArray()
                .Single(choice => choice.GetProperty("properties").GetProperty("operation").GetProperty("const").GetString() == "name_model_body");
            if (branch.GetProperty("properties").GetProperty("handles").GetProperty("maxItems").GetInt32() != 1 ||
                !branch.GetProperty("properties").TryGetProperty("expected_name", out _) ||
                branch.GetProperty("required").EnumerateArray().Any(field => field.GetString() == "expected_name"))
                throw new Exception("Published body name inputs disagree with the parser.");
            return Task.CompletedTask;
        });
        await test("reject invalid body naming batches and invisible labels before a native edit", () =>
        {
            MustReject(Name("A", " \t"));
            MustReject(Name("A", "   "));
            MustReject(Name("A", "Housing\0cap"));
            MustReject(Name("A", "Housing"), Name("a", "Cover"));
            MustReject(Name("A", "Housing"), new { operation = "erase", handles = new[] { "B" } });
            MustReject(new { operation = "name_model_body", handles = new[] { "A", "B" }, name = "Bearing" });
            MustReject(new { operation = "name_model_body", handles = new[] { "A" }, name = "Bearing", expected_name = 7 });
            return Task.CompletedTask;
        });
    }
}
