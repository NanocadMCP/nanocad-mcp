using NanoCAD.Contracts;
using Teigha.DatabaseServices;
using Teigha.Geometry;

namespace NanoCADBridge;

internal static class CadEllipseReader
{
    internal static EllipseGeometry Read(Ellipse e,Action check)
    {
        if(e.GetType()!=typeof(Ellipse))throw new DomainException("UNSUPPORTED_ENTITY_TYPE","Only exact native Ellipse geometry is supported.");
        static double[] Point(Point3d p)=>new[]{p.X,p.Y,p.Z};
        static double[] Vector(Vector3d p)=>new[]{p.X,p.Y,p.Z};
        check();var data=new EllipseGeometry(Point(e.Center),Vector(e.MajorAxis),Vector(e.MinorAxis),e.MajorRadius,e.MinorRadius,e.RadiusRatio,
            Vector(e.Normal),Point(e.StartPoint),Point(e.EndPoint),e.StartParam,e.EndParam,e.Closed);
        check();EllipseVerification.Verify(data);return data;
    }
    internal static double Length(Ellipse e,Action check)
    {
        var data=Read(e,check);double first=e.GetDistanceAtParameter(data.start_parameter);check();
        double last=e.GetDistanceAtParameter(data.end_parameter);check();
        return EllipseVerification.LengthFromDistances(first,last);
    }
}
