using System.Text.Json;
namespace NanoCAD.Contracts;
public sealed record ModelLoftSpec(ModelExtrusionSpec[] Sections,string? PathHandle,string[][] GuideHandles):EntitySpec("Solid3d",Sections[0].Layer);
public static partial class CreateValidation
{
 private static EntitySpec ParseModelLoft(JsonElement item,string path,string layer){
  Shape(item,path,"type","kind","layer","sections","path_handle","guide_handles");
  var a=Required(item,"sections",path);if(a.ValueKind!=JsonValueKind.Array||a.GetArrayLength()<2)throw Invalid(path,"Loft needs at least two ordered section profiles.");
  var sections=a.EnumerateArray().Select((s,i)=>{
   Shape(s,path,"plane","profile","name","editable_profile");
   var v=JsonSerializer.SerializeToElement(new{plane=Required(s,"plane",path),profile=Required(s,"profile",path),name=Text(s,"name",path,64,"Section"+i),editable_profile=s.TryGetProperty("editable_profile",out var e)&&e.GetBoolean(),distance=1,direction="positive"});
   return (ModelExtrusionSpec)ParseModelExtrusion(v,path,layer);
  }).ToArray();
  string? trajectory=item.TryGetProperty("path_handle",out var p)&&p.ValueKind!=JsonValueKind.Null?CurveHandle(item,"path_handle",path):null;
  var rails=Required(item,"guide_handles",path);if(rails.ValueKind!=JsonValueKind.Array)throw Invalid(path,"Provide an array of ordered guide chains.");
  var guides=rails.EnumerateArray().Select(g=>{if(g.ValueKind!=JsonValueKind.Array||g.GetArrayLength()==0)throw Invalid(path,"A guide chain needs at least one curve.");return g.EnumerateArray().Select(h=>h.ValueKind==JsonValueKind.String?Validation.NormalizeHandle(h.GetString()!):throw Invalid(path,"Expected handle.")).ToArray();}).ToArray();
  if(trajectory!=null&&guides.Length>0)throw Invalid(path,"Choose a center path or guide rails.");
  return new ModelLoftSpec(sections,trajectory,guides);
 }
}
