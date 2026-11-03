using System.Text.Json;
using NanoCAD.Contracts;
using Teigha.DatabaseServices;

namespace NanoCADBridge;

internal static class CadRasterImageDisplay390
{
    internal static object Read(RasterImage image)
    {
        var frame=image.Orientation;
        static double[] Point(Teigha.Geometry.Point3d p)=>[p.X,p.Y,p.Z];
        static double[] Vector(Teigha.Geometry.Vector3d v)=>[v.X,v.Y,v.Z];
        using var boundary=image.GetClipBoundary();
        return new{position=Point(frame.Origin),x_axis=Vector(frame.Xaxis),y_axis=Vector(frame.Yaxis),
            show_image=image.ShowImage,is_clipped=image.IsClipped,brightness=image.Brightness,contrast=image.Contrast,fade=image.Fade,
            image_transparency=image.ImageTransparency,show_unaligned=(image.DisplayOptions&ImageDisplayOptions.ShowUnaligned)!=0,
            display_options_raw=(int)image.DisplayOptions,clip_boundary_type=image.ClipBoundaryType.ToString(),
            clip_boundary=boundary.Cast<Teigha.Geometry.Point2d>().Select(p=>new[]{p.X,p.Y}).ToArray(),clip_editing=CadRasterImageClip391.Read(image)};
    }
    internal static void Apply(RasterImage image,RasterImageDisplay390 change)
    {
        var before=Wire.Element(Read(image));
        if(change.Brightness is int brightness)image.Brightness=(byte)brightness;
        if(change.Contrast is int contrast)image.Contrast=(byte)contrast;
        if(change.Fade is int fade)image.Fade=(byte)fade;
        if(change.ShowImage is bool shown)image.ShowImage=shown;
        if(change.ImageTransparency is bool transparent)image.ImageTransparency=transparent;
        if(change.ShowUnaligned is bool unaligned)
            image.DisplayOptions=unaligned?image.DisplayOptions|ImageDisplayOptions.ShowUnaligned:image.DisplayOptions&~ImageDisplayOptions.ShowUnaligned;
        var after=Wire.Element(Read(image));
        var requested=new Dictionary<string,JsonElement>();
        void Expected(string name,object? value){if(value!=null)requested.Add(name,Wire.Element(value));}
        Expected("brightness",change.Brightness);Expected("contrast",change.Contrast);Expected("fade",change.Fade);
        Expected("show_image",change.ShowImage);Expected("image_transparency",change.ImageTransparency);Expected("show_unaligned",change.ShowUnaligned);
        foreach(var field in before.EnumerateObject())
        {
            if(field.Name=="display_options_raw")continue;
            var expected=requested.TryGetValue(field.Name,out var replacement)?replacement:field.Value;
            if(expected.GetRawText()!=after.GetProperty(field.Name).GetRawText())
                throw new DomainException("API_EXCEPTION","Native image adjustment did not retain the requested display or unchanged placement/clipping: "+field.Name+".");
        }
        int allowed=(change.ShowImage.HasValue?(int)ImageDisplayOptions.Show:0)|
            (change.ImageTransparency.HasValue?(int)ImageDisplayOptions.Transparent:0)|
            (change.ShowUnaligned.HasValue?(int)ImageDisplayOptions.ShowUnaligned:0);
        if((before.GetProperty("display_options_raw").GetInt32()&~allowed)!=(after.GetProperty("display_options_raw").GetInt32()&~allowed))
            throw new DomainException("API_EXCEPTION","Native image adjustment changed an unrequested display option.");
    }
}
