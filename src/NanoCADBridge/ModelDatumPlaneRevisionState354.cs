using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using Multicad;
using Multicad.DatabaseServices;
using Multicad.Mc3D;
using NanoCAD.Contracts;
using Db=Teigha.DatabaseServices;

namespace NanoCADBridge;

// Revision of existing fixed construction and its exact same-part consumers.
// No owner reassignment, replacement datum or source-coordinate regeneration.
internal static class ModelDatumPlaneRevisionState354
{
    internal sealed record Snapshot(ModelDatumPlaneState353.Part Part,ModelDatumPlaneState353.Snapshot Datum,
        ModelViewFollowers.Snapshot Views,ModelDatumMirrorConsumer354[] Mirrors,string[] Downstream,string Token,
        JsonElement Sketches,string[] DirectConsumers,ModelDatumDependents411.Snapshot Dependents);
    internal static void Need([System.Diagnostics.CodeAnalysis.DoesNotReturnIf(false)]bool ok,string code,string message)=>ModelDatumPlaneState353.Need(ok,code,message);
    static string Key(McObjectId id)=>id.ToString();
    static string[] Ids(IEnumerable<McObjectId>? ids,Action cancel)
    {Need(ids!=null,"DEPENDENCY_INVENTORY_UNAVAILABLE","The exact native construction consumer inventory is unavailable.");ModelInventoryBudget.Ids(ids,cancel);return ids.Select(id=>{cancel();return Key(id);}).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(id=>id,StringComparer.OrdinalIgnoreCase).ToArray();}
    internal static Snapshot Capture(Mc3dSolid body,WorkPlane plane,ModifyArguments args,DocumentState state,Db.Transaction tx,Action cancel)
    {
        var part=ModelDatumPlaneState353.CapturePart(body,args,state,tx,cancel);var datum=ModelDatumPlaneState353.Capture(plane,body,state,tx,cancel);
        ModelDatumPlaneState353.MirrorAffinity(datum,body);
        Need(plane.DefinitionType==WPLDefinitionType.Fixed&&!plane.IsGCSObject&&plane.ParamVarID.IsNull,
            "OUT_OF_SCOPE","Revise an existing fixed non-GCS datum. Dependent datum definitions and global coordinate-system elements retain their own editing scope.");
        Need(datum.Design.GetProperty("constraint_ids").GetArrayLength()==0,"OUT_OF_SCOPE","This fixed-plane branch does not set or solve attached native 3D constraints.");
        var views=ModelViewFollowers.Resolve(body,part.Contents,cancel,includeStandalone:true);
        ModelDatumDependents411.RequireConsumerScope(plane,body,datum.Document,cancel);
        var mirrors=new List<ModelDatumMirrorConsumer354>();var sketches=new Dictionary<string,JsonElement>(StringComparer.OrdinalIgnoreCase);var ids=part.Contents.ToHashSet();var roots=new HashSet<McObjectId>();
        void Sketch(PlanarSketch sketch)
        {
            Need(!sketch.ID.IsNull&&ids.Contains(sketch.ID)&&sketch.HostSolidID==body.ID&&sketch.DbEntity.Document?.ID==datum.Document.ID&&sketch.PlanarEntityID==plane.ID&&
                sketch.Status.ToString()=="Ok"&&!sketch.IsSuppressed&&!sketch.IsBeyondEndOfPart&&!sketch.IsInEditMode,
                "DATUM_CONSUMER_ADAPTER_REQUIRED","A direct datum sketch must retain this exact WorkPlane and healthy selected-part ownership.");
            var links=Ids(sketch.GetDependsOn(false,false),cancel);Need(links.Contains(Key(plane.ID),StringComparer.OrdinalIgnoreCase),"DATUM_DEPENDENCY_CHANGED","A datum-supported sketch lacks its actual native plane dependency.");
            var profiles=part.Contents.Where(id=>id.GetObjectOfType<SketchProfile>() is {} profile&&profile.ParentSketchID==sketch.ID).ToArray();
            foreach(var id in profiles){var profile=id.GetObjectOfType<SketchProfile>()!;Need(profile.ID==id&&profile.HostSolidID==body.ID&&profile.DbEntity.Document?.ID==datum.Document.ID,"PROFILE_OWNERSHIP_MISMATCH","A datum-supported profile lacks exact selected-part ownership.");}
            sketches[Key(sketch.ID)]=Wire.Element(new{sketch_id=Key(sketch.ID),support_id=Key(plane.ID),profile_ids=profiles.Select(Key).OrderBy(id=>id,StringComparer.OrdinalIgnoreCase).ToArray()});roots.Add(sketch.ID);
        }
        var direct=plane.GetDependent();Need(Ids(direct,cancel).SequenceEqual(datum.Read.direct_consumer_ids,StringComparer.OrdinalIgnoreCase),"PRECONDITION_FAILED","Native datum direct consumers changed during capture.");
        foreach(var id in direct!)
        {
            cancel();Need(!id.IsNull,"DATUM_CONSUMER_ADAPTER_REQUIRED","A native datum direct consumer has no identity.");
            if(id.GetObjectOfType<PlanarSketch>() is {} sketch){Need(sketch.ID==id,"OBJECT_IDENTITY_MISMATCH","The direct sketch resolves another identity.");Sketch(sketch);continue;}
            if(id.GetObjectOfType<SketchProfile>() is {} profile)
            {Need(profile.ID==id&&ids.Contains(id)&&profile.ParentSketchID.GetObjectOfType<PlanarSketch>()!=null,"PROFILE_OWNERSHIP_MISMATCH","A direct profile lacks its exact selected-part parent.");Sketch(profile.ParentSketchID.GetObjectOfType<PlanarSketch>()!);roots.Add(id);continue;}
            var mirror=id.GetObjectOfType<MirrorFeature>();
            Need(mirror!=null&&mirror.ID==id&&ids.Contains(id)&&mirror.HostSolidID==body.ID&&mirror.DbEntity.Document?.ID==datum.Document.ID&&
                mirror.Status.ToString()=="Ok"&&!mirror.IsSuppressed&&!mirror.IsBeyondEndOfPart,
                "DATUM_CONSUMER_ADAPTER_REQUIRED","Use actual selected-part sketch/profile or Mirror consumers. Another datum definition or foreign part is not changed by this fixed-plane revision.");
            var links=Ids(mirror.GetDependsOn(false,false),cancel);Need(links.Contains(Key(plane.ID),StringComparer.OrdinalIgnoreCase),"DATUM_DEPENDENCY_CHANGED","The native mirror does not retain this exact plane dependency.");
            var sources=new List<string>();for(int index=0;index<mirror.SourceItemsCount;index++)
            {
                cancel();var source=mirror.GetSourceItemFeature(index);Need(!source.IsNull,"DEPENDENCY_INVENTORY_UNAVAILABLE","A retained native mirror source/subfeature ID is unavailable.");
                var feature=source.GetObjectOfType<HistoryItem>();
                Need(feature!=null&&feature.ID==source&&ids.Contains(source)&&feature.HostSolidID==body.ID&&feature.DbEntity.Document?.ID==datum.Document.ID&&
                    feature.Status.ToString()=="Ok"&&!feature.IsSuppressed&&!feature.IsBeyondEndOfPart,
                    "DATUM_CONSUMER_ADAPTER_REQUIRED","This selected-part feature mirror requires actual healthy material source features in the retained part and owner document. External source features and whole-solid null source IDs require their own dependency adapter.");
                sources.Add(Key(source));
            }
            Need(sources.Count>0,"MODEL_STATE_UNAVAILABLE","A retained mirror has no native source items.");
            mirrors.Add(new(Key(id),sources.ToArray(),mirror.HasSubFeatureItems,ModelDatumPlaneState353.Id(mirror.OrderObjBefore),links));
            roots.Add(id);
        }
        var downstream=datum.Read.downstream_consumer_ids;
        var actualDownstream=plane.GetAllDependent(false);Need(Ids(actualDownstream,cancel).SequenceEqual(downstream,StringComparer.OrdinalIgnoreCase),"PRECONDITION_FAILED","Native datum downstream consumers changed during capture.");
        foreach(var id in actualDownstream!)
        {
            cancel();
            // Use actual dependency IDs returned by this exact plane. A drawing
            // follower can belong to its own verified native view document;
            // it must not be re-resolved by guessing the part's owner document.
            Need(!id.IsNull&&(id==body.ID||ids.Contains(id)||ModelViewFollowers.OwnsDependent(views,id)),
                "OUT_OF_SCOPE","A construction consumer is outside this selected part and its actual documented view/Section group.");
        }
        var ordered=mirrors.OrderBy(item=>item.feature_id,StringComparer.OrdinalIgnoreCase).ToArray();
        if(ids.Contains(plane.ID))roots.Add(plane.ID);
        var dependents=ModelDatumDependents411.Capture(body,part.Representation,args,state,tx,roots,cancel);
        var selectedSketches=Wire.Element(sketches.OrderBy(pair=>pair.Key,StringComparer.OrdinalIgnoreCase).Select(pair=>pair.Value).ToArray());var directIds=Ids(direct,cancel);
        var observed=Wire.Element(new{datum=datum.Stored,part=part.Parameters,part.Metadata,part.Display,mirrors=ordered,sketches=selectedSketches,direct=directIds,downstream,
            design=dependents.Part.Preserved,inputs=dependents.Inputs.OrderBy(pair=>pair.Key,StringComparer.OrdinalIgnoreCase).Select(pair=>new{id=pair.Key,state=pair.Value.Stored}).ToArray(),dependents.Storage,affected=dependents.Affected.OrderBy(id=>id,StringComparer.OrdinalIgnoreCase).ToArray()});new ModelInventoryBudget(cancel).Add(observed);
        string token=Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(Stable(observed,cancel),Wire.Json)));
        return new(part,datum,views,ordered,downstream,token,selectedSketches,directIds,dependents);
    }
    static JsonElement Stable(JsonElement value,Action cancel)
    {
        cancel();var root=JsonNode.Parse(value.GetRawText())!;
        // Only SolidGeometryReader's known diagnostic fields, never arbitrary
        // metadata or a source record with a similarly named stored property.
        if(root["part"]?["native"] is JsonObject native){native.Remove("inspection_duration_ms");native.Remove("budget_notice");}
        return Wire.Element(root);
    }
    internal static bool SamePhysical(ModelDatumPlaneRead353 a,SketchPlaneSpec target)
    {
        var normal=DrawingWorkplane.Normal(target);var n=new[]{normal.X,normal.Y,normal.Z};
        return a.plane_wcs.GetProperty("normal_wcs").EnumerateArray().Zip(n).All(pair=>ModelDatumPlaneState353.Requested(pair.First.GetDouble(),pair.Second))&&
            ModelDatumPlaneState353.Requested(a.plane_wcs.GetProperty("oriented_plane_offset").GetDouble(),DrawingWorkplane.Dot(target.Origin,normal));
    }
    static JsonElement Node(JsonElement value,Action cancel)
    {
        var node=JsonNode.Parse(value.GetRawText())!.AsObject();
        if(node["dependencies"] is JsonArray dependencies)node["dependencies"]=JsonSerializer.SerializeToNode(dependencies.Select(link=>link!.GetValue<string>()).OrderBy(id=>id,StringComparer.OrdinalIgnoreCase).ToArray(),Wire.Json);
        // Existing generated B-rep marker policy only. Explicit design/source IDs
        // and number of retained edges remain checked.
        if(node["parameters"] is JsonObject parameters&&parameters["edge_ids"] is JsonArray edges){cancel();parameters["edge_count"]=edges.Count;parameters.Remove("edge_ids");}
        return Wire.Element(node);
    }
    static JsonElement Root(JsonElement value,string handle)
    {
        var node=JsonNode.Parse(value.GetRawText())!.AsObject();string? extension=node["extension"]?.GetValue<string>();node["handle"]="$CURRENT_BODY_REPRESENTATION";
        if(node["owned"] is JsonArray owned)foreach(var value2 in owned)if(value2?["common"] is JsonObject common&&common["handle"]?.GetValue<string>()==extension&&common["owner_handle"]?.GetValue<string>()==handle)common["owner_handle"]="$CURRENT_BODY_REPRESENTATION";
        if(node["owned"] is JsonObject opaque&&opaque["records"] is JsonArray records)foreach(var value2 in records)if(value2 is JsonObject record&&record["handle"]?.GetValue<string>()==extension&&record["owner"]?.GetValue<string>()==handle)record["owner"]="$CURRENT_BODY_REPRESENTATION";
        return Wire.Element(node);
    }
    static JsonElement Definition(JsonElement value)
    {var node=JsonNode.Parse(value.GetRawText())!.AsObject();node.Remove("normal");node.Remove("oriented_plane_offset");return Wire.Element(node);}
    internal static string[] Verify(Snapshot before,Snapshot after,ModelDatumPlaneRevisionSpec354 edit,Action cancel)
    {
        void Require(bool ok,string message)=>Need(ok,"MODEL_STATE_UNAVAILABLE",message);
        Require(before.Part.Body.ID==after.Part.Body.ID&&before.Part.DrawingDocument==after.Part.DrawingDocument&&before.Part.OwnerDocument==after.Part.OwnerDocument&&
            before.Datum.Plane.ID==after.Datum.Plane.ID&&before.Datum.Document.ID==after.Datum.Document.ID,"Retained datum/part identity or actual document changed.");
        Require(SamePhysical(after.Datum.Read,edit.Plane),"The native oriented plane location/normal differs from the requested revision.");
        Require(ModelParameterSnapshot.Equivalent(Definition(before.Datum.Design),Definition(after.Datum.Design))&&
            before.Datum.Read.host_solid_id==after.Datum.Read.host_solid_id&&before.Datum.Read.in_selected_body_contents==after.Datum.Read.in_selected_body_contents&&
            before.Datum.Read.native_order_before_id==after.Datum.Read.native_order_before_id&&before.Datum.Read.dependency_ids.SequenceEqual(after.Datum.Read.dependency_ids,StringComparer.OrdinalIgnoreCase)&&
            before.Datum.Read.direct_consumer_ids.SequenceEqual(after.Datum.Read.direct_consumer_ids,StringComparer.OrdinalIgnoreCase)&&before.Downstream.SequenceEqual(after.Downstream,StringComparer.OrdinalIgnoreCase),
            "Retained fixed definition/name/driver/constraint/host/order/source/consumer links changed.");
        Require(before.Part.Contents.ToHashSet().SetEquals(after.Part.Contents)&&after.Part.Body.LastPartItemID==before.Views.LastHistoryItem&&
            ModelParameterSnapshot.Equivalent(Wire.Element(before.Mirrors),Wire.Element(after.Mirrors))&&ModelParameterSnapshot.Equivalent(before.Sketches,after.Sketches),"Retained feature/source/subfeature, supported sketch/profile or history identities/order changed.");
        var warnings=ModelDatumDependents411.Verify(before.Dependents,after.Dependents,cancel);
        Require(before.Part.Display.GetRawText()==after.Part.Display.GetRawText(),"Retained body appearance changed.");
        Require(before.Part.Inventory.Where(id=>id!=before.Part.Representation&&id!=before.Datum.Representation).ToHashSet().SetEquals(after.Part.Inventory.Where(id=>id!=after.Part.Representation&&id!=after.Datum.Representation)),"An unrelated drawing entity was added/lost during native plane revision.");
        if(before.Datum.Representation.HasValue)
        {
            Require(after.Datum.Representation.HasValue,"The retained native datum lost its existing drawing representation.");
            var oldRepresentation=before.Datum.Read.representation!.Value;var newRepresentation=after.Datum.Read.representation!.Value;
            Require(oldRepresentation.GetProperty("properties").GetRawText()==newRepresentation.GetProperty("properties").GetRawText()&&
                Root(oldRepresentation.GetProperty("metadata"),before.Datum.Representation.Value.Handle.ToString()).GetRawText()==Root(newRepresentation.GetProperty("metadata"),after.Datum.Representation!.Value.Handle.ToString()).GetRawText(),
                "The retained datum's actual drawing appearance/readable metadata changed.");
        }
        // A newly materialized real native representation is reported rather than
        // confused with a new WorkPlane ID or forced body ownership.
        ModelViewFollowers.Verify(after.Part.Body,before.Views,cancel,refreshStandalone:true);
        return warnings;
    }
}
