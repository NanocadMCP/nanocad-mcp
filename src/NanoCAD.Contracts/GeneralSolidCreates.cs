using System.Text.Json;
namespace NanoCAD.Contracts;
public sealed record CurveSolidSpec(string Kind,string[] Profiles,string? PathHandle,string[] Guides,Point3? BasePoint,bool Align,bool Bank,double Twist,double Scale,bool Ruled,string LayerName):EntitySpec("Solid3d",LayerName)
{
 // An omitted option preserves the legacy sweep/loft operation fingerprint.
 [System.Text.Json.Serialization.JsonIgnore(Condition=System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
 public bool? Closed { get; init; }
}
public sealed record HelixSpec(Point3 Origin,Point3 Axis,Point3 Radial,double BaseRadius,double TopRadius,double Height,double Turns,bool RightHanded,string LayerName):EntitySpec("Helix",LayerName);
public static partial class CreateValidation
{
 private static string CurveHandle(JsonElement o,string n,string p)=>Validation.NormalizeHandle(Text(o,n,p,16));
 private static EntitySpec ParseCurveSolid(JsonElement item,string path,string layer,string kind)
 {
  string[] Handles(string name,int min){var a=Required(item,name,path);if(a.ValueKind!=JsonValueKind.Array||a.GetArrayLength()<min)throw Invalid(path,"Insufficient profile/guide handles.");var r=a.EnumerateArray().Select(v=>v.ValueKind==JsonValueKind.String?Validation.NormalizeHandle(v.GetString()!):throw Invalid(path,"Expected handle string.")).ToArray();if(r.Distinct().Count()!=r.Length)throw Invalid(path,"Duplicate source handles.");return r;}
  if(kind=="sweep"){
   Shape(item,path,"type","kind","layer","profile_handle","path_handle","base_point","align","bank","twist_angle","scale_factor");
   bool Flag(string n){var v=Required(item,n,path);if(v.ValueKind is not(JsonValueKind.True or JsonValueKind.False))throw Invalid(path,"Expected boolean.");return v.GetBoolean();}
   return new CurveSolidSpec(kind,new[]{CurveHandle(item,"profile_handle",path)},CurveHandle(item,"path_handle",path),Array.Empty<string>(),TripleValue(item,"base_point",path),Flag("align"),Flag("bank"),Number(Required(item,"twist_angle",path),path),Number(Required(item,"scale_factor",path),path,1e-9,1e9),false,layer);
  }
  Shape(item,path,"type","kind","layer","section_handles","guide_handles","path_handle","interpolation","closed");
  string interpolation=Text(item,"interpolation",path,16);if(interpolation is not "smooth" and not "ruled")throw Invalid(path,"Expected smooth or ruled.");
  var profiles=Handles("section_handles",2);var guides=Handles("guide_handles",0);
  string? trajectory=item.TryGetProperty("path_handle",out var v)&&v.ValueKind!=JsonValueKind.Null?CurveHandle(item,"path_handle",path):null;
  if(trajectory!=null&&guides.Length>0)throw Invalid(path,"Choose either guides or a path for the native loft.");
  bool? closed=null;
  if(item.TryGetProperty("closed",out var closedValue))
  {
   if(closedValue.ValueKind is not(JsonValueKind.True or JsonValueKind.False))throw Invalid(path+".closed","Expected boolean.");
   closed=closedValue.GetBoolean();
  }
  return new CurveSolidSpec(kind,profiles,trajectory,guides,null,false,false,0,1,interpolation=="ruled",layer){Closed=closed};
 }
 private static EntitySpec ParseHelix(JsonElement item,string path,string layer){
  Shape(item,path,"type","layer","axis_origin","axis_direction","radial_direction","base_radius","top_radius","height","turns","right_handed");
  var axis=TripleValue(item,"axis_direction",path);var radial=TripleValue(item,"radial_direction",path);
  double length(Point3 p)=>Math.Sqrt(p.X*p.X+p.Y*p.Y+p.Z*p.Z);
  double a=length(axis),r=length(radial);
  if(a<1e-9||r<1e-9||Math.Abs(axis.X*radial.X+axis.Y*radial.Y+axis.Z*radial.Z)>1e-8*a*r)throw Invalid(path,"Axis and radial direction must be nonzero and perpendicular.");
  var handed=Required(item,"right_handed",path);if(handed.ValueKind is not(JsonValueKind.True or JsonValueKind.False))throw Invalid(path,"Expected right_handed boolean.");
  return new HelixSpec(TripleValue(item,"axis_origin",path),axis,radial,Number(Required(item,"base_radius",path),path,1e-9,1e6),Number(Required(item,"top_radius",path),path,0,1e6),Number(Required(item,"height",path),path,1e-9,1e6),Number(Required(item,"turns",path),path,1e-9,double.MaxValue),handed.GetBoolean(),layer);
 }
}
