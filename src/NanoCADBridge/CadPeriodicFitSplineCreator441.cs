using NanoCAD.Contracts;
using Teigha.DatabaseServices;
using Teigha.Geometry;

namespace NanoCADBridge;

internal static class CadPeriodicFitSplineCreator441
{
    private const double MachineEpsilon = 2.2204460492503131e-16;
    private const string MismatchCode = "NATIVE_CLOSED_FIT_SPLINE_MISMATCH";
    private sealed record NativeConstruction(string stage, int degree, bool closed, bool periodic);
    private static void Require(bool condition, string message, object? details = null)
    {
        if (!condition) throw new DomainException(MismatchCode, message, false, details);
    }
    private static bool Finite(Point3d point) => double.IsFinite(point.X) && double.IsFinite(point.Y) && double.IsFinite(point.Z);
    private static bool Finite(Vector3d vector) => double.IsFinite(vector.X) && double.IsFinite(vector.Y) && double.IsFinite(vector.Z);
    private static Point3d Point(double[] value) => new(value[0], value[1], value[2]);
    private static NativeConstruction Observe(NurbCurve3d curve, string stage, Action cancel)
    {
        cancel(); int degree = curve.Degree;
        cancel(); bool closed = curve.IsClosed();
        cancel(); bool periodic = curve.IsPeriodic(out _);
        cancel(); return new(stage, degree, closed, periodic);
    }

    internal static Spline CreateLocal(PeriodicFitSplineSpec441 spec, Action? check = null)
    {
        var cancel = check ?? (() => { }); cancel();
        var values = new Point3d[spec.FitPoints.Length];
        for (int i = 0; i < values.Length; i++) { cancel(); values[i] = new(spec.FitPoints[i].X, spec.FitPoints[i].Y, 0); }
        using var points = new Point3dCollection(values);
        // Original SDK nrxdbgate/geometry.h: NcGeNurbCurve3d(degree,
        // fitPolyline, isPeriodic). The matching Teigha constructor is also
        // documented in ncNET_ref_en and the installed PE passes all three
        // arguments directly to OdGeNurbCurve3d. This temporary geometry is a
        // fit-point carrier, never a stored faceted entity or our interpolation.
        using var fitPolyline = new PolylineCurve3d(points);
        using var native = new NurbCurve3d(PeriodicFitSplineSpec441.NativeDegree, fitPolyline, true);
        var construction = new List<NativeConstruction> { Observe(native, "after_periodic_fit_constructor", cancel) };
        cancel();
        native.FitTolerance = new Tolerance(Tolerance.Global.EqualVector, spec.FitTolerance);
        construction.Add(Observe(native, "after_fit_tolerance", cancel));
        // Original SDK nrxdbgate/geometry.h and ncNET_ref_en expose MakeClosed;
        // installed IL directly calls OdGeNurbCurve3d.makeClosed. The native
        // operation creates a smooth connecting spline and joins it to the
        // initial curve; it does nothing when already closed. Run it after all
        // fit setters, with no manually supplied seam points/tangents/controls.
        // Native closure may change degree; actual geometry remains mandatory.
        cancel();
        try { native.MakeClosed(); }
        catch (Teigha.Runtime.Exception error)
        {
            cancel(); throw new DomainException(MismatchCode, "Native smooth closure failed.", false,
                new { stage = "native_make_closed", native_error_status = error.ErrorStatus.ToString(), native_construction = construction });
        }
        var closedNative = Observe(native, "after_native_make_closed", cancel); construction.Add(closedNative);
        Require(closedNative.degree > 0 && closedNative.closed,
            "Native smooth closure did not retain a closed curve of positive degree.", new { native_construction = construction });
        // Installed Curve.CreateFromGeCurve calls OdDbCurve.createFromOdGeCurve;
        // the returned DB wrapper owns its detached native result. Its NURBS
        // representation need not retain the geometry helper's FitData record.
        var created = Curve.CreateFromGeCurve(native);
        try
        {
            Require(created != null && created.GetType() == typeof(Spline) && created.ObjectId.IsNull && created.OwnerId.IsNull,
                "Native closed fitting did not return a detached exact Spline.",
                new { runtime_type = created?.GetType().FullName, detached = created?.ObjectId.IsNull, owner_empty = created?.OwnerId.IsNull,
                    native_construction = construction });
            var spline = (Spline)created!;
            try { VerifyCreated(spline, spec with { Workplane = null }, cancel); }
            catch (DomainException error) when (error.Error.code == MismatchCode)
            {
                throw new DomainException(error.Error.code, error.Message, false,
                    new { native_construction = construction, verification = error.Error.details });
            }
            return spline;
        }
        catch { created?.Dispose(); throw; }
    }

    // The ordinary creation writer transforms the local curve once. Reobserve
    // the actual WCS result here without transforming a resident entity.
    internal static void VerifyCreated(Spline spline, PeriodicFitSplineSpec441 spec, Action cancel)
    {
        cancel(); Require(spline.GetType() == typeof(Spline) && !spline.IsNull,
            "Creation did not return a complete native Spline.", new { runtime_type = spline.GetType().FullName });
        var shape = CadSplineReader.ReadReported(spline, cancel);
        var entity = shape.native_inspection.entity_definition;
        var facts = new { requested_construction = spec.Construction, initial_fit_degree = PeriodicFitSplineSpec441.NativeDegree,
            reported_nurbs_degree = shape.degree, reported_nurbs_closed = shape.closed, reported_nurbs_periodic = shape.periodic,
            reported_planar = shape.planar, native_entity_definition = entity };
        void Need(bool condition, string message) => Require(condition, message, facts);
        // NurbsData flags describe the vendor's stored representation. The
        // actual native entity's Closed/Degree getters are separate facts. A
        // normalized nonperiodic representation must not reject a closed smooth
        // native result, and a false optional planar flag is not a projection
        // request: the complete controls still have to prove the given plane.
        Need(shape.degree > 0 && entity.degree == shape.degree && entity.control_point_count == shape.control_points.Length && entity.closed,
            "Creation did not retain an actually closed native Spline with consistent degree and control-point observations.");
        Need(shape.control_points.Length > 0 && shape.knots.Length > 1 &&
            shape.end_parameter > shape.start_parameter &&
            shape.knots.Zip(shape.knots.Skip(1), (a, b) => a <= b).All(value => value) &&
            (shape.rational ? shape.weights.Length == shape.control_points.Length && shape.weights.All(weight => weight > 0) :
                shape.weights.Length == 0 || shape.weights.Length == shape.control_points.Length && shape.weights.All(weight => weight > 0)),
            "Native closed fitted curve has an incomplete or inconsistent finite NURBS definition.");

        var plane = spec.Workplane ?? DrawingWorkplane.XY;
        var origin = new Point3d(plane.Origin.X, plane.Origin.Y, plane.Origin.Z);
        var n = DrawingWorkplane.Normal(plane); var normal = new Vector3d(n.X, n.Y, n.Z);
        Point3d World(Point2 local)
        {
            var point = DrawingWorkplane.World(plane, local); return new(point.X, point.Y, point.Z);
        }
        // Native absolute geometry resolution plus coordinate roundoff; this
        // does not constrain the SDK's choice of controls, knots or seam phase.
        double precision = 1e-7;
        foreach (var point in shape.control_points)
        {
            cancel(); precision = Math.Max(precision, 32 * MachineEpsilon * point.Max(Math.Abs));
        }
        foreach (var point in shape.control_points)
        {
            cancel(); Need(Math.Abs((Point(point) - origin).DotProduct(normal)) <= precision,
                "Native closed fitted curve left the explicitly requested workplane; no projection is performed.");
        }
        foreach (var point in spec.FitPoints)
        {
            cancel(); var expected = World(point); var nearest = spline.GetClosestPointTo(expected, false); cancel();
            Need(Finite(nearest) && (nearest - expected).Length <= spec.FitTolerance + precision,
                "Actual native closed fitted curve misses a requested fit point beyond its specified fit tolerance.");
        }

        // A closed/periodic flag alone is insufficient: observe position and
        // derivatives of the actual DB curve at both ends of its native domain.
        // Check geometric forward-tangent continuity, independent of native
        // parameter speed. Repeating an open fit endpoint can give only C0.
        Need((spline.StartPoint - spline.EndPoint).Length <= precision,
            "Actual native closed seam is not position-continuous.");
        var firstStart = spline.GetFirstDerivative(spline.StartParam); cancel();
        var firstEnd = spline.GetFirstDerivative(spline.EndParam); cancel();
        Need(Finite(firstStart) && Finite(firstEnd) && double.IsFinite(firstStart.Length) && double.IsFinite(firstEnd.Length) &&
            firstStart.Length > 0 && firstEnd.Length > 0,
            "Actual native closed seam has no finite nonzero tangent.");
        Need((firstStart.GetNormal() - firstEnd.GetNormal()).Length <= 1e-7,
            "Actual native closed seam does not retain the same forward tangent direction.");
        cancel();
    }
}
