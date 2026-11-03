using System.Security.Cryptography;
using System.Text.Json;

namespace NanoCAD.Contracts;

public sealed record CameraView(string camera_token, int viewport_number, string? viewport_handle,
    double[] target_wcs, double[] direction_wcs, double[] center_view, double width, double height,
    double twist_radians, string projection, bool front_clip_enabled, bool back_clip_enabled,
    double front_clip_distance, double back_clip_distance,string render_mode="unknown",string? visual_style_handle=null);

public static class CameraViews
{
    public static CameraView Create(string bridge, string document, string revision, int viewport, string? handle,
        double[] target, double[] direction, double[] center, double width, double height, double twist,
        double frontDistance, double backDistance,string renderMode="unknown",string? visualStyleHandle=null)
    {
        bool Finite(double[] a, int count) => a.Length == count && a.All(double.IsFinite);
        if (!Finite(target,3) || !Finite(direction,3) || !Finite(center,2) || direction.All(v => v == 0) ||
            !new[]{width,height,twist,frontDistance,backDistance}.All(double.IsFinite) || width <= 0 || height <= 0 || viewport < 1)
            throw new DomainException("VIEW_UNAVAILABLE", "The native camera returned invalid coordinates or viewport identity.");
        var view = new CameraView("",viewport,handle,target.ToArray(),direction.ToArray(),center.ToArray(),width,height,twist,
            "orthographic",false,false,frontDistance,backDistance,renderMode,visualStyleHandle);
        byte[] bytes = JsonSerializer.SerializeToUtf8Bytes(new { bridge, document, revision, view },Wire.Json);
        return view with { camera_token = Convert.ToHexString(SHA256.HashData(bytes)) };
    }
}
