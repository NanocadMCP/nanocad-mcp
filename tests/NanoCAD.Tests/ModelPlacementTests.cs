using NanoCAD.Contracts;
using NanoCAD.McpServer;

internal static class ModelPlacementTests
{
    internal static async Task Run(Func<string, Func<Task>, Task> test)
    {
        string id = Guid.NewGuid().ToString(), last = Guid.NewGuid().ToString("B");
        ModifyArguments Parse(params object[] ops) => ModifyValidation.Parse(Wire.Element(new { bridge_instance_id = id, document_id = id, operation_id = id, expected_revision = "1", units = "Millimeters", operations = ops }));
        object Move(double x) => new { kind = "move", displacement = new[] { x, 2d, 3d } };
        object Rotate(double x = 30, double center = 5) => new { kind = "rotate", center = new[] { center, 6d, 7d }, angles_degrees = new[] { x, 20d, 90d } };
        object Add(object p) => new { operation = "add_model_placement", handles = new[] { "A" }, expected_last_item_id = last, placement = p };
        object Set(object before, object after) => new { operation = "set_model_placement", handles = new[] { "A" }, expected_last_item_id = last, expected_placement = before, placement = after };
        void Deny(Action action) { try { action(); } catch (DomainException) { return; } throw new Exception("Unsupported placement accepted"); }
        await test("model placement definitions preserve vector pivot units and expected values", () =>
        {
            var a = Parse(Add(Rotate())); if (a.Operations[0] is not ModelPlacementEdit { Placement: { Kind: "rotate", Vector: { X: 30, Y: 20, Z: 90 }, Center: { X: 5, Y: 6, Z: 7 } } }) throw new Exception("Transform definition lost");
            if (a.Fingerprint() == Parse(Add(Rotate(31))).Fingerprint() || a.Fingerprint() == Parse(Add(Rotate(center: 6))).Fingerprint()) throw new Exception("Pivot/angle missing in identity");
            if (Parse(Set(Move(1), Move(2))).Fingerprint() == Parse(Set(Move(0), Move(2))).Fingerprint()) throw new Exception("Expected offset missing from identity");
            Deny(() => Parse(Set(Move(1), Rotate()))); Deny(() => Parse(Set(Rotate(), Rotate(center: 6)))); Deny(() => Parse(Add(Rotate(361)))); Deny(() => Parse(Add(Move(1e6 + 1))));
            Deny(() => Parse(Add(new { kind = "move", displacement = new[] { 1d, 2d, 3d }, creates_copy = true }))); return Task.CompletedTask;
        });
        await test("model placement is exclusive and reports non-guaranteed recovery", () =>
        {
            var a = Parse(Add(Move(10))); if (!WriteRecovery.NotGuaranteed(a)) throw new Exception("Missing recovery warning");
            Deny(() => Parse(Add(Move(1)), Add(Rotate()))); Deny(() => Parse(new { operation = "add_model_placement", handles = new[] { "A", "B" }, expected_last_item_id = last, placement = Move(1) }));
            Deny(() => Parse(new { operation = "add_model_placement", handles = new[] { "A" }, expected_last_item_id = Guid.Empty.ToString("B"), placement = Move(1) })); return Task.CompletedTask;
        });
        await test("model placement history follows mixed operations with interposed non-feature nodes", () =>
        {
            var features = Enumerable.Range(0, 16).Select(i => "f" + i).ToArray();
            var links = features.SelectMany((f, i) => i == 0 ? new[] { new ModelSequenceLink(f, null) } : new[] { new ModelSequenceLink(f, "s" + i), new ModelSequenceLink("s" + i, features[i - 1]) }).Reverse().ToArray();
            if (!ModelFeatureSequence.FromHistory(links, features.Reverse().ToArray(), features[0], features[^1]).SequenceEqual(features)) throw new Exception("Feature order guessed from enumeration");
            Deny(() => ModelFeatureSequence.FromHistory(links, features, features[0], features[^2]));
            var cycle = links.Select(n => n.Id == "s3" ? n with { Previous = "f4" } : n).ToArray(); Deny(() => ModelFeatureSequence.FromHistory(cycle, features, features[0], features[^1])); return Task.CompletedTask;
        });
        await test("model placement normalization retains feature order through a fixed anchor", () =>
        {
            var features = new[] { "extrude", "move", "rotate" };
            ModelSequenceLink[] before = [new("extrude", null), new("move", "extrude"), new("anchor", null), new("rotate", "move")];
            ModelSequenceLink[] after = [new("extrude", null), new("move", "extrude"), new("anchor", "move"), new("rotate", "anchor"), new("move2", "rotate")];
            var prior = ModelFeatureSequence.FromHistory(before, features, "extrude", "rotate"); var next = ModelFeatureSequence.FromHistory(after, [.. features, "move2"], "extrude", "move2");
            if (!prior.Append("move2").SequenceEqual(next)) throw new Exception("Anchor normalization changed semantic order");
            var invalid = after.Select(n => n.Id == "anchor" ? n with { Previous = "extrude" } : n).ToArray(); Deny(() => ModelFeatureSequence.FromHistory(invalid, [.. features, "move2"], "extrude", "move2")); return Task.CompletedTask;
        });
        await test("model placement schema includes explicit pivot and final-feature expectations", () =>
        {
            var output = AuditServerContractSupport.Output("modify_entities").GetProperty("properties").GetProperty("data").GetProperty("anyOf")[0].GetProperty("anyOf")[0].GetProperty("properties").GetProperty("model_placement_changes").GetProperty("items");
            if (!output.GetProperty("properties").GetProperty("anchor_ref").TryGetProperty("anyOf", out _)) throw new Exception("Move without anchor must conform to output schema");
            foreach (var name in new[] { "add_model_placement", "set_model_placement" })
            {
                var o = AuditServerContractSupport.Branch("modify_entities", name);
                if (o.GetProperty("properties").TryGetProperty("expected_placement", out _) != (name == "set_model_placement")) throw new Exception("Schema expectation differs");
                var description = o.GetProperty("description").GetString()!; if (!description.Contains("DEGREES") || !description.Contains("ROLLBACK_NOT_GUARANTEED") || !description.Contains("WorkPoint")) throw new Exception("Placement semantics absent");
            }
            return Task.CompletedTask;
        });
    }
}
