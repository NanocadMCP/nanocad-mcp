using System.Text.Json.Nodes;

namespace NanoCAD.McpServer;

internal static class SolidGeometryAnalysisSchema
{
    internal static void Extend(JsonObject schema)
    {
        var modes=(JsonArray)schema["properties"]!["mode"]!["enum"]!;
        modes.Add("solid_containment");modes.Add("solid_interference");
        schema["properties"]!["mode"]!["description"]="solid_containment checks one WCS point against one top-level native Solid3d. Native bounds can prove the point is outside. Points within those bounds require whole-body B-rep classification, which can be unavailable on the host. solid_interference checks two top-level native Solid3d shapes using detached copies. It does not measure clearance or classify touching. These solid modes do not accept workplane.";
        var conditions=(JsonArray)schema["allOf"]!;
        conditions.Add(JsonNode.Parse("""
          {"if":{"properties":{"mode":{"const":"solid_containment"}}},
           "then":{"required":["point"],"properties":{"handles":{"minItems":1,"maxItems":1}},"not":{"required":["workplane"]}}}
          """));
        conditions.Add(JsonNode.Parse("""
          {"if":{"properties":{"mode":{"const":"solid_interference"}}},
           "then":{"properties":{"handles":{"minItems":2,"maxItems":2}},
                   "allOf":[{"not":{"required":["point"]}},{"not":{"required":["workplane"]}}]}}
          """));
    }

    internal static void ExtendOutput(JsonObject schema)
    {
        JsonObject Text()=>new(){["type"]="string"};
        JsonObject Flag()=>new(){["type"]="boolean"};
        JsonObject Const(string value)=>new(){["type"]="string",["const"]=value};
        JsonObject Nullable(JsonObject value)=>new(){["anyOf"]=new JsonArray(value,new JsonObject{["type"]="null"})};
        JsonObject Ref()=>new(){["$ref"]="#/$defs/ref"};
        JsonObject Common(string mode,string solver)=>new(){
            ["mode"]=Const(mode),["coordinate_system"]=Const("WCS"),["solver"]=Const(solver),
            ["units"]=BasicModelingSchema.Obj(new(){["name"]=Text(),["value"]=new JsonObject{["type"]="integer"}})};
        var containment=Common("solid_containment","native_bounds_and_body_brep");
        containment["body"]=Ref();containment["point"]=new JsonObject{["type"]="array",["minItems"]=3,["maxItems"]=3,["items"]=new JsonObject{["type"]="number"}};
        containment["classification"]=new JsonObject{["type"]="string",["enum"]=new JsonArray("inside","outside","boundary")};
        containment["method"]=new JsonObject{["type"]="string",["enum"]=new JsonArray("conservative_native_bounds_exclusion","native_body_brep_point_containment")};
        containment["native_classification"]=Nullable(Text());containment["container_available"]=Nullable(Flag());
        containment["tolerance_policy"]=Const("native_kernel_with_conservative_bounds_exclusion");
        var interference=Common("solid_interference","native_solid_check_interference");
        interference["first"]=Ref();interference["second"]=Ref();interference["interference"]=Flag();
        interference["contact_evaluated"]=new JsonObject{["const"]=false};
        interference["distance_evaluated"]=new JsonObject{["const"]=false};interference["warning"]=Text();
        var alternatives=(JsonArray)schema["oneOf"]!;
        alternatives.Add(BasicModelingSchema.Obj(containment));alternatives.Add(BasicModelingSchema.Obj(interference));
    }
}
