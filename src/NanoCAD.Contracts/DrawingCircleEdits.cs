using System.Text.Json;

namespace NanoCAD.Contracts;

public sealed record DrawingCircleEdit(string Action,string[] Targets,double ExpectedRadius,double? Radius=null,
    string? ConstraintId=null,string? ExpectedExpression=null,double? Distance=null,string? Direction=null,string? Name=null)
    :EditSpec(Action,Targets);
public sealed record DrawingCircleChange(string operation,EntityRef source_ref,string? constraint_id,string? variable_id,
    double radius,EntityRef? body_ref,string? sketch_id,string? projection_id,string? feature_id,EntityRef[] rebuilt_bodies);

public static partial class ModifyValidation
{
    private static DrawingCircleEdit ParseDrawingCircle(JsonElement item,string path,string[] handles,string action)
    {
        if(handles.Length!=1)throw Invalid(path,"Choose exactly one source Circle.");
        double Radius(string field)=>Number(Required(item,field,path),path+"."+field,.001,1e6);
        double expected=Radius("expected_radius");
        if(action=="add_drawing_radius")
        {
            Shape(item,path,"operation","handles","expected_radius");return new(action,handles,expected);
        }
        if(action=="set_drawing_radius")
        {
            Shape(item,path,"operation","handles","expected_radius","constraint_id","expected_expression","radius");
            if(!Guid.TryParseExact(Text(item,"constraint_id",path,38),"B",out var id)||id==Guid.Empty)
                throw Invalid(path+".constraint_id","Use a current opaque constraint ID from inspect_constraints.");
            return new(action,handles,expected,Radius("radius"),id.ToString("B").ToUpperInvariant(),Text(item,"expected_expression",path,8192));
        }
        Shape(item,path,"operation","handles","expected_radius","distance","direction","name");
        var direction=Text(item,"direction",path,8);if(direction is not "positive" and not "negative")throw Invalid(path,"Direction must be positive or negative WCS Z.");
        return new(action,handles,expected,Distance:Number(Required(item,"distance",path),path+".distance",.001,1e6),Direction:direction,Name:Text(item,"name",path,128));
    }
}
