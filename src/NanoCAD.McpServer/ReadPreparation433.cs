using System.Text.Json;
using System.Text.Json.Nodes;
using ModelContextProtocol;
using NanoCAD.Contracts;

namespace NanoCAD.McpServer;

public sealed record ReadPreparationSelection433(string Operation, string? Variant, JsonElement Schema, JsonElement Branch);

// Stateless projection of an explicitly requested read. Never submits a write,
// performs extra reads, creates identifiers or changes the original Result.
public static class ReadPreparation433
{
    public static readonly string[] Readers = ["get_document", "inspect_objects", "inspect_model_history", "get_model_variables", "inspect_solid_topology", "inspect_sketch_constraints", "inspect_constraints"];
    public static (JsonElement Arguments, ReadPreparationSelection433? Selection) Extract(string reader, JsonElement arguments)
    {
        if (arguments.ValueKind != JsonValueKind.Object || !arguments.TryGetProperty("prepare_for", out var option)) return (arguments, null);
        if (!Readers.Contains(reader, StringComparer.Ordinal)) throw Invalid("prepare_for is available only on the published preparation-enabled read tools.");
        if (option.ValueKind != JsonValueKind.Object) throw Invalid("prepare_for must be an object.");
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (var field in option.EnumerateObject())
            if (field.Name is not ("operation" or "variant") || !names.Add(field.Name)) throw Invalid("Unknown or duplicate prepare_for field: " + field.Name);
        string? Text(string name, bool required)
        {
            if (!option.TryGetProperty(name, out var value)) return required ? throw Invalid("prepare_for." + name + " is required.") : null;
            if (value.ValueKind != JsonValueKind.String || value.GetString() is not { Length: > 0 } text || text.Length > 128 || text.Any(char.IsControl))
                throw Invalid("prepare_for." + name + " must be a nonempty operation/variant name.");
            return text;
        }
        string operation = Text("operation", true)!; string? variant = Text("variant", false);
        if (!Wire.WriteOperations.Contains(operation, StringComparer.Ordinal)) throw Invalid("prepare_for.operation must name an existing write tool without its nanocad. prefix.");
        bool batched = operation is "create_entities" or "modify_entities" or "modify_structure";
        if (batched && variant == null) throw Invalid("Select the exact prepare_for.variant from the existing operation schema.");
        JsonElement schema;
        try { using var parsed = JsonDocument.Parse(GuidanceCatalog.ReadSchema(operation, variant)); schema = parsed.RootElement.Clone(); }
        catch (McpProtocolException exception) { throw Invalid(exception.Message); }
        string? array = ArrayField(operation);
        var branch = array == null ? schema : schema.GetProperty("properties").GetProperty(array).GetProperty("items");
        var stripped = new JsonObject(); var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var property in arguments.EnumerateObject())
        {
            if (!seen.Add(property.Name)) throw Invalid("Duplicate argument: " + property.Name);
            if (property.Name != "prepare_for") stripped.Add(property.Name, JsonNode.Parse(property.Value.GetRawText()));
        }
        return (JsonSerializer.SerializeToElement(stripped), new(operation, variant, schema, branch.Clone()));
    }

    public static JsonObject Build(string reader, ReadPreparationSelection433 selection, Result result)
    {
        var output = new JsonObject
        {
            ["operation"] = selection.Operation,
            ["variant"] = selection.Variant,
            ["status"] = "source_unavailable",
            ["detail"] = "No current context-bound observations were bound. Preparation never changes whether an existing operation is supported.",
            ["source"] = new JsonObject
            {
                ["reader"] = reader,
                ["context"] = result.context == null ? null : JsonSerializer.SerializeToNode(result.context),
                ["scope"] = "this_returned_read_only; no_additional_reads_or_solver_assessment"
            }
        };
        if (!result.success || result.context == null || result.data == null) return output;
        var data = Wire.Element(result.data);
        var bindings = new JsonArray();
        void Binding(string argument, JsonNode? value, string pointer, string origin) => bindings.Add(new JsonObject
        { ["argument"] = argument, ["value"] = value, ["source_pointer"] = pointer, ["origin"] = origin });
        var properties = selection.Schema.GetProperty("properties");
        if (properties.TryGetProperty("bridge_instance_id", out _)) Binding("/bridge_instance_id", JsonValue.Create(result.context.bridge_instance_id), "/context/bridge_instance_id", "read_context");
        if (properties.TryGetProperty("document_id", out _)) Binding("/document_id", JsonValue.Create(result.context.document_id), "/context/document_id", "read_context");
        if (properties.TryGetProperty("expected_revision", out _)) Binding("/expected_revision", JsonValue.Create(result.context.revision), "/context/revision", "read_context");
        if (properties.TryGetProperty("units", out var units) && units.TryGetProperty("const", out var constant))
            Binding("/units", JsonNode.Parse(constant.GetRawText()), "schema:/properties/units/const", "schema_constant");
        output["status"] = "observed";
        output["detail"] = "Observed bindings and current values only, not an executable command or an eligibility/solver guarantee. Choose intended changes, supply remaining required inputs and a fresh operation_id. Do not copy current values as the desired edit. References expire with context; use focused operation help for the complete contract.";
        output["request_bindings"] = bindings;
        var candidates = ReadPreparationBindings433.Candidates(reader, selection.Operation, selection.Variant, data, result.context, selection.Branch);
        var fields = Fields(selection);
        var common = bindings.Select(node => node!["argument"]!.GetValue<string>()).ToHashSet(StringComparer.Ordinal);
        foreach (var candidate in candidates.OfType<JsonObject>())
        {
            var bound = new HashSet<string>(common, StringComparer.Ordinal);
            if (candidate["bindings"] is JsonArray observed)
                foreach (var binding in observed.OfType<JsonObject>()) if (binding["argument"] is JsonValue argument) bound.Add(argument.GetValue<string>());
            candidate["unbound_required"] = new JsonArray(fields.OfType<JsonObject>().Where(field => field["required"]!.GetValue<bool>() && !bound.Contains(field["argument"]!.GetValue<string>()))
                .Select(field => (JsonNode?)new JsonObject { ["argument"] = field["argument"]!.DeepClone(), ["alternative"] = field["alternative"]?.DeepClone() }).ToArray());
        }
        output["candidates"] = candidates;
        output["input_fields"] = fields;
        if (reader == "get_document" && selection is { Operation: "modify_entities", Variant: "add_model_hole" })
        {
            output["next_reads"] = new JsonObject
            {
                ["inspect_model_history"] = "Read expected_last_item_id from the selected part's last_item_id.",
                ["inspect_solid_topology"] = "Set include_native_model_faces=true to read the current planar native model face ID and face_token."
            };
        }
        var facts = new JsonObject
        {
            ["request_distance_unit"] = units.ValueKind == JsonValueKind.Object && units.TryGetProperty("const", out var distance) ? JsonNode.Parse(distance.GetRawText()) : null,
            ["request_distance_unit_source"] = "selected input schema units const; does not classify angular/dimensionless controls",
            ["angular_units"] = "field_specific; only explicit field or observation units apply",
            ["observation_units"] = "not inferred from the write tool units; see returned explicit unit/frame facts"
        };
        foreach (string name in new[] { "units", "coordinate_system", "workplane", "parameter_space", "unit_policy", "value_scope", "value_unit", "linear_units", "angular_units" })
            if (data.ValueKind == JsonValueKind.Object && data.TryGetProperty(name, out var value))
                facts["observed_" + name] = new JsonObject { ["value"] = JsonNode.Parse(value.GetRawText()), ["source_pointer"] = "/data/" + name };
        output["unit_facts"] = facts;
        return output;
    }

    private static JsonArray Fields(ReadPreparationSelection433 selection)
    {
        var fields = new JsonArray();
        void Add(JsonElement schema, string prefix, string schemaPointer, string? alternative)
        {
            if (!schema.TryGetProperty("properties", out var properties)) return;
            var required = schema.TryGetProperty("required", out var list) ? list.EnumerateArray().Select(v => v.GetString()).ToHashSet(StringComparer.Ordinal) : [];
            foreach (var property in properties.EnumerateObject())
            {
                if (property.Name is "entities" or "operations") continue;
                var field = new JsonObject
                {
                    ["argument"] = prefix + "/" + property.Name,
                    ["required"] = required.Contains(property.Name),
                    ["alternative"] = alternative,
                    ["schema_pointer"] = schemaPointer + "/properties/" + property.Name
                };
                foreach (string key in new[] { "type", "description", "const", "enum", "minimum", "maximum", "exclusiveMinimum", "exclusiveMaximum" })
                    if (property.Value.TryGetProperty(key, out var value)) field[key] = JsonNode.Parse(value.GetRawText());
                if (property.Name == "operation_id") field["role"] = "new_write_identity; never preallocated by a read";
                fields.Add(field);
            }
        }
        Add(selection.Schema, "", "schema:", null);
        string? array = ArrayField(selection.Operation);
        if (array != null)
        {
            if (selection.Branch.TryGetProperty("oneOf", out var alternatives))
            {
                int index = 0;
                foreach (var branch in alternatives.EnumerateArray())
                { Add(branch, "/" + array + "/0", "schema:/properties/" + array + "/items/oneOf/" + index, index.ToString()); index++; }
            }
            else Add(selection.Branch, "/" + array + "/0", "schema:/properties/" + array + "/items", null);
        }
        return fields;
    }

    public static JsonElement Attach(JsonElement envelope, JsonObject preparation)
    {
        var copy = JsonNode.Parse(envelope.GetRawText())!.AsObject();
        copy.Add("preparation", preparation.DeepClone());
        return JsonSerializer.SerializeToElement(copy);
    }
    public static JsonObject? Fit(JsonObject preparation, Func<JsonObject?, int> measure, int budget)
    {
        bool Fits(JsonObject candidate)
        {
            try { return measure(candidate) <= budget; }
            catch (Exception error) when (error is JsonException or InvalidOperationException) { return false; }
        }
        if (Fits(preparation)) return preparation;
        var marker = new JsonObject
        {
            ["operation"] = preparation["operation"]!.DeepClone(),
            ["variant"] = preparation["variant"]?.DeepClone(),
            ["status"] = "details_omitted",
            ["details_omitted"] = true,
            ["detail"] = "Optional preparation exceeded remaining response bytes. Original read is unchanged. Use a narrower read and focused operation/schema help; no operation is disabled."
        };
        return Fits(marker) ? marker : null;
    }
    private static string? ArrayField(string operation) => operation == "create_entities" ? "entities" : operation is "modify_entities" or "modify_structure" ? "operations" : null;
    private static DomainException Invalid(string message) => new("INVALID_ARGUMENT", message);
}
