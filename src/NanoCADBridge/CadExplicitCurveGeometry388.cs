using NanoCAD.Contracts;
using Teigha.DatabaseServices;
using Teigha.Geometry;

namespace NanoCADBridge;

// Direct geometry setters use WCS points. Never flatten the resident object,
// alter UCS, transform owned metadata or manufacture a parametric dependency.
internal static class CadExplicitCurveGeometry388
{
    internal static SketchPlaneSpec? Frame(EditSpec[] operations,string handle)
    {
        var relevant=operations.Where(op=>op.Handles.Contains(handle)).ToArray();
        var edits=relevant.OfType<CurveGeometrySpec>().Where(op=>op.Workplane!=null).ToArray();
        if(edits.Length==0)return null;
        var frame=edits[0].Workplane!;
        if(relevant.Any(op=>op is not PropertiesSpec && (op is not CurveGeometrySpec curve || curve.Workplane!=frame)))
            throw new DomainException("INVALID_OPERATION_ORDER","An explicit workplane geometry edit cannot share a target with legacy transforms or a different frame.");
        return frame;
    }
    private static Point3d World(SketchPlaneSpec frame,Point2 p)
    {var q=DrawingWorkplane.World(frame,p);return new(q.X,q.Y,q.Z);}
    internal static void Check(Entity entity,SketchPlaneSpec frame,Action cancel)
    {
        cancel();
        if(CadPlaneCurveGeometry393.Handles(entity)){CadPlaneCurveGeometry393.Check(entity,frame,cancel);return;}
        if(entity.GetType()!=typeof(Line)&&entity.GetType()!=typeof(Circle))
            throw new DomainException("UNSUPPORTED_ENTITY_TYPE","Explicit-plane geometry replacement supports exact independent native Line/Circle/Arc/Ellipse/Spline.");
        _=CadCurveWorkplane.Require((Curve)entity,frame,cancel);
        static bool Finite(Vector3d n)=>double.IsFinite(n.X)&&double.IsFinite(n.Y)&&double.IsFinite(n.Z)&&Math.Abs(n.Length-1)<=1e-10;
        if(entity is Line line)
        {
            var length=line.StartPoint.DistanceTo(line.EndPoint);
            if(!double.IsFinite(line.Thickness)||Math.Abs(line.Thickness)>1e-9||!Finite(line.Normal)||!double.IsFinite(length)||length<1e-9)
                throw new DomainException("INVALID_GEOMETRY","Line requires finite distinct points, a finite unit normal and zero thickness; no flattening is performed.");
        }
        else if(entity is Circle circle && (!double.IsFinite(circle.Radius)||circle.Radius<1e-9||circle.Radius>1e9||!double.IsFinite(circle.Thickness)||Math.Abs(circle.Thickness)>1e-9||!Finite(circle.Normal)))
            throw new DomainException("INVALID_GEOMETRY","Circle requires a finite positive radius, a finite unit normal and zero thickness.");
    }
    private static void Require(Entity entity,CurveEditGeometry geometry,SketchPlaneSpec frame,string error)
    {
        bool Same(Point3d p,Point2 q)=>p.DistanceTo(World(frame,q))<=1e-7;
        bool matches=entity is Line line && geometry.Type=="Line" && Same(line.StartPoint,geometry.Start!)&&Same(line.EndPoint,geometry.End!) ||
            entity is Circle circle && geometry.Type=="Circle" && Same(circle.Center,geometry.Center!)&&Math.Abs(circle.Radius-geometry.Radius)<=1e-7;
        if(!matches)throw new DomainException(error,"Current curve differs from the explicit-frame expected native geometry.");
    }
    internal static void Apply(Entity entity,CurveGeometrySpec edit,Action cancel)
    {
        var frame=edit.Workplane??throw new DomainException("INVALID_ARGUMENT","An explicit workplane is required.");
        if(CadPlaneCurveGeometry393.Handles(entity)){CadPlaneCurveGeometry393.Apply(entity,edit,cancel);return;}
        Check(entity,frame,cancel);Require(entity,edit.ExpectedGeometry,frame,"GEOMETRY_CHANGED");
        Vector3d normal=entity is Line line?line.Normal:((Circle)entity).Normal;
        if(entity is Line target){target.StartPoint=World(frame,edit.Geometry.Start!);target.EndPoint=World(frame,edit.Geometry.End!);}
        else {var circleTarget=(Circle)entity;circleTarget.Center=World(frame,edit.Geometry.Center!);circleTarget.Radius=edit.Geometry.Radius;}
        Check(entity,frame,cancel);Require(entity,edit.Geometry,frame,"NATIVE_GEOMETRY_MISMATCH");
        var actual=entity is Line changed?changed.Normal:((Circle)entity).Normal;
        if((actual-normal).Length>1e-10)throw new DomainException("NATIVE_GEOMETRY_MISMATCH","The native direct setter changed the source normal.");
    }
}
