using System.Diagnostics.CodeAnalysis;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using Multicad;
using Multicad.DatabaseServices;
using Multicad.Mc3D;
using NanoCAD.Contracts;
using Db=Teigha.DatabaseServices;

namespace NanoCADBridge;

// Terminal feature deletion. Profile consumers opt into exact
// selected-link release with retained construction, not an inferred cascade.
internal static class ModelFeatureDeletionState378
{
    internal const string CleanupScope="Actual history/driver/storage scope observed at native commit. Native auxiliary cleanup can be deferred; refresh current history before another dependent edit. Undo and rollback are not guaranteed.";
    internal sealed record Snapshot(ModelFeatureReorderState345.Snapshot Part,McObjectId Feature,string Kind,
        McObjectId RemainingLast,string[] Drivers,JsonElement Dependencies,string Token,ModelSketchBodyStorage364.Proof Storage,ModelFeatureProfileRetention384.Binding? RetainedProfile);
    internal static void Need([DoesNotReturnIf(false)] bool ok,string code,string message)
    {if(!ok)throw new DomainException(code,message);}
    internal static string Key(McObjectId id)=>id.ToString();
    static string? Kind(McObjectId id)=>id.QueryObjectType()==typeof(FilletFeature)&&id.GetObjectOfType<FilletFeature>()!=null?"fillet":
        id.QueryObjectType()==typeof(ChamferFeature)&&id.GetObjectOfType<ChamferFeature>()!=null?"chamfer":
        id.QueryObjectType()==typeof(ThicknessFeature)&&id.GetObjectOfType<ThicknessFeature>()!=null?"shell":
        id.QueryObjectType()==typeof(ExtrudeFeature)&&id.GetObjectOfType<ExtrudeFeature>()!=null?"extrusion":
        id.QueryObjectType()==typeof(RevolveFeature)&&id.GetObjectOfType<RevolveFeature>()!=null?"revolution":
        id.QueryObjectType()==typeof(MirrorFeature)&&id.GetObjectOfType<MirrorFeature>()!=null?"mirror":null;
    internal static Snapshot Capture(string handle,string featureId,ModifyArguments args,DocumentState state,Db.Transaction tx,Action cancel)
    {
        var loaded=AppDomain.CurrentDomain.GetAssemblies().Select(value=>value.GetName().Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        Need(loaded.Contains("mapimgd")&&loaded.Contains("MapiBaseTypes"),"CAPABILITY_UNAVAILABLE","MultiCAD runtime must already be loaded.");
        Need(args.TargetSpace=="model","OUT_OF_SCOPE","Native feature deletion requires Model Space.");
        var drawing=ModelFeatureReorderState345.Scope(state,tx);var raw=CadStructureWriter.Find(state.Document.Database,handle);
        var solid=tx.GetObject(raw,Db.OpenMode.ForRead) as Db.Solid3d;
        Need(solid!=null&&solid.GetType()==typeof(Db.Solid3d),"UNSUPPORTED_ENTITY_TYPE","Select one native part representation.");
        var layer=(Db.LayerTableRecord)tx.GetObject(solid.LayerId,Db.OpenMode.ForRead);
        Need(!layer.IsLocked&&!layer.IsDependent&&!layer.IsFrozen&&!layer.IsOff,"LAYER_UNAVAILABLE","The selected part layer is unavailable.");
        var bodyId=McObjectId.FromHandle(raw.Handle.Value,drawing.ID);var body=bodyId.GetObjectOfType<Mc3dSolid>();
        Need(body!=null&&body.ID==bodyId,"OBJECT_IDENTITY_MISMATCH","Select the exact current native part.");
        var contents=ModelInventoryBudget.History(body,cancel).ToArray();
        var feature=contents.SingleOrDefault(id=>Key(id).Equals(featureId,StringComparison.OrdinalIgnoreCase));
        Need(!feature.IsNull,"OUT_OF_SCOPE","The selected forming feature is absent from this current part history.");
        var native=feature.GetObjectOfType<HistoryItem>();string? kind=Kind(feature);
        Need(!feature.IsNull&&native!=null&&native.ID==feature&&kind!=null,"OUT_OF_SCOPE","Select a terminal Fillet, Chamfer, Shell, extrusion, revolution or feature Mirror; profile-consuming deletion requires exact retained-profile release scope.");
        var pending=kind=="mirror"&&native.Status.ToString()=="NeedUpdate"?feature:McObjectId.Null;
        var before=ModelFeatureReorderState345.Capture(body,raw,args,state,tx,cancel,captureFullDesign:true,pendingTerminalMirror:pending,captureResidentIdentity:true);
        Need(before.Contents.Contains(feature)&&ModelHistoryEditGuard.CurrentRepresentation(body,state,tx,cancel)==raw,"OBJECT_IDENTITY_MISMATCH","Select the unique current representation and exact current history feature.");
        Need(before.Features.Length>1&&Key(feature).Equals(before.Features[^1],StringComparison.OrdinalIgnoreCase)&&
            body.LastPartItemID==feature&&before.Views.LastModelItem==feature&&!before.Views.HasFollowers,
            "OUT_OF_SCOPE","Keep the base and all other features fixed; select the actual terminal feature of a part without drawing-view followers.");
        var remaining=before.Contents.Single(id=>Key(id).Equals(before.Features[^2],StringComparison.OrdinalIgnoreCase));
        Need(native.OrderObjBefore==remaining,"OUT_OF_SCOPE","The selected terminal requires its actual immediately preceding retained forming feature; nonforming marker chains need a separate adapter.");
        var owner=ModelProfileDiscovery348.OwnerDocument(body,state);
        Need(native.HostSolidID==body.ID&&native.DbEntity.Document!=null&&native.DbEntity.Document.ID==owner.ID,
            "DEPENDENCY_OWNERSHIP_MISMATCH","The selected typed feature is not in this current body's actual owner document.");
        var retainedProfile=kind is "extrusion" or "revolution"?ModelFeatureProfileRetention384.Capture(before,feature,owner,state,tx,cancel):null;
        var drivers=Drivers(feature,before.Preserved,cancel);
        var observations=Dependencies(feature,before,owner,drivers,retainedProfile,cancel);
        var storage=Storage(native,body,owner,drawing,raw,state,tx,cancel);
        var tokenState=new{body=Key(body.ID),feature=Key(feature),kind,remaining_last=Key(remaining),before.NativeDocument,
            before.Preserved,before.Properties,before.BodyMetadata,features=before.Features,
            previous=before.Previous.OrderBy(pair=>pair.Key,StringComparer.OrdinalIgnoreCase).ToArray(),
            constraints=before.Constraints.OrderBy(pair=>pair.Key,StringComparer.OrdinalIgnoreCase).Select(pair=>new{id=pair.Key,observed=pair.Value}).ToArray(),
            inventory=before.Inventory.Select(id=>id.Handle.ToString()).OrderBy(id=>id,StringComparer.OrdinalIgnoreCase).ToArray(),
            dependencies=observations,drivers,storage,retained_profile=retainedProfile==null?null:new{retainedProfile.Profile.Stored,retainedProfile.Dependents,retainedProfile.Autoerase}};
        string token=Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(tokenState,Wire.Json)));
        return new(before,feature,kind,remaining,drivers,observations,token,storage,retainedProfile);
    }
    static JsonElement Dependencies(McObjectId feature,ModelFeatureReorderState345.Snapshot before,McDocument owner,string[] drivers,ModelFeatureProfileRetention384.Binding? retainedProfile,Action cancel)
    {
        var known=before.Contents.ToHashSet();known.Add(before.Body.ID);
        var selected=feature.GetObjectOfType<HistoryItem>();Need(selected!=null,"HISTORY_UNAVAILABLE","The selected feature disappeared.");
        var direct=selected.GetDependent();var all=selected.GetAllDependent(false);var critical=selected.GetCriticalDependsOn();var criticalAll=selected.GetCriticalAllDependsOn();
        Need(direct!=null&&all!=null&&critical!=null&&criticalAll!=null,"DEPENDENCY_INVENTORY_UNAVAILABLE","Selected native direct/recursive/critical dependency inventories are unavailable; no empty-cascade claim is made.");
        foreach(var ids in new[]{direct,all,critical,criticalAll})ModelInventoryBudget.Ids(ids,cancel);
        foreach(var id in direct.Concat(all))
        {
            cancel();Need(!id.IsNull&&known.Contains(id),"DEPENDENCY_OWNERSHIP_MISMATCH","A selected feature has a foreign or unclassified dependent.");
            if(id==before.Body.ID)continue;
            var item=id.GetObjectOfType<HistoryItem>();
            Need(item!=null&&item.ID==id&&item.HostSolidID==before.Body.ID&&item.DbEntity.Document!=null&&item.DbEntity.Document.ID==owner.ID,
                "DEPENDENCY_OWNERSHIP_MISMATCH","A selected dependent is not an actual same-part native history item.");
            Need(id==feature||!ModelFeatureReorderState345.Forming(id),"OUT_OF_SCOPE","Another forming feature depends on this selected feature; explicit cascade deletion is outside this terminal adapter.");
        }
        var observed=Critical(before,owner,cancel);
        foreach(var row in observed.EnumerateArray())
        {
            cancel();if(row.GetProperty("id").GetString()==Key(feature))continue;
            foreach(string field in new[]{"critical","critical_all"})
                Need(!row.GetProperty(field).EnumerateArray().Any(value=>drivers.Contains(value.GetString()!,StringComparer.OrdinalIgnoreCase)||
                        value.GetString()==Key(feature)&&(retainedProfile==null||row.GetProperty("id").GetString()!=Key(retainedProfile.Profile.Profile.ID))),
                    "OUT_OF_SCOPE","Deleting this feature or retiring a selected driver would critically cascade into a retained native item or body.");
            Need(!row.GetProperty("subentity_bindings").EnumerateArray().Any(value=>
                    value.GetProperty("creating_feature_id").GetString()==Key(feature)),
                "OUT_OF_SCOPE","A retained native item critically requires a subentity created by the selected feature; this terminal deletion cannot remove that source.");
            foreach(var construction in row.GetProperty("document_construction_bindings").EnumerateArray())
                foreach(string field in new[]{"critical","critical_all"})
                    Need(!construction.GetProperty(field).EnumerateArray().Any(value=>value.GetString()==Key(feature)||
                        drivers.Contains(value.GetString()!,StringComparer.OrdinalIgnoreCase)),
                        "OUT_OF_SCOPE","A retained document construction critically requires the selected feature or driver; this terminal deletion cannot remove that source.");
        }
        var mirror=feature.GetObjectOfType<MirrorFeature>();
        var result=Wire.Element(new{selected=Key(feature),direct=direct.Select(Key).OrderBy(value=>value,StringComparer.OrdinalIgnoreCase).ToArray(),all=all.Select(Key).OrderBy(value=>value,StringComparer.OrdinalIgnoreCase).ToArray(),retained_critical_inputs=observed,
            mirror_sources=mirror==null?(JsonElement?)null:MirrorSources(mirror,before,owner,cancel)});
        new ModelInventoryBudget(cancel).Add(result);return result;
    }
    static JsonElement MirrorSources(MirrorFeature mirror,ModelFeatureReorderState345.Snapshot part,McDocument owner,Action cancel)
    {
        Need(mirror.HasSubFeatureItems&&!mirror.HasWholeSolidsItems&&!mirror.HasBlockReferenceItems&&mirror.SourceItemsCount>0,
            "OUT_OF_SCOPE","Terminal Mirror deletion supports retained same-part feature sources; whole solids and block sources require their own preservation scope.");
        var rows=new List<object>();var bytes=new ModelInventoryBudget(cancel);
        for(int index=0;index<mirror.SourceItemsCount;index++)
        {
            cancel();var source=mirror.GetSourceItem(index);var feature=mirror.GetSourceItemFeature(index);
            // The SDK exposes source object and program-feature identifiers
            // separately. A program-feature ID is not a history-object alias.
            var item=source.IsNull?null:source.GetObjectOfType<HistoryItem>();
            Need(!source.IsNull&&!feature.IsNull&&source!=mirror.ID&&item!=null&&item.ID==source&&
                (source==part.Body.ID||part.Contents.Contains(source)&&item.HostSolidID==part.Body.ID)&&item.DbEntity.Document?.ID==owner.ID&&
                (source==part.Body.ID||item.Status.ToString()=="Ok"||ProjectedHistorySources.HealthySuppressed(item))&&!item.IsBeyondEndOfPart,
                "DEPENDENCY_OWNERSHIP_MISMATCH","A Mirror source object must be an exact retained part/body object in its actual owner document; native source-feature IDs are not substituted for it.");
            var row=new{index,object_id=Key(source),native_feature_id=Key(feature)};bytes.Add(row);rows.Add(row);
        }
        var dependencies=mirror.GetDependsOn(false,false);
        Need(dependencies!=null,"DEPENDENCY_INVENTORY_UNAVAILABLE","The selected Mirror's actual input dependencies are unavailable.");ModelInventoryBudget.Ids(dependencies,cancel);
        var originals=dependencies.Where(id=>part.Contents.Contains(id)&&id!=mirror.ID&&id.GetObjectOfType<FeatureEntity>() is {} item&&item.ID==id&&
            item.HostSolidID==part.Body.ID&&item.DbEntity.Document?.ID==owner.ID).ToArray();
        Need(originals.Length>0,"DEPENDENCY_OWNERSHIP_MISMATCH","The selected feature Mirror needs actual retained forming-source dependencies in this part.");
        return Wire.Element(new{source_items=rows,retained_forming_dependencies=originals.Select(Key).OrderBy(value=>value,StringComparer.OrdinalIgnoreCase).ToArray()});
    }
    internal static JsonElement Critical(ModelFeatureReorderState345.Snapshot part,McDocument owner,Action cancel)
    {
        var known=part.Contents.ToHashSet();known.Add(part.Body.ID);var observed=new List<object>();var budget=new ModelInventoryBudget(cancel);
        var allowed=known.Select(Key).Concat(part.Preserved.GetProperty("native_source_ids").EnumerateArray().Select(value=>value.GetString()!))
            .Concat(part.Preserved.GetProperty("variables").EnumerateArray().Select(value=>value.GetProperty("mc_id").GetString()!)).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var bindingsByInput=new Dictionary<McObjectId,JsonElement>();HashSet<McObjectId>? existingGcs=null;
        bool ExistingGcs(McObjectId input)
        {
            if(existingGcs==null)
            {
                cancel();var history=owner.Get3dHistory();
                Need(history!=null&&history.DocumentID==owner.ID&&history.Document?.ID==owner.ID,
                    "DEPENDENCY_OWNERSHIP_MISMATCH","The existing GCS inventory must belong to this part's exact actual owner document.");
                var ids=history.GetExistingGCSElementsIDs();
                Need(ids!=null,"DEPENDENCY_INVENTORY_UNAVAILABLE","The existing document GCS inventory is unavailable; no coordinate objects are created to complete it.");
                ModelInventoryBudget.Ids(ids,cancel);existingGcs=ids.ToHashSet();
            }
            return existingGcs.Contains(input);
        }
        foreach(var id in known.OrderBy(Key,StringComparer.OrdinalIgnoreCase))
        {
            cancel();var item=id.GetObjectOfType<HistoryItem>();
            if(item==null){Need(id!=part.Body.ID,"HISTORY_UNAVAILABLE","The current body critical dependency inventory is unavailable.");continue;}
            Need(item.ID==id&&(id==part.Body.ID||item.HostSolidID==part.Body.ID)&&item.DbEntity.Document!=null&&item.DbEntity.Document.ID==owner.ID,
                "DEPENDENCY_OWNERSHIP_MISMATCH","A retained critical-input owner is not an exact current same-part history item.");
            var inputs=item.GetCriticalDependsOn();var inputsAll=item.GetCriticalAllDependsOn();
            Need(inputs!=null&&inputsAll!=null,"DEPENDENCY_INVENTORY_UNAVAILABLE","A retained critical-input list is null/unavailable; this is not a claim of an empty native cascade.");
            ModelInventoryBudget.Ids(inputs,cancel);ModelInventoryBudget.Ids(inputsAll,cancel);
            var bindings=new Dictionary<McObjectId,JsonElement>();
            foreach(var (field,values) in new[]{("critical",inputs),("critical_all",inputsAll)})foreach(var input in values)
            {
                cancel();if(!input.IsNull&&allowed.Contains(Key(input)))continue;
                if(!bindingsByInput.TryGetValue(input,out var binding))
                {binding=CriticalInput(input,id,field,part,owner,ExistingGcs,cancel);bindingsByInput.Add(input,binding);}
                bindings[input]=binding;
            }
            var row=new{id=Key(id),critical=inputs.Select(Key).OrderBy(value=>value,StringComparer.OrdinalIgnoreCase).ToArray(),critical_all=inputsAll.Select(Key).OrderBy(value=>value,StringComparer.OrdinalIgnoreCase).ToArray(),
                subentity_bindings=bindings.OrderBy(pair=>Key(pair.Key),StringComparer.OrdinalIgnoreCase).Select(pair=>pair.Value).Where(value=>value.TryGetProperty("creating_feature_id",out _)).ToArray(),
                document_construction_bindings=bindings.OrderBy(pair=>Key(pair.Key),StringComparer.OrdinalIgnoreCase).Select(pair=>pair.Value).Where(value=>!value.TryGetProperty("creating_feature_id",out _)).ToArray()};budget.Add(row);observed.Add(row);
        }
        var result=Wire.Element(observed);new ModelInventoryBudget(cancel).Add(result);return result;
    }
    static JsonElement CriticalInput(McObjectId input,McObjectId consumer,string field,ModelFeatureReorderState345.Snapshot part,McDocument owner,Func<McObjectId,bool> existingGcs,Action cancel)
    {
        cancel();string? type=null,parent=null,creating=null,hostDocument=null;
        DomainException Rejected(string reason)=>new("DEPENDENCY_OWNERSHIP_MISMATCH",
            $"Unclassified native critical input: consumer={Key(consumer)}; field={field}; input={Key(input)}; type={type??"unavailable"}; parent={parent??"null"}; host_feature_or_parent={creating??"null"}; id_host_document={hostDocument??"null"}; reason={reason}.",
            details:new{consumer_id=Key(consumer),critical_field=field,input_id=Key(input),native_type=type,parent_id=parent,host_feature_or_parent_id=creating,id_host_document=hostDocument,reason});
        try
        {
            if(input.IsNull)throw Rejected("null_input");
            bool fev=input.IsFace||input.IsEdge||input.IsVertex;
            type=fev?input.IsFace?"native_face":input.IsEdge?"native_edge":"native_vertex":input.QueryObjectType()?.FullName;
            var parentId=input.GetParentId(false);parent=parentId.IsNull?null:Key(parentId);
            var creator=input.GetHostFeatureOrParentId(false);creating=creator.IsNull?null:Key(creator);
            var document=input.IdHostDoc;hostDocument=document.IsNull?null:Key(document);
            if(!fev)
            {
                // SDK DocHistory.GetGCSElement can create missing objects.
                // Only the existing-only inventory may establish this binding.
                var plane=input.GetObjectOfType<WorkPlane>();
                if(plane==null||plane.ID!=input||plane.DbEntity.Document?.ID!=owner.ID||
                    !plane.IsGCSObject||!existingGcs(input)||
                    !plane.HostSolidID.IsNull&&plane.HostSolidID!=part.Body.ID||
                    plane.Status.ToString()!="Ok"||plane.IsSuppressed||plane.IsBeyondEndOfPart)
                    throw Rejected("not_an_exact_existing_GCS_WorkPlane_in_the_actual_owner");
                using var position=plane.Position;
                if(position==null)throw Rejected("existing_GCS_plane_position_unavailable");
                static double[] P(Multicad.Geometry.Point3d point)=>[point.X,point.Y,point.Z];
                static double[] V(Multicad.Geometry.Vector3d vector)=>[vector.X,vector.Y,vector.Z];
                var origin=P(position.Origin);var x=V(position.DirU);var y=V(position.DirV);var normal=V(position.Normal);var exposed=V(plane.Normal);
                double Dot(double[] a,double[] b)=>a.Zip(b).Sum(pair=>pair.First*pair.Second);
                var cross=new[]{x[1]*y[2]-x[2]*y[1],x[2]*y[0]-x[0]*y[2],x[0]*y[1]-x[1]*y[0]};
                if(!origin.Concat(x).Concat(y).Concat(normal).Concat(exposed).All(double.IsFinite)||
                    !ModelDatumPlaneState353.Requested(Dot(x,x),1)||!ModelDatumPlaneState353.Requested(Dot(y,y),1)||
                    !ModelDatumPlaneState353.Requested(Dot(normal,normal),1)||!ModelDatumPlaneState353.Requested(Dot(x,y),0)||
                    !cross.Zip(normal).All(pair=>ModelDatumPlaneState353.Requested(pair.First,pair.Second))||
                    !normal.Zip(exposed).All(pair=>ModelDatumPlaneState353.Requested(pair.First,pair.Second)))
                    throw Rejected("existing_GCS_plane_has_no_finite_coherent_oriented_frame");
                var dependencies=plane.GetDependsOn(false,false);var critical=plane.GetCriticalDependsOn();var criticalAll=plane.GetCriticalAllDependsOn();
                if(dependencies==null||critical==null||criticalAll==null)throw Rejected("existing_GCS_plane_dependencies_unavailable");
                foreach(var values in new[]{dependencies,critical,criticalAll})ModelInventoryBudget.Ids(values,cancel);var parameter=plane.ParamVarID;
                if(!parameter.IsNull&&!part.Preserved.GetProperty("variables").EnumerateArray().Any(value=>value.GetProperty("mc_id").GetString()==Key(parameter)&&value.GetProperty("host_object_id").GetString()==Key(input)))
                    throw Rejected("existing_GCS_plane_parameter_lacks_its_actual_owner_network_binding");
                var gcs=Wire.Element(new{input_id=Key(input),kind="existing_document_GCS_plane",owner_document_id=Key(owner.ID),
                    actual_type=plane.GetType().FullName,gcs_item_type=plane.GCSItemType.ToString(),plane.IsGCSObject,plane.IsFixedDef,
                    name=plane.Name,definition_type=plane.DefinitionType.ToString(),parameter_variable_id=parameter.IsNull?null:Key(parameter),
                    host_solid_id=plane.HostSolidID.IsNull?null:Key(plane.HostSolidID),previous=plane.OrderObjBefore.IsNull?null:Key(plane.OrderObjBefore),
                    status=plane.Status.ToString(),plane.IsSuppressed,plane.IsBeyondEndOfPart,properties=new{layer=plane.DbEntity.Layer,visibility=plane.DbEntity.Visibility},
                    frame=new{origin_wcs=origin,x_axis_wcs=x,y_axis_wcs=y,normal_wcs=normal},dependencies=dependencies.Select(Key).OrderBy(value=>value,StringComparer.OrdinalIgnoreCase).ToArray(),
                    critical=critical.Select(Key).OrderBy(value=>value,StringComparer.OrdinalIgnoreCase).ToArray(),critical_all=criticalAll.Select(Key).OrderBy(value=>value,StringComparer.OrdinalIgnoreCase).ToArray()});
                new ModelInventoryBudget(cancel).Add(gcs);return gcs;
            }
            if(parentId!=part.Body.ID||part.Body.DbEntity.Document?.ID!=owner.ID)
                throw Rejected("FEV_parent_is_not_the_exact_selected_part_in_its_actual_owner");
            // hostFeatureOrParent falls back to the parent if creation cannot
            // be resolved. Such a fallback cannot prove selected-feature cascade
            // safety; require an actual retained creating history feature.
            var feature=creator.IsNull?null:creator.GetObjectOfType<HistoryItem>();
            if(creator==part.Body.ID||!part.Contents.Contains(creator)||feature==null||feature.ID!=creator||
                feature.HostSolidID!=part.Body.ID||feature.DbEntity.Document?.ID!=owner.ID||feature.IsBeyondEndOfPart)
                throw Rejected("FEV_creating_feature_is_unresolved_or_outside_the_actual_part");
            using var geometry=Service.GetFEVGeometry(input,FEVGeometryQueryMode.IdMatchExact|FEVGeometryQueryMode.CacheIgnore|FEVGeometryQueryMode.EnvelopeExact|FEVGeometryQueryMode.CacheLock);
            if(geometry==null)throw Rejected("exact_current_FEV_geometry_unavailable");
            if(input.IsEdge&&geometry.GeometryType is Multicad.Geometry.EntityGeomType.kLine or Multicad.Geometry.EntityGeomType.kCircArc)
                ModelSketchNativeReferences410.Geometry(input,part.Body.ID,cancel);
            var result=Wire.Element(new{input_id=Key(input),kind=type,parent_id=Key(parentId),creating_feature_id=Key(creator),owner_document_id=Key(owner.ID),geometry_kind=geometry.GeometryType.ToString()});
            new ModelInventoryBudget(cancel).Add(result);return result;
        }
        catch(OperationCanceledException){throw;}
        catch(DomainException error)when(error.Error.code!="DEPENDENCY_OWNERSHIP_MISMATCH")
        {throw Rejected("native_observation_rejected_"+error.Error.code);}
        catch(DomainException){throw;}
        catch(Exception error)when(error is not OutOfMemoryException){throw Rejected("native_observation_failed_"+error.GetType().Name);}
    }
    static string[] Drivers(McObjectId feature,JsonElement preserved,Action cancel)
    {
        string target=Key(feature);var owned=preserved.GetProperty("variables").EnumerateArray().Where(value=>value.GetProperty("host_object_id").GetString()==target).ToArray();
        var ids=owned.Select(value=>value.GetProperty("mc_id").GetString()!).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach(var variable in owned)
        {
            cancel();var objects=variable.GetProperty("downstream_objects");var variables=variable.GetProperty("downstream_variables");
            Need(objects.ValueKind==JsonValueKind.Array&&variables.ValueKind==JsonValueKind.Array,"VARIABLE_DEPENDENCIES_UNAVAILABLE","A selected native driver needs complete actual downstream identities.");
            Need(objects.EnumerateArray().All(value=>value.GetString()==target)&&variables.EnumerateArray().All(value=>ids.Contains(value.GetString()!)),
                "OUT_OF_SCOPE","A selected feature-owned driver is consumed outside the requested erase scope.");
        }
        return ids.OrderBy(value=>value,StringComparer.OrdinalIgnoreCase).ToArray();
    }
    static ModelSketchBodyStorage364.Proof Storage(HistoryItem feature,Mc3dSolid body,McDocument owner,McDocument drawing,Db.ObjectId representation,DocumentState state,Db.Transaction tx,Action cancel)
    {
        string? handle=null;var path=new List<string>();var budget=new ModelInventoryBudget(cancel);
        ModelSketchBodyStorage364.Proof Result(bool proved,string reason)=>new(Key(feature.ID),Key(owner.ID),handle,path.ToArray(),proved,reason);
        long rawHandle=feature.ID.Handle;
        if(rawHandle==0||!state.Document.Database.TryGetObjectId(new Db.Handle(rawHandle),out var raw)||raw.IsNull||!raw.IsValid||raw.IsErased||raw.IsEffectivelyErased)
            return Result(false,"selected_typed_feature_has_no_current_resident_storage_handle; no_metadata_retirement_allowance");
        handle=Validation.NormalizeHandle(raw.Handle.ToString());
        if(McObjectId.FromHandle(raw.Handle.Value,owner.ID)!=feature.ID&&McObjectId.FromHandle(raw.Handle.Value,drawing.ID)!=feature.ID)
            return Result(false,"resident_storage_does_not_roundtrip_to_exact_selected_feature_ID");
        var seen=new HashSet<Db.ObjectId>();var child=tx.GetObject(raw,Db.OpenMode.ForRead);
        while(child.ObjectId!=representation)
        {
            cancel();var childDatabase=child.Database;
            if(childDatabase==null||childDatabase.UnmanagedObject==IntPtr.Zero||childDatabase.UnmanagedObject!=state.Document.Database.UnmanagedObject||
                !seen.Add(child.ObjectId)||child.OwnerId.IsNull||!child.OwnerId.IsValid||child.OwnerId.IsErased||child.OwnerId.IsEffectivelyErased)
                return Result(false,"selected_storage_has_no_exact_current_body_owner_path");
            var parent=tx.GetObject(child.OwnerId,Db.OpenMode.ForRead);string key=Validation.NormalizeHandle(parent.Handle.ToString());budget.Add(key);path.Add(key);
            bool reverse=parent.ExtensionDictionary==child.ObjectId;
            if(parent.GetType()==typeof(Db.DBDictionary)&&parent is Db.DBDictionary dictionary)
            {int count=0;foreach(Db.DBDictionaryEntry entry in dictionary){cancel();if(entry.Value==child.ObjectId)count++;}reverse=count==1;}
            if(!reverse)return Result(false,"selected_storage_owner_lacks_exact_reverse_membership");child=parent;
        }
        if(path.Count<2||feature.HostSolidID!=body.ID)return Result(false,"selected_storage_is_not_exact_body_owned_extension_storage");
        return Result(true,"current_typed_selected_feature_resident_storage_roundtrip_and_actual_body_owner_path");
    }
    internal static JsonElement ExpectedDesign(Snapshot before,string[] retired,Action cancel)
    {
        string feature=Key(before.Feature);var retiredIds=retired.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var value=JsonNode.Parse(before.Part.Preserved.GetRawText())!.AsObject();value["last"]=Key(before.RemainingLast);
        void Remove(string field,string key,Func<string,bool> gone)
        {var rows=value[field]!.AsArray();for(int i=rows.Count-1;i>=0;i--){cancel();if(gone(rows[i]![key]!.GetValue<string>()))rows.RemoveAt(i);}}
        Remove("nodes","mc_id",id=>id.Equals(feature,StringComparison.OrdinalIgnoreCase));Remove("construction","id",id=>id.Equals(feature,StringComparison.OrdinalIgnoreCase));
        Remove("variables","mc_id",retiredIds.Contains);
        foreach(var variable in value["variables"]!.AsArray())
        {
            cancel();foreach(string name in new[]{"downstream_objects","downstream_variables"})
            if(variable![name] is JsonArray downstream)
                for(int i=downstream.Count-1;i>=0;i--){string id=downstream[i]!.GetValue<string>();if(id.Equals(feature,StringComparison.OrdinalIgnoreCase)||retiredIds.Contains(id))downstream.RemoveAt(i);}
        }
        return ModelFeatureProfileRetention384.Expected(Wire.Element(value),before.RetainedProfile,before.Feature,cancel);
    }
}
