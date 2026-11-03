using NanoCAD.Contracts;
using Teigha.DatabaseServices;
using Teigha.Geometry;

namespace NanoCADBridge;

// SDK Editor.TraceBoundary returns detached objects. Keep native curved data;
// classify closed loops using temporary Region/Brep instead of sampled polygons.
internal static class CadBoundaryTrace408
{
    private sealed record Loop(int Index,Entity Entity,Region? Region,Point3d? Witness,object Geometry,object? CreateSpec,string? Unavailable,string? SeedContainment);
    private static double Number(double n)=>double.IsFinite(n)?n:throw new DomainException("BOUNDARY_GEOMETRY_UNAVAILABLE","Native boundary contains nonfinite geometry.");
    private static double[] Point(Point3d p)=>[Number(p.X),Number(p.Y),Number(p.Z)];
    private static double[] Vector(Vector3d v)=>[Number(v.X),Number(v.Y),Number(v.Z)];
    private static string Containment(Region region,Point3d point,Action cancel)
    {
        using var brep=new Teigha.BoundaryRepresentation.Brep(region);
        string? result=null;
        foreach(Teigha.BoundaryRepresentation.Face face in brep.Faces)
        using(face)
        {
            cancel();using var container=face.GetPointContainment(point,out var native);
            var value=PointContainment.Classify(native.ToString(),container==null||container.IsNull);
            if(value is "inside" or "boundary")return value;result=value;
        }
        return result??"unavailable";
    }
    private static Region? DetachedRegion(Entity entity,Action cancel)
    {
        if(entity is Region original)return (Region)original.Clone();
        if(entity is not Curve curve||!curve.Closed)return null;
        using var copy=(Curve)curve.Clone();using var inputs=new DBObjectCollection();inputs.Add(copy);
        using var regions=Region.CreateFromCurves(inputs);
        Region? result=null;
        try
        {
            cancel();
            if(regions.Count!=1||regions[0] is not Region region)throw new DomainException("BOUNDARY_GEOMETRY_UNAVAILABLE","One closed native boundary did not form a single temporary Region.");
            result=(Region)region.Clone();return result;
        }
        finally {foreach(DBObject region in regions)region.Dispose();}
    }
    private static (object Geometry,object? CreateSpec,string? Unavailable) Geometry(Entity entity,Action cancel)
    {
        if(entity is Polyline polyline)
        {
            var matrix=Matrix3d.PlaneToWorld(polyline.Normal);
            var origin=new Point3d(0,0,polyline.Elevation).TransformBy(matrix);
            var frame=new{origin=Point(origin),x_axis=Vector(Vector3d.XAxis.TransformBy(matrix)),y_axis=Vector(Vector3d.YAxis.TransformBy(matrix))};
            var vertices=new List<object>();var createVertices=new List<object>();var capacity=new SerializedCapacity357(Wire.PayloadBudget-8192,cancel);
            for(int i=0;i<polyline.NumberOfVertices;i++)
            {
                cancel();var local=polyline.GetPoint2dAt(i);var bulge=Number(polyline.GetBulgeAt(i));
                var vertex=new{point_wcs=Point(polyline.GetPoint3dAt(i)),point_ocs=new[]{Number(local.X),Number(local.Y)},bulge,start_width=Number(polyline.GetStartWidthAt(i)),end_width=Number(polyline.GetEndWidthAt(i))};
                capacity.Add(vertex);vertices.Add(vertex);createVertices.Add(new{point=new[]{local.X,local.Y},bulge});
            }
            object geometry=new{closed=polyline.Closed,normal=Vector(polyline.Normal),elevation=Number(polyline.Elevation),vertices=vertices.ToArray()};
            object? spec=polyline.Closed&&vertices.Count>=3?new{type="Polyline",vertices=createVertices.ToArray(),closed=true,workplane=frame}:null;
            return(geometry,spec,spec==null?"Closed native boundary does not fit the existing Polyline create grammar; geometry is retained without faceting.":null);
        }
        if(entity is Circle circle)
        {
            var matrix=Matrix3d.PlaneToWorld(circle.Normal);
            var frame=new{origin=Point(circle.Center),x_axis=Vector(Vector3d.XAxis.TransformBy(matrix)),y_axis=Vector(Vector3d.YAxis.TransformBy(matrix))};
            return(new{center=Point(circle.Center),normal=Vector(circle.Normal),radius=Number(circle.Radius)},new{type="Circle",center=new[]{0d,0d},radius=Number(circle.Radius),workplane=frame},null);
        }
        if(entity is Region region)return(CadRegionBooleanEditor.Read(region,cancel),null,"Region is observed as native boundary data; no coordinate recreation or automatic source association is promised.");
        if(entity is Spline spline)return(CadSplineReader.ReadReported(spline,cancel),null,"Native Spline boundary is retained in the read result; choose an applicable exact creation or explicit fitting representation.");
        if(entity is Ellipse ellipse)return(CadEllipseReader.Read(ellipse,cancel),null,"Native Ellipse boundary is retained; no implicit conversion is performed.");
        return(new{runtime_type=entity.GetType().FullName},null,"No geometry serializer for this native boundary kind; no sampled substitute returned.");
    }
    internal static object Read(Arguments read,BoundaryTrace408 args,DocumentState state,Transaction transaction,Action cancel)
    {
        var db=state.Document.Database;CadWriteSpace.Resolve(db,transaction,read.ReadSpace);cancel();
        var seed=new Point3d(args.Point[0],args.Point[1],args.Point[2]);
        var currentUcs=state.Document.Editor.CurrentUserCoordinateSystem;
        var nativeSeed=seed.TransformBy(currentUcs.Inverse());
        using var boundaries=state.Document.Editor.TraceBoundary(nativeSeed,args.DetectIslands);
        var loops=new List<Loop>();
        try
        {
            for(int i=0;i<boundaries.Count;i++)
            {
                cancel();if(boundaries[i] is not Entity entity||!entity.ObjectId.IsNull)
                    throw new DomainException("NATIVE_BOUNDARY_MISMATCH","TraceBoundary did not return detached native entities.");
                var data=Geometry(entity,cancel);
                if(data.CreateSpec!=null)data=CreationContext(data,entity,read.ReadSpace,db);
                var region=DetachedRegion(entity,cancel);
                try
                {
                    var classification=region==null?null:Containment(region,seed,cancel);
                    Point3d? witness=entity is Curve curve?curve.StartPoint:null;
                    loops.Add(new(i,entity,region,witness,data.Geometry,data.CreateSpec,data.Unavailable,classification));region=null;
                }
                finally{region?.Dispose();}
            }
            var output=new List<object>();var budget=new SerializedCapacity357(Wire.PayloadBudget-8192,cancel);
            foreach(var loop in loops)
            {
                int depth=0;bool classified=loop.Region!=null&&loop.Witness!=null;var enclosing=new List<int>();
                if(classified)
                foreach(var other in loops)
                {
                    cancel();if(other.Index==loop.Index)continue;
                    if(other.Region==null){classified=false;continue;}
                    var containment=Containment(other.Region,loop.Witness!.Value,cancel);
                    if(containment=="inside"){depth++;enclosing.Add(other.Index);}
                    else if(containment is "boundary" or "unavailable")classified=false;
                }
                string role=classified?(depth%2==0?(depth==0?"outer":"island"):"hole"):"unclassified";
                var item=new{index=loop.Index,native_type=loop.Entity.GetType().Name,role,classification_available=classified,nesting_depth=classified?(int?)depth:null,enclosing_indices=enclosing.ToArray(),seed_containment=loop.SeedContainment,geometry=loop.Geometry,create_spec=loop.CreateSpec,create_unavailable_reason=loop.Unavailable,source_handles_available=false,associative=false};
                budget.Add(item);output.Add(item);
            }
            cancel();return new{mode="trace_boundary",coordinate_system="WCS",units=new{name=db.Insunits.ToString(),value=(int)db.Insunits},solver="native_editor_trace_boundary_and_region_brep",point=args.Point,native_seed_ucs=Point(nativeSeed),detect_islands=args.DetectIslands,found=loops.Any(loop=>loop.SeedContainment is "inside" or "boundary"),boundaries=output.ToArray(),result_scope="detached_native_boundaries_in_current_active_editor_context",associative=false,warning="Native discovery can depend on the visible drawing area and current editor plane. No source handles or associations are inferred. Review unclassified/touching loops before using them as outer boundaries or holes; create_spec supplies geometry, not ownership links."};
        }
        finally
        {
            foreach(var loop in loops)loop.Region?.Dispose();
            foreach(DBObject item in boundaries)item.Dispose();
        }
    }
    private static (object Geometry,object? CreateSpec,string? Unavailable) CreationContext((object Geometry,object? CreateSpec,string? Unavailable) data,Entity entity,string space,Database database)
    {
        if((int)database.Insunits!=4)return(data.Geometry,null,"Native boundary is available in drawing units; create_entities requires an explicit Millimeters drawing, so no create-ready spec is claimed.");
        try
        {
            var candidate=Wire.Element(data.CreateSpec!);
            if(space=="paper")
            {
                bool flat=entity is Polyline pl&&pl.Normal.IsEqualTo(Vector3d.ZAxis)&&pl.Elevation==0||entity is Circle c&&c.Normal.IsEqualTo(Vector3d.ZAxis)&&c.Center.Z==0;
                if(!flat)return(data.Geometry,null,"Native boundary is available; current Paper creation supports unframed sheet-plane geometry only.");
                var fields=candidate.EnumerateObject().Where(property=>property.Name!="workplane").ToDictionary(property=>property.Name,property=>(object)property.Value.Clone(),StringComparer.Ordinal);
                if(entity is Circle circle)fields["center"]=new[]{circle.Center.X,circle.Center.Y};
                candidate=Wire.Element(fields);
            }
            var specs=CreateValidation.ParseEntities(Wire.Element(new[]{candidate}),allowDimensions:true);
            WriteSpace.Check(new CreateArguments("00000000-0000-0000-0000-000000000001","00000000-0000-0000-0000-000000000002","0","00000000-0000-0000-0000-000000000003","Millimeters",specs){TargetSpace=space});
            return(data.Geometry,candidate,null);
        }
        catch(DomainException error){return(data.Geometry,null,"Native boundary is available; create grammar/context is not applicable: "+error.Error.message);}
    }
}
