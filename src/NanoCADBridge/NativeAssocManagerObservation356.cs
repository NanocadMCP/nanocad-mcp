using System.Text.Json;
using NanoCAD.Contracts;
using Db=Teigha.DatabaseServices;

namespace NanoCADBridge;

// Opted in only by retained extrusion extent snapshots. The installed manager
// class exposes no instance state getter: do not initialize/audit/evaluate a
// network to turn an unavailable private inventory into an empty one.
internal static class NativeAssocManagerObservation356
{
    internal const string Warning="ASSOC_MANAGER_PRIVATE_STATE_UNOBSERVED: Exact resident manager identity/owner, readable owned dictionaries/Xrecords, XData and reactors remain checked together with actual body-owner MCS variables/dependencies and available sketch constraints. The SDK exposes no private manager-state inventory; private solver internals are not claimed completely observed or preserved. Extent setters do not edit that manager or initialize/evaluate its network.";
    internal const string Scope="exact_manager_anchor_and_readable_owned_metadata; actual_MCS_variables_dependencies_and_available_sketch_constraints_checked_separately; private_manager_internals_unobserved";
    internal static object Capture(Db.AssocManager manager,Db.DBObject root,Action cancel)
    {
        cancel();
        if(manager.GetType()!=typeof(Db.AssocManager)||manager.ObjectId.IsNull||manager.OwnerId.IsNull||manager.Database!=root.Database)
            throw new DomainException("UNSUPPORTED_OWNED_METADATA","The native association manager lacks an exact resident local owner identity.");
        // Common native ID/owner/XData/reactor/extension data is captured by the
        // owning walker. This marker deliberately supplies no invented graph.
        return new{adapter="resident_assoc_manager_anchor356",private_state_complete=false,observation_scope=Scope};
    }
    internal static NativeAssocManagerObservationRead356[] Describe(ModelExtrusionExtentsState351.Snapshot snapshot)
    {
        var values=new List<NativeAssocManagerObservationRead356>();
        void Add(JsonElement metadata,string subject)
        {
            foreach(var entry in metadata.GetProperty("owned").EnumerateArray())
            {
                if(!entry.TryGetProperty("assoc_manager_observation",out _))continue;
                var common=entry.GetProperty("common");
                values.Add(new(subject,snapshot.OwnerDocument,common.GetProperty("handle").GetString()!,common.GetProperty("owner_handle").GetString()!,
                    common.GetProperty("runtime_type").GetString()!,false,Scope));
            }
        }
        Add(snapshot.Metadata,"selected_body");
        foreach(var source in snapshot.Sources.EnumerateArray())Add(source.GetProperty("metadata"),"retained_source_handle:"+source.GetProperty("handle").GetString());
        return values.OrderBy(value=>value.subject_scope,StringComparer.Ordinal).ThenBy(value=>value.manager_handle,StringComparer.Ordinal).ToArray();
    }
    internal static string[] Warnings(IEnumerable<NativeAssocManagerObservationRead356> values)=>values.Any()?[Warning]:[];
    internal static string[] Warnings(JsonElement metadata)=>metadata.GetProperty("owned").EnumerateArray().Any(entry=>entry.TryGetProperty("assoc_manager_observation",out _))?[Warning]:[];
}
