using System.Text.Json.Nodes;
using NanoCAD.Contracts;

namespace NanoCAD.McpServer;

internal static class MLeaderStyleSchema337
{
    private static JsonObject S()=>new(){["type"]="string"};
    private static JsonObject B()=>new(){["type"]="boolean"};
    private static JsonObject N()=>new(){["type"]="number"};
    private static JsonObject Enum(string[] values)=>new(){["type"]="string",["enum"]=new JsonArray(values.Select(value=>(JsonNode?)JsonValue.Create(value)).ToArray())};
    private static JsonObject I()=>new(){["type"]="integer"};
    private static JsonObject H()=>new(){["type"]="string",["pattern"]="^[0-9A-Fa-f]{1,16}$"};
    private static JsonObject O(params(string Name,JsonObject Schema)[] fields)
    {
        var properties=new JsonObject();foreach(var field in fields)properties[field.Name]=field.Schema;
        return new(){["type"]="object",["additionalProperties"]=false,["properties"]=properties,
            ["required"]=new JsonArray(fields.Select(field=>(JsonNode?)JsonValue.Create(field.Name)).ToArray())};
    }
    private static JsonObject Nullable(JsonObject value)=>new(){["anyOf"]=new JsonArray(value,new JsonObject{["type"]="null"})};
    private static JsonObject A(JsonObject value)=>new(){["type"]="array",["items"]=value};
    internal static IEnumerable<JsonObject> Operations()
    {
        foreach(string operation in MLeaderStyleValidation337.Operations)
        {
            bool creating=operation=="create_mleader_style";
            JsonObject Size(bool positive)=>new(){["type"]="number",[positive?"exclusiveMinimum":"minimum"]=0};
            JsonObject Aci()=>new(){["type"]="integer",["minimum"]=0,["maximum"]=256};
            JsonObject Rgb()=>new(){["type"]="array",["minItems"]=3,["maxItems"]=3,["items"]=new JsonObject{["type"]="integer",["minimum"]=0,["maximum"]=255}};
            var fields=O(("text_style_handle",H()),("text_height",Size(true)),("arrow_size",Size(false)),("landing_gap",Size(false)),
                ("dogleg_length",Size(false)),("enable_landing",B()),("enable_dogleg",B()),("scale",Size(true)),("leader_linetype_handle",H()),
                ("leader_lineweight_hundredth_mm",new JsonObject{["type"]="integer",["enum"]=new JsonArray(ModifyValidation.Lineweights.Select(v=>(JsonNode?)JsonValue.Create(v)).ToArray())}),
                ("leader_color_aci",Aci()),("leader_color_rgb",Rgb()),("text_color_aci",Aci()),("text_color_rgb",Rgb()),
                ("enable_frame_text",B()),("text_alignment",Enum(MLeaderStyleValidation337.TextAlignments)),("text_attachment_type",Enum(MLeaderStyleValidation337.HorizontalAttachments)),("text_angle_type",Enum(MLeaderStyleValidation337.TextAngles)),
                ("text_attachment_left",Enum(MLeaderStyleValidation337.HorizontalAttachments)),("text_attachment_right",Enum(MLeaderStyleValidation337.HorizontalAttachments)));
            fields.Remove("required");if(!creating)fields["minProperties"]=1;
            fields["allOf"]=new JsonArray(new JsonObject{["not"]=new JsonObject{["required"]=new JsonArray("leader_color_aci","leader_color_rgb")}},
                new JsonObject{["not"]=new JsonObject{["required"]=new JsonArray("text_color_aci","text_color_rgb")}},
                new JsonObject{["not"]=new JsonObject{["required"]=new JsonArray("text_attachment_type","text_attachment_left")}},
                new JsonObject{["not"]=new JsonObject{["required"]=new JsonArray("text_attachment_type","text_attachment_right")}});
            fields["description"]="Sparse stored defaults; omitted fields and content/arrow symbol/unselected attachment flags remain. Frame and native left/center/right text alignment are sparse defaults. text_angle_type selects the native InsertAngle/HorizontalAngle/AlwaysRightReadingAngle mode, not a numeric rotation. text_attachment_left/right independently set actual horizontal LeftLeader/RightLeader defaults; text_attachment_type sets both and cannot combine with side fields. The actual attachment direction is retained; side requests require horizontal direction. Lengths are native stored drawing-unit values, scale is dimensionless; no implicit conversion. Local text-style/linetype handles come from get_structure. Native ACI0 ByBlock/256 ByLayer or exclusive true RGB; lineweight is hundredths of a millimeter with native inheritance.";
            var token=new JsonObject{["type"]="string",["pattern"]="^[0-9A-F]{64}$",["description"]="Fresh complete get_structure mleader_styles properties.editing.style_token for the base/selected style."};
            var name=new JsonObject{["type"]="string",["minLength"]=1,["maxLength"]=255};
            var edit=creating?O(("operation",new JsonObject{["type"]="string",["const"]=operation}),("name",name),("base_style_handle",H()),("base_style_token",token),("properties",fields)):
                O(("operation",new JsonObject{["type"]="string",["const"]=operation}),("handle",H()),("expected_name",name),("style_token",token),("properties",fields));
            edit["description"]="One native ordinary local non-annotative MText-content MLeaderStyle per request. "+(creating?
                "Create a reusable native style from a checked detached clone of an existing base, without overwriting it or selecting a new current style. Base-owned extension dictionaries require their cloning adapter. Base consumers are not edited: copy verifies their actual local identities/style links/properties/readable owned metadata without requiring field/context/content edit eligibility. Opaque native field/context internals are not claimed observed.":
                "Edit the existing style defaults in place; existing native leaders keep their style links/content/owners and per-entity overrides. Inherited rendering may legitimately reflow; no coordinate reconstruction or override reset.")+
                " Default template raw content and unselected stored settings remain; explicitly requested angle/alignment/side attachments may change their native derived direction/attachment and used text presentation, with actual before/after observations and rendered review; Field-bearing styles/templates and foreign persistent dependencies need their owning adapter. Changed inherited fonts are checked against visible engineering text where native provenance permits; inline/unknown override/font coverage is reported, not claimed complete. Current document defaults remain. Native rollback/Undo is not guaranteed; inspect the actual useful rendered result after the group.";
            yield return edit;
        }
    }
    internal static JsonObject ReadProperties()
    {
        JsonObject Color()=>O(("method",S()),("aci",Nullable(I())),("rgb",Nullable(A(I()))));
        var settings=O(("text_style_handle",Nullable(H())),("text_height",N()),("arrow_size",N()),("landing_gap",N()),("dogleg_length",N()),
            ("enable_landing",B()),("enable_dogleg",B()),("scale",N()),("leader_linetype_handle",Nullable(H())),("leader_lineweight_hundredth_mm",I()),
            ("leader_color",Color()),("text_color",Color()),("arrow_symbol_handle",Nullable(H())),("content_type",S()),("annotative",S()),("leader_line_type",S()),
            ("text_alignment",S()),("text_angle_type",S()),("text_attachment_direction",S()),("text_attachment_type",S()),("text_attachment_left",S()),("text_attachment_right",S()),("enable_frame_text",B()),
            ("default_mtext",Nullable(O(("has_fields",B()),("column_type",S()),("content_utf8_bytes",I())))));
        var result=O(("native_type",new JsonObject{["type"]="string",["const"]="MLeaderStyle"}),("is_local",B()),("can_reference",B()),("is_current",B()),
            ("settings",settings),("editing",O(("style_token",new JsonObject{["type"]="string",["pattern"]="^[0-9A-F]{64}$"}))),
            ("editing_unavailable_reason",S()),("lifecycle",NativeStyleLifecycleSchema352.Read()),("creation_candidate",B()));
        result["required"]=new JsonArray("native_type","is_local","can_reference","is_current");
        result["description"]="Additive native stored-default readback and fresh complete editing token. creation_candidate is available only after the local complete source-scope checks; true also requires no base-owned extension dictionary, while false does not prevent supported in-place style edits. It is a preflight scope candidate, not a guarantee of clone/runtime success. Optional unavailable metadata does not make a reusable style unreadable. Settings preserve raw native units; effective leader overrides are separate and no complete mask getter is implied.";
        return result;
    }
    internal static JsonObject LeaderReadSchema()
    {
        JsonObject Color()=>O(("method",S()),("aci",Nullable(I())),("rgb",Nullable(A(I()))));
        var settings=O(("text_style_handle",Nullable(H())),("text_height",N()),("arrow_size",N()),("landing_gap",N()),("dogleg_length",N()),
            ("enable_landing",B()),("enable_dogleg",B()),("scale",N()),("leader_linetype_handle",Nullable(H())),("leader_lineweight_hundredth_mm",I()),
            ("leader_color",Color()),("text_color",Color()),("enable_frame_text",B()),("text_alignment",S()),("text_angle_type",S()),("text_attachment_direction",S()),("text_attachment_type",S()),("text_attachment_left",S()),("text_attachment_right",S()));
        return O(("style_ref",O(("bridge_instance_id",S()),("document_id",S()),("handle",H()))),("effective_settings",settings),
            ("complete_override_mask_available",new JsonObject{["type"]="boolean",["const"]=false}));
    }
}
