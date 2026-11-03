using System.Text.Json;
using System.Text.Json.Nodes;
using NanoCAD.Contracts;
using static NanoCADBridge.CadTableContentsReader364;

namespace NanoCADBridge;

// Explicit delete scope. Every unselected identity/owned entry remains exact.
// Selected Field storage may retire; surviving selected storage remains in the
// actual inventory. This comparison never declares that it was erased/reused.
internal static class CadTableFieldDeletionMetadata379
{
    static bool Exact(object? a,object? b)=>JsonSerializer.Serialize(a,Wire.Json)==JsonSerializer.Serialize(b,Wire.Json);
    static void Need(bool value,string message,object? details=null)
    {if(!value)throw new DomainException("NATIVE_TABLE_MISMATCH",message,false,details);}
    static JsonElement Common(JsonElement record,HashSet<string> retired)
    {
        var result=JsonNode.Parse(record.GetProperty("common").GetRawText())!.AsObject();
        string? extension=result["extension_handle"]?.GetValue<string>();
        if(extension!=null&&retired.Contains(extension))result["extension_handle"]=null;
        return Wire.Element(result);
    }
    static bool Owned(JsonElement edge,string parent,IReadOnlyDictionary<string,JsonElement> records)
        =>edge.GetProperty("owned").GetBoolean()&&edge.GetProperty("owner_handle").GetString()==parent&&
          records.TryGetValue(edge.GetProperty("handle").GetString()!,out var child)&&child.GetProperty("common").GetProperty("owner_handle").GetString()==parent;
    static JsonElement Entries(JsonElement record,IReadOnlyDictionary<string,JsonElement> records,HashSet<string> retired,Action cancel)
    {
        var entries=new List<JsonElement>();string id=CadTableFieldDeletion379.Id(record);
        foreach(var edge in record.GetProperty("entries").EnumerateArray())
        {cancel();if(!retired.Contains(edge.GetProperty("handle").GetString()!)||!Owned(edge,id,records))entries.Add(edge.Clone());}
        return Wire.Element(entries);
    }
    static JsonElement Carrier(JsonElement record,Snapshot snapshot,int[][] origins,HashSet<string> selected,HashSet<string> retired,Action cancel)
    {
        var common=record.GetProperty("common");var role=record.GetProperty("table_cell_mtext_owner");
        Need(common.GetProperty("runtime_type").GetString()=="Teigha.DatabaseServices.MText"&&
            role.GetProperty("table_handle").GetString()==snapshot.Identity.Split(':')[0]&&role.GetProperty("block_handle").GetString()==common.GetProperty("owner_handle").GetString(),
            "Actual native carrier identity/owner differs from its retained Table layout.");
        string? field=role.GetProperty("field_handle").GetString();var slots=new List<object>();var seen=new HashSet<(int,int,int)>();int count=0;
        foreach(var binding in role.GetProperty("cell_bindings").EnumerateArray())
        {
            cancel();int row=binding.GetProperty("row").GetInt32(),column=binding.GetProperty("column").GetInt32(),index=binding.GetProperty("content_index").GetInt32();
            Need(field!=null&&row>=0&&row<snapshot.Rows&&column>=0&&column<snapshot.Columns&&index>=0&&
                index<snapshot.Contents[checked(row*snapshot.Columns+column)].Length&&seen.Add((row,column,index)),"Native carrier repeats or misplaces a Field binding.");
            Need(snapshot.Contents[checked(row*snapshot.Columns+column)][index].field_handle==field&&binding.GetProperty("field_handle").GetString()==field,
                "Native carrier binding differs from the actual current indexed slot.");
            count++;if(!selected.Contains(field!))slots.Add(new{row,column,origin=origins[checked(row*snapshot.Columns+column)][index],field});
        }
        Need(field==null?count==0:count==snapshot.Contents.SelectMany(values=>values).Count(slot=>slot.field_handle==field),"Native carrier does not expose every actual matching Field binding.");
        var result=JsonNode.Parse(record.GetRawText())!.AsObject();result.Remove("cached_contents");
        result["common"]=JsonNode.Parse(Common(record,retired).GetRawText());
        result["table_cell_mtext_owner"]!["cell_bindings"]=JsonSerializer.SerializeToNode(slots,Wire.Json);
        if(field!=null&&selected.Contains(field))result["table_cell_mtext_owner"]!["field_handle"]=null;
        return Wire.Element(result);
    }
    static bool Plain(JsonElement record)=>record.TryGetProperty("table_cell_mtext_owner",out var role)&&role.GetProperty("field_handle").ValueKind==JsonValueKind.Null&&
        record.GetProperty("common").GetProperty("xdata").ValueKind==JsonValueKind.Null&&record.GetProperty("common").GetProperty("reactors").GetArrayLength()==0&&
        record.GetProperty("common").GetProperty("extension_handle").ValueKind==JsonValueKind.Null;
    internal static void Verify(Snapshot before,Snapshot after,int[][] origins,CadTableFieldDeletion379.Selection selected,Action cancel)
    {
        var old=CadTableFieldDeletion379.Records(before,cancel);var current=CadTableFieldDeletion379.Records(after,cancel);
        var initial=before.Contents.Select(slots=>Enumerable.Range(0,slots.Length).ToArray()).ToArray();
        foreach(string id in selected.Fields)
        {cancel();Need(after.Contents.SelectMany(slots=>slots).All(slot=>slot.field_handle!=id),"Native DeleteContent left/reassigned the selected Field in a retained slot.",new{field_handle=id});}
        foreach(var property in before.Metadata.EnumerateObject())
        {
            cancel();if(property.Name=="owned")continue;
            Need(after.Metadata.TryGetProperty(property.Name,out var actual),"Native deletion changed Table metadata observation shape.");
            if(property.Name=="extension")
            {
                bool oldSelected=property.Value.ValueKind==JsonValueKind.String&&selected.Retirable.Contains(property.Value.GetString()!);
                bool newSelected=actual.ValueKind==JsonValueKind.String&&selected.Retirable.Contains(actual.GetString()!);
                Need(Exact(property.Value,actual)||(oldSelected||property.Value.ValueKind==JsonValueKind.Null)&&(newSelected||actual.ValueKind==JsonValueKind.Null),"Native deletion replaced Table extension containing unrelated data.");
            }
            else Need(Exact(property.Value,actual),"Native deletion changed Table identity/owner/XData or unrelated persistent dependencies.",new{property=property.Name});
        }
        Need(before.Metadata.EnumerateObject().Count()==after.Metadata.EnumerateObject().Count(),"Native deletion changed Table metadata shape.");
        foreach(string id in old.Keys.Concat(current.Keys).Distinct(StringComparer.Ordinal))
        {
            cancel();bool had=old.TryGetValue(id,out var prior),has=current.TryGetValue(id,out var actual);
            bool oldCarrier=had&&prior.TryGetProperty("table_cell_mtext_owner",out _),newCarrier=has&&actual.TryGetProperty("table_cell_mtext_owner",out _);
            if(!had||!has)
            {
                var record=had?prior:actual;
                Need(had&&(selected.Retirable.Contains(id)||selected.Carriers.Contains(id))||Plain(record),
                    "Native Field deletion introduced/lost an unselected Field, sibling or unrelated owned record.",new{handle=id,change=had?"lost":"introduced",runtime_type=record.GetProperty("common").GetProperty("runtime_type").GetString()});
                if(oldCarrier||newCarrier)_=Carrier(record,had?before:after,had?initial:origins,selected.Fields,selected.Retirable,cancel);
                continue;
            }
            Need(oldCarrier==newCarrier,"Native deletion changed an unrelated record into/out of an MText carrier.",new{handle=id});
            if(selected.Retirable.Contains(id))
            {
                // Native may retain unused selected-owned storage. It must not
                // acquire a different native class/owner or new ownership edge.
                var oldCommon=JsonNode.Parse(Common(prior,selected.Retirable).GetRawText())!.AsObject();var newCommon=JsonNode.Parse(Common(actual,selected.Retirable).GetRawText())!.AsObject();
                bool definition=selected.Definitions.Contains(id);
                if(definition)
                {
                    // VerifyRetirement checks every surviving selected tree node
                    // against the actual sole owning-dictionary reactor relation.
                    // Native cleanup may remove that ownership link or private
                    // selected bytes; no foreign dependency is admitted.
                    oldCommon.Remove("xdata");newCommon.Remove("xdata");oldCommon.Remove("reactors");newCommon.Remove("reactors");
                }
                Need(Exact(Wire.Element(oldCommon),Wire.Element(newCommon)),
                    "Native cleanup changed a surviving selected storage identity/owner/XData/dependency.",new{handle=id});
                if(prior.TryGetProperty("entries",out var oldEntries)&&actual.TryGetProperty("entries",out var entries))
                {
                    var retained=new List<JsonElement>();
                    foreach(var edge in oldEntries.EnumerateArray())
                    {cancel();if(current.ContainsKey(edge.GetProperty("handle").GetString()!))retained.Add(edge.Clone());}
                    Need(Exact(Wire.Element(retained),entries)&&Exact(prior.GetProperty("dictionary_hard_owned"),actual.GetProperty("dictionary_hard_owned")),
                        "Native cleanup introduced/reordered foreign data or changed selected storage ownership.",new{handle=id});
                }
                else Need(definition,"Native cleanup changed selected binding storage's observation kind.",new{handle=id});
                continue;
            }
            if(oldCarrier)
                Need(Exact(Carrier(prior,before,initial,selected.Fields,selected.Retirable,cancel),Carrier(actual,after,origins,selected.Fields,selected.Retirable,cancel)),
                    "Native cleanup changed an unselected carrier Field, owner, XData, dependency or dictionary path.",new{handle=id});
            else if(prior.TryGetProperty("entries",out _)&&actual.TryGetProperty("entries",out _))
                Need(Exact(Common(prior,selected.Retirable),Common(actual,selected.Retirable))&&Exact(prior.GetProperty("dictionary_hard_owned"),actual.GetProperty("dictionary_hard_owned"))&&
                    Exact(Entries(prior,old,selected.Retirable,cancel),Entries(actual,current,selected.Retirable,cancel)),"Native deletion changed unrelated dictionary data/entries/order/ownership.",new{handle=id});
            else Need(Exact(prior,actual),"Native deletion changed an unselected Field/settings/layout block or unrelated owned metadata.",new{handle=id});
        }
    }
}
