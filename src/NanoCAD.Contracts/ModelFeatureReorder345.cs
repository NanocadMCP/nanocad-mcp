using System.Text.Json;

namespace NanoCAD.Contracts;

public sealed record ModelFeatureReorderSpec345(string[] Targets,string FeatureId,string PredecessorId,
    string ExpectedCurrentPredecessorId,string ExpectedLastItemId,string? ExpectedResultingLastItemId=null):EditSpec("reorder_model_feature",Targets);
public sealed record ModelFeatureOrderChange345(string mc_id,string? before,string? after);
public sealed record ModelFeatureReorderChange345(int operation_index,EntityRef source,EntityRef body_ref,
    string body_mc_id,string feature_id,string predecessor_id,string old_predecessor_id,string last_item_id,
    bool source_representation_replaced,ModelFeatureOrderChange345[] order_changes);
public sealed record ModelFeatureReorderOutput345(string operation_id,string status,bool replayed,
    EntityRef[] modified,EntityRef[] erased,EntityRef[] created,CopyMapping[] copies,string undo_history,
    ModelFeatureReorderChange345[] model_feature_reorders);

public static class ModelFeatureReorderValidation345
{
    static DomainException Invalid(string path,string message)=>new("INVALID_ARGUMENT",message,false,new{path});
    static string Id(JsonElement item,string key,string path)
    {
        if(!item.TryGetProperty(key,out var value)||value.ValueKind!=JsonValueKind.String)
            throw Invalid(path+"."+key,"Read the current nonempty native history ID.");
        string text;try{text=value.GetString()!;}catch(InvalidOperationException){throw Invalid(path+"."+key,"Expected valid Unicode.");}
        if(!Guid.TryParseExact(text,"B",out var id)||id==Guid.Empty)
            throw Invalid(path+"."+key,"Read the current nonempty native history ID.");
        return id.ToString("B").ToUpperInvariant();
    }
    public static EditSpec Parse(JsonElement item,string path,string[] handles)
    {
        if(item.ValueKind!=JsonValueKind.Object||handles.Length!=1)
            throw Invalid(path,"Select exactly one existing native Model Space part.");
        string[] allowed=["operation","handles","feature_id","predecessor_id","expected_current_predecessor_id","expected_last_item_id","expected_resulting_last_item_id"];
        var seen=new HashSet<string>(StringComparer.Ordinal);
        foreach(var field in item.EnumerateObject())
            if(!allowed.Contains(field.Name)||!seen.Add(field.Name))throw Invalid(path+"."+field.Name,"Unknown or duplicate field.");
        if(!item.TryGetProperty("operation",out var operation)||operation.ValueKind!=JsonValueKind.String||!operation.ValueEquals("reorder_model_feature"))
            throw Invalid(path+".operation","Use reorder_model_feature.");
        string feature=Id(item,"feature_id",path),predecessor=Id(item,"predecessor_id",path),before=Id(item,"expected_current_predecessor_id",path);
        if(feature==predecessor||feature==before)throw Invalid(path,"A feature cannot be its own predecessor.");
        if(predecessor==before)throw Invalid(path+".predecessor_id","Request a different native predecessor.");
        return new ModelFeatureReorderSpec345(handles,feature,predecessor,before,Id(item,"expected_last_item_id",path),
            item.TryGetProperty("expected_resulting_last_item_id",out _)?Id(item,"expected_resulting_last_item_id",path):null);
    }
    public static void Batch(IReadOnlyList<EditSpec> edits)
    {
        if(edits.Any(e=>e is ModelFeatureReorderSpec345)&&edits.Count!=1)
            throw new DomainException("INVALID_OPERATION_ORDER","Native feature reorder must be the sole operation in its request.");
    }
}

// Operates only on observed native links. It never writes a graph or infers
// dependency independence; the resident SDK TryOnly call determines eligibility.
public static class ModelFeatureReorderPlanning345
{
    const long MaxBytes=8L*1024*1024;
    static void Need(bool ok,string message){if(!ok)throw new DomainException("MODEL_ORDER_UNAVAILABLE",message);}
    public static string[] FeatureOrder(ModelSequenceLink[] history,string[] featureIds,string last,Action? cancel=null)
    {
        long bytes=2;var nodes=new Dictionary<string,ModelSequenceLink>(StringComparer.OrdinalIgnoreCase);
        foreach(var link in history)
        {
            cancel?.Invoke();bytes+=JsonSerializer.SerializeToUtf8Bytes(link,Wire.Json).LongLength+1;
            if(bytes>MaxBytes)throw new DomainException("RESPONSE_LIMIT_EXCEEDED","Native ordering inventory exceeds its actual8MiB serialized budget.");
            Need(nodes.TryAdd(link.Id,link),"Native history contains duplicate identities.");
        }
        var features=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach(string id in featureIds){cancel?.Invoke();Need(features.Add(id)&&nodes.ContainsKey(id),"Feature identity is absent or duplicated in native history.");}
        Need(features.Count>0,"Native forming-feature inventory is empty.");
        var roots=new List<string>();var memo=new Dictionary<string,string?>(StringComparer.OrdinalIgnoreCase);
        foreach(string id in featureIds)
        {
            cancel?.Invoke();var seen=new HashSet<string>(StringComparer.OrdinalIgnoreCase){id};var path=new List<string>();
            string? current=nodes[id].Previous,previousFeature=null;
            while(current!=null)
            {
                cancel?.Invoke();Need(seen.Add(current)&&nodes.ContainsKey(current),"Native predecessor inventory is incomplete or cyclic.");
                if(features.Contains(current)){previousFeature=current;break;}
                if(memo.TryGetValue(current,out previousFeature))break;
                path.Add(current);current=nodes[current].Previous;
            }
            foreach(string entry in path){cancel?.Invoke();memo[entry]=previousFeature;}
            if(previousFeature==null)roots.Add(id);
        }
        Need(roots.Count==1,"Native features must form one complete retained part sequence.");
        return ModelFeatureSequence.FromHistory(history,featureIds,roots[0],last,cancel);
    }
    public static string[] MoveAfter(string[] before,string feature,string predecessor,Action? cancel=null,bool allowTerminal=false)
    {
        var seen=new HashSet<string>(StringComparer.OrdinalIgnoreCase);var result=new List<string>();
        int selected=-1,anchor=-1;long bytes=2;
        for(int index=0;index<before.Length;index++)
        {
            cancel?.Invoke();string id=before[index];Need(seen.Add(id),"Native feature sequence has duplicate identities.");
            bytes+=JsonSerializer.SerializeToUtf8Bytes(id,Wire.Json).LongLength+1;
            if(bytes>MaxBytes)throw new DomainException("RESPONSE_LIMIT_EXCEEDED","Native ordering plan exceeds its actual8MiB serialized budget.");
            if(id.Equals(feature,StringComparison.OrdinalIgnoreCase))selected=index;
            if(id.Equals(predecessor,StringComparison.OrdinalIgnoreCase))anchor=index;
        }
        Need(selected>0&&selected<before.Length&&anchor>=0&&anchor<before.Length&&selected!=anchor&&
            (allowTerminal||selected<before.Length-1&&anchor<before.Length-1),
            "Keep the base fixed. Terminal-changing order needs an explicit resulting terminal; otherwise keep the terminal and End-Of-Part fixed.");
        foreach(string id in before)
        {
            cancel?.Invoke();if(id.Equals(feature,StringComparison.OrdinalIgnoreCase))continue;
            result.Add(id);if(id.Equals(predecessor,StringComparison.OrdinalIgnoreCase))result.Add(feature);
        }
        Need(!result.SequenceEqual(before,StringComparer.OrdinalIgnoreCase),"The feature already follows this feature in the native sequence.");
        return result.ToArray();
    }
}
