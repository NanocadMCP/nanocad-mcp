using NanoCAD.Contracts;
using Multicad.Mc3D;
using M=Multicad.Geometry;
using G=Teigha.Geometry;
using Db=Teigha.DatabaseServices;

namespace NanoCADBridge;

internal static class DrawingWorkplaneNative
{
    internal static void RequireArcSolverFrame(Multicad.DatabaseServices.McDocument sheet,SketchPlaneSpec frame,DocumentState state,Db.Transaction tx)
    {
        var n=DrawingWorkplane.Normal(frame);var normal=new G.Vector3d(n.X,n.Y,n.Z);
        var canonical=G.Matrix3d.PlaneToWorld(normal).CoordinateSystem3d;
        bool Same(G.Vector3d x,G.Vector3d y)=>(x-canonical.Xaxis).Length<1e-9&&(y-canonical.Yaxis).Length<1e-9;
        bool valid=Same(new(frame.XAxis.X,frame.XAxis.Y,frame.XAxis.Z),new(frame.YAxis.X,frame.YAxis.Y,frame.YAxis.Z));
        using var plane=Plane(frame);using var manager=sheet.Get2dConstraintsManager(Multicad.DatabaseServices.Constraints.Constraint2dNetType.ACAD,sheet.ID,plane);
        if(manager!=null&&!manager.ScopeId.IsNull)
        {
            var native=manager.ScopeId;bool found=state.Document.Database.TryGetObjectId(new Db.Handle(native.Handle),out var id);
            if(!found||id.IsNull||id.IsErased||tx.GetObject(id,Db.OpenMode.ForRead) is not Db.Assoc2dConstraintGroup group)
                throw new DomainException("CONSTRAINT_SCOPE_MISMATCH","Existing arc solver plane cannot be verified.");
            using var persisted=group.WorkPlane;var basis=persisted.GetCoordinateSystem();valid&=Same(basis.Xaxis,basis.Yaxis);
        }
        if(!valid)throw new DomainException("OUT_OF_SCOPE","Arc constraint edits require the canonical entity OCS axes for the workplane normal. A rotated in-plane solver basis changed arc endpoints in native verification; this edit was refused before writing. Geometry creation and source projection are separate operations.");
    }
    internal static M.Plane3d Plane(SketchPlaneSpec f)=>new(new(f.Origin.X,f.Origin.Y,f.Origin.Z),new M.Vector3d(f.XAxis.X,f.XAxis.Y,f.XAxis.Z),new M.Vector3d(f.YAxis.X,f.YAxis.Y,f.YAxis.Z));
    internal static M.Point3d Point(SketchPlaneSpec f,Point2 p){var q=DrawingWorkplane.World(f,p);return new(q.X,q.Y,q.Z);}
    internal static G.Matrix3d Matrix(SketchPlaneSpec f)
    {
        var n=DrawingWorkplane.Normal(f);
        return G.Matrix3d.AlignCoordinateSystem(G.Point3d.Origin,G.Vector3d.XAxis,G.Vector3d.YAxis,G.Vector3d.ZAxis,
            new(f.Origin.X,f.Origin.Y,f.Origin.Z),new(f.XAxis.X,f.XAxis.Y,f.XAxis.Z),new(f.YAxis.X,f.YAxis.Y,f.YAxis.Z),new(n.X,n.Y,n.Z));
    }
    internal static void Match(PlanarSketch sketch,SketchPlaneSpec frame)
    {
        using var plane=sketch.Plane;var o=Point(frame,new(0,0));var x=Point(frame,new(1,0));var y=Point(frame,new(0,1));
        if(new[]{o,x,y}.Any(p=>Math.Abs(plane.SignedDistanceTo(p))>1e-7))throw new DomainException("OUT_OF_SCOPE","Consumer sketch no longer belongs to the source workplane.");
    }
    internal static double Orientation(PlanarSketch sketch,SketchPlaneSpec frame)
    {
        Match(sketch,frame);var n=DrawingWorkplane.Normal(frame);using var plane=sketch.Plane;
        return plane.SignedDistanceTo(new(frame.Origin.X+n.X,frame.Origin.Y+n.Y,frame.Origin.Z+n.Z))>0?1:-1;
    }
    internal static SketchPlaneSpec FromSketch(PlanarSketch sketch,Db.Entity? circularSource=null)
    {
        var o=sketch.TransformPointSketchToWorld(M.Point3d.Origin);var x=sketch.TransformPointSketchToWorld(new(1,0,0))-o;var y=sketch.TransformPointSketchToWorld(new(0,1,0))-o;
        var f=new SketchPlaneSpec(new(o.X,o.Y,o.Z),new(x.X,x.Y,x.Z),new(y.X,y.Y,y.Z));
        G.Vector3d? n=circularSource is Db.Arc a?a.Normal:circularSource is Db.Circle c?c.Normal:null;
        if(n.HasValue&&DrawingWorkplane.Dot(DrawingWorkplane.Normal(f),new(n.Value.X,n.Value.Y,n.Value.Z))<0)f=f with{YAxis=new(-f.YAxis.X,-f.YAxis.Y,-f.YAxis.Z)};
        return f;
    }
}
