using System.Text.Json;

namespace NanoCAD.Contracts;

public static class SketchConstraintsValidation
{
    public static Arguments Parse(JsonElement input)
    {
        if (input.ValueKind != JsonValueKind.Object) throw new DomainException("INVALID_ARGUMENT", "Expected object.");
        var values = new Dictionary<string,JsonElement>(StringComparer.Ordinal);
        foreach (var p in input.EnumerateObject())
            if (!values.TryAdd(p.Name,p.Value)) throw new DomainException("INVALID_ARGUMENT", "Duplicate argument: " + p.Name);
        if (!values.Remove("sketch_id",out var value) || value.ValueKind != JsonValueKind.String ||
            !Guid.TryParseExact(value.GetString(),"B",out var id) || id == Guid.Empty)
            throw new DomainException("INVALID_ARGUMENT", "A braced opaque sketch_id from current model history is required.");
        return ModelVariablesValidation.Parse(Wire.Element(values)) with { ModelObjectId = id.ToString("B").ToUpperInvariant() };
    }
}
