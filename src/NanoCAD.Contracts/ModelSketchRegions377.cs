namespace NanoCAD.Contracts;

// One atomic native area; its holes are retained boundaries, not extra regions
// to toggle. The native profile remains the geometry/selection authority.
public sealed record ModelSketchRegion377(SketchConstraintCurve[] Outer,SketchConstraintCurve[][] Holes);
public static class ModelSketchRegions377
{
    public static ModelSketchRegion377 Select(SketchConstraintCurve[] curves,Point2 point,Action cancel)
    {
        if(curves.Length>0&&curves.All(curve=>curve.Kind=="circle"))
        {
            var cell=ModelCircleRegions350.Select(curves.Select(curve=>new ModelCircleLoop350(curve.Id,curve.Center!,curve.Radius,curve.Id)).ToArray(),[point],cancel).Single();
            return new([curves.Single(curve=>curve.Id==cell.outer_element_id)],cell.hole_element_ids.Select(id=>new[]{curves.Single(curve=>curve.Id==id)}).ToArray());
        }
        return new(ModelSketchSelection362.Select(curves,point,cancel),[]);
    }
}
