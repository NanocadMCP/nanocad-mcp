using System.Text.Json;
using static NanoCAD.Contracts.StructureWriteValidation;

namespace NanoCAD.Contracts;

public sealed record RasterControlPoint(Point2 Pixel, Point2 Drawing);
public sealed record RasterCalibration(RasterControlPoint First, RasterControlPoint Second, RasterControlPoint? CheckPoint,
    double? MaxCheckErrorMm)
{
    public static RasterCalibration Parse(JsonElement value, string path)
    {
        Shape(value, path, "anchors", "check_point", "max_check_error_mm");
        RasterControlPoint Pair(JsonElement pair, string at)
        {
            Shape(pair, at, "pixel", "drawing");
            return new(ExternalReferenceValidation385.Point(Required(pair,"pixel",at),at+".pixel"),
                ExternalReferenceValidation385.Point(Required(pair,"drawing",at),at+".drawing"));
        }
        var anchors=Required(value,"anchors",path);
        if (anchors.ValueKind!=JsonValueKind.Array || anchors.GetArrayLength()!=2) throw Invalid(path+".anchors","Supply two pixel/drawing control pairs.");
        var a=Pair(anchors[0],path+".anchors[0]"); var b=Pair(anchors[1],path+".anchors[1]");
        if (a.Pixel==b.Pixel || a.Drawing==b.Drawing) throw Invalid(path+".anchors","Both pixel and drawing baselines must have distinct endpoints.");
        var check=value.TryGetProperty("check_point",out var c)?Pair(c,path+".check_point"):null;
        double? limit=value.TryGetProperty("max_check_error_mm",out var t)?ExternalReferenceValidation385.Number(t,path+".max_check_error_mm"):null;
        if (limit<0 || limit.HasValue && check==null) throw Invalid(path+".max_check_error_mm","A nonnegative tolerance requires a check_point.");
        return new(a,b,check,limit);
    }
    public (double A,double B) Coefficients(int handedness)
    {
        double x=Second.Pixel.X-First.Pixel.X,y=handedness*(Second.Pixel.Y-First.Pixel.Y);
        double u=Second.Drawing.X-First.Drawing.X,v=Second.Drawing.Y-First.Drawing.Y;
        double baseline=x*x+y*y, a=(u*x+v*y)/baseline,b=(v*x-u*y)/baseline;
        if (!double.IsFinite(a) || !double.IsFinite(b) || (a==0 && b==0)) throw Invalid("calibration.anchors","Control baselines do not define a finite positive scale.");
        return (a,b);
    }
    public Point2 Map(Point2 pixel,int handedness)
    {
        var (a,b)=Coefficients(handedness);
        var x=pixel.X-First.Pixel.X;var y=handedness*(pixel.Y-First.Pixel.Y);
        var mapped=new Point2(First.Drawing.X+a*x-b*y,First.Drawing.Y+b*x+a*y);
        if (!double.IsFinite(mapped.X)||!double.IsFinite(mapped.Y)) throw Invalid("calibration","The requested transformation exceeds the coordinate range.");
        return mapped;
    }
}
public sealed record RasterCalibrationResult(double mm_per_pixel,double? check_error_mm);
