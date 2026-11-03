using System.Text.Json.Nodes;

namespace NanoCAD.McpServer;

internal static class HatchPropertiesSchema
{
    internal static JsonObject Edit()=>new(){
        ["type"]="object",["additionalProperties"]=false,
        ["properties"]=new JsonObject{
            ["operation"]=new JsonObject{["type"]="string",["const"]="set_hatch_properties"},
            ["handles"]=new JsonObject{["type"]="array",["minItems"]=1,["maxItems"]=1,["items"]=new JsonObject{["type"]="string",["pattern"]="^[0-9A-Fa-f]{1,16}$"}},
            ["hatch_token"]=new JsonObject{["type"]="string",["pattern"]="^[0-9A-Fa-f]{64}$"},
            ["properties"]=new JsonObject{["type"]="object",["additionalProperties"]=false,["minProperties"]=1,["properties"]=new JsonObject{
                ["pattern_name"]=new JsonObject{["type"]="string",["minLength"]=1,["maxLength"]=255,["description"]="SOLID or an installed native predefined pattern name; not a PAT file path. Native SDK resolution runs on the resident hatch in its document context; a detached preview cannot certify installed PAT availability."},
                ["pattern_angle"]=new JsonObject{["type"]="number",["minimum"]=0,["exclusiveMaximum"]=2*Math.PI,["description"]="Radians in the existing Hatch OCS plane."},
                ["pattern_scale"]=new JsonObject{["type"]="number",["exclusiveMinimum"]=0,["description"]="Stored native PatternScale, used by predefined/custom patterns. UserDefined parallel-line spacing is the separate pattern_space property."},
                ["pattern_space"]=new JsonObject{["type"]="number",["exclusiveMinimum"]=0,["description"]="Native UserDefined-only parallel-line spacing in drawing millimeters. Keep the existing UserDefined pattern: do not combine this with pattern_name, which selects PreDefined."},
                ["origin_ocs"]=new JsonObject{["type"]="array",["minItems"]=2,["maxItems"]=2,["items"]=new JsonObject{["type"]="number"},["description"]="Stored Hatch.Origin in its existing OCS plane, drawing millimeters; not WCS XY for a tilted/elevated hatch."},
                ["hatch_style"]=new JsonObject{["enum"]=new JsonArray("normal","outer","ignore"),["description"]="Native island handling: normal alternating fill, outer outside-only fill, ignore fills across islands. Stored boundary loops and associations remain."},
                ["pattern_double"]=new JsonObject{["type"]="boolean"}}}},
        ["required"]=new JsonArray("operation","handles","hatch_token","properties"),
        ["description"]="Change stored properties of one existing exact native Hatch in Model or the active ordinary Paper space. Read inspect_objects geometry.editing and use its fresh hatch_token. SOLID and installed predefined patterns can be selected; absent pattern_name retains the existing predefined/user/custom pattern. Angle, positive scale, positive UserDefined-only pattern_space, OCS origin, native island style and double pattern are optional. Retained UserDefined spacing remains independent of PatternScale. Unspecified active editable values are retained. Unrequested inactive SOLID angle/scale/double/origin may normalize natively; actual readback is authoritative. Explicit requested fields remain checked. Native boundary loops, plane, identity, display properties and local native associative references are preserved; this does not rebuild contours or detach associations. Supported native loops are polyline/bulge, line, circular/elliptical arc and NURBS definitions. Associative local Line/Circle/Arc/Ellipse/Spline/Polyline boundaries retain forward IDs and reverse hatch reactors. Gradient fills, external/custom boundary sources, fields and unclassified owned dependencies use their owning route. Distinct hatches may be edited together; no other operation may share a target. Resident native EvaluateHatch performs evaluation; there is no estimated density or hatch-line count ceiling. Undo/rollback is not guaranteed; a native pending/unknown response is never success."};

    internal static JsonObject Read()=>new(){["type"]="object",["additionalProperties"]=false,["properties"]=new JsonObject{
        ["hatch_token"]=new JsonObject{["type"]="string",["pattern"]="^[0-9A-F]{64}$"},
        ["scope"]=new JsonObject{["type"]="string",["const"]="stored_native_loops_and_local_associations"},
        ["source_handles"]=new JsonObject{["type"]="array",["items"]=new JsonObject{["type"]="string",["pattern"]="^[0-9A-F]{1,16}$"}},
        ["boundary_replacement"]=HatchBoundarySchema349.Read(),["association_lifecycle"]=HatchAssociationSchema358.Read()},
        ["required"]=new JsonArray("hatch_token","scope","source_handles")};
}
