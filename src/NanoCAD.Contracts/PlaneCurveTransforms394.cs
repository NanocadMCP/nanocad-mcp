using System.Text.Json;
namespace NanoCAD.Contracts;

// Optional nulls leave every existing no-frame normalized request unchanged.
public static class PlaneCurveTransforms394
{
    public static SketchPlaneSpec? Parse(JsonElement item)=>item.TryGetProperty("workplane",out var value)?DrawingWorkplane.Parse(value):null;
    public static SketchPlaneSpec? Frame(EditSpec edit)=>edit switch
    {MoveSpec move=>move.Workplane,CopySpec copy=>copy.Workplane,RotateSpec rotate=>rotate.Workplane,
     ScaleSpec scale=>scale.Workplane,Mirror2dSpec mirror=>mirror.Workplane,_=>null};
    public static EditSpec WithoutFrame(EditSpec edit)=>edit switch
    {MoveSpec move=>move with{Workplane=null},CopySpec copy=>copy with{Workplane=null},
     RotateSpec rotate=>rotate with{Workplane=null},ScaleSpec scale=>scale with{Workplane=null},
     Mirror2dSpec mirror=>mirror with{Workplane=null},_=>throw new DomainException("INVALID_ARGUMENT","Expected an ordinary plane transform.")};
}
