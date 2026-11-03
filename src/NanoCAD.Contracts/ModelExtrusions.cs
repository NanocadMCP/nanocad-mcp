using System.Text.Json;

namespace NanoCAD.Contracts;

public sealed record SketchPlaneSpec(Point3 Origin, Point3 XAxis, Point3 YAxis);
public sealed record ModelProfileSpec(string Kind, Point2? Center, double Radius, Point2[]? Vertices,
    [property:System.Text.Json.Serialization.JsonIgnore(Condition=System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)] double[]? Bulges=null);
public sealed record ModelExtrusionSpec(SketchPlaneSpec Plane, ModelProfileSpec Profile, double Distance, string Direction, string Name, string LayerName,
    [property:System.Text.Json.Serialization.JsonIgnore(Condition=System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingDefault)] bool EditableProfile=false,
    [property:System.Text.Json.Serialization.JsonIgnore(Condition=System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)] NativeSketchInitialization385? NativeGeometry=null,
    [property:System.Text.Json.Serialization.JsonIgnore(Condition=System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)] Point3? RegionPoint=null)
    : EntitySpec("Solid3d", LayerName);
public sealed record ExtrusionOracle(double Area, double Volume, Point3 Centroid, Point3 Min, Point3 Max, Point3 Normal);

public static partial class CreateValidation
{
    internal static EntitySpec ParseModelExtrusion(JsonElement item, string path, string layer,Action? cancel=null,bool allowNativeGeometry=false)
    {
        Shape(item,path,allowNativeGeometry
            ?["type","kind","layer","plane","profile","distance","direction","name","editable_profile","native_geometry","initial_constraints","region_point"]
            :["type","kind","layer","plane","profile","distance","direction","name","editable_profile"]);
        var plane=Required(item,"plane",path); string pp=path+".plane";
        Shape(plane,pp,"origin","x_axis","y_axis");
        Point3 Triple(JsonElement value,string field,string at)
        {
            var a=Required(value,field,at);
            if(a.ValueKind!=JsonValueKind.Array||a.GetArrayLength()!=3)throw Invalid(at+"."+field,"Expected WCS XYZ triple.");
            return new(Number(a[0],at,-1e6,1e6),Number(a[1],at,-1e6,1e6),Number(a[2],at,-1e6,1e6));
        }
        var frame=new SketchPlaneSpec(Triple(plane,"origin",pp),Triple(plane,"x_axis",pp),Triple(plane,"y_axis",pp));
        double Dot(Point3 a,Point3 b)=>a.X*b.X+a.Y*b.Y+a.Z*b.Z;
        if(Math.Abs(Dot(frame.XAxis,frame.XAxis)-1)>1e-10||Math.Abs(Dot(frame.YAxis,frame.YAxis)-1)>1e-10||Math.Abs(Dot(frame.XAxis,frame.YAxis))>1e-10)
            throw Invalid(pp,"Plane axes must be perpendicular unit vectors; no implicit normalization or scaling.","INVALID_GEOMETRY");
        if(item.TryGetProperty("native_geometry",out var native))
        {
            if(item.TryGetProperty("profile",out _)||item.TryGetProperty("editable_profile",out _))
                throw Invalid(path,"Use native_geometry or the legacy profile/editable_profile branch, not both.");
            var constraints=item.TryGetProperty("initial_constraints",out var initial)?initial:Wire.Element(Array.Empty<object>());
            var geometry=NativeSketchValidation385.Parse(native,constraints,frame,path+".native_geometry");
            var region=Triple(item,"region_point",path);DrawingWorkplane.Local(frame,region);
            string nativeDirection=Text(item,"direction",path,16);
            if(nativeDirection is not "positive" and not "negative")throw Invalid(path+".direction","Use positive or negative relative to plane x_axis cross y_axis.");
            return new ModelExtrusionSpec(frame,new("native_sketch",null,0,null),Number(Required(item,"distance",path),path+".distance",.001,1e6),nativeDirection,Text(item,"name",path,64,"Model"),layer,
                NativeGeometry:geometry,RegionPoint:region);
        }
        if(item.TryGetProperty("initial_constraints",out _)||item.TryGetProperty("region_point",out _))
            throw Invalid(path,"initial_constraints and region_point belong to the native_geometry branch.");
        var profile=Required(item,"profile",path); string sp=path+".profile";
        Shape(profile,sp,"kind","center","radius","vertices","bulges");string kind=Text(profile,"kind",sp,16);
        ModelProfileSpec shape;
        Point2[] ProfilePoints(int minimum)
        {
            var values=Required(profile,"vertices",sp);
            if(values.ValueKind!=JsonValueKind.Array||values.GetArrayLength()<minimum)
                throw Invalid(sp+".vertices",$"Expected at least {minimum} XY points.");
            var result=new List<Point2>();var budget=new ProfileGeometryBudget(cancel);
            foreach(var value in values.EnumerateArray())
            {
                cancel?.Invoke();
                if(value.ValueKind!=JsonValueKind.Array||value.GetArrayLength()!=2)throw Invalid(sp+".vertices","Expected XY point.");
                var point=new Point2(Number(value[0],sp+".vertices"),Number(value[1],sp+".vertices"));
                budget.Add(point);result.Add(point);
            }
            return result.ToArray();
        }
        if(kind=="circle")
        {
            Shape(profile,sp,"kind","center","radius");
            shape=new(kind,Point(profile,"center",sp),Number(Required(profile,"radius",sp),sp+".radius",.001,1e6),null);
        }
        else if(kind=="polygon")
        {
            Shape(profile,sp,"kind","vertices");
            var points=ProfilePoints(3);ValidateSimplePolygon(points,sp,cancel);
            shape=new(kind,null,0,points);
        }
        else if(kind=="polycurve")
        {
            Shape(profile,sp,"kind","vertices","bulges");var points=ProfilePoints(2);var values=Required(profile,"bulges",sp);
            if(values.ValueKind!=JsonValueKind.Array||values.GetArrayLength()!=points.Length)throw Invalid(sp,"Supply one bulge per vertex, including the closing edge.");
            var bulges=values.EnumerateArray().Select(v=>{cancel?.Invoke();return Number(v,sp+".bulges",-1000,1000);}).ToArray();shape=new(kind,null,0,points,bulges);
            DrawingArcContours.Measure(ModelPolycurve.Curves(shape,cancel),1,cancel);
        }
        else throw Invalid(sp+".kind","Use circle, polygon or exact closed Line/Arc polycurve, without holes.");
        ProfileGeometryBudget.Profile(shape,cancel);
        if((shape.Vertices??new[]{shape.Center!}).Any(p=>{cancel?.Invoke();return Math.Abs(p.X)>1e6||Math.Abs(p.Y)>1e6;}))
            throw Invalid(sp,"Local profile coordinates are limited to +/-1000000 mm.","INVALID_GEOMETRY");
        string direction=Text(item,"direction",path,16);
        if(direction is not "positive" and not "negative")throw Invalid(path+".direction","Use positive or negative relative to plane x_axis cross y_axis.");
        bool editable=false;if(item.TryGetProperty("editable_profile",out var ep)){if(ep.ValueKind is not JsonValueKind.True and not JsonValueKind.False)throw Invalid(path+".editable_profile","Expected boolean.");editable=ep.GetBoolean();}
        var spec=new ModelExtrusionSpec(frame,shape,Number(Required(item,"distance",path),path+".distance",.001,1e6),direction,Text(item,"name",path,64,"Model"),layer,editable);
        var oracle=ModelExtrusionGeometry.Measure(spec,cancel);
        if(!double.IsFinite(oracle.Volume)||oracle.Volume<=1e-12||new[]{oracle.Min.X,oracle.Min.Y,oracle.Min.Z,oracle.Max.X,oracle.Max.Y,oracle.Max.Z}.Any(v=>!double.IsFinite(v)||Math.Abs(v)>1e9))
            throw Invalid(path,"Extrusion exceeds finite geometry bounds.","INVALID_GEOMETRY");
        return spec;
    }
}

public static class ModelExtrusionGeometry
{
    public static Point3 World(SketchPlaneSpec plane,Point2 p)=>new(plane.Origin.X+plane.XAxis.X*p.X+plane.YAxis.X*p.Y,
        plane.Origin.Y+plane.XAxis.Y*p.X+plane.YAxis.Y*p.Y,plane.Origin.Z+plane.XAxis.Z*p.X+plane.YAxis.Z*p.Y);
    public static ExtrusionOracle Measure(ModelExtrusionSpec spec,Action? cancel=null)
    {
        ProfileGeometryBudget.Profile(spec.Profile,cancel);
        var u=spec.Plane.XAxis;var v=spec.Plane.YAxis;
        var n=new Point3(u.Y*v.Z-u.Z*v.Y,u.Z*v.X-u.X*v.Z,u.X*v.Y-u.Y*v.X);
        double area;Point2 center;Point3 low,high;
        if(spec.Profile.Kind=="circle")
        {
            area=Math.PI*spec.Profile.Radius*spec.Profile.Radius;center=spec.Profile.Center!;
            var w=World(spec.Plane,center);double r=spec.Profile.Radius;
            var radius=new Point3(r*Math.Sqrt(u.X*u.X+v.X*v.X),r*Math.Sqrt(u.Y*u.Y+v.Y*v.Y),r*Math.Sqrt(u.Z*u.Z+v.Z*v.Z));
            low=new(w.X-radius.X,w.Y-radius.Y,w.Z-radius.Z);high=new(w.X+radius.X,w.Y+radius.Y,w.Z+radius.Z);
        }
        else if(spec.Profile.Kind=="polycurve")
        {
            var curves=ModelPolycurve.Curves(spec.Profile,cancel);var local=DrawingArcContours.Measure(curves,1,cancel);area=local.Area;center=new(local.Centroid.X,local.Centroid.Y);
            (low,high)=ModelPolycurve.Bounds(curves,spec.Plane,cancel);
        }
        else
        {
            var p=spec.Profile.Vertices!;var anchor=p[0];double twiceArea=0,cx=0,cy=0;
            for(int i=0;i<p.Length;i++)
            {
                cancel?.Invoke();
                var a=p[i];var b=p[(i+1)%p.Length];double ax=a.X-anchor.X,ay=a.Y-anchor.Y,bx=b.X-anchor.X,by=b.Y-anchor.Y;
                double cross=ax*by-bx*ay;twiceArea+=cross;cx+=(ax+bx)*cross;cy+=(ay+by)*cross;
            }
            area=Math.Abs(twiceArea)*.5;center=new(anchor.X+cx/(3*twiceArea),anchor.Y+cy/(3*twiceArea));
            var world=p.Select(point=>{cancel?.Invoke();return World(spec.Plane,point);}).ToArray();
            low=new(world.Min(q=>q.X),world.Min(q=>q.Y),world.Min(q=>q.Z));high=new(world.Max(q=>q.X),world.Max(q=>q.Y),world.Max(q=>q.Z));
        }
        double distance=spec.Direction=="positive"?spec.Distance:-spec.Distance;
        var c=World(spec.Plane,center);var offset=new Point3(n.X*distance,n.Y*distance,n.Z*distance);
        return new(area,area*spec.Distance,new(c.X+offset.X*.5,c.Y+offset.Y*.5,c.Z+offset.Z*.5),
            new(low.X+Math.Min(0,offset.X),low.Y+Math.Min(0,offset.Y),low.Z+Math.Min(0,offset.Z)),
            new(high.X+Math.Max(0,offset.X),high.Y+Math.Max(0,offset.Y),high.Z+Math.Max(0,offset.Z)),n);
    }
}
