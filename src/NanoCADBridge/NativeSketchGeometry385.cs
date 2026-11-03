using System.Text.Json;
using System.Text.Json.Nodes;
using NanoCAD.Contracts;

namespace NanoCADBridge;

internal static class NativeSketchGeometry385
{
    internal static SketchConstraintCurve[] Curves(JsonElement geometry,SketchPlaneSpec plane,Action cancel)
    {
        var result=new List<SketchConstraintCurve>();var budget=new ModelInventoryBudget(cancel);
        Point3 P(JsonElement p)=>new(p[0].GetDouble(),p[1].GetDouble(),p[2].GetDouble());
        void Add(string id,JsonElement g)
        {
            cancel();if(g.GetProperty("kind").GetString()=="polyline")
            {NativeSketchMembers385.Need(g.GetProperty("geometry_details_complete").GetBoolean(),"SKETCH_GEOMETRY_UNAVAILABLE","Polyline segment geometry is incomplete.");foreach(var segment in g.GetProperty("segments").EnumerateArray())Add(id+"/boundary/"+segment.GetProperty("index").GetInt32(),segment.GetProperty("geometry"));return;}
            if(g.GetProperty("kind").GetString() is "spline" or "elliptical_arc")
            {var native=NativeSketchCurveGeometry445.Curve(id,g,plane,cancel);budget.Add(native);result.Add(native);return;}
            var curve=g.GetProperty("kind").GetString()=="line"?new SketchConstraintCurve(id,"line",DrawingWorkplane.Local(plane,P(g.GetProperty("start"))),DrawingWorkplane.Local(plane,P(g.GetProperty("end")))):
                ModelPolycurve.FromSketchArc(id,P(g.GetProperty("center")),g.GetProperty("radius").GetDouble(),P(g.GetProperty("normal")),P(g.GetProperty("reference_direction")),g.GetProperty("start_angle").GetDouble(),g.GetProperty("end_angle").GetDouble(),plane);
            budget.Add(curve);result.Add(curve);
        }
        foreach(var row in geometry.GetProperty("elements").EnumerateArray())Add(row.GetProperty("element_id").GetString()!,row.GetProperty("geometry"));return result.ToArray();
    }
    // Normalize intended coordinates only. Identity, segment type/count,
    // outer/hole topology, frame and all non-geometric headers remain actual.
    internal static void Motion(JsonNode? value,Action cancel)
    {
        cancel();if(value is JsonObject obj)
        {
            if(obj["kind"] is JsonValue tag&&tag.TryGetValue<string>(out var kind)&&kind is "spline" or "elliptical_arc")
            {
                // Selected owned solver motion can move control coordinates,
                // endpoint derivatives and ellipse geometric axes/radii/angles.
                // Keep the actual representation, degree, knot vector, weights,
                // fit settings, closure and every identity/header intact. Each
                // coordinate array keeps its real arity and point count.
                void Coordinates(JsonNode? node)
                {cancel();if(node is JsonArray array)for(int i=0;i<array.Count;i++){if(array[i] is JsonArray nested)Coordinates(nested);else array[i]=null;}}
                foreach(string name in new[]{"start","end","center","start_tangent","end_tangent","major_axis","minor_axis"})Coordinates(obj[name]);
                if(kind=="spline"&&obj["definition"] is JsonObject definition)
                {
                    foreach(string name in new[]{"control_points","start_point","end_point"})Coordinates(definition[name]);
                    if(definition["fit_data"] is JsonObject fit)foreach(string name in new[]{"points","start_tangent","end_tangent"})Coordinates(fit[name]);
                }
                if(kind=="elliptical_arc")foreach(string name in new[]{"major_radius","minor_radius","start_angle","end_angle"})if(obj.ContainsKey(name))obj[name]=null;
                return;
            }
            foreach(string key in new[]{"start","end","center","radius","reference_direction","start_angle","end_angle"})if(obj.ContainsKey(key))obj[key]=null;
            foreach(var field in obj.ToArray())if(field.Key!="frame"&&field.Key!="normal"&&field.Key!="plane")Motion(field.Value,cancel);
        }
        else if(value is JsonArray array)foreach(var row in array)Motion(row,cancel);
    }
}
