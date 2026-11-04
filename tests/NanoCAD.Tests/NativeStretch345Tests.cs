using System.Text.Json;
using NanoCAD.Contracts;
using NanoCAD.McpServer;

internal static class NativeStretch345Tests
{
    public static async Task Run(Func<string, Func<Task>, Task> test)
    {
        void Need(bool valid, string message) { if (!valid) throw new Exception(message); }
        void Reject(Action action, string code = "INVALID_ARGUMENT")
        { try { action(); } catch (DomainException error) when (error.Error.code == code) { return; } throw new Exception("Invalid native stretch accepted."); }
        var shape = Wire.Element(new { runtime_type = "Teigha.DatabaseServices.Polyline", closed = false, geometry = new { vertices = new[] { new[] { 0d, 0d, 0d }, new[] { 20d, 0d, 0d } } } });
        var points = Enumerable.Range(0, 257).Select(i => new NativeStretchPoint345(i, new[] { (double)i, 0d, 0d })).ToArray();
        string token = NativeStretchRead345.Token(shape, points);
        NativeStretchEdit345 Parse(object value) => NativeStretchValidation345.Parse(Wire.Element(value), "$.operations[0]", ["A1"]);
        object Operation(int[] indices, double[]? displacement = null, string? expected = null) => new { operation = "stretch_curve", mode = "native", handles = new[] { "A1" }, expected_stretch_token = expected ?? token, indices, displacement_wcs = displacement ?? new[] { 0d, 5d, 0d } };
        await test("block345 native stretch binds complete indexed points and moves only actual selected indices", () =>
        {
            var edit = Parse(Operation([256, 0])); var expected = NativeStretchRead345.Expected(points, edit);
            Need(edit.Indices.SequenceEqual(new[] { 256, 0 }) && expected.Length == 257 && expected[0].point_wcs.SequenceEqual(new[] { 0d, 5d, 0d }) && expected[256].point_wcs.SequenceEqual(new[] { 256d, 5d, 0d }) && expected[128].point_wcs.SequenceEqual(points[128].point_wcs), "Actual indices/unselected points were reinterpreted or capped.");
            Need(NativeStretchRead345.Token(shape, expected) != token && NativeStretchRead345.Token(Wire.Element(new { changed_native_shape = true }), points) != token, "Token must bind both complete source shape and actual stretch inventory.");
            Reject(() => Parse(Operation([0, 0]))); Reject(() => Parse(Operation([-1]))); Reject(() => Parse(Operation([], new[] { 0d, 1d, 0d })));
            Reject(() => Parse(Operation([0], new[] { 0d, 1d, 1d }))); Reject(() => Parse(Operation([0], new[] { 0d, 1d }))); Reject(() => Parse(Operation([0], expected: "stale")));
            var maximum = Parse(Operation([int.MaxValue])); Need(maximum.Indices.Single() == int.MaxValue, "Native Int32 index incorrectly capped by a fixture/model count.");
            Reject(() => NativeStretchRead345.Expected(points, maximum), "STRETCH_POINT_NOT_FOUND");
            Reject(() => NativeStretchRead345.Validate([new(1, [0d, 0d, 0d])]), "NATIVE_STRETCH_READ_FAILED");
            bool canceled = false; try { NativeStretchRead345.Expected(points, edit, () => throw new OperationCanceledException()); } catch (OperationCanceledException) { canceled = true; }
            Need(canceled, "Native point work discarded cancellation.");
            Reject(() => NativeStretchRead345.Token(Wire.Element(new { content = new string('x', NativeCurveRead342.SnapshotByteBudget) }), points), "RESPONSE_LIMIT_EXCEEDED");
            return Task.CompletedTask;
        });
        await test("block345 actual native stretch publication matches sole independent retained-root parser and Paper route", () =>
        {
            var operation = Operation([1]); JsonElement Payload(params object[] operations) => Wire.Element(new { bridge_instance_id = Guid.NewGuid().ToString(), document_id = Guid.NewGuid().ToString(), expected_revision = "1", operation_id = Guid.NewGuid().ToString(), units = "Millimeters", target_space = "paper", operations });
            Need(ModifyValidation.Parse(Payload(operation)).Operations.Single() is NativeStretchEdit345, "Actual parser/Paper route missing.");
            Reject(() => ModifyValidation.Parse(Payload(operation, new { operation = "erase", handles = new[] { "A2" } })), "INVALID_OPERATION_ORDER");
            var branch = AuditServerContractSupport.Branch("modify_entities", "stretch_curve").GetProperty("oneOf").EnumerateArray()
                .Single(v => v.GetProperty("required").EnumerateArray().Any(field => field.GetString() == "expected_stretch_token"));
            Need(branch.GetProperty("properties").GetProperty("mode").GetProperty("const").GetString() == "native" && branch.GetProperty("required").EnumerateArray().Any(v => v.GetString() == "expected_stretch_token") && !branch.GetProperty("properties").GetProperty("indices").TryGetProperty("maxItems", out _), "Public native point/token contract or admission changed.");
            var vector = branch.GetProperty("properties").GetProperty("displacement_wcs");
            Need(vector.GetProperty("prefixItems")[2].GetProperty("const").GetInt32() == 0 && vector.GetProperty("items").ValueKind == JsonValueKind.False, "Published native stretch vector must preserve zero Z admission.");
            return Task.CompletedTask;
        });
    }
}
