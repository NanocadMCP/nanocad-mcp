using NanoCAD.Contracts;
using Db=Teigha.DatabaseServices;
using G=Teigha.Geometry;

namespace NanoCADBridge;

internal static class DrawingSplineConstraints445
{
    internal static SketchConstraintCurve Capture(string handle,Db.Spline spline,SketchPlaneSpec frame,Action cancel)
    {
        cancel();var shape=CadNativeCurveInspection342.ReadSpline(spline,cancel,NativeCurveRead342.SnapshotByteBudget);
        var retained=CurveSplineGeometry445.FromNative(shape);
        SplineConstraintGeometry445.Complete(retained);
        if(!shape.planar)throw new DomainException("PLANAR_GEOMETRY_REQUIRED","Drawing Spline constraints require actual planar native geometry in the selected workplane; no projection is performed.");
        Point2 Local(double[] point)=>DrawingWorkplane.Local(frame,new(point[0],point[1],point[2]));
        foreach(var point in shape.control_points){cancel();_=Local(point);}
        if(shape.native_inspection.fit_data is {} fit)foreach(var point in fit.points){cancel();_=Local(point);}
        Point2? Derivative(double parameter)
        {
            cancel();G.Vector3d vector;
            try{vector=spline.GetFirstDerivative(parameter);}
            catch(Teigha.Runtime.Exception){cancel();return null;}
            var v=new Point3(vector.X,vector.Y,vector.Z);
            if(!new[]{v.X,v.Y,v.Z}.All(double.IsFinite)||Math.Abs(DrawingWorkplane.Dot(v,DrawingWorkplane.Normal(frame)))>1e-7*Math.Max(1,vector.Length))
                throw new DomainException("NATIVE_SPLINE_TANGENT_UNAVAILABLE","The complete native endpoint derivative must lie in the selected workplane.");
            return new(DrawingWorkplane.Dot(v,frame.XAxis),DrawingWorkplane.Dot(v,frame.YAxis));
        }
        // Closed/periodic curves have no unambiguous open endpoint roles. Their
        // complete definition remains usable for whole-object anchors.
        bool endpoints=!shape.closed&&!shape.periodic;
        return new(handle,"spline",Local(shape.start_point),Local(shape.end_point),Spline:retained,
            StartTangent:endpoints?Derivative(shape.start_parameter):null,EndTangent:endpoints?Derivative(shape.end_parameter):null);
    }
}
