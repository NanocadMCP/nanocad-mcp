using System.Text.Json.Nodes;

namespace NanoCAD.McpServer;

internal static class DimensionFormatSchema
{
    internal static JsonObject Edit()
    {
        JsonObject S() => new(){["type"]="string",["minLength"]=1,["maxLength"]=255};
        JsonObject N(bool positive=false) => positive ? new(){["type"]="number",["exclusiveMinimum"]=0,["maximum"]=1e9}
            : new(){["type"]="number",["minimum"]=-1e9,["maximum"]=1e9};
        JsonObject Precision() => new(){["type"]="integer",["minimum"]=0,["maximum"]=8};
        var arrow=N(); arrow["minimum"]=0;
        return new JsonObject{
            ["type"]="object",["additionalProperties"]=false,
            ["properties"]=new JsonObject{
                ["operation"]=new JsonObject{["type"]="string",["const"]="set_dimension_format"},
                ["handles"]=new JsonObject{["type"]="array",["minItems"]=1,["maxItems"]=1,["items"]=new JsonObject{["type"]="string",["pattern"]="^[0-9A-Fa-f]{1,16}$"}},
                ["properties"]=new JsonObject{["type"]="object",["additionalProperties"]=false,["minProperties"]=1,["properties"]=new JsonObject{
                    ["dimension_style"]=S(),["text_height"]=N(true),["arrow_size"]=arrow,["dimension_scale"]=N(true),
                    ["linear_precision"]=Precision(),["angular_precision"]=Precision(),
                    ["decimal_separator"]=new JsonObject{["enum"]=new JsonArray(".",",")},
                    ["tolerance_mode"]=new JsonObject{["enum"]=new JsonArray("none","deviation","limits")},
                    ["upper_deviation"]=Deviation("Signed native DIMTP value; no measurement-unit or DIMLFAC conversion is applied."),["lower_deviation"]=Deviation("Signed lower deviation stored as negative DIMTM; no measurement-unit or DIMLFAC conversion is applied."),["tolerance_precision"]=Precision()}}},
            ["required"]=new JsonArray("operation","handles","properties"),
            ["description"]="Edit the format of one existing native aligned, rotated, radial, diametric, angular or ordinate dimension. Read inspect_objects geometry.format and actual dimension_styles first. Text/arrow sizes are drawing millimeters; dimension_scale is dimensionless. Assigning dimension_style preserves existing per-object overrides; provide desired effective fields explicitly. Deviations are literal native DIMTP and signed negative DIMTM values, NOT universally millimeters or geometry.measurement radians; no DIMLFAC or angular/display-unit conversion is applied. Use the dimension's actual unit convention; do not infer it from measurement_unit. Supply both deviations and select deviation/limits. No invented tolerances: use the task or applicable standard. Measurement references, source geometry and text override are retained; native label placement may adapt. Does not create a driving constraint or associative link. A target must have only format edits in this batch; distinct dimensions may be formatted together. Unknown custom Dimension types/fields are excluded; no manual label-movement policy is changed."};
        JsonObject Deviation(string description){var n=N();n["description"]=description;return n;}
    }
    internal static JsonObject Read()
    {
        JsonObject N() => new(){["type"]="number"}; JsonObject I() => new(){["type"]="integer"};
        return new JsonObject{["type"]="object",["additionalProperties"]=false,["properties"]=new JsonObject{
            ["text_height"]=N(),["arrow_size"]=N(),["dimension_scale"]=N(),["linear_precision"]=I(),["angular_precision"]=I(),
            ["decimal_separator"]=new JsonObject{["type"]="string"},["tolerance_mode"]=new JsonObject{["type"]="string"},
            ["upper_deviation"]=N(),["lower_deviation"]=N(),["tolerance_precision"]=I(),
            ["deviation_units"]=new JsonObject{["type"]="string",["const"]="native_DIMTP_signed_DIMTM_no_conversion"},
            ["linear_factor"]=N(),["linear_unit_format"]=I(),["angular_unit_format"]=I(),["text_style_handle"]=new JsonObject{["type"]="string"}}};
    }
}
