using System.Text.Json.Nodes;

namespace NanoCAD.McpServer;

internal static partial class SolidMeasurementSchema409
{
    internal const string FitDescription = "cylindrical_fit measures axial overlap and signed minimum radial clearance of a finite straight external shaft and internal bore on distinct solids. Select complete circular cylindrical bands with parallel axes. Positive clearance allows a radial gap; negative clearance indicates local interference. Clearance is unavailable without axial engagement or with the shaft axis outside the bore. All stations use the reported shaft axis.";

    private static JsonObject FitOutput()
    {
        JsonObject Signed() => new() { ["type"] = "number" };
        JsonObject Band(string role) => Obj(new()
        {
            ["face"] = FaceRef(),
            ["role"] = Const(role),
            ["diameter"] = Number(),
            ["axis_origin"] = Triple(),
            ["axis_direction"] = Triple(),
            ["start_station"] = Signed(),
            ["end_station"] = Signed()
        });
        return Obj(new()
        {
            ["mode"] = Const("cylindrical_fit"),
            ["coordinate_system"] = Const("WCS"),
            ["solver"] = Const("native_trimmed_cylindrical_faces"),
            ["units"] = Obj(new() { ["name"] = Text(), ["value"] = new JsonObject { ["type"] = "integer" } }),
            ["shaft"] = Band("shaft"),
            ["bore"] = Band("bore"),
            ["axial_overlap"] = Number(),
            ["axial_gap"] = Number(),
            ["minimum_radial_clearance"] = Nullable(Signed()),
            ["axis_offset"] = Number(),
            ["fit_status"] = new JsonObject { ["enum"] = new JsonArray("clearance", "interference", "contact_within_tolerance", "axially_separated", "end_contact_within_tolerance", "shaft_axis_outside_bore") },
            ["point_tolerance"] = Number(),
            ["vector_tolerance"] = Number(),
            ["result_scope"] = Text()
        });
    }
}
