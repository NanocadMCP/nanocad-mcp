using System.Text.Json;
using System.Text.Json.Nodes;

namespace NanoCAD.McpServer;
internal static class DocumentConstraintsSchema
{
    internal static JsonElement Input()
    {
        var schema=JsonNode.Parse(ModelVariablesSchema.Input().GetRawText())!.AsObject();
        var properties=schema["properties"]!.AsObject();properties.Remove("handle");properties["workplane"]=WorkplaneSchema.Input();
        properties["include_variables"]=new JsonObject{["type"]="boolean",["default"]=false,["description"]="Include the bounded whole ACAD document variable inventory, stored values and persisted direct downstream links, with dimension source refs from the selected workplane. Needed for User drivers, names and expressions; does not solve/evaluate. If native variable service is uninitialized, VARIABLES_UNAVAILABLE is returned without creating it; constraint-only inspection remains available with false."};
        properties["handles"]=new JsonObject{["type"]="array",["minItems"]=1,["uniqueItems"]=true,
            ["items"]=new JsonObject{["type"]="string",["pattern"]=@"^[0-9A-Fa-f]{1,16}$"}};
        schema["required"]=new JsonArray("bridge_instance_id","document_id","expected_revision","handles");
        return JsonSerializer.SerializeToElement(schema);
    }
}
