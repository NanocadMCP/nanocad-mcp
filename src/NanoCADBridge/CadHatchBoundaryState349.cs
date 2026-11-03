using System.Text.Json;
using NanoCAD.Contracts;
using Teigha.DatabaseServices;
using Teigha.Geometry;

namespace NanoCADBridge;

// Verify useful exact straight/circular loops. Native reversal/start vertices
// and an equivalent stored line/arc representation do not reconstruct the fill.
internal static class CadHatchBoundaryState349
{
    internal static void Need([System.Diagnostics.CodeAnalysis.DoesNotReturnIf(false)]bool ok,string code,string message)
    {if(!ok)throw new DomainException(code,message);}
    static bool Near(double first,double second)=>double.IsFinite(second)&&Math.Abs(first-second)<=1e-8;
    static bool Same(Point2d first,Point2d second)=>Near(first.X,second.X)&&Near(first.Y,second.Y);
    static bool Polygon(Point2[] expected,Point2d[] actual,Action cancel)
    {
        int count=actual.Length;if(count>1&&Same(actual[0],actual[count-1]))count--;
        if(expected.Length!=count)return false;
        for(int start=0;start<count;start++)foreach(int direction in new[]{1,-1})
        {
            bool same=true;for(int index=0;index<count;index++)
            {cancel();var value=actual[(start+direction*index+count)%count];if(!Near(expected[index].X,value.X)||!Near(expected[index].Y,value.Y)){same=false;break;}}
            if(same)return true;
        }
        return false;
    }
    sealed record Vertex(Point2d Point,double Bulge);
    sealed record Curve(string Kind,Point2d Start,Point2d End,Point2d Center,double Radius,double Sweep,bool Bounded,bool Clockwise);
    sealed record Loop(HatchLoopTypes LoopType,bool IsPolyline,Vertex[] Vertices,Curve[] Curves);
    static Loop Capture(HatchLoop actual,Action cancel)
    {
        if(actual.IsPolyline)
        {
            var points=new List<Vertex>();foreach(BulgeVertex vertex in actual.Polyline){cancel();points.Add(new(vertex.Vertex,vertex.Bulge));}
            return new(actual.LoopType,true,points.ToArray(),[]);
        }
        // Read each native collection once. All matching below uses managed
        // values, so unordered hole trials never revisit disposed wrappers.
        var curves=actual.Curves;var values=new List<Curve>();
        try
        {
            foreach(Curve2d curve in curves)
            {
                cancel();
                if(curve.GetType()==typeof(LineSegment2d))values.Add(new("line",curve.StartPoint,curve.EndPoint,default,0,0,true,false));
                else if(curve.GetType()==typeof(CircularArc2d))
                {
                    var arc=(CircularArc2d)curve;using var interval=arc.GetInterval();
                    values.Add(new("circle",arc.StartPoint,arc.EndPoint,arc.Center,arc.Radius,
                        interval.UpperBound-interval.LowerBound,interval.IsBounded,arc.IsClockWise));
                }
                else values.Add(new("unsupported",default,default,default,0,0,false,false));
            }
            return new(actual.LoopType,false,[],values.ToArray());
        }
        finally
        {
            var released=new HashSet<Curve2d>(ReferenceEqualityComparer.Instance);
            foreach(Curve2d curve in curves)if(released.Add(curve))curve.Dispose();
        }
    }
    static bool Matches(HatchBoundaryLoop349 expected,Loop actual,Action cancel)
    {
        if(actual.IsPolyline)
        {
            var values=actual.Vertices.ToList();
            if(expected.Vertices is {} polygon)return values.All(value=>{cancel();return Near(value.Bulge,0);})&&Polygon(polygon,values.Select(value=>value.Point).ToArray(),cancel);
            if(values.Count==3&&Same(values[0].Point,values[2].Point)&&Near(values[2].Bulge,0))values.RemoveAt(2);
            if(values.Count!=2||!Near(Math.Abs(values[0].Bulge),1)||!Near(values[0].Bulge,values[1].Bulge))return false;
            var first=values[0].Point;var second=values[1].Point;var center=expected.Center!;
            return Near((first.X+second.X)/2,center.X)&&Near((first.Y+second.Y)/2,center.Y)&&Near(first.GetDistanceTo(second)/2,expected.Radius);
        }
        var starts=new List<Point2d>();var ends=new List<Point2d>();double span=0;bool? clockwise=null;
        foreach(var curve in actual.Curves)
        {
            cancel();if(expected.Vertices!=null)
            {if(curve.Kind!="line")return false;}
            else
            {
                if(curve.Kind!="circle")return false;var center=expected.Center!;
                if(!Near(curve.Center.X,center.X)||!Near(curve.Center.Y,center.Y)||!Near(curve.Radius,expected.Radius)||!curve.Bounded)return false;
                double sweep=curve.Sweep;if(!double.IsFinite(sweep)||sweep<=0||sweep>2*Math.PI+1e-8||clockwise.HasValue&&clockwise.Value!=curve.Clockwise)return false;
                clockwise??=curve.Clockwise;span+=sweep;
            }
            starts.Add(curve.Start);ends.Add(curve.End);
        }
        if(starts.Count==0)return false;
        for(int index=0;index<starts.Count;index++){cancel();if(!Same(ends[index],starts[(index+1)%starts.Count]))return false;}
        return expected.Vertices is {} points?Polygon(points,starts.ToArray(),cancel):Near(span,2*Math.PI);
    }
    internal static void VerifyLoops(Hatch hatch,SetHatchBoundarySpec349 edit,Action cancel)
    {
        Need(hatch.NumberOfLoops==1+edit.Holes.Length,"NATIVE_HATCH_BOUNDARY_MISMATCH","Native boundary loop count differs from the requested outer and holes.");
        var loops=new List<Loop>();int outer=-1;
        for(int index=0;index<hatch.NumberOfLoops;index++)
        {
            cancel();var loop=Capture(hatch.GetLoopAt(index),cancel);loops.Add(loop);
            Need((loop.LoopType&(HatchLoopTypes.NotClosed|HatchLoopTypes.SelfIntersecting|HatchLoopTypes.Duplicate))==0,
                "INVALID_GEOMETRY","Native Hatch reports an invalid boundary topology.");
            if((loop.LoopType&HatchLoopTypes.External)!=0){Need(outer<0,"NATIVE_HATCH_BOUNDARY_MISMATCH","Native Hatch has multiple external loops.");outer=index;}
        }
        Need(outer>=0&&Matches(edit.Outer,loops[outer],cancel),"NATIVE_HATCH_BOUNDARY_MISMATCH","Native external loop differs from the requested straight/circular boundary.");
        var remaining=Enumerable.Range(0,loops.Count).Where(index=>index!=outer).ToHashSet();
        foreach(var hole in edit.Holes)
        {
            cancel();int selected=-1;foreach(int index in remaining){cancel();if(Matches(hole,loops[index],cancel)){selected=index;break;}}
            Need(selected>=0,"NATIVE_HATCH_BOUNDARY_MISMATCH","A requested native hole is missing or has different geometry.");remaining.Remove(selected);
        }
    }
    internal static void Retained(CadHatchEditingReader.Snapshot before,CadHatchEditingReader.Snapshot after,Action cancel)
    {
        Need(before.Identity==after.Identity&&before.Sources.GetRawText()==after.Sources.GetRawText(),"NATIVE_HATCH_BOUNDARY_MISMATCH","Boundary editing changed Hatch identity/owner or source references.");
        foreach(var property in before.Stored.Preserved.EnumerateObject())
        {cancel();if(property.Name!="loops")Need(property.Value.GetRawText()==after.Stored.Preserved.GetProperty(property.Name).GetRawText(),"NATIVE_HATCH_BOUNDARY_MISMATCH","Boundary editing changed retained plane, root display, associations or owned metadata.");}
        Need(BlockMoveGeometry.SameProperties(Wire.Element(before.Stored.Pattern),Wire.Element(after.Stored.Pattern))&&
            BlockMoveGeometry.SameProperties(before.Stored.Definitions,after.Stored.Definitions),"NATIVE_HATCH_BOUNDARY_MISMATCH","Boundary editing changed a retained pattern/island/origin setting or pattern definition.");
    }
}
