using System.Text.Json;
using System.Text.Json.Nodes;
using NanoCAD.Contracts;

namespace NanoCADBridge;

// TableContent's field slots are real membership edges, not dictionary
// entries. Adapt only the comparison input to the selected-Field classifier.
// Captured owner records retain their real type and field_bindings. The owning
// editors compare every retained Table scalar, formula, format and topology.
internal static class CadTableFormulaMetadata360
{
    static void Need(bool value,string message)
    {if(!value)throw new DomainException("NATIVE_TABLE_MISMATCH",message);}
    internal static JsonElement Comparable(JsonElement metadata,CadTableEditingReader.Snapshot snapshot,Action cancel)
    {
        var result=JsonNode.Parse(metadata.GetRawText())!.AsObject();var records=result["owned"]!.AsArray();
        foreach(var node in records)
        {
            cancel();var record=node!.AsObject();if(record["table_content_owner"] is not JsonObject owner)continue;
            Need(owner["table_handle"]!.GetValue<string>()==Validation.NormalizeHandle(snapshot.Identity.Split(':')[0])&&
                owner["rows"]!.GetValue<int>()==snapshot.Rows&&owner["columns"]!.GetValue<int>()==snapshot.Columns&&
                owner["table_style_handle"]!.GetValue<string>()==snapshot.Common.GetProperty("table_style_handle").GetString(),
                "Native TableContent observation does not match the retained Table snapshot.");
            var edges=new JsonArray();var seen=new HashSet<string>(StringComparer.Ordinal);var slots=new HashSet<(int Row,int Column)>();
            foreach(var slotNode in record["field_bindings"]!.AsArray())
            {
                cancel();var slot=slotNode!.AsObject();int row=slot["row"]!.GetValue<int>(),column=slot["column"]!.GetValue<int>();
                Need(row>=0&&column>=0&&row<snapshot.Rows&&column<snapshot.Columns&&slots.Add((row,column)),
                    "Native TableContent repeats or misplaces a formula Field slot.");
                var field=snapshot.Values[checked(row*snapshot.Columns+column)]?.native_formula_field;
                Need(field!=null&&field.handle==slot["handle"]!.GetValue<string>()&&field.owner_handle==slot["owner_handle"]!.GetValue<string>()&&
                    field.runtime_type==slot["runtime_type"]!.GetValue<string>()&&
                    slot["owned"]!.GetValue<bool>()==(field.owner_handle==record["common"]!["handle"]!.GetValue<string>()),
                    "Native TableContent field slot is outside its actual Table formula snapshot.");
                // One actual Field can bind multiple cells. Check every slot,
                // then emit its actual owner edge once for graph comparison.
                string handle=slot["handle"]!.GetValue<string>();if(!seen.Add(handle))continue;
                edges.Add(new JsonObject{["handle"]=handle,["owner_handle"]=slot["owner_handle"]!.GetValue<string>(),
                    ["runtime_type"]=slot["runtime_type"]!.GetValue<string>(),["owned"]=slot["owned"]!.GetValue<bool>()});
            }
            Need(slots.Count==snapshot.Values.Count(value=>value?.native_formula_field!=null),
                "Native TableContent does not retain every actual Table formula Field binding.");
            record.Remove("table_content_owner");record.Remove("field_bindings");
            // Null means this comparison node is not a dictionary. No native
            // dictionary hard-ownership flag has been invented or observed.
            record["dictionary_hard_owned"]=null;record["entries"]=edges;
        }
        return Wire.Element(result);
    }
}
