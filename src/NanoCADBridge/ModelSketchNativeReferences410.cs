using System.Globalization;
using System.Text.Json;
using Multicad;
using Multicad.Mc3D;
using NanoCAD.Contracts;
using Db=Teigha.DatabaseServices;
using G=Multicad.Geometry;

namespace NanoCADBridge;

// Same-part native FEV references are subentities, not source drawing handles.
internal static class ModelSketchNativeReferences410
{
    internal static McObjectId[] Resolve(string[] ids,Mc3dSolid body,Db.ObjectId representation,DocumentState state,string bridge,Action cancel)
    {
        if(ids.Length==0)return [];
        var edges=ModelEdgeReader.Capture(representation,state,bridge,CancellationToken.None,cancel);
        var result=new List<McObjectId>();
        foreach(string selected in ids)
        {
            cancel();var edge=edges.edges.SingleOrDefault(e=>e.model_edge_id.Equals(selected,StringComparison.OrdinalIgnoreCase));
            if(edge==null)throw new DomainException("PRECONDITION_FAILED","The reference must be a current model_edge_id of this same native part.");
            var id=McObjectId.FromGsMarker(body.ID,long.Parse(edge.marker,CultureInfo.InvariantCulture));
            if(!id.IsEdge||id.ParentId!=body.ID||!id.ToString().Equals(selected,StringComparison.OrdinalIgnoreCase))
                throw new DomainException("OBJECT_IDENTITY_MISMATCH","The current native edge reference does not bind the requested part.");
            Geometry(id,body.ID,cancel);result.Add(id);
        }
        return result.ToArray();
    }
    internal static JsonElement Geometry(McObjectId id,McObjectId expectedBody,Action cancel)
    {
        cancel();if(id.IsNull||!id.IsEdge||id.ParentId!=expectedBody)
            throw new DomainException("SKETCH_REFERENCE_UNAVAILABLE","Require a current native edge of the selected part.");
        var body=expectedBody.GetObjectOfType<Mc3dSolid>();
        if(body==null||body.ID!=expectedBody)throw new DomainException("SKETCH_REFERENCE_UNAVAILABLE","The source part is unavailable.");
        using var geometry=Service.GetFEVGeometry(id,FEVGeometryQueryMode.IdMatchExact|FEVGeometryQueryMode.CacheIgnore|FEVGeometryQueryMode.EnvelopeExact|FEVGeometryQueryMode.CacheLock);
        if(geometry==null)throw new DomainException("SKETCH_REFERENCE_UNAVAILABLE","The exact native edge geometry is unavailable.");
        JsonElement result;
        if(geometry.GeometryType==G.EntityGeomType.kLine){using var line=geometry.LineSeg;result=Wire.Element(SketchGeometryReader.Line(line));}
        else if(geometry.GeometryType==G.EntityGeomType.kCircArc){using var arc=geometry.CircArc;result=Wire.Element(SketchGeometryReader.Arc(arc));}
        else throw new DomainException("SKETCH_REFERENCE_UNAVAILABLE","This retained sketch reference adapter supports native straight or circular edges.");
        new ModelInventoryBudget(cancel).Add(result);cancel();return result;
    }
}
