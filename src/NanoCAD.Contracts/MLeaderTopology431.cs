using System.Text.Json;

namespace NanoCAD.Contracts;

public sealed record MLeaderTopologyAction431(string Action,int? LeaderIndex,int? LeaderLineIndex,int? VertexIndex,Point3[] Points);
public sealed record MLeaderTopologyEdit431(string[] Targets,string ExpectedGeometryToken,MLeaderTopologyAction431[] Edits)
    :EditSpec("edit_mleader_topology",Targets);

public static class MLeaderTopologyValidation431
{
    public static readonly string[] Actions=["add_line","remove_line","remove_branch","insert_vertex","remove_vertex"];
    static DomainException Bad(string path,string message)=>new("INVALID_ARGUMENT",path+": "+message,false,new{path});
    static JsonElement Required(JsonElement value,string name,string path)=>value.TryGetProperty(name,out var item)?item:throw Bad(path+"."+name,"Required field.");
    static void Shape(JsonElement value,string path,params string[] fields)
    {
        if(value.ValueKind!=JsonValueKind.Object)throw Bad(path,"Expected object.");
        var seen=new HashSet<string>(StringComparer.Ordinal);
        foreach(var p in value.EnumerateObject())if(!seen.Add(p.Name)||!fields.Contains(p.Name,StringComparer.Ordinal))throw Bad(path+"."+p.Name,"Unknown or duplicate field.");
    }
    static string Text(JsonElement value,string path)=>value.ValueKind==JsonValueKind.String?value.GetString()!:throw Bad(path,"Expected string.");
    static int Index(JsonElement value,string path)=>value.ValueKind==JsonValueKind.Number&&value.TryGetInt32(out int index)&&index>=0?index:throw Bad(path,"Expected nonnegative actual native Int32 index.");
    static Point3 Point(JsonElement value,string path)
    {
        if(value.ValueKind!=JsonValueKind.Array||value.GetArrayLength()!=3)throw Bad(path,"Expected WCS XYZ point.");
        var p=value.EnumerateArray().Select((v,i)=>v.ValueKind==JsonValueKind.Number&&v.TryGetDouble(out double n)&&double.IsFinite(n)&&Math.Abs(n)<=1e6?n:throw Bad(path+"["+i+"]","Expected finite drawing coordinate within +/-1000000 mm.")).ToArray();
        if(p[2]!=0)throw Bad(path,"This ordinary leader route uses WCS XY/Z0.");return new(p[0],p[1],p[2]);
    }
    public static MLeaderTopologyEdit431 Parse(JsonElement value,string path,string[] handles)
    {
        Shape(value,path,"operation","handles","expected_geometry_token","edits");
        if(handles.Length!=1)throw Bad(path+".handles","Select one current ordinary native MLeader.");
        if(Text(Required(value,"operation",path),path+".operation")!="edit_mleader_topology")throw Bad(path+".operation","Expected edit_mleader_topology.");
        string token=Text(Required(value,"expected_geometry_token",path),path+".expected_geometry_token");
        if(token.Length!=64||token.Any(c=>!Uri.IsHexDigit(c)))throw Bad(path+".expected_geometry_token","Read complete geometry.leader_editing.geometry_token.");
        var edits=Required(value,"edits",path);if(edits.ValueKind!=JsonValueKind.Array||edits.GetArrayLength()==0)throw Bad(path+".edits","Supply ordered topology edits.");
        var result=new List<MLeaderTopologyAction431>();int ordinal=0;
        foreach(var edit in edits.EnumerateArray())
        {
            string at=path+".edits["+ordinal+++ "]";string action=Text(Required(edit,"action",at),at+".action");
            int? branch=null,line=null,vertex=null;Point3[] points=[];
            switch(action)
            {
                case "add_line":
                    Shape(edit,at,"action","leader_index","vertices_wcs");
                    if(edit.TryGetProperty("leader_index",out var b)&&b.ValueKind!=JsonValueKind.Null)branch=Index(b,at+".leader_index");
                    var vertices=Required(edit,"vertices_wcs",at);if(vertices.ValueKind!=JsonValueKind.Array||vertices.GetArrayLength()<2)throw Bad(at+".vertices_wcs","A line needs at least two ordered native vertices, arrow first and landing last.");
                    points=vertices.EnumerateArray().Select((p,i)=>Point(p,at+".vertices_wcs["+i+"]")).ToArray();break;
                case "remove_branch":
                    Shape(edit,at,"action","leader_index");branch=Index(Required(edit,"leader_index",at),at+".leader_index");break;
                case "remove_line":
                    Shape(edit,at,"action","leader_line_index");line=Index(Required(edit,"leader_line_index",at),at+".leader_line_index");break;
                case "insert_vertex":
                case "remove_vertex":
                    Shape(edit,at,action=="insert_vertex"?["action","leader_line_index","vertex_index","point_wcs"]:["action","leader_line_index","vertex_index"]);
                    line=Index(Required(edit,"leader_line_index",at),at+".leader_line_index");vertex=Index(Required(edit,"vertex_index",at),at+".vertex_index");
                    if(action=="insert_vertex")points=[Point(Required(edit,"point_wcs",at),at+".point_wcs")];break;
                default:throw Bad(at+".action","Use a published native topology action.");
            }
            for(int i=1;i<points.Length;i++)if(points[i]==points[i-1])throw Bad(at,"Adjacent line vertices cannot coincide.");
            result.Add(new(action,branch,line,vertex,points));
        }
        var parsed=new MLeaderTopologyEdit431(handles,token.ToUpperInvariant(),result.ToArray());SerializedCapacity357.Check(parsed);return parsed;
    }
    public static void Batch(IReadOnlyCollection<EditSpec> operations)
    {if(operations.Any(e=>e is MLeaderTopologyEdit431)&&operations.Count!=1)throw new DomainException("INVALID_OPERATION_ORDER","Use the complete token-bound leader topology group as the sole operation; ordered edits use current native indices.");}
}
