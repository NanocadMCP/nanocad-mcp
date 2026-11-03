using System.Text.Json;
using NanoCAD.Contracts;
using Teigha.DatabaseServices;
using Teigha.Geometry;

namespace NanoCADBridge;

internal static class CadHatchPatternEditor
{
    private static void Require(bool ok,string message)
    {if(!ok)throw new DomainException("UNSUPPORTED_HATCH_CONTENT",message);}
    internal static (HatchSpec Spec,JsonElement Snapshot) Capture(Hatch hatch,Action cancel)
    {
        cancel();using var reactors=hatch.GetPersistentReactorIds();
        Require(hatch.GetType()==typeof(Hatch)&&!hatch.HasFields&&reactors.Count==0&&!hatch.Associative&&
            hatch.PatternName=="ANSI31"&&hatch.PatternType==HatchPatternType.PreDefined&&!hatch.PatternDouble&&
            hatch.Normal==Vector3d.ZAxis&&hatch.Elevation==0&&hatch.HatchStyle==HatchStyle.Normal&&hatch.NumberOfLoops>=1,
            "Requires non-associative ANSI31, normal style, polygon outer loop and optional polygon/circle holes in WCSXY/Z0; no fields or persistent dependencies.");
        var polygons=new List<Point2[]>();var circles=new List<HatchHole>();var capacity=new SerializedCapacity357(cancel:cancel);
        for(int i=0;i<hatch.NumberOfLoops;i++)
        {
            cancel();var loop=hatch.GetLoopAt(i);Require(loop.IsPolyline,"Pattern editing requires stored polyline loops.");
            using var ids=hatch.GetAssociatedObjectIdsAt(i);Require(ids.Count==0,"Pattern boundary must not reference external source objects.");
            var vertices=loop.Polyline.Cast<BulgeVertex>().Select(v=>{cancel();capacity.Add(new{vertex=new[]{v.Vertex.X,v.Vertex.Y},v.Bulge});return v;}).ToArray();
            if(vertices.Length==2&&Math.Abs(Math.Abs(vertices[0].Bulge)-1)<1e-12&&Math.Abs(vertices[0].Bulge-vertices[1].Bulge)<1e-12)
            {
                var a=vertices[0].Vertex;var b=vertices[1].Vertex;
                circles.Add(new(Center:new((a.X+b.X)/2,(a.Y+b.Y)/2),Radius:a.GetDistanceTo(b)/2));
            }
            else
            {
                Require(vertices.Length>=3&&vertices.All(v=>v.Bulge==0),"Only straight polygons or exact two-semicircle holes are editable.");
                polygons.Add(vertices.Select(v=>new Point2(v.Vertex.X,v.Vertex.Y)).ToArray());
            }
        }
        Require(polygons.Count>0,"A polygon outer boundary is required.");
        double Area(Point2[] p)=>Math.Abs(Enumerable.Range(0,p.Length).Sum(i=>{cancel();return (p[i].X-p[0].X)*(p[(i+1)%p.Length].Y-p[0].Y)-(p[(i+1)%p.Length].X-p[0].X)*(p[i].Y-p[0].Y);}));
        var outer=polygons.OrderByDescending(Area).First();
        var holes=polygons.Where(p=>!ReferenceEquals(p,outer)).Select(p=>new HatchHole(p)).Concat(circles).ToArray();
        HatchRegionValidation.Validate(outer,holes,cancel);
        var spec=new HatchSpec(outer,hatch.Layer,"ANSI31",hatch.PatternAngle,hatch.PatternScale,holes.Length==0?null:holes);
        HatchPatternDensity.Bound(spec.Boundary,CadHatchPattern.Definition(hatch),holes.Sum(h=>h.Vertices?.Length??2));
        var geometry=System.Text.Json.Nodes.JsonNode.Parse(Wire.Element(AnnotationReader.HatchGeometry(hatch,CancellationToken.None,false)).GetRawText())!.AsObject();
        // Freshness/admission metadata is observational, not preserved pattern
        // geometry. A legitimate resident angle/scale edit changes its token.
        geometry.Remove("editing");geometry.Remove("editing_unavailable_reason");
        // Keep the legacy projection: it already snapshots Origin/HatchStyle
        // separately; predefined PatternSpace is inactive/derived, not an old
        // preservation requirement. Avoid adding an unsupported new oracle.
        geometry.Remove("pattern_space");geometry.Remove("origin_ocs");geometry.Remove("hatch_style");
        return (spec,Wire.Element(new{geometry,origin=new[]{hatch.Origin.X,hatch.Origin.Y},
            style=(int)hatch.HatchStyle,layer=hatch.LayerId.Handle.ToString(),linetype=hatch.LinetypeId.Handle.ToString(),color=hatch.ColorIndex,lineweight=(int)hatch.LineWeight,hatch.Visible}));
    }
    internal static void Apply(Hatch hatch,SetHatchPatternSpec edit,Action cancel,bool evaluate)
    {
        var before=Capture(hatch,cancel);var expected=HatchPatternEditGeometry.Expected(before.Snapshot,edit);
        var target=before.Spec with{PatternAngle=edit.Angle,PatternScale=edit.Scale};
        double bound=CadHatchPattern.Configure(hatch,target);cancel();
        if(evaluate){hatch.EvaluateHatch(true);CadHatchPattern.VerifyEvaluation(hatch,bound);}
        HatchPatternEditGeometry.Verify(expected,Capture(hatch,cancel).Snapshot);
    }
}
