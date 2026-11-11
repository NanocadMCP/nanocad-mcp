using System.Text.Json;
using System.Text.Json.Serialization;

namespace NanoCAD.Contracts;

public sealed record StructureRecord(EntityRef @ref, string kind, string name, object properties);
public sealed record StructurePage(string section, StructureRecord[] items, int total_count, int returned_count, bool complete, string? next_cursor);
public sealed record BlockInfo(EntityRef @ref, string name, bool is_layout, bool is_anonymous, bool is_dynamic,
    bool is_xref, bool is_overlay, bool is_dependent, double[] origin, string units, string? external_path);
public sealed record AttributeInfo(EntityRef @ref, string tag, string text, double[] position, double height, double rotation,
    bool invisible, bool constant, string coordinate_system, double[] transform_to_wcs, LayerDto layer, object properties, AttributeTextFormat text_format, object? multiline = null);
public sealed record AttributeTextFormat(EntityRef text_style_ref, string text_style_name, double width_factor, double oblique,
    string horizontal_mode, string vertical_mode, double[] alignment_point);
public sealed record InsertInfo(BlockInfo definition, double[] position, double[] normal, double[] scale_factors, double rotation,
    double[] transform_to_parent, double[] transform_to_wcs, AttributeInfo[] attributes, bool is_dynamic, string expansion, object properties);
public sealed record BlockNode(string[] occurrence_path, EntitySummary summary, double[] parent_to_wcs,
    EntityDetail? entity, InsertInfo? insert, AttributeInfo? attribute_definition);
public sealed record BlockInspection(EntityRef root, BlockNode[] nodes, bool complete, string[] warnings,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] object? definition_editing = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] object? definition_text_editing = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] object? definition_membership = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] object? nested_attribute_editing = null)
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public DynamicBlockPropertiesInventory? dynamic_properties { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? dynamic_properties_unavailable_reason { get; init; }
}

public static class StructureValidation
{
    public static readonly string[] Sections = { "layers", "linetypes", "text_styles", "dimension_styles", "table_styles", "mleader_styles", "blocks", "groups", "layouts", "viewports", "viewport_clipping" };
    public static Arguments Parse(string operation, JsonElement input)
    {
        if (input.ValueKind != JsonValueKind.Object) throw new DomainException("INVALID_ARGUMENT", "Expected object.");
        var allowed = new HashSet<string>(new[] { "bridge_instance_id", "document_id", "expected_revision" }, StringComparer.Ordinal);
        allowed.UnionWith(operation == "get_structure" ? new[] { "section", "page_size", "cursor" } : new[] { "handle", "max_depth", "max_nodes" });
        var seen = new HashSet<string>(); var common = new Dictionary<string, JsonElement>();
        foreach (var property in input.EnumerateObject())
        {
            if (!seen.Add(property.Name) || !allowed.Contains(property.Name)) throw new DomainException("INVALID_ARGUMENT", "Unknown or duplicate structural argument: " + property.Name);
            if (property.Name is "bridge_instance_id" or "document_id" or "expected_revision" or "page_size" or "cursor") common.Add(property.Name, property.Value.Clone());
        }
        // Reuse strict document/revision/cursor validation. No CAD object enters this parser.
        var args = Validation.Parse("query_objects", Wire.Element(common));
        string Text(string name, int limit)
        {
            if (!input.TryGetProperty(name, out var item) || item.ValueKind != JsonValueKind.String || item.GetString() is not { Length: > 0 } text || text.Length > limit)
                throw new DomainException("INVALID_ARGUMENT", name + " is required and must be a bounded string.");
            return text;
        }
        int Integer(string name, int value, int min, int max)
        {
            if (input.TryGetProperty(name, out var item) && (item.ValueKind != JsonValueKind.Number || !item.TryGetInt32(out value) || value < min || value > max))
                throw new DomainException("INVALID_ARGUMENT", name + " is outside the allowed integer range.");
            return value;
        }
        if (operation == "get_structure")
        {
            string section = Text("section", 32);
            if (!Sections.Contains(section)) throw new DomainException("INVALID_ARGUMENT", "Unknown structure section.");
            return args with { Section = section };
        }
        return args with { BlockHandle = Validation.NormalizeHandle(Text("handle", 16)), MaxDepth = Integer("max_depth", 4, 0, int.MaxValue), MaxNodes = Integer("max_nodes", int.MaxValue, 1, int.MaxValue) };
    }
}
