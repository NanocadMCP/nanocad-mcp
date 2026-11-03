using System.Text.Json;
namespace NanoCAD.Contracts;
public sealed record RegionSpec(string[][] Boundaries,string LayerName):EntitySpec("Region",LayerName);
public static partial class CreateValidation
{
 private static RegionSpec ParseRegion(JsonElement item,string path,string layer){
  Shape(item,path,"type","layer","boundary_handles","subtract_boundaries");
  string[] Loop(JsonElement a){if(a.ValueKind!=JsonValueKind.Array||a.GetArrayLength()==0)throw Invalid(path,"A closed boundary requires curve handles.");var h=a.EnumerateArray().Select(v=>v.ValueKind==JsonValueKind.String?Validation.NormalizeHandle(v.GetString()!):throw Invalid(path,"Expected curve handle.")).ToArray();if(h.Distinct().Count()!=h.Length)throw Invalid(path,"Duplicate curve in boundary.");return h;}
  var result=new List<string[]>{Loop(Required(item,"boundary_handles",path))};var holes=Required(item,"subtract_boundaries",path);if(holes.ValueKind!=JsonValueKind.Array)throw Invalid(path,"Expected subtract boundary list.");foreach(var h in holes.EnumerateArray())result.Add(Loop(h));return new(result.ToArray(),layer);
 }
}
