using System.Diagnostics.CodeAnalysis;
using Multicad;
using Multicad.DatabaseServices;
using Multicad.Mc3D;
using NanoCAD.Contracts;

namespace NanoCADBridge;

// Public construction identities are current McObjectId work IDs. They are
// not McDbObject.Guid values: the SDK documents GetObjectIdByGuid as the
// persistent-Guid -> work-ID mapping, not as a work-ID parser.
internal static class ModelNativeObjectResolver415
{
    static void Need([DoesNotReturnIf(false)] bool ok,string code,string message)
    {if(!ok)throw new DomainException(code,message);}
    static McObjectId Parse(string identity,string message)
    {
        Need(Guid.TryParseExact(identity,"B",out var guid)&&guid!=Guid.Empty,"INVALID_ARGUMENT",message);
        var id=new McObjectId(guid);
        Need(!id.IsNull&&id.ToString().Equals(identity,StringComparison.OrdinalIgnoreCase),"INVALID_ARGUMENT",message);
        return id;
    }
    static T Verify<T>(McObjectId requested,T? actual,McDocument owner,string code,string message) where T:McEntity
    {
        // Keep exact typed identity and actual native document admission. A
        // matching top-level document or coincident handle is not sufficient.
        var document=actual?.DbEntity.Document;
        if(actual==null||actual.ID!=requested||!actual.ID.ToString().Equals(requested.ToString(),StringComparison.OrdinalIgnoreCase)||
            document==null||document.ID!=owner.ID)
            throw new DomainException(code,message,false,new{component="exact_native_work_id_resolution",
                requested_native_id=requested.ToString(),resolved_native_id=actual?.ID.ToString(),
                selected_owner_document_id=owner.ID.ToString(),resolved_owner_document_id=document?.ID.ToString(),
                expected_type=typeof(T).FullName});
        return actual;
    }
    internal static SketchProfile Profile(string identity,McDocument owner,Action cancel)
    {
        cancel();return Profile(Parse(identity,"Read a current native profile ID."),owner,cancel);
    }
    internal static SketchProfile Profile(McObjectId identity,McDocument owner,Action cancel)
    {
        cancel();return Verify(identity,identity.IsNull?null:identity.GetObjectOfType<SketchProfile>(),owner,
            "PROFILE_OWNERSHIP_MISMATCH","The exact native profile ID is not present in this document.");
    }
    internal static PlanarSketch Sketch(string identity,McDocument owner,Action cancel)
    {
        cancel();return Sketch(Parse(identity,"Read the current native sketch ID."),owner,cancel);
    }
    internal static PlanarSketch Sketch(McObjectId identity,McDocument owner,Action cancel)
    {
        cancel();return Verify(identity,identity.IsNull?null:identity.GetObjectOfType<PlanarSketch>(),owner,
            "SKETCH_OWNERSHIP_MISMATCH","The exact PlanarSketch is absent from the selected part's actual native owner document.");
    }
    internal static WorkPlane Plane(string identity,McDocument owner,Action cancel)
    {
        cancel();var id=Parse(identity,"Choose an actual current native plane ID.");
        return Verify(id,id.GetObjectOfType<WorkPlane>(),owner,
            "DATUM_DOCUMENT_MISMATCH","The exact WorkPlane is unavailable in the selected part's actual native owner document.");
    }
}
