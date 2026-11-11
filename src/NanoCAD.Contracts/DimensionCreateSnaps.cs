using System.Text.Json;
using System.Text.Json.Serialization;

namespace NanoCAD.Contracts;

public enum DimensionPointRole
{
    Center, XLine1, XLine2, DimLinePoint, ChordPoint, FarChordPoint,
    XLine1Start, XLine1End, XLine2Start, XLine2End, ArcPoint,
    Origin, DefiningPoint, LeaderEndPoint, OverrideCenter, JogPoint
}

public sealed record DimensionCreatePoint(DimensionPointRole Role, PlanarPointInput Input);

public sealed record SnappedDimensionSpec(string Kind, DimensionCreatePoint[] Points,
    double Rotation, double LeaderLength, string? Axis, double JogAngle,
    string DimensionStyle, string TextOverride, string LayerName, DimensionSourcePoint385[]? Associations)
    : EntitySpec("Dimension", LayerName), IDimensionCreation, INativeSnappedCreation
{
    [JsonIgnore]
    public string SourcePath { get; init; } = "$.entities[0]";

    public EntitySpec Materialize(Func<PlanarPointInput, Point2> resolve)
    {
        var fields = new Dictionary<string, object>(StringComparer.Ordinal)
        {
            ["type"] = "Dimension",
            ["kind"] = Kind,
            ["layer"] = Layer,
            ["dimension_style"] = DimensionStyle,
            ["text_override"] = TextOverride
        };
        foreach (var point in Points)
        {
            var coordinate = resolve(point.Input);
            fields.Add(DimensionCreateSnaps.Field(point.Role), new[] { coordinate.X, coordinate.Y });
        }
        if (Kind == "rotated") fields.Add("rotation", Rotation);
        if (Kind is "radial" or "diametric") fields.Add("leader_length", LeaderLength);
        if (Kind == "ordinate") fields.Add("axis", Axis!);
        if (Kind == "jogged_radial") fields.Add("jog_angle", JogAngle);
        if (Associations != null)
            fields.Add("associations", Associations.Select(binding => new
            { role = binding.Role, source_handle = binding.SourceHandle, snap = binding.Snap }).ToArray());
        return CreateValidation.ParseResolvedDimension(Wire.Element(fields), SourcePath, Layer);
    }
}

public static class DimensionCreateSnaps
{
    public static bool Supports(string kind) => kind is "aligned" or "rotated" or "radial" or "diametric"
        or "angular_3point" or "angular_2line" or "ordinate" or "arc_length" or "jogged_radial";

    public static bool HasSnap(JsonElement item) => item.EnumerateObject()
        .Any(field => field.Name.EndsWith("_snap", StringComparison.Ordinal));

    public static DimensionPointRole[] Roles(string kind) => kind switch
    {
        "aligned" or "rotated" => [DimensionPointRole.XLine1, DimensionPointRole.XLine2, DimensionPointRole.DimLinePoint],
        "radial" => [DimensionPointRole.Center, DimensionPointRole.ChordPoint],
        "diametric" => [DimensionPointRole.ChordPoint, DimensionPointRole.FarChordPoint],
        "angular_3point" or "arc_length" =>
            [DimensionPointRole.Center, DimensionPointRole.XLine1, DimensionPointRole.XLine2, DimensionPointRole.ArcPoint],
        "angular_2line" => [DimensionPointRole.XLine1Start, DimensionPointRole.XLine1End,
            DimensionPointRole.XLine2Start, DimensionPointRole.XLine2End, DimensionPointRole.ArcPoint],
        "ordinate" => [DimensionPointRole.Origin, DimensionPointRole.DefiningPoint, DimensionPointRole.LeaderEndPoint],
        "jogged_radial" => [DimensionPointRole.Center, DimensionPointRole.ChordPoint,
            DimensionPointRole.OverrideCenter, DimensionPointRole.JogPoint],
        _ => throw InputError.Create("$.kind", "Select an ordinary coordinate-based dimension kind.")
    };

    public static string Field(DimensionPointRole role) => role switch
    {
        DimensionPointRole.Center => "center",
        DimensionPointRole.XLine1 => "xline1",
        DimensionPointRole.XLine2 => "xline2",
        DimensionPointRole.DimLinePoint => "dimline_point",
        DimensionPointRole.ChordPoint => "chord_point",
        DimensionPointRole.FarChordPoint => "far_chord_point",
        DimensionPointRole.XLine1Start => "xline1_start",
        DimensionPointRole.XLine1End => "xline1_end",
        DimensionPointRole.XLine2Start => "xline2_start",
        DimensionPointRole.XLine2End => "xline2_end",
        DimensionPointRole.ArcPoint => "arc_point",
        DimensionPointRole.Origin => "origin",
        DimensionPointRole.DefiningPoint => "defining_point",
        DimensionPointRole.LeaderEndPoint => "leader_end_point",
        DimensionPointRole.OverrideCenter => "override_center",
        DimensionPointRole.JogPoint => "jog_point",
        _ => throw new ArgumentOutOfRangeException(nameof(role))
    };
}

public static partial class CreateValidation
{
    internal static EntitySpec ParseResolvedDimension(JsonElement item, string path, string layer) =>
        ParseDimension(item, path, layer);

    private static SnappedDimensionSpec ParseSnappedDimension(JsonElement item, string kind, string path, string layer)
    {
        var roles = DimensionCreateSnaps.Roles(kind);
        var allowed = new List<string> { "type", "kind", "layer", "dimension_style", "text_override" };
        foreach (var role in roles)
        {
            string field = DimensionCreateSnaps.Field(role);
            allowed.Add(field); allowed.Add(field + "_snap");
        }
        if (kind == "rotated") allowed.Add("rotation");
        if (kind is "radial" or "diametric") allowed.Add("leader_length");
        if (kind is "aligned" or "rotated" or "radial" or "diametric") allowed.Add("associations");
        if (kind == "ordinate") allowed.Add("axis");
        if (kind == "jogged_radial") allowed.Add("jog_angle");
        Shape(item, path, allowed.ToArray());

        string style = Text(item, "dimension_style", path, 255);
        string text = Text(item, "text_override", path, 4096, "<>");
        if ((kind is "angular_3point" or "angular_2line" or "ordinate" or "arc_length" or "jogged_radial") &&
            !DimensionTextContent.PreservesMeasurement(text))
            throw Invalid(path + ".text_override", "Retain one <> measurement token with ordinary literal affixes.");
        var points = roles.Select(role => new DimensionCreatePoint(role,
            PlanarPointInput.Parse(item, DimensionCreateSnaps.Field(role), path))).ToArray();
        double rotation = 0, leader = 0, jog = 0;
        string? axis = null;
        if (kind == "rotated")
        {
            rotation = Number(Required(item, "rotation", path), path + ".rotation", 0, Math.PI * 2);
            if (rotation >= Math.PI * 2) throw Invalid(path + ".rotation", "Rotation must be less than 2*pi.", "INVALID_GEOMETRY");
        }
        if ((kind is "radial" or "diametric") && item.TryGetProperty("leader_length", out var length))
            leader = Number(length, path + ".leader_length", 0);
        if (kind == "ordinate")
        {
            axis = Text(item, "axis", path, 1);
            if (axis is not ("x" or "y")) throw Invalid(path + ".axis", "Ordinate axis must be x or y.");
        }
        if (kind == "jogged_radial")
        {
            jog = Number(Required(item, "jog_angle", path), path + ".jog_angle", 0, Math.PI);
            if (jog <= 0 || jog >= Math.PI) throw Invalid(path + ".jog_angle", "Jog angle must be strictly between 0 and pi.");
        }
        var associations = item.TryGetProperty("associations", out var bindings)
            ? DimensionAssociationValidation385.ParseBindings(bindings, path + ".associations") : null;
        return new(kind, points, rotation, leader, axis, jog, style, text, layer, associations) { SourcePath = path };
    }
}
