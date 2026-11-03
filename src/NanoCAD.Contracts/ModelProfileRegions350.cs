using System.Text.Json;

namespace NanoCAD.Contracts;

public sealed record ModelProfileRegionsSpec350(string[] Targets,string FeatureId,string ProfileId,string ExpectedSketchId,
    string ExpectedRegionToken,string ExpectedLastItemId,string[] AddSourceHandles,Point3[] RegionPoints)
    :EditSpec("set_model_profile_regions",Targets);
public sealed record ModelCircleLoop350(string element_id,Point2 center,double radius,string source_object_id);
public sealed record ModelCircleRegion350(string outer_element_id,string[] hole_element_ids);
public sealed record ModelProfileRegionsRead350(string profile_id,string parent_sketch_id,string mc_document_id,
    string feature_id,string region_token,ModelProfilePlane340 plane,ModelCircleLoop350[] loops,ModelCircleRegion350[] regions,
    string scope,JsonElement constraint_observation);
public sealed record ModelProfileRegionsChange350(int operation_index,EntityRef source,EntityRef body_ref,string body_mc_id,
    string feature_id,string profile_id,string parent_sketch_id,string region_token,string mc_document_id,string last_item_id,
    bool source_representation_replaced,int region_count,int hole_count,EntityRef[] added_source_refs,string[] added_projection_ids,
    int retained_downstream_count,string downstream_history_sha256,JsonElement constraint_observation);
public sealed record ModelProfileRegionsOutput350(string operation_id,string status,bool replayed,EntityRef[] modified,
    EntityRef[] erased,EntityRef[] created,CopyMapping[] copies,string undo_history,ModelProfileRegionsChange350[] model_profile_regions);

public static class ModelProfileRegionsValidation350
{
    static DomainException Invalid(string path,string message)=>new("INVALID_ARGUMENT",message,false,new{path});
    static string Text(JsonElement item,string key,string path)
    {if(!item.TryGetProperty(key,out var value)||value.ValueKind!=JsonValueKind.String)throw Invalid(path+"."+key,"Expected a string.");return value.GetString()!;}
    static string Id(JsonElement item,string key,string path)
    {if(!Guid.TryParseExact(Text(item,key,path),"B",out var id)||id==Guid.Empty)throw Invalid(path+"."+key,"Read the current exact native ID.");return id.ToString("B").ToUpperInvariant();}
    public static EditSpec Parse(JsonElement item,string path,string[] handles)
    {
        if(item.ValueKind!=JsonValueKind.Object||handles.Length!=1)throw Invalid(path,"Select one existing native part.");
        string[] fields=["operation","handles","feature_id","profile_id","expected_sketch_id","expected_region_token","expected_last_item_id","add_source_handles","region_points"];
        var seen=new HashSet<string>(StringComparer.Ordinal);foreach(var field in item.EnumerateObject())
            if(!fields.Contains(field.Name)||!seen.Add(field.Name))throw Invalid(path+"."+field.Name,"Unknown or duplicate field.");
        if(Text(item,"operation",path)!="set_model_profile_regions")throw Invalid(path+".operation","Use set_model_profile_regions.");
        string token=Text(item,"expected_region_token",path);if(token.Length!=64||!token.All(Uri.IsHexDigit))throw Invalid(path+".expected_region_token","Read current exact profile_region_ids editing metadata.");
        var sources=new List<string>();var unique=new HashSet<string>(StringComparer.Ordinal);
        if(item.TryGetProperty("add_source_handles",out var additions))
        {
            if(additions.ValueKind!=JsonValueKind.Array)throw Invalid(path+".add_source_handles","Expected committed drawing handles.");
            foreach(var value in additions.EnumerateArray())
            {
                if(value.ValueKind!=JsonValueKind.String)throw Invalid(path+".add_source_handles","Expected a committed source handle.");
                string source=Validation.NormalizeHandle(value.GetString()!);
                if(source==handles[0]||!unique.Add(source))throw Invalid(path+".add_source_handles","Use distinct sources separate from the body.");sources.Add(source);
            }
        }
        if(!item.TryGetProperty("region_points",out var points)||points.ValueKind!=JsonValueKind.Array||points.GetArrayLength()==0)
            throw Invalid(path+".region_points","Select each desired bounded region by one interior WCS point.");
        var selected=new List<Point3>();foreach(var point in points.EnumerateArray())
        {
            if(point.ValueKind!=JsonValueKind.Array||point.GetArrayLength()!=3)throw Invalid(path+".region_points","Use WCS [x,y,z] points.");
            var xyz=new double[3];for(int i=0;i<3;i++)if(point[i].ValueKind!=JsonValueKind.Number||!point[i].TryGetDouble(out xyz[i])||!double.IsFinite(xyz[i])||Math.Abs(xyz[i])>1e6)
                throw Invalid(path+".region_points","Use finite drawing millimeters within1e6.");selected.Add(new(xyz[0],xyz[1],xyz[2]));
        }
        return new ModelProfileRegionsSpec350(handles,Id(item,"feature_id",path),Id(item,"profile_id",path),Id(item,"expected_sketch_id",path),
            token.ToUpperInvariant(),Id(item,"expected_last_item_id",path),sources.ToArray(),selected.ToArray());
    }
    public static void Batch(IReadOnlyList<EditSpec> edits)
    {if(edits.Any(edit=>edit is ModelProfileRegionsSpec350)&&edits.Count!=1)throw new DomainException("INVALID_OPERATION_ORDER","A native profile region edit must be the sole operation.");}
}

// Pure topology of separated/nested circles. No native solver or factory.
public static class ModelCircleRegions350
{
    const double Epsilon=1e-7;
    static void Need(bool valid,string message){if(!valid)throw new DomainException("PROFILE_REGION_AMBIGUOUS",message);}
    static double Distance(Point2 a,Point2 b)=>Math.Sqrt((a.X-b.X)*(a.X-b.X)+(a.Y-b.Y)*(a.Y-b.Y));
    public static Dictionary<string,string?> Parents(ModelCircleLoop350[] loops,Action cancel)
    {
        Need(loops.Length>0&&loops.Select(loop=>loop.element_id).Distinct(StringComparer.OrdinalIgnoreCase).Count()==loops.Length,"Circle loop identities must be complete and unique.");
        var parents=new Dictionary<string,string?>(StringComparer.OrdinalIgnoreCase);
        foreach(var a in loops)
        {
            cancel();Need(double.IsFinite(a.radius)&&a.radius>=.001&&double.IsFinite(a.center.X)&&double.IsFinite(a.center.Y),"Circle loop geometry must be finite and noncollapsed.");
            ModelCircleLoop350? parent=null;
            foreach(var b in loops)
            {
                cancel();if(a.element_id==b.element_id)continue;double d=Distance(a.center,b.center);
                Need(d>a.radius+b.radius+Epsilon||d+Math.Min(a.radius,b.radius)+Epsilon<Math.Max(a.radius,b.radius),"Intersecting, coincident or tangent circles need another region-topology adapter.");
                if(d+a.radius+Epsilon<b.radius&&(parent==null||b.radius<parent.radius))parent=b;
            }
            parents.Add(a.element_id,parent?.element_id);
        }
        return parents;
    }
    public static ModelCircleRegion350[] Select(ModelCircleLoop350[] loops,Point2[] points,Action cancel)
    {
        var parents=Parents(loops,cancel);var selected=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach(var point in points)
        {
            cancel();Need(double.IsFinite(point.X)&&double.IsFinite(point.Y),"Region point must be finite.");ModelCircleLoop350? inside=null;
            foreach(var loop in loops)
            {
                cancel();double d=Distance(point,loop.center);Need(Math.Abs(d-loop.radius)>Epsilon,"A region point is on a boundary; choose an unambiguous interior point.");
                if(d<loop.radius&&(inside==null||loop.radius<inside.radius))inside=loop;
            }
            Need(inside!=null,"Each point must lie in a bounded native region.");Need(selected.Add(inside!.element_id),"Two points select the same region; no native toggle is repeated.");
        }
        Need(selected.Count>0,"Select at least one bounded region.");
        foreach(string id in selected){cancel();Need(parents[id]==null||!selected.Contains(parents[id]!),"Adjacent nested selected cells may merge; their boundary-union adapter is outside this operation.");}
        return selected.OrderBy(id=>id,StringComparer.OrdinalIgnoreCase).Select(id=>new ModelCircleRegion350(id,
            parents.Where(pair=>pair.Value==id).Select(pair=>pair.Key).OrderBy(key=>key,StringComparer.OrdinalIgnoreCase).ToArray())).ToArray();
    }
}
