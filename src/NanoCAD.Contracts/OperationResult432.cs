using System.Security.Cryptography;
using System.Text.Json;

namespace NanoCAD.Contracts;

public sealed record OperationResultArguments432(string BridgeId, string OperationId, int WaitMs, string Mode, string? Cursor, int PageSize)
{
    public static OperationResultArguments432 Parse(JsonElement input)
    {
        if (input.ValueKind != JsonValueKind.Object) throw Invalid("Arguments must be an object.");
        var fields = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        foreach (var property in input.EnumerateObject())
            if (property.Name is not ("bridge_instance_id" or "operation_id" or "wait_ms" or "mode" or "cursor" or "page_size") ||
                !fields.TryAdd(property.Name, property.Value)) throw Invalid("Unknown or duplicate argument: " + property.Name);
        string Text(string name, int maximum, string? fallback = null)
        {
            if (!fields.TryGetValue(name, out var value)) return fallback ?? throw Invalid(name + " is required.");
            if (value.ValueKind != JsonValueKind.String || value.GetString() is not { Length: > 0 } text ||
                text.Length > maximum || text.Any(char.IsControl)) throw Invalid(name + " must be a bounded nonempty string.");
            return text;
        }
        string Id(string name)
        {
            if (!Guid.TryParseExact(Text(name, 36), "D", out var id) || id == Guid.Empty) throw Invalid(name + " must be a nonempty UUID.");
            return id.ToString("D");
        }
        int Number(string name, int fallback, int minimum, int maximum)
        {
            if (!fields.TryGetValue(name, out var value)) return fallback;
            if (value.ValueKind != JsonValueKind.Number || !value.TryGetInt32(out int number) || number < minimum || number > maximum)
                throw Invalid(name + $" must be an Int32 integer from {minimum} to {maximum}.");
            return number;
        }
        var bridge = Id("bridge_instance_id"); var operation = Id("operation_id");
        var mode = Text("mode", 16, "result");
        if (mode is not ("result" or "references" or "warnings")) throw Invalid("mode must be result, references or warnings.");
        var cursor = fields.ContainsKey("cursor") ? Text("cursor", 2048) : null;
        if (cursor != null && mode == "result") throw Invalid("Use mode references or warnings with its corresponding page cursor.");
        return new(bridge, operation, Number("wait_ms", 2000, 0, 6000), mode, cursor, Number("page_size", 100, 1, int.MaxValue));
    }
    private static DomainException Invalid(string message) => new("INVALID_ARGUMENT", message);
}

/// <summary>A reference observed in the recorded output; its role does not imply it was changed.</summary>
public sealed record OperationReference432(EntityRef @ref, string role, string provenance);
public sealed record OperationReferencesPage432(OperationReference432[] items, long total_count, int returned_count, bool complete, string? next_cursor);
/// <summary>Reassemble a warning by warning_index and text_offset. Offsets count UTF-16 characters.</summary>
public sealed record OperationWarning432(int warning_index, string text, int text_offset, int total_characters, bool complete);
public sealed record OperationWarningsPage432(OperationWarning432[] items, int total_count, int returned_count, bool complete, string? next_cursor);
public sealed record OperationResultLookup432(string bridge_instance_id, string operation_id, bool found, string? operation,
    string? original_request_id, string? phase, bool job_finished, bool commit_recorded, string outcome, Result? recorded_result,
    bool recorded_result_included, string? result_fingerprint, OperationReferencesPage432? references_page,
    OperationWarningsPage432? warnings_page, string detail);

/// <summary>Stateless pages over the historical Result retained by the existing bridge ledger.</summary>
public static class OperationResult432
{
    // Each page leaves room for the other page, the lookup envelope, and transport escaping.
    private const int PageBytes = Wire.PayloadBudget / 4;
    private const int PageEnvelopeReserve = 2048;
    private sealed record Cursor432(int version, string bridge_instance_id, string operation_id, string mode,
        string result_fingerprint, long offset, int text_offset);

    public static Result NotFound(string requestId, OperationResultArguments432 arguments) => Result.Ok(requestId,
        new OperationResultLookup432(arguments.BridgeId, arguments.OperationId, false, null, null, null, false, false, "unknown",
            null, false, null, null, null,
            "Operation was not found in this bridge lifetime. Its outcome is unknown; this is not permission to repeat a write or use a new operation_id."));

    public static Result Recorded(string requestId, OperationResultArguments432 arguments, string operation, string originalRequestId,
        string phase, bool jobFinished, bool commitRecorded, Result? recorded, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        string? fingerprint = null; OperationReferencesPage432? references = null; OperationWarningsPage432? warnings = null;
        string outcome = "pending";
        if (recorded != null)
        {
            byte[] bytes = JsonSerializer.SerializeToUtf8Bytes(recorded, Wire.Json);
            fingerprint = Convert.ToHexString(SHA256.HashData(bytes));
            using var document = JsonDocument.Parse(bytes);
            outcome = Outcome(document.RootElement);
            var cursor = ReadCursor(arguments, fingerprint);
            if (arguments.Mode is "result" or "references") references = References(document.RootElement, arguments, fingerprint, cursor, cancellationToken);
            if (arguments.Mode is "result" or "warnings") warnings = Warnings(recorded.warnings, arguments, fingerprint, cursor, cancellationToken);
        }
        else if (arguments.Cursor != null) throw InvalidCursor("No recorded result is available for this cursor.");
        bool include = arguments.Mode == "result" && recorded != null;
        return Result.Ok(requestId, new OperationResultLookup432(arguments.BridgeId, arguments.OperationId, true, operation,
            originalRequestId, phase, jobFinished, commitRecorded, outcome, include ? recorded : null, include, fingerprint, references, warnings,
            "Historical ledger observation only. job_finished means the queue job has a final response; it does not establish native command completion or current document state. References are recorded output observations, not an inferred changed-object set."));
    }

    private static string Outcome(JsonElement result)
    {
        if (result.TryGetProperty("error", out var error) && error.ValueKind == JsonValueKind.Object &&
            error.TryGetProperty("details", out var details) && details.ValueKind == JsonValueKind.Object &&
            details.TryGetProperty("outcome", out var failure) && failure.ValueKind == JsonValueKind.String)
            return KnownOutcome(failure.GetString());
        if (result.TryGetProperty("data", out var data) && data.ValueKind == JsonValueKind.Object &&
            data.TryGetProperty("status", out var status) && status.ValueKind == JsonValueKind.String)
            return KnownOutcome(status.GetString());
        return "unknown";
    }
    private static string KnownOutcome(string? value) => value is "committed" or "completed" or "not_started" or "unknown" or "rolled_back" or "pending" ? value : "unknown";

    private static OperationReferencesPage432 References(JsonElement result, OperationResultArguments432 arguments, string fingerprint,
        Cursor432? cursor, CancellationToken cancellationToken)
    {
        long offset = cursor?.offset ?? 0, total = 0; int bytes = 2; bool full = false;
        var items = new List<OperationReference432>();
        IEnumerable<OperationReference432> Observations()
        {
            if (result.TryGetProperty("data", out var data))
                foreach (var item in FindReferences(data, "/data", "returned_reference", cancellationToken)) yield return item;
            if (result.TryGetProperty("error", out var error) && error.ValueKind == JsonValueKind.Object && error.TryGetProperty("details", out var details))
                foreach (var item in FindReferences(details, "/error/details", "returned_reference", cancellationToken)) yield return item;
        }
        foreach (var item in Observations())
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (total++ < offset || full) continue;
            int length = JsonSerializer.SerializeToUtf8Bytes(item, Wire.Json).Length + (items.Count == 0 ? 0 : 1);
            if (items.Count == arguments.PageSize || bytes + length > PageBytes - PageEnvelopeReserve) { full = true; continue; }
            items.Add(item); bytes += length;
        }
        if (offset > total || cursor?.text_offset > 0) throw InvalidCursor("Reference page offset is outside this recorded result.");
        long next = offset + items.Count; bool complete = next == total;
        if (!complete && items.Count == 0) throw new DomainException("RESPONSE_LIMIT_EXCEEDED", "A recorded reference observation exceeds the page byte budget; its original result remains in the ledger.");
        return new(items.ToArray(), total, items.Count, complete, complete ? null : MakeCursor(arguments, fingerprint, "references", next));
    }

    private static IEnumerable<OperationReference432> FindReferences(JsonElement value, string path, string role, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (value.ValueKind == JsonValueKind.Object)
        {
            if (value.EnumerateObject().Count() == 3 && value.TryGetProperty("bridge_instance_id", out var bridge) && bridge.ValueKind == JsonValueKind.String &&
                value.TryGetProperty("document_id", out var document) && document.ValueKind == JsonValueKind.String &&
                value.TryGetProperty("handle", out var handle) && handle.ValueKind == JsonValueKind.String)
            { yield return new(new(bridge.GetString()!, document.GetString()!, handle.GetString()!), role, path); yield break; }
            foreach (var property in value.EnumerateObject())
                foreach (var item in FindReferences(property.Value, path + "/" + property.Name.Replace("~", "~0").Replace("/", "~1"), property.Name, cancellationToken)) yield return item;
        }
        else if (value.ValueKind == JsonValueKind.Array)
        {
            int index = 0;
            foreach (var element in value.EnumerateArray())
            {
                foreach (var item in FindReferences(element, path + "/" + index, role, cancellationToken)) yield return item;
                index++;
            }
        }
    }

    private static OperationWarningsPage432 Warnings(string[] source, OperationResultArguments432 arguments, string fingerprint,
        Cursor432? cursor, CancellationToken cancellationToken)
    {
        long rawOffset = cursor?.offset ?? 0; int textOffset = cursor?.text_offset ?? 0;
        if (rawOffset > source.Length || rawOffset == source.Length && textOffset != 0) throw InvalidCursor("Warning page offset is outside this recorded result.");
        int index = (int)rawOffset, bytes = 2; var items = new List<OperationWarning432>();
        if (index < source.Length && (textOffset > source[index].Length || SplitsSurrogate(source[index], textOffset)))
            throw InvalidCursor("Warning text offset is outside a Unicode character boundary.");
        while (index < source.Length && items.Count < arguments.PageSize)
        {
            cancellationToken.ThrowIfCancellationRequested();
            string text = source[index]; int remaining = PageBytes - PageEnvelopeReserve - bytes - (items.Count == 0 ? 0 : 1);
            int length = Math.Min(text.Length - textOffset, Math.Max(0, remaining));
            OperationWarning432 Fragment(int count) => new(index, text.Substring(textOffset, count), textOffset, text.Length, textOffset + count == text.Length);
            if (SplitsSurrogate(text, textOffset + length)) length--;
            if (length == 0 && textOffset < text.Length) break;
            var fragment = Fragment(length);
            int size = JsonSerializer.SerializeToUtf8Bytes(fragment, Wire.Json).Length;
            if (size > remaining)
            {
                int low = 0, high = length;
                while (low < high)
                {
                    int middle = low + (high - low + 1) / 2;
                    if (JsonSerializer.SerializeToUtf8Bytes(Fragment(middle), Wire.Json).Length <= remaining) low = middle; else high = middle - 1;
                }
                length = low;
                if (SplitsSurrogate(text, textOffset + length)) length--;
                if (length <= 0) break;
                fragment = Fragment(length); size = JsonSerializer.SerializeToUtf8Bytes(fragment, Wire.Json).Length;
            }
            items.Add(fragment); bytes += size + (items.Count == 1 ? 0 : 1);
            textOffset += length;
            if (textOffset == text.Length) { index++; textOffset = 0; }
            else break;
        }
        bool complete = index == source.Length;
        return new(items.ToArray(), source.Length, items.Count, complete, complete ? null : MakeCursor(arguments, fingerprint, "warnings", index, textOffset));
    }

    private static bool SplitsSurrogate(string text, int offset) => offset > 0 && offset < text.Length && char.IsHighSurrogate(text[offset - 1]) && char.IsLowSurrogate(text[offset]);
    private static string MakeCursor(OperationResultArguments432 arguments, string fingerprint, string mode, long offset, int textOffset = 0) =>
        Convert.ToBase64String(JsonSerializer.SerializeToUtf8Bytes(new Cursor432(1, arguments.BridgeId, arguments.OperationId, mode, fingerprint, offset, textOffset), Wire.Json));

    private static Cursor432? ReadCursor(OperationResultArguments432 arguments, string fingerprint)
    {
        if (arguments.Cursor == null) return null;
        try
        {
            using var document = JsonDocument.Parse(Convert.FromBase64String(arguments.Cursor));
            if (document.RootElement.ValueKind != JsonValueKind.Object) throw InvalidCursor("Invalid page cursor.");
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var property in document.RootElement.EnumerateObject())
                if (property.Name is not ("version" or "bridge_instance_id" or "operation_id" or "mode" or "result_fingerprint" or "offset" or "text_offset") || !seen.Add(property.Name))
                    throw InvalidCursor("Invalid page cursor.");
            var cursor = document.RootElement.Deserialize<Cursor432>(Wire.Json);
            if (seen.Count != 7 || cursor == null || cursor.version != 1 || cursor.bridge_instance_id != arguments.BridgeId ||
                cursor.operation_id != arguments.OperationId || cursor.mode != arguments.Mode || cursor.result_fingerprint != fingerprint ||
                cursor.offset < 0 || cursor.text_offset < 0) throw InvalidCursor("Page cursor does not match this bridge, operation, mode and recorded result. Read the first page again if the recorded result changed.");
            return cursor;
        }
        catch (Exception exception) when (exception is FormatException or JsonException or InvalidOperationException or ArgumentException)
        { throw InvalidCursor("Invalid page cursor."); }
    }
    private static DomainException InvalidCursor(string message) => new("INVALID_CURSOR", message);
}
