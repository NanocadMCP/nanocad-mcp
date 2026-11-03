using System.Text.Json;
using System.Text.Json.Nodes;

namespace NanoCAD.McpServer;

internal static class GeometryAnalysisSchema
{
    public static JsonElement Input()
    {
        var root=JsonNode.Parse("""
    {"type":"object","additionalProperties":false,
     "required":["bridge_instance_id","document_id","expected_revision","mode","handles"],
     "properties":{
       "bridge_instance_id":{"type":"string","format":"uuid"},
       "document_id":{"type":"string","format":"uuid"},
       "expected_revision":{"type":"string","pattern":"^[0-9]{1,20}$"},
       "mode":{"type":"string","enum":["metrics","intersections","distance","relations","containment"]},
       "handles":{"type":"array","minItems":1,"uniqueItems":true,"items":{"type":"string","pattern":"^[0-9A-Fa-f]{1,16}$"}},
       "point":{"type":"array","minItems":3,"maxItems":3,"items":{"type":"number","minimum":-1e12,"maximum":1e12}}
     },
     "allOf":[{"if":{"properties":{"mode":{"enum":["intersections","distance","relations"]}}},"then":{"properties":{"handles":{"minItems":2,"maxItems":2}},"not":{"required":["point"]}}},{"if":{"properties":{"mode":{"const":"containment"}}},"then":{"required":["point"],"properties":{"handles":{"minItems":1,"maxItems":1}}}},{"if":{"required":["workplane"]},"then":{"properties":{"mode":{"enum":["relations","containment"]}}},"else":{"if":{"properties":{"mode":{"const":"containment"}}},"then":{"properties":{"point":{"prefixItems":[{},{},{"const":0}]}}}}}]
    }
    """)!.AsObject();
        var frame=WorkplaneSchema.Input();frame["description"]="Optional plane for relations/containment on native Line/Circle/Arc/Polyline. Origin uses drawing units; axes are perpendicular unit WCS vectors. Input point and all output witness points remain WCS triples. No projection or UCS change. Omit for existing WCSXY/Z0 behavior.";
        root["properties"]!["workplane"]=frame;
        SolidGeometryAnalysisSchema.Extend(root);
        return JsonSerializer.SerializeToElement(new JsonObject{["type"]="object",["oneOf"]=new JsonArray(root,BoundaryTraceSchema408.Input(),SolidMeasurementSchema409.Input())});
    }
}
