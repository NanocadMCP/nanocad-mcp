using System.Text.Json.Nodes;
namespace NanoCAD.McpServer;

internal static class ConstructionCurveSchema443
{
    internal const string Scope="Ray and Xline are real unbounded native construction curves, not long finite Lines. base_point uses WCS XYZ millimeters; direction uses a finite nonzero WCS XYZ vector and is normalized without changing its sign. Ray starts at its base and extends only along that direction; Xline extends both ways. Basic move/rotate/copy/erase and ordinary set_properties operate on independent top-level entities in Model or active ordinary Paper; Paper creation uses base/direction Z0. Placement uses the existing operation's declared XY/workplane frame. No endpoint trimming, array source acquisition, length/area or finite bounds is implied. Find existing objects by type/handle/selection; frame finite drawing objects rather than an infinite entity. Dependent, model-owned, nested or Field-bearing entities need their owning adapter. Missing Undo alone does not block authorized work; reconcile any unknown before another write.";
    internal static JsonObject Create(string kind)
    {
        JsonObject Point()=>new(){["type"]="array",["minItems"]=3,["maxItems"]=3,["items"]=new JsonObject{["type"]="number"}};
        return new(){["type"]="object",["additionalProperties"]=false,["properties"]=new JsonObject{
            ["type"]=new JsonObject{["const"]=kind},["layer"]=new JsonObject{["type"]="string",["minLength"]=1,["maxLength"]=255,["default"]="0"},
            ["base_point"]=Point(),["direction"]=Point()},["required"]=new JsonArray("type","base_point","direction"),["description"]=Scope};
    }
}
