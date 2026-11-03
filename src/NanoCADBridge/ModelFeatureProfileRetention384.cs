using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using System.Text.Json.Nodes;
using Multicad;
using Multicad.DatabaseServices;
using Multicad.Mc3D;
using NanoCAD.Contracts;
using Db=Teigha.DatabaseServices;

namespace NanoCADBridge;

// The two public typed profile setters release only this explicitly selected
// consumer. Native Erase cleanup remains observed, not a keep-used guarantee.
internal static class ModelFeatureProfileRetention384
{
    internal const string Warning="NATIVE_PROFILE_RETENTION: This erase first releases the selected extrusion/revolution's exact profile links. Retained profile/sketch/source/control data are verified at native commit; later reuse requires a fresh current profile read. Native auxiliary cleanup, Undo and rollback are not guaranteed.";
    internal sealed record Binding(ModelProfileLifecycleState340.Snapshot Profile,JsonElement Dependents,bool Autoerase);
    static void Need([DoesNotReturnIf(false)] bool ok,string code,string message)=>ModelFeatureDeletionState378.Need(ok,code,message);
    static string Key(McObjectId id)=>id.ToString();
    static bool Same(string? a,string? b)=>string.Equals(a,b,StringComparison.OrdinalIgnoreCase);
    internal static McObjectId ProfileId(McObjectId feature)=>feature.GetObjectOfType<ExtrudeFeature>() is {} extrusion?extrusion.ProfileID:
        feature.GetObjectOfType<RevolveFeature>() is {} revolution?revolution.ProfileID:McObjectId.Null;
    internal static string[] Owners(McDocument owner,McObjectId profile,Action cancel)
    {
        var consumers=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        void Observe(HistoryItem? item,McObjectId id,bool uses)
        {
            cancel();Need(item!=null&&item.ID==id&&item.DbEntity.Document!=null&&item.DbEntity.Document.ID==owner.ID,
                "PROFILE_DISCOVERY_UNAVAILABLE","A current typed native profile-consumer inventory item has no exact owner identity.");
            if(uses)consumers.Add(Key(id));
        }
        foreach(var id in ModelProfileDiscovery348.Ids<ExtrudeFeature>(owner,cancel))
        {var item=id.GetObjectOfType<ExtrudeFeature>();Observe(item,id,item!=null&&item.ProfileID==profile);}
        foreach(var id in ModelProfileDiscovery348.Ids<RevolveFeature>(owner,cancel))
        {var item=id.GetObjectOfType<RevolveFeature>();Observe(item,id,item!=null&&item.ProfileID==profile);}
        foreach(var id in ModelProfileDiscovery348.Ids<ExtrudeAlongPathFeature>(owner,cancel))
        {var item=id.GetObjectOfType<ExtrudeAlongPathFeature>();Observe(item,id,item!=null&&item.ProfileID==profile);}
        foreach(var id in ModelProfileDiscovery348.Ids<LoftFeature>(owner,cancel))
        {
            var item=id.GetObjectOfType<LoftFeature>();bool uses=item!=null&&item.ProfileID==profile;
            if(item!=null)for(int index=0;index<item.SectionsCount;index++)
            {cancel();var section=item.GetSection(index);Need(section!=null,"PROFILE_DISCOVERY_UNAVAILABLE","A current loft profile section is unavailable.");uses|=section.ID==profile;}
            Observe(item,id,uses);
        }
        return consumers.OrderBy(id=>id,StringComparer.OrdinalIgnoreCase).ToArray();
    }
    internal static Binding Capture(ModelFeatureReorderState345.Snapshot part,McObjectId feature,McDocument owner,
        DocumentState state,Db.Transaction tx,Action cancel)
    {
        var id=ProfileId(feature);Need(!id.IsNull,"PROFILE_OWNERSHIP_MISMATCH","The selected typed profile-consuming feature has no current profile.");
        var profile=ModelProfileDiscovery348.Resolve(Key(id),owner,cancel);
        var value=ModelProfileLifecycleState340.Capture(profile,part.Body,state,tx,cancel,allowCircularHoles:true);
        Need(profile.Dependent3dFeatureID==feature&&profile.HostSolidID==part.Body.ID&&value.Sketch.HostSolidID==part.Body.ID&&
            part.Contents.Contains(profile.ID)&&part.Contents.Contains(value.Sketch.ID)&&Owners(owner,id,cancel).SequenceEqual(new[]{Key(feature)},StringComparer.OrdinalIgnoreCase),
            "PROFILE_OWNERSHIP_MISMATCH","The selected feature must be the exact sole native consumer of its actual same-part profile and parent sketch.");
        var direct=profile.GetDependent();var all=profile.GetAllDependent(false);
        Need(direct!=null&&all!=null,"DEPENDENCY_INVENTORY_UNAVAILABLE","The selected retained profile needs actual complete current dependent inventories.");
        ModelInventoryBudget.Ids(direct,cancel);ModelInventoryBudget.Ids(all,cancel);
        foreach(var dependent in direct.Concat(all))
        {
            cancel();Need(dependent==part.Body.ID||part.Contents.Contains(dependent),"DEPENDENCY_OWNERSHIP_MISMATCH","The retained profile has a foreign or unclassified dependent.");
            if(dependent==part.Body.ID)continue;
            var item=dependent.GetObjectOfType<HistoryItem>();
            Need(item!=null&&item.ID==dependent&&item.HostSolidID==part.Body.ID&&item.DbEntity.Document!=null&&item.DbEntity.Document.ID==owner.ID,
                "DEPENDENCY_OWNERSHIP_MISMATCH","A retained profile dependent lacks exact same-part native ownership.");
            Need(dependent==feature||!ModelFeatureReorderState345.Forming(dependent),"OUT_OF_SCOPE","Another forming feature depends on the selected retained profile; this is one leaf consumer release.");
        }
        var dependencies=Wire.Element(new{direct=direct.Select(Key).OrderBy(id=>id,StringComparer.OrdinalIgnoreCase).ToArray(),all=all.Select(Key).OrderBy(id=>id,StringComparer.OrdinalIgnoreCase).ToArray()});
        new ModelInventoryBudget(cancel).Add(dependencies);
        return new(value,dependencies,value.Sketch.AutoeraseOfInclObjects);
    }
    internal static ModelFeatureDeletionProfile384 Describe(Binding binding,string disposition)
    {
        var value=binding.Profile;
        return new(Key(value.Profile.ID),Key(value.Sketch.ID),value.Stored.GetProperty("mc_document_id").GetString()!,value.Token,
            value.Stored.GetProperty("profile_host_solid_id").GetString(),value.Stored.GetProperty("sketch_host_solid_id").GetString(),
            value.Sources.Select(source=>source.id).ToArray(),value.ProjectionIds.Select(Key).ToArray(),disposition);
    }
    internal const string BeforeDisposition="explicit_profile_and_sketch_retention_after_selected_consumer_release";
    internal const string AfterDisposition="unused_same_owner_profile_and_sketch_retained;_refresh_before_reuse";
    internal static void Release(Binding binding,McObjectId feature,Action cancel)
    {
        cancel();var profile=binding.Profile.Profile;var id=profile.ID;
        Need(ProfileId(feature)==id&&profile.Dependent3dFeatureID==feature,"PRECONDITION_FAILED","The selected forward/backward profile links changed before release.");
        // No UpdateAll occurs between the temporary unset consumer and Erase.
        // The unconsumed Null state exists in normal native construction; the
        // SDK exposes these setters but does not promise erase retention.
        if(feature.GetObjectOfType<ExtrudeFeature>() is {} extrusion)
        {extrusion.TryModify((uint)McDbObject.ObjChangesType.Geom);extrusion.ProfileID=McObjectId.Null;}
        else if(feature.GetObjectOfType<RevolveFeature>() is {} revolution)
        {revolution.TryModify((uint)McDbObject.ObjChangesType.Geom);revolution.ProfileID=McObjectId.Null;}
        else throw new DomainException("PROFILE_OWNERSHIP_MISMATCH","The selected typed profile consumer disappeared before release.");
        profile.TryModify((uint)McDbObject.ObjChangesType.Geom);profile.Dependent3dFeatureID=McObjectId.Null;
        Need(ProfileId(feature).IsNull&&profile.Dependent3dFeatureID.IsNull&&profile.ParentSketchID==binding.Profile.Sketch.ID,
            "PROFILE_RELEASE_FAILED","The native setters did not release the exact selected consumer links.");
        var critical=profile.GetCriticalDependsOn();var criticalAll=profile.GetCriticalAllDependsOn();
        Need(critical!=null&&criticalAll!=null,"DEPENDENCY_INVENTORY_UNAVAILABLE","The released profile critical inventories are unavailable; Erase is not called.");
        ModelInventoryBudget.Ids(critical,cancel);ModelInventoryBudget.Ids(criticalAll,cancel);
        Need(!critical.Concat(criticalAll).Contains(feature),"PROFILE_RELEASE_FAILED",
            "The retained profile still critically requires the selected feature after link release; Erase is not called.");
    }
    internal static JsonElement Expected(JsonElement design,Binding? binding,McObjectId feature,Action cancel)
    {
        if(binding==null)return design;
        var value=JsonNode.Parse(design.GetRawText())!.AsObject();string profile=Key(binding.Profile.Profile.ID),selected=Key(feature);
        foreach(var row in value["profiles"]!.AsArray())
        {
            cancel();if(!Same(row!["profile_id"]?.GetValue<string>(),profile))continue;
            Need(Same(row["dependent_feature_id"]?.GetValue<string>(),selected),"PROFILE_OWNERSHIP_MISMATCH","The retained profile snapshot does not identify the selected consumer.");
            row["dependent_feature_id"]=null;Remove(row["dependencies"] as JsonArray,selected,cancel);
        }
        foreach(var node in value["nodes"]!.AsArray())
        {cancel();if(Same(node!["mc_id"]?.GetValue<string>(),profile))Remove(node["dependencies"] as JsonArray,selected,cancel);}
        return Wire.Element(value);
    }
    static void Remove(JsonArray? values,string selected,Action cancel)
    {if(values==null)return;for(int index=values.Count-1;index>=0;index--){cancel();if(Same(values[index]?.GetValue<string>(),selected))values.RemoveAt(index);}}
    internal static JsonElement ExpectedCritical(JsonElement observations,Binding? binding,McObjectId feature,Action cancel)
    {
        var values=JsonNode.Parse(observations.GetRawText())!.AsArray();string selected=Key(feature);
        for(int index=values.Count-1;index>=0;index--)
        {
            cancel();var row=values[index]!;string? id=row["id"]?.GetValue<string>();if(Same(id,selected)){values.RemoveAt(index);continue;}
            if(binding!=null&&Same(id,Key(binding.Profile.Profile.ID)))
            {Remove(row["critical"] as JsonArray,selected,cancel);Remove(row["critical_all"] as JsonArray,selected,cancel);}
        }
        return Wire.Element(values);
    }
    internal static Binding Verify(Binding before,ModelFeatureReorderState345.Snapshot after,McObjectId removed,
        DocumentState state,Db.Transaction tx,Action cancel)
    {
        var owner=ModelProfileDiscovery348.OwnerDocument(after.Body,state);
        var profile=ModelProfileDiscovery348.Resolve(Key(before.Profile.Profile.ID),owner,cancel);
        var current=ModelProfileLifecycleState340.Capture(profile,after.Body,state,tx,cancel,allowCircularHoles:true);
        Need(profile.ID==before.Profile.Profile.ID&&profile.ParentSketchID==before.Profile.Sketch.ID&&profile.Dependent3dFeatureID.IsNull&&
            profile.HostSolidID==after.Body.ID&&current.Sketch.HostSolidID==after.Body.ID&&current.Sketch.AutoeraseOfInclObjects==before.Autoerase&&
            Owners(owner,profile.ID,cancel).Length==0,"PROFILE_RETENTION_FAILED","The released profile/parent was lost, transferred or consumed by another native feature.");
        var expected=JsonNode.Parse(before.Profile.Stored.GetRawText())!.AsObject();expected["dependent_feature_id"]=null;
        Remove(expected["dependencies"] as JsonArray,Key(removed),cancel);
        Need(ModelFeatureReorderState345.Equivalent(Wire.Element(expected),current.Stored,cancel),"PROFILE_RETENTION_FAILED",
            "The retained unused profile changed its parent, selected contour, support, source, host, name or geometry after the selected consumer release.");
        var direct=profile.GetDependent();var all=profile.GetAllDependent(false);
        Need(direct!=null&&all!=null,"DEPENDENCY_INVENTORY_UNAVAILABLE","Released profile dependent inventories are unavailable.");
        ModelInventoryBudget.Ids(direct,cancel);ModelInventoryBudget.Ids(all,cancel);
        var expectedLinks=JsonNode.Parse(before.Dependents.GetRawText())!.AsObject();foreach(string field in new[]{"direct","all"})Remove(expectedLinks[field] as JsonArray,Key(removed),cancel);
        var actual=Wire.Element(new{direct=direct.Select(Key).OrderBy(id=>id,StringComparer.OrdinalIgnoreCase).ToArray(),all=all.Select(Key).OrderBy(id=>id,StringComparer.OrdinalIgnoreCase).ToArray()});
        Need(ModelFeatureReorderState345.Equivalent(Wire.Element(expectedLinks),actual,cancel),"PROFILE_RETENTION_FAILED","Native erase changed another retained profile dependent relationship.");
        return new(current,actual,before.Autoerase);
    }
}
