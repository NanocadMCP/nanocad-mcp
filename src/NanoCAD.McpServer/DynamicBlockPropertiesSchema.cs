using System.Text.Json.Nodes;

namespace NanoCAD.McpServer;

internal static class DynamicBlockPropertiesSchema
{
    internal static JsonObject Operation()
    {
        JsonObject Value(string type, JsonObject schema) => new()
        {
            ["type"] = "object",
            ["additionalProperties"] = false,
            ["properties"] = new JsonObject { ["type"] = new JsonObject { ["type"] = "string", ["const"] = type }, ["value"] = schema },
            ["required"] = new JsonArray("type", "value")
        };
        JsonObject Point(int count) => new() { ["type"] = "array", ["minItems"] = count, ["maxItems"] = count, ["items"] = new JsonObject { ["type"] = "number" } };
        return new JsonObject
        {
            ["type"] = "object",
            ["additionalProperties"] = false,
            ["properties"] = new JsonObject
            {
                ["operation"] = new JsonObject { ["type"] = "string", ["const"] = "set_dynamic_block_property" },
                ["handles"] = new JsonObject { ["type"] = "array", ["minItems"] = 1, ["maxItems"] = 1, ["items"] = new JsonObject { ["type"] = "string", ["pattern"] = "^[0-9A-Fa-f]{1,16}$" } },
                ["expected_properties_token"] = new JsonObject { ["type"] = "string", ["pattern"] = "^[0-9A-Fa-f]{64}$" },
                ["property_index"] = new JsonObject { ["type"] = "integer", ["minimum"] = 0, ["maximum"] = int.MaxValue },
                ["property_name"] = new JsonObject { ["type"] = "string" },
                ["value"] = new JsonObject
                {
                    ["type"] = "object",
                    ["additionalProperties"] = false,
                    ["properties"] = new JsonObject
                    {
                        ["type"] = new JsonObject { ["type"] = "string", ["enum"] = new JsonArray("double", "int16", "int32", "string", "point2d", "point3d") },
                        ["value"] = new JsonObject { ["type"] = new JsonArray("number", "string", "array"), ["minItems"] = 2, ["maxItems"] = 3, ["items"] = new JsonObject { ["type"] = "number" } }
                    },
                    ["required"] = new JsonArray("type", "value"),
                    ["oneOf"] = new JsonArray(
                        Value("double", new() { ["type"] = "number" }),
                        Value("int16", new() { ["type"] = "integer", ["minimum"] = short.MinValue, ["maximum"] = short.MaxValue }),
                        Value("int32", new() { ["type"] = "integer", ["minimum"] = int.MinValue, ["maximum"] = int.MaxValue }),
                        Value("string", new() { ["type"] = "string" }), Value("point2d", Point(2)), Value("point3d", Point(3)))
                }
            },
            ["required"] = new JsonArray("operation", "handles", "expected_properties_token", "property_index", "property_name", "value"),
            ["description"] = "Set one observed editable native property of an existing local dynamic BlockReference in the active Model or ordinary paper space. Read geometry.dynamic_properties first and copy its complete token, current zero-based property_index and exact property_name. The index selects only that current collection snapshot; it is not persistent identity and duplicate names are never resolved by name alone. Use the observed native CLR value type and exact native allowed values when supplied. UnitsType is a native category; numeric values and parameter points keep their native units and frame without conversion. An empty allowed-values list does not prove an unrestricted range. ReadOnly properties are never set. The native evaluator owns dependent property values, visibility state and the actual anonymous representation; receipts report before/after collection snapshots and fresh inventory without inventing dependency edges or property correspondence after regeneration. Preserve the root identity/placement and owned attribute identity/text/format. This does not author or convert dynamic definitions, rebuild geometry by coordinates or synchronize attributes. One property per request avoids invalid collection wrappers after native evaluation. validate_only checks current ownership, token, type, readOnly and enumerated values without calling the setter; resulting geometry is not predicted. Undo/rollback is not guaranteed; an unknown result requires inspection before another write."
        };
    }

    private static JsonObject Text() => new() { ["type"] = "string" };
    private static JsonObject Number() => new() { ["type"] = "number" };
    private static JsonObject Integer() => new() { ["type"] = "integer" };
    private static JsonObject Flag() => new() { ["type"] = "boolean" };
    private static JsonObject Constant(string value) => new() { ["type"] = "string", ["const"] = value };
    private static JsonObject Nullable(JsonObject value) => new() { ["anyOf"] = new JsonArray(value, new JsonObject { ["type"] = "null" }) };
    private static JsonObject Array(JsonObject item) => new() { ["type"] = "array", ["items"] = item };
    private static JsonObject Object(params (string Name, JsonNode Value)[] fields)
    {
        var properties = new JsonObject(); var required = new JsonArray();
        foreach (var (name, value) in fields) { properties[name] = value; required.Add(name); }
        return new() { ["type"] = "object", ["additionalProperties"] = false, ["properties"] = properties, ["required"] = required };
    }
    private static JsonObject Point(int dimensions) => new()
    {
        ["type"] = "array",
        ["minItems"] = dimensions,
        ["maxItems"] = dimensions,
        ["items"] = Number(),
        ["description"] = "Actual native parameter coordinates in the native frame; no WCS, sheet or unit conversion is implied."
    };
    private static JsonObject NativeObjectIdentity()
    {
        var nullId = Object(("is_null", new JsonObject { ["const"] = true }), ("database_scope", Constant("none")));
        var observed = Object(("is_null", new JsonObject { ["const"] = false }), ("is_valid", Flag()),
            ("handle", Nullable(new JsonObject { ["type"] = "string", ["pattern"] = "^[0-9A-Fa-f]{1,16}$" })),
            ("raw_id_token", new JsonObject { ["type"] = "string", ["pattern"] = "^[0-9A-Fa-f]{16}$" }),
            ("is_erased", Nullable(Flag())), ("is_effectively_erased", Nullable(Flag())),
            ("database_scope", new JsonObject { ["type"] = "string", ["enum"] = new JsonArray("unavailable", "current_document", "foreign_database") }),
            ("identity_scope", Constant("current_native_ObjectId; no_foreign_EntityRef_or_persistent_alias")));
        var unavailable = Object(("is_null", new JsonObject { ["const"] = false }),
            ("observation_error", Text()), ("database_scope", Constant("unavailable")));
        return new()
        {
            ["oneOf"] = new JsonArray(nullId, observed, unavailable),
            ["description"] = "Raw actual native ObjectId observation. Handle and runtime token do not create an EntityRef for a foreign database or a persistent identity alias. Null, fully observed and observation-error states remain distinct."
        };
    }
    private static JsonObject TypedOutputValue(string type, JsonObject value) => Object(("type", Constant(type)), ("value", value));
    private static JsonObject ObservedValue() => new()
    {
        ["type"] = "object",
        ["additionalProperties"] = false,
        ["properties"] = new JsonObject
        {
            ["type"] = new JsonObject { ["type"] = "string", ["enum"] = new JsonArray("double", "int16", "int32", "string", "point2d", "point3d", "object_id", "null", "unsupported") },
            ["value"] = new JsonObject { ["type"] = new JsonArray("number", "string", "array", "object", "null") }
        },
        ["required"] = new JsonArray("type", "value"),
        ["oneOf"] = new JsonArray(TypedOutputValue("double", Number()),
            TypedOutputValue("int16", new() { ["type"] = "integer", ["minimum"] = short.MinValue, ["maximum"] = short.MaxValue }),
            TypedOutputValue("int32", new() { ["type"] = "integer", ["minimum"] = int.MinValue, ["maximum"] = int.MaxValue }),
            TypedOutputValue("string", Text()), TypedOutputValue("point2d", Point(2)), TypedOutputValue("point3d", Point(3)),
            TypedOutputValue("object_id", NativeObjectIdentity()),
            TypedOutputValue("null", new() { ["type"] = "null" }), TypedOutputValue("unsupported", new() { ["type"] = "null" })),
        ["description"] = "Observed native CLR value with its exact type tag. object_id, null and unsupported are read-only observations, not setter types. Numeric values and points retain native units and frame without conversion."
    };
    private static JsonObject RequestedValue()
    {
        var result = ObservedValue();
        var kinds = result["properties"]!["type"]!["enum"]!.AsArray();
        var variants = result["oneOf"]!.AsArray();
        for (int index = variants.Count - 1; index >= 6; index--) { variants.RemoveAt(index); kinds.RemoveAt(index); }
        result["description"] = "The requested value in one of the six admitted native setter types, with no numeric coercion or unit/frame conversion.";
        return result;
    }
    private static JsonObject PropertyInfo()
    {
        var editable = Flag();
        editable["description"] = "Observed native type, ReadOnly and value-completeness eligibility only. This is not write authorization: current owner, space/layer and dependency guards can still refuse a write.";
        var allowed = Array(ObservedValue());
        allowed["description"] = "Actual native enumerated values. An empty list does not establish an unrestricted range.";
        return Object(("index", new JsonObject { ["type"] = "integer", ["minimum"] = 0 }),
            ("name", Text()), ("description", Text()),
            ("native_type_code", new JsonObject { ["type"] = "integer", ["minimum"] = short.MinValue, ["maximum"] = short.MaxValue }),
            ("native_block_identity", TypedOutputValue("object_id", NativeObjectIdentity())),
            ("units_type", Integer()), ("units_type_name", Text()), ("value_native_type", Text()),
            ("value", ObservedValue()), ("allowed_values", allowed), ("read_only", Flag()), ("show", Flag()),
            ("visible_in_current_visibility_state", Flag()), ("editable", editable), ("unavailable_reason", Nullable(Text())));
    }
    internal static JsonObject Inventory(JsonObject reference)
    {
        var inventory = Object(("instance_ref", reference.DeepClone()), ("dynamic_definition_ref", reference.DeepClone()),
            ("representation_ref", reference.DeepClone()), ("anonymous_representation_ref", Nullable(reference.DeepClone().AsObject())),
            ("properties_token", new JsonObject { ["type"] = "string", ["pattern"] = "^[0-9A-Fa-f]{64}$" }),
            ("selector_scope", Text()), ("value_scope", Text()), ("properties", Array(PropertyInfo())),
            ("complete", Flag()), ("geometry_prediction_available", Flag()));
        inventory["description"] = "Current native dynamic occurrence property snapshot, actual canonical definition and representation identities. Property indices select this collection snapshot only, never persistent parameter correspondence. Native units/frame remain unconverted; reading the collection does not predict evaluator geometry.";
        return inventory;
    }
    internal static JsonObject Change(JsonObject reference)
    {
        var property = Object(("property_index", new JsonObject { ["type"] = "integer", ["minimum"] = 0 }),
            ("property_name", Text()), ("previous_state", Nullable(PropertyInfo())), ("state", Nullable(PropertyInfo())), ("requested", Flag()));
        property["description"] = "Before/after collection-slot observation. Null records an absent slot, not a fabricated parameter identity or dependency edge after native regeneration.";
        JsonObject AttributeState() => new()
        {
            ["type"] = "object",
            ["additionalProperties"] = true,
            ["description"] = "Actual opaque native attribute snapshot. Returned members preserve observed identity, text, formatting and native geometry without constructing replacement identities."
        };
        var attribute = Object(("attribute", reference.DeepClone()), ("previous_state", AttributeState()), ("state", AttributeState()));
        return Object(("operation_index", new JsonObject { ["type"] = "integer", ["minimum"] = 0 }),
            ("source", reference.DeepClone()), ("dynamic_definition", reference.DeepClone()),
            ("previous_representation", reference.DeepClone()), ("representation", reference.DeepClone()),
            ("property_index", new JsonObject { ["type"] = "integer", ["minimum"] = 0 }), ("property_name", Text()),
            ("requested_value", RequestedValue()), ("native_value", ObservedValue()),
            ("comparison_scope", Text()), ("property_changes", Array(property)), ("attribute_changes", Array(attribute)),
            ("previous_attribute_order", Array(reference.DeepClone().AsObject())), ("attribute_order", Array(reference.DeepClone().AsObject())),
            ("inventory", Inventory(reference)));
    }
}
