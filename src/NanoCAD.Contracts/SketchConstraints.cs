using System.Text.Json;

namespace NanoCAD.Contracts;

public static class SketchConstraintsValidation
{
    public static Arguments Parse(JsonElement input)
    {
        if (input.ValueKind != JsonValueKind.Object) throw new DomainException("INVALID_ARGUMENT", "Expected object.");
        var values = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        foreach (var p in input.EnumerateObject())
            if (!values.TryAdd(p.Name, p.Value)) throw new DomainException("INVALID_ARGUMENT", "Duplicate argument: " + p.Name);
        if (!values.Remove("sketch_id", out var value) || value.ValueKind != JsonValueKind.String ||
            !Guid.TryParseExact(value.GetString(), "B", out var id) || id == Guid.Empty)
            throw new DomainException("INVALID_ARGUMENT", "A braced opaque sketch_id from current model history is required.");
        bool measure = false;
        if (values.Remove("measure_smooth", out var observe))
        {
            if (observe.ValueKind is not (JsonValueKind.True or JsonValueKind.False)) throw new DomainException("INVALID_ARGUMENT", "measure_smooth must be boolean.");
            measure = observe.GetBoolean();
        }
        bool documentScope = false;
        if (values.Remove("sketch_scope", out var scope))
        {
            if (scope.ValueKind != JsonValueKind.String || scope.GetString() is not ("body_history" or "document"))
                throw new DomainException("INVALID_ARGUMENT", "sketch_scope must be body_history or document.");
            documentScope = scope.GetString() == "document";
        }
        return ModelVariablesValidation.Parse(Wire.Element(values)) with { ModelObjectId = id.ToString("B").ToUpperInvariant(), MeasureSmooth = measure, DocumentSketchScope = documentScope };
    }
}
