using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using Multicad;
using Multicad.DatabaseServices;
using Multicad.Mc3D;
using NanoCAD.Contracts;
using Teigha.DatabaseServices;

namespace NanoCADBridge;

// MultiCAD feature edges have their own identity. Teigha B-rep subentity IDs
// from inspect_solid_topology must never be passed to AddFilletFeature.
internal static class ModelEdgeReader
{
    internal sealed record CircleGeometry(double[] center,double[] normal,double radius);
    internal sealed record Edge(string marker,string model_edge_id,string curve_type,double[] start,double[] end,double length,CircleGeometry? circle=null);
    internal sealed record Snapshot(EntityRef source_ref,string body_mc_id,string last_item_id,string edge_token,Edge[] edges);
    private static void Need(bool valid,string code,string message){if(!valid)throw new DomainException(code,message);}
    public static object Read(Arguments args,DocumentState state,Transaction tx,ObjectId model,string instance,CancellationToken cancel)
    {
        var db=state.Document.Database;
        long bits=unchecked((long)ulong.Parse(args.Handles![0],NumberStyles.HexNumber,CultureInfo.InvariantCulture));
        Need(db.TryGetObjectId(new Handle(bits),out var id)&&!id.IsNull&&id.IsValid&&!id.IsErased&&!id.IsEffectivelyErased,"OBJECT_NOT_FOUND","Model body handle is unavailable.");
        Need(tx.GetObject(id,OpenMode.ForRead) is Solid3d solid&&solid.GetType()==typeof(Solid3d)&&solid.OwnerId==model,"OUT_OF_SCOPE","Require one top-level native model representation.");
        return Capture(id,state,instance,cancel);
    }
    internal static Snapshot Capture(ObjectId representation,DocumentState state,string instance,CancellationToken cancel,Action? checkCancel=null)
    {
        void Budget(){checkCancel?.Invoke();cancel.ThrowIfCancellationRequested();}
        Budget();var inventoryBudget=new ModelInventoryBudget(Budget);
        var active=McDocument.ActiveDocument;
        Need(state.Document.IsNamedDrawing&&active!=null&&!string.IsNullOrWhiteSpace(active.Path)&&
             string.Equals(Path.GetFullPath(active.Path),Path.GetFullPath(state.Document.Name),StringComparison.OrdinalIgnoreCase),
             "DOCUMENT_CONTEXT_MISMATCH","The active MultiCAD document differs from the requested named drawing.");
        var bodyId=McObjectId.FromHandle(representation.Handle.Value,active!.ID);
        NativeTopologyReadTrace362.Observe("edge_body_id");
        var body=bodyId.GetObjectOfType<Mc3dSolid>();
        NativeTopologyReadTrace362.Observe("edge_body_lookup");
        Need(body!=null&&body.ID==bodyId&&!body.LastPartItemID.IsNull,"UNSUPPORTED_ENTITY_TYPE","Selected Solid3d has no available native model history.");
        Budget();var solidBody=body!.GetSolidBody();
        NativeTopologyReadTrace362.Observe("edge_solid_body");
        Need(solidBody!=null,"HISTORY_UNAVAILABLE","Native body geometry is unavailable.");
        var markers=solidBody!.GetEdges();Budget();
        NativeTopologyReadTrace362.Observe("edge_markers");
        Need(markers!=null&&markers.Length>0,"SUBENTITY_GEOMETRY_UNAVAILABLE","Native model has no readable edges.");
        var edges=new List<Edge>();var seen=new HashSet<long>();
        foreach(long marker in markers!)
        {
            Budget();Need(marker>0&&seen.Add(marker),"SUBENTITY_IDENTITY_UNAVAILABLE","Duplicate or invalid native model edge marker.");
            var edgeId=McObjectId.FromGsMarker(bodyId,marker);
            NativeTopologyReadTrace362.Observe("edge_native_id",marker);
            Need(!edgeId.IsNull&&edgeId.IsEdge&&edgeId.ParentId==bodyId&&edgeId.GsMarker==marker,
                "SUBENTITY_IDENTITY_UNAVAILABLE","Marker did not resolve to an edge owned by this model.");
            // Exact current native ID, WCS geometry, model-only lookup and no cache update.
            using var geometry=Service.GetFEVGeometry(edgeId,FEVGeometryQueryMode.IdMatchExact|FEVGeometryQueryMode.CacheIgnore|FEVGeometryQueryMode.EnvelopeExact|FEVGeometryQueryMode.CacheLock);
            NativeTopologyReadTrace362.Observe("edge_subentity_geometry",marker);
            var curve=geometry?.AsCurve();
            Need(curve!=null,"SUBENTITY_GEOMETRY_UNAVAILABLE","Native edge curve is unavailable; no partial inventory returned.");
            var start=curve!.StartPoint;var end=curve.EndPoint;double length=curve.LengthOfCurve;
            var coordinates=new[]{start.X,start.Y,start.Z,end.X,end.Y,end.Z,length};
            Need(coordinates.All(double.IsFinite)&&length>1e-9,"SUBENTITY_GEOMETRY_UNAVAILABLE","Native edge has invalid endpoints or length.");
            CircleGeometry? circle=null;
            if(geometry!.GeometryType==Multicad.Geometry.EntityGeomType.kCircArc)
            {
                using var arc=geometry.CircArc;
                if(arc!=null&&double.IsFinite(arc.Radius)&&arc.Radius>0&&
                   Math.Abs(length-2*Math.PI*arc.Radius)<=1e-7*Math.Max(1,length))
                    circle=new([arc.Center.X,arc.Center.Y,arc.Center.Z],[arc.Normal.X,arc.Normal.Y,arc.Normal.Z],arc.Radius);
            }
            var observed=new Edge(marker.ToString(CultureInfo.InvariantCulture),edgeId.ToString(),curve.GetType().Name,
                [start.X,start.Y,start.Z],[end.X,end.Y,end.Z],length,circle);
            inventoryBudget.Add(observed);edges.Add(observed);Budget();
            NativeTopologyReadTrace362.Observe("edge_descriptor",marker);
        }
        var ordered=edges.OrderBy(e=>long.Parse(e.marker,CultureInfo.InvariantCulture)).ToArray();
        string handle=Validation.NormalizeHandle(representation.Handle.ToString());
        var source=new EntityRef(instance,state.Id,handle);
        string last=body.LastPartItemID.ToString();
        Budget();var serialized=JsonSerializer.SerializeToUtf8Bytes(new{source,body_mc_id=bodyId.ToString(),last_item_id=last,edges=ordered},Wire.Json);
        if(serialized.LongLength>8L*1024*1024)throw new DomainException("RESPONSE_LIMIT_EXCEEDED","Native model edge snapshot exceeds the actual8MiB serialized byte budget.");
        var token=Convert.ToHexString(SHA256.HashData(serialized));Budget();
        return new(source,bodyId.ToString(),last,token,ordered);
    }
}
