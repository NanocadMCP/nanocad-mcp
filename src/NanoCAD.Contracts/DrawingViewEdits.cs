using System.Text.Json;
using static NanoCAD.Contracts.StructureWriteValidation;

namespace NanoCAD.Contracts;

public sealed record DrawingViewTarget(string LayoutHandle, string Handle, string ExpectedName);
public sealed record SetDrawingViewPropertiesEdit(DrawingViewTarget Target, Point2? Origin, double? ModelScale,
    bool? ShowHiddenLines, bool? ShowHatches, bool? ShowTangentEdges) : StructureEdit("set_drawing_view_properties")
{
    public bool? PreviewMode { get; init; }
}
public sealed record DrawingViewSectionRetirementTarget(string SectionId, string SectionToken);
public sealed record RefreshDrawingViewsEdit(DrawingViewTarget[] Targets) : StructureEdit("refresh_drawing_views");
public sealed record DeleteDrawingViewsEdit(DrawingViewTarget[] Targets) : StructureEdit("delete_drawing_views")
{
    public DrawingViewSectionRetirementTarget[] Sections { get; init; } = [];
}

public static class DrawingViewEditValidation
{
    public static bool Handles(string operation) => operation is "set_drawing_view_properties" or "refresh_drawing_views" or "delete_drawing_views";
    private static string Handle(JsonElement input, string key, string path)
    {
        string value = Text(input, key, path, 16);
        try { return Validation.NormalizeHandle(value); }
        catch (DomainException error) when (error.Error.code == "INVALID_HANDLE")
        { throw Invalid(path + "." + key, error.Error.message, "INVALID_HANDLE"); }
    }
    private static DrawingViewTarget Target(JsonElement input, string path) => new(
        Handle(input, "layout_handle", path), Handle(input, "handle", path), Text(input, "expected_name", path, 4096));
    private static double Number(JsonElement value, string path, bool positive = false)
    {
        if (value.ValueKind != JsonValueKind.Number || !value.TryGetDouble(out var number) || !double.IsFinite(number) || positive && number <= 0)
            throw Invalid(path, positive ? "Expected a finite positive native model-scale denominator." : "Expected a finite coordinate in millimeters.");
        return number == 0 ? 0 : number;
    }
    private static bool? Boolean(JsonElement input, string key, string path)
    {
        if (!input.TryGetProperty(key, out var value)) return null;
        if (value.ValueKind is not (JsonValueKind.True or JsonValueKind.False)) throw Invalid(path + "." + key, "Expected a boolean.");
        return value.GetBoolean();
    }
    public static StructureEdit Parse(JsonElement input, string path)
    {
        string operation = Text(input, "operation", path, 32);
        if (operation == "refresh_drawing_views")
        {
            Shape(input, path, "operation", "targets"); var array = Required(input, "targets", path);
            if (array.ValueKind != JsonValueKind.Array || array.GetArrayLength() == 0) throw Invalid(path + ".targets", "Select a nonempty explicit set of current native drawing views.");
            var targets = new List<DrawingViewTarget>();
            foreach (var item in array.EnumerateArray())
            {
                string itemPath = path + ".targets[" + targets.Count + "]"; Shape(item, itemPath, "layout_handle", "handle", "expected_name");
                targets.Add(Target(item, itemPath));
            }
            if (targets.Select(item => item.Handle).Distinct(StringComparer.Ordinal).Count() != targets.Count)
                throw Invalid(path + ".targets", "Each drawing view handle must be selected once.");
            return new RefreshDrawingViewsEdit(targets.OrderBy(item => item.Handle, StringComparer.Ordinal).ToArray());
        }
        if (operation == "delete_drawing_views")
        {
            Shape(input, path, "operation", "targets", "section_targets"); var array = Required(input, "targets", path);
            if (array.ValueKind != JsonValueKind.Array || array.GetArrayLength() == 0) throw Invalid(path + ".targets", "Select a nonempty explicit native view leaf/subtree.");
            var targets = new List<DrawingViewTarget>();
            foreach (var item in array.EnumerateArray())
            {
                string itemPath = path + ".targets[" + targets.Count + "]"; Shape(item, itemPath, "layout_handle", "handle", "expected_name");
                targets.Add(Target(item, itemPath));
            }
            if (targets.Select(item => item.Handle).Distinct(StringComparer.Ordinal).Count() != targets.Count)
                throw Invalid(path + ".targets", "Each drawing view handle must be selected once.");
            var sections = new List<DrawingViewSectionRetirementTarget>(); var seen = new HashSet<Guid>();
            if (input.TryGetProperty("section_targets", out var selectedSections))
            {
                if (selectedSections.ValueKind != JsonValueKind.Array) throw Invalid(path + ".section_targets", "Select an array of exact native Sections, or omit it to retain the Sections.");
                foreach (var section in selectedSections.EnumerateArray())
                {
                    string at = path + ".section_targets[" + sections.Count + "]"; Shape(section, at, "section_id", "section_token");
                    if (!Guid.TryParseExact(Text(section, "section_id", at, 38), "B", out var id) || id == Guid.Empty)
                        throw Invalid(at + ".section_id", "Read the exact nonempty native section_id in braces, not a drawing handle.");
                    if (!seen.Add(id)) throw Invalid(at + ".section_id", "Select each native Section once.");
                    string token = Text(section, "section_token", at, 64);
                    if (token.Length != 64 || token.Any(character => !Uri.IsHexDigit(character))) throw Invalid(at + ".section_token", "Read the current complete section_cut token.");
                    sections.Add(new(id.ToString("B").ToUpperInvariant(), token.ToUpperInvariant()));
                }
            }
            return new DeleteDrawingViewsEdit(targets.OrderBy(item => item.Handle, StringComparer.Ordinal).ToArray())
            { Sections = sections.OrderBy(section => section.SectionId, StringComparer.Ordinal).ToArray() };
        }
        if (operation != "set_drawing_view_properties") throw Invalid(path + ".operation", "Unknown existing native drawing-view operation.");
        Shape(input, path, "operation", "layout_handle", "handle", "expected_name", "origin", "model_scale", "show_hidden_lines", "show_hatches", "show_tangent_edges", "preview_mode");
        Point2? origin = null;
        if (input.TryGetProperty("origin", out var point))
        {
            if (point.ValueKind != JsonValueKind.Array || point.GetArrayLength() != 2) throw Invalid(path + ".origin", "Expected WCS [x,y] in millimeters; the native view plane axes and Z stay unchanged.");
            origin = new(Number(point[0], path + ".origin[0]"), Number(point[1], path + ".origin[1]"));
        }
        double? scale = input.TryGetProperty("model_scale", out var scaleValue) ? Number(scaleValue, path + ".model_scale", true) : null;
        var hidden = Boolean(input, "show_hidden_lines", path); var hatches = Boolean(input, "show_hatches", path); var tangent = Boolean(input, "show_tangent_edges", path);
        var preview = Boolean(input, "preview_mode", path);
        if (origin == null && scale == null && hidden == null && hatches == null && tangent == null && preview == null) throw Invalid(path, "Provide at least one placement, scale or display property.");
        return new SetDrawingViewPropertiesEdit(Target(input, path), origin, scale, hidden, hatches, tangent) { PreviewMode = preview };
    }
    public static void Batch(IReadOnlyList<StructureEdit> edits)
    {
        if (!edits.Any(edit => edit is SetDrawingViewPropertiesEdit or DeleteDrawingViewsEdit or RefreshDrawingViewsEdit)) return;
        if (edits.Count == 1 && edits[0] is DeleteDrawingViewsEdit or RefreshDrawingViewsEdit) return;
        if (edits.Any(edit => edit is not SetDrawingViewPropertiesEdit))
            throw Invalid("$.operations", "Drawing-view property edits form their own native group; refresh or deletion must be the sole operation.", "INVALID_OPERATION_ORDER");
        if (edits.Cast<SetDrawingViewPropertiesEdit>().Select(edit => edit.Target.Handle).Distinct(StringComparer.Ordinal).Count() != edits.Count)
            throw Invalid("$.operations", "Combine requested properties of each native view in one operation.", "INVALID_OPERATION_ORDER");
    }
}
