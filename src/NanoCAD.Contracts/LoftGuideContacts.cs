using System.Text.Json;

namespace NanoCAD.Contracts;

public static class LoftGuideContacts
{
    public const string Mode = "loft_guide_contacts";

    internal static GeometryAnalysisArguments Parse(JsonElement input)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var readFields = new Dictionary<string, JsonElement>();
        string? guide = null;
        foreach (var field in input.EnumerateObject())
        {
            if (!seen.Add(field.Name)) throw Invalid("Duplicate argument: " + field.Name);
            switch (field.Name)
            {
                case "guide_handle":
                    if (field.Value.ValueKind != JsonValueKind.String) throw Invalid("guide_handle must be a handle string.");
                    guide = Validation.NormalizeHandle(field.Value.GetString()!);
                    break;
                case "bridge_instance_id": case "document_id": case "expected_revision": case "handles":
                    readFields.Add(field.Name, field.Value);
                    break;
                case "mode": break;
                default: throw Invalid("Unknown loft guide argument: " + field.Name);
            }
        }
        var read = Validation.Parse("inspect_objects", Wire.Element(readFields));
        if (guide == null || read.BridgeId == null || read.DocumentId == null || read.ExpectedRevision == null || read.Handles is not { Length: > 0 })
            throw Invalid("Supply a guide_handle, ordered section handles and an explicit bridge, document and revision.");
        if (read.Handles.Contains(guide)) throw Invalid("The guide cannot also be a section.");
        return new(read, Mode, null) { GuideHandle = guide };
    }

    private static DomainException Invalid(string message) => new("INVALID_ARGUMENT", message);
}
