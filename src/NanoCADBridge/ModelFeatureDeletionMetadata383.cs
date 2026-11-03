using System.Text.Json;
using System.Text.Json.Nodes;
using Multicad;
using Multicad.Mc3D;
using NanoCAD.Contracts;
using Db=Teigha.DatabaseServices;

namespace NanoCADBridge;

// Deletion only. Existing raw/native roundtrips and the selected before-owner
// proof bound retired storage; no class/name/cache or general local-ID waiver.
internal static class ModelFeatureDeletionMetadata383
{
    static readonly StringComparer Ids=StringComparer.OrdinalIgnoreCase;
    static string? Text(JsonNode? value)=>value?.GetValue<string>();
    static string Handle(JsonNode value)=>Text(value["common"]?["handle"])!;
    static JsonNode? Copy(JsonNode? value)=>value==null?null:JsonNode.Parse(value.ToJsonString());
    static bool Exact(JsonNode? left,JsonNode? right)=>left?.ToJsonString()==right?.ToJsonString();
    static JsonObject? BodyShadow(JsonObject body,string representation,Action cancel)
    {
        if(body["root"] is not JsonObject root||root.Count!=6||
            new[]{"type","owner","has_fields","extension","xdata","reactors"}.Any(key=>!root.ContainsKey(key))||body["owned"] is not JsonArray)return null;
        // Both readers observe the same resident DBObject. Their root field
        // names differ; State345 additionally normalizes only direct owned
        // back-references to its actual current body representation.
        var result=Copy(body)!.AsObject();
        result["root"]=new JsonObject{["type"]=Copy(root["type"]),["owner_handle"]=Copy(root["owner"]),
            ["HasFields"]=Copy(root["has_fields"]),["extension_handle"]=Copy(root["extension"]),
            ["xdata"]=Copy(root["xdata"]),["reactors"]=Copy(root["reactors"])};
        foreach(var row in result["owned"]!.AsArray())
        {
            cancel();if(row?["common"] is JsonObject common&&Text(common["owner_handle"])=="$CURRENT_BODY_REPRESENTATION")
                common["owner_handle"]=representation;
        }
        return result;
    }
    static bool RetireLinks(JsonObject previous,JsonObject current,string field,HashSet<string> removed,Action cancel)
    {
        if(Exact(previous[field],current[field]))return true;
        if(previous[field] is not JsonArray before||current[field] is not JsonArray after)return false;
        int next=0;
        foreach(var row in before)
        {
            cancel();if(next<after.Count&&Exact(row,after[next])){next++;continue;}
            if(Text(row) is not {} id||!removed.Contains(id))return false;
        }
        if(next!=after.Count)return false;
        previous[field]=Copy(current[field]);return true;
    }
    static bool ResidentRoot(JsonObject previous,JsonObject current,string identity,string owner)
    {
        if(previous["resident_identity"] is not JsonObject before||current["resident_identity"] is not JsonObject after||!Exact(before,after)||before.Count!=5||
            !new[]{"native_id","native_owner_document_id","raw_handle","raw_owner_handle","runtime_type"}.All(before.ContainsKey)||
            !Ids.Equals(Text(before["native_id"]),identity)||!Ids.Equals(Text(before["native_owner_document_id"]),owner)||
            string.IsNullOrWhiteSpace(Text(before["raw_handle"])))return false;
        bool Root(JsonObject envelope)=>envelope["metadata"]?["root"] is JsonObject root&&Text(root["type"])==Text(before["runtime_type"])&&
            Ids.Equals(Text(root["owner_handle"]),Text(before["raw_owner_handle"]));
        return Root(previous)&&Root(current);
    }
    internal static Dictionary<string,string> Bindings(ModelFeatureReorderState345.Snapshot part,DocumentState state,Db.Transaction tx,Action cancel)
    {
        var owner=ModelProfileDiscovery348.OwnerDocument(part.Body,state);var drawing=ModelProfileDiscovery348.DrawingDocument(state);
        var owned=part.BodyMetadata.GetProperty("owned").EnumerateArray().Select(value=>value.GetProperty("common").GetProperty("handle").GetString()!).ToHashSet(Ids);
        var result=new Dictionary<string,string>(Ids);
        foreach(var id in part.Contents)
        {
            cancel();var item=id.GetObjectOfType<HistoryItem>();
            if(item==null||item.ID!=id||item.HostSolidID!=part.Body.ID||item.DbEntity.Document==null||item.DbEntity.Document.ID!=owner.ID||id.Handle==0)continue;
            if(!state.Document.Database.TryGetObjectId(new Db.Handle(id.Handle),out var raw)||raw.IsNull||!raw.IsValid||raw.IsErased||raw.IsEffectivelyErased)continue;
            string handle=Validation.NormalizeHandle(raw.Handle.ToString());if(!owned.Contains(handle))continue;
            if(McObjectId.FromHandle(raw.Handle.Value,owner.ID)!=id&&McObjectId.FromHandle(raw.Handle.Value,drawing.ID)!=id)continue;
            var resident=tx.GetObject(raw,Db.OpenMode.ForRead);var database=resident.Database;
            if(database==null||database.UnmanagedObject==IntPtr.Zero||database.UnmanagedObject!=state.Document.Database.UnmanagedObject)continue;
            result.Add(id.ToString(),handle);
        }
        // A retained snapshot shadow must have one exact current native owner,
        // not an inferred alias merely because two IDs share a raw record.
        var ambiguous=result.GroupBy(pair=>pair.Value,Ids).Where(group=>group.Count()!=1).Select(group=>group.Key).ToHashSet(Ids);
        foreach(string id in result.Where(pair=>ambiguous.Contains(pair.Value)).Select(pair=>pair.Key).ToArray())result.Remove(id);
        return result;
    }
    internal static bool Retained(JsonElement beforeBody,JsonElement afterBody,JsonElement expectedDesign,JsonElement actualDesign,
        ModelSketchBodyStorage364.Proof proof,IReadOnlyDictionary<string,string> bindings,string beforeRepresentation,string afterRepresentation,Action cancel,
        out string reason,out JsonElement normalizedBody,out JsonElement normalizedDesign)
    {
        normalizedBody=beforeBody;normalizedDesign=expectedDesign;
        var prior=JsonNode.Parse(beforeBody.GetRawText())!.AsObject();var actual=JsonNode.Parse(afterBody.GetRawText())!.AsObject();
        var beforeRows=prior["owned"]!.AsArray();var afterRows=actual["owned"]!.AsArray();
        if(beforeRows.Select(value=>Handle(value!)).Distinct(Ids).Count()!=beforeRows.Count||afterRows.Select(value=>Handle(value!)).Distinct(Ids).Count()!=afterRows.Count)
        {reason="body_owned_record_identity_duplicated";return false;}
        var old=beforeRows.ToDictionary(value=>Handle(value!),value=>value!,Ids);var current=afterRows.ToDictionary(value=>Handle(value!),value=>value!,Ids);
        var removed=old.Keys.Except(current.Keys,Ids).ToHashSet(Ids);
        if(removed.Count==0)
            return ModelSketchBodyStorage364.Retained(afterBody,beforeBody,[proof],cancel,out reason,out normalizedBody);
        if(!proof.proved||proof.raw_handle==null||!removed.Contains(proof.raw_handle))
        {reason="retired_storage_has_no_exact_selected_feature_anchor";return false;}
        // Reverse the existing new-tree proof. Its exact-target addition rule
        // now permits only forward removal of links into the retired tree;
        // unchanged dangling links can remain observed. Do not pre-remove them
        // from one snapshot. Every missing record must still reach the exact
        // selected anchor, and unrelated records/links stay exact.
        bool retained=ModelSketchBodyStorage364.Retained(afterBody,beforeBody,[proof],cancel,out reason,out normalizedBody,newReactorTargets:removed);
        if(!retained)return false;
        var canonicalBody=JsonNode.Parse(normalizedBody.GetRawText())!.AsObject();
        var canonical=canonicalBody["owned"]!.AsArray().ToDictionary(value=>Handle(value!),value=>value!,Ids);
        var design=JsonNode.Parse(expectedDesign.GetRawText())!.AsObject();var observed=JsonNode.Parse(actualDesign.GetRawText())!.AsObject();
        var selected=new HashSet<string>(Ids){proof.native_id};bool aggregateShadows=false;string? shadowFailure=null;
        bool Envelope(JsonNode? previous,JsonNode? next,string identity)
        {
            if(previous is not JsonObject left||next is not JsonObject right||Exact(left,right))return true;
            bool Fail(string predicate)
            {
                // Bounded identity/predicate facts from completed snapshots;
                // neither opaque payload nor another native read is needed.
                shadowFailure??=predicate+"; native_id="+identity+"; before_raw="+(Text(left["resident_identity"]?["raw_handle"])??"unwitnessed")+
                    "; after_raw="+(Text(right["resident_identity"]?["raw_handle"])??"unwitnessed");return false;
            }
            if(Text(left["scope"])!="exact_resident_DWG_owned_metadata"||Text(right["scope"])!=Text(left["scope"])||
                left["metadata"] is not JsonObject oldMetadata||right["metadata"] is not JsonObject currentMetadata)return Fail("metadata_envelope_scope_or_shape_changed");
            var oldAggregate=BodyShadow(prior,beforeRepresentation,cancel);var currentAggregate=BodyShadow(actual,afterRepresentation,cancel);
            bool aggregate=oldAggregate!=null&&currentAggregate!=null&&Exact(oldMetadata,oldAggregate)&&Exact(currentMetadata,currentAggregate);
            bool Root(JsonNode root,JsonNode common)=>Text(root["type"])==Text(common["runtime_type"])&&
                Ids.Equals(Text(root["owner_handle"]),Text(common["owner_handle"]))&&Ids.Equals(Text(root["extension_handle"]),Text(common["extension_handle"]))&&
                Exact(root["xdata"],common["xdata"])&&Exact(root["reactors"],common["reactors"]);
            if(aggregate)
            {
                // A native cohosted item can expose the complete body graph,
                // rather than one owned child. Admit this redundant observation
                // only when its entire before/after metadata equals the two
                // completed body snapshots, including all headers and payloads.
                // The selected-tree body comparator above already proved the
                // only permitted delta; no native alias/class/cache is inferred.
                var normalizedAggregate=BodyShadow(canonicalBody,afterRepresentation,cancel);
                if(normalizedAggregate==null)return Fail("completed_body_shadow_shape_unavailable");left["metadata"]=normalizedAggregate;aggregateShadows=true;
            }
            else if(bindings.TryGetValue(identity,out string? holder)&&old.TryGetValue(holder,out var oldHolder)&&current.TryGetValue(holder,out var currentHolder))
            {
                // Both shadows were captured through the native item's actual
                // raw roundtrip. Keep the existing individual-holder proof.
                if(!Root(oldMetadata["root"]!,oldHolder["common"]!)||!Root(currentMetadata["root"]!,currentHolder["common"]!))return Fail("body_owned_holder_root_binding_changed");
                bool Owned(JsonArray rows,IReadOnlyDictionary<string,JsonNode> records)=>rows.All(row=>row!=null&&records.TryGetValue(Handle(row),out var record)&&Exact(row,record));
                if(!Owned(oldMetadata["owned"]!.AsArray(),old)||!Owned(currentMetadata["owned"]!.AsArray(),current))return Fail("body_owned_shadow_is_not_exact_record_subset");
                // Both roots match their actual holders above. The completed
                // body comparison has proved only selected-tree link retirement,
                // including unchanged references to the now-erased target.
                oldMetadata["root"]!["reactors"]=Copy(currentMetadata["root"]!["reactors"]);
                var rows=oldMetadata["owned"]!.AsArray();
                for(int index=rows.Count-1;index>=0;index--)
                {cancel();string handle=Handle(rows[index]!);if(removed.Contains(handle))rows.RemoveAt(index);else rows[index]=Copy(canonical[handle]);}
            }
            else if(ResidentRoot(left,right,identity,proof.native_owner_document_id))
            {
                // A retained datum can have its own resident root outside the
                // body-owned tree. Capture proved this exact native/raw/owner
                // identity on both sides. Only incoming selected-tree reactors
                // may retire; every other root field and all owned data stay exact.
                if(oldMetadata["root"] is not JsonObject oldRoot||currentMetadata["root"] is not JsonObject currentRoot||
                    !RetireLinks(oldRoot,currentRoot,"reactors",removed,cancel))return Fail("resident_root_reactors_changed_outside_selected_retirement");
                if(!Exact(oldMetadata,currentMetadata))return Fail("resident_root_other_metadata_changed");
            }
            else return Fail("resident_witness_absent_or_changed_and_no_exact_body_owned_holder");
            var links=left["reactor_observation"]!.AsObject();
            var observedLinks=right["reactor_observation"]!.AsObject();
            if(!RetireLinks(links,observedLinks,"native_reactor_ids",selected,cancel))return Fail("native_reactor_ids_changed_outside_selected_retirement");
            var reactors=links["drawing_reactors"]!.AsArray();var observedReactors=observedLinks["drawing_reactors"]!.AsArray();int nextReactor=0;
            foreach(var priorReactor in reactors)
            {
                cancel();var row=priorReactor!;string? handle=Text(row["handle"]);
                if(handle==null||!removed.Contains(handle))
                {if(nextReactor>=observedReactors.Count||!Exact(row,observedReactors[nextReactor++]))return Fail("unrelated_drawing_reactor_changed");continue;}
                var common=old[handle]["common"]!;string? target=Text(row["native_target_id"]);
                if(Text(row["runtime_type"])!=Text(common["runtime_type"])||!Ids.Equals(Text(row["owner_handle"]),Text(common["owner_handle"]))||
                    target!=null&&!selected.Contains(target))return Fail("retired_drawing_reactor_lacks_exact_prior_descriptor");
                if(nextReactor<observedReactors.Count&&Ids.Equals(handle,Text(observedReactors[nextReactor]?["handle"])))
                {
                    var observedRow=observedReactors[nextReactor]!.AsObject();
                    // The reader does not open an erased target. Only its exact
                    // already-proved handle may retain this unavailable shape.
                    bool unavailable=observedRow.Count==4&&new[]{"handle","runtime_type","owner_handle","native_target_id"}.All(observedRow.ContainsKey)&&
                        observedRow["runtime_type"]==null&&observedRow["owner_handle"]==null&&observedRow["native_target_id"]==null;
                    if(!Exact(row,observedRow)&&!unavailable)return Fail("retired_drawing_reactor_changed_outside_unavailable_shape");nextReactor++;
                }
            }
            if(nextReactor!=observedReactors.Count)return Fail("drawing_reactor_added_or_duplicated");
            links["drawing_reactors"]=Copy(observedLinks["drawing_reactors"]);
            return true;
        }
        bool Rows(string field,string key,Action<JsonObject,JsonObject,string> compare)
        {
            var values=design[field]!.AsArray();var next=observed[field]!.AsArray();
            if(values.Count!=next.Count)return false;
            for(int index=0;index<values.Count;index++)
            {
                cancel();var left=values[index]!.AsObject();var right=next[index]!.AsObject();string? identity=Text(left[key]);
                if(identity==null||!Ids.Equals(identity,Text(right[key])))return false;compare(left,right,identity);
            }
            return true;
        }
        bool shadows=true;
        if(!Rows("construction","id",(left,right,id)=>shadows&=Envelope(left["metadata"],right["metadata"],id))||
            !Rows("sketches","id",(left,right,id)=>shadows&=Envelope(left["retained"]?["native_metadata"],right["retained"]?["native_metadata"],id)))shadows=false;
        normalizedDesign=Wire.Element(design);
        if(!shadows){reason="retained_native_metadata_shadow_lacks_exact_body_storage_binding; "+(shadowFailure??"construction_or_sketch_row_identity_changed");return false;}
        reason=aggregateShadows?"exact_surviving_body_metadata_and_completed_aggregate_shadows_except_proved_selected_storage_and_its_retired_reactors":
            "exact_surviving_body_metadata_except_proved_selected_storage_and_its_retired_reactors";return true;
    }
}
