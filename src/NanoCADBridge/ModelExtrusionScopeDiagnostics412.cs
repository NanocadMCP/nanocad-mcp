using System.Text.Json;
using Multicad;
using Multicad.Mc3D;
using NanoCAD.Contracts;
using Db=Teigha.DatabaseServices;

namespace NanoCADBridge;

// Failure observations only. No alternate owner is admitted or rebound, and
// no native update/rebuild/registration is performed by this helper.
internal static class ModelExtrusionScopeDiagnostics412
{
    internal static JsonElement Describe(string requested,Mc3dSolid selected,McObjectId drawing,
        Db.ObjectId representation,IEnumerable<McObjectId> contents,DocumentState state,Db.Transaction tx,Action cancel)
    {
        var values=new Dictionary<string,object?>();var notes=new List<object>();
        string? Id(McObjectId id)=>id.IsNull?null:id.ToString();
        void Observe(string field,Func<object?> read)
        {
            cancel();
            try{values[field]=read();}
            catch(Exception error)when(error is not OperationCanceledException)
            {values[field]=null;notes.Add(new{field,error_type=error.GetType().Name,code=(error as DomainException)?.Error.code});}
        }
        object Membership(Mc3dSolid body,McObjectId feature)
        {
            var actual=ModelInventoryBudget.History(body,cancel);
            return new{count=actual.Count,contains_requested_feature=actual.Contains(feature),last_item_id=Id(body.LastPartItemID)};
        }
        object Dwg(McObjectId native)
        {
            bool converted=native.ToHandle(out long handle,out McObjectId reportedDocument);
            Db.ObjectId raw=Db.ObjectId.Null;if(handle!=0)state.Document.Database.TryGetObjectId(new Db.Handle(handle),out raw);
            bool resident=!raw.IsNull&&!raw.IsErased&&!raw.IsEffectivelyErased;
            var roundtrip=handle==0?McObjectId.Null:McObjectId.FromHandle(handle,drawing);
            var value=resident?tx.GetObject(raw,Db.OpenMode.ForRead):null;
            return new{conversion_returned=converted,converted_handle=handle==0?null:unchecked((ulong)handle).ToString("X"),
                // Installed ToHandle may leave its document output null. Keep
                // that raw observation separate from the verified drawing.
                converted_document_id=Id(reportedDocument),current_drawing_document_id=Id(drawing),
                current_drawing_roundtrip_id=Id(roundtrip),roundtrip_matches_native=roundtrip==native,
                resident_in_current_drawing=resident,resident_runtime_type=value?.GetType().FullName,
                same_selected_representation=resident&&raw==representation};
        }
        values["requested_feature_id"]=requested;values["selected_drawing_document_id"]=Id(drawing);
        values["selected_representation_handle"]=representation.Handle.ToString();
        Observe("selected_body_id",()=>Id(selected.ID));
        Observe("selected_owner_document_id",()=>Id(selected.DbEntity.Document.ID));
        Observe("selected_body_dwg_conversion",()=>Dwg(selected.ID));
        Observe("selected_history",()=>new{count=contents.Count(),contains_requested_feature=contents.Any(id=>id.ToString().Equals(requested,StringComparison.OrdinalIgnoreCase)),last_item_id=Id(selected.LastPartItemID)});
        if(Guid.TryParseExact(requested,"B",out var guid)&&guid!=Guid.Empty)
        {
            var exact=new McObjectId(guid);ExtrudeFeature? feature=null;bool matches=false;
            Observe("exact_feature_lookup",()=>
            {
                feature=exact.GetObjectOfType<ExtrudeFeature>();
                matches=feature!=null&&feature.ID==exact;
                return new{requested_native_id=Id(exact),typed_found=feature!=null,typed_feature_id=feature==null?null:Id(feature.ID),exact_typed_identity=matches};
            });
            if(feature!=null&&matches)
            {
                var found=feature;
                Observe("feature_owner_document_id",()=>Id(found.DbEntity.Document.ID));
                Observe("feature_host_solid_id",()=>Id(found.HostSolidID));
                Mc3dSolid? host=null;
                Observe("feature_host_lookup",()=>
                {
                    host=found.HostSolid;
                    return new{typed_found=host!=null,typed_host_id=host==null?null:Id(host.ID),matches_host_solid_id=host!=null&&host.ID==found.HostSolidID};
                });
                if(host!=null)
                {
                    var observed=host;
                    Observe("feature_host_owner_document_id",()=>Id(observed.DbEntity.Document.ID));
                    Observe("feature_host_dwg_conversion",()=>Dwg(observed.ID));
                    Observe("feature_host_history",()=>Membership(observed,exact));
                }
            }
        }
        else notes.Add(new{field="exact_feature_lookup",error_type="InvalidBracedGuid",code=(string?)null});
        values["diagnostic_scope"]="exact_requested_ID_and_observed_native_hosts_only; no_owner_substitution_or_history_repair";
        values["observation_notes"]=notes;return Wire.Element(values);
    }
}
