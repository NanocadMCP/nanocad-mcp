using NanoCAD.Contracts;
using Teigha.DatabaseServices;
using Teigha.Geometry;

namespace NanoCADBridge;

internal static class CadRasterCalibration
{
    internal sealed record Plan(CoordinateSystem3d Frame,RasterCalibrationResult Result);
    internal static Plan Prepare(RasterImage image,RasterCalibration calibration)
    {
        var transform=image.PixelToModelTransform;
        var inverse=transform.Inverse();
        var old=image.Orientation;
        var x=Vector3d.XAxis.TransformBy(transform);var y=Vector3d.YAxis.TransformBy(transform);
        int hand=Math.Sign(x.CrossProduct(y).Z);
        if (hand==0) throw new DomainException("INVALID_GEOMETRY","The current raster pixel frame is degenerate in drawing XY.");
        Point3d NewFramePoint(Point3d world)
        {
            var p=world.TransformBy(inverse);
            var mapped=calibration.Map(new(p.X,p.Y),hand);
            return new(mapped.X,mapped.Y,0);
        }
        // Use native coordinates for both corners and pixel centres, including
        // its image-axis convention and any half-pixel origin offset.
        var origin=NewFramePoint(old.Origin);
        var frame=new CoordinateSystem3d(origin, NewFramePoint(old.Origin+old.Xaxis)-origin,NewFramePoint(old.Origin+old.Yaxis)-origin);
        var (a,b)=calibration.Coefficients(hand);
        double? error=null;
        if (calibration.CheckPoint is {} check)
        {
            var projected=calibration.Map(check.Pixel,hand);
            error=Distance(projected,check.Drawing);
        }
        var result=new RasterCalibrationResult(Math.Sqrt(a*a+b*b),error);
        if (!double.IsFinite(result.mm_per_pixel)||result.mm_per_pixel<=0 || error.HasValue&&!double.IsFinite(error.Value))
            throw new DomainException("INVALID_GEOMETRY","The calibration scale or residual is outside the numeric range.");
        if (error>calibration.MaxCheckErrorMm)
            throw new DomainException("CALIBRATION_CHECK_FAILED","Independent control point exceeds the requested tolerance.",false,result);
        return new(frame,result);
    }
    static double Distance(Point2 a,Point2 b)=>Math.Sqrt(Math.Pow(a.X-b.X,2)+Math.Pow(a.Y-b.Y,2));
    internal static RasterCalibrationResult Apply(RasterImage image,RasterCalibration request,Plan prepared)
    {
        using var before=image.GetClipBoundary();
        var flags=image.DisplayOptions;
        image.Orientation=prepared.Frame;
        var matrix=image.PixelToModelTransform;
        Point2 Read(Point2 pixel)
        {
            var point=new Point3d(pixel.X,pixel.Y,0).TransformBy(matrix);
            return new(point.X,point.Y);
        }
        foreach (var anchor in new[]{request.First,request.Second})
        {
            var actual=Read(anchor.Pixel);
            if (!ModelParameterSnapshot.Near(anchor.Drawing.X,actual.X)||!ModelParameterSnapshot.Near(anchor.Drawing.Y,actual.Y))
                throw new DomainException("API_EXCEPTION","The native raster pixel transformation did not retain a calibration anchor.");
        }
        double? error=request.CheckPoint is {} check ? Distance(Read(check.Pixel),check.Drawing) : null;
        using var after=image.GetClipBoundary();
        if (flags!=image.DisplayOptions||!before.Cast<Point2d>().SequenceEqual(after.Cast<Point2d>())||
            (error is double residual && (!ModelParameterSnapshot.Near(residual,prepared.Result.check_error_mm!.Value)||residual>request.MaxCheckErrorMm)))
            throw new DomainException("API_EXCEPTION","Raster calibration changed clipping or failed independent control-point readback.");
        return prepared.Result with {check_error_mm=error};
    }
}
