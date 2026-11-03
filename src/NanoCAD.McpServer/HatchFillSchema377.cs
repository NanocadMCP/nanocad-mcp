using System.Text.Json.Nodes;

namespace NanoCAD.McpServer;

internal static class HatchFillSchema377
{
    static JsonObject Text()=>new(){["type"]="string"};
    static JsonObject Number()=>new(){["type"]="number"};
    static JsonObject Bool()=>new(){["type"]="boolean"};
    static JsonObject Array(JsonNode item)=>new(){["type"]="array",["items"]=item};
    static JsonObject Nullable(JsonNode item)=>new(){["anyOf"]=new JsonArray(item,new JsonObject{["type"]="null"})};
    static JsonObject Object(params(string Name,JsonNode Shape)[] fields)
    {
        var properties=new JsonObject();var required=new JsonArray();foreach(var field in fields){properties[field.Name]=field.Shape;required.Add(field.Name);}
        return new(){["type"]="object",["additionalProperties"]=false,["properties"]=properties,["required"]=required};
    }
    static JsonObject Choice(params string[] values)=>new(){["type"]="string",["enum"]=new JsonArray(values.Select(value=>(JsonNode?)JsonValue.Create(value)).ToArray())};
    static JsonObject NativeName()=>new(){["type"]="string",["minLength"]=1,["description"]="Native installed name, not a file/path/qualifier. Availability is resolved on the resident Hatch; detached preview is not an availability oracle."};
    static JsonObject Angle()=>new(){["type"]="number",["minimum"]=0,["exclusiveMaximum"]=2*Math.PI,["description"]="Native radians in the existing Hatch OCS plane."};
    static JsonObject Positive()=>new(){["type"]="number",["exclusiveMinimum"]=0};
    static JsonObject Point2()=>new(){["type"]="array",["minItems"]=2,["maxItems"]=2,["items"]=Number()};
    static JsonObject Rgb()=>new(){["type"]="array",["minItems"]=3,["maxItems"]=3,["items"]=new JsonObject{["type"]="integer",["minimum"]=0,["maximum"]=255}};
    static JsonObject NativeSingle()=>new(){["type"]="number",["description"]="Finite native32-bit float; the server converts once and rejects overflow. No undocumented range is implied."};
    static JsonObject ColorRead()=>Object(("method",Text()),("aci",Nullable(new JsonObject{["type"]="integer"})),("rgb",Nullable(Rgb())));
    internal static JsonArray Operations()
    {
        var solid=Object(("kind",new JsonObject{["const"]="solid"}));
        var pattern=Object(("kind",new JsonObject{["const"]="pattern"}),("pattern_type",Choice("predefined","user_defined","custom_defined")),
            ("pattern_name",NativeName()),("angle",Angle()),("scale",Positive()),("origin_ocs",Point2()),("pattern_double",Bool()),("spacing",Nullable(Positive())));
        pattern["allOf"]=new JsonArray(new JsonObject{
            ["if"]=new JsonObject{["properties"]=new JsonObject{["pattern_type"]=new JsonObject{["const"]="user_defined"}},["required"]=new JsonArray("pattern_type")},
            ["then"]=new JsonObject{["properties"]=new JsonObject{["spacing"]=Positive()}},
            ["else"]=new JsonObject{["properties"]=new JsonObject{["spacing"]=new JsonObject{["type"]="null"}}}});
        pattern["description"]="Complete line-pattern settings. User-defined spacing is independent of scale and must be positive; predefined/custom spacing is null. Select solid via kind=solid.";
        var colors=Array(Object(("value",NativeSingle()),("color_rgb",Rgb())));colors["minItems"]=1;
        var gradient=Object(("kind",new JsonObject{["const"]="gradient"}),("gradient_type",Choice("predefined","user_defined")),
            ("gradient_name",NativeName()),("angle",Angle()),("shift",NativeSingle()),("one_color_mode",Bool()),("shade_tint",NativeSingle()),("colors",colors));
        gradient["description"]="Complete native gradient settings and color/value array in its current order. Types are the actual SDK predefined/user-defined choices; names are native strings, not a fixed template whitelist. One-color mode, tint and colors are explicit native stored properties; no synthesized shade formula or color evaluation is performed.";
        var operations=new JsonArray();foreach(var fill in new[]{solid,pattern,gradient})
        {
            var handles=Array(new JsonObject{["type"]="string",["pattern"]="^[0-9A-Fa-f]{1,16}$"});handles["minItems"]=1;handles["maxItems"]=1;
            var operation=Object(("operation",new JsonObject{["const"]="set_hatch_fill"}),("handles",handles),
                ("fill_token",new JsonObject{["type"]="string",["pattern"]="^[0-9A-Fa-f]{64}$"}),("fill",fill));
            operation["description"]="Choose or edit the active native fill of one existing Hatch: ordinary solid/line pattern or gradient. Read geometry.fill_editing and use its non-null fresh fill_token. The same resident Hatch keeps stored boundaries, plane, island handling, readable owned data and local bidirectional associations; no source curve is rebuilt or detached. Distinct Hatch targets can form a dedicated fill batch. Field-bearing/annotative/foreign or unclassified dependencies have no editing token; active settings may still be observed. Resident native name lookup and evaluation apply. Undo/rollback is not guaranteed; pending/unknown never means success.";
            operations.Add(operation);
        }
        return operations;
    }
    internal static JsonObject Read()
    {
        var gradient=Object(("gradient_type",Text()),("gradient_name",Text()),("angle",Number()),("shift",Number()),("one_color_mode",Bool()),("shade_tint",Number()),
            ("colors",Array(Object(("value",Number()),("color",ColorRead())))));
        var definition=Object(("angle",Number()),("base_point",Point2()),("offset",Point2()),("dashes",Array(Number())));
        var pattern=Object(("pattern_type",Text()),("pattern_name",Text()),("angle",Number()),("scale",Number()),("origin_ocs",Point2()),("pattern_double",Bool()),("spacing",Number()),("definitions",Array(definition)));
        var solid=Object(("pattern_name",Text()),("pattern_type",Text()));
        var read=Object(("fill_kind",Choice("solid","pattern","gradient")),("native_object_type",Text()),
            ("active_fill",new JsonObject{["oneOf"]=new JsonArray(solid,pattern,gradient)}),
            ("fill_token",Nullable(new JsonObject{["type"]="string",["pattern"]="^[0-9A-F]{64}$"})),
            ("source_handles",Nullable(Array(new JsonObject{["type"]="string",["pattern"]="^[0-9A-F]{1,16}$"}))),
            ("editing_unavailable_reason",Nullable(Text())),("observation_scope",Text()));
        read["description"]="Complete active stored native fill, including every gradient color/value or line definition; no generated-line/color evaluation. Editing availability is separate: a null token with an explicit reason does not hide the active settings. Inactive ordinary pattern fields in the outer geometry are null for a gradient.";
        return read;
    }
    internal static JsonObject Change(JsonNode entityRef)=>Object(("operation_index",new JsonObject{["type"]="integer",["minimum"]=0}),
        ("hatch_ref",entityRef),("fill_token",new JsonObject{["type"]="string",["pattern"]="^[0-9A-F]{64}$"}),
        ("fill_kind",Choice("solid","pattern","gradient")),("source_handles",Array(Text())),("warnings",Array(Text())));
}
