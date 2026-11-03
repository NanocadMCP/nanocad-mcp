using System.Text.Json;

namespace NanoCAD.Contracts;

public sealed record SolidOffsetEdit353(string[] Targets,string TopologyToken,double Distance):EditSpec("offset_solid",Targets);
public sealed record SolidStockBounds353(double[] min,double[] max);
public sealed record SolidStockGeometry353(double volume,SolidStockBounds353 bounds_wcs,
    bool? native_brep_validated,string? native_brep_validation_unavailable_reason);
public sealed record SolidStockChange353(int operation_index,string operation,EntityRef source_ref,
    string boundary_mode,string side,double native_offset_distance,string[] removed_face_ids,
    SolidStockGeometry353 before,SolidStockGeometry353 after,
    string observation_scope="actual whole-body WCS bounds and native volume; no general wall-thickness or clearance measurement",
    string construction_method="native_shell_body");

public static class SolidShellSide353
{
    public static string Parse(JsonElement item,string path)
    {
        if(!item.TryGetProperty("side",out var value))return "inward";
        if(value.ValueKind==JsonValueKind.String&&value.GetString() is "inward" or "outward")return value.GetString()!;
        throw new DomainException("INVALID_ARGUMENT",path+".side: Use inward or outward.");
    }
    public static double NativeOffset(string side,double thickness)=>side switch
    {
        "inward"=>-thickness,"outward"=>thickness,
        _=>throw new DomainException("INVALID_ARGUMENT","Unknown shell side.")
    };
}

public static partial class ModifyValidation
{
    private static SolidOffsetEdit353 ParseSolidOffset353(JsonElement item,string path,string[] handles)
    {
        Shape(item,path,"operation","handles","topology_token","distance");
        if(handles.Length!=1)throw Invalid(path+".handles","Offset exactly one direct solid per operation.");
        string token=Text(item,"topology_token",path,64);
        if(token.Length!=64||token.Any(c=>!(c>='0'&&c<='9'||c>='A'&&c<='F')))
            throw Invalid(path+".topology_token","Expected the exact uppercase topology snapshot token.");
        double distance=Number(Required(item,"distance",path),path+".distance",-1e6,1e6);
        if(Math.Abs(distance)<.001)throw Invalid(path+".distance","Native offset magnitude must be at least0.001mm.","INVALID_GEOMETRY");
        return new(handles,token,distance);
    }
}
