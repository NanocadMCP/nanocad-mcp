namespace NanoCAD.Contracts;

// SubentId and PointRefOnObject are separate SDK observations. A decoded native
// selector does not require an optional raw pick. Generic Spline selectors may
// instead carry a real finite endpoint pick; only unique WCS evidence admits it.
public static class NativeCurvePointBinding445
{
    public static string? Resolve(bool hasSubentity,string? subtype,bool spline,Point3? reportedPoint,
        IReadOnlyDictionary<string,Point3> actualPoints)
    {
        static bool Finite(Point3 p)=>double.IsFinite(p.X)&&double.IsFinite(p.Y)&&double.IsFinite(p.Z);
        static bool Near(double a,double b)=>double.IsFinite(a)&&double.IsFinite(b)&&Math.Abs(a-b)<=1e-8*Math.Max(1,Math.Max(Math.Abs(a),Math.Abs(b)));
        if(hasSubentity)
        {
            string? decoded=subtype switch{"Start"=>"start","End"=>"end","Center"=>"center",_=>null};
            if(decoded!=null)return actualPoints.TryGetValue(decoded,out var actual)&&Finite(actual)?decoded:null;
            if(!spline||subtype is not ("Unknown" or "Curve" or "Point"))return null;
        }
        if(reportedPoint==null||!Finite(reportedPoint))return null;
        var roles=hasSubentity?new[]{"start","end"}:new[]{"start","end","center"};
        var matches=roles.Where(role=>actualPoints.TryGetValue(role,out var point)&&Finite(point)&&
            Near(point.X,reportedPoint.X)&&Near(point.Y,reportedPoint.Y)&&Near(point.Z,reportedPoint.Z)).ToArray();
        return matches.Length==1?matches[0]:null;
    }
}
