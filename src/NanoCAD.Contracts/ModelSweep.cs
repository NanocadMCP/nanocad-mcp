using System.Text.Json;
namespace NanoCAD.Contracts;
public sealed record ModelSweepSpec(ModelExtrusionSpec Section,string[] PathHandles,string Orientation,double TaperAngle):EntitySpec("Solid3d",Section.Layer);
public static partial class CreateValidation
{
 private static EntitySpec ParseModelSweep(JsonElement item,string path,string layer){
  Shape(item,path,"type","kind","layer","plane","profile","name","editable_profile","path_handles","orientation","taper_angle");
  var value=JsonSerializer.SerializeToElement(new{plane=Required(item,"plane",path),profile=Required(item,"profile",path),name=Text(item,"name",path,64,"Sweep"),editable_profile=item.TryGetProperty("editable_profile",out var e)&&e.GetBoolean(),distance=1,direction="positive"});
  var section=(ModelExtrusionSpec)ParseModelExtrusion(value,path,layer);
  var paths=Required(item,"path_handles",path);if(paths.ValueKind!=JsonValueKind.Array||paths.GetArrayLength()==0)throw Invalid(path,"Provide the ordered connected path curves.");
  var handles=paths.EnumerateArray().Select(p=>p.ValueKind==JsonValueKind.String?Validation.NormalizeHandle(p.GetString()!):throw Invalid(path,"Expected curve handle.")).ToArray();
  if(handles.Distinct().Count()!=handles.Length)throw Invalid(path,"Duplicate path curves.");
  string orientation=Text(item,"orientation",path,32);if(orientation is not "normal_to_path" and not "parallel_to_profile")throw Invalid(path,"Unknown sweep profile orientation.");
  return new ModelSweepSpec(section,handles,orientation,Number(Required(item,"taper_angle",path),path,-Math.PI/2+1e-6,Math.PI/2-1e-6));
 }
}
