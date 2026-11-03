using System.Text.Json;
using NanoCAD.Contracts;
using NanoCAD.McpServer;

internal static class DimensionCreateTests
{
    private static CreateArguments Parse(params object[] entities) => CreateValidation.Parse(Wire.Element(new
    { bridge_instance_id = "10000000-0000-0000-0000-000000000001", document_id = "10000000-0000-0000-0000-000000000002",
        operation_id = "10000000-0000-0000-0000-000000000003", expected_revision = "0", units = "Millimeters", entities }));
    private static object Linear(string kind = "aligned", double endX = 30, string style = "Standard") => new
    { type = "Dimension", kind, dimension_style = style, xline1 = new[] { 0d, 0 }, xline2 = new[] { endX, 40 }, dimline_point = new[] { 5d, 50 } };
    public static async Task Run(Func<string, Func<Task>, Task> test)
    {
        await test("dimension creation validates geometry style and replay intent", () =>
        {
            var first = Parse(Linear()); var changed = Parse(Linear(endX: 31)); var style = Parse(Linear(style: "Other"));
            if (first.Entities[0] is not DimensionSpec { Kind: "aligned", DimensionStyle: "Standard", TextOverride: "<>" } ||
                first.Fingerprint() == changed.Fingerprint() || first.Fingerprint() == style.Fingerprint()) throw new Exception("Dimension identity/intent lost.");
            foreach (string kind in new[] { "radial", "diametric" })
            {
                var fields = new Dictionary<string, object> { ["type"] = "Dimension", ["kind"] = kind, ["dimension_style"] = "Standard", ["chord_point"] = new[] { 10, 0 } };
                fields[kind == "radial" ? "center" : "far_chord_point"] = new[] { 0, 0 };
                if (Parse(fields).Entities[0] is not DimensionSpec) throw new Exception("Dimension variant lost.");
            }
            return Task.CompletedTask;
        });
        await test("dimension creation rejects degenerate late and block-member requests before native dispatch", () =>
        {
            void Reject(Action action) { try { action(); } catch (DomainException) { return; } throw new Exception("Expected refusal."); }
            Reject(() => CreateValidation.ParseEntities(Wire.Element(new[] { Linear() })));
            var blockVariants = BlockWriteSchema.Define().GetProperty("properties").GetProperty("entities").GetProperty("items").GetProperty("oneOf");
            if (blockVariants.EnumerateArray().Any(v => v.GetProperty("properties").GetProperty("type").GetProperty("const").GetString() == "Dimension"))
                throw new Exception("New dimensions leaked into block-definition input schema.");
            Reject(() => Parse(Enumerable.Repeat(Linear(), 17).ToArray()));
            Reject(() => Parse(Linear(), new { type = "Dimension", kind = "aligned" }));
            Reject(() => Parse(new { type = "Dimension", kind = "rotated", dimension_style = "Standard", xline1 = new[] { 0,0 }, xline2 = new[] { 0,10 }, dimline_point = new[] { 10,10 }, rotation = 0 }));
            return Task.CompletedTask;
        });
    }
}

