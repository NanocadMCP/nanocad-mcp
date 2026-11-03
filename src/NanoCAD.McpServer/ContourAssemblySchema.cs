using System.Text.Json.Nodes;
namespace NanoCAD.McpServer;

internal static class ContourAssemblySchema
{
    private static JsonObject Obj(JsonObject fields)=>new(){["type"]="object",["additionalProperties"]=false,["required"]=new JsonArray(fields.Select(p=>(JsonNode?)JsonValue.Create(p.Key)).ToArray()),["properties"]=fields};
    private static JsonObject Num(double min,double max)=>new(){["type"]="number",["minimum"]=min,["maximum"]=max};
    private static JsonObject Arr(JsonNode items,int min,int? max=null)
    {var result=new JsonObject{["type"]="array",["minItems"]=min,["items"]=items};if(max.HasValue)result["maxItems"]=max.Value;return result;}
    private static JsonObject Point()=>Arr(Num(-1e6,1e6),2,2);
    private static JsonObject Flag()=>new(){["type"]="boolean"};
    internal static JsonObject Operation(string kind)
    {
        var line=Obj(new(){["type"]=new JsonObject{["const"]="Line"},["start"]=Point(),["end"]=Point()});
        var arc=Obj(new(){["type"]=new JsonObject{["const"]="Arc"},["center"]=Point(),["radius"]=Num(.001,1e6),["start_angle"]=Num(-2*Math.PI,2*Math.PI),["sweep_angle"]=Num(1e-7,2*Math.PI-1e-5)});
        var poly=Obj(new(){["type"]=new JsonObject{["const"]="Polyline"},["closed"]=Flag(),["vertices"]=Arr(Obj(new(){["position"]=Point(),["bulge"]=Num(-1e6,1e6),["start_width"]=Num(0,1e6),["end_width"]=Num(0,1e6)}),2)});
        var fields=new JsonObject{["operation"]=new JsonObject{["const"]=kind},["handles"]=Arr(new JsonObject{["type"]="string",["pattern"]="^[0-9A-Fa-f]{1,16}$"},kind=="join_curves"?2:1,kind=="join_curves"?null:1),
            ["expected_sources"]=Arr(new JsonObject{["oneOf"]=new JsonArray(line,arc,poly)},kind=="join_curves"?2:1,kind=="join_curves"?null:1),["erase_sources"]=Flag()};
        if(kind=="join_curves"){fields["closed"]=Flag();fields["reverse"]=Arr(Flag(),2);}
        if(kind=="split_curve")fields["distances"]=Arr(Num(0,1e9),1);
        var result=Obj(fields);
        fields["workplane"]=WorkplaneSchema.Input();
        result["description"]="One sole operation per request, exact independent native Line/Arc/Polyline in WCSXY/Z0. Read complete geometry first; expected_sources follows handles in order. Arc angles are radians, positive CCW sweep. Polyline positions use point_ocs; include every bulge/start_width/end_width and closed. Constraints, fields, persistent dependencies and native model ownership require the owning adapter. Complete requests and results are bounded by actual serialized bytes; geometry is never truncated to a curve or vertex count. erase_sources is explicit: true replaces original entities with new handles, false retains originals as well. Returns contour_assembly_results with all source/generated refs; refresh handles before further editing. New curves copy display properties of the FIRST source; source application metadata, fields and dependencies are not copied. "+(kind switch{
            "join_curves"=>"Join at least two OPEN sources in explicit handle order into one Polyline. reverse has one Boolean per source: traverse it backward when true; signed arc bulges and taper widths follow traversal. All joining endpoints must coincide within1e-7mm; closed=true additionally requires the last endpoint at the first. Does not bridge gaps, sort inputs, remove intersections or infer a simple region. Only active segment widths are retained; open terminal metadata is not part of an edge.",
            "explode_polyline"=>"Decompose one Polyline into every exact Line/Arc segment. Widths must be zero because Line/Arc cannot retain them. Native Arcs are always CCW, so clockwise source arc segments reverse stored endpoints while preserving the same locus. Generated refs follow source segment order.",
            _=>"Divide a Line/Arc/Polyline at strictly increasing distances in mm along its STORED direction, including true arc lengths. Open sources need internal cuts and yield N+1 pieces. Closed Polyline accepts0 as a seam, distances must be less than perimeter; yields N open pieces, final piece wraps across the original seam. A single cut opens a closed Polyline. Source type, curvature and interpolated taper widths are retained; there is no discarded interval. Closed results may repeat first point as the final endpoint of an OPEN path."})+" Inspect API geometry and a visual checkpoint after a meaningful group.";
        result["description"]=result["description"]!.GetValue<string>().Replace("in WCSXY/Z0","in one explicit workplane (WCSXY/Z0 when omitted)").Replace("Polyline positions use point_ocs","expected_sources positions and Arc angles use the supplied local frame; with no workplane Polyline positions use point_ocs");
        return result;
    }
}
