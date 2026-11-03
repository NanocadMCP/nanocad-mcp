using NanoCAD.Contracts;
using Teigha.DatabaseServices;
using Teigha.Geometry;

namespace NanoCADBridge;

internal static class HatchLoopGeometry
{
    internal sealed record Vertex(double[] point_ocs,double bulge);
    internal sealed record Boundary(string Kind,bool Complete,Vertex[] Vertices);
    private static double N(double n)=>double.IsFinite(n)?n:throw new DomainException("OBJECT_READ_FAILED","Non-finite hatch boundary.");
    private static void Budget(long bytes)
    {
        if(bytes>Wire.PayloadBudget-8192)throw new DomainException("RESPONSE_LIMIT_EXCEEDED","Stored hatch boundary exceeds the actual response-byte budget; no edge-count truncation occurred.");
    }
    private static int Bytes(Vertex vertex)=>System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(vertex,Wire.Json).Length+1;
    internal static Boundary Read(HatchLoop loop,CancellationToken cancel)
    {
        if(loop.IsPolyline)
        {
            var vertices=new List<Vertex>();long bytes=1024;
            foreach(BulgeVertex vertex in loop.Polyline)
            {
                cancel.ThrowIfCancellationRequested();
                var value=new Vertex(new[]{N(vertex.Vertex.X),N(vertex.Vertex.Y)},N(vertex.Bulge));
                bytes+=Bytes(value);Budget(bytes);vertices.Add(value);
            }
            return new("polyline",true,vertices.ToArray());
        }
        // AppendLoop(ObjectIdCollection) stores straight Polyline sources as
        // a chain of owned LineSegment2d wrappers in the installed SDK.
        var curves=loop.Curves;
        try
        {
            if(curves.Count<3)return new("unsupported_curves",false,Array.Empty<Vertex>());
            var starts=new List<Point2d>();var ends=new List<Point2d>();long bytes=1024;
            for(int i=0;i<curves.Count;i++)
            {
                cancel.ThrowIfCancellationRequested();
                if(curves[i].GetType()!=typeof(LineSegment2d))return new("unsupported_curves",false,Array.Empty<Vertex>());
                var line=(LineSegment2d)curves[i];starts.Add(line.StartPoint);ends.Add(line.EndPoint);
                N(starts[i].X);N(starts[i].Y);N(ends[i].X);N(ends[i].Y);
                bytes+=Bytes(new(new[]{starts[i].X,starts[i].Y},0));Budget(bytes);
            }
            for(int i=0;i<starts.Count;i++){cancel.ThrowIfCancellationRequested();if(ends[i].GetDistanceTo(starts[(i+1)%starts.Count])>1e-8)return new("unsupported_curve_chain",false,Array.Empty<Vertex>());}
            return new("line_segments",true,starts.Select(p=>new Vertex(new[]{p.X,p.Y},0)).ToArray());
        }
        finally{foreach(Curve2d curve in curves)curve.Dispose();}
    }
}
