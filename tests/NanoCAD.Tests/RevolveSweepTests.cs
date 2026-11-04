using System.Text.Json;
using NanoCAD.Contracts;
using NanoCAD.McpServer;

internal static class RevolveSweepTests
{
    public static async Task Run(Func<string, Func<Task>, Task> test)
    {
        string bridge = Guid.NewGuid().ToString("D"), doc = Guid.NewGuid().ToString("D"), op = Guid.NewGuid().ToString("D");
        CreateArguments Parse(params object[] entities) => CreateValidation.Parse(Wire.Element(new { bridge_instance_id = bridge, document_id = doc, operation_id = op, expected_revision = "1", units = "Millimeters", entities }));
        object Revolve(double angle = 2 * Math.PI, double start = 0, double radius = 2, double x = 0) => new { type = "Solid3d", kind = "revolve", profile = new[] { new[] { radius, 0 }, new[] { 4d, 0 }, new[] { 4d, 6 }, new[] { radius, 6 } }, axis_origin = new[] { x, 0, 0 }, angle, start_angle = start };
        object Sweep(object path, double radius = 1) => new { type = "Solid3d", kind = "sweep", radius, path };
        object Line(double z = 10) => new { kind = "line", start = new[] { 0d, 0, 0 }, end = new[] { 0d, 0, z } };
        object Arc(double radius = 10, double angle = Math.PI / 2) => new { kind = "arc", center = new[] { 30d, 40, 5 }, radius, start_angle = 0d, sweep_angle = angle };
        void Reject(object entity) { try { Parse(entity); } catch (DomainException) { return; } throw new Exception("Invalid geometry accepted."); }
        await test("solid revolve retains radial axial geometry origin and angular intent", () =>
        {
            var a = Parse(Revolve()); var spec = (RevolvedSolidSpec)a.Entities.Single();
            if (spec.Profile[2] != new Point2(4, 6) || spec.Angle != Math.PI * 2) throw new Exception("Revolve intent lost.");
            foreach (var changed in new[] { Revolve(Math.PI), Revolve(start: 1), Revolve(x: 5), Revolve(radius: 1) })
                if (Parse(changed).Fingerprint() == a.Fingerprint()) throw new Exception("Revolve payload change missing from ledger fingerprint.");
            return Task.CompletedTask;
        });
        await test("solid revolve rejects axis crossing zero angle and malformed boundaries", () =>
        {
            Reject(Revolve(radius: -1)); Reject(Revolve(angle: 0)); Reject(Revolve(angle: Math.PI * 2 + 0.1)); Reject(Revolve(start: Math.PI * 2));
            Reject(new { type = "Solid3d", kind = "revolve", profile = new[] { new[] { 0d, 0 }, new[] { 2d, 2 }, new[] { 0d, 2 }, new[] { 2d, 0 } }, axis_origin = new[] { 0d, 0, 0 }, angle = Math.PI });
            return Task.CompletedTask;
        });
        await test("solid sweep retains 3D path and rejects self-overlap or unsupported controls", () =>
        {
            var a = Parse(Sweep(Line())); if (((SweptSolidSpec)a.Entities.Single()).Path.End != new Point3(0, 0, 10)) throw new Exception("XYZ path lost.");
            foreach (var changed in new[] { Sweep(Line(20)), Sweep(Line(), 2), Sweep(Arc()) })
                if (Parse(changed).Fingerprint() == a.Fingerprint()) throw new Exception("Sweep intent lost from ledger fingerprint.");
            Reject(Sweep(Line(0))); Reject(Sweep(Line(1e6 + 1))); Reject(Sweep(Arc(1))); Reject(Sweep(Arc(angle: Math.PI + 0.1)));
            Reject(new { type = "Solid3d", kind = "sweep", radius = 1d, path = Line(), twist = 1d });
            return Task.CompletedTask;
        });
        await test("solid revolve sweep share batch budget and remain outside block definitions", () =>
        {
            var pair = new[] { Revolve(), Sweep(Line()) };
            if (Parse(Enumerable.Range(0, 9).Select(i => pair[i % 2]).ToArray()).Entities.Length != 9) throw new Exception("Former body count ceiling remains.");
            foreach (var entity in pair) { try { CreateValidation.ParseEntities(Wire.Element(new[] { entity })); throw new Exception("Solid entered block definition scope."); } catch (DomainException) { } }
            File.WriteAllText(Path.Combine(Environment.GetEnvironmentVariable("NANOCAD_MCP_HOME")!, "tools-list.json"), JsonSerializer.Serialize(ToolCatalog.Tools, Wire.Json));
            return Task.CompletedTask;
        });
    }
}
