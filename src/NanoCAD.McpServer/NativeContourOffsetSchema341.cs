using System.Text.Json.Nodes;

namespace NanoCAD.McpServer;

internal static class NativeContourOffsetSchema341
{
    // Extend the existing sole offset_curve branch so variant guidance remains
    // unambiguous and all accepted legacy sides keep their parser/behavior.
    internal static void Extend(JsonObject offset)
    {
        var properties=offset["properties"]!.AsObject();
        properties["mode"]=new JsonObject{["enum"]=new JsonArray("native","legacy")};
        properties["signed_distance"]=new JsonObject{["type"]="number",["minimum"]=-1e6,["maximum"]=1e6,
            ["not"]=new JsonObject{["const"]=0},["description"]="Signed native kernel distance in drawing millimeters. Supply this field or side and distance."};
        properties["workplane"]=WorkplaneSchema.Input();
        properties["workplane"]!["description"]="Physical source/offset plane for native_positive/native_negative only, required for a Line with no unique native plane. Existing source geometry must already lie in it. The declared oriented normal is passed to native GetOffsetCurvesGivenPlaneNormal; no point is transformed or projected. Circular/curved sources may omit this field to use their original native offset behavior.";
        properties["side"]!.AsObject()["enum"]=new JsonArray("left","right","inward","outward","native_positive","native_negative");
        properties["side"]!.AsObject()["description"]="Legacy left/right uses a directed Line; inward/outward uses accepted circular/closed convex straight geometry. Explicit native_positive/native_negative passes the corresponding signed magnitude to native Curve.GetOffsetCurves, or GetOffsetCurvesGivenPlaneNormal when workplane is supplied, for Line/Arc/Circle/planar Polyline (open/concave/bulged)/Ellipse/Spline. A Line requires workplane to select its physical offset plane. Native sign is family/orientation dependent; no universal left/inward meaning is promised.";
        var distance=properties["distance"]!.AsObject();distance.Remove("minimum");distance["exclusiveMinimum"]=0;
        offset["allOf"]=new JsonArray(new JsonObject{
            ["if"]=new JsonObject{["properties"]=new JsonObject{["side"]=new JsonObject{["enum"]=new JsonArray("left","right","inward","outward")}},["required"]=new JsonArray("side")},
            ["then"]=new JsonObject{["properties"]=new JsonObject{["distance"]=new JsonObject{["minimum"]=.001}},["not"]=new JsonObject{["required"]=new JsonArray("workplane")}}});
        offset["required"]=new JsonArray("operation","handles");
        offset["oneOf"]=new JsonArray(
            new JsonObject
            {
                ["required"]=new JsonArray("signed_distance"),["properties"]=new JsonObject{["mode"]=new JsonObject{["const"]="native"}},
                ["not"]=new JsonObject{["anyOf"]=new JsonArray(new JsonObject{["required"]=new JsonArray("side")},new JsonObject{["required"]=new JsonArray("distance")})}
            },
            new JsonObject
            {
                ["required"]=new JsonArray("side","distance"),["not"]=new JsonObject{["required"]=new JsonArray("signed_distance")},
                ["if"]=new JsonObject{["properties"]=new JsonObject{["side"]=new JsonObject{["enum"]=new JsonArray("native_positive","native_negative")}}},
                ["then"]=new JsonObject{["properties"]=new JsonObject{["mode"]=new JsonObject{["const"]="native"}}},
                ["else"]=new JsonObject{["properties"]=new JsonObject{["mode"]=new JsonObject{["const"]="legacy"}}}
            });
        offset["description"]="Create independent offsets while retaining the exact source. Existing legacy sides retain their accepted scope. Explicit native_positive/native_negative supports exact native zero-thickness Line/Arc/Circle, zero-width/zero-thickness planar Polyline with open, concave or bulged segments, Ellipse and Spline in Model or active ordinary Paper, including shifted/tilted planes. Optional workplane chooses an oriented physical plane and is required for a Line; source geometry must already lie in it. No projection or coordinate conversion is performed. Without workplane the existing native offset method and source plane remain. The resident kernel runs once and all valid returned native curve roots are included in created[] and contour_results; result count/type is not predicted. Native outputs can be Line/Arc/Circle/Polyline/Ellipse/Spline and are independent display copies without inherited fields/reactors/XData/owned metadata. Source observed geometry/display/identity/metadata remain checked; unclassified read-only owned data, referenced field internals and original spline fit constraints may have explicit deep-verification limitations. Generated roots can be queried, visually framed and erased. Public inspection reports complete finite native Spline NURBS/fit observations within actual native getter and serialized response limits. Legacy create/transform/replacement/analysis admission remains separate; a read limitation does not invalidate a native factory result. No manual tessellation, source replacement, analytic area/bbox oracle or arbitrary vertex/result count ceiling is used for this native route. One native-offset operation per request; distance is a positive drawing length and sign is selected explicitly. Empty or infeasible native results report a scoped failure, not family-wide unavailability. Actual visual review is required; native rollback/Undo is not guaranteed.";
    }
}
