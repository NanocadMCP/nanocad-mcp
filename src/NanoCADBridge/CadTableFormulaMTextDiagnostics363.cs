using System.Security.Cryptography;
using System.Text.Json;
using NanoCAD.Contracts;

namespace NanoCADBridge;

// Failure details only. No native reads/writes, generated-data classification
// or reduction of the exact inventories used by the preservation comparer.
internal static class CadTableFormulaMTextDiagnostics363
{
    const int DetailsBytes=8192;
    static byte[] Bytes(object value)=>JsonSerializer.SerializeToUtf8Bytes(value,Wire.Json);
    static string Hash(byte[] bytes)=>Convert.ToHexString(SHA256.HashData(bytes));
    static object Small(JsonElement value,int cap,string reason)
    {
        var bytes=Bytes(value);
        if(bytes.Length<=cap)return value;
        return new{complete=false,omitted_reason=reason,observed_serialized_bytes=bytes.LongLength,sha256=Hash(bytes)};
    }
    static object Observation(JsonElement value,int sampleBytes,Action cancel)
    {
        var bytes=Bytes(value);var sample=new List<object>();int used=2;long count=value.ValueKind==JsonValueKind.Array?value.GetArrayLength():value.ValueKind==JsonValueKind.Null?0:1;
        long omitted=0,compacted=0;
        IEnumerable<JsonElement> Values()=>value.ValueKind==JsonValueKind.Array?value.EnumerateArray():value.ValueKind==JsonValueKind.Null?[]:[value];
        foreach(var item in Values())
        {
            cancel();var itemBytes=Bytes(item);var observed=Small(item,384,"large_diagnostic_item_omitted");if(itemBytes.Length>384)compacted++;
            int size=Bytes(observed).Length+1;if(used+size<=sampleBytes){sample.Add(observed);used+=size;}else omitted++;
        }
        return new{value_kind=value.ValueKind.ToString(),observed_count=count,sample,omitted_count=omitted,compacted_count=compacted,
            observed_serialized_bytes=bytes.LongLength,sha256=Hash(bytes),complete=omitted==0&&compacted==0};
    }
    static object Record(JsonElement value,HashSet<string> fields,HashSet<string> containers,Action cancel)
    {
        var common=value.GetProperty("common");string id=common.GetProperty("handle").GetString()!;
        var properties=new Dictionary<string,object?>
        {
            ["handle"]=id,["owner_handle"]=common.GetProperty("owner_handle"),
            ["runtime_type"]=Small(common.GetProperty("runtime_type"),384,"large_runtime_type_omitted"),
            ["extension_handle"]=common.GetProperty("extension_handle"),
            ["xdata"]=Observation(common.GetProperty("xdata"),384,cancel),
            ["reactors"]=Observation(common.GetProperty("reactors"),384,cancel),
            ["selected_formula_field"]=fields.Contains(id),["selected_field_only_container"]=containers.Contains(id)
        };
        if(value.TryGetProperty("entries",out var entries))
        {properties["dictionary_hard_owned"]=value.GetProperty("dictionary_hard_owned");properties["entries"]=Observation(entries,768,cancel);}
        if(value.TryGetProperty("data",out var data))properties["xrecord_data"]=Observation(data,768,cancel);
        if(value.TryGetProperty("table_formula_field",out var field))properties["readable_field"]=Small(field,768,"large_readable_field_omitted");
        return properties;
    }
    internal static JsonElement Carrier(JsonElement record,IReadOnlyDictionary<string,JsonElement> records,HashSet<string> fields,HashSet<string> containers,
        bool lost,bool xdataAbsent,bool reactorsEmpty,bool extensionAllowed,bool fieldAllowed,CadTableEditingReader.Snapshot before,CadTableEditingReader.Snapshot after,Action cancel)
    {
        cancel();var common=record.GetProperty("common");var role=record.GetProperty("table_cell_mtext_owner");
        string carrier=common.GetProperty("handle").GetString()!;var extension=common.GetProperty("extension_handle");
        var pending=new Stack<string>();var visited=new HashSet<string>(StringComparer.Ordinal);var sample=new List<object>();long count=0,omitted=0,compacted=0,observedBytes=0;int used=2;
        if(extension.ValueKind==JsonValueKind.String)pending.Push(extension.GetString()!);
        using var graphHash=IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        while(pending.Count>0)
        {
            cancel();string id=pending.Pop();if(!visited.Add(id))continue;
            if(!records.TryGetValue(id,out var value))
            {var missing=Bytes(new{handle=id,record_present=false});graphHash.AppendData(missing);graphHash.AppendData(new byte[]{10});count++;observedBytes+=missing.Length+1;omitted++;continue;}
            var bytes=Bytes(value);graphHash.AppendData(bytes);graphHash.AppendData(new byte[]{10});count++;observedBytes+=bytes.LongLength+1;
            var item=Record(value,fields,containers,cancel);int size=Bytes(item).Length+1;
            if(size>1536){item=Small(value,0,"large_owned_record_omitted_from_diagnostics");size=Bytes(item).Length+1;compacted++;}
            if(used+size<=3072){sample.Add(item);used+=size;}else omitted++;
            if(value.TryGetProperty("entries",out var entries))
                foreach(var edge in entries.EnumerateArray())
                {
                    cancel();string child=edge.GetProperty("handle").GetString()!;
                    if(edge.GetProperty("owned").GetBoolean()&&edge.GetProperty("owner_handle").GetString()==id&&
                        records.TryGetValue(child,out var owned)&&owned.GetProperty("common").GetProperty("owner_handle").GetString()==id)pending.Push(child);
                }
            var childExtension=value.GetProperty("common").GetProperty("extension_handle");if(childExtension.ValueKind==JsonValueKind.String)pending.Push(childExtension.GetString()!);
        }
        var graph=Wire.Element(new{observed_count=count,sample=sample.ToArray(),omitted_count=omitted,
            compacted_count=compacted,observed_serialized_bytes=observedBytes,ordered_sha256=Convert.ToHexString(graphHash.GetHashAndReset()),
            record_samples_complete=omitted==0&&compacted==0,complete=false,scope="diagnostic_record_fields_only; full_owned_record_bytes_hashed; complete_verification_inventory_unchanged"});
        // Obtain the ordered hash once; fallback must retain this same graph
        // fingerprint, not reset/claim an empty graph after diagnostic overflow.
        JsonElement Build(bool compact)=>Wire.Element(new{
            table_handle=role.GetProperty("table_handle"),carrier_handle=carrier,carrier_owner_handle=common.GetProperty("owner_handle"),
            carrier_runtime_type=Small(common.GetProperty("runtime_type"),384,"large_carrier_runtime_type_omitted"),
            layout_block_handle=role.GetProperty("block_handle"),carrier_change=lost?"lost":"introduced",default_field_handle=role.GetProperty("field_handle"),
            guards=new{xdata_absent=xdataAbsent,reactors_empty=reactorsEmpty,extension_null_or_selected_field_only=extensionAllowed,default_field_absent_or_selected=fieldAllowed},
            carrier_xdata_kind=common.GetProperty("xdata").ValueKind.ToString(),carrier_reactor_count=common.GetProperty("reactors").GetArrayLength(),
            cell_binding_count=role.GetProperty("cell_bindings").GetArrayLength(),
            carrier_xdata=compact?Small(common.GetProperty("xdata"),0,"carrier_xdata_sample_omitted_to_retain_failure_essentials"):Observation(common.GetProperty("xdata"),768,cancel),
            carrier_reactors=compact?Small(common.GetProperty("reactors"),0,"carrier_reactor_sample_omitted_to_retain_failure_essentials"):Observation(common.GetProperty("reactors"),768,cancel),
            extension_handle=extension,native_cell_bindings=compact?Small(role.GetProperty("cell_bindings"),0,"cell_binding_sample_omitted_to_retain_failure_essentials"):Observation(role.GetProperty("cell_bindings"),768,cancel),
            selected_field_handles=Observation(Wire.Element(fields.OrderBy(id=>id,StringComparer.Ordinal).ToArray()),compact?128:512,cancel),
            owned_extension_graph=compact?new{complete=false,omitted_reason="owned_graph_sample_omitted_to_retain_failure_essentials",observed_count=count,observed_serialized_bytes=observedBytes,ordered_sha256=graph.GetProperty("ordered_sha256").GetString()}:(object)graph,
            cached_contents=Small(record.GetProperty("cached_contents"),0,"generated_display_cache_not_used_as_ownership_or_data_loss_proof"),
            before_table_token=before.Token,after_table_token=after.Token,diagnostic_byte_budget=DetailsBytes,
            scope="bounded_actual_carrier_failure_predicates_and_owned_graph_diagnostics; exact_verification_unchanged; no_generated_metadata_waiver_or_rollback_claim"});
        var result=Build(false);if(Bytes(result).Length>DetailsBytes)result=Build(true);
        if(Bytes(result).Length>DetailsBytes)throw new InvalidOperationException("Bounded Table MText failure details exceed the serialized-byte budget.");
        return result;
    }
}
