using System.Text.Json;
using System.Text.Json.Nodes;
using Multicad;
using Multicad.DatabaseServices;
using Multicad.Mc3D;
using NanoCAD.Contracts;
using Db=Teigha.DatabaseServices;

namespace NanoCADBridge;

internal static partial class ModelSketchLifecycleWriter362
{
    sealed record Section446(ModelProfileLifecycleState340.Snapshot Profile,ModelSketchLifecycleReader362.Snapshot Sketch,ModelProfileConsumers410.Snapshot Consumers);
    static Section446[] ReadSections446(ModelProfileLoft446 edit,Mc3dSolid body,McDocument owner,DocumentState state,Db.Transaction tx,Action cancel)
    {
        var result=new List<Section446>();
        foreach(var request in edit.Profiles)
        {
            cancel();var profile=ModelProfileDiscovery348.Resolve(request.ProfileId,owner,cancel);
            var consumers=ModelProfileConsumers410.Capture(profile,owner,cancel);ModelProfileConsumers410.RequireBody(consumers,body);
            var selected=ModelProfileLifecycleState340.Capture(profile,body,state,tx,cancel,allowCircularHoles:true);
            Need(Key(selected.Sketch.ID).Equals(request.ExpectedSketchId,StringComparison.OrdinalIgnoreCase)&&selected.Token==request.ExpectedProfileToken,"PRECONDITION_FAILED","A selected loft profile, exact parent, boundary or source binding changed; refresh every current profile token.");
            var sketch=ModelSketchLifecycleReader362.Capture(body,selected.Sketch,state,tx,cancel);result.Add(new(selected,sketch,consumers));
        }
        return result.ToArray();
    }
    static McObjectId[] FeatureProfiles446(HistoryItem feature,Action cancel)=>feature is LoftFeature loft?ModelProfileLoftNative446.Profiles(loft,cancel):[FeatureProfile(feature)];
    static void AllowSections446(Binding binding,HashSet<McObjectId> allowed,Action cancel)
    {
        foreach(var selected in binding.Sections446)
        {cancel();allowed.Add(selected.Profile.Profile.ID);allowed.Add(selected.Sketch.Sketch.ID);allowed.UnionWith(selected.Profile.ProjectionIds);allowed.UnionWith(selected.Sketch.Projections.NativeReferences);allowed.UnionWith(ModelSketchLifecycleMembership366.ProjectionSources(selected.Sketch,cancel));if(!selected.Sketch.Sketch.PlanarEntityID.IsNull)allowed.Add(selected.Sketch.Sketch.PlanarEntityID);}
    }
    static object SectionBaseline446(Binding binding)=>binding.Sections446.Select(s=>new{profile_id=Key(s.Profile.Profile.ID),profile_token=s.Profile.Token,sketch_id=Key(s.Sketch.Sketch.ID),sketch_token=s.Sketch.Token,s.Consumers}).ToArray();
    static void NormalizeAdditionalSections446(JsonObject copy,Binding binding,HistoryItem? feature)
    {
        if(feature is not LoftFeature)return;string identity=copy["mc_id"]!.GetValue<string>();
        foreach(var section in binding.Sections446.Skip(1))
        {
            bool isSketch=identity==Key(section.Sketch.Sketch.ID),isProfile=identity==Key(section.Profile.Profile.ID);if(!isSketch&&!isProfile)continue;
            bool acquired=isSketch?section.Sketch.Stored.GetProperty("host_solid_id").ValueKind==JsonValueKind.Null:section.Profile.Stored.GetProperty("profile_host_solid_id").ValueKind==JsonValueKind.Null;
            if(acquired){copy["host_solid_id"]=null;copy["native_order_before_id"]=null;}
            if(copy["parameters"] is JsonObject fields)
            {fields.Remove("profile_editing");if(isProfile){fields.Remove("dependent_feature_id");fields.Remove("ownership");fields.Remove("forward_consumers");}}
        }
    }
    static ModelProfileConsumed446[] VerifySections446(Binding binding,LoftFeature feature,ModelProfileLifecycleState340.Snapshot primary,
        ModelSketchLifecycleReader362.Snapshot primarySketch,McObjectId[] contents,DocumentState state,Db.Transaction tx,Action cancel)
    {
        var dependencies=feature.GetDependsOn(false,false);Need(dependencies!=null,"PROFILE_OWNERSHIP_MISMATCH","Native loft dependencies are unavailable.");
        var result=new List<ModelProfileConsumed446>();
        for(int index=0;index<binding.Sections446.Length;index++)
        {
            cancel();var before=binding.Sections446[index];
            var selected=index==0?primary:ModelProfileLifecycleState340.Capture(before.Profile.Profile,binding.Body,state,tx,cancel,allowCircularHoles:true);
            var sketch=index==0?primarySketch:ModelSketchLifecycleReader362.Capture(binding.Body,before.Sketch.Sketch,state,tx,cancel);
            Need(selected.Profile.ID==before.Profile.Profile.ID&&selected.Sketch.ID==before.Sketch.Sketch.ID&&
                contents.Contains(selected.Profile.ID)&&contents.Contains(selected.Sketch.ID)&&selected.Profile.HostSolidID==binding.Body.ID&&selected.Sketch.HostSolidID==binding.Body.ID&&dependencies.Contains(selected.Profile.ID)&&
                selected.ProjectionIds.All(id=>contents.Contains(id)&&id.GetObjectOfType<ProjectionSketchEnt>() is {} projection&&projection.HostSolidID==binding.Body.ID&&projection.HostSketchID==selected.Sketch.ID),
                "PROFILE_OWNERSHIP_MISMATCH","A loft section lacks its exact selected-body profile/sketch/projection composition or forward dependency.");
            if(index>0)RequireSketch(binding with{Sketch=before.Sketch,Profile=before.Profile,Consumers=before.Consumers},sketch,null,feature,cancel);
            RequireProfileDesign(before.Profile.Stored,selected.Stored,before.Sketch.Stored.GetProperty("host_solid_id").ValueKind==JsonValueKind.Null,
                before.Profile.Stored.GetProperty("profile_host_solid_id").ValueKind==JsonValueKind.Null,true,cancel);
            ModelProfileConsumers410.VerifyRetained(before.Consumers,selected.Profile,binding.Owner,cancel,addedConsumer:feature.ID);
            result.Add(new(Key(selected.Profile.ID),Key(selected.Sketch.ID),selected.Token,sketch.Token,ModelProfileDiscovery348.Ownership(selected)));
        }
        return result.ToArray();
    }
}
