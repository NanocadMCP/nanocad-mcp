using System.Text.Json;

namespace NanoCAD.Contracts;

public sealed record DimensionFormatChanges(string? DimensionStyle, double? TextHeight, double? ArrowSize,
    double? DimensionScale, int? LinearPrecision, int? AngularPrecision, string? DecimalSeparator,
    string? ToleranceMode, double? UpperDeviation, double? LowerDeviation, int? TolerancePrecision);
public sealed record DimensionFormatSpec(string[] Targets, DimensionFormatChanges Properties)
    : EditSpec("set_dimension_format", Targets);

public static partial class ModifyValidation
{
    private static DimensionFormatSpec ParseDimensionFormat(JsonElement item, string path, string[] handles)
    {
        Shape(item, path, "operation", "handles", "properties");
        if(handles.Length != 1) throw Invalid(path + ".handles", "Address one existing dimension per operation.");
        var p = Required(item, "properties", path); var pp = path + ".properties";
        Shape(p, pp, "dimension_style", "text_height", "arrow_size", "dimension_scale", "linear_precision",
            "angular_precision", "decimal_separator", "tolerance_mode", "upper_deviation", "lower_deviation", "tolerance_precision");
        if(!p.EnumerateObject().Any()) throw Invalid(pp, "At least one dimension format property is required.");
        string? S(string name) => p.TryGetProperty(name, out _) ? Text(p, name, pp, 255) : null;
        double? N(string name, bool positive = false)
        {
            if(!p.TryGetProperty(name, out var value)) return null;
            var n = Number(value, pp + "." + name);
            if(positive && n <= 0) throw Invalid(pp + "." + name, "Expected a positive dimension size.");
            return n;
        }
        int? Precision(string name)
        {
            if(!p.TryGetProperty(name, out var value)) return null;
            if(value.ValueKind != JsonValueKind.Number || !value.TryGetInt32(out var n) || n is <0 or >8)
                throw Invalid(pp + "." + name, "Native decimal precision must be an integer from0 to8.");
            return n;
        }
        var separator = S("decimal_separator");
        if(separator != null && separator is not ("." or ",")) throw Invalid(pp + ".decimal_separator", "Use dot or comma.");
        var mode = S("tolerance_mode");
        if(mode != null && mode is not ("none" or "deviation" or "limits")) throw Invalid(pp + ".tolerance_mode", "Use none, deviation or limits.");
        var upper = N("upper_deviation"); var lower = N("lower_deviation");
        if(upper.HasValue != lower.HasValue) throw Invalid(pp, "Supply both signed deviations together.");
        if(upper.HasValue && (upper < lower || mode is not ("deviation" or "limits")))
            throw Invalid(pp, "Signed upper deviation must be >= lower deviation; select deviation or limits mode explicitly.");
        var arrow = N("arrow_size"); if(arrow < 0) throw Invalid(pp + ".arrow_size", "Arrow size cannot be negative.");
        return new(handles, new(S("dimension_style"), N("text_height", true), arrow, N("dimension_scale", true),
            Precision("linear_precision"), Precision("angular_precision"), separator, mode, upper, lower, Precision("tolerance_precision")));
    }
}
