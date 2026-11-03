using System.Collections;
using NanoCAD.Contracts;

internal static class ViewportMviewTests
{
    private static void Check(bool value) { if (!value) throw new Exception("MVIEW safety assertion failed."); }
    private static void Refused(Action action, string code = "UNSUPPORTED_VIEWPORT")
    { try { action(); } catch (DomainException e) when (e.Error.code == code) { return; } throw new Exception("Expected " + code); }
    private static ViewportDataValue[] Example(int layers = 0)
    {
        var prefix = new List<ViewportDataValue> { new(1001, "ACAD"), new(1000, "MVIEW"), new(1002, "{"), new(1070, 16),
            new(1010, new[] { 0d, 0, 0 }), new(1010, new[] { 0d, 0, 1 }) };
        prefix.AddRange(Enumerable.Range(0, 7).Select(i => new ViewportDataValue(1040, (double)i)));
        prefix.AddRange(Enumerable.Range(0, 8).Select(i => new ViewportDataValue(1070, i)));
        prefix.AddRange(Enumerable.Range(0, 7).Select(i => new ViewportDataValue(1040, (double)i)));
        prefix.Add(new(1070, 0)); prefix.Add(new(1002, "{"));
        prefix.AddRange(Enumerable.Range(0, layers).Select(i => new ViewportDataValue(1003, "Layer" + i)));
        prefix.Add(new(1002, "}")); prefix.Add(new(1002, "}")); return prefix.ToArray();
    }
    private sealed class Source(ViewportDataValue[] values, bool infinite = false) : IEnumerator, IDisposable
    {
        private int index = -1;
        public bool Disposed; public int Reads;
        public object Current { get { Reads++; return values[index % values.Length]; } }
        public bool MoveNext() { index++; return infinite || index < values.Length; }
        public void Reset() => throw new NotSupportedException();
        public void Dispose() => Disposed = true;
    }
    private static ViewportDataValue[] Read(ViewportDataValue[] data) => ViewportMviewData.Read(new Source(data), o => (ViewportDataValue)o, () => { });
    public static async Task Run(Func<string, Func<Task>, Task> test)
    {
        await test("viewport MVIEW distinguishes native record from third-party malformed and oversized XData", () =>
        {
            Check(Read(Example()).Length == 32); Check(Read(Example(512)).Length == 544); Check(Read(Array.Empty<ViewportDataValue>()).Length == 0);
            Check(Read(Example(600)).Length==632);
            foreach (var change in new[] { (0, new ViewportDataValue(1001, "THIRD_PARTY")), (1, new(1000, "OTHER")), (3, new(1070, 17)),
                (6, new(1040, double.NaN)), (6, new(1040, "5")), (4, new(1010, new[] { 0d, 1 })), (13, new(1071, 2)), (30, new(1002, "{")) })
            { var data = Example(); data[change.Item1] = change.Item2; Refused(() => Read(data)); }
            Refused(() => Read(Example().Concat(new[] { new ViewportDataValue(1001, "OTHER") }).ToArray()));
            return Task.CompletedTask;
        });
        await test("viewport MVIEW enumeration bounds Current access and disposes on cancellation and converter faults", () =>
        {
            var oversized = new Source(Example(), true); Refused(() => ViewportMviewData.Read(oversized, o => (ViewportDataValue)o, () => { }),"RESPONSE_LIMIT_EXCEEDED");
            Check(oversized.Disposed && oversized.Reads>544);
            var canceled = new Source(Example()); int checks = 0;
            try { ViewportMviewData.Read(canceled, o => (ViewportDataValue)o, () => { if (++checks == 8) throw new OperationCanceledException(); }); }
            catch (OperationCanceledException) { }
            Check(canceled.Disposed && canceled.Reads < 3);
            var faulted = new Source(Example());
            try { ViewportMviewData.Read(faulted, _ => throw new InvalidOperationException(), () => { }); }
            catch (InvalidOperationException) { }
            Check(faulted.Disposed && faulted.Reads == 1); return Task.CompletedTask;
        });
        await test("viewport MVIEW comparison preserves snap grid frozen layers and unrelated view fields", () =>
        {
            var before = Read(Example(2)); var after = before.ToArray();
            for (int i = 6; i <= 9; i++) after[i] = new(1040, 500d + i);
            var edit = new SetViewportViewEdit("1E", "38", new(new[] { 10d, 20 }, 500, Math.PI / 2, true));
            ViewportMviewData.RequireDesired(before, after, edit);
            var locked = edit with { Properties = new(null, null, null, true) };
            ViewportMviewData.RequireDesired(before, before, locked);
            Refused(() => ViewportMviewData.RequireDesired(before, after, locked), "API_EXCEPTION");
            foreach (int index in new[] { 4, 10, 13, 21, 30 })
            {
                var bad = after.ToArray(); bad[index] = bad[index] with { Value = index == 4 ? new[] { 1d, 0, 0 } : index == 30 ? "ChangedLayer" : index == 13 ? 1 : 8d };
                Refused(() => ViewportMviewData.RequireDesired(before, bad, edit), "API_EXCEPTION");
            }
            Refused(() => ViewportMviewData.RequireSame(before, after), "API_EXCEPTION"); return Task.CompletedTask;
        });
    }
}
