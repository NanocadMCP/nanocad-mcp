namespace NanoCAD.Contracts;

// Preflight for a separate replacement selection; this neither edits an old
// profile nor builds substitute sketch geometry. Native contours are checked
// against these intended boundaries after SDK selection.
public static class ModelProfileRebindRegions430
{
    public static ModelSketchRegion377[] Select(SketchConstraintCurve[] curves,Point2[] points,Action cancel)
    {
        if(points.Length==0)throw new DomainException("INVALID_ARGUMENT","Select at least one bounded replacement region.");
        if(curves.Length>0&&curves.All(curve=>curve.Kind=="circle"))
        {
            var cells=ModelCircleRegions350.Select(curves.Select(curve=>new ModelCircleLoop350(curve.Id,curve.Center!,curve.Radius,curve.Id)).ToArray(),points,cancel);
            return cells.Select(cell=>new ModelSketchRegion377([curves.Single(curve=>curve.Id==cell.outer_element_id)],
                cell.hole_element_ids.Select(id=>new[]{curves.Single(curve=>curve.Id==id)}).ToArray())).ToArray();
        }
        return ModelProfileLoopRegions440.Select(curves,points,cancel);
    }
}
