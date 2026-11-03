using System.Text.Json;

namespace NanoCAD.Contracts;

public sealed record NativeCornerCurveSpec343(string Kind,string[] Targets,string[] ExpectedCurveTokens,string[] Ends,
    bool TrimSources,double? Radius,double[] Distances,string CornerMode="common_endpoint"):EditSpec(Kind,Targets);

public sealed record ContourCornerResult343(int operation_index,string operation,EntityRef[] sources,string[] ends,
    EntityRef connector,bool sources_trimmed,double[][] attachment_points,double[] attachment_parameters,
    double[] setback_lengths,double? radius,
    [property:System.Text.Json.Serialization.JsonIgnore(Condition=System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)] string? corner_mode=null,
    [property:System.Text.Json.Serialization.JsonIgnore(Condition=System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)] double[]? corner_point=null,
    [property:System.Text.Json.Serialization.JsonIgnore(Condition=System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)] double[]? source_corner_gaps=null);

public static class IndependentContourCornersValidation343
{
    private static DomainException Bad(string path,string message)=>new("INVALID_ARGUMENT",path+": "+message,false,new{path});
    private static string Text(JsonElement value,string path)
    {if(value.ValueKind!=JsonValueKind.String)throw Bad(path,"Expected string.");try{return value.GetString()!;}catch(InvalidOperationException){throw Bad(path,"Invalid Unicode scalar text.");}}
    private static JsonElement Required(JsonElement value,string key,string path)=>value.TryGetProperty(key,out var item)?item:throw Bad(path+"."+key,"Required field is missing.");
    private static JsonElement[] Pair(JsonElement value,string path)
    {if(value.ValueKind!=JsonValueKind.Array||value.GetArrayLength()!=2)throw Bad(path,"Provide exactly two ordered entries.");return value.EnumerateArray().ToArray();}
    private static double Length(JsonElement value,string path)
    {if(value.ValueKind!=JsonValueKind.Number||!value.TryGetDouble(out double number)||!double.IsFinite(number)||number<=0||number>1e6)throw Bad(path,"Expected positive drawing length at most1000000 millimeters.");return number;}
    public static NativeCornerCurveSpec343 Parse(JsonElement input,string path,string[] handles,string kind)
    {
        if(kind is not ("fillet_curves" or "chamfer_curves"))throw Bad(path+".operation","Expected fillet_curves or chamfer_curves.");
        if(input.ValueKind!=JsonValueKind.Object)throw Bad(path,"Expected object.");
        var allowed=new HashSet<string>(["operation","handles","expected_curve_tokens","ends","trim_sources","corner_mode",kind=="fillet_curves"?"radius":"distances"],StringComparer.Ordinal);var seen=new HashSet<string>(StringComparer.Ordinal);
        foreach(var item in input.EnumerateObject())if(!seen.Add(item.Name)||!allowed.Contains(item.Name))throw Bad(path+"."+item.Name,"Unknown or duplicate field.");
        if(Text(Required(input,"operation",path),path+".operation")!=kind)throw Bad(path+".operation","Operation differs from selected branch.");
        // Preserve explicit wire order; generic normalized handle-set traversal
        // is not an ordering contract for first/second setbacks or solver seeds.
        var ordered=Pair(Required(input,"handles",path),path+".handles").Select((v,i)=>Validation.NormalizeHandle(Text(v,path+".handles["+i+"]"))).ToArray();
        if(ordered[0]==ordered[1]||handles.Length!=2||!ordered.ToHashSet(StringComparer.Ordinal).SetEquals(handles))throw Bad(path+".handles","Select two distinct existing normalized source handles.");
        var tokens=Pair(Required(input,"expected_curve_tokens",path),path+".expected_curve_tokens").Select((v,i)=>Text(v,path+".expected_curve_tokens["+i+"]")).ToArray();
        for(int i=0;i<2;i++){if(tokens[i].Length!=64||tokens[i].Any(c=>!Uri.IsHexDigit(c)))throw Bad(path+".expected_curve_tokens["+i+"]","Read the corresponding current geometry.native_curve.curve_token.");tokens[i]=tokens[i].ToUpperInvariant();}
        var ends=Pair(Required(input,"ends",path),path+".ends").Select((v,i)=>Text(v,path+".ends["+i+"]")).ToArray();
        if(ends.Any(v=>v is not ("start" or "end")))throw Bad(path+".ends","Use stored start or end for each corresponding source.");
        var flag=Required(input,"trim_sources",path);if(flag.ValueKind is not (JsonValueKind.True or JsonValueKind.False))throw Bad(path+".trim_sources","Choose explicitly whether both selected ends are trimmed in place.");
        double? radius=null;double[] distances=[];
        if(kind=="fillet_curves")radius=Length(Required(input,"radius",path),path+".radius");
        else distances=Pair(Required(input,"distances",path),path+".distances").Select((v,i)=>Length(v,path+".distances["+i+"]")).ToArray();
        string mode=input.TryGetProperty("corner_mode",out var selectedMode)?Text(selectedMode,path+".corner_mode"):"common_endpoint";
        if(mode is not ("common_endpoint" or "supporting_line_intersection"))throw Bad(path+".corner_mode","Use common_endpoint or supporting_line_intersection.");
        return new(kind,ordered,tokens,ends,flag.GetBoolean(),radius,distances,mode);
    }
    public static void Batch(IReadOnlyList<EditSpec> edits)
    {if(edits.Any(e=>e is NativeCornerCurveSpec343)&&edits.Count!=1)throw new DomainException("INVALID_OPERATION_ORDER","The two-curve corner must be the sole operation; read its current returned sources/connector before further edits.");}
    public static double TargetDistance(string end,double startDistance,double endDistance,double setback)
    {
        if(end is not ("start" or "end")||!double.IsFinite(startDistance)||!double.IsFinite(endDistance)||!double.IsFinite(setback)||setback<=0||endDistance<=startDistance||setback>=endDistance-startDistance)
            throw new DomainException("INVALID_CORNER_SETBACK","The setback must be positive and shorter than the actual finite source length; no source extension is performed.");
        double result=end=="start"?startDistance+setback:endDistance-setback;
        if(!double.IsFinite(result)||result<=startDistance||result>=endDistance)throw new DomainException("INVALID_CORNER_SETBACK","Native distance cannot represent an interior setback.");
        return result;
    }
}
