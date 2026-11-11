using System.Text.Json;

namespace NanoCAD.Contracts;

public static class DrawingViewsValidation
{
    public static Arguments Parse(JsonElement input)
    {
        if (input.ValueKind != JsonValueKind.Object) throw new DomainException("INVALID_ARGUMENT", "Expected object.");
        var values = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        foreach (var p in input.EnumerateObject())
            if (!values.TryAdd(p.Name, p.Value)) throw new DomainException("INVALID_ARGUMENT", "Duplicate argument: " + p.Name);
        string? geometryViewHandle = null;
        if (values.Remove("geometry_view_handle", out var selected))
        {
            if (selected.ValueKind != JsonValueKind.String)
                throw new DomainException("INVALID_ARGUMENT", "geometry_view_handle must be a view handle from this layout.");
            geometryViewHandle = Validation.NormalizeHandle(selected.GetString()!);
        }
        string? sourceIdentityHandle = null;
        if (values.Remove("source_identity_view_handle", out var sourceIdentity))
        {
            if (sourceIdentity.ValueKind != JsonValueKind.String) throw new DomainException("INVALID_ARGUMENT", "source_identity_view_handle must be one current view handle from the selected layout.");
            sourceIdentityHandle = Validation.NormalizeHandle(sourceIdentity.GetString()!);
        }
        int PageValue(string name, int fallback, int minimum, int maximum)
        {
            if (!values.Remove(name, out var value)) return fallback;
            if (geometryViewHandle == null || value.ValueKind != JsonValueKind.Number || !value.TryGetInt32(out int number) || number < minimum || number > maximum)
                throw new DomainException("INVALID_ARGUMENT", name + " requires a selected geometry_view_handle and an integer in its published page range.");
            return number;
        }
        int start = PageValue("geometry_start_index", 0, 0, int.MaxValue), size = PageValue("geometry_page_size", 100, 1, int.MaxValue);
        bool geometrySources = false;
        if (values.Remove("include_geometry_sources", out var sourceOption))
        {
            if (geometryViewHandle == null || sourceOption.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
                throw new DomainException("INVALID_ARGUMENT", "include_geometry_sources requires a selected geometry_view_handle and a boolean value.");
            geometrySources = sourceOption.GetBoolean();
        }
        if (!values.Remove("layout_handle", out var layout) || values.ContainsKey("handle"))
            throw new DomainException("INVALID_ARGUMENT", "Explicit layout_handle from get_structure layouts is required.");
        values.Add("handle", layout);
        return ModelVariablesValidation.Parse(Wire.Element(values)) with { GeometryViewHandle = geometryViewHandle, GeometryStartIndex = start, GeometryPageSize = size, DrawingViewSourceIdentityHandle = sourceIdentityHandle, IncludeGeometrySources = geometrySources };
    }
}
