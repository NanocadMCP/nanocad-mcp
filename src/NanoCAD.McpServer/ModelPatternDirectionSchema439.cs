using System.Text.Json.Nodes;

namespace NanoCAD.McpServer;

internal static class ModelPatternDirectionSchema439
{
    internal static JsonObject Id()=>new(){["type"]="string",["pattern"]=@"^\{[0-9A-Fa-f]{8}-[0-9A-Fa-f]{4}-[0-9A-Fa-f]{4}-[0-9A-Fa-f]{4}-[0-9A-Fa-f]{12}\}$"};
    static JsonObject Object(JsonObject fields)=>new(){["type"]="object",["additionalProperties"]=false,["properties"]=fields,["required"]=new JsonArray(fields.Select(field=>(JsonNode?)JsonValue.Create(field.Key)).ToArray())};
    static JsonObject Vector()=>new(){["type"]="array",["minItems"]=3,["maxItems"]=3,["items"]=new JsonObject{["type"]="number",["minimum"]=-1e9,["maximum"]=1e9}};
    internal static JsonObject Selector()=>new(){["oneOf"]=new JsonArray(
        Object(new(){["kind"]=new JsonObject{["const"]="global_axis"},["axis"]=new JsonObject{["enum"]=new JsonArray("X","Y","Z")}}),
        Object(new(){["kind"]=new JsonObject{["const"]="work_axis"},["axis_id"]=Id()}),
        Object(new(){["kind"]=new JsonObject{["const"]="fixed_axis"},["origin"]=Vector(),["direction"]=Vector()}),
        Object(new(){["kind"]=new JsonObject{["const"]="line"},["handle"]=BasicModelingSchema.Handle()}),
        Object(new(){["kind"]=new JsonObject{["const"]="edge"},["edge_id"]=Id(),["edge_token"]=new JsonObject{["type"]="string",["pattern"]="^[0-9A-Fa-f]{64}$"}})),
        ["description"]="An actual native array binding. global_axis resolves X/Y/Z in the selected part's owner; work_axis reuses an observed same-owner WorkAxis, preserving its definition and dependencies. fixed_axis explicitly creates retained native WorkAxis construction at WCS origin with the nonzero direction; it adds no face/edge constraint. line consumes an existing exact top-level Line without a prior projection. edge selects a current same-part native edge/token; rectangular directions require a linear edge, circular axes accept linear/circular edges. No axis is inferred from a bounding box or recreated primitive."};
    internal static JsonObject Binding()=>Object(new(){["native_id"]=Id(),["parameter"]=new JsonObject{["type"]="number"},
        ["gs_marker"]=new JsonObject{["type"]="integer"},["data"]=new JsonObject{["type"]="integer"},
        ["spacing_mode"]=new JsonObject{["enum"]=new JsonArray("step","range")},["reversed"]=new JsonObject{["type"]="boolean"}});
}
