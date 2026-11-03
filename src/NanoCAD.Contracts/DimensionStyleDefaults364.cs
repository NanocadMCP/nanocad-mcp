using System.Text.Json;
using System.Text.Json.Nodes;

namespace NanoCAD.Contracts;

// Reusable native annotation defaults. These never convert or drive source geometry.
public sealed record DimensionStyleDefaults364(
    int? LinearPrecision = null, int? AngularPrecision = null, string? DecimalSeparator = null,
    int? LinearUnitFormat = null, int? AngularUnitFormat = null, double? Rounding = null,
    int? LinearZeroSuppression = null, int? AngularZeroSuppression = null,
    string? ToleranceMode = null, double? UpperDeviation = null, double? LowerDeviation = null,
    int? TolerancePrecision = null, int? ToleranceZeroSuppression = null, double? ToleranceHeightFactor = null,
    double? ExtensionOffset = null, double? ExtensionBeyond = null, double? TextGap = null);

public static class DimensionStyleDefaultsValidation364
{
    public static readonly string[] Names = ["linear_precision", "angular_precision", "decimal_separator",
        "linear_unit_format", "angular_unit_format", "rounding", "linear_zero_suppression", "angular_zero_suppression",
        "tolerance_mode", "upper_deviation", "lower_deviation", "tolerance_precision", "tolerance_zero_suppression",
        "tolerance_height_factor", "extension_offset", "extension_beyond", "text_gap"];
    private static DomainException Invalid(string path, string message) => new("INVALID_ARGUMENT", message, false, new { path });
    public static DimensionStyleDefaults364? Parse(JsonElement properties, string path)
    {
        if (!properties.EnumerateObject().Any(p => Names.Contains(p.Name, StringComparer.Ordinal))) return null;
        int? Integer(string name, int minimum, int maximum)
        {
            if (!properties.TryGetProperty(name, out var value)) return null;
            if (value.ValueKind != JsonValueKind.Number || !value.TryGetInt32(out int number) || number < minimum || number > maximum)
                throw Invalid(path + "." + name, $"Expected a native integer from {minimum} to {maximum}.");
            return number;
        }
        double? Number(string name)
        {
            if (!properties.TryGetProperty(name, out var value)) return null;
            if (value.ValueKind != JsonValueKind.Number || !value.TryGetDouble(out double number) || !double.IsFinite(number))
                throw Invalid(path + "." + name, "Expected a finite native number.");
            return number;
        }
        string? Text(string name)
        {
            if (!properties.TryGetProperty(name, out var value)) return null;
            if (value.ValueKind != JsonValueKind.String) throw Invalid(path + "." + name, "Expected a native format choice.");
            return value.GetString();
        }
        var result = new DimensionStyleDefaults364(Integer("linear_precision", 0, 8), Integer("angular_precision", 0, 8),
            Text("decimal_separator"), Integer("linear_unit_format", 1, 6), Integer("angular_unit_format", 0, 4), Number("rounding"),
            Integer("linear_zero_suppression", 0, 15), Integer("angular_zero_suppression", 0, 3), Text("tolerance_mode"),
            Number("upper_deviation"), Number("lower_deviation"), Integer("tolerance_precision", 0, 8),
            Integer("tolerance_zero_suppression", 0, 15), Number("tolerance_height_factor"), Number("extension_offset"),
            Number("extension_beyond"), Number("text_gap"));
        Validate(result, path); return result;
    }
    public static void Validate(DimensionStyleDefaults364 value, string path = "properties")
    {
        void Integer(int? number, int minimum, int maximum, string name)
        { if (number.HasValue && (number.Value < minimum || number.Value > maximum)) throw Invalid(path + "." + name, "Native integer is outside its supported range."); }
        Integer(value.LinearPrecision, 0, 8, "linear_precision"); Integer(value.AngularPrecision, 0, 8, "angular_precision");
        Integer(value.LinearUnitFormat, 1, 6, "linear_unit_format"); Integer(value.AngularUnitFormat, 0, 4, "angular_unit_format");
        Integer(value.LinearZeroSuppression, 0, 15, "linear_zero_suppression"); Integer(value.AngularZeroSuppression, 0, 3, "angular_zero_suppression");
        Integer(value.TolerancePrecision, 0, 8, "tolerance_precision"); Integer(value.ToleranceZeroSuppression, 0, 15, "tolerance_zero_suppression");
        foreach (var (number, name) in new[] { (value.Rounding, "rounding"), (value.UpperDeviation, "upper_deviation"),
            (value.LowerDeviation, "lower_deviation"), (value.ToleranceHeightFactor, "tolerance_height_factor"),
            (value.ExtensionOffset, "extension_offset"), (value.ExtensionBeyond, "extension_beyond"), (value.TextGap, "text_gap") })
            if (number.HasValue && !double.IsFinite(number.Value)) throw Invalid(path + "." + name, "Expected a finite native number.");
        if (value.Rounding < 0 || value.ExtensionOffset < 0 || value.ExtensionBeyond < 0)
            throw Invalid(path, "Rounding and extension distances must be nonnegative; zero is retained.");
        if (value.ToleranceHeightFactor <= 0) throw Invalid(path + ".tolerance_height_factor", "Expected a positive dimensionless text-height factor.");
        if (value.DecimalSeparator != null && value.DecimalSeparator is not ("." or ","))
            throw Invalid(path + ".decimal_separator", "Use dot or comma.");
        if (value.ToleranceMode != null && value.ToleranceMode is not ("none" or "deviation" or "limits"))
            throw Invalid(path + ".tolerance_mode", "Use none, deviation or limits.");
        if (value.UpperDeviation.HasValue != value.LowerDeviation.HasValue ||
            value.UpperDeviation.HasValue && (value.UpperDeviation < value.LowerDeviation || value.ToleranceMode is not ("deviation" or "limits")))
            throw Invalid(path, "Supply both signed deviations, upper >= lower, and explicitly select deviation or limits.");
    }
    public static DimensionVariables Expected(DimensionVariables source, DimensionStyleDefaults364 value)
    {
        Validate(value);
        var result = source with
        {
            dimdec = value.LinearPrecision ?? source.dimdec, dimadec = value.AngularPrecision ?? source.dimadec,
            dimdsep = value.DecimalSeparator == null ? source.dimdsep : value.DecimalSeparator[0],
            dimlunit = value.LinearUnitFormat ?? source.dimlunit, dimaunit = value.AngularUnitFormat ?? source.dimaunit,
            dimrnd = value.Rounding ?? source.dimrnd, dimzin = value.LinearZeroSuppression ?? source.dimzin,
            dimazin = value.AngularZeroSuppression ?? source.dimazin,
            dimtol = value.ToleranceMode == null ? source.dimtol : value.ToleranceMode == "deviation",
            dimlim = value.ToleranceMode == null ? source.dimlim : value.ToleranceMode == "limits",
            dimtp = value.UpperDeviation ?? source.dimtp, dimtm = value.LowerDeviation.HasValue ? -value.LowerDeviation.Value : source.dimtm,
            dimtdec = value.TolerancePrecision ?? source.dimtdec, dimtzin = value.ToleranceZeroSuppression ?? source.dimtzin,
            dimtfac = value.ToleranceHeightFactor ?? source.dimtfac,
            dimexo = value.ExtensionOffset ?? source.dimexo, dimexe = value.ExtensionBeyond ?? source.dimexe,
            dimgap = value.TextGap ?? source.dimgap
        };
        if (value.ToleranceMode is "deviation" or "limits" && result.dimtp < -result.dimtm)
            throw Invalid("properties", "The resulting signed upper deviation is below the lower deviation; read the stored defaults and supply the intended pair.");
        return result;
    }
    // Only explicitly requested setters may change effective inherited fields.
    // A retained value is allowed because equal-valued explicit overrides cannot
    // be distinguished from inheritance using an effective snapshot alone.
    public static string[] Paths(DimensionStyleChanges changes)
    {
        var paths = new List<string>();
        if (changes.DimensionScale.HasValue) paths.Add("dimension_scale");
        if (changes.TextHeight.HasValue) paths.Add("text_height");
        if (changes.ArrowSize.HasValue) paths.Add("arrow_size");
        if (changes.Defaults is not { } d) return paths.ToArray();
        void Add(bool supplied, params string[] names) { if (supplied) paths.AddRange(names.Select(n => "variables." + n)); }
        Add(d.LinearPrecision.HasValue, "dimdec"); Add(d.AngularPrecision.HasValue, "dimadec"); Add(d.DecimalSeparator != null, "dimdsep");
        Add(d.LinearUnitFormat.HasValue, "dimlunit"); Add(d.AngularUnitFormat.HasValue, "dimaunit"); Add(d.Rounding.HasValue, "dimrnd");
        Add(d.LinearZeroSuppression.HasValue, "dimzin"); Add(d.AngularZeroSuppression.HasValue, "dimazin");
        Add(d.ToleranceMode != null, "dimtol", "dimlim"); Add(d.UpperDeviation.HasValue, "dimtp", "dimtm");
        Add(d.TolerancePrecision.HasValue, "dimtdec"); Add(d.ToleranceZeroSuppression.HasValue, "dimtzin");
        Add(d.ToleranceHeightFactor.HasValue, "dimtfac"); Add(d.ExtensionOffset.HasValue, "dimexo");
        Add(d.ExtensionBeyond.HasValue, "dimexe"); Add(d.TextGap.HasValue, "dimgap"); return paths.ToArray();
    }
    public static bool EffectiveFieldsRetained(JsonElement before, JsonElement after, JsonElement expectedStyle,
        string[] permittedPaths, Action cancel, out string? mismatch)
    {
        mismatch = null; cancel();
        if (before.ValueKind != JsonValueKind.Object || after.ValueKind != JsonValueKind.Object || expectedStyle.ValueKind != JsonValueKind.Object)
        { mismatch = "effective_snapshot_shape"; return false; }
        var normalized = JsonNode.Parse(after.GetRawText())!.AsObject();
        foreach (string path in permittedPaths)
        {
            cancel(); var components = path.Split('.');
            if (components.Length is < 1 or > 2) { mismatch = "unproved_field_path"; return false; }
            bool Field(JsonElement root, out JsonElement field)
            {
                field = root;
                foreach (var component in components)
                    if (field.ValueKind != JsonValueKind.Object || !field.TryGetProperty(component, out field)) return false;
                return true;
            }
            if (!Field(before, out var prior) || !Field(after, out var current) || !Field(expectedStyle, out var expected))
            { mismatch = path; return false; }
            if (current.GetRawText() != prior.GetRawText() && current.GetRawText() != expected.GetRawText())
            { mismatch = path; return false; }
            JsonObject owner = components.Length == 1 ? normalized : normalized[components[0]]!.AsObject();
            owner[components[^1]] = JsonNode.Parse(prior.GetRawText());
        }
        cancel(); using var retained = JsonDocument.Parse(normalized.ToJsonString(Wire.Json));
        // Both observations come from the same ordered native snapshot writer.
        if (before.GetRawText() != retained.RootElement.GetRawText())
        {
            mismatch = "unrequested_effective_field";
            foreach (var property in before.EnumerateObject())
            {
                cancel();
                if (!retained.RootElement.TryGetProperty(property.Name, out var current) || property.Value.GetRawText() != current.GetRawText())
                {
                    mismatch = property.Name;
                    if (property.Value.ValueKind == JsonValueKind.Object && current.ValueKind == JsonValueKind.Object)
                        foreach (var child in property.Value.EnumerateObject())
                        { cancel(); if (!current.TryGetProperty(child.Name, out var field) || child.Value.GetRawText() != field.GetRawText()) { mismatch += "." + child.Name; break; } }
                    break;
                }
            }
            return false;
        }
        return true;
    }
}
