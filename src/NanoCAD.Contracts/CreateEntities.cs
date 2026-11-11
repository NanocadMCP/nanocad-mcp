using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;

namespace NanoCAD.Contracts;

public sealed record Point2(double X, double Y);
public sealed record Vertex2(Point2 Point, double Bulge);
public abstract record EntitySpec(string Type, string Layer)
{
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public SketchPlaneSpec? Workplane { get; init; }
}
public sealed record LineSpec(Point2 Start, Point2 End, string LayerName) : EntitySpec("Line", LayerName);
public sealed record CircleSpec(Point2 Center, double Radius, string LayerName) : EntitySpec("Circle", LayerName);
public sealed record ArcSpec(Point2 Center, double Radius, double StartAngle, double SweepAngle, string LayerName) : EntitySpec("Arc", LayerName);
public sealed record PolylineSpec(Vertex2[] Vertices, bool Closed, string LayerName) : EntitySpec("Polyline", LayerName);
public sealed record TextSpec(string Text, Point2 Position, double Height, string TextStyle, string LayerName) : EntitySpec("Text", LayerName)
{
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)] public TextCreateFormat442? Format442 { get; init; }
}
public sealed record CreateArguments(string BridgeId, string DocumentId, string ExpectedRevision, string OperationId,
    string Units, EntitySpec[] Entities) : IWriteArguments
{
    public string Operation => "create_entities";
    public string TargetSpace { get; init; } = "model";
    // Cast each element to object so .NET 6 serializes every derived geometry
    // field; serializing EntitySpec[] alone would lose those fields.
    public string Fingerprint() => Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(new
    {
        Operation,
        BridgeId,
        DocumentId,
        ExpectedRevision,
        Units,
        TargetSpace,
        Entities = Entities.Cast<object>().ToArray()
    }, Wire.Json)));
}
public sealed record CreatedEntities(string operation_id, string status, bool replayed, EntityRef[] created, string undo_history,
    [property: System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)] SmoothPolylineFit409[]? polyline_fits = null,
    [property: System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)] ModelSketchCreation410[]? model_sketch_creation = null);
public sealed record WriteErrorDetails(string operation_id, string outcome, bool replayed, string undo_history, object? cause);

public static partial class CreateValidation
{
    public const double Maximum = 1e9;
    public const double Minimum = 1e-9;
    private static DomainException Invalid(string path, string message, string code = "INVALID_ARGUMENT") =>
        new(code, path + ": " + message, false, new { path });
    private static void Shape(JsonElement value, string path, params string[] allowed)
    {
        if (value.ValueKind != JsonValueKind.Object) throw Invalid(path, "Expected object.");
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var property in value.EnumerateObject())
            if (!seen.Add(property.Name) || !allowed.Contains(property.Name, StringComparer.Ordinal))
                throw Invalid(path + "." + property.Name, "Unknown or duplicate field.");
    }
    private static JsonElement Required(JsonElement value, string name, string path) => value.TryGetProperty(name, out var item)
        ? item : throw Invalid(path + "." + name, "Required field is missing.");
    private static string Text(JsonElement value, string name, string path, int max = int.MaxValue, string? fallback = null)
    {
        if (fallback != null && !value.TryGetProperty(name, out _)) return fallback;
        var item = Required(value, name, path);
        if (item.ValueKind != JsonValueKind.String) throw Invalid(path + "." + name, "Expected string.");
        string text;
        try { text = item.GetString()!; }
        catch (InvalidOperationException) { throw Invalid(path + "." + name, "Invalid Unicode string."); }
        if (text.Length == 0 || text.Length > max || text.Any(char.IsControl))
            throw Invalid(path + "." + name, $"Expected 1–{max} characters without control characters.");
        return text;
    }
    private static double Number(JsonElement value, string path, double minimum = -Maximum, double maximum = Maximum)
    {
        if (value.ValueKind != JsonValueKind.Number || !value.TryGetDouble(out var number) || !double.IsFinite(number) || number < minimum || number > maximum)
            throw InputError.Create(path, "Expected a finite number in the supported range.", new { minimum, maximum }, "INVALID_GEOMETRY");
        return number == 0 ? 0 : number;
    }
    private static Point2 Point(JsonElement value, string name, string path)
    {
        var item = Required(value, name, path); path += "." + name;
        if (item.ValueKind != JsonValueKind.Array || item.GetArrayLength() != 2) throw Invalid(path, "Expected XY pair.", "INVALID_GEOMETRY");
        return new(Number(item[0], path + "[0]"), Number(item[1], path + "[1]"));
    }
    private static bool Boolean(JsonElement value, string name, string path, bool fallback)
    {
        if (!value.TryGetProperty(name, out var item)) return fallback;
        if (item.ValueKind is not (JsonValueKind.True or JsonValueKind.False)) throw Invalid(path + "." + name, "Expected boolean.");
        return item.GetBoolean();
    }
    private static bool Coincident(Point2 a, Point2 b) => Math.Abs(a.X - b.X) < Minimum && Math.Abs(a.Y - b.Y) < Minimum;
    public static CreateArguments Parse(JsonElement value)
    {
        Shape(value, "$", "bridge_instance_id", "document_id", "expected_revision", "operation_id", "units", "entities", "target_space");
        if (System.Text.Encoding.UTF8.GetByteCount(value.GetRawText()) > Wire.MaxRequestBytes - 1024)
            throw Invalid("$", "Create request exceeds the IPC budget.");
        string Id(string name)
        {
            var text = Text(value, name, "$", 36);
            if (!Guid.TryParseExact(text, "D", out var id) || id == Guid.Empty) throw Invalid("$." + name, "Expected nonempty UUID.");
            return id.ToString("D");
        }
        var bridge = Id("bridge_instance_id"); var document = Id("document_id"); var operation = Id("operation_id");
        var revision = Text(value, "expected_revision", "$", 20);
        if (revision.Any(c => c is < '0' or > '9') || !ulong.TryParse(revision, NumberStyles.None, CultureInfo.InvariantCulture, out var revisionNumber))
            throw Invalid("$.expected_revision", "Expected decimal uint64 string.");
        var units = Text(value, "units", "$", 32);
        if (units != "Millimeters") throw InputError.Create("$.units", "Expected Millimeters.", new[] { "Millimeters" }, "UNSUPPORTED_UNITS");
        var result = new CreateArguments(bridge, document, revisionNumber.ToString(CultureInfo.InvariantCulture), operation, units,
            ParseEntities(Required(value, "entities", "$"), allowDimensions: true))
        { TargetSpace = WriteSpace.Parse(value) };
        WriteSpace.Check(result); return result;
    }
    // Shared semantic geometry parser; callers still validate their own root,
    // identity, request budget and coordinate-system meaning.
    public static EntitySpec[] ParseEntities(JsonElement entities, string collectionPath = "$.entities", bool allowDimensions = false, bool allowCurves = false, bool allowBlockMembers = false)
    {
        if (entities.ValueKind != JsonValueKind.Array || entities.GetArrayLength() < 1)
            throw Invalid(collectionPath, "Expected a nonempty entity list.");
        try { SerializedCapacity357.Check(entities, Wire.MaxRequestBytes - 1024); }
        catch (JsonException) { throw Invalid(collectionPath, "Malformed Unicode in entity JSON."); }
        int index = 0;
        var specs = new List<EntitySpec>();
        foreach (var original in entities.EnumerateArray())
        {
            var item = original;
            string path = $"{collectionPath}[{index++}]";
            if (item.ValueKind != JsonValueKind.Object) throw Invalid(path, "Expected object.");
            string type = Text(item, "type", path), layer = Text(item, "layer", path, 255, "0");
            SketchPlaneSpec? workplane = null;
            if (item.TryGetProperty("workplane", out var wp))
            {
                if (type == "Point" && item.TryGetProperty("position_snap", out _))
                    throw Invalid(path + ".workplane", "position_snap resolves directly in WCS; omit workplane.");
                if (type == "Line" && (item.TryGetProperty("start_snap", out _) || item.TryGetProperty("end_snap", out _)))
                    throw Invalid(path + ".workplane", "Snapped Line endpoints resolve directly in WCS. Omit workplane.");
                if (item.TryGetProperty("construction", out var sourceConstruction) && sourceConstruction.ValueKind == JsonValueKind.String && sourceConstruction.GetString() is "native_curve" or "fit_points_3d") throw Invalid(path, "This construction uses actual WCS geometry; workplane is not accepted.");
                if (!allowDimensions || type is not ("Line" or "Circle" or "Arc" or "Ellipse" or "Spline" or "Polyline" or "Point" or "Wipeout")) throw Invalid(path, "workplane is supported only for top-level Line/Circle/Arc/Ellipse/Spline/Polyline/Point/Wipeout creation.");
                workplane = DrawingWorkplane.Parse(wp); item = DrawingWorkplane.WithoutFrame(item);
            }
            double Size(string name) => Number(Required(item, name, path), path + "." + name, Minimum);
            switch (type)
            {
                case "MLeader" when allowDimensions && item.TryGetProperty("content_type", out var leaderContent442) && leaderContent442.ValueKind == JsonValueKind.String && leaderContent442.GetString() == "block": specs.Add(MLeaderInstanceValidation442.ParseCreate(item, path, layer)); break;
                case "Wipeout" when allowDimensions: specs.Add(WipeoutValidation431.ParseCreate(item, path, layer)); break;
                case "Point" when allowDimensions: specs.Add(ParsePoint408(item, path, layer)); break;
                case "Ray" or "Xline" when allowDimensions: specs.Add(ConstructionCurves443.Parse(item, path, layer)); break;
                case "Polyline3d" when allowDimensions: specs.Add(ParsePolyline3d441(item, path, layer)); break;
                case "Spline" when allowDimensions || allowCurves:
                    EntitySpec spline = item.TryGetProperty("construction", out var construction) && allowDimensions
                        ? construction.ValueKind == JsonValueKind.String ? construction.GetString() switch
                        {
                            "native_curve" => ParseCurveSpline408(item, path, layer),
                            "fit_points_3d" => ParseSpatialFitSpline441(item, path, layer),
                            "closed_fit_points" => ParsePeriodicFitSpline441(item, path, layer),
                            _ => ParseFitSpline408(item, path, layer)
                        } : ParseFitSpline408(item, path, layer)
                        : ParseSpline(item, path, layer);
                    specs.Add(spline); break;
                case "Ellipse" when allowDimensions || allowCurves:
                    specs.Add(ParseEllipse(item, path, layer)); break;
                case "MText" when allowDimensions || allowBlockMembers:
                    var mtext = ParseMText(item, path, layer);
                    specs.Add(mtext); break;
                case "Solid3d" when allowDimensions:

                    specs.Add(ParseSolid(item, path, layer)); break;
                case "BlockReference" when allowBlockMembers:
                    specs.Add(BlockTemplateValidation391.ParseReference(item, path, layer)); break;
                case "Hatch" when allowBlockMembers:
                    specs.Add(ParseAnnotation(item, type, path, layer)); break;
                case "MLeader" or "Table" or "Hatch" when allowDimensions:
                    specs.Add(ParseAnnotation(item, type, path, layer)); break;
                case "Tolerance" when allowDimensions: specs.Add(ParseTolerance356(item, path, layer)); break;
                case "Dimension" when allowDimensions:
                    specs.Add(ParseDimension(item, path, layer)); break;
                case "Region": specs.Add(ParseRegion(item, path, layer)); break;
                case "Helix": specs.Add(ParseHelix(item, path, layer)); break;
                case "Line" when allowDimensions && (item.TryGetProperty("start_snap", out _) || item.TryGetProperty("end_snap", out _)):
                    specs.Add(ParseSnappedLine(item, path, layer)); break;
                case "Line":
                    Shape(item, path, "type", "layer", "start", "end");
                    var start = Point(item, "start", path); var end = Point(item, "end", path);
                    if (Coincident(start, end)) throw Invalid(path, "Line endpoints must differ.", "INVALID_GEOMETRY");
                    specs.Add(new LineSpec(start, end, layer)); break;
                case "Circle":
                    specs.Add(ParsePlanarCircle(item, path, layer, allowDimensions)); break;
                case "Arc":
                    specs.Add(ParsePlanarArc(item, path, layer, allowDimensions)); break;
                case "Polyline":
                    if (item.TryGetProperty("construction", out _))
                    {
                        if (!allowDimensions) throw Invalid(path, "Sampled smooth construction is a top-level Polyline branch; inline block members use explicit vertices.");
                        specs.Add(ParseSmoothPolyline409(item, path, layer)); break;
                    }
                    Shape(item, path, "type", "layer", "vertices", "closed");
                    bool closed = Boolean(item, "closed", path, false);
                    var array = Required(item, "vertices", path);
                    if (array.ValueKind != JsonValueKind.Array || array.GetArrayLength() < (closed ? 3 : 2))
                        throw Invalid(path + ".vertices", "Expected at least two vertices, at least three for a closed polyline.", "INVALID_GEOMETRY");
                    var points = new List<Vertex2>();
                    foreach (var vertex in array.EnumerateArray())
                    {
                        string vertexPath = path + $".vertices[{points.Count}]"; Shape(vertex, vertexPath, "point", "bulge");
                        var point = Point(vertex, "point", vertexPath);
                        double bulge = vertex.TryGetProperty("bulge", out var b) ? Number(b, vertexPath + ".bulge", -1000, 1000) : 0;
                        if (points.Count > 0 && Coincident(points[^1].Point, point)) throw Invalid(vertexPath, "Zero-length segment.", "INVALID_GEOMETRY");
                        points.Add(new(point, bulge));
                    }
                    if (closed && Coincident(points[0].Point, points[^1].Point)) throw Invalid(path, "Closed polyline must not repeat its first point.", "INVALID_GEOMETRY");
                    if (!closed && points[^1].Bulge != 0) throw Invalid(path, "Last bulge of an open polyline must be zero.", "INVALID_GEOMETRY");
                    // Reject a closed shape consisting only of collinear straight segments.
                    if (closed && points.All(p => p.Bulge == 0))
                    {
                        var origin = points[0].Point; var direction = points[1].Point;
                        if (points.Skip(2).All(p => Math.Abs((direction.X - origin.X) * (p.Point.Y - origin.Y) -
                            (direction.Y - origin.Y) * (p.Point.X - origin.X)) < Minimum * Minimum))
                            throw Invalid(path, "Closed polyline is collinear.", "INVALID_GEOMETRY");
                    }
                    specs.Add(new PolylineSpec(points.ToArray(), closed, layer)); break;
                case "Text":
                    Shape(item, path, "type", "layer", "text", "position", "height", "text_style", "format");
                    var text = Text(item, "text", path, 4096);
                    if (text.Contains("%<", StringComparison.Ordinal)) throw Invalid(path + ".text", "Field expressions are outside plain Text creation.");
                    specs.Add(new TextSpec(text, Point(item, "position", path), Size("height"), Text(item, "text_style", path, 255, "Standard"), layer) { Format442 = item.TryGetProperty("format", out var textFormat442) ? TextFormatValidation442.Create(textFormat442, path + ".format") : null }); break;
                default: throw Invalid(path + ".type", "Unsupported entity type for this creation scope.");
            }
            if (workplane != null) specs[^1] = specs[^1] with { Workplane = workplane };
        }
        if (specs.Any(spec => spec is ModelExtrusionSpec or ModelLoftSpec or ModelSweepSpec) && specs.Count != 1)
            throw Invalid(collectionPath, "A native model extrusion must be the only entity in its request; do not mix native history creation and detached geometry.");
        return specs.ToArray();
    }
}
