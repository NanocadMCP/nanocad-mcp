using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace NanoCAD.Contracts;

public sealed record WritePreviewRequest(string operation, JsonElement arguments)
{
    public const string Route = "validate_write";

    public static IOperationArguments Parse(JsonElement input)
    {
        if (input.ValueKind != JsonValueKind.Object || input.EnumerateObject().Count() != 2 ||
            !input.TryGetProperty("operation", out var name) || name.ValueKind != JsonValueKind.String ||
            !input.TryGetProperty("arguments", out var args) || !Wire.WriteOperations.Contains(name.GetString()!))
            throw InputError.Create("$", "Expected a write operation and its arguments.");
        return WriteValidation.Parse(name.GetString()!, args);
    }
}

public sealed record WritePreviewResult(string operation, string status, bool document_changed,
    string validation_scope, object? predicted_effects = null);

public sealed record WriteStateObservation(string status, ReadContext? context, object? observations,
    string? reason = null);

// A revision qualified with its original document and bridge lifetime. The
// ordinary decimal revision remains the wire value for existing callers.
public static class ScopedWriteRevision
{
    public static string? Format(ReadContext context) =>
        TryParse($"{context.bridge_instance_id}/{context.document_id}/{context.revision}", out var parsed)
            ? $"{parsed.bridge_instance_id}/{parsed.document_id}/{parsed.revision}" : null;

    public static bool TryParse(string? value, out ReadContext context)
    {
        context = null!;
        if (value is null || value.Length is < 75 or > 94) return false;
        var parts = value.Split('/');
        if (parts.Length != 3 || !Guid.TryParseExact(parts[0], "D", out var bridge) ||
            !Guid.TryParseExact(parts[1], "D", out var document) || parts[2].Length > 20 ||
            !ulong.TryParse(parts[2], NumberStyles.None, CultureInfo.InvariantCulture, out var revision)) return false;
        context = new(bridge.ToString("D"), document.ToString("D"), revision.ToString(CultureInfo.InvariantCulture), "");
        return true;
    }
}

public static class InputError
{
    public static DomainException Create(string path, string rule, object? allowed = null, string code = "INVALID_ARGUMENT") =>
        new(code, $"{path}: {rule}", false, new InputIssue(path, rule, allowed));

    private sealed record InputIssue(string path, string rule,
        [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] object? allowed);

    public static DomainError Complete(DomainError error)
    {
        if (error.details is null || !(error.code.StartsWith("INVALID_", StringComparison.Ordinal) || error.code == "UNSUPPORTED_UNITS")) return error;
        var details = error.details is JsonElement json ? json : Wire.Element(error.details);
        if (details.ValueKind != JsonValueKind.Object || details.EnumerateObject().Count() != 1 ||
            !details.TryGetProperty("path", out var field) || field.ValueKind != JsonValueKind.String) return error;
        string path = field.GetString()!;
        string prefix = path + ": ";
        return error with { details = new InputIssue(path, error.message.StartsWith(prefix, StringComparison.Ordinal)
            ? error.message.Substring(prefix.Length) : error.message, null) };
    }
}
