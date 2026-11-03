using System.Text.Json.Nodes;

namespace NanoCAD.McpServer;

public static class DimensionStyleDefaultsSchema364
{
    private static readonly (string Public, string Native, string Description)[] Fields =
    [
        ("linear_precision", "dimdec", "Native DIMDEC primary linear precision, integer0..8."),
        ("angular_precision", "dimadec", "Native DIMADEC angular precision, integer0..8."),
        ("decimal_separator", "dimdsep", "Native DIMDSEP: dot or comma; readback is the exact UTF-16 code unit."),
        ("linear_unit_format", "dimlunit", "Native DIMLUNIT:1 scientific,2 decimal,3 engineering,4 architectural,5 fractional,6 Windows desktop. This formats annotation; it does not convert geometry."),
        ("angular_unit_format", "dimaunit", "Native DIMAUNIT:0 decimal degrees,1 degrees/minutes/seconds,2 gradians,3 radians,4 surveyor bearings. Source angular measurement remains separate."),
        ("rounding", "dimrnd", "Native DIMRND nonnegative primary linear rounding increment;0 disables rounding. No implicit model-unit or DIMLFAC conversion."),
        ("linear_zero_suppression", "dimzin", "Native DIMZIN bitmask0..15. Leading/trailing zeros use bits4/8; native feet/inches behavior uses bits0/1 according to the selected unit format."),
        ("angular_zero_suppression", "dimazin", "Native DIMAZIN mask0..3:0 none,1 leading,2 trailing,3 both."),
        ("tolerance_mode", "dimtol", "deviation enables DIMTOL and disables DIMLIM; limits enables DIMLIM and disables DIMTOL; none disables both. Omitted mode preserves both exact native flags."),
        ("upper_deviation", "dimtp", "Signed literal native DIMTP. Supply both signed deviations with deviation/limits and upper >= lower. No automatic mm/radian/display-unit conversion; use the actual DIM convention."),
        ("lower_deviation", "dimtm", "Signed public lower deviation is stored as negative native DIMTM. Supply both deviations together. Readback exposes raw DIMTM, so public lower = -variables.dimtm."),
        ("tolerance_precision", "dimtdec", "Native DIMTDEC tolerance decimal precision, integer0..8."),
        ("tolerance_zero_suppression", "dimtzin", "Native DIMTZIN tolerance zero-suppression mask0..15; interpretation follows native units. Alternate tolerance variables stay unchanged."),
        ("tolerance_height_factor", "dimtfac", "Native DIMTFAC positive dimensionless tolerance text-height factor."),
        ("extension_offset", "dimexo", "Native DIMEXO nonnegative extension-line origin offset, in drawing length units before dimension-scale presentation."),
        ("extension_beyond", "dimexe", "Native DIMEXE nonnegative extension-line extension beyond the dimension line, in drawing length units before dimension-scale presentation."),
        ("text_gap", "dimgap", "Native DIMGAP signed drawing length; negative native values request a box around dimension text. Preserve the signed value; do not normalize it to a positive spacing.")
    ];
    public static void Add(JsonObject properties)
    {
        JsonObject Integer(int min, int max) => new() { ["type"] = "integer", ["minimum"] = min, ["maximum"] = max };
        JsonObject Number() => new() { ["type"] = "number" };
        foreach (var (name, _, description) in Fields)
        {
            JsonObject schema = name switch
            {
                "linear_precision" or "angular_precision" or "tolerance_precision" => Integer(0, 8),
                "linear_unit_format" => Integer(1, 6), "angular_unit_format" => Integer(0, 4),
                "linear_zero_suppression" or "tolerance_zero_suppression" => Integer(0, 15),
                "angular_zero_suppression" => Integer(0, 3),
                "decimal_separator" => new() { ["enum"] = new JsonArray(".", ",") },
                "tolerance_mode" => new() { ["enum"] = new JsonArray("none", "deviation", "limits") },
                _ => Number()
            };
            if (name is "rounding" or "extension_offset" or "extension_beyond") schema["minimum"] = 0;
            if (name == "tolerance_height_factor") schema["exclusiveMinimum"] = 0;
            schema["description"] = description; properties[name] = schema;
        }
    }
    public static void DescribeReadVariables(JsonObject variables)
    {
        foreach (var (_, name, description) in Fields) variables["properties"]![name]!["description"] = description;
        variables["properties"]!["dimlim"]!["description"] = "Raw native DIMLIM. A read preserves it independently from DIMTOL; the write tolerance_mode deliberately sets both flags.";
    }
    public static void Deviations(JsonObject propertiesObject)
    {
        propertiesObject["dependentRequired"] = new JsonObject
        {
            ["upper_deviation"] = new JsonArray("lower_deviation", "tolerance_mode"),
            ["lower_deviation"] = new JsonArray("upper_deviation", "tolerance_mode")
        };
        propertiesObject["allOf"] = new JsonArray(new JsonObject
        {
            ["if"] = new JsonObject { ["anyOf"] = new JsonArray(
                new JsonObject { ["required"] = new JsonArray("upper_deviation") },
                new JsonObject { ["required"] = new JsonArray("lower_deviation") }) },
            ["then"] = new JsonObject { ["properties"] = new JsonObject {
                ["tolerance_mode"] = new JsonObject { ["enum"] = new JsonArray("deviation", "limits") } } }
        });
    }
}
