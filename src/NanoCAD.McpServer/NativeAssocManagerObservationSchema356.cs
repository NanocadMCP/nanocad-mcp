using System.Text.Json.Nodes;
namespace NanoCAD.McpServer;
internal static class NativeAssocManagerObservationSchema356
{
    static JsonObject Text()=>new(){["type"]="string"};
    internal static JsonObject Read()=>new(){["type"]="object",["additionalProperties"]=false,
        ["required"]=new JsonArray("subject_scope","mcs_owner_document_id","manager_handle","owner_handle","runtime_type","private_state_complete","observation_scope"),
        ["properties"]=new JsonObject{["subject_scope"]=Text(),["mcs_owner_document_id"]=ModelDatumPlaneSchema353.Id(),
            ["manager_handle"]=BasicModelingSchema.Handle(),["owner_handle"]=BasicModelingSchema.Handle(),["runtime_type"]=Text(),
            ["private_state_complete"]=new JsonObject{["const"]=false},["observation_scope"]=Text()},
        ["description"]="Exact readable resident drawing-owned AssocManager anchor. mcs_owner_document_id names the separately verified body-owner MCS scope; it does not equate ACAD association storage with that network. Private manager state is unavailable through this SDK and is explicitly unobserved. Exact readable metadata and actual native engineering dependencies remain checked; no network initialization/evaluation or empty-inventory claim."};
}
