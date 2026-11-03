using System.Text.Json;
using System.Text.Json.Nodes;
using Multicad;
using Multicad.DatabaseServices;
using Multicad.Mc3D;
using NanoCAD.Contracts;
using G=Multicad.Geometry;
using Db=Teigha.DatabaseServices;

namespace NanoCADBridge;

// A new selection on the existing acquired sketch. The old profile, sketch
// geometry, constraints and its other forming consumers are never edited.
internal static class ModelProfileRebindSelection430
{
    internal sealed record Plan(ModelSketchLifecycleReader362.Snapshot Sketch,ModelSketchRegion377[] Regions);
    static void Need([System.Diagnostics.CodeAnalysis.DoesNotReturnIf(false)] bool ok,string code,string message)=>ModelProfileRebindState349.Need(ok,code,message);
    static string Key(McObjectId id)=>id.ToString();
    static bool Equal(JsonElement a,JsonElement b,Action cancel)=>ModelProfileRebindState349.Equivalent(a,b,cancel);
    internal static Plan Resolve(ModelProfileRebindSpec349 edit,ModelProfileRebindState349.Snapshot before,DocumentState state,Db.Transaction tx,Action cancel)
    {
        Need(edit.ReplacementRegions is {Length:>0},"INVALID_ARGUMENT","Select bounded replacement regions.");
        Need(before.Contents.Contains(before.Old.Sketch.ID)&&before.Old.Sketch.HostSolidID==before.Body.ID,
            "PROFILE_REBIND_ACQUISITION_UNAVAILABLE","Region replacement requires the current profile's already acquired same-part sketch.");
        var sketch=ModelSketchLifecycleReader362.Capture(before.Body,before.Old.Sketch,state,tx,cancel);
        var curves=ModelSketchLifecycleReader362.Curves(sketch,cancel);
        var points=edit.ReplacementRegions.Select(point=>{cancel();return DrawingWorkplane.Local(sketch.Plane,point);}).ToArray();
        return new(sketch,ModelProfileRebindRegions430.Select(curves,points,cancel));
    }
    internal static SketchProfile Create(Plan plan,ModelProfileRebindSpec349 edit,ModelProfileRebindState349.Snapshot before,
        Action cancel,Action<string> trace,Action<McObjectId> observe)
    {
        cancel();trace("create_replacement_profile_on_acquired_sketch");var profile=plan.Sketch.Sketch.CreateProfile();
        if(profile!=null)observe(profile.ID);
        Need(profile!=null&&!profile.ID.IsNull&&profile.ParentSketchID==plan.Sketch.Sketch.ID&&
            !before.Construction.Contains(profile.ID)&&!before.Contents.Contains(profile.ID),
            "PROFILE_OWNERSHIP_MISMATCH","Native replacement selection did not return a fresh profile on the exact acquired sketch.");
        var owner=plan.Sketch.Sketch.DbEntity.Document;
        Need(owner!=null&&Key(owner.ID)==before.OwnerDocument&&(profile.DbEntity.Document==null||profile.DbEntity.Document.ID==owner.ID),
            "DOCUMENT_CONTEXT_MISMATCH","Fresh replacement profile has a foreign native owner; no owner transfer is attempted.");
        if(profile.DbEntity.Document==null)Need(McObjectManager.Add2Document(profile.DbEntity,owner),"PROFILE_OWNERSHIP_MISMATCH","Fresh replacement profile could not be registered in the exact sketch owner document.");
        Need(profile.DbEntity.Document?.ID==owner.ID,"PROFILE_OWNERSHIP_MISMATCH","Fresh replacement profile has no verified actual owner document.");
        profile.DbEntity.Visibility=0;profile.DbEntity.Update();
        Need(profile.Clear()&&profile.IsEmpty,"MODEL_UPDATE_FAILED","Fresh replacement profile did not clear its selected native regions.");
        var normal=DrawingWorkplane.Normal(plan.Sketch.Plane);
        foreach(var point in edit.ReplacementRegions!)
        {
            cancel();trace("select_replacement_native_region");using var ray=new G.Line3d(new G.Point3d(point.X,point.Y,point.Z),new G.Vector3d(normal.X,normal.Y,normal.Z));
            Need(profile.AddOrRemoveRegionByLine(ray),"MODEL_UPDATE_FAILED","Native replacement profile could not select the requested bounded region.");
        }
        profile.DbEntity.Update();McObjectManager.UpdateAll();return profile;
    }
    static JsonElement Without(JsonElement value,params string[] fields)
    {var node=JsonNode.Parse(value.GetRawText())!.AsObject();foreach(string field in fields)node.Remove(field);return Wire.Element(node);}
    static void SketchMetadata(JsonElement before,JsonElement after,string identity,string added,Action cancel)
    {
        Need(before.GetProperty("scope").GetString()==after.GetProperty("scope").GetString(),"PROFILE_SOURCE_CHANGED","Native sketch metadata scope changed during profile selection.");
        if(!before.TryGetProperty("metadata",out _)){Need(Equal(before,after,cancel),"PROFILE_SOURCE_CHANGED","Native sketch metadata availability changed.");return;}
        JsonElement Row(JsonElement value)=>Wire.Element(new{handle=identity,native_id=identity,metadata=value.GetProperty("metadata"),reactor_observation=value.GetProperty("reactor_observation")});
        var prior=Wire.Element(new[]{Row(before)});var current=Wire.Element(new[]{Row(after)});
        var additions=new Dictionary<string,string[]>(StringComparer.OrdinalIgnoreCase){{identity,[added]}};
        Need(ModelProfileSourcePreservation360.Retained(prior,current,identity,new Dictionary<string,string[]>(),additions,new HashSet<string>(),new HashSet<string>(),
            (a,b)=>Equal(a,b,cancel),cancel,out _,out _,out _),"PROFILE_SOURCE_CHANGED","Native selection changed retained sketch user metadata or unrelated reactors.");
    }
    internal static void VerifySelected(Plan plan,SketchProfile profile,Mc3dSolid body,DocumentState state,Db.Transaction tx,Action cancel)
    {
        var selected=ModelProfileElementsReader358.Capture(body,profile,state,tx,cancel);var pending=plan.Regions.ToList();
        for(int i=0;i<selected.Outer.Length;i++)
        {
            cancel();int match=pending.FindIndex(region=>ModelProfileBoundary340.Same(region.Outer,selected.Outer[i],cancel)&&
                region.Holes.Length==selected.Holes[i].Length&&region.Holes.All(hole=>selected.Holes[i].Any(actual=>ModelProfileBoundary340.Same(hole,actual,cancel))));
            Need(match>=0,"PROFILE_GEOMETRY_MISMATCH","Native replacement selection has an unexpected outer or hole boundary.");pending.RemoveAt(match);
        }
        Need(pending.Count==0,"PROFILE_GEOMETRY_MISMATCH","Native replacement selection omitted a requested bounded region.");
    }
    internal static string[] VerifyPrepared(Plan plan,ModelProfileRebindState349.Snapshot before,ModelProfileRebindState349.Snapshot after,
        DocumentState state,Db.Transaction tx,Action cancel)
    {
        var profile=after.Next.Profile;string added=Key(profile.ID);var warnings=new List<string>();
        Need(before.DrawingDocument==after.DrawingDocument&&before.OwnerDocument==after.OwnerDocument&&before.Body.ID==after.Body.ID&&
            before.Views.LastHistoryItem==after.Body.LastPartItemID&&before.FeatureOrder.SequenceEqual(after.FeatureOrder,StringComparer.OrdinalIgnoreCase),
            "MODEL_ORDER_MISMATCH","Creating a replacement profile changed the retained part identity, forming order or End-of-Part.");
        Need(before.Construction.All(after.Construction.Contains)&&after.Construction.Except(before.Construction).SequenceEqual(new[]{profile.ID}),
            "PROFILE_OWNERSHIP_MISMATCH","Native selection changed document construction outside the one fresh replacement profile.");
        Need(before.Contents.All(after.Contents.Contains)&&after.Contents.Except(before.Contents).All(id=>id==profile.ID),
            "PROFILE_OWNERSHIP_MISMATCH","Native selection changed existing part composition or added unrelated history.");
        Need(profile.ParentSketchID==before.Old.Sketch.ID&&profile.Dependent3dFeatureID.IsNull&&after.NextOwners.Length==0,
            "PROFILE_OWNERSHIP_MISMATCH","Fresh replacement selection acquired an undeclared native consumer or a different sketch.");
        Need(before.Display.GetRawText()==after.Display.GetRawText()&&Equal(before.Sources,after.Sources,cancel)&&
            Equal(Wire.Element(before.OldConsumers),Wire.Element(after.OldConsumers),cancel)&&
            before.DirectDependents.SequenceEqual(after.DirectDependents,StringComparer.OrdinalIgnoreCase)&&before.AllDependents.SequenceEqual(after.AllDependents,StringComparer.OrdinalIgnoreCase)&&
            ModelProfileRebindState349.ConstraintsRetained(before.ConstraintObservations,after.ConstraintObservations,cancel),
            "MODEL_DEPENDENCY_CHANGED","Native selection changed retained sources, display, old profile consumers or observed constraints.");
        foreach(var id in after.Inventory.Except(before.Inventory))
        {
            cancel();Need(id==after.Representation||McObjectId.FromHandle(id.Handle.Value,profile.DbEntity.Document.ID)==profile.ID,
                "OBJECT_IDENTITY_MISMATCH","Native selection created an unrelated drawing object.");
        }
        Need(before.Inventory.Except(after.Inventory).All(id=>id==before.Representation)&&
            (before.Representation==after.Representation||!after.Inventory.Contains(before.Representation)),"OBJECT_IDENTITY_MISMATCH","Native selection removed unrelated drawing objects or duplicated the part representation.");
        var currentNodes=after.Parameters.GetProperty("nodes").EnumerateArray().ToDictionary(node=>node.GetProperty("mc_id").GetString()!,StringComparer.OrdinalIgnoreCase);
        foreach(string field in new[]{"owner","body","last_item","external_sources","native_source_ids","native"})
            Need(Equal(before.Parameters.GetProperty(field),after.Parameters.GetProperty(field),cancel),"MODEL_STATE_UNAVAILABLE","Profile selection changed the existing body geometry, owner, terminal or native sources before rebind.");
        foreach(var node in before.Parameters.GetProperty("nodes").EnumerateArray())
        {cancel();Need(currentNodes.TryGetValue(node.GetProperty("mc_id").GetString()!,out var current)&&Equal(node,current,cancel),"MODEL_DEPENDENCY_CHANGED","Profile selection changed an existing feature's parameters, native order or dependencies.");}
        var variables=after.Parameters.GetProperty("variables").EnumerateArray().ToDictionary(value=>value.GetProperty("mc_id").GetString()!,StringComparer.OrdinalIgnoreCase);
        var oldVariables=before.Parameters.GetProperty("variables").EnumerateArray().ToArray();
        Need(oldVariables.Length==variables.Count,"MODEL_DRIVER_CHANGED","Profile selection changed the native variable inventory.");
        foreach(var variable in oldVariables)
        {cancel();Need(variables.TryGetValue(variable.GetProperty("mc_id").GetString()!,out var current)&&ModelProfileConsumers410.RetainedVariable(variable,current,new HashSet<string>(StringComparer.OrdinalIgnoreCase){added},cancel),"MODEL_DRIVER_CHANGED","Profile selection changed retained variables, formulas or undeclared consumers.");}
        var currentProfiles=after.Profiles.EnumerateArray().ToDictionary(value=>value.GetProperty("profile_id").GetString()!,StringComparer.OrdinalIgnoreCase);
        foreach(var prior in before.Profiles.EnumerateArray())
        {cancel();Need(currentProfiles.TryGetValue(prior.GetProperty("profile_id").GetString()!,out var current)&&Equal(prior,current,cancel),"PROFILE_GEOMETRY_MISMATCH","Profile selection changed an existing selected profile or its sketch/source bindings.");}
        var sketch=ModelSketchLifecycleReader362.Capture(before.Body,before.Old.Sketch,state,tx,cancel);
        Need(Equal(Without(plan.Sketch.Stored,"profiles","containing_ids","native_metadata"),Without(sketch.Stored,"profiles","containing_ids","native_metadata"),cancel),
            "SKETCH_GEOMETRY_MISMATCH","Profile selection changed the acquired sketch geometry, support or observed constraints.");
        Need(plan.Sketch.Containing.All(sketch.Containing.Contains)&&sketch.Containing.Except(plan.Sketch.Containing).All(id=>id==profile.ID&&sketch.Sketch.ContainsObject(id)),
            "SKETCH_OWNERSHIP_MISMATCH","Profile selection changed unrelated native sketch membership.");
        SketchMetadata(plan.Sketch.Stored.GetProperty("native_metadata"),sketch.Stored.GetProperty("native_metadata"),Key(sketch.Sketch.ID),added,cancel);
        var actualPeers=sketch.Profiles.EnumerateArray().ToDictionary(value=>value.GetProperty("profile_id").GetString()!,StringComparer.OrdinalIgnoreCase);
        JsonElement Peer(JsonElement peer)
        {
            var node=JsonNode.Parse(peer.GetRawText())!.AsObject();node["state"]!.AsObject().Remove("containing_ids");return Wire.Element(node);
        }
        foreach(var peer in plan.Sketch.Profiles.EnumerateArray())
        {cancel();Need(actualPeers.TryGetValue(peer.GetProperty("profile_id").GetString()!,out var current)&&Equal(Peer(peer),Peer(current),cancel),"PROFILE_GEOMETRY_MISMATCH","Creating a replacement changed a retained peer profile, consumer or metadata.");}
        Need(actualPeers.Count==plan.Sketch.Profiles.GetArrayLength()+1&&actualPeers.ContainsKey(added),"PROFILE_OWNERSHIP_MISMATCH","Native selection did not add exactly one profile to the acquired sketch.");
        var storage=ModelSketchConstraintMetadata379.Compare(Wire.Element(new{}),Wire.Element(new{}),before.BodyMetadata,after.BodyMetadata,before.StorageBindings,after.StorageBindings,cancel);
        Need(storage.retained,"MODEL_STATE_UNAVAILABLE","Native profile selection changed retained user data or unrelated native storage.");if(storage.changed)warnings.Add(ModelSketchConstraintMetadata379.Warning);
        VerifySelected(plan,profile,before.Body,state,tx,cancel);ModelViewFollowers.Verify(before.Body,before.Views,cancel,refreshStandalone:false);return warnings.ToArray();
    }
}
