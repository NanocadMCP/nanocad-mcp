using System.Text.Json;
using NanoCAD.Contracts;
using NanoCAD.McpServer;

internal static class DrawingViewCreateTests
{
    internal static Dictionary<string, object> Edit() => new() { ["operation"] = "create_drawing_views", ["source_handle"] = "00071", ["name_prefix"] = "Views", ["front_origin"] = new[] { 2000d, 0 }, ["top_origin"] = new[] { 2000d, 1000 }, ["scale"] = 0.5 };
    internal static Dictionary<string, object> Input(params Dictionary<string, object>[] edits) => new() { ["bridge_instance_id"] = "11111111-1111-1111-1111-111111111111", ["document_id"] = "22222222-2222-2222-2222-222222222222", ["operation_id"] = "33333333-3333-3333-3333-333333333333", ["expected_revision"] = "0012", ["units"] = "Millimeters", ["operations"] = edits };
    private static StructureWriteArguments Parse(Dictionary<string, object> edit) => StructureWriteValidation.Parse(Wire.Element(Input(edit)));
    private static void Reject(Dictionary<string, object> input)
    { try { StructureWriteValidation.Parse(Wire.Element(input)); } catch (DomainException) { return; } throw new Exception("Invalid drawing-view creation accepted."); }
    public static async Task Run(Func<string, Func<Task>, Task> test)
    {
        await test("drawing view creation requires explicit placement scale and source", () =>
        {
            var args = Parse(Edit()); var spec = (CreateDrawingViewsEdit)args.Operations.Single();
            if (spec.SourceHandle != "71" || args.ExpectedRevision != "12" || spec.Scale != 0.5 || spec.TopOrigin?.Y != 1000) throw new Exception("Wrong normalization.");
            foreach (var key in new[] { "source_handle", "name_prefix", "front_origin", "scale" }) { var edit = Edit(); edit.Remove(key); Reject(Input(edit)); }
            return Task.CompletedTask;
        });
        await test("drawing view creation rejects ambiguous or excessive placements and mixed batches", () =>
        {
            foreach (var value in new[] { new[] { 2001d, 1000 }, new[] { 2000d, 0 }, new[] { 2000d, -1 }, new[] { 2000d, 0, 0 }, new[] { 2000d, 1e6 + 1 } }) { var edit = Edit(); edit["top_origin"] = value; Reject(Input(edit)); }
            foreach (var value in new[] { 0d, -1, 1001, 0.0001 }) { var edit = Edit(); edit["scale"] = value; Reject(Input(edit)); }
            var unknown = Edit(); unknown["command"] = "anything"; Reject(Input(unknown));
            var units = Input(Edit()); units["units"] = "Inches"; Reject(units);
            Reject(Input(Edit(), new() { ["operation"] = "create_layer", ["name"] = "L" })); Reject(Input(Edit(), Edit()));
            return Task.CompletedTask;
        });
        await test("drawing view creation fingerprint binds every geometric field", () =>
        {
            var original = Parse(Edit()).Fingerprint();
            foreach (var key in new[] { "source_handle", "name_prefix", "front_origin", "top_origin", "scale" })
            {
                var edit = Edit(); edit[key] = key switch { "source_handle" => "72", "name_prefix" => "Other", "front_origin" => new[] { 2000d, 1 }, "top_origin" => new[] { 2000d, 1001 }, _ => 1d };
                if (Parse(edit).Fingerprint() == original) throw new Exception("Unbound field: " + key);
            }
            var normalized = Edit(); normalized["source_handle"] = "71"; if (Parse(normalized).Fingerprint() != original) throw new Exception("Equivalent handles differ.");
            return Task.CompletedTask;
        });
        await test("drawing view creation schema is a structure operation with focused placement guidance", () =>
        {
            var tool = ToolCatalog.Tools.Single(t => t.Name == "nanocad.modify_structure");
            var help = AuditServerContractSupport.Help("modify_structure", "create_drawing_views");
            if (!tool.InputSchema.GetRawText().Contains("create_drawing_views") || !help.Contains("ALL views") || !tool.InputSchema.GetRawText().Contains("iso_bottom_right")) throw new Exception("Missing bounded creation schema or focused scale guidance.");
            var duplicate = JsonDocument.Parse(Wire.Element(Input(Edit())).GetRawText().Replace("\"scale\":0.5", "\"scale\":0.5,\"scale\":1"));
            try { StructureWriteValidation.Parse(duplicate.RootElement); } catch (DomainException) { return Task.CompletedTask; } finally { duplicate.Dispose(); }
            throw new Exception("Duplicate drawing-view field accepted.");
        });
        await test("drawing view projections validate axes quadrants uniqueness and limits", () =>
        {
            var directions = new[] { "bottom", "left", "right", "iso_top_left", "iso_top_right", "iso_bottom_left", "iso_bottom_right" };
            var origins = new[] { new[] { 2000d, -10 }, new[] { 1900d, 0 }, new[] { 2100d, 0 }, new[] { 1900d, 10 }, new[] { 2100d, 10 }, new[] { 1900d, -10 }, new[] { 2100d, -10 } };
            Dictionary<string, object> View(int i) => new() { ["direction"] = directions[i], ["origin"] = origins[i] };
            var edit = Edit(); edit["projected_views"] = Enumerable.Range(0, 7).Select(View).ToArray();
            if (((CreateDrawingViewsEdit)Parse(edit).Operations.Single()).ProjectedViews.Length != 7) throw new Exception("Missing projection.");
            for (var i = 0; i < 7; i++)
            {
                var wrong = View(i); wrong["origin"] = new[] { 2000d, 0 }; edit["projected_views"] = new[] { wrong }; Reject(Input(edit));
                wrong = View(i); wrong["origin"] = new[] { 4000d - origins[i][0], -origins[i][1] }; edit["projected_views"] = new[] { wrong }; Reject(Input(edit));
            }
            edit["projected_views"] = new[] { View(0), View(0) }; Reject(Input(edit));
            edit["projected_views"] = Enumerable.Range(0, 8).Select(i => View(i % 7)).ToArray(); Reject(Input(edit));
            edit["projected_views"] = new[] { new Dictionary<string, object> { { "direction", "front" }, { "origin", new[] { 0, 1 } } } }; Reject(Input(edit));
            edit["projected_views"] = new[] { new Dictionary<string, object> { { "direction", "left" } } }; Reject(Input(edit));
            edit["projected_views"] = null!; Reject(Input(edit));
            return Task.CompletedTask;
        });
        await test("drawing view projections fingerprint binds direction origin and canonical set", () =>
        {
            Dictionary<string, object> View(string direction, double x) => new() { ["direction"] = direction, ["origin"] = new[] { x, 10d } };
            var a = View("iso_top_right", 2100); var b = View("iso_top_left", 1900);
            var edit = Edit(); edit["projected_views"] = new[] { a, b }; var fingerprint = Parse(edit).Fingerprint();
            edit["projected_views"] = new[] { b, a }; if (Parse(edit).Fingerprint() != fingerprint) throw new Exception("Set order changed fingerprint.");
            edit["projected_views"] = new[] { a, View("iso_top_left", 1800) }; if (Parse(edit).Fingerprint() == fingerprint) throw new Exception("Origin unbound.");
            edit["projected_views"] = new[] { a }; if (Parse(edit).Fingerprint() == fingerprint) throw new Exception("Direction set unbound.");
            edit["projected_views"] = Array.Empty<object>(); if (Parse(edit).Fingerprint() != Parse(Edit()).Fingerprint()) throw new Exception("Omitted and empty projections differ.");
            return Task.CompletedTask;
        });
        await test("drawing view creation supports a single oriented root and selected projections", () =>
        {
            var edit = Edit(); edit.Remove("top_origin");
            var root = (CreateDrawingViewsEdit)Parse(edit).Operations.Single();
            if (root.TopOrigin != null || root.ProjectedViews.Length != 0 || root.ProjectionPlane != null) throw new Exception("Omission added a view or changed the legacy projection.");
            var legacy = Parse(edit).Fingerprint();
            edit["projection_plane"] = new { origin = new[] { 0d, 0, 0 }, x_axis = new[] { 1d, 0, 0 }, y_axis = new[] { 0d, 1, 0 } };
            var planar = Parse(edit); var plane = ((CreateDrawingViewsEdit)planar.Operations.Single()).ProjectionPlane!;
            if (plane.XAxis != new Point3(1, 0, 0) || plane.YAxis != new Point3(0, 1, 0) || planar.Fingerprint() == legacy) throw new Exception("Explicit basis is not preserved or fingerprinted.");
            edit["projection_plane"] = new { origin = new[] { 0d, 0, 1 }, x_axis = new[] { 1d, 0, 0 }, y_axis = new[] { 0d, 1, 0 } };
            if (Parse(edit).Fingerprint() == planar.Fingerprint()) throw new Exception("Projection origin is unbound.");
            edit["projection_plane"] = new { origin = new[] { 0d, 0, 0 }, x_axis = new[] { 0d, 1, 0 }, y_axis = new[] { -1d, 0, 0 } };
            if (Parse(edit).Fingerprint() == planar.Fingerprint()) throw new Exception("Rotation within a plane is unbound.");
            edit["projected_views"] = new[] { new { direction = "right", origin = new[] { 2100d, 0 } } };
            var selected = (CreateDrawingViewsEdit)Parse(edit).Operations.Single();
            if (selected.TopOrigin != null || selected.ProjectedViews.Single().Direction != "right") throw new Exception("Selected projection was replaced by a pair.");
            edit["top_origin"] = null!; Reject(Input(edit));
            return Task.CompletedTask;
        });
        await test("drawing view creation rejects incomplete scaled and skew projection frames", () =>
        {
            var edit = Edit();
            foreach (var plane in new object[]{null!,new{origin=new[]{0,0,0}},
                new{origin=new[]{0d,0,0},x_axis=new[]{2d,0,0},y_axis=new[]{0d,1,0}},
                new{origin=new[]{0d,0,0},x_axis=new[]{1d,0,0},y_axis=new[]{1d,0,0}},
                new{origin=new[]{0d,0},x_axis=new[]{1d,0,0},y_axis=new[]{0d,1,0}},
                new{origin=new[]{1e6+1,0,0},x_axis=new[]{1d,0,0},y_axis=new[]{0d,1,0}}})
            { edit["projection_plane"] = plane; Reject(Input(edit)); }
            return Task.CompletedTask;
        });
    }
}
