using System.Text.Json;

namespace NanoCAD.Contracts;

public sealed record NativeExplode386(string[] Targets,string SourceMode):EditSpec("explode_native",Targets);
public sealed record NativeExplodeObject386(EntityRef @ref,string runtime_type,string owner_handle,string layer);
public sealed record NativeExplodeChange386(int operation_index,EntityRef source,string source_mode,bool source_erased,
    NativeExplodeObject386[] generated,string[] warnings);
public sealed record NativeExplodeOutput386(string operation_id,string status,bool replayed,EntityRef[] modified,
    EntityRef[] erased,EntityRef[] created,CopyMapping[] copies,string undo_history,NativeExplodeChange386[] native_explode_changes);

public static class NativeExplodeValidation386
{
    public static NativeExplode386 Parse(JsonElement value,string path,string[] handles)
    {
        var seen=new HashSet<string>(StringComparer.Ordinal);
        foreach(var property in value.EnumerateObject())
            if(!seen.Add(property.Name)||property.Name is not ("operation" or "handles" or "source_mode"))
                throw new DomainException("INVALID_ARGUMENT",path+"."+property.Name+": Unknown or duplicate field.");
        if(!value.TryGetProperty("source_mode",out var mode)||mode.ValueKind!=JsonValueKind.String||mode.GetString() is not ("retain" or "replace"))
            throw new DomainException("INVALID_ARGUMENT",path+".source_mode: Explicit retain or replace required.");
        return new(handles,mode.GetString()!);
    }
    public static void Batch(IEnumerable<EditSpec> operations)
    {
        var all=operations.ToArray();if(!all.Any(value=>value is NativeExplode386))return;
        if(all.Any(value=>value is not NativeExplode386)||all.SelectMany(value=>value.Handles).Distinct(StringComparer.Ordinal).Count()!=all.Sum(value=>value.Handles.Length))
            throw new DomainException("INVALID_OPERATION_ORDER","Native explode requires a dedicated batch of distinct existing sources; use returned generated handles only in a later request.");
    }
}
