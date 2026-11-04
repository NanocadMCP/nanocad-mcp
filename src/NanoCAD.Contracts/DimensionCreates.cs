using System.Text.Json;

namespace NanoCAD.Contracts;

public interface IDimensionCreation { string DimensionStyle { get; } string TextOverride { get; } }
public sealed record DimensionSpec(string Kind, Point2 First, Point2 Second, Point2? DimLinePoint, double Rotation,
    double LeaderLength, string DimensionStyle, string TextOverride, string LayerName, DimensionSourcePoint385[]? Associations=null) : EntitySpec("Dimension", LayerName),IDimensionCreation;
public sealed record ProjectedAlignedDimensionSpec(string ViewHandle, int LineIndex, Point2 DimLinePoint, string LayerName)
    : EntitySpec("Dimension", LayerName);

public static partial class CreateValidation
{
    private static EntitySpec ParseDimension(JsonElement item, string path, string layer)
    {
        string kind = Text(item, "kind", path, 24);
        if (kind == "projected_aligned")
        {
            Shape(item, path, "type", "kind", "layer", "view_handle", "line_index", "dimline_point");
            string view = Validation.NormalizeHandle(Text(item, "view_handle", path, 16));
            var lineIndex = Required(item, "line_index", path);
            if (lineIndex.ValueKind != JsonValueKind.Number || !lineIndex.TryGetInt32(out int index) || index < 0)
                throw Invalid(path + ".line_index", "Use the nonnegative current projection geometry index.");
            return new ProjectedAlignedDimensionSpec(view, index, Point(item, "dimline_point", path), layer);
        }
        string style = Text(item, "dimension_style", path, 255);
        string text = Text(item, "text_override", path, 4096, "<>");
        if(kind is "angular_3point" or "angular_2line" or "ordinate")return ParseAngularOrdinate(item,kind,path,style,text,layer);
        if(kind is "arc_length" or "jogged_radial")return ParseExtendedDimension356(item,kind,path,style,text,layer);
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
            item.TryGetProperty("associations",out var associations)?DimensionAssociationValidation385.ParseBindings(associations,path+".associations"):null);
    }
}
