using System.Text.Json;

namespace NanoCAD.Contracts;

public sealed record CopyTransform2d407(string[] Targets, Point2 Center, Point2 Translation, double Rotation, double Scale)
    : EditSpec("copy_transform_2d", Targets);
public sealed record PolarRepeat2d407(string[] Targets, Point2 Center, int Count, double AngleStep, bool RotateItems, Point2? BasePoint)
    : EditSpec("polar_repeat", Targets);
public sealed record RepetitionResult407(int operation_index, string operation, bool associative, int instance_count_including_seed,
    EntityRef[] source_refs, EntityRef[] created_refs);

public static partial class ModifyValidation
{
    private static CopyTransform2d407 ParseCopyTransform407(JsonElement input, string path, string[] handles)
    {
        Shape(input,path,"operation","handles","center","translation","rotation","scale");
        return new(handles,
            input.TryGetProperty("center",out _)?Point(input,"center",path):new(0,0),
            input.TryGetProperty("translation",out _)?Point(input,"translation",path):new(0,0),
            input.TryGetProperty("rotation",out var angle)?Number(angle,path+".rotation"):0,
            input.TryGetProperty("scale",out var factor)?Number(factor,path+".scale",1e-9,1e9):1);
    }
    private static PolarRepeat2d407 ParsePolarRepeat407(JsonElement input,string path,string[] handles)
    {
        Shape(input,path,"operation","handles","center","count","angle_step","rotate_items","base_point");
        var count=Required(input,"count",path);
        if(count.ValueKind!=JsonValueKind.Number||!count.TryGetInt32(out int n)||n<1)
            throw Invalid(path+".count","Expected a positive integer instance count including the original seed.");
        bool rotate=true;
        if(input.TryGetProperty("rotate_items",out var option))
        {
            if(option.ValueKind is not (JsonValueKind.True or JsonValueKind.False))throw Invalid(path+".rotate_items","Expected boolean.");
            rotate=option.GetBoolean();
        }
        Point2? seed=input.TryGetProperty("base_point",out _)?Point(input,"base_point",path):null;
        if(!rotate&&seed is null)throw Invalid(path+".base_point","An explicit group base point is required when orientation is retained.");
        return new(handles,Point(input,"center",path),n,Number(Required(input,"angle_step",path),path+".angle_step"),rotate,seed);
    }
}

public static class CopyTransforms2d407
{
    public static bool Handles(EditSpec edit)=>edit is CopyTransform2d407 or PolarRepeat2d407;
    public static int CopiesPerSource(EditSpec edit)=>edit is PolarRepeat2d407 repeat?repeat.Count-1:1;
    public static IEnumerable<CopyTransform2d407> Expand(EditSpec edit,Action cancel)
    {
        if(edit is CopyTransform2d407 single){cancel();yield return single;yield break;}
        var repeat=(PolarRepeat2d407)edit;
        for(int index=1;index<repeat.Count;index++)
        {
            cancel();double angle=repeat.AngleStep*index;
            if(!double.IsFinite(angle))throw new DomainException("INVALID_GEOMETRY","Repeated rotation must remain finite.");
            if(repeat.RotateItems)yield return new(repeat.Handles,repeat.Center,new(0,0),angle,1);
            else
            {
                var seed=repeat.BasePoint!;double x=seed.X-repeat.Center.X,y=seed.Y-repeat.Center.Y;
                var delta=new Point2(x*Math.Cos(angle)-y*Math.Sin(angle)-x,x*Math.Sin(angle)+y*Math.Cos(angle)-y);
                if(!double.IsFinite(delta.X)||!double.IsFinite(delta.Y))throw new DomainException("INVALID_GEOMETRY","Repeated displacement must remain finite.");
                yield return new(repeat.Handles,seed,delta,0,1);
            }
        }
    }
}
