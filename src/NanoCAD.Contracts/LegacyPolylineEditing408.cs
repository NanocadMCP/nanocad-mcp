using System.Text.Json;

namespace NanoCAD.Contracts;

public sealed record LegacyPolylineVertex408(string VertexHandle, Point2? Position, double? Bulge, double? StartWidth, double? EndWidth);
public sealed record LegacyPolylineInsert408(string AfterVertexHandle, Point2 Position, double Bulge, double StartWidth, double EndWidth);
public sealed record LegacyPolylineEdit408(string[] Targets, string PolylineToken, bool? Closed,
    LegacyPolylineVertex408[] Vertices, LegacyPolylineInsert408[] Inserts, string[] RemoveVertices)
    : EditSpec("set_legacy_polyline", Targets);

public static partial class ModifyValidation
{
    private static LegacyPolylineEdit408 ParseLegacyPolyline408(JsonElement item, string path, string[] handles)
    {
        Shape(item,path,"operation","handles","polyline_token","closed","vertices","inserts","remove_vertices");
        if(handles.Length!=1)throw Invalid(path+".handles","Choose one actual native Polyline2d root per sparse edit; multiple roots can be edited in the same operations batch.");
        string token=Text(item,"polyline_token",path,64);
        if(token.Length!=64||token.Any(c=>!Uri.IsHexDigit(c)))throw Invalid(path+".polyline_token","Use the complete current 64-character polyline_token from editing detail.");
        bool? closed=null;
        if(item.TryGetProperty("closed",out var closure))
        {
            if(closure.ValueKind is not(JsonValueKind.True or JsonValueKind.False))throw Invalid(path+".closed","Expected boolean.");
            closed=closure.GetBoolean();
        }
        string Handle(JsonElement value,string at)
        {
            if(value.ValueKind!=JsonValueKind.String)throw Invalid(at,"Expected an actual hexadecimal vertex handle.");
            return Validation.NormalizeHandle(value.GetString()!);
        }
        double? Optional(JsonElement value,string key,string at,double minimum=-1e9)=>value.TryGetProperty(key,out var number)?Number(number,at+"."+key,minimum,1e9):null;
        JsonElement[] Items(string key)
        {
            if(!item.TryGetProperty(key,out var array))return [];
            if(array.ValueKind!=JsonValueKind.Array)throw Invalid(path+"."+key,"Expected array.");
            return array.EnumerateArray().ToArray();
        }
        var vertices=new List<LegacyPolylineVertex408>();var changed=new HashSet<string>(StringComparer.Ordinal);
        foreach(var value in Items("vertices"))
        {
            string at=path+".vertices["+vertices.Count+"]";
            Shape(value,at,"vertex_handle","position","bulge","start_width","end_width");
            string handle=Handle(Required(value,"vertex_handle",at),at+".vertex_handle");
            if(!changed.Add(handle))throw Invalid(at+".vertex_handle","Each existing vertex may be changed once within this edit.");
            Point2? position=value.TryGetProperty("position",out _)?Point(value,"position",at):null;
            var edit=new LegacyPolylineVertex408(handle,position,Optional(value,"bulge",at),Optional(value,"start_width",at,0),Optional(value,"end_width",at,0));
            if(edit.Position==null&&edit.Bulge==null&&edit.StartWidth==null&&edit.EndWidth==null)throw Invalid(at,"Supply at least one requested vertex change.");
            vertices.Add(edit);
        }
        var inserts=new List<LegacyPolylineInsert408>();
        foreach(var value in Items("inserts"))
        {
            string at=path+".inserts["+inserts.Count+"]";
            Shape(value,at,"after_vertex_handle","position","bulge","start_width","end_width");
            inserts.Add(new(Handle(Required(value,"after_vertex_handle",at),at+".after_vertex_handle"),Point(value,"position",at),
                Number(Required(value,"bulge",at),at+".bulge"),Number(Required(value,"start_width",at),at+".start_width",0),Number(Required(value,"end_width",at),at+".end_width",0)));
        }
        var removed=new List<string>();var removedSet=new HashSet<string>(StringComparer.Ordinal);
        foreach(var value in Items("remove_vertices"))
        {
            string at=path+".remove_vertices["+removed.Count+"]",handle=Handle(value,at);
            if(!removedSet.Add(handle)||changed.Contains(handle)||inserts.Any(insert=>insert.AfterVertexHandle==handle))
                throw Invalid(at,"A removed vertex must be distinct and cannot also be edited or used as an insertion anchor.");
            removed.Add(handle);
        }
        if(closed==null&&vertices.Count==0&&inserts.Count==0&&removed.Count==0)throw Invalid(path,"Supply a closure, vertex, insertion or removal change.");
        return new(handles,token.ToUpperInvariant(),closed,vertices.ToArray(),inserts.ToArray(),removed.ToArray());
    }
}

public static class LegacyPolylineValidation408
{
    public static void Batch(IEnumerable<EditSpec> edits)
    {
        var seen=new HashSet<string>(StringComparer.Ordinal);
        foreach(var edit in edits.OfType<LegacyPolylineEdit408>())
            if(!seen.Add(edit.Handles.Single()))throw new DomainException("INVALID_OPERATION_ORDER","Use one token-bound sparse edit per Polyline2d in a request. Ordinary placement, properties, copies and erasure may surround it in order; refresh editing detail for a later sparse edit.");
    }
}
