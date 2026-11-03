using System.Text.Json;
namespace NanoCAD.Contracts;
public sealed record ProfileSolidSpec(string Kind,string ProfileHandle,Point3 Vector,Point3 Origin,double Angle,double StartAngle,string LayerName):EntitySpec("Solid3d",LayerName);
public static partial class CreateValidation
{
 private static EntitySpec ParseProfileSolid(JsonElement item,string path,string layer,string kind){
  var fields=new[]{"type","kind","layer","profile_handle"}.Concat(kind=="extrusion"?new[]{"direction","taper_angle"}:new[]{"axis_origin","axis_direction","angle","start_angle"});Shape(item,path,fields.ToArray());
  var vector=TripleValue(item,kind=="extrusion"?"direction":"axis_direction",path);
  if(Math.Sqrt(vector.X*vector.X+vector.Y*vector.Y+vector.Z*vector.Z)<1e-9)throw Invalid(path,"A nonzero extrusion vector or revolution axis is required.");
  double angle=Number(Required(item,kind=="extrusion"?"taper_angle":"angle",path),path);
  if(kind=="extrusion"?Math.Abs(angle)>=Math.PI/2:angle<=0||angle>2*Math.PI)throw Invalid(path,"Extrusion taper must be between -pi/2 and pi/2; revolution angle must be positive and no greater than 2pi.");
  return new ProfileSolidSpec(kind,CurveHandle(item,"profile_handle",path),vector,kind=="extrusion"?new(0,0,0):TripleValue(item,"axis_origin",path),angle,kind=="extrusion"?0:Number(Required(item,"start_angle",path),path),layer);
 }
}
