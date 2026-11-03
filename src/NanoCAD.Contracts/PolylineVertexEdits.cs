using System.Text.Json;

namespace NanoCAD.Contracts;

public sealed record PolylineVertexEdit(int Index, Point2 ExpectedPosition, Point2 Position,
    [property:System.Text.Json.Serialization.JsonIgnore(Condition=System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)] double? ExpectedBulge=null,
    [property:System.Text.Json.Serialization.JsonIgnore(Condition=System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)] double? Bulge=null,
    [property:System.Text.Json.Serialization.JsonIgnore(Condition=System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)] double? ExpectedStartWidth=null,
    [property:System.Text.Json.Serialization.JsonIgnore(Condition=System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)] double? StartWidth=null,
    [property:System.Text.Json.Serialization.JsonIgnore(Condition=System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)] double? ExpectedEndWidth=null,
    [property:System.Text.Json.Serialization.JsonIgnore(Condition=System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)] double? EndWidth=null);
public sealed record PolylineVerticesSpec(string[] Targets, PolylineVertexEdit[] Vertices,
    [property:System.Text.Json.Serialization.JsonIgnore(Condition=System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)] SketchPlaneSpec? Workplane=null) : EditSpec("set_polyline_vertices", Targets);

public static partial class ModifyValidation
{
    private static PolylineVerticesSpec ParsePolylineVertices(JsonElement item, string path, string[] handles)
    {
        Shape(item, path, "operation", "handles", "vertices", "workplane");
        if (handles.Length != 1) throw Invalid(path + ".handles", "Vertex edits require one native Polyline.");
        var vertices = Required(item, "vertices", path);
        if (vertices.ValueKind != JsonValueKind.Array || vertices.GetArrayLength() < 1)
            throw Invalid(path + ".vertices", "Choose distinct vertices.");
        var edits = new List<PolylineVertexEdit>(); var seen = new HashSet<int>();
        foreach (var vertex in vertices.EnumerateArray())
        {
            string at = path + ".vertices[" + edits.Count + "]";
            Shape(vertex, at, "index", "expected_position", "position", "expected_bulge", "bulge", "expected_start_width", "start_width", "expected_end_width", "end_width");
            var index = Required(vertex, "index", at);
            if (index.ValueKind != JsonValueKind.Number || !index.TryGetInt32(out int n) || n < 0 || !seen.Add(n))
                throw Invalid(at + ".index", "Use distinct nonnegative Int32 indices within the source vertex count.");
            bool hasExpected=vertex.TryGetProperty("expected_bulge",out var old),hasBulge=vertex.TryGetProperty("bulge",out var value);
            if(hasExpected!=hasBulge)throw Invalid(at,"expected_bulge and bulge must be supplied together.");
            (double? Expected,double? Value) Width(string name)
            {
                bool hasBefore=vertex.TryGetProperty("expected_"+name,out var before),hasAfter=vertex.TryGetProperty(name,out var after);
                if(hasBefore!=hasAfter)throw Invalid(at,"expected_"+name+" and "+name+" must be supplied together.");
                return hasBefore?(Number(before,at+".expected_"+name,0,1e6),Number(after,at+"."+name,0,1e6)):(null,null);
            }
            var start=Width("start_width");var end=Width("end_width");
            edits.Add(new(n, Point(vertex, "expected_position", at), Point(vertex, "position", at),
                hasExpected?Number(old,at+".expected_bulge",-1e6,1e6):null,hasBulge?Number(value,at+".bulge",-1e6,1e6):null,
                start.Expected,start.Value,end.Expected,end.Value));
        }
        var frame=item.TryGetProperty("workplane",out var workplane)?DrawingWorkplane.Parse(workplane):null;
        return new(handles, edits.OrderBy(edit => edit.Index).ToArray(),frame);
    }
}
