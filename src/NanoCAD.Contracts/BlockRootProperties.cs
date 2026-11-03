using System.Text.Json;
using System.Text.Json.Nodes;

namespace NanoCAD.Contracts;

public sealed record BlockPropertySymbols(string? LayerName,string? LayerHandle,string? LinetypeName,string? LinetypeHandle);
public static class BlockRootProperties
{
    // Build the expected full snapshot, replacing ONLY requested root fields.
    // Attribute geometry, ownership, text and stored appearance remain intact.
    public static JsonElement Expected(JsonElement before,PropertyChanges changes,BlockPropertySymbols symbols)
    {
        var result=JsonNode.Parse(before.GetRawText())!;
        var properties=result["state"]!["properties"]!;
        var common=properties["stored"]!["common"]!;
        if(changes.Layer!=null)
        {
            if(symbols.LayerName==null||symbols.LayerHandle==null)throw new InvalidOperationException("Resolved layer identity required.");
            result["layer_name"]=symbols.LayerName;properties["layer"]=symbols.LayerHandle;
        }
        if(changes.ColorAci is { } aci)
        {
            properties["color"]=aci;
            common["color"]=new JsonObject{["method"]=aci==256?"by_layer":aci==0?"by_block":"aci",["aci"]=aci is >=1 and <=255?JsonValue.Create(aci):null,["rgb"]=null};
        }
        if(changes.Linetype!=null)
        {
            if(symbols.LinetypeName==null||symbols.LinetypeHandle==null)throw new InvalidOperationException("Resolved linetype identity required.");
            properties["linetype"]=symbols.LinetypeHandle;common["linetype"]=symbols.LinetypeName;
        }
        if(changes.LineweightHundredthMm is { } weight)
        {
            properties["lineweight"]=weight;
            common["lineweight"]=new JsonObject{["mode"]=weight switch{-1=>"by_layer",-2=>"by_block",-3=>"default",_=>"explicit"},["value_hundredth_mm"]=weight>=0?JsonValue.Create(weight):null,["native_value"]=weight};
        }
        if(changes.LinetypeScale is { } scale)
        {
            properties["stored"]!["LinetypeScale"] = scale;
            common["linetype_scale"] = scale;
        }
        if(changes.Visible is { } visible) common["visible"] = visible;
        return Wire.Element(result);
    }
}
