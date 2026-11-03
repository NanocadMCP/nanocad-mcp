using System.Text.Json;
using System.Text.Json.Nodes;
using ModelContextProtocol.Protocol;

namespace NanoCAD.McpServer;

internal static class OperationResultSchema432
{
    internal static Tool Tool() => new()
    {
        Name = "nanocad.get_operation_result",
        Description = "Read the historical outcome of an existing write by bridge lifetime and operation ID without submitting native work. Pending reads may wait briefly. References and warnings are byte-bounded pages of recorded output, not fresh CAD observations or an inferred changed-object set. Nothing is saved or activated and reader cancellation never cancels the write. A missing ledger entry means unknown, not permission to repeat. Exact payload replay remains supported by the original write tool.",
        InputSchema = JsonSerializer.SerializeToElement(Input()),
        OutputSchema = Output(),
        Annotations = new() { ReadOnlyHint = true, DestructiveHint = false, IdempotentHint = true, OpenWorldHint = false }
    };

    private static JsonObject Input()
    {
        var input = Object(("bridge_instance_id", Uuid("Original live bridge_instance_id. Ledger entries do not survive a bridge restart.")),
            ("operation_id", Uuid("Original write operation_id; no payload, document activation or current revision is required.")));
        var fields = input["properties"]!.AsObject();
        fields["wait_ms"] = new JsonObject { ["type"] = "integer", ["minimum"] = 0, ["maximum"] = 6000, ["default"] = 2000,
            ["description"] = "Reader-local wait for the existing job, in milliseconds. Does not extend or cancel native execution." };
        fields["mode"] = new JsonObject { ["type"] = "string", ["enum"] = new JsonArray("result", "references", "warnings"), ["default"] = "result",
            ["description"] = "result includes the historical response when it fits and initial pages. references/warnings page recorded observations without repeating the full response." };
        fields["cursor"] = new JsonObject { ["type"] = "string", ["minLength"] = 1, ["maxLength"] = 2048,
            ["description"] = "Exact next_cursor for mode=references or warnings, bound to this bridge, operation and result fingerprint. Do not use with mode=result. If the recorded result changes, start the page again." };
        fields["page_size"] = new JsonObject { ["type"] = "integer", ["minimum"] = 1, ["maximum"] = int.MaxValue, ["default"] = 100,
            ["description"] = "Desired page length, shortened by actual response bytes. No ceiling on total recorded references or warnings." };
        return input;
    }

    internal static JsonElement Output()
    {
        var reference = Object(("bridge_instance_id", Text()), ("document_id", Text()), ("handle", Text()));
        var observations = Object(("ref", reference), ("role", Text()), ("provenance", Text()));
        var fragment = Object(("warning_index", Integer()), ("text", Text()), ("text_offset", Integer()), ("total_characters", Integer()), ("complete", Bool()));
        fragment["description"] = "Lossless warning text fragment. Reassemble by zero-based warning_index and UTF-16 text_offset; complete means this warning is fully returned through this offset.";
        var lookup = Object(("bridge_instance_id", Text()), ("operation_id", Text()), ("found", Bool()), ("operation", Nullable(Text())),
            ("original_request_id", Nullable(Text())), ("phase", Nullable(Text())), ("job_finished", Bool()), ("commit_recorded", Bool()),
            ("outcome", Text()), ("recorded_result", Nullable(new JsonObject { ["type"] = "object", ["description"] = "Original historical Result; its context and identities do not establish the current drawing state." })),
            ("recorded_result_included", Bool()), ("result_fingerprint", Nullable(Text())),
            ("references_page", Nullable(Page(observations))), ("warnings_page", Nullable(Page(fragment))), ("detail", Text()));
        var envelope = Object(("contract_version", Text()), ("request_id", Text()), ("success", Bool()),
            ("context", Nullable(Context())), ("data", Nullable(lookup)),
            ("error", Nullable(Object(("code", Text()), ("message", Text()), ("retryable", Bool()), ("details", Nullable(new JsonObject { ["type"] = "object" }))))),
            ("warnings", Array(Text())));
        envelope["properties"]!["receipt"] = Receipt();
        return JsonSerializer.SerializeToElement(envelope);
    }

    internal static JsonObject Receipt() => Object(("operation", Text()), ("operation_id", Nullable(Text())), ("status", Text()), ("outcome", Text()),
        ("details_omitted", Bool()), ("historical_context", Nullable(Context())), ("original_error_code", Nullable(Text())),
        ("original_warning_count", Integer()), ("returned_warning_count", Integer()), ("warnings_complete", Bool()),
        ("lookup", Nullable(Object(("tool", Text()), ("bridge_instance_id", Text()), ("operation_id", Text()),
            ("result_mode", Text()), ("references_mode", Text()), ("warnings_mode", Text())))), ("detail", Text()), ("next_action", Text()));
    internal static JsonObject CompactWriteData()
    {
        var data = Object(("operation_id", Text()), ("status", Text()), ("details_omitted", new JsonObject { ["const"] = true }));
        var fields = data["properties"]!.AsObject();
        fields["outcome"] = Text(); fields["replayed"] = Bool(); fields["undo_history"] = Text(); fields["action"] = Text();
        fields["written_path"] = Nullable(Text()); fields["written_bytes"] = Nullable(Integer());
        return data;
    }
    private static JsonObject Page(JsonObject item) => Object(("items", Array(item)), ("total_count", Integer()), ("returned_count", Integer()), ("complete", Bool()), ("next_cursor", Nullable(Text())));
    private static JsonObject Context() => Object(("bridge_instance_id", Text()), ("document_id", Text()), ("revision", Text()), ("captured_at_utc", Text()));
    private static JsonObject Uuid(string description) => new() { ["type"] = "string", ["format"] = "uuid", ["minLength"] = 36, ["maxLength"] = 36, ["description"] = description };
    private static JsonObject Text() => new() { ["type"] = "string" };
    private static JsonObject Integer() => new() { ["type"] = "integer" };
    private static JsonObject Bool() => new() { ["type"] = "boolean" };
    private static JsonObject Array(JsonObject item) => new() { ["type"] = "array", ["items"] = item };
    private static JsonObject Nullable(JsonObject schema) => new() { ["anyOf"] = new JsonArray(schema, new JsonObject { ["type"] = "null" }) };
    private static JsonObject Object(params (string Name, JsonObject Schema)[] fields)
    {
        var properties = new JsonObject(); var required = new JsonArray();
        foreach (var (name, schema) in fields) { properties[name] = schema; required.Add(name); }
        return new() { ["type"] = "object", ["additionalProperties"] = false, ["properties"] = properties, ["required"] = required };
    }
}
