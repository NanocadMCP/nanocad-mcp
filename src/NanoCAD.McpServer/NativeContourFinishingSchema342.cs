using System.Text.Json.Nodes;

namespace NanoCAD.McpServer;

internal static class NativeContourFinishingSchema342
{
    private static JsonObject Point()=>new(){["type"]="array",["minItems"]=3,["maxItems"]=3,["items"]=new JsonObject{["type"]="number",["minimum"]=-1e6,["maximum"]=1e6}};
    // Mutate the existing ONE branch, retaining its operation const and exact
    // old schema as the conditional legacy arm (there are no duplicate variants).
    internal static void Extend(JsonObject existing,string kind)
    {
        var legacy=(JsonObject)existing.DeepClone();var oldDescription=legacy["description"]?.GetValue<string>()??"";
        var nativeProperties=new JsonObject{["operation"]=new JsonObject{["const"]=kind},["handles"]=new JsonObject{["type"]="array",["minItems"]=1,["maxItems"]=1,["items"]=new JsonObject{["type"]="string",["pattern"]="^[0-9A-Fa-f]{1,16}$"}},
            ["mode"]=new JsonObject{["const"]="native"},["expected_curve_token"]=new JsonObject{["type"]="string",["pattern"]="^[0-9A-Fa-f]{64}$",["description"]="Current geometry.native_curve.curve_token, read together with original document/revision/context. This binds complete native shape, not a copied metadata graph."}};
        if(kind=="trim_curve")
        {nativeProperties["boundary_handles"]=new JsonObject{["type"]="array",["minItems"]=1,["uniqueItems"]=true,["items"]=new JsonObject{["type"]="string",["pattern"]="^[0-9A-Fa-f]{1,16}$"}};nativeProperties["pick_point_wcs"]=Point();}
        else if(kind=="split_curve")
        {nativeProperties["cut_parameters"]=new JsonObject{["type"]="array",["minItems"]=1,["items"]=new JsonObject{["type"]="number"},["description"]="Strictly increasing actual native parameters from the inspected source interval, not arc lengths or normalized fractions. Open curves require interior cuts; closed curves may include the start seam and never end_parameter."};nativeProperties["erase_sources"]=new JsonObject{["type"]="boolean"};}
        else {nativeProperties["end"]=new JsonObject{["enum"]=new JsonArray("start","end")};nativeProperties["target_point_wcs"]=Point();}
        var required=new JsonArray(nativeProperties.Select(p=>(JsonNode?)JsonValue.Create(p.Key)).ToArray());
        nativeProperties["workplane"]=WorkplaneSchema.Input();
        nativeProperties["workplane"]!["description"]="Optional declared physical plane. All points remain WCS XYZ and native cuts remain actual parameters, never local XY coordinates. Requires source, cutting boundaries and results to lie in that plane. Circular/curved sources otherwise use their actual native plane. A Line has no unique plane; without this field native finite intersections and collinear endpoint extension operate on its actual WCS locus.";
        var native=new JsonObject{["type"]="object",["additionalProperties"]=false,["properties"]=nativeProperties,["required"]=required};
        existing.Clear();existing["type"]="object";
        // Outer properties keep operation discoverable to variant guidance;
        // allowed/required fields remain strict in the selected schema arm.
        var union=new JsonObject();foreach(var p in legacy["properties"]!.AsObject())union[p.Key]=p.Value?.DeepClone();foreach(var p in nativeProperties)union[p.Key]=p.Value?.DeepClone();existing["properties"]=union;
        existing["oneOf"]=new JsonArray(legacy,native);existing["description"]=oldDescription+" Explicit mode=native uses exact Line/Arc/Circle/Polyline/Ellipse/Spline in Model or active ordinary Paper using a fresh complete native shape token. Points are WCS XYZ, including shifted/tilted planes; optional workplane imposes a declared coplanar frame without projection or coordinate conversion. Circular/curved sources use their actual native plane, and a Line has no unique plane. Closed curves have no extendable endpoints. "+(kind switch{
            "trim_curve"=>"Native finite Line/Arc/Circle/Polyline/Ellipse/Spline boundaries remain read-only and must lie in the selected declared or actual curved-source plane when one exists. A Line without workplane uses actual finite WCS intersections without a unique plane. IntersectWith and GetSplitCurves produce actual pieces; remove exactly one piece containing pick_point_wcs and append every other piece. Pick must lie on the source away from cuts; ambiguous coincident/self-intersection matches refuse. Replaces source explicitly (source_erased=true).",
            "split_curve"=>"Native GetSplitCurves consumes cut_parameters and returns every independent real piece, without guessed count/order/type. erase_sources=false retains exact source; true replaces it explicitly.",
            _=>"Native Extend(start/end,target_point_wcs) changes the selected open endpoint in place. Target lies outside the finite source locus and in its selected declared or actual curved-source plane when one exists. A Line target must lie on its original infinite support. Retains source handle/opposite endpoint/display/observable metadata; verifies actual requested endpoint and sampled original native locus. Native feasibility is kernel-specific; no arbitrary extrapolation/tessellation is constructed."})+
            " Source replacement/mutation requires actual independent ownership (no fields/reactors/native model consumers); replacement also refuses application XData/owned dictionaries rather than silently discarding them. Read-only split-copy may retain richer source data with specific observation warnings. All returned roots retain source display and have independent clean dependency graphs; complete refs are returned in contour_results/created. No artificial curve/vertex/control/result ceiling or analytic bbox/area oracle. One native finishing operation per request; actual bytes/cancellation bound inventories. Native rollback/Undo is not guaranteed; meaningful final visual review is required.";
    }
}
