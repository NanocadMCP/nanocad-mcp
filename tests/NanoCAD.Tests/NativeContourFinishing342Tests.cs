using System.Text.Json;
using NanoCAD.Contracts;
using NanoCAD.McpServer;

internal static class NativeContourFinishing342Tests
{
    public static async Task Run(Func<string, Func<Task>, Task> test)
    {
        void Need(bool valid, string message) { if (!valid) throw new Exception(message); }
        void Reject(Action action, string code = "INVALID_ARGUMENT")
        { try { action(); } catch (DomainException e) when (e.Error.code == code) { return; } throw new Exception("Invalid native finishing accepted."); }
        string token = new('A', 64);
        NativeContourFinishingSpec342 Parse(object value, string kind) => NativeContourFinishingValidation342.Parse(Wire.Element(value), "$.operations[0]", ["A1"], kind);
        await test("block342 native finishing parameters, WCS pick and exact source token remain explicit", () =>
        {
            var cuts = Enumerable.Range(1, 80).Select(i => i / 100d).ToArray();
            var split = Parse(new { operation = "split_curve", mode = "native", handles = new[] { "A1" }, expected_curve_token = token, cut_parameters = cuts, erase_sources = false }, "split_curve");
            Need(split.CutParameters.SequenceEqual(cuts) && !split.EraseSource, "No whole-curve cut ceiling or distance reinterpretation.");
            var trim = Parse(new { operation = "trim_curve", mode = "native", handles = new[] { "A1" }, expected_curve_token = token, boundary_handles = new[] { "A2", "A3" }, pick_point_wcs = new[] { 2d, 3d, 4d } }, "trim_curve");
            Need(trim.EraseSource && trim.PickPointWcs == new Point3(2, 3, 4) && trim.BoundaryHandles.SequenceEqual(new[] { "A2", "A3" }), "Native trim source replacement/3D point/boundary refs lost.");
            var extend = Parse(new { operation = "extend_curve", mode = "native", handles = new[] { "A1" }, expected_curve_token = token, end = "start", target_point_wcs = new[] { 5d, 6d, 7d } }, "extend_curve");
            Need(extend.End == "start" && !extend.EraseSource && extend.TargetPointWcs == new Point3(5, 6, 7), "Native stored endpoint meaning lost.");
            Reject(() => Parse(new { operation = "split_curve", mode = "native", handles = new[] { "A1" }, expected_curve_token = token, cut_parameters = new[] { .5, .5 }, erase_sources = true }, "split_curve"));
            Reject(() => Parse(new { operation = "trim_curve", mode = "native", handles = new[] { "A1" }, expected_curve_token = token, boundary_handles = new[] { "A1" }, pick_point_wcs = new[] { 0d, 0d, 0d } }, "trim_curve"));
            Reject(() => Parse(new { operation = "extend_curve", mode = "native", handles = new[] { "A1" }, expected_curve_token = "old", end = "end", target_point_wcs = new[] { 0d, 0d, 0d } }, "extend_curve"));
            Reject(() => Parse(new { operation = "extend_curve", mode = "native", handles = new[] { "A1" }, expected_curve_token = token, end = "end", target_point_wcs = new[] { 0d, 0d }, guess_parameter = .5 }, "extend_curve"));
            return Task.CompletedTask;
        });
        await test("block342 actual published finishing variants retain legacy and native sole request dispatch", () =>
        {
            var context = new { bridge_instance_id = Guid.NewGuid().ToString(), document_id = Guid.NewGuid().ToString(), expected_revision = "1", operation_id = Guid.NewGuid().ToString(), units = "Millimeters", target_space = "paper" };
            JsonElement Payload(params object[] operations) => Wire.Element(new { context.bridge_instance_id, context.document_id, context.expected_revision, context.operation_id, context.units, context.target_space, operations });
            var native = new { operation = "split_curve", mode = "native", handles = new[] { "A1" }, expected_curve_token = token, cut_parameters = new[] { .5 }, erase_sources = false };
            Need(ModifyValidation.Parse(Payload(native)).Operations.Single() is NativeContourFinishingSpec342, "Actual parser native split/Paper hook missing.");
            Need(ModifyValidation.Parse(Payload(new { operation = "trim_curve", handles = new[] { "A1" }, boundary_handles = new[] { "A2" }, pick_point = new[] { 1, 2 } })).Operations.Single() is TrimCurveSpec, "Legacy trim rerouted.");
            Reject(() => ModifyValidation.Parse(Payload(native, new { operation = "erase", handles = new[] { "A2" } })), "INVALID_OPERATION_ORDER");
            var branches = ToolCatalog.Tools.Single(t => t.Name == "nanocad.modify_entities").InputSchema.GetProperty("properties").GetProperty("operations").GetProperty("items").GetProperty("oneOf").EnumerateArray().ToArray();
            foreach (string kind in new[] { "trim_curve", "split_curve", "extend_curve" })
            {
                var matching = branches.Where(b => b.GetProperty("properties").GetProperty("operation").GetProperty("const").GetString() == kind).ToArray();
                Need(matching.Length == 1 && matching[0].GetProperty("oneOf").GetArrayLength() == 2, "Ambiguous/missing complete legacy/native variant " + kind);
                var arm = matching[0].GetProperty("oneOf")[1]; Need(arm.GetProperty("properties").GetProperty("mode").GetProperty("const").GetString() == "native" && arm.GetProperty("required").EnumerateArray().Any(v => v.GetString() == "expected_curve_token"), "Native precondition absent.");
            }
            return Task.CompletedTask;
        });
        await test("native trim accepts one explicit curve snap and a kept-piece preview count", () =>
        {
            var pick = new { handle = "0a1", expected_curve_token = token.ToLowerInvariant(), snap = "nearest", reference_point_wcs = new[] { 4d, 5d, 6d }, tolerance = .1 };
            var trim = new { operation = "trim_curve", handles = new[] { "A1" }, boundary_handles = new[] { "A2" }, expected_curve_token = token, pick_snap = pick, expected_kept_pieces = 2 };
            var parsed = Parse(trim, "trim_curve");
            Need(parsed.PickSnap is { Handle: "A1", Snap: "nearest", Tolerance: .1 } snap && snap.ExpectedCurveToken == token &&
                snap.ReferencePointWcs == new Point3(4, 5, 6) && parsed.PickPointWcs == null && parsed.ExpectedKeptPieces == 2, "Resolved input meaning or preview guard lost.");
            Reject(() => Parse(new { trim.operation, trim.handles, trim.boundary_handles, trim.expected_curve_token, trim.pick_snap, pick_point_wcs = new[] { 4d, 5d, 6d } }, "trim_curve"));
            Reject(() => Parse(new { trim.operation, trim.handles, trim.boundary_handles, trim.expected_curve_token, trim.pick_snap, expected_kept_pieces = 0 }, "trim_curve"));
            Reject(() => Parse(new { trim.operation, trim.handles, trim.boundary_handles, trim.expected_curve_token, trim.pick_snap, mode = "legacy" }, "trim_curve"));
            var extend = Parse(new { operation = "extend_curve", handles = new[] { "A1" }, expected_curve_token = token, end = "end", target_point_wcs = new[] { 5d, 6d, 7d } }, "extend_curve");
            Need(extend.TargetPointWcs == new Point3(5, 6, 7), "Unambiguous native extension inputs did not select native dispatch.");
            return Task.CompletedTask;
        });
        await test("Point placement accepts a finite existing-curve intersection snap as its WCS coordinate", () =>
        {
            var pick = new
            {
                handle = "A1",
                expected_curve_token = token,
                snap = "intersection",
                other_handle = "0a2",
                other_expected_curve_token = token,
                reference_point_wcs = new[] { 4d, 5d, 6d },
                tolerance = .5
            };
            EntitySpec ParsePoint(object value) => CreateValidation.ParseEntities(Wire.Element(new[] { value }), allowDimensions: true).Single();
            var value = ParsePoint(new { type = "Point", position_snap = pick });
            Need(value is PointFromNativeSnapSpec { PositionSnap: { Snap: "intersection", OtherHandle: "A2", Tolerance: .5 } }, "Intersection snap did not reach the normal Point route.");
            Need(ParsePoint(new { type = "Point", position = new[] { 4d, 5d, 6d } }) is PointSpec408, "Literal Point behavior changed.");
            Reject(() => ParsePoint(new { type = "Point", position_snap = pick, position = new[] { 4d, 5d, 6d } }));
            Reject(() => ParsePoint(new { type = "Point", position_snap = pick, workplane = new { origin = new[] { 0, 0, 0 } } }));
            Reject(() => ParsePoint(new { type = "Point", position_snap = new { pick.handle, pick.expected_curve_token, pick.snap, other_handle = "0a1", pick.other_expected_curve_token, pick.reference_point_wcs, pick.tolerance } }));
            Reject(() => ParsePoint(new { type = "Point", position_snap = new { pick.handle, pick.expected_curve_token, snap = "nearest", pick.reference_point_wcs, tolerance = 0d } }));
            return Task.CompletedTask;
        });
        await test("Line endpoints can independently use literal coordinates or native snaps", () =>
        {
            var start = new { handle = "A1", expected_curve_token = token, snap = "start" };
            var end = new { handle = "A2", expected_curve_token = token, snap = "mid" };
            EntitySpec ParseLine(object value) => CreateValidation.ParseEntities(Wire.Element(new[] { value }), allowDimensions: true).Single();
            Need(ParseLine(new { type = "Line", start_snap = start, end_snap = end }) is LineFromNativeSnapSpec
            { Start: null, End: null, StartSnap: { Snap: "start" }, EndSnap: { Snap: "mid" } }, "Snapped endpoints did not reach native Line construction.");
            Need(ParseLine(new { type = "Line", start = new[] { 1d, 2d }, end_snap = end }) is LineFromNativeSnapSpec
            { Start: { X: 1, Y: 2 }, StartSnap: null, EndSnap: { Snap: "mid" } }, "Mixed literal/snapped endpoints lost their independent meaning.");
            Need(ParseLine(new { type = "Line", start = new[] { 1d, 2d }, end = new[] { 3d, 4d } }) is LineSpec, "Literal Line behavior changed.");
            Reject(() => ParseLine(new { type = "Line", start = new[] { 1d, 2d }, start_snap = start, end_snap = end }));
            Reject(() => ParseLine(new { type = "Line", start_snap = start }));
            Reject(() => ParseLine(new { type = "Line", start_snap = start, end_snap = end, workplane = new { origin = new[] { 0, 0, 0 } } }));
            var entities = ToolCatalog.Tools.Single(t => t.Name == "nanocad.create_entities").InputSchema.GetProperty("properties").GetProperty("entities").GetProperty("items").GetProperty("oneOf");
            var line = entities.EnumerateArray().Single(branch => branch.GetProperty("properties").GetProperty("type").GetProperty("const").GetString() == "Line");
            Need(line.GetProperty("properties").GetProperty("start_snap").GetProperty("properties").TryGetProperty("expected_curve_token", out _) &&
                line.GetProperty("properties").TryGetProperty("end_snap", out _), "The focused Line schema hides snap inputs.");
            return Task.CompletedTask;
        });
    }
}
