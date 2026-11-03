using Multicad;
using Multicad.DatabaseServices;
using Multicad.Geometry;
using Multicad.Mc3D;
using NanoCAD.Contracts;
namespace NanoCADBridge;
// A stored reference proves its consumer, while GetGeometry proves its current
// native target and curve/plane kind. Never classify a bare ID as a valid source.
// This works for document curves and body-owned path geometry alike; no Model
// Space ownership restriction is inferred merely to make the history readable.
internal static class NativePathSources
{
 internal sealed record Inspection(HashSet<string> Sources,Dictionary<string,object> Details,Dictionary<string,string> Failures);
 // Ownership checks need the complete stored bindings of neighboring bodies,
 // not admission of those bodies for editing. A broken unrelated target must
 // not block a selected body when its stored ID is demonstrably different.
 internal static HashSet<string> ReferencedIds(IEnumerable<McObjectId> contents,Action? cancel=null){
  var result=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
  void Add(McObjectId id){if(!id.IsNull)result.Add(id.ToString());}
  foreach(var id in contents){
   cancel?.Invoke();
   var sketch=id.GetObjectOfType<PlanarSketch>();if(sketch!=null)Add(sketch.PlanarEntityID);
   var projection=id.GetObjectOfType<ProjectionSketchEnt>();if(projection!=null&&projection.SrcObjectID.IsEdge)Add(projection.SrcObjectID);
   var array=id.GetObjectOfType<ArrayFeature>();
   if(array is CircularPatternFeature or RectangularPatternFeature)
    for(int dimension=0;dimension<(array is CircularPatternFeature?1:2);dimension++)
    {cancel?.Invoke();var binding=NativePatternObservation439.TryRead(array,dimension);if(binding!=null)Add(ModelPatternReferences439.Id(binding.native_id));}
   var sweep=id.GetObjectOfType<ExtrudeAlongPathFeature>();
   if(sweep!=null){var paths=sweep.GetPath();if(paths==null)throw new DomainException("HISTORY_UNAVAILABLE","Cannot prove shared-source ownership with an unavailable native path binding inventory.",details:new{consumer_id=id.ToString(),role="sweep_path"});foreach(var path in paths)if(path!=null)Add(path.ID);}
   var loft=id.GetObjectOfType<LoftFeature>();if(loft==null)continue;
   if(loft.LoftType==LoftType.WithCenterLine){
    if(loft.Path==null)throw new DomainException("HISTORY_UNAVAILABLE","Cannot prove shared-source ownership with an unavailable native centerline binding.",details:new{consumer_id=id.ToString(),role="loft_centerline"});
    Add(loft.Path.ID);
   }
   if(loft.GuideRailsCount<0)throw new DomainException("HISTORY_UNAVAILABLE","Native rail binding inventory has an invalid count.",details:new{consumer_id=id.ToString(),role="loft_rail"});
   for(int i=0;i<loft.GuideRailsCount;i++){
    var paths=loft.GetGuideRailsParams(i);if(paths==null)throw new DomainException("HISTORY_UNAVAILABLE","Cannot prove shared-source ownership with an unavailable native rail binding inventory.",details:new{consumer_id=id.ToString(),role="loft_rail",rail_index=i});
    foreach(var path in paths)if(path!=null)Add(path.ID);
   }
  }
  return result;
 }
 internal static HashSet<string> Read(IEnumerable<McObjectId> contents,Action? cancel=null){var current=contents.ToArray();return NativeSketchHistory385.Complete(current,Collect(current,true,cancel),cancel).Sources;}
 internal static Inspection Inspect(IEnumerable<McObjectId> contents,Action? cancel=null){var current=contents.ToArray();return NativeSketchHistory385.Complete(current,Collect(current,false,cancel),cancel);}
 private static Inspection Collect(IEnumerable<McObjectId> contents,bool failFast,Action? cancel){
  var result=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
  var bindings=new Dictionary<string,List<(string Consumer,string Role,string Geometry)>>(StringComparer.OrdinalIgnoreCase);
  var failures=new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase);
  void Accept(McObjectId id,McObjectId consumer,string role,string geometry){
   string key=id.ToString();result.Add(key);
   if(!bindings.TryGetValue(key,out var items))bindings[key]=items=[];
   items.Add((consumer.ToString(),role,geometry));
  }
  void Validate(McGeomParam? reference,McObjectId consumer,string role,bool plane){
   cancel?.Invoke();
   var id=reference?.ID??McObjectId.Null;
   void Fail(string reason,string? geometry=null)=>throw new DomainException("NATIVE_PATH_STATE_UNAVAILABLE",
    "A stored native path or sketch support cannot be verified. Refresh this body's history before dependent edits.",
    details:new{consumer_id=consumer.ToString(),source_id=id.IsNull?null:id.ToString(),role,reason,geometry_type=geometry});
   if(reference==null||id.IsNull){Fail("missing_bound_native_identity");return;}
   var item=id.GetObjectOfType<HistoryItem>();
   if(item!=null&&(item.ID!=id||item.Status.ToString()!="Ok"&&!ProjectedHistorySources.HealthySuppressed(item)))
    throw new DomainException("MODEL_STATE_UNAVAILABLE","A referenced native history item is unavailable or unhealthy.",
     details:new{consumer_id=consumer.ToString(),source_id=id.ToString(),role,status=item.Status.ToString(),suppressed=item.IsSuppressed});
   // A native work plane has a dedicated current geometric getter. A healthy
   // support HistoryItem is still checked above, including one outside contents.
   if(!plane&&id.GetObjectOfType<WorkAxis>() is {} workAxis){
    using var line=workAxis.PositionAsLineSeg;
    if(line==null||!new[]{line.StartPoint.X,line.StartPoint.Y,line.StartPoint.Z,line.EndPoint.X,line.EndPoint.Y,line.EndPoint.Z}.All(double.IsFinite)||line.Length<=0){Fail("work_axis_geometry_unavailable");return;}
    Accept(id,consumer,role,"work_axis");return;
   }
   if(plane&&id.GetObjectOfType<WorkPlane>() is {} workPlane){
    if(workPlane.ID!=id||workPlane.Status.ToString()!="Ok"&&!ProjectedHistorySources.HealthySuppressed(workPlane)){Fail("unhealthy_work_plane");return;}
    using var position=workPlane.Position;
    if(position==null||!new[]{position.Normal.X,position.Normal.Y,position.Normal.Z,position.PointOnPlane.X,position.PointOnPlane.Y,position.PointOnPlane.Z}.All(double.IsFinite)||
       position.Normal.X*position.Normal.X+position.Normal.Y*position.Normal.Y+position.Normal.Z*position.Normal.Z<1e-20){Fail("work_plane_geometry_unavailable");return;}
    Accept(id,consumer,role,"work_plane");return;
   }
   try{
    // SDK GetGeometry retrieves the geometric entity from the bound target;
    // this is a getter, not Init/snapping, Sync, edit mode or a native factory.
    using var geometry=reference.GetGeometry();
    if(geometry==null){Fail("bound_geometry_unavailable");return;}
    var kind=geometry.GeometryType;
    if(plane?kind!=EntityGeomType.kPlaneSegment:(kind&EntityGeomType.kCurveEntities)==0){Fail(plane?"bound_target_is_not_planar_support":"bound_target_is_not_curve",kind.ToString());return;}
    Accept(id,consumer,role,kind.ToString());
   }catch(DomainException){throw;}
   catch(Exception error)when(error is not OperationCanceledException){throw new DomainException("NATIVE_PATH_STATE_UNAVAILABLE","Reading the exact bound native path or sketch support failed.",
    details:new{consumer_id=consumer.ToString(),source_id=id.ToString(),role,reason="bound_geometry_read_failed",native_error=error.Message});}
  }
  void Add(McGeomParam? reference,McObjectId consumer,string role,bool plane=false){
   void RecordFailure(string message){
    string source=reference==null||reference.ID.IsNull?consumer.ToString():reference.ID.ToString();
    result.Remove(source);bindings.Remove(source);failures[source]=message;
    // Document support/path targets need not be part contents. Mark their
    // consumer as incomplete too so the history read never claims complete
    // merely because an unavailable external target is absent from its nodes.
    failures[consumer.ToString()]=message;
   }
   try{Validate(reference,consumer,role,plane);}
   catch(DomainException error)when(!failFast){
    RecordFailure(error.Error.code+": "+error.Message);
   }
   catch(Exception error)when(error is not DomainException and not OperationCanceledException){
    if(failFast)throw new DomainException("NATIVE_PATH_STATE_UNAVAILABLE","Reading the exact stored native path or sketch support failed.",
     details:new{consumer_id=consumer.ToString(),source_id=reference==null||reference.ID.IsNull?null:reference.ID.ToString(),role,reason="native_target_read_failed",native_error=error.Message});
    RecordFailure("NATIVE_PATH_STATE_UNAVAILABLE: "+error.Message);
   }
  }
  foreach(var id in contents){
   cancel?.Invoke();
   try{
   var sketch=id.GetObjectOfType<PlanarSketch>();if(sketch!=null&&!sketch.PlanarEntityID.IsNull)Add(new McGeomParam{ID=sketch.PlanarEntityID},id,"sketch_support",true);
   var array=id.GetObjectOfType<ArrayFeature>();
   if(array is CircularPatternFeature or RectangularPatternFeature)
    for(int dimension=0;dimension<(array is CircularPatternFeature?1:2);dimension++)
    {cancel?.Invoke();var binding=NativePatternObservation439.TryRead(array,dimension);if(binding!=null)Add(ModelPatternReferences439.Reference(binding),id,array is CircularPatternFeature?"circular_pattern_axis":"rectangular_pattern_direction");}
   var projection=id.GetObjectOfType<ProjectionSketchEnt>();
   if(projection!=null&&projection.SrcObjectID.IsEdge)
   {
    var host=projection.HostSketchID.GetObjectOfType<PlanarSketch>();
    if(projection.ID!=id||host==null||host.ID!=projection.HostSketchID||host.DbEntity.Document==null||projection.DbEntity.Document==null||
       host.DbEntity.Document.ID!=projection.DbEntity.Document.ID||host.HostSolidID.IsNull||projection.HostSolidID!=host.HostSolidID||
       projection.SrcObjectID.ParentId!=host.HostSolidID||host.Status.ToString()!="Ok"&&!ProjectedHistorySources.HealthySuppressed(host))
     throw new DomainException("SKETCH_OWNERSHIP_MISMATCH","A native projected edge must retain its healthy sketch, document and exact source part.",details:new{projection_id=id.ToString(),source_id=projection.SrcObjectID.ToString(),host_sketch_id=projection.HostSketchID.ToString(),host_solid_id=projection.HostSolidID.ToString()});
    ModelSketchNativeReferences410.Geometry(projection.SrcObjectID,host.HostSolidID,cancel??(()=>{}));
    Add(new McGeomParam{ID=projection.SrcObjectID},id,"sketch_reference");
   }
   var sweep=id.GetObjectOfType<ExtrudeAlongPathFeature>();if(sweep!=null){
    var paths=sweep.GetPath();
    if(paths==null||paths.Count==0)throw new DomainException("NATIVE_PATH_STATE_UNAVAILABLE","Native sweep path inventory is unavailable.",details:new{consumer_id=id.ToString(),role="sweep_path"});
    foreach(var path in paths)Add(path,id,"sweep_path");
   }
   var loft=id.GetObjectOfType<LoftFeature>();if(loft==null)continue;
   if(loft.LoftType==LoftType.WithCenterLine)Add(loft.Path,id,"loft_centerline");
   if(loft.GuideRailsCount<0)throw new DomainException("NATIVE_PATH_STATE_UNAVAILABLE","Native loft returned an invalid rail count.",details:new{consumer_id=id.ToString(),role="loft_rail"});
   for(int i=0;i<loft.GuideRailsCount;i++){
    var rail=loft.GetGuideRailsParams(i);
    if(rail==null||rail.Count==0)throw new DomainException("NATIVE_PATH_STATE_UNAVAILABLE","Native loft rail inventory is unavailable.",details:new{consumer_id=id.ToString(),role="loft_rail",rail_index=i});
    foreach(var path in rail)Add(path,id,"loft_rail");
   }
   }catch(DomainException error)when(!failFast){failures[id.ToString()]=error.Error.code+": "+error.Message;}
   catch(Exception error)when(!failFast&&error is not OperationCanceledException){failures[id.ToString()]="NATIVE_PATH_STATE_UNAVAILABLE: "+error.Message;}
   catch(Exception error)when(error is not DomainException and not OperationCanceledException){throw new DomainException("NATIVE_PATH_STATE_UNAVAILABLE","Reading a native feature's stored path or support inventory failed.",
    details:new{consumer_id=id.ToString(),role="feature_path_or_support_inventory",reason="native_reference_inventory_failed",native_error=error.Message});}
  }
  // A target shared by valid and invalid bindings remains unverified.
  foreach(var key in failures.Keys){result.Remove(key);bindings.Remove(key);}
  var details=bindings.ToDictionary(pair=>pair.Key,pair=>(object)new{
   kind=pair.Value.All(v=>v.Role=="sketch_support")?"native_support_source":"native_path_source",
   source_id=pair.Key,consumer_ids=pair.Value.Select(v=>v.Consumer).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(v=>v,StringComparer.Ordinal).ToArray(),
   roles=pair.Value.Select(v=>v.Role).Distinct(StringComparer.Ordinal).OrderBy(v=>v,StringComparer.Ordinal).ToArray(),
   geometry_types=pair.Value.Select(v=>v.Geometry).Distinct(StringComparer.Ordinal).OrderBy(v=>v,StringComparer.Ordinal).ToArray(),
   verification="current_bound_native_geometry"},StringComparer.OrdinalIgnoreCase);
  return new(result,details,failures);
 }
}
