using Multicad.DatabaseServices.Constraints;
using NanoCAD.Contracts;

namespace NanoCADBridge;

internal static class DrawingConstraints431
{
    internal sealed record Parent(Point3 Normal,double Elevation,double Thickness,bool Closed,int VertexCount);
    internal static bool ParentPreserved(DrawingProfileWriter.Source before,DrawingProfileWriter.Source after)
    {
        if(before.Parent==null)return after.Parent==null;
        if(before.Parent!=after.Parent||before.Curve.Polyline==null||after.Curve.Polyline==null)return false;
        var a=before.Curve.Polyline.Vertices;var b=after.Curve.Polyline.Vertices;
        return a.Length==b.Length&&a.Zip(b).All(pair=>pair.First.StartWidth==pair.Second.StartWidth&&pair.First.EndWidth==pair.Second.EndWidth);
    }
    // The documented SDK uses an all-NaN point for an unspecified/complete
    // object. A finite point is an endpoint/center pick; a mixed nonfinite
    // triple is invalid evidence, not silently a whole-object anchor.
    internal static bool WholeObject(IConstraintObjectInfo reference)
    {
        var point=reference.PointRefOnObject;
        // Some direct non-Polyline receipts expose index0; an index alone is
        // not a segment binding without an actual Polyline/native selector.
        return reference.SubentId.IsNull&&
            double.IsNaN(point.X)&&double.IsNaN(point.Y)&&double.IsNaN(point.Z);
    }
    internal static int? Segment(SketchConstraintCurve source,IConstraintObjectInfo reference)
    {
        if(source.Kind!="polyline")return null;
        if(WholeObject(reference))return null;
        if(reference.PolylineSegmentIndex<0)throw new DomainException("CONSTRAINT_SCOPE_MISMATCH","Polyline reference lacks its exact native segment index.");
        _=DrawingConstraintOperands431.Resolve(source,reference.PolylineSegmentIndex);
        return reference.PolylineSegmentIndex;
    }
}
