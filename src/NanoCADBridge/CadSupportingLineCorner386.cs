using System.Diagnostics.CodeAnalysis;
using NanoCAD.Contracts;
using Teigha.DatabaseServices;
using Teigha.Geometry;

namespace NanoCADBridge;

// This is a read-only geometric construction. The finite resident sources
// are not extended; the existing corner writer proves each final attachment
// lies strictly inside its original source before any in-place trim.
internal static class CadSupportingLineCorner386
{
    static void Need([DoesNotReturnIf(false)] bool valid,string message,string code="AMBIGUOUS_CORNER")
    {if(!valid)throw new DomainException(code,message);}
    static Point3d End(Line line,string end)=>end=="start"?line.StartPoint:line.EndPoint;
    // Engineering chamfer dimensions refer to the supporting intersection.
    // Return only the actual finite-source removal from the stored endpoint.
    internal static double Setback(Line source,string end,Point3d corner,double requested)
    {
        double gap=End(source,end).DistanceTo(corner);
        double full=End(source,end=="start"?"end":"start").DistanceTo(corner);
        Need(double.IsFinite(gap)&&double.IsFinite(full)&&double.IsFinite(requested)&&
            requested-gap>Tolerance.Global.EqualPoint&&full-requested>Tolerance.Global.EqualPoint,
            "Each supporting-corner chamfer distance must exceed the actual source gap and remain shorter than the corner-to-opposite length; attachments stay strictly inside the original Line.","INVALID_CORNER_SETBACK");
        return requested-gap;
    }
    internal static Point3d Intersection(Line first,Line second,string[] ends,Action cancel)
    {
        cancel();var direction1=first.EndPoint-first.StartPoint;var direction2=second.EndPoint-second.StartPoint;
        Need(double.IsFinite(direction1.Length)&&double.IsFinite(direction2.Length)&&
            direction1.Length>Tolerance.Global.EqualPoint&&direction2.Length>Tolerance.Global.EqualPoint,
            "A supporting-line corner requires two finite noncollapsed Lines.","INVALID_GEOMETRY");
        Need(direction1.GetNormal().CrossProduct(direction2.GetNormal()).Length>Tolerance.Global.EqualVector,
            "Parallel or collinear supporting Lines do not identify one corner.");
        // Installed managed IntersectWith takes LinearEntity3d by value and
        // returns Point3d[]. The CHM C++/CLI caret is not a managed ref argument.
        using var support1=new Line3d(first.StartPoint,first.EndPoint);
        using var support2=new Line3d(second.StartPoint,second.EndPoint);
        cancel();var points=support1.IntersectWith(support2,Tolerance.Global);cancel();
        Need(points!=null&&points.Length==1,"The actual supporting Lines have no unique native intersection; skew lines are not projected into a plane.","NO_COMMON_CORNER");
        var corner=points[0];Need(double.IsFinite(corner.X)&&double.IsFinite(corner.Y)&&double.IsFinite(corner.Z),
            "Native supporting-line intersection is not finite.","INVALID_GEOMETRY");
        var lines=new[]{first,second};
        for(int i=0;i<lines.Length;i++)
        {
            cancel();var selected=End(lines[i],ends[i]);var opposite=End(lines[i],ends[i]=="start"?"end":"start");
            var retainedRay=opposite-corner;var selectedRay=selected-corner;
            Need(double.IsFinite(retainedRay.Length)&&retainedRay.Length>Tolerance.Global.EqualPoint,
                "The selected opposite endpoint coincides with the supporting corner; choose the intended source end.","INVALID_CORNER_SETBACK");
            var unit=retainedRay.GetNormal();double selectedDistance=selectedRay.DotProduct(unit);
            Need(double.IsFinite(selectedDistance)&&selectedRay.CrossProduct(unit).Length<=Tolerance.Global.EqualPoint&&
                selectedDistance>=-Tolerance.Global.EqualPoint&&retainedRay.Length-selectedDistance>Tolerance.Global.EqualPoint,
                "The supporting corner must lie at or beyond the selected endpoint, away from the retained opposite endpoint. Interior crossings and the opposite end need a different explicit selection.","INVALID_CORNER_SELECTION");
        }
        return corner;
    }
}
