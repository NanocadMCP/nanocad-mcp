using System.Text.Json;
using System.Text.Json.Nodes;
using Multicad;
using Multicad.DatabaseServices;
using Multicad.Mc3D;
using NanoCAD.Contracts;
using Db=Teigha.DatabaseServices;

namespace NanoCADBridge;

// Public design observations for a one-feature suppression inside the history.
// No opaque native storage/cache equality and no inferred suppression cascade.
internal static class ModelFeatureSuppressionState414
{
    internal sealed record Snapshot(JsonElement Model,JsonElement Construction,
        Dictionary<string,JsonElement?> Constraints,HashSet<string> Affected,string[] Warnings,HashSet<string>? Pending=null);
    static void Need([System.Diagnostics.CodeAnalysis.DoesNotReturnIf(false)] bool ok,string code,string message)
    {if(!ok)throw new DomainException(code,message);}
    static string Key(McObjectId id)=>id.ToString();
    static string[] Ids(IEnumerable<McObjectId> ids,Action cancel)
    {ModelInventoryBudget.Ids(ids,cancel);return ids.Select(Key).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(id=>id,StringComparer.OrdinalIgnoreCase).ToArray();}
    internal static Snapshot Capture(Mc3dSolid body,Db.ObjectId representation,McObjectId selected,ModelViewFollowers.Snapshot views,
        ModifyArguments args,DocumentState state,Db.Transaction tx,Action cancel,ModelFeatureSuppressionRecovery420.Context? recovery=null)
    {
        var owner=ModelProfileDiscovery348.OwnerDocument(body,state);var contents=ModelInventoryBudget.History(body,cancel).ToArray();
        var read=new Arguments(args.BridgeId,args.DocumentId,args.ExpectedRevision,100,null,null,null,[Validation.NormalizeHandle(representation.Handle.ToString())]);
        var model=((Db.BlockTable)tx.GetObject(state.Document.Database.BlockTableId,Db.OpenMode.ForRead))[Db.BlockTableRecord.ModelSpace];
        var history=Wire.Element(ModelHistoryReader.Read(read,state,tx,model,args.BridgeId,CancellationToken.None,cancel)).GetProperty("items")[0];
        ProjectedHistorySources.Source[]? external=null;NativePathSources.Inspection? recoverySources=null;
        if(recovery!=null)
        {
            external=ProjectedHistorySources.Read(body,contents,state,tx,cancel);
            recoverySources=ModelFeatureSuppressionRecovery420.RequireHistory(body,contents,external,recovery,cancel);
        }
        Need(recovery!=null||history.GetProperty("complete").GetBoolean(),"HISTORY_UNAVAILABLE","Nonterminal suppression needs complete typed history and actual native source bindings.");
        // GetPartContents is an inventory, not native sequence order. Assigned
        // predecessor links stay in each node and are verified independently.
        var orderedNodes=recovery==null?Wire.Element(history.GetProperty("nodes").EnumerateArray().OrderBy(node=>node.GetProperty("mc_id").GetString(),StringComparer.OrdinalIgnoreCase).ToArray()):
            ModelFeatureSuppressionRecovery420.ObservedNodes(history.GetProperty("nodes"),contents,recoverySources!,external!,cancel);
        var nodes=ModelDependencyGraph410.Nodes(orderedNodes,contents,cancel);
        var variables=Wire.Element(ModelVariablesReader.Read(read,state,tx,model,args.BridgeId,CancellationToken.None,cancel));
        var variableStates=new List<JsonElement>();var budget=new ModelInventoryBudget(cancel);
        foreach(var variable in variables.GetProperty("variables").EnumerateArray())
        {
            cancel();var value=JsonNode.Parse(variable.GetRawText())!.AsObject();
            foreach(string field in new[]{"downstream_variables","downstream_objects"})
                if(value[field] is JsonArray refs)value[field]=JsonSerializer.SerializeToNode(refs.Select(id=>id!.GetValue<string>()).OrderBy(id=>id,StringComparer.OrdinalIgnoreCase).ToArray(),Wire.Json);
            variableStates.Add(Wire.Element(value));
        }
        external??=ProjectedHistorySources.Read(body,contents,state,tx,cancel);
        if(recovery==null)ProjectedHistorySources.Healthy(contents,external);
        var design=Wire.Element(new{body=Key(body.ID),last_item=Key(body.LastPartItemID),owner=Key(owner.ID),
            variables_owner=variables.GetProperty("mc_document_id"),variables=variableStates.OrderBy(value=>value.GetProperty("mc_id").GetString(),StringComparer.OrdinalIgnoreCase).ToArray(),
            external_sources=external,native_source_ids=(recovery==null?NativePathSources.Read(contents,cancel):recoverySources!.Sources).OrderBy(id=>id,StringComparer.OrdinalIgnoreCase).ToArray(),nodes});
        var sketches=new List<object>();var profiles=new List<object>();var observedSketches=new HashSet<McObjectId>();
        var constraints=new Dictionary<string,JsonElement?>(StringComparer.OrdinalIgnoreCase);
        var warnings=new List<string>();
        JsonElement ObserveSketch(PlanarSketch sketch)
        {
            Need(sketch.DbEntity.Document?.ID==owner.ID&&(sketch.HostSolidID.IsNull||sketch.HostSolidID==body.ID)&&!sketch.IsInEditMode,
                "SKETCH_OWNERSHIP_MISMATCH","A retained sketch needs its actual same-owner document construction or selected-part affinity outside edit mode.");
            var geometry=Wire.Element(SketchGeometryReader.Read(sketch,cancel,budget.Add));
            Need(geometry.GetProperty("geometry_details_complete").GetBoolean(),"SKETCH_GEOMETRY_UNAVAILABLE","Retained sketch geometry is incomplete.");
            if(!observedSketches.Add(sketch.ID))return geometry;
            var containing=sketch.GetContainingObjects(true,true,true);
            Need(containing!=null,"SKETCH_BINDING_UNAVAILABLE","Retained sketch membership is unavailable.");
            sketches.Add(new{id=Key(sketch.ID),owner=Key(owner.ID),host=ModelProfileDiscovery348.Id(sketch.HostSolidID),name=sketch.Name,
                support=ModelProfileDiscovery348.Id(sketch.PlanarEntityID),geometry,containing_ids=Ids(containing,cancel)});
            JsonElement? observed=null;
            try{observed=ModelSketchConstraintObservation353.Read(body,sketch,cancel);}
            catch(OperationCanceledException){throw;}
            catch(Exception){warnings.Add(ModelSketchConstraintObservation353.Warning);}
            constraints.Add(Key(sketch.ID),observed);
            if(observed.HasValue&&!ModelSketchConstraintObservation353.Complete(observed.Value))warnings.Add(ModelSketchConstraintObservation353.Warning);
            return geometry;
        }
        foreach(var id in contents.OrderBy(Key,StringComparer.OrdinalIgnoreCase))
        {
            cancel();var item=id.GetObjectOfType<HistoryItem>();
            if(item==null)continue; // verified native sources were checked above
            bool documentConstruction=item.HostSolidID.IsNull&&(id.GetObjectOfType<PlanarSketch>()!=null||id.GetObjectOfType<SketchProfile>()!=null);
            Need(item.ID==id&&(item.HostSolidID==body.ID||documentConstruction)&&item.DbEntity.Document?.ID==owner.ID&&!item.IsBeyondEndOfPart,
                "DEPENDENCY_OWNERSHIP_MISMATCH","Every retained history item must remain in this actual part owner before End-of-Part.");
            if(ModelFeatureReorderState345.Forming(id))
                Need(nodes.EnumerateArray().Single(node=>node.GetProperty("mc_id").GetString()==Key(id)).GetProperty("parameters").ValueKind==JsonValueKind.Object,
                    "MODEL_PARAMETERS_UNAVAILABLE","A retained forming feature lacks its public typed design observation.");
            if(id.GetObjectOfType<PlanarSketch>() is {} sketch)
                ObserveSketch(sketch);
            if(id.GetObjectOfType<SketchProfile>() is {} profile)
            {
                var parent=profile.ParentSketchID.GetObjectOfType<PlanarSketch>();
                Need(parent!=null&&parent.ID==profile.ParentSketchID&&parent.DbEntity.Document?.ID==owner.ID&&
                    (parent.HostSolidID.IsNull||parent.HostSolidID==body.ID),
                    "PROFILE_OWNERSHIP_MISMATCH","A retained profile lacks its actual same-owner parent sketch or has foreign part affinity.");
                var consumers=ModelProfileConsumers410.Capture(profile,owner,cancel,recovery?.Affected);ModelProfileConsumers410.RequireBody(consumers,body);
                var sketchGeometry=ObserveSketch(parent);
                var selectedAreas=profile.GetContours(false);Need(selectedAreas!=null,"PROFILE_GEOMETRY_UNAVAILABLE","Retained selected profile areas are unavailable.");
                var areas=new List<object>();
                try
                {
                    foreach(var area in selectedAreas)
                    {
                        cancel();Need(area!=null,"PROFILE_GEOMETRY_UNAVAILABLE","A retained selected area is unavailable.");using var outer=area.External;
                        var boundary=ModelProfileElementsReader358.Loop(outer,sketchGeometry.GetProperty("frame"),cancel);
                        var holes=area.Internal;Need(holes!=null,"PROFILE_GEOMETRY_UNAVAILABLE","Retained profile hole inventory is unavailable.");
                        try{areas.Add(new{outer=boundary,holes=holes.Select(hole=>{cancel();return ModelProfileElementsReader358.Loop(hole,sketchGeometry.GetProperty("frame"),cancel);}).ToArray()});}
                        finally{foreach(var hole in holes.Distinct(ReferenceEqualityComparer.Instance))if(hole is IDisposable disposable)disposable.Dispose();}
                    }
                }
                finally{foreach(var area in selectedAreas.Distinct(ReferenceEqualityComparer.Instance))if(area is IDisposable disposable)disposable.Dispose();}
                object nativePlane;
                using(var plane=profile.Plane)nativePlane=new{origin=new[]{plane.Origin.X,plane.Origin.Y,plane.Origin.Z},normal=new[]{plane.Normal.X,plane.Normal.Y,plane.Normal.Z}};
                profiles.Add(new{profile_id=Key(id),parent_sketch_id=Key(profile.ParentSketchID),owner=Key(owner.ID),host=ModelProfileDiscovery348.Id(profile.HostSolidID),
                    name=profile.Name,consumer=ModelProfileDiscovery348.Id(profile.Dependent3dFeatureID),forward_consumers=consumers.Consumers,
                    geometry=sketchGeometry,profile_geometry=new{areas},native_plane=nativePlane});
            }
        }
        // Reactor-derived closure establishes actual downstream ownership. It
        // is not used as a deletion list or as permission to suppress peers.
        // SDK GetCritical* describes erase/existence propagation, not whether
        // the distinct Suppress(false) operation requires suppressing a peer.
        if(recovery!=null)ModelFeatureSuppressionRecovery420.RequireClosureOwnership(body,selected,contents,views,cancel);
        var affected=recovery?.Affected??ModelDependencyGraph410.OwnedClosure(body,[selected],cancel,views);
        var result=new Snapshot(design,Wire.Element(new{nodes,sketches,profiles}),constraints,affected,warnings.Distinct(StringComparer.Ordinal).ToArray(),recovery?.Pending);
        budget.Add(new{result.Model,result.Construction});return result;
    }
    static JsonElement Protected(Snapshot snapshot,IReadOnlySet<string> affected,McObjectId selected,Action cancel)
    {
        var result=JsonNode.Parse(snapshot.Model.GetRawText())!.AsObject();
        var nodes=JsonNode.Parse(ModelDependencyGraph410.ProtectedNodes(snapshot.Model,affected,cancel:cancel).GetRawText())!.AsArray();
        foreach(var node in nodes)
        {
            cancel();string id=node!["mc_id"]!.GetValue<string>();
            if(snapshot.Pending?.Contains(id)==true)node["status"]="pending_recovery_verified_separately";
            if(id==Key(selected)){node["suppressed"]=null;node["status"]="selected_suppression_verified_separately";}
        }
        result["nodes"]=nodes;return Wire.Element(result);
    }
    internal static string[] Verify(Snapshot before,Snapshot after,McObjectId selected,Action cancel)
    {
        Need(ModelParameterSnapshot.Equivalent(Protected(before,before.Affected,selected,cancel),Protected(after,before.Affected,selected,cancel)),
            "MODEL_STATE_UNAVAILABLE","Nonterminal suppression changed a retained feature/source/profile/driver/formula link, an unselected suppression choice or native order.");
        // Native topology/profile geometry can rebuild when an earlier feature
        // is omitted. Retain actual member/support/consumer and loop topology,
        // while the existing affected-graph adapter permits derived motion.
        var stationary=before.Construction.GetProperty("sketches").EnumerateArray().Select(row=>row.GetProperty("id").GetString()!)
            .Where(id=>!before.Affected.Contains(id)).ToHashSet(StringComparer.OrdinalIgnoreCase);
        JsonElement Construction(Snapshot value)
        {
            var retained=JsonNode.Parse(ModelDependencyGraph410.ProtectPartDerived(value.Construction,before.Affected,stationary,cancel).GetRawText())!.AsObject();
            retained.Remove("nodes");return Wire.Element(retained);
        }
        Need(ModelParameterSnapshot.Equivalent(Construction(before),Construction(after)),"MODEL_STATE_UNAVAILABLE","Nonterminal suppression changed retained sketch membership, profile region topology, parent/consumer ownership or independent construction geometry.");
        Need(before.Constraints.Keys.ToHashSet(StringComparer.OrdinalIgnoreCase).SetEquals(after.Constraints.Keys),"MODEL_STATE_UNAVAILABLE","Retained sketch constraint scopes changed.");
        var warnings=before.Warnings.Concat(after.Warnings).ToList();
        foreach(var pair in before.Constraints)
        {
            cancel();var current=after.Constraints[pair.Key];
            if(pair.Value.HasValue&&current.HasValue)
            {
                var previous=before.Affected.Contains(pair.Key)?ModelDependencyGraph410.ConstraintPoints(pair.Value.Value,cancel):pair.Value.Value;
                var actual=before.Affected.Contains(pair.Key)?ModelDependencyGraph410.ConstraintPoints(current.Value,cancel):current.Value;
                Need(ModelSketchConstraintObservation353.Equivalent(previous,actual,cancel),"MODEL_STATE_UNAVAILABLE","An observed retained sketch constraint or its actual scope changed.");
            }
            else warnings.Add(ModelSketchConstraintObservation353.Warning);
        }
        return warnings.Distinct(StringComparer.Ordinal).ToArray();
    }
}
