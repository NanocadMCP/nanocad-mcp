using System.Text.Json;
using static NanoCAD.Contracts.StructureWriteValidation;

namespace NanoCAD.Contracts;

public sealed record BlockAttributeSpec(string Tag, string DefaultText, Point2 Position, double Height, string TextStyle, double Rotation);
public sealed record DefineBlockEdit(string Name, Point2 BasePoint, EntitySpec[] Entities, BlockAttributeSpec[] Attributes) : StructureEdit("define_block")
{
    // A second polymorphic level must retain each primitive's geometry in the
    // fingerprint; casting only the outer StructureEdit loses these fields.
    public override object CanonicalValue() => new
    { Operation, Name, BasePoint, Entities = Entities.Cast<object>().ToArray(), Attributes };
}
public sealed record InsertBlockEdit(string DefinitionHandle, Point2 Position, string Layer, double Rotation, double Scale,
    SortedDictionary<string, string> AttributeValues) : StructureEdit("insert_block");

// Pure validation shared by the MCP boundary and the native structural writer.
public static class BlockWriteValidation
{
    public static bool OrdinaryName(string name) => name.Length is > 0 and <= 255 && name == name.Trim() &&
        !name.Any(char.IsControl) && !name.Any(c => "<>/\\\":;?*|=,`".Contains(c));
    public static bool Tag(string text) => text.Length is > 0 and <= 64 && (text[0] is >= 'A' and <= 'Z' || text[0] == '_') &&
        text.All(c => c is >= 'A' and <= 'Z' or >= '0' and <= '9' || c == '_');
    private static double Number(JsonElement value, string path, double minimum = -1e9, double maximum = 1e9)
    {
        if (value.ValueKind != JsonValueKind.Number || !value.TryGetDouble(out var number) || !double.IsFinite(number) || number < minimum || number > maximum)
            throw Invalid(path, "Expected a finite number in the supported range.", "INVALID_GEOMETRY");
        return number == 0 ? 0 : number;
    }
    private static Point2 Point(JsonElement value, string name, string path)
    {
        var input = Required(value, name, path);
        if (input.ValueKind != JsonValueKind.Array || input.GetArrayLength() != 2) throw Invalid(path + "." + name, "Expected an XY pair at Z=0.", "INVALID_GEOMETRY");
        return new(Number(input[0], path + "." + name + "[0]"), Number(input[1], path + "." + name + "[1]"));
    }
    public static double NormalizeRotation(double value)
    {
        if (!double.IsFinite(value) || Math.Abs(value) > 1e6) throw Invalid("rotation", "Expected finite radians within ±1e6.", "INVALID_GEOMETRY");
        double result = value % (2 * Math.PI);
        if (result < 0) result += 2 * Math.PI;
        return result == 0 || result >= 2 * Math.PI ? 0 : result;
    }
    private static double Rotation(JsonElement value, string path) => value.TryGetProperty("rotation", out var input)
        ? NormalizeRotation(Number(input, path + ".rotation", -1e6, 1e6)) : 0;
    private static string Style(JsonElement value, string path) => value.TryGetProperty("text_style", out _)
        ? Text(value, "text_style", path, 255) : "Standard";
    public static DefineBlockEdit ParseDefine(JsonElement value, string path = "$")
    {
        Shape(value, path, "operation", "name", "base_point", "entities", "attributes");
        if (Text(value, "operation", path, 32) != "define_block") throw Invalid(path + ".operation", "Expected define_block.");
        string name = Text(value, "name", path, 255);
        if (!OrdinaryName(name)) throw Invalid(path + ".name", "Expected an ordinary non-anonymous, unqualified block name.");
        var origin = Point(value, "base_point", path);
        var entities = CreateValidation.ParseEntities(Required(value, "entities", path), path + ".entities", allowCurves:true,allowBlockMembers:true);
        foreach (var entity in entities)
        {
            if(entity is not (LineSpec or CircleSpec or ArcSpec or EllipseSpec or SplineSpec or PolylineSpec or TextSpec or MTextSpec or HatchSpec or BlockReferenceSpec391))
                throw Invalid(path+".entities","Expected ordinary curve/Text/MText, independent inline Hatch or an existing static BlockReference member.","UNSUPPORTED_BLOCK_CONTENT");
            if (entity.Layer != "0") throw Invalid(path + ".entities", "Definition members use layer 0 only.", "UNSUPPORTED_BLOCK_CONTENT");
            if (entity is TextSpec text && !IsPlainAttribute(text.Text)) throw Invalid(path + ".entities", "Definition Text must be plain without fields or %% codes.");
        }
        var attributes = new List<BlockAttributeSpec>(); var tags = new HashSet<string>(StringComparer.Ordinal);
        if (value.TryGetProperty("attributes", out var array))
        {
            if (array.ValueKind != JsonValueKind.Array) throw Invalid(path + ".attributes", "Expected a variable-attribute array.");
            foreach (var input in array.EnumerateArray())
            {
                string current = path + ".attributes[" + attributes.Count + "]";
                Shape(input, current, "tag", "default_text", "position", "height", "text_style", "rotation");
                string tag = Text(input, "tag", current, 64), text = Text(input, "default_text", current, 4096, true);
                if (!Tag(tag) || !tags.Add(tag)) throw Invalid(current + ".tag", "Expected a unique uppercase ASCII attribute tag.");
                if (!IsPlainAttribute(text)) throw Invalid(current + ".default_text", "Plain single-line text only; no fields or %% codes.");
                attributes.Add(new(tag, text, Point(input, "position", current), Number(Required(input, "height", current), current + ".height", 1e-9), Style(input, current), Rotation(input, current)));
            }
        }
        if (entities.Any(entity=>entity is SplineSpec { Closed: true }) && attributes.Count!=0)
            throw Invalid(path + ".attributes", "Closed spline block members require an attribute-free definition.", "UNSUPPORTED_BLOCK_CONTENT");
        var result = new DefineBlockEdit(name, origin, entities, attributes.ToArray());
        ValidateBatch(new StructureEdit[] { result });
        return result;
    }
    public static InsertBlockEdit ParseInsert(JsonElement value, string path = "$")
    {
        Shape(value, path, "operation", "definition_handle", "position", "layer", "rotation", "scale", "attribute_values");
        if (Text(value, "operation", path, 32) != "insert_block") throw Invalid(path + ".operation", "Expected insert_block.");
        var handle = Validation.NormalizeHandle(Text(value, "definition_handle", path, 16));
        var position = Point(value, "position", path); string layer = Text(value, "layer", path, 255);
        if (!OrdinaryName(layer)) throw Invalid(path + ".layer", "Expected an ordinary existing layer name.");
        double scale = value.TryGetProperty("scale", out var size) ? Number(size, path + ".scale", 1e-6, 1e6) : 1;
        var values = new SortedDictionary<string, string>(StringComparer.Ordinal);
        if (value.TryGetProperty("attribute_values", out var map))
        {
            if (map.ValueKind != JsonValueKind.Object) throw Invalid(path + ".attribute_values", "Expected an attribute tag object.");
            foreach (var property in map.EnumerateObject())
            {
                if (!Tag(property.Name) || values.ContainsKey(property.Name)) throw Invalid(path + ".attribute_values", "Invalid or duplicate attribute tag.");
                string text = Text(map, property.Name, path + ".attribute_values", 4096, true);
                if (!IsPlainAttribute(text)) throw Invalid(path + ".attribute_values." + property.Name, "Plain single-line text only.");
                values.Add(property.Name, text);
            }
        }
        return new(handle, position, layer, Rotation(value, path), scale, values);
    }
    public static int ValidateBatch(IEnumerable<StructureEdit> operations)
    {
        int objects = 0;
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var operation in operations)
        {
            if (operation is DefineBlockEdit definition)
            {
                if (!names.Add(definition.Name)) throw Invalid("operations", "Duplicate new block name ignoring case.", "INVALID_OPERATION_ORDER");
                objects += 1 + definition.Entities.Length + definition.Attributes.Length;
            }
            else if (operation is InsertBlockEdit)
            {
                objects++; // Native preflight adds each existing definition's attributes.
            }
            else if (operation is CreateLayerEdit) objects++;
        }
        return objects;
    }
    public static void CheckObjectBudget(int count)
    {
        if (count < 0) throw Invalid("operations", "Invalid native object count.");
    }
    public static string[] ResolveAttributeValues(IReadOnlyList<BlockAttributeSpec> templates, IReadOnlyDictionary<string, string> values)
    {
        if (templates.Any(t => !Tag(t.Tag)) || templates.Select(t => t.Tag).Distinct(StringComparer.Ordinal).Count() != templates.Count)
            throw new DomainException("UNSUPPORTED_BLOCK_CONTENT", "Definition has unsupported or duplicate attribute tags.");
        var known = templates.Select(t => t.Tag).ToHashSet(StringComparer.Ordinal);
        if (values.Keys.Any(tag => !known.Contains(tag))) throw new DomainException("ATTRIBUTE_TAG_MISMATCH", "Input contains an attribute tag not present in the definition.");
        var result = templates.Select(t => values.TryGetValue(t.Tag, out var text) ? text : t.DefaultText).ToArray();
        if (result.Any(text => !IsPlainAttribute(text))) throw new DomainException("UNSUPPORTED_BLOCK_CONTENT", "An attribute default or replacement is not bounded plain text.");
        return result;
    }
}

public sealed record BlockPlacement(Point2 BasePoint, Point2 Position, double Rotation, double Scale)
{
    private static void Coordinate(double value)
    {
        if (!double.IsFinite(value) || Math.Abs(value) > 1e9) throw new DomainException("INVALID_GEOMETRY", "Block placement exceeds finite coordinate bounds.");
    }
    private void Check()
    {
        Coordinate(BasePoint.X); Coordinate(BasePoint.Y); Coordinate(Position.X); Coordinate(Position.Y);
        if (!double.IsFinite(Rotation) || Math.Abs(Rotation) > 1e6 || !double.IsFinite(Scale) || Scale is < 1e-6 or > 1e6)
            throw new DomainException("INVALID_GEOMETRY", "Invalid uniform scale or rotation.");
    }
    public Point2 Transform(Point2 point)
    {
        Check(); Coordinate(point.X); Coordinate(point.Y);
        double x = (point.X - BasePoint.X) * Scale, y = (point.Y - BasePoint.Y) * Scale;
        var result = new Point2(Position.X + Math.Cos(Rotation) * x - Math.Sin(Rotation) * y,
            Position.Y + Math.Sin(Rotation) * x + Math.Cos(Rotation) * y);
        Coordinate(result.X); Coordinate(result.Y); return result;
    }
    public double Size(double value)
    {
        Check(); double transformed = value * Scale;
        if (!double.IsFinite(value) || value < 1e-9 || value > 1e9 || !double.IsFinite(transformed) || transformed < 1e-9 || transformed > 1e9)
            throw new DomainException("INVALID_GEOMETRY", "Block placement exceeds finite size bounds.");
        return transformed;
    }
    public double[] Matrix()
    {
        Check(); double c = Math.Cos(Rotation) * Scale, s = Math.Sin(Rotation) * Scale;
        return new[] { c, -s, 0, Position.X - c * BasePoint.X + s * BasePoint.Y,
            s, c, 0, Position.Y - s * BasePoint.X - c * BasePoint.Y,
            0, 0, Scale, 0, 0, 0, 0, 1 };
    }
}
