using NanoCAD.Contracts;
using Teigha.DatabaseServices;
using Teigha.Geometry;

namespace NanoCADBridge;

internal static class HatchLoopGeometry
{
    internal sealed record Vertex(double[] point_ocs, double bulge);
    internal sealed record Boundary(string Kind, bool Complete, Vertex[] Vertices, object[]? Curves = null);
    private static double N(double n) => double.IsFinite(n) ? n : throw new DomainException("OBJECT_READ_FAILED", "Non-finite hatch boundary.");
    private static void Budget(long bytes)
    {
        if (bytes > Wire.PayloadBudget - 8192) throw new DomainException("RESPONSE_LIMIT_EXCEEDED", "Stored hatch boundary exceeds the actual response-byte budget; no edge-count truncation occurred.");
    }
    private static int Bytes(Vertex vertex) => System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(vertex, Wire.Json).Length + 1;
    internal static Boundary Read(HatchLoop loop, CancellationToken cancel)
    {
        if (loop.IsPolyline)
        {
            var vertices = new List<Vertex>(); long bytes = 1024;
            foreach (BulgeVertex vertex in loop.Polyline)
            {
                cancel.ThrowIfCancellationRequested();
                var value = new Vertex(new[] { N(vertex.Vertex.X), N(vertex.Vertex.Y) }, N(vertex.Bulge));
                bytes += Bytes(value); Budget(bytes); vertices.Add(value);
            }
            return new("polyline", true, vertices.ToArray());
        }
        // AppendLoop(ObjectIdCollection) stores straight Polyline sources as
        // a chain of owned LineSegment2d wrappers in the installed SDK.
        var curves = loop.Curves;
        try
        {
            if (curves.Count == 0) return new("empty_curve_loop", false, []);
            if (curves.Cast<Curve2d>().Any(curve => curve.GetType() != typeof(LineSegment2d)))
            {
                var observed = new List<object>(); var capacity = new SerializedCapacity357(Wire.PayloadBudget - 8192, cancel.ThrowIfCancellationRequested);
                bool complete = true;
                foreach (Curve2d curve in curves)
                {
                    cancel.ThrowIfCancellationRequested();
                    object edge;
                    try { edge = CadHatchEditingReader.ReadStoredCurve(curve, cancel.ThrowIfCancellationRequested); }
                    catch (DomainException e) when (e.Error.code == "UNSUPPORTED_HATCH_CONTENT")
                    { complete = false; edge = new { kind = "unavailable", runtime_type = curve.GetType().FullName, reason = e.Message }; }
                    capacity.Add(edge); observed.Add(edge);
                }
                return new("native_curve_loop", complete, [], observed.ToArray());
            }
            if (curves.Count < 3) return new("unsupported_curves", false, []);
            var starts = new List<Point2d>(); var ends = new List<Point2d>(); long bytes = 1024;
            for (int i = 0; i < curves.Count; i++)
            {
                cancel.ThrowIfCancellationRequested();
                var line = (LineSegment2d)curves[i]; starts.Add(line.StartPoint); ends.Add(line.EndPoint);
                N(starts[i].X); N(starts[i].Y); N(ends[i].X); N(ends[i].Y);
                bytes += Bytes(new(new[] { starts[i].X, starts[i].Y }, 0)); Budget(bytes);
            }
            for (int i = 0; i < starts.Count; i++) { cancel.ThrowIfCancellationRequested(); if (ends[i].GetDistanceTo(starts[(i + 1) % starts.Count]) > 1e-8) return new("unsupported_curve_chain", false, Array.Empty<Vertex>()); }
            return new("line_segments", true, starts.Select(p => new Vertex(new[] { p.X, p.Y }, 0)).ToArray());
        }
        finally { foreach (Curve2d curve in curves) curve.Dispose(); }
    }
}
