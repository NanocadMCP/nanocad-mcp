using System.Text.Json;
using System.Text.Json.Nodes;

namespace NanoCAD.Contracts;

public sealed record SetHatchPatternSpec(string[] Targets,double ExpectedAngle,double ExpectedScale,double Angle,double Scale):EditSpec("set_hatch_pattern",Targets);
public static partial class ModifyValidation
{
    private static SetHatchPatternSpec ParseHatchPattern(JsonElement item,string path,string[] handles)
    {
        Shape(item,path,"operation","handles","expected_angle","expected_scale","angle","scale");
        if(handles.Length!=1)throw Invalid(path,"Exactly one native ANSI31 Hatch handle required.");
        double Read(string name,bool angle)
        {
            var value=Required(item,name,path);
            if(value.ValueKind!=JsonValueKind.Number||!value.TryGetDouble(out var n)||!double.IsFinite(n)||
                (angle?n<0||n>=2*Math.PI:n<.01||n>100))throw Invalid(path+"."+name,"Angle must be0..2pi exclusive; scale0.01..100.","INVALID_GEOMETRY");
            return n==0?0:n;
        }
        return new(handles,Read("expected_angle",true),Read("expected_scale",false),Read("angle",true),Read("scale",false));
    }
}
public static class HatchPatternEditGeometry
{
    public static JsonElement Expected(JsonElement before,SetHatchPatternSpec edit)
    {
        var geometry=before.GetProperty("geometry");
        if(Math.Abs(geometry.GetProperty("pattern_angle").GetDouble()-edit.ExpectedAngle)>1e-10 ||
            Math.Abs(geometry.GetProperty("pattern_scale").GetDouble()-edit.ExpectedScale)>1e-10)
            throw new DomainException("HATCH_PATTERN_CHANGED","Stored angle/scale differs from expectation after preceding edits.");
        var result=JsonNode.Parse(before.GetRawText())!;result["geometry"]!["pattern_angle"]=edit.Angle;result["geometry"]!["pattern_scale"]=edit.Scale;
        return Wire.Element(result);
    }
    public static void Verify(JsonElement expected,JsonElement actual)
    {
        if(!BlockMoveGeometry.SameProperties(expected,actual))
            throw new DomainException("NATIVE_HATCH_MISMATCH","Pattern edit changed preserved boundary/properties or failed to retain requested parameters.");
    }
}
