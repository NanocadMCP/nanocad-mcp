using System.Text.Json;

namespace NanoCAD.Contracts;

public sealed record BoundaryTrace408(double[] Point,bool DetectIslands);

public static class BoundaryTraceValidation408
{
    public static GeometryAnalysisArguments Parse(JsonElement input)
    {
        if(input.ValueKind!=JsonValueKind.Object)throw new DomainException("INVALID_ARGUMENT","Boundary arguments must be an object.");
        var seen=new HashSet<string>(StringComparer.Ordinal);var read=new Dictionary<string,JsonElement>();
        foreach(var property in input.EnumerateObject())
        {
            if(!seen.Add(property.Name)||property.Name is not ("bridge_instance_id" or "document_id" or "expected_revision" or "mode" or "point" or "detect_islands" or "space"))
                throw new DomainException("INVALID_ARGUMENT","Unknown or duplicate boundary argument: "+property.Name);
            if(property.Name is not ("mode" or "point" or "detect_islands"))read.Add(property.Name,property.Value);
        }
        var args=Validation.Parse("query_objects",Wire.Element(read));
        if(args.BridgeId==null||args.DocumentId==null||args.ExpectedRevision==null)throw new DomainException("INVALID_ARGUMENT","Boundary discovery requires the current bridge, active document and expected_revision.");
        if(!input.TryGetProperty("point",out var point)||point.ValueKind!=JsonValueKind.Array||point.GetArrayLength()!=3)
            throw new DomainException("INVALID_ARGUMENT","point must be a WCS XYZ seed.");
        var coordinates=point.EnumerateArray().Select(item=>item.ValueKind==JsonValueKind.Number&&item.TryGetDouble(out var n)&&double.IsFinite(n)&&Math.Abs(n)<=1e12?n:throw new DomainException("INVALID_GEOMETRY","Seed coordinates must be finite drawing-unit values within ±1e12.")).ToArray();
        bool detect=true;
        if(input.TryGetProperty("detect_islands",out var islands))
        {if(islands.ValueKind is not (JsonValueKind.True or JsonValueKind.False))throw new DomainException("INVALID_ARGUMENT","detect_islands must be boolean.");detect=islands.GetBoolean();}
        return new(args,"trace_boundary",coordinates){BoundaryTrace=new(coordinates,detect)};
    }
}
