using System.Text.Json;
using NanoCAD.Contracts;
using NanoCAD.McpServer;

internal static class CurveCreateSnapTests
{
    private static readonly object Center = new
    {
        handle = "a1",
        expected_curve_token = new string('a', 64),
        snap = "center"
    };

    internal static async Task Run(Func<string, Func<Task>, Task> test)
    {
        await test("planar curve snaps retain native intent and materialize through the original geometry parser", () =>
        {
            var circle = Parse(new { type = "Circle", center_snap = Center, radius = 3 });
            Check(circle is CircleFromNativeSnapSpec { Center.Snap.Handle: "A1", Radius: 3 }, "Circle snap source was lost.");
            var resolved = ((INativeSnappedCreation)circle).Materialize(_ => new(12, 25));
            Check(resolved is CircleSpec { Center.X: 12, Center.Y: 25, Radius: 3 }, "Circle resolution changed its radius or center.");
            var arc = Parse(new { type = "Arc", center_snap = Center, radius = 5, start_angle = .25, sweep_angle = 1.5 });
            Check(((INativeSnappedCreation)arc).Materialize(_ => new(-2, 7)) is
                ArcSpec { Center.X: -2, Center.Y: 7, Radius: 5, StartAngle: .25, SweepAngle: 1.5 }, "Arc angles changed.");
            Reject(() => ((INativeSnappedCreation)circle).Materialize(_ => new(1e10, 0)));
            return Task.CompletedTask;
        });
        await test("planar curve snaps reject coordinate conflicts invalid scalars and inline handle references", () =>
        {
            Reject(() => Parse(new { type = "Circle", center = new[] { 0, 0 }, center_snap = Center, radius = 3 }));
            Reject(() => Parse(new { type = "Circle", center_snap = Center, radius = -1 }));
            Reject(() => Parse(new { type = "Arc", center_snap = Center, radius = 3, start_angle = 0, sweep_angle = Math.PI * 2 }));
            Reject(() => CreateValidation.ParseEntities(Wire.Element(new[] { new { type = "Circle", center_snap = Center, radius = 3 } })));
            var legacy = Parse(new { type = "Circle", center = new[] { 4, 8 }, radius = 3 });
            Check(legacy is CircleSpec { Center.X: 4, Center.Y: 8 }, "Existing literal circle creation changed.");
            return Task.CompletedTask;
        });
        await test("planar curve snaps preserve explicit workplanes and normalized replay identity", () =>
        {
            var frame = new { origin = new[] { 5, 6, 7 }, x_axis = new[] { 1, 0, 0 }, y_axis = new[] { 0, 0, 1 } };
            var circle = Parse(new { type = "Circle", center_snap = Center, radius = 3, workplane = frame });
            var resolved = ((INativeSnappedCreation)circle).Materialize(_ => new(12, 9));
            Check(resolved is CircleSpec { Workplane.Origin.Z: 7, Center.X: 12, Center.Y: 9 }, "Materialized workplane was lost.");
            var a = Create(Wire.Element(new { type = "Circle", center_snap = Center, radius = 3 }));
            var b = Create(Wire.Element(new
            {
                radius = 3d,
                layer = "0",
                center_snap = new
                { snap = "center", expected_curve_token = new string('A', 64), handle = "0A1" },
                type = "Circle"
            }));
            Check(a.Fingerprint() == b.Fingerprint(), "Field ordering or normalized token spelling changed replay intent.");
            return Task.CompletedTask;
        });
        await test("Circle and Arc publish callable exclusive center snap fields", () =>
        {
            var schema = ToolCatalog.Tools.Single(tool => tool.Name == "nanocad.create_entities").InputSchema;
            var items = schema.GetProperty("properties").GetProperty("entities").GetProperty("items");
            var variants = items.GetProperty("oneOf");
            foreach (string type in new[] { "Circle", "Arc" })
            {
                var variant = variants.EnumerateArray().Single(item =>
                    item.GetProperty("properties").GetProperty("type").GetProperty("const").GetString() == type);
                var properties = variant.GetProperty("properties");
                var snap = properties.GetProperty("center_snap");
                if (!snap.TryGetProperty("properties", out _)) snap = items.GetProperty("properties").GetProperty("center_snap");
                Check(snap.GetProperty("properties").TryGetProperty("expected_curve_token", out _),
                    "Client cannot discover the native curve freshness field.");
                Check(!variant.GetProperty("required").EnumerateArray().Any(field => field.GetString() == "center") &&
                    variant.GetProperty("allOf")[0].GetProperty("oneOf").GetArrayLength() == 2, "Point alternatives lost their exclusive validation.");
            }
            return Task.CompletedTask;
        });
    }

    private static EntitySpec Parse(object value) => CreateValidation.ParseEntities(Wire.Element(new[] { value }), allowDimensions: true)[0];

    private static CreateArguments Create(JsonElement entity) => CreateValidation.Parse(Wire.Element(new
    {
        bridge_instance_id = "aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa",
        document_id = "bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb",
        operation_id = "cccccccc-cccc-cccc-cccc-cccccccccccc",
        expected_revision = "3",
        units = "Millimeters",
        entities = new[] { entity }
    }));

    private static void Reject(Action action)
    {
        try { action(); }
        catch (DomainException) { return; }
        throw new Exception("Invalid snap creation was accepted.");
    }

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
    }
}
