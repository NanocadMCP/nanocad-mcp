using System.Text.Json;
using NanoCAD.Contracts;
using Teigha.DatabaseServices;
using Teigha.Geometry;

namespace NanoCADBridge;

// Native Curve.TransformBy is the placement primitive: controls/fit definitions
// are observed completely, never reconstructed as a newly created Spline.
// This admission is deliberately separate from replacement/paired-analysis rules.
internal static class NativeSplinePlacement407
{
    internal static bool Placement(EditSpec operation) => operation is CopyTransform2d407 or PolarRepeat2d407 ||
        (operation is MoveSpec or CopySpec or RotateSpec or ScaleSpec or Mirror2dSpec) && PlaneCurveTransforms394.Frame(operation) == null;
    internal static bool OnlyPlacement(IEnumerable<EditSpec> operations)
    {
        bool found = false;
        foreach (var operation in operations)
        {
            found = true;
            if (!Placement(operation)) return false;
        }
        return found;
    }
    private static bool Copies(EditSpec operation) => operation is CopySpec or CopyTransform2d407 or PolarRepeat2d407 { Count: > 1 } or Mirror2dSpec { Copy: true };
    private static void Need(bool condition, string message, string code = "UNSUPPORTED_SPLINE_PLACEMENT")
    {
        if (!condition) throw new DomainException(code, message);
    }
    internal static NativeSplineGeometry342 CheckGeometry(Entity entity, Action cancel)
    {
        Need(entity.GetType() == typeof(Spline) && entity is Spline, "Placement requires an exact native Spline.", "UNSUPPORTED_ENTITY_TYPE");
        var spline = (Spline)entity;
        cancel(); var geometry = CadNativeCurveInspection342.ReadSpline(spline, cancel, NativeCurveRead342.SnapshotByteBudget);
        NativeCurveRead342.RequireFinite(geometry, cancel);
        Need(geometry.planar, "Native Spline placement in XY requires a reported planar shape; no projection is performed.", "PLANAR_GEOMETRY_REQUIRED");
        Need(geometry.degree >= 1 && geometry.control_points.Length > 0 && geometry.knots.Length > 0 &&
            geometry.end_parameter > geometry.start_parameter, "Native Spline must report its complete nonempty shape and increasing finite parameter domain.", "INVALID_GEOMETRY");
        void Point(double[] point)
        {
            cancel();
            Need(Math.Abs(point[2]) <= 1e-9, "The complete native Spline must lie in WCS XY/Z=0; no projection is performed.", "PLANAR_GEOMETRY_REQUIRED");
        }
        foreach (var point in geometry.control_points) Point(point);
        Point(geometry.start_point); Point(geometry.end_point);
        // Fit points/tangents are part of the retained engineering definition,
        // not a replacement control-point approximation. Periodic and unclamped
        // native representations retain their actual knots/weights/parameterization.
        if (geometry.has_fit_data)
        {
            var fit = geometry.native_inspection.fit_data;
            Need(fit != null, "Native fitted Spline data is unavailable; its fitted definition cannot be verified for placement.", "SPLINE_FIT_DATA_UNAVAILABLE");
            foreach (var point in fit!.points) Point(point);
            if (fit.start_tangent is { } start) Point(start);
            if (fit.end_tangent is { } end) Point(end);
        }
        if (geometry.native_inspection.plane is { } plane)
        {
            var normal = new Vector3d(plane.normal[0], plane.normal[1], plane.normal[2]);
            Need(normal.Length > 0, "Native Spline plane has no usable normal.", "INVALID_GEOMETRY");
            var unit = normal.GetNormal(); var origin = new Point3d(plane.point[0], plane.point[1], plane.point[2]);
            foreach (var point in geometry.control_points)
            {
                cancel();
                Need(Math.Abs((new Point3d(point[0], point[1], point[2]) - origin).DotProduct(unit)) <= EllipseVerification.Tolerance,
                    "Reported native plane does not contain the complete Spline controls.", "NATIVE_SPLINE_PLANE_MISMATCH");
            }
            // A straight Spline has no unique plane normal; any reported plane
            // containing it is valid. Complete XY controls establish the requested
            // physical plane without imposing an artificial +Z normal requirement.
        }
        return geometry;
    }
    internal static NativeSplineGeometry342 CheckSource(Entity source, IReadOnlyCollection<EditSpec> operations,
        Database database, Transaction transaction, Action cancel)
    {
        Need(OnlyPlacement(operations), "The fitted/periodic Spline placement route cannot mix replacement, curvature, constraint or content edits.", "INVALID_OPERATION_ORDER");
        Need(!source.ObjectId.IsNull && source.Database == database && !source.ObjectId.IsErased && !source.ObjectId.IsEffectivelyErased,
            "Placement source must be a live resident Spline in the current document.", "OBJECT_NOT_FOUND");
        Need(!source.OwnerId.IsNull && transaction.GetObject(source.OwnerId, OpenMode.ForRead) is BlockTableRecord { IsLayout: true },
            "A nested/block-owned Spline requires its owning definition edit route.", "OUT_OF_SCOPE");
        var observed = CheckGeometry(source, cancel);
        CadIndependentCurve.Require(source, database, transaction, cancel);
        Need(source.Annotative != AnnotativeStates.True, "Native annotation contexts require their owning placement route.", "UNSUPPORTED_CURVE_DEPENDENCIES");
        if (operations.Any(Copies) && !source.ExtensionDictionary.IsNull)
        {
            cancel();
            var dictionary = transaction.GetObject(source.ExtensionDictionary, OpenMode.ForRead) as DBDictionary;
            Need(dictionary != null && dictionary.Count == 0,
                "A fitted/periodic Spline with owned extension records requires a resident deep-clone route; ordinary detached copies cannot certify those records.",
                "UNSUPPORTED_CURVE_CLONE_DEPENDENCIES");
        }
        // In-place native TransformBy retains the original owner/identity. The
        // root pipeline retains its metadata/display admission and cancellation.
        return observed;
    }
    internal static void VerifyUnchanged(Spline source, NativeSplineGeometry342 before, Action cancel)
    {
        var after = CheckGeometry(source, cancel);
        object Definition(NativeSplineGeometry342 value) => new
        {
            value.degree, value.rational, value.periodic, value.closed, value.planar, value.has_fit_data,
            value.control_points, value.knots, value.weights, value.control_point_tolerance, value.knot_tolerance,
            value.start_parameter, value.end_parameter, value.start_point, value.end_point,
            value.native_inspection.fit_data, value.native_inspection.entity_definition
        };
        // Ignore the arbitrary plane orientation of a straight curve and prose
        // diagnostics; neither is a retained definition or an engineering edit.
        Need(JsonSerializer.Serialize(Definition(before), Wire.Json) == JsonSerializer.Serialize(Definition(after), Wire.Json),
            "Native copy preparation changed the original Spline's complete observed definition.", "NATIVE_SPLINE_SOURCE_CHANGED");
    }
}
