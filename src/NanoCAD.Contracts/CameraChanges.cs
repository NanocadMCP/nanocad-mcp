using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;

namespace NanoCAD.Contracts;

public sealed record CameraChangeArguments(string BridgeId,string DocumentId,string ExpectedRevision,string OperationId,
    string ExpectedCameraToken,string Action,string? Preset,string? RestoreToken,string DisplayStyle="preserve",string[]? FrameHandles=null,double FramePadding=.1) : IOperationArguments
{
    public string Operation=>"set_view";
    public string Fingerprint()=>Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(new{
        Operation,BridgeId,DocumentId,ExpectedRevision,ExpectedCameraToken,Action,Preset,RestoreToken,DisplayStyle,FrameHandles,FramePadding},Wire.Json)));
}
public sealed record CameraChangeResult(string operation_id,string status,bool replayed,CameraView before,CameraView after,
    string restore_token,string scope,bool saved,CameraRendererState370? renderer_before=null,CameraRendererState370? renderer_after=null,
    string renderer_scope="editor_view_only; native_renderer_unobserved");
public sealed record CameraRendererState370(string view_document_id,string view_window_handle,double[] eye_mm,double[] target_mm,
    double[] up_direction_wcs,bool perspective,double native_scale_or_angle,double pixel_size_mm,double[] viewport_pixels,bool? requested_projection_matches,string observation_scope);

public static class CameraChangeValidation
{
    public static readonly string[] Presets={"top","bottom","front","back","left","right","isometric"};
    public static CameraChangeArguments Parse(JsonElement input)
    {
        void Need(bool c,string m){if(!c)throw new DomainException("INVALID_ARGUMENT",m);}
        Need(input.ValueKind==JsonValueKind.Object,"Expected camera change object.");
        var fields=new Dictionary<string,JsonElement>(StringComparer.Ordinal);
        foreach(var p in input.EnumerateObject())Need(p.Name is "bridge_instance_id" or "document_id" or "expected_revision" or "operation_id" or "expected_camera_token" or "action" or "preset" or "restore_token" or "display_style" or "frame_handles" or "frame_padding" && fields.TryAdd(p.Name,p.Value),"Unknown or duplicate camera argument.");
        string Text(string key,int max){Need(fields.TryGetValue(key,out var p)&&p.ValueKind==JsonValueKind.String,"Missing string: "+key);var v=p.GetString()!;Need(v.Length>0&&v.Length<=max,"Invalid length: "+key);return v;}
        string Id(string key){Need(Guid.TryParseExact(Text(key,36),"D",out var id)&&id!=Guid.Empty,"Expected UUID: "+key);return id.ToString("D");}
        string bridge=Id("bridge_instance_id"),document=Id("document_id"),operation=Id("operation_id"),revision=Text("expected_revision",20);
        Need(revision.All(c=>c is >= '0' and <= '9')&&ulong.TryParse(revision,NumberStyles.None,CultureInfo.InvariantCulture,out _),"Invalid revision.");
        string token=Text("expected_camera_token",64);
        Need(token.Length==64&&token.All(c=>c is >= '0' and <= '9' or >= 'A' and <= 'F'),"Expected current camera token.");
        string action=Text("action",7);Need(action is "preset" or "restore","Unknown camera action.");
        string? preset=null,restore=null;
        string style="preserve";
        if(action=="preset"){preset=Text("preset",10);Need(Presets.Contains(preset)&&!fields.ContainsKey("restore_token"),"Expected one named preset.");if(fields.ContainsKey("display_style"))style=Text("display_style",12);Need(style is "preserve" or "shaded_edges" or "wireframe_2d","Unknown display style.");}
        else {restore=Id("restore_token");Need(!fields.ContainsKey("preset")&&!fields.ContainsKey("display_style"),"Restore uses only the stored view and display style.");}
        string[]? handles=null;double padding=.1;
        if(fields.TryGetValue("frame_handles",out var frame))
        {
            Need(action=="preset"&&frame.ValueKind==JsonValueKind.Array&&frame.GetArrayLength()>=1,"Framing requires a nonempty handle set with a preset.");
            var list=new List<string>();foreach(var handle in frame.EnumerateArray())
            {Need(handle.ValueKind==JsonValueKind.String,"Expected hex framing handles.");var normalized=Validation.NormalizeHandle(handle.GetString()!);Need(!list.Contains(normalized),"Duplicate framing handle.");list.Add(normalized);}
            handles=list.ToArray();
        }
        if(fields.TryGetValue("frame_padding",out var pad))
        {
            Need(handles!=null&&pad.ValueKind==JsonValueKind.Number&&pad.TryGetDouble(out _),"frame_padding requires frame_handles and a number.");padding=pad.GetDouble();
            Need(double.IsFinite(padding)&&padding>=0&&padding<=1,"Framing padding is a per-side fraction0–1; default0.1.");
        }
        return new(bridge,document,ulong.Parse(revision,CultureInfo.InvariantCulture).ToString(CultureInfo.InvariantCulture),operation,token,action,preset,restore,style,handles,padding);
    }
    public static double[] Direction(string preset)=>preset switch {
        "top"=>new[]{0d,0d,1d},"bottom"=>new[]{0d,0d,-1d},"front"=>new[]{0d,-1d,0d},"back"=>new[]{0d,1d,0d},
        "left"=>new[]{-1d,0d,0d},"right"=>new[]{1d,0d,0d},"isometric"=>new[]{1d,-1d,1d},
        _=>throw new DomainException("INVALID_ARGUMENT","Unknown view preset.")};
    public static bool SameState(CameraView a,CameraView b)
    {
        bool Near(double x,double y)=>x==y||Math.Abs(x-y)<=1e-10*Math.Max(1,Math.Max(Math.Abs(x),Math.Abs(y)));
        bool Points(double[] x,double[] y)=>x.Length==y.Length&&x.Zip(y,Near).All(v=>v);
        double[] Direction(double[] v){double m=v.Max(Math.Abs);var n=v.Select(x=>x/m).ToArray();double l=Math.Sqrt(n.Sum(x=>x*x));return n.Select(x=>x/l).ToArray();}
        return a.viewport_number==b.viewport_number&&a.viewport_handle==b.viewport_handle&&a.projection==b.projection&&a.render_mode==b.render_mode&&a.visual_style_handle==b.visual_style_handle&&
            a.front_clip_enabled==b.front_clip_enabled&&a.back_clip_enabled==b.back_clip_enabled&&
            Points(a.target_wcs,b.target_wcs)&&Points(Direction(a.direction_wcs),Direction(b.direction_wcs))&&Points(a.center_view,b.center_view)&&
            Near(a.width,b.width)&&Near(a.height,b.height)&&Near(a.twist_radians,b.twist_radians)&&
            Near(a.front_clip_distance,b.front_clip_distance)&&Near(a.back_clip_distance,b.back_clip_distance);
    }
    public static bool SameUntwistedOrthographicProjection(CameraView a,CameraView b,Point3 viewX,Point3 viewY)
    {
        // This is equality of the projected position of EVERY world point:
        // screenX = dot(point-target, viewX) - centerX (and likewise for Y).
        // Orthographic cameras can encode the same image using different
        // target/center pairs. Keep raw tokens and SameState unchanged.
        if(a.projection!="orthographic" || a.front_clip_enabled || a.back_clip_enabled || a.twist_radians!=0 || b.twist_radians!=0 ||
            !SameState(a with{target_wcs=b.target_wcs,center_view=b.center_view},b))return false;
        double Dot(Point3 x,Point3 y)=>x.X*y.X+x.Y*y.Y+x.Z*y.Z;
        if(!new[]{viewX.X,viewX.Y,viewX.Z,viewY.X,viewY.Y,viewY.Z}.All(double.IsFinite) ||
            Math.Abs(Dot(viewX,viewX)-1)>1e-10 || Math.Abs(Dot(viewY,viewY)-1)>1e-10 || Math.Abs(Dot(viewX,viewY))>1e-10)return false;
        var n=new Point3(viewX.Y*viewY.Z-viewX.Z*viewY.Y,viewX.Z*viewY.X-viewX.X*viewY.Z,viewX.X*viewY.Y-viewX.Y*viewY.X);
        double scale=a.direction_wcs.Max(Math.Abs);
        var direction=new Point3(a.direction_wcs[0]/scale,a.direction_wcs[1]/scale,a.direction_wcs[2]/scale);
        if(Math.Abs(Dot(n,direction)/Math.Sqrt(Dot(direction,direction))-1)>1e-10)return false;
        var delta=new Point3(a.target_wcs[0]-b.target_wcs[0],a.target_wcs[1]-b.target_wcs[1],a.target_wcs[2]-b.target_wcs[2]);
        double dx=Dot(delta,viewX)+a.center_view[0]-b.center_view[0];
        double dy=Dot(delta,viewY)+a.center_view[1]-b.center_view[1];
        // Scale tolerance to the visible field, not to potentially large WCS
        // coordinates: a real pan must not disappear in relative world error.
        return double.IsFinite(dx)&&double.IsFinite(dy)&&Math.Abs(dx)<=1e-10*Math.Max(1,a.width)&&Math.Abs(dy)<=1e-10*Math.Max(1,a.height);
    }
}
