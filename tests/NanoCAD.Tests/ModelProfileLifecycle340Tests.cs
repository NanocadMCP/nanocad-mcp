using System.Text.Json;
using NanoCAD.Contracts;
using NanoCAD.McpServer;

internal static class ModelProfileLifecycle340Tests
{
    public static async Task Run(Func<string, Func<Task>, Task> test)
    {
        static void Need(bool ok) { if (!ok) throw new Exception("Native profile lifecycle340 contract assertion failed."); }
        static void Reject(Action action) { try { action(); } catch (DomainException) { return; } throw new Exception("An invalid lifecycle request was accepted."); }
        const string last = "{00000000-0000-0000-0000-000000000001}", profile = "{00000000-0000-0000-0000-000000000002}", sketch = "{00000000-0000-0000-0000-000000000003}";
        await test("block340 actual contour equality admits split reverse order and detects another arc", () =>
        {
            SketchConstraintCurve Line(double a, double b) => new("line", "line", new(a, 0), new(b, 0));
            Need(ModelProfileBoundary340.Same([Line(0, 4)], [Line(4, 2), Line(2, 0)]));
            Need(!ModelProfileBoundary340.Same([Line(0, 4)], [Line(0, 3)]));
            var circle = new SketchConstraintCurve("circle", "circle", Center: new(0, 0), Radius: 2);
            var upper = new SketchConstraintCurve("upper", "arc", new(2, 0), new(-2, 0), new(0, 0), 2, Math.PI);
            var lower = new SketchConstraintCurve("lower", "arc", new(-2, 0), new(2, 0), new(0, 0), 2, Math.PI);
            Need(ModelProfileBoundary340.Same([circle], [lower, upper]));
            Need(!ModelProfileBoundary340.Same([upper], [lower]));
            bool cancelled = false; try { ModelProfileBoundary340.Same([circle], [upper, lower], () => throw new OperationCanceledException()); }
            catch (OperationCanceledException) { cancelled = true; }
            Need(cancelled);
            return Task.CompletedTask;
        });
        await test("block340 retained profile source identity and explicit plane", () =>
        {
            var input = Wire.Element(new
            {
                operation = "create_model_profile",
                handles = new[] { "A1" },
                expected_last_item_id = last,
                source_handles = new[] { "b1", "b2", "b3", "b4" },
                plane = new { origin = new[] { 10.0, 20.0, 7.0 }, x_axis = new[] { 1.0, 0.0, 0.0 }, y_axis = new[] { 0.0, 1.0, 0.0 } }
            });
            var parsed = ModelProfileLifecycleValidation340.Parse(input, "$.operations[0]", ["A1"]);
            Need(parsed is ModelProfileCreate340 { SourceHandles: ["B1", "B2", "B3", "B4"], Plane.Origin.Z: 7 });
            // Model size is byte bounded; a longer source list is not itself
            // an invalid CAD request. Native contour validation follows later.
            var many = Wire.Element(new
            {
                operation = "create_model_profile",
                handles = new[] { "A1" },
                expected_last_item_id = last,
                source_handles = Enumerable.Range(1, 65).Select(i => (i + 4096).ToString("X")).ToArray(),
                plane = new { origin = new[] { 0.0, 0.0, 0.0 }, x_axis = new[] { 1.0, 0.0, 0.0 }, y_axis = new[] { 0.0, 1.0, 0.0 } }
            });
            Need(((ModelProfileCreate340)ModelProfileLifecycleValidation340.Parse(many, "$", ["A1"])).SourceHandles.Length == 65);
            var omitted = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(input.GetRawText())!; omitted.Remove("plane");
            Reject(() => ModelProfileLifecycleValidation340.Parse(Wire.Element(omitted), "$", ["A1"]));
            omitted["plane"] = input.GetProperty("plane"); omitted["source_handles"] = Wire.Element(new[] { "b1", "B1" });
            Reject(() => ModelProfileLifecycleValidation340.Parse(Wire.Element(omitted), "$", ["A1"]));
            return Task.CompletedTask;
        });
        await test("block340 exact profile freshness and semantic feature contract", () =>
        {
            object Input(double distance, string mode = "join", string token = "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA") => new
            {
                operation = "add_model_extrusion_from_profile",
                handles = new[] { "A1" },
                expected_last_item_id = last,
                profile_id = profile,
                expected_sketch_id = sketch,
                expected_profile_token = token,
                part_operation = mode,
                distance,
                direction = "negative"
            };
            var parsed = ModelProfileLifecycleValidation340.Parse(Wire.Element(Input(.0001)), "$", ["A1"]);
            Need(parsed is ModelProfileExtrude340 { Distance: .0001, PartOperation: "join", Direction: "negative", ProfileId: profile, ExpectedSketchId: sketch });
            Need(ModelProfileLifecycleValidation340.Parse(Wire.Element(Input(3, "cut")), "$", ["A1"]) is ModelProfileExtrude340);
            Reject(() => ModelProfileLifecycleValidation340.Parse(Wire.Element(Input(0)), "$", ["A1"]));
            Reject(() => ModelProfileLifecycleValidation340.Parse(Wire.Element(Input(3, "new_body")), "$", ["A1"]));
            Reject(() => ModelProfileLifecycleValidation340.Parse(Wire.Element(Input(3, token: "stale")), "$", ["A1"]));
            Reject(() => ModelProfileLifecycleValidation340.Batch([parsed, new EraseSpec(["B1"])]));
            var schema = ModelProfileLifecycleSchema340.Operations(); Need(schema.Count == 2 && schema.ToJsonString().Contains("expected_profile_token") && schema.ToJsonString().Contains("expected_sketch_id"));
            Need(ModelProfileLifecycleSchema340.ReadProperties().ToJsonString().Contains("source_object_ids"));
            var refSchema = new System.Text.Json.Nodes.JsonObject { ["type"] = "object" };
            Need(ModelProfileLifecycleSchema340.ChangeOutput(refSchema).ToJsonString().Contains("body_ref"));
            return Task.CompletedTask;
        });
        await test("block348 document profile read is optional and publicly discoverable", () =>
        {
            var envelope = new Dictionary<string, object>
            {
                ["bridge_instance_id"] = Guid.NewGuid().ToString("D"),
                ["document_id"] = Guid.NewGuid().ToString("D"),
                ["expected_revision"] = "7",
                ["handles"] = new[] { "A1" }
            };
            var ordinary = ModelHistoryValidation.Parse(Wire.Element(envelope));
            Need(!ordinary.IncludeDocumentProfiles && ordinary.DocumentProfileCursor == null && ordinary.ModelProfileIds == null);
            envelope["include_document_profiles"] = true;
            envelope["document_profile_cursor"] = profile;
            envelope["profile_ids"] = new[] { sketch };
            var selected = ModelHistoryValidation.Parse(Wire.Element(envelope));
            Need(selected.IncludeDocumentProfiles && selected.DocumentProfileCursor == profile && selected.ModelProfileIds is [sketch]);
            var tool = ToolCatalog.Tools.Single(t => t.Name == "nanocad.inspect_model_history");
            var input = tool.InputSchema.GetProperty("properties");
            Need(input.GetProperty("include_document_profiles").GetProperty("type").GetString() == "boolean" &&
                input.GetProperty("document_profile_cursor").GetProperty("type").GetString() == "string" &&
                input.GetProperty("profile_ids").GetProperty("items").GetProperty("type").GetString() == "string");
            var publishedOutput = tool.OutputSchema!.Value.GetRawText();
            Need(publishedOutput.Contains("\"document_profile_inventory\"", StringComparison.Ordinal) &&
                publishedOutput.Contains("\"document_profiles\"", StringComparison.Ordinal));
            envelope["include_document_profiles"] = false;
            Reject(() => ModelHistoryValidation.Parse(Wire.Element(envelope)));
            envelope.Remove("document_profile_cursor");
            envelope["profile_ids"] = new[] { sketch, sketch.ToLowerInvariant() };
            Reject(() => ModelHistoryValidation.Parse(Wire.Element(envelope)));
            return Task.CompletedTask;
        });
    }
}
