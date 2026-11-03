using NanoCAD.Contracts;
using Teigha.DatabaseServices;
using Teigha.Geometry;

namespace NanoCADBridge;

internal static class CadContourEditor
{
    internal static bool Handles(EditSpec edit)=>edit is TrimCurveSpec or ExtendCurveSpec or OffsetCurveSpec;
    private static Point2 Point(Point3d point)=>new(point.X,point.Y);
    private static Point3d Point(Point2 point)=>new(point.X,point.Y,0);
    internal static ContourCurve Capture(Entity entity,Action cancel)
    {
        var type=entity.GetType();
        if(type!=typeof(Line)&&type!=typeof(Arc)&&type!=typeof(Circle)&&type!=typeof(Polyline))
            throw new DomainException("UNSUPPORTED_ENTITY_TYPE","Contour operations use exact native Line/Arc/Circle or straight Polyline in WCS XY/Z0.");
        CadModifier.CheckGeometry(entity,cancel);cancel();
        ContourCurve geometry;
        if(entity is Line line)geometry=new("Line",Point(line.StartPoint),Point(line.EndPoint));
        else if(entity is Circle circle)geometry=new("Circle",Center:Point(circle.Center),Radius:circle.Radius);
        else if(entity is Arc arc)geometry=new("Arc",Center:Point(arc.Center),Radius:arc.Radius,
            StartAngle:ContourGeometry.Angle(arc.StartAngle),Sweep:ContourGeometry.Angle(arc.EndAngle-arc.StartAngle));
        else
        {
            var polyline=(Polyline)entity;
            if(polyline.NumberOfVertices<2)throw new DomainException("INVALID_GEOMETRY","Contour polylines require at least two vertices.");
            var points=new List<Point2>();var budget=new CadContourBudget344(cancel);
            for(int i=0;i<polyline.NumberOfVertices;i++)
            {
                cancel();
                if(polyline.GetBulgeAt(i)!=0||polyline.GetStartWidthAt(i)!=0||polyline.GetEndWidthAt(i)!=0||polyline.ConstantWidth!=0)
                    throw new DomainException("OUT_OF_SCOPE","Contour polyline boundaries/offsets require straight segments with zero widths.");
                var point=Point(polyline.GetPoint3dAt(i));budget.Add(point);points.Add(point);
            }
            geometry=new("Polyline",Vertices:points.ToArray(),Closed:polyline.Closed);
        }
        CadContourBudget344.Check(geometry,cancel);ContourGeometry.Require(geometry,cancel);return geometry;
    }
    internal static void CheckSource(Entity source,EditSpec edit,Action cancel,Transaction? tr=null)
    {
        Capture(source,cancel);
        if(edit is TrimCurveSpec)
        {
            using var dependencies=source.GetPersistentReactorIds();
            if(source.HasFields||dependencies.Count!=0)
                throw new DomainException("UNSUPPORTED_CURVE_DEPENDENCIES","Trim replaces the source with independent pieces; curves with fields or persistent dependencies need their owning adapter.");
            if(tr!=null)
            {
                CadIndependentCurve.Require(source,source.Database,tr,cancel);
                CadContourBudget344.Replacement(source,tr,cancel);
            }
        }
    }
    internal static ContourCurve[] Plan(Entity source,EditSpec edit,Func<string,Entity> boundary,Action cancel,Transaction? tr=null)
    {
        CheckSource(source,edit,cancel,tr);var geometry=Capture(source,cancel);
        var boundaries=new List<ContourCurve>();var budget=new CadContourBudget344(cancel);budget.Add(geometry);
        foreach(var handle in EditReferences.Boundaries(edit))
        {cancel();var captured=Capture(boundary(handle),cancel);budget.Add(captured);boundaries.Add(captured);}
        var result=edit switch
        {
            TrimCurveSpec trim=>ContourGeometry.Trim(geometry,boundaries.ToArray(),trim.PickPoint,cancel),
            ExtendCurveSpec extend=>new[]{ContourGeometry.Extend(geometry,boundaries.ToArray(),extend.End,cancel)},
            OffsetCurveSpec offset=>new[]{ContourGeometry.Offset(geometry,offset.Distance,offset.Side,cancel)},
            _=>throw new InvalidOperationException("Not a contour operation.")
        };
        CadContourBudget344.Check(result,cancel);return result;
    }
    internal static Entity Create(Entity source,ContourCurve geometry,Action cancel)
    {
        Entity entity=geometry.Kind switch
        {
            "Line"=>new Line(Point(geometry.Start!),Point(geometry.End!)),
            "Circle"=>new Circle(Point(geometry.Center!),Vector3d.ZAxis,geometry.Radius),
            "Arc"=>new Arc(Point(geometry.Center!),Vector3d.ZAxis,geometry.Radius,geometry.StartAngle,ContourGeometry.Angle(geometry.StartAngle+geometry.Sweep)),
            "Polyline"=>new Polyline(),
            _=>throw new DomainException("UNSUPPORTED_ENTITY_TYPE","Unexpected contour result type.")
        };
        try
        {
            // Copy display properties only: generated curves are independent and
            // must not inherit the source's fields, constraints or reactor graph.
            entity.SetPropertiesFrom(source);
            if(entity is Polyline polyline)
            {
                for(int i=0;i<geometry.Vertices!.Length;i++)
                {cancel();var point=geometry.Vertices[i];polyline.AddVertexAt(i,new Point2d(point.X,point.Y),0,0,0);}
                polyline.Closed=geometry.Closed;
            }
            Verify(entity,geometry,cancel);return entity;
        }
        catch(System.Exception primary)
        {try{entity.Dispose();}catch(System.Exception cleanup){throw new AggregateException("Contour construction and cleanup failed.",primary,cleanup);}throw;}
    }
    internal static void Extend(Entity source,ContourCurve geometry,Action cancel)
    {
        if(source is Line line){line.StartPoint=Point(geometry.Start!);line.EndPoint=Point(geometry.End!);}
        else if(source is Arc arc){arc.StartAngle=geometry.StartAngle;arc.EndAngle=ContourGeometry.Angle(geometry.StartAngle+geometry.Sweep);}
        else throw new DomainException("UNSUPPORTED_ENTITY_TYPE","Only native Line and Arc can be extended.");
        Verify(source,geometry,cancel);
    }
    internal static void Verify(Entity entity,ContourCurve expected,Action cancel)
    {
        var actual=Capture(entity,cancel);
        bool Number(double a,double b)=>Math.Abs(a-b)<=1e-7;
        bool Same(Point2? a,Point2? b)=>a!=null&&b!=null&&Number(a.X,b.X)&&Number(a.Y,b.Y);
        bool valid=actual.Kind==expected.Kind;
        if(expected.Kind=="Line")valid&=Same(actual.Start,expected.Start)&&Same(actual.End,expected.End)||Same(actual.Start,expected.End)&&Same(actual.End,expected.Start);
        else if(expected.Kind=="Polyline")
        {
            PolylineShape Shape(ContourCurve c)=>new(c.Closed,c.Vertices!.Select(p=>{cancel();return new PolylineShapeVertex(p,0,0,0);}).ToArray());
            valid&=PolylineTopologyGeometry.SameLocus(Shape(actual),Shape(expected),cancel);
        }
        else
        {
            valid&=Same(actual.Center,expected.Center)&&Number(actual.Radius,expected.Radius);
            if(expected.Kind=="Arc")valid&=Same(ContourGeometry.At(actual,0),ContourGeometry.At(expected,0))&&Same(ContourGeometry.At(actual,actual.Sweep),ContourGeometry.At(expected,expected.Sweep))&&Number(actual.Sweep,expected.Sweep);
        }
        if(!valid)throw new DomainException("NATIVE_GEOMETRY_MISMATCH","Native contour differs from the computed trim/extension/offset.");
    }
}
