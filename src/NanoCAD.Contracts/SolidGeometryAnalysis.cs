using System.Text.Json;

namespace NanoCAD.Contracts;

public static class SolidGeometryAnalysisValidation
{
    public static bool Handles(string? mode)=>mode is "solid_containment" or "solid_interference";

    public static GeometryAnalysisArguments Parse(Arguments read,string mode,JsonElement input)
    {
        if(!Handles(mode))throw new DomainException("INVALID_ARGUMENT","Unknown solid analysis mode.");
        if(read.BridgeId==null||read.DocumentId==null||read.ExpectedRevision==null||read.Handles==null)
            throw new DomainException("INVALID_ARGUMENT","Solid analysis requires explicit bridge/document/revision and body handles.");
        if(input.TryGetProperty("workplane",out _))
            throw new DomainException("INVALID_ARGUMENT","Solid analysis uses WCS geometry; workplane is not applicable.");
        if(read.Handles!.Length!=(mode=="solid_containment"?1:2))
            throw new DomainException("INVALID_ARGUMENT",mode=="solid_containment"
                ?"Solid containment requires one body handle and a WCS point."
                :"Solid interference requires exactly two distinct body handles.");
        double[]? point=null;
        if(mode=="solid_containment")
        {
            if(!input.TryGetProperty("point",out var value)||value.ValueKind!=JsonValueKind.Array||value.GetArrayLength()!=3)
                throw new DomainException("INVALID_ARGUMENT","Solid containment requires a WCS XYZ point.");
            point=value.EnumerateArray().Select(v=>v.ValueKind==JsonValueKind.Number&&v.TryGetDouble(out var n)&&double.IsFinite(n)&&Math.Abs(n)<=1e12
                ?n:throw new DomainException("INVALID_ARGUMENT","Point coordinates must be finite numbers within ±1e12 drawing units.")).ToArray();
        }
        else if(input.TryGetProperty("point",out _))
            throw new DomainException("INVALID_ARGUMENT","Solid interference does not accept a point.");
        return new(read,mode,point);
    }
}
