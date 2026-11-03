using System.Text.Json.Nodes;

namespace NanoCAD.McpServer;

internal static class PolylineVertexSchema431
{
    internal static void Extend(JsonObject operation)
    {
        operation["properties"]!["workplane"]=WorkplaneSchema.Input();
        var item=operation["properties"]!["vertices"]!["items"]!;
        var required=item["dependentRequired"] as JsonObject??new JsonObject();
        foreach(string name in new[]{"start_width","end_width"})
        {
            JsonObject Width()=>new(){["type"]="number",["minimum"]=0,["maximum"]=1e6};
            item["properties"]![name]=Width();item["properties"]!["expected_"+name]=Width();
            required[name]=new JsonArray("expected_"+name);required["expected_"+name]=new JsonArray(name);
        }
        item["dependentRequired"]=required;
        operation["description"]=operation["description"]!.GetValue<string>()+
            " Optional paired expected_start_width/start_width and expected_end_width/end_width change the selected outgoing segment taper without resending the full contour. Omit each pair to preserve it. For width-only edits repeat the unchanged expected_position as position. An open terminal vertex has no outgoing segment. Curvature/width changes require independent sources. Optional workplane makes positions and signed bulges local to an explicit oriented plane, preserving root normal/elevation; convert actual point_wcs into that frame and negate stored bulges if normals oppose. No projection, root transform or UCS change. Framed targets must be independent and may share only appearance edits or Polyline edits in the same frame.";
    }
}
