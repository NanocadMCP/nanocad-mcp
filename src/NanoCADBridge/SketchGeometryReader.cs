using Multicad.Geometry;
using Multicad.Mc3D;
using NanoCAD.Contracts;

namespace NanoCADBridge;

// Geometry only: never enters edit mode or asks a constraints manager to solve.
// Native validation is pending; selected explicitly so ordinary history reads are unchanged.
internal static class SketchGeometryReader
{
    internal static object Read(PlanarSketch sketch, Action budget,Action<object>? account=null)
    {
        budget();
        var geometry = sketch.GetAllItemsGeometry(false); // SDK: false means global coordinates.
        if (geometry == null)
            throw new DomainException("SKETCH_GEOMETRY_UNAVAILABLE", "Native sketch geometry is unavailable; no empty-sketch claim is made.");
        try
        {
            budget();
            var origin=sketch.TransformPointSketchToWorld(Point3d.Origin);
            var x=sketch.TransformPointSketchToWorld(new Point3d(1,0,0))-origin;
            var y=sketch.TransformPointSketchToWorld(new Point3d(0,1,0))-origin;
            var n=new Vector3d(x.Y*y.Z-x.Z*y.Y,x.Z*y.X-x.X*y.Z,x.X*y.Y-x.Y*y.X);
            double norm=Math.Sqrt(n.X*n.X+n.Y*n.Y+n.Z*n.Z);
            if(!double.IsFinite(norm)||norm<1e-12)throw new DomainException("SKETCH_FRAME_UNAVAILABLE","Native sketch coordinates did not provide a finite plane basis.");
            var frame=new{origin_wcs=Point(origin),x_step_wcs=Vector(x),y_step_wcs=Vector(y),plane_normal_wcs=Vector(new Vector3d(n.X/norm,n.Y/norm,n.Z/norm)),
                basis_scope="native_sketch_coordinates_to_WCS_steps_not_assumed_unit_axes"};
            budget();
            var items = new List<object>();
            bool complete = true;
            foreach (var pair in geometry.OrderBy(pair => pair.Key.ToString(), StringComparer.Ordinal))
            {
                budget();
                if (pair.Key.IsNull) throw new DomainException("OBJECT_IDENTITY_MISMATCH", "Sketch geometry has an empty element identity.");
                object? detail = null;
                if (pair.Value?.GeometryType == EntityGeomType.kLine)
                {
                    using var line = pair.Value.LineSeg;
                    detail = Line(line);
                }
                else if (pair.Value?.GeometryType == EntityGeomType.kCircArc)
                {
                    using var arc = pair.Value.CircArc;
                    detail = Arc(arc);
                }
                else if(pair.Value?.GeometryType==EntityGeomType.kSpline)
                {using var spline=pair.Value.Nurb;detail=NativeSketchCurveGeometry445.Spline(spline,budget);}
                else if(pair.Value?.GeometryType==EntityGeomType.kEllipticArc)
                {using var ellipse=pair.Value.EllipArc;detail=NativeSketchCurveGeometry445.Ellipse(ellipse,budget);}
                else if (pair.Value?.GeometryType == EntityGeomType.kPolyline)
                {
                    using var polyline = pair.Value.Polyline;
                    using var segments = polyline.Segments;
                    int count = segments.Count;
                    if (count < 0)
                        throw new DomainException("ANALYSIS_LIMIT_EXCEEDED", "Native polyline returned an invalid segment count.");
                    var parts = new List<object>();
                    var partBytes=account==null?null:new ModelInventoryBudget(budget);
                    bool segmentsComplete = true;
                    for (uint index = 0; index < count; index++)
                    {
                        budget();
                        var type = segments.SegType(index);
                        object? part = null;
                        if (type == SegPlType.sgLine)
                        {
                            using var line = segments.GetLineSegAt(index);
                            part = Line(line);
                        }
                        else if (type == SegPlType.sgArc)
                        {
                            using var arc = segments.GetArcSegAt(index);
                            part = Arc(arc);
                        }
                        else if(type==SegPlType.sgNurb)
                        {using var spline=segments.GetNurbSegAt(index);part=NativeSketchCurveGeometry445.Spline(spline,budget);}
                        else if(type==SegPlType.sgEllipse)
                        {using var ellipse=segments.GetEllipsSegAt(index);part=NativeSketchCurveGeometry445.Ellipse(ellipse,budget);}
                        if (part == null) segmentsComplete = false;
                        var record=new { index, native_segment_type = type.ToString(),
                            status = part == null ? "unsupported_geometry" : "read", geometry = part,
                            unavailable_reason = part == null ? "native_segment_type_not_yet_serialized" : (string?)null };
                        partBytes?.Add(record);parts.Add(record);
                    }
                    if (!segmentsComplete) complete = false;
                    detail = new { kind = "polyline", closed = polyline.IsClosed(), segments = parts,
                        geometry_details_complete = segmentsComplete,
                        segment_index_scope = "geometry_order_only_not_constraint_subentity_identity" };
                }
                if (detail == null) complete = false;
                var item=new { element_id = pair.Key.ToString(), runtime_type = pair.Value?.GetType().FullName, geometry_type = pair.Value?.GeometryType.ToString(),
                    status = detail == null ? "unsupported_geometry" : "read", geometry = detail,
                    unavailable_reason = detail == null ? "native_geometry_type_not_yet_serialized" : (string?)null };
                account?.Invoke(item);items.Add(item);
            }
            return new { kind = "sketch", sketch_id = sketch.ID.ToString(), coordinate_system = "WCS", length_unit = "drawing_units",frame,
                identity_scope = "bridge_document_revision", geometry_details_complete = complete, elements = items,
                constraints = "not_read_or_evaluated" };
        }
        finally
        {
            // SDK creates geometry wrappers. Release every distinct wrapper, also on bounded failure.
            foreach (var value in geometry.Values.Distinct(ReferenceEqualityComparer.Instance))
                if (value is IDisposable disposable) disposable.Dispose();
        }
    }
    internal static object Line(LineSeg3d line) => new { kind = "line", start = Point(line.StartPoint), end = Point(line.EndPoint) };
    internal static object Arc(CircArc3d arc) => new { kind = "circular_arc", center = Point(arc.Center), radius = Finite(arc.Radius),
        normal = Vector(arc.Normal), reference_direction = Vector(arc.RefVector),
        start_angle = Finite(arc.StartAng), end_angle = Finite(arc.EndAng), angle_unit = "radians" };
    private static double Finite(double value) => double.IsFinite(value) ? value : throw new DomainException("INVALID_GEOMETRY", "Non-finite sketch geometry.");
    private static double[] Point(Point3d point) => new[] { Finite(point.X), Finite(point.Y), Finite(point.Z) };
    private static double[] Vector(Vector3d vector) => new[] { Finite(vector.X), Finite(vector.Y), Finite(vector.Z) };
}
