using System.Text.Json;

namespace NanoCAD.Contracts;

public static class ModelVariablesValidation
{
    public static Arguments Parse(JsonElement input)
    {
        if (input.ValueKind != JsonValueKind.Object || input.EnumerateObject().Any(p => p.Name is not ("bridge_instance_id" or "document_id" or "expected_revision" or "handle")))
            throw new DomainException("INVALID_ARGUMENT", "Model variables accepts bridge_instance_id, document_id, expected_revision and one body handle.");
        var values = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        foreach (var p in input.EnumerateObject())
            if (!values.TryAdd(p.Name, p.Value)) throw new DomainException("INVALID_ARGUMENT", "Duplicate argument: " + p.Name);
        if (!values.Remove("handle", out var handle) || handle.ValueKind != JsonValueKind.String)
            throw new DomainException("INVALID_ARGUMENT", "A Mc3dSolid body handle is required.");
        values.Add("handles", Wire.Element(new[] { handle.GetString() }));
        var args = Validation.Parse("inspect_objects", Wire.Element(values));
        if (args.BridgeId == null || args.ExpectedRevision == null)
            throw new DomainException("INVALID_ARGUMENT", "Model variables requires explicit bridge/document/revision.");
        return args;
    }
}
