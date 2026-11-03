using System.Text.Json.Nodes;

namespace NanoCAD.McpServer;

public static class ModelSketchVisibilitySchema436
{
    static JsonObject Id()=>new(){["type"]="string",["pattern"]="^\\{[0-9A-Fa-f]{8}-[0-9A-Fa-f]{4}-[0-9A-Fa-f]{4}-[0-9A-Fa-f]{4}-[0-9A-Fa-f]{12}\\}$"};
    static JsonObject Ids()=>new(){["type"]="array",["uniqueItems"]=true,["items"]=Id()};
    static JsonObject Bool()=>new(){["type"]="boolean"};
    static JsonObject Number()=>new(){["type"]="number"};
    static JsonObject Text()=>new(){["type"]="string"};
    static JsonObject Nullable(JsonObject value)=>new(){["anyOf"]=new JsonArray(value,new JsonObject{["type"]="null"})};
    static JsonObject Array(JsonObject value)=>new(){["type"]="array",["items"]=value};
    static JsonObject Token()=>new(){["type"]="string",["pattern"]="^[0-9A-Fa-f]{64}$"};
    static JsonObject Object(JsonObject properties,params string[] required)=>new(){["type"]="object",["additionalProperties"]=false,["properties"]=properties,["required"]=new JsonArray(required.Select(name=>(JsonNode?)JsonValue.Create(name)).ToArray())};
    static JsonObject RequiredObject(JsonObject properties)=>Object(properties,properties.Select(pair=>pair.Key).ToArray());
    public static JsonObject Operation()
    {
        var ids=Ids();ids["minItems"]=1;
        var value=Object(new(){["operation"]=new JsonObject{["const"]="set_model_sketch_visibility"},
            ["handles"]=new JsonObject{["type"]="array",["minItems"]=1,["maxItems"]=1,["items"]=new JsonObject{["type"]="string",["pattern"]="^[0-9A-Fa-f]{1,16}$"}},
            ["sketch_id"]=Id(),["expected_visibility_token"]=Token(),["sketch_visible"]=Bool(),["dimensions_visible"]=Bool(),["constraint_ids"]=ids},
            "operation","handles","sketch_id","expected_visibility_token");
        value["anyOf"]=new JsonArray(new JsonObject{["required"]=new JsonArray("sketch_visible")},new JsonObject{["required"]=new JsonArray("dimensions_visible")});
        value["dependentRequired"]=new JsonObject{["constraint_ids"]=new JsonArray("dimensions_visible")};
        value["not"]=new JsonObject{["required"]=new JsonArray("sketch_visible","dimensions_visible"),["properties"]=new JsonObject{["sketch_visible"]=new JsonObject{["const"]=false},["dimensions_visible"]=new JsonObject{["const"]=true}}};
        value["description"]="Set existing native sketch and dimensional-control presentation through the selected Model Space part. Read inspect_sketch_constraints presentation.visibility_token first. Select dimensional controls by their actual constraint_ids; omit constraint_ids to select all current dimensional controls. Set at least one visibility flag; boolean writes set native visibility to 0 or 1 and read back the actual value. Showing dimensions may enable the selected sketch as its native parent prerequisite; the receipt discloses that change. Showing dimensions together with explicit sketch_visible=false is invalid. A control with no native annotation (null dimension_id/DimId) is reported unavailable; its retained driving constraint and parameter are not missing, and the result must not imply every requested annotation was shown. A dimensions-only request with no available selected annotations fails before writing; available annotations can otherwise change with explicit unavailable IDs in the receipt. Does not enter or exit sketch edit mode, create dimensions or change geometry, parameters or dependencies. Native visibility is a presentation property, not a guarantee of screen rendering. Sole operation.";
        return value;
    }
    public static JsonObject Dimension()
    {
        var value=RequiredObject(new(){["constraint_id"]=Id(),["dimension_id"]=Nullable(Id()),["native_visibility"]=Nullable(Number()),["visible"]=Nullable(Bool()),["unavailable_reason"]=Nullable(Text())});
        value["properties"]!["native_visibility"]!["description"]="Actual annotation DbEntity.Visibility value; null means unavailable. A boolean request writes native 0 or 1, followed by readback.";
        value["properties"]!["visible"]!["description"]="Observed native_visibility > 0, or null when unavailable. This native property does not guarantee annotation rendering in the current view/edit mode.";
        value["allOf"]=new JsonArray(new JsonObject{["if"]=new JsonObject{["properties"]=new JsonObject{["dimension_id"]=new JsonObject{["type"]="null"}}},
            ["then"]=new JsonObject{["properties"]=new JsonObject{["native_visibility"]=new JsonObject{["type"]="null"},["visible"]=new JsonObject{["type"]="null"},["unavailable_reason"]=Text()}}});
        value["description"]="Actual native dimensional-control presentation. dimension_id=null explicitly means the retained dimensional control has no native annotation (DimId); native_visibility and visible are null and unavailable_reason explains that condition. It does not mean the driving constraint or parameter is missing. A non-null dimension_id can still have unavailable visibility; no annotation is invented.";
        return value;
    }
    public static JsonObject Presentation()
    {
        var value=RequiredObject(new(){["sketch_id"]=Id(),["visibility_token"]=Nullable(Token()),["in_edit_mode"]=Nullable(Bool()),["native_visibility"]=Nullable(Number()),["sketch_visible"]=Nullable(Bool()),
            ["dimensions_visible"]=Nullable(Bool()),["dimensions_complete"]=Bool(),["dimensions"]=Nullable(Array(Dimension())),["unavailable_reason"]=Nullable(Text())});
        value["properties"]!["dimensions_visible"]!["description"]="Aggregate native annotation visibility only when every current dimensional control has an available annotation state and all states agree. Null means mixed, empty or unavailable; inspect the individual entries. A true value never includes unavailable annotations.";
        value["properties"]!["dimensions_complete"]!["description"]="Whether actual native annotation (DimId) visibility is completely observed for the current dimensional controls. A missing annotation or unavailable annotation visibility is explicit per control and is not a complete observation of visible dimensions.";
        value["properties"]!["in_edit_mode"]!["description"]="Actual selected sketch edit state, or null when unavailable. This read does not enter or exit edit mode.";
        value["properties"]!["native_visibility"]!["description"]="Actual selected sketch native visibility value, or null when unavailable. A boolean request writes native 0 or 1, followed by readback.";
        value["properties"]!["sketch_visible"]!["description"]="Observed native_visibility > 0, or null when unavailable. This property alone does not guarantee screen rendering in the current view/edit mode.";
        value["properties"]!["visibility_token"]!["description"]="Fresh observed presentation identity for this body/sketch in the current read context. Null means no usable visibility token was obtained; this read never creates annotations or changes presentation.";
        value["allOf"]=new JsonArray(new JsonObject{["if"]=new JsonObject{["properties"]=new JsonObject{["dimensions"]=new JsonObject{["type"]="null"}}},
            ["then"]=new JsonObject{["properties"]=new JsonObject{["dimensions_complete"]=new JsonObject{["const"] =false},["dimensions_visible"]=new JsonObject{["type"]="null"},["unavailable_reason"]=Text()}}});
        value["description"]="Observed selected sketch presentation within inspect_sketch_constraints. Native absence of an annotation is reported per control and kept separate from a missing control or unavailable enumeration. dimensions=null with dimensions_complete=false means inventory unavailable, never an empty network. An unavailable presentation does not make the existing constraint inventory unavailable or trigger edit mode or annotation creation.";
        return value;
    }
    public static JsonObject Change(JsonObject entityRef)
    {
        var value=RequiredObject(new(){["operation_index"]=new JsonObject{["type"]="integer",["minimum"]=0},["source"]=entityRef.DeepClone(),["body_ref"]=entityRef.DeepClone(),["sketch_id"]=Id(),
            ["requested_sketch_visible"]=Nullable(Bool()),["requested_dimensions_visible"]=Nullable(Bool()),["selected_constraint_ids"]=Ids(),["sketch_parent_enabled"]=Bool(),
            ["before"]=Presentation(),["after"]=Presentation(),["changed_constraint_ids"]=Ids(),["unavailable_constraint_ids"]=Ids()});
        value["properties"]!["sketch_parent_enabled"]!["description"]="True only when showing dimensional annotations also enabled this selected parent sketch as a native prerequisite. The before/after sketch_visible fields disclose the actual parent change.";
        value["properties"]!["unavailable_constraint_ids"]!["description"]="Requested dimensional controls whose native annotation could not be changed or observed, including null DimId. Nonempty means the receipt does not claim all requested dimensions were shown or hidden.";
        value["properties"]!["selected_constraint_ids"]!["description"]="Actual dimensional control IDs resolved by this request; omitted input constraint_ids resolves to all current dimensional controls. Empty for a sketch-only request.";
        return value;
    }
}
