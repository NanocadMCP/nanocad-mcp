using System.Text.Json;
using System.Text.Json.Nodes;
using static NanoCAD.Contracts.StructureWriteValidation;

namespace NanoCAD.Contracts;

// Useful reusable row-role appearance, retaining the existing scalar/grid types.
public static class TableStyleRoleDefaults365
{
    static int? Color(JsonElement input,string path)
    {
        if(!input.TryGetProperty("color_aci",out var value))return null;
        if(value.ValueKind!=JsonValueKind.Number||!value.TryGetInt32(out int aci)||aci is <0 or >256)
            throw Invalid(path+".color_aci","Use native ACI0 ByBlock,1..255 explicit or256 ByLayer.");
        return aci;
    }
    public static TableCellBackgroundChanges Background(JsonElement input,string path)
    {
        Shape(input,path,"mode","color_aci","color_rgb");string mode=Text(input,"mode",path,16);
        if(mode=="none"){Shape(input,path,"mode");return new(true);}
        if(mode!="color")throw Invalid(path+".mode","Select none or color.");
        var aci=Color(input,path);var rgb=AppearanceValidation.Parse(input,path).ColorRgb;
        if(aci==null&&rgb==null)throw Invalid(path,"Color background requires explicit ACI or RGB.");return new(false,aci,rgb);
    }
    public static TableGridFormatChanges[] Grids(JsonElement input,string path)
    {
        if(input.ValueKind!=JsonValueKind.Array||input.GetArrayLength()==0)throw Invalid(path,"Select at least one native role grid line.");
        var result=new List<TableGridFormatChanges>();var seen=new HashSet<string>(StringComparer.Ordinal);
        foreach(var item in input.EnumerateArray())
        {
            string at=path+"["+result.Count+"]";Shape(item,at,"line","visible","color_aci","color_rgb","lineweight_hundredth_mm");
            string line=Text(item,"line",at,32);
            if(!TableEditingValidation.GridLines.Contains(line,StringComparer.Ordinal)||!seen.Add(line))throw Invalid(at,"Select distinct actual native grid lines.");
            if(item.EnumerateObject().Count()==1)throw Invalid(at,"Select a grid appearance property.");
            bool? visible=null;
            if(item.TryGetProperty("visible",out var flag))visible=flag.ValueKind is JsonValueKind.True or JsonValueKind.False?flag.GetBoolean():throw Invalid(at+".visible","Expected boolean.");
            int? weight=null;
            if(item.TryGetProperty("lineweight_hundredth_mm",out var value))
            {
                if(value.ValueKind!=JsonValueKind.Number||!value.TryGetInt32(out int parsed)||!ModifyValidation.Lineweights.Contains(parsed))throw Invalid(at+".lineweight_hundredth_mm","Select a published native lineweight, including inheritance.");
                weight=parsed;
            }
            var aci=Color(item,at);var rgb=AppearanceValidation.Parse(item,at).ColorRgb;
            result.Add(new(line,visible,aci,rgb,weight,null));
        }
        return result.ToArray();
    }
    public static TableStyleColor337 ExpectedColor(int? aci,RgbColor? rgb)=>rgb is {} value?new("true_color",null,value.Channels()):
        new(aci==0?"by_block":aci==256?"by_layer":"aci",aci is 0 or 256?null:aci,null);
    public static JsonElement ExpectedGrids(JsonElement before,TableGridFormatChanges[]? selected)
    {
        var result=JsonNode.Parse(before.GetRawText())!.AsArray();
        foreach(var change in selected??Array.Empty<TableGridFormatChanges>())
        {
            var grid=result.Single(item=>item!["line"]!.GetValue<string>()==change.Line)!.AsObject();
            if(change.Visible is {} visible)grid["visible"]=visible;
            if(change.Lineweight is {} weight)grid["lineweight"]=weight;
            if(change.ColorAci!=null||change.ColorRgb!=null)grid["color"]=JsonSerializer.SerializeToNode(ExpectedColor(change.ColorAci,change.ColorRgb),Wire.Json);
        }
        return Wire.Element(result);
    }
}
