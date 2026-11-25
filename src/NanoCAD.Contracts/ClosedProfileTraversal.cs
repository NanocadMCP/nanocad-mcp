using System.Globalization;
using System.Text.Json.Nodes;

namespace NanoCAD.Contracts;

// A selected closed area has a winding and adjacent edges but no designated first edge.
public static class ClosedProfileTraversal
{
    public static void AlignForComparison(JsonObject boundary, int winding, int counterpartWinding, Action cancel)
    {
        if (winding is not (1 or -1) || counterpartWinding is not (1 or -1) || boundary["elements"] is not JsonArray elements) return;
        var rows = elements.ToArray();
        var shapes = rows.Select(r => r!["geometry"]!.ToJsonString()).ToArray();
        int start = Start(shapes, cancel);
        boundary["boundary_winding"] = winding;
        if (start == 0) return;
        elements.Clear();
        for (int i = 0; i < rows.Length; i++)
        {
            cancel();
            var row = rows[(start + i) % rows.Length]!;
            row["element_id"] = i.ToString(CultureInfo.InvariantCulture);
            elements.Add(row);
        }
    }

    public static int Winding(IReadOnlyList<SketchConstraintCurve> edges, Action cancel)
    {
        if (edges.Count < 3 || edges.Any(e => e.Kind is not ("line" or "arc") || e.Start == null || e.End == null))
            return 0;
        static bool Touches(Point2 p, SketchConstraintCurve e) =>
            DrawingProfileGeometry.Distance(p, e.Start!) <= 1e-7 || DrawingProfileGeometry.Distance(p, e.End!) <= 1e-7;
        var origin = edges[0].Start!;
        double twiceArea = 0, absoluteTerms = 0;
        for (int i = 0; i < edges.Count; i++)
        {
            cancel();
            var edge = edges[i]; var previous = edges[(i + edges.Count - 1) % edges.Count]; var next = edges[(i + 1) % edges.Count];
            var a = edge.Start!; var b = edge.End!;
            bool forward = Touches(a, previous);
            if (forward == Touches(b, previous) || forward == Touches(a, next) || forward != Touches(b, next)) return 0;
            var chord = (a.X - origin.X) * (b.Y - origin.Y) - (a.Y - origin.Y) * (b.X - origin.X);
            var sector = edge.Kind == "arc" ? edge.Radius * edge.Radius * (edge.Sweep - Math.Sin(edge.Sweep)) : 0;
            twiceArea += (forward ? 1 : -1) * (chord + sector);
            absoluteTerms += Math.Abs(chord) + Math.Abs(sector);
        }
        return double.IsFinite(twiceArea) && Math.Abs(twiceArea) > Math.Max(1e-14, absoluteTerms * 1e-12) ? Math.Sign(twiceArea) : 0;
    }

    // Lexicographically smallest rotation, in linear time. Reversing or sorting
    // the sequence would discard traversal and adjacency information.
    public static int Start(IReadOnlyList<string> ordered, Action cancel)
    {
        int n = ordered.Count, a = 0, b = 1, offset = 0;
        while (a < n && b < n && offset < n)
        {
            cancel();
            int difference = string.CompareOrdinal(ordered[(a + offset) % n], ordered[(b + offset) % n]);
            if (difference == 0) { offset++; continue; }
            if (difference > 0) { a += offset + 1; if (a == b) a++; }
            else { b += offset + 1; if (a == b) b++; }
            offset = 0;
        }
        return Math.Min(a, b);
    }
}
