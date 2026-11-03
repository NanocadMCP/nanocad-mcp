using NanoCAD.Contracts;
using Teigha.DatabaseServices;
using Teigha.Geometry;

namespace NanoCADBridge;

internal static class CadPointContainment
{
    internal static string Read(Curve source,double[] coordinates,Action check,SketchPlaneSpec? workplane=null)
    {
        if(workplane!=null){DrawingWorkplane.Local(workplane,new(coordinates[0],coordinates[1],coordinates[2]));CadCurveWorkplane.Require(source,workplane,check);}
        bool Normal(Vector3d n)=>n.X==0&&n.Y==0&&Math.Abs(n.Z)==1;
        if(source is Circle circle)
        {
            if(workplane==null&&(circle.Center.Z!=0||!Normal(circle.Normal)))throw new DomainException("PLANAR_GEOMETRY_REQUIRED","Containment requires a WCS XY/Z0 contour.");
            if(!double.IsFinite(circle.Radius)||circle.Radius<=0)throw new DomainException("DEGENERATE_GEOMETRY","Containment requires a positive-radius circle.");
        }
        else if(source is Polyline polyline)
        {
            if(workplane==null&&(polyline.Elevation!=0||!Normal(polyline.Normal)))throw new DomainException("PLANAR_GEOMETRY_REQUIRED","Containment requires a WCS XY/Z0 contour.");
            if(!polyline.Closed)throw new DomainException("CLOSED_BOUNDARY_REQUIRED","Containment requires a closed polyline.");
            if(polyline.NumberOfVertices<3)throw new DomainException("ANALYSIS_LIMIT_EXCEEDED","Containment requires at least three straight polyline vertices.");
            var points=new List<Point2>();var capacity=new SerializedCapacity357(cancel:check);
            for(int i=0;i<polyline.NumberOfVertices;i++)
            {
                check();if(polyline.GetBulgeAt(i)!=0)throw new DomainException("UNSUPPORTED_BOUNDARY","Containment initially supports straight polyline segments only.");
                var point=polyline.GetPoint2dAt(i);var value=new Point2(point.X,point.Y);capacity.Add(value);points.Add(value);
            }
            SimplePolygonValidation.Validate(points.ToArray(),cancel:check);
        }
        else throw new DomainException("UNSUPPORTED_ENTITY_TYPE","Containment supports a native Circle or a simple closed straight Polyline.");
        check();
        var bounds=source.GeometricExtents;check();
        if(NanoCAD.Contracts.PointContainment.OutsideNativeBounds(coordinates,
            new[]{bounds.MinPoint.X,bounds.MinPoint.Y,bounds.MinPoint.Z},new[]{bounds.MaxPoint.X,bounds.MaxPoint.Y,bounds.MaxPoint.Z}))return "outside";
        using var copied=source.Clone() as IDisposable;
        if(copied is not Curve clone||clone.GetType()!=source.GetType()||!clone.ObjectId.IsNull)
            throw new DomainException("NATIVE_CONTAINMENT_MISMATCH","Containment requires an owned detached boundary copy.");
        using var curves=new DBObjectCollection();curves.Add(clone);
        using var regions=Region.CreateFromCurves(curves);
        try
        {
            check();
            if(regions.Count!=1||regions[0] is not Region region||!region.ObjectId.IsNull)
                throw new DomainException("NATIVE_CONTAINMENT_MISMATCH","Expected exactly one temporary native region.");
            using var brep=new Teigha.BoundaryRepresentation.Brep(region);
            int faces=0;string? result=null;
            foreach(Teigha.BoundaryRepresentation.Face face in brep.Faces)
            using(face)
            {
                check();if(++faces!=1)throw new DomainException("NATIVE_CONTAINMENT_MISMATCH","Expected exactly one region face.");
                using var container=face.GetPointContainment(new Point3d(coordinates[0],coordinates[1],coordinates[2]),out var native);
                check();result=NanoCAD.Contracts.PointContainment.Classify(native.ToString(),container==null||container.IsNull);
            }
            if(faces!=1||result==null)throw new DomainException("NATIVE_CONTAINMENT_MISMATCH","Native region has no face.");
            return result;
        }
        finally { foreach(DBObject region in regions)region.Dispose(); }
    }
}
