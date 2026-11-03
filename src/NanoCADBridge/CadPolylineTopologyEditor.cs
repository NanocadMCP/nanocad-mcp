using NanoCAD.Contracts;
using Teigha.DatabaseServices;
using Teigha.Geometry;

namespace NanoCADBridge;

internal static class CadPolylineTopologyEditor
{
    internal static bool Handles(EditSpec operation) => operation is PolylineGeometrySpec or PolylineCornersSpec or PolylineReverseSpec;
    private static PolylineShape Capture(Polyline polyline, Action cancel)
    {
        CadModifier.CheckGeometry(polyline, cancel);
        var vertices = new List<PolylineShapeVertex>();var budget=new CadContourBudget344(cancel);
        for (int i = 0; i < polyline.NumberOfVertices; i++)
        {
            cancel(); var p = polyline.GetPoint2dAt(i);
            var value=new PolylineShapeVertex(new(p.X,p.Y),polyline.GetBulgeAt(i),polyline.GetStartWidthAt(i),polyline.GetEndWidthAt(i));
            budget.Add(value);vertices.Add(value);
        }
        var shape = new PolylineShape(polyline.Closed,vertices.ToArray());CadContourBudget344.Check(shape,cancel);PolylineTopologyGeometry.Validate(shape,cancel);return shape;
    }
    internal static void Apply(Polyline polyline, EditSpec edit, Action cancel)
    {
        if(PolylineEditFrames431.Frame(edit)!=null){CadPolylineWorkplane431.Apply(polyline,edit,cancel);return;}
        var source = Capture(polyline, cancel);
        var expected = edit switch { PolylineGeometrySpec p => p.ExpectedGeometry, PolylineCornersSpec p => p.ExpectedGeometry, PolylineReverseSpec p => p.ExpectedGeometry, _ => throw new InvalidOperationException() };
        if (!PolylineTopologyGeometry.Equivalent(source,expected,cancel)) throw new DomainException("GEOMETRY_CHANGED", "Polyline topology, positions, curvature or widths differ from expected_geometry after preceding edits.");
        var result = edit switch { PolylineGeometrySpec p => p.Geometry, PolylineCornersSpec p => PolylineTopologyGeometry.Corners(source,p.Corners,cancel), PolylineReverseSpec => PolylineTopologyGeometry.Reverse(source,cancel), _ => throw new InvalidOperationException() };
        PolylineTopologyGeometry.Validate(result,cancel);CadContourBudget344.Check(result,cancel);
        // Resize the existing DB entity, retaining its identity/properties. Preview
        // performs the same setters on a detached clone before any write-open.
        while (polyline.NumberOfVertices > result.Vertices.Length) { cancel(); polyline.RemoveVertexAt(polyline.NumberOfVertices - 1); }
        while (polyline.NumberOfVertices < result.Vertices.Length)
        {
            cancel(); int i = polyline.NumberOfVertices; var v = result.Vertices[i];
            polyline.AddVertexAt(i, new Point2d(v.Position.X, v.Position.Y), v.Bulge, v.StartWidth, v.EndWidth);
        }
        for (int i = 0; i < result.Vertices.Length; i++)
        {
            cancel(); var v = result.Vertices[i]; polyline.SetPointAt(i, new(v.Position.X, v.Position.Y));
            polyline.SetBulgeAt(i, v.Bulge); polyline.SetStartWidthAt(i, v.StartWidth); polyline.SetEndWidthAt(i, v.EndWidth);
        }
        polyline.Closed = result.Closed;
        if (!PolylineTopologyGeometry.Equivalent(Capture(polyline,cancel),result,cancel)) throw new DomainException("NATIVE_GEOMETRY_MISMATCH", "Polyline setters did not retain requested topology/segments/widths.");
    }
}
