using System.Text.Json;

namespace NanoCAD.Contracts;

public sealed record MarkerDimensionPage(int StartIndex, int PageSize);

public static class DrawingViewsValidation
{
    public static Arguments Parse(JsonElement input)
    {
        if (input.ValueKind != JsonValueKind.Object) throw new DomainException("INVALID_ARGUMENT", "Expected object.");
        var values = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        foreach (var p in input.EnumerateObject())
            if (!values.TryAdd(p.Name, p.Value)) throw new DomainException("INVALID_ARGUMENT", "Duplicate argument: " + p.Name);
        var placement = values.Remove("sheet_placement", out var plan) ? SheetPlacementRequest.Parse(plan) : null;
        if (placement != null && (values.ContainsKey("source_identity_view_handle") || values.ContainsKey("geometry_view_handle")))
            throw new DomainException("INVALID_ARGUMENT", "Request sheet placement as a separate read from projection detail or source identity.");
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
        int? marker = null;
        if (values.Remove("geometry_source_marker", out var markerValue))
        {
            if (geometryViewHandle == null || markerValue.ValueKind != JsonValueKind.Number ||
                !markerValue.TryGetInt32(out int value) || value < 0 || values.ContainsKey("geometry_start_index") ||
                values.ContainsKey("geometry_page_size") || values.ContainsKey("include_geometry_sources"))
                throw new DomainException("INVALID_ARGUMENT", "geometry_source_marker requires one view and a nonnegative marker, without geometry pagination or include_geometry_sources.");
            marker = value;
        }
        int start = PageValue("geometry_start_index", 0, 0, int.MaxValue), size = PageValue("geometry_page_size", 100, 1, int.MaxValue);
        MarkerDimensionPage? dimensions = null;
        if (values.Remove("marker_dimensions", out var options))
        {
            if (marker == null || sourceIdentityHandle != null || options.ValueKind != JsonValueKind.Object)
                throw new DomainException("INVALID_ARGUMENT", "marker_dimensions requires a selected view and geometry_source_marker in its own marker read.");
            var seen = new HashSet<string>(StringComparer.Ordinal);
            int first = 0, count = 100;
            foreach (var property in options.EnumerateObject())
            {
                if (!seen.Add(property.Name) || property.Name is not ("start_index" or "page_size") ||
                    property.Value.ValueKind != JsonValueKind.Number || !property.Value.TryGetInt32(out int value) ||
                    value < (property.Name == "start_index" ? 0 : 1))
                    throw new DomainException("INVALID_ARGUMENT", "Invalid marker_dimensions page option: " + property.Name);
                if (property.Name == "start_index") first = value;
                else count = value;
            }
            dimensions = new(first, count);
        }
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
        return ModelVariablesValidation.Parse(Wire.Element(values)) with { GeometryViewHandle = geometryViewHandle, GeometryStartIndex = start, GeometryPageSize = size, DrawingViewSourceIdentityHandle = sourceIdentityHandle, IncludeGeometrySources = geometrySources, GeometrySourceMarker = marker, MarkerDimensions = dimensions, SheetPlacement = placement };
    }
}
