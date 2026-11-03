using System.Text.Json;

namespace NanoCAD.Contracts;

public sealed record GeometryAnalysisArguments(Arguments Read, string Mode, double[]? Point,SketchPlaneSpec? Workplane=null)
{
    public BoundaryTrace408? BoundaryTrace {get;init;}
    public SolidMeasurement409? SolidMeasurement {get;init;}
}

public static class GeometryAnalysisValidation
{
    public static GeometryAnalysisArguments Parse(JsonElement input)
    {
        if (input.ValueKind != JsonValueKind.Object) throw new DomainException("INVALID_ARGUMENT", "Arguments must be an object.");
        if(input.TryGetProperty("mode",out var boundaryMode)&&boundaryMode.ValueKind==JsonValueKind.String&&boundaryMode.GetString()=="trace_boundary")
            return BoundaryTraceValidation408.Parse(input);
        if(input.TryGetProperty("mode",out var measurementMode)&&measurementMode.ValueKind==JsonValueKind.String&&SolidMeasurementValidation409.Handles(measurementMode.GetString()))
            return SolidMeasurementValidation409.Parse(input);
        var names = new HashSet<string>(StringComparer.Ordinal);
        var read = new Dictionary<string, JsonElement>();
        foreach (var p in input.EnumerateObject())
        {
            if (!names.Add(p.Name)) throw new DomainException("INVALID_ARGUMENT", "Duplicate argument: " + p.Name);
            if (p.Name is not ("mode" or "point" or "workplane")) read.Add(p.Name, p.Value);
        }
        var args = Validation.Parse("inspect_objects", Wire.Element(read));
        if (args.BridgeId == null || args.ExpectedRevision == null)
            throw new DomainException("INVALID_ARGUMENT", "Geometry analysis requires explicit bridge_instance_id and expected_revision.");
        if(input.TryGetProperty("mode",out var solidMode)&&solidMode.ValueKind==JsonValueKind.String&&SolidGeometryAnalysisValidation.Handles(solidMode.GetString()))
            return SolidGeometryAnalysisValidation.Parse(args,solidMode.GetString()!,input);
        if (!input.TryGetProperty("mode", out var mode) || mode.ValueKind != JsonValueKind.String || mode.GetString() is not ("metrics" or "intersections" or "distance" or "relations" or "containment"))
            throw new DomainException("INVALID_ARGUMENT", "mode must be metrics, intersections, distance, relations or containment.");
        if (mode.GetString() is "intersections" or "distance" or "relations" && args.Handles is not { Length: 2 })
            throw new DomainException("INVALID_ARGUMENT", "Pair analysis requires exactly two distinct handles; metrics accepts a nonempty handle set.");
        double[]? point = null;
        if (input.TryGetProperty("point", out var location))
        {
            if (mode.GetString() is not ("metrics" or "containment") || location.ValueKind != JsonValueKind.Array || location.GetArrayLength() != 3)
                throw new DomainException("INVALID_ARGUMENT", "point is a WCS three-coordinate array available only in metrics or containment mode.");
            point = location.EnumerateArray().Select(x => x.ValueKind == JsonValueKind.Number && x.TryGetDouble(out var n) && double.IsFinite(n) && Math.Abs(n) <= 1e12
                ? n : throw new DomainException("INVALID_ARGUMENT", "Point coordinates must be finite numbers within ±1e12 drawing units.")).ToArray();
        }
        var frame=input.TryGetProperty("workplane",out var plane)?DrawingWorkplane.Parse(plane):null;
        if(frame!=null&&mode.GetString() is not ("relations" or "containment"))
            throw new DomainException("INVALID_ARGUMENT","workplane applies to relations and containment; other analysis modes retain their existing WCS semantics.");
        if(mode.GetString()=="containment" && (args.Handles!.Length!=1 || point==null || frame==null&&point[2]!=0))
            throw new DomainException("INVALID_ARGUMENT","Containment requires exactly one handle and one WCS XY/Z0 point.");
        if(frame!=null&&point!=null)DrawingWorkplane.Local(frame,new(point[0],point[1],point[2]));
        return new(args, mode.GetString()!, point,frame);
    }
}
