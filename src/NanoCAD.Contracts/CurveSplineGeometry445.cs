namespace NanoCAD.Contracts;

// Exact observed WCS definition shared by native sketch and ordinary drawing
// constraints. Nullable fields represent getters absent from an SDK surface;
// they are never populated with guessed tolerances or parameterization.
public sealed record CurveSplineFit445(int degree,double[][] points,double fit_tolerance,bool tangents_exist,
    double[]? start_tangent,double[]? end_tangent,string? knot_parameterization);
public sealed record CurveSplineGeometry445(int degree,bool rational,bool periodic,bool closed,bool planar,bool has_fit_data,
    double[][] control_points,double[] knots,double[] weights,double? control_point_tolerance,double knot_tolerance,
    double start_parameter,double end_parameter,double[] start_point,double[] end_point,
    CurveSplineFit445? fit_data,string? fit_data_unavailable_reason,string observation_scope,
    NativeSplineEntityObservation342? entity_definition=null)
{
    public static CurveSplineGeometry445 FromNative(NativeSplineGeometry342 value)=>new(value.degree,value.rational,value.periodic,value.closed,value.planar,value.has_fit_data,
        value.control_points,value.knots,value.weights,value.control_point_tolerance,value.knot_tolerance,value.start_parameter,value.end_parameter,value.start_point,value.end_point,
        value.native_inspection.fit_data is {} fit?new(fit.degree,fit.points,fit.fit_tolerance,fit.tangents_exist,fit.start_tangent,fit.end_tangent,fit.knot_parameterization):null,
        value.native_inspection.fit_data_unavailable_reason,"native_DbSpline_reported_definition_and_entity_flags",value.native_inspection.entity_definition);
}
