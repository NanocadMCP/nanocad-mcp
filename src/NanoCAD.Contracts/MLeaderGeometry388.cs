using System.Security.Cryptography;
using System.Text.Json;

namespace NanoCAD.Contracts;

public sealed record MLeaderVertexEdit388(int Index,Point3 PointWcs);
public sealed record MLeaderGeometryEdit388(string[] Targets,string ExpectedGeometryToken,int LeaderLineIndex,MLeaderVertexEdit388[] Vertices)
    :EditSpec("set_mleader_vertices",Targets);
public sealed record MLeaderLineRead388(int leader_index,int leader_line_index,double[][] vertices_wcs);
public sealed record MLeaderGeometryRead388(string geometry_token,bool complete,MLeaderLineRead388[] lines,JsonElement line_styles,JsonElement layout,string scope);

public static class MLeaderGeometryValidation388
{
    private static DomainException Bad(string path,string message)=>new("INVALID_ARGUMENT",path+": "+message,false,new{path});
    private static JsonElement Required(JsonElement value,string name,string path)=>value.TryGetProperty(name,out var item)?item:throw Bad(path+"."+name,"Required field is missing.");
    private static void Shape(JsonElement value,string path,params string[] allowed)
    {
        if(value.ValueKind!=JsonValueKind.Object)throw Bad(path,"Expected object.");
        var seen=new HashSet<string>(StringComparer.Ordinal);
        foreach(var p in value.EnumerateObject())if(!seen.Add(p.Name)||!allowed.Contains(p.Name,StringComparer.Ordinal))throw Bad(path+"."+p.Name,"Unknown or duplicate field.");
    }
    private static string Text(JsonElement value,string path)=>value.ValueKind==JsonValueKind.String?value.GetString()!:throw Bad(path,"Expected string.");
    private static int Index(JsonElement value,string path)=>value.ValueKind==JsonValueKind.Number&&value.TryGetInt32(out int index)&&index>=0?index:throw Bad(path,"Expected a nonnegative native Int32 index.");
    public static MLeaderGeometryEdit388 Parse(JsonElement value,string path,string[] handles)
    {
        Shape(value,path,"operation","handles","expected_geometry_token","leader_line_index","vertices");
        if(handles.Length!=1)throw Bad(path+".handles","Select one existing exact native MText-content MLeader.");
        if(Text(Required(value,"operation",path),path+".operation")!="set_mleader_vertices")throw Bad(path+".operation","Expected set_mleader_vertices.");
        string token=Text(Required(value,"expected_geometry_token",path),path+".expected_geometry_token");
        if(token.Length!=64||token.Any(c=>!Uri.IsHexDigit(c)))throw Bad(path+".expected_geometry_token","Read current geometry.leader_editing.geometry_token.");
        int line=Index(Required(value,"leader_line_index",path),path+".leader_line_index");
        var values=Required(value,"vertices",path);if(values.ValueKind!=JsonValueKind.Array||values.GetArrayLength()==0)throw Bad(path+".vertices","Supply nonempty distinct actual vertex indices and WCS XY/Z0 points.");
        var vertices=new List<MLeaderVertexEdit388>();var seen=new HashSet<int>();int ordinal=0;
        foreach(var item in values.EnumerateArray())
        {
            string p=path+".vertices["+ordinal+++"]";Shape(item,p,"index","point_wcs");int index=Index(Required(item,"index",p),p+".index");
            if(!seen.Add(index))throw Bad(p+".index","Vertex indices must be distinct.");
            var coordinates=Required(item,"point_wcs",p);if(coordinates.ValueKind!=JsonValueKind.Array||coordinates.GetArrayLength()!=3)throw Bad(p+".point_wcs","Expected WCS XYZ drawing-length point.");
            var point=coordinates.EnumerateArray().Select((v,i)=>v.ValueKind==JsonValueKind.Number&&v.TryGetDouble(out double n)&&double.IsFinite(n)&&Math.Abs(n)<=1e6?n:throw Bad(p+".point_wcs["+i+"]","Expected finite drawing-length coordinate within +/-1000000 mm.")).ToArray();
            if(point[2]!=0)throw Bad(p+".point_wcs[2]","This ordinary leader edit preserves WCS XY/Z0.");
            vertices.Add(new(index,new(point[0],point[1],0)));
        }
        var result=new MLeaderGeometryEdit388(handles,token.ToUpperInvariant(),line,vertices.ToArray());SerializedCapacity357.Check(result);return result;
    }
    public static void Batch(IReadOnlyList<EditSpec> operations)
    {if(operations.Any(e=>e is MLeaderGeometryEdit388)&&operations.Count!=1)throw new DomainException("INVALID_OPERATION_ORDER","Native leader geometry requires its sole token-bound operation; inspect the retained result before subsequent edits.");}
    public static string Token<T>(T complete,Action cancel)
    {
        SerializedCapacity357.Check(complete,cancel:cancel);cancel();var bytes=JsonSerializer.SerializeToUtf8Bytes(complete,Wire.Json);cancel();
        var token=Convert.ToHexString(SHA256.HashData(bytes));cancel();return token;
    }
}
