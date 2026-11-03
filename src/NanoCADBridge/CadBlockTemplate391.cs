using System.Text.Json;
using NanoCAD.Contracts;
using Teigha.DatabaseServices;
using Teigha.Geometry;

namespace NanoCADBridge;

// The native reference owns placement. Fill definitions retain their resident
// PAT/loop context; no detached Hatch evaluation/transform availability probe.
internal static class CadBlockTemplate391
{
    internal const string Warning="STATIC_BLOCK_TEMPLATE: Native MText, independent Hatch and ordinary nested static references remain shared definition content. Nested occurrences retain their values; only the new root variable attributes are initialized. Inspect the local hierarchy and useful Model/Paper result. Native fill completion and nested ownership have no guaranteed Undo/rollback.";
    private static void Need(bool condition,string message)
    {if(!condition)throw new DomainException("UNSUPPORTED_BLOCK_CONTENT",message);}
    internal static void RequireHatch(Hatch hatch,Action cancel)
    {
        cancel();Need(hatch.GetType()==typeof(Hatch)&&!hatch.HasFields&&hatch.Annotative!=AnnotativeStates.True&&!hatch.Associative&&
            hatch.HatchObjectType==HatchObjectType.HatchObject&&hatch.Normal.IsEqualTo(Vector3d.ZAxis)&&
            double.IsFinite(hatch.Elevation)&&Math.Abs(hatch.Elevation)<=1e-9,"Static block fills require independent non-annotative native XY Hatch loops; dependent/gradient/Field content needs its owning route.");
        var stored=CadHatchEditingReader.Stored(hatch,cancel);
        Need(stored.SourceHandles.Length==0,"Independent block fill retains boundary source links requiring their owning adapter.");
    }
    internal static object StoredHatch(Hatch hatch,Action cancel)
    {
        RequireHatch(hatch,cancel);var value=CadHatchEditingReader.Stored(hatch,cancel);
        return new{type="Hatch",geometry=value.Preserved,pattern=value.Pattern,pattern_definitions=value.Definitions};
    }
    internal static void VerifyHatch(Hatch hatch,HatchSpec spec,Action cancel)
    {
        RequireHatch(hatch,cancel);
        var stored=CadHatchEditingReader.Stored(hatch,cancel);var loops=stored.Preserved.GetProperty("loops");
        var expected=new List<(Point2[] Points,double[] Bulges)>{(spec.Boundary,new double[spec.Boundary.Length])};
        foreach(var hole in spec.Holes??Array.Empty<HatchHole>())
        {
            if(hole.Vertices is {} points)expected.Add((points,new double[points.Length]));
            else
            {
                var center=hole.Center!;
                expected.Add((new[]{new Point2(center.X+hole.Radius,center.Y),new Point2(center.X-hole.Radius,center.Y)},new[]{1d,1d}));
            }
        }
        Need(loops.GetArrayLength()==expected.Count,"Native block fill returned a different complete loop count.");
        for(int i=0;i<expected.Count;i++)
        {
            cancel();var loop=loops[i];Need(loop.GetProperty("polyline").GetBoolean(),"Native block fill did not retain its requested polyline/bulge loops.");
            var vertices=loop.GetProperty("vertices");Need(vertices.GetArrayLength()==expected[i].Points.Length,"Native block fill changed a requested loop vertex count.");
            bool Matches(int first,bool reversed)
            {
                int count=expected[i].Points.Length;
                for(int j=0;j<count;j++)
                {
                    cancel();int at=(first+(reversed?-j:j)+count)%count;var actual=vertices[j];var p=actual.GetProperty("point_ocs");var desired=expected[i].Points[at];
                    double bulge=reversed?-expected[i].Bulges[(at-1+count)%count]:expected[i].Bulges[at];
                    if(!BlockMoveGeometry.Close(p[0].GetDouble(),desired.X)||!BlockMoveGeometry.Close(p[1].GetDouble(),desired.Y)||
                        !BlockMoveGeometry.Close(actual.GetProperty("bulge").GetDouble(),bulge))return false;
                }
                return true;
            }
            Need(Enumerable.Range(0,expected[i].Points.Length).Any(first=>Matches(first,false)||Matches(first,true)),
                "Native block fill does not retain the requested local boundary or hole; cyclic start and equivalent reversed traversal are allowed.");
        }
        Need(hatch.PatternName==spec.PatternName&&hatch.PatternType==HatchPatternType.PreDefined&&hatch.HatchStyle==HatchStyle.Normal&&
            (spec.PatternName=="SOLID"?hatch.IsSolidFill:!hatch.IsSolidFill&&!hatch.PatternDouble&&
                BlockMoveGeometry.Close(hatch.PatternAngle,spec.PatternAngle)&&BlockMoveGeometry.Close(hatch.PatternScale,spec.PatternScale)),
            "Native block fill does not retain the requested pattern/island settings.");
    }
    internal static void VerifyMember(Entity member,EntitySpec spec,ObjectId style,Action cancel)
    {
        if(spec is HatchSpec fill){VerifyHatch((Hatch)member,fill,cancel);return;}
        if(spec is MTextSpec text){CadMTextCreator.Verify((MText)member,text,style);return;}
        if(spec is BlockReferenceSpec391 nested)
        {
            var actual=(BlockReference)member;CadBlockMover.CheckRoot(actual);
            Need(Validation.NormalizeHandle(actual.BlockTableRecord.Handle.ToString())==nested.DefinitionHandle&&
                BlockMoveGeometry.Close(actual.Position.X,nested.Position.X)&&BlockMoveGeometry.Close(actual.Position.Y,nested.Position.Y)&&
                BlockMoveGeometry.Close(actual.Rotation,nested.Rotation)&&BlockMoveGeometry.Close(actual.ScaleFactors.X,nested.Scale),
                "Native nested reference does not retain its requested definition/placement.");return;
        }
        CadModifier.CheckGeometry(member,cancel);
        if(member is Ellipse ellipse&&spec is EllipseSpec e)CadEllipseCreator.Verify(ellipse,e,cancel);
        if(member is Spline spline&&spec is SplineSpec s)CadSplineCreator.Verify(spline,s,cancel);
    }
}
