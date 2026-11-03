using System.Text.Json;
using System.Text.Json.Nodes;
using Multicad;
using Multicad.Mc3D;
using NanoCAD.Contracts;
using Db=Teigha.DatabaseServices;

namespace NanoCADBridge;

// Additive selection of an actual document-owned datum for the established
// native feature-mirror path. Existing body/GCS selection is unchanged.
internal static class ModelDatumPlaneMirror353
{
    internal sealed record Binding(ModelDatumPlaneState353.Part Part,ModelDatumPlaneState353.Snapshot Datum);
    internal static Binding Resolve(string id,string token,Mc3dSolid body,ModifyArguments args,DocumentState state,Db.Transaction tx,Action cancel,ModelViewFollowers.Snapshot? followers=null)
    {
        var owner=ModelProfileDiscovery348.OwnerDocument(body,state);var plane=ModelDatumPlaneState353.Resolve(id,owner,cancel);
        var datum=ModelDatumPlaneState353.Capture(plane,body,state,tx,cancel);ModelDatumPlaneState353.MirrorAffinity(datum,body);
        ModelDatumPlaneState353.Need(datum.Token.Equals(token,StringComparison.OrdinalIgnoreCase),"PRECONDITION_FAILED","The native datum geometry/definition/actual host/source/consumer state changed; refresh datum_plane_ids.");
        var part=ModelDatumPlaneState353.CapturePart(body,args,state,tx,cancel);return new(part with{Views=followers??part.Views},datum);
    }
    internal static void PreWrite(Binding before,ModifyArguments args,DocumentState state,Action cancel)
    {
        using var tx=state.Document.Database.TransactionManager.StartTransaction();var datum=ModelDatumPlaneState353.Capture(before.Datum.Plane,before.Part.Body,state,tx,cancel);
        var current=ModelDatumPlaneState353.CapturePart(before.Part.Body,args,state,tx,cancel);
        ModelDatumPlaneState353.Need(before.Datum.Token==datum.Token&&before.Part.Inventory.SetEquals(current.Inventory)&&
            ModelParameterSnapshot.Equivalent(before.Part.Parameters,current.Parameters)&&before.Part.Metadata.GetRawText()==current.Metadata.GetRawText()&&before.Part.Display.GetRawText()==current.Display.GetRawText(),
            "PRECONDITION_FAILED","Native datum/part state changed immediately before the planned mirror insertion.");
    }
    static JsonElement Node(JsonElement value,bool drawing,Action cancel)
    {
        var node=JsonNode.Parse(value.GetRawText())!.AsObject();
        if(drawing)
        {
            string? previous=node["native_order_before_id"]?.GetValue<string>();node.Remove("native_order_before_id");
            if(node["dependencies"] is JsonArray dependencies)
                for(int i=dependencies.Count-1;i>=0;i--){cancel();if(dependencies[i]!.GetValue<string>().Equals(previous,StringComparison.OrdinalIgnoreCase))dependencies.RemoveAt(i);}
        }
        if(node["dependencies"] is JsonArray links)node["dependencies"]=JsonSerializer.SerializeToNode(links.Select(link=>link!.GetValue<string>()).OrderBy(id=>id,StringComparer.OrdinalIgnoreCase).ToArray(),Wire.Json);
        // Existing generated B-rep edge-marker policy: explicit profile, source,
        // plane, datum, driver IDs and the stored number of edges remain.
        if(node["parameters"] is JsonObject parameters&&parameters["edge_ids"] is JsonArray edges){parameters["edge_count"]=edges.Count;parameters.Remove("edge_ids");}
        return Wire.Element(node);
    }
    static JsonElement Root(JsonElement metadata,string handle)
    {
        var node=JsonNode.Parse(metadata.GetRawText())!.AsObject();string? extension=node["extension"]?.GetValue<string>();node["handle"]="$CURRENT_BODY_REPRESENTATION";
        // Only an actual owned dictionary's body-root back-reference can change
        // on native representation replacement. Arbitrary XData stays exact.
        if(node["owned"] is JsonArray owned)
            foreach(var value in owned)
                if(value?["common"] is JsonObject common&&common["handle"]?.GetValue<string>()==extension&&common["owner_handle"]?.GetValue<string>()==handle)common["owner_handle"]="$CURRENT_BODY_REPRESENTATION";
        if(node["owned"] is JsonObject opaque&&opaque["records"] is JsonArray records)
            foreach(var value in records)
                if(value is JsonObject record&&record["handle"]?.GetValue<string>()==extension&&record["owner"]?.GetValue<string>()==handle)record["owner"]="$CURRENT_BODY_REPRESENTATION";
        return Wire.Element(node);
    }
    internal static ModelDatumPlaneRead353 Verify(Binding before,Mc3dSolid body,McObjectId feature,ModifyArguments args,DocumentState state,Db.Transaction tx,Action cancel)
    {
        var after=ModelDatumPlaneState353.CapturePart(body,args,state,tx,cancel);var datum=ModelDatumPlaneState353.Capture(before.Datum.Plane,body,state,tx,cancel);
        ModelDatumPlaneState353.VerifyMirror(before.Datum,datum,feature,cancel);
        void Need(bool value,string message)=>ModelDatumPlaneState353.Need(value,"MODEL_STATE_UNAVAILABLE",message);
        Need(before.Part.DrawingDocument==after.DrawingDocument&&before.Part.OwnerDocument==after.OwnerDocument,"The retained mirror part's native document changed.");
        Need(body.LastPartItemID==(before.Part.Views.ViewIds.Length==0?feature:before.Part.Views.LastHistoryItem)&&feature.GetObjectOfType<HistoryItem>() is {} actualFeature&&
            actualFeature.HostSolidID==body.ID&&actualFeature.Status.ToString()=="Ok"&&!actualFeature.IsSuppressed&&actualFeature.OrderObjBefore==before.Part.Views.LastModelItem,
            "The inserted mirror feature's actual health/host/predecessor/terminal changed.");
        var oldNodes=before.Part.Parameters.GetProperty("nodes").EnumerateArray().ToArray();var nodes=after.Parameters.GetProperty("nodes").EnumerateArray().ToDictionary(node=>node.GetProperty("mc_id").GetString()!,StringComparer.OrdinalIgnoreCase);
        var views=before.Part.Views.ViewIds.Select(id=>id.ToString()).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach(var old in oldNodes)
        {
            cancel();string id=old.GetProperty("mc_id").GetString()!;Need(nodes.TryGetValue(id,out var current),"An existing native history identity was lost during datum-based mirror insertion.");
            Need(ModelParameterSnapshot.Equivalent(Node(old,views.Contains(id),cancel),Node(current,views.Contains(id),cancel)),"Native mirror insertion changed another retained feature's parameters/order/explicit design links.");
        }
        Need(ModelParameterSnapshot.Equivalent(before.Part.Parameters.GetProperty("external_sources"),after.Parameters.GetProperty("external_sources"))&&
            ModelParameterSnapshot.Equivalent(before.Part.Parameters.GetProperty("native_source_ids"),after.Parameters.GetProperty("native_source_ids")),"Native mirror insertion changed retained source references/geometry.");
        var mirror=feature.GetObjectOfType<MirrorFeature>();Need(mirror!=null&&mirror.HostSolidID==body.ID,"The native mirror feature identity/host is unavailable.");
        var allowed=(mirror!.GetAllDependent(false)??throw new DomainException("DEPENDENCY_INVENTORY_UNAVAILABLE","Actual mirror consumers are unavailable.")).Select(id=>id.ToString()).Append(feature.ToString()).ToHashSet(StringComparer.OrdinalIgnoreCase);
        for(int i=0;i<mirror.SourceItemsCount;i++){cancel();var id=mirror.GetSourceItemFeature(i);Need(!id.IsNull,"Native mirror source/subfeature inventory is unavailable.");allowed.Add(id.ToString());}
        var variables=after.Parameters.GetProperty("variables").EnumerateArray().ToDictionary(value=>value.GetProperty("mc_id").GetString()!,StringComparer.OrdinalIgnoreCase);
        foreach(var old in before.Part.Parameters.GetProperty("variables").EnumerateArray())
        {
            cancel();string id=old.GetProperty("mc_id").GetString()!;Need(variables.TryGetValue(id,out var current),"Native mirror insertion lost an existing owning variable.");
            var a=JsonNode.Parse(old.GetRawText())!.AsObject();var b=JsonNode.Parse(current.GetRawText())!.AsObject();
            var prior=a["downstream_objects"]!.AsArray().Select(item=>item!.GetValue<string>()).ToHashSet(StringComparer.OrdinalIgnoreCase);
            var actual=b["downstream_objects"]!.AsArray().Select(item=>item!.GetValue<string>()).ToHashSet(StringComparer.OrdinalIgnoreCase);
            Need(prior.IsSubsetOf(actual)&&actual.Except(prior,StringComparer.OrdinalIgnoreCase).All(allowed.Contains),"Native mirror insertion lost or acquired an unrelated existing variable consumer.");
            a.Remove("downstream_objects");b.Remove("downstream_objects");Need(Wire.Element(a).GetRawText()==Wire.Element(b).GetRawText(),"Native mirror insertion changed an existing variable expression/value/name/owner/dependency.");
        }
        Need(before.Part.Display.GetRawText()==after.Display.GetRawText()&&Root(before.Part.Metadata,before.Part.Representation.Handle.ToString()).GetRawText()==
            Root(after.Metadata,after.Representation.Handle.ToString()).GetRawText(),"Native mirror insertion changed retained body display/readable owned metadata.");
        ModelViewFollowers.VerifyAfterInsertion(body,before.Part.Views,feature,cancel,refreshStandalone:true);return datum.Read;
    }
}
