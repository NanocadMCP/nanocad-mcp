using System.Text.Json;
using NanoCAD.Contracts;
using G = Multicad.Geometry;

namespace NanoCADBridge;

// Read-only complete native polyline observation. No membership, ownership,
// edit readiness or independent-object identity is inferred here.
internal static class NativeSketchPolylineGeometry
{
    internal static JsonElement Read(G.EntityGeometry geometry, Action cancel)
    {
        cancel();
        if (geometry.GeometryType != G.EntityGeomType.kPolyline)
            throw new DomainException("SKETCH_GEOMETRY_UNAVAILABLE", "A native polyline is required for this observation.");
        using var polyline = geometry.Polyline;
        using var segments = polyline.Segments;
        var rows = new List<object>(); var budget = new ModelInventoryBudget(cancel);
        for (int index = 0; index < segments.Count; index++)
        {
            cancel(); object row;
            switch (segments.SegType((uint)index))
            {
                case G.SegPlType.sgLine: using (var line = segments.GetLineSegAt((uint)index)) row = SketchGeometryReader.Line(line); break;
                case G.SegPlType.sgArc: using (var arc = segments.GetArcSegAt((uint)index)) row = SketchGeometryReader.Arc(arc); break;
                case G.SegPlType.sgNurb: using (var spline = segments.GetNurbSegAt((uint)index)) row = NativeSketchCurveGeometry445.Spline(spline, cancel); break;
                case G.SegPlType.sgEllipse: using (var ellipse = segments.GetEllipsSegAt((uint)index)) row = NativeSketchCurveGeometry445.Ellipse(ellipse, cancel); break;
                default: throw new DomainException("SKETCH_GEOMETRY_UNAVAILABLE", "The native polyline requires its complete segment adapter.");
            }
            budget.Add(row); rows.Add(row);
        }
        var result = Wire.Element(new { kind = "polyline", closed = polyline.ClosedLogically, segments = rows });
        new ModelInventoryBudget(cancel).Add(result); return result;
    }
}
