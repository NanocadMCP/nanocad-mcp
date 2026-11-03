using NanoCAD.Contracts;
using NanoCAD.Ipc;
using Teigha.DatabaseServices;
using Teigha.Geometry;

namespace NanoCADBridge;

internal static class CadContourAssemblyEditor
{
    internal static ContourSource Capture(Entity e,Action cancel,SketchPlaneSpec? workplane=null)
    {
        if(workplane!=null)
        {
            // Transform only a detached clone. Validate and plan in the user's
            // frame without changing the source, its OCS or the document UCS.
            using var local=CadContourBudget344.Detached(e);local.TransformBy(DrawingWorkplaneNative.Matrix(workplane).Inverse());
            return Capture(local,cancel);
        }
        CadModifier.CheckGeometry(e,cancel);
        Point2 P(Point3d p)=>new(p.X,p.Y);
        PolylineShape shape;
        if(e.GetType()==typeof(Line))
        {var l=(Line)e;shape=new(false,[new(P(l.StartPoint),0,0,0),new(P(l.EndPoint),0,0,0)]);}
        else if(e.GetType()==typeof(Arc))
        {var a=(Arc)e;shape=new(false,[new(P(a.StartPoint),Math.Tan(ContourGeometry.Angle(a.EndAngle-a.StartAngle)/4),0,0),new(P(a.EndPoint),0,0,0)]);}
        else if(e.GetType()==typeof(Polyline))
        {
            var p=(Polyline)e;var vertices=new List<PolylineShapeVertex>();var budget=new CadContourBudget344(cancel);
            if(p.NumberOfVertices<2)throw new DomainException("INVALID_GEOMETRY","Contour assembly requires at least two native Polyline vertices.");
            for(int i=0;i<p.NumberOfVertices;i++)
            {cancel();var value=new PolylineShapeVertex(P(p.GetPoint3dAt(i)),p.GetBulgeAt(i),p.GetStartWidthAt(i),p.GetEndWidthAt(i));budget.Add(value);vertices.Add(value);}
            shape=new(p.Closed,vertices.ToArray());
        }
        else throw new DomainException("UNSUPPORTED_ENTITY_TYPE","Contour assembly requires exact native Line/Arc/Polyline.");
        CadContourBudget344.Check(shape,cancel);PolylineTopologyGeometry.Validate(shape,cancel);return new(e.GetType().Name,shape);
    }
    internal static ContourSource[] Plan(ContourAssemblySpec edit,Entity[] sources,Action cancel,Transaction? tr=null)
    {
        var values=new List<ContourSource>();var budget=new CadContourBudget344(cancel);
        foreach(var source in sources)
        {
            cancel();if(edit.EraseSources&&tr!=null)CadContourBudget344.Replacement(source,tr,cancel);
            var value=Capture(source,cancel,edit.Workplane);budget.Add(value);values.Add(value);
        }
        var current=values.ToArray();CadContourBudget344.Check(current,cancel);
        for(int i=0;i<current.Length;i++)
        {cancel();if(current[i].Type!=edit.Sources[i].Type||!PolylineTopologyGeometry.SameLocus(current[i].Shape,edit.Sources[i].Shape,cancel))
                throw new DomainException("GEOMETRY_CHANGED","A complete expected_sources geometry differs from the ordered native source.");
        }
        var result=ContourAssemblyGeometry.Plan(edit with{Sources=current},cancel);CadContourBudget344.Check(result,cancel);return result;
    }
    internal static Entity Create(Entity properties,ContourSource geometry,Action cancel,SketchPlaneSpec? workplane=null)
    {
        var segments=ContourAssemblyGeometry.Segments(geometry.Shape,cancel);
        Point3d P(Point2 p)=>new(p.X,p.Y,0);
        Entity entity;
        if(geometry.Type=="Line")entity=new Line(P(segments.Single().Start),P(segments.Single().End));
        else if(geometry.Type=="Arc")
        {
            var s=segments.Single();var c=s.Center;var start=s.Bulge>0?s.Start:s.End;
            double angle=ContourGeometry.Angle(Math.Atan2(start.Y-c.Y,start.X-c.X));
            entity=new Arc(P(c),Vector3d.ZAxis,s.Radius,angle,angle+Math.Abs(s.Sweep));
        }
        else entity=new Polyline();
        try
        {
            entity.SetPropertiesFrom(properties);
            if(entity is Polyline p)
            {
                for(int i=0;i<geometry.Shape.Vertices.Length;i++){cancel();var v=geometry.Shape.Vertices[i];p.AddVertexAt(i,new(v.Position.X,v.Position.Y),v.Bulge,v.StartWidth,v.EndWidth);}
                p.Closed=geometry.Shape.Closed;
            }
            Verify(entity,geometry,cancel);
            if(workplane!=null){entity.TransformBy(DrawingWorkplaneNative.Matrix(workplane));Verify(entity,geometry,cancel,workplane);}
            return entity;
        }
        catch(System.Exception primary)
        {try{entity.Dispose();}catch(System.Exception cleanup){throw new AggregateException("Contour assembly construction and cleanup failed.",primary,cleanup);}throw;}
    }
    internal static void Verify(Entity entity,ContourSource expected,Action cancel,SketchPlaneSpec? workplane=null)
    {
        var shape=expected.Shape;
        // Native Arc stores CCW direction. A clockwise Polyline arc becomes an
        // equivalent CCW Arc, with the same locus/length and reversed endpoints.
        if(expected.Type=="Arc"&&shape.Vertices[0].Bulge<0)shape=PolylineTopologyGeometry.Reverse(shape,cancel);
        var actual=Capture(entity,cancel,workplane);
        if(actual.Type!=expected.Type||!PolylineTopologyGeometry.SameLocus(actual.Shape,shape,cancel))
            throw new DomainException("NATIVE_GEOMETRY_MISMATCH","Generated curve differs from the analytic assembly/split result.");
    }
    internal static void Preview(ContourAssemblySpec edit,Entity[] sources,Action cancel,Transaction? tr=null)
    {foreach(var geometry in Plan(edit,sources,cancel,tr)){using var entity=Create(sources[0],geometry,cancel,edit.Workplane);}}
    internal static ModifiedEntities Apply(ModifyArguments args,CadModifier.Resolved resolved,Transaction tr,List<Entity> owned,WriteJob job,List<string>? warnings=null)
    {
        var edit=(ContourAssemblySpec)args.Operations.Single();
        var sources=edit.Handles.Select(h=>(Entity)tr.GetObject(resolved.Sources[h],OpenMode.ForRead)).ToArray();
        var states=new List<CadContourBudget344.SourceState>();var stateBytes=new CadContourBudget344(job.CheckCancellation);
        foreach(var source in sources){job.CheckCancellation();var state=CadContourBudget344.Source(source,job.CheckCancellation);stateBytes.Add(state);states.Add(state);}
        var plan=Plan(edit,sources,job.CheckCancellation,tr);var created=new List<EntityRef>();
        EntityRef Ref(string h)=>new(args.BridgeId,args.DocumentId,h);
        var refs=edit.Handles.Select(Ref).ToArray();var projected=CadContourBudget344.Projected(args,plan.Length,job.CheckCancellation);
        CadContourBudget344.Output(new(args.OperationId,"committed",false,[],edit.EraseSources?refs:[],projected,[],"single_command",
            contour_assembly_results:[new(edit.Kind,refs,projected,edit.EraseSources)]),job.CheckCancellation);
        // Every constructed native root is registered before any later failure,
        // then the whole result exists before the first resident append/erase.
        var prepared=new List<Entity>();
        foreach(var geometry in plan)
        {job.CheckCancellation();var result=Create(sources[0],geometry,job.CheckCancellation,edit.Workplane);owned.Add(result);prepared.Add(result);}
        var model=(BlockTableRecord)tr.GetObject(resolved.Model,OpenMode.ForWrite);
        for(int i=0;i<plan.Length;i++)
        {
            job.CheckCancellation();var result=prepared[i];
            model.AppendEntity(result);tr.AddNewlyCreatedDBObject(result,true);Verify(result,plan[i],job.CheckCancellation,edit.Workplane);
            created.Add(Ref(Validation.NormalizeHandle(result.Handle.ToString())));
        }
        for(int i=0;i<sources.Length;i++){job.CheckCancellation();CadContourBudget344.VerifySource(sources[i],states[i],job.CheckCancellation);}
        if(edit.EraseSources)foreach(var source in sources){job.CheckCancellation();source.UpgradeOpen();source.Erase(true);}
        if(!edit.EraseSources)CadContourBudget344.RetentionWarning(states,warnings);
        return new(args.OperationId,"committed",false,[],edit.EraseSources?refs:[],created.ToArray(),[],"single_command",
            contour_assembly_results:[new(edit.Kind,refs,created.ToArray(),edit.EraseSources)]);
    }
}
