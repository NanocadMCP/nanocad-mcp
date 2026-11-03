using NanoCAD.Contracts;
using Teigha.DatabaseServices;
using Teigha.Geometry;

namespace NanoCADBridge;

// Validate the native WCS geometry; never flatten or transform its database
// entity (or a dependency-bearing clone) for planar analysis.
internal static class CadCurveWorkplane
{
    internal static Vector3d Require(Curve curve,SketchPlaneSpec frame,Action check)
    {
        var n=DrawingWorkplane.Normal(frame);var normal=new Vector3d(n.X,n.Y,n.Z);
        void Point(Point3d p)=>DrawingWorkplane.Local(frame,new(p.X,p.Y,p.Z));
        void Normal(Vector3d actual)
        {
            if(!double.IsFinite(actual.X)||!double.IsFinite(actual.Y)||!double.IsFinite(actual.Z)||Math.Abs(Math.Abs(actual.DotProduct(normal))-1)>1e-10||actual.CrossProduct(normal).Length>1e-10)
                throw new DomainException("PLANAR_GEOMETRY_REQUIRED","Curve normal differs from the requested workplane; no projection performed.");
        }
        switch(curve)
        {
            case Line line:Point(line.StartPoint);Point(line.EndPoint);break;
            case Circle circle:Point(circle.Center);Normal(circle.Normal);break;
            case Arc arc:Point(arc.Center);Normal(arc.Normal);break;
            case Polyline polyline:
                Normal(polyline.Normal);
                for(int i=0;i<polyline.NumberOfVertices;i++){check();Point(polyline.GetPoint3dAt(i));}
                break;
            default:throw new DomainException("UNSUPPORTED_ENTITY_TYPE","Explicit-plane analysis supports native Line/Circle/Arc/Polyline only.");
        }
        check();return normal;
    }
}
