using System.Text.Json;
using NanoCAD.Contracts;

namespace NanoCADBridge;

internal static class ModelParameterDimensionBinding
{
    internal sealed record Anchor(int Slot, string Source, int Marker, double? Parameter, string? Data);
    internal sealed record Snapshot(string DimensionHandle, string LayoutHandle, string ViewHandle, string DimensionId,
        string ViewId, string BodyId, Anchor[] Anchors, ParameterDimensionReading Reading)
    {
        internal ParameterDimensionObservation Compare(Snapshot? current, string? unavailableCode = null)
        {
            if (current == null) return new(DimensionHandle, "unavailable", Reading, null, unavailableCode ?? "OBSERVATION_UNAVAILABLE");
            if (Anchors.Any(a => !a.Parameter.HasValue) || current.Anchors.Any(a => !a.Parameter.HasValue))
                return new(DimensionHandle, "unavailable", Reading, current.Reading, "ANCHOR_PARAMETER_UNAVAILABLE");
            return new(DimensionHandle, SameLinks(current) ? "same_stored_anchors" : "stored_anchors_changed", Reading, current.Reading, null);
        }

        private bool SameLinks(Snapshot other) => LayoutHandle == other.LayoutHandle && ViewHandle == other.ViewHandle &&
            DimensionId == other.DimensionId && ViewId == other.ViewId && BodyId == other.BodyId && Anchors.SequenceEqual(other.Anchors);
    }

    internal static DomainException Unavailable(string handle, string reason) =>
        new("PROJECTED_DIMENSION_ASSOCIATION_UNAVAILABLE", $"Dimension {handle}: {reason}");

    internal static bool NominalMatches(double nominal, double expected) => double.IsFinite(nominal) && double.IsFinite(expected) && expected > 0 &&
        Math.Abs(nominal - expected) <= Math.Max(1e-7, Math.Abs(expected) * 1e-9);

    internal static Snapshot Read(string handle, string layoutHandle, JsonElement raw)
    {
        var links = raw.GetProperty("links");
        if (!raw.GetProperty("links_complete").GetBoolean() || !raw.GetProperty("stored_anchor_parameters_available").GetBoolean() ||
            links.GetArrayLength() != 1)
            throw Unavailable(handle, "Expected complete local stored anchors on one projection view.");
        var link = links[0];
        var sources = link.GetProperty("source_mc_ids");
        if (!link.GetProperty("healthy").GetBoolean() || sources.GetArrayLength() != 1)
            throw Unavailable(handle, "Expected a healthy view with one identified source body.");
        var measurement = raw.GetProperty("measurement");
        if (measurement.ValueKind != JsonValueKind.Object || !measurement.TryGetProperty("sheet_unit", out var unit) || unit.GetString() != "drawing_units")
            throw Unavailable(handle, "A paper-space length measurement is unavailable.");
        double? Number(string name) => measurement.TryGetProperty(name, out var field) && field.ValueKind == JsonValueKind.Number &&
            field.TryGetDouble(out double number) && double.IsFinite(number) ? number : null;
        var sheet = Number("sheet_geometry");
        var projected = Number("physical_length_from_projection");
        var denominator = Number("view_scale_denominator");
        var nominal = Number("native_nominal_length");
        var factor = Number("native_measurement_factor");
        bool? valid = measurement.TryGetProperty("native_value_is_valid", out var flag) && flag.ValueKind is JsonValueKind.True or JsonValueKind.False
            ? flag.GetBoolean() : null;
        bool calibrated = projected is > 0 && denominator is > 0 && sheet is > 0;
        string nominalComparison = !calibrated || !nominal.HasValue || valid != true ? "unavailable" :
            NominalMatches(nominal.Value, projected!.Value) ? "match" : "mismatch";
        string factorComparison = !calibrated || !factor.HasValue ? "unavailable" : factor.Value == denominator ? "match" : "mismatch";
        var viewHandle = link.GetProperty("view_ref").GetProperty("handle").GetString()!;
        var viewId = link.GetProperty("view_mc_id").GetString()!;
        string? sheetReason = !sheet.HasValue && measurement.TryGetProperty("unavailable_reason", out var reason) ? reason.GetString() : null;
        var reading = new ParameterDimensionReading(layoutHandle, viewHandle, link.GetProperty("source_refs")[0].GetProperty("handle").GetString()!,
            sheet, sheetReason, projected, denominator, factor, nominal, valid, nominalComparison, factorComparison, measurement.GetProperty("text_mode").GetString()!);
        var anchors = raw.GetProperty("anchors").EnumerateArray().Where(a => !a.GetProperty("is_null").GetBoolean())
            .Select(a => new Anchor(a.GetProperty("slot").GetInt32(), a.GetProperty("source_mc_id").GetString()!.ToUpperInvariant(), a.GetProperty("gs_marker").GetInt32(),
                a.GetProperty("parameter").ValueKind == JsonValueKind.Number ? a.GetProperty("parameter").GetDouble() : null,
                a.GetProperty("data").GetString())).ToArray();
        if (anchors.Length == 0 || anchors.Any(anchor => !string.Equals(anchor.Source, viewId, StringComparison.OrdinalIgnoreCase)))
            throw Unavailable(handle, "Expected stored anchors from the selected view.");
        return new(handle, layoutHandle, viewHandle,
            raw.GetProperty("dimension_mc_id").GetString()!, viewId, sources[0].GetString()!, anchors, reading);
    }
}
