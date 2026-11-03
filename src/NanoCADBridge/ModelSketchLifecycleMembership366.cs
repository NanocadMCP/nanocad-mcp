using System.Security.Cryptography;
using System.Text.Json;
using Multicad;
using Multicad.DatabaseServices;
using Multicad.Mc3D;
using NanoCAD.Contracts;
using Db=Teigha.DatabaseServices;

namespace NanoCADBridge;

// Selected projection identity proof only. Diagnostic native observations never
// expand the admitted set or reinterpret a started unknown write as success.
internal static class ModelSketchLifecycleMembership366
{
    static string Key(McObjectId id)=>id.ToString();
    static void Need([System.Diagnostics.CodeAnalysis.DoesNotReturnIf(false)] bool value,string message)
    {if(!value)throw new DomainException("OBJECT_IDENTITY_MISMATCH",message);}
    internal static McObjectId[] ProjectionSources(ModelSketchLifecycleReader362.Snapshot after,Action cancel)
    {
        var document=after.Sketch.DbEntity.Document;
        Need(document!=null&&!document.ID.IsNull,"The selected sketch's current actual native owner document is unavailable.");
        var bindings=after.Projections.Bindings.EnumerateArray().ToDictionary(row=>row.GetProperty("id").GetString()!,StringComparer.OrdinalIgnoreCase);
        var actual=new HashSet<McObjectId>();
        foreach(var id in after.Projections.Ids)
        {
            cancel();var projection=id.GetObjectOfType<ProjectionSketchEnt>();
            Need(projection!=null&&projection.ID==id&&projection.HostSketchID==after.Sketch.ID&&!projection.SrcObjectID.IsNull&&
                projection.DbEntity.Document!=null&&projection.DbEntity.Document.ID==document.ID&&
                projection.PlanarObjectID==after.Sketch.PlanarEntityID&&projection.Status.ToString()=="Ok"&&!projection.IsSuppressed,
                "A selected projection changed its actual native identity, parent, source or support after capture.");
            Need(bindings.TryGetValue(Key(id),out var binding)&&binding.GetProperty("host_sketch_id").GetString()==Key(after.Sketch.ID)&&
                binding.GetProperty("source_object_id").GetString()==Key(projection.SrcObjectID),
                "A current projection no longer matches its captured actual source binding.");
            actual.Add(projection.SrcObjectID);
        }
        Need(bindings.Keys.ToHashSet(StringComparer.OrdinalIgnoreCase).SetEquals(after.Projections.Ids.Select(Key))&&
            actual.Select(Key).ToHashSet(StringComparer.OrdinalIgnoreCase).SetEquals(after.Projections.Sources.Select(source=>source.id).Concat(after.Projections.NativeReferences.Select(Key))),
            "Selected projection/source identities differ from the exact captured native inventory.");
        return actual.ToArray();
    }
    static string Hash(object value)=>Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(value,Wire.Json)));
    internal static object Describe(string scope,McObjectId[] before,McObjectId[] after,HashSet<McObjectId> allowed,
        McObjectId body,McObjectId sketch,McObjectId? profile,McObjectId? feature,McDocument drawing,McDocument owner,
        DocumentState state,Db.Transaction tx,Action cancel)
    {
        var old=before.ToHashSet();var current=after.ToHashSet();var added=current.Except(old).OrderBy(Key,StringComparer.Ordinal).ToArray();
        var missing=old.Except(current).OrderBy(Key,StringComparer.Ordinal).ToArray();var unexpected=added.Where(id=>!allowed.Contains(id)).ToArray();
        var rows=new List<object>();int used=2048;
        foreach(var id in unexpected.Concat(missing).Concat(added).Distinct())
        {
            cancel();string? runtime=null,lookup=null,nativeOwner=null,host=null,parent=null,consumer=null,projectionSource=null,projectionParent=null,sweepProfile=null;
            string? handle=null,rawRuntime=null,rawOwner=null,diagnosticError=null;bool? drawingRoundtrip=null,ownerRoundtrip=null;
            try
            {
                runtime=id.QueryObjectType()?.FullName;var history=id.GetObjectOfType<HistoryItem>();
                if(history!=null){lookup=Key(history.ID);nativeOwner=history.DbEntity.Document?.ID.ToString();host=history.HostSolidID.IsNull?null:Key(history.HostSolidID);}
                else if(id.GetObjectOfType<Multicad.DatabaseServices.StandardObjects.DbGeometry>() is {} geometry)
                {lookup=Key(geometry.ID);nativeOwner=geometry.DbEntity.Document?.ID.ToString();}
                if(id.GetObjectOfType<ProjectionSketchEnt>() is {} projection)
                {projectionSource=projection.SrcObjectID.IsNull?null:Key(projection.SrcObjectID);projectionParent=projection.HostSketchID.IsNull?null:Key(projection.HostSketchID);}
                if(id.GetObjectOfType<SketchProfile>() is {} selected)
                {parent=selected.ParentSketchID.IsNull?null:Key(selected.ParentSketchID);consumer=selected.Dependent3dFeatureID.IsNull?null:Key(selected.Dependent3dFeatureID);}
                if(id.GetObjectOfType<ExtrudeAlongPathFeature>() is {} sweep)sweepProfile=sweep.ProfileID.IsNull?null:Key(sweep.ProfileID);
                if(id.Handle!=0&&state.Document.Database.TryGetObjectId(new Db.Handle(id.Handle),out var raw)&&!raw.IsNull&&!raw.IsErased&&!raw.IsEffectivelyErased)
                {
                    var resident=tx.GetObject(raw,Db.OpenMode.ForRead);handle=raw.Handle.ToString();rawRuntime=resident.GetType().FullName;rawOwner=resident.OwnerId.IsNull?null:resident.OwnerId.Handle.ToString();
                    drawingRoundtrip=McObjectId.FromHandle(raw.Handle.Value,drawing.ID)==id;ownerRoundtrip=McObjectId.FromHandle(raw.Handle.Value,owner.ID)==id;
                }
            }
            catch(OperationCanceledException){throw;}
            catch(Exception error){diagnosticError=error.GetType().Name;}
            var row=new{mc_id=Key(id),added=added.Contains(id),missing=missing.Contains(id),admitted=allowed.Contains(id),runtime_type=runtime,
                typed_lookup_id=lookup,native_document_id=nativeOwner,host_solid_id=host,parent_sketch_id=parent,dependent_feature_id=consumer,
                projection_source_id=projectionSource,projection_host_sketch_id=projectionParent,sweep_profile_id=sweepProfile,
                resident_handle=handle,resident_runtime_type=rawRuntime,resident_owner_handle=rawOwner,drawing_handle_roundtrip=drawingRoundtrip,
                owner_handle_roundtrip=ownerRoundtrip,diagnostic_read_error=diagnosticError};
            int size=JsonSerializer.SerializeToUtf8Bytes(row,Wire.Json).Length+1;if(used+size>3840)break;rows.Add(row);used+=size;
        }
        var addedIds=added.Select(Key).ToArray();var missingIds=missing.Select(Key).ToArray();var unexpectedIds=unexpected.Select(Key).ToArray();
        int Sample(string[] ids,int budget)
        {
            int count=0,bytes=2;foreach(string id in ids){cancel();int size=JsonSerializer.SerializeToUtf8Bytes(id,Wire.Json).Length+1;if(bytes+size>budget)break;bytes+=size;count++;}return count;
        }
        // Full captured IDs determine counts/hashes; native detail and ID samples
        // are bounded by real bytes without imposing a model-size limit.
        int addedSample=Sample(addedIds,512),missingSample=Sample(missingIds,512),unexpectedSample=Sample(unexpectedIds,1024);
        object Result()=>new{scope,body_mc_id=Key(body),sketch_id=Key(sketch),profile_id=profile.HasValue?Key(profile.Value):null,feature_id=feature.HasValue?Key(feature.Value):null,
            drawing_document_id=Key(drawing.ID),actual_owner_document_id=Key(owner.ID),before_count=before.Length,before_ids_sha256=Hash(before.Select(Key).ToArray()),
            after_count=after.Length,after_ids_sha256=Hash(after.Select(Key).ToArray()),allowed_count=allowed.Count,allowed_ids_sha256=Hash(allowed.Select(Key).OrderBy(id=>id,StringComparer.Ordinal).ToArray()),
            added_count=addedIds.Length,added_ids_sha256=Hash(addedIds),added_ids=addedIds.Take(addedSample).ToArray(),omitted_added_count=addedIds.Length-addedSample,
            missing_count=missingIds.Length,missing_ids_sha256=Hash(missingIds),missing_ids=missingIds.Take(missingSample).ToArray(),omitted_missing_count=missingIds.Length-missingSample,
            unexpected_count=unexpectedIds.Length,unexpected_ids_sha256=Hash(unexpectedIds),unexpected_ids=unexpectedIds.Take(unexpectedSample).ToArray(),omitted_unexpected_count=unexpectedIds.Length-unexpectedSample,
            native_observations=rows.ToArray(),omitted_native_observation_count=unexpected.Concat(missing).Concat(added).Distinct().Count()-rows.Count,
            diagnostic_scope="actual_selected_native_IDs_and_current_typed_owner_bindings; diagnostic_only_no_membership_waiver",native_complete=false};
        var result=Result();
        while(JsonSerializer.SerializeToUtf8Bytes(result,Wire.Json).Length>4096)
        {
            cancel();if(rows.Count>0)rows.RemoveAt(rows.Count-1);
            else if(addedSample>0)addedSample--;
            else if(missingSample>0)missingSample--;
            else if(unexpectedSample>0)unexpectedSample--;
            else throw new DomainException("RESPONSE_LIMIT_EXCEEDED","Native lifecycle membership diagnostic identity header exceeds its actual byte budget.");
            result=Result();
        }
        return result;
    }
}
