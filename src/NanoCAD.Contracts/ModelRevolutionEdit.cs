using System.Text.Json;
namespace NanoCAD.Contracts;

public sealed record ModelRevolutionEdit(string[] Targets,string ExpectedLastItemId,double ExpectedAngleDegrees,double AngleDegrees):EditSpec("set_model_revolution",Targets);
public sealed record ModelRevolutionChange(EntityRef body_ref,string feature_id,string variable_id,double previous_angle_degrees,double angle_degrees);
public static partial class ModifyValidation
{
    private static ModelRevolutionEdit ParseModelRevolution(JsonElement item,string path,string[] handles)
    {
        Shape(item,path,"operation","handles","expected_last_item_id","expected_angle_degrees","angle_degrees");
        if(handles.Length!=1)throw Invalid(path,"Choose one native body.");
        string last=Text(item,"expected_last_item_id",path,38);
        if(!Guid.TryParseExact(last,"B",out var id)||id==Guid.Empty)throw Invalid(path,"Read a current braced last_item_id.");
        double Angle(string key)
        {
            double value=Number(Required(item,key,path),path+"."+key,0,360);
            if(value<=0||value*Math.PI/180<=0)throw Invalid(path+"."+key,"Use a positive angle representable in native radians, at most360 degrees.");
            return value;
        }
        return new(handles,id.ToString("B").ToUpperInvariant(),Angle("expected_angle_degrees"),Angle("angle_degrees"));
    }
}
