using System.Text.Json.Nodes;

namespace NanoCAD.McpServer;

internal static class SolidStockSchema353
{
    internal const string Scope="Direct independent native Solid3d wall/stock editing retains its handle and root display/XData/dependency identities. Native geometry is checked on detached preview before resident editing and as finite nonempty geometry afterwards, including actual BRep validation when the native modeler implements it. An explicitly unavailable BRep check is disclosed, not treated as invalid geometry; a false validation or other actual failure remains an error. Healthy owned drawing views/sections use the existing post-Commit refresh route. Native histories, fields and unknown dependencies require their owning adapter. No scale approximation, target replacement or parametric feature. Started writes have no guaranteed rollback/Undo; preserve and inspect an unresolved outcome. Reacquire current topology after every shape change. Whole-body volume/WCS bounds are observations, not general wall-thickness or clearance measurements. Native geometry/modeler applicability remains case dependent.";
    private static JsonObject Number()=>new(){["type"]="number"};
    private static JsonObject Text()=>new(){["type"]="string"};
    private static JsonObject Array(JsonObject item)=>new(){["type"]="array",["items"]=item};
    private static JsonObject Object(params (string Name,JsonObject Shape)[] fields)
    {
        var p=new JsonObject();foreach(var f in fields)p[f.Name]=f.Shape;
        return new(){["type"]="object",["properties"]=p,["required"]=new JsonArray(fields.Select(f=>(JsonNode?)JsonValue.Create(f.Name)).ToArray()),["additionalProperties"]=false};
    }
    internal static JsonObject OffsetOperation()
    {
        var handle=Text();handle["pattern"]="^[0-9A-Fa-f]{1,16}$";
        var handles=Array(handle);handles["minItems"]=1;handles["maxItems"]=1;
        var token=Text();token["pattern"]="^[0-9A-F]{64}$";
        var distance=new JsonObject{["oneOf"]=new JsonArray(
            new JsonObject{["type"]="number",["minimum"]=-1e6,["maximum"]=-.001},
            new JsonObject{["type"]="number",["minimum"]=.001,["maximum"]=1e6})};
        distance["description"]="Signed native face-normal offset in millimeters: positive outward, negative inward; changes every face through OffsetBody.";
        var result=Object(("operation",new(){["const"]="offset_solid"}),("handles",handles),("topology_token",token),("distance",distance));
        result["description"]="Offset every face of one current direct body by a signed native distance. Read inspect_solid_topology and its exact token. The offset may heal or remove locally infeasible faces; assess the intended whole-part shape. A stock-edited target cannot have another edit in this batch. "+Scope;return result;
    }
    internal static JsonObject Change(JsonObject reference)
    {
        JsonObject Point(){var p=Array(Number());p["minItems"]=3;p["maxItems"]=3;return p;}
        JsonObject Geometry()=>Object(("volume",Number()),("bounds_wcs",Object(("min",Point()),("max",Point()))),
            ("native_brep_validated",new(){["type"]=new JsonArray("boolean","null")}),
            ("native_brep_validation_unavailable_reason",new(){["type"]=new JsonArray("string","null")}));
        var result=Object(("operation_index",new(){["type"]="integer",["minimum"]=0}),("operation",Text()),
            ("source_ref",reference),("boundary_mode",Text()),("side",Text()),("native_offset_distance",Number()),
            ("removed_face_ids",Array(Text())),("before",Geometry()),("after",Geometry()),("observation_scope",Text()),("construction_method",Text()));
        result["description"]="Actual same-body before/after native volume and WCS bounds. construction_method distinguishes native_shell_body for open walls, native_offset_difference for a closed cavity, and native_offset_body for signed machining stock. A closed cavity uses the native offset/difference of the original shape because this host rejects ShellBody with no opening faces. The retained target is not replaced. native_offset_distance records the exact argument passed to the kernel; it is not a persisted parametric driver or independent thickness measurement.";
        return result;
    }
}
