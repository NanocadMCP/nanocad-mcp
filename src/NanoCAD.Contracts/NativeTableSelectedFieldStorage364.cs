using System.Text.Json;

namespace NanoCAD.Contracts;

// Comparison admission for an already captured selected-Field container only.
// Nothing is removed from the native inventory or from stable common metadata.
// A typed dictionary/observed Field's one reactor on its actual hard-owning dictionary is an
// ownership link, proved by the unique reverse owned edge, rather than a name.
public static class NativeTableSelectedFieldStorage364
{
    public static bool ReactorsMatchOwningDictionary(JsonElement record,IReadOnlyDictionary<string,JsonElement> records,Action cancel)
    {
        cancel();var common=record.GetProperty("common");var reactors=common.GetProperty("reactors");
        if(reactors.GetArrayLength()==0)return true;
        if(reactors.GetArrayLength()!=1)return false;
        string? runtime=common.GetProperty("runtime_type").GetString();
        bool dictionary=runtime=="Teigha.DatabaseServices.DBDictionary"&&record.TryGetProperty("dictionary_hard_owned",out var hard)&&hard.ValueKind==JsonValueKind.True;
        bool field=runtime=="Teigha.DatabaseServices.Field"&&record.TryGetProperty("table_bound_field",out var definition)&&
            definition.GetProperty("handle").GetString()==common.GetProperty("handle").GetString()&&
            definition.GetProperty("owner_handle").GetString()==common.GetProperty("owner_handle").GetString()&&definition.GetProperty("runtime_type").GetString()==runtime;
        if(!dictionary&&!field)return false;
        string id=common.GetProperty("handle").GetString()!;
        var owner=common.GetProperty("owner_handle");
        if(owner.ValueKind!=JsonValueKind.String||reactors[0].ValueKind!=JsonValueKind.String||owner.GetString()==id||reactors[0].GetString()!=owner.GetString()||
            !records.TryGetValue(owner.GetString()!,out var parent))return false;
        var parentCommon=parent.GetProperty("common");
        if(parentCommon.GetProperty("runtime_type").GetString()!="Teigha.DatabaseServices.DBDictionary"||
            parentCommon.GetProperty("handle").GetString()!=owner.GetString()||
            !parent.TryGetProperty("dictionary_hard_owned",out var parentHard)||parentHard.ValueKind!=JsonValueKind.True||
            !parent.TryGetProperty("entries",out var entries))return false;
        int references=0;
        foreach(var edge in entries.EnumerateArray())
        {
            cancel();if(edge.GetProperty("handle").GetString()!=id)continue;
            if(!edge.GetProperty("owned").GetBoolean()||edge.GetProperty("owner_handle").GetString()!=owner.GetString()||
                edge.GetProperty("runtime_type").GetString()!=common.GetProperty("runtime_type").GetString())return false;
            references++;
        }
        return references==1;
    }
}
