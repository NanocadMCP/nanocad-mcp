using System.Text.Json.Nodes;

namespace NanoCAD.McpServer;
internal static class DrawingCircleSchema
{
    internal static IEnumerable<JsonObject> Operations()
    {
        JsonObject Text(int maximum)=>new(){["type"]="string",["minLength"]=1,["maxLength"]=maximum};
        JsonObject Length()=>new(){["type"]="number",["minimum"]=.001,["maximum"]=1e6};
        foreach(var action in new[]{"add_drawing_radius","set_drawing_radius","extrude_drawing_circle"})
        {
            var fields=new JsonObject{["operation"]=new JsonObject{["const"]=action},
                ["handles"]=new JsonObject{["type"]="array",["minItems"]=1,["maxItems"]=1,["items"]=new JsonObject{["type"]="string",["pattern"]="^[0-9A-Fa-f]{1,16}$"}},["expected_radius"]=Length()};
            if(action=="set_drawing_radius")
            {
                fields["constraint_id"]=new JsonObject{["type"]="string",["pattern"]=@"^\{[0-9A-Fa-f]{8}(-[0-9A-Fa-f]{4}){3}-[0-9A-Fa-f]{12}\}$"};
                fields["expected_expression"]=Text(8192);fields["radius"]=Length();
            }
            if(action=="extrude_drawing_circle")
            {fields["distance"]=Length();fields["direction"]=new JsonObject{["enum"]=new JsonArray("positive","negative")};fields["name"]=Text(128);}
            yield return new JsonObject{["type"]="object",["additionalProperties"]=false,["properties"]=fields,
                ["required"]=new JsonArray(fields.Select(p=>(JsonNode?)JsonValue.Create(p.Key)).ToArray()),
                ["description"]="Sole operation on one exact native Model Space Circle in WCSXY/Z0, millimeters, expected_radius required. add_drawing_radius attaches a native driving radius without changing the circle. extrude_drawing_circle creates a native body with an associative projection of this source; source is retained. set_drawing_radius edits the exact current DimRadial and rebuilds the dependent healthy native bodies containing one exact projection of this circle into one straight distance extrusion, including a later join/cut feature. The write checks source radius, retained projection/sketch/profile/feature identity, operation/depth and current body validity. Other constraints on this circle, dependent variable formulas and unsupported feature types remain outside this scope. Read inspect_constraints and inspect_model_history first, then verify geometry and an MCP image. ROLLBACK_NOT_GUARANTEED; results report actual constraint/projection/body identities. No internal sketch-constraint or arbitrary profile support is implied."};
        }
    }
}
