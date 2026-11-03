using System.Text.Json;
using System.Text.Json.Nodes;
using NanoCAD.Contracts;

namespace NanoCAD.McpServer;

internal static class ModelHistorySchema
{
    public static JsonElement Input()
    {
        var schema=JsonNode.Parse("""
    {"type":"object","additionalProperties":false,
     "required":["bridge_instance_id","document_id","expected_revision","handles"],
     "properties":{
       "bridge_instance_id":{"type":"string","format":"uuid"},
       "document_id":{"type":"string","format":"uuid"},
       "expected_revision":{"type":"string","pattern":"^[0-9]{1,20}$"},
       "handles":{"type":"array","minItems":1,"uniqueItems":true,"items":{"type":"string","pattern":"^[0-9A-Fa-f]{1,16}$"}},
       "sketch_geometry_ids":{"type":"array","minItems":1,"uniqueItems":true,"description":"Optional specific PlanarSketch IDs from current history; return element geometry in WCS with element IDs. Omit for ordinary history metadata. Unsupported geometry is explicitly incomplete. Response size is controlled by the transport byte budget. No constraint solving or edit mode.","items":{"type":"string","pattern":"^\\{[0-9A-Fa-f]{8}-[0-9A-Fa-f]{4}-[0-9A-Fa-f]{4}-[0-9A-Fa-f]{4}-[0-9A-Fa-f]{12}\\}$"}}
     }}
    """)!;
        foreach(var field in ModelProfileLifecycleSchema340.HistoryInputProperties())
            schema["properties"]![field.Key]=field.Value?.DeepClone();
        foreach(var field in ModelSketchLifecycleSchema362.HistoryInputProperties())schema["properties"]![field.Key]=field.Value?.DeepClone();
        schema["properties"]!["profile_region_ids"]=ModelProfileRegionsSchema350.HistoryInput();
        schema["properties"]!["profile_element_ids"]=ModelProfileElementsSchema358.HistoryInput();
        schema["properties"]!["extrusion_extent_ids"]=ModelExtrusionExtentsSchema351.HistoryInput();
        schema["properties"]!["feature_deletion_ids"]=ModelFeatureDeletionSchema378.HistoryInput();
        schema["properties"]!["sketch_constraint_parameter_ids"]=ModelSketchConstraintParameterSchema379.ReadInput();
        schema["allOf"]=new JsonArray(new JsonObject{["if"]=new JsonObject{["required"]=new JsonArray("sketch_constraint_parameter_ids")},["then"]=new JsonObject{["required"]=new JsonArray("sketch_ids"),["properties"]=new JsonObject{["handles"]=new JsonObject{["maxItems"]=1},["sketch_ids"]=new JsonObject{["maxItems"]=1}}}});
        schema["properties"]!["datum_plane_ids"]=ModelDatumPlaneSchema353.HistoryIds();
        schema["properties"]!["datum_plane_revision_ids"]=ModelDatumPlaneRevisionSchema354.HistoryInput();
        schema["properties"]!["include_document_datum_planes"]=new JsonObject{["type"]="boolean",["default"]=false,["description"]="Optional readonly WorkPlane identity pages in the selected bodies' one shared actual native owner document, which can differ from the active DWG lookup container. Omitted by default; no document construction scan for ordinary history. Actual byte paging, no plane/body-count ceiling."};
        schema["properties"]!["document_datum_cursor"]=ModelDatumPlaneSchema353.Id();
        schema["properties"]!["document_datum_cursor"]!["description"]="The preceding datum identity page's next_cursor in this same document/revision, with include_document_datum_planes=true.";
        return JsonSerializer.SerializeToElement(schema);
    }
}
