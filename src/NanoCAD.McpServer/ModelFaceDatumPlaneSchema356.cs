using System.Text.Json.Nodes;
namespace NanoCAD.McpServer;
internal static class ModelFaceDatumPlaneSchema356
{
    static JsonObject Text()=>new(){["type"]="string"};
    static JsonObject Number()=>new(){["type"]="number"};
    internal static JsonObject Definition()=>new(){["type"]="object",["additionalProperties"]=false,
        ["required"]=new JsonArray("kind","face_token","face_id","edge_token","edge_id","native_angle_radians"),
        ["properties"]=new JsonObject{["kind"]=new JsonObject{["const"]="face_angle"},["face_token"]=ModelDatumPlaneSchema353.Token(),["face_id"]=ModelDatumPlaneSchema353.Id(),
            ["edge_token"]=ModelDatumPlaneSchema353.Token(),["edge_id"]=ModelDatumPlaneSchema353.Id(),["native_angle_radians"]=new JsonObject{["type"]="number",["minimum"]=-Math.PI,["maximum"]=Math.PI}},
        ["description"]="Native PlaneLineNAngle from a current planar face and one straight boundary edge of this same part, using inspect_solid_topology with include_native_model_faces/include_native_model_edges and the returned native_model_faces/native_model_edges tokens and MultiCAD IDs. Teigha B-rep IDs are a different identity system. Signed radians follow the SDK source/frame convention, not a requested WCS rotation. The actual oriented physical plane and unsigned dihedral are read before retained Mirror reuse. No fixed-coordinate substitute or internal sketch solver."};
    internal static JsonObject Relation()=>new(){["type"]="object",["additionalProperties"]=false,
        ["required"]=new JsonArray("kind","source_body_id","face_id","edge_id","face_geometry","edge_geometry","source_token","unsigned_dihedral_radians","observed_native_parameter_radians","angle_scope"),
        ["properties"]=new JsonObject{["kind"]=new JsonObject{["const"]="face_angle"},["source_body_id"]=ModelDatumPlaneSchema353.Id(),["face_id"]=ModelDatumPlaneSchema353.Id(),["edge_id"]=ModelDatumPlaneSchema353.Id(),
            ["face_geometry"]=new JsonObject{["type"]="object"},["edge_geometry"]=new JsonObject{["type"]="object"},["source_token"]=ModelDatumPlaneSchema353.Token(),["unsigned_dihedral_radians"]=Number(),
            ["observed_native_parameter_radians"]=new JsonObject{["type"]=new JsonArray("number","null")},["angle_scope"]=Text()},
        ["description"]="Actual native definition source IDs/geometry for an observable same-part face-angle datum. IDs are retained definition references, not a durable generated topology marker claim. ParamVal is the current SDK scalar in radians; SetDefinition can rebase its internal frame, so no initial scalar equality or WCS handedness is inferred. Missing source_relationship is not an empty source network."};
}
