using System.Text.Json;
using NanoCAD.Contracts;
using NanoCAD.McpServer;

internal static class DimensionTextEditTests
{
    public static async Task Run(Func<string, Func<Task>, Task> test)
    {
        var bridge = Guid.NewGuid().ToString(); var doc = Guid.NewGuid().ToString(); var id = Guid.NewGuid().ToString();
        object Edit(string expected = "<>", string text = "Length <> mm", string[]? handles = null) => new { operation = "replace_dimension_text", handles = handles ?? new[] { "00Af" }, expected_text = expected, text };
        ModifyArguments Parse(params object[] edits) => ModifyValidation.Parse(Wire.Element(new { bridge_instance_id = bridge, document_id = doc, expected_revision = "9", operation_id = id, units = "Millimeters", operations = edits }));
        void Reject(params object[] edits) { try { Parse(edits); } catch (DomainException) { return; } throw new Exception("Invalid dimension text accepted."); }
        await test("dimension text edit preserves measurement token ordered intent and replay identity", () =>
        {
            var args = Parse(Edit()); var edit = (ReplaceDimensionTextSpec)args.Operations.Single();
            if (edit.Handles.Single() != "AF" || edit.Text != "Length <> mm") throw new Exception("Literal/identity changed.");
            if (args.Fingerprint() == Parse(Edit(text: "Radius <> mm")).Fingerprint() || args.Fingerprint() == Parse(Edit(expected: "old")).Fingerprint()) throw new Exception("Replay loses text intent.");
            if (args.Fingerprint() != Parse(Edit(handles: new[] { "AF" })).Fingerprint()) throw new Exception("Normalization differs.");
            Parse(Edit(text: "")); Parse(Edit(), Edit(expected: "Length <> mm", text: "<>"));
            return Task.CompletedTask;
        });
        await test("dimension text edit rejects lost measurement formatting ambiguity and excessive work", () =>
        {
            foreach (string text in new[] { "123", " ", "<> <>", "<>\nmm", "<>\\Pfoo", "{<>}", "%%c<>", "%<field>%", "<x>", new string('x', 255) + "<>" }) Reject(Edit(text: text));
            Reject(Edit(expected: "%<field>%")); Reject(Edit(handles: new[] { "AF", "B0" })); Parse(Enumerable.Range(0, 17).Select(_ => Edit()).ToArray());
            Reject(new { operation = "replace_dimension_text", handles = new[] { "AF" }, text = "<>" });
            Reject(new { operation = "replace_dimension_text", handles = new[] { "AF" }, expected_text = "", text = "<>", evaluate = true });
            Reject(new { operation = "erase", handles = new[] { "AF" } }, Edit()); return Task.CompletedTask;
        });
        await test("dimension text edit schema exposes bounded dedicated mutation without new tool", () =>
        {
            var tool = ToolCatalog.Tools.Single(t => t.Name == "nanocad.modify_entities");
            var variant = AuditServerContractSupport.Branch("modify_entities", "replace_dimension_text");
            if (variant.GetProperty("properties").GetProperty("handles").GetProperty("maxItems").GetInt32() != 1 || variant.GetProperty("properties").GetProperty("text").GetProperty("maxLength").GetInt32() != 256 || tool.Annotations?.ReadOnlyHint != false) throw new Exception("Dimension scope changed.");
            File.WriteAllText(Path.Combine(Environment.GetEnvironmentVariable("NANOCAD_MCP_HOME")!, "tools-list.json"), JsonSerializer.Serialize(ToolCatalog.Tools, Wire.Json));
            return Task.CompletedTask;
        });
        await test("dimension text geometry accepts only sliding along the same dimension line", () =>
        {
            object Aligned(double x, double y, double secondX = 3) => new { xline1_point = new[] { 0, 0, 0 }, xline2_point = new[] { secondX, 4, 0 }, dimline_point = new[] { x, y, 0 } };
            var before = Wire.Element(Aligned(4, -3));
            if (!DimensionTextGeometry.SameDefinition(before, Wire.Element(Aligned(7, 1))) || DimensionTextGeometry.SameDefinition(before, Wire.Element(Aligned(7, 1.001))) || DimensionTextGeometry.SameDefinition(before, Wire.Element(Aligned(7, 1, 3.001)))) throw new Exception("Aligned perpendicular offset or measured endpoint changed.");
            object Rotated(double x, double y) => new { xline1_point = new[] { 0, 0, 0 }, xline2_point = new[] { 3, 4, 0 }, rotation = Math.PI / 2, dimline_point = new[] { x, y, 0 } };
            if (!DimensionTextGeometry.SameDefinition(Wire.Element(Rotated(8, 0)), Wire.Element(Rotated(8, 20))) || DimensionTextGeometry.SameDefinition(Wire.Element(Rotated(8, 0)), Wire.Element(Rotated(8.001, 20)))) throw new Exception("Rotated line geometry mismatch.");
            return Task.CompletedTask;
        });
    }
}
