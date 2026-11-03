using System.Text.Json;
namespace NanoCAD.Contracts;

public sealed record SetDimensionTextPositionSpec(string[] Targets,Point2 Expected,Point2 Position)
    :EditSpec("set_dimension_text_position",Targets);
public static partial class ModifyValidation
{
    private static SetDimensionTextPositionSpec ParseDimensionTextPosition(JsonElement item,string path,string[] handles)
    {
        Shape(item,path,"operation","handles","expected_text_position","text_position","movement_mode");
        if(handles.Length!=1)throw Invalid(path,"One dimension is required.");
        if(Number(Required(item,"movement_mode",path),path+".movement_mode",2,2)!=2)
            throw Invalid(path,"Explicit movement_mode=2 is required.");
        Point2 Read(string field)
        {
            var p=Point(item,field,path);
            if(Math.Abs(p.X)>1e6||Math.Abs(p.Y)>1e6)throw Invalid(path,"Text position exceeds coordinate bounds.");
            return p;
        }
        return new(handles,Read("expected_text_position"),Read("text_position"));
    }
}
