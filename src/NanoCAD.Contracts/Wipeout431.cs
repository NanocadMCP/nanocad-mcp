using System.Text.Json;

namespace NanoCAD.Contracts;

public sealed record WipeoutSpec431(Point2[] Boundary,string LayerName):EntitySpec("Wipeout",LayerName);
public sealed record WipeoutBoundaryEdit431(string[] Targets,string ExpectedGeometryToken,Point3[] BoundaryWorld):EditSpec("set_wipeout_boundary",Targets);
public static class WipeoutValidation431
{
    static DomainException Bad(string path,string message)=>new("INVALID_ARGUMENT",path+": "+message,false,new{path});
    static JsonElement Required(JsonElement value,string name,string path)=>value.TryGetProperty(name,out var item)?item:throw Bad(path+"."+name,"Required field.");
    static string Text(JsonElement value,string path)=>value.ValueKind==JsonValueKind.String?value.GetString()!:throw Bad(path,"Expected string.");
    static void Shape(JsonElement value,string path,params string[] names)
    {
        if(value.ValueKind!=JsonValueKind.Object)throw Bad(path,"Expected object.");var seen=new HashSet<string>(StringComparer.Ordinal);
        foreach(var p in value.EnumerateObject())if(!seen.Add(p.Name)||!names.Contains(p.Name,StringComparer.Ordinal))throw Bad(path+"."+p.Name,"Unknown or duplicate field.");
    }
    static double Coordinate(JsonElement value,string path)=>value.ValueKind==JsonValueKind.Number&&value.TryGetDouble(out double n)&&double.IsFinite(n)&&Math.Abs(n)<=1e6?n:throw Bad(path,"Expected finite drawing coordinate within +/-1000000 mm.");
    public static WipeoutSpec431 ParseCreate(JsonElement value,string path,string layer)
    {
        Shape(value,path,"type","layer","boundary");var input=Required(value,"boundary",path);
        if(input.ValueKind!=JsonValueKind.Array)throw Bad(path+".boundary","Expected simple polygon boundary; do not repeat its closing vertex.");
        var polygon=input.EnumerateArray().Select((p,i)=>
        {
            string at=path+".boundary["+i+"]";if(p.ValueKind!=JsonValueKind.Array||p.GetArrayLength()!=2)throw Bad(at,"Expected local XY drawing point.");
            return new Point2(Coordinate(p[0],at+"[0]"),Coordinate(p[1],at+"[1]"));
        }).ToArray();SimplePolygonValidation.Validate(polygon,path+".boundary");return new(polygon,layer);
    }
    public static WipeoutBoundaryEdit431 ParseEdit(JsonElement value,string path,string[] handles)
    {
        Shape(value,path,"operation","handles","expected_geometry_token","boundary_world");if(handles.Length!=1)throw Bad(path+".handles","Select one current native Wipeout.");
        string token=Text(Required(value,"expected_geometry_token",path),path+".expected_geometry_token");if(token.Length!=64||token.Any(c=>!Uri.IsHexDigit(c)))throw Bad(path+".expected_geometry_token","Read current Wipeout geometry_token.");
        var input=Required(value,"boundary_world",path);if(input.ValueKind!=JsonValueKind.Array||input.GetArrayLength()<3)throw Bad(path+".boundary_world","Supply at least three ordered WCS points in the existing mask plane.");
        var points=input.EnumerateArray().Select((p,i)=>
        {
            string at=path+".boundary_world["+i+"]";if(p.ValueKind!=JsonValueKind.Array||p.GetArrayLength()!=3)throw Bad(at,"Expected WCS XYZ point.");
            return new Point3(Coordinate(p[0],at+"[0]"),Coordinate(p[1],at+"[1]"),Coordinate(p[2],at+"[2]"));
        }).ToArray();var edit=new WipeoutBoundaryEdit431(handles,token.ToUpperInvariant(),points);SerializedCapacity357.Check(edit);return edit;
    }
    public static void Batch(IReadOnlyCollection<EditSpec> edits)
    {if(edits.Any(e=>e is WipeoutBoundaryEdit431)&&edits.Count!=1)throw new DomainException("INVALID_OPERATION_ORDER","Use one complete token-bound Wipeout boundary edit; refresh its geometry before placement changes.");}
}
