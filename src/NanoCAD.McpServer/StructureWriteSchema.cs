using System.Text.Json;
using System.Text.Json.Nodes;
using NanoCAD.Contracts;

namespace NanoCAD.McpServer;

internal static class StructureWriteSchema
{
    private static JsonObject Text(int max, int min = 1) => new() { ["type"] = "string", ["minLength"] = min, ["maxLength"] = max };
    private static JsonObject Object(JsonObject properties, params string[] required) => new()
    { ["type"] = "object", ["additionalProperties"] = false, ["properties"] = properties, ["required"] = new JsonArray(required.Select(s => (JsonNode?)JsonValue.Create(s)).ToArray()) };
    private static JsonObject Properties(bool create)
    {
        JsonObject Flag(bool initialFalse = false) => create && initialFalse ? new() { ["type"] = "boolean", ["const"] = false } : new() { ["type"] = "boolean" };
        var properties = Object(new JsonObject
        {
            ["color_aci"] = new JsonObject { ["type"] = "integer", ["minimum"] = 1, ["maximum"] = 255 },
            ["linetype"] = Text(255),
            ["lineweight_hundredth_mm"] = new JsonObject { ["type"] = "integer", ["enum"] = new JsonArray(StructureWriteValidation.LayerLineweights.Select(w => (JsonNode?)JsonValue.Create(w)).ToArray()) },
            ["is_off"] = Flag(true),
            ["is_frozen"] = Flag(true),
            ["is_locked"] = Flag(true),
            ["is_plottable"] = Flag()
        });
        properties["minProperties"] = 1;
        AppearancePropertySchema.ExtendInput(properties, layer: true);
        return properties;
    }
    public static JsonElement Input()
    {
        JsonObject Id() { var value = Text(36); value["format"] = "uuid"; return value; }
        JsonObject Handle() { var value = Text(16); value["pattern"] = "^[0-9A-Fa-f]{1,16}$"; return value; }
        JsonObject Kind(string name) => new() { ["type"] = "string", ["const"] = name };
        JsonObject AttributeText()
        {
            var value = Text(4096, 0);
            value["pattern"] = "^(?!.*(?:%<|%%))[^\\u0000-\\u001f\\u007f-\\u009f]*$";
            return value;
        }
        var revision = Text(20); revision["pattern"] = "^[0-9]{1,20}$";
        var create = Object(new JsonObject { ["operation"] = Kind("create_layer"), ["name"] = Text(255), ["properties"] = Properties(true) }, "operation", "name");
        create["description"] = "New ordinary layer only. Defaults: ACI 7, Continuous, Default lineweight, visible/thawed/unlocked/plottable. No overwrite; new handles become usable after commit.";
        var layer = Object(new JsonObject { ["operation"] = Kind("set_layer_properties"), ["handle"] = Handle(), ["properties"] = Properties(false) }, "operation", "handle", "properties");
        layer["description"] = "Edit an existing independent native layer, including0/Defpoints. The current layer cannot be switched off, frozen or locked; select a different current layer first. Unspecified properties are preserved.";
        var current = Object(new JsonObject { ["operation"] = Kind("set_current_layer"), ["handle"] = Handle(), ["expected_current_handle"] = Handle() }, "operation", "handle", "expected_current_handle");
        current["description"] = "Select an existing local native layer, including0, without changing entity assignments. Read get_structure layers is_current and provide its handle as expected_current_handle. Target must be visible, thawed and unlocked. One operation per request; allowed from Model or Paper.";
        var rename = Object(new JsonObject { ["operation"] = Kind("rename_layer"), ["handle"] = Handle(), ["expected_name"] = Text(255), ["name"] = Text(255) }, "operation", "handle", "expected_name", "name");
        rename["description"] = "Rename one ordinary independent native layer by existing handle and exact expected_name. Preserve layer identity, entity assignments, properties and current-layer identity. Separate batch of distinct sources/destinations. Destination must not exist, ignoring case; no swaps, case-only/no-op rename,0/Defpoints/special/dependent names, fields or persistent reactors.";
        var attribute = Object(new JsonObject
        {
            ["operation"] = Kind("set_attribute_text"),
            ["block_handle"] = Handle(),
            ["attribute_handle"] = Handle(),
            ["owner_definition_handle"] = Handle(),
            ["expected_definition_token"] = new JsonObject { ["type"] = "string", ["pattern"] = "^[0-9A-F]{64}$", ["minLength"] = 64, ["maxLength"] = 64 },
            ["expected_text"] = new JsonObject { ["type"] = "string", ["maxLength"] = 8192 },
            ["text"] = new JsonObject { ["type"] = "string", ["maxLength"] = 8192 },
            ["content_type"] = new JsonObject { ["type"] = "string", ["enum"] = new JsonArray("single_line", "mtext"), ["default"] = "single_line" }
        }, "operation", "block_handle", "attribute_handle", "expected_text", "text");
        attribute["description"] = "Replace variable occurrence text in an ordinary static top-level Model/active Paper insertion. For a shared nested value, supply owner_definition_handle plus expected_definition_token from inspect_block.nested_attribute_editing (same complete membership token), and actual direct block_handle/owned attribute_handle. A separate batch may edit distinct nested attributes of one shared owner definition. Its native members/defaults and every actual Model/Paper/nested consumer remain; only selected shared occurrence values change. This dimensionless content edit performs no insertion-unit conversion. Default content_type=single_line retains plain single-line semantics. content_type=mtext edits an existing native multiline attribute with canonical literal Contents (native paragraph/escape encoding), expected_text from inspect_block multiline.contents. Preserve its native MText definition/style/placement and the root identity/owner/tag. This does not convert kinds or redefine blocks. Read formatted/Field-bearing multiline content without truncation; its replacement, dynamic/xref editing and fit/aligned single-line labels need their owning routes. Omitting both new fields keeps the existing top-level insertion semantics.";
        attribute["allOf"] = new JsonArray(new JsonObject
        {
            ["if"] = new JsonObject { ["properties"] = new JsonObject { ["content_type"] = new JsonObject { ["const"] = "mtext" } }, ["required"] = new JsonArray("content_type") },
            ["then"] = new JsonObject
            {
                ["properties"] = new JsonObject
                {
                    ["expected_text"] = new JsonObject { ["type"] = "string", ["maxLength"] = 8192, ["description"] = "Exact current canonical Contents from multiline.contents. Existing codec limits:4096 decoded characters,8192 encoded characters,2048 per paragraph." },
                    ["text"] = new JsonObject { ["type"] = "string", ["maxLength"] = 8192, ["description"] = "Canonical literal MText: join paragraphs with native \\P; encode literal backslash as \\\\, braces as \\{ / \\}, caret as ^ followed by a space. Existing limits4096 decoded/8192 encoded/2048 per paragraph. No controls, Fields (%<), CAD percent codes (%%) or inline formatting. Server checks reversible exact canonical encoding." }
                }
            },
            ["else"] = new JsonObject { ["properties"] = new JsonObject { ["expected_text"] = AttributeText(), ["text"] = AttributeText() } }
        });
        var attributeConditions = (JsonArray)attribute["allOf"]!;
        attributeConditions.Add(new JsonObject { ["if"] = new JsonObject { ["required"] = new JsonArray("owner_definition_handle") }, ["then"] = new JsonObject { ["required"] = new JsonArray("expected_definition_token") } });
        attributeConditions.Add(new JsonObject { ["if"] = new JsonObject { ["required"] = new JsonArray("expected_definition_token") }, ["then"] = new JsonObject { ["required"] = new JsonArray("owner_definition_handle") } });
        var input = Object(new JsonObject
        {
            ["bridge_instance_id"] = Id(),
            ["document_id"] = Id(),
            ["expected_revision"] = revision,
            ["operation_id"] = Id(),
            ["units"] = new JsonObject { ["type"] = "string", ["const"] = "Millimeters" },
            ["target_space"] = new JsonObject
            {
                ["type"] = "string",
                ["enum"] = new JsonArray("model", "paper"),
                ["description"] = "Optional block insertion/attribute target; default Model. Paper targets the active ordinary sheet outside its model viewport. Explicit-layout and document-setting operations retain their own layout scope."
            },
            ["operations"] = new JsonObject
            {
                ["type"] = "array",
                ["minItems"] = 1,
                ["items"] = new JsonObject
                {
                    ["oneOf"] = new JsonArray(create, layer, current, rename, attribute, JsonNode.Parse(LayoutEditSchema.Edit(true).GetRawText()), JsonNode.Parse(LayoutEditSchema.Edit(false).GetRawText()),
                    JsonNode.Parse(DrawingViewCreateSchema.Create().GetRawText()), JsonNode.Parse(SectionViewSchema.Create().GetRawText()), JsonNode.Parse(BlockWriteSchema.Define().GetRawText()), JsonNode.Parse(BlockWriteSchema.Insert().GetRawText()),
                    JsonNode.Parse(StyleWriteSchema.SetFont().GetRawText()), JsonNode.Parse(StyleWriteSchema.SetProperties().GetRawText()), JsonNode.Parse(StyleWriteSchema.CreateTextStyle().GetRawText()), JsonNode.Parse(DimensionStyleWriteSchema.SetProperties().GetRawText()), JsonNode.Parse(DimensionStyleWriteSchema.Create().GetRawText()), JsonNode.Parse(LinetypeWriteSchema.Create().GetRawText()), JsonNode.Parse(ViewportWriteSchema.Edit().GetRawText()), JsonNode.Parse(LayoutCreateSchema.Create().GetRawText()), JsonNode.Parse(ViewportCreateSchema.Create().GetRawText()))
                },
                ["description"] = "Ordered structural request. Layer renames use a separate batch of distinct existing layers. Select a current layer/style, rename/delete an existing structure record, or edit one existing style/layout in a separate request. Delete unused layers/styles or an inactive empty Paper layout; ownership/current-record guards preserve existing drawing content. Existing viewport camera edits use their separate scope; ordinary rectangular viewport copy/delete groups are bounded by actual bytes. Document settings and explicit-layout operations retain their layout scope. Block target_space defaults to Model; Paper uses the active ordinary sheet outside its model viewport. Complete request, source observations and projected result use actual serialized-byte budgets. Dependencies must already exist; new handles become usable after commit. Undo/rollback limitations are disclosed by the operation and result; requests are not universally atomic. No implicit save."
            }
        }, "bridge_instance_id", "document_id", "expected_revision", "operation_id", "units", "operations");
        var choices = input["properties"]!["operations"]!["items"]!["oneOf"]!.AsArray();
        foreach (var reference in ExternalReferenceSchema385.Operations()) choices.Add(reference);
        choices.Add(JsonNode.Parse(BlockDefinitionRenameSchema349.Input().GetRawText()));
        choices.Add(JsonNode.Parse(BlockDefinitionGeometrySchema388.Input().GetRawText()));
        choices.Add(BlockFromObjectsSchema407.Input());
        foreach (var library in LibraryBlockDwgSchema.Operations()) choices.Add(library);
        foreach (var group in DrawingGroupSchema431.Input()) choices.Add(group);
        choices.Add(JsonNode.Parse(BlockDefinitionTextSchema391.Input().GetRawText()));
        choices.Add(JsonNode.Parse(BlockDefinitionMembersSchema392.Input().GetRawText()));
        choices.Add(NativeBlockArraySchema384.Insert());
        choices.Add(NativeBlockArraySchema384.Edit());
        foreach (var lifecycle in StructureLifecycleSchema.Operations()) choices.Add(lifecycle);
        foreach (var viewportLifecycle in ViewportLifecycleSchema.Operations()) choices.Add(viewportLifecycle);
        foreach (var tableStyle in TableStyleSchema337.Operations()) choices.Add(tableStyle);
        foreach (var leaderStyle in MLeaderStyleSchema337.Operations()) choices.Add(leaderStyle);
        foreach (var nativeStyle in NativeStyleLifecycleSchema352.Operations()) choices.Add(nativeStyle);
        foreach (var clipping in ViewportClippingSchema358.Operations()) choices.Add(clipping);
        choices.Add(JsonNode.Parse(DrawingViewEditSchema.Properties().GetRawText()));
        choices.Add(JsonNode.Parse(DrawingViewEditSchema.Delete().GetRawText()));
        choices.Add(JsonNode.Parse(DrawingViewLifecycleSchema345.Sources().GetRawText()));
        choices.Add(JsonNode.Parse(DrawingViewLifecycleSchema345.Rename().GetRawText()));
        choices.Add(JsonNode.Parse(DrawingViewLifecycleSchema345.Child().GetRawText()));
        choices.Add(SectionCutSchema351.Operation());
        input["properties"]!["operations"]!["anyOf"] = new JsonArray(
            new JsonObject { },
            new JsonObject { ["items"] = new JsonObject { ["properties"] = new JsonObject { ["operation"] = new JsonObject { ["const"] = "set_drawing_view_properties" } }, ["required"] = new JsonArray("operation") } },
            new JsonObject { ["items"] = new JsonObject { ["properties"] = new JsonObject { ["operation"] = new JsonObject { ["enum"] = new JsonArray("copy_viewport", "delete_viewport") } }, ["required"] = new JsonArray("operation") } });
        return JsonSerializer.SerializeToElement(input);
    }
}


