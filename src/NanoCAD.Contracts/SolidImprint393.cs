using System.Text.Json;

namespace NanoCAD.Contracts;

// Native imprint of retained curve copies onto one existing direct solid.
public sealed record SolidImprintCurve393(string Handle,string CurveToken);
public sealed record SolidImprintEditSpec393(string[] Targets,string TopologyToken,SolidImprintCurve393[] SourceCurves)
    :EditSpec("imprint_solid",Targets);
public sealed record SolidImprintSource393(EntityRef source,string curve_token);
public sealed record SolidImprintTopology393(string snapshot_token,int faces,int edges);
public sealed record SolidImprintChange393(int operation_index,EntityRef target,SolidImprintSource393[] sources,
    SolidImprintTopology393 before,SolidImprintTopology393 after,bool topology_token_changed,
    int native_calls_completed,string observation_scope);

public static partial class ModifyValidation
{
    private static EditSpec ParseSolidImprint393(JsonElement item,string path,string[] handles)
    {
        Shape(item,path,"operation","handles","topology_token","source_curves");
        if(handles.Length!=1)throw Invalid(path+".handles","Native imprint needs one existing direct solid.");
        string Token(JsonElement value,string name,string at)
        {
            var token=Text(value,name,at,64);
            if(token.Length!=64||token.Any(c=>!(c is >= '0' and <= '9' or >= 'A' and <= 'F')))
                throw Invalid(at+"."+name,"Use the exact current uppercase SHA-256 read token.");
            return token;
        }
        var topology=Token(item,"topology_token",path);var input=Required(item,"source_curves",path);
        if(input.ValueKind!=JsonValueKind.Array||input.GetArrayLength()==0)
            throw Invalid(path+".source_curves","Use retained curve handle/token pairs from actual native-curve inspection.");
        var sources=new List<SolidImprintCurve393>();var seen=new HashSet<string>(StringComparer.Ordinal);int index=0;
        foreach(var value in input.EnumerateArray())
        {
            string at=path+".source_curves["+(index++)+"]";Shape(value,at,"handle","curve_token");
            string handle=Validation.NormalizeHandle(Text(value,"handle",at,16));
            if(handles.Contains(handle)||!seen.Add(handle))throw Invalid(at+".handle","Target and retained source handles must be distinct.");
            sources.Add(new(handle,Token(value,"curve_token",at)));
        }
        return new SolidImprintEditSpec393(handles,topology,sources.ToArray());
    }
}