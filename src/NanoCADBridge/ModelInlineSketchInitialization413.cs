using NanoCAD.Contracts;

namespace NanoCADBridge;

// Adapt the existing inline contour to the same resident edit-sheet inputs as
// create_model_sketch. Sample3dObject.cs:872-890,943-959 documents that lifecycle.
// Selection stays AutoProcessExternalContours: no inferred interior point and
// no external drawing source or synthetic constraint is introduced.
internal static class ModelInlineSketchInitialization413
{
    internal static NanoCAD.Contracts.NativeSketchInitialization385 Input(ModelExtrusionSpec spec,Action cancel)
    {
        Point3 World(Point2 point)=>ModelExtrusionGeometry.World(spec.Plane,point);
        if(spec.Profile.Kind=="circle")
            return Validate([new("contour_0","circle",[World(spec.Profile.Center!)],spec.Profile.Radius)],spec.Plane,cancel);
        var vertices=spec.Profile.Vertices!;
        var curves=spec.Profile.Kind=="polycurve"?ModelPolycurve.Curves(spec.Profile,cancel):
            Enumerable.Range(0,vertices.Length).Select(i=>new SketchConstraintCurve(i.ToString(),"line",vertices[i],vertices[(i+1)%vertices.Length])).ToArray();
        var geometry=new List<NativeSketchPrimitive385>();
        foreach(var curve in curves)
        {
            cancel();
            Point3[] points=curve.Kind=="line"?[World(curve.Start!),World(curve.End!)]:
                [World(curve.Start!),World(ModelPolycurve.Midpoint(curve)),World(curve.End!)];
            geometry.Add(new("contour_"+geometry.Count,curve.Kind,points,null));
        }
        return Validate(geometry.ToArray(),spec.Plane,cancel);
    }
    static NanoCAD.Contracts.NativeSketchInitialization385 Validate(NativeSketchPrimitive385[] geometry,SketchPlaneSpec plane,Action cancel)
    {
        // Use the native input adapter's complete validation before the writer
        // starts its undo point. A legacy contour can fit its local limits yet
        // exceed this adapter's WCS or three-point arc range after placement.
        var rows=geometry.Select(item=>
        {
            cancel();var points=item.Points.Select(point=>new[]{point.X,point.Y,point.Z}).ToArray();
            return item.Kind=="circle"?(object)new{key=item.Key,kind=item.Kind,points,radius=item.Radius}:
                new{key=item.Key,kind=item.Kind,points};
        }).ToArray();
        try{return NativeSketchValidation385.Parse(Wire.Element(rows),Wire.Element(Array.Empty<object>()),plane,"profile.native_inputs");}
        catch(DomainException error)
        {
            throw new DomainException("INLINE_PROFILE_NATIVE_INPUT_UNAVAILABLE","The inline contour is outside the retained native input adapter's supported range. No append started. Inspect validation_details for the exact field or arc condition.",false,
                new{validation_code=error.Error.code,validation_message=error.Message,validation_details=error.Error.details});
        }
    }
}
