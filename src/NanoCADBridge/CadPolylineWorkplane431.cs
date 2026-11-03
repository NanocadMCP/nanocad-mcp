using NanoCAD.Contracts;
using Teigha.DatabaseServices;
using Teigha.Geometry;

namespace NanoCADBridge;

// Change the existing root through documented OCS setters. Never TransformBy a
// resident source, project points, change UCS, or recreate its owned metadata.
internal static class CadPolylineWorkplane431
{
    private static double Orientation(Polyline polyline,SketchPlaneSpec frame)
    {
        var n=DrawingWorkplane.Normal(frame);
        return polyline.Normal.DotProduct(new(n.X,n.Y,n.Z))>0?1:-1;
    }
    internal static PolylineShape Capture(Polyline polyline,SketchPlaneSpec frame,Action cancel)
    {
        cancel();if(polyline.GetType()!=typeof(Polyline))throw new DomainException("UNSUPPORTED_ENTITY_TYPE","Framed topology editing requires one exact native Polyline.");
        _=CadCurveWorkplane.Require(polyline,frame,cancel);
        if(!double.IsFinite(polyline.Thickness)||Math.Abs(polyline.Thickness)>1e-9)
            throw new DomainException("PLANAR_GEOMETRY_REQUIRED","Framed Polyline topology editing retains a zero-thickness planar root.");
        double orientation=Orientation(polyline,frame);var vertices=new List<PolylineShapeVertex>();var budget=new CadContourBudget344(cancel);
        for(int index=0;index<polyline.NumberOfVertices;index++)
        {
            cancel();var world=polyline.GetPoint3dAt(index);var local=DrawingWorkplane.Local(frame,new(world.X,world.Y,world.Z));
            var vertex=new PolylineShapeVertex(local,orientation*polyline.GetBulgeAt(index),polyline.GetStartWidthAt(index),polyline.GetEndWidthAt(index));
            budget.Add(vertex);vertices.Add(vertex);
        }
        var result=new PolylineShape(polyline.Closed,vertices.ToArray());PolylineTopologyGeometry.Validate(result,cancel);return result;
    }
    internal static void Check(Entity entity,SketchPlaneSpec frame,Action cancel)
    {
        if(entity is not Polyline polyline||entity.GetType()!=typeof(Polyline))throw new DomainException("UNSUPPORTED_ENTITY_TYPE","Framed Polyline edits require an exact native Polyline.");
        _=Capture(polyline,frame,cancel);
    }
    internal static void Apply(Polyline polyline,EditSpec edit,Action cancel)
    {
        var frame=PolylineEditFrames431.Frame(edit)??throw new DomainException("INVALID_ARGUMENT","An explicit Polyline workplane is required.");
        var source=Capture(polyline,frame,cancel);PolylineShape result;
        if(edit is PolylineVerticesSpec sparse)result=PolylineSparseGeometry431.Apply(source,sparse.Vertices,cancel);
        else
        {
            var expected=edit switch{PolylineGeometrySpec value=>value.ExpectedGeometry,PolylineReverseSpec value=>value.ExpectedGeometry,PolylineCornersSpec value=>value.ExpectedGeometry,_=>throw new InvalidOperationException()};
            if(!PolylineTopologyGeometry.Equivalent(source,expected,cancel))throw new DomainException("GEOMETRY_CHANGED","Polyline differs from complete expected_geometry in the declared workplane.");
            result=edit switch{PolylineGeometrySpec value=>value.Geometry,PolylineReverseSpec=>PolylineTopologyGeometry.Reverse(source,cancel),PolylineCornersSpec value=>PolylineTopologyGeometry.Corners(source,value.Corners,cancel),_=>throw new InvalidOperationException()};
        }
        PolylineTopologyGeometry.Validate(result,cancel);CadContourBudget344.Check(result,cancel);
        var normal=polyline.Normal;double elevation=polyline.Elevation,thickness=polyline.Thickness,orientation=Orientation(polyline,frame);
        var id=polyline.ObjectId;var owner=polyline.OwnerId;var points=new Point2d[result.Vertices.Length];
        var toOcs=Matrix3d.WorldToPlane(normal);
        // Resolve every WCS->OCS point before any native setter is executed.
        for(int index=0;index<points.Length;index++)
        {
            cancel();var world=DrawingWorkplane.World(frame,result.Vertices[index].Position);var ocs=new Point3d(world.X,world.Y,world.Z).TransformBy(toOcs);
            if(!double.IsFinite(ocs.X)||!double.IsFinite(ocs.Y)||!double.IsFinite(ocs.Z)||Math.Abs(ocs.Z-elevation)>1e-7)
                throw new DomainException("PLANAR_GEOMETRY_REQUIRED","Requested point is outside the retained Polyline OCS plane; no projection performed.");
            points[index]=new(ocs.X,ocs.Y);
        }
        while(polyline.NumberOfVertices>result.Vertices.Length){cancel();polyline.RemoveVertexAt(polyline.NumberOfVertices-1);}
        while(polyline.NumberOfVertices<result.Vertices.Length)
        {
            cancel();int index=polyline.NumberOfVertices;var v=result.Vertices[index];polyline.AddVertexAt(index,points[index],orientation*v.Bulge,v.StartWidth,v.EndWidth);
        }
        var selected=edit is PolylineVerticesSpec vertexEdit?vertexEdit.Vertices.Select(vertex=>vertex.Index).ToHashSet():null;
        for(int index=0;index<points.Length;index++)
        {
            cancel();if(selected!=null&&!selected.Contains(index))continue;
            var v=result.Vertices[index];polyline.SetPointAt(index,points[index]);polyline.SetBulgeAt(index,orientation*v.Bulge);
            polyline.SetStartWidthAt(index,v.StartWidth);polyline.SetEndWidthAt(index,v.EndWidth);
        }
        polyline.Closed=result.Closed;
        if(polyline.ObjectId!=id||polyline.OwnerId!=owner||polyline.Normal!=normal||polyline.Elevation!=elevation||polyline.Thickness!=thickness||
            !PolylineTopologyGeometry.Equivalent(Capture(polyline,frame,cancel),result,cancel))
            throw new DomainException("NATIVE_GEOMETRY_MISMATCH","Polyline setters failed the requested local geometry or changed the retained native root/plane.");
    }
}
