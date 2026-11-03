using System.Text.Json.Nodes;
namespace NanoCAD.McpServer;
internal static class TableNativeLayoutSchema356
{
    private static JsonObject Text()=>new(){["type"]="string"};
    internal static JsonObject Read()=>new(){["type"]="object",["additionalProperties"]=false,
        ["required"]=new JsonArray("scope","operations","native_size_reflow","size_observation","explicit_resize"),
        ["properties"]=new JsonObject{["scope"]=Text(),["operations"]=new JsonObject{["type"]="array",["items"]=Text()},
            ["native_size_reflow"]=Text(),["size_observation"]=Text(),["explicit_resize"]=Text()},
        ["description"]="Native content/formula/style layout can change physical row/column sizes; current sizes are observed separately from exact topology/stored content. Explicit resize requests retain their own requested-size verification."};
}
