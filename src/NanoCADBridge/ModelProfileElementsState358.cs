using System.Text.Json;
using System.Text.Json.Nodes;
using Multicad;
using Multicad.DatabaseServices;
using Multicad.Mc3D;
using NanoCAD.Contracts;
using Db=Teigha.DatabaseServices;

namespace NanoCADBridge;

internal static class ModelProfileElementsState358
{
    internal sealed record Snapshot(Mc3dSolid Body,ExtrudeFeature? Feature,Db.ObjectId Representation,string DrawingDocument,string OwnerDocument,
        McObjectId[] Contents,McObjectId[] Construction,JsonElement Parameters,ModelProfileElementsReader358.Snapshot Selected,
        JsonElement OtherProfiles,JsonElement Sources,JsonElement BodyMetadata,JsonElement Display,HashSet<Db.ObjectId> Inventory,
        ModelViewFollowers.Snapshot Views,string[] FeatureOrder,string[] DirectDownstream,string[] Downstream,string[] Owners,string[] Warnings,
        JsonElement DrawingInventory);
    internal static void Need([System.Diagnostics.CodeAnalysis.DoesNotReturnIf(false)] bool ok,string code,string message)
    {if(!ok)throw new DomainException(code,message);}
    static string Key(McObjectId id)=>id.ToString();
    internal static bool Equal(JsonElement a,JsonElement b,Action cancel)=>ModelProfileRebindState349.Equivalent(a,b,cancel);
    internal static JsonElement Metadata(Db.DBObject value,Db.Transaction tx,Action cancel,bool body,bool observeNativeProxy=false)
    {
        using var xdata=value.XData;using var reactors=value.GetPersistentReactorIds();var bytes=new ModelInventoryBudget(cancel);var links=new List<string?>();
        foreach(Db.ObjectId id in reactors){cancel();string? handle=id.IsNull?null:id.Handle.ToString();bytes.Add(new{handle});links.Add(handle);}
        object? data=xdata==null?null:NativeOwnedMetadata.Buffer(xdata,cancel,bytes.Add);
        var owned=JsonNode.Parse(NativeOwnedMetadata.Capture(value,tx,cancel,observeAssocManager:true,observeNativeProxy:observeNativeProxy).GetRawText())!.AsArray();
        if(body)foreach(var entry in owned)
        {cancel();if(entry!["common"] is JsonObject common&&common["owner_handle"]?.GetValue<string>()==Validation.NormalizeHandle(value.Handle.ToString()))common["owner_handle"]="$CURRENT_BODY_REPRESENTATION";}
        var result=Wire.Element(new{root=new{type=value.GetType().FullName,owner_handle=body?null:value.OwnerId.Handle.ToString(),value.HasFields,
            extension_handle=value.ExtensionDictionary.IsNull?null:value.ExtensionDictionary.Handle.ToString(),xdata=data,reactors=links.OrderBy(id=>id,StringComparer.Ordinal)},owned});
        if(observeNativeProxy&&value.GetType()==typeof(Db.ProxyObject)&&value is Db.ProxyObject proxy)
        {
            var observed=JsonNode.Parse(result.GetRawText())!.AsObject();
            observed["root"]!["native_proxy_observation"]=JsonSerializer.SerializeToNode(NativeOwnedMetadata.NativeProxyObservation(proxy,value,tx,cancel,bytes.Add),Wire.Json);
            result=Wire.Element(observed);
        }
        bytes.Add(result);return result;
    }
    static JsonElement SourceReactors(Db.Entity entity,McObjectId native,McDocument drawing,McDocument owner,
        ModelProfileElementsReader358.Snapshot selected,Db.Transaction tx,Action cancel)
        =>SourceReactors(entity,native,drawing,owner,selected.Sketch.ID,selected.Projections.Ids,tx,cancel);
    internal static JsonElement SourceReactors(Db.Entity entity,McObjectId native,McDocument drawing,McDocument owner,
        McObjectId sketch,McObjectId[] projections,Db.Transaction tx,Action cancel)
    {
        // These are reads of the actual source and actual selected construction.
        // Opaque DWG reactor objects are described but never reinterpreted as
        // projection links by runtime name or arbitrary metadata content.
        var source=native.GetObjectOfType<Multicad.DatabaseServices.StandardObjects.DbGeometry>();
        var nativeLinks=source?.DbEntity.GetReactors();
        Need(source==null||source.ID==native,"OBJECT_IDENTITY_MISMATCH","The source reactor read resolved another native object.");
        return ReactorObservation(entity,drawing,owner,sketch,projections,nativeLinks,tx,cancel);
    }
    internal static JsonElement ReactorObservation(Db.DBObject entity,McDocument drawing,McDocument owner,
        McObjectId sketch,McObjectId[] projections,IEnumerable<McObjectId>? nativeLinks,Db.Transaction tx,Action cancel)
    {
        if(nativeLinks!=null)ModelInventoryBudget.Ids(nativeLinks,cancel);
        var targets=(nativeLinks??[]).Concat(projections).Append(sketch).Where(id=>!id.IsNull).Distinct().ToArray();var records=new List<object>();var budget=new ModelInventoryBudget(cancel);
        using var links=entity.GetPersistentReactorIds();
        foreach(Db.ObjectId id in links)
        {
            cancel();Db.DBObject? item=!id.IsNull&&id.IsValid&&!id.IsErased&&!id.IsEffectivelyErased?tx.GetObject(id,Db.OpenMode.ForRead):null;
            string? target=null;
            if(item!=null)
                foreach(var known in targets)
                {
                    cancel();if(known.Handle==0||known.Handle!=id.Handle.Value)continue;
                    if(McObjectId.FromHandle(id.Handle.Value,owner.ID)==known||McObjectId.FromHandle(id.Handle.Value,drawing.ID)==known)
                    {target=Key(known);break;}
                }
            var row=new{handle=id.IsNull?null:id.Handle.ToString(),runtime_type=item?.GetType().FullName,
                owner_handle=item==null||item.OwnerId.IsNull?null:item.OwnerId.Handle.ToString(),native_target_id=target};
            budget.Add(row);records.Add(row);
        }
        var value=Wire.Element(new{native_reactor_ids=nativeLinks?.Select(Key).OrderBy(id=>id,StringComparer.OrdinalIgnoreCase).ToArray(),
            drawing_reactors=records.OrderBy(value=>Wire.Element(value).GetProperty("handle").GetString(),StringComparer.OrdinalIgnoreCase).ToArray()});
        budget.Add(value);return value;
    }
    static JsonElement DrawingMembership(HashSet<Db.ObjectId> inventory,McDocument drawing,McDocument owner,
        ModelProfileElementsReader358.Snapshot selected,Db.Transaction tx,Action cancel)
    {
        // Projections were read through their exact native class, selected
        // sketch membership and real owner document by the profile reader.
        // Resolve drawing references to those IDs, never by proxy class name.
        var projections=selected.Projections.Ids.ToHashSet();var rows=new List<object>();var budget=new ModelInventoryBudget(cancel);
        foreach(var raw in inventory.OrderBy(id=>id.Handle.ToString(),StringComparer.OrdinalIgnoreCase))
        {
            cancel();var item=tx.GetObject(raw,Db.OpenMode.ForRead);string? native=null;string? matchDocument=null;
            var current=McObjectId.FromHandle(raw.Handle.Value,drawing.ID);
            if(projections.Contains(current)){native=Key(current);matchDocument=Key(drawing.ID);}
            else if(owner.ID!=drawing.ID)
            {
                current=McObjectId.FromHandle(raw.Handle.Value,owner.ID);
                if(projections.Contains(current)){native=Key(current);matchDocument=Key(owner.ID);}
            }
            var row=new{handle=raw.Handle.ToString(),runtime_type=item.GetType().FullName,
                owner_handle=item.OwnerId.IsNull?null:item.OwnerId.Handle.ToString(),native_projection_id=native,
                projection_match_document_id=matchDocument};budget.Add(row);rows.Add(row);
        }
        var value=Wire.Element(rows);budget.Add(value);return value;
    }
    internal static Snapshot Capture(Mc3dSolid body,ModelProfileElementsSpec358 edit,ModifyArguments args,
        DocumentState state,Db.Transaction tx,Action cancel,bool includeStandalone=false,string[]? retainedSourceHandles=null)
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
        var feature=profile.Dependent3dFeatureID.IsNull?null:profile.Dependent3dFeatureID.GetObjectOfType<ExtrudeFeature>();
        Need(profile.Dependent3dFeatureID.IsNull||feature!=null&&feature.ProfileID==profile.ID&&feature.HostSolidID==body.ID&&
            feature.DbEntity.Document!=null&&feature.DbEntity.Document.ID==owner.ID&&contents.Contains(feature.ID)&&feature.Status.ToString()=="Ok"&&
            !feature.IsSuppressed&&!feature.IsBeyondEndOfPart,
            "PROFILE_OWNERSHIP_MISMATCH","Use unused actual-owner construction or this part's retained healthy extrusion profile; no ownership transfer is implied.");
        var selected=ModelProfileElementsReader358.Capture(body,profile,state,tx,cancel);
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
        Need(feature==null?owners.Count==0:owners.Count==1&&owners[0]==Key(feature.ID),"PROFILE_OWNERSHIP_MISMATCH","Actual document native consumers do not identify exactly this selected extrusion.");
        var others=new List<JsonElement>();var sources=new Dictionary<string,JsonElement>(StringComparer.OrdinalIgnoreCase);var budget=new ModelInventoryBudget(cancel);
        void Source(string handle)
        {
            cancel();if(sources.ContainsKey(handle))return;var raw=CadStructureWriter.Find(state.Document.Database,handle);var entity=tx.GetObject(raw,Db.OpenMode.ForRead) as Db.Entity;
            Need(entity!=null&&entity.OwnerId==ModelFeatureReorderState345.Model(state.Document.Database,tx),"PROFILE_SOURCE_UNAVAILABLE","A retained drawing source is not present in ordinary Model Space.");
            var detail=Wire.Element(EntityAdapter.Detail(entity,EntityAdapter.Summary(entity,entity.OwnerId,"","",false),CancellationToken.None));cancel();
            var native=McObjectId.FromHandle(raw.Handle.Value,drawing.ID);
            var nativeSource=native.IsNull?null:native.GetObjectOfType<Multicad.DatabaseServices.StandardObjects.DbGeometry>();
            bool observeProxy=nativeSource!=null&&nativeSource.ID==native&&nativeSource.DbEntity.Document!=null;
            var value=Wire.Element(new{handle,native_id=Key(native),geometry=detail.GetProperty("geometry"),
                properties=EntityAdapter.StoredProperties(entity),metadata=Metadata(entity,tx,cancel,false,observeNativeProxy:observeProxy),
                reactor_observation=SourceReactors(entity,native,drawing,owner,selected,tx,cancel)});budget.Add(value);sources.Add(handle,value);
        }
        foreach(var id in contents)
        {
            cancel();var other=id.GetObjectOfType<SketchProfile>();if(other==null||other.ID==profile.ID)continue;
            var value=ModelProfileElementsReader358.Capture(body,other,state,tx,cancel);budget.Add(value.Stored);others.Add(value.Stored);
            foreach(var source in value.Projections.Sources)Source(source.handle);
        }
        foreach(var source in ProjectedHistorySources.Read(body,contents,state,tx,cancel))Source(source.handle);
        foreach(var source in selected.Projections.Sources)Source(source.handle);
        foreach(string handle in edit.AddSourceHandles)Source(handle);
        foreach(string handle in retainedSourceHandles??[])Source(handle);
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
        var direct=(feature?.GetDependent()??profile.GetDependent());var downstream=(feature?.GetAllDependent(false)??profile.GetAllDependent(false));
        Need(direct!=null&&downstream!=null,"DEPENDENCY_INVENTORY_UNAVAILABLE","Actual direct/transitive downstream inventory is unavailable.");ModelInventoryBudget.Ids(direct,cancel);ModelInventoryBudget.Ids(downstream,cancel);
        Need(direct.Concat(downstream).All(id=>id==body.ID||contents.Contains(id)||ModelViewFollowers.OwnsDependent(views,id)),"OUT_OF_SCOPE","A dependent is outside this retained native part.");
        var notices=new List<string>();notices.AddRange(ModelSketchConstraintObservation353.Warnings([selected.Constraints]));ModelFeatureOracle.Read(solid);
        cancel();using(var brep=new Teigha.BoundaryRepresentation.Brep(solid))
        {
            Need(!brep.IsNull,"INVALID_GEOMETRY","Native B-rep is unavailable.");bool? valid;
            try{valid=brep.CheckEntity();}
            catch(Teigha.Runtime.Exception error)when(error.ErrorStatus.ToString() is "NotImplemented" or "NotImplementedYet"){valid=null;}
            catch(NotSupportedException){valid=null;}
            cancel();Need(valid!=false,"INVALID_GEOMETRY","Native B-rep validation found an invalid rebuilt part.");if(valid==null)notices.Add(ModelFeatureReorderState345.ValidityWarning);
        }
        var bodyMetadata=Metadata(solid,tx,cancel,true,observeNativeProxy:true);var display=Wire.Element(new{layer=solid.LayerId.Handle.ToString(),linetype=solid.LinetypeId.Handle.ToString(),properties=EntityAdapter.StoredProperties(solid)});
        var sourceValues=Wire.Element(sources.OrderBy(pair=>pair.Key,StringComparer.OrdinalIgnoreCase).Select(pair=>pair.Value).ToArray());
        if(bodyMetadata.GetProperty("owned").EnumerateArray().Any(entry=>entry.TryGetProperty("assoc_manager_observation",out _))||
            sourceValues.EnumerateArray().Any(value=>value.GetProperty("metadata").GetProperty("owned").EnumerateArray().Any(entry=>entry.TryGetProperty("assoc_manager_observation",out _))))
            notices.Add("ASSOC_MANAGER_PRIVATE_STATE_UNOBSERVED: Element lifecycle retains exact resident manager anchors and all readable root/owned metadata, actual MCS variables/dependencies and available sketch constraints; private solver internals are not enumerated, initialized or claimed completely preserved.");
        notices.AddRange(NativeOwnedMetadata.NativeProxyWarnings(bodyMetadata,sourceValues,selected.Stored,Wire.Element(others)));
        budget.Add(new{parameters,selected=selected.Stored,other_profiles=others,sources=sourceValues,bodyMetadata,display,downstream});
        var inventory=ModelFeatureReorderState345.Inventory(state,tx,cancel);
        var drawingInventory=DrawingMembership(inventory,drawing,owner,selected,tx,cancel);
        return new(body,feature,representation,Key(drawing.ID),Key(owner.ID),contents,ModelProfileDiscovery348.ConstructionIds(owner,cancel),parameters,selected,
            Wire.Element(others.OrderBy(value=>value.GetProperty("profile_id").GetString(),StringComparer.OrdinalIgnoreCase).ToArray()),sourceValues,bodyMetadata,display,
            inventory,views,order,direct.Select(Key).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(id=>id,StringComparer.OrdinalIgnoreCase).ToArray(),
            downstream.Select(Key).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(id=>id,StringComparer.OrdinalIgnoreCase).ToArray(),owners.ToArray(),notices.ToArray(),drawingInventory);
    }
    static JsonElement CanonicalNode(JsonElement source,string sketch,string profile,HashSet<string> changing,Action cancel)
    {
        var value=JsonNode.Parse(source.GetRawText())!.AsObject();string id=value["mc_id"]!.GetValue<string>();
        if(value["dependencies"] is JsonArray dependencies)
        {
            var ids=dependencies.Select(item=>item!.GetValue<string>()).Where(edge=>
                !(id.Equals(sketch,StringComparison.OrdinalIgnoreCase)||id.Equals(profile,StringComparison.OrdinalIgnoreCase))||!changing.Contains(edge))
                .OrderBy(edge=>edge,StringComparer.OrdinalIgnoreCase).ToArray();value["dependencies"]=JsonSerializer.SerializeToNode(ids,Wire.Json);
        }
        if(value["parameters"] is JsonObject fields)
        {
            if(fields["edge_ids"] is JsonArray edges){fields["edge_count"]=edges.Count;fields.Remove("edge_ids");}
            // One drawing source may also feed another retained sketch. Its
            // history node survives, but this request's projection backlinks
            // necessarily change; every other source link remains exact.
            if(fields["kind"]?.GetValue<string>()=="projected_source"&&fields["projection_ids"] is JsonArray projections)
                fields["projection_ids"]=JsonSerializer.SerializeToNode(projections.Select(item=>{cancel();return item!.GetValue<string>();})
                    .Where(edge=>!changing.Contains(edge)).OrderBy(edge=>edge,StringComparer.OrdinalIgnoreCase).ToArray(),Wire.Json);
        }
        return Wire.Element(value);
    }
    static JsonElement UnchangedProfile(JsonElement value)
    {var copy=JsonNode.Parse(value.GetRawText())!.AsObject();copy.Remove("constraints");return Wire.Element(copy);}
    internal static void Verify(Snapshot before,Snapshot after,McObjectId[] removed,McObjectId[] added,
        SketchConstraintCurve[] desired,ModelCircleRegion350[]? regions,Action cancel)
    {
        var selected=after.Selected;string sketch=Key(selected.Sketch.ID),profile=Key(selected.Profile.ID);
        Need(before.DrawingDocument==after.DrawingDocument&&before.OwnerDocument==after.OwnerDocument&&before.Body.ID==after.Body.ID&&
            before.BodyMetadata.GetRawText()==after.BodyMetadata.GetRawText()&&before.Display.GetRawText()==after.Display.GetRawText(),
            "MODEL_STATE_UNAVAILABLE","Element lifecycle changed native document/body identity, display or readable owned data.");
        Need(before.FeatureOrder.SequenceEqual(after.FeatureOrder,StringComparer.OrdinalIgnoreCase)&&after.Body.LastPartItemID==before.Views.LastHistoryItem&&
            before.Feature?.ID==after.Feature?.ID&&before.Selected.Profile.ID==selected.Profile.ID&&before.Selected.Sketch.ID==selected.Sketch.ID&&
            before.Owners.SequenceEqual(after.Owners,StringComparer.OrdinalIgnoreCase),"PROFILE_OWNERSHIP_MISMATCH","Retained feature/profile/parent/forming order or consumer affinity changed.");
        if(before.Feature==null)
            Need(Equal(before.Parameters.GetProperty("native"),after.Parameters.GetProperty("native"),cancel),
                "NATIVE_GEOMETRY_MISMATCH","Editing unused construction changed the existing part's observed native geometry.");
        foreach(string field in new[]{"owner_document_id","profile_name","sketch_name","profile_host_solid_id","sketch_host_solid_id","profile_previous","sketch_previous","support_id","dependent_feature_id"})
            Need(Equal(before.Selected.Stored.GetProperty(field),selected.Stored.GetProperty(field),cancel),"PROFILE_OWNERSHIP_MISMATCH","Native profile/sketch identity, actual host, support, name or predecessor changed.");
        Need(Equal(before.Selected.Geometry.GetProperty("frame"),selected.Geometry.GetProperty("frame"),cancel)&&
            ModelSketchConstraintObservation353.Equivalent(before.Selected.Constraints,selected.Constraints,cancel),
            "MODEL_STATE_UNAVAILABLE","Native frame/support or available constraint inventory changed.");
        var old=before.Selected.Geometry.GetProperty("elements").EnumerateArray().ToDictionary(value=>value.GetProperty("element_id").GetString()!,StringComparer.OrdinalIgnoreCase);
        var current=selected.Geometry.GetProperty("elements").EnumerateArray().ToDictionary(value=>value.GetProperty("element_id").GetString()!,StringComparer.OrdinalIgnoreCase);
        var removedKeys=removed.Select(Key).ToHashSet(StringComparer.OrdinalIgnoreCase);var addedKeys=added.Select(Key).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach(var element in old)
        {
            cancel();if(removedKeys.Contains(element.Key)){Need(!current.ContainsKey(element.Key),"NATIVE_GEOMETRY_MISMATCH","A requested element remains in sketch geometry.");continue;}
            Need(current.TryGetValue(element.Key,out var actual)&&Equal(element.Value,actual,cancel),"NATIVE_GEOMETRY_MISMATCH","An unrequested native element/geometry was lost or changed.");
        }
        Need(current.Keys.Except(old.Keys,StringComparer.OrdinalIgnoreCase).ToHashSet(StringComparer.OrdinalIgnoreCase).SetEquals(addedKeys)&&
            removed.All(id=>!selected.Containing.Contains(id)&&!selected.Sketch.ContainsObject(id))&&added.All(id=>selected.ElementIds.Contains(id)&&selected.Containing.Contains(id)&&
                id.GetObjectOfType<ProjectionSketchEnt>() is {} projection&&projection.HostSketchID==selected.Sketch.ID&&
                (before.Feature==null?projection.HostSolidID.IsNull||projection.HostSolidID==after.Body.ID&&after.Contents.Contains(id):projection.HostSolidID==after.Body.ID&&after.Contents.Contains(id))),
            "PROFILE_OWNERSHIP_MISMATCH","Removed/added actual sketch membership or projection/native host disposition differs from the requested element change.");
        // Native included-object membership is broader than geometry keys.
        // Retain every unrelated included object/projection, even when it has
        // no independent geometry element in this selected contour.
        var oldContaining=before.Selected.Containing.ToHashSet();var newContaining=selected.Containing.ToHashSet();
        foreach(var id in oldContaining.Except(newContaining))
        {cancel();Need(removed.Contains(id),"PROFILE_OWNERSHIP_MISMATCH","Element lifecycle lost an unrequested native included object/projection.");}
        foreach(var id in newContaining.Except(oldContaining))
        {cancel();Need(added.Contains(id),"PROFILE_OWNERSHIP_MISMATCH","Element lifecycle introduced an unrequested native included object/projection.");}
        var actualBoundary=ModelProfileLifecycleState340.Boundary(selected.Geometry,selected.Plane,cancel);
        Need(ModelProfileElementGeometry358.SameBoundary(desired,actualBoundary,cancel),"NATIVE_GEOMETRY_MISMATCH","Remaining/new native element geometry differs from the surviving design contour.");
        VerifySelected(selected,desired,regions,cancel);
        var retiredLinks=before.Selected.Projections.Sources.ToDictionary(source=>source.id,
            source=>source.projection_ids.Where(removedKeys.Contains).ToArray(),StringComparer.OrdinalIgnoreCase);
        var newLinks=selected.Projections.Sources.ToDictionary(source=>source.id,
            source=>source.projection_ids.Where(addedKeys.Contains).ToArray(),StringComparer.OrdinalIgnoreCase);
        var oldLinked=before.Selected.Projections.Sources.Select(source=>source.id).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var currentLinked=selected.Projections.Sources.Select(source=>source.id).ToHashSet(StringComparer.OrdinalIgnoreCase);
        if(!ModelProfileSourcePreservation360.Retained(before.Sources,after.Sources,sketch,retiredLinks,newLinks,oldLinked,currentLinked,
            (left,right)=>Equal(left,right,cancel),cancel,out var sourceReason,out var sourceBefore,out var sourceAfter))
            throw new DomainException("PROFILE_SOURCE_CHANGED","Element lifecycle changed an original/new drawing source, unrelated association or its readable metadata.",false,
                ModelProfileSourcePreservation360.Describe(before.Sources,after.Sources,sourceBefore,sourceAfter,sourceReason,cancel));
        var other=after.OtherProfiles.EnumerateArray().ToDictionary(value=>value.GetProperty("profile_id").GetString()!,StringComparer.OrdinalIgnoreCase);
        foreach(var peer in before.OtherProfiles.EnumerateArray())
        {cancel();Need(other.TryGetValue(peer.GetProperty("profile_id").GetString()!,out var value)&&Equal(UnchangedProfile(peer),UnchangedProfile(value),cancel)&&
            ModelSketchConstraintObservation353.Equivalent(peer.GetProperty("constraints"),value.GetProperty("constraints"),cancel),"PROFILE_SOURCE_CHANGED","Another retained profile design or available constraint inventory changed.");}
        var changes=removed.Concat(added).ToHashSet();var sourceEdges=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach(var source in before.Selected.Projections.Sources)
            if(source.projection_ids.All(id=>removedKeys.Contains(id)))
                sourceEdges.Add(source.id); // Pure completed before-state, never dereference retired native wrappers.
        foreach(var source in selected.Projections.Sources)if(source.projection_ids.Any(addedKeys.Contains))sourceEdges.Add(source.id);
        var edgeKeys=changes.Select(Key).Concat(sourceEdges).ToHashSet(StringComparer.OrdinalIgnoreCase);
        Need(before.Contents.Except(after.Contents).All(id=>removed.Contains(id)||sourceEdges.Contains(Key(id)))&&after.Contents.Except(before.Contents).All(id=>added.Contains(id)||sourceEdges.Contains(Key(id)))&&
            before.Construction.Except(after.Construction).All(removed.Contains)&&after.Construction.Except(before.Construction).All(added.Contains),
            "MODEL_STATE_UNAVAILABLE","Native history/document construction changed outside requested element retirement/addition.");
        if(!ModelProfileDrawingMembership360.Retained(before.DrawingInventory,after.DrawingInventory,before.Representation.Handle.ToString(),after.Representation.Handle.ToString(),
            removedKeys,addedKeys,cancel,out var inventoryReason))
            throw new DomainException("OBJECT_IDENTITY_MISMATCH","An unrelated drawing object changed membership or a duplicate body remains.",false,
                ModelProfileDrawingMembership360.Describe(before.DrawingInventory,after.DrawingInventory,before.Representation.Handle.ToString(),after.Representation.Handle.ToString(),inventoryReason,cancel));
        var nodes=after.Parameters.GetProperty("nodes").EnumerateArray().ToDictionary(value=>value.GetProperty("mc_id").GetString()!,StringComparer.OrdinalIgnoreCase);
        foreach(var node in before.Parameters.GetProperty("nodes").EnumerateArray())
        {
            cancel();string id=node.GetProperty("mc_id").GetString()!;if(removedKeys.Contains(id))continue;
            if(sourceEdges.Contains(id)&&!nodes.ContainsKey(id))continue;
            Need(nodes.TryGetValue(id,out var value)&&Equal(CanonicalNode(node,sketch,profile,edgeKeys,cancel),CanonicalNode(value,sketch,profile,edgeKeys,cancel),cancel),
                "MODEL_STATE_UNAVAILABLE","Retained native feature parameters, source references, predecessor or unrequested dependency changed.");
        }
        Need(Equal(before.Parameters.GetProperty("variables"),after.Parameters.GetProperty("variables"),cancel)&&before.Downstream.SequenceEqual(after.Downstream,StringComparer.OrdinalIgnoreCase)&&
            before.DirectDownstream.SequenceEqual(after.DirectDownstream,StringComparer.OrdinalIgnoreCase),"MODEL_DRIVER_CHANGED","An existing native variable/formula/driver or downstream consumer changed.");
        ModelViewFollowers.Verify(after.Body,before.Views,cancel,refreshStandalone:true);
    }
    internal static void VerifySelected(ModelProfileElementsReader358.Snapshot selected,SketchConstraintCurve[] desired,ModelCircleRegion350[]? regions,Action cancel)
    {
        if(regions==null)
        {
            Need(selected.Outer.Length==1&&selected.Holes.Length==1&&selected.Holes[0].Length==0&&ModelProfileElementGeometry358.SameBoundary(desired,selected.Outer[0],cancel),
                "PROFILE_GEOMETRY_MISMATCH","Actual selected native profile is not the surviving single complete design contour.");return;
        }
        var circles=desired.ToDictionary(curve=>curve.Id,StringComparer.OrdinalIgnoreCase);var observed=new List<ModelCircleRegion350>();
        string Match(SketchConstraintCurve[] curves)
        {
            cancel();var matches=circles.Where(pair=>ModelProfileElementGeometry358.SameBoundary([pair.Value],curves,cancel)).Select(pair=>pair.Key).ToArray();
            Need(matches.Length==1,"PROFILE_GEOMETRY_MISMATCH","Actual native selected Circle boundary does not match one exact surviving element.");return matches[0];
        }
        for(int i=0;i<selected.Outer.Length;i++){cancel();observed.Add(new(Match(selected.Outer[i]),selected.Holes[i].Select(Match).OrderBy(id=>id,StringComparer.OrdinalIgnoreCase).ToArray()));}
        Need(Equal(Wire.Element(regions.OrderBy(region=>region.outer_element_id,StringComparer.OrdinalIgnoreCase).ToArray()),
            Wire.Element(observed.OrderBy(region=>region.outer_element_id,StringComparer.OrdinalIgnoreCase).ToArray()),cancel),"PROFILE_GEOMETRY_MISMATCH","Actual native selected Circle areas/holes differ from the explicit surviving region design.");
    }
}
