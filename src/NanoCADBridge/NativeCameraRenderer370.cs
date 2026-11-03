using NanoCAD.Contracts;
using Teigha.Geometry;
using Multicad.ApplicationServices;
using NativePoint=Multicad.Geometry.Point3d;
using NativeVector=Multicad.Geometry.Vector3d;
using Application=HostMgd.ApplicationServices.Application;

namespace NanoCADBridge;

// The editor view record and the existing MultiCAD view expose separate camera
// APIs. Do not infer that regeneration copies one camera to the other. This
// adapter uses their documented common millimetre WCS and observes native
// pixel size instead of inventing an absolute ScaleOrAngle conversion.
internal static class NativeCameraRenderer370
{
    internal sealed record Snapshot(CameraRendererState370 State);
    static double[] Point(NativePoint value)=>new[]{value.X,value.Y,value.Z};
    static double[] Vector(NativeVector value)=>new[]{value.X,value.Y,value.Z};
    static NativePoint Point(double[] value)=>new(value[0],value[1],value[2]);
    static NativeVector Vector(double[] value)=>new(value[0],value[1],value[2]);
    static double Length(double[] value)=>new Vector3d(value[0],value[1],value[2]).Length;
    static bool Finite(double[] value)=>value.Length==3&&value.All(double.IsFinite);
    static bool Near(double a,double b)=>Math.Abs(a-b)<=1e-8*Math.Max(1,Math.Max(Math.Abs(a),Math.Abs(b)));
    static void Need(bool value,string message)
    {if(!value)throw new DomainException("CAMERA_RENDERER_UNAVAILABLE",message);}
    internal static Snapshot Capture(NativeView active)
    {
        Need(active.GetCamera(out CameraData camera),"The current native view did not return its camera.");
        var eye=Point(camera.Eye);var target=Point(camera.Target);var up=Vector(camera.UpDir);
        double pixelSize=active.GetPixelSize();
        var direction=eye.Zip(target,(a,b)=>a-b).ToArray();
        object screenSize=Application.GetSystemVariable("SCREENSIZE");
        Need(screenSize is Point2d screen&&double.IsFinite(screen.X)&&double.IsFinite(screen.Y)&&screen.X>0&&screen.Y>0,
             "The active viewport pixel dimensions are unavailable.");
        var pixels=(Point2d)screenSize;
        Need(Finite(eye)&&Finite(target)&&Finite(up)&&Length(direction)>0&&Length(up)>0&&
             !camera.Perspective&&double.IsFinite(camera.ScaleOrAngle)&&camera.ScaleOrAngle>0&&
             double.IsFinite(pixelSize)&&pixelSize>0&&active.Handle!=IntPtr.Zero&&!active.DocID.IsNull,
             "The current native view camera, scale, window or orthographic pixel size is unavailable.");
        return new(new(active.DocID.ToString(),active.Handle.ToInt64().ToString("X"),eye,target,up,false,camera.ScaleOrAngle,pixelSize,new[]{pixels.X,pixels.Y},null,
            "native_MultiCAD_camera_millimetres; pixels_not_observed"));
    }
    internal static void RequireBinding(NativeView active,Snapshot before)
    {
        Need(active.DocID.ToString()==before.State.view_document_id&&active.Handle.ToInt64().ToString("X")==before.State.view_window_handle,
             "The native camera belongs to another existing view/document/window.");
        object screenSize=Application.GetSystemVariable("SCREENSIZE");
        Need(screenSize is Point2d pixels&&Near(pixels.X,before.State.viewport_pixels[0])&&Near(pixels.Y,before.State.viewport_pixels[1]),
             "The native viewport pixel dimensions changed since this camera snapshot.");
    }
    internal static Snapshot Plan(Snapshot before,CameraView desired)
    {
        Need(desired.projection=="orthographic"&&!desired.front_clip_enabled&&!desired.back_clip_enabled&&desired.twist_radians==0,
             "Explicit native camera synchronization requires an unclipped untwisted orthographic preset.");
        var direction=new Vector3d(desired.direction_wcs[0],desired.direction_wcs[1],desired.direction_wcs[2]).GetNormal();
        var plane=Matrix3d.PlaneToWorld(direction);
        var x=Vector3d.XAxis.TransformBy(plane);var y=Vector3d.YAxis.TransformBy(plane);
        var target=new Point3d(desired.target_wcs[0],desired.target_wcs[1],desired.target_wcs[2])+x*desired.center_view[0]+y*desired.center_view[1];
        double distance=Length(before.State.eye_mm.Zip(before.State.target_mm,(a,b)=>a-b).ToArray());
        var eye=target+direction*distance;
        object size=Application.GetSystemVariable("SCREENSIZE");
        Need(size is Point2d screen&&double.IsFinite(screen.X)&&double.IsFinite(screen.Y)&&screen.X>0&&screen.Y>0,
             "The active viewport pixel dimensions are unavailable.");
        var pixels=(Point2d)size;double pixelSize=desired.height/pixels.Y;
        Need(double.IsFinite(pixelSize)&&pixelSize>0,"The requested native camera pixel scale is invalid.");
        return new(before.State with{eye_mm=new[]{eye.X,eye.Y,eye.Z},target_mm=new[]{target.X,target.Y,target.Z},
            up_direction_wcs=new[]{y.X,y.Y,y.Z},pixel_size_mm=pixelSize});
    }
    internal static void Apply(NativeView active,Snapshot desired,Action requireScope)
    {
        RequireBinding(active,desired);requireScope();
        var camera=desired.State;
        Need(active.SetCamera(Point(camera.eye_mm),Point(camera.target_mm),Vector(camera.up_direction_wcs),false,camera.native_scale_or_angle,false),
             "The current native view did not confirm camera application.");
        requireScope();RequireBinding(active,desired);
        // Zoom is documented as >1 zoom-in; native pixel size is mm/pixel.
        // Its observed ratio supplies scale without guessing the opaque camera
        // scale field or zooming all/unhiding unrelated objects.
        double actual=active.GetPixelSize();double ratio=actual/camera.pixel_size_mm;
        Need(double.IsFinite(ratio)&&ratio>0,"The native camera returned an invalid pixel scale after application.");
        if(!Near(actual,camera.pixel_size_mm))Need(active.Zoom(ratio),"The current native view did not confirm requested pixel scaling.");
        requireScope();RequireBinding(active,desired);
    }
    internal static Snapshot Verify(NativeView active,Snapshot desired)
    {
        RequireBinding(active,desired);var actual=Capture(active);var a=actual.State;var b=desired.State;
        var n=new Vector3d(b.eye_mm[0]-b.target_mm[0],b.eye_mm[1]-b.target_mm[1],b.eye_mm[2]-b.target_mm[2]).GetNormal();
        var direction=new Vector3d(a.eye_mm[0]-a.target_mm[0],a.eye_mm[1]-a.target_mm[1],a.eye_mm[2]-a.target_mm[2]).GetNormal();
        var up=new Vector3d(b.up_direction_wcs[0],b.up_direction_wcs[1],b.up_direction_wcs[2]).GetNormal();
        var actualUp=new Vector3d(a.up_direction_wcs[0],a.up_direction_wcs[1],a.up_direction_wcs[2]).GetNormal();
        var delta=new Vector3d(a.target_mm[0]-b.target_mm[0],a.target_mm[1]-b.target_mm[1],a.target_mm[2]-b.target_mm[2]);
        var right=up.CrossProduct(n).GetNormal();
        // Orthographic depth can normalize without changing the projection;
        // only screen-plane centre, directions and mm/pixel define the image.
        // This numeric comparison describes normalization; it cannot decide
        // whether the rendered engineering frame is useful. Report the actual
        // camera and a difference warning instead of refusing a native preset.
        bool same=n.DotProduct(direction)>1-1e-10&&up.DotProduct(actualUp)>1-1e-10&&
             Near(delta.DotProduct(right),0)&&Near(delta.DotProduct(up),0)&&Near(a.pixel_size_mm,b.pixel_size_mm);
        return new(a with{requested_projection_matches=same});
    }
}
