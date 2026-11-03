using NanoCAD.Contracts;
using Teigha.DatabaseServices;
using Teigha.Geometry;

namespace NanoCADBridge;

// Normalize only a proved independent detached preview for the existing exact
// XY geometry contract. Resident/source properties are never transformed.
internal static class CadPlaneCurveGeometry393
{
    internal static bool Handles(Entity entity)=>entity.GetType()==typeof(Arc)||entity.GetType()==typeof(Ellipse)||entity.GetType()==typeof(Spline);
    static Entity Local(Entity source,SketchPlaneSpec frame,Action cancel)
    {
        var copy=CadContourBudget344.Detached(source);
        try
        {
            cancel();copy.TransformBy(DrawingWorkplaneNative.Matrix(frame).Inverse());
            // This preserves the established exact Arc/Ellipse/Spline geometry
            // scope, including complete clamped NURBS expectations. Opposite
            // circular/elliptical normals need the intended oriented frame.
            CheckLocal(copy,cancel);return copy;
        }
        catch{copy.Dispose();throw;}
    }
    internal static void CheckLocal(Entity entity,Action cancel)
    {
        // Native frame inversion has ordinary floating-point roundoff. Use the
        // established geometry readback tolerance only for this proved local
        // copy; do not snap/project any point or relax legacy XY admissions.
        bool NearZero(double value)=>double.IsFinite(value)&&Math.Abs(value)<=EllipseVerification.Tolerance;
        if(entity is Ellipse ellipse)
        {
            var data=CadEllipseReader.Read(ellipse,cancel);
            if(!NearZero(data.center[2])||!NearZero(data.major_axis[2])||!NearZero(data.minor_axis[2])||
                !NearZero(data.normal[0])||!NearZero(data.normal[1])||!NearZero(data.normal[2]-1))
                throw new DomainException("OUT_OF_SCOPE","The existing Ellipse is not in the declared oriented plane; no projection is performed.");
        }
        else if(entity is Spline spline)
        {
            var data=CadSplineReader.Read(spline,cancel);
            if(!data.planar||data.control_points.Any(p=>!NearZero(p[2]))||
                data.closed&&!data.control_points[0].SequenceEqual(data.control_points[^1]))
                throw new DomainException("OUT_OF_SCOPE","The existing complete clamped Spline is not in the declared plane; no projection is performed.");
        }
        else CadModifier.CheckGeometry(entity,cancel);
    }
    internal static void Check(Entity entity,SketchPlaneSpec frame,Action cancel)
    {
        if(!Handles(entity))throw new DomainException("UNSUPPORTED_ENTITY_TYPE","Explicit-plane replacement supports exact Line/Circle/Arc/Ellipse/Spline.");
        using var copy=Local(entity,frame,cancel);
    }
    internal static void VerifyCreated(Entity entity,EntitySpec spec,Action cancel)
    {
        // Creation applies the frame to a newly constructed detached root.
        // Verify the actual resident shape in the declared local frame without
        // transforming that resident entity or its owned/display properties.
        Entity? local=null;
        try
        {
            var actual=spec.Workplane is {} frame?(local=Local(entity,frame,cancel)):entity;
            if(actual is Ellipse ellipse&&spec is EllipseSpec ellipseSpec)CadEllipseCreator.Verify(ellipse,ellipseSpec,cancel);
            else if(actual is Spline spline&&spec is SplineSpec splineSpec)SplineCreation.RequireMatch(splineSpec,CadSplineReader.Read(spline,cancel),spec.Workplane==null?0:EllipseVerification.Tolerance);
            else throw new DomainException("UNSUPPORTED_ENTITY_TYPE","Expected the created exact Ellipse/Spline geometry.");
        }
        finally{local?.Dispose();}
    }
    internal static void Apply(Entity source,CurveGeometrySpec edit,Action cancel)
    {
        var frame=edit.Workplane??throw new DomainException("INVALID_ARGUMENT","Use the intended explicit oriented workplane.");
        var id=source.ObjectId;var owner=source.OwnerId;
        using var prepared=Local(source,frame,cancel);
        CadCurveGeometryEditor.Apply(prepared,edit with{Workplane=null},cancel,localPlane:true);
        cancel();prepared.TransformBy(DrawingWorkplaneNative.Matrix(frame));
        if(source is Arc arc&&prepared is Arc changedArc)
        {
            if((arc.Normal-changedArc.Normal).Length>1e-10)throw new DomainException("NATIVE_GEOMETRY_MISMATCH","Prepared Arc differs from the retained source normal.");
            // Native angles are now in this Arc's OCS. Its original normal,
            // thickness and stored display/owned properties remain untouched.
            arc.Center=changedArc.Center;arc.Radius=changedArc.Radius;arc.StartAngle=changedArc.StartAngle;arc.EndAngle=changedArc.EndAngle;
        }
        else if(source is Ellipse ellipse&&prepared is Ellipse changedEllipse)
        {
            if((ellipse.Normal-changedEllipse.Normal).Length>1e-10)throw new DomainException("NATIVE_GEOMETRY_MISMATCH","Prepared Ellipse differs from the retained source normal.");
            // Set uses angle arguments. As in the existing XY adapter, create
            // the full ellipse first, then assign native parameters explicitly.
            ellipse.Set(changedEllipse.Center,ellipse.Normal,changedEllipse.MajorAxis,changedEllipse.RadiusRatio,0,2*Math.PI);
            CadEllipseCreator.SetParameter(ellipse,"set_StartParam",changedEllipse.StartParam);
            CadEllipseCreator.SetParameter(ellipse,"set_EndParam",changedEllipse.EndParam);
        }
        else if(source is Spline spline&&prepared is Spline changedSpline)
        {
            var data=changedSpline.NurbsData;
            // The installed getter supplies independent points. Keep that
            // backing collection alive until the native setter copies it.
            using var points=data.GetControlPoints();cancel();spline.NurbsData=data;
        }
        else throw new DomainException("UNSUPPORTED_ENTITY_TYPE","Expected the same exact native curve type; no conversion.");
        cancel();if(source.ObjectId!=id||source.OwnerId!=owner)throw new DomainException("NATIVE_GEOMETRY_MISMATCH","Native geometry setters changed the retained curve identity/owner.");
        using var actual=Local(source,frame,cancel);
        CadCurveGeometryEditor.Require(actual,edit.Geometry,"NATIVE_GEOMETRY_MISMATCH",cancel,localPlane:true);
    }
}
