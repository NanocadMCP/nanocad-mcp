using System.Text.Json.Nodes;

namespace NanoCAD.McpServer;

internal static class ModelSketchConstraintObservationSchema353
{
    internal static JsonObject Read()=>new(){["type"]="object",["additionalProperties"]=false,
        ["required"]=new JsonArray("document_id","sketch_id","scope_id","network","solver_status","constraints_complete","constraints","unavailable_reason","warning"),
        ["properties"]=new JsonObject{["document_id"]=ModelDatumPlaneSchema353.Id(),["sketch_id"]=ModelDatumPlaneSchema353.Id(),["scope_id"]=ModelDatumPlaneSchema353.Id(),
            ["network"]=new JsonObject{["type"]="string"},["solver_status"]=new JsonObject{["const"]="not_evaluated"},["constraints_complete"]=new JsonObject{["type"]="boolean"},
            ["constraints"]=new JsonObject{["anyOf"]=new JsonArray(new JsonObject{["type"]="array",["items"]=new JsonObject{["type"]="object"}},new JsonObject{["type"]="null"})},
            ["unavailable_reason"]=new JsonObject{["type"]=new JsonArray("string","null")},["warning"]=new JsonObject{["type"]=new JsonArray("string","null")}},
        ["oneOf"]=new JsonArray(
            new JsonObject{["properties"]=new JsonObject{["constraints_complete"]=new JsonObject{["const"]=true},["constraints"]=new JsonObject{["type"]="array"},["unavailable_reason"]=new JsonObject{["type"]="null"},["warning"]=new JsonObject{["type"]="null"}}},
            new JsonObject{["properties"]=new JsonObject{["constraints_complete"]=new JsonObject{["const"]=false},["constraints"]=new JsonObject{["type"]="null"},["unavailable_reason"]=new JsonObject{["type"]="string"},["warning"]=new JsonObject{["type"]="string"}}}),
        ["description"]="Actual exact readonly sketch constraint manager scope. constraints_complete=false with constraints=null means enumeration unavailable, never an empty network or complete preservation claim. Available inventories are fully compared around a retained operation; geometry/source/feature/owning driver checks remain. No constraint setter, bootstrap or solver is requested."};
}
