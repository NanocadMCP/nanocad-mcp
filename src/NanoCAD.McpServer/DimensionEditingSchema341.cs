using System.Text.Json.Nodes;
using NanoCAD.Contracts;

namespace NanoCAD.McpServer;

public static class DimensionEditingSchema341
{
    private static JsonObject Number(int sign=0)=>sign==2?new(){["type"]="number",["exclusiveMinimum"]=0,["maximum"]=1e9}:new(){["type"]="number",["minimum"]=sign==1?0:-1e9,["maximum"]=1e9};
    private static JsonObject Integer(int min,int max)=>new(){["type"]="integer",["minimum"]=min,["maximum"]=max};
    private static JsonObject Boolean()=>new(){["type"]="boolean"};
    private static JsonObject Text(string description)=>new(){["type"]="string",["description"]=description};
    private static JsonObject Handles()=>new(){["type"]="array",["minItems"]=1,["maxItems"]=1,["items"]=new JsonObject{["type"]="string",["pattern"]="^[0-9A-Fa-f]{1,16}$"}};
    public static JsonObject DisplayFields()
    {
        var p=new JsonObject();
        foreach(string name in DimensionEditingValidation341.DisplayNames)p[name]=name switch
        {
            "linear_unit_format"=>Integer(1,6),"angular_unit_format"=>Integer(0,4),"alternate_unit_format"=>Integer(1,8),
            "linear_zero_suppression" or "alternate_zero_suppression"=>Integer(0,15),"angular_zero_suppression"=>Integer(0,3),"alternate_precision"=>Integer(0,8),
            "primary_prefix" or "primary_suffix" or "alternate_prefix" or "alternate_suffix"=>Text("Literal visible Unicode affix; no fields, CAD format/percent codes or measurement markers. Supply both prefix and suffix, including empty strings, to replace the entire native template."),
            "alternate_enabled" or "suppress_extension1" or "suppress_extension2" or "suppress_dimension1" or "suppress_dimension2"=>Boolean(),
            "alternate_factor"=>Number(2),"rounding" or "alternate_rounding" or "extension_offset" or "extension_beyond"=>Number(1),_=>Number()
        };
        p["linear_factor"]!["description"]="Literal native DIMLFAC display multiplier; signed/zero native semantics retained. This does not scale measured geometry or change document units; no automatic conversion.";
        p["linear_unit_format"]!["description"]="Native DIMLUNIT:1 scientific,2 decimal,3 engineering,4 architectural,5 fractional,6 Windows environment.";
        p["angular_unit_format"]!["description"]="Native DIMAUNIT:0 decimal degrees,1 degrees/minutes/seconds,2 gradians,3 radians,4 surveyor. geometry.measurement stays radians.";
        p["alternate_unit_format"]!["description"]="Native DIMALTU:1 scientific,2 decimal,3 engineering,4 stacked architectural,5 stacked fractional,6 architectural,7 fractional,8 Windows environment.";
        p["rounding"]!["description"]="Native DIMRND display increment,0 disables rounding; not source-geometry rounding or automatically radians.";
        p["alternate_rounding"]!["description"]="Literal native DIMALTRND display increment,0 disables; alternate_factor and format define the display convention.";
        p["text_gap"]!["description"]="Signed native DIMGAP in drawing millimeters; negative native box semantics retained.";
        foreach(string name in new[]{"extension_offset","extension_beyond"})p[name]!["description"]="Native DIMEXO/DIMEXE stored drawing millimeters; native dimension scale may affect visible size.";
        return p;
    }
    public static JsonObject DisplayOperation()=>new(){["type"]="object",["additionalProperties"]=false,
        ["required"]=new JsonArray("operation","handles","properties"),["properties"]=new JsonObject{
            ["operation"]=new JsonObject{["const"]="set_dimension_display"},["handles"]=Handles(),
            ["properties"]=new JsonObject{["type"]="object",["additionalProperties"]=false,["minProperties"]=1,["properties"]=DisplayFields()}},
        ["description"]="Change supported effective native display/unit fields of one exact ordinary existing dimension in Model or active ordinary Paper. Read geometry.display, format and definition first. Primary/alternate formats/factors/rounding change labels, never measured CAD geometry or driving constraints. Prefix/suffix replace native DIMPOST or DIMAPOST with prefix<>suffix (installed SDK measurement marker); requested literal glyphs are checked before writing. A retained explicit DimensionText override may mask these visible changes. Style identity, measured references, raw text override, plane, manual text position and DIMTMOVE policy remain. Automatic native text layout may reflow. Associations/fields/annotation contexts need their owning route. No guaranteed Undo/rollback; target cannot share another mutation in this request."};
    private static JsonObject Definition(string kind)
    {
        var fields=new JsonObject();var required=new JsonArray();
        foreach(string name in DimensionEditingValidation341.DefinitionNames(kind))
        {required.Add(name);fields[name]=name switch{"rotation"=>Number(),"jog_angle"=>Number(),"leader_length"=>Number(1),"using_x_axis"=>Boolean(),_=>new JsonObject{["type"]="array",["minItems"]=3,["maxItems"]=3,["prefixItems"]=new JsonArray(Number(),Number(),new JsonObject{["const"]=0}),["items"]=false}};}
        return new(){["type"]="object",["additionalProperties"]=false,["required"]=required,["properties"]=fields};
    }
    public static JsonObject DefinitionOperation()
    {
        var choices=new JsonArray();
        foreach(string kind in DimensionEditingValidation341.Kinds)choices.Add(new JsonObject{["properties"]=new JsonObject{
            ["dimension_kind"]=new JsonObject{["const"]=kind},["expected_definition"]=Definition(kind),["definition"]=Definition(kind)}});
        return new(){["type"]="object",["additionalProperties"]=false,["required"]=new JsonArray("operation","handles","dimension_kind","expected_definition","definition"),
            ["properties"]=new JsonObject{["operation"]=new JsonObject{["const"]="set_dimension_definition"},["handles"]=Handles(),
                ["dimension_kind"]=new JsonObject{["type"]="string",["enum"]=new JsonArray(DimensionEditingValidation341.Kinds.Select(s=>(JsonNode?)JsonValue.Create(s)).ToArray())},
                ["expected_definition"]=new JsonObject{["type"]="object"},["definition"]=new JsonObject{["type"]="object"}},["oneOf"]=choices,
            ["description"]="Edit the complete stored native definition of one existing independent aligned, rotated, radial, diametric, two-line/three-point angular, ordinate, arc-length or jogged-radius dimension. Read exact dimension_kind and complete geometry.definition; use expected_definition to bind those current anchors. XYZ points remain in WCS XY/Z0, drawing millimeters; rotated rotation is radians. Changes affect dimension references only, not the referenced CAD source or driving constraints. Native style/text/owner/metadata/plane/movement remain; manual diameter DIMTMOVE0/1 stays excluded. Automatic label/line/arc placement may normalize along the same physical line/arc. No primitive reconstruction, new association or guaranteed rollback. Target must have only this operation in the request."};
    }
    public static JsonObject ReadDisplay()
    {
        var edit=DisplayFields();var p=new JsonObject();
        foreach(var field in edit)
        {
            if(field.Key is "primary_prefix" or "primary_suffix" or "alternate_prefix" or "alternate_suffix")continue;
            // Observe actual native values, including unknown/unusual stored
            // codes and magnitudes. Edit admission limits are not read limits.
            p[field.Key]=new JsonObject{["type"]=field.Value!["type"]!.DeepClone()};
            if(field.Value["description"] is {} description)p[field.Key]!["description"]=description.DeepClone();
        }
        p["primary_template"]=Text("Actual native DIMPOST including <> if present; raw formatting is retained, not parsed as requested literal affixes.");
        p["alternate_template"]=Text("Actual native DIMAPOST; the installed SDK declares prefix<>suffix. Existing raw strings are retained.");
        p["text_movement"]=new JsonObject{["type"]="integer",["description"]="Actual native DIMTMOVE; these operations never change it. Manual diametric policy0/1 remains excluded from definition editing."};
        p["automatic_text_position"]=Boolean();
        p["units"]=Text("Display fields use native DIM-variable conventions, no implicit conversion; geometry.measurement is separate.");
        return new(){["type"]="object",["additionalProperties"]=false,["properties"]=p};
    }
}
