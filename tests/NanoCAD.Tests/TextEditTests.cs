using System.Text.Json;
using NanoCAD.Contracts;
using NanoCAD.McpServer;

internal static class TextEditTests
{
    public static async Task Run(Func<string, Func<Task>, Task> test)
    {
        var bridge = Guid.NewGuid().ToString("D"); var doc = Guid.NewGuid().ToString("D"); var operation = Guid.NewGuid().ToString("D");
        object Edit(string expected = "Old", string text = "New №7", string[]? handles = null) => new { operation = "replace_text", handles = handles ?? new[] { "00Af" }, expected_text = expected, text };
        ModifyArguments Parse(params object[] edits) => ModifyValidation.Parse(Wire.Element(new { bridge_instance_id = bridge, document_id = doc, expected_revision = "18446744073709551615", operation_id = operation, units = "Millimeters", operations = edits }));
        void Reject(params object[] edits)
        {
            try { Parse(edits); }
            catch (DomainException) { return; }
            throw new Exception("Invalid text edit accepted.");
        }
        await test("text replacement preserves literal identity and fingerprints both expected and new values", () =>
        {
            var args = Parse(Edit()); var edit = (ReplaceTextSpec)args.Operations.Single();
            if (edit.Handles.Single() != "AF" || edit.Text != "New №7" || edit.ExpectedText != "Old" || args.ExpectedRevision != "18446744073709551615") throw new Exception("Text or context changed.");
            if (args.Fingerprint() == Parse(Edit(text: "Different")).Fingerprint() || args.Fingerprint() == Parse(Edit(expected: "Different")).Fingerprint()) throw new Exception("Text intent lost from replay identity.");
            if (args.Fingerprint() != Parse(Edit(handles: new[] { "AF" })).Fingerprint()) throw new Exception("Handle normalization changed intent.");
            var ordered = Parse(Edit(text: "Next"), Edit(expected: "Next", text: "Final"));
            if (ordered.Operations.Length != 2 || ordered.Fingerprint() == Parse(Edit(expected: "Next", text: "Final"), Edit(text: "Next")).Fingerprint()) throw new Exception("Ordered text dependencies lost.");
            return Task.CompletedTask;
        });
        await test("text replacement rejects ambiguous targets invalid literals and erase ordering", () =>
        {
            Reject(Edit(handles: new[] { "AF", "B0" })); Reject(Edit(text: "")); Reject(Edit(text: "a\nb")); Reject(Edit(text: "%<field>%")); Reject(Edit(expected: "%%c10"));
            Reject(Edit(text: new string('x', 4097))); Reject(Edit(expected: new string('x', 16385)));
            Reject(new { operation = "replace_text", handles = new[] { "AF" }, text = "New" });
            Reject(new { operation = "replace_text", handles = new[] { "AF" }, expected_text = "Old", text = "New", evaluate = true });
            var batch = Enumerable.Range(0, 17).Select(_ => Edit(expected: new string('x', 2048), text: new string('y', 2048))).ToArray();
            var parsed = Parse(batch);
            if (parsed.Operations.Length != 17 || parsed.Operations.Any(e => e is not ReplaceTextSpec value || value.ExpectedText.Length != 2048 || value.Text.Length != 2048)) throw new Exception("Literal batch was truncated.");
            if (parsed.Fingerprint() == Parse(batch.Take(16).ToArray()).Fingerprint()) throw new Exception("Final replacement missing from replay identity.");
            Reject(new { operation = "erase", handles = new[] { "AF" } }, Edit());
            return Task.CompletedTask;
        });
        await test("text replacement schema exposes one handle and write annotation with no new tool", () =>
        {
            var tool = ToolCatalog.Tools.Single(t => t.Name == "nanocad.modify_entities");
            var schema = AuditServerContractSupport.Branch("modify_entities", "replace_text");
            var props = schema.GetProperty("properties");
            if (props.GetProperty("handles").GetProperty("maxItems").GetInt32() != 1 || props.GetProperty("text").GetProperty("maxLength").GetInt32() != 4096 || tool.Annotations?.ReadOnlyHint != false) throw new Exception("Replacement capability boundary changed.");
            File.WriteAllText(Path.Combine(Environment.GetEnvironmentVariable("NANOCAD_MCP_HOME")!, "tools-list.json"), JsonSerializer.Serialize(ToolCatalog.Tools, Wire.Json));
            return Task.CompletedTask;
        });
    }
}
