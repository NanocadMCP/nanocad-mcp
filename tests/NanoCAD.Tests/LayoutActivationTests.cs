using System.Text.Json;
using NanoCAD.Contracts;

internal static class LayoutActivationTests
{
    private static void Check(bool value, string text) { if (!value) throw new Exception(text); }
    private static Dictionary<string, object?> Input() => new() { ["bridge_instance_id"] = "11111111-1111-4111-8111-111111111111",
        ["operation_id"] = "22222222-2222-4222-8222-222222222222", ["document_id"] = "33333333-3333-4333-8333-333333333333",
        ["expected_revision"] = "0004", ["action"] = "activate_layout", ["layout_handle"] = "001e" };
    private static void Reject(Action action)
    { try { action(); throw new Exception("Accepted invalid input"); } catch (DomainException error) { Check(error.Error.code is "INVALID_ARGUMENT" or "INVALID_HANDLE", "input refusal"); } }
    public static async Task Run(Func<string, Func<Task>, Task> test)
    {
        await test("layout activation binds canonical layout document revision and bridge to replay intent", () =>
        {
            var input = Input(); var parsed = DocumentValidation.Parse(Wire.Element(input));
            Check(parsed.LayoutHandle == "1E" && parsed.ExpectedRevision == "4" && parsed.Action == "activate_layout", "canonical typed request");
            input["layout_handle"] = "1E"; input["expected_revision"] = "4";
            Check(parsed.Fingerprint() == DocumentValidation.Parse(Wire.Element(input)).Fingerprint(), "same intent");
            foreach (var pair in new[] { ("layout_handle", "1A"), ("expected_revision", "5"), ("document_id", "44444444-4444-4444-8444-444444444444") })
            { var altered = Input(); altered[pair.Item1] = pair.Item2; Check(parsed.Fingerprint() != DocumentValidation.Parse(Wire.Element(altered)).Fingerprint(), "changed " + pair.Item1); }
            return Task.CompletedTask;
        });
        await test("layout activation rejects file flags commands missing lifetimes and duplicate fields", () =>
        {
            foreach (string key in Input().Keys) { var input = Input(); input.Remove(key); Reject(() => DocumentValidation.Parse(Wire.Element(input))); }
            foreach (string key in new[] { "path", "read_only", "overwrite", "save", "command", "close_after" })
            { var input = Input(); input[key] = true; Reject(() => DocumentValidation.Parse(Wire.Element(input))); }
            foreach (string handle in new[] { "", "0", "ZZ", "1234567890ABCDEF0" })
            { var input = Input(); input["layout_handle"] = handle; Reject(() => DocumentValidation.Parse(Wire.Element(input))); }
            string raw = JsonSerializer.Serialize(Input()).Replace("\"layout_handle\":\"001e\"", "\"layout_handle\":\"1E\",\"layout_handle\":\"1A\"");
            using var duplicate = JsonDocument.Parse(raw); Reject(() => DocumentValidation.Parse(duplicate.RootElement));
            return Task.CompletedTask;
        });
        await test("layout activation handle remains unavailable to previous document actions", () =>
        {
            foreach (string action in new[] { "activate", "save", "open", "export_dxf" })
            { var input = Input(); input["action"] = action; Reject(() => DocumentValidation.Parse(Wire.Element(input))); }
            return Task.CompletedTask;
        });
    }
}
