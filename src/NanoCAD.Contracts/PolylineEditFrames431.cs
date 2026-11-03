namespace NanoCAD.Contracts;

// Framed Polyline inputs use local XY and signed bulges in that same oriented
// plane. Stored OCS coordinates are never confused with the selected frame.
public static class PolylineEditFrames431
{
    public static SketchPlaneSpec? Frame(EditSpec edit)=>edit switch
    {
        PolylineVerticesSpec value=>value.Workplane,
        PolylineGeometrySpec value=>value.Workplane,
        PolylineReverseSpec value=>value.Workplane,
        PolylineCornersSpec value=>value.Workplane,
        _=>null
    };
    public static bool ChangesSegments(PolylineVerticesSpec edit)=>edit.Vertices.Any(vertex=>vertex.Bulge!=null||vertex.StartWidth!=null||vertex.EndWidth!=null);
    public static SketchPlaneSpec? Frame(IEnumerable<EditSpec> edits,string handle)
    {
        var selected=edits.Where(edit=>edit.Handles.Contains(handle)).ToArray();
        var frame=selected.Select(Frame).FirstOrDefault(value=>value!=null);
        if(frame==null)return null;
        if(selected.Any(edit=>edit is not PropertiesSpec && Frame(edit)!=frame))
            throw new DomainException("INVALID_OPERATION_ORDER","A framed Polyline target may share its batch only with appearance edits or Polyline edits using the same explicit workplane. Refresh geometry before changing frames or using legacy transforms.");
        return frame;
    }
}

public static class PolylineSparseGeometry431
{
    public static PolylineShape Apply(PolylineShape source,PolylineVertexEdit[] edits,Action? cancel=null)
    {
        var vertices=source.Vertices.ToArray();var selected=new HashSet<int>();
        foreach(var edit in edits)
        {
            cancel?.Invoke();
            if(edit.Index<0||edit.Index>=vertices.Length||!selected.Add(edit.Index))throw new DomainException("VERTEX_NOT_FOUND","Select distinct indices in the current Polyline vertex inventory.");
            var before=vertices[edit.Index];
            if(Math.Abs(before.Position.X-edit.ExpectedPosition.X)>1e-7||Math.Abs(before.Position.Y-edit.ExpectedPosition.Y)>1e-7||
                edit.ExpectedBulge is {} bulge&&Math.Abs(before.Bulge-bulge)>1e-12||
                edit.ExpectedStartWidth is {} start&&Math.Abs(before.StartWidth-start)>1e-9||
                edit.ExpectedEndWidth is {} end&&Math.Abs(before.EndWidth-end)>1e-9)
                throw new DomainException("GEOMETRY_CHANGED","Selected vertex position, curvature or widths differ from their expected values after preceding edits.");
            if(!source.Closed&&edit.Index==vertices.Length-1&&(edit.Bulge!=null||edit.StartWidth!=null||edit.EndWidth!=null))
                throw new DomainException("SEGMENT_NOT_FOUND","The last vertex of an open Polyline has no outgoing segment to change.");
        }
        foreach(var edit in edits)
        {
            cancel?.Invoke();var before=vertices[edit.Index];
            vertices[edit.Index]=before with{Position=edit.Position,Bulge=edit.Bulge??before.Bulge,StartWidth=edit.StartWidth??before.StartWidth,EndWidth=edit.EndWidth??before.EndWidth};
        }
        var result=source with{Vertices=vertices};PolylineTopologyGeometry.Validate(result,cancel);return result;
    }
}
