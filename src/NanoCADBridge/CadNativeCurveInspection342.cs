using System.Text.Json;
using NanoCAD.Contracts;
using Teigha.DatabaseServices;
using Teigha.Geometry;

namespace NanoCADBridge;

// Read-only reported native geometry. Owner/display/dependency metadata and
// operation-specific planarity/validity admission belong to the calling writer.
internal static class CadNativeCurveInspection342
{
    private sealed class Budget
    {
        private long bytes = 2; private readonly int limit; private readonly Action cancel;
        internal Budget(Action check, int maxBytes) { cancel = check; limit = maxBytes; }
        internal long Mark => bytes;
        internal void Restore(long mark) => bytes = mark;
        internal void Add(object value)
        {
            cancel(); bytes += JsonSerializer.SerializeToUtf8Bytes(value, Wire.Json).LongLength + 1;
            if (bytes > limit) throw new DomainException("RESPONSE_LIMIT_EXCEEDED", "Complete native curve arrays exceed the actual serialized-byte budget; no entries were truncated.");
        }
        internal void Finish(object value) => NativeCurveRead342.CheckSerializedBudget(value, limit, cancel);
    }
    internal static bool Supported(Curve curve) => curve.GetType() == typeof(Line) || curve.GetType() == typeof(Arc) ||
        curve.GetType() == typeof(Circle) || curve.GetType() == typeof(Polyline) || curve.GetType() == typeof(Ellipse) || curve.GetType() == typeof(Spline) ||
        curve is Polyline3d spatial&&spatial.GetType()==typeof(Polyline3d)&&spatial.PolyType==Poly3dType.SimplePoly;
    private static double Number(double value) => double.IsFinite(value) ? value : throw new DomainException("NATIVE_CURVE_READ_FAILED", "Native curve contains a nonfinite reported value.");
    private static double[] Point(Point3d value) => new[] { Number(value.X), Number(value.Y), Number(value.Z) };
    private static double[] Vector(Vector3d value) => new[] { Number(value.X), Number(value.Y), Number(value.Z) };
    private static void Count(int count) { if (count < 0) throw new DomainException("NATIVE_CURVE_READ_FAILED", "Native curve reported a negative collection count."); }

    internal static NativeCurvePlaneObservation342 ObservePlane(Curve curve, Action cancel)
    {
        cancel();
        if (curve.GetType() == typeof(Line)) return new(null, "line_has_no_unique_plane");
        if (!curve.IsPlanar) return new(null, "native_curve_is_non_planar");
        try
        {
            cancel(); using var plane = curve.GetPlane();
            if (plane == null) return new(null, "native_plane_getter_returned_null");
            var result = new NativeCurvePlane342(Point(plane.PointOnPlane), Vector(plane.Normal));
            cancel(); return new(result, null);
        }
        catch (Teigha.Runtime.Exception error) { cancel(); return new(null, "NATIVE_PLANE_UNAVAILABLE: " + error.ErrorStatus); }
        catch (DomainException error) when (error.Error.code == "NATIVE_CURVE_READ_FAILED")
        { cancel(); return new(null, "NATIVE_PLANE_UNAVAILABLE: " + error.Message); }
    }

    private static NativeSplineFit342 Fit(Spline spline, Budget budget, Action cancel, out string? partialReason)
    {
        cancel(); var fit = spline.FitData;
        // Fresh PE: FitData getter allocates an independent Point3dCollection;
        // GetFitPoints returns that backing field, not a resident spline pointer.
        using var points = fit.GetFitPoints(); Count(points.Count); var result = new List<double[]>();
        for (int i = 0; i < points.Count; i++) { cancel(); var point = Point(points[i]); budget.Add(point); result.Add(point); }
        // Installed managed FitData getter leaves its two returned tangent
        // fields zero instead of copying native getFitData out vectors. The
        // documented direct getters read the actual getFitTangents output.
        // These optional getters must not discard successfully read fit points,
        // tolerance and flags. A native automatic fit may not expose its stored
        // endpoint conditions through the same getter as an explicit fit.
        var reasons = new List<string>();
        double[]? Tangent(Func<Vector3d> getter, string name)
        {
            try { cancel(); return Vector(getter()); }
            catch (Teigha.Runtime.Exception error) { cancel(); reasons.Add(name + ": " + error.ErrorStatus); return null; }
            catch (DomainException error) when (error.Error.code == "NATIVE_CURVE_READ_FAILED")
            { cancel(); reasons.Add(name + ": " + error.Message); return null; }
        }
        var start = fit.TangentsExist ? Tangent(() => spline.StartFitTangent, "start_fit_tangent") : null;
        var end = fit.TangentsExist ? Tangent(() => spline.EndFitTangent, "end_fit_tangent") : null;
        partialReason = reasons.Count == 0 ? null : "NATIVE_SPLINE_FIT_TANGENTS_UNAVAILABLE: " + string.Join("; ", reasons);
        cancel(); return new(fit.Degree, result.ToArray(), Number(fit.FitTolerance), fit.TangentsExist, start, end, fit.KnotParam.ToString());
    }

    internal static NativeSplineGeometry342 ReadSpline(Spline spline, Action cancel,
        int maxSerializedBytes = NativeCurveRead342.PublicGeometryByteBudget)
    {
        cancel(); if (spline.GetType() != typeof(Spline) || spline.IsNull)
            throw new DomainException("UNSUPPORTED_SPLINE", "Native detail requires an exact non-null Spline; no reconstruction is performed.");
        var budget = new Budget(cancel, maxSerializedBytes); var native = spline.NurbsData;
        // NurbsData.init allocates its independent native point collection.
        using var points = native.GetControlPoints(); Count(points.Count);
        var knots = native.GetKnots(); var weights = native.GetWeights(); Count(knots.Count); Count(weights.Count);
        var controls = new List<double[]>(); var knotValues = new List<double>(); var weightValues = new List<double>();
        for (int i = 0; i < points.Count; i++) { cancel(); var point = Point(points[i]); budget.Add(point); controls.Add(point); }
        for (int i = 0; i < knots.Count; i++) { cancel(); double value = Number(knots[i]); budget.Add(value); knotValues.Add(value); }
        for (int i = 0; i < weights.Count; i++) { cancel(); double value = Number(weights[i]); budget.Add(value); weightValues.Add(value); }
        cancel(); bool hasFit = spline.HasFitData; NativeSplineFit342? fit = null; string? fitReason = null;
        if (hasFit)
        {
            long mark = budget.Mark;
            try { fit = Fit(spline, budget, cancel, out fitReason); }
            catch (Teigha.Runtime.Exception error) { cancel(); budget.Restore(mark); fitReason = "NATIVE_SPLINE_FIT_UNAVAILABLE: " + error.ErrorStatus; }
            catch (DomainException error) when (error.Error.code == "NATIVE_CURVE_READ_FAILED")
            { cancel(); budget.Restore(mark); fitReason = "NATIVE_SPLINE_FIT_UNAVAILABLE: " + error.Message; }
        }
        var plane = ObservePlane(spline, cancel);
        var inspection = new NativeSplineInspection342("complete_reported_native_nurbs", plane.plane, plane.unavailable_reason, fit, fitReason,
            "Reported native fit points/tolerance/tangent flag and knot parameterization when available. Optional tangent getter failure leaves the readable fit data intact, with null vectors and an explicit unavailable reason; no complete external constraint/association graph is certified.",
            "Read detail does not extend legacy creation, transform, geometry replacement or paired-analysis admission.",
            new(spline.Degree, spline.NumControlPoints, spline.IsRational, spline.Closed));
        var result = new NativeSplineGeometry342(native.Degree, native.Rational, native.Periodic, native.Closed, spline.IsPlanar, hasFit,
            controls.ToArray(), knotValues.ToArray(), weightValues.ToArray(), Number(native.ControlPointTolerance), Number(native.KnotTolerance),
            Number(spline.StartParam), Number(spline.EndParam), Point(spline.StartPoint), Point(spline.EndPoint), inspection);
        NativeCurveRead342.RequireFinite(result, cancel); budget.Finish(result); return result;
    }

    internal static JsonElement Shape(Curve curve, Action cancel, NativeSplineGeometry342? observedSpline = null)
    {
        cancel(); if (!Supported(curve)) throw new DomainException("UNSUPPORTED_ENTITY_TYPE", "Native curve inspection supports exact Line, Arc, Circle, Polyline, simple Polyline3d, Ellipse and Spline.");
        var budget = new Budget(cancel, NativeCurveRead342.SnapshotByteBudget); object geometry;
        if (observedSpline != null && curve.GetType() != typeof(Spline))
            throw new DomainException("NATIVE_CURVE_READ_FAILED", "A sampled Spline definition cannot describe a different curve type.");
        if (curve is Spline spline) geometry = observedSpline ?? ReadSpline(spline, cancel, NativeCurveRead342.SnapshotByteBudget);
        else if (curve is Polyline3d spatial)geometry=CadSpatialCurveCreator441.ReadPolyline3d(spatial,cancel);
        else if (curve is Polyline polyline)
        {
            int count = polyline.NumberOfVertices; Count(count); var vertices = new List<object>();
            for (int i = 0; i < count; i++)
            {
                cancel(); var ocs = polyline.GetPoint2dAt(i);
                var vertex = new { point = Point(polyline.GetPoint3dAt(i)), point_ocs = new[] { Number(ocs.X), Number(ocs.Y) },
                    bulge = Number(polyline.GetBulgeAt(i)), start_width = Number(polyline.GetStartWidthAt(i)), end_width = Number(polyline.GetEndWidthAt(i)) };
                budget.Add(vertex); vertices.Add(vertex);
            }
            geometry = new { vertices, normal = Vector(polyline.Normal), elevation = Number(polyline.Elevation), thickness = Number(polyline.Thickness), constant_width = Number(polyline.ConstantWidth) };
        }
        else if (curve is Ellipse ellipse) geometry = new { center = Point(ellipse.Center), major_axis = Vector(ellipse.MajorAxis), minor_axis = Vector(ellipse.MinorAxis),
            major_radius = Number(ellipse.MajorRadius), minor_radius = Number(ellipse.MinorRadius), radius_ratio = Number(ellipse.RadiusRatio), normal = Vector(ellipse.Normal) };
        else if (curve is Arc arc) geometry = new { center = Point(arc.Center), radius = Number(arc.Radius), normal = Vector(arc.Normal),
            start_angle_ocs = Number(arc.StartAngle), end_angle_ocs = Number(arc.EndAngle), sweep_angle = Number(arc.TotalAngle), thickness = Number(arc.Thickness) };
        else if (curve is Circle circle) geometry = new { center = Point(circle.Center), radius = Number(circle.Radius), normal = Vector(circle.Normal), thickness = Number(circle.Thickness) };
        else geometry = new { start = Point(curve.StartPoint), end = Point(curve.EndPoint), normal = Vector(((Line)curve).Normal), thickness = Number(((Line)curve).Thickness) };
        cancel(); var plane = ObservePlane(curve, cancel); double start = Number(curve.StartParam), end = Number(curve.EndParam);
        double? length = null; string? lengthReason = null;
        try { cancel(); length = Number(curve.GetDistanceAtParameter(end) - curve.GetDistanceAtParameter(start)); cancel(); }
        catch (Teigha.Runtime.Exception error) { cancel(); lengthReason = "NATIVE_LENGTH_UNAVAILABLE: " + error.ErrorStatus; }
        catch (DomainException error) when (error.Error.code == "NATIVE_CURVE_READ_FAILED")
        { cancel(); lengthReason = "NATIVE_LENGTH_UNAVAILABLE: " + error.Message; }
        var result = new { runtime_type = curve.GetType().FullName, closed = curve.Closed, planar = curve.IsPlanar,
            start_parameter = start, end_parameter = end, start_point = Point(curve.StartPoint), end_point = Point(curve.EndPoint),
            length, length_unavailable_reason = lengthReason, plane = plane.plane, plane_unavailable_reason = plane.unavailable_reason, geometry };
        budget.Finish(result); cancel(); return Wire.Element(result);
    }

    internal static NativeCurveObservation342 ReadProperties(Curve curve, Action cancel, NativeSplineGeometry342? observedSpline = null)
    {
        // Public Detail can reuse its already captured NURBS/fit arrays. The
        // writer omits this argument and takes its own fresh complete snapshot.
        return ReadPropertiesFromShape(Shape(curve,cancel,observedSpline),cancel);
    }

    internal static NativeCurveObservation342 ReadPropertiesFromShape(JsonElement shape,Action cancel)
    {
        static double[] PointValue(JsonElement value) => value.EnumerateArray().Select(n => n.GetDouble()).ToArray();
        var rawPlane = shape.GetProperty("plane");
        var plane = rawPlane.ValueKind == JsonValueKind.Null ? null : new NativeCurvePlane342(
            PointValue(rawPlane.GetProperty("point")), PointValue(rawPlane.GetProperty("normal")));
        var rawLength = shape.GetProperty("length");
        var value = new NativeCurveObservation342(NativeCurveRead342.Token(shape, cancel), shape.GetProperty("runtime_type").GetString()!,
            shape.GetProperty("start_parameter").GetDouble(), shape.GetProperty("end_parameter").GetDouble(),
            shape.GetProperty("closed").GetBoolean(), shape.GetProperty("planar").GetBoolean(),
            PointValue(shape.GetProperty("start_point")), PointValue(shape.GetProperty("end_point")),
            rawLength.ValueKind == JsonValueKind.Null ? null : rawLength.GetDouble(), shape.GetProperty("length_unavailable_reason").GetString(),
            plane, shape.GetProperty("plane_unavailable_reason").GetString(),
            "Token covers the complete reported native shape in this read; ownership, fields and external association graphs are separate. Parameter values are native parameters, not drawing lengths.");
        NativeCurveRead342.CheckSerializedBudget(value, NativeCurveRead342.PublicGeometryByteBudget, cancel); return value;
    }
}
