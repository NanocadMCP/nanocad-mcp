using System.Text.Json;
using System.Text.Json.Nodes;
using NanoCAD.Contracts;
using Teigha.Colors;
using Teigha.DatabaseServices;

namespace NanoCADBridge;

internal static class CadAppearanceProperties
{
    private static void Need(bool value,string message)
    {if(!value)throw new DomainException("NATIVE_PROPERTY_MISMATCH",message);}
    private static bool Same(object? expected,object? actual)=>ModelParameterSnapshot.Equivalent(
        JsonSerializer.SerializeToElement(expected,Wire.Json),JsonSerializer.SerializeToElement(actual,Wire.Json));
    private static object ReadColor(Color color)=>new{method=color.IsByLayer?"by_layer":color.IsByBlock?"by_block":color.IsByAci?"aci":color.IsByColor?"true_color":color.ColorMethod.ToString(),
        aci=color.IsByAci?(int?)color.ColorIndex:null,rgb=color.IsByColor?new[]{(int)color.Red,(int)color.Green,(int)color.Blue}:null};
    internal static StoredTransparency ReadTransparency(Transparency value)=>value.IsByLayer?new("by_layer",null,null):
        value.IsByBlock?new("by_block",null,null):value.IsByAlpha?new("explicit",value.Alpha,100d*(255-value.Alpha)/255):new("unknown",null,null);
    internal static string ReadDescription(LayerTableRecord layer)
    {
        string value=layer.Description;
        if(JsonSerializer.SerializeToUtf8Bytes(value,Wire.Json).Length>Wire.PayloadBudget-8192)
            throw new DomainException("RESPONSE_LIMIT_EXCEEDED","Stored layer description exceeds the actual serialized-byte budget; no text truncation occurred.");
        return value;
    }
    private static Transparency NativeTransparency(TransparencyChange change)=>change.Mode switch{
        "explicit" when change.Alpha is {} alpha=>new Transparency(alpha),
        "by_layer"=>new Transparency(TransparencyMethod.ByLayer),"by_block"=>new Transparency(TransparencyMethod.ByBlock),
        _=>throw new DomainException("INVALID_ARGUMENT","Unsupported native transparency mode.")};
    private static object ColorExpected(object before,RgbColor? rgb)=>rgb==null?before:new{method="true_color",aci=(int?)null,rgb=rgb.Channels()};
    internal static void Apply(Entity entity,RgbColor? rgb,TransparencyChange? transparency)
    {
        if(rgb==null&&transparency==null)return;
        using var originalColor=entity.Color;var beforeColor=ReadColor(originalColor);var beforeTransparency=ReadTransparency(entity.Transparency);
        var id=entity.ObjectId;var owner=entity.OwnerId;var layer=entity.LayerId;var linetype=entity.LinetypeId;
        var weight=entity.LineWeight;var visible=entity.Visible;double scale=entity.LinetypeScale;
        if(rgb is {} selected){using var native=Color.FromRgb(selected.Red,selected.Green,selected.Blue);entity.Color=native;}
        if(transparency!=null)entity.Transparency=NativeTransparency(transparency);
        using var actualColor=entity.Color;
        Need(Same(ColorExpected(beforeColor,rgb),ReadColor(actualColor))&&Same(transparency==null?beforeTransparency:AppearanceValidation.Expected(transparency),ReadTransparency(entity.Transparency))&&
            entity.ObjectId==id&&entity.OwnerId==owner&&entity.LayerId==layer&&entity.LinetypeId==linetype&&entity.LineWeight==weight&&entity.Visible==visible&&entity.LinetypeScale==scale,
            "Native entity appearance setter did not retain requested RGB/transparency or changed unselected root properties/ownership.");
    }
    internal static void Apply(LayerTableRecord layer,RgbColor? rgb,TransparencyChange? transparency,string? description)
    {
        if(rgb==null&&transparency==null&&description==null)return;
        using var originalColor=layer.Color;var beforeColor=ReadColor(originalColor);var beforeTransparency=ReadTransparency(layer.Transparency);
        string beforeDescription=layer.Description,name=layer.Name;var id=layer.ObjectId;var owner=layer.OwnerId;var linetype=layer.LinetypeObjectId;var weight=layer.LineWeight;
        bool off=layer.IsOff,frozen=layer.IsFrozen,locked=layer.IsLocked,plottable=layer.IsPlottable;
        if(rgb is {} selected){using var native=Color.FromRgb(selected.Red,selected.Green,selected.Blue);layer.Color=native;}
        if(transparency!=null)layer.Transparency=NativeTransparency(transparency);
        if(description!=null)layer.Description=description;
        using var actualColor=layer.Color;
        Need(Same(ColorExpected(beforeColor,rgb),ReadColor(actualColor))&&Same(transparency==null?beforeTransparency:AppearanceValidation.Expected(transparency),ReadTransparency(layer.Transparency))&&
            layer.Description==(description??beforeDescription)&&layer.Name==name&&layer.ObjectId==id&&layer.OwnerId==owner&&layer.LinetypeObjectId==linetype&&layer.LineWeight==weight&&
            layer.IsOff==off&&layer.IsFrozen==frozen&&layer.IsLocked==locked&&layer.IsPlottable==plottable,
            "Native layer appearance setter did not retain requested RGB/transparency/description or changed unselected properties/ownership.");
    }
    // Existing block verification has a private raw transparency snapshot in
    // addition to common read fields. Use its native value wrapper; retain all
    // attributes. The redundant color-index field is normalized only after the
    // entity setter independently verifies the exact requested RGB/method.
    internal static JsonElement ExpectedBlock(JsonElement expected,RgbColor? rgb,TransparencyChange? transparency)
    {
        if(rgb==null&&transparency==null)return expected;
        var value=JsonNode.Parse(expected.GetRawText())!;var properties=value["state"]!["properties"]!;var stored=properties["stored"]!;var common=stored["common"]!;
        if(rgb is {} selected)
        {
            using var native=Color.FromRgb(selected.Red,selected.Green,selected.Blue);
            common["color"]=JsonSerializer.SerializeToNode(ReadColor(native),Wire.Json);
        }
        if(transparency!=null)
        {
            var native=NativeTransparency(transparency);common["transparency"]=JsonSerializer.SerializeToNode(AppearanceValidation.Expected(transparency),Wire.Json);
            stored["transparency"]=JsonSerializer.SerializeToNode(new{alpha=native.Alpha,by_alpha=native.IsByAlpha,by_layer=native.IsByLayer,by_block=native.IsByBlock},Wire.Json);
        }
        return Wire.Element(value);
    }
    internal static JsonElement NormalizeRequestedBlockColorIndex(JsonElement expected,RgbColor? rgb,BlockReference actual)
    {
        if(rgb==null)return expected;
        using var color=actual.Color;
        Need(Same(new{method="true_color",aci=(int?)null,rgb=rgb.Channels()},ReadColor(color)),
            "Block RGB must be independently verified before normalizing its derived native color index.");
        var value=JsonNode.Parse(expected.GetRawText())!;
        value["state"]!["properties"]!["color"]=(int)actual.ColorIndex;
        return Wire.Element(value);
    }
}
