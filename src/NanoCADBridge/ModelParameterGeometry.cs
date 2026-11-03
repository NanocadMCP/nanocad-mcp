using Multicad;
using Multicad.DatabaseServices;
using Multicad.Mc3D;
using NanoCAD.Contracts;
using Db=Teigha.DatabaseServices;

namespace NanoCADBridge;

// Reconstruct parameter edits from retained profiles and the actual feature order.
// The live body is only read: preview booleans and transforms use detached solids.
internal static class ModelParameterGeometry
{
    private static void Need(bool ok,string code,string message){if(!ok)throw new DomainException(code,message);}
    private static string Key(McObjectId id)=>id.ToString();
    internal static ModelFeatureOracle.Metrics Preview(Mc3dSolid body,DocumentState state,Db.Transaction tx,
        IReadOnlyDictionary<McObjectId,double> distances,IReadOnlyDictionary<McObjectId,double> angles,Action cancel,IReadOnlyDictionary<McObjectId,ModelPlacement>? placementChanges=null,bool ignoreTerminalFinishing=false)
    {
        var contents=ModelInventoryBudget.History(body,cancel);var ids=contents.ToArray();
        var views=ModelViewFollowers.Resolve(body,ids,cancel);
        var roots=ids.Select(id=>id.GetObjectOfType<RevolveFeature>()).Where(f=>f!=null).ToArray();
        if(roots.Length>0)
        {
            Need(roots.Length==1&&angles.Keys.All(id=>id==roots[0]!.ID),"OUT_OF_SCOPE","One retained projected revolution root required.");var root=roots[0]!;
            double angle=angles.TryGetValue(root.ID,out var degrees)?degrees*Math.PI/180:root.Extent==RevolveParamExtType.All?2*Math.PI:root.Angle;
            return DrawingProfileWriter.PreviewRevolutionAngle(body,state,tx,angle,cancel,distances.ToDictionary(p=>p.Key,p=>p.Value),placementChanges);
        }
        Need(angles.Count==0,"OUT_OF_SCOPE","Angular changes require a retained revolution.");
        var external=ProjectedHistorySources.Read(body,ids,state,tx,cancel);ProjectedHistorySources.Healthy(ids,external);
        var sketches=ids.Select(id=>id.GetObjectOfType<PlanarSketch>()).Where(s=>s!=null).ToDictionary(s=>s!.ID,s=>s!);
        var profiles=ids.Select(id=>id.GetObjectOfType<SketchProfile>()).Where(p=>p!=null).ToDictionary(p=>p!.ID,p=>p!);
        var features=ids.Select(id=>id.GetObjectOfType<ExtrudeFeature>()).Where(f=>f!=null).ToDictionary(f=>Key(f!.ID),f=>f!);
        var placements=ModelPlacementHistory.Ids(ids);
        var finishing=ids.Where(id=>id.GetObjectOfType<FilletFeature>()!=null||id.GetObjectOfType<ChamferFeature>()!=null).ToArray();
        Need(!ignoreTerminalFinishing||finishing.Length==1&&views.LastModelItem==finishing[0],
            "OUT_OF_SCOPE","Detached base preview may ignore only one terminal healthy fillet/chamfer.");
        Need(placementChanges==null||placementChanges.Keys.All(placements.Contains),"OUT_OF_SCOPE","A changed placement is outside the retained history.");
        Need(features.Count>=1&&profiles.Count==features.Count&&sketches.Count==features.Count,"OUT_OF_SCOPE","Require distinct retained sketch/profile/straight extrusion bindings per body.");
        Need(sketches.Values.All(s=>s.HostSolidID==body.ID&&!s.IsInEditMode),"OUT_OF_SCOPE","Sketch owner or availability changed.");
        var allowed=sketches.Keys.Concat(profiles.Keys).Concat(features.Values.Select(f=>f.ID)).Append(body.ID)
            .Concat(ids.Where(id=>id.GetObjectOfType<ProjectionSketchEnt>()!=null)).Concat(placements)
            .Concat(ids.Where(id=>external.Any(s=>Key(id)==s.id)))
            .Concat(ignoreTerminalFinishing?finishing:[]).Concat(views.ViewIds).ToHashSet();
        Need(allowed.SetEquals(ids),"OUT_OF_SCOPE","Additional forming/finishing operations require a supported reconstruction.");
        foreach(var f in features.Values){DrawingProfileWriter.StraightExtrusion(f,body);Need(profiles.ContainsKey(f.ProfileID),"OUT_OF_SCOPE","Feature profile binding unavailable.");}
        Need(features.Values.Select(f=>f.ProfileID).Distinct().Count()==profiles.Count&&profiles.Values.All(p=>sketches.ContainsKey(p.ParentSketchID)&&p.HostSolidID==body.ID)&&profiles.Values.Select(p=>p.ParentSketchID).Distinct().Count()==sketches.Count,"OUT_OF_SCOPE","Sketch/profile bindings must be one-to-one.");
        Need(distances.Keys.All(id=>features.ContainsKey(Key(id))),"OUT_OF_SCOPE","A changed distance is outside the reconstructed chain.");
        var links=ids.Select(id=>{var h=id.GetObjectOfType<HistoryItem>();var prev=h?.OrderObjBefore??McObjectId.Null;return new ModelSequenceLink(Key(id),prev.IsNull?null:Key(prev));}).ToArray();
        var last=ignoreTerminalFinishing?finishing[0].GetObjectOfType<HistoryItem>()!.OrderObjBefore:views.LastModelItem;
        Need(!last.IsNull,"MODEL_ORDER_UNAVAILABLE","Detached base has no predecessor before finishing.");
        var orders=new List<string[]>();foreach(var root in features.Keys)
            try{cancel();orders.Add(ModelFeatureSequence.FromHistory(links,features.Keys.Concat(placements.Select(Key)).ToArray(),root,Key(last),cancel));}
            catch(DomainException e)when(e.Error.code=="MODEL_ORDER_UNAVAILABLE"){}
        Need(orders.Count==1&&features[orders[0][0]].PartOperation==PartFeatureOperation.Join,"MODEL_ORDER_UNAVAILABLE","One ordered additive base and following cut/join/placement chain required.");
        var steps=orders[0].Select(key=>
        {
            cancel();if(!features.TryGetValue(key,out var f))
            {
                var id=placements.Single(id=>Key(id)==key);var placementStep=ModelPlacementHistory.Step(id);
                return placementChanges!=null&&placementChanges.TryGetValue(id,out var placement)?placementStep with{Placement=placement}:placementStep;
            }
            var step=DrawingProfileWriter.ReadTail(f,profiles[f.ProfileID].ParentSketchID,cancel);
            return distances.TryGetValue(f.ID,out var distance)?step with{Definition=step.Definition with{Distance=distance}}:step;
        }).ToArray();
        using var shape=ModelFeatureOracle.Tool(steps[0].Definition,cancel);
        ModelFeatureOracle.Read(shape);
        foreach(var step in steps.Skip(1))ModelPlacementHistory.Apply(shape,step,cancel);
        return ModelFeatureOracle.Read(shape);
    }
}
