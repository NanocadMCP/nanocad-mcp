using System.Text.Json;

namespace NanoCAD.Contracts;

public sealed record SplineSpec(int Degree,Point2[] ControlPoints,double[] Knots,double[] Weights,string LayerName,bool Closed=false) : EntitySpec("Spline",LayerName);

public static partial class CreateValidation
{
    internal static SplineSpec ParseSpline(JsonElement item,string path,string layer)
    {
        Shape(item,path,"type","layer","degree","control_points","knots","weights","closed");
        bool closed=Boolean(item,"closed",path,false);
        double degreeNumber=Number(Required(item,"degree",path),path+".degree",1,5);
        if(degreeNumber!=Math.Truncate(degreeNumber))throw Invalid(path+".degree","Spline degree must be an integer.");
        int degree=(int)degreeNumber;
        var array=Required(item,"control_points",path);
        if(array.ValueKind!=JsonValueKind.Array||array.GetArrayLength()<degree+1)
            throw Invalid(path+".control_points","Expected at least degree+1 XY control points.","INVALID_GEOMETRY");
        var points=new Point2[array.GetArrayLength()];
        for(int i=0;i<points.Length;i++)
        {
            var point=array[i];string pointPath=path+$".control_points[{i}]";
            if(point.ValueKind!=JsonValueKind.Array||point.GetArrayLength()!=2)throw Invalid(pointPath,"Expected XY pair.","INVALID_GEOMETRY");
            points[i]=new(Number(point[0],pointPath+"[0]"),Number(point[1],pointPath+"[1]"));
        }
        bool TooClose(Point2 a,Point2 b)=>Math.Pow(a.X-b.X,2)+Math.Pow(a.Y-b.Y,2)<1e-12;
        if(points.Zip(points.Skip(1),TooClose).Any(v=>v)||
            (closed ? points.Length<4||points[0]!=points[^1] : TooClose(points[0],points[^1])))
            throw Invalid(path+".control_points","Neighboring controls must be at least1e-6 mm apart. Closed curves require at least4 controls and exactly repeated first/last control; open endpoints must be separated.","INVALID_GEOMETRY");
        var knotArray=Required(item,"knots",path);
        if(knotArray.ValueKind!=JsonValueKind.Array||knotArray.GetArrayLength()!=points.Length+degree+1)
            throw Invalid(path+".knots","Knot count must equal control count+degree+1.","INVALID_GEOMETRY");
        var knots=knotArray.EnumerateArray().Select((v,i)=>Number(v,path+$".knots[{i}]",0,1)).ToArray();
        if(!knots.Take(degree+1).All(v=>v==0)||!knots.Skip(points.Length).All(v=>v==1))
            throw Invalid(path+".knots","Clamped knot vector must have degree+1 endpoint knots at0 and1.","INVALID_GEOMETRY");
        int repeated=0;
        for(int i=degree+1;i<points.Length;i++)
        {
            if(knots[i]<=0||knots[i]>=1||knots[i]<knots[i-1])throw Invalid(path+".knots","Internal knots must be ordered and strictly within0..1.","INVALID_GEOMETRY");
            repeated=knots[i]==knots[i-1]?repeated+1:1;
            if(repeated>degree)throw Invalid(path+".knots","Internal knot multiplicity exceeds degree.","INVALID_GEOMETRY");
        }
        for(int i=1;i<knots.Length;i++)
            if(knots[i]!=knots[i-1]&&knots[i]-knots[i-1]<1e-6)throw Invalid(path+".knots","Distinct knots must be separated by at least1e-6.","INVALID_GEOMETRY");
        var weightArray=Required(item,"weights",path);
        if(weightArray.ValueKind!=JsonValueKind.Array||weightArray.GetArrayLength()!=0&&weightArray.GetArrayLength()!=points.Length)
            throw Invalid(path+".weights","Use empty weights for nonrational, otherwise one weight per control point.","INVALID_GEOMETRY");
        var weights=weightArray.EnumerateArray().Select((v,i)=>Number(v,path+$".weights[{i}]",.01,100)).ToArray();
        return new(degree,points,knots,weights,layer,closed);
    }
}

public static class SplineCreation
{
    public static void RequireMatch(SplineSpec spec,SplineGeometry actual,double localPlaneTolerance=0)
    {
        SplineVerification.Verify(actual);
        bool Near(double a,double b)=>Math.Abs(a-b)<=1e-7;
        bool Values(double[] a,double[] b)=>a.Length==b.Length&&a.Zip(b,Near).All(v=>v);
        bool Point(double[] actualPoint,Point2 desired)=>Values(actualPoint,new[]{desired.X,desired.Y,0});
        bool rational=spec.Weights.Length!=0;
        // Affine knot reparameterization and uniform rational-weight scaling
        // preserve the curve. Native storage may choose either representation.
        double domain=actual.end_parameter-actual.start_parameter;
        bool knots=domain>0&&actual.knots.Length==spec.Knots.Length&&
            actual.knots.Zip(spec.Knots,(stored,requested)=>Near((stored-actual.start_parameter)/domain,requested)).All(v=>v);
        bool weights=!rational||actual.weights.Length==spec.Weights.Length&&
            actual.weights.Zip(spec.Weights,(stored,requested)=>Near(stored/actual.weights[0],requested/spec.Weights[0])).All(v=>v);
        // Closed clamped curves may report NurbsData.Periodic=true; preserve that
        // native flag while requiring the requested clamped geometric curve.
        bool inPlane=localPlaneTolerance==0?SplineVerification.LiesInWcsXy(actual):actual.control_points.All(p=>Math.Abs(p[2])<=localPlaneTolerance);
        if(actual.closed!=spec.Closed||actual.periodic&&!spec.Closed||actual.degree!=spec.Degree||actual.rational!=rational||!actual.planar||!inPlane||
            actual.control_points.Length!=spec.ControlPoints.Length||!actual.control_points.Zip(spec.ControlPoints,Point).All(v=>v)||
            !knots||!weights||
            !Point(actual.start_point,spec.ControlPoints[0])||!Point(actual.end_point,spec.ControlPoints[^1]))
            throw new DomainException("NATIVE_SPLINE_MISMATCH","Constructed native spline differs from requested controls, knots, weights, flags or domain.");
    }
}
