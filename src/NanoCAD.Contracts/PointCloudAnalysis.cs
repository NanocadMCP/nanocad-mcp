using System.Globalization;
using System.Text.Json;

namespace NanoCAD.Contracts;

public sealed record PointCloudInspectArguments(Arguments Read, string Mode, int PageSize = 100, string? Cursor = null);
public sealed record PointCloudSection(double[] Origin, double[] Normal, double[] XAxis, double Thickness);
public sealed record PointCloudRegion(double[] Min, double[] Max);
public sealed record PointCloudMeasurement(string Kind, ulong? PointIndex = null, ulong? SecondPointIndex = null,
    double[]? PlaneOrigin = null, double[]? PlaneNormal = null, uint? FeatureId = null);
public sealed record PointCloudAnalysisArguments(Arguments Read, string Mode, string Units,
    PointCloudSection? Section = null, PointCloudRegion? Region = null, int PageSize = 100, string? Cursor = null,
    PointCloudMeasurement? Measurement = null, double[]? QueryPoint = null, bool IncludeAttributes = false);

public static class PointCloudInspectValidation
{
    public static PointCloudInspectArguments Parse(JsonElement input)
    {
        PointCloudAnalysisValidation.Shape(input, "request", "bridge_instance_id", "document_id", "expected_revision", "handle", "mode", "page_size", "cursor");
        var mode = input.TryGetProperty("mode", out var value)
            ? PointCloudAnalysisValidation.Choice(value, "mode", "metadata", "features") : "metadata";
        var read = PointCloudAnalysisValidation.Read(input);
        if (mode == "metadata" && (input.TryGetProperty("page_size", out _) || input.TryGetProperty("cursor", out _)))
            throw PointCloudAnalysisValidation.Invalid("Metadata does not accept paging arguments.");
        return new(read, mode, PointCloudAnalysisValidation.PageSize(input), PointCloudAnalysisValidation.Cursor(input));
    }
}

public static class PointCloudAnalysisValidation
{
    public static PointCloudAnalysisArguments Parse(JsonElement input)
    {
        Shape(input, "request", "bridge_instance_id", "document_id", "expected_revision", "handle", "mode", "units",
            "section", "region", "page_size", "cursor", "measurement", "query_point", "include_attributes");
        var read = Read(input);
        var mode = Choice(Required(input, "mode"), "mode", "points", "section", "statistics", "measure");
        var units = Choice(Required(input, "units"), "units", "drawing_units");
        if (mode == "measure")
        {
            foreach (var name in new[] { "section", "region", "page_size", "cursor", "query_point", "include_attributes" })
                if (input.TryGetProperty(name, out _)) throw Invalid("Measurement does not accept " + name + ".");
            return new(read, mode, units, Measurement: Measurement(Required(input, "measurement")));
        }
        if (input.TryGetProperty("measurement", out _)) throw Invalid("measurement is available only in measure mode.");
        var section = input.TryGetProperty("section", out var sectionValue) ? Section(sectionValue) : null;
        if (mode == "section" && section == null) throw Invalid("section mode requires an explicit section frame and thickness.");
        if (mode == "points" && section != null) throw Invalid("Use section mode to select an oriented slab.");
        var region = input.TryGetProperty("region", out var regionValue) ? Region(regionValue) : null;
        var query = input.TryGetProperty("query_point", out var point) ? Vector(point, "query_point") : null;
        if (query != null && mode != "statistics") throw Invalid("query_point is available only in statistics mode.");
        var attributes = false;
        if (input.TryGetProperty("include_attributes", out var include))
        {
            if (mode is not ("points" or "section") || include.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
                throw Invalid("include_attributes is a boolean available only in points or section mode.");
            attributes = include.GetBoolean();
        }
        return new(read, mode, units, section, region, PageSize(input), Cursor(input), QueryPoint: query, IncludeAttributes: attributes);
    }

    internal static Arguments Read(JsonElement input)
    {
        var fields = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        foreach (var name in new[] { "bridge_instance_id", "document_id", "expected_revision", "handle" })
            fields.Add(name, Required(input, name));
        return PointCloudReadValidation.Parse(Wire.Element(fields));
    }

    internal static void Shape(JsonElement input, string path, params string[] allowed)
    {
        if (input.ValueKind != JsonValueKind.Object) throw Invalid(path + " must be an object.");
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var property in input.EnumerateObject())
            if (!seen.Add(property.Name) || !allowed.Contains(property.Name, StringComparer.Ordinal))
                throw Invalid("Unknown or duplicate " + path + " field: " + property.Name + ".");
    }

    internal static string Choice(JsonElement value, string name, params string[] values)
    {
        if (value.ValueKind != JsonValueKind.String || value.GetString() is not { } text || !values.Contains(text, StringComparer.Ordinal))
            throw Invalid(name + " must be " + string.Join(" or ", values) + ".");
        return text;
    }

    internal static int PageSize(JsonElement input)
    {
        if (!input.TryGetProperty("page_size", out var value)) return 100;
        if (value.ValueKind != JsonValueKind.Number || !value.TryGetInt32(out var size) || size < 1)
            throw Invalid("page_size must be a positive Int32 integer.");
        return size;
    }

    internal static string? Cursor(JsonElement input)
    {
        if (!input.TryGetProperty("cursor", out var value)) return null;
        if (value.ValueKind != JsonValueKind.String || value.GetString() is not { Length: > 0 and <= 8192 } text)
            throw Invalid("cursor must be a nonempty opaque string of at most8192 characters.");
        return text;
    }

    static PointCloudSection Section(JsonElement input)
    {
        Shape(input, "section", "origin", "normal", "x_axis", "thickness");
        var origin = Vector(Required(input, "origin"), "section.origin");
        var normal = Unit(Vector(Required(input, "normal"), "section.normal"), "section.normal");
        var xAxis = Unit(Vector(Required(input, "x_axis"), "section.x_axis"), "section.x_axis");
        if (Math.Abs(Dot(normal, xAxis)) > 1e-8) throw Invalid("section.x_axis must be perpendicular to section.normal.");
        // Remove accepted roundoff so the declared section basis is orthonormal.
        var dot = Dot(normal, xAxis);
        xAxis = Unit(Enumerable.Range(0, 3).Select(i => xAxis[i] - dot * normal[i]).ToArray(), "section.x_axis");
        var thickness = Number(Required(input, "thickness"), "section.thickness");
        if (thickness <= 0) throw Invalid("section.thickness must be positive; it is the total slab width.");
        return new(origin, normal, xAxis, thickness);
    }

    static PointCloudRegion Region(JsonElement input)
    {
        Shape(input, "region", "min", "max");
        var min = Vector(Required(input, "min"), "region.min");
        var max = Vector(Required(input, "max"), "region.max");
        if (Enumerable.Range(0, 3).Any(i => min[i] > max[i])) throw Invalid("region.min must not exceed region.max on any WCS axis.");
        return new(min, max);
    }

    static PointCloudMeasurement Measurement(JsonElement input)
    {
        Shape(input, "measurement", "kind", "point_index", "second_point_index", "reference_plane", "feature_id");
        var kind = Choice(Required(input, "kind"), "measurement.kind", "point_distance", "point_plane_distance", "feature_diameter");
        string[] fields = kind switch
        {
            "point_distance" => ["kind", "point_index", "second_point_index"],
            "point_plane_distance" => ["kind", "point_index", "reference_plane"],
            _ => ["kind", "feature_id"]
        };
        Shape(input, "measurement", fields);
        if (kind == "point_distance") return new(kind, Index(Required(input, "point_index"), "point_index"), Index(Required(input, "second_point_index"), "second_point_index"));
        if (kind == "point_plane_distance")
        {
            var plane = Required(input, "reference_plane");
            Shape(plane, "measurement.reference_plane", "origin", "normal");
            return new(kind, Index(Required(input, "point_index"), "point_index"),
                PlaneOrigin: Vector(Required(plane, "origin"), "measurement.reference_plane.origin"),
                PlaneNormal: Unit(Vector(Required(plane, "normal"), "measurement.reference_plane.normal"), "measurement.reference_plane.normal"));
        }
        var feature = Required(input, "feature_id");
        if (feature.ValueKind != JsonValueKind.Number || !feature.TryGetUInt32(out var id) || id == uint.MaxValue)
            throw Invalid("feature_id must be a native UInt32 feature ID other than the null sentinel4294967295.");
        return new(kind, FeatureId: id);
    }

    static ulong Index(JsonElement input, string name)
    {
        if (input.ValueKind != JsonValueKind.String || input.GetString() is not { Length: > 0 and <= 20 } text ||
            text.Any(c => c is < '0' or > '9') || text.Length > 1 && text[0] == '0' ||
            !ulong.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out var result))
            throw Invalid(name + " must be a canonical decimal UInt64 string from a returned native point index.");
        return result;
    }

    static JsonElement Required(JsonElement input, string name) => input.TryGetProperty(name, out var value)
        ? value : throw Invalid(name + " is required.");
    static double Number(JsonElement input, string name) => input.ValueKind == JsonValueKind.Number && input.TryGetDouble(out var value) && double.IsFinite(value)
        ? value : throw Invalid(name + " must be a finite number.");
    static double[] Vector(JsonElement input, string name)
    {
        if (input.ValueKind != JsonValueKind.Array || input.GetArrayLength() != 3) throw Invalid(name + " requires exactly three finite WCS coordinates.");
        return input.EnumerateArray().Select(v => Number(v, name)).ToArray();
    }
    static double[] Unit(double[] vector, string name)
    {
        var scale = vector.Max(v => Math.Abs(v));
        if (scale == 0) throw Invalid(name + " must be nonzero.");
        var normalized = vector.Select(v => v / scale).ToArray();
        var length = Math.Sqrt(normalized.Sum(v => v * v));
        return normalized.Select(v => v / length).ToArray();
    }
    static double Dot(double[] first, double[] second) => Enumerable.Range(0, 3).Sum(i => first[i] * second[i]);
    internal static DomainException Invalid(string message) => new("INVALID_ARGUMENT", message);
}
