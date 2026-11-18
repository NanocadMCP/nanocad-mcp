using System.Text.Json;

namespace NanoCAD.Contracts;

public sealed record DrilledSolidCopySpec(string SourceHandle, Point3 Start, Point3 Direction, double Radius, double Depth, string LayerName) : EntitySpec("Solid3d", LayerName)
{
    public static void VerifyRemoval(double before, double after, Func<(double Material, double Removed)> measureLocally)
    {
        double removed = before - after;
        if (double.IsFinite(removed) && removed > Math.Max(1e-7, Math.Abs(before) * 1e-9)) return;
        var local = measureLocally();
        if (local.Material == 0) throw new DomainException("NO_MATERIAL_REMOVED", "The drilling cylinder contains no stock material.");
        if (!double.IsFinite(local.Material) || !double.IsFinite(local.Removed) || local.Material < 0 || local.Removed <= 0 ||
            Math.Abs(local.Material - local.Removed) > local.Material * 1e-8)
            throw new DomainException("DRILL_EFFECT_UNVERIFIED", "The volume removed inside the drilling cylinder does not match its original material.", false,
                new { original_local_volume = double.IsFinite(local.Material) ? (double?)local.Material : null, removed_local_volume = double.IsFinite(local.Removed) ? (double?)local.Removed : null });
    }
}
public sealed record SolidDrillEditSpec(string[] Targets, Point3 Start, Point3 Direction, double Radius, double Depth) : EditSpec("drill_solid", Targets)
{
    public DrilledSolidCopySpec AsCopySpec(string layer) => new(Targets.Single(), Start, Direction, Radius, Depth, layer);
}

public static partial class CreateValidation
{
    private static EntitySpec ParseDrilledSolidCopy(JsonElement item, string path, string layer)
    {
        Shape(item, path, "type", "kind", "layer", "source_handle", "start", "direction", "radius", "depth");
        string handle = Validation.NormalizeHandle(Text(item, "source_handle", path, 16));
        var start = TripleValue(item, "start", path); var direction = TripleValue(item, "direction", path);
        if (Math.Sqrt(direction.X * direction.X + direction.Y * direction.Y + direction.Z * direction.Z) < 1e-9)
            throw Invalid(path + ".direction", "Drill direction must be nonzero.", "INVALID_GEOMETRY");
        return new DrilledSolidCopySpec(handle, start, direction,
            Number(Required(item, "radius", path), path + ".radius", 1e-3, 1e6),
            Number(Required(item, "depth", path), path + ".depth", 1e-3, 1e6), layer);
    }
}

public static partial class ModifyValidation
{
    private static SolidDrillEditSpec ParseSolidDrill(JsonElement item, string path, string[] handles)
    {
        if (handles.Length != 1) throw Invalid(path + ".handles", "Drill exactly one direct solid per operation.");
        Shape(item, path, "operation", "handles", "start", "direction", "radius", "depth");
        Point3 Triple(string name)
        {
            var value = Required(item, name, path);
            if (value.ValueKind != JsonValueKind.Array || value.GetArrayLength() != 3) throw Invalid(path + "." + name, "Expected WCS XYZ triple.", "INVALID_GEOMETRY");
            return new(Number(value[0], path + "." + name + "[0]", -1e6, 1e6), Number(value[1], path + "." + name + "[1]", -1e6, 1e6), Number(value[2], path + "." + name + "[2]", -1e6, 1e6));
        }
        var start = Triple("start"); var direction = Triple("direction");
        if (Math.Sqrt(direction.X * direction.X + direction.Y * direction.Y + direction.Z * direction.Z) < 1e-9)
            throw Invalid(path + ".direction", "Drill direction must be nonzero.", "INVALID_GEOMETRY");
        return new(handles, start, direction, Number(Required(item, "radius", path), path + ".radius", 1e-3, 1e6),
            Number(Required(item, "depth", path), path + ".depth", 1e-3, 1e6));
    }
}
