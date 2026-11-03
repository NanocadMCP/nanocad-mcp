using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using Multicad;
using Multicad.DatabaseServices;
using Multicad.Geometry;
using Multicad.Mc3D;
using NanoCAD.Contracts;
using NanoCAD.Ipc;
using Teigha.DatabaseServices;

namespace NanoCADBridge;

// These are MultiCAD feature-face identities, not Teigha B-rep native_id values.
// Read planar support data so an opening face can be selected by its datum
// plane; no cross-kernel ID conversion is guessed.
internal static class ModelFaceReader
{
    private sealed record Plane(double[] origin,double[] normal);
    private sealed record Face(string marker,string model_face_id,string? surface_type,
        Plane? plane,string? detail_unavailable_reason,CylinderGeometry? cylinder=null);
    private sealed record CylinderGeometry(double[] axis_origin,double[] axis_direction,double radius,double length);
    private static void Need(bool ok,string code,string message)
    {if(!ok)throw new DomainException(code,message);}
    internal static object Capture(ObjectId representation,DocumentState state,string instance,CancellationToken cancel,Action? checkCancel=null)
    {
        void Budget(){checkCancel?.Invoke();cancel.ThrowIfCancellationRequested();}
        Budget();var inventoryBudget=new ModelInventoryBudget(Budget);
        var active=McDocument.ActiveDocument;
        Need(state.Document.IsNamedDrawing&&active!=null&&!string.IsNullOrWhiteSpace(active.Path)&&
            string.Equals(Path.GetFullPath(active.Path),Path.GetFullPath(state.Document.Name),StringComparison.OrdinalIgnoreCase),
            "DOCUMENT_CONTEXT_MISMATCH","Native model faces require the exact active named DWG.");
        var bodyId=McObjectId.FromHandle(representation.Handle.Value,active!.ID);
        NativeTopologyReadTrace362.Observe("face_body_id");
        var body=bodyId.GetObjectOfType<Mc3dSolid>();
        NativeTopologyReadTrace362.Observe("face_body_lookup");
        Need(body!=null&&body.ID==bodyId&&!body.LastPartItemID.IsNull,
            "UNSUPPORTED_ENTITY_TYPE","Selected Solid3d has no native model history.");
        Budget();var solidBody=body!.GetSolidBody();
        NativeTopologyReadTrace362.Observe("face_solid_body");
        Need(solidBody!=null,"HISTORY_UNAVAILABLE","Native model body geometry is unavailable.");
        var markers=solidBody!.GetFaces();Budget();
        NativeTopologyReadTrace362.Observe("face_markers");
        Need(markers!=null&&markers.Length>0,
            "SUBENTITY_IDENTITY_UNAVAILABLE","Native model contains no readable faces.");
        var faces=new List<Face>();var seen=new HashSet<long>();
        foreach(long marker in markers!)
        {
            Budget();
            Need(marker>0&&seen.Add(marker),"SUBENTITY_IDENTITY_UNAVAILABLE","Duplicate or invalid native model face marker.");
            var faceId=McObjectId.FromGsMarker(bodyId,marker);
            NativeTopologyReadTrace362.Observe("face_native_id",marker);
            Need(!faceId.IsNull&&faceId.IsFace&&faceId.ParentId==bodyId&&faceId.GsMarker==marker,
                "SUBENTITY_IDENTITY_UNAVAILABLE","Marker did not resolve to a face owned by this model.");
            // Exact current native ID, WCS geometry, model-only lookup and no cache update.
            using var geometry=Service.GetFEVGeometry(faceId,FEVGeometryQueryMode.IdMatchExact|FEVGeometryQueryMode.CacheIgnore|FEVGeometryQueryMode.EnvelopeExact|FEVGeometryQueryMode.CacheLock);
            NativeTopologyReadTrace362.Observe("face_subentity_geometry",marker);
            // Read typed borrowed descriptors. AsSurface constructs owning curved
            // surface wrappers in this SDK; do not materialize them for inspection.
            using var planar=geometry?.Plane;
            using var cylinder=geometry?.GeometryType==EntityGeomType.kCylinderSegment?geometry.CylinderSurf:null;
            Plane? plane=null;
            if(planar!=null)
            {
                var origin=planar.Origin;var normal=planar.Normal;
                var values=new[]{origin.X,origin.Y,origin.Z,normal.X,normal.Y,normal.Z};
                if(values.All(double.IsFinite)&&normal.X*normal.X+normal.Y*normal.Y+normal.Z*normal.Z>1e-18)
                    plane=new([origin.X,origin.Y,origin.Z],[normal.X,normal.Y,normal.Z]);
            }
            var observed=new Face(marker.ToString(CultureInfo.InvariantCulture),faceId.ToString(),
                planar?.GetType().Name??geometry?.GeometryType.ToString(),plane,
                plane==null?geometry==null?"native_surface_unavailable":cylinder!=null?null:"not_a_finite_planar_support":null,
                cylinder is {} c?new CylinderGeometry([c.BasePnt.X,c.BasePnt.Y,c.BasePnt.Z],[c.Axis.X,c.Axis.Y,c.Axis.Z],c.Radius,c.Length):null);
            inventoryBudget.Add(observed);faces.Add(observed);
            NativeTopologyReadTrace362.Observe("face_descriptor",marker);
        }
        var ordered=faces.OrderBy(f=>long.Parse(f.marker,CultureInfo.InvariantCulture)).ToArray();
        var source=new EntityRef(instance,state.Id,Validation.NormalizeHandle(representation.Handle.ToString()));
        string last=body.LastPartItemID.ToString();
        Budget();var serialized=JsonSerializer.SerializeToUtf8Bytes(new
            {source,body_mc_id=bodyId.ToString(),last_item_id=last,faces=ordered},Wire.Json);
        if(serialized.LongLength>8L*1024*1024)throw new DomainException("RESPONSE_LIMIT_EXCEEDED","Native model face snapshot exceeds the actual8MiB serialized byte budget.");
        string token=Convert.ToHexString(SHA256.HashData(serialized));Budget();
        return new{source_ref=source,body_mc_id=bodyId.ToString(),last_item_id=last,
            face_token=token,identity_scope="current_bridge_document_revision_model_snapshot",
            complete=true,faces=ordered};
    }
}
