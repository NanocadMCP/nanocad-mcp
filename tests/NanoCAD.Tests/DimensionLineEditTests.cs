using System.Text.Json;
using NanoCAD.Contracts;
using NanoCAD.McpServer;

internal static class DimensionLineEditTests
{
    public static async Task Run(Func<string, Func<Task>, Task> test)
    {
        string bridge = Guid.NewGuid().ToString("D"), doc = Guid.NewGuid().ToString("D"), id = Guid.NewGuid().ToString("D");
        object Edit(double old = 20, double target = 30, string handle = "AF") => new { operation = "set_dimension_line", handles = new[] { handle }, expected_dimline_point = new[] { 0.0, old }, dimline_point = new[] { 0.0, target } };
        ModifyArguments Parse(params object[] edits) => ModifyValidation.Parse(Wire.Element(new { bridge_instance_id = bridge, document_id = doc, operation_id = id, expected_revision = "7", units = "Millimeters", operations = edits }));
        void Reject(params object[] edits) { try { Parse(edits); } catch (DomainException) { return; } throw new Exception("Invalid dimension line edit accepted."); }
        await test("dimension line contract preserves expectation target order and combined bounds", () =>
        {
            var a = Parse(Edit()); var e = (SetDimensionLineSpec)a.Operations.Single();
            if (e.Point != new Point2(0, 30) || e.ExpectedPoint != new Point2(0, 20) || a.Fingerprint() != Parse(Edit(handle: "00af")).Fingerprint()) throw new Exception("Lost placement intent.");
            if (a.Fingerprint() == Parse(Edit(old: 21)).Fingerprint() || a.Fingerprint() == Parse(Edit(target: 31)).Fingerprint()) throw new Exception("Lost placement replay guard.");
            if (Parse(Edit(), Edit(30, 40)).Fingerprint() == Parse(Edit(30, 40), Edit()).Fingerprint()) throw new Exception("Order lost.");
            Reject(Edit(target: 1000001)); Reject(Edit(old: -1000001));
            Reject(new { operation = "set_dimension_line", handles = new[] { "AF", "B0" }, expected_dimline_point = new[] { 0, 20 }, dimline_point = new[] { 0, 30 } });
            Reject(new { operation = "set_dimension_line", handles = new[] { "AF" }, expected_dimline_point = new[] { 0, 20, 0 }, dimline_point = new[] { 0, 30 } });
            var mixed = Enumerable.Range(0, 16).Select(_ => Edit()).Append(new { operation = "replace_dimension_text", handles = new[] { "AF" }, expected_text = "<>", text = "Length <>" }).ToArray(); Parse(mixed);
            return Task.CompletedTask;
        });
        await test("dimension line geometry treats parallel points equally and rejects endpoint orientation and offset errors", () =>
        {
            var before = Wire.Element(new { xline1_point = new[] { 0, 0, 0 }, xline2_point = new[] { 30, 40, 0 }, dimline_point = new[] { -8, 6, 0 } });
            var edit = new SetDimensionLineSpec(new[] { "AF" }, new(22, 46), new(-16, 12));
            var expected = DimensionLineGeometry.Expected(before, edit);
            if (DimensionLineGeometry.Axis(before).Measurement != 50 || !DimensionTextGeometry.SameDefinition(expected, DimensionLineGeometry.At(expected, new(14, 52)))) throw new Exception("Wrong aligned placement/measurement.");
            foreach (var bad in new[] { edit with { ExpectedPoint = new(0, 0) }, edit with { Point = new(30, 40) } })
            { try { DimensionLineGeometry.Expected(before, bad); } catch (DomainException) { continue; } throw new Exception("Invalid physical line accepted."); }
            var rotated = Wire.Element(new { xline1_point = new[] { 0, 0, 0 }, xline2_point = new[] { 60, 40, 0 }, dimline_point = new[] { 0, 20, 0 }, rotation = 0.0 });
            if (DimensionLineGeometry.Axis(rotated).Measurement != 60) throw new Exception("Wrong projected measurement.");
            var result = DimensionLineGeometry.Expected(rotated, new(new[] { "AF" }, new(25, 20), new(30, -15)));
            if (!DimensionTextGeometry.SameDefinition(result, DimensionLineGeometry.At(result, new(90, -15))) || DimensionTextGeometry.SameDefinition(result, DimensionLineGeometry.At(result, new(90, -14)))) throw new Exception("Wrong rotated physical line equivalence.");
            var changed = Wire.Element(new { xline1_point = new[] { 0, 0, 0 }, xline2_point = new[] { 61, 40, 0 }, dimline_point = new[] { 30, -15, 0 }, rotation = 0.0 });
            if (DimensionTextGeometry.SameDefinition(result, changed)) throw new Exception("Measured endpoint mutation accepted.");
            return Task.CompletedTask;
        });
        await test("dimension line schema exposes bounded dedicated edit without new tools", () =>
        {
            var t = ToolCatalog.Tools.Single(t => t.Name == "nanocad.modify_entities");
            var s = AuditServerContractSupport.Branch("modify_entities", "set_dimension_line"); var p = s.GetProperty("properties");
            if (p.GetProperty("handles").GetProperty("maxItems").GetInt32() != 1 || p.GetProperty("dimline_point").GetProperty("items").GetProperty("maximum").GetDouble() != 1e6 || t.Annotations?.ReadOnlyHint != false) throw new Exception("Dimension placement scope changed.");
            File.WriteAllText(Path.Combine(Environment.GetEnvironmentVariable("NANOCAD_MCP_HOME")!, "tools-list.json"), JsonSerializer.Serialize(ToolCatalog.Tools, Wire.Json));
            return Task.CompletedTask;
        });
    }
}
