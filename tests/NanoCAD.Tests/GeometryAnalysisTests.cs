using System.Text.Json;
using NanoCAD.Contracts;
using NanoCAD.McpServer;

internal static class GeometryAnalysisTests
{
    public static async Task Run(Func<string, Func<Task>, Task> test)
    {
        Dictionary<string, object> Input(string mode = "metrics", params string[] handles) => new()
        {
            ["bridge_instance_id"] = Guid.NewGuid().ToString("D"),
            ["document_id"] = Guid.NewGuid().ToString("D"),
            ["expected_revision"] = "00017",
            ["mode"] = mode,
            ["handles"] = handles.Length == 0 ? new[] { "0a" } : handles
        };
        void Reject(object input)
        {
            try { GeometryAnalysisValidation.Parse(input is JsonElement json ? json : Wire.Element(input)); }
            catch (DomainException) { return; }
            throw new Exception("Unsafe analysis arguments accepted.");
        }
        await test("selected contours retain reversed source handles, nearest gaps, junctions and ambiguous endpoint clusters", () =>
        {
            var input = Input(SelectedContours.Mode, "DA", "FA");
            if (GeometryAnalysisValidation.Parse(Wire.Element(input)).Workplane != DrawingWorkplane.XY) throw new Exception("Default contour plane missing.");
            input["point"] = new[] { 0, 0, 0 }; Reject(input);
            SelectedContours.Source Edge(string h, double x, double y, double xx, double yy) => new(h, new(x, y), new(xx, yy), false);
            var square = new[] { Edge("A", 0, 0, 1, 0), Edge("B", 1, 1, 1, 0), Edge("C", 1, 1, 0, 1), Edge("D", 0, 0, 0, 1) };
            var result = SelectedContours.Build(square.Reverse(), 1e-7, () => { });
            var order = result.Components.Single().order!;
            if (!order.Select(s => s.handle).SequenceEqual(new[] { "A", "B", "C", "D" }) || !order.Select(s => s.reverse).SequenceEqual(new[] { false, true, false, true }))
                throw new Exception("Unordered sources lost their actual identities or traversal direction.");
            result = SelectedContours.Build(square.Take(3), 1e-7, () => { });
            if (result.Components.Single().status != "open" || result.Gaps.Length != 2 || result.Gaps.Any(g => g.distance != 1)) throw new Exception("Missing nearest gap.");
            result = SelectedContours.Build([.. square.Take(3), Edge("D", 0, .2, 0, 1)], 1e-7, () => { });
            if (result.Components.Single().status != "open" || result.Gaps.Any(g => Math.Abs(g.distance - .2) > 1e-12) ||
                result.Gaps.Length != 2) throw new Exception("A 0.2 drawing-unit gap was healed or measured between the wrong endpoints.");
            result = SelectedContours.Build(square.Append(Edge("E", 1, 0, 2, 0)), 1e-7, () => { });
            if (result.Components.Single().status != "branched" || result.Components.Single().order != null) throw new Exception("A branch acquired a guessed loop.");
            result = SelectedContours.Build([Edge("A", 0, 0, 0, 2), Edge("B", .8, 0, 3, 3), Edge("C", 1.6, 0, 4, 4)], 1, () => { });
            if (result.Components.Single().status != "ambiguous_coincidence") throw new Exception("A transitive coincidence chain was treated as one junction.");
            return Task.CompletedTask;
        });
        await test("smooth junction measurement distinguishes path continuation, a right angle and a cusp", () =>
        {
            var edge = new SketchConstraintCurve("first", "line", new(0, 0), new(2, 0));
            var next = new SketchConstraintCurve("second", "line", new(2, 0), new(5, 0));
            double Angle(SketchConstraintCurve b, string role) => SplineConstraintGeometry445.ContinuationAngle(edge, "end", b, role);
            if (Angle(next, "start") != 0 || Math.Abs(Angle(next, "end") - Math.PI) > 1e-12 ||
                Math.Abs(Angle(next with { End = new(2, 4) }, "start") - Math.PI / 2) > 1e-12) throw new Exception("Continuation angle differs from the oriented endpoint tangents.");
            var input = Input(); input.Remove("handles"); input.Remove("mode"); input["handle"] = "AB"; input["sketch_id"] = Guid.NewGuid().ToString("B");
            input["measure_smooth"] = true; input["sketch_scope"] = "document"; var args = SketchConstraintsValidation.Parse(Wire.Element(input));
            if (!args.MeasureSmooth || !args.DocumentSketchScope) throw new Exception("Selected relation measurements lost their scope.");
            input["measure_smooth"] = "yes";
            try { SketchConstraintsValidation.Parse(Wire.Element(input)); } catch (DomainException) { return Task.CompletedTask; }
            throw new Exception("A non-boolean measurement selector was accepted.");
        });
        await test("planar section modes preserve the selected frame, local window and segment direction", () =>
        {
            var input = Input(SolidSectionAnalysis.Section);
            input["workplane"] = new { origin = new[] { 5, 6, 7 }, x_axis = new[] { 0, 1, 0 }, y_axis = new[] { 0, 0, 1 } };
            var pair = new[] { new[] { -3, 0 }, new[] { 4, 2 } };
            input["window"] = pair;
            var parsed = GeometryAnalysisValidation.Parse(Wire.Element(input)).Section!;
            if (parsed.Window![0] != new Point2(-3, 0) || parsed.Plane.Origin != new Point3(5, 6, 7)) throw new Exception("Wrong local frame.");
            input["mode"] = SolidSectionAnalysis.Intervals;
            Reject(input);
            input.Remove("window");
            input["segment"] = pair.Reverse().ToArray();
            if (GeometryAnalysisValidation.Parse(Wire.Element(input)).Section!.Segment![0] != new Point2(4, 2)) throw new Exception("Segment order was lost.");
            input["segment"] = new[] { pair[0], pair[0] };
            Reject(input);
            input["mode"] = SolidSectionAnalysis.Section;
            Reject(input);
            return Task.CompletedTask;
        });
        await test("geometry analysis validates native read modes identity and bounded input", () =>
        {
            var input = Input(); input["point"] = new[] { 1d, 2d, 3d };
            var parsed = GeometryAnalysisValidation.Parse(Wire.Element(input));
            if (parsed.Read.ExpectedRevision != "17" || parsed.Read.Handles![0] != "A" || parsed.Point![2] != 3 || Wire.ValidateBridge("analyze_geometry", Wire.Element(input)) != parsed.Read.BridgeId)
                throw new Exception("Analysis context lost.");
            foreach (var missing in new[] { "bridge_instance_id", "document_id", "expected_revision", "handles", "mode" })
            { var bad = Input(); bad.Remove(missing); Reject(bad); }
            Reject(Input("intersections", "1")); Reject(Input("intersections", "1", "2", "3"));
            Reject(Input("metrics", "a", "0A")); Reject(Input("metrics", "0")); Reject(Input("union", "1"));
            GeometryAnalysisValidation.Parse(Wire.Element(Input("metrics", Enumerable.Range(1, 65).Select(i => i.ToString("X")).ToArray())));
            var extended = Input(); extended["extend"] = true; Reject(extended);
            var wrongMode = Input("intersections", "1", "2"); wrongMode["point"] = new[] { 0, 0, 0 }; Reject(wrongMode);
            foreach (var point in new object[] { new[] { 1d, 2d }, new[] { 1d, 2d, 1e13 }, new object[] { 1, 2, "3" } })
            { var bad = Input(); bad["point"] = point; Reject(bad); }
            using var duplicate = JsonDocument.Parse(Wire.Element(Input()).GetRawText().Replace("\"mode\":\"metrics\"", "\"mode\":\"metrics\",\"mode\":\"metrics\""));
            Reject(duplicate.RootElement);
            GeometryAnalysisValidation.Parse(Wire.Element(Input("intersections", "1", "2")));
            return Task.CompletedTask;
        });
        await test("geometry analysis publishes read-only contract without topology claims", () =>
        {
            var tool = ToolCatalog.Tools.Single(t => t.Name == "nanocad.analyze_geometry");
            if (tool.Annotations?.ReadOnlyHint != true || tool.Annotations.DestructiveHint != false || Wire.WriteOperations.Contains("analyze_geometry"))
                throw new Exception("Analysis became a mutation.");
            var alternatives = AuditServerContractSupport.Output("analyze_geometry").GetProperty("properties").GetProperty("data").GetProperty("anyOf")[0].GetProperty("oneOf");
            var modes = alternatives.EnumerateArray().Where(branch => branch.TryGetProperty("properties", out var properties) && properties.TryGetProperty("mode", out _))
                .Select(branch => branch.GetProperty("properties").GetProperty("mode").GetProperty("const").GetString()).ToHashSet();
            var intersections = alternatives.EnumerateArray().Single(branch => branch.TryGetProperty("properties", out var properties) && properties.TryGetProperty("mode", out var mode) && mode.GetProperty("const").GetString() == "intersections");
            if (!new[] { "metrics", "intersections", "distance", "relations", "containment" }.All(modes.Contains) || intersections.GetProperty("properties").GetProperty("overlap_evaluated").GetProperty("const").GetBoolean())
                throw new Exception("Analysis schema misrepresents topology support.");
            File.WriteAllText(Path.Combine(Environment.GetEnvironmentVariable("NANOCAD_MCP_HOME")!, "tools-list.json"), JsonSerializer.Serialize(ToolCatalog.Tools, Wire.Json));
            return Task.CompletedTask;
        });
        await test("geometry analysis workplane keeps WCS points and rejects off-plane or incompatible requests", () =>
        {
            var frame = new { origin = new[] { 20d, 30, 40 }, x_axis = new[] { .6, .8, 0 }, y_axis = new[] { -.48, .36, .8 } };
            var relation = Input("relations", "A", "B"); relation["workplane"] = frame;
            var parsed = GeometryAnalysisValidation.Parse(Wire.Element(relation));
            if (parsed.Workplane == null || parsed.Workplane.Origin.Z != 40 || parsed.Point != null) throw new Exception("Analysis frame lost.");
            var containment = Input("containment", "A"); containment["workplane"] = frame; containment["point"] = new[] { 18.8, 38.4, 48d };
            parsed = GeometryAnalysisValidation.Parse(Wire.Element(containment));
            if (parsed.Point![2] != 48) throw new Exception("WCS query was silently converted.");
            containment["point"] = new[] { 18.8, 38.4, 49d }; Reject(containment);
            foreach (var mode in new[] { "metrics", "distance", "intersections" }) { var bad = Input(mode, "A", "B"); bad["workplane"] = frame; Reject(bad); }
            relation["workplane"] = new { origin = new[] { 0, 0, 0 }, x_axis = new[] { 1, 0, 0 }, y_axis = new[] { 1, 0, 0 } }; Reject(relation);
            var schema = AuditServerContractSupport.Input("analyze_geometry").GetProperty("oneOf").EnumerateArray()
                .Single(branch => branch.TryGetProperty("properties", out var properties) && properties.TryGetProperty("mode", out var mode) && mode.TryGetProperty("enum", out _));
            if (!schema.GetProperty("properties").TryGetProperty("workplane", out _)) throw new Exception("Analysis frame missing from MCP schema.");
            return Task.CompletedTask;
        });
        await test("cylinder analysis admits only the current selected face operands", () =>
        {
            var first = new { handle = "0a", native_id = "7" };
            var second = new { handle = "B", native_id = "7" };
            var diameter = Input("cylinder_diameter", "A");
            diameter["faces"] = new[] { first };
            var parsed = GeometryAnalysisValidation.Parse(Wire.Element(diameter));
            if (parsed.SolidMeasurement?.Faces is not [{ Handle: "A", NativeId: "7" }])
                throw new Exception("Cylinder face selection lost the native identity.");
            diameter["faces"] = new[] { first, second }; Reject(diameter);
            diameter["faces"] = new[] { first }; diameter["point"] = new[] { 0, 0, 0 }; Reject(diameter);

            foreach (var mode in new[] { "axis_axis_distance", "axis_plane_distance" })
            {
                var pair = Input(mode, "B", "A");
                pair["faces"] = new[] { first, second };
                parsed = GeometryAnalysisValidation.Parse(Wire.Element(pair));
                if (parsed.SolidMeasurement?.Faces is not [{ Handle: "A", NativeId: "7" }, { Handle: "B", NativeId: "7" }])
                    throw new Exception("Axis analysis changed the ordered source faces.");
                pair["handles"] = new[] { "A" }; Reject(pair);
                pair["faces"] = new[] { first, first }; Reject(pair);
                pair["faces"] = new[] { first, new { handle = "0A", native_id = "11" } };
                GeometryAnalysisValidation.Parse(Wire.Element(pair));
            }

            var modes = AuditServerContractSupport.Input("analyze_geometry").GetProperty("oneOf")[2].GetProperty("oneOf")
                .EnumerateArray().ToDictionary(branch => branch.GetProperty("properties").GetProperty("mode").GetProperty("const").GetString()!);
            if (modes["cylinder_diameter"].GetProperty("properties").GetProperty("faces").GetProperty("maxItems").GetInt32() != 1 ||
                modes["axis_plane_distance"].GetProperty("properties").GetProperty("faces").GetProperty("minItems").GetInt32() != 2)
                throw new Exception("Published axis analysis schema lost the mode-specific face counts.");
            return Task.CompletedTask;
        });
    }
}
