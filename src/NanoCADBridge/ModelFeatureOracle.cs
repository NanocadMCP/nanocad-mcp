using NanoCAD.Contracts;
using Teigha.DatabaseServices;
using Teigha.Geometry;

namespace NanoCADBridge;

internal static class ModelFeatureOracle
{
    internal sealed record Metrics(double Volume,Point3 Centroid,Point3 Min,Point3 Max);
    internal static Metrics Read(Solid3d solid)
    {
        if(solid.IsNull)throw new DomainException("EMPTY_SOLID_RESULT","Feature would leave an empty body.");
        var mass=solid.MassProperties;var box=solid.GeometricExtents;
        var values=new[]{mass.Volume,mass.Centroid.X,mass.Centroid.Y,mass.Centroid.Z,box.MinPoint.X,box.MinPoint.Y,box.MinPoint.Z,box.MaxPoint.X,box.MaxPoint.Y,box.MaxPoint.Z};
        if(!values.All(double.IsFinite)||mass.Volume<=1e-12)throw new DomainException("INVALID_GEOMETRY","Feature result has no finite positive mass/bounds.");
        return new(mass.Volume,new(mass.Centroid.X,mass.Centroid.Y,mass.Centroid.Z),new(box.MinPoint.X,box.MinPoint.Y,box.MinPoint.Z),new(box.MaxPoint.X,box.MaxPoint.Y,box.MaxPoint.Z));
    }
    internal static bool Same(Metrics a,Metrics b)
    {
        bool Number(double x,double y)=>Math.Abs(x-y)<=Math.Max(1e-6,Math.Max(Math.Abs(x),Math.Abs(y))*1e-8);
        bool Point(Point3 x,Point3 y)=>Number(x.X,y.X)&&Number(x.Y,y.Y)&&Number(x.Z,y.Z);
        return Number(a.Volume,b.Volume)&&Point(a.Centroid,b.Centroid)&&Point(a.Min,b.Min)&&Point(a.Max,b.Max);
    }
    internal static Solid3d Tool(ModelExtrusionSpec spec,Action cancel)
    {
        var normal=ModelExtrusionGeometry.Measure(spec,cancel).Normal;
        Point3d Point(Point3 p)=>new(p.X,p.Y,p.Z);Vector3d Vector(Point3 p)=>new(p.X,p.Y,p.Z);
        using Curve profile=spec.Profile.Kind=="circle"
            ?new Circle(Point(ModelExtrusionGeometry.World(spec.Plane,spec.Profile.Center!)),Vector(normal),spec.Profile.Radius)
            :new Polyline(spec.Profile.Vertices!.Length);
        if(profile is Polyline polygon)
        {
            for(int i=0;i<spec.Profile.Vertices!.Length;i++)
            {cancel();var point=spec.Profile.Vertices[i];polygon.AddVertexAt(i,new Point2d(point.X,point.Y),spec.Profile.Kind=="polycurve"?spec.Profile.Bulges![i]:0,0,0);}
            polygon.Closed=true;
            polygon.TransformBy(Matrix3d.AlignCoordinateSystem(Point3d.Origin,Vector3d.XAxis,Vector3d.YAxis,Vector3d.ZAxis,
                Point(spec.Plane.Origin),Vector(spec.Plane.XAxis),Vector(spec.Plane.YAxis),Vector(normal)));
        }
        using var options=new SweepOptions();var cutter=new Solid3d();
        try{cutter.RecordHistory=false;cancel();cutter.CreateExtrudedSolid(profile,Vector(normal)*(spec.Direction=="positive"?spec.Distance:-spec.Distance),options);return cutter;}
        catch{cutter.Dispose();throw;}
    }
    internal static void Apply(Solid3d detached,ModelExtrusionSpec spec,string operation,Action cancel)
    {
        if(!detached.ObjectId.IsNull)throw new DomainException("OUT_OF_SCOPE","Boolean oracle only accepts detached solids.");
        var before=Read(detached);using var cutter=Tool(spec,cancel);cancel();
        detached.BooleanOperation(operation=="cut"?BooleanOperationType.BoolSubtract:BooleanOperationType.BoolUnite,cutter);
        var expected=Read(detached);double threshold=Math.Max(1e-8,before.Volume*1e-10);
        if(operation=="cut"?expected.Volume>=before.Volume-threshold:expected.Volume<=before.Volume+threshold)
            throw new DomainException("NO_MATERIAL_CHANGE","The profile does not produce the requested nonempty added/removed material.");
    }
    internal static Metrics Expected(Solid3d source,ModelExtrusionSpec spec,string operation,Action cancel)
    {
        using var result=(Solid3d)source.Clone();result.RecordHistory=false;cancel();
        Apply(result,spec,operation,cancel);return Read(result);
    }
}
