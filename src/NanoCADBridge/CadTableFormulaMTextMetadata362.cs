using System.Text.Json;
using System.Text.Json.Nodes;
using NanoCAD.Contracts;

namespace NanoCADBridge;

// Compare actual MText carrier metadata separately from its generated display
// cache. The owning Table editors verify every stored cell/input/formula/format
// and topology. Only selected cell Fields can enter/leave a carrier's graph;
// unrelated dictionaries, pointer entries, XData and reactors stay exact.
internal static class CadTableFormulaMTextMetadata362
{
    static void Need(bool value,string message)
    {if(!value)throw new DomainException("NATIVE_TABLE_MISMATCH",message);}
    static bool Exact(JsonElement a,JsonElement b)=>a.GetRawText()==b.GetRawText();
    static string Id(JsonElement item)=>item.GetProperty("common").GetProperty("handle").GetString()!;
    static Dictionary<string,JsonElement> Records(JsonElement metadata)
    {
        var records=new Dictionary<string,JsonElement>(StringComparer.Ordinal);
        foreach(var item in metadata.GetProperty("owned").EnumerateArray())
            Need(records.TryAdd(Id(item),item),"Native Table storage metadata repeats an actual object identity.");
        return records;
    }
    static bool Empty(JsonElement common)=>common.GetProperty("xdata").ValueKind==JsonValueKind.Null&&common.GetProperty("reactors").GetArrayLength()==0;
    static HashSet<string> Containers(Dictionary<string,JsonElement> records,HashSet<string> fields,Action cancel)
    {
        var result=new HashSet<string>(StringComparer.Ordinal);bool changed;
        do
        {
            changed=false;
            foreach(var (id,record) in records)
            {
                cancel();if(result.Contains(id)||!record.TryGetProperty("entries",out var entries)||entries.GetArrayLength()==0)continue;
                var common=record.GetProperty("common");if(common.GetProperty("xdata").ValueKind!=JsonValueKind.Null||common.GetProperty("extension_handle").ValueKind!=JsonValueKind.Null||
                    !NativeTableSelectedFieldStorage364.ReactorsMatchOwningDictionary(record,records,cancel))continue;
                bool only=true;
                foreach(var edge in entries.EnumerateArray())
                {
                    cancel();string child=edge.GetProperty("handle").GetString()!;
                    if(!edge.GetProperty("owned").GetBoolean()||edge.GetProperty("owner_handle").GetString()!=id||
                        !records.TryGetValue(child,out var owned)||owned.GetProperty("common").GetProperty("owner_handle").GetString()!=id||
                        !fields.Contains(child)&&!result.Contains(child)){only=false;break;}
                }
                if(only){result.Add(id);changed=true;}
            }
        }while(changed);
        return result;
    }
    static bool Extension(JsonElement extension,HashSet<string> selectedContainers)=>extension.ValueKind==JsonValueKind.Null||selectedContainers.Contains(extension.GetString()!);
    static void Role(JsonElement record,CadTableEditingReader.Snapshot snapshot,Action cancel)
    {
        var common=record.GetProperty("common");var role=record.GetProperty("table_cell_mtext_owner");
        Need(common.GetProperty("runtime_type").GetString()=="Teigha.DatabaseServices.MText"&&
            role.GetProperty("table_handle").GetString()==Validation.NormalizeHandle(snapshot.Identity.Split(':')[0])&&
            role.GetProperty("block_handle").GetString()==common.GetProperty("owner_handle").GetString(),
            "Native MText carrier does not match its actual Table/layout owner.");
        var id=role.GetProperty("field_handle");var slots=new HashSet<(int Row,int Column)>();
        foreach(var slot in role.GetProperty("cell_bindings").EnumerateArray())
        {
            cancel();int row=slot.GetProperty("row").GetInt32(),column=slot.GetProperty("column").GetInt32();
            Need(id.ValueKind==JsonValueKind.String&&row>=0&&column>=0&&row<snapshot.Rows&&column<snapshot.Columns&&slots.Add((row,column)),
                "Native MText carrier repeats or misplaces an actual cell Field binding.");
            var field=snapshot.Values[checked(row*snapshot.Columns+column)]?.native_formula_field;
            Need(field!=null&&field.handle==id.GetString()&&slot.GetProperty("field_handle").GetString()==field.handle,
                "Native MText default Field is outside its actual cell formula snapshot.");
        }
        Need(id.ValueKind==JsonValueKind.Null?slots.Count==0:
            slots.Count>0&&slots.Count==snapshot.Values.Count(value=>value?.native_formula_field?.handle==id.GetString()),
            "Native MText carrier does not expose every matching Table Field slot.");
    }
    internal static void Verify(JsonElement before,JsonElement after,CadTableEditingReader.Snapshot old,CadTableEditingReader.Snapshot current,
        string[] selectedBefore,string[] selectedAfter,Action cancel)
    {
        var prior=Records(before);var actual=Records(after);var oldFields=selectedBefore.ToHashSet(StringComparer.Ordinal);var newFields=selectedAfter.ToHashSet(StringComparer.Ordinal);
        var oldContainers=Containers(prior,oldFields,cancel);var newContainers=Containers(actual,newFields,cancel);
        foreach(var record in prior.Values.Where(value=>value.TryGetProperty("table_cell_mtext_owner",out _)))Role(record,old,cancel);
        foreach(var record in actual.Values.Where(value=>value.TryGetProperty("table_cell_mtext_owner",out _)))Role(record,current,cancel);
        foreach(var id in prior.Keys.Concat(actual.Keys).Distinct(StringComparer.Ordinal))
        {
            cancel();bool had=prior.TryGetValue(id,out var a)&&a.TryGetProperty("table_cell_mtext_owner",out _);
            bool has=actual.TryGetValue(id,out var b)&&b.TryGetProperty("table_cell_mtext_owner",out _);if(!had&&!has)continue;
            Need(!prior.ContainsKey(id)||had,"Native metadata changed an unrelated record into a Table MText carrier.");
            Need(!actual.ContainsKey(id)||has,"Native metadata changed a Table MText carrier into an unrelated record.");
            if(!had||!has)
            {
                var record=had?a:b;var common=record.GetProperty("common");var role=record.GetProperty("table_cell_mtext_owner");var field=role.GetProperty("field_handle");
                bool xdataAbsent=common.GetProperty("xdata").ValueKind==JsonValueKind.Null,reactorsEmpty=common.GetProperty("reactors").GetArrayLength()==0;
                bool extensionAllowed=Extension(common.GetProperty("extension_handle"),had?oldContainers:newContainers);
                bool fieldAllowed=field.ValueKind==JsonValueKind.Null||(had?oldFields:newFields).Contains(field.GetString()!);
                if(!xdataAbsent||!reactorsEmpty||!extensionAllowed||!fieldAllowed)
                    throw new DomainException("NATIVE_TABLE_MISMATCH","Native layout regeneration introduced/lost an MText carrier with retained unrelated metadata or an unselected Field.",false,
                        CadTableFormulaMTextDiagnostics363.Carrier(record,had?prior:actual,had?oldFields:newFields,had?oldContainers:newContainers,
                            had,xdataAbsent,reactorsEmpty,extensionAllowed,fieldAllowed,old,current,cancel));
                continue;
            }
            var ca=a.GetProperty("common");var cb=b.GetProperty("common");
            foreach(var property in ca.EnumerateObject())
            {
                cancel();if(property.Name=="extension_handle")continue;
                Need(cb.TryGetProperty(property.Name,out var value)&&Exact(property.Value,value),
                    "Native Table layout changed MText carrier identity/owner/XData or an unrelated reactor.");
            }
            Need(ca.EnumerateObject().Count()==cb.EnumerateObject().Count(),"Native MText carrier metadata observation shape changed.");
            var ea=ca.GetProperty("extension_handle");var eb=cb.GetProperty("extension_handle");
            Need(Exact(ea,eb)||Extension(ea,oldContainers)&&Extension(eb,newContainers),
                "Native cell formula editing replaced an MText extension containing unrelated data.");
            var ra=a.GetProperty("table_cell_mtext_owner");var rb=b.GetProperty("table_cell_mtext_owner");
            foreach(var property in ra.EnumerateObject())
            {
                cancel();if(property.Name is "field_handle" or "cell_bindings")continue;
                Need(rb.TryGetProperty(property.Name,out var value)&&Exact(property.Value,value),"Native MText carrier's actual Table/layout storage role changed.");
            }
            var fa=ra.GetProperty("field_handle");var fb=rb.GetProperty("field_handle");
            Need(Exact(fa,fb)||(fa.ValueKind==JsonValueKind.Null||oldFields.Contains(fa.GetString()!))&&
                (fb.ValueKind==JsonValueKind.Null||newFields.Contains(fb.GetString()!)),"Native Table layout rebound an unselected MText Field.");
        }
    }
    internal static JsonElement WithoutCarriers(JsonElement metadata)
    {
        var result=JsonNode.Parse(metadata.GetRawText())!.AsObject();var records=result["owned"]!.AsArray();
        for(int index=records.Count-1;index>=0;index--)
            if(records[index]!.AsObject().ContainsKey("table_cell_mtext_owner"))records.RemoveAt(index);
        return Wire.Element(result);
    }
}
