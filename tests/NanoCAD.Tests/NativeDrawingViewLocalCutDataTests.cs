using NanoCAD.Contracts;

internal static class NativeDrawingViewLocalCutDataTests
{
    static readonly NativeDrawingViewLocalCutExpected Expected = new(
        "{B1000000-1111-2222-3333-000000000001}", "{B1000000-1111-2222-3333-000000000002}",
        "{B1000000-1111-2222-3333-000000000003}", "{B1000000-1111-2222-3333-000000000004}");
    const string Target = "{B1000000-1111-2222-3333-000000000005}";
    const string DepthView = "{B1000000-1111-2222-3333-000000000006}";
    const string Foreign = "{F2000000-AAAA-BBBB-CCCC-000000000099}";
    static void Need(bool condition, string message) { if (!condition) throw new Exception(message); }
    static char[] Buffer(string? target = Target, string? depthView = DepthView)
    {
        string?[] slots = [Expected.Drawing, Expected.TopDocument, Expected.OwnerDocument, Expected.Sketch, target, depthView];
        var result = new char[NativeDrawingViewLocalCutData.BufferLength];
        for (int slot = 0; slot < slots.Length; slot++)
            if (slots[slot] is { } value) value.CopyTo(0, result, slot * NativeDrawingViewLocalCutData.SlotWidth, value.Length);
        return result;
    }
    static NativeDrawingViewLocalCutRead Read(char[]? text = null, double[]? values = null, NativeDrawingViewLocalCutExpected? expected = null) =>
        NativeDrawingViewLocalCutData.Parse(0, NativeDrawingViewLocalCutData.CompletePhase, text ?? Buffer(), values ?? [-2.5, 4.25, 0.125, 5], expected ?? Expected);
    static DomainException Refuse(Action action)
    {
        try { action(); }
        catch (DomainException error)
        {
            Need(error.Error.code == "DRAWING_VIEW_STATE_UNAVAILABLE" && !error.Error.retryable,
                "An incomplete native cut read became available/retryable or lost its precise refusal code.");
            return error;
        }
        throw new Exception("An invalid native cut receipt was accepted as an actual read.");
    }
    internal static async Task Run(Func<string, Func<Task>, Task> test)
    {
        await test("native local-cut wire preserves distinct target and depth-view IDs with their actual point and depth", () =>
        {
            var result = Read();
            Need(result.Drawing == Expected.Drawing && result.TopDocument == Expected.TopDocument &&
                result.OwnerDocument == Expected.OwnerDocument && result.Sketch == Expected.Sketch &&
                result.TargetView == Target && result.DepthView == DepthView && result.TargetView != result.DepthView,
                "The native local-cut depth-reference ID was truncated, swapped or replaced by the target ID.");
            Need(result.DepthPoint == new Point3(-2.5, 4.25, 0.125) && result.Depth == 5,
                "Native cut point/depth changed units, lost a component or used a caller value.");
            return Task.CompletedTask;
        });
        await test("native local-cut wire retains absent actual view references without expected-ID fallback", () =>
        {
            foreach (var pair in new (string? Target, string? Depth)[] { (null, null), (Target, null), (null, DepthView), (Foreign, DepthView) })
            {
                var result = Read(Buffer(pair.Target, pair.Depth));
                Need(result.TargetView == pair.Target && result.DepthView == pair.Depth,
                    "An absent or different actual native view reference was inferred from another returned/expected ID.");
            }
            return Task.CompletedTask;
        });
        await test("native local-cut wire refuses failed or partial reads even when all returned IDs look plausible", () =>
        {
            foreach (int status in new[] { 1, unchecked((int)0x80004005), unchecked((int)0x80070057) })
            {
                var error = Refuse(() => NativeDrawingViewLocalCutData.Parse(status, 6, Buffer(), [0, 0, 0, 5], Expected));
                var details = Wire.Element(error.Error.details!);
                Need(details.GetProperty("native_hresult").GetString() == "0x" + unchecked((uint)status).ToString("X8") &&
                    details.GetProperty("observed_ids")[5].GetString() == DepthView,
                    "A failed read lost the actual native status or distinct depth-reference observation.");
            }
            foreach (int phase in new[] { -1, 0, 1, 5, 7 })
                Refuse(() => NativeDrawingViewLocalCutData.Parse(0, phase, Buffer(), [0, 0, 0, 5], Expected));
            return Task.CompletedTask;
        });
        await test("native local-cut wire refuses stale drawing top owner and sketch context", () =>
        {
            foreach (var expected in new[] { Expected with { Drawing = Foreign }, Expected with { TopDocument = Foreign },
                Expected with { OwnerDocument = Foreign }, Expected with { Sketch = Foreign } })
                Refuse(() => Read(expected: expected));
            var lower = Buffer(); for (int i = 0; i < lower.Length; i++) lower[i] = char.ToLowerInvariant(lower[i]);
            Need(Read(lower).DepthView == DepthView, "GUID case normalization changed actual native reference identity.");
            return Task.CompletedTask;
        });
        await test("native local-cut wire refuses malformed bounded IDs and nonfinite native point or depth", () =>
        {
            Refuse(() => NativeDrawingViewLocalCutData.Parse(0, 6, null!, [0, 0, 0, 5], Expected));
            Refuse(() => NativeDrawingViewLocalCutData.Parse(0, 6, Buffer(), null!, Expected));
            foreach (int size in new[] { 0, 38, 233, 235 }) Refuse(() => Read(new char[size]));
            foreach (int size in new[] { 0, 3, 5 }) Refuse(() => Read(values: new double[size]));
            var unterminated = Buffer(); Array.Fill(unterminated, 'X', 5 * 39, 39); Refuse(() => Read(unterminated));
            var trailing = Buffer(null, null); trailing[5 * 39 + 1] = 'X'; Refuse(() => Read(trailing));
            var absentContext = Buffer(); Array.Clear(absentContext, 3 * 39, 39); Refuse(() => Read(absentContext));
            Refuse(() => Read(Buffer(Guid.Empty.ToString("B"))));
            foreach (int index in new[] { 0, 1, 2, 3 })
                foreach (double invalid in new[] { double.NaN, double.PositiveInfinity, double.NegativeInfinity })
                { double[] values = [0, 0, 0, 5]; values[index] = invalid; Refuse(() => Read(values: values)); }
            return Task.CompletedTask;
        });
    }
}
