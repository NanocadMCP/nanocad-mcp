using System.Text.Json.Nodes;

namespace NanoCAD.McpServer;
internal static class ExtendedAnnotationSchema356
{
    static JsonObject Text()=>new(){["type"]="string"};
    static JsonObject InputStyle()=>new(){["type"]="string",["minLength"]=1,["maxLength"]=255};
    static JsonObject InputCodes()=>new(){["type"]="string",["minLength"]=1,["description"]="Supplied native frame codes/text, including native rows/newlines; no fields or other control bytes. Whole request byte budget applies."};
    static JsonObject Number()=>new(){["type"]="number"};
    static JsonObject InputNumber()=>new(){["type"]="number",["minimum"]=-1e9,["maximum"]=1e9};
    static JsonObject Point(int size=2)=>new(){["type"]="array",["minItems"]=size,["maxItems"]=size,["items"]=Number()};
    static JsonObject InputPoint()=>new(){["type"]="array",["minItems"]=2,["maxItems"]=2,["items"]=InputNumber()};
    static JsonObject Object(params(string Name,JsonNode Value)[] fields)=>new(){["type"]="object",["additionalProperties"]=false,
        ["required"]=new JsonArray(fields.Select(f=>(JsonNode?)JsonValue.Create(f.Name)).ToArray()),["properties"]=new JsonObject(fields.Select(f=>new KeyValuePair<string,JsonNode?>(f.Name,f.Value)))};
    static JsonObject Nullable(JsonObject value)=>new(){["anyOf"]=new JsonArray(value,new JsonObject{["type"]="null"})};
    internal static IEnumerable<JsonObject> Creates()
    {
        foreach(string kind in new[]{"arc_length","jogged_radial"})
        {
            var fields=new List<(string,JsonNode)>{("type",new JsonObject{["const"]="Dimension"}),("kind",new JsonObject{["const"]=kind}),("dimension_style",InputStyle())};
            foreach(string n in kind=="arc_length"?new[]{"center","xline1","xline2","arc_point"}:new[]{"center","chord_point","override_center","jog_point"})fields.Add((n,InputPoint()));
            if(kind=="jogged_radial")fields.Add(("jog_angle",new JsonObject{["type"]="number",["exclusiveMinimum"]=0,["exclusiveMaximum"]=Math.PI,["description"]="Native jog angle in radians, selected for the intended drawing."}));
            var value=Object(fields.ToArray());value["properties"]!["layer"]=InputStyle();value["properties"]!["text_override"]=new JsonObject{["type"]="string",["minLength"]=2,["maxLength"]=256,["description"]="Plain literal affixes around exactly one <> measurement marker; no fields or format codes."};
            value["description"]=kind=="arc_length"?"Actual native ArcDimension in WCS/sheet XY. Equal-radius endpoint rays are ordered counterclockwise; arc_point selects placement inside that sector. Measurement is native arc length, not chord length. Plain <> affixes preserve the measurement. No source association is created.":
                "Actual native RadialDimensionLarge. center and chord_point define the real radius; override_center and jog_point control the drawing representation without moving source geometry. jog_angle is radians. No guessed default standard or source association.";
            yield return value;
        }
        var tolerance=Object(("type",new JsonObject{["const"]="Tolerance"}),("codes",InputCodes()),("position",InputPoint()),("dimension_style",InputStyle()));
        tolerance["properties"]!["rotation"]=InputNumber();tolerance["properties"]!["layer"]=InputStyle();
        tolerance["description"]="Actual native FeatureControlFrame with supplied raw native codes/text, existing local dimension style and WCS/sheet XY position/rotation radians. Native rows/newlines are accepted; fields/control bytes are excluded. Request byte budget applies. The adapter does not select or validate GD&T/GOST/ISO semantics; use the drawing requirements and inspect actual glyphs. No lines/text reconstruction or automatic source association.";
        yield return tolerance;
    }
    internal static JsonObject Edit()
    {
        var props=new JsonObject{["codes"]=InputCodes(),["position"]=InputPoint(),["rotation"]=InputNumber(),["dimension_style_handle"]=BasicModelingSchema.Handle(),["text_height"]=InputNumber(),["dimension_scale"]=InputNumber(),["text_gap"]=InputNumber()};
        props["text_height"]!.AsObject()["exclusiveMinimum"]=0;props["dimension_scale"]!.AsObject()["exclusiveMinimum"]=0;
        var fields=new JsonObject{["type"]="object",["additionalProperties"]=false,["minProperties"]=1,["properties"]=props};
        var handles=new JsonObject{["type"]="array",["minItems"]=1,["maxItems"]=1,["items"]=BasicModelingSchema.Handle()};
        var result=Object(("operation",new JsonObject{["const"]="set_tolerance"}),("handles",handles),("tolerance_token",new JsonObject{["type"]="string",["pattern"]="^[0-9A-Fa-f]{64}$"}),("properties",fields));
        result["description"]="Edit one current independent native FeatureControlFrame using its complete fresh geometry.tolerance_token. Native codes, XY placement, radians rotation, existing dimension-style link and effective text height/scale/gap are editable. A style assignment adopts the native frame's observed effective style behavior; all unrequested fields and shared styles stay checked. Associated/field/annotative frames need their owning adapter. No association or invented tolerance interpretation. Target must have only this operation; native preview and post-Commit fresh-read checks accompany uncertain recovery/Undo.";return result;
    }
    internal static JsonObject ArcDetails()
    {
        var result=Object(("is_partial",new JsonObject{["type"]="boolean"}),("arc_start_parameter",Nullable(Number())),("arc_end_parameter",Nullable(Number())),
            ("arc_symbol_type",new JsonObject{["type"]="integer"}),("has_leader",new JsonObject{["type"]="boolean"}),("leader1_point",Nullable(Point(3))),("leader2_point",Nullable(Point(3))),("scope",Text()));return result;
    }
    static JsonObject NativeColor()=>Object(("method",Text()),("ColorIndex",new JsonObject{["type"]="integer"}),("Red",new JsonObject{["type"]="integer"}),("Green",new JsonObject{["type"]="integer"}),("Blue",new JsonObject{["type"]="integer"}),
        ("HasColorName",new JsonObject{["type"]="boolean"}),("ColorName",Text()),("BookName",Text()));
    internal static JsonObject Read()=>Object(("annotation_kind",new JsonObject{["const"]="FeatureControlFrame"}),
        ("geometry",Object(("codes",Text()),("position",Point(3)),("normal",Point(3)),("direction",Point(3)),("dimension_style_handle",Nullable(Text())),("text_style_handle",Nullable(Text())),
            ("text_height",Number()),("dimension_scale",Number()),("text_gap",Number()),("border_color",NativeColor()),("text_color",NativeColor()))),
        ("editable",new JsonObject{["type"]="boolean"}),("tolerance_token",Nullable(Text())),("unavailable_reason",Nullable(Text())),("observation_scope",Text()));
    internal static JsonObject Change(JsonNode reference)=>Object(("operation_index",new JsonObject{["type"]="integer"}),("source_ref",reference.DeepClone()),("before",Read()),("after",Read()));
}
