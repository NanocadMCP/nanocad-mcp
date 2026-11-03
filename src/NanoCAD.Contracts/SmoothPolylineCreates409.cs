using System.Text.Json;
using System.Text.Json.Serialization;

namespace NanoCAD.Contracts;

public sealed record SmoothPolylineSpec409(Point2[] SourcePoints,double FitTolerance,bool Closed,int[] CornerIndices,
    Point2? StartTangent,Point2? EndTangent,bool IncludeSampleDetails,string LayerName):EntitySpec("Polyline",LayerName)
{
    public string Construction=>"fit_points";
}

public sealed record SmoothPolylineSample409(int source_index,int segment_index,double segment_parameter,double distance);
public sealed record SmoothPolylineFit409(int entity_index,string method,string source_kind,string source_sha256,
    int source_point_count,double fit_tolerance,string sample_error_metric,double numerical_resolution,double maximum_sample_distance,double rms_sample_distance,
    double largest_source_chord,double maximum_smooth_join_angle,int line_count,int arc_count,bool closed,
    int[] corner_indices,int[] corner_vertex_indices,bool retained_source_link,string coordinate_frame,string? native_candidate_rejection,
    [property:JsonIgnore(Condition=JsonIgnoreCondition.WhenWritingNull)] SmoothPolylineSample409[]? samples);

public static partial class CreateValidation
{
    internal static SmoothPolylineSpec409 ParseSmoothPolyline409(JsonElement item,string path,string layer)
    {
        Shape(item,path,"type","layer","construction","source_points","fit_tolerance","closed","corner_indices","start_tangent","end_tangent","include_sample_details");
        if(Text(item,"construction",path,16)!="fit_points")throw Invalid(path+".construction","Expected fit_points.");
        bool closed=Boolean(item,"closed",path,false);var raw=Required(item,"source_points",path);
        if(raw.ValueKind!=JsonValueKind.Array||raw.GetArrayLength()<(closed?3:2))
            throw Invalid(path+".source_points","Supply at least two ordered samples, or three for explicit closure.","INVALID_GEOMETRY");
        var points=new Point2[raw.GetArrayLength()];
        for(int i=0;i<points.Length;i++)
        {
            var p=raw[i];string at=path+$".source_points[{i}]";
            if(p.ValueKind!=JsonValueKind.Array||p.GetArrayLength()!=2)throw Invalid(at,"Expected a local XY pair.","INVALID_GEOMETRY");
            points[i]=new(Number(p[0],at+"[0]"),Number(p[1],at+"[1]"));
            if(i>0&&points[i]==points[i-1])throw Invalid(at,"Consecutive samples must differ.","INVALID_GEOMETRY");
        }
        if(points[0]==points[^1])throw Invalid(path+".source_points","Do not repeat the first sample; closed=true adds the closing span explicitly. Open endpoints must differ.","INVALID_GEOMETRY");
        double tolerance=Number(Required(item,"fit_tolerance",path),path+".fit_tolerance",0);
        if(tolerance<=0)throw Invalid(path+".fit_tolerance","A finite positive sample-distance tolerance is required.","INVALID_GEOMETRY");
        var corners=new List<int>();
        if(item.TryGetProperty("corner_indices",out var cornerArray))
        {
            if(cornerArray.ValueKind!=JsonValueKind.Array)throw Invalid(path+".corner_indices","Expected source index array.");
            foreach(var (value,index) in cornerArray.EnumerateArray().Select((p,i)=>(p,i)))
            {
                string at=path+$".corner_indices[{index}]";
                if(value.ValueKind!=JsonValueKind.Number||!value.TryGetInt32(out int corner)||corner<0||corner>=points.Length||
                    !closed&&(corner==0||corner==points.Length-1)||corners.Contains(corner))
                    throw Invalid(at,"Use distinct actual source indices; open endpoints are not internal corners.");
                corners.Add(corner);
            }
        }
        bool hasStart=item.TryGetProperty("start_tangent",out _),hasEnd=item.TryGetProperty("end_tangent",out _);
        if(hasStart!=hasEnd||closed&&hasStart)throw Invalid(path,"Supply both endpoint directions only for an open fit, or omit both.");
        Point2? start=null,end=null;
        if(hasStart)
        {
            start=Point(item,"start_tangent",path);end=Point(item,"end_tangent",path);
            if(Math.Max(Math.Abs(start.X),Math.Abs(start.Y))<Minimum||Math.Max(Math.Abs(end.X),Math.Abs(end.Y))<Minimum)
                throw Invalid(path,"Endpoint tangent directions need a component of at least1e-9.","INVALID_GEOMETRY");
        }
        return new(points,tolerance,closed,corners.OrderBy(i=>i).ToArray(),start,end,Boolean(item,"include_sample_details",path,false),layer);
    }
}
