using System.Text.Json.Nodes;

namespace NanoCAD.McpServer;

internal static class SmoothPolylineSchema409
{
    private static JsonObject Number()=>new(){["type"]="number"};
    private static JsonObject Integer()=>new(){["type"]="integer",["minimum"]=0};
    private static JsonObject Indices()=>new(){["type"]="array",["uniqueItems"]=true,["items"]=Integer()};
    internal static JsonObject Entity()
    {
        JsonObject Pair(string description)=>new(){["type"]="array",["minItems"]=2,["maxItems"]=2,
            ["items"]=new JsonObject{["type"]="number",["minimum"]=-1e9,["maximum"]=1e9},["description"]=description};
        return new(){["type"]="object",["additionalProperties"]=false,["properties"]=new JsonObject
        {
            ["type"]=new JsonObject{["const"]="Polyline"},["construction"]=new JsonObject{["const"]="fit_points"},
            ["layer"]=new JsonObject{["type"]="string",["minLength"]=1,["maxLength"]=255,["default"]="0"},["workplane"]=WorkplaneSchema.Input(),
            ["source_points"]=new JsonObject{["type"]="array",["minItems"]=2,["items"]=Pair("Ordered local XY sample in millimeters; WCS XY/Z0 by default."),
                ["description"]="Ordered supplied samples, with distinct neighbors and no repeated first/last point. Explicit closure requires at least three. Samples define fit observations; straight sample chords are not an inferred underlying surface. No fixed sample-count cap."},
            ["fit_tolerance"]=new JsonObject{["type"]="number",["exclusiveMinimum"]=0,["maximum"]=1e9,
                ["description"]="Maximum measured Euclidean distance in millimeters of an actual feasible ordered correspondence from supplied points to the finite arc/line chain. Witness segment/parameter positions are nondecreasing; errors are not independent global minima or an optimal RMS claim. Must reach coordinate numerical resolution. No bound for unseen geometry between samples; largest source chord is disclosed. No scan or manufacturing tolerance is inferred."},
            ["closed"]=new JsonObject{["type"]="boolean",["default"]=false,["description"]="Explicitly adds the last-to-first span and requires a smooth seam unless source index0 is an intentional corner. Never inferred from sample proximity."},
            ["corner_indices"]=new JsonObject{["type"]="array",["uniqueItems"]=true,["items"]=Integer(),
                ["description"]="Actual source indices of intentional corners. G1 may break only at those exact points. Open endpoints are not internal corners. Omit to require every internal join and any closed seam to be forward tangent-continuous."},
            ["start_tangent"]=Pair("Forward tangent direction with a component of at least1e-9 in the local workplane; supply both endpoint directions for an open fit, or omit both for estimated directions."),
            ["end_tangent"]=Pair("Nonzero forward end direction, with the same paired/open requirements."),
            ["include_sample_details"]=new JsonObject{["type"]="boolean",["default"]=false,
                ["description"]="Optional exact source-index to result-segment/parameter/distance map. Default returns compact provenance/hash/error/gap/join metrics. Complete actual response bytes govern admission; details are never silently truncated."}
        },["required"]=new JsonArray("type","construction","source_points","fit_tolerance"),
            ["dependentRequired"]=new JsonObject{["start_tangent"]=new JsonArray("end_tangent"),["end_tangent"]=new JsonArray("start_tangent")},
            ["description"]="Create an independent native lightweight Polyline of circular arcs and genuinely straight lines, with forward G1 tangent joins on smooth spans, explicit sample-distance tolerance and source provenance/error report. A native arc-conversion candidate is preferred and verified; a qualified deterministic biarc fit is disclosed when needed. Never returns a Spline or a dense straight-segment mesh for a curved smooth span. Source points/clouds remain independent; no retained source association or future-fit constraint is created. Preserve real gaps by separate open curves and real corners with explicit indices."};
    }
    internal static JsonObject FitResult()
    {
        var properties=new JsonObject
        {
            ["entity_index"]=Integer(),["method"]=new JsonObject{["type"]="string",["enum"]=new JsonArray("native_fit_spline_arc_conversion","deterministic_tangent_biarcs")},
            ["source_kind"]=new JsonObject{["const"]="ordered_request_points"},["source_sha256"]=new JsonObject{["type"]="string",["pattern"]="^[0-9A-F]{64}$"},
            ["source_point_count"]=Integer(),["fit_tolerance"]=Number(),["sample_error_metric"]=new JsonObject{["const"]="ordered_correspondence_witness"},
            ["numerical_resolution"]=Number(),["maximum_sample_distance"]=Number(),["rms_sample_distance"]=Number(),
            ["largest_source_chord"]=Number(),["maximum_smooth_join_angle"]=Number(),["line_count"]=Integer(),["arc_count"]=Integer(),["closed"]=new JsonObject{["type"]="boolean"},
            ["corner_indices"]=Indices(),["corner_vertex_indices"]=Indices(),["retained_source_link"]=new JsonObject{["const"]=false},["coordinate_frame"]=new JsonObject{["type"]="string"},
            ["native_candidate_rejection"]=new JsonObject{["type"]=new JsonArray("string","null")},
            ["samples"]=new JsonObject{["type"]="array",["items"]=new JsonObject{["type"]="object",["additionalProperties"]=false,
                ["properties"]=new JsonObject{["source_index"]=Integer(),["segment_index"]=Integer(),["segment_parameter"]=new JsonObject{["type"]="number",["minimum"]=0,["maximum"]=1},["distance"]=Number()},
                ["required"]=new JsonArray("source_index","segment_index","segment_parameter","distance")}}
        };
        return new(){["type"]="object",["additionalProperties"]=false,["properties"]=properties,
            ["required"]=new JsonArray(properties.Select(p=>p.Key).Where(n=>n!="samples").Select(n=>(JsonNode?)JsonValue.Create(n)).ToArray())};
    }
}
