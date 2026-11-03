using System.Text.Json.Nodes;

namespace NanoCAD.McpServer;

// Explicit additional native inspection; separate from the projection-only
// prepare_for option. The selected write still performs its own admission.
internal static class EraseAssessmentSchema444
{
    internal const string Description = " Optional erase_assessment=true evaluates the exact complete selected handle set against the existing Model Space erase admission checks, including native-owned history, retained consumers and ordinary-target guards. It is an explicit additional read, not prepare_for, a deletion, task-provenance inference, permission or a cached eligibility token. Show the returned assessment together with each target's known creation/temporary role before requesting erase. Only eligible with complete=true reports current technical admission; blocked/unavailable or missing observations are not clearance. Writes recheck all guards and external approval remains independent.";

    internal static JsonObject Input() => new()
    {
        ["type"] = "boolean", ["default"] = false,
        ["description"] = "Explicitly inspect current erase admission for all requested handles together in Model Space, without deleting or changing them. Returns data.erase_assessment; omitted by default. This performs additional dependency/ownership reads, independently of sections and prepare_for. Eligibility does not establish user authorization or that a source is unnecessary. The actual erase rechecks all guards."
    };

    private static JsonObject Text() => new() { ["type"] = "string" };
    private static JsonObject Array(JsonObject item) => new() { ["type"] = "array", ["items"] = item };
    private static JsonObject Nullable(JsonObject item) => new() { ["anyOf"] = new JsonArray(item, new JsonObject { ["type"] = "null" }) };
    private static JsonObject Object(JsonObject properties, params string[] required) => new()
    {
        ["type"] = "object", ["additionalProperties"] = false, ["properties"] = properties,
        ["required"] = new JsonArray(required.Select(name => (JsonNode?)JsonValue.Create(name)).ToArray())
    };
    internal static JsonObject Output()
    {
        var reference = Object(new JsonObject { ["bridge_instance_id"] = Text(), ["document_id"] = Text(), ["handle"] = Text() },
            "bridge_instance_id", "document_id", "handle");
        var target = Object(new JsonObject
        {
            ["ref"] = reference, ["entity_type"] = Text(),
            ["route"] = new JsonObject { ["type"] = "string", ["enum"] = new JsonArray("native_owner", "ordinary") },
            ["native_owner_id"] = Nullable(Text()), ["native_owned_history_ids"] = Array(Text())
        }, "ref", "entity_type", "route", "native_owner_id", "native_owned_history_ids");
        var blocker = Object(new JsonObject
        {
            ["code"] = Text(), ["message"] = Text(), ["retryable"] = new JsonObject { ["type"] = "boolean" },
            ["details"] = new JsonObject { ["description"] = "Exact current admission error details where available; null is unobserved, never an empty dependency inventory." }
        }, "code", "message", "retryable", "details");
        return Object(new JsonObject
        {
            ["status"] = new JsonObject { ["type"] = "string", ["enum"] = new JsonArray("eligible", "blocked", "unavailable") },
            ["complete"] = new JsonObject { ["type"] = "boolean" },
            ["target_space"] = new JsonObject { ["const"] = "model" },
            ["requested_handles"] = Array(Text()), ["targets"] = Array(target),
            ["native_owned_scope"] = Array(Text()), ["blockers"] = Array(blocker),
            ["first_blocker_only"] = new JsonObject { ["type"] = "boolean" },
            ["coverage"] = Text(), ["authorization"] = new JsonObject { ["const"] = "not_assessed" },
            ["provenance"] = new JsonObject { ["const"] = "not_assessed" },
            ["write_guards"] = new JsonObject { ["const"] = "rechecked_at_write" }, ["detail"] = Text()
        }, "status", "complete", "target_space", "requested_handles", "targets", "native_owned_scope", "blockers", "coverage", "authorization", "provenance", "write_guards");
    }
}
