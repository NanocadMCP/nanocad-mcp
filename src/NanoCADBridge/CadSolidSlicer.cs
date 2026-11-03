using NanoCAD.Contracts;
using Teigha.DatabaseServices;
using Teigha.Geometry;

namespace NanoCADBridge;

internal static class CadSolidSlicer
{
    internal static Solid3d Apply(Solid3d source,SliceSolidSpec spec,Action cancel)
    {
        cancel();
        using var plane=new Plane(new Point3d(spec.PlaneOrigin.X,spec.PlaneOrigin.Y,spec.PlaneOrigin.Z),
            new Vector3d(spec.PlaneNormal.X,spec.PlaneNormal.Y,spec.PlaneNormal.Z));
        // Native Slice retains the positive half in source and returns the negative
        // half. Keep both; choosing/removing foundry excess is a separate design choice.
        Solid3d? negative=null;
        try
        {
            negative=source.Slice(plane,true);
            if(negative==null||negative.IsNull||source.IsNull)
                throw new DomainException("PLANE_DOES_NOT_SPLIT_SOLID","The plane did not produce two nonempty solid sides.");
            negative.SetPropertiesFrom(source);
            CadSolidEditor.CheckGeometry(source,cancel);CadSolidEditor.CheckGeometry(negative,cancel);
            return negative;
        }
        catch{negative?.Dispose();throw;}
    }
}
