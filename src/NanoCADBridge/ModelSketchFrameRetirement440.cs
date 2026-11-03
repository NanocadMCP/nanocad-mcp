using System.Text.Json;
using System.Text.Json.Nodes;
using Multicad;
using Multicad.Mc3D;
using NanoCAD.Contracts;

namespace NanoCADBridge;

// Explicit engineering replacement only. This proof does not classify opaque
// native storage or mutate CAD. It recognizes the selected sketch's one exact
// retired reverse link, on actual SDK-substituted source parents only.
internal static class ModelSketchFrameRetirement440
{
    internal sealed record Input(string native_id,string? sdk_parent_id,string? sdk_host_feature_or_parent_id,string? substituted_source_id);
    internal sealed record Source(string native_id,string raw_handle,string owner_document_id,string[] owner_path);
    internal sealed record Before(string sketch_handle,string[] retired_ids,string[] raw_dependencies,string[] parent_dependencies,
        Input[] inputs,Source[] sources,JsonElement stored);
    internal sealed record Comparison(JsonElement before_design,JsonElement after_design,JsonElement before_body,JsonElement after_body,string[] retired_source_ids);
    static readonly StringComparer Ids=StringComparer.OrdinalIgnoreCase;
    static void Need([System.Diagnostics.CodeAnalysis.DoesNotReturnIf(false)]bool ok,string message)=>ModelDatumPlaneState353.Need(ok,"FRAME_RETIREMENT_UNPROVED",message);
    static string Key(McObjectId id)=>id.ToString();
    static string? Text(JsonNode? value)=>value?.GetValue<string>();
    static string[] Strings(JsonElement value)=>value.EnumerateArray().Select(row=>row.GetString()!).ToArray();
    static JsonObject Copy(JsonElement value)=>JsonNode.Parse(value.GetRawText())!.AsObject();
    static bool Exact(JsonNode? a,JsonNode? b)=>a?.ToJsonString()==b?.ToJsonString();
    internal static string[] FrameIds(JsonElement selected)=>new[]{"point_ref","axis_ref","axis_ref2"}
        .Select(role=>selected.GetProperty("frame_references").GetProperty(role).GetProperty("native_id"))
        .Where(id=>id.ValueKind==JsonValueKind.String).Select(id=>id.GetString()!).Distinct(Ids).ToArray();
    static JsonElement Node(JsonElement nodes,string sketch)=>nodes.EnumerateArray().Single(row=>row.GetProperty("mc_id").GetString()==sketch);
    internal static Before Capture(Mc3dSolid body,PlanarSketch sketch,JsonElement selected,JsonElement nodes,
        McObjectId[] contents,ModelSketchConstraintMetadata379.Binding[] bindings,Action cancel)
    {
        var node=Node(nodes,Key(sketch.ID));var raw=Strings(node.GetProperty("dependencies"));var parents=Strings(node.GetProperty("stable_dependencies"));
        var retired=FrameIds(selected).Append(Key(sketch.PlanarEntityID)).Distinct(Ids).ToArray();
        Need(retired.All(id=>raw.Contains(id,Ids)),"Each retired frame/support identity must be an actual current raw dependency of this selected sketch.");
        var sketchBinding=bindings.SingleOrDefault(row=>Ids.Equals(row.native_id,Key(sketch.ID)));
        Need(sketchBinding?.raw_handle!=null&&sketchBinding.owner_document_id==Key(body.DbEntity.Document.ID),"The selected sketch lacks its exact current resident ID/owner/handle roundtrip.");
        var inputs=new List<Input>();var sources=new List<Source>();
        foreach(string text in retired)
        {
            cancel();var id=new McObjectId(Guid.Parse(text));var parent=id.GetParentId(false);var creator=id.GetHostFeatureOrParentId(false);
            // The SDK's actual collapsed inventory decides whether substitution
            // happened. ParentId alone cannot manufacture a source dependency.
            string? mapped=parents.Contains(text,Ids)?text:
                !creator.IsNull&&creator!=body.ID&&parents.Contains(Key(creator),Ids)?Key(creator):
                !parent.IsNull&&parents.Contains(Key(parent),Ids)?Key(parent):null;
            inputs.Add(new(text,parent.IsNull?null:Key(parent),creator.IsNull?null:Key(creator),mapped));if(mapped==null)continue;
            var source=new McObjectId(Guid.Parse(mapped));var native=source.GetObjectOfType<HistoryItem>();
            bool owned=source==body.ID||contents.Contains(source)&&native!=null&&native.ID==source&&native.HostSolidID==body.ID&&native.DbEntity.Document?.ID==body.DbEntity.Document.ID;
            var binding=bindings.SingleOrDefault(row=>Ids.Equals(row.native_id,mapped));
            if(!owned||binding==null||!binding.proved||binding.raw_handle==null||binding.owner_document_id!=Key(body.DbEntity.Document.ID))continue;
            if(!sources.Any(row=>Ids.Equals(row.native_id,mapped)))sources.Add(new(mapped,binding.raw_handle,binding.owner_document_id,binding.owner_path));
        }
        var rows=inputs.ToArray();var sourceRows=sources.OrderBy(row=>row.native_id,Ids).ToArray();
        var stored=Wire.Element(new{sketch_id=Key(sketch.ID),sketch_handle=sketchBinding!.raw_handle,retired_ids=retired,
            raw_dependencies=raw,parent_dependencies=parents,inputs=rows,proved_resident_sources=sourceRows,
            rule="actual_GetDependsOn_true_false_with_observed_SDK_parent_or_creating_feature_and_exact_resident_source_binding; no_inferred_creator_or_cache"});
        new ModelInventoryBudget(cancel).Add(stored);
        return new(sketchBinding.raw_handle!,retired,raw,parents,rows,sourceRows,stored);
    }
    internal static bool EmptyFrame(JsonElement selected)=>new[]{"point_ref","axis_ref","axis_ref2"}.All(role=>
    {
        var reference=selected.GetProperty("frame_references").GetProperty(role);
        return reference.GetProperty("is_empty").GetBoolean()&&reference.GetProperty("native_id").ValueKind==JsonValueKind.Null&&reference.GetProperty("complex_form").GetString()=="empty";
    });
    static bool RemoveOne(JsonArray prior,JsonArray actual,Func<JsonNode?,bool> target)
    {
        var old=prior.Where(target).ToArray();var current=actual.Where(target).ToArray();
        if(old.Length==0&&current.Length==0)return false;
        if(old.Length==1&&current.Length==1&&Exact(old[0],current[0]))return false; // retained edge: no normalization
        Need(old.Length==1&&current.Length==0,"A retired source reactor must be one exact previously observed selected-sketch link, removed without duplication or substitution.");
        prior.Remove(old[0]);return true;
    }
    static JsonObject Metadata(JsonObject design,string id)=>design["construction"]!.AsArray().Single(row=>Text(row?["id"])==id)!["native_metadata"]!.AsObject();
    static JsonObject? Owned(JsonObject metadata,string handle)
    {
        var records=metadata["owned"]!.AsArray().Where(row=>Ids.Equals(Text(row?["common"]?["handle"]),handle)).ToArray();
        Need(records.Length<=1,"A retired source handle is ambiguous inside the actual body-owned inventory.");return records.SingleOrDefault()?.AsObject();
    }
    static void MirrorBodyOwnedRecord(ModelSketchSupportRecoveryState440.Snapshot before,ModelSketchSupportRecoveryState440.Snapshot after,
        JsonObject prior,JsonObject current,Source source,JsonObject oldRecord,JsonObject newRecord,string sketchHandle)
    {
        JsonObject? View(JsonObject design,ModelSketchSupportRecoveryState440.Snapshot snapshot)
        {
            var rows=design["construction"]!.AsArray().Where(row=>Text(row?["id"])==Key(snapshot.Body.ID)).ToArray();
            Need(rows.Length<=1,"The duplicate body-construction inventory is ambiguous.");if(rows.Length==0)return null;
            var binding=snapshot.Storage.SingleOrDefault(row=>Ids.Equals(row.native_id,Key(snapshot.Body.ID)));
            Need(binding!=null&&binding.proved&&binding.owner_document_id==Key(snapshot.Owner.ID)&&
                binding.raw_handle==Validation.NormalizeHandle(snapshot.Representation.Handle.ToString())&&Text(rows[0]?["owner_document_id"])==Key(snapshot.Owner.ID),
                "The duplicate construction view lacks the exact selected body/owner/representation binding.");
            var native=rows[0]!["native_metadata"]!.AsObject();
            Need(Text(native["scope"])=="exact_resident_DWG_owned_metadata"&&
                Text(native["metadata"]?["root"]?["type"])==snapshot.BodyMetadata.GetProperty("root").GetProperty("type").GetString(),
                "The duplicate construction view is not the exact selected body metadata inventory.");
            var record=Owned(native["metadata"]!.AsObject(),source.raw_handle);
            Need(record!=null,"The duplicate body inventory lacks the already-proved exact retired source record.");return record;
        }
        var oldCopy=View(prior,before);var newCopy=View(current,after);if(oldCopy==null&&newCopy==null)return;
        // Prove the entire observed record in each duplicate before changing
        // only its already-proved one reactor. No array-index/source inference,
        // root-reactor normalization or payload/other-record waiver applies.
        Need(oldCopy!=null&&newCopy!=null&&Exact(oldCopy,oldRecord)&&Exact(newCopy,newRecord),
            "The duplicate source-owned records do not exactly match the independently captured before/after body-owned records.");
        Need(RemoveOne(oldCopy["common"]!["reactors"]!.AsArray(),newCopy["common"]!["reactors"]!.AsArray(),row=>Ids.Equals(Text(row),sketchHandle)),
            "The duplicate body view does not contain the same exact selected-sketch reactor retirement.");
    }
    internal static Comparison Normalize(ModelSketchSupportRecoveryState440.Snapshot before,ModelSketchSupportRecoveryState440.Snapshot after,
        ModelSketchSupportRecoveryState440.Scope scope,JsonElement prior,JsonElement current,Action cancel)
    {
        var proof=before.Retirement;Need(proof!=null,"Explicit target-frame replacement requires its observed native retirement proof before mutation.");
        var sketchBinding=after.Storage.SingleOrDefault(row=>Ids.Equals(row.native_id,Key(scope.Sketch)));
        Need(sketchBinding?.raw_handle==proof.sketch_handle&&sketchBinding.owner_document_id==Key(after.Owner.ID),"The actual selected-sketch ID/owner/handle changed during explicit frame replacement.");
        Need(EmptyFrame(after.Design.GetProperty("selected_sketch")),"Native support assignment did not retire all three old frame bindings; no clearing fallback is permitted.");
        var actualNode=Node(after.Design.GetProperty("nodes"),Key(scope.Sketch));
        var afterRaw=Strings(actualNode.GetProperty("dependencies"));var afterParents=Strings(actualNode.GetProperty("stable_dependencies"));
        Need(!proof.retired_ids.Any(id=>afterRaw.Contains(id,Ids)),"An explicitly retired frame/support identity is still an actual selected-sketch dependency.");
        var a=Copy(prior);var b=Copy(current);var oldBody=Copy(before.BodyMetadata);var newBody=Copy(after.BodyMetadata);var admitted=new List<string>();
        // Only this explicit typed field is replaced. All local geometry,
        // controls/selectors/drivers and other sketch observations stay exact.
        a["selected_sketch"]!.AsObject().Remove("frame_references");b["selected_sketch"]!.AsObject().Remove("frame_references");
        foreach(var source in proof.sources)
        {
            cancel();if(afterRaw.Contains(source.native_id,Ids)||afterParents.Contains(source.native_id,Ids))continue;
            // Any unretired raw source with the same observed SDK parent makes
            // retirement ambiguous, even if the collapsed API omitted it.
            bool residual=false;
            foreach(string text in afterRaw)
            {cancel();var id=new McObjectId(Guid.Parse(text));if((id.GetParentId(false).ToString()==source.native_id||id.GetHostFeatureOrParentId(false).ToString()==source.native_id)&&!proof.retired_ids.Contains(text,Ids)){residual=true;break;}}
            if(residual)continue;
            var binding=after.Storage.SingleOrDefault(row=>Ids.Equals(row.native_id,source.native_id));
            if(binding==null||!binding.proved||binding.raw_handle!=source.raw_handle||binding.owner_document_id!=source.owner_document_id||!binding.owner_path.SequenceEqual(source.owner_path))continue;
            // The body root itself is not construction storage. Its reverse
            // edge is not admitted by this bounded source-record adapter.
            if(source.native_id==Key(before.Body.ID))continue;
            var left=Metadata(a,source.native_id);var right=Metadata(b,source.native_id);
            if(Text(left["scope"])!="exact_resident_DWG_owned_metadata"||Text(right["scope"])!=Text(left["scope"]))continue;
            var x=left["reactor_observation"]!.AsObject();var y=right["reactor_observation"]!.AsObject();
            var oldRoot=left["metadata"]!["root"]!.AsObject();var newRoot=right["metadata"]!["root"]!.AsObject();
            bool removed=RemoveOne(oldRoot["reactors"]!.AsArray(),newRoot["reactors"]!.AsArray(),row=>Ids.Equals(Text(row),proof.sketch_handle));
            if(!removed)continue;
            Need(RemoveOne(x["native_reactor_ids"]!.AsArray(),y["native_reactor_ids"]!.AsArray(),row=>Ids.Equals(Text(row),Key(scope.Sketch))),"The removed drawing reactor lacks the exact selected-sketch native reactor identity.");
            Need(RemoveOne(x["drawing_reactors"]!.AsArray(),y["drawing_reactors"]!.AsArray(),row=>Ids.Equals(Text(row?["handle"]),proof.sketch_handle)&&Text(row?["native_target_id"])==Key(scope.Sketch)),"The removed persistent reactor lacks its exact selected-sketch ID/handle observation.");
            var oldRecord=Owned(oldBody,source.raw_handle);var newRecord=Owned(newBody,source.raw_handle);
            Need(oldRecord!=null&&newRecord!=null&&Text(oldRecord["common"]?["runtime_type"])==Text(oldRoot["type"])&&Text(newRecord["common"]?["runtime_type"])==Text(newRoot["type"]),"The retired source lacks matching exact body-owned record copies and native runtime identity.");
            MirrorBodyOwnedRecord(before,after,a,b,source,oldRecord,newRecord,proof.sketch_handle);
            Need(RemoveOne(oldRecord["common"]!["reactors"]!.AsArray(),newRecord["common"]!["reactors"]!.AsArray(),row=>Ids.Equals(Text(row),proof.sketch_handle)),"The selected-sketch reactor removal does not match its same source handle in body-owned storage.");
            admitted.Add(source.native_id);
        }
        return new(Wire.Element(a),Wire.Element(b),Wire.Element(oldBody),Wire.Element(newBody),admitted.ToArray());
    }
}
