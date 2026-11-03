using System.Text.Json;

namespace NanoCAD.Contracts;

public sealed record HatchBoundaryLoop349(string Kind,Point2[]? Vertices,Point2? Center,double Radius);
public sealed record SetHatchBoundarySpec349(string[] Targets,string HatchToken,HatchBoundaryLoop349 Outer,HatchBoundaryLoop349[] Holes)
    :EditSpec("set_hatch_boundary",Targets);
public sealed record HatchBoundaryRead349(bool candidate,string scope,string[] required_inputs,string coordinate_system);
public sealed record HatchBoundaryChange349(int operation_index,EntityRef hatch_ref,string hatch_token,int previous_loop_count,int loop_count,
    int hole_count,string boundary_scope,string[] warnings);
public sealed record HatchBoundaryOutput349(string operation_id,string status,bool replayed,EntityRef[] modified,EntityRef[] erased,EntityRef[] created,
    CopyMapping[] copies,string undo_history,HatchBoundaryChange349[] hatch_boundary_changes);

public static class HatchBoundaryValidation349
{
    static DomainException Invalid(string path,string message)=>new("INVALID_ARGUMENT",message,false,new{path});
    static void Shape(JsonElement value,string path,params string[] fields)
    {
        if(value.ValueKind!=JsonValueKind.Object)throw Invalid(path,"Expected an object.");var seen=new HashSet<string>(StringComparer.Ordinal);
        foreach(var field in value.EnumerateObject())if(!seen.Add(field.Name)||!fields.Contains(field.Name))throw Invalid(path+"."+field.Name,"Unknown or duplicate field.");
        foreach(string field in fields)if(!value.TryGetProperty(field,out _))throw Invalid(path+"."+field,"Required field.");
    }
    static string Text(JsonElement item,string field,string path)
    {if(item.ValueKind!=JsonValueKind.Object||!item.TryGetProperty(field,out var value)||value.ValueKind!=JsonValueKind.String)throw Invalid(path+"."+field,"Expected a string.");return value.GetString()!;}
    static double Number(JsonElement value,string path)
    {
        if(value.ValueKind!=JsonValueKind.Number||!value.TryGetDouble(out double number)||!double.IsFinite(number)||Math.Abs(number)>1e9)
            throw Invalid(path,"Expected a finite native coordinate within +/-1e9.");return number==0?0:number;
    }
    static Point2 Point(JsonElement value,string path)
    {if(value.ValueKind!=JsonValueKind.Array||value.GetArrayLength()!=2)throw Invalid(path,"Use an OCS XY pair.");return new(Number(value[0],path+"[0]"),Number(value[1],path+"[1]"));}
    static HatchBoundaryLoop349 Loop(JsonElement value,string path)
    {
        string kind=Text(value,"kind",path);
        if(kind=="polygon")
        {
            Shape(value,path,"kind","vertices");var vertices=value.GetProperty("vertices");
            if(vertices.ValueKind!=JsonValueKind.Array||vertices.GetArrayLength()<3)throw Invalid(path+".vertices","Use at least three polygon vertices without a repeated closing vertex.");
            return new(kind,vertices.EnumerateArray().Select((point,index)=>Point(point,path+".vertices["+index+"]")).ToArray(),null,0);
        }
        if(kind=="circle")
        {
            Shape(value,path,"kind","center","radius");double radius=Number(value.GetProperty("radius"),path+".radius");
            if(radius<=0)throw Invalid(path+".radius","Circle radius must be positive.");return new(kind,null,Point(value.GetProperty("center"),path+".center"),radius);
        }
        throw Invalid(path+".kind","Use polygon or circle. Native curves are not sampled into polygons.");
    }
    public static EditSpec Parse(JsonElement item,string path,string[] handles)
    {
        Shape(item,path,"operation","handles","hatch_token","outer_loop","holes");
        if(Text(item,"operation",path)!="set_hatch_boundary"||handles.Length!=1)throw Invalid(path,"Address one exact native Hatch per boundary operation.");
        string token=Text(item,"hatch_token",path);
        if(token.Length!=64||!token.All(Uri.IsHexDigit))throw Invalid(path+".hatch_token","Read the current exact geometry.editing.hatch_token.");
        var holes=item.GetProperty("holes");if(holes.ValueKind!=JsonValueKind.Array)throw Invalid(path+".holes","Use an array; empty explicitly removes all holes.");
        return new SetHatchBoundarySpec349(handles,token.ToUpperInvariant(),Loop(item.GetProperty("outer_loop"),path+".outer_loop"),
            holes.EnumerateArray().Select((hole,index)=>Loop(hole,path+".holes["+index+"]")).ToArray());
    }
    public static void Batch(IReadOnlyCollection<EditSpec> edits)
    {
        if(!edits.Any(edit=>edit is SetHatchBoundarySpec349))return;
        if(edits.Any(edit=>edit is not SetHatchBoundarySpec349)||edits.SelectMany(edit=>edit.Handles).Distinct(StringComparer.Ordinal).Count()!=edits.Count)
            throw new DomainException("INVALID_OPERATION_ORDER","Boundary replacement uses a dedicated batch of distinct Hatches; read each token after any preceding operation.");
    }
}

// Straight/circular topology validation, with cancellation at every segment
// comparison. Actual request/snapshot bytes bound size, not a vertex/hole cap.
public static class HatchBoundaryTopology349
{
    const double Epsilon=1e-9;
    static DomainException Bad(string message)=>new("INVALID_GEOMETRY",message);
    static double Cross(Point2 a,Point2 b,Point2 c)=>(b.X-a.X)*(c.Y-a.Y)-(b.Y-a.Y)*(c.X-a.X);
    static double Distance(Point2 a,Point2 b)=>Math.Sqrt(Math.Pow(a.X-b.X,2)+Math.Pow(a.Y-b.Y,2));
    static double Segment(Point2 point,Point2 a,Point2 b)
    {double x=b.X-a.X,y=b.Y-a.Y,t=Math.Clamp(((point.X-a.X)*x+(point.Y-a.Y)*y)/(x*x+y*y),0,1);return Distance(point,new(a.X+t*x,a.Y+t*y));}
    static bool Crosses(Point2 a,Point2 b,Point2 c,Point2 d)
    {
        if(Segment(a,c,d)<=Epsilon||Segment(b,c,d)<=Epsilon||Segment(c,a,b)<=Epsilon||Segment(d,a,b)<=Epsilon)return true;
        return Cross(a,b,c)*Cross(a,b,d)<0&&Cross(c,d,a)*Cross(c,d,b)<0;
    }
    static bool Inside(Point2 point,Point2[] polygon,Action cancel)
    {
        bool result=false;
        for(int index=0;index<polygon.Length;index++)
        {
            cancel();var a=polygon[index];var b=polygon[(index+1)%polygon.Length];if(Segment(point,a,b)<=Epsilon)return false;
            if((a.Y>point.Y)!=(b.Y>point.Y)&&point.X<(b.X-a.X)*(point.Y-a.Y)/(b.Y-a.Y)+a.X)result=!result;
        }
        return result;
    }
    static double Clearance(Point2 point,Point2[] polygon,Action cancel)
    {double value=double.PositiveInfinity;for(int index=0;index<polygon.Length;index++){cancel();value=Math.Min(value,Segment(point,polygon[index],polygon[(index+1)%polygon.Length]));}return value;}
    static void Valid(HatchBoundaryLoop349 loop,Action cancel)
    {
        if(loop.Kind=="circle")return;var p=loop.Vertices!;double area=0;var origin=p[0];
        for(int index=0;index<p.Length;index++)
        {
            cancel();var a=p[index];var b=p[(index+1)%p.Length];if(Distance(a,b)<=Epsilon)throw Bad("Boundary polygon contains an indistinguishable/repeated consecutive vertex.");
            area+=(a.X-origin.X)*(b.Y-origin.Y)-(a.Y-origin.Y)*(b.X-origin.X);
            for(int other=index+1;other<p.Length;other++)
            {cancel();if(other==index+1||index==0&&other==p.Length-1)continue;if(Crosses(a,b,p[other],p[(other+1)%p.Length]))throw Bad("Boundary polygon crosses or touches itself.");}
        }
        if(!double.IsFinite(area)||Math.Abs(area)<=1e-12)throw Bad("Boundary polygon has zero or nonfinite enclosed area.");
    }
    static bool PolygonCrosses(Point2[] first,Point2[] second,Action cancel)
    {
        for(int i=0;i<first.Length;i++)for(int j=0;j<second.Length;j++)
        {cancel();if(Crosses(first[i],first[(i+1)%first.Length],second[j],second[(j+1)%second.Length]))return true;}return false;
    }
    static bool Contains(HatchBoundaryLoop349 outer,HatchBoundaryLoop349 hole,Action cancel)
    {
        if(outer.Vertices is {} polygon)
        {
            if(hole.Vertices is {} inner)return inner.All(point=>Inside(point,polygon,cancel))&&!PolygonCrosses(inner,polygon,cancel);
            return Inside(hole.Center!,polygon,cancel)&&Clearance(hole.Center!,polygon,cancel)>hole.Radius+Epsilon;
        }
        if(hole.Vertices is {} points)return points.All(point=>{cancel();return Distance(point,outer.Center!)<outer.Radius-Epsilon;});
        return Distance(outer.Center!,hole.Center!)+hole.Radius<outer.Radius-Epsilon;
    }
    static bool Interacts(HatchBoundaryLoop349 first,HatchBoundaryLoop349 second,Action cancel)
    {
        if(first.Vertices is {} a&&second.Vertices is {} b)return PolygonCrosses(a,b,cancel)||Inside(a[0],b,cancel)||Inside(b[0],a,cancel);
        if(first.Vertices==null&&second.Vertices==null)return Distance(first.Center!,second.Center!)<=first.Radius+second.Radius+Epsilon;
        var polygon=first.Vertices??second.Vertices!;var circle=first.Vertices==null?first:second;
        return Inside(circle.Center!,polygon,cancel)||Clearance(circle.Center!,polygon,cancel)<=circle.Radius+Epsilon;
    }
    public static void Validate(HatchBoundaryLoop349 outer,HatchBoundaryLoop349[] holes,Action cancel)
    {
        Valid(outer,cancel);foreach(var hole in holes){cancel();Valid(hole,cancel);if(!Contains(outer,hole,cancel))throw Bad("Every hole must lie strictly inside the outer loop without touching it.");}
        for(int first=0;first<holes.Length;first++)for(int second=first+1;second<holes.Length;second++)
        {cancel();if(Interacts(holes[first],holes[second],cancel))throw Bad("Boundary holes must not touch, overlap or nest.");}
    }
}
