using System.Text.Json;
using System.Text.Json.Nodes;
using Multicad;
using Multicad.DatabaseServices;
using Multicad.Geometry;
using Multicad.Mc3D;
using NanoCAD.Contracts;

namespace NanoCADBridge;

// This context belongs to one selected Suppress(false)/Unsuppress(false).
// GetAllDependent is an observed reactor closure, never a suppression list.
// Healthy editing elsewhere keeps its existing admission rules.
internal static class ModelFeatureSuppressionRecovery420
{
    internal sealed record Context(HashSet<string> Affected,HashSet<string> Pending);
    static void Need([System.Diagnostics.CodeAnalysis.DoesNotReturnIf(false)] bool ok,string code,string message)
    {if(!ok)throw new DomainException(code,message);}
    static string Key(McObjectId id)=>id.ToString();
    internal static bool Admitted(HistoryItem item,Context context)=>
        item.Status.ToString()=="NeedUpdate"&&context.Affected.Contains(Key(item.ID));
    internal static bool Healthy(HistoryItem item)=>item.Status.ToString()=="Ok"&&!item.IsSuppressed||ProjectedHistorySources.HealthySuppressed(item);
    static void Owned(HistoryItem item,McObjectId id,Mc3dSolid body)
    {
        bool construction=item.HostSolidID.IsNull&&(id.GetObjectOfType<PlanarSketch>()!=null||id.GetObjectOfType<SketchProfile>()!=null);
        Need(item.ID==id&&(item.HostSolidID==body.ID||construction)&&item.DbEntity.Document?.ID==body.DbEntity.Document.ID,
            "DEPENDENCY_OWNERSHIP_MISMATCH","A suppression recovery node lacks exact actual part/document ownership.");
        Need(!item.IsBeyondEndOfPart,"OUT_OF_SCOPE","Suppression recovery does not cross End-of-Part.");
        if(id.GetObjectOfType<PlanarSketch>() is {} sketch)
            Need(!sketch.IsInEditMode,"HOST_BUSY","Finish editing the actual sketch before changing feature suppression.");
    }
    internal static Context? Create(Mc3dSolid body,McObjectId selected,McObjectId[] contents,ModelViewFollowers.Snapshot views,Action cancel)
    {
        if(!contents.Any(id=>{cancel();return id.GetObjectOfType<HistoryItem>()?.Status.ToString()=="NeedUpdate";}))return null;
        var root=selected.GetObjectOfType<HistoryItem>();
        Need(root!=null&&root.ID==selected&&root.HostSolidID==body.ID,"DEPENDENCY_OWNERSHIP_MISMATCH","The selected suppression root is unavailable.");
        var downstream=root!.GetAllDependent(false);
        Need(downstream!=null,"DEPENDENCY_INVENTORY_UNAVAILABLE","The actual native downstream inventory is unavailable.");
        ModelInventoryBudget.Ids(downstream!,cancel);var known=contents.ToHashSet();
        var affected=new HashSet<string>(StringComparer.OrdinalIgnoreCase){Key(selected)};
        foreach(var id in downstream!)
        {
            cancel();if(id==body.ID)continue; // exact selected aggregate, not a history consumer
            if(!known.Contains(id))
            {
                Need(ModelViewFollowers.OwnsDependent(views,id),"DEPENDENCY_OWNERSHIP_MISMATCH","A selected native dependent is outside this part and its retained drawing views.");
                continue;
            }
            affected.Add(Key(id));
        }
        var pending=new HashSet<string>(StringComparer.OrdinalIgnoreCase);var context=new Context(affected,pending);
        foreach(var id in contents)
        {
            cancel();var item=id.GetObjectOfType<HistoryItem>();if(item==null)continue;Owned(item,id,body);
            Need(Healthy(item)||Admitted(item,context),"MODEL_STATE_UNAVAILABLE","Only the selected feature and its actual downstream closure may enter suppression recovery with NeedUpdate; critical or unrelated pending nodes remain protected.");
            if(item.Status.ToString()=="NeedUpdate")pending.Add(Key(id));
        }
        var sources=NativeSources(body,contents,context,cancel);
        foreach(var id in downstream!.Where(id=>id!=body.ID&&known.Contains(id)))
        {
            cancel();if(id.GetObjectOfType<HistoryItem>()!=null)continue;
            Need(sources.Details.TryGetValue(Key(id),out var detail),"DEPENDENCY_OWNERSHIP_MISMATCH","A native downstream helper lacks a current sketch binding.");
            var observed=Wire.Element(detail!);
            Need(observed.TryGetProperty("kind",out var kind)&&(kind.GetString() is "native_sketch_input_source" or "native_sketch_relation_source" or "native_sketch_construction_point")&&
                observed.TryGetProperty("sketch_id",out _),"DEPENDENCY_OWNERSHIP_MISMATCH","A downstream helper is not a witnessed native sketch member, relation or point.");
            string host=observed.GetProperty("sketch_id").GetString()!;var hostId=contents.SingleOrDefault(candidate=>Key(candidate).Equals(host,StringComparison.OrdinalIgnoreCase));
            var sketch=hostId.GetObjectOfType<PlanarSketch>();
            Need(sketch!=null,"DEPENDENCY_OWNERSHIP_MISMATCH","A downstream helper's actual sketch is unavailable.");Owned(sketch!,hostId,body);
        }
        return context;
    }
    internal static void RequireClosureOwnership(Mc3dSolid body,McObjectId selected,McObjectId[] contents,ModelViewFollowers.Snapshot views,Action cancel)
    {
        var root=selected.GetObjectOfType<HistoryItem>();var downstream=root?.GetAllDependent(false);
        Need(root!=null&&downstream!=null,"DEPENDENCY_INVENTORY_UNAVAILABLE","The selected native downstream inventory became unavailable.");
        ModelInventoryBudget.Ids(downstream!,cancel);var known=contents.ToHashSet();
        foreach(var id in downstream!)
        {
            cancel();if(id==body.ID)continue;
            Need(known.Contains(id)||ModelViewFollowers.OwnsDependent(views,id),"DEPENDENCY_OWNERSHIP_MISMATCH","Suppression recovery acquired a foreign downstream consumer.");
        }
        // GetAllDependent defaults to reactors; the SDK does not promise its
        // activation inventory is invariant. Full414 snapshots compare the
        // actual stored GetDependsOn identities and all suppression choices.
    }
    internal static NativePathSources.Inspection RequireHistory(Mc3dSolid body,McObjectId[] contents,ProjectedHistorySources.Source[] external,Context context,Action cancel,bool allowPending=true)
    {
        var native=NativeSources(body,contents,context,cancel);var sources=new HashSet<string>(native.Sources,StringComparer.OrdinalIgnoreCase);
        sources.UnionWith(external.Select(source=>source.id));
        foreach(var id in contents)
        {
            cancel();var item=id.GetObjectOfType<HistoryItem>();
            if(item==null){Need(sources.Contains(Key(id)),"HISTORY_UNAVAILABLE","A retained native source is missing or unverified.");continue;}
            Owned(item,id,body);
            Need(Healthy(item)||allowPending&&Admitted(item,context),"MODEL_STATE_UNAVAILABLE","Suppression recovery left a critical, unrelated pending or unresolved active history node.");
        }
        return native;
    }
    // The ordinary reader intentionally requires healthy referenced history.
    // Here NeedUpdate is admitted only in the already witnessed selected
    // closure, while exact target geometry and sketch membership still prove
    // every source. No status is changed or inferred from an opaque bare ID.
    internal static NativePathSources.Inspection NativeSources(Mc3dSolid body,McObjectId[] contents,Context context,Action cancel)
    {
        var sources=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var bindings=new Dictionary<string,List<(string Consumer,string Role,string Geometry)>>(StringComparer.OrdinalIgnoreCase);
        void Reference(McGeomParam? reference,McObjectId consumer,string role,bool plane=false)
        {
            cancel();Need(reference!=null&&!reference.ID.IsNull,"NATIVE_PATH_STATE_UNAVAILABLE","A retained source binding is unavailable.");
            var id=reference!.ID;var item=id.GetObjectOfType<HistoryItem>();
            if(item!=null)
            {
                Need(item.ID==id&&item.DbEntity.Document?.ID==body.DbEntity.Document.ID&&
                    (Healthy(item)||Admitted(item,context)),"MODEL_STATE_UNAVAILABLE","A referenced native construction is foreign, critical or pending outside the selected recovery closure.");
            }
            string geometryType;
            if(plane&&id.GetObjectOfType<WorkPlane>() is {} support)
            {
                using var position=support.Position;
                Need(position!=null&&new[]{position.Normal.X,position.Normal.Y,position.Normal.Z,position.PointOnPlane.X,position.PointOnPlane.Y,position.PointOnPlane.Z}.All(double.IsFinite)&&
                    position.Normal.X*position.Normal.X+position.Normal.Y*position.Normal.Y+position.Normal.Z*position.Normal.Z>1e-20,
                    "NATIVE_PATH_STATE_UNAVAILABLE","The actual bound work plane geometry is unavailable.");
                geometryType="work_plane";
            }
            else
            {
                using var geometry=reference.GetGeometry();
                Need(geometry!=null&&(plane?geometry.GeometryType==EntityGeomType.kPlaneSegment:(geometry.GeometryType&EntityGeomType.kCurveEntities)!=0),
                    "NATIVE_PATH_STATE_UNAVAILABLE","The exact bound native target has no current curve/plane geometry.");
                geometryType=geometry!.GeometryType.ToString();
            }
            string key=Key(id);sources.Add(key);
            if(!bindings.TryGetValue(key,out var rows))bindings[key]=rows=[];
            rows.Add((Key(consumer),role,geometryType));
        }
        foreach(var id in contents)
        {
            cancel();
            if(id.GetObjectOfType<PlanarSketch>() is {} sketch&&!sketch.PlanarEntityID.IsNull)
                Reference(new McGeomParam{ID=sketch.PlanarEntityID},id,"sketch_support",true);
            if(id.GetObjectOfType<ProjectionSketchEnt>() is {} projection&&projection.SrcObjectID.IsEdge)
            {
                var host=projection.HostSketchID.GetObjectOfType<PlanarSketch>();
                Need(host!=null&&host.ID==projection.HostSketchID&&contents.Contains(host.ID)&&projection.ID==id&&
                    projection.DbEntity.Document?.ID==host.DbEntity.Document?.ID&&projection.HostSolidID==body.ID&&host.HostSolidID==body.ID&&projection.SrcObjectID.ParentId==body.ID,
                    "SKETCH_OWNERSHIP_MISMATCH","The projected edge lost its exact same-part native sketch binding.");
                Owned(host!,host.ID,body);ModelSketchNativeReferences410.Geometry(projection.SrcObjectID,body.ID,cancel);
                Reference(new McGeomParam{ID=projection.SrcObjectID},id,"sketch_reference");
            }
            if(id.GetObjectOfType<ExtrudeAlongPathFeature>() is {} sweep)
            {
                var paths=sweep.GetPath();Need(paths!=null&&paths.Count>0,"NATIVE_PATH_STATE_UNAVAILABLE","A native sweep has unavailable paths.");
                foreach(var path in paths!)Reference(path,id,"sweep_path");
            }
            if(id.GetObjectOfType<LoftFeature>() is not {} loft)continue;
            if(loft.LoftType==LoftType.WithCenterLine)Reference(loft.Path,id,"loft_centerline");
            Need(loft.GuideRailsCount>=0,"NATIVE_PATH_STATE_UNAVAILABLE","A native loft has an invalid rail inventory.");
            for(int index=0;index<loft.GuideRailsCount;index++)
            {
                cancel();var rail=loft.GetGuideRailsParams(index);Need(rail!=null&&rail.Count>0,"NATIVE_PATH_STATE_UNAVAILABLE","A native loft rail inventory is unavailable.");
                foreach(var path in rail!)Reference(path,id,"loft_rail");
            }
        }
        var details=bindings.ToDictionary(pair=>pair.Key,pair=>(object)new{
            kind=pair.Value.All(value=>value.Role=="sketch_support")?"native_support_source":"native_path_source",
            source_id=pair.Key,consumer_ids=pair.Value.Select(value=>value.Consumer).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(value=>value,StringComparer.Ordinal).ToArray(),
            roles=pair.Value.Select(value=>value.Role).Distinct(StringComparer.Ordinal).OrderBy(value=>value,StringComparer.Ordinal).ToArray(),
            geometry_types=pair.Value.Select(value=>value.Geometry).Distinct(StringComparer.Ordinal).OrderBy(value=>value,StringComparer.Ordinal).ToArray(),
            verification="current_bound_native_geometry_with_selected_pending_recovery"},StringComparer.OrdinalIgnoreCase);
        return NativeSketchHistory385.Complete(contents,new(sources,details,new(StringComparer.OrdinalIgnoreCase)),cancel);
    }
    internal static JsonElement ObservedNodes(JsonElement nodes,McObjectId[] contents,NativePathSources.Inspection sources,ProjectedHistorySources.Source[] external,Action cancel)
    {
        // RequireHistory has independently proved every source before this
        // adapter removes the ordinary healthy-reader's advisory reason.
        var observed=JsonNode.Parse(nodes.GetRawText())!.AsArray();var ids=contents.ToDictionary(Key,StringComparer.OrdinalIgnoreCase);
        foreach(var row in observed)
        {
            cancel();string key=row!["mc_id"]!.GetValue<string>();
            if(ids[key].GetObjectOfType<HistoryItem>()==null)
            {
                object? detail=null;
                if(sources.Details.TryGetValue(key,out var native))detail=native;
                else if(external.SingleOrDefault(candidate=>candidate.id.Equals(key,StringComparison.OrdinalIgnoreCase)) is {} projected)
                    detail=new{kind="projected_source",projected.id,projected.handle,projected.projection_ids,projected.revolution_axis_ids,projected.rotation_anchor_ids,verification="current_native_identity_and_projection_owner"};
                Need(detail!=null,"HISTORY_UNAVAILABLE","A retained native source lacks its independently verified binding detail.");
                row["parameters"]=JsonSerializer.SerializeToNode(detail,Wire.Json);
            }
            row["detail_unavailable_reason"]=null;
        }
        return Wire.Element(observed.OrderBy(row=>row!["mc_id"]!.GetValue<string>(),StringComparer.OrdinalIgnoreCase).ToArray());
    }
}
