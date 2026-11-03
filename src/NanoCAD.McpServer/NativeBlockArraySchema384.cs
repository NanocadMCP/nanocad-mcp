using System.Text.Json;
using System.Text.Json.Nodes;

namespace NanoCAD.McpServer;

internal static class NativeBlockArraySchema384
{
    private static JsonObject Text() => new() { ["type"] = "string" };
    private static JsonObject Number() => new() { ["type"] = "number" };
    private static JsonObject Count() => new() { ["type"] = "integer", ["minimum"] = 1, ["maximum"] = 65535,
        ["description"] = "Positive native UInt16 count; this is the SDK representation, not an arbitrary instance ceiling." };
    private static JsonObject Handle() => new() { ["type"] = "string", ["pattern"] = "^[0-9A-Fa-f]{1,16}$", ["minLength"] = 1, ["maxLength"] = 16 };
    private static JsonObject Token() => new() { ["type"] = "string", ["pattern"] = "^[0-9A-Fa-f]{64}$", ["minLength"] = 64, ["maxLength"] = 64 };
    private static JsonObject Object(JsonObject properties, params string[] required) => new() { ["type"] = "object", ["additionalProperties"] = false,
        ["properties"] = properties, ["required"] = new JsonArray(required.Select(s => (JsonNode?)JsonValue.Create(s)).ToArray()) };
    private static JsonObject Properties() => new() { ["rows"] = Count(), ["columns"] = Count(),
        ["row_spacing"] = new JsonObject { ["type"] = "number", ["description"] = "Native signed row spacing in drawing millimeters; no implicit unit conversion." },
        ["column_spacing"] = new JsonObject { ["type"] = "number", ["description"] = "Native signed column spacing in drawing millimeters; no implicit unit conversion." } };
    internal static JsonObject Insert()
    {
        var source = JsonNode.Parse(BlockWriteSchema.Insert().GetRawText())!.AsObject();
        var properties = source["properties"]!.AsObject(); properties.Remove("attribute_values");
        properties["operation"] = new JsonObject { ["type"] = "string", ["const"] = "insert_block_array" };
        foreach (var pair in Properties()) properties.Add(pair.Key, pair.Value!.DeepClone());
        var required = source["required"]!.AsArray();
        foreach (string name in new[] { "rows", "columns", "row_spacing", "column_spacing" }) required.Add(name);
        source["description"] = "Create ONE native rectangular MInsertBlock from an existing supported ordinary static Millimeters definition without attributes. Read its definition handle first. Native rows/columns/spacings remain editable; all occurrences share that definition. WCS XY/Z0, +Z, positive uniform scale and radians rotation; target_space Model or active ordinary Paper. This is not an associative source/action network or a polar/path array. No independent copies or new source definition are manufactured. Choose counts/spacing/placement against existing content; Undo/recovery is not guaranteed.";
        return source;
    }
    internal static JsonObject Edit()
    {
        var properties = Object(Properties()); properties["minProperties"] = 1;
        var value = Object(new JsonObject { ["operation"] = new JsonObject { ["type"] = "string", ["const"] = "set_block_array_properties" },
            ["handle"] = Handle(), ["array_token"] = Token(), ["properties"] = properties }, "operation", "handle", "array_token", "properties");
        value["description"] = "Edit native rows/columns/row_spacing/column_spacing IN PLACE on one current local attribute-free MInsertBlock. Read inspect_objects geometry.array_token and the current parameters first; use the exact requested Model/active Paper target_space. Preserve the array handle, shared definition/members, placement, display and observed owned data. Unspecified values stay. Existing reflected XY frames are retained. Ordinary modify_entities move/copy/rotate/positive uniform scale/mirror/erase use the MInsert owning adapter within their published eligibility. Native MInsert explode returned NotApplicable in the assessed case and has no accepted useful result; this does not disable its placement or parameter edits. Associated/Field-bearing/annotative/custom roots and other native array families require their owning route. Counts use the native UInt16 representation; complete observations use actual serialized-byte limits. Undo/recovery is not guaranteed.";
        return value;
    }
    internal static JsonObject Read(JsonObject reference)
    {
        var point = new JsonObject { ["type"] = "array", ["minItems"] = 3, ["maxItems"] = 3, ["items"] = Number() };
        JsonObject Nullable(JsonObject value) => new() { ["anyOf"] = new JsonArray(value, new JsonObject { ["type"] = "null" }) };
        var properties = new JsonObject { ["inspection_scope"] = new JsonObject { ["const"] = "native_minsert_parameters" },
            ["native_type"] = new JsonObject { ["const"] = "MInsertBlock" }, ["owner_ref"] = reference.DeepClone(), ["definition_ref"] = reference.DeepClone(),
            ["position"] = point.DeepClone(), ["rotation"] = Number(), ["scale_factors"] = point.DeepClone(), ["normal"] = point.DeepClone(),
            ["rows"] = new JsonObject { ["type"] = "integer", ["minimum"] = 0, ["maximum"] = 65535 },
            ["columns"] = new JsonObject { ["type"] = "integer", ["minimum"] = 0, ["maximum"] = 65535 },
            ["row_spacing"] = Number(), ["column_spacing"] = Number(), ["instance_count"] = new JsonObject { ["type"] = "integer", ["minimum"] = 0 },
            ["array_token"] = Nullable(Token()), ["editing_unavailable_reason"] = Nullable(Text()),
            ["bounds"] = Nullable(Object(new JsonObject { ["min"] = point.DeepClone(), ["max"] = point.DeepClone() }, "min", "max")),
            ["bounds_unavailable_reason"] = Nullable(Text()) };
        var result = Object(properties, properties.Select(pair => pair.Key).ToArray());
        properties["bounds_kind"]=Nullable(new JsonObject{["type"]="string",["const"]="conservative_minsert_spacing_envelope",
            ["description"]="Whole-grid conservative spacing envelope, reused for candidate queries/framing; not exact native extents. Native getter may cover only the seed. No occurrence enumeration; may include extra area."});
        result["description"] = "Actual native MInsert parameters/placement and current owner/shared-definition identities. Counts reflect native UInt16 bits. Does not enumerate copies or claim associative/action dependencies. Optional complete edit token covers the supported root and unchanged shared source observation; unavailable edit scope retains the active parameter observation and reports a reason.";
        return result;
    }
}
