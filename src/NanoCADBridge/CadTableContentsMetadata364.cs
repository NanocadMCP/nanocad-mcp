using System.Text.Json;
using System.Text.Json.Nodes;
using NanoCAD.Contracts;
using static NanoCADBridge.CadTableContentsReader364;

namespace NanoCADBridge;

// Fields are unselected here: their owner graph/settings remain exact. Only
// native slot indices may follow the explicit ordinary-content origin plan.
internal static class CadTableContentsMetadata364
{
    static void Need(bool value,string message,object? details=null)
    {if(!value)throw new DomainException("NATIVE_TABLE_MISMATCH",message,false,details);}
    static bool Exact(object? a,object? b)=>JsonSerializer.Serialize(a,Wire.Json)==JsonSerializer.Serialize(b,Wire.Json);
    static string Id(JsonElement record)=>record.GetProperty("common").GetProperty("handle").GetString()!;
    static Dictionary<string,JsonElement> Records(JsonElement metadata)
    {
        var result=new Dictionary<string,JsonElement>(StringComparer.Ordinal);
        foreach(var record in metadata.GetProperty("owned").EnumerateArray())Need(result.TryAdd(Id(record),record),"Indexed content storage repeats a native object identity.");return result;
    }
    static object Bindings(JsonElement role,Snapshot snapshot,int[][] origins,Action cancel)
    {
        var result=new List<(int Row,int Column,int Origin,string Field)>();var seen=new HashSet<(int,int,int)>();
        string? field=role.GetProperty("field_handle").GetString();
        foreach(var slot in role.GetProperty("cell_bindings").EnumerateArray())
        {
            cancel();int row=slot.GetProperty("row").GetInt32(),column=slot.GetProperty("column").GetInt32(),index=slot.GetProperty("content_index").GetInt32();
            Need(row>=0&&column>=0&&row<snapshot.Rows&&column<snapshot.Columns&&index>=0&&index<snapshot.Contents[checked(row*snapshot.Columns+column)].Length&&seen.Add((row,column,index)),
                "Native MText role repeats or misplaces an indexed Field slot.");
            var actual=snapshot.Contents[checked(row*snapshot.Columns+column)][index];
            Need(field!=null&&actual.field_handle==field&&slot.GetProperty("field_handle").GetString()==field,"Native MText role has no matching indexed Field binding.");
            result.Add((row,column,origins[checked(row*snapshot.Columns+column)][index],field!));
        }
        Need(field==null?result.Count==0:result.Count==snapshot.Contents.SelectMany(slots=>slots).Count(slot=>slot.field_handle==field),
            "Native MText role does not expose every matching indexed Field binding.");
        return result.OrderBy(slot=>slot.Row).ThenBy(slot=>slot.Column).ThenBy(slot=>slot.Origin).Select(slot=>new{row=slot.Row,column=slot.Column,origin=slot.Origin,field=slot.Field}).ToArray();
    }
    static JsonElement ComparableCarrier(JsonElement record,Snapshot snapshot,int[][] origins,Action cancel)
    {
        var common=record.GetProperty("common");var role=record.GetProperty("table_cell_mtext_owner");
        Need(common.GetProperty("runtime_type").GetString()=="Teigha.DatabaseServices.MText"&&role.GetProperty("table_handle").GetString()==snapshot.Identity.Split(':')[0]&&
            role.GetProperty("block_handle").GetString()==common.GetProperty("owner_handle").GetString(),"Actual indexed MText owner role differs from its Table/layout.");
        var result=JsonNode.Parse(record.GetRawText())!.AsObject();result.Remove("cached_contents");
        result["table_cell_mtext_owner"]!["cell_bindings"]=JsonSerializer.SerializeToNode(Bindings(role,snapshot,origins,cancel),Wire.Json);
        return Wire.Element(result);
    }
    internal static void Verify(Snapshot before,Snapshot after,int[][] origins,Action cancel)
    {
        var old=Records(before.Metadata);var current=Records(after.Metadata);
        var oldOrigins=before.Contents.Select(slots=>Enumerable.Range(0,slots.Length).ToArray()).ToArray();
        foreach(var property in before.Metadata.EnumerateObject())
        {cancel();if(property.Name=="owned")continue;Need(after.Metadata.TryGetProperty(property.Name,out var actual)&&Exact(property.Value,actual),"Indexed editing changed Table root owned metadata.");}
        Need(before.Metadata.EnumerateObject().Count()==after.Metadata.EnumerateObject().Count(),"Indexed Table metadata shape changed.");
        foreach(string id in old.Keys.Concat(current.Keys).Distinct(StringComparer.Ordinal))
        {
            cancel();bool had=old.TryGetValue(id,out var a),has=current.TryGetValue(id,out var b);
            bool oldCarrier=had&&a.TryGetProperty("table_cell_mtext_owner",out _),newCarrier=has&&b.TryGetProperty("table_cell_mtext_owner",out _);
            if(had&&has)
            {
                Need(oldCarrier==newCarrier,"Native indexed metadata changed an unrelated record into/out of an MText carrier.");
                Need(Exact(oldCarrier?ComparableCarrier(a,before,oldOrigins,cancel):a,newCarrier?ComparableCarrier(b,after,origins,cancel):b),
                    "Indexed editing changed unselected Field/block ownership, readable settings, XData, reactors, siblings or dictionary order.",new{handle=id});
                continue;
            }
            var record=had?a:b;var common=record.GetProperty("common");
            // Same existing guard as the current formula carrier adapter with
            // no selected Fields: a carrier with real metadata/Field cannot leave.
            bool plain=(had?oldCarrier:newCarrier)&&common.GetProperty("xdata").ValueKind==JsonValueKind.Null&&common.GetProperty("reactors").GetArrayLength()==0&&
                common.GetProperty("extension_handle").ValueKind==JsonValueKind.Null&&record.GetProperty("table_cell_mtext_owner").GetProperty("field_handle").ValueKind==JsonValueKind.Null;
            Need(plain,"Native layout introduced/lost retained unrelated metadata or an unselected Field carrier.",new{handle=id,owner_handle=common.GetProperty("owner_handle"),runtime_type=common.GetProperty("runtime_type"),change=had?"lost":"introduced",xdata_kind=common.GetProperty("xdata").ValueKind.ToString(),reactor_count=common.GetProperty("reactors").GetArrayLength(),extension_handle=common.GetProperty("extension_handle")});
            _=ComparableCarrier(record,had?before:after,had?oldOrigins:origins,cancel);
        }
    }
}
