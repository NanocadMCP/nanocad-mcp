using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace NanoCAD.Contracts;

public sealed record ModelParameterArguments(string BridgeId, string DocumentId, string ExpectedRevision, string OperationId,
    string Units, string Handle, string VariableId, string ExpectedExpression, double Value,
    string? RequestedExpression = null, bool Propagate = false,string[]? ScopeHandles=null) : IWriteArguments
{
    public string Operation => "set_model_parameter";
    public string Expression => RequestedExpression ?? Value.ToString("R", CultureInfo.InvariantCulture);
    public bool UsesExpressionPlan => RequestedExpression != null || Propagate || ScopeHandles!=null;
    public string Fingerprint() => Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(new
    { Operation, BridgeId, DocumentId, ExpectedRevision, Units, Handle, VariableId, ExpectedExpression, Value, RequestedExpression, Propagate, ScopeHandles }, Wire.Json)));
}
public sealed record ModelParameterResult(string operation_id, string status, bool replayed, EntityRef body_ref,
    string? feature_id, string variable_id, string previous_expression, string expression, double value,
    string value_unit, double volume, string undo_history,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] ModelParameterChange[]? parameter_changes = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] EntityRef[]? rebuilt_bodies = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? owner_kind = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? sketch_id = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? constraint_id = null)
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? model_healthy { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? downstream_values_changed { get; init; }
}
public sealed record ModelParameterChange(string variable_id, string? feature_id, string previous_expression,
    string expression, double previous_value, double value,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? value_unit = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] EntityRef? body_ref = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? owner_kind = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? sketch_id = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? constraint_id = null);

public static class ModelParameterValidation
{
    public static ModelParameterArguments Parse(JsonElement input)
    {
        void Need(bool condition, string message) { if (!condition) throw new DomainException("INVALID_ARGUMENT", message); }
        Need(input.ValueKind == JsonValueKind.Object, "Expected object.");
        var fields = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        foreach (var p in input.EnumerateObject())
            Need(p.Name is "bridge_instance_id" or "document_id" or "expected_revision" or "operation_id" or "units" or "handle" or "variable_id" or "expected_expression" or "value" or "expression" or "propagate" or "scope_handles" && fields.TryAdd(p.Name, p.Value), "Unknown or duplicate field.");
        string Text(string name, int? max = null)
        {
            Need(fields.TryGetValue(name, out var value) && value.ValueKind == JsonValueKind.String, "Missing string: " + name);
            var text = value.GetString()!;
            Need(text.Length > 0 && (max == null || text.Length <= max) && !text.Any(char.IsControl), "Invalid string: " + name);
            return text;
        }
        string Id(string name)
        {
            Need(Guid.TryParseExact(Text(name, 36), "D", out var id) && id != Guid.Empty, "Expected nonempty UUID: " + name);
            return id.ToString("D");
        }
        string bridge = Id("bridge_instance_id"), document = Id("document_id"), operation = Id("operation_id");
        var revision = Text("expected_revision", 20);
        Need(revision.All(c => c is >= '0' and <= '9') && ulong.TryParse(revision, NumberStyles.None, CultureInfo.InvariantCulture, out _), "Invalid revision.");
        var units = Text("units", 32);
        if (units != "Millimeters") throw new DomainException("UNSUPPORTED_UNITS", "Explicit Millimeters required.");
        Need(ulong.TryParse(Text("handle", 16), NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out var handle) && handle != 0, "Invalid body handle.");
        Need(Guid.TryParseExact(Text("variable_id", 38), "B", out var variable) && variable != Guid.Empty, "Expected opaque braced MCS UUID from get_model_variables.");
        var expected = Text("expected_expression");
        Need(fields.ContainsKey("value") != fields.ContainsKey("expression"), "Supply exactly one of value or expression.");
        double value = 0; string? expression = null;
        if(fields.ContainsKey("expression")) { expression=Text("expression",1024); ModelArithmetic.Parse(expression); }
        else
        {
            var number=fields["value"];
            Need(number.ValueKind==JsonValueKind.Number&&number.TryGetDouble(out value),"Expected numeric value.");
            Need(double.IsFinite(value), "Use a finite scalar in the verified native control's actual units and value domain.");
        }
        bool propagate=false;
        if(fields.TryGetValue("propagate",out var propagation))
        { Need(propagation.ValueKind is JsonValueKind.True or JsonValueKind.False,"propagate must be boolean."); propagate=propagation.GetBoolean(); }
        return new(bridge, document, ulong.Parse(revision, CultureInfo.InvariantCulture).ToString(CultureInfo.InvariantCulture), operation,
            units, handle.ToString("X", CultureInfo.InvariantCulture), variable.ToString("B").ToUpperInvariant(), expected, value, expression, propagate,ModelParameterScope.Parse(input,handle.ToString("X",CultureInfo.InvariantCulture)));
    }
}
