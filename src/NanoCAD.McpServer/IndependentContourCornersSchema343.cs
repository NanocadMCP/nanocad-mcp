using System.Text.Json.Nodes;

namespace NanoCAD.McpServer;

internal static class IndependentContourCornersSchema343
{
    private static JsonObject Length()=>new(){["type"]="number",["exclusiveMinimum"]=0,["maximum"]=1e6};
    private static JsonObject Pair(JsonObject item)=>new(){["type"]="array",["minItems"]=2,["maxItems"]=2,["items"]=item};
    internal static JsonObject[] Operations()=>new[]{Operation("fillet_curves"),Operation("chamfer_curves")};
    private static JsonObject Operation(string kind)
    {
        var properties=new JsonObject{
            ["operation"]=new JsonObject{["const"]=kind},
            ["handles"]=Pair(new(){["type"]="string",["pattern"]="^[0-9A-Fa-f]{1,16}$"}),
            ["expected_curve_tokens"]=Pair(new(){["type"]="string",["pattern"]="^[0-9A-Fa-f]{64}$"}),
            ["ends"]=Pair(new(){["enum"]=new JsonArray("start","end")}),["trim_sources"]=new JsonObject{["type"]="boolean"},
            ["corner_mode"]=new JsonObject{["enum"]=new JsonArray("common_endpoint","supporting_line_intersection"),["default"]="common_endpoint"}};
        properties["handles"]!["uniqueItems"]=true;
        properties["expected_curve_tokens"]!["description"]="Two current geometry.native_curve.curve_token values in the exact handles order, read with current document/revision.";
        properties["ends"]!["description"]="The selected stored endpoint of each source. Opposite endpoints remain. With supporting_line_intersection select the end nearest the native supporting corner; the corner must lie at or beyond that end away from the retained opposite end, never inside the source.";
        properties["corner_mode"]!["description"]="Omitted/common_endpoint preserves the common-endpoint Line/Arc path. supporting_line_intersection uses two exact Lines and their unique native 3D supporting-line intersection, admitting separated selected ends. Parallel, collinear, skew and interior-crossing selections are not projected or guessed. Tangencies and chamfer attachments must still lie strictly inside both original finite Lines; no source extension is performed.";
        properties["trim_sources"]!["description"]="true trims both selected ends in place, retaining their handles; false retains complete sources and creates only an independent connector. Neither choice erases a source.";
        if(kind=="fillet_curves")properties["radius"]=Length();else{properties["distances"]=Pair(Length());properties["distances"]!["description"]="Positive drawing lengths in handles order. common_endpoint measures along each native Line/Arc from its original selected endpoint. supporting_line_intersection measures each chamfer leg from the actual supporting intersection toward the retained opposite endpoint: distance must exceed the original endpoint-to-corner gap and be shorter than the corner-to-opposite length. Attachments remain inside the original finite Lines. These are not parameter increments, sampled chords or offsets.";}
        return new(){["type"]="object",["additionalProperties"]=false,["properties"]=properties,["required"]=new JsonArray(properties.Where(p=>p.Key!="corner_mode").Select(p=>(JsonNode?)JsonValue.Create(p.Key)).ToArray()),
            ["description"]="Connect two exact ordinary zero-thickness native Line/Arc sources in Model or active ordinary Paper at a common selected endpoint, or explicitly choose supporting_line_intersection for separated coplanar Line ends. "+
            (kind=="fillet_curves"?"For two Lines the exact retained-ray construction creates a native CircularArc3d/Arc of the requested radius using the common or supporting corner. Line/Arc and Arc/Arc use the native CircularArc3d tangent solver with finite interior source seeds at the common corner. Each attachment must lie inside its original finite source and have native tangency. A radius whose tangency falls in a gap or consumes a source is refused; source-specific kernel feasibility remains.":"Native distance-to-parameter evaluation locates the finite source attachments, then creates a native Line between them. For supporting_line_intersection each requested distance is measured from the actual intersection and the source gap is deducted before finite-source evaluation.")+ 
            " The connector inherits first-source stored display/layer only, with no copied dependency graph. In-place trim retains the native source shape between its opposite endpoint and actual attachment. Source handles/opposite endpoints/native frames/appearance/observed metadata remain; fields/reactors/model consumers require their owning route for mutation. Supported owned dictionaries/Xrecords are preserved; other owned content has an explicit owning-route/observation limitation. Read-only source retention is distinct from mutation. Complete pair provenance and actual attachments/setbacks are returned in contour_corner_results. One corner operation per request. No automatic associations are added; started native recovery/Undo is not guaranteed. Review the final engineering shape."};
    }
    internal static JsonObject ReadResult()
    {
        JsonObject Ref()=>new(){["$ref"]="#/$defs/ref"};
        var properties=new JsonObject{["operation_index"]=new JsonObject{["type"]="integer",["minimum"]=0},["operation"]=new JsonObject{["enum"]=new JsonArray("fillet_curves","chamfer_curves")},
            ["sources"]=Pair(Ref()),["ends"]=Pair(new(){["enum"]=new JsonArray("start","end")}),["connector"]=Ref(),["sources_trimmed"]=new JsonObject{["type"]="boolean"},
            ["attachment_points"]=Pair(new(){["type"]="array",["minItems"]=3,["maxItems"]=3,["items"]=new JsonObject{["type"]="number"}}),
            ["attachment_parameters"]=Pair(new(){["type"]="number",["description"]="Native original-source parameter, not length."}),
            ["setback_lengths"]=Pair(new(){["type"]="number",["exclusiveMinimum"]=0,["description"]="Observed original-source native arc-length removal from the selected stored endpoint. For supporting_line_intersection chamfer, this plus source_corner_gaps equals the requested intersection-based distance."}),["radius"]=new JsonObject{["type"]=new JsonArray("number","null")},
            ["corner_mode"]=new JsonObject{["enum"]=new JsonArray("common_endpoint","supporting_line_intersection")},
            ["corner_point"]=new JsonObject{["type"]="array",["minItems"]=3,["maxItems"]=3,["items"]=new JsonObject{["type"]="number"},["description"]="Actual WCS common endpoint or unique native supporting-line intersection, in drawing millimeters."},
            ["source_corner_gaps"]=Pair(new(){["type"]="number",["minimum"]=0,["description"]="Distance from each ORIGINAL selected source endpoint to corner_point, observed before any trim."})};
        return new(){["type"]="object",["additionalProperties"]=false,["properties"]=properties,["required"]=new JsonArray(properties.Where(p=>p.Key is not ("corner_mode" or "corner_point" or "source_corner_gaps")).Select(p=>(JsonNode?)JsonValue.Create(p.Key)).ToArray())};
    }
}
