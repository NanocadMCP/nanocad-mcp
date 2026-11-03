using System.Globalization;
using System.Text.Json;
namespace NanoCAD.Contracts;
public sealed record SolidFaceEditSpec(string Kind,string[] Targets,string TopologyToken,string[] Faces,double Distance,double Angle,Point3? Origin,Point3? Direction,SolidEditTransform? Transform,[property:System.Text.Json.Serialization.JsonIgnore(Condition=System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)] string? PathHandle=null,[property:System.Text.Json.Serialization.JsonIgnore(Condition=System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)] string? PathToken=null):EditSpec(Kind,Targets);
public static partial class ModifyValidation
{
 private static SolidFaceEditSpec ParseFaceEdit(JsonElement item,string path,string[] handles,string kind)
 {
  if(handles.Length!=1)throw Invalid(path,"Select one body with faces from its current topology snapshot.");
  var fields=new List<string>{"operation","handles","topology_token","faces"};
  if(kind is "offset_faces" or "presspull_faces" or "extrude_faces")fields.Add("distance");
  if(kind is "extrude_faces" or "taper_faces")fields.Add("angle");
  if(kind=="taper_faces")fields.AddRange(new[]{"base_point","draft_direction"});
  if(kind=="transform_faces")fields.Add("transform");
  if(kind=="extrude_faces_along_path")fields.AddRange(new[]{"path_handle","path_token"});
  Shape(item,path,fields.ToArray());
  string token=Text(item,"topology_token",path,64);
  if(token.Length!=64||token.Any(c=>!Uri.IsHexDigit(c)||char.IsLower(c)))throw Invalid(path,"Read topology_token from inspect_solid_topology.");
  var a=Required(item,"faces",path);if(a.ValueKind!=JsonValueKind.Array||a.GetArrayLength()==0)throw Invalid(path,"Select at least one face.");
  var faces=a.EnumerateArray().Select(v=>{var t=v.ValueKind==JsonValueKind.String?v.GetString():null;if(!long.TryParse(t,NumberStyles.None,CultureInfo.InvariantCulture,out var n)||n<=0||n.ToString(CultureInfo.InvariantCulture)!=t)throw Invalid(path,"Use native face IDs from the topology snapshot.");return t!;}).ToArray();
  if(faces.Distinct().Count()!=faces.Length)throw Invalid(path,"Duplicate face IDs.");
  Point3 Triple(string name){var v=Required(item,name,path);if(v.ValueKind!=JsonValueKind.Array||v.GetArrayLength()!=3)throw Invalid(path,"Expected XYZ triple.");return new(Number(v[0],path),Number(v[1],path),Number(v[2],path));}
  double distance=fields.Contains("distance")?Number(Required(item,"distance",path),path,-1e6,1e6):0;
  if(fields.Contains("distance")&&Math.Abs(distance)<1e-9)throw Invalid(path,"Distance must be nonzero.");
  double angle=fields.Contains("angle")?Number(Required(item,"angle",path),path,-Math.PI/2+1e-6,Math.PI/2-1e-6):0;
  var direction=kind=="taper_faces"?Triple("draft_direction"):null;
  if(direction!=null&&direction.X*direction.X+direction.Y*direction.Y+direction.Z*direction.Z<1e-18)throw Invalid(path,"Draft direction must be nonzero.");
  SolidEditTransform? transform=null;
  if(kind=="transform_faces"){
   var synthetic=JsonSerializer.SerializeToElement(new {operation="transform_solid",handles,transform=Required(item,"transform",path)});
   transform=ParseSolidEdit(synthetic,path,handles).Transform;
  }
  string? pathHandle=kind=="extrude_faces_along_path"?Validation.NormalizeHandle(Text(item,"path_handle",path,16)):null;
  if(pathHandle!=null&&handles.Contains(pathHandle))throw Invalid(path+".path_handle","The retained path must be a distinct current native curve.");
  string? pathToken=null;
  if(kind=="extrude_faces_along_path"&&item.TryGetProperty("path_token",out _)){
   pathToken=Text(item,"path_token",path,64);
   if(pathToken.Length!=64||pathToken.Any(c=>!Uri.IsHexDigit(c)||char.IsLower(c)))throw Invalid(path+".path_token","Use the exact current uppercase geometry.native_curve.curve_token.");
  }
  return new(kind,handles,token,faces,distance,angle,kind=="taper_faces"?Triple("base_point"):null,direction,transform,pathHandle,pathToken);
 }
}
