using System.Text.Json;

namespace NanoCAD.Contracts;

public sealed record NativeTableFormulaFieldRead358(string handle,string owner_handle,string runtime_type,
    string field_code,string object_reference_code,string evaluator_id,string format,string evaluation_options,string filing_options,
    bool is_text_field,bool has_fields,bool private_state_complete,string scope);

// Compare actual readable ownership graphs. Only native formula field records
// proved by the caller's selected cell bindings may enter/leave/change code.
public static class NativeTableFormulaMetadata358
{
    static DomainException Bad(string message)=>new("NATIVE_TABLE_MISMATCH",message);
    static bool Exact(JsonElement a,JsonElement b)=>a.GetRawText()==b.GetRawText();
    static string Id(JsonElement record)=>record.GetProperty("common").GetProperty("handle").GetString()!;
    static Dictionary<string,JsonElement> Records(JsonElement value,Action cancel)
    {
        var result=new Dictionary<string,JsonElement>(StringComparer.Ordinal);
        foreach(var record in value.GetProperty("owned").EnumerateArray())
        {cancel();if(!result.TryAdd(Id(record),record))throw Bad("Native table metadata repeats an actual ownership identity.");}
        return result;
    }
    static bool Formula(JsonElement record)=>record.TryGetProperty("table_formula_field",out _);
    static HashSet<string> Containers(Dictionary<string,JsonElement> records,HashSet<string> fields,Action cancel)
    {
        var result=new HashSet<string>(StringComparer.Ordinal);bool changed;
        do
        {
            changed=false;
            foreach(var (id,record) in records)
            {
                cancel();if(result.Contains(id)||!record.TryGetProperty("entries",out var entries)||entries.GetArrayLength()==0)continue;
                var common=record.GetProperty("common");
                if(common.GetProperty("extension_handle").ValueKind!=JsonValueKind.Null||common.GetProperty("xdata").ValueKind!=JsonValueKind.Null||!NativeTableSelectedFieldStorage364.ReactorsMatchOwningDictionary(record,records,cancel))continue;
                bool only=true;
                foreach(var entry in entries.EnumerateArray())
                {cancel();string child=entry.GetProperty("handle").GetString()!;if(!OwnedEdge(entry,id,records)||!fields.Contains(child)&&!result.Contains(child)){only=false;break;}}
                if(only){result.Add(id);changed=true;}
            }
        }while(changed);
        return result;
    }
    static bool OwnedEdge(JsonElement entry,string parent,Dictionary<string,JsonElement> records)
        =>entry.GetProperty("owned").GetBoolean()&&records.TryGetValue(entry.GetProperty("handle").GetString()!,out var child)&&
            child.GetProperty("common").GetProperty("owner_handle").GetString()==parent&&entry.GetProperty("owner_handle").GetString()==parent;
    static JsonElement Entries(JsonElement record,Dictionary<string,JsonElement> records,HashSet<string> authorized,Action cancel)
    {
        var result=new List<JsonElement>();
        foreach(var entry in record.GetProperty("entries").EnumerateArray())
        {cancel();if(!authorized.Contains(entry.GetProperty("handle").GetString()!)||!OwnedEdge(entry,Id(record),records))result.Add(entry.Clone());}
        return Wire.Element(result);
    }
    static void ExistingField(JsonElement before,JsonElement after,Action cancel)
    {
        if(!Exact(before.GetProperty("common"),after.GetProperty("common")))throw Bad("Native formula editing changed an existing Field identity, owner, XData, extension or unrelated reactor.");
        var old=before.GetProperty("table_formula_field");var current=after.GetProperty("table_formula_field");
        foreach(var property in old.EnumerateObject())
        {
            cancel();if(property.Name is "field_code" or "object_reference_code")continue;
            if(!current.TryGetProperty(property.Name,out var value)||!Exact(property.Value,value))throw Bad("Native formula editing changed unrequested readable Field settings or observation scope.");
        }
        if(old.EnumerateObject().Count()!=current.EnumerateObject().Count())throw Bad("Native formula Field observation shape changed.");
    }
    public static void Verify(JsonElement before,JsonElement after,IEnumerable<string> selectedBefore,IEnumerable<string> selectedAfter,Action cancel)
    {
        cancel();var oldFields=selectedBefore.ToHashSet(StringComparer.Ordinal);var newFields=selectedAfter.ToHashSet(StringComparer.Ordinal);
        var old=Records(before,cancel);var current=Records(after,cancel);
        foreach(string id in oldFields){cancel();if(!old.TryGetValue(id,out var item)||!Formula(item))throw Bad("Approved prior formula field is outside the actually observed table ownership graph.");}
        foreach(string id in newFields){cancel();if(!current.TryGetValue(id,out var item)||!Formula(item))throw Bad("Approved final formula field is outside the actually observed table ownership graph.");}
        var oldContainers=Containers(old,oldFields,cancel);var newContainers=Containers(current,newFields,cancel);
        var authorized=oldFields.Concat(newFields).Concat(oldContainers).Concat(newContainers).ToHashSet(StringComparer.Ordinal);
        foreach(var property in before.EnumerateObject())
        {
            cancel();if(property.Name is "owned" or "extension")continue;
            if(!after.TryGetProperty(property.Name,out var value)||!Exact(property.Value,value))throw Bad("Native formula editing changed the Table root identity/owner/XData or unrelated metadata.");
        }
        if(before.EnumerateObject().Count()!=after.EnumerateObject().Count())throw Bad("Native Table metadata root shape changed.");
        var oldExtension=before.GetProperty("extension");var newExtension=after.GetProperty("extension");
        if(!Exact(oldExtension,newExtension))
        {
            bool oldAllowed=oldExtension.ValueKind==JsonValueKind.Null||oldContainers.Contains(oldExtension.GetString()!);
            bool newAllowed=newExtension.ValueKind==JsonValueKind.Null||newContainers.Contains(newExtension.GetString()!);
            if(!oldAllowed||!newAllowed)throw Bad("Native formula editing replaced an extension dictionary containing unrelated data.");
        }
        foreach(var (id,prior) in old)
        {
            cancel();
            if(!current.TryGetValue(id,out var actual))
            {if(!oldFields.Contains(id)&&!oldContainers.Contains(id))throw Bad("Native formula editing lost a retained unrelated owned record.");continue;}
            if(Formula(prior)&&oldFields.Contains(id)&&newFields.Contains(id))ExistingField(prior,actual,cancel);
            else if(prior.TryGetProperty("entries",out _)&&actual.TryGetProperty("entries",out _))
            {
                if(!Exact(prior.GetProperty("common"),actual.GetProperty("common"))||
                   !Exact(prior.GetProperty("dictionary_hard_owned"),actual.GetProperty("dictionary_hard_owned"))||
                   !Exact(Entries(prior,old,authorized,cancel),Entries(actual,current,authorized,cancel)))
                    throw Bad("Native formula editing changed unrelated dictionary entries, order, hard ownership or metadata.");
            }
            else if(!Exact(prior,actual))throw Bad("Native formula editing changed an unselected native Field or unrelated owned record.");
        }
        foreach(var (id,actual) in current)
        {cancel();if(!old.ContainsKey(id)&&!newFields.Contains(id)&&!newContainers.Contains(id))throw Bad("Native formula editing introduced an unclassified owned record.");}
    }
}
