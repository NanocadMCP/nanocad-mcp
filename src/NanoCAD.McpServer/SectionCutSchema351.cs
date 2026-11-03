using System.Text.Json.Nodes;

namespace NanoCAD.McpServer;

internal static class SectionCutSchema351
{
    static JsonObject Text(string description,int maximum=4096)=>new(){["type"]="string",["minLength"]=1,["maxLength"]=maximum,["description"]=description};
    static JsonObject Handle(string description)=>new(){["type"]="string",["minLength"]=1,["maxLength"]=16,["pattern"]="^[0-9A-Fa-f]{1,16}$",["description"]=description};
    static JsonObject Id(string description)=>new(){["type"]="string",["pattern"]="^\\{[0-9A-Fa-f]{8}-[0-9A-Fa-f]{4}-[0-9A-Fa-f]{4}-[0-9A-Fa-f]{4}-[0-9A-Fa-f]{12}\\}$",["description"]=description};
    static JsonObject Token()=>new(){["type"]="string",["pattern"]="^[0-9A-Fa-f]{64}$"};
    static JsonObject Array(JsonNode value,int minimum=0)=>new(){["type"]="array",["minItems"]=minimum,["items"]=value};
    static JsonObject Object(params (string Name,JsonNode Value)[] fields)=>new(){["type"]="object",["additionalProperties"]=false,
        ["required"]=new JsonArray(fields.Select(field=>(JsonNode?)JsonValue.Create(field.Name)).ToArray()),
        ["properties"]=new JsonObject(fields.Select(field=>new KeyValuePair<string,JsonNode?>(field.Name,field.Value)))};
    internal static JsonObject Operation()
    {
        var target=Object(("layout_handle",Handle("Current owning layout handle.")),("handle",Handle("Exact current view handle.")),("expected_name",Text("Current native name.")),("view_token",Token()));
        var point=new JsonObject{["type"]="array",["minItems"]=3,["maxItems"]=3,["items"]=new JsonObject{["type"]="number",["minimum"]=-1e6,["maximum"]=1e6}};
        var line=Array(point,2);line["maxItems"]=2;
        var result=Object(("operation",new JsonObject{["const"]="set_section_view_cut"}),
            ("section_id",Id("Current exact native Section ID, not a drawing handle.")),("section_token",Token()),("targets",Array(target,1)),("section_line",line));
        result["description"]="Move or resize an existing ordinary two-point native Plane section cut in its retained viewing/vertical basis. Select every associated view and descendant, possibly on different existing layouts, with fresh get_drawing_views tokens. The native section/view identities, explicit selected Model sources, placed view planes/scales/settings and model bodies remain. It regenerates the retained views; no new Section, source reconstruction, owner reset, live cut, offset/jog/notch edit or layout transfer. Read section_cut metadata first. Recovery and Undo are not guaranteed.";return result;
    }
    internal static JsonObject Read(JsonNode reference)=>Object(("section_ref",reference.DeepClone()),("section_id",Text("Actual native section ID.")),("section_token",Token()),("scope",Text("Resident two-point native plane cut; complete section/root token.")));
    internal static JsonObject Change(JsonNode reference)=>Object(("operation_index",new JsonObject{["type"]="integer"}),
        ("section_ref",reference.DeepClone()),("section_id",Text("Retained native section ID.")),("section_token",Token()),("view_refs",Array(reference.DeepClone())),
        ("processing_count",new JsonObject{["type"]="integer",["minimum"]=0}),("processing_inventory_sha256",Token()),
        ("processing_scope",Text("SDK configuration-derived processing inventory; each view retains its separate explicit selected sources.")));
}
