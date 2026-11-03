using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Nodes;
using Multicad;
using Multicad.DatabaseServices;
using Multicad.Mc3D;
using NanoCAD.Contracts;
using NanoCAD.Ipc;
using Db=Teigha.DatabaseServices;

namespace NanoCADBridge;

// Explicit existing-body target: retained construction has its actual document/host
// scope, then its exact ID becomes one native forming-feature input.
internal static class ModelProfileLifecycleWriter340
{
    static void Need([DoesNotReturnIf(false)] bool ok,string code,string message){if(!ok)throw new DomainException(code,message);}
    static string Key(McObjectId id)=>id.ToString();
    sealed record SourceState(DrawingProfileWriter.Source Source,JsonElement Properties,JsonElement Owned,JsonElement XData);
    sealed record ProfileParent(string Feature,string Kind,string Profile,string Sketch);
    sealed record PairedOrder(string Current,string? Previous);
    sealed record Binding(EditSpec Edit,Mc3dSolid Body,Db.ObjectId Representation,string NativeDocument,
        string Layer,JsonElement Properties,JsonElement Before,McObjectId[] Contents,HashSet<Db.ObjectId> Inventory,
        ModelViewFollowers.Snapshot Views,SourceState[] Sources,ModelExtrusionSpec? Definition,
        ModelProfileLifecycleState340.Snapshot? Profile,McObjectId[] DocumentConstruction,string ModelDocument,ProfileParent[] ProfileParents,ModelProfileConsumers410.Snapshot? Consumers=null);
    internal static bool Handles(ModifyArguments args)=>args.Operations.Any(e=>e is ModelProfileCreate340 or ModelProfileExtrude340);
    static McDocument Scope(DocumentState state,Db.Transaction tx,string? expected=null)
    {
        var active=McDocument.ActiveDocument;
        Need(active!=null&&state.Document.IsNamedDrawing&&!string.IsNullOrWhiteSpace(active.Path)&&
            string.Equals(Path.GetFullPath(active.Path),Path.GetFullPath(state.Document.Name),StringComparison.OrdinalIgnoreCase),
            "DOCUMENT_CONTEXT_MISMATCH","Use the matching named native model document.");
        Need(expected==null||active.ID.ToString()==expected,"DOCUMENT_CONTEXT_MISMATCH","Native document identity changed.");
        var model=((Db.BlockTable)tx.GetObject(state.Document.Database.BlockTableId,Db.OpenMode.ForRead))[Db.BlockTableRecord.ModelSpace];
        Need(state.Document.Database.CurrentSpaceId==model,"OUT_OF_SCOPE","Activate Model Space before editing native profile history.");
        return active;
    }
    static HashSet<Db.ObjectId> Inventory(DocumentState state,Db.Transaction tx,Action cancel)
    {
        var model=((Db.BlockTable)tx.GetObject(state.Document.Database.BlockTableId,Db.OpenMode.ForRead))[Db.BlockTableRecord.ModelSpace];
        var result=new HashSet<Db.ObjectId>();var bytes=new ModelInventoryBudget(cancel);
        foreach(Db.ObjectId id in (Db.BlockTableRecord)tx.GetObject(model,Db.OpenMode.ForRead))
        {cancel();if(id.IsErased||id.IsEffectivelyErased)continue;bytes.Add(id.Handle.ToString());result.Add(id);}
        return result;
    }
    static JsonElement XData(Db.DBObject value,Action cancel)
    {
        using var data=value.XData;var budget=new ModelInventoryBudget(cancel);
        return JsonSerializer.SerializeToElement(data==null?null:NativeOwnedMetadata.Buffer(data,cancel,item=>budget.Add(item)),Wire.Json);
    }
    static SourceState Source(DrawingProfileWriter.Source source,DocumentState state,Db.Transaction tx,Action cancel)
    {
        var entity=(Db.Entity)tx.GetObject(source.Id,Db.OpenMode.ForRead);
        return new(source,Wire.Element(EntityAdapter.StoredProperties(entity)),NativeOwnedMetadata.Capture(entity,tx,cancel),XData(entity,cancel));
    }
    static ModelProfileSpec Contour(DrawingProfileWriter.Source[] sources,Action cancel)
    {
        var curves=sources.Select(s=>{cancel();return s.Curve;}).ToArray();
        if(curves.Length==1&&curves[0].Kind=="circle")return new("circle",curves[0].Center,curves[0].Radius,null);
        Need(curves.All(c=>c.Kind is "line" or "arc"),"INVALID_GEOMETRY","Select one Circle or one complete closed Line/Arc contour.");
        return curves.Any(c=>c.Kind=="arc")?ModelPolycurve.FromCurves(curves,cancel):DrawingProfileGeometry.Profile(curves,cancel);
    }
    internal static void Preflight(ModifyArguments args,DocumentState state,Db.Transaction tx,Action? cancel=null)
    {
        var loaded=AppDomain.CurrentDomain.GetAssemblies().Select(a=>a.GetName().Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        Need(loaded.Contains("mapimgd")&&loaded.Contains("MapiBaseTypes"),"CAPABILITY_UNAVAILABLE","MultiCAD must already be loaded.");
        Resolve(args,state,tx,cancel??(()=>{}));
    }
    [MethodImpl(MethodImplOptions.NoInlining)]
    static Binding Resolve(ModifyArguments args,DocumentState state,Db.Transaction tx,Action cancel)
    {
        Need(args.Operations.Length==1&&Handles(args),"INVALID_OPERATION_ORDER","A profile lifecycle operation must be the sole operation.");
        Need(args.Units=="Millimeters"&&args.TargetSpace=="model","OUT_OF_SCOPE","Native profile lifecycle uses drawing millimeters in Model Space.");
        var edit=args.Operations[0];var active=Scope(state,tx);var db=state.Document.Database;
        var id=CadStructureWriter.Find(db,edit.Handles.Single());var solid=tx.GetObject(id,Db.OpenMode.ForRead) as Db.Solid3d;
        var model=((Db.BlockTable)tx.GetObject(db.BlockTableId,Db.OpenMode.ForRead))[Db.BlockTableRecord.ModelSpace];
        Need(solid!=null&&solid.GetType()==typeof(Db.Solid3d)&&solid.OwnerId==model,"UNSUPPORTED_ENTITY_TYPE","Select one existing top-level native model body.");
        var layer=(Db.LayerTableRecord)tx.GetObject(solid.LayerId,Db.OpenMode.ForRead);
        Need(!layer.IsLocked&&!layer.IsOff&&!layer.IsFrozen&&!layer.IsDependent,"LAYER_UNAVAILABLE","Body layer is unavailable for editing.");
        var native=McObjectId.FromHandle(id.Handle.Value,active.ID);var body=native.GetObjectOfType<Mc3dSolid>();
        Need(body!=null&&body.ID==native,"OBJECT_IDENTITY_MISMATCH","The handle does not identify the current native body.");
        Need(ModelHistoryEditGuard.CurrentRepresentation(body,state,tx,cancel)==id,"OBJECT_IDENTITY_MISMATCH","Select the unique current native body representation.");
        var modelDocument=ModelProfileDiscovery348.OwnerDocument(body,state);
        string expected=edit is ModelProfileCreate340 create?create.ExpectedLastItemId:((ModelProfileExtrude340)edit).ExpectedLastItemId;
        Need(Key(body.LastPartItemID).Equals(expected,StringComparison.OrdinalIgnoreCase),"PRECONDITION_FAILED","Refresh this body's last_item_id before changing its profile history.");
        var contents=ModelInventoryBudget.History(body,cancel).ToArray();Need(contents.Length>0,"HISTORY_UNAVAILABLE","An existing healthy native part history is required.");
        var views=ModelViewFollowers.Resolve(body,contents,cancel,includeStandalone:true);
        if(edit is ModelProfileExtrude340)ModelViewFollowers.PrepareInsertion(views,true);
        var before=ModelParameterSnapshot.Capture(new(args.BridgeId,args.DocumentId,args.ExpectedRevision,args.OperationId,args.Units,edit.Handles[0],"","",0),state,cancel);
        ModelParameterSnapshot.Healthy(before);var sources=new List<SourceState>();var bytes=new ModelInventoryBudget(cancel);
        ModelExtrusionSpec? definition;ModelProfileLifecycleState340.Snapshot? retained=null;ModelProfileConsumers410.Snapshot? consumers=null;
        if(edit is ModelProfileCreate340 request)
        {
            var projected=ProjectedHistorySources.Read(body,contents,state,tx,cancel);
            foreach(string handle in request.SourceHandles)
            {
                cancel();var source=DrawingProfileWriter.SourceOf(CadStructureWriter.Find(db,handle),state,tx,true,request.Plane);
                Need(projected.All(old=>!old.id.Equals(Key(source.Native),StringComparison.OrdinalIgnoreCase))&&
                    !NativePathSources.ReferencedIds(contents,cancel).Contains(Key(source.Native)),"OUT_OF_SCOPE",
                    "Creation requires new retained source projections; an already bound source needs its existing profile route.");
                var snapshot=Source(source,state,tx,cancel);bytes.Add(snapshot.Properties);bytes.Add(snapshot.Owned);bytes.Add(snapshot.XData);sources.Add(snapshot);
            }
            definition=new(request.Plane,Contour(sources.Select(s=>s.Source).ToArray(),cancel),1,"positive",request.Name,solid.Layer);
        }
        else
        {
            var extrusionRequest=(ModelProfileExtrude340)edit;
            var profile=ModelProfileDiscovery348.Resolve(extrusionRequest.ProfileId,modelDocument,cancel);
            consumers=ModelProfileConsumers410.Capture(profile,modelDocument,cancel);ModelProfileConsumers410.RequireBody(consumers,body);
            retained=ModelProfileLifecycleState340.Capture(profile,body,state,tx,cancel,allowCircularHoles:true);
            Need(Key(retained.Sketch.ID).Equals(extrusionRequest.ExpectedSketchId,StringComparison.OrdinalIgnoreCase)&&retained.Token==extrusionRequest.ExpectedProfileToken,
                "PRECONDITION_FAILED","The selected profile, parent sketch, contour or source bindings changed; refresh its history token.");
            foreach(var external in retained.Sources)
            {
                cancel();var source=DrawingProfileWriter.SourceOf(CadStructureWriter.Find(db,external.handle),state,tx,false,retained.Plane);
                var snapshot=Source(source,state,tx,cancel);bytes.Add(snapshot.Properties);bytes.Add(snapshot.Owned);bytes.Add(snapshot.XData);sources.Add(snapshot);
            }
            // Native AddExtrudeFeature consumes the exact selected profile ID,
            // including its observed circular inner loops. Only single-contour
            // construction needs a coordinate definition; never substitute the
            // outer circle for the complete native selected area.
            definition=retained.Contour is {} contour
                ?new(retained.Plane,contour,extrusionRequest.Distance,extrusionRequest.Direction,extrusionRequest.Name,solid.Layer):null;
        }
        if(definition!=null)ModelExtrusionGeometry.Measure(definition,cancel);ModelFeatureOracle.Read(solid);
        var inventory=Inventory(state,tx,cancel);var properties=Wire.Element(EntityAdapter.StoredProperties(solid));
        bytes.Add(before);bytes.Add(properties);bytes.Add(views.Metadata);if(retained!=null)bytes.Add(retained.Stored);
        return new(edit,body,id,active.ID.ToString(),solid.Layer,properties,before,contents,inventory,views,sources.ToArray(),definition,retained,
            ModelProfileDiscovery348.ConstructionIds(modelDocument,cancel),modelDocument.ID.ToString(),
            edit is ModelProfileExtrude340?RetainedProfileParents(body,modelDocument,contents,before,cancel):[],consumers);
    }
    static (HistoryItem? Feature,McObjectId Profile,string? Kind) ProfileParentFeature(McObjectId id)
    {
        var extrusion=id.GetObjectOfType<ExtrudeFeature>();
        if(extrusion!=null)return(extrusion,extrusion.ProfileID,"extrusion");
        var sweep=id.GetObjectOfType<ExtrudeAlongPathFeature>();
        return sweep==null?(null,McObjectId.Null,null):(sweep,sweep.ProfileID,"sweep");
    }
    static ProfileParent[] RetainedProfileParents(Mc3dSolid body,McDocument owner,McObjectId[] contents,JsonElement before,Action cancel)
    {
        var nodes=before.GetProperty("nodes").EnumerateArray().ToDictionary(node=>node.GetProperty("mc_id").GetString()!,StringComparer.OrdinalIgnoreCase);
        var result=new List<ProfileParent>();
        foreach(var id in contents)
        {
            cancel();var (feature,profileId,kind)=ProfileParentFeature(id);if(feature==null)continue;
            if(!nodes.TryGetValue(Key(id),out var saved)||saved.GetProperty("parameters").ValueKind!=JsonValueKind.Object||
                saved.GetProperty("parameters").GetProperty("kind").GetString()!=kind||saved.GetProperty("parameters").GetProperty("profile_id").GetString()!=Key(profileId))continue;
            var profile=profileId.GetObjectOfType<SketchProfile>();if(profile==null||profile.ID!=profileId)continue;
            var sketch=profile.ParentSketchID.GetObjectOfType<PlanarSketch>();if(sketch==null||sketch.ID!=profile.ParentSketchID)continue;
            var sd=feature.DbEntity.Document;var pd=profile.DbEntity.Document;var kd=sketch.DbEntity.Document;
            if(feature.ID!=id||feature.HostSolidID!=body.ID||profile.HostSolidID!=body.ID||sketch.HostSolidID!=body.ID||
                sd==null||pd==null||kd==null||sd.ID!=owner.ID||pd.ID!=owner.ID||kd.ID!=owner.ID||
                !nodes.ContainsKey(Key(profile.ID))||!nodes.ContainsKey(Key(sketch.ID)))continue;
            result.Add(new(Key(id),kind!,Key(profile.ID),Key(sketch.ID)));
        }
        return result.ToArray();
    }
    static bool OrderMirrors(JsonElement node,string? expected)
    {
        if(node.GetProperty("native_order_before_id").GetString()!=expected)return false;
        return node.GetProperty("parameters").ValueKind!=JsonValueKind.Object||
            !node.GetProperty("parameters").TryGetProperty("native_order_before_id",out var mirror)||mirror.GetString()==expected;
    }
    static Dictionary<string,PairedOrder> VerifiedParentSketchOrders(Binding binding,Dictionary<string,JsonElement> current,McObjectId? consumed,Action cancel)
    {
        var orders=new Dictionary<string,PairedOrder>(StringComparer.OrdinalIgnoreCase);
        // AddExtrudeFeature/SetOrderObjBefore may materialize the EXISTING
        // retained extrusion/sweep's own sketch into its assigned order chain.
        // SDK getOrderObjBefore exposes assigned edges, not all dependencies.
        // Admit only the paired oldPrev→ownSketch→sameOldFeature splice, retaining
        // every old relative ordering edge and the separately checked new
        // feature predecessor/terminal. No arbitrary predecessor is removed.
        if(consumed==null||binding.Edit is not ModelProfileExtrude340)return orders;
        var previous=binding.Before.GetProperty("nodes").EnumerateArray().ToDictionary(node=>node.GetProperty("mc_id").GetString()!,StringComparer.OrdinalIgnoreCase);
        foreach(var parent in binding.ProfileParents)
        {
            cancel();if(!previous.TryGetValue(parent.Feature,out var oldFeature)||!previous.TryGetValue(parent.Sketch,out var oldSketch)||
                !current.TryGetValue(parent.Feature,out var newFeature)||!current.TryGetValue(parent.Sketch,out var newSketch))continue;
            string? oldBefore=oldFeature.GetProperty("native_order_before_id").GetString();
            if(oldBefore==null||!previous.ContainsKey(oldBefore)||oldBefore==parent.Feature||oldBefore==parent.Sketch||
                !OrderMirrors(oldFeature,oldBefore)||!OrderMirrors(oldSketch,null)||
                !OrderMirrors(newFeature,parent.Sketch)||!OrderMirrors(newSketch,oldBefore))continue;
            var identity=binding.Contents.Single(id=>Key(id)==parent.Feature);var (feature,profileId,kind)=ProfileParentFeature(identity);
            if(feature==null||feature.ID!=identity||feature.HostSolidID!=binding.Body.ID||kind!=parent.Kind||Key(profileId)!=parent.Profile)continue;
            var profile=profileId.GetObjectOfType<SketchProfile>();if(profile==null||Key(profile.ID)!=parent.Profile||Key(profile.ParentSketchID)!=parent.Sketch)continue;
            var sketch=profile.ParentSketchID.GetObjectOfType<PlanarSketch>();if(sketch==null||Key(sketch.ID)!=parent.Sketch)continue;
            var sd=feature.DbEntity.Document;var pd=profile.DbEntity.Document;var kd=sketch.DbEntity.Document;
            if(profile.HostSolidID!=binding.Body.ID||sketch.HostSolidID!=binding.Body.ID||sd==null||pd==null||kd==null||
                Key(sd.ID)!=binding.ModelDocument||Key(pd.ID)!=binding.ModelDocument||Key(kd.ID)!=binding.ModelDocument||
                orders.ContainsKey(parent.Feature)||orders.ContainsKey(parent.Sketch))continue;
            orders.Add(parent.Feature,new(parent.Sketch,oldBefore));orders.Add(parent.Sketch,new(oldBefore,null));
        }
        return orders;
    }
    static JsonElement RetainedInsertionNode(JsonElement current,Dictionary<string,PairedOrder> orders)
    {
        string id=current.GetProperty("mc_id").GetString()!;if(!orders.TryGetValue(id,out var order)||!OrderMirrors(current,order.Current))return current;
        var copy=JsonNode.Parse(current.GetRawText())!.AsObject();copy["native_order_before_id"]=order.Previous;
        if(copy["parameters"] is JsonObject parameters&&parameters.ContainsKey("native_order_before_id"))parameters["native_order_before_id"]=order.Previous;
        return Wire.Element(copy);
    }
    static JsonElement RetainedNode(JsonElement node,string? consumedProfile,string? rootView)
    {
        var copy=JsonNode.Parse(node.GetRawText())!.AsObject();string id=copy["mc_id"]!.GetValue<string>();
        if(id==rootView)copy["native_order_before_id"]=null;
        if(copy["parameters"] is JsonObject fields)
        {
            if(fields["edge_ids"] is JsonArray edges){fields["edge_count"]=edges.Count;fields.Remove("edge_ids");}
            // Only the selected profile adds its requested consumer. The full
            // previous forward graph is independently retained; one backlink
            // cannot represent all legitimate shared consumers.
            if(id==consumedProfile){fields.Remove("profile_editing");fields.Remove("dependent_feature_id");}
        }
        return Wire.Element(copy);
    }
    static JsonElement ConstructionDesign(JsonElement snapshot,bool acquiredBody)
    {
        var copy=JsonNode.Parse(snapshot.GetRawText())!.AsObject();copy.Remove("dependent_feature_id");
        if(acquiredBody)
        {
            // Native consumption establishes host and history order. These scope
            // fields are separately verified against actual body composition.
            copy.Remove("profile_host_solid_id");copy.Remove("sketch_host_solid_id");
            copy.Remove("profile_previous");copy.Remove("sketch_previous");
        }
        return Wire.Element(copy);
    }
    static void VerifyRetained(Binding binding,JsonElement after,McObjectId? consumed,Action cancel)
    {
        string? root=binding.Views.ViewIds.Length==0?null:binding.Views.Metadata.EnumerateArray()
            .Single(n=>n.GetProperty("previous").GetString()==Key(binding.Views.LastModelItem)).GetProperty("id").GetString();
        var nodes=after.GetProperty("nodes").EnumerateArray().ToDictionary(n=>n.GetProperty("mc_id").GetString()!,StringComparer.OrdinalIgnoreCase);
        var insertionOrders=VerifiedParentSketchOrders(binding,nodes,consumed,cancel);
        foreach(var before in binding.Before.GetProperty("nodes").EnumerateArray())
        {
            cancel();string key=before.GetProperty("mc_id").GetString()!;
            bool present=nodes.TryGetValue(key,out var current);if(present)current=RetainedInsertionNode(current,insertionOrders);
            if(!(present&&ModelParameterSnapshot.Equivalent(RetainedNode(before,consumed?.ToString(),root),RetainedNode(current,consumed?.ToString(),root))))
                throw new DomainException("MODEL_STATE_UNAVAILABLE","Profile lifecycle changed an existing history item's identity, parameters, source or predecessor.",false,
                    RetainedDetails(before,present?current:null,consumed?.ToString(),root,binding.Before.GetProperty("nodes").GetArrayLength(),nodes.Count,cancel));
        }
        var variables=after.GetProperty("variables").EnumerateArray().ToDictionary(v=>v.GetProperty("mc_id").GetString()!,StringComparer.OrdinalIgnoreCase);
        string? consumedKey=consumed?.ToString();
        var addedConsumers=consumedKey!=null?after.GetProperty("nodes").EnumerateArray()
            .Where(node=>node.GetProperty("parameters").ValueKind==JsonValueKind.Object&&node.GetProperty("parameters").TryGetProperty("profile_id",out var selected)&&selected.GetString()==consumedKey)
            .Select(node=>node.GetProperty("mc_id").GetString()!).Except(binding.Before.GetProperty("nodes").EnumerateArray().Select(node=>node.GetProperty("mc_id").GetString()!),StringComparer.OrdinalIgnoreCase).ToHashSet(StringComparer.OrdinalIgnoreCase):new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach(var before in binding.Before.GetProperty("variables").EnumerateArray())
        {cancel();Need(variables.TryGetValue(before.GetProperty("mc_id").GetString()!,out var current)&&ModelProfileConsumers410.RetainedVariable(before,current,addedConsumers,cancel),
            "MODEL_STATE_UNAVAILABLE","Profile lifecycle changed an existing variable, formula, owner or consumer.");}
    }
    // Completed managed history only: the same normalization/comparer used by
    // the refusal above. This diagnostic neither reads native data nor admits
    // changed nodes, reordered arrays, aliases or opaque storage differences.
    static object RetainedDetails(JsonElement before,JsonElement? current,string? consumed,string? root,int beforeCount,int afterCount,Action cancel)
    {
        string id=before.GetProperty("mc_id").GetString()!;
        try
        {
            var left=RetainedNode(before,consumed,root);JsonElement? right=current.HasValue?RetainedNode(current.Value,consumed,root):null;
            var leaves=new List<object>();long changed=0;int used=2;
            string? Hash(JsonElement? value)=>value.HasValue?Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(value.Value,Wire.Json))):null;
            JsonElement? Value(JsonElement? value)=>value.HasValue&&value.Value.ValueKind is not (JsonValueKind.Object or JsonValueKind.Array)&&
                JsonSerializer.SerializeToUtf8Bytes(value.Value,Wire.Json).Length<=192?value:null;
            string Segment(string key)=>key.Replace("~","~0").Replace("/","~1");
            void Walk(string path,JsonElement? a,JsonElement? b)
            {
                cancel();if(a.HasValue&&b.HasValue&&ModelParameterSnapshot.Equivalent(a.Value,b.Value))return;
                if((!a.HasValue||a.Value.ValueKind==JsonValueKind.Object)&&(!b.HasValue||b.Value.ValueKind==JsonValueKind.Object))
                {
                    var keys=new HashSet<string>(StringComparer.Ordinal);
                    if(a.HasValue)foreach(var field in a.Value.EnumerateObject())keys.Add(field.Name);
                    if(b.HasValue)foreach(var field in b.Value.EnumerateObject())keys.Add(field.Name);
                    keys.Remove("inspection_duration_ms");keys.Remove("budget_notice");
                    if(keys.Count>0)
                    {
                        foreach(string key in keys)
                        {JsonElement? av=a.HasValue&&a.Value.TryGetProperty(key,out var af)?af:null;
                         JsonElement? bv=b.HasValue&&b.Value.TryGetProperty(key,out var bf)?bf:null;Walk(path+"/"+Segment(key),av,bv);}return;
                    }
                }
                if((!a.HasValue||a.Value.ValueKind==JsonValueKind.Array)&&(!b.HasValue||b.Value.ValueKind==JsonValueKind.Array))
                {
                    int ac=a?.GetArrayLength()??0,bc=b?.GetArrayLength()??0;
                    if(ac+bc>0){for(int index=0;index<Math.Max(ac,bc);index++)Walk(path+"/"+index,index<ac&&a.HasValue?a.Value[index]:null,index<bc&&b.HasValue?b.Value[index]:null);return;}
                }
                changed++;if(path.Length>384)path="/@path-sha256="+Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(path)));
                var leaf=new{path,before_kind=a?.ValueKind.ToString(),after_kind=b?.ValueKind.ToString(),before_sha256=Hash(a),after_sha256=Hash(b),before_value=Value(a),after_value=Value(b)};
                int bytes=JsonSerializer.SerializeToUtf8Bytes(leaf,Wire.Json).Length+1;if(used+bytes<=2048){used+=bytes;leaves.Add(leaf);}
            }
            Walk("/retained_history_node",left,right);
            object Result()=>new{scope="completed_normalized_retained_history_node; verified_paired_parent_sketch_order_only; no_metadata_classification",
                node_mc_id=id,current_node_present=current.HasValue,consumed_profile_id=consumed,root_view_id=root,before_node_count=beforeCount,after_node_count=afterCount,
                comparison="ModelParameterSnapshot.Equivalent(RetainedNode): ordered arrays; numeric Near; inspection_duration_ms/budget_notice ignored",
                normalization="only proved paired old extrusion-or-sweep/ownSketch order splice at both reported predecessor fields; root view predecessor cleared; edge IDs replaced by count; selected consumed profile editing/consumer fields removed",
                before_sha256=Hash(left),after_sha256=Hash(right),changed_leaf_count=changed,changed_leaves=leaves.ToArray(),omitted_changed_leaf_count=changed-leaves.Count,
                diagnostic_compaction=changed==leaves.Count?(string?)null:"actual_serialized_detail_byte_budget"};
            var result=Result();while(JsonSerializer.SerializeToUtf8Bytes(result,Wire.Json).Length>4096&&leaves.Count>0){leaves.RemoveAt(leaves.Count-1);result=Result();}
            if(JsonSerializer.SerializeToUtf8Bytes(result,Wire.Json).Length<=4096)return result;
            return new{scope="completed_normalized_retained_history_node",node_mc_id=id,current_node_present=current.HasValue,before_sha256=Hash(left),after_sha256=Hash(right),
                changed_leaf_count=changed,omitted_changed_leaf_count=changed,diagnostic_unavailable_reason="Actual serialized diagnostic exceeds 4096 bytes."};
        }
        catch(OperationCanceledException){throw;}
        catch(Exception error){return new{scope="completed_normalized_retained_history_node",node_mc_id=id,current_node_present=current.HasValue,diagnostic_unavailable_reason=error.GetType().Name};}
    }
    static object FailureDetails(string cause,string stage,string message,string? body,string? sketch,string? profile,string? feature,object? verification,string? cleanup)
    {
        byte[] Bytes(object? value)=>JsonSerializer.SerializeToUtf8Bytes(value,Wire.Json);
        string Hash(byte[] value)=>Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(value));
        string Text(string value,int budget)
        {
            if(Bytes(value).Length<=budget)return value;int low=0,high=value.Length;
            string Prefix(int count){if(count>0&&count<value.Length&&char.IsHighSurrogate(value[count-1])&&char.IsLowSurrogate(value[count]))count--;return value[..count];}
            while(low<high){int middle=low+(high-low+1)/2;if(Bytes(Prefix(middle)).Length<=budget)low=middle;else high=middle-1;}return Prefix(low);
        }
        var detailBytes=Bytes(verification);var messageBytes=Bytes(message);string reported=Text(message,768);
        object? detail=verification;
        if(detailBytes.Length>4096)detail=new{verification_details_sha256=Hash(detailBytes),verification_details_serialized_bytes=detailBytes.Length,
            diagnostic_unavailable_reason="Captured verification details exceed 4096 bytes; complete fingerprint retained."};
        object Result(object? nested)=>new{cause=Text(cause,128),stage=Text(stage,128),message=reported,message_sha256=Hash(messageBytes),message_serialized_bytes=messageBytes.Length,
            omitted_message_utf16_units=message.Length-reported.Length,verification_details=nested,body_mc_id=body,sketch_id=sketch,profile_id=profile,feature_id=feature,cleanup_error=cleanup==null?null:Text(cleanup,128)};
        var result=Result(detail);if(Bytes(result).Length<=8192)return result;
        return Result(new{verification_details_sha256=Hash(detailBytes),verification_details_serialized_bytes=detailBytes.Length,
            diagnostic_unavailable_reason="Captured details exceed complete 8192-byte error budget; complete fingerprint retained."});
    }
    static object OwnershipDetails(McObjectId[] before,McObjectId[] after,bool retained,bool? profilePresent,bool? sketchPresent,bool? profileOwner,bool? sketchOwner,Action cancel)
    {
        // Completed managed ID inventories and already-observed verification
        // flags; this detail builder never issues additional native getters.
        try
        {
            var missing=new List<int>();
            for(int index=0;index<before.Length;index++){cancel();if(!after.Contains(before[index]))missing.Add(index);}
            var details=new{diagnostic_scope="completed_history_ownership_predicate_components",
                all_existing_ids_retained=retained,missing_existing_id_count=missing.Count,
                missing_existing_id_indices_sha256=Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(missing,Wire.Json))),
                profile_in_history=profilePresent,sketch_in_history=sketchPresent,
                profile_host_matches_body=profileOwner,sketch_host_matches_body=sketchOwner,
                null_component_reason="The component was not observed during this verification stage."};
            if(JsonSerializer.SerializeToUtf8Bytes(details,Wire.Json).Length<=8192)return details;
            return new{diagnostic_unavailable_reason="Captured ownership component details exceed their byte budget."};
        }
        catch(OperationCanceledException){throw;}
        catch(Exception error){return new{diagnostic_unavailable_reason="Captured ownership diagnostics unavailable: "+error.GetType().Name};}
    }
    [MethodImpl(MethodImplOptions.NoInlining)]
    internal static Result Execute(ModifyArguments args,DocumentState state,WriteJob job,ReadContext context,JsonLog log)
    {
        Binding? binding=null;McUndoPoint? tracker=null;PlanarSketch? sketch=null;SketchProfile? profile=null;ExtrudeFeature? feature=null;
        bool started=false;string stage="preparation";McObjectId terminal=McObjectId.Null;double observedVolume=double.NaN;
        void Trace(string next){stage=next;log.Write(job.Operation,job.OriginalRequestId,"model_profile340_checkpoint",transaction:next,operationId:job.OperationId);}
        void Close(){var value=tracker;tracker=null;if(value==null)return;try{value.Stop();}finally{value.Dispose();}}
        try
        {
            using(var tx=state.Document.Database.TransactionManager.StartTransaction())binding=Resolve(args,state,tx,job.CheckCancellation);
            // Recheck all source identities/geometry before the first setter.
            using(var tx=state.Document.Database.TransactionManager.StartTransaction())
            {
                Scope(state,tx,binding.NativeDocument);VerifySources(binding,state,tx,job.CheckCancellation);
                Need(ModelProfileDiscovery348.OwnerDocument(binding.Body,state).ID.ToString()==binding.ModelDocument,
                    "DOCUMENT_CONTEXT_MISMATCH","The selected body's native owner document changed during preparation.");
                Need(binding.Body.LastPartItemID==binding.Views.LastHistoryItem,"PRECONDITION_FAILED","Native terminal history changed during source preparation.");
                if(binding.Profile!=null)
                {
                    var current=ModelProfileLifecycleState340.Capture(binding.Profile.Profile,binding.Body,state,tx,job.CheckCancellation,allowCircularHoles:true);
                    Need(current.Token==binding.Profile.Token,"PRECONDITION_FAILED","The reusable profile changed during source preparation.");
                    if(binding.Consumers!=null)ModelProfileConsumers410.VerifyRetained(binding.Consumers,current.Profile,current.Profile.DbEntity.Document,job.CheckCancellation);
                }
            }
            job.CheckCancellation();Trace("source_verified");ModelViewFollowers.CheckStandalone(binding.Body,binding.Views,job.CheckCancellation);tracker=new McUndoPoint(false);tracker.Start();started=true;
            if(binding.Edit is ModelProfileExtrude340)ModelViewFollowers.PrepareInsertion(binding.Views,false);
            if(binding.Edit is ModelProfileCreate340)
            {
                (sketch,profile)=ModelExtrusionFactory.CreateProfile(binding.Body,binding.Definition!,job.CheckCancellation,Trace,
                    binding.Sources.Select(s=>s.Source.Native).ToArray(),ModelProfileDiscovery348.OwnerDocument(binding.Body,state));
                terminal=binding.Body.LastPartItemID;
                // Do not manufacture a profile predecessor or use End-of-Part
                // to suppress pre-existing model items. Observe native order.
                Need(!terminal.IsNull,"MODEL_UPDATE_FAILED","Profile construction lost the native terminal item.");
            }
            else
            {
                var request=(ModelProfileExtrude340)binding.Edit;profile=binding.Profile!.Profile;sketch=binding.Profile.Sketch;
                feature=binding.Body.AddExtrudeFeature(profile.ID,request.Distance,0,
                    request.Direction=="positive"?FeatureExtentDirection.Positive:FeatureExtentDirection.Negative);
                Need(feature!=null,"MODEL_UPDATE_FAILED","Native extrusion from the retained profile was not created.");
                feature.Name=request.Name;feature.PartOperation=request.PartOperation=="cut"?PartFeatureOperation.Cut:PartFeatureOperation.Join;feature.DbEntity.Update();
                if(feature.OrderObjBefore!=binding.Views.LastModelItem)
                    Need(feature.SetOrderObjBefore(binding.Views.LastModelItem,false),"MODEL_UPDATE_FAILED","Native extrusion rejected the retained predecessor.");
                terminal=feature.ID;Trace("retained_profile_consumed");
            }
            if(feature!=null)ModelViewFollowers.CompleteInsertion(binding.Views,terminal);
            Trace("update_before");McObjectManager.UpdateAll();Trace("update_returned");
            var currentProfile=profile??throw new DomainException("MODEL_STATE_UNAVAILABLE","The native profile was not returned.");
            var currentSketch=sketch??throw new DomainException("MODEL_STATE_UNAVAILABLE","The native parent sketch was not returned.");
            (EntityRef Body,ModelProfileLifecycleState340.Snapshot Profile) Verify()
            {
                job.CheckCancellation();using var tx=state.Document.Database.TransactionManager.StartTransaction();var active=Scope(state,tx,binding.NativeDocument);
                var modelDocument=ModelProfileDiscovery348.OwnerDocument(binding.Body,state);
                Need(modelDocument.ID.ToString()==binding.ModelDocument,"DOCUMENT_CONTEXT_MISMATCH","The selected body's actual native owner document changed during profile lifecycle.");
                var representation=ModelHistoryEditGuard.CurrentRepresentation(binding.Body,state,tx,job.CheckCancellation);var inventory=Inventory(state,tx,job.CheckCancellation);
                Need(representation==binding.Representation||!inventory.Contains(binding.Representation),"OBJECT_IDENTITY_MISMATCH","The old body representation remains alongside its replacement.");
                var body=(Db.Solid3d)tx.GetObject(representation,Db.OpenMode.ForRead);
                Need(body.Layer==binding.Layer&&Wire.Element(EntityAdapter.StoredProperties(body)).GetRawText()==binding.Properties.GetRawText(),
                    "MODEL_STATE_UNAVAILABLE","Profile lifecycle changed retained body display properties.");
                observedVolume=ModelFeatureOracle.Read(body).Volume;var ids=ModelInventoryBudget.History(binding.Body,job.CheckCancellation).ToArray();
                bool retainedContents=binding.Contents.All(ids.Contains);
                Need(retainedContents,"PROFILE_OWNERSHIP_MISMATCH","An existing body-history identity disappeared during profile lifecycle.");
                if(feature!=null)
                {
                    bool profilePresent=ids.Contains(currentProfile.ID),sketchPresent=ids.Contains(currentSketch.ID);
                    bool profileOwner=currentProfile.HostSolidID==binding.Body.ID,sketchOwner=currentSketch.HostSolidID==binding.Body.ID;
                    if(!profilePresent||!sketchPresent||!profileOwner||!sketchOwner)
                        throw new DomainException("PROFILE_OWNERSHIP_MISMATCH","Consumed profile or parent sketch lacks actual selected-body composition and native host.",false,
                            OwnershipDetails(binding.Contents,ids,true,profilePresent,sketchPresent,profileOwner,sketchOwner,job.CheckCancellation));
                }
                var captured=ModelProfileLifecycleState340.Capture(currentProfile,binding.Body,state,tx,job.CheckCancellation,allowCircularHoles:feature!=null);
                var added=ids.Except(binding.Contents).ToHashSet();var projections=captured.ProjectionIds.ToHashSet();
                var allowed=new HashSet<McObjectId>{currentProfile.ID,currentSketch.ID};
                allowed.UnionWith(projections);allowed.UnionWith(binding.Sources.Select(s=>s.Source.Native));
                if(!currentSketch.PlanarEntityID.IsNull)allowed.Add(currentSketch.PlanarEntityID);
                if(feature==null)
                {
                    Need(currentProfile.Dependent3dFeatureID.IsNull,"PROFILE_OWNERSHIP_MISMATCH","Standalone construction unexpectedly acquired a forming-feature owner.");
                    var actualBoundary=ModelProfileLifecycleState340.Boundary(captured.ProfileGeometry,binding.Definition!.Plane,job.CheckCancellation);
                    Need(ModelProfileBoundary340.Same(actualBoundary,binding.Sources.Select(s=>s.Source.Curve).ToArray(),job.CheckCancellation),
                        "PROFILE_GEOMETRY_MISMATCH","The actual selected native profile boundary differs from the requested retained source contour.");
                    Need(captured.Sources.Select(s=>s.id).ToHashSet(StringComparer.OrdinalIgnoreCase).SetEquals(binding.Sources.Select(s=>Key(s.Source.Native)))&&
                        captured.Sources.All(s=>s.projection_ids.Count(projections.Select(Key).Contains)==1),"PROFILE_OWNERSHIP_MISMATCH","Not every committed source was retained as one exact native profile projection.");
                    Need(terminal==binding.Views.LastHistoryItem&&binding.Body.LastPartItemID==binding.Views.LastHistoryItem,
                        "MODEL_STATE_UNAVAILABLE","Document profile construction changed the selected body's native terminal item.");
                }
                else
                {
                    allowed.Add(feature.ID);var request=(ModelProfileExtrude340)binding.Edit;
                    Need(ids.Contains(feature.ID)&&feature.HostSolidID==binding.Body.ID&&feature.ProfileID==currentProfile.ID&&
                        feature.OrderObjBefore==binding.Views.LastModelItem&&feature.Extent==ExtrudeParamExtType.Dist&&
                        feature.Direction==(request.Direction=="positive"?FeatureExtentDirection.Positive:FeatureExtentDirection.Negative)&&
                        feature.PartOperation==(request.PartOperation=="cut"?PartFeatureOperation.Cut:PartFeatureOperation.Join)&&
                        ModelParameterSnapshot.Near(feature.Distance,request.Distance)&&ModelParameterSnapshot.Near(feature.Angle,0)&&
                        !feature.DistanceParamVarID.IsNull,"MODEL_STATE_UNAVAILABLE","The native extrusion's profile owner, extent, direction, distance or predecessor differs from the request.");
                    Need(binding.Consumers!=null,"PROFILE_DISCOVERY_UNAVAILABLE","The retained profile consumer baseline is unavailable.");
                    ModelProfileConsumers410.VerifyRetained(binding.Consumers,currentProfile,modelDocument,job.CheckCancellation,addedConsumer:feature.ID);
                    Need(projections.All(id=>ids.Contains(id)&&id.GetObjectOfType<ProjectionSketchEnt>() is {} projection&&
                        projection.HostSketchID==currentSketch.ID&&projection.HostSolidID==binding.Body.ID),
                        "PROFILE_OWNERSHIP_MISMATCH","Consumed profile projections lack actual selected-body composition and native host.");
                    bool acquiredBody=binding.Profile!.Stored.GetProperty("profile_host_solid_id").ValueKind==JsonValueKind.Null||
                        binding.Profile.Stored.GetProperty("sketch_host_solid_id").ValueKind==JsonValueKind.Null;
                    Need(ModelParameterSnapshot.Equivalent(binding.Profile!.Geometry,captured.Geometry)&&
                        ModelParameterSnapshot.Equivalent(binding.Profile.ProfileGeometry,captured.ProfileGeometry)&&
                        ModelParameterSnapshot.Equivalent(ConstructionDesign(binding.Profile.Stored,acquiredBody),ConstructionDesign(captured.Stored,acquiredBody)),
                        "PROFILE_GEOMETRY_MISMATCH","Consuming the profile changed its native sketch, contour, plane, support, source bindings or predecessor.");
                }
                Need(added.IsSubsetOf(allowed),"MODEL_STATE_UNAVAILABLE","Unexpected native history objects were added outside the selected profile lifecycle.");
                var construction=ModelProfileDiscovery348.ConstructionIds(modelDocument,job.CheckCancellation);
                Need(binding.DocumentConstruction.All(construction.Contains)&&construction.Except(binding.DocumentConstruction).All(allowed.Contains),
                    "MODEL_STATE_UNAVAILABLE","Profile lifecycle removed existing document construction or added an unrelated sketch/profile/projection.");
                Need(binding.Body.LastPartItemID==(feature==null?binding.Views.LastHistoryItem:binding.Views.ViewIds.Length==0?terminal:binding.Views.LastHistoryItem),"MODEL_STATE_UNAVAILABLE","The native terminal history item differs from the verified insertion.");
                if(feature==null)ModelViewFollowers.Verify(binding.Body,binding.Views,job.CheckCancellation,refreshStandalone:true);
                else if(binding.Views.HasFollowers)ModelViewFollowers.VerifyAfterInsertion(binding.Body,binding.Views,terminal,job.CheckCancellation,refreshStandalone:true);
                foreach(var old in binding.Inventory)
                {job.CheckCancellation();Need(old==binding.Representation||inventory.Contains(old),"MODEL_STATE_UNAVAILABLE","A retained drawing object disappeared during native profile lifecycle.");}
                foreach(var extra in inventory.Except(binding.Inventory).Where(id=>id!=representation))
                {job.CheckCancellation();Need(allowed.Contains(McObjectId.FromHandle(extra.Handle.Value,active.ID)),"MODEL_STATE_UNAVAILABLE","An unrelated top-level drawing object was created during native profile lifecycle.");}
                VerifySources(binding,state,tx,job.CheckCancellation);
                string handle=Validation.NormalizeHandle(representation.Handle.ToString());
                var after=ModelParameterSnapshot.Capture(new(args.BridgeId,args.DocumentId,args.ExpectedRevision,args.OperationId,args.Units,handle,"","",0),state,job.CheckCancellation);
                ModelParameterSnapshot.Healthy(after);VerifyRetained(binding,after,feature==null?null:currentProfile.ID,job.CheckCancellation);
                var oldVariables=binding.Before.GetProperty("variables").EnumerateArray().Select(v=>v.GetProperty("mc_id").GetString()!).ToHashSet(StringComparer.OrdinalIgnoreCase);
                foreach(var variable in after.GetProperty("variables").EnumerateArray())
                {
                    job.CheckCancellation();if(oldVariables.Contains(variable.GetProperty("mc_id").GetString()!))continue;
                    string? host=variable.GetProperty("host_object_id").GetString();
                    Need(host!=null&&added.Select(Key).Contains(host),"MODEL_STATE_UNAVAILABLE","A new variable has no selected new native history owner.");
                }
                if(feature!=null)
                {
                    var variable=ModelParameterSnapshot.Variable(after,Key(feature.DistanceParamVarID));
                    Need(variable.GetProperty("host_object_id").GetString()==Key(feature.ID)&&variable.GetProperty("value_available").GetBoolean()&&
                        ModelParameterSnapshot.Near(variable.GetProperty("value").GetDouble(),((ModelProfileExtrude340)binding.Edit).Distance),
                        "MODEL_STATE_UNAVAILABLE","The native distance variable does not belong to the requested extrusion or has the wrong value.");
                }
                return(new(args.BridgeId,args.DocumentId,handle),captured);
            }
            var verified=Verify();Trace("profile_verified");job.CheckCancellation();job.BeginCommit();Close();verified=Verify();
            var source=new EntityRef(args.BridgeId,args.DocumentId,binding.Edit.Handles[0]);bool replaced=source.handle!=verified.Body.handle;
            var mapping=new ModelProfileChange340(0,binding.Edit.Operation,source,verified.Body,Key(binding.Body.ID),Key(currentSketch.ID),Key(currentProfile.ID),
                verified.Profile.Token,Key(binding.Body.LastPartItemID),binding.Sources.Select(s=>new EntityRef(args.BridgeId,args.DocumentId,Validation.NormalizeHandle(s.Source.Id.Handle.ToString()))).ToArray(),
                replaced,feature==null?null:Key(feature.ID),feature==null?null:Key(feature.DistanceParamVarID),ModelProfileDiscovery348.Ownership(verified.Profile));
            var data=new ModelProfileOutput340(args.OperationId,"committed",false,[verified.Body],replaced?[source]:[],replaced?[verified.Body]:[],[],"not_guaranteed",[mapping]);
            var warnings=new List<string>{WriteRecovery.Warning,
                "MODEL_PROFILE_LIFECYCLE: Refresh exact profile_ids through inspect_model_history, body history, variables and drawing views; model rebuild may update dependents. Unused construction reports actual document/native-host scope. Profile IDs are native identities, never invented DWG entity handles. Inspect the final engineering result through MCP."};
            if(feature!=null)
            {
                double beforeVolume=binding.Before.GetProperty("native").GetProperty("volume").GetDouble();
                double effect=((ModelProfileExtrude340)binding.Edit).PartOperation=="cut"?beforeVolume-observedVolume:observedVolume-beforeVolume;
                if(effect<=Math.Max(1e-8,beforeVolume*1e-10))warnings.Add("MATERIAL_EFFECT_UNVERIFIED: Native feature, requested parameters and nonempty body were verified, but mass integration did not establish added/removed material. Inspect the useful native join/cut before accepting it.");
            }
            var result=Result.Ok(job.OriginalRequestId,data,context,warnings.ToArray());
            result=ModelViewFollowers.WithWarnings(result,binding.Views);job.RecordCommit(result);Trace("commit_recorded");return result;
        }
        catch(Exception error)
        {
            if(job.CommittedResult is {} committed)return committed;
            Exception? cleanup=null;try{Close();}catch(Exception e){cleanup=e;}
            bool unknown=started||cleanup!=null;string cause=error is DomainException domain?domain.Error.code:error is OperationCanceledException?"CANCELLED":"MODEL_PROFILE_LIFECYCLE_FAILED";
            return job.Error(unknown?"WRITE_OUTCOME_UNKNOWN":cause,unknown?
                "Native profile lifecycle is unverified; partial construction or feature history may remain. Inspect the current body and refresh history before another dependent edit.":
                "Native profile lifecycle did not start.",unknown?"unknown":"not_started",context,
                FailureDetails(cause,stage,error.Message,binding==null?null:Key(binding.Body.ID),sketch==null?null:Key(sketch.ID),profile==null?null:Key(profile.ID),feature==null?null:Key(feature.ID),
                    (error as DomainException)?.Error.details,cleanup?.GetType().Name)) with{warnings=[WriteRecovery.Warning]};
        }
    }
    static void VerifySources(Binding binding,DocumentState state,Db.Transaction tx,Action cancel)
    {
        foreach(var before in binding.Sources)
        {
            cancel();var source=DrawingProfileWriter.SourceOf(before.Source.Id,state,tx,false,before.Source.Workplane);var entity=(Db.Entity)tx.GetObject(source.Id,Db.OpenMode.ForRead);
            Need(source.Native==before.Source.Native&&source.Layer==before.Source.Layer&&DrawingProfileGeometry.Same(source.Curve,before.Source.Curve)&&
                Wire.Element(EntityAdapter.StoredProperties(entity)).GetRawText()==before.Properties.GetRawText()&&
                NativeOwnedMetadata.Capture(entity,tx,cancel).GetRawText()==before.Owned.GetRawText()&&XData(entity,cancel).GetRawText()==before.XData.GetRawText(),
                "PROFILE_SOURCE_CHANGED","A retained profile source changed geometry, identity, layer, display or owned metadata during lifecycle.");
        }
    }
}
