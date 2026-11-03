using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using System.Text.Json.Nodes;
using Multicad;
using Multicad.DatabaseServices;
using Multicad.Mc3D;
using NanoCAD.Contracts;
using Db=Teigha.DatabaseServices;

namespace NanoCADBridge;

// Complete observed native ordering is separate from GetPartContents inventory.
// No B-rep/mass before==after comparison: a useful reorder can change the shape.
internal static class ModelFeatureReorderState345
{
    internal const string ValidityWarning="NATIVE_SOLID_VALIDITY_UNAVAILABLE: Native B-rep validation was unavailable; inspect the rebuilt part visually.";
    internal sealed record Snapshot(Mc3dSolid Body,Db.ObjectId Representation,string NativeDocument,
        McObjectId[] Contents,string[] Features,Dictionary<string,string?> Previous,
        HashSet<Db.ObjectId> Inventory,ModelViewFollowers.Snapshot Views,JsonElement Preserved,
        JsonElement Properties,JsonElement BodyMetadata,string[] Warnings,Dictionary<string,JsonElement> Constraints);
    internal static void Need([DoesNotReturnIf(false)] bool ok,string code,string message)
    {if(!ok)throw new DomainException(code,message);}
    internal static string Key(McObjectId id)=>id.ToString();
    internal static string? Id(McObjectId id)=>id.IsNull?null:Key(id);
    internal static Db.ObjectId Model(Db.Database db,Db.Transaction tx)=>
        ((Db.BlockTable)tx.GetObject(db.BlockTableId,Db.OpenMode.ForRead))[Db.BlockTableRecord.ModelSpace];
    internal static McDocument Scope(DocumentState state,Db.Transaction tx,string? expected=null)
    {
        var active=McDocument.ActiveDocument;
        Need(active!=null&&state.Document.IsNamedDrawing&&!string.IsNullOrWhiteSpace(active.Path)&&
            string.Equals(Path.GetFullPath(active.Path),Path.GetFullPath(state.Document.Name),StringComparison.OrdinalIgnoreCase)&&
            (expected==null||active.ID.ToString()==expected),"DOCUMENT_CONTEXT_MISMATCH","Use the matching active named native model document.");
        Need(state.Document.Database.CurrentSpaceId==Model(state.Document.Database,tx),"OUT_OF_SCOPE","Activate Model Space before reordering a part feature.");
        return active;
    }
    internal static HashSet<Db.ObjectId> Inventory(DocumentState state,Db.Transaction tx,Action cancel)
    {
        var result=new HashSet<Db.ObjectId>();var bytes=new ModelInventoryBudget(cancel);
        foreach(Db.ObjectId id in (Db.BlockTableRecord)tx.GetObject(Model(state.Document.Database,tx),Db.OpenMode.ForRead))
        {
            cancel();if(id.IsNull||!id.IsValid||id.IsErased||id.IsEffectivelyErased)continue;
            bytes.Add(id.Handle.ToString());result.Add(id);
        }
        return result;
    }
    internal static bool Forming(McObjectId id)=>id.GetObjectOfType<ExtrudeFeature>()!=null||
        id.GetObjectOfType<RevolveFeature>()!=null||id.GetObjectOfType<ExtrudeAlongPathFeature>()!=null||
        id.GetObjectOfType<LoftFeature>()!=null||id.GetObjectOfType<HoleFeature>()!=null||
        id.GetObjectOfType<ThreadFeature>()!=null||id.GetObjectOfType<CircularPatternFeature>()!=null||
        id.GetObjectOfType<RectangularPatternFeature>()!=null||id.GetObjectOfType<MirrorFeature>()!=null||
        id.GetObjectOfType<FilletFeature>()!=null||id.GetObjectOfType<ChamferFeature>()!=null||id.GetObjectOfType<ThicknessFeature>()!=null;
    static void SortSet(JsonObject item,string name,Action cancel)
    {
        if(item[name] is not JsonArray values)return;
        var result=new List<string>();foreach(var value in values){cancel();result.Add(value!.GetValue<string>());}
        item[name]=JsonSerializer.SerializeToNode(result.OrderBy(value=>value,StringComparer.Ordinal).ToArray(),Wire.Json);
    }
    static JsonElement Root(Db.DBObject item,Action cancel)
    {
        using var xdata=item.XData;using var reactors=item.GetPersistentReactorIds();
        var bytes=new ModelInventoryBudget(cancel);var ids=new List<string>();
        foreach(Db.ObjectId id in reactors){cancel();string value=id.IsNull?"null":id.Handle.ToString();bytes.Add(value);ids.Add(value);}
        object? data=xdata==null?null:NativeOwnedMetadata.Buffer(xdata,cancel,value=>bytes.Add(value));
        var result=Wire.Element(new{type=item.GetType().FullName,owner=item.OwnerId.Handle.ToString(),has_fields=item.HasFields,
            extension=item.ExtensionDictionary.IsNull?null:item.ExtensionDictionary.Handle.ToString(),
            xdata=data,reactors=ids.OrderBy(value=>value,StringComparer.Ordinal).ToArray()});
        new ModelInventoryBudget(cancel).Add(result);return result;
    }
    internal static Snapshot Capture(Mc3dSolid body,Db.ObjectId representation,ModifyArguments args,
        DocumentState state,Db.Transaction tx,Action cancel,bool captureFullDesign=false,McObjectId pendingTerminalMirror=default,bool captureResidentIdentity=false)
    {
        var active=Scope(state,tx);var solid=tx.GetObject(representation,Db.OpenMode.ForRead) as Db.Solid3d;
        Need(solid!=null&&solid.GetType()==typeof(Db.Solid3d)&&solid.OwnerId==Model(state.Document.Database,tx)&&!solid.IsNull,
            "INVALID_GEOMETRY","The part must retain one nonempty top-level native solid representation.");
        Need(McObjectId.FromHandle(representation.Handle.Value,active.ID)==body.ID,"OBJECT_IDENTITY_MISMATCH","The current representation belongs to another native body.");
        var ids=ModelInventoryBudget.History(body,cancel).ToArray();var inventory=Inventory(state,tx,cancel);
        Need(ids.Length>0&&ids.Distinct().Count()==ids.Length,"HISTORY_UNAVAILABLE","Native part history is empty or contains duplicate identities.");
        // Only terminal Mirror deletion can observe its selected pending node.
        // Its actual status remains in the snapshot; retained peers and all
        // ordinary callers keep the existing health admission unchanged.
        if(!pendingTerminalMirror.IsNull)
        {
            var pending=pendingTerminalMirror.GetObjectOfType<MirrorFeature>();
            Need(captureFullDesign&&ids.Contains(pendingTerminalMirror)&&body.LastPartItemID==pendingTerminalMirror&&
                pendingTerminalMirror.QueryObjectType()==typeof(MirrorFeature)&&pending!=null&&pending.ID==pendingTerminalMirror&&
                pending.HostSolidID==body.ID&&pending.DbEntity.Document?.ID==body.DbEntity.Document.ID&&
                pending.Status.ToString()=="NeedUpdate"&&!pending.IsSuppressed&&!pending.IsBeyondEndOfPart,
                "MODEL_STATE_UNAVAILABLE","Only the exact active terminal Mirror may be pending in a deletion preservation snapshot.");
        }
        var views=ModelViewFollowers.Resolve(body,ids,cancel,includeStandalone:true);var bytes=new ModelInventoryBudget(cancel);
        var previous=new Dictionary<string,string?>(StringComparer.OrdinalIgnoreCase);var links=new List<ModelSequenceLink>();var features=new List<string>();
        foreach(var id in ids)
        {
            cancel();var node=id.GetObjectOfType<HistoryItem>();string? before=null;
            if(node!=null)
            {
                Need(node.ID==id&&node.HostSolidID==body.ID&&
                    (node.Status.ToString()=="Ok"||ProjectedHistorySources.HealthySuppressed(node)||id==pendingTerminalMirror)&&!node.IsBeyondEndOfPart,
                    "MODEL_STATE_UNAVAILABLE","Every retained native history item must be healthy and owned by this part; intentional healthy suppression is retained and End Of Part scope remains protected.");
                before=Id(node.OrderObjBefore);
            }
            string key=Key(id);previous.Add(key,before);var link=new ModelSequenceLink(key,before);bytes.Add(link);links.Add(link);
            if(Forming(id))features.Add(key);
        }
        var order=ModelFeatureReorderPlanning345.FeatureOrder(links.ToArray(),features.ToArray(),Key(views.LastModelItem),cancel);
        var read=new Arguments(args.BridgeId,args.DocumentId,args.ExpectedRevision,100,null,null,null,[Validation.NormalizeHandle(representation.Handle.ToString())]);
        var history=Wire.Element(ModelHistoryReader.Read(read,state,tx,Model(state.Document.Database,tx),args.BridgeId,CancellationToken.None,cancel)).GetProperty("items")[0];
        Need(history.GetProperty("complete").GetBoolean(),"HISTORY_UNAVAILABLE","Native source/support history contains an unverified reference.");
        var kept=new List<JsonElement>();
        foreach(var observed in history.GetProperty("nodes").EnumerateArray())
        {
            cancel();var node=JsonNode.Parse(observed.GetRawText())!.AsObject();
            string? orderedBefore=node["native_order_before_id"]?.GetValue<string>();node.Remove("native_order_before_id");
            // Only the separately observed assigned ordering edge is excluded.
            // Explicit profile/source/axis/support/driver references below stay
            // retained, including references that coincide with this edge.
            if(orderedBefore!=null&&node["dependencies"] is JsonArray dependencies)
                for(int index=dependencies.Count-1;index>=0;index--)
                {cancel();if(dependencies[index]!.GetValue<string>().Equals(orderedBefore,StringComparison.OrdinalIgnoreCase))dependencies.RemoveAt(index);}
            SortSet(node,"dependencies",cancel);
            string observedId=observed.GetProperty("mc_id").GetString()!;
            if(features.Contains(observedId,StringComparer.OrdinalIgnoreCase))
                Need(node["parameters"] is JsonObject,"MODEL_PARAMETERS_UNAVAILABLE","A retained forming feature needs its typed parameter/reference preservation adapter before reordering.");
            if(node["parameters"] is JsonObject parameters)
            {
                parameters.Remove("native_order_before_id");
                if(parameters["edge_ids"] is JsonArray edges){parameters["edge_count"]=edges.Count;parameters.Remove("edge_ids");}
            }
            var value=Wire.Element(node);bytes.Add(value);kept.Add(value);
        }
        // Deletion reads supply no write operations; explicit full capture
        // short-circuits that array and observes the same retained sources,
        // construction metadata, sketch constraints and native paths.
        bool terminalIntent=captureFullDesign || args.Operations[0] is ModelFeatureReorderSpec345{ExpectedResultingLastItemId:not null};
        var owner=ModelProfileDiscovery348.OwnerDocument(body,state);
        var sketches=new List<JsonElement>();var profiles=new List<JsonElement>();var construction=new List<object>();
        var constraints=new Dictionary<string,JsonElement>(StringComparer.OrdinalIgnoreCase);
        foreach(var id in ids)
        {
            cancel();var native=id.GetObjectOfType<HistoryItem>();
            if(terminalIntent&&native!=null)
            {
                Need(native.DbEntity.Document!=null&&native.DbEntity.Document.ID==owner.ID,
                    "DEPENDENCY_OWNERSHIP_MISMATCH","Each retained native history item must resolve in this part's actual owner document.");
                var value=new{id=Key(id),owner=Key(owner.ID),
                    layer=native.DbEntity.Layer,visibility=native.DbEntity.Visibility,
                    metadata=ModelSketchLifecycleReader362.Metadata(id,native.DbEntity,owner,state,tx,cancel,observeResidentIdentity:captureResidentIdentity)};
                bytes.Add(value);construction.Add(value);
            }
            var sketch=id.GetObjectOfType<PlanarSketch>();
            if(sketch!=null)
            {
                Need(!sketch.IsInEditMode,"MODEL_STATE_UNAVAILABLE","Exit native sketch edit mode before reordering the part.");
                var geometryBytes=new ModelInventoryBudget(cancel);var geometry=Wire.Element(SketchGeometryReader.Read(sketch,cancel,value=>geometryBytes.Add(value)));
                Need(geometry.GetProperty("geometry_details_complete").GetBoolean(),"SKETCH_GEOMETRY_UNAVAILABLE","This sketch contains geometry outside the current preservation adapter.");
                JsonElement? retained=null;
                if(terminalIntent)
                {
                    var observed=ModelSketchLifecycleReader362.Capture(body,sketch,state,tx,cancel);
                    var copy=JsonNode.Parse(observed.Stored.GetRawText())!.AsObject();
                    // Assigned order is verified separately. Peer profiles use
                    // the independent selected-contour snapshot below; all
                    // containing/source/geometry/metadata/support state stays.
                    copy.Remove("previous");copy.Remove("profiles");copy.Remove("constraint_observation");
                    retained=Wire.Element(copy);constraints.Add(Key(id),observed.Constraints);
                }
                var item=Wire.Element(new{id=Key(id),support=Id(sketch.PlanarEntityID),geometry,retained});bytes.Add(item);sketches.Add(item);
            }
            var profile=id.GetObjectOfType<SketchProfile>();
            if(profile!=null)
            {
                var full=ModelProfileLifecycleState340.Capture(profile,body,state,tx,cancel,allowCircularHoles:true);
                var value=JsonNode.Parse(full.Stored.GetRawText())!.AsObject();value.Remove("profile_previous");value.Remove("sketch_previous");
                var saved=Wire.Element(value);bytes.Add(saved);profiles.Add(saved);
            }
        }
        var variables=Wire.Element(ModelVariablesReader.Read(read,state,tx,Model(state.Document.Database,tx),args.BridgeId,CancellationToken.None,cancel));
        var variableStates=new List<JsonElement>();
        foreach(var variable in variables.GetProperty("variables").EnumerateArray())
        {
            cancel();var value=JsonNode.Parse(variable.GetRawText())!.AsObject();SortSet(value,"downstream_variables",cancel);SortSet(value,"downstream_objects",cancel);
            var saved=Wire.Element(value);bytes.Add(saved);variableStates.Add(saved);
        }
        var external=ProjectedHistorySources.Read(body,ids,state,tx,cancel);var sourceStates=new List<object>();
        foreach(var source in external.OrderBy(value=>value.handle,StringComparer.OrdinalIgnoreCase))
        {
            cancel();var id=CadStructureWriter.Find(state.Document.Database,source.handle);var entity=(Db.Entity)tx.GetObject(id,Db.OpenMode.ForRead);
            var detail=terminalIntent?Wire.Element(EntityAdapter.Detail(entity,EntityAdapter.Summary(entity,entity.OwnerId,"","",false),CancellationToken.None)):default;
            var item=new{source,properties=EntityAdapter.StoredProperties(entity),root=Root(entity,cancel),
                geometry=terminalIntent?(JsonElement?)detail.GetProperty("geometry"):null,
                metadata=terminalIntent?(JsonElement?)ModelProfileElementsState358.Metadata(entity,tx,cancel,false,observeNativeProxy:true):null};bytes.Add(item);sourceStates.Add(item);
        }
        var paths=new Dictionary<string,object>(StringComparer.OrdinalIgnoreCase);
        if(terminalIntent)foreach(var id in ids)
        {
            cancel();var sweep=id.GetObjectOfType<ExtrudeAlongPathFeature>();if(sweep==null)continue;
            var bound=sweep.GetPath();Need(bound!=null&&bound.Count>0,"NATIVE_PATH_STATE_UNAVAILABLE","A retained typed sweep needs its complete ordered native path inventory.");
            foreach(var path in bound)
            {
                cancel();Need(path!=null&&!path.ID.IsNull,"NATIVE_PATH_STATE_UNAVAILABLE","A retained sweep path identity is unavailable.");
                if(paths.ContainsKey(Key(path.ID)))continue;
                var native=path.ID.GetObjectOfType<Multicad.DatabaseServices.StandardObjects.DbGeometry>();
                Db.ObjectId raw=Db.ObjectId.Null;
                Need(native!=null&&native.ID==path.ID&&path.ID.Handle!=0&&
                    state.Document.Database.TryGetObjectId(new Db.Handle(path.ID.Handle),out raw)&&!raw.IsNull&&!raw.IsErased&&!raw.IsEffectivelyErased&&
                    McObjectId.FromHandle(raw.Handle.Value,active.ID)==path.ID,
                    "NATIVE_PATH_STATE_UNAVAILABLE","The retained sweep path needs an exact current resident drawing-curve identity; no foreign or guessed owner is admitted.");
                var entity=tx.GetObject(raw,Db.OpenMode.ForRead) as Db.Curve;
                Need(entity!=null&&entity.OwnerId==Model(state.Document.Database,tx),"NATIVE_PATH_STATE_UNAVAILABLE","A retained sweep path is not a current top-level drawing curve.");
                var detail=Wire.Element(EntityAdapter.Detail(entity,EntityAdapter.Summary(entity,entity.OwnerId,"","",false),CancellationToken.None));
                var value=new{id=Key(path.ID),handle=Validation.NormalizeHandle(raw.Handle.ToString()),properties=EntityAdapter.StoredProperties(entity),
                    geometry=detail.GetProperty("geometry"),metadata=ModelProfileElementsState358.Metadata(entity,tx,cancel,false,observeNativeProxy:true)};
                bytes.Add(value);paths.Add(Key(path.ID),value);
            }
        }
        // Validate actual path/support health independently; absence is not an
        // inferred unknown source. Read-only owned source data is not stripped.
        var nativeSources=NativePathSources.Read(ids,cancel).OrderBy(id=>id,StringComparer.Ordinal).ToArray();
        var preserved=Wire.Element(new{body=Key(body.ID),last=Id(body.LastPartItemID),
            nodes=kept.OrderBy(value=>value.GetProperty("mc_id").GetString(),StringComparer.Ordinal).ToArray(),
            variables_owner=variables.GetProperty("mc_document_id"),variables=variableStates.OrderBy(value=>value.GetProperty("mc_id").GetString(),StringComparer.Ordinal).ToArray(),
            sketches=sketches.OrderBy(value=>value.GetProperty("id").GetString(),StringComparer.Ordinal).ToArray(),profiles=profiles.OrderBy(value=>value.GetProperty("profile_id").GetString(),StringComparer.Ordinal).ToArray(),
            construction=construction.OrderBy(value=>Wire.Element(value).GetProperty("id").GetString(),StringComparer.OrdinalIgnoreCase).ToArray(),
            external_sources=sourceStates,path_sources=paths.OrderBy(pair=>pair.Key,StringComparer.OrdinalIgnoreCase).Select(pair=>pair.Value).ToArray(),native_source_ids=nativeSources});
        new ModelInventoryBudget(cancel).Add(preserved);
        var notices=new List<string>();notices.AddRange(ModelSketchConstraintObservation353.Warnings(constraints.Values));
        try {cancel();using var brep=new Teigha.BoundaryRepresentation.Brep(solid);Need(!brep.IsNull&&brep.CheckEntity(),"INVALID_GEOMETRY","Native solid validation reports an invalid part.");}
        catch(Teigha.Runtime.Exception){notices.Add(ValidityWarning);}
        catch(NotSupportedException){notices.Add(ValidityWarning);}
        if(sourceStates.Count>0)notices.Add(terminalIntent?
            "SOURCE_OWNED_METADATA_OBSERVATION: Current source/path geometry, properties and classified native metadata are retained exactly; public association observations do not certify unobservable private solver state.":
            "SOURCE_OWNED_METADATA_SCOPE: External source geometry, display, XData, fields/reactor IDs and extension identity are retained; opaque extension contents are not deeply inspected by this reorder adapter.");
        // Body representation-owned application data must survive replacement.
        // Normalize only its owned dictionary's back-reference to this root;
        // all dictionary/Xrecord IDs, content and references remain exact.
        var owned=JsonNode.Parse(NativeOwnedMetadata.Capture(solid,tx,cancel,observeAssocManager:terminalIntent,observeNativeProxy:true).GetRawText())!.AsArray();
        string handle=Validation.NormalizeHandle(representation.Handle.ToString());
        foreach(var entry in owned)
        {
            cancel();if(entry!["common"] is JsonObject common&&common["owner_handle"]?.GetValue<string>()==handle)common["owner_handle"]="$CURRENT_BODY_REPRESENTATION";
        }
        var bodyRoot=JsonNode.Parse(Root(solid,cancel).GetRawText())!.AsObject();
        var bodyMetadata=Wire.Element(new{root=bodyRoot,owned});new ModelInventoryBudget(cancel).Add(bodyMetadata);
        notices.AddRange(NativeOwnedMetadata.NativeProxyWarnings(preserved,bodyMetadata));
        new ModelInventoryBudget(cancel).Add(new{preserved,bodyMetadata});
        var display=Wire.Element(new{layer_handle=solid.LayerId.Handle.ToString(),linetype_handle=solid.LinetypeId.Handle.ToString(),properties=EntityAdapter.StoredProperties(solid)});
        return new(body,representation,active.ID.ToString(),ids,order,previous,inventory,views,preserved,display,bodyMetadata,notices.ToArray(),constraints);
    }
    internal static bool Preserved(Snapshot before,Snapshot after,McObjectId expectedLast,Action cancel)
    {
        // Only the actual separately verified terminal field changes. No
        // native ID, explicit source/driver edge or metadata is normalized.
        if(after.Preserved.GetProperty("last").GetString()!=Id(expectedLast))return false;
        var current=JsonNode.Parse(after.Preserved.GetRawText())!.AsObject();
        current["last"]=JsonNode.Parse(before.Preserved.GetProperty("last").GetRawText());
        if(!Equivalent(before.Preserved,Wire.Element(current),cancel)||!before.Constraints.Keys.ToHashSet(StringComparer.OrdinalIgnoreCase).SetEquals(after.Constraints.Keys))return false;
        foreach(var pair in before.Constraints)
        {cancel();if(!ModelSketchConstraintObservation353.Equivalent(pair.Value,after.Constraints[pair.Key],cancel))return false;}
        return true;
    }
    internal static bool Equivalent(JsonElement left,JsonElement right,Action cancel)
    {
        var pairs=new Stack<(JsonElement Left,JsonElement Right)>();pairs.Push((left,right));
        while(pairs.Count>0)
        {
            cancel();var (a,b)=pairs.Pop();if(a.ValueKind!=b.ValueKind)return false;
            if(a.ValueKind==JsonValueKind.Object)
            {
                if(a.EnumerateObject().Count()!=b.EnumerateObject().Count())return false;
                foreach(var field in a.EnumerateObject())
                {
                    cancel();if(!b.TryGetProperty(field.Name,out var value))return false;
                    // Integer/binary/user metadata is exact, not a geometry
                    // tolerance comparison. No large XData number can drift.
                    if(field.Name is "root" or "properties" or "metadata" or "native_metadata") {if(field.Value.GetRawText()!=value.GetRawText())return false;}
                    else pairs.Push((field.Value,value));
                }
            }
            else if(a.ValueKind==JsonValueKind.Array)
            {if(a.GetArrayLength()!=b.GetArrayLength())return false;for(int i=0;i<a.GetArrayLength();i++){cancel();pairs.Push((a[i],b[i]));}}
            else if(a.ValueKind==JsonValueKind.Number){if(!ModelParameterSnapshot.Near(a.GetDouble(),b.GetDouble()))return false;}
            else if(a.GetRawText()!=b.GetRawText())return false;
        }
        return true;
    }
}
