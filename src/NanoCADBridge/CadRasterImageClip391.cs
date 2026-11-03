using System.Text.Json;
using NanoCAD.Contracts;
using Teigha.DatabaseServices;
using Teigha.Geometry;

namespace NanoCADBridge;

// Native display clipping. Source pixels, definition and physical frame stay
// untouched; WCS inputs use the actual native pixel-to-model transform.
internal static class CadRasterImageClip391
{
    static double[] P(Point3d p)=>[p.X,p.Y,p.Z];
    static bool Enabled(RasterImage image)=>(image.DisplayOptions&ImageDisplayOptions.Clip)!=0;
    internal static object Read(RasterImage image)
    {
        double[][]? world=null;string? reason=null;
        try{var transform=image.PixelToModelTransform;using var points=image.GetClipBoundary();world=points.Cast<Point2d>().Select(p=>P(new Point3d(p.X,p.Y,0).TransformBy(transform))).ToArray();}
        catch(System.Exception error){reason=error.GetType().Name+": "+error.Message;if(reason.Length>1024)reason=reason[..1024];}
        return new{enabled=Enabled(image),boundary_world=world,unavailable_reason=reason};
    }
    static Point3d[] Normalize(Point3d[] points,double tolerance)
    {
        if(points.Length>1&&points[0].DistanceTo(points[^1])<=tolerance)points=points[..^1];
        var ring=new LinkedList<Point3d>(points);var node=ring.First;int unchanged=0;
        while(ring.Count>3&&unchanged<ring.Count)
        {
            var previous=node!.Previous??ring.Last!;var next=node.Next??ring.First!;
            var direction=next.Value-previous.Value;double squared=direction.DotProduct(direction);
            double projection=(node.Value-previous.Value).DotProduct(direction);
            if(squared>0&&projection>=0&&projection<=squared&&node.Value.DistanceTo(previous.Value+direction*(projection/squared))<=tolerance)
            {ring.Remove(node);node=previous;unchanged=0;}
            else{node=next;unchanged++;}
        }
        return ring.ToArray();
    }
    internal static Point2d[] Resolve(RasterImage image,SetRasterImageClip391 edit,Action cancel)
    {
        if(edit.Mode=="clear")return [];
        var transform=image.PixelToModelTransform;var inverse=transform.Inverse();double tolerance=Tolerance.Global.EqualPoint;
        var points=Normalize(edit.Boundary!.Select(p=>new Point3d(p.X,p.Y,p.Z)).ToArray(),tolerance);
        if(points.Length<3)throw new DomainException("INVALID_ARGUMENT","The clip polygon requires three distinct image-plane vertices.");
        var result=new List<Point2d>(points.Length);
        foreach(var point in points)
        {
            cancel();var pixel=point.TransformBy(inverse);var projected=new Point3d(pixel.X,pixel.Y,0).TransformBy(transform);
            if(!double.IsFinite(pixel.X)||!double.IsFinite(pixel.Y)||projected.DistanceTo(point)>tolerance)
                throw new DomainException("INVALID_ARGUMENT","Every clip vertex must lie on this actual native raster plane; no WCS XY assumption or silent projection is used.");
            result.Add(new(pixel.X,pixel.Y));
        }
        for(int index=0;index<points.Length;index++)
            if(points[index].DistanceTo(points[(index+1)%points.Length])<=tolerance)
                throw new DomainException("INVALID_ARGUMENT","A clip polygon cannot have zero-length adjacent edges.");
        return result.ToArray();
    }
    static bool SamePolygon(Point3d[] expected,Point3d[] actual,double tolerance)
    {
        expected=Normalize(expected,tolerance);actual=Normalize(actual,tolerance);
        if(expected.Length!=actual.Length)return false;
        for(int offset=0;offset<actual.Length;offset++)foreach(int direction in new[]{1,-1})
        {
            bool same=true;for(int index=0;index<expected.Length;index++)
                if(expected[index].DistanceTo(actual[(offset+direction*index+actual.Length)%actual.Length])>tolerance){same=false;break;}
            if(same)return true;
        }
        return false;
    }
    internal static void Apply(RasterImage image,SetRasterImageClip391 edit,Action cancel)
    {
        var points=Resolve(image,edit,cancel);var before=Wire.Element(CadRasterImageDisplay390.Read(image));cancel();
        if(edit.Mode=="clear")
        {image.SetClipBoundaryToWholeImage();image.DisplayOptions&=~ImageDisplayOptions.Clip;}
        else
        {using var boundary=new Point2dCollection(points);image.SetClipBoundary(ClipBoundaryType.Poly,boundary);image.DisplayOptions|=ImageDisplayOptions.Clip;}
        cancel();var after=Wire.Element(CadRasterImageDisplay390.Read(image));
        foreach(var field in before.EnumerateObject())
        {
            if(field.Name is "is_clipped" or "clip_boundary_type" or "clip_boundary" or "clip_editing" or "display_options_raw")continue;
            if(field.Value.GetRawText()!=after.GetProperty(field.Name).GetRawText())throw new DomainException("API_EXCEPTION","Native raster clipping changed retained placement or appearance: "+field.Name+".");
        }
        int mask=~(int)ImageDisplayOptions.Clip;
        if((before.GetProperty("display_options_raw").GetInt32()&mask)!=(after.GetProperty("display_options_raw").GetInt32()&mask)||Enabled(image)!=(edit.Mode=="polygon"))
            throw new DomainException("API_EXCEPTION","Native raster clipping changed an unrequested display option or did not retain the requested clip enable flag.");
        if(edit.Mode=="polygon")
        {
            using var actual=image.GetClipBoundary();var transform=image.PixelToModelTransform;
            if(!SamePolygon(edit.Boundary!.Select(p=>new Point3d(p.X,p.Y,p.Z)).ToArray(),actual.Cast<Point2d>().Select(p=>new Point3d(p.X,p.Y,0).TransformBy(transform)).ToArray(),Tolerance.Global.EqualPoint))
                throw new DomainException("API_EXCEPTION","Native clip boundary differs from the requested WCS contour; refresh the actual native image before dependent work.");
        }
    }
}
