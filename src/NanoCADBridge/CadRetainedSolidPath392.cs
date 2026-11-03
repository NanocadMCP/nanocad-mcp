using System.Text.Json;
using System.Text.Json.Nodes;
using NanoCAD.Contracts;
using Teigha.DatabaseServices;
using Teigha.Geometry;

namespace NanoCADBridge;

// Exact retained native paths for direct face modeling. This does not widen
// generic2D inspection/edit admission or rebuild a helix from coordinates.
internal static class CadRetainedSolidPath392
{
    internal static bool Supported(Curve path)
        =>CadNativeCurveInspection342.Supported(path)||path.GetType()==typeof(Helix);
    private static double Number(double value)=>double.IsFinite(value)?value:
        throw new DomainException("NATIVE_CURVE_READ_FAILED","Native path contains a nonfinite reported value.");
    private static double[] Point(Point3d value)=>new[]{Number(value.X),Number(value.Y),Number(value.Z)};
    private static double[] Vector(Vector3d value)=>new[]{Number(value.X),Number(value.Y),Number(value.Z)};
    private static void Count(int count)
    {if(count<0)throw new DomainException("NATIVE_CURVE_READ_FAILED","Native path reported a negative collection count.");}

    private static JsonElement Nurbs(Helix helix,Action cancel)
    {
        cancel();var native=helix.NurbsData;long bytes=2;
        void Add(object value)
        {
            cancel();bytes+=JsonSerializer.SerializeToUtf8Bytes(value,Wire.Json).LongLength+1;
            if(bytes>NativeCurveRead342.SnapshotByteBudget)
                throw new DomainException("RESPONSE_LIMIT_EXCEEDED","Complete native Helix arrays exceed the actual serialized-byte budget; no entries were truncated.");
        }
        // The inherited native getter supplies independent reported arrays.
        // Complete NURBS is defining evidence; optional fit construction
        // metadata is not required by either retained-path native call.
        using var points=native.GetControlPoints();Count(points.Count);
        var knots=native.GetKnots();var weights=native.GetWeights();Count(knots.Count);Count(weights.Count);
        var controls=new List<double[]>();var knotValues=new List<double>();var weightValues=new List<double>();
        for(int i=0;i<points.Count;i++){cancel();var point=Point(points[i]);Add(point);controls.Add(point);}
        for(int i=0;i<knots.Count;i++){cancel();double value=Number(knots[i]);Add(value);knotValues.Add(value);}
        for(int i=0;i<weights.Count;i++){cancel();double value=Number(weights[i]);Add(value);weightValues.Add(value);}
        cancel();var result=Wire.Element(new
        {
            degree=native.Degree,rational=native.Rational,periodic=native.Periodic,closed=native.Closed,
            control_points=controls.ToArray(),knots=knotValues.ToArray(),weights=weightValues.ToArray(),
            control_point_tolerance=Number(native.ControlPointTolerance),knot_tolerance=Number(native.KnotTolerance),
            start_parameter=Number(helix.StartParam),end_parameter=Number(helix.EndParam),
            start_point=Point(helix.StartPoint),end_point=Point(helix.EndPoint),
            reported_properties=new{degree=helix.Degree,control_point_count=helix.NumControlPoints,rational=helix.IsRational,closed=helix.Closed},
            scope="complete_reported_native_nurbs; optional fit construction metadata is not observed"
        });
        NativeCurveRead342.CheckSerializedBudget(result,NativeCurveRead342.SnapshotByteBudget,cancel);return result;
    }

    private static (bool? Planar,string? PlanarReason,NativeCurvePlane342? Plane,string? PlaneReason) Plane(Helix helix,Action cancel)
    {
        bool? planar=null;
        try{cancel();planar=helix.IsPlanar;cancel();}
        catch(Teigha.Runtime.Exception error)
        {cancel();string reason="NATIVE_PLANAR_UNAVAILABLE: "+error.ErrorStatus;return(null,reason,null,reason);}
        if(planar==false)return(false,null,null,"native_curve_is_non_planar");
        try
        {
            cancel();using var plane=helix.GetPlane();
            if(plane==null)return(planar,null,null,"native_plane_getter_returned_null");
            var observed=new NativeCurvePlane342(Point(plane.PointOnPlane),Vector(plane.Normal));
            cancel();return(planar,null,observed,null);
        }
        catch(Teigha.Runtime.Exception error){cancel();return(planar,null,null,"NATIVE_PLANE_UNAVAILABLE: "+error.ErrorStatus);}
        catch(DomainException error)when(error.Error.code=="NATIVE_CURVE_READ_FAILED")
        {cancel();return(planar,null,null,"NATIVE_PLANE_UNAVAILABLE: "+error.Message);}
    }

    internal static JsonElement Shape(Curve path,Action cancel)
    {
        if(path.GetType()!=typeof(Helix))return CadNativeCurveInspection342.Shape(path,cancel);
        cancel();var helix=(Helix)path;
        var geometry=JsonNode.Parse(Nurbs(helix,cancel).GetRawText())!.AsObject();
        geometry["helix"]=JsonNode.Parse(Wire.Element(new
        {
            axis_origin=Point(helix.GetAxisPoint()),axis_direction=Vector(helix.AxisVector),
            base_radius=Number(helix.BaseRadius),top_radius=Number(helix.TopRadius),
            height=Number(helix.Height),turns=Number(helix.Turns),
            right_handed=helix.Twist,constraint_mode=helix.Constrain.ToString()
        }).GetRawText());
        var plane=Plane(helix,cancel);double start=Number(path.StartParam),end=Number(path.EndParam);
        double? length=null;string? reason=null;
        try{cancel();length=Number(path.GetDistanceAtParameter(end)-path.GetDistanceAtParameter(start));cancel();}
        catch(Teigha.Runtime.Exception error){cancel();reason="NATIVE_LENGTH_UNAVAILABLE: "+error.ErrorStatus;}
        catch(DomainException error)when(error.Error.code=="NATIVE_CURVE_READ_FAILED")
        {cancel();reason="NATIVE_LENGTH_UNAVAILABLE: "+error.Message;}
        var result=Wire.Element(new{runtime_type=path.GetType().FullName,closed=path.Closed,planar=plane.Planar,
            planar_unavailable_reason=plane.PlanarReason,start_parameter=start,end_parameter=end,
            start_point=Point(path.StartPoint),end_point=Point(path.EndPoint),length,length_unavailable_reason=reason,
            plane=plane.Plane,plane_unavailable_reason=plane.PlaneReason,geometry});
        CadContourBudget344.Check(result,cancel);return result;
    }

    internal static JsonElement ReadHelix(Helix helix,Action cancel)
    {
        var shape=Shape(helix,cancel);var result=JsonNode.Parse(shape.GetRawText())!.AsObject();result.Remove("geometry");
        result["curve_token"]=NativeCurveRead342.Token(StableShape(shape),cancel);
        result["inspection_scope"]="Token covers complete reported native NURBS and Helix parameters; optional length/plane availability and fit construction metadata are excluded. Ownership, fields and external association graphs are separate. Parameter values are native parameters, not drawing lengths.";
        var observation=Wire.Element(result);NativeCurveRead342.CheckSerializedBudget(observation,NativeCurveRead342.PublicGeometryByteBudget,cancel);return observation;
    }

    internal static void RequireCurrentToken(Curve path,string? token,Action cancel)
    {
        if(token==null)
        {
            if(path.GetType()==typeof(Helix))
                throw new DomainException("PATH_TOKEN_REQUIRED","Read this retained native Helix with inspect_objects include_native_curve=true and supply geometry.native_curve.curve_token as path_token.");
            return; // Established native path payloads remain unchanged.
        }
        var shape=Shape(path,cancel);
        if(NativeCurveRead342.Token(path.GetType()==typeof(Helix)?StableShape(shape):shape,cancel)!=token)
            throw new DomainException("STALE_NATIVE_PATH","The native path shape changed; read its current geometry.native_curve.curve_token before face modeling.");
    }

    internal static JsonElement StableShape(Curve path,Action cancel)=>StableShape(Shape(path,cancel));
    private static JsonElement StableShape(JsonElement observed)
    {
        var shape=JsonNode.Parse(observed.GetRawText())!.AsObject();
        // Actual defining native geometry remains mandatory. Availability of
        // derived length/plane diagnostics does not gate retained native paths.
        foreach(string key in new[]{"length","length_unavailable_reason","plane","plane_unavailable_reason","planar","planar_unavailable_reason"})shape.Remove(key);
        if(shape["geometry"] is JsonObject geometry)
        {
            geometry.Remove("planar");
            if(geometry["native_inspection"] is JsonObject inspection)
            {inspection.Remove("plane");inspection.Remove("plane_unavailable_reason");}
        }
        return Wire.Element(shape);
    }
}