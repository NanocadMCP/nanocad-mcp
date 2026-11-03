using System.Text.Json;
namespace NanoCAD.Contracts;
public sealed record Mirror2dSpec(string[] Targets,Point2 Start,Point2 End,bool Copy,
 [property:System.Text.Json.Serialization.JsonIgnore(Condition=System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)] SketchPlaneSpec? Workplane=null):EditSpec("mirror",Targets);
public static partial class ModifyValidation
{
 private static Mirror2dSpec ParseMirror2d(JsonElement item,string path,string[] handles){
  Shape(item,path,"operation","handles","axis_start","axis_end","copy","workplane");var a=Point(item,"axis_start",path);var b=Point(item,"axis_end",path);
  if(Math.Sqrt(Math.Pow(a.X-b.X,2)+Math.Pow(a.Y-b.Y,2))<1e-9)throw Invalid(path,"Mirror axis endpoints must differ.");
  var copy=Required(item,"copy",path);if(copy.ValueKind is not(JsonValueKind.True or JsonValueKind.False))throw Invalid(path,"copy must be boolean.");return new(handles,a,b,copy.GetBoolean(),PlaneCurveTransforms394.Parse(item));
 }
}
