using System.Text.Json;

namespace NanoCAD.Contracts;

public interface IDimensionCreation { string DimensionStyle { get; } string TextOverride { get; } }
public sealed record DimensionSpec(string Kind, Point2 First, Point2 Second, Point2? DimLinePoint, double Rotation,
    double LeaderLength, string DimensionStyle, string TextOverride, string LayerName, DimensionSourcePoint385[]? Associations = null) : EntitySpec("Dimension", LayerName), IDimensionCreation;
public sealed record ProjectedAlignedDimensionSpec(string ViewHandle, int LineIndex, Point2 DimLinePoint, string LayerName)
    : EntitySpec("Dimension", LayerName)
{ public string Kind => "projected_aligned"; }
public sealed record ProjectedCircleCentersSpec(string ViewHandle, int FirstMarker, int SecondMarker, Point2 DimLinePoint, string LayerName)
    : EntitySpec("Dimension", LayerName)
{ public string Kind => "projected_aligned"; }
public sealed record ProjectedDiametricDimensionSpec(string ViewHandle, int ArcIndex, Point2 DimLinePoint, string LayerName)
    : EntitySpec("Dimension", LayerName)
{ public string Kind => "projected_diametric"; }
public sealed record ProjectedRadialDimensionSpec(string ViewHandle, int ArcIndex, Point2 DimLinePoint, string LayerName)
    : EntitySpec("Dimension", LayerName)
{ public string Kind => "projected_radial"; }
public sealed record ProjectedArcLengthDimensionSpec(string ViewHandle, int ArcIndex, Point2 DimLinePoint, string LayerName)
    : EntitySpec("Dimension", LayerName)
{ public string Kind => "projected_arc_length"; }
public sealed record ProjectedAngularDimensionSpec(string ViewHandle, int ArcIndex, Point2 DimLinePoint, string LayerName)
    : EntitySpec("Dimension", LayerName)
{ public string Kind => "projected_angular"; }
public sealed record ProjectedCornerDimensionSpec(string ViewHandle, int FirstLineIndex, int SecondLineIndex, Point2 DimLinePoint, string LayerName)
    : EntitySpec("Dimension", LayerName)
{ public string Kind => "projected_angular"; }

public sealed record ProjectedCornerGeometry(int FirstVertex, int SecondVertex, bool ReverseRays, double Sweep)
{
    public const double EndpointTolerance = 1e-7;
    public static ProjectedCornerGeometry Resolve(Point2 a, Point2 b, Point2 c, Point2 d, Point2 placement)
    {
        const string error = "INVALID_GEOMETRY";
        var first = new[] { a, b }; var second = new[] { c, d };
        if (AngularDimensionGeometry.Length(a, b) < .001 || AngularDimensionGeometry.Length(c, d) < .001)
            throw new DomainException(error, "Both projected rays must have nonzero length.");
        var shared = (from i in new[] { 0, 1 }
                      from j in new[] { 0, 1 }
                      where AngularDimensionGeometry.Length(first[i], second[j]) <= EndpointTolerance
                      select (i, j)).ToArray();
        if (shared.Length != 1) throw new DomainException(error, "Select two projected lines with one shared native endpoint.");
        var (i1, i2) = shared[0]; var vertex = first[i1];
        var ray1 = AngularDimensionGeometry.Difference(first[1 - i1], vertex);
        var ray2 = AngularDimensionGeometry.Difference(second[1 - i2], vertex);
        var turn = AngularDimensionGeometry.Turn(ray1, ray2);
        if (AngularDimensionGeometry.Length(vertex, placement) < .001 || !double.IsFinite(turn) || Math.Abs(Math.Sin(turn)) < AngularDimensionGeometry.MinimumAngle)
            throw new DomainException(error, "Select two non-collinear projected rays and place the dimension away from their vertex.");
        if (AngularDimensionGeometry.InSector(vertex, ray1, placement, turn)) return new(i1, i2, false, turn);
        double reverse = 2 * Math.PI - turn;
        if (AngularDimensionGeometry.InSector(vertex, ray2, placement, reverse)) return new(i1, i2, true, reverse);
        throw new DomainException(error, "Place dimline_point strictly inside the desired angular sector.");
    }
}

public static partial class CreateValidation
{
    private static EntitySpec ParseDimension(JsonElement item, string path, string layer)
    {
        string kind = Text(item, "kind", path, 24);
        if (kind is "projected_aligned" or "projected_diametric" or "projected_radial" or "projected_arc_length" or "projected_angular")
        {
            if (kind == "projected_aligned" && item.TryGetProperty("circle_center_markers", out var circles))
            {
                Shape(item, path, "type", "kind", "layer", "view_handle", "circle_center_markers", "dimline_point");
                if (circles.ValueKind != JsonValueKind.Array || circles.GetArrayLength() != 2 ||
                    circles[0].ValueKind != JsonValueKind.Number || !circles[0].TryGetInt32(out var firstMarker) || firstMarker < 0 ||
                    circles[1].ValueKind != JsonValueKind.Number || !circles[1].TryGetInt32(out var secondMarker) || secondMarker < 0 || firstMarker == secondMarker)
                    throw Invalid(path + ".circle_center_markers", "Select two distinct current full-circle GS markers on this view.");
                return new ProjectedCircleCentersSpec(Validation.NormalizeHandle(Text(item, "view_handle", path, 16)),
                    firstMarker, secondMarker, Point(item, "dimline_point", path), layer);
            }
            if (kind == "projected_angular" && item.TryGetProperty("line_indices", out var pair))
            {
                Shape(item, path, "type", "kind", "layer", "view_handle", "line_indices", "dimline_point");
                if (pair.ValueKind != JsonValueKind.Array || pair.GetArrayLength() != 2 ||
                    pair[0].ValueKind != JsonValueKind.Number || !pair[0].TryGetInt32(out int a) || a < 0 ||
                    pair[1].ValueKind != JsonValueKind.Number || !pair[1].TryGetInt32(out int b) || b < 0 || a == b)
                    throw Invalid(path + ".line_indices", "Select two distinct nonnegative current view line indices.");
                return new ProjectedCornerDimensionSpec(Validation.NormalizeHandle(Text(item, "view_handle", path, 16)), a, b, Point(item, "dimline_point", path), layer);
            }
            string indexField = kind == "projected_aligned" ? "line_index" : "arc_index";
            Shape(item, path, "type", "kind", "layer", "view_handle", indexField, "dimline_point");
            string view = Validation.NormalizeHandle(Text(item, "view_handle", path, 16));
            var geometryIndex = Required(item, indexField, path);
            if (geometryIndex.ValueKind != JsonValueKind.Number || !geometryIndex.TryGetInt32(out int index) || index < 0)
                throw Invalid(path + "." + indexField, "Use the nonnegative current projection geometry index.");
            var placement = Point(item, "dimline_point", path);
            return kind switch
            {
                "projected_aligned" => new ProjectedAlignedDimensionSpec(view, index, placement, layer),
                "projected_diametric" => new ProjectedDiametricDimensionSpec(view, index, placement, layer),
                "projected_radial" => new ProjectedRadialDimensionSpec(view, index, placement, layer),
                "projected_arc_length" => new ProjectedArcLengthDimensionSpec(view, index, placement, layer),
                _ => new ProjectedAngularDimensionSpec(view, index, placement, layer)
            };
        }
        if (DimensionCreateSnaps.Supports(kind) && DimensionCreateSnaps.HasSnap(item))
            return ParseSnappedDimension(item, kind, path, layer);
        string style = Text(item, "dimension_style", path, 255);
        string text = Text(item, "text_override", path, 4096, "<>");
        if (kind is "angular_3point" or "angular_2line" or "ordinate") return ParseAngularOrdinate(item, kind, path, style, text, layer);
        if (kind is "arc_length" or "jogged_radial") return ParseExtendedDimension356(item, kind, path, style, text, layer);
        Point2 first, second; Point2? line = null; double rotation = 0, leader = 0;
        if (kind is "aligned" or "rotated")
        {
            Shape(item, path, kind == "aligned" ? new[] { "type", "kind", "layer", "dimension_style", "text_override", "xline1", "xline2", "dimline_point", "associations" } :
                new[] { "type", "kind", "layer", "dimension_style", "text_override", "xline1", "xline2", "dimline_point", "rotation", "associations" });
            first = Point(item, "xline1", path); second = Point(item, "xline2", path); line = Point(item, "dimline_point", path);
            if (kind == "rotated") rotation = Number(Required(item, "rotation", path), path + ".rotation", 0, Math.PI * 2);
            if (rotation >= Math.PI * 2) throw Invalid(path, "Rotation must be less than 2*pi.", "INVALID_GEOMETRY");
            if (kind == "rotated" && Math.Abs((second.X - first.X) * Math.Cos(rotation) + (second.Y - first.Y) * Math.Sin(rotation)) < Minimum)
                throw Invalid(path, "Rotated dimension has zero projected length.", "INVALID_GEOMETRY");
        }
        else if (kind is "radial" or "diametric")
        {
            Shape(item, path, kind == "radial" ? new[] { "type", "kind", "layer", "dimension_style", "text_override", "center", "chord_point", "leader_length", "associations" } :
                new[] { "type", "kind", "layer", "dimension_style", "text_override", "chord_point", "far_chord_point", "leader_length", "associations" });
            first = Point(item, kind == "radial" ? "center" : "chord_point", path);
            second = Point(item, kind == "radial" ? "chord_point" : "far_chord_point", path);
            if (item.TryGetProperty("leader_length", out var length)) leader = Number(length, path + ".leader_length", 0);
        }
        else throw Invalid(path, "Dimension kind must be aligned, rotated, radial, diametric, angular_3point, angular_2line or ordinate.");
        if (Coincident(first, second)) throw Invalid(path, "Dimension defining points must be distinct.", "INVALID_GEOMETRY");
        return new DimensionSpec(kind, first, second, line, rotation, leader, style, text, layer,
            item.TryGetProperty("associations", out var associations) ? DimensionAssociationValidation385.ParseBindings(associations, path + ".associations") : null);
    }
}
