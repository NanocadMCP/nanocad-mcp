using System.Text.Json;
using System.Text.Json.Nodes;
using Multicad;
using Multicad.Mc3D;
using NanoCAD.Contracts;
using Db=Teigha.DatabaseServices;

namespace NanoCADBridge;

internal static class ModelProfileRegionsState350
{
    internal sealed record Snapshot(Mc3dSolid Body,ExtrudeFeature Feature,Db.ObjectId Representation,string DrawingDocument,string OwnerDocument,
        McObjectId[] Contents,McObjectId[] Construction,JsonElement Parameters,ModelProfileRegionsReader350.Snapshot Selected,
        JsonElement OtherProfiles,JsonElement Sources,JsonElement BodyMetadata,JsonElement Display,HashSet<Db.ObjectId> Inventory,
        ModelViewFollowers.Snapshot Views,string[] FeatureOrder,string[] DirectDownstream,string[] Downstream,string[] Owners,string[] Warnings);
    internal static void Need([System.Diagnostics.CodeAnalysis.DoesNotReturnIf(false)] bool ok,string code,string message)
    {if(!ok)throw new DomainException(code,message);}
    static string Key(McObjectId id)=>id.ToString();
    internal static bool Equal(JsonElement a,JsonElement b,Action cancel)=>ModelProfileRebindState349.Equivalent(a,b,cancel);
    static JsonElement Metadata(Db.DBObject value,Db.Transaction tx,Action cancel,bool body)
    {
        using var xdata=value.XData;using var reactors=value.GetPersistentReactorIds();var bytes=new ModelInventoryBudget(cancel);var links=new List<string?>();
        foreach(Db.ObjectId id in reactors){cancel();string? handle=id.IsNull?null:id.Handle.ToString();bytes.Add(new{handle});links.Add(handle);}
        object? data=xdata==null?null:NativeOwnedMetadata.Buffer(xdata,cancel,bytes.Add);
        var owned=JsonNode.Parse(NativeOwnedMetadata.Capture(value,tx,cancel).GetRawText())!.AsArray();
        if(body)foreach(var entry in owned)
        {cancel();if(entry!["common"] is JsonObject common&&common["owner_handle"]?.GetValue<string>()==Validation.NormalizeHandle(value.Handle.ToString()))common["owner_handle"]="$CURRENT_BODY_REPRESENTATION";}
        var result=Wire.Element(new{root=new{type=value.GetType().FullName,owner_handle=body?null:value.OwnerId.Handle.ToString(),value.HasFields,
            extension_handle=value.ExtensionDictionary.IsNull?null:value.ExtensionDictionary.Handle.ToString(),xdata=data,reactors=links.OrderBy(id=>id,StringComparer.Ordinal)},owned});bytes.Add(result);return result;
    }
    internal static Snapshot Capture(Mc3dSolid body,ExtrudeFeature feature,ModelProfileRegionsSpec350 edit,ModifyArguments args,
        DocumentState state,Db.Transaction tx,Action cancel,bool includeStandalone=false)
    {
        var drawing=ModelFeatureReorderState345.Scope(state,tx);var owner=ModelProfileDiscovery348.OwnerDocument(body,state);
        var representation=ModelHistoryEditGuard.CurrentRepresentation(body,state,tx,cancel);var solid=tx.GetObject(representation,Db.OpenMode.ForRead) as Db.Solid3d;
        Need(solid!=null&&solid.GetType()==typeof(Db.Solid3d)&&solid.OwnerId==ModelFeatureReorderState345.Model(state.Document.Database,tx)&&!solid.IsNull,
            "INVALID_GEOMETRY","The existing part has no unique nonempty ordinary native solid representation.");
        var layer=(Db.LayerTableRecord)tx.GetObject(solid.LayerId,Db.OpenMode.ForRead);
        Need(!layer.IsLocked&&!layer.IsOff&&!layer.IsFrozen&&!layer.IsDependent,"LAYER_UNAVAILABLE","Body layer is unavailable.");
        var contents=ModelInventoryBudget.History(body,cancel).ToArray();var parameters=ModelParameterSnapshot.Capture(
            new(args.BridgeId,args.DocumentId,args.ExpectedRevision,args.OperationId,args.Units,representation.Handle.ToString(),"","",0),state,cancel);
        ModelParameterSnapshot.Healthy(parameters);var profile=ModelProfileDiscovery348.Resolve(edit.ProfileId,owner,cancel);
        Need(feature.ID.ToString().Equals(edit.FeatureId,StringComparison.OrdinalIgnoreCase)&&feature.ProfileID==profile.ID&&feature.HostSolidID==body.ID&&
            feature.DbEntity.Document!=null&&feature.DbEntity.Document.ID==owner.ID&&profile.Dependent3dFeatureID==feature.ID,
            "PROFILE_OWNERSHIP_MISMATCH","The exact profile lacks this one retained extrusion relationship in the actual owner document.");
        var selected=ModelProfileRegionsReader350.Capture(body,profile,state,tx,cancel);
        var peers=ModelProfileDiscovery348.Ids<SketchProfile>(owner,cancel).Select(id=>id.GetObjectOfType<SketchProfile>()).ToArray();
        Need(peers.All(peer=>peer!=null),"PROFILE_DISCOVERY_UNAVAILABLE","Native same-document profile inventory is unavailable.");
        Need(peers.Count(peer=>peer!.ParentSketchID==selected.Sketch.ID)==1,"OUT_OF_SCOPE","A sketch shared by multiple profiles needs its joint selected-region adapter.");
        var owners=new List<string>();
        foreach(var id in ModelProfileDiscovery348.Ids<ExtrudeFeature>(owner,cancel))
        {cancel();var item=id.GetObjectOfType<ExtrudeFeature>();Need(item!=null,"PROFILE_DISCOVERY_UNAVAILABLE","Native extrusion inventory is unavailable.");if(item.ProfileID==profile.ID)owners.Add(Key(item.ID));}
        foreach(var id in ModelProfileDiscovery348.Ids<RevolveFeature>(owner,cancel))
        {cancel();var item=id.GetObjectOfType<RevolveFeature>();Need(item!=null,"PROFILE_DISCOVERY_UNAVAILABLE","Native revolution inventory is unavailable.");Need(item.ProfileID!=profile.ID,"OUT_OF_SCOPE","The profile also has a revolution consumer.");}
        foreach(var id in ModelProfileDiscovery348.Ids<ExtrudeAlongPathFeature>(owner,cancel))
        {cancel();var item=id.GetObjectOfType<ExtrudeAlongPathFeature>();Need(item!=null,"PROFILE_DISCOVERY_UNAVAILABLE","Native sweep inventory is unavailable.");Need(item.ProfileID!=profile.ID,"OUT_OF_SCOPE","The profile also has a sweep consumer.");}
        foreach(var id in ModelProfileDiscovery348.Ids<LoftFeature>(owner,cancel))
        {
            cancel();var item=id.GetObjectOfType<LoftFeature>();Need(item!=null&&item.ProfileID!=profile.ID,"OUT_OF_SCOPE","The profile also has a loft consumer.");
            for(int i=0;i<item.SectionsCount;i++){cancel();var section=item.GetSection(i);Need(section!=null&&section.ID!=profile.ID,"OUT_OF_SCOPE","The profile is also a loft section.");}
        }
        Need(owners.Count==1&&owners[0]==Key(feature.ID),"PROFILE_OWNERSHIP_MISMATCH","Actual document native consumers do not identify exactly this selected extrusion.");
        var others=new List<JsonElement>();var sources=new Dictionary<string,JsonElement>(StringComparer.OrdinalIgnoreCase);var budget=new ModelInventoryBudget(cancel);
        void Source(string handle)
        {
            cancel();if(sources.ContainsKey(handle))return;var raw=CadStructureWriter.Find(state.Document.Database,handle);var entity=tx.GetObject(raw,Db.OpenMode.ForRead) as Db.Entity;
            Need(entity!=null&&entity.OwnerId==ModelFeatureReorderState345.Model(state.Document.Database,tx),"PROFILE_SOURCE_UNAVAILABLE","A retained drawing source is not present in ordinary Model Space.");
            var detail=Wire.Element(EntityAdapter.Detail(entity,EntityAdapter.Summary(entity,entity.OwnerId,"","",false),CancellationToken.None));cancel();
            var value=Wire.Element(new{handle,native_id=Key(McObjectId.FromHandle(raw.Handle.Value,drawing.ID)),geometry=detail.GetProperty("geometry"),
                properties=EntityAdapter.StoredProperties(entity),metadata=Metadata(entity,tx,cancel,false)});budget.Add(value);sources.Add(handle,value);
        }
        foreach(var id in contents)
        {
            cancel();var other=id.GetObjectOfType<SketchProfile>();if(other==null||other.ID==profile.ID)continue;
            var value=ModelProfileLifecycleState340.Capture(other,body,state,tx,cancel,documentScope:owner,allowCircularHoles:true);budget.Add(value.Stored);others.Add(value.Stored);
            foreach(var source in value.Sources)Source(source.handle);
        }
        foreach(var source in ProjectedHistorySources.Read(body,contents,state,tx,cancel))Source(source.handle);
        foreach(string handle in edit.AddSourceHandles)Source(handle);
        void DrawingPath(McObjectId native)
        {
            cancel();if(native.IsNull||native.Handle==0||!state.Document.Database.TryGetObjectId(new Db.Handle(native.Handle),out var raw)||raw.IsNull||raw.IsErased||raw.IsEffectivelyErased)return;
            if(tx.GetObject(raw,Db.OpenMode.ForRead) is not Db.Curve curve||curve.OwnerId!=ModelFeatureReorderState345.Model(state.Document.Database,tx))return;
            if(McObjectId.FromHandle(raw.Handle.Value,drawing.ID)==native)Source(raw.Handle.ToString());
        }
        foreach(var id in contents)
        {
            cancel();var sweep=id.GetObjectOfType<ExtrudeAlongPathFeature>();if(sweep!=null)foreach(var path in sweep.GetPath()??[])DrawingPath(path.ID);
            var loft=id.GetObjectOfType<LoftFeature>();if(loft==null)continue;
            if(loft.LoftType==LoftType.WithCenterLine&&loft.Path!=null)DrawingPath(loft.Path.ID);
            for(int i=0;i<loft.GuideRailsCount;i++){cancel();foreach(var path in loft.GetGuideRailsParams(i)??[])DrawingPath(path.ID);}
        }
        var nativeSources=NativePathSources.Read(contents,cancel);ProjectedHistorySources.Healthy(contents,ProjectedHistorySources.Read(body,contents,state,tx,cancel));
        var links=new List<ModelSequenceLink>();var forming=new List<string>();var nodes=parameters.GetProperty("nodes").EnumerateArray().ToArray();
        foreach(var id in contents)
        {
            cancel();var item=id.GetObjectOfType<HistoryItem>();
            if(item==null)Need(nodes.Any(node=>node.GetProperty("mc_id").GetString()==Key(id)&&(!node.TryGetProperty("detail_unavailable_reason",out var reason)||reason.ValueKind==JsonValueKind.Null))||nativeSources.Contains(Key(id)),
                "HISTORY_UNAVAILABLE","Native source/support preservation has no complete typed adapter.");
            else Need(item.ID==id&&item.HostSolidID==body.ID&&item.Status.ToString()=="Ok"&&!item.IsSuppressed&&!item.IsBeyondEndOfPart,
                "MODEL_STATE_UNAVAILABLE","Retained native history is inactive, foreign or unhealthy.");
            links.Add(new(Key(id),item==null||item.OrderObjBefore.IsNull?null:Key(item.OrderObjBefore)));
            if(ModelFeatureReorderState345.Forming(id))
            {forming.Add(Key(id));Need(nodes.Single(node=>node.GetProperty("mc_id").GetString()==Key(id)).GetProperty("parameters").ValueKind==JsonValueKind.Object,
                "MODEL_PARAMETERS_UNAVAILABLE","A retained forming item requires its complete parameter adapter.");}
        }
        var views=ModelViewFollowers.Resolve(body,contents,cancel,includeStandalone);var order=ModelFeatureReorderPlanning345.FeatureOrder(links.ToArray(),forming.ToArray(),Key(views.LastModelItem),cancel);
        var direct=feature.GetDependent();var downstream=feature.GetAllDependent(false);
        Need(direct!=null&&downstream!=null,"DEPENDENCY_INVENTORY_UNAVAILABLE","Actual direct/transitive downstream inventory is unavailable.");ModelInventoryBudget.Ids(direct,cancel);ModelInventoryBudget.Ids(downstream,cancel);
        Need(direct.Concat(downstream).All(id=>id==body.ID||contents.Contains(id)||ModelViewFollowers.OwnsDependent(views,id)),"OUT_OF_SCOPE","A dependent is outside this retained native part.");
        var notices=new List<string>();notices.AddRange(ModelSketchConstraintObservation353.Warnings([selected.Constraints]));ModelFeatureOracle.Read(solid);
        try{using var brep=new Teigha.BoundaryRepresentation.Brep(solid);Need(!brep.IsNull&&brep.CheckEntity(),"INVALID_GEOMETRY","Native B-rep validation found an invalid rebuilt part.");}
        catch(Teigha.Runtime.Exception){notices.Add(ModelFeatureReorderState345.ValidityWarning);}catch(NotSupportedException){notices.Add(ModelFeatureReorderState345.ValidityWarning);}
        var bodyMetadata=Metadata(solid,tx,cancel,true);var display=Wire.Element(new{layer=solid.LayerId.Handle.ToString(),linetype=solid.LinetypeId.Handle.ToString(),properties=EntityAdapter.StoredProperties(solid)});
        var sourceValues=Wire.Element(sources.OrderBy(pair=>pair.Key,StringComparer.OrdinalIgnoreCase).Select(pair=>pair.Value).ToArray());
        budget.Add(new{parameters,selected=selected.Stored,other_profiles=others,sources=sourceValues,bodyMetadata,display,downstream});
        return new(body,feature,representation,Key(drawing.ID),Key(owner.ID),contents,ModelProfileDiscovery348.ConstructionIds(owner,cancel),parameters,selected,
            Wire.Element(others.OrderBy(value=>value.GetProperty("profile_id").GetString(),StringComparer.OrdinalIgnoreCase).ToArray()),sourceValues,bodyMetadata,display,
            ModelFeatureReorderState345.Inventory(state,tx,cancel),views,order,direct.Select(Key).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(id=>id,StringComparer.OrdinalIgnoreCase).ToArray(),
            downstream.Select(Key).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(id=>id,StringComparer.OrdinalIgnoreCase).ToArray(),owners.ToArray(),notices.ToArray());
    }
    static JsonElement Node(JsonElement source,string sketch,string profile,HashSet<string> added,Action cancel)
    {
        var value=JsonNode.Parse(source.GetRawText())!.AsObject();string id=value["mc_id"]!.GetValue<string>();
        if(id.Equals(sketch,StringComparison.OrdinalIgnoreCase)||id.Equals(profile,StringComparison.OrdinalIgnoreCase))
        {
            if(value["dependencies"] is JsonArray deps)for(int i=deps.Count-1;i>=0;i--){cancel();if(added.Contains(deps[i]!.GetValue<string>()))deps.RemoveAt(i);}
        }
        // Direct dependencies are an inventory, not construction order. Keep
        // every original ID/multiplicity while comparing a canonical ordering.
        if(value["dependencies"] is JsonArray dependencies)
            value["dependencies"]=new JsonArray(dependencies.Select(item=>item!.GetValue<string>()).OrderBy(item=>item,StringComparer.OrdinalIgnoreCase)
                .Select(item=>(JsonNode?)JsonValue.Create(item)).ToArray());
        if(value["parameters"] is JsonObject fields&&fields["edge_ids"] is JsonArray edges){fields["edge_count"]=edges.Count;fields.Remove("edge_ids");}
        return Wire.Element(value);
    }
    internal static void Verify(Snapshot before,Snapshot after,ModelCircleRegion350[] expected,McObjectId[] projections,Action cancel)
    {
        static string? Support(JsonElement value)=>value.TryGetProperty("support_id",out var id)?id.GetString():null;
        Need(before.DrawingDocument==after.DrawingDocument&&before.OwnerDocument==after.OwnerDocument&&
            before.BodyMetadata.GetRawText()==after.BodyMetadata.GetRawText()&&before.Display.GetRawText()==after.Display.GetRawText(),
            "MODEL_STATE_UNAVAILABLE","Region selection changed actual document/body identity, display or owned data.");
        Need(before.FeatureOrder.SequenceEqual(after.FeatureOrder,StringComparer.OrdinalIgnoreCase)&&after.Body.LastPartItemID==before.Views.LastHistoryItem,
            "MODEL_ORDER_MISMATCH","Region selection changed native forming order or End-of-Part.");
        var selected=after.Selected;Need(before.Selected.Profile.ID==selected.Profile.ID&&before.Selected.Sketch.ID==selected.Sketch.ID&&
            after.Feature.ProfileID==selected.Profile.ID&&selected.Profile.Dependent3dFeatureID==after.Feature.ID&&after.Owners.SequenceEqual(before.Owners,StringComparer.OrdinalIgnoreCase),
            "PROFILE_OWNERSHIP_MISMATCH","The existing native consumer/profile/parent relationship changed.");
        Need(Equal(before.Selected.SketchGeometry.GetProperty("frame"),selected.SketchGeometry.GetProperty("frame"),cancel)&&
            Support(before.Selected.Stored)==Support(selected.Stored)&&ModelSketchConstraintObservation353.Equivalent(before.Selected.Constraints,selected.Constraints,cancel),
            "PROFILE_GEOMETRY_MISMATCH","Region editing changed existing native frame/support/constraints.");
        var oldElements=before.Selected.SketchGeometry.GetProperty("elements").EnumerateArray().ToDictionary(value=>value.GetProperty("element_id").GetString()!,StringComparer.OrdinalIgnoreCase);
        var current=selected.SketchGeometry.GetProperty("elements").EnumerateArray().ToDictionary(value=>value.GetProperty("element_id").GetString()!,StringComparer.OrdinalIgnoreCase);
        foreach(var element in oldElements){cancel();Need(current.TryGetValue(element.Key,out var actual)&&Equal(element.Value,actual,cancel),"PROFILE_GEOMETRY_MISMATCH","An existing native sketch element or its geometry was lost/changed.");}
        var newIds=projections.Select(Key).ToHashSet(StringComparer.OrdinalIgnoreCase);
        Need(current.Keys.Except(oldElements.Keys,StringComparer.OrdinalIgnoreCase).ToHashSet(StringComparer.OrdinalIgnoreCase).SetEquals(newIds)&&
            projections.All(id=>after.Contents.Contains(id)&&id.GetObjectOfType<ProjectionSketchEnt>() is {} projection&&projection.HostSketchID==selected.Sketch.ID&&projection.HostSolidID==after.Body.ID),
            "PROFILE_OWNERSHIP_MISMATCH","Added native projection identities/hosts/body composition are incomplete or unrelated.");
        Need(Equal(Wire.Element(expected),Wire.Element(ModelProfileRegionsReader350.SourceKeys(selected.Regions,selected.Loops)),cancel),
            "PROFILE_GEOMETRY_MISMATCH","Actual selected native areas/hole loops differ from the explicit desired Circle cells.");
        Need(Equal(before.Sources,after.Sources,cancel)&&Equal(before.OtherProfiles,after.OtherProfiles,cancel),"PROFILE_SOURCE_CHANGED","Region editing changed retained sources/metadata or another native profile.");
        var allowed=projections.ToHashSet();
        foreach(var id in projections)
        {cancel();var projection=id.GetObjectOfType<ProjectionSketchEnt>();Need(projection!=null,"PROFILE_OWNERSHIP_MISMATCH","An added native projection disappeared.");allowed.Add(projection.SrcObjectID);}
        Need(before.Contents.All(after.Contents.Contains)&&after.Contents.Except(before.Contents).All(allowed.Contains)&&
            before.Construction.All(after.Construction.Contains)&&after.Construction.Except(before.Construction).All(projections.Contains),
            "MODEL_STATE_UNAVAILABLE","History/document construction changed beyond explicitly added source projections.");
        Need(before.Inventory.Except(after.Inventory).All(id=>id==before.Representation)&&after.Inventory.Except(before.Inventory).All(id=>id==after.Representation)&&
            (after.Representation==before.Representation||!after.Inventory.Contains(before.Representation)),"OBJECT_IDENTITY_MISMATCH","An unrelated drawing object changed membership or a duplicate body representation remains.");
        var allowedEdges=allowed.Select(Key).ToHashSet(StringComparer.OrdinalIgnoreCase);string sketch=Key(selected.Sketch.ID),profile=Key(selected.Profile.ID);
        var nodes=after.Parameters.GetProperty("nodes").EnumerateArray().ToDictionary(value=>value.GetProperty("mc_id").GetString()!,StringComparer.OrdinalIgnoreCase);
        foreach(var old in before.Parameters.GetProperty("nodes").EnumerateArray())
        {cancel();string id=old.GetProperty("mc_id").GetString()!;Need(nodes.TryGetValue(id,out var actual)&&Equal(Node(old,sketch,profile,allowedEdges,cancel),Node(actual,sketch,profile,allowedEdges,cancel),cancel),
            "MODEL_STATE_UNAVAILABLE","An existing native feature parameter, source reference, dependency or predecessor changed.");}
        Need(Equal(before.Parameters.GetProperty("variables"),after.Parameters.GetProperty("variables"),cancel)&&before.Downstream.SequenceEqual(after.Downstream,StringComparer.OrdinalIgnoreCase)&&
            before.DirectDownstream.SequenceEqual(after.DirectDownstream,StringComparer.OrdinalIgnoreCase),
            "DEPENDENCY_OWNERSHIP_MISMATCH","An existing native variable/formula/driver/consumer or downstream dependency was lost/changed.");
        ModelViewFollowers.Verify(after.Body,before.Views,cancel,refreshStandalone:true);
    }
}
