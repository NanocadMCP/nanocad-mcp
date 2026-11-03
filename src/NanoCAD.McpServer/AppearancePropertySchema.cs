using System.Text.Json.Nodes;

namespace NanoCAD.McpServer;

internal static class AppearancePropertySchema
{
    private static JsonObject Object(JsonObject fields,params string[] required)=>new(){["type"]="object",["additionalProperties"]=false,
        ["properties"]=fields,["required"]=new JsonArray(required.Select(name=>(JsonNode?)JsonValue.Create(name)).ToArray())};
    private static JsonObject Nullable(JsonObject schema)=>new(){["anyOf"]=new JsonArray(schema,new JsonObject{["type"]="null"})};
    internal static JsonObject Transparency(bool layer=false)
    {
        JsonObject Mode(string value)=>new(){["type"]="string",["const"]=value};
        var choices=new JsonArray(Object(new JsonObject{["mode"]=Mode("explicit"),["percent"]=new JsonObject{
            ["type"]="number",["minimum"]=0,["maximum"]=100}},"mode","percent"));
        if(!layer){choices.Add(Object(new JsonObject{["mode"]=Mode("by_layer")},"mode"));choices.Add(Object(new JsonObject{["mode"]=Mode("by_block")},"mode"));}
        return new(){["oneOf"]=choices,["description"]="Stored native transparency. Explicit percent0..100:0 opaque,100 clear; rounded to the nearest alpha byte with midpoint away from zero. Readback publishes exact alpha and its quantized percentage. " +
            (layer?"A layer requires its own explicit transparency.":"by_layer/by_block restores that native inheritance mode; effective display also depends on layer/block and drawing/view settings.")};
    }
    internal static JsonObject ReadTransparency()=>Object(new JsonObject{
        ["mode"]=new JsonObject{["type"]="string",["enum"]=new JsonArray("explicit","by_layer","by_block","unknown")},
        ["alpha"]=Nullable(new JsonObject{["type"]="integer",["minimum"]=0,["maximum"]=255}),
        ["percent"]=Nullable(new JsonObject{["type"]="number",["minimum"]=0,["maximum"]=100})},"mode","alpha","percent");
    // Extend the existing complete properties object, retaining old ACI fields.
    internal static void ExtendInput(JsonObject schema,bool layer=false)
    {
        var fields=schema["properties"]!.AsObject();
        fields["color_rgb"]=new JsonObject{["type"]="array",["minItems"]=3,["maxItems"]=3,
            ["items"]=new JsonObject{["type"]="integer",["minimum"]=0,["maximum"]=255},
            ["description"]="Explicit native RGB true color [red,green,blue]. Choose this or color_aci; assignment replaces the previous color method. Unspecified color is preserved."};
        fields["transparency"]=Transparency(layer);
        if(layer)fields["description"]=new JsonObject{["type"]="string",["pattern"]="^[^\\u0000]*$",
            ["description"]="Stored native layer description. Empty string clears it. Valid Unicode, no NUL; real serialized-byte budget applies, without a hidden character-count ceiling."};
        var exclusive=new JsonObject{["not"]=new JsonObject{["required"]=new JsonArray("color_aci","color_rgb")}};
        if(schema["allOf"] is not JsonArray constraints){constraints=new JsonArray();schema["allOf"]=constraints;}
        constraints.Add(exclusive);
    }
    internal static void ExtendRead(JsonObject schema,bool layer=false)
    {
        // Optional additive fields keep earlier bridge responses compatible.
        var fields=schema["properties"]!.AsObject();fields["transparency"]=ReadTransparency();
        if(layer)fields["description"]=new JsonObject{["type"]="string"};
    }
}
