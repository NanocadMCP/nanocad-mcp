using System.Text.Json;
using System.Text.Json.Nodes;
using NanoCAD.Contracts;
using NanoCAD.Ipc;

namespace NanoCAD.McpServer;

internal sealed record WriteRequestOptions(JsonElement Arguments, bool ValidateOnly, bool ReturnState)
{
    private const string UuidPattern = "[0-9a-fA-F]{8}(?:-[0-9a-fA-F]{4}){3}-[0-9a-fA-F]{12}";
    private const string NumericRevision = "[0-9]{1,20}";
    internal const string QualifiedRevisionPattern = "^" + UuidPattern + "/" + UuidPattern + "/" + NumericRevision + "$";

    private static bool UnitsDefault(string operation) => operation is
        "create_entities" or "modify_entities" or "modify_structure" or "set_model_parameter";

    private static bool ScopedRevision(string operation) => operation != "manage_document";

    internal static async Task<WriteRequestOptions> BindAsync(string operation, JsonElement input,
        Func<string?, Descriptor> choose, Func<Descriptor, CancellationToken, Task<DocumentList>> list,
        CancellationToken cancellation)
    {
        if (input.ValueKind != JsonValueKind.Object) throw InputError.Create("$", "Expected an object.");
        var arguments = new JsonObject();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        bool validate = false, state = false;
        foreach (var member in input.EnumerateObject())
        {
            if (!seen.Add(member.Name)) throw InputError.Create("$." + member.Name, "Duplicate field.");
            if (member.Name == "validate_only") validate = ReadFlag(member);
            else if (member.Name == "return_state") state = ReadFlag(member);
            else arguments.Add(member.Name, JsonNode.Parse(member.Value.GetRawText()));
        }
        if (validate && state) throw InputError.Create("$.return_state", "Result state requires a write, so it cannot be combined with validate_only.");
        if (UnitsDefault(operation) && !arguments.ContainsKey("units")) arguments["units"] = "Millimeters";

        string? bridge = ReadId("bridge_instance_id"), document = ReadId("document_id");
        bool omittedBridge = bridge == null;
        if (ScopedRevision(operation) && arguments["expected_revision"] is JsonValue revision &&
            revision.TryGetValue<string>(out var text) && ScopedWriteRevision.TryParse(text, out var scope))
        {
            if (bridge != null && bridge != scope.bridge_instance_id || document != null && document != scope.document_id)
                throw InputError.Create("$.expected_revision", "The qualified revision must match the supplied bridge and document.");
            if (bridge == null)
            {
                var selected = choose(null);
                if (selected.bridge_instance_id != scope.bridge_instance_id)
                    throw new DomainException("STALE_DOCUMENT", "The qualified revision belongs to another bridge lifetime.", true);
                bridge = selected.bridge_instance_id;
            }
            if (document == null)
            {
                var selected = choose(bridge);
                var open = await list(selected, cancellation).ConfigureAwait(false);
                if (open.bridge_instance_id != bridge || open.documents.Length != 1 || open.active_document_id == null)
                    throw new DomainException("AMBIGUOUS_DOCUMENT", "Specify document_id when the bridge does not have exactly one open active drawing.");
                var active = open.documents[0];
                if (active.document_id != scope.document_id || !active.is_active || active.revision != scope.revision)
                    throw new DomainException("STALE_DOCUMENT", "The qualified revision is no longer current for the only active drawing.", true);
                document = active.document_id;
            }
            arguments["expected_revision"] = scope.revision;
        }
        if (ScopedRevision(operation) && document == null)
            throw InputError.Create("$.document_id", "Specify document_id or use the qualified context.write_revision from a read.");
        if (omittedBridge) arguments["bridge_instance_id"] = bridge ?? choose(null).bridge_instance_id;
        if (document != null && !arguments.ContainsKey("document_id")) arguments["document_id"] = document;
        return new(Wire.Element(arguments), validate, state);

        string? ReadId(string name)
        {
            if (!arguments.ContainsKey(name)) return null;
            if (arguments[name] is not JsonValue value || !value.TryGetValue<string>(out var id) || !Guid.TryParseExact(id, "D", out var uuid))
                throw InputError.Create("$." + name, "Expected a UUID.");
            return uuid.ToString("D");
        }
    }

    private static bool ReadFlag(JsonProperty member) => member.Value.ValueKind switch
    {
        JsonValueKind.True => true,
        JsonValueKind.False => false,
        _ => throw InputError.Create("$." + member.Name, "Expected a boolean.", new[] { true, false })
    };

    internal static JsonElement Publish(string name, JsonElement schema)
    {
        string operation = name.StartsWith("nanocad.", StringComparison.Ordinal) ? name.Substring(8) : name;
        if (!Wire.WriteOperations.Contains(operation)) return schema;
        var root = JsonNode.Parse(schema.GetRawText())!.AsObject();
        Apply(root);
        var constraints = root["allOf"] as JsonArray ?? new JsonArray();
        constraints.Add(new JsonObject { ["not"] = new JsonObject
        {
            ["required"] = new JsonArray("validate_only", "return_state"),
            ["properties"] = new JsonObject
            {
                ["validate_only"] = new JsonObject { ["const"] = true },
                ["return_state"] = new JsonObject { ["const"] = true }
            }
        } });
        if (ScopedRevision(operation))
            constraints.Add(new JsonObject
            {
                ["if"] = new JsonObject { ["properties"] = new JsonObject
                {
                    ["expected_revision"] = new JsonObject { ["pattern"] = "^" + NumericRevision + "$" }
                } },
                ["then"] = new JsonObject { ["required"] = new JsonArray("document_id") }
            });
        if (root["allOf"] is null) root["allOf"] = constraints;
        return Wire.Element(root);

        void Apply(JsonObject node)
        {
            if (node["properties"] is JsonObject fields)
            {
                fields["validate_only"] = new JsonObject { ["type"] = "boolean", ["default"] = false,
                    ["description"] = "Run available preflight checks without writing." };
                fields["return_state"] = new JsonObject { ["type"] = "boolean", ["default"] = false,
                    ["description"] = "Include current observations of returned objects after a confirmed write." };
                var optional = new HashSet<string>(StringComparer.Ordinal) { "bridge_instance_id" };
                if (UnitsDefault(operation) && fields["units"] is JsonObject units)
                {
                    units["default"] = "Millimeters";
                    optional.Add("units");
                }
                if (ScopedRevision(operation) && fields["expected_revision"] is JsonObject revision)
                {
                    revision.Remove("maxLength");
                    revision["pattern"] = "^(?:(?:" + UuidPattern + "/" + UuidPattern + "/)?" + NumericRevision + ")$";
                    revision["description"] = "Use context.write_revision to omit document_id for the sole open active drawing.";
                    optional.Add("document_id");
                }
                if (fields["bridge_instance_id"] is JsonObject bridge)
                    bridge["description"] = "Omit only when there is exactly one local bridge.";
                if (node["required"] is JsonArray required)
                    for (int i = required.Count - 1; i >= 0; --i)
                        if (required[i] is JsonValue field && field.TryGetValue<string>(out var key) && optional.Contains(key)) required.RemoveAt(i);
            }
            foreach (var keyword in new[] { "oneOf", "anyOf", "allOf" })
                if (node[keyword] is JsonArray alternatives)
                    foreach (var branch in alternatives.OfType<JsonObject>()) Apply(branch);
        }
    }
}
