using System.Text.Json;

namespace NanoCAD.Contracts;

public sealed record ModelSupportRecoveryPreview(string PlaneId, string FrameReferencePolicy)
{
    public static ModelSupportRecoveryPreview Parse(JsonElement value)
    {
        if (value.ValueKind != JsonValueKind.Object)
            throw new DomainException("INVALID_ARGUMENT", "support_recovery_preview requires a selected WorkPlane.");
        string? plane = null;
        string? policy = null;
        foreach (var field in value.EnumerateObject())
        {
            if (field.Name == "plane_id" && plane == null && field.Value.ValueKind == JsonValueKind.String &&
                Guid.TryParseExact(field.Value.GetString(), "B", out var id) && id != Guid.Empty)
                plane = id.ToString("B").ToUpperInvariant();
            else if (field.Name == "frame_reference_policy" && policy == null && field.Value.ValueKind == JsonValueKind.String &&
                field.Value.GetString() is "preserve" or "use_target_datum")
                policy = field.Value.GetString();
            else
                throw new DomainException("INVALID_ARGUMENT", "Invalid, missing or repeated support recovery field: " + field.Name);
        }
        if (plane == null)
            throw new DomainException("INVALID_ARGUMENT", "Select a specific replacement WorkPlane by its current plane_id.");
        return new(plane, policy ?? "preserve");
    }
}
