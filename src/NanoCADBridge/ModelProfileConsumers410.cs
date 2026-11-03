using System.Text.Json;
using System.Text.Json.Nodes;
using Multicad;
using Multicad.DatabaseServices;
using Multicad.Mc3D;
using NanoCAD.Contracts;

namespace NanoCADBridge;

// A profile can be reused by retained native features. Sharing adds a forward
// binding; transferring/removing a consumer requires a separate explicit edit.
internal static class ModelProfileConsumers410
{
    internal sealed record Consumer(string FeatureId,string Kind,string BodyId,string[] Profiles);
    internal sealed record Snapshot(string ProfileId,string SketchId,string DocumentId,string? HostBodyId,string? Backlink,Consumer[] Consumers);
    private static void Need([System.Diagnostics.CodeAnalysis.DoesNotReturnIf(false)] bool ok,string code,string message){if(!ok)throw new DomainException(code,message);}
    private static string? Key(McObjectId id)=>id.IsNull?null:id.ToString();
    internal static Snapshot Capture(SketchProfile selected,McDocument owner,Action cancel,IReadOnlySet<string>? admittedPendingIds=null)
    {
        Need(!selected.ID.IsNull&&selected.DbEntity.Document?.ID==owner.ID,"PROFILE_OWNERSHIP_MISMATCH","The selected profile must retain its exact native owner document.");
        var sketch=selected.ParentSketchID.GetObjectOfType<PlanarSketch>();
        Need(sketch!=null&&sketch.ID==selected.ParentSketchID&&sketch.DbEntity.Document?.ID==owner.ID&&!sketch.IsInEditMode,
            "PROFILE_OWNERSHIP_MISMATCH","The selected profile's actual parent sketch/document is unavailable or in edit mode.");
        var result=new List<Consumer>();var bytes=new ModelInventoryBudget(cancel);
        void Add(McObjectId id,HistoryItem? feature,string kind,McObjectId[] profiles)
        {
            cancel();Need(feature!=null&&feature.ID==id&&feature.DbEntity.Document?.ID==owner.ID,"PROFILE_DISCOVERY_UNAVAILABLE","Complete actual forming-feature/profile bindings are unavailable.");
            if(!profiles.Contains(selected.ID))return;
            Need(!profiles.Any(p=>p.IsNull),"PROFILE_DISCOVERY_UNAVAILABLE","An actual consumer has an incomplete ordered profile binding.");
            Need(feature!.Status.ToString()=="Ok"||ProjectedHistorySources.HealthySuppressed(feature)||
                feature.Status.ToString()=="NeedUpdate"&&admittedPendingIds?.Contains(id.ToString())==true,
                "MODEL_STATE_UNAVAILABLE","An existing profile consumer is unhealthy or pending outside the selected recovery closure.");
            Need(!feature.HostSolidID.IsNull,"PROFILE_OWNERSHIP_MISMATCH","A consumed profile lacks an actual native part owner.");
            var row=new Consumer(id.ToString(),kind,feature.HostSolidID.ToString(),profiles.Select(p=>p.ToString()).ToArray());bytes.Add(row);result.Add(row);
        }
        foreach(var id in ModelProfileDiscovery348.Ids<ExtrudeFeature>(owner,cancel))
        {var value=id.GetObjectOfType<ExtrudeFeature>();Add(id,value,"extrusion",value==null?[]:[value.ProfileID]);}
        foreach(var id in ModelProfileDiscovery348.Ids<RevolveFeature>(owner,cancel))
        {var value=id.GetObjectOfType<RevolveFeature>();Add(id,value,"revolution",value==null?[]:[value.ProfileID]);}
        foreach(var id in ModelProfileDiscovery348.Ids<ExtrudeAlongPathFeature>(owner,cancel))
        {var value=id.GetObjectOfType<ExtrudeAlongPathFeature>();Add(id,value,"sweep",value==null?[]:[value.ProfileID]);}
        foreach(var id in ModelProfileDiscovery348.Ids<LoftFeature>(owner,cancel))
        {
            cancel();var value=id.GetObjectOfType<LoftFeature>();Need(value!=null&&value.ID==id,"PROFILE_DISCOVERY_UNAVAILABLE","A native loft consumer is unavailable.");
            // SDK SectionsCount includes the base profile; managed GetSection
            // forwards the native index without adjusting it.
            Add(id,value,"loft",ModelProfileLoftNative446.InventoryProfiles(value!,cancel));
        }
        var consumers=result.OrderBy(row=>row.FeatureId,StringComparer.OrdinalIgnoreCase).ToArray();
        string? host=Key(selected.HostSolidID)??Key(sketch!.HostSolidID)??consumers.FirstOrDefault()?.BodyId;
        Need(consumers.All(c=>string.Equals(c.BodyId,host,StringComparison.OrdinalIgnoreCase)),"PROFILE_OWNERSHIP_MISMATCH","Profile sharing across different native parts is outside the selected single-part graph.");
        if(!selected.Dependent3dFeatureID.IsNull)
            Need(consumers.Any(c=>c.FeatureId.Equals(selected.Dependent3dFeatureID.ToString(),StringComparison.OrdinalIgnoreCase)),"PROFILE_OWNERSHIP_MISMATCH","The retained profile backlink is not one of its actual forward consumers.");
        return new(selected.ID.ToString(),sketch.ID.ToString(),owner.ID.ToString(),host,Key(selected.Dependent3dFeatureID),consumers);
    }
    internal static void RequireBody(Snapshot snapshot,Mc3dSolid body)
    {Need(snapshot.HostBodyId==null||snapshot.HostBodyId.Equals(body.ID.ToString(),StringComparison.OrdinalIgnoreCase),"PROFILE_OWNERSHIP_MISMATCH","The actual profile consumers belong to another native part.");}
    internal static void VerifyRetained(Snapshot before,SketchProfile selected,McDocument owner,Action cancel,
        McObjectId changingConsumer=default,McObjectId addedConsumer=default)
    {
        var after=Capture(selected,owner,cancel);
        Need(before.ProfileId==after.ProfileId&&before.SketchId==after.SketchId&&before.DocumentId==after.DocumentId&&
            (before.HostBodyId==null||before.HostBodyId==after.HostBodyId),"PROFILE_OWNERSHIP_MISMATCH","Sharing/rebinding changed the retained native profile, sketch or existing part owner.");
        string? changing=Key(changingConsumer),added=Key(addedConsumer);
        foreach(var consumer in before.Consumers)
        {
            cancel();if(consumer.FeatureId==changing)continue;
            var current=after.Consumers.SingleOrDefault(c=>c.FeatureId==consumer.FeatureId);
            Need(current!=null&&ModelParameterSnapshot.Equivalent(Wire.Element(consumer),Wire.Element(current)),"PROFILE_OWNERSHIP_MISMATCH","An unselected existing profile consumer or one of its ordered profiles changed.");
        }
        var old=before.Consumers.Select(c=>c.FeatureId).ToHashSet(StringComparer.OrdinalIgnoreCase);
        Need(after.Consumers.Where(c=>!old.Contains(c.FeatureId)).All(c=>c.FeatureId==added||c.FeatureId==changing),"PROFILE_OWNERSHIP_MISMATCH","The profile acquired an undeclared forming consumer.");
        if(added!=null)Need(after.Consumers.Any(c=>c.FeatureId==added),"PROFILE_OWNERSHIP_MISMATCH","The new native feature did not retain this exact selected profile.");
    }
    internal static bool RetainedVariable(JsonElement before,JsonElement after,IReadOnlySet<string> addedConsumers,Action cancel)
    {
        var old=JsonNode.Parse(before.GetRawText())!.AsObject();var current=JsonNode.Parse(after.GetRawText())!.AsObject();
        foreach(string field in new[]{"downstream_objects","downstream_variables"})
        {
            cancel();if(old[field] is not JsonArray prior||current[field] is not JsonArray observed)return false;
            var kept=prior.Select(id=>id!.GetValue<string>()).ToHashSet(StringComparer.OrdinalIgnoreCase);
            var now=observed.Select(id=>id!.GetValue<string>()).ToHashSet(StringComparer.OrdinalIgnoreCase);
            if(!kept.IsSubsetOf(now)||now.Except(kept,StringComparer.OrdinalIgnoreCase).Any(id=>field!="downstream_objects"||!addedConsumers.Contains(id)))return false;
            old[field]=JsonSerializer.SerializeToNode(kept.OrderBy(id=>id,StringComparer.OrdinalIgnoreCase).ToArray(),Wire.Json);
            current[field]=JsonSerializer.SerializeToNode(kept.OrderBy(id=>id,StringComparer.OrdinalIgnoreCase).ToArray(),Wire.Json);
        }
        return ModelParameterSnapshot.Equivalent(Wire.Element(old),Wire.Element(current));
    }
}
