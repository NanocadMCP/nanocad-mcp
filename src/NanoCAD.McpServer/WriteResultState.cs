using System.Text.Json;
using NanoCAD.Contracts;

namespace NanoCAD.McpServer;

internal static class WriteResultState
{
    internal static async Task<WriteStateObservation?> ReadAsync(string operation, Result result,
        Func<string, object, CancellationToken, Task<Result>> read, CancellationToken cancellation)
    {
        if (!result.success || result.context is not { } context || result.data is null) return null;
        if (result.warnings.Any(warning => warning.StartsWith("COMMAND_COMPLETION_PENDING:", StringComparison.Ordinal)))
            return new("unavailable", context, null, "COMMAND_COMPLETION_PENDING");
        var data = Element(result.data);
        if (data.ValueKind != JsonValueKind.Object || !data.TryGetProperty("status", out var status) ||
            status.ValueKind != JsonValueKind.String || status.GetString() is not ("committed" or "completed")) return null;

        // The write already returned the document/view/structure state. An
        // object lookup on a layer, layout or camera would use the wrong API.
        if (operation is "manage_document" or "modify_structure" or "set_view")
            return new("in_result", context, null, "The operation's verified document, structure or view data is in data.");

        var handles = new HashSet<string>(StringComparer.Ordinal);
        foreach (var name in new[] { "created", "modified", "rebuilt_bodies" })
            if (data.TryGetProperty(name, out var values) && values.ValueKind == JsonValueKind.Array)
                foreach (var reference in values.EnumerateArray()) Add(reference, handles);
        if (data.TryGetProperty("body_ref", out var body)) Add(body, handles);
        if (data.TryGetProperty("erased", out var erased) && erased.ValueKind == JsonValueKind.Array)
        {
            var gone = new HashSet<string>(StringComparer.Ordinal);
            foreach (var reference in erased.EnumerateArray()) Add(reference, gone);
            handles.ExceptWith(gone);
        }
        if (handles.Count == 0) return new("in_result", context, null, "The receipt has no surviving object references to inspect.");

        var current = await read("inspect_objects", new
        {
            context.bridge_instance_id,
            context.document_id,
            expected_revision = context.revision,
            handles = handles.ToArray(),
            sections = new[] { "geometry", "editing" }
        }, cancellation).ConfigureAwait(false);
        if (!current.success || current.data is null || !SameContext(current.context, context))
            return new("unavailable", context, null, current.error?.code ?? "WRITE_CONTEXT_CHANGED");

        var observed = Element(current.data);
        if (!observed.TryGetProperty("items", out var items) || items.ValueKind != JsonValueKind.Array)
            return new("unavailable", context, null, "INVALID_OBSERVATION");
        var objects = new List<object>();
        foreach (var item in items.EnumerateArray())
        {
            if (!item.TryGetProperty("success", out var success) || success.ValueKind != JsonValueKind.True ||
                !item.TryGetProperty("entity", out var entity) || entity.ValueKind != JsonValueKind.Object ||
                !entity.TryGetProperty("ref", out var identity) || !TryReference(identity, out var handle) ||
                !handles.Remove(handle) || !entity.TryGetProperty("geometry", out var geometry)) continue;
            objects.Add(new { @ref = identity, type = entity.GetProperty("type"), geometry });
        }
        object? modelHistory = null;
        string? reason = handles.Count == 0 ? null : "Some returned objects could not be inspected at the committed revision.";
        if (operation == "set_model_parameter" && body.ValueKind == JsonValueKind.Object && TryReference(body, out var bodyHandle))
        {
            var history = await read("inspect_model_history", new
            { context.bridge_instance_id, context.document_id, expected_revision = context.revision, handles = new[] { bodyHandle } }, cancellation).ConfigureAwait(false);
            if (history.success && history.data is not null && SameContext(history.context, context))
            {
                var snapshot = Element(history.data);
                if (snapshot.TryGetProperty("items", out var records) && records.ValueKind == JsonValueKind.Array && records.GetArrayLength() == 1)
                {
                    var item = records[0];
                    if (item.TryGetProperty("ref", out var reference) && TryReference(reference, out var found) && found == bodyHandle &&
                        item.TryGetProperty("last_item_id", out var terminal)) modelHistory = new { @ref = reference, last_item_id = terminal };
                }
            }
            if (modelHistory == null) reason ??= "Current model history was unavailable.";
        }
        return new(objects.Count == 0 ? "unavailable" : reason == null ? "observed" : "partial", context,
            objects.Count == 0 && modelHistory == null ? null : new { objects, model_history = modelHistory }, reason);

        void Add(JsonElement reference, HashSet<string> destination)
        { if (TryReference(reference, out var handle)) destination.Add(handle); }

        bool TryReference(JsonElement reference, out string handle)
        {
            handle = "";
            if (reference.ValueKind != JsonValueKind.Object ||
                !ReadString(reference, "bridge_instance_id", out var bridge) || bridge != context.bridge_instance_id ||
                !ReadString(reference, "document_id", out var doc) || doc != context.document_id ||
                !ReadString(reference, "handle", out var value)) return false;
            try { handle = Validation.NormalizeHandle(value); return true; }
            catch (DomainException) { return false; }
        }
    }

    private static JsonElement Element(object data) => data is JsonElement json ? json : Wire.Element(data);

    private static bool ReadString(JsonElement obj, string name, out string value)
    {
        value = "";
        if (!obj.TryGetProperty(name, out var field) || field.ValueKind != JsonValueKind.String) return false;
        value = field.GetString()!;
        return true;
    }

    private static bool SameContext(ReadContext? actual, ReadContext expected) => actual is not null &&
        actual.bridge_instance_id == expected.bridge_instance_id && actual.document_id == expected.document_id && actual.revision == expected.revision;
}
