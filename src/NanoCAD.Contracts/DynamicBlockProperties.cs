using System.Text.Json;
using System.Text.Json.Nodes;

namespace NanoCAD.Contracts;

public sealed record DynamicBlockPropertyValue(string type, JsonElement value);
public sealed record SetDynamicBlockPropertySpec(string Handle, string ExpectedPropertiesToken, int PropertyIndex,
    string PropertyName, DynamicBlockPropertyValue Value) : EditSpec("set_dynamic_block_property", [Handle]);

public sealed record DynamicBlockPropertyInfo(int index, string name, string description, short native_type_code,
    DynamicBlockPropertyValue native_block_identity,
    int units_type, string units_type_name, string value_native_type, DynamicBlockPropertyValue value,
    DynamicBlockPropertyValue[] allowed_values, bool read_only, bool show, bool visible_in_current_visibility_state,
    bool editable, string? unavailable_reason);
public sealed record DynamicBlockPropertiesInventory(EntityRef instance_ref, EntityRef dynamic_definition_ref,
    EntityRef representation_ref, EntityRef? anonymous_representation_ref, string properties_token, string selector_scope,
    string value_scope, DynamicBlockPropertyInfo[] properties, bool complete, bool geometry_prediction_available);
public sealed record DynamicBlockObservedPropertyChange(int property_index, string property_name,
    DynamicBlockPropertyInfo? previous_state, DynamicBlockPropertyInfo? state, bool requested);
public sealed record DynamicBlockAttributeChange(EntityRef attribute, JsonElement previous_state, JsonElement state);
public sealed record DynamicBlockPropertyChange(int operation_index, EntityRef source, EntityRef dynamic_definition,
    EntityRef previous_representation, EntityRef representation, int property_index, string property_name,
    DynamicBlockPropertyValue requested_value, DynamicBlockPropertyValue native_value, string comparison_scope,
    DynamicBlockObservedPropertyChange[] property_changes, DynamicBlockAttributeChange[] attribute_changes,
    EntityRef[] previous_attribute_order, EntityRef[] attribute_order,
    DynamicBlockPropertiesInventory inventory);

public static class DynamicBlockPropertiesValidation
{
    public const string Operation = "set_dynamic_block_property";
    public const string ComparisonScope = "before_selector_and_immediate_native_wrapper_witness; fresh_inventory_ordinal_comparison_is_not_persistent_identity";
    public static bool Handles(string operation) => operation == Operation;
    private static DomainException Invalid(string path, string message) =>
        new("INVALID_ARGUMENT", path + ": " + message, false, new { path });
    private static void Shape(JsonElement value, string path, params string[] fields)
    {
        if (value.ValueKind != JsonValueKind.Object) throw Invalid(path, "Expected object.");
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var field in value.EnumerateObject())
            if (!seen.Add(field.Name) || !fields.Contains(field.Name, StringComparer.Ordinal))
                throw Invalid(path + "." + field.Name, "Unknown or duplicate field.");
    }
    private static JsonElement Required(JsonElement value, string field, string path) =>
        value.TryGetProperty(field, out var found) ? found : throw Invalid(path + "." + field, "Required field is missing.");
    private static string Text(JsonElement value, string field, string path)
    {
        var found = Required(value, field, path);
        if (found.ValueKind != JsonValueKind.String) throw Invalid(path + "." + field, "Expected string.");
        return ReadString(found, path + "." + field);
    }
    private static string ReadString(JsonElement value, string path)
    {
        try { return value.GetString()!; }
        catch (InvalidOperationException) { throw Invalid(path, "Invalid Unicode string."); }
    }
    private static double Number(JsonElement value, string path)
    {
        if (value.ValueKind != JsonValueKind.Number || !value.TryGetDouble(out double number) || !double.IsFinite(number))
            throw Invalid(path, "Expected a finite native numeric value; no unit conversion is performed.");
        return number == 0 ? 0d : number;
    }
    public static DynamicBlockPropertyValue ParseValue(JsonElement input, string path)
    {
        Shape(input, path, "type", "value");
        string type = Text(input, "type", path); var value = Required(input, "value", path);
        JsonElement canonical;
        switch (type)
        {
            case "double": canonical = Wire.Element(Number(value, path + ".value")); break;
            case "int16":
                if (value.ValueKind != JsonValueKind.Number || !value.TryGetInt16(out short small))
                    throw Invalid(path + ".value", "Expected an exact Int16 native value.");
                canonical = Wire.Element(small); break;
            case "int32":
                if (value.ValueKind != JsonValueKind.Number || !value.TryGetInt32(out int integer))
                    throw Invalid(path + ".value", "Expected an exact Int32 native value.");
                canonical = Wire.Element(integer); break;
            case "string":
                if (value.ValueKind != JsonValueKind.String) throw Invalid(path + ".value", "Expected the exact native string.");
                canonical = Wire.Element(ReadString(value, path + ".value")); break;
            case "point2d":
            case "point3d":
                int dimensions = type == "point2d" ? 2 : 3;
                if (value.ValueKind != JsonValueKind.Array || value.GetArrayLength() != dimensions)
                    throw Invalid(path + ".value", "Use the exact native parameter point dimension, without frame conversion.");
                canonical = Wire.Element(value.EnumerateArray().Select((number, index) => Number(number, path + ".value[" + index + "]")).ToArray());
                break;
            default: throw Invalid(path + ".type", "Use the observed editable native value type: double, int16, int32, string, point2d or point3d.");
        }
        return new(type, canonical);
    }
    public static SetDynamicBlockPropertySpec Parse(JsonElement input, string path)
    {
        Shape(input, path, "operation", "handles", "expected_properties_token", "property_index", "property_name", "value");
        if (Text(input, "operation", path) != Operation) throw Invalid(path + ".operation", "Select set_dynamic_block_property.");
        var handles = Required(input, "handles", path);
        if (handles.ValueKind != JsonValueKind.Array || handles.GetArrayLength() != 1 || handles[0].ValueKind != JsonValueKind.String)
            throw Invalid(path + ".handles", "Select one actual existing dynamic BlockReference.");
        string suppliedHandle = ReadString(handles[0], path + ".handles[0]"), handle;
        try { handle = Validation.NormalizeHandle(suppliedHandle); }
        catch (DomainException error) { throw Invalid(path + ".handles[0]", error.Error.message); }
        string token = Text(input, "expected_properties_token", path);
        if (token.Length != 64 || token.Any(character => !Uri.IsHexDigit(character)))
            throw Invalid(path + ".expected_properties_token", "Use the current complete property inventory's SHA-256 token.");
        var index = Required(input, "property_index", path);
        if (index.ValueKind != JsonValueKind.Number || !index.TryGetInt32(out int ordinal) || ordinal < 0)
            throw Invalid(path + ".property_index", "Use the observed zero-based current snapshot index.");
        return new(handle, token.ToUpperInvariant(), ordinal, Text(input, "property_name", path),
            ParseValue(Required(input, "value", path), path + ".value"));
    }
    public static void Batch(IReadOnlyList<EditSpec> operations)
    {
        if (operations.Any(item => item is SetDynamicBlockPropertySpec) && operations is not [SetDynamicBlockPropertySpec])
            throw Invalid("$.operations", "Set one observed property in a separate request, then refresh its native collection before another property change.");
    }
    public static bool SameValue(DynamicBlockPropertyValue first, DynamicBlockPropertyValue second)
    {
        if (first.type != second.type) return false;
        return first.type switch
        {
            "double" => first.value.GetDouble() == second.value.GetDouble(),
            "int16" => first.value.GetInt16() == second.value.GetInt16(),
            "int32" => first.value.GetInt32() == second.value.GetInt32(),
            "string" => string.Equals(first.value.GetString(), second.value.GetString(), StringComparison.Ordinal),
            "point2d" or "point3d" => first.value.GetArrayLength() == second.value.GetArrayLength() &&
                first.value.EnumerateArray().Select(number => number.GetDouble()).SequenceEqual(second.value.EnumerateArray().Select(number => number.GetDouble())),
            _ => first.value.GetRawText() == second.value.GetRawText()
        };
    }
    // Native dynamic actions own orientation, location and visibility effects.
    // The operation reports those full snapshots but never grants permission
    // to replace identity, text, formatting, style or actual user bindings.
    public static JsonElement RetainedAttributeState(JsonElement snapshot)
    {
        var row = JsonNode.Parse(snapshot.GetRawText())!.AsObject();
        foreach (string field in new[] { "position", "alignment", "normal", "Rotation", "Invisible", "IsMirroredInX", "IsMirroredInY" }) row.Remove(field);
        if (row["display"]?["common"] is JsonObject display) display.Remove("visible");
        if (row["multiline"] is JsonObject multiline)
        {
            foreach (string field in new[] { "position", "normal", "direction" }) multiline.Remove(field);
            if (multiline["properties"]?["common"] is JsonObject appearance) appearance.Remove("visible");
        }
        return Wire.Element(row);
    }
    public static JsonElement RetainedRootState(JsonElement snapshot)
    {
        var row = JsonNode.Parse(snapshot.GetRawText())!.AsObject();
        // Collection order is observed separately; actual ObjectIds supply
        // correspondence and their complete set must still remain identical.
        row.Remove("attribute_ids"); return Wire.Element(row);
    }
    // Reserve known mandatory receipt duplication before a native setter. This
    // is transport admission, not a prediction of native geometry/lookup effects.
    public static ModifiedEntities ProjectedResult(ModifyArguments args, SetDynamicBlockPropertySpec edit,
        DynamicBlockPropertiesInventory before, EntityRef[]? attributes = null)
    {
        EntityRef MaximumRef() => new(args.BridgeId, args.DocumentId, "FFFFFFFFFFFFFFFF");
        var selected = before.properties[edit.PropertyIndex];
        var expected = selected with { value = edit.Value };
        var rows = before.properties.ToArray(); rows[edit.PropertyIndex] = expected;
        var inventory = before with { representation_ref = MaximumRef(), anonymous_representation_ref = MaximumRef(), properties = rows };
        var change = new DynamicBlockPropertyChange(0, before.instance_ref, before.dynamic_definition_ref,
            before.representation_ref, MaximumRef(), edit.PropertyIndex, edit.PropertyName, edit.Value, edit.Value, ComparisonScope,
            [new(edit.PropertyIndex, edit.PropertyName, selected, expected, true)], [], attributes ?? [], attributes ?? [], inventory);
        return new ModifiedEntities(args.OperationId, "committed", false, [before.instance_ref], [], [], [], "not_guaranteed")
        { dynamic_block_changes = [change] };
    }
}
