using System.Text.Json;
using System.Text.Json.Nodes;
using NanoCAD.Contracts;

namespace NanoCAD.McpServer;

internal static class StructureSchema
{
    private static JsonObject S() => new() { ["type"] = "string" };
    private static JsonObject B() => new() { ["type"] = "boolean" };
    private static JsonObject N() => new() { ["type"] = "number" };
    private static JsonObject I() => new() { ["type"] = "integer" };
    private static JsonObject R(string name) => new() { ["$ref"] = "#/$defs/" + name };
    private static JsonObject Null(JsonObject schema) => new() { ["anyOf"] = new JsonArray(schema, new JsonObject { ["type"] = "null" }) };
    private static JsonObject Array(JsonObject schema, int? size = null)
    {
        var result = new JsonObject { ["type"] = "array", ["items"] = schema };
        if (size.HasValue) { result["minItems"] = size.Value; result["maxItems"] = size.Value; }
        return result;
    }
    private static JsonObject P(int size = 3) => Array(N(), size);
    private static JsonObject O(params (string Name, JsonObject Schema)[] fields)
    {
        var properties = new JsonObject(); var required = new JsonArray();
        foreach (var field in fields) { properties[field.Name] = field.Schema; required.Add(field.Name); }
        return new() { ["type"] = "object", ["additionalProperties"] = false, ["properties"] = properties, ["required"] = required };
    }
    public static JsonElement Input(string operation)
    {
        JsonObject Id() => new() { ["type"] = "string", ["format"] = "uuid", ["minLength"] = 36, ["maxLength"] = 36 };
        var properties = new JsonObject { ["bridge_instance_id"] = Id(), ["document_id"] = Id(),
            ["expected_revision"] = new JsonObject { ["type"] = "string", ["pattern"] = "^[0-9]{1,20}$", ["maxLength"] = 20 } };
        var required = new JsonArray("document_id");
        if (operation == "get_structure")
        {
            properties["section"] = new JsonObject { ["type"] = "string", ["enum"] = new JsonArray(StructureValidation.Sections.Select(s => (JsonNode?)JsonValue.Create(s)).ToArray()) };
            properties["page_size"] = new JsonObject { ["type"] = "integer", ["minimum"] = 1, ["maximum"] = int.MaxValue, ["default"] = 100 };
            properties["cursor"] = new JsonObject { ["type"] = "string", ["minLength"] = 1, ["maxLength"] = 2048, ["description"] = "Opaque snapshot cursor; same document/section, expires in 60 seconds. Follow until complete." };
            required.Add("section");
        }
        else
        {
            properties["handle"] = new JsonObject { ["type"] = "string", ["minLength"] = 1, ["maxLength"] = 16, ["pattern"] = "^[0-9A-Fa-f]{1,16}$", ["description"] = "Exact native block definition from get_structure blocks, or top-level native BlockReference in Model/ordinary Paper from query_objects. Definition inspection reports parent_definition coordinates and direct member IDs without fabricating an insertion; occurrence inspection retains its actual owner. MText and independent Hatch loops/patterns remain in their parent definition frame; ordinary nested references expose the actual stored transform and attributes. definition_editing supplies the current token and supported direct-member geometry edit scope; definition_text_editing supplies literal shared Text/MText content readiness, exact stored contents and its own current token. nested_attribute_editing supplies actual direct nested variable attribute handles/contents, eligibility, the complete shared definition token and all actual consumers for set_attribute_text. No layout activation." };
            properties["max_depth"] = new JsonObject { ["type"] = "integer", ["minimum"] = 0, ["default"] = 4 };
            properties["max_nodes"] = new JsonObject { ["type"] = "integer", ["minimum"] = 1, ["maximum"] = int.MaxValue, ["default"] = int.MaxValue };
            required.Add("handle");
        }
        return JsonSerializer.SerializeToElement(new JsonObject { ["type"] = "object", ["additionalProperties"] = false, ["properties"] = properties, ["required"] = required });
    }
    public static JsonObject Output(string operation, JsonObject definitions)
    {
        definitions["block_info"] = O(("ref", R("ref")), ("name", S()), ("is_layout", B()), ("is_anonymous", B()), ("is_dynamic", B()),
            ("is_xref", B()), ("is_overlay", B()), ("is_dependent", B()), ("origin", P()), ("units", S()), ("external_path", Null(S())));
        definitions["block_info"]!["properties"]!["external_reference"]=ExternalReferenceSchema385.DefinitionRead(R("ref"));
        if (operation == "get_structure")
        {
            var layer = O(("is_off", B()), ("is_frozen", B()), ("is_locked", B()), ("is_dependent", B()), ("is_current", B()), ("is_plottable", B()),
                ("color", O(("method", S()), ("aci", Null(I())), ("rgb", Null(P())))), ("linetype", R("ref")), ("lineweight", I()));
            AppearancePropertySchema.ExtendRead(layer, layer: true);
            definitions["structure_record"] = O(("ref", R("ref")), ("kind", S()), ("name", S()), ("properties", new JsonObject { ["oneOf"] = new JsonArray(
                layer,
                O(("native_type",new JsonObject{["type"]="string",["const"]="Group"}),("description",S()),("selectable",B()),("member_handles",Array(S())),("member_count",I()),("group_token",S()),("members_complete",B())),
                JsonNode.Parse(LinetypeReadSchema.Properties().GetRawText()),
                JsonNode.Parse(StyleReadSchema.TextStyleProperties().GetRawText()),
                JsonNode.Parse(DimensionStyleReadSchema.Properties().GetRawText()),
                R("block_info"), JsonNode.Parse(LayoutReadSchema.Layout().GetRawText()), JsonNode.Parse(LayoutReadSchema.Viewport().GetRawText()),
                O(("native_type",S()),("is_local",B()),("can_reference",B())),
                O(("native_type",new JsonObject{["type"]="string",["const"]="TableStyle"}),("is_local",B()),("can_reference",B()),("is_current",B()),("table_style",TableStyleSchema337.ReadProperties())),
                O(("native_type",new JsonObject{["type"]="string",["const"]="TableStyle"}),("is_local",B()),("can_reference",B()),("is_current",B()),("table_style_unavailable_reason",S())),
                MLeaderStyleSchema337.ReadProperties(),
                ViewportClippingSchema358.ReadWithReason(R("ref"))
            ) }));
            return O(("section", S()), ("items", Array(R("structure_record"))), ("total_count", I()), ("returned_count", I()), ("complete", B()), ("next_cursor", Null(S())));
        }
        definitions["attribute_text_format"] = O(("text_style_ref",R("ref")),("text_style_name",S()),("width_factor",N()),("oblique",N()),
            ("horizontal_mode",S()),("vertical_mode",S()),("alignment_point",P()));
        definitions["attribute_text_format"]!["description"]="Stored DBText formatting; oblique is radians. alignment_point uses the attribute coordinate_system and transform_to_wcs, meaningful according to the stored alignment modes. Style references belong to the owning document.";
        definitions["attribute_multiline"] = O(("contents", S()), ("plain_text", Null(S())), ("position", P()), ("normal", P()), ("direction", P()),
            ("width", N()), ("text_height", N()), ("attachment", S()), ("line_spacing_style", S()), ("line_spacing_factor", N()), ("flow_direction", S()),
            ("text_style_ref", Null(R("ref"))), ("text_style_unavailable_reason", Null(S())),
            ("appearance", O(("background_fill", B()),("use_background_color", B()),("background_scale_factor",N()),
                ("background_color",O(("method",S()),("aci",Null(I())),("rgb",Null(P(3))))),
                ("background_transparency",O(("alpha",I()),("by_alpha",B()),("by_layer",B()),("by_block",B()))),
                ("column_type",S()),("column_width",N()),("column_gutter_width",N()),("column_auto_height",B()),("column_flow_reversed",B()),
                ("explicit_column_count",Null(I())),("explicit_column_heights",Array(N())),("dynamic_fixed_height",Null(N())),("properties",R("properties")))),
            ("has_fields", B()), ("can_replace_literal_contents", B()));
        definitions["attribute_multiline"]!["description"] = "Actual native multiline definition in the attribute coordinate_system. Contents are complete stored MText data, not fresh evaluated Field values. Native frame, background/transparency and explicit column settings are observed; generated glyph origins/heights are not a stored-definition oracle. A missing/foreign font identity is nullable with an explicit reason, not a guessed style. plain_text is null if the existing decoder cannot observe it. can_replace_literal_contents indicates content eligibility; insertion/layer/current-font/owned-data preflight still applies.";
        definitions["attribute_info"] = O(("ref", R("ref")), ("tag", S()), ("text", S()), ("position", P()), ("height", N()), ("rotation", N()),
            ("invisible", B()), ("constant", B()), ("coordinate_system", S()), ("transform_to_wcs", P(16)),("layer",R("layer")),("properties",R("properties")),("text_format",R("attribute_text_format")),("multiline",Null(R("attribute_multiline"))));
        definitions["insert_info"] = O(("definition", R("block_info")), ("position", P()), ("normal", P()), ("scale_factors", P()), ("rotation", N()),
            ("transform_to_parent", P(16)), ("transform_to_wcs", P(16)), ("attributes", Array(R("attribute_info"))), ("is_dynamic", B()), ("expansion", S()),("properties",R("properties")));
        definitions["block_node"] = O(("occurrence_path", Array(S())), ("summary", R("summary")), ("parent_to_wcs", P(16)),
            ("entity", Null(R("entity"))), ("insert", Null(R("insert_info"))), ("attribute_definition", Null(R("attribute_info"))));
        var inspection=O(("root",R("ref")),("nodes",Array(R("block_node"))),("complete",B()),("warnings",Array(S())));
        inspection["properties"]!["definition_editing"]=BlockDefinitionGeometrySchema388.Read(R("ref"));
        inspection["properties"]!["definition_text_editing"]=BlockDefinitionTextSchema391.Read(R("ref"));
        inspection["properties"]!["definition_membership"]=BlockDefinitionMembersSchema392.Read(R("ref"));
        inspection["properties"]!["nested_attribute_editing"]=NestedAttributeSchema393.Read(R("ref"));
        inspection["description"]="For a definition root, occurrence paths start with the definition handle and parent_to_wcs maps to that definition frame, not an invented WCS placement. Choose an actual referring insertion to obtain a drawing transform.";
        return inspection;
    }
}
