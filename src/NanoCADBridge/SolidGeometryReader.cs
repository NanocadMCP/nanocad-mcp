using System.Diagnostics;
using NanoCAD.Contracts;
using Teigha.DatabaseServices;
using Teigha.Geometry;

namespace NanoCADBridge;

internal static class SolidGeometryReader
{
    public static object Read(Solid3d solid, CancellationToken cancellation)
    {
        if (solid.GetType() != typeof(Solid3d)) throw new DomainException("UNSUPPORTED_ENTITY_TYPE", "Only native Solid3d is supported; custom solid subclasses require a separate adapter.");
        cancellation.ThrowIfCancellationRequested();
        var watch = Stopwatch.StartNew();
        bool empty = solid.IsNull;
        double? volume = null; double[]? centroid = null; object? bounds = null;
        if (!empty)
        {
            var mass = solid.MassProperties;
            volume = Number(mass.Volume); centroid = Point(mass.Centroid);
            var box = solid.GeometricExtents;
            bounds = new { min = Point(box.MinPoint), max = Point(box.MaxPoint) };
        }
        bool recordHistory = solid.RecordHistory, showHistory = solid.ShowHistory;
        cancellation.ThrowIfCancellationRequested();
        return new { solid_kind = "Solid3d", body_state = empty ? "empty" : "body", volume, centroid, bounds,
            volume_unit = "drawing_units_cubed", record_history = recordHistory, show_history = showHistory,
            history_scope = "native_flags_only_feature_graph_not_exposed",
            inspection_duration_ms = watch.ElapsedMilliseconds,
            budget_notice = watch.ElapsedMilliseconds > 500 ? "Inspection exceeded the former diagnostic budget; properties were read successfully." : null };
    }
    private static double Number(double n) => double.IsFinite(n) ? n : throw new DomainException("OBJECT_READ_FAILED", "Non-finite native solid property.");
    private static double[] Point(Point3d p) => new[] { Number(p.X), Number(p.Y), Number(p.Z) };
}
