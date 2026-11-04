using NanoCAD.Contracts;
using NanoCAD.McpServer;

internal static class SketchConstraintTests
{
    public static async Task Run(Func<string, Func<Task>, Task> test)
    {
        void Need(bool valid) { if (!valid) throw new Exception("Sketch constraint assertion failed."); }
        void Reject(Action action) { try { action(); } catch (DomainException) { return; } throw new Exception("Invalid constraint relation accepted."); }
        var geometry = new[]{new SketchConstraintCurve("a","line",new(0,0),new(30,0)),new SketchConstraintCurve("b","line",new(30,0),new(30,20)),
            new SketchConstraintCurve("c","line",new(30,20),new(0,20)),new SketchConstraintCurve("d","circle",Center:new(10,10),Radius:3),
            new SketchConstraintCurve("e","circle",Center:new(10,10),Radius:6)};
        SketchConstraintSpec Relation(string kind, string first, string? second = null, string? p = null, string? q = null, double? value = null) => new(kind, first, second, p, q, value);
        void Check(params SketchConstraintSpec[] constraints) => SketchConstraintGeometry.Validate(constraints, geometry);
        await test("sketch constraints preserve rectangle relations and radial dimensions", () =>
        {
            Check(Relation("horizontal", "a"), Relation("vertical", "b"), Relation("parallel", "a", "c"), Relation("perpendicular", "a", "b"),
                Relation("equal", "a", "c"), Relation("coincident", "a", "b", "end", "start"), Relation("concentric", "d", "e"),
                Relation("radius", "d", value: 3), Relation("diameter", "e", value: 12), Relation("length", "b", value: 20));
            return Task.CompletedTask;
        });
        await test("sketch constraints reject reshaping foreign references and mismatched points", () =>
        {
            foreach (var invalid in new[]{Relation("horizontal","b"),Relation("vertical","a"),Relation("parallel","a","b"),
                Relation("equal","a","b"),Relation("equal","d","e"),Relation("length","a",value:29),Relation("radius","d",value:4),
                Relation("radius","a",value:3),Relation("coincident","a","b","start","end"),Relation("coincident","a","d","center","center"),
                Relation("horizontal","missing"),Relation("concentric","a","b")}) Reject(() => Check(invalid));
            Reject(() => SketchConstraintGeometry.Validate(new[] { Relation("horizontal", "zero") }, new[] { new SketchConstraintCurve("zero", "line", new(0, 0), new(0, 0)) }));
            return Task.CompletedTask;
        });
        await test("sketch constraints contract retains opaque identities sole operation and recovery", () =>
        {
            string id = Guid.NewGuid().ToString("D"), sketch = Guid.NewGuid().ToString("B"), a = Guid.NewGuid().ToString("B"), b = Guid.NewGuid().ToString("B");
            object Edit(params object[] constraints) => new { operation = "add_sketch_constraints", handles = new[] { "AB" }, sketch_id = sketch, expected_binding_token = new string('A', 64), constraints };
            ModifyArguments Parse(params object[] operations) => ModifyValidation.Parse(Wire.Element(new { bridge_instance_id = id, document_id = id, expected_revision = "1", operation_id = id, units = "Millimeters", operations }));
            var parsed = Parse(Edit(new { kind = "coincident", first = a, second = b, first_point = "end", second_point = "start" }, new { kind = "radius", first = b, value = 3 }));
            var edit = (SketchConstraintsEdit)parsed.Operations.Single();
            Need(edit.SketchId == sketch.ToUpperInvariant() && edit.Constraints[0].First == a.ToUpperInvariant() && edit.ExpectedBindingToken == new string('A', 64) && WriteRecovery.NotGuaranteed(parsed));
            Reject(() => Parse(new { operation = "add_sketch_constraints", handles = new[] { "AB" }, sketch_id = sketch, constraints = new[] { new { kind = "radius", first = b, value = 3 } } }));
            Reject(() => Parse(Edit(new { kind = "parallel", first = a, second = b }, new { kind = "parallel", first = b, second = a })));
            Reject(() => Parse(Edit(new { kind = "parallel", first = a, second = a }))); Reject(() => Parse(Edit(new { kind = "radius", first = a, value = 0 })));
            Reject(() => Parse(Edit(new { kind = "horizontal", first = a, value = 2 }))); Reject(() => Parse(Edit(new { kind = "coincident", first = a, second = b })));
            Reject(() => Parse(Edit(new { kind = "length", first = "A3", value = 20 })));
            Reject(() => Parse(Edit(new { kind = "radius", first = b, value = 3 }), new { operation = "erase", handles = new[] { "AC" } }));
            var tool = ToolCatalog.Tools.Single(t => t.Name == "nanocad.modify_entities");
            var focused = AuditServerContractSupport.Branch("modify_entities", "add_sketch_constraints");
            Need(tool.InputSchema.GetRawText().Contains("add_sketch_constraints") && focused.GetProperty("required").EnumerateArray().Any(v => v.GetString() == "expected_binding_token") && focused.GetProperty("properties").GetProperty("handles").GetProperty("maxItems").GetInt32() == 1 && AuditServerContractSupport.Output("modify_entities").GetRawText().Contains("sketch_constraint_changes"));
            return Task.CompletedTask;
        });
    }
}
