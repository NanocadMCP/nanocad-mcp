using System.Text.Json.Nodes;
using NanoCAD.Contracts;
using NanoCAD.McpServer;

internal static class DrawingProfileTests
{
    internal static async Task Run(Func<string, Func<Task>, Task> test)
    {
        var id = Guid.NewGuid().ToString("D");
        JsonObject Input(JsonObject operation) => new() { ["bridge_instance_id"] = id, ["document_id"] = id, ["expected_revision"] = "1", ["operation_id"] = id, ["units"] = "Millimeters", ["operations"] = new JsonArray(operation) };
        void Deny(Action action) { try { action(); } catch (DomainException) { return; } throw new Exception("Invalid drawing-profile operation accepted"); }
        SketchConstraintCurve Line(string h, double x, double y, double a, double b) => new(h, "line", new(x, y), new(a, b));
        await test("drawing profile resolves unordered reversed concave contours with analytic moments", () =>
        {
            var curves = new[] { Line("A", 0, 0, 4, 0), Line("B", 4, 2, 4, 0), Line("C", 2, 2, 4, 2), Line("D", 2, 2, 2, 4), Line("E", 0, 4, 2, 4), Line("F", 0, 0, 0, 4) };
            var ordered = new[] { curves[0], curves[4], curves[2], curves[5], curves[1], curves[3] }; var v = DrawingProfileGeometry.Measure(ordered, -3);
            if (Math.Abs(v.Area - 12) > 1e-8 || Math.Abs(v.Volume - 36) > 1e-8 || Math.Abs(v.Centroid.X - 5d / 3) > 1e-8 || Math.Abs(v.Centroid.Y - 5d / 3) > 1e-8 || v.Centroid.Z != -1.5) throw new Exception("Concave polygon moments incorrect");
            Deny(() => DrawingProfileGeometry.Profile(curves.Take(5).ToArray()));
            Deny(() => DrawingProfileGeometry.Profile(new[] { Line("A", 0, 0, 4, 4), Line("B", 4, 4, 0, 4), Line("C", 0, 4, 4, 0), Line("D", 4, 0, 0, 0) }));
            Deny(() => DrawingProfileGeometry.Profile(curves.Concat(new[] { Line("G", 4, 0, 8, 0) }).ToArray())); return Task.CompletedTask;
        });
        await test("drawing relations verify tangent collinear point distance and type mismatches", () =>
        {
            var curves = new[] { Line("A", 0, 0, 10, 0), Line("B", 12, 0, 18, 0), new SketchConstraintCurve("C", "circle", Center: new(5, 3), Radius: 3), new SketchConstraintCurve("D", "circle", Center: new(11, 3), Radius: 3) };
            SketchConstraintSpec Spec(string kind, string a, string? b = null, string? p = null, string? q = null, double? n = null) => new(kind, a, b, p, q, n);
            DrawingProfileGeometry.Validate(new[] { Spec("tangent", "A", "C"), Spec("tangent", "C", "D"), Spec("collinear", "A", "B"), Spec("distance", "C", "D", "center", "center", 6), Spec("fix_point", "A", p: "start") }, curves);
            Deny(() => DrawingProfileGeometry.Validate(new[] { Spec("distance", "C", "D", "center", "center", 7) }, curves));
            Deny(() => DrawingProfileGeometry.Validate(new[] { Spec("tangent", "A", "B") }, curves));
            Deny(() => DrawingProfileGeometry.Validate(new[] { Spec("fix_point", "C", p: "end") }, curves)); return Task.CompletedTask;
        });
        await test("drawing constraint contract binds all handles and rejects ambiguous extra fields", () =>
        {
            var operation = new JsonObject { ["operation"] = "add_drawing_constraints", ["handles"] = new JsonArray("a", "b"), ["constraints"] = new JsonArray(new JsonObject { ["kind"] = "coincident", ["first"] = "00A", ["second"] = "B", ["first_point"] = "end", ["second_point"] = "start" }) };
            var input = Input(operation); var edit = (DrawingProfileEdit)ModifyValidation.Parse(Wire.Element(input)).Operations.Single(); if (edit.Constraints![0].First != "A") throw new Exception("Source handle not normalized");
            var relation = operation["constraints"]![0]!.AsObject(); relation["second"] = "C"; Deny(() => ModifyValidation.Parse(Wire.Element(input))); relation["second"] = "B";
            relation["value"] = 1; Deny(() => ModifyValidation.Parse(Wire.Element(input))); relation.Remove("value");
            operation["constraints"]!.AsArray().Add(new JsonObject { ["kind"] = "coincident", ["first"] = "B", ["second"] = "A", ["first_point"] = "start", ["second_point"] = "end" }); Deny(() => ModifyValidation.Parse(Wire.Element(input))); return Task.CompletedTask;
        });
        await test("drawing dimension and profile contract requires current preconditions and sole operation", () =>
        {
            var op = new JsonObject { ["operation"] = "set_drawing_dimension", ["handles"] = new JsonArray("A", "B"), ["constraint_id"] = "{" + id + "}", ["expected_expression"] = "30", ["expected_value"] = 30, ["value"] = 45 }; var input = Input(op);
            if (ModifyValidation.Parse(Wire.Element(input)).Operations[0] is not DrawingProfileEdit { Value: 45, ExpectedValue: 30 }) throw new Exception("Dimension guards lost");
            op.Remove("expected_expression"); Deny(() => ModifyValidation.Parse(Wire.Element(input)));
            op = new JsonObject { ["operation"] = "extrude_drawing_profile", ["handles"] = new JsonArray("A", "B", "C"), ["distance"] = 10, ["direction"] = "negative", ["name"] = "Plate" }; input = Input(op); ModifyValidation.Parse(Wire.Element(input));
            input["operations"]!.AsArray().Add(op.DeepClone()); Deny(() => ModifyValidation.Parse(Wire.Element(input))); return Task.CompletedTask;
        });
        await test("drawing profile catalog exposes general workflow and honest recovery limits", () =>
        {
            var tool = ToolCatalog.Tools.Single(t => t.Name == "nanocad.modify_entities"); var schema = tool.InputSchema.GetRawText();
            foreach (var word in new[] { "add_drawing_constraints", "set_drawing_dimension", "extrude_drawing_profile" }) if (!schema.Contains(word)) throw new Exception("Missing profile operation: " + word);
            var help = AuditServerContractSupport.Help("modify_entities", "add_drawing_constraints");
            foreach (var word in new[] { "ROLLBACK_NOT_GUARANTEED", "Underconstrained" }) if (!help.Contains(word)) throw new Exception("Missing focused profile guidance: " + word);
            if (!tool.OutputSchema!.Value.GetRawText().Contains("drawing_profile_changes")) throw new Exception("Missing model associations"); return Task.CompletedTask;
        });
        await test("drawing coordinate dimensions measure axis separation not aligned distance", () =>
        {
            var curves = new[] { Line("A", 0, 0, -3, 4), new SketchConstraintCurve("B", "circle", Center: new(8, -6), Radius: 2) };
            DrawingProfileGeometry.Validate(new[] { new SketchConstraintSpec("distance_x", "A", "A", "start", "end", 3), new("distance_y", "A", "A", "end", "start", 4), new("distance_x", "B", "A", "center", "start", 8), new("distance_y", "A", "B", "start", "center", 6) }, curves);
            Deny(() => DrawingProfileGeometry.Validate(new[] { new SketchConstraintSpec("distance_x", "A", "A", "start", "end", 5) }, curves));
            Deny(() => DrawingProfileGeometry.Validate(new[] { new SketchConstraintSpec("distance_y", "B", "B", "center", "center", .001) }, curves));
            Deny(() => DrawingProfileGeometry.Validate(new[] { new SketchConstraintSpec("distance_x", "A", "B", "start", "end", 8) }, curves)); return Task.CompletedTask;
        });
        await test("drawing coordinate dimension contract allows distinct same-line points and rejects duplicates", () =>
        {
            var relation = new JsonObject { ["kind"] = "distance_x", ["first"] = "A", ["second"] = "A", ["first_point"] = "start", ["second_point"] = "end", ["value"] = 3 };
            var op = new JsonObject { ["operation"] = "add_drawing_constraints", ["handles"] = new JsonArray("A"), ["constraints"] = new JsonArray(relation) }; var input = Input(op);
            var edit = (DrawingProfileEdit)ModifyValidation.Parse(Wire.Element(input)).Operations.Single(); if (edit.Constraints![0].Kind != "distance_x") throw new Exception("Axis dimension missing");
            relation["second_point"] = "start"; Deny(() => ModifyValidation.Parse(Wire.Element(input))); relation["second_point"] = "end";
            var reversed = relation.DeepClone().AsObject(); reversed["first_point"] = "end"; reversed["second_point"] = "start"; op["constraints"]!.AsArray().Add(reversed); Deny(() => ModifyValidation.Parse(Wire.Element(input)));
            var schema = ToolCatalog.Tools.Single(t => t.Name == "nanocad.modify_entities").InputSchema.GetRawText(); if (!schema.Contains("distance_x") || !schema.Contains("distance_y")) throw new Exception("Axis dimension schema absent"); return Task.CompletedTask;
        });
    }
}
