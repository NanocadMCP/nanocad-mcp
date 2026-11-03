using System.Text.Json.Nodes;

namespace NanoCAD.McpServer;

internal static class AssociativeArraySchema431
{
    static JsonObject Object(JsonObject fields,params string[] required)=>new(){["type"]="object",["additionalProperties"]=false,["properties"]=fields,["required"]=new JsonArray(required.Select(value=>(JsonNode?)JsonValue.Create(value)).ToArray())};
    static JsonObject Number()=>new(){["type"]="number",["exclusiveMinimum"]=0,["maximum"]=1e9};
    static JsonObject Handles(bool one)
    {var result=new JsonObject{["type"]="array",["minItems"]=1,["uniqueItems"]=true,["items"]=new JsonObject{["type"]="string",["pattern"]="^[0-9A-Fa-f]{1,16}$"}};if(one)result["maxItems"]=1;return result;}
    internal static JsonObject Operation(bool create)
    {
        var fields=new JsonObject{["operation"]=new JsonObject{["const"]=create?"create_associative_rectangular_array":"set_associative_rectangular_array"},["handles"]=Handles(!create),
            ["columns"]=new JsonObject{["type"]="integer",["minimum"]=1,["maximum"]=int.MaxValue},["rows"]=new JsonObject{["type"]="integer",["minimum"]=1,["maximum"]=int.MaxValue},
            ["column_spacing"]=Number(),["row_spacing"]=Number()};
        if(create)fields["base_point"]=new JsonObject{["type"]="array",["minItems"]=2,["maxItems"]=2,["items"]=new JsonObject{["type"]="number",["minimum"]=-1e6,["maximum"]=1e6}};
        else
        {
            fields["array_token"]=new JsonObject{["type"]="string",["pattern"]="^[0-9A-Fa-f]{64}$"};
            fields["axes_angle_degrees"]=new JsonObject{["type"]="number",["exclusiveMinimum"]=0,["exclusiveMaximum"]=180,["description"]="Native rectangular angle between column and row axes, in degrees.90 means orthogonal. Retains column axis and base normal; changes only the requested angle driver to a literal."};
            fields["position_wcs"]=new JsonObject{["type"]="array",["minItems"]=3,["maxItems"]=3,["items"]=new JsonObject{["type"]="number",["minimum"]=-1e6,["maximum"]=1e6},["description"]="Absolute native array insertion position in WCS millimeters. Retains native root/source ownership, rotation, scale and other parameters."};
        }
        var result=Object(fields,create?["operation","handles","base_point","columns","rows","column_spacing","row_spacing"]:["operation","handles","array_token"]);
        result["description"]=create?
            "Create one retained native2D rectangular AssocArray action network from the selected independent ordinary Model-space Line/Circle/Arc/Polyline/Ellipse/Spline group. This differs from MInsert and independent repeated copies. Explicit WCSXY base_point, positive native column/row counts including the seed and positive millimeter spacings; columns along+X, rows along+Y, one level. Sources are acquired by native array ownership; use returned actual array/source IDs afterwards. Sole operation. Fields, persistent dependencies, native3D history and annotative sources need their owning route. No artificial item-count ceiling; actual host resources still govern execution. Undo/rollback not guaranteed. Inspect the useful rendered group and then use set_associative_rectangular_array for future count/spacing changes.":
            "Edit counts/spacings, axes_angle_degrees or position_wcs of one actual retained native rectangular AssocArray root in ordinary Model space using its current array_token from inspection. MInsert and independent copies are different representations. Supply at least one requested field. Changes retain native root, actual source IDs, rotation/scale, levels and other unrequested parameters. Angle uses degrees;90 is orthogonal. Position is the absolute native insertion position, not a replacement of source coordinates. Numeric setters replace expressions only on the requested fields with literal values; installed managed expression getters cannot return expression text, so do not infer drivers from numbers. No Commit SDK stub is called. Sole operation; positive Int32 counts have no artificial item ceiling. Refresh array_token after the edit and inspect the meaningful rendered result; Undo/rollback not guaranteed.";
        return result;
    }
    static JsonObject RectangularObservation()
    {
        JsonObject Text()=>new(){["type"]="string"};JsonObject Scalar()=>new(){["type"]="number"};
        JsonObject Triple()=>new(){["type"]="array",["minItems"]=3,["maxItems"]=3,["items"]=Scalar()};
        var fields=new JsonObject{["array_token"]=new JsonObject{["type"]="string",["pattern"]="^[0-9A-Fa-f]{64}$"},["kind"]=new JsonObject{["const"]="rectangular"},["array_handle"]=Text(),
            ["source_handles"]=new JsonObject{["type"]="array",["minItems"]=1,["items"]=Text()},["columns"]=new JsonObject{["type"]="integer",["minimum"]=1},["rows"]=new JsonObject{["type"]="integer",["minimum"]=1},["levels"]=new JsonObject{["type"]="integer",["minimum"]=1},
            ["column_spacing"]=Scalar(),["row_spacing"]=Scalar(),["level_spacing"]=Scalar(),["row_elevation"]=Scalar(),["axes_angle"]=Scalar(),["axes_angle_unit"]=new JsonObject{["const"]="degrees"},["x_axis_direction"]=Triple(),
            ["y_axis_direction"]=new JsonObject{["anyOf"]=new JsonArray(Triple(),new JsonObject{["type"]="null"}),["description"]="Actual managed native Y-axis getter, or null when that getter did not expose a valid direction. A zero optional-out result is never presented as an actual axis."},
            ["y_axis_direction_unavailable_reason"]=new JsonObject{["type"]=new JsonArray("string","null")},["base_normal"]=Triple(),["position_wcs"]=Triple(),["scale_factors"]=Triple(),["rotation"]=Scalar(),["driver_expression_observation"]=Text()};
        return Object(fields,fields.Select(pair=>pair.Key).ToArray());
    }
    internal static JsonObject Observation()=>new(){["oneOf"]=new JsonArray(RectangularObservation(),AssociativeArraySchema441.Observation(true),AssociativeArraySchema441.Observation(false))};
    internal static JsonObject Change(JsonObject entityRef)=>Object(new(){["operation"]=new JsonObject{["enum"]=new JsonArray("create_associative_rectangular_array","set_associative_rectangular_array","create_associative_polar_array","set_associative_polar_array","create_associative_path_array","set_associative_path_array")},["array"]=entityRef.DeepClone(),
        ["selected_sources"]=new JsonObject{["type"]="array",["items"]=entityRef.DeepClone()},["native_sources"]=new JsonObject{["type"]="array",["items"]=entityRef.DeepClone()},["associative"]=new JsonObject{["const"]=true},["parameters"]=Observation()},
        "operation","array","selected_sources","native_sources","associative","parameters");
}
