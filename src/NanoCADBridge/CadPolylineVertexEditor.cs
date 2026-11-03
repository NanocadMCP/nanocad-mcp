using NanoCAD.Contracts;
using Teigha.DatabaseServices;
using Teigha.Geometry;

namespace NanoCADBridge;

internal static class CadPolylineVertexEditor
{
    internal static void Apply(Polyline polyline, PolylineVerticesSpec edit, Action cancel)
    {
        if(edit.Workplane!=null){CadPolylineWorkplane431.Apply(polyline,edit,cancel);return;}
        CadModifier.CheckGeometry(polyline, cancel);
        int count = polyline.NumberOfVertices;
        bool closed = polyline.Closed;
        var positions = new Point2d[count]; var bulges = new double[count];
        var starts = new double[count]; var ends = new double[count];
        for (int i = 0; i < count; i++)
        {
            cancel(); positions[i] = polyline.GetPoint2dAt(i); bulges[i] = polyline.GetBulgeAt(i);
            starts[i] = polyline.GetStartWidthAt(i); ends[i] = polyline.GetEndWidthAt(i);
        }
        // Resolve all expectations before setters, including in ordered clone preview.
        foreach (var vertex in edit.Vertices)
        {
            cancel();
            if (vertex.Index >= count) throw new DomainException("VERTEX_NOT_FOUND", "Requested index exceeds this polyline's current vertex count.");
            var expected = new Point2d(vertex.ExpectedPosition.X, vertex.ExpectedPosition.Y);
            if (positions[vertex.Index].GetDistanceTo(expected) > 1e-7)
                throw new DomainException("GEOMETRY_CHANGED", "Vertex differs from expected_position after preceding edits.");
            if(vertex.ExpectedBulge is {} expectedBulge)
            {
                if(Math.Abs(bulges[vertex.Index]-expectedBulge)>1e-12)throw new DomainException("GEOMETRY_CHANGED","Outgoing segment differs from expected_bulge after preceding edits.");
            }
            if(!closed&&vertex.Index==count-1&&(vertex.Bulge!=null||vertex.StartWidth!=null||vertex.EndWidth!=null))throw new DomainException("SEGMENT_NOT_FOUND","The last vertex of an open polyline has no outgoing segment.");
            if(vertex.ExpectedStartWidth is {} start&&Math.Abs(starts[vertex.Index]-start)>1e-9||
               vertex.ExpectedEndWidth is {} end&&Math.Abs(ends[vertex.Index]-end)>1e-9)
                throw new DomainException("GEOMETRY_CHANGED","Outgoing segment widths differ from their expected values after preceding edits.");
        }
        var changed=new HashSet<int>();
        foreach(var vertex in edit.Vertices)
        {cancel();changed.Add(vertex.Index);positions[vertex.Index]=new(vertex.Position.X,vertex.Position.Y);if(vertex.Bulge is {} bulge)bulges[vertex.Index]=bulge;
            if(vertex.StartWidth is {} start)starts[vertex.Index]=start;if(vertex.EndWidth is {} end)ends[vertex.Index]=end;}
        for (int i = 0; i < (closed ? count : count - 1); i++)
        {
            cancel();
            int next = (i + 1) % count;
            if ((changed.Contains(i) || changed.Contains(next)) && positions[i].GetDistanceTo(positions[next]) < 1e-9)
                throw new DomainException("DEGENERATE_GEOMETRY", "Vertex edit would collapse an adjacent segment.");
        }
        var planned=new List<object>();var bytes=new CadContourBudget344(cancel);
        for(int i=0;i<count;i++)
        {
            cancel();var vertex=new{point=new[]{positions[i].X,positions[i].Y,0d},bulge=bulges[i],start_width=starts[i],end_width=ends[i]};
            bytes.Add(vertex);planned.Add(vertex);
        }
        CadContourBudget344.Check(new{closed,polyline.Elevation,normal=new[]{polyline.Normal.X,polyline.Normal.Y,polyline.Normal.Z},
            polyline.Thickness,polyline.ConstantWidth,vertices=planned},cancel);
        foreach (var vertex in edit.Vertices) { cancel(); polyline.SetPointAt(vertex.Index, positions[vertex.Index]);if(vertex.Bulge!=null)polyline.SetBulgeAt(vertex.Index,bulges[vertex.Index]);
            if(vertex.StartWidth!=null)polyline.SetStartWidthAt(vertex.Index,starts[vertex.Index]);if(vertex.EndWidth!=null)polyline.SetEndWidthAt(vertex.Index,ends[vertex.Index]);}
        if (polyline.NumberOfVertices != count || polyline.Closed != closed)
            throw new DomainException("NATIVE_GEOMETRY_MISMATCH", "Vertex setter changed polyline topology.");
        for (int i = 0; i < count; i++)
        {
            cancel();
            if (polyline.GetPoint2dAt(i).GetDistanceTo(positions[i]) > 1e-7 ||
                Math.Abs(polyline.GetBulgeAt(i) - bulges[i]) > 1e-12 ||
                Math.Abs(polyline.GetStartWidthAt(i) - starts[i]) > 1e-9 ||
                Math.Abs(polyline.GetEndWidthAt(i) - ends[i]) > 1e-9)
                throw new DomainException("NATIVE_GEOMETRY_MISMATCH", "Vertex setter did not preserve expected positions, bulges and widths.");
        }
    }
}
