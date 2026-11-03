using NanoCAD.Contracts;
using Teigha.DatabaseServices;
using Teigha.Geometry;

namespace NanoCADBridge;

internal static class CadSplineCreator
{
    internal static Spline Create(SplineSpec spec)
    {
        // Constructor copies NURBS data into its native object. This input
        // collection is caller-owned; DoubleCollections are managed arrays.
        using var points=new Point3dCollection(spec.ControlPoints.Select(p=>new Point3d(p.X,p.Y,0)).ToArray());
        var spline=new Spline(spec.Degree,spec.Weights.Length!=0,spec.Closed,false,points,new DoubleCollection(spec.Knots),new DoubleCollection(spec.Weights),1e-7,1e-7);
        try { Verify(spline,spec,()=>{});return spline; }
        catch { spline.Dispose();throw; }
    }
    internal static void Verify(Spline spline,SplineSpec spec,Action check)=>SplineCreation.RequireMatch(spec,CadSplineReader.Read(spline,check));
}
