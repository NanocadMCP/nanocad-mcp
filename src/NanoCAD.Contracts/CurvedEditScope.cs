namespace NanoCAD.Contracts;

public static class CurvedEditScope
{
    public static void Require(EllipseGeometry ellipse)
    {
        EllipseVerification.Verify(ellipse);
        if(ellipse.center[2]!=0 || ellipse.major_axis[2]!=0 || ellipse.minor_axis[2]!=0 ||
            ellipse.normal[0]!=0 || ellipse.normal[1]!=0 || ellipse.normal[2]<=0)
            throw new DomainException("OUT_OF_SCOPE","Ellipse edits require WCS XY/Z=0 and normal+Z; no projection is performed.");
        if(ellipse.major_radius<1e-9 || ellipse.minor_radius<1e-9)
            throw new DomainException("INVALID_GEOMETRY","Edited ellipse radii must remain at least1e-9 drawing units.");
    }
    public static void Require(SplineGeometry spline)
    {
        SplineVerification.Verify(spline);
        if(spline.closed&&!spline.control_points[0].SequenceEqual(spline.control_points[^1]))
            throw new DomainException("UNSUPPORTED_SPLINE","Closed spline edits require exactly repeated first/last controls in the verified clamped representation.");
        if(!SplineVerification.LiesInWcsXy(spline))
            throw new DomainException("OUT_OF_SCOPE","Spline edits require every control point in WCS XY/Z=0; no projection is performed.");
    }
}
