using System.Text.Json;
using System.Text.Json.Nodes;
using NanoCAD.Contracts;
using NanoCAD.McpServer;

internal static class BlockCurveWriteTests
{
    private static JsonObject Definition() => JsonSerializer.SerializeToNode(new
    {
        operation = "define_block",
        name = "CurveBlock",
        base_point = new[] { 10, 20 },
        entities = new object[]{
        new{type="Ellipse",center=new[]{5,6},major_axis=new[]{6,8},radius_ratio=.5,start_parameter=.2,sweep_parameter=1.1},
        new{type="Spline",degree=2,control_points=new[]{new[]{0,0},new[]{3,4},new[]{8,0}},knots=new[]{0,0,0,1,1,1},weights=new[]{1d,.5,1d}}}
    })!.AsObject();
    private static DefineBlockEdit Parse(JsonObject o) => BlockWriteValidation.ParseDefine(Wire.Element(o));
    private static string Fingerprint(DefineBlockEdit edit) => new StructureWriteArguments("11111111-1111-4111-8111-111111111111", "22222222-2222-4222-8222-222222222222", "1", "33333333-3333-4333-8333-333333333333", "Millimeters", new StructureEdit[] { edit }).Fingerprint();
    public static async Task Run(Func<string, Func<Task>, Task> test)
    {
        void Deny(Action action) { try { action(); } catch (DomainException) { return; } throw new Exception("Unsupported curve block accepted"); }
        await test("curve block preserves local frames and nested geometry fingerprint", () =>
        {
            var original = Definition(); var parsed = Parse(original);
            if (parsed.BasePoint != new Point2(10, 20) || ((EllipseSpec)parsed.Entities[0]).Center != new Point2(5, 6) || ((SplineSpec)parsed.Entities[1]).ControlPoints[1] != new Point2(3, 4)) throw new Exception("Definition coordinates were reinterpreted");
            Action<JsonObject>[] changes ={o=>o["base_point"]![0]=11,o=>o["entities"]![0]!["center"]![0]=6,o=>o["entities"]![0]!["major_axis"]![1]=9,
                o=>o["entities"]![0]!["radius_ratio"]=.6,o=>o["entities"]![0]!["start_parameter"]=.3,o=>o["entities"]![0]!["sweep_parameter"]=1.2,
                o=>o["entities"]![1]!["control_points"]![1]![0]=4,o=>o["entities"]![1]!["weights"]![1]=.6,
                o=>{o["entities"]![1]!["degree"]=1;o["entities"]![1]!["knots"]=new JsonArray(0,0,.5,1,1);}};
            foreach (var change in changes) { var altered = (JsonObject)original.DeepClone(); change(altered); if (Fingerprint(Parse(altered)) == Fingerprint(parsed)) throw new Exception("Nested curve intent missing from fingerprint"); }
            var a = (JsonObject)original.DeepClone(); a["entities"]![1]!["degree"] = 1; a["entities"]![1]!["knots"] = new JsonArray(0, 0, .4, 1, 1); var b = (JsonObject)a.DeepClone(); b["entities"]![1]!["knots"]![2] = .6;
            if (Fingerprint(Parse(a)) == Fingerprint(Parse(b))) throw new Exception("Internal knot omitted"); return Task.CompletedTask;
        });
        await test("curve block opt-in retains layer zero and excludes annotations solids and nested inserts", () =>
        {
            Deny(() => CreateValidation.ParseEntities(Wire.Element(Definition()["entities"]!)));
            var bad = Definition(); bad["entities"]![0]!["layer"] = "Other"; Deny(() => Parse(bad));
            bad = Definition(); bad["entities"]![1]!["closed"] = true; Deny(() => Parse(bad));
            foreach (var type in new[] { "MText", "Dimension", "Hatch", "Solid3d", "BlockReference" }) { bad = Definition(); bad["entities"]![0]!["type"] = type; Deny(() => Parse(bad)); }
            var curves = CreateValidation.ParseEntities(Wire.Element(Definition()["entities"]!), allowCurves: true); if (curves.Length != 2) throw new Exception("Curves opt-in missing"); return Task.CompletedTask;
        });
        await test("curve block aggregate preview budget includes spline controls across definitions", () =>
        {
            var points = Enumerable.Range(0, 64).Select(i => new Point2(i, 0)).ToArray(); var knots = new[] { 0d }.Concat(Enumerable.Range(0, 64).Select(i => i / 63d)).Append(1d).ToArray();
            var spline = new SplineSpec(1, points, knots, Array.Empty<double>(), "0");
            var poly = new PolylineSpec(Enumerable.Range(0, 1000).Select(i => new Vertex2(new Point2(i, 0), 0)).ToArray(), false, "0");
            var members = Enumerable.Repeat<EntitySpec>(poly, 3).Concat(Enumerable.Repeat<EntitySpec>(spline, 8)).ToArray();
            var defs = Enumerable.Range(0, 3).Select(i => (StructureEdit)new DefineBlockEdit("B" + i, new(0, 0), members, Array.Empty<BlockAttributeSpec>())).ToArray();
            BlockWriteValidation.ValidateBatch(defs.Take(2)); if (BlockWriteValidation.ValidateBatch(defs) != 36) throw new Exception("Former preview count ceiling retained"); return Task.CompletedTask;
        });
        await test("curve block schema exposes supported members with correct local vector meaning", () =>
        {
            var schema = BlockWriteSchema.Define(); var variants = schema.GetProperty("properties").GetProperty("entities").GetProperty("items").GetProperty("oneOf");
            var types = variants.EnumerateArray().Select(v => v.GetProperty("properties").GetProperty("type").GetProperty("const").GetString()).ToHashSet();
            if (!types.SetEquals(new[] { "Line", "Circle", "Arc", "Ellipse", "Spline", "Polyline", "Text", "MText", "Hatch", "BlockReference" })) throw new Exception("Block variants differ");
            foreach (var variant in variants.EnumerateArray()) if (variant.GetProperty("properties").GetProperty("layer").GetProperty("const").GetString() != "0") throw new Exception("Inherited layer scope lost");
            var ellipse = variants.EnumerateArray().Single(v => v.GetProperty("properties").GetProperty("type").GetProperty("const").GetString() == "Ellipse");
            var axis = ellipse.GetProperty("properties").GetProperty("major_axis").GetProperty("description").GetString()!;
            if (!axis.Contains("local XY major-axis vector") || !axis.Contains("not an endpoint") || !schema.GetProperty("description").GetString()!.Contains("base_point is not subtracted")) throw new Exception("Local curve meaning lost");
            File.WriteAllText(Path.Combine(Environment.GetEnvironmentVariable("NANOCAD_MCP_HOME")!, "tools-list.json"), JsonSerializer.Serialize(ToolCatalog.Tools, Wire.Json)); return Task.CompletedTask;
        });
    }
}
