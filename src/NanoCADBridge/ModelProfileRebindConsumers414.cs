using System.Text.Json;
using System.Text.Json.Nodes;
using Multicad;
using Multicad.DatabaseServices;
using Multicad.Mc3D;
using NanoCAD.Contracts;

namespace NanoCADBridge;

// Read complete typed forward uses. A singular profile backlink neither makes
// a shared profile exclusive nor authorizes transferring another consumer.
internal static class ModelProfileRebindConsumers414
{
    static void Need([System.Diagnostics.CodeAnalysis.DoesNotReturnIf(false)] bool ok,string message)
    {if(!ok)throw new DomainException("PROFILE_OWNERSHIP_MISMATCH",message);}
    static string Key(McObjectId id)=>id.ToString();
    internal static ModelProfileConsumers410.Snapshot Capture(SketchProfile profile,McDocument owner,Mc3dSolid body,
        McObjectId releasedConsumer,Action cancel)
    {
        Need(profile.DbEntity.Document?.ID==owner.ID,"The exact profile owner changed.");var uses=new List<ModelProfileConsumers410.Consumer>();
        void Observe(McObjectId id,HistoryItem? feature,string kind,McObjectId[] profiles)
        {
            cancel();Need(feature!=null&&feature.ID==id&&feature.DbEntity.Document?.ID==owner.ID,"A typed profile consumer inventory is unavailable.");
            if(!profiles.Contains(profile.ID))return;
            Need(feature.HostSolidID==body.ID&&(feature.Status.ToString()=="Ok"||ProjectedHistorySources.HealthySuppressed(feature))&&profiles.All(p=>!p.IsNull),
                "A real profile consumer has foreign ownership, unavailable bindings or unhealthy state.");
            uses.Add(new(Key(id),kind,Key(feature.HostSolidID),profiles.Select(Key).ToArray()));
        }
        foreach(var id in ModelProfileDiscovery348.Ids<ExtrudeFeature>(owner,cancel))
        {var item=id.GetObjectOfType<ExtrudeFeature>();Observe(id,item,"extrusion",item==null?[]:[item.ProfileID]);}
        foreach(var id in ModelProfileDiscovery348.Ids<RevolveFeature>(owner,cancel))
        {var item=id.GetObjectOfType<RevolveFeature>();Observe(id,item,"revolution",item==null?[]:[item.ProfileID]);}
        foreach(var id in ModelProfileDiscovery348.Ids<ExtrudeAlongPathFeature>(owner,cancel))
        {var item=id.GetObjectOfType<ExtrudeAlongPathFeature>();Observe(id,item,"sweep",item==null?[]:[item.ProfileID]);}
        foreach(var id in ModelProfileDiscovery348.Ids<LoftFeature>(owner,cancel))
        {
            var item=id.GetObjectOfType<LoftFeature>();Need(item!=null&&item.ID==id,"A typed loft consumer is unavailable.");
            Observe(id,item,"loft",ModelProfileLoftNative446.InventoryProfiles(item,cancel));
        }
        var consumers=uses.OrderBy(item=>item.FeatureId,StringComparer.OrdinalIgnoreCase).ToArray();string? backlink=ModelProfileDiscovery348.Id(profile.Dependent3dFeatureID);
        Need(backlink==null||consumers.Any(item=>item.FeatureId==backlink)||!releasedConsumer.IsNull&&backlink==Key(releasedConsumer),
            "The observed profile backlink is neither an actual forward consumer nor the exact selected consumer just released by this rebind.");
        var result=new ModelProfileConsumers410.Snapshot(Key(profile.ID),Key(profile.ParentSketchID),Key(owner.ID),
            ModelProfileDiscovery348.Id(profile.HostSolidID)??consumers.FirstOrDefault()?.BodyId,backlink,consumers);
        ModelProfileConsumers410.RequireBody(result,body);return result;
    }
    internal static void VerifyDelta(ModelProfileConsumers410.Snapshot before,ModelProfileConsumers410.Snapshot after,
        McObjectId selected,bool adding,Action cancel)
    {
        Need(before.ProfileId==after.ProfileId&&before.SketchId==after.SketchId&&before.DocumentId==after.DocumentId,
            "A retained profile or parent owner identity changed.");
        foreach(var prior in before.Consumers.Where(item=>item.FeatureId!=Key(selected)))
        {
            cancel();var current=after.Consumers.SingleOrDefault(item=>item.FeatureId==prior.FeatureId);
            Need(current!=null&&ModelParameterSnapshot.Equivalent(Wire.Element(prior),Wire.Element(current)),"An unselected real consumer or its ordered profile list changed.");
        }
        var expected=before.Consumers.Where(item=>item.FeatureId!=Key(selected)).Select(item=>item.FeatureId).ToHashSet(StringComparer.OrdinalIgnoreCase);
        if(adding)expected.Add(Key(selected));
        Need(expected.SetEquals(after.Consumers.Select(item=>item.FeatureId)),"The actual profile forward-consumer change exceeds the selected extrusion.");
    }
    internal static void RequireAcyclic(ExtrudeFeature selected,ModelProfileLifecycleState340.Snapshot next,string[] downstream,Action cancel)
    {
        var forbidden=downstream.ToHashSet(StringComparer.OrdinalIgnoreCase);forbidden.Add(Key(selected.ID));forbidden.Remove(Key(selected.HostSolidID));
        foreach(var identity in new[]{next.Profile.ID,next.Sketch.ID})
        {
            cancel();var item=identity.GetObjectOfType<HistoryItem>();Need(item!=null&&item.ID==identity,"The replacement profile/sketch lacks its exact native history identity.");
            Need(!forbidden.Contains(Key(item.ID)),"The replacement profile/sketch already depends on the selected extrusion.");
            var inputs=item.GetAllDependsOn(false);Need(inputs!=null,"The replacement profile/sketch upstream dependency inventory is unavailable; no acyclic claim is made.");
            ModelInventoryBudget.Ids(inputs,cancel);
            Need(!inputs.Any(id=>forbidden.Contains(Key(id))),"The replacement profile/sketch uses the selected extrusion or one of its downstream consumers; rebind would create a native cycle.");
        }
    }
    internal static bool VariablesRetained(JsonElement before,JsonElement after,ModelProfileLifecycleState340.Snapshot old,
        ModelProfileLifecycleState340.Snapshot next,IReadOnlySet<string> beforeAffected,IReadOnlySet<string> afterAffected,Action cancel)
    {
        var oldRows=before.EnumerateArray().ToDictionary(row=>row.GetProperty("mc_id").GetString()!,StringComparer.OrdinalIgnoreCase);
        var newRows=after.EnumerateArray().ToDictionary(row=>row.GetProperty("mc_id").GetString()!,StringComparer.OrdinalIgnoreCase);
        if(!oldRows.Keys.ToHashSet(StringComparer.OrdinalIgnoreCase).SetEquals(newRows.Keys))return false;
        HashSet<string> SketchDrivers(string sketch)
        {
            var result=oldRows.Where(pair=>new[]{"host_object_id","sketch_id"}.Any(field=>pair.Value.TryGetProperty(field,out var value)&&value.GetString()==sketch)).Select(pair=>pair.Key).ToHashSet(StringComparer.OrdinalIgnoreCase);
            bool changed;
            do
            {
                changed=false;foreach(var pair in oldRows)
                {
                    cancel();if(result.Contains(pair.Key))continue;var refs=pair.Value.GetProperty("downstream_variables");
                    if(refs.ValueKind==JsonValueKind.Array&&refs.EnumerateArray().Any(value=>result.Contains(value.GetString()!)))changed|=result.Add(pair.Key);
                }
            }while(changed);
            return result;
        }
        var oldDrivers=SketchDrivers(Key(old.Sketch.ID));var nextDrivers=SketchDrivers(Key(next.Sketch.ID));
        foreach(var pair in oldRows)
        {
            cancel();var prior=JsonNode.Parse(pair.Value.GetRawText())!.AsObject();var current=JsonNode.Parse(newRows[pair.Key].GetRawText())!.AsObject();
            foreach(string field in new[]{"downstream_variables","downstream_objects"})
            {
                if(prior[field] is not JsonArray a||current[field] is not JsonArray b)
                {if(prior[field]?.ToJsonString()!=current[field]?.ToJsonString())return false;continue;}
                var left=a.Select(value=>value!.GetValue<string>()).ToHashSet(StringComparer.OrdinalIgnoreCase);var right=b.Select(value=>value!.GetValue<string>()).ToHashSet(StringComparer.OrdinalIgnoreCase);
                var removed=left.Except(right,StringComparer.OrdinalIgnoreCase).ToArray();var added=right.Except(left,StringComparer.OrdinalIgnoreCase).ToArray();
                if((removed.Length>0||added.Length>0)&&(field!="downstream_objects"||
                    removed.Length>0&&(!oldDrivers.Contains(pair.Key)||removed.Any(id=>!beforeAffected.Contains(id)))||
                    added.Length>0&&(!nextDrivers.Contains(pair.Key)||added.Any(id=>!afterAffected.Contains(id)))))return false;
                var retained=left.Intersect(right,StringComparer.OrdinalIgnoreCase).OrderBy(id=>id,StringComparer.OrdinalIgnoreCase).ToArray();
                prior[field]=JsonSerializer.SerializeToNode(retained,Wire.Json);current[field]=JsonSerializer.SerializeToNode(retained,Wire.Json);
            }
            if(!ModelParameterSnapshot.Equivalent(Wire.Element(prior),Wire.Element(current)))return false;
        }
        return true;
    }
}
