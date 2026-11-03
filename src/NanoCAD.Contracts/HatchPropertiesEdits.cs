using System.Text.Json;

namespace NanoCAD.Contracts;

public sealed record HatchPropertyChanges(string? PatternName,double? PatternAngle,double? PatternScale,double? PatternSpace,
    Point2? OriginOcs,string? HatchStyle,bool? PatternDouble);
public sealed record SetHatchPropertiesSpec(string[] Targets,string HatchToken,HatchPropertyChanges Properties)
    :EditSpec("set_hatch_properties",Targets);

public static partial class ModifyValidation
{
    private static SetHatchPropertiesSpec ParseHatchProperties(JsonElement item,string path,string[] handles)
    {
        Shape(item,path,"operation","handles","hatch_token","properties");
        if(handles.Length!=1)throw Invalid(path+".handles","Address one exact native Hatch per operation.");
        string token=Text(item,"hatch_token",path,64);
        if(token.Length!=64||token.Any(c=>!Uri.IsHexDigit(c)))throw Invalid(path+".hatch_token","Use the fresh SHA256 hatch_token from inspect_objects geometry.editing.");
        var properties=Required(item,"properties",path);string pp=path+".properties";
        Shape(properties,pp,"pattern_name","pattern_angle","pattern_scale","pattern_space","origin_ocs","hatch_style","pattern_double");
        if(!properties.EnumerateObject().Any())throw Invalid(pp,"At least one hatch property is required.");
        double Finite(JsonElement value,string name)
        {
            if(value.ValueKind!=JsonValueKind.Number||!value.TryGetDouble(out double number)||!double.IsFinite(number))
                throw Invalid(name,"Expected a finite native property value.","INVALID_GEOMETRY");
            return number==0?0:number;
        }
        double? Number(string name)=>properties.TryGetProperty(name,out var value)?Finite(value,pp+"."+name):null;
        string? name=properties.TryGetProperty("pattern_name",out _)?Text(properties,"pattern_name",pp,255):null;
        if(name!=null&&(name.Trim()!=name||name.IndexOfAny(new[]{'\\','/',':','|'})>=0))
            throw Invalid(pp+".pattern_name","Use an installed predefined pattern name (or SOLID), not a file/path or external-qualified name.");
        double? angle=Number("pattern_angle"),scale=Number("pattern_scale"),space=Number("pattern_space");
        if(angle is <0||angle>=2*Math.PI)throw Invalid(pp+".pattern_angle","Use OCS radians in [0,2pi).");
        if(scale<=0)throw Invalid(pp+".pattern_scale","Native pattern scale must be positive.");
        if(space<=0)throw Invalid(pp+".pattern_space","Native user-defined pattern spacing must be positive.");
        Point2? origin=null;
        if(properties.TryGetProperty("origin_ocs",out var point))
        {
            if(point.ValueKind!=JsonValueKind.Array||point.GetArrayLength()!=2)throw Invalid(pp+".origin_ocs","Use a native hatch-plane OCS XY pair.");
            origin=new(Finite(point[0],pp+".origin_ocs[0]"),Finite(point[1],pp+".origin_ocs[1]"));
        }
        string? style=properties.TryGetProperty("hatch_style",out _)?Text(properties,"hatch_style",pp,16):null;
        if(style!=null&&style is not ("normal" or "outer" or "ignore"))throw Invalid(pp+".hatch_style","Use normal, outer or ignore native island handling.");
        bool? doubled=null;
        if(properties.TryGetProperty("pattern_double",out var doubleValue))
        {
            if(doubleValue.ValueKind is not (JsonValueKind.True or JsonValueKind.False))throw Invalid(pp+".pattern_double","Expected Boolean.");
            doubled=doubleValue.GetBoolean();
        }
        return new(handles,token.ToUpperInvariant(),new(name,angle,scale,space,origin,style,doubled));
    }
}

public static class HatchPropertiesValidation
{
    // Distinct hatches can share a request. A fresh token addresses one stored
    // source state, so preceding edits of that same source are not implicit.
    public static void Batch(IReadOnlyCollection<EditSpec> edits)
    {
        foreach(var edit in edits.OfType<SetHatchPropertiesSpec>())
            if(edits.Count(candidate=>candidate.Handles.Contains(edit.Handles[0],StringComparer.Ordinal))!=1)
                throw new DomainException("INVALID_OPERATION_ORDER","set_hatch_properties requires only its dedicated operation on that Hatch in this request; refresh its token after other edits.");
    }
}
