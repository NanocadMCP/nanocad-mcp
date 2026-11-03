using NanoCAD.Contracts;
using Teigha.DatabaseServices;
using Teigha.Geometry;

namespace NanoCADBridge;

// SDK Entity.GeometricExtents, Polyline2d.GetEnumerator/VertexPosition and
// Vertex2d.Position/Bulge/width/type; installed hostdbmgd confirms VertexPosition
// takes Vertex2d by value (the CHM incorrectly renders its signature as ref).
internal static class CadImportedGeometry407
{
    private static double Finite(double value) => double.IsFinite(value) ? value : throw new DomainException("OBJECT_READ_FAILED", "Imported geometry contains a nonfinite reported value.");
    private static double[] Point(Point3d point) => [Finite(point.X), Finite(point.Y), Finite(point.Z)];
    private static double[] Vector(Vector3d vector) => [Finite(vector.X), Finite(vector.Y), Finite(vector.Z)];
    internal static object PointGeometry(DBPoint point) => new { position = Point(point.Position), normal = Vector(point.Normal), thickness = Finite(point.Thickness) };
    internal static object RayGeometry(Ray ray) => new { base_point = Point(ray.BasePoint), unit_direction = Vector(ray.UnitDir), extent = "half_infinite" };
    internal static object XlineGeometry(Xline line) => new { base_point = Point(line.BasePoint), unit_direction = Vector(line.UnitDir), extent = "infinite_both_directions" };
    internal static object PolylineGeometry(Polyline2d polyline, Action cancel, bool includeEditing = true)
    {
        var transaction = polyline.Database.TransactionManager.TopTransaction ?? throw new DomainException("OBJECT_READ_FAILED", "Legacy polyline read requires the current transaction.");
        var budget = new SerializedCapacity357(Wire.PayloadBudget - 8192, cancel); var vertices = new List<object>();
        foreach (ObjectId id in polyline)
        {
            cancel();
            if (id.IsNull || !id.IsValid) throw new DomainException("OBJECT_READ_FAILED", "Legacy polyline contains an invalid vertex identity.");
            if (id.IsErased || id.IsEffectivelyErased) continue;
            if (transaction.GetObject(id, OpenMode.ForRead) is not Vertex2d vertex)
                throw new DomainException("OBJECT_READ_FAILED", "Legacy polyline child is not a Vertex2d.");
            var observation = new
            {
                handle = Validation.NormalizeHandle(vertex.Handle.ToString()),
                point_wcs = Point(polyline.VertexPosition(vertex)), stored_position_ocs = Point(vertex.Position),
                vertex_type = vertex.VertexType.ToString(), bulge = Finite(vertex.Bulge),
                start_width = Finite(vertex.StartWidth), end_width = Finite(vertex.EndWidth)
            };
            budget.Add(observation); vertices.Add(observation);
        }
        var geometry = new
        {
            representation = "legacy_native_polyline2d", polyline_type = polyline.PolyType.ToString(), closed = polyline.Closed,
            elevation = Finite(polyline.Elevation), normal = Vector(polyline.Normal), thickness = Finite(polyline.Thickness),
            constant_width = Finite(polyline.ConstantWidth), default_start_width = Finite(polyline.DefaultStartWidth),
            default_end_width = Finite(polyline.DefaultEndWidth), vertex_count = vertices.Count, vertices,
            vertex_semantics = "Reported native stored vertices and types; curve-fit/spline-control vertices are not an evaluated or sampled boundary.",
            editing_scope = "ordinary_SimplePoly_retained_native_edit; fitted_forms_are_observed_without_conversion",
            editing = includeEditing ? CadLegacyPolyline408.Editing(polyline, transaction, cancel) : null
        };
        SerializedCapacity357.Check(geometry, Wire.PayloadBudget - 8192, cancel); return geometry;
    }
}
