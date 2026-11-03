using System.Text.Json.Nodes;

namespace NanoCAD.McpServer;

internal static class BoundaryTraceSchema408
{
    internal const string Description="Use analyze_geometry mode=trace_boundary to obtain native closed boundaries around one interior WCS point in the current active Model/Paper editor context, including islands by default. Frame the relevant drawing area when native visibility affects discovery. This read returns detached native geometry and create-ready specs where supported; it does not append entities, save, change UCS/view, or create source associations. Native source handles are not inferred. Inspect enclosing/hole roles and any unavailable geometry before creating the required native contour/Hatch. Bounds are not a boundary trace substitute. No artificial contour-count ceiling; actual response-byte capacity applies.";
    internal static JsonObject Input()=>new()
    {
        ["type"]="object",["additionalProperties"]=false,["description"]=Description,
        ["required"]=new JsonArray("bridge_instance_id","document_id","expected_revision","mode","point"),
        ["properties"]=new JsonObject
        {
            ["bridge_instance_id"]=new JsonObject{["type"]="string",["format"]="uuid"},
            ["document_id"]=new JsonObject{["type"]="string",["format"]="uuid"},
            ["expected_revision"]=new JsonObject{["type"]="string",["pattern"]="^[0-9]{1,20}$"},
            ["mode"]=new JsonObject{["const"]="trace_boundary"},
            ["point"]=new JsonObject{["type"]="array",["minItems"]=3,["maxItems"]=3,["items"]=new JsonObject{["type"]="number",["minimum"]=-1e12,["maximum"]=1e12},["description"]="Interior seed in WCS drawing units; the bridge maps it to the current native editor UCS."},
            ["detect_islands"]=new JsonObject{["type"]="boolean",["default"]=true},
            ["space"]=new JsonObject{["enum"]=new JsonArray("model","paper"),["default"]="model",["description"]="Must match the active editing space. No automatic context switch."}
        }
    };
    internal static JsonObject Output()=>new()
    {
        ["type"]="object",["additionalProperties"]=false,
        ["required"]=new JsonArray("mode","coordinate_system","units","solver","point","native_seed_ucs","detect_islands","found","boundaries","result_scope","associative","warning"),
        ["properties"]=new JsonObject
        {
            ["mode"]=new JsonObject{["const"]="trace_boundary"},["coordinate_system"]=new JsonObject{["const"]="WCS"},
            ["units"]=new JsonObject{["type"]="object"},["solver"]=new JsonObject{["const"]="native_editor_trace_boundary_and_region_brep"},
            ["point"]=Triple(),["native_seed_ucs"]=Triple(),["detect_islands"]=new JsonObject{["type"]="boolean"},["found"]=new JsonObject{["type"]="boolean"},
            ["boundaries"]=new JsonObject{["type"]="array",["items"]=new JsonObject{["type"]="object"}},
            ["result_scope"]=new JsonObject{["type"]="string"},["associative"]=new JsonObject{["const"]=false},["warning"]=new JsonObject{["type"]="string"}
        }
    };
    private static JsonObject Triple()=>new(){["type"]="array",["minItems"]=3,["maxItems"]=3,["items"]=new JsonObject{["type"]="number"}};
}
