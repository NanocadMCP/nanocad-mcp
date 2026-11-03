using System.Text.Json;
using System.Security.Cryptography;

namespace NanoCAD.Contracts;

public sealed record NativeCurvePlane342(double[] point, double[] normal);
public sealed record NativeCurvePlaneObservation342(NativeCurvePlane342? plane, string? unavailable_reason);
public sealed record NativeCurveObservation342(string curve_token, string runtime_type, double start_parameter, double end_parameter,
    bool closed, bool planar, double[] start_point, double[] end_point, double? length, string? length_unavailable_reason,
    NativeCurvePlane342? plane, string? plane_unavailable_reason, string inspection_scope);
public sealed record NativeSplineFit342(int degree, double[][] points, double fit_tolerance,
    bool tangents_exist, double[]? start_tangent, double[]? end_tangent, string knot_parameterization);
public sealed record NativeSplineEntityObservation342(int degree, int control_point_count, bool rational, bool closed);
public sealed record NativeSplineInspection342(string scope, NativeCurvePlane342? plane, string? plane_unavailable_reason,
    NativeSplineFit342? fit_data, string? fit_data_unavailable_reason, string fit_constraints_scope,
    string edit_support_scope, NativeSplineEntityObservation342 entity_definition);

// The original public NURBS field names remain. This is observed native state,
// not the input specification/admission policy for creating or editing a spline.
public sealed record NativeSplineGeometry342(int degree, bool rational, bool periodic, bool closed, bool planar, bool has_fit_data,
    double[][] control_points, double[] knots, double[] weights, double control_point_tolerance, double knot_tolerance,
    double start_parameter, double end_parameter, double[] start_point, double[] end_point,
    NativeSplineInspection342 native_inspection);

public static class NativeCurveRead342
{
    public const int SnapshotByteBudget = 8 * 1024 * 1024;
    public const int PublicGeometryByteBudget = Wire.PayloadBudget - 8192;

    public static void RequireFinite(NativeSplineGeometry342 value, Action? cancel = null)
    {
        void Check() => cancel?.Invoke();
        void Number(double n) { Check(); if (!double.IsFinite(n)) throw new DomainException("NATIVE_CURVE_READ_FAILED", "Native curve contains a nonfinite reported value."); }
        void Point(double[] point) { Check(); if (point.Length != 3) throw new DomainException("NATIVE_CURVE_READ_FAILED", "Native curve point/vector has no complete XYZ value."); foreach (double n in point) Number(n); }
        foreach (var p in value.control_points) Point(p);
        foreach (double n in value.knots) Number(n);
        foreach (double n in value.weights) Number(n);
        Number(value.control_point_tolerance); Number(value.knot_tolerance);
        Number(value.start_parameter); Number(value.end_parameter); Point(value.start_point); Point(value.end_point);
        if (value.native_inspection.plane is { } plane) { Point(plane.point); Point(plane.normal); }
        if (value.native_inspection.fit_data is { } fit)
        {
            foreach (var p in fit.points) Point(p); Number(fit.fit_tolerance);
            if (fit.start_tangent is { } start) Point(start);
            if (fit.end_tangent is { } end) Point(end);
        }
        // No degree/fit/periodic/control-count/clamped-domain relation belongs
        // to this read adapter. The legacy edit/analysis validators are separate.
        Check();
    }

    public static int CheckSerializedBudget(object value, int maxBytes, Action? cancel = null)
    {
        cancel?.Invoke(); int bytes = JsonSerializer.SerializeToUtf8Bytes(value, Wire.Json).Length; cancel?.Invoke();
        if (bytes > maxBytes) throw new DomainException("RESPONSE_LIMIT_EXCEEDED", "Complete native curve data exceeds the actual serialized-byte budget; no controls, knots, weights or fit data were truncated.");
        return bytes;
    }

    public static string Token(JsonElement shape, Action? cancel = null)
    {
        cancel?.Invoke(); var bytes = JsonSerializer.SerializeToUtf8Bytes(shape, Wire.Json); cancel?.Invoke();
        if (bytes.Length > SnapshotByteBudget) throw new DomainException("RESPONSE_LIMIT_EXCEEDED", "Complete native curve snapshot exceeds the actual serialized-byte budget.");
        string token = Convert.ToHexString(SHA256.HashData(bytes)); cancel?.Invoke(); return token;
    }
}
