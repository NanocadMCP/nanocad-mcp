using System.Security.Cryptography;
using System.Text.Json;
using NanoCAD.Contracts;

namespace NanoCADBridge;

// This bounds error diagnostics only. Readable verification inventories and
// native admission/preservation checks are never compacted by this helper.
internal static class CadTableFormulaOwnershipDiagnostics360
{
    internal const int DetailsByteBudget=8192;
    static byte[] Bytes(object value)=>JsonSerializer.SerializeToUtf8Bytes(value,Wire.Json);
    static JsonElement Omitted(byte[] bytes,string reason)=>Wire.Element(new{complete=false,omitted_reason=reason,
        observed_serialized_bytes=bytes.LongLength,sha256=Convert.ToHexString(SHA256.HashData(bytes))});
    static JsonElement Item(object value,int budget,string reason)
    {var bytes=Bytes(value);return bytes.Length<=budget?Wire.Element(value):Omitted(bytes,reason);}
    sealed record Sequence(JsonElement[] sample,JsonElement? last_observation,long observed_count,long omitted_count,long compacted_count,
        long observed_serialized_bytes,bool complete,string ordered_sha256);
    static Sequence Observe(IEnumerable<object> values,Action cancel,int sampleBudget=1536)
    {
        using var hash=IncrementalHash.CreateHash(HashAlgorithmName.SHA256);var sample=new List<JsonElement>();
        long count=0,omitted=0,compacted=0,observedBytes=0;int sampleBytes=2;JsonElement? last=null;
        foreach(var value in values)
        {
            cancel();var bytes=Bytes(value);hash.AppendData(bytes);hash.AppendData(new byte[]{10});count++;observedBytes+=bytes.LongLength+1;
            var item=bytes.Length<=512?Wire.Element(value):Omitted(bytes,"large_diagnostic_observation_omitted");
            if(bytes.Length>512)compacted++;
            int size=Bytes(item).Length+1;last=item;
            if(sampleBytes+size<=sampleBudget){sample.Add(item);sampleBytes+=size;}else omitted++;
        }
        return new(sample.ToArray(),omitted>0?last:null,count,omitted,compacted,observedBytes,omitted==0&&compacted==0,
            Convert.ToHexString(hash.GetHashAndReset()));
    }
    static Sequence Summary(Sequence value)=>value with{sample=[],last_observation=null,omitted_count=value.observed_count,complete=value.observed_count==0};
    internal static JsonElement Create(string table,string field,string? owner,string? runtimeType,IEnumerable<object> path,
        JsonElement definition,IEnumerable<object> bindings,object? mismatch,Action cancel)
    {
        cancel();var owners=Observe(path,cancel);var cells=Observe(bindings,cancel);var fieldBytes=Bytes(definition);
        var fieldDefinition=fieldBytes.Length<=1536?definition:Omitted(fieldBytes,"large_readable_field_definition_omitted_from_diagnostics");
        var mismatchDefinition=mismatch==null?JsonSerializer.SerializeToElement<object?>(null,Wire.Json):Item(mismatch,1536,"large_owner_mismatch_diagnostic_omitted");
        string? type=runtimeType;
        bool typeComplete=runtimeType==null||Bytes(runtimeType).Length<=512;
        if(!typeComplete)type=runtimeType![..Math.Min(128,runtimeType!.Length)];
        JsonElement Build(Sequence ownerPath,Sequence nativeBindings,JsonElement readableField)=>Wire.Element(new{
            table_handle=table,field_handle=field,owner_handle=owner,owner_runtime_type=type,owner_runtime_type_complete=typeComplete,
            owner_runtime_type_sha256=typeComplete?null:Convert.ToHexString(SHA256.HashData(Bytes(runtimeType!))),
            actual_owner_path=ownerPath,field_definition=readableField,native_cell_bindings=nativeBindings,owner_mismatch=mismatchDefinition,
            diagnostic_byte_budget=DetailsByteBudget,
            scope="bounded_read_only_actual_owner_path_and_native_binding; complete_verification_inventory_unchanged; no_rollback_claim"});
        var result=Build(owners,cells,fieldDefinition);
        if(Bytes(result).Length>DetailsByteBudget)
            result=Build(Summary(owners),Summary(cells),Omitted(fieldBytes,"readable_field_definition_omitted_to_retain_owner_failure_essentials"));
        // Native IDs and the capped runtime prefix are small; this final check
        // enforces actual serialized bytes if the shape ever changes later.
        if(Bytes(result).Length>DetailsByteBudget)throw new InvalidOperationException("Bounded native Table ownership diagnostics exceed their serialized byte budget.");
        return result;
    }
}
