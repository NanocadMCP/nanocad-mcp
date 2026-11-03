using System.Text.Json;
using Multicad;
using Multicad.Mc3D;
using NanoCAD.Contracts;
using G=Multicad.Geometry;

namespace NanoCADBridge;

// SDK abstract geometry IDs have selected-sketch membership independently of
// resident object inclusion. This helper proves only that narrow Spline case;
// it does not assign edit ownership or replace native marker correspondence.
internal static class NativeSketchMembership445
{
    internal static Dictionary<McObjectId,JsonElement> AbstractSplines(PlanarSketch sketch,Action cancel,IReadOnlySet<McObjectId>? excluded=null)
    {
        cancel();var owner=sketch.DbEntity.Document;
        NativeSketchMembers385.Need(!sketch.ID.IsNull&&owner!=null&&!owner.ID.IsNull,"SKETCH_BINDING_UNAVAILABLE","Abstract Spline observation requires the actual selected sketch and owner.");
        var origin=sketch.TransformPointSketchToWorld(G.Point3d.Origin);var x=sketch.TransformPointSketchToWorld(new G.Point3d(1,0,0))-origin;var y=sketch.TransformPointSketchToWorld(new G.Point3d(0,1,0))-origin;
        var plane=new SketchPlaneSpec(new(origin.X,origin.Y,origin.Z),new(x.X,x.Y,x.Z),new(y.X,y.Y,y.Z));
        NativeSketchMembers385.Need(new[]{origin.X,origin.Y,origin.Z,x.X,x.Y,x.Z,y.X,y.Y,y.Z}.All(double.IsFinite)&&Math.Abs(DrawingWorkplane.Dot(plane.XAxis,plane.XAxis)-1)<1e-9&&Math.Abs(DrawingWorkplane.Dot(plane.YAxis,plane.YAxis)-1)<1e-9&&Math.Abs(DrawingWorkplane.Dot(plane.XAxis,plane.YAxis))<1e-9,
            "SKETCH_FRAME_UNAVAILABLE","Abstract Spline observation requires the actual finite orthonormal sketch frame.");
        var geometry=sketch.GetAllItemsGeometry(false);NativeSketchMembers385.Need(geometry!=null,"SKETCH_GEOMETRY_UNAVAILABLE","The selected sketch's actual WCS geometry-ID inventory is unavailable.");
        var result=new Dictionary<McObjectId,JsonElement>();
        try
        {
            ModelInventoryBudget.Ids(geometry.Keys,cancel);
            foreach(var pair in geometry)
            {
                cancel();if(pair.Value?.GeometryType!=G.EntityGeomType.kSpline||excluded?.Contains(pair.Key)==true||pair.Key.GetObject()!=null)continue;
                NativeSketchMembers385.Need(!pair.Key.IsNull,"SKETCH_BINDING_UNAVAILABLE","An abstract native Spline has no actual geometry identity.");
                using var spline=pair.Value.Nurb;var observed=NativeSketchCurveGeometry445.Spline(spline,cancel);
                NativeSketchCurveGeometry445.Curve(pair.Key.ToString(),observed,plane,cancel);
                using var item=sketch.GetItemGeometry(pair.Key,false);
                NativeSketchMembers385.Need(item!=null&&item.GeometryType==G.EntityGeomType.kSpline&&pair.Key.GetObject()==null,"SKETCH_BINDING_UNAVAILABLE","The abstract Spline lacks its exact independent WCS item geometry or acquired resident object storage.");
                using var itemSpline=item.Nurb;
                NativeSketchMembers385.Need(ModelParameterSnapshot.Equivalent(observed,NativeSketchCurveGeometry445.Spline(itemSpline,cancel)),"SKETCH_GEOMETRY_UNAVAILABLE","The abstract Spline's complete WCS item geometry differs from its geometry-ID map.");
                result.Add(pair.Key,observed);
            }
        }
        finally{foreach(var value in geometry.Values.Distinct(ReferenceEqualityComparer.Instance))if(value is IDisposable disposable)disposable.Dispose();}
        NativeSketchMembers385.Need(sketch.DbEntity.Document?.ID==owner.ID,"SKETCH_BINDING_UNAVAILABLE","The abstract Spline's selected-sketch owner changed during observation.");
        return result;
    }
    internal static bool Member(PlanarSketch sketch,McObjectId id,G.EntityGeometry? item,Action cancel)
    {
        cancel();if(id.IsNull||item==null)return false;
        if(sketch.ContainsObject(id))return true;
        if(item.GeometryType!=G.EntityGeomType.kSpline||id.GetObject()!=null)return false;
        var observed=AbstractSplines(sketch,cancel);if(!observed.TryGetValue(id,out var actual))return false;
        using var spline=item.Nurb;return ModelParameterSnapshot.Equivalent(actual,NativeSketchCurveGeometry445.Spline(spline,cancel));
    }
}
