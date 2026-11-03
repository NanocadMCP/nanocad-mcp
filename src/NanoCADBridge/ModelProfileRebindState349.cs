using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using System.Text.Json.Nodes;
using Multicad;
using Multicad.DatabaseServices;
using Multicad.Mc3D;
using NanoCAD.Contracts;
using Db=Teigha.DatabaseServices;

namespace NanoCADBridge;

// This state adapter owns the intended profile-edge change. Native geometry,
// source identities and forming-feature order are observed, never rewritten.
internal static class ModelProfileRebindState349
{
    internal const string AssocWarning="ASSOC_MANAGER_PRIVATE_STATE_UNOBSERVED: Exact resident manager identity/owner, readable owned dictionaries/Xrecords, XData and reactors remain checked together with actual body-owner MCS variables/dependencies and available sketch constraints. The SDK exposes no private manager-state inventory; private solver internals are not claimed completely observed or preserved. Profile rebind uses the native ProfileID setter and body rebuild without directly inspecting or editing private manager internals.";
    internal sealed record Snapshot(Mc3dSolid Body,Db.ObjectId Representation,string DrawingDocument,string OwnerDocument,
        McObjectId[] Contents,McObjectId[] Construction,JsonElement Parameters,JsonElement Profiles,JsonElement Sources,
        JsonElement BodyMetadata,JsonElement Display,HashSet<Db.ObjectId> Inventory,ModelViewFollowers.Snapshot Views,
        string[] FeatureOrder,string[] DirectDependents,string[] AllDependents,string[] Drivers,string[] OldOwners,string[] NextOwners,
        ModelProfileLifecycleState340.Snapshot Old,ModelProfileLifecycleState340.Snapshot Next,string[] Warnings,
        ModelProfileConsumers410.Snapshot OldConsumers,ModelProfileConsumers410.Snapshot NextConsumers,HashSet<string> Affected,
        ModelSketchConstraintMetadata379.Binding[] StorageBindings,JsonElement ConstraintObservations);
    internal static void Need([DoesNotReturnIf(false)] bool ok,string code,string message)
    {if(!ok)throw new DomainException(code,message);}
    internal static string Key(McObjectId id)=>id.ToString();
    internal static bool Same(McObjectId id,string value)=>Key(id).Equals(value,StringComparison.OrdinalIgnoreCase);
    internal static bool Equivalent(JsonElement a,JsonElement b,Action cancel)
    {
        var pairs=new Stack<(JsonElement A,JsonElement B)>();pairs.Push((a,b));
        while(pairs.Count>0)
        {
            cancel();var (left,right)=pairs.Pop();if(left.ValueKind!=right.ValueKind)return false;
            if(left.ValueKind==JsonValueKind.Object)
            {
                static bool Field(string name)=>name is not ("inspection_duration_ms" or "budget_notice");
                var fields=left.EnumerateObject().Where(value=>Field(value.Name)).ToArray();
                if(fields.Length!=right.EnumerateObject().Count(value=>Field(value.Name)))return false;
                foreach(var field in fields)
                {
                    cancel();if(!right.TryGetProperty(field.Name,out var value))return false;
                    if(field.Name is "root" or "properties" or "metadata" or "owned")
                    {if(field.Value.GetRawText()!=value.GetRawText())return false;}
                    else pairs.Push((field.Value,value));
                }
            }
            else if(left.ValueKind==JsonValueKind.Array)
            {if(left.GetArrayLength()!=right.GetArrayLength())return false;for(int i=0;i<left.GetArrayLength();i++)pairs.Push((left[i],right[i]));}
            else if(left.ValueKind==JsonValueKind.Number)
            {if(!ModelParameterSnapshot.Near(left.GetDouble(),right.GetDouble()))return false;}
            else if(left.GetRawText()!=right.GetRawText())return false;
        }
        return true;
    }
    static JsonElement Root(Db.DBObject item,Action cancel)
    {
        using var data=item.XData;using var reactors=item.GetPersistentReactorIds();var budget=new ModelInventoryBudget(cancel);
        var ids=new List<string>();foreach(Db.ObjectId id in reactors){cancel();ids.Add(id.Handle.ToString());budget.Add(ids[^1]);}
        object? xdata=data==null?null:NativeOwnedMetadata.Buffer(data,cancel,value=>budget.Add(value));
        return Wire.Element(new{type=item.GetType().FullName,has_fields=item.HasFields,
            extension=item.ExtensionDictionary.IsNull?null:item.ExtensionDictionary.Handle.ToString(),xdata,
            reactors=ids.OrderBy(value=>value,StringComparer.OrdinalIgnoreCase).ToArray()});
    }
    static JsonElement Owned(Db.Entity entity,Db.Transaction tx,Action cancel,bool body)
    {
        // The existing adapter observes the exact resident AssocManager anchor
        // and keeps its readable common data/extensions; it invents no graph.
        var entries=JsonNode.Parse(NativeOwnedMetadata.Capture(entity,tx,cancel,observeAssocManager:true).GetRawText())!.AsArray();
        if(body)foreach(var entry in entries)
        {
            cancel();if(entry!["common"] is JsonObject common&&common["owner_handle"]?.GetValue<string>()==Validation.NormalizeHandle(entity.Handle.ToString()))
                common["owner_handle"]="$CURRENT_BODY_REPRESENTATION";
        }
        var value=Wire.Element(new{root=Root(entity,cancel),owned=entries});new ModelInventoryBudget(cancel).Add(value);return value;
    }
    static string[] Sorted(IEnumerable<McObjectId> ids,Action cancel)
    {return ids.Select(id=>{cancel();return Key(id);}).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(id=>id,StringComparer.OrdinalIgnoreCase).ToArray();}
    static string[] Downstream(HistoryItem feature,Mc3dSolid body,McObjectId[] contents,ModelViewFollowers.Snapshot views,bool transitive,Action cancel)
    {
        var ids=transitive?feature.GetAllDependent(false):feature.GetDependent();
        Need(ids!=null,"DEPENDENCY_INVENTORY_UNAVAILABLE","Native extrusion downstream inventory is unavailable; no empty-consumer claim is made.");
        ModelInventoryBudget.Ids(ids,cancel);var known=contents.ToHashSet();known.Add(body.ID);
        foreach(var id in ids)
        {
            cancel();Need(!id.IsNull&&(known.Contains(id)||ModelViewFollowers.OwnsDependent(views,id)),"DEPENDENCY_OWNERSHIP_MISMATCH","The extrusion has a dependent outside this retained part.");
            if(id==body.ID||ModelViewFollowers.OwnsDependent(views,id))continue;var item=id.GetObjectOfType<HistoryItem>();
            Need(item!=null&&item.HostSolidID==body.ID&&(item.Status.ToString()=="Ok"||ProjectedHistorySources.HealthySuppressed(item))&&!item.IsBeyondEndOfPart,
                "DEPENDENCY_OWNERSHIP_MISMATCH","A downstream item is unclassified, inactive or unhealthy.");
        }
        return Sorted(ids,cancel);
    }
    static string[] Owners(McDocument document,McObjectId profile,Action cancel)
    {
        var result=new HashSet<McObjectId>();var budget=new ModelInventoryBudget(cancel);
        void Owner(HistoryItem? item,bool uses)
        {
            cancel();Need(item!=null&&item.DbEntity.Document!=null&&item.DbEntity.Document.ID==document.ID,
                "PROFILE_DISCOVERY_UNAVAILABLE","A native forming-feature inventory item has unavailable owner scope.");
            if(uses){result.Add(item.ID);budget.Add(Key(item.ID));}
        }
        foreach(var id in ModelProfileDiscovery348.Ids<ExtrudeFeature>(document,cancel))
        {var item=id.GetObjectOfType<ExtrudeFeature>();Owner(item,item!=null&&item.ProfileID==profile);}
        foreach(var id in ModelProfileDiscovery348.Ids<RevolveFeature>(document,cancel))
        {var item=id.GetObjectOfType<RevolveFeature>();Owner(item,item!=null&&item.ProfileID==profile);}
        foreach(var id in ModelProfileDiscovery348.Ids<ExtrudeAlongPathFeature>(document,cancel))
        {var item=id.GetObjectOfType<ExtrudeAlongPathFeature>();Owner(item,item!=null&&item.ProfileID==profile);}
        foreach(var id in ModelProfileDiscovery348.Ids<LoftFeature>(document,cancel))
        {
            var item=id.GetObjectOfType<LoftFeature>();bool uses=item!=null&&item.ProfileID==profile;
            if(item!=null)for(int i=0;i<item.SectionsCount;i++){cancel();var section=item.GetSection(i);Need(section!=null,"PROFILE_DISCOVERY_UNAVAILABLE","Native loft section inventory is unavailable.");uses|=section.ID==profile;}
            Owner(item,uses);
        }
        return Sorted(result,cancel);
    }
    internal static Snapshot Capture(Mc3dSolid body,ExtrudeFeature feature,ModelProfileRebindSpec349 edit,
        ModifyArguments args,DocumentState state,Db.Transaction tx,Action cancel,bool includeStandalone=false)
    {
        var drawing=ModelFeatureReorderState345.Scope(state,tx);var owner=ModelProfileDiscovery348.OwnerDocument(body,state);
        var representation=ModelHistoryEditGuard.CurrentRepresentation(body,state,tx,cancel);
        var solid=tx.GetObject(representation,Db.OpenMode.ForRead) as Db.Solid3d;
        Need(solid!=null&&!solid.IsNull&&McObjectId.FromHandle(representation.Handle.Value,drawing.ID)==body.ID,
            "OBJECT_IDENTITY_MISMATCH","The unique current native body representation is unavailable.");
        var layer=(Db.LayerTableRecord)tx.GetObject(solid.LayerId,Db.OpenMode.ForRead);
        Need(!layer.IsLocked&&!layer.IsDependent&&!layer.IsFrozen&&!layer.IsOff,"LAYER_UNAVAILABLE","Body layer is unavailable for editing.");
        var contents=ModelInventoryBudget.History(body,cancel).ToArray();
        Need(contents.Contains(feature.ID)&&feature.HostSolidID==body.ID&&feature.ID.GetObjectOfType<ExtrudeFeature>()?.ID==feature.ID,
            "PROFILE_OWNERSHIP_MISMATCH","Selected extrusion is absent from the actual native body composition.");
        var oldProfile=ModelProfileDiscovery348.Resolve(edit.ExpectedCurrentProfileId,owner,cancel);
        var newProfile=ModelProfileDiscovery348.Resolve(edit.ProfileId,owner,cancel);
        // Read old construction in its actual document even after native release.
        // A stale old forming backlink does not invent a host or forbid reading it.
        var old=ModelProfileRebindProfiles414.Capture(oldProfile,null,state,tx,cancel,owner);
        var next=ModelProfileRebindProfiles414.Capture(newProfile,body,state,tx,cancel,owner);
        var views=ModelViewFollowers.Resolve(body,contents,cancel,includeStandalone);
        var read=new ModelParameterArguments(args.BridgeId,args.DocumentId,args.ExpectedRevision,args.OperationId,args.Units,
            Validation.NormalizeHandle(representation.Handle.ToString()),"","",0);
        var parameters=ModelParameterSnapshot.Capture(read,state,cancel);ModelParameterSnapshot.Healthy(parameters);
        var historyNodes=parameters.GetProperty("nodes").EnumerateArray().ToArray();
        Need(historyNodes.All(node=>!node.TryGetProperty("detail_unavailable_reason",out var reason)||reason.ValueKind==JsonValueKind.Null),
            "HISTORY_UNAVAILABLE","Retained history contains an unverified native source or support.");
        var links=new List<ModelSequenceLink>();var forming=new List<string>();
        foreach(var id in contents)
        {
            cancel();var item=id.GetObjectOfType<HistoryItem>();
            if(item!=null)Need((item.HostSolidID==body.ID||item.HostSolidID.IsNull&&(id.GetObjectOfType<PlanarSketch>()!=null||id.GetObjectOfType<SketchProfile>()!=null))&&
                (item.Status.ToString()=="Ok"||ProjectedHistorySources.HealthySuppressed(item))&&!item.IsBeyondEndOfPart,
                "MODEL_STATE_UNAVAILABLE","Retained body history is unhealthy or has foreign ownership.");
            links.Add(new(Key(id),item==null||item.OrderObjBefore.IsNull?null:Key(item.OrderObjBefore)));
            if(ModelFeatureReorderState345.Forming(id))
            {
                forming.Add(Key(id));var node=historyNodes.Single(value=>value.GetProperty("mc_id").GetString()!.Equals(Key(id),StringComparison.OrdinalIgnoreCase));
                Need(node.GetProperty("parameters").ValueKind==JsonValueKind.Object,"MODEL_PARAMETERS_UNAVAILABLE","A retained forming feature needs its typed preservation adapter.");
            }
        }
        var order=ModelFeatureReorderPlanning345.FeatureOrder(links.ToArray(),forming.ToArray(),Key(views.LastModelItem),cancel);
        var profiles=new List<ModelProfileLifecycleState340.Snapshot>{old};if(next.Profile.ID!=old.Profile.ID)profiles.Add(next);
        foreach(var id in contents)
        {
            cancel();var profile=id.GetObjectOfType<SketchProfile>();
            if(profile!=null&&profile.ID!=oldProfile.ID&&profile.ID!=newProfile.ID)
                profiles.Add(ModelProfileRebindProfiles414.Capture(profile,body,state,tx,cancel,owner));
        }
        var sources=new Dictionary<string,JsonElement>(StringComparer.OrdinalIgnoreCase);
        void Source(string handle)
        {
            cancel();if(sources.ContainsKey(handle))return;
            var id=CadStructureWriter.Find(state.Document.Database,handle);var entity=tx.GetObject(id,Db.OpenMode.ForRead) as Db.Entity;
            Need(entity!=null&&entity.OwnerId==ModelFeatureReorderState345.Model(state.Document.Database,tx),"PROFILE_SOURCE_UNAVAILABLE","A retained source is no longer top-level Model Space.");
            var summary=EntityAdapter.Summary(entity,entity.OwnerId,"","",false);var detail=Wire.Element(EntityAdapter.Detail(entity,summary,CancellationToken.None));
            sources.Add(handle,Wire.Element(new{handle,native_id=Key(McObjectId.FromHandle(id.Handle.Value,drawing.ID)),
                geometry=detail.GetProperty("geometry"),properties=EntityAdapter.StoredProperties(entity),metadata=Owned(entity,tx,cancel,false)}));
        }
        foreach(var profile in profiles)foreach(var source in profile.Sources)Source(source.handle);
        foreach(var source in ProjectedHistorySources.Read(body,contents,state,tx,cancel))Source(source.handle);
        // Preserve resident drawing paths too. Body-owned/generated targets are
        // checked by NativePathSources and their retained feature/profile graph;
        // a native GUID's Handle is never accepted without the exact roundtrip.
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
        var budget=new ModelInventoryBudget(cancel);foreach(var value in sources.Values)budget.Add(value);
        var profileValues=Wire.Element(profiles.OrderBy(value=>Key(value.Profile.ID),StringComparer.OrdinalIgnoreCase).Select(value=>value.Stored).ToArray());budget.Add(profileValues);
        var direct=Downstream(feature,body,contents,views,false,cancel);var all=Downstream(feature,body,contents,views,true,cancel);
        var oldUses=ModelProfileRebindConsumers414.Capture(oldProfile,owner,body,feature.ProfileID==newProfile.ID?feature.ID:McObjectId.Null,cancel);
        var nextUses=ModelProfileRebindConsumers414.Capture(newProfile,owner,body,McObjectId.Null,cancel);
        var affected=ModelDependencyGraph410.OwnedClosure(body,[feature.ID],cancel,views);
        var drivers=Sorted(new[]{feature.DistanceParamVarID,feature.Distance2ParamVarID,feature.TaperAngleParamVarID,feature.TaperAngle2ParamVarID}.Where(id=>!id.IsNull),cancel);
        var notices=new List<string>();
        try{cancel();using var brep=new Teigha.BoundaryRepresentation.Brep(solid);Need(!brep.IsNull&&brep.CheckEntity(),"INVALID_GEOMETRY","Native B-rep validation reports an invalid rebuilt part.");}
        catch(Teigha.Runtime.Exception){notices.Add(ModelFeatureReorderState345.ValidityWarning);}
        catch(NotSupportedException){notices.Add(ModelFeatureReorderState345.ValidityWarning);}
        var metadata=Owned(solid,tx,cancel,true);
        if(NativeAssocManagerObservation356.Warnings(metadata).Length>0||sources.Values.Any(source=>NativeAssocManagerObservation356.Warnings(source.GetProperty("metadata")).Length>0))
            notices.Add(AssocWarning);
        var display=Wire.Element(new{layer=solid.LayerId.Handle.ToString(),linetype=solid.LinetypeId.Handle.ToString(),properties=EntityAdapter.StoredProperties(solid)});
        // Only these exact native records participate in the intended ProfileID
        // binding change. Their owned user records/foreign links stay protected.
        var storage=ModelSketchConstraintMetadata379.Bind([feature.ID,oldProfile.ID,newProfile.ID,old.Sketch.ID,next.Sketch.ID],representation,owner,drawing,state,tx,cancel);
        var constraints=new[]{old.Sketch,next.Sketch}.DistinctBy(sketch=>sketch.ID).OrderBy(sketch=>Key(sketch.ID),StringComparer.OrdinalIgnoreCase)
            .Select(sketch=>ModelSketchConstraintObservation353.Read(body,sketch,cancel)).ToArray();
        notices.AddRange(ModelSketchConstraintObservation353.Warnings(constraints));var constraintValues=Wire.Element(constraints);
        var sourceValues=Wire.Element(sources.OrderBy(pair=>pair.Key,StringComparer.OrdinalIgnoreCase).Select(pair=>pair.Value).ToArray());
        budget.Add(new{parameters,profileValues,sourceValues,metadata,display,direct,all,drivers,storage,constraintValues});
        return new(body,representation,Key(drawing.ID),Key(owner.ID),contents,ModelProfileDiscovery348.ConstructionIds(owner,cancel),
            parameters,profileValues,sourceValues,metadata,display,ModelFeatureReorderState345.Inventory(state,tx,cancel),views,order,direct,all,drivers,
            oldUses.Consumers.Select(item=>item.FeatureId).ToArray(),nextUses.Consumers.Select(item=>item.FeatureId).ToArray(),old,next,notices.ToArray(),oldUses,nextUses,affected,storage,constraintValues);
    }
    static void RemoveEdge(JsonObject item,string property,string? edge,Action cancel)
    {
        if(item[property] is not JsonArray values)return;
        var kept=new List<string>();foreach(var value in values){cancel();string id=value!.GetValue<string>();if(edge==null||!id.Equals(edge,StringComparison.OrdinalIgnoreCase))kept.Add(id);}
        item[property]=JsonSerializer.SerializeToNode(kept.OrderBy(id=>id,StringComparer.OrdinalIgnoreCase).ToArray(),Wire.Json);
    }
    // Native document/body acquisition and assigned ordering are scope changes.
    // Actual host composition and complete forming order are verified separately.
    // Parent/support/source/contour and all non-order dependencies stay compared.
    internal static JsonElement ProfileDesign(JsonElement value,bool scopeChange,Action cancel)
    {
        var node=JsonNode.Parse(value.GetRawText())!.AsObject();
        RemoveEdge(node,"dependencies",node["profile_previous"]?.GetValue<string>(),cancel);
        RemoveEdge(node,"sketch_dependencies",node["sketch_previous"]?.GetValue<string>(),cancel);
        node.Remove("profile_previous");node.Remove("sketch_previous");
        // A plane's in-plane origin/U/V are representation choices. Capture
        // independently validates its oriented normal against the sketch frame.
        // Keep physical plane offset/normal plus the exact reported sketch frame.
        var plane=value.GetProperty("native_plane");var origin=plane.GetProperty("origin");var normal=plane.GetProperty("normal");
        double offset=0;for(int i=0;i<3;i++)offset+=origin[i].GetDouble()*normal[i].GetDouble();
        node["native_plane"]=JsonSerializer.SerializeToNode(new{normal,offset},Wire.Json);
        if(scopeChange){node.Remove("dependent_feature_id");node.Remove("profile_host_solid_id");node.Remove("sketch_host_solid_id");}
        return Wire.Element(node);
    }
    internal static ModelProfileDisposition349 Disposition(Snapshot after)
    {
        var profile=after.Old.Profile;var sketch=after.Old.Sketch;bool member=after.Contents.Contains(profile.ID);
        string disposition=profile.Dependent3dFeatureID.IsNull?"native_forming_backlink_absent":
            profile.Dependent3dFeatureID==after.Next.Profile.Dependent3dFeatureID?"retained_previous_backlink":"native_forming_backlink_present";
        return new(Key(profile.ID),Key(sketch.ID),after.OwnerDocument,ModelProfileDiscovery348.Id(profile.HostSolidID),
            ModelProfileDiscovery348.Id(sketch.HostSolidID),ModelProfileDiscovery348.Id(profile.Dependent3dFeatureID),member,disposition,
            "retained_protected; no release_or_reuse_guarantee; obtain_fresh_exact_profile_read_before_any_later_consumer");
    }
    static JsonElement NodeDesign(JsonElement value,string feature,string oldProfile,string nextProfile,Action cancel)
    {
        var node=JsonNode.Parse(value.GetRawText())!.AsObject();string id=node["mc_id"]!.GetValue<string>();
        if(id.Equals(feature,StringComparison.OrdinalIgnoreCase))
        {
            string? previous=node["native_order_before_id"]?.GetValue<string>();node.Remove("native_order_before_id");
            foreach(string field in new[]{"dependencies","stable_dependencies"})
            {RemoveEdge(node,field,previous,cancel);RemoveEdge(node,field,oldProfile,cancel);RemoveEdge(node,field,nextProfile,cancel);}
            var parameters=node["parameters"]!.AsObject();parameters.Remove("profile_id");parameters.Remove("native_order_before_id");parameters.Remove("profile_rebind");
            if(parameters["terminal"] is JsonObject terminal)terminal.Remove("profile_id");
        }
        return Wire.Element(node);
    }
    internal static HashSet<string> Construction(ModelProfileLifecycleState340.Snapshot profile)
    {
        var result=new HashSet<string>(StringComparer.OrdinalIgnoreCase){Key(profile.Profile.ID),Key(profile.Sketch.ID)};
        result.UnionWith(profile.ProjectionIds.Select(Key));result.UnionWith(profile.Sources.Select(source=>source.id));return result;
    }
    internal static bool ConstraintsRetained(JsonElement before,JsonElement after,Action cancel)
    {
        var current=after.EnumerateArray().ToDictionary(value=>value.GetProperty("sketch_id").GetString()!,StringComparer.OrdinalIgnoreCase);
        var prior=before.EnumerateArray().ToArray();
        return prior.Length==current.Count&&prior.All(value=>current.TryGetValue(value.GetProperty("sketch_id").GetString()!,out var next)&&ModelSketchConstraintObservation353.Equivalent(value,next,cancel));
    }
    internal static string[] Verify(Snapshot before,Snapshot after,ExtrudeFeature feature,ModelProfileRebindSpec349 edit,Action cancel)
    {
        var warnings=new List<string>();
        Need(before.DrawingDocument==after.DrawingDocument&&before.OwnerDocument==after.OwnerDocument,"DOCUMENT_CONTEXT_MISMATCH","Native drawing or actual owner document changed during rebind.");
        Need(before.Parameters.GetProperty("owner").GetString()==after.OwnerDocument,"DOCUMENT_CONTEXT_MISMATCH","Selected body variable-owner document does not match actual profile scope.");
        Need(before.FeatureOrder.SequenceEqual(after.FeatureOrder,StringComparer.OrdinalIgnoreCase)&&before.Body.LastPartItemID==before.Views.LastHistoryItem&&after.Body.LastPartItemID==before.Views.LastHistoryItem,
            "MODEL_ORDER_MISMATCH","Profile rebind changed forming-feature order or native End-of-Part.");
        Need(before.Display.GetRawText()==after.Display.GetRawText(),"MODEL_STATE_UNAVAILABLE","Profile rebind changed retained body display.");
        // Native binding payload can change only at the selected records whose
        // actual current identity and reverse owner path are proved. Retain all
        // prior user records, XData/Fields, ownership and foreign references.
        var empty=Wire.Element(new{});
        var storage=ModelSketchConstraintMetadata379.Compare(empty,empty,before.BodyMetadata,after.BodyMetadata,before.StorageBindings,after.StorageBindings,cancel);
        Need(storage.retained,"MODEL_STATE_UNAVAILABLE","Profile rebind changed prior user data, foreign links or unproved native owned storage.");
        if(storage.changed)warnings.Add(ModelSketchConstraintMetadata379.Warning);
        Need(after.Inventory.Except(before.Inventory).All(id=>id==after.Representation)&&before.Inventory.Except(after.Inventory).All(id=>id==before.Representation)&&
            (after.Representation==before.Representation||!after.Inventory.Contains(before.Representation)),"OBJECT_IDENTITY_MISMATCH","Rebind changed an unrelated drawing object or left a duplicate body representation.");
        Need(before.Construction.ToHashSet().SetEquals(after.Construction),"PROFILE_OWNERSHIP_MISMATCH","Rebind removed document construction or created an unrelated sketch/profile/projection.");
        Need(Equivalent(before.Sources,after.Sources,cancel),"PROFILE_SOURCE_CHANGED","Rebind changed retained drawing source geometry, identity, display or owned data.");
        Need(ConstraintsRetained(before.ConstraintObservations,after.ConstraintObservations,cancel),"MODEL_DEPENDENCY_CHANGED","Rebind changed observed sketch constraints, their variable bindings or native constraint-manager scope.");
        var next=after.Next;
        Need(feature.ProfileID==next.Profile.ID&&next.Profile.DbEntity.Document?.ID==feature.DbEntity.Document.ID&&
            (next.Profile.HostSolidID.IsNull||next.Profile.HostSolidID==after.Body.ID&&after.Contents.Contains(next.Profile.ID))&&
            (next.Sketch.HostSolidID.IsNull||next.Sketch.HostSolidID==after.Body.ID&&after.Contents.Contains(next.Sketch.ID)),
            "PROFILE_OWNERSHIP_MISMATCH","The exact new profile lacks actual same-owner construction/selected-part affinity.");
        ModelProfileRebindConsumers414.VerifyDelta(before.OldConsumers,after.OldConsumers,feature.ID,false,cancel);
        ModelProfileRebindConsumers414.VerifyDelta(before.NextConsumers,after.NextConsumers,feature.ID,true,cancel);
        Need(next.ProjectionIds.All(id=>id.GetObjectOfType<ProjectionSketchEnt>() is {} p&&p.DbEntity.Document?.ID==next.Profile.DbEntity.Document.ID&&p.HostSketchID==next.Sketch.ID&&
            (p.HostSolidID.IsNull||p.HostSolidID==after.Body.ID&&after.Contents.Contains(id))),
            "PROFILE_OWNERSHIP_MISMATCH","New profile projections lack actual body/sketch composition.");
        var dependencies=feature.GetDependsOn(false,false);Need(dependencies!=null&&dependencies.Contains(next.Profile.ID),"DEPENDENCY_OWNERSHIP_MISMATCH","Extrusion lost its actual new-profile dependency.");
        Need(Equivalent(ProfileDesign(before.Old.Stored,true,cancel),ProfileDesign(after.Old.Stored,true,cancel),cancel)&&
            Equivalent(ProfileDesign(before.Next.Stored,true,cancel),ProfileDesign(after.Next.Stored,true,cancel),cancel),
            "PROFILE_GEOMETRY_MISMATCH","Rebind changed retained old/new contour, sketch, plane, support or source bindings.");
        var oldSet=Construction(before.Old);var nextSet=Construction(before.Next);
        var allowed=oldSet.Union(nextSet).ToHashSet(StringComparer.OrdinalIgnoreCase);
        if(after.OldOwners.Length>0)
            Need(before.Contents.Where(id=>oldSet.Contains(Key(id))).All(after.Contents.Contains),"PROFILE_OWNERSHIP_MISMATCH","Rebind removed retained construction still used by another actual profile consumer.");
        Need(before.Contents.Where(id=>!oldSet.Contains(Key(id))).All(after.Contents.Contains)&&after.Contents.Except(before.Contents).All(id=>nextSet.Contains(Key(id))),
            "PROFILE_OWNERSHIP_MISMATCH","Native history changed outside the selected old/new construction scope.");
        var afterNodes=ModelDependencyGraph410.ProtectedNodes(after.Parameters,before.Affected,cancel:cancel,sketchMotion:true).EnumerateArray().ToDictionary(node=>node.GetProperty("mc_id").GetString()!,StringComparer.OrdinalIgnoreCase);
        foreach(var node in ModelDependencyGraph410.ProtectedNodes(before.Parameters,before.Affected,cancel:cancel,sketchMotion:true).EnumerateArray())
        {
            cancel();string id=node.GetProperty("mc_id").GetString()!;if(allowed.Contains(id))continue;
            Need(afterNodes.TryGetValue(id,out var current)&&Equivalent(NodeDesign(node,edit.FeatureId,edit.ExpectedCurrentProfileId,edit.ProfileId,cancel),NodeDesign(current,edit.FeatureId,edit.ExpectedCurrentProfileId,edit.ProfileId,cancel),cancel),
                "MODEL_DEPENDENCY_CHANGED","Rebind changed a retained feature, parameter, driver ID or non-profile dependency.");
        }
        JsonElement PeerProfiles(Snapshot value)
        {
            var preserved=value.Profiles.EnumerateArray().Select(profile=>ProfileDesign(profile,false,cancel)).ToArray();
            var stationary=value.Profiles.EnumerateArray().Select(profile=>profile.GetProperty("parent_sketch_id").GetString()!).Where(id=>!before.Affected.Contains(id)).ToHashSet(StringComparer.OrdinalIgnoreCase);
            var root=JsonNode.Parse(ModelDependencyGraph410.ProtectPartDerived(Wire.Element(new{nodes=Array.Empty<object>(),sketches=Array.Empty<object>(),profiles=preserved}),before.Affected,stationary,cancel).GetRawText())!.AsObject();
            foreach(var row in root["profiles"]!.AsArray())
                if(before.Affected.Contains(row!["profile_id"]!.GetValue<string>())&&!stationary.Contains(row["parent_sketch_id"]!.GetValue<string>())&&row["native_plane"] is JsonObject plane)plane["offset"]=null;
            return Wire.Element(root["profiles"]!);
        }
        var afterProfiles=PeerProfiles(after).EnumerateArray().ToDictionary(value=>value.GetProperty("profile_id").GetString()!,StringComparer.OrdinalIgnoreCase);
        foreach(var profile in PeerProfiles(before).EnumerateArray())
        {
            cancel();string id=profile.GetProperty("profile_id").GetString()!;
            if(id.Equals(edit.ExpectedCurrentProfileId,StringComparison.OrdinalIgnoreCase)||id.Equals(edit.ProfileId,StringComparison.OrdinalIgnoreCase))continue;
            Need(afterProfiles.TryGetValue(id,out var current)&&Equivalent(profile,current,cancel),
                "MODEL_DEPENDENCY_CHANGED","Rebind changed another profile's design or forming owner.");
        }
        Need(ModelProfileRebindConsumers414.VariablesRetained(before.Parameters.GetProperty("variables"),after.Parameters.GetProperty("variables"),before.Old,before.Next,before.Affected,after.Affected,cancel)&&
            before.Drivers.SequenceEqual(after.Drivers,StringComparer.OrdinalIgnoreCase),
            "MODEL_DRIVER_CHANGED","Rebind changed native variable IDs, values, formulas, owners or consumers.");
        Need(before.DirectDependents.All(id=>after.DirectDependents.Contains(id,StringComparer.OrdinalIgnoreCase))&&before.AllDependents.All(id=>after.AllDependents.Contains(id,StringComparer.OrdinalIgnoreCase)),
            "MODEL_DEPENDENCY_CHANGED","A retained native downstream consumer lost its extrusion relationship.");
        ModelViewFollowers.Verify(after.Body,before.Views,cancel,refreshStandalone:true);
        return warnings.ToArray();
    }
}
