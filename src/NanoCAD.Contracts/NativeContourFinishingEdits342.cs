using System.Text.Json;

namespace NanoCAD.Contracts;

// Native parameters are read from the actual curve, never inferred from a
// tessellation, distance fraction, vertex index or common family formula.
public sealed record NativeContourFinishingSpec342(string Kind,string[] Targets,string ExpectedCurveToken,
    string[] BoundaryHandles,double[] CutParameters,bool EraseSource,Point3? PickPointWcs,string? End,Point3? TargetPointWcs,
    [property:System.Text.Json.Serialization.JsonIgnore(Condition=System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)] SketchPlaneSpec? Workplane=null)
    :EditSpec(Kind,Targets);

public static class NativeContourFinishingValidation342
{
    private static DomainException Bad(string path,string message)=>new("INVALID_ARGUMENT",path+": "+message,false,new{path});
    private static string Text(JsonElement value,string path)
    {if(value.ValueKind!=JsonValueKind.String)throw Bad(path,"Expected string.");try{return value.GetString()!;}catch(InvalidOperationException){throw Bad(path,"Invalid Unicode scalar text.");}}
    public static bool NativeMode(JsonElement input,string path)
    {return input.TryGetProperty("mode",out var mode)&&Text(mode,path+".mode")=="native";}
    private static JsonElement Required(JsonElement input,string name,string path)=>input.TryGetProperty(name,out var value)?value:throw Bad(path+"."+name,"Required field is missing.");
    private static double Number(JsonElement value,string path)
    {if(value.ValueKind!=JsonValueKind.Number||!value.TryGetDouble(out double number)||!double.IsFinite(number))throw Bad(path,"Expected finite native value.");return number==0?0:number;}
    private static Point3 Point(JsonElement value,string path)
    {if(value.ValueKind!=JsonValueKind.Array||value.GetArrayLength()!=3)throw Bad(path,"Expected WCS XYZ triple.");var p=value.EnumerateArray().Select((v,i)=>Number(v,path+"["+i+"]")).ToArray();if(p.Any(v=>Math.Abs(v)>1e6))throw Bad(path,"WCS coordinates must be within +/-1000000 drawing millimeters.");return new(p[0],p[1],p[2]);}
    public static NativeContourFinishingSpec342 Parse(JsonElement input,string path,string[] handles,string kind)
    {
        if(handles.Length!=1)throw Bad(path+".handles","Select exactly one existing native source curve.");
        if(kind is not ("trim_curve" or "split_curve" or "extend_curve"))throw Bad(path+".operation","Expected native trim_curve, split_curve or extend_curve.");
        var allowed=new HashSet<string>(["operation","handles","mode","expected_curve_token","workplane"],StringComparer.Ordinal);
        foreach(var field in kind=="trim_curve"?new[]{"boundary_handles","pick_point_wcs"}:kind=="split_curve"?new[]{"cut_parameters","erase_sources"}:new[]{"end","target_point_wcs"})allowed.Add(field);
        if(input.ValueKind!=JsonValueKind.Object)throw Bad(path,"Expected object.");var seen=new HashSet<string>(StringComparer.Ordinal);
        foreach(var property in input.EnumerateObject())if(!seen.Add(property.Name)||!allowed.Contains(property.Name))throw Bad(path+"."+property.Name,"Unknown or duplicate field.");
        if(Text(Required(input,"operation",path),path+".operation")!=kind||!NativeMode(input,path))throw Bad(path+".mode","Use explicit native mode.");
        string token=Text(Required(input,"expected_curve_token",path),path+".expected_curve_token");
        if(token.Length!=64||token.Any(c=>!Uri.IsHexDigit(c)))throw Bad(path+".expected_curve_token","Expected the current 64-hex geometry.native_curve.curve_token.");
        string[] boundaries=[];double[] cuts=[];Point3? pick=null,target=null;string? end=null;bool erase=kind=="trim_curve";
        if(kind=="trim_curve")
        {
            var values=Required(input,"boundary_handles",path);if(values.ValueKind!=JsonValueKind.Array||values.GetArrayLength()==0)throw Bad(path+".boundary_handles","Select nonempty distinct existing finite boundaries.");
            var refs=new HashSet<string>(StringComparer.Ordinal);foreach(var value in values.EnumerateArray())
            {string handle=Validation.NormalizeHandle(Text(value,path+".boundary_handles"));if(handles.Contains(handle)||!refs.Add(handle))throw Bad(path+".boundary_handles","Source and normalized boundary handles must be distinct.");}
            boundaries=refs.ToArray();pick=Point(Required(input,"pick_point_wcs",path),path+".pick_point_wcs");
        }
        else if(kind=="split_curve")
        {
            var values=Required(input,"cut_parameters",path);if(values.ValueKind!=JsonValueKind.Array||values.GetArrayLength()==0)throw Bad(path+".cut_parameters","Provide nonempty strictly increasing native parameters from the current source interval.");
            cuts=values.EnumerateArray().Select((v,i)=>Number(v,path+".cut_parameters["+i+"]")).ToArray();for(int i=1;i<cuts.Length;i++)if(cuts[i]<=cuts[i-1])throw Bad(path+".cut_parameters","Native parameters must increase strictly without duplicate cuts.");
            var flag=Required(input,"erase_sources",path);if(flag.ValueKind is not (JsonValueKind.True or JsonValueKind.False))throw Bad(path+".erase_sources","Expected explicit Boolean source replacement choice.");erase=flag.GetBoolean();
        }
        else
        {end=Text(Required(input,"end",path),path+".end");if(end is not ("start" or "end"))throw Bad(path+".end","Use stored start or end.");target=Point(Required(input,"target_point_wcs",path),path+".target_point_wcs");}
        var frame=input.TryGetProperty("workplane",out var workplane)?DrawingWorkplane.Parse(workplane):null;
        return new(kind,handles,token.ToUpperInvariant(),boundaries,cuts,erase,pick,end,target,frame);
    }
    public static void Batch(IReadOnlyList<EditSpec> operations)
    {if(operations.Any(e=>e is NativeContourFinishingSpec342)&&operations.Count!=1)throw new DomainException("INVALID_OPERATION_ORDER","Native finishing must be the sole operation; refresh returned current curve refs/token before subsequent editing.");}
}
