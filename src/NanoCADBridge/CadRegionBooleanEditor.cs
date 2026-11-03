using System.Runtime.CompilerServices;
using System.Text.Json;
using NanoCAD.Contracts;
using Teigha.DatabaseServices;
using Teigha.Geometry;
using Brep=Teigha.BoundaryRepresentation.Brep;
using Face=Teigha.BoundaryRepresentation.Face;
using Edge=Teigha.BoundaryRepresentation.Edge;
using BoundaryLoop=Teigha.BoundaryRepresentation.BoundaryLoop;
using Vertex=Teigha.BoundaryRepresentation.Vertex;

namespace NanoCADBridge;

// Native Region bodies stay native. Only private detached operands are consumed.
// Area is readback, never a predicted Boolean result or preservation oracle.
internal static class CadRegionBooleanEditor
{
    internal sealed record Geometry(double Area,double Perimeter,RegionPlaneValue Plane,RegionTopologyValue Topology,JsonElement Shape);
    internal sealed record Outcome(double AreaBefore,double AreaAfter,RegionPlaneValue Plane,RegionTopologyValue TopologyAfter);
    private const int SnapshotByteBudget=8*1024*1024;
    private static void Need(bool value,string message,string code="UNSUPPORTED_REGION_DEPENDENCIES")
    {if(!value)throw new DomainException(code,message);}
    private sealed class Budget
    {
        private long bytes=2048;
        internal void Add(object value)
        {
            bytes+=JsonSerializer.SerializeToUtf8Bytes(value,Wire.Json).Length+1;
            Need(bytes<=SnapshotByteBudget,"Native Region snapshots exceed the actual8MiB serialized-byte budget; no Region/face/edge count was truncated.","RESPONSE_LIMIT_EXCEEDED");
        }
    }
    private static double Number(double value)
    {Need(double.IsFinite(value),"Native Region has a nonfinite stored geometry value.","INVALID_GEOMETRY");return value==0?0:value;}
    private static double[] Point(Point3d value)=>new[]{Number(value.X),Number(value.Y),Number(value.Z)};
    private static double[] Vector(Vector3d value)=>new[]{Number(value.X),Number(value.Y),Number(value.Z)};
    private static string? Id(ObjectId id)=>id.IsNull?null:Validation.NormalizeHandle(id.Handle.ToString());
    private static Region Exact(Entity entity)
    {Need(entity.GetType()==typeof(Region),"boolean_region requires exact native Region operands.","UNSUPPORTED_ENTITY_TYPE");return (Region)entity;}

    private static RegionPlaneValue Plane(Region region)
    {
        using var plane=region.GetPlane();
        Need(plane!=null,"The native Region plane is unavailable.","REGION_PLANE_UNAVAILABLE");
        var normal=plane!.Normal;Number(normal.Length);
        Need(normal.Length>0,"The native Region plane has a zero normal.","INVALID_GEOMETRY");
        var stored=region.Normal;Number(stored.Length);
        Need(stored.Length>0&&normal.GetNormal().CrossProduct(stored.GetNormal()).Length<=Tolerance.Global.EqualVector,
            "Native Region plane and stored normal disagree.","INVALID_GEOMETRY");
        return new(Point(plane.PointOnPlane),Vector(stored.GetNormal()));
    }
    private static Vector3d V(double[] value)=>new(value[0],value[1],value[2]);
    private static Point3d P(double[] value)=>new(value[0],value[1],value[2]);
    private static bool SamePlane(RegionPlaneValue first,RegionPlaneValue second,bool oriented)
    {
        var a=V(first.normal);var b=V(second.normal);var delta=P(second.point)-P(first.point);
        var tolerance=Tolerance.Global;
        // A native kernel may rebase its arbitrary point within the same plane.
        // Opposite source orientations are coplanar; target orientation is kept.
        return (oriented?(a-b).Length<=tolerance.EqualVector:a.CrossProduct(b).Length<=tolerance.EqualVector)&&
            Math.Abs(delta.DotProduct(a))<=tolerance.EqualPoint;
    }
    private static double[]? VertexPoint(Vertex? vertex)
    {using(vertex){return vertex==null||vertex.IsNull?null:Point(vertex.Point);}}
    private static object EdgeGeometry(Edge edge,Action cancel,Budget budget)
    {
        cancel();Need(!edge.IsNull,"Native Region contains a null edge.","INVALID_GEOMETRY");
        using var curve=edge.Curve;
        Need(curve!=null,"Native Region edge geometry is unavailable.","REGION_GEOMETRY_UNAVAILABLE");
        var geometry=CadRegionBoundarySnapshot346.Capture(curve!,cancel);
        var observed=new{forward=edge.IsOrientToCurve,start_vertex=VertexPoint(edge.Vertex1),end_vertex=VertexPoint(edge.Vertex2),geometry};
        budget.Add(observed);return observed;
    }
    private static Geometry Capture(Region region,Action cancel,Budget budget)
    {
        cancel();Exact(region);Need(!region.IsNull,"Native Region result is empty.","EMPTY_GEOMETRY");
        double area=Number(region.Area),perimeter=Number(region.Perimeter);
        Need(area>0&&perimeter>0,"Native Region result is empty or degenerate.","EMPTY_GEOMETRY");
        var plane=Plane(region);var normal=region.Normal;
        Need(normal.Length>0&&V(plane.normal).CrossProduct(normal.GetNormal()).Length<=Tolerance.Global.EqualVector,
            "Native Region plane and stored normal disagree.","INVALID_GEOMETRY");
        var bounds=region.GeometricExtents;var min=Point(bounds.MinPoint);var max=Point(bounds.MaxPoint);
        var faces=new List<object>();int loopsCount=0,edgesCount=0;
        using(var brep=new Brep(region))
        {
            Need(!brep.IsNull,"Native Region B-rep is unavailable.","REGION_GEOMETRY_UNAVAILABLE");
            cancel();Need(brep.CheckEntity(),"Native Region B-rep failed its native validity check.","INVALID_GEOMETRY");
            foreach(Face face in brep.Faces)using(face)
            {
                cancel();Need(!face.IsNull,"Native Region contains a null face.","INVALID_GEOMETRY");
                var loops=new List<object>();
                foreach(BoundaryLoop loop in face.Loops)using(loop)
                {
                    cancel();Need(!loop.IsNull,"Native Region contains a null boundary loop.","INVALID_GEOMETRY");
                    var edges=new List<object>();
                    foreach(Edge edge in loop.Edges)using(edge){edges.Add(EdgeGeometry(edge,cancel,budget));edgesCount++;}
                    Need(edges.Count>0,"Native Region boundary loop has no edges.","INVALID_GEOMETRY");
                    loops.Add(edges);loopsCount++;
                }
                Need(loops.Count>0,"Native Region face has no boundary loops.","INVALID_GEOMETRY");faces.Add(loops);
            }
        }
        Need(faces.Count>0,"Native Region has no faces.","EMPTY_GEOMETRY");
        var shape=Wire.Element(new{area,perimeter,plane,normal=Vector(normal),bounds=new{min,max},faces});
        Need(JsonSerializer.SerializeToUtf8Bytes(shape,Wire.Json).Length<=SnapshotByteBudget,"Native Region shape exceeds the actual snapshot-byte budget.","RESPONSE_LIMIT_EXCEEDED");
        return new(area,perimeter,plane,new(faces.Count,loopsCount,edgesCount,true),shape);
    }
    private static JsonElement Metadata(Region source,Transaction tr,Action cancel,Budget budget,bool readOwned)
    {
        cancel();using var data=source.XData;
        var xdata=data==null?null:NativeOwnedMetadata.Buffer(data,cancel,budget.Add);
        using var reactors=source.GetPersistentReactorIds();var links=new List<string?>();
        foreach(ObjectId id in reactors){cancel();var handle=Id(id);budget.Add(handle!);links.Add(handle);}
        // Target-owned dictionaries/Xrecords retain native ownership flags,
        // reactors, reference translation and exact typed scalar values. Tool
        // dependency graphs are not traversed: a read-only operand remains
        // usable, without claiming its uninspected owned records were verified.
        JsonElement? owned=readOwned?NativeOwnedMetadata.Capture(source,tr,cancel):null;
        if(owned.HasValue)budget.Add(owned.Value);
        var common=new{id=Id(source.ObjectId),owner=Id(source.OwnerId),layer=Id(source.LayerId),linetype=Id(source.LinetypeId),
            properties=EntityAdapter.StoredProperties(source),has_fields=source.HasFields,annotative=source.Annotative.ToString(),
            extension=Id(source.ExtensionDictionary),reactors=links.OrderBy(value=>value,StringComparer.Ordinal).ToArray()};
        budget.Add(common);return Wire.Element(new{common,xdata,owned});
    }
    internal static void CheckSource(Region target,Database db,Transaction tr,Action cancel)
    {
        Exact(target);using var reactors=target.GetPersistentReactorIds();
        Need(!target.HasFields&&reactors.Count==0&&target.Annotative!=AnnotativeStates.True,
            "Region geometry editing requires no fields, persistent source associations or annotation contexts; use their owning route.");
        var budget=new Budget();_ = Metadata(target,tr,cancel,budget,true);_ = Capture(target,cancel,budget);
        var loaded=AppDomain.CurrentDomain.GetAssemblies().Select(a=>a.GetName().Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        if(loaded.Contains("mapimgd")&&loaded.Contains("MapiBaseTypes"))CheckNativeOwner(target,db,tr,cancel);
    }
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void CheckNativeOwner(Region source,Database db,Transaction tr,Action cancel)
    {
        var document=Multicad.DatabaseServices.McDocument.ActiveDocument;
        Need(HostMgd.ApplicationServices.Application.DocumentManager.MdiActiveDocument?.Database==db&&document!=null&&
            (!string.IsNullOrEmpty(db.Filename)?string.Equals(Path.GetFullPath(db.Filename),Path.GetFullPath(document!.Path),StringComparison.OrdinalIgnoreCase):true),
            "Cannot establish this Region's matching native document.","DOCUMENT_CONTEXT_MISMATCH");
        var sourceId=Multicad.McObjectId.FromHandle(source.Handle.Value,document!.ID);if(sourceId.IsNull)return;
        Need(sourceId.GetObjectOfType<Multicad.Mc3D.HistoryItem>()==null,"Region belongs to a native feature history; use its owning adapter.");
        var model=((BlockTable)tr.GetObject(db.BlockTableId,OpenMode.ForRead))[BlockTableRecord.ModelSpace];var budget=new Budget();
        foreach(ObjectId id in (BlockTableRecord)tr.GetObject(model,OpenMode.ForRead))
        {
            cancel();if(tr.GetObject(id,OpenMode.ForRead) is not Solid3d)continue;
            var body=Multicad.McObjectId.FromHandle(id.Handle.Value,document.ID).GetObjectOfType<Multicad.Mc3D.Mc3dSolid>();if(body==null)continue;
            var contents=body.GetPartContents(false);Need(contents!=null,"Native history inventory is unavailable for proving Region independence.");
            foreach(var item in contents!)
            {
                cancel();budget.Add(item.ToString());
                Need(item!=sourceId&&!(item.GetObjectOfType<Multicad.Mc3D.ProjectionSketchEnt>() is {} projection&&projection.SrcObjectID==sourceId)&&
                    !(item.GetObjectOfType<Multicad.Mc3D.RevolveFeature>() is {} revolution&&revolution.Axis.ID==sourceId),
                    "Region is referenced by a native model history; use its source/model adapter.");
            }
        }
    }
    internal static void CheckTool(Entity entity,Action cancel)
    {_ = Capture(Exact(entity),cancel,new Budget());}
    internal static void Preview(RegionBooleanEditSpec edit,Region target,Func<string,Region> readTool,Database db,Transaction tr,Action cancel)
    {_ = Run(edit,target,readTool,db,tr,cancel,true);}
    internal static Outcome Apply(RegionBooleanEditSpec edit,Region target,Func<string,Region> readTool,Database db,Transaction tr,Action cancel)
        =>Run(edit,target,readTool,db,tr,cancel,false);
    private static Outcome Run(RegionBooleanEditSpec edit,Region target,Func<string,Region> readTool,Database db,Transaction tr,Action cancel,bool preview)
    {
        CheckSource(target,db,tr,cancel);var budget=new Budget();var before=Capture(target,cancel,budget);var properties=Metadata(target,tr,cancel,budget,true);
        var tools=new List<(Region Source,Geometry Geometry,JsonElement Properties)>();var operands=new List<Region>();Region? privateTarget=null;
        long bytes=JsonSerializer.SerializeToUtf8Bytes(before.Shape,Wire.Json).Length+JsonSerializer.SerializeToUtf8Bytes(properties,Wire.Json).Length;
        try
        {
            foreach(var handle in edit.ToolHandles)
            {
                cancel();var source=readTool(handle);Exact(source);
                Need(!source.ObjectId.IsNull&&source.Database==db&&source.OwnerId==target.OwnerId&&source.ObjectId!=target.ObjectId,
                    "Tools must be distinct committed Regions in the target's ordinary native space.","OUT_OF_SCOPE");
                var geometry=Capture(source,cancel,budget);Need(SamePlane(before.Plane,geometry.Plane,false),"Native Region operands are not coplanar; no projection was performed.","NONCOPLANAR_REGIONS");
                var metadata=Metadata(source,tr,cancel,budget,false);
                bytes+=JsonSerializer.SerializeToUtf8Bytes(geometry.Shape,Wire.Json).Length+JsonSerializer.SerializeToUtf8Bytes(metadata,Wire.Json).Length;
                Need(bytes<=SnapshotByteBudget,"Selected Region/tool snapshots exceed the actual aggregate8MiB serialized-byte budget.","RESPONSE_LIMIT_EXCEEDED");
                tools.Add((source,geometry,metadata));
                var operand=(Region)source.Clone();operands.Add(operand);
                Need(operand.ObjectId.IsNull,"Native tool clone did not detach its database identity.","OBJECT_IDENTITY_MISMATCH");
            }
            var operation=edit.Operator switch{"union"=>BooleanOperationType.BoolUnite,"intersection"=>BooleanOperationType.BoolIntersect,
                "subtract"=>BooleanOperationType.BoolSubtract,_=>throw new DomainException("INVALID_ARGUMENT","Unknown Region Boolean operator.")};
            if(preview){privateTarget=(Region)target.Clone();Need(privateTarget.ObjectId.IsNull,"Region preview target must be detached.","OBJECT_IDENTITY_MISMATCH");}
            var working=privateTarget??target;Geometry after=before;
            foreach(var operand in operands)
            {
                cancel();working.BooleanOperation(operation,operand);after=Capture(working,cancel,new Budget());
                Need(SamePlane(before.Plane,after.Plane,true),"Native Region Boolean changed the target's geometric plane/orientation.","REGION_PLANE_MISMATCH");
            }
            Need(properties.GetRawText()==Metadata(target,tr,cancel,new Budget(),true).GetRawText(),"Native Region Boolean changed target identity, owner or stored metadata/appearance.","OBJECT_IDENTITY_MISMATCH");
            if(preview)Need(ModelParameterSnapshot.Equivalent(before.Shape,Capture(target,cancel,new Budget()).Shape),"Detached Region preview changed its committed source.","BOOLEAN_SOURCE_CHANGED");
            foreach(var tool in tools)
            {
                cancel();Need(ModelParameterSnapshot.Equivalent(tool.Geometry.Shape,Capture(tool.Source,cancel,new Budget()).Shape)&&
                    tool.Properties.GetRawText()==Metadata(tool.Source,tr,cancel,new Budget(),false).GetRawText(),
                    "Native Region Boolean changed a read-only committed tool.","BOOLEAN_SOURCE_CHANGED");
            }
            return new(before.Area,after.Area,after.Plane,after.Topology);
        }
        finally{privateTarget?.Dispose();foreach(var operand in operands)operand.Dispose();}
    }
    internal static (object? Geometry,string? Reason) TryRead(Region region,Action cancel)
    {
        try{var value=Capture(region,cancel,new Budget());return(new{scope="native_planar_region_brep",plane=value.Plane,perimeter=value.Perimeter,topology=value.Topology},null);}
        catch(OperationCanceledException){throw;}
        catch(DomainException error){return(null,error.Error.code+": "+error.Error.message);}
        catch(System.Exception error){return(null,"Native Region Boolean geometry unavailable: "+error.GetType().Name);}
    }
    internal static object Read(Region region,Action cancel)
    {
        var details=TryRead(region,cancel);
        return new{area=Number(region.Area),normal=Vector(region.Normal),boolean_geometry=details.Geometry,
            boolean_geometry_unavailable_reason=details.Reason};
    }
}
