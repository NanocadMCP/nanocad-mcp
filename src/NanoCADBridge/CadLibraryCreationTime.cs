using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Text.Json;
using Multicad;
using Multicad.DatabaseServices;
using Multicad.DatabaseServices.StandardObjects;
using NanoCAD.Contracts;
using Teigha.DatabaseServices;
using Application = HostMgd.ApplicationServices.Application;

namespace NanoCADBridge;

// Only a newly cloned exact local BlockReference can acquire this witnessed
// native field. Existing source application data never enters this route.
internal static class CadLibraryCreationTime
{
    internal const string Corroboration = "IMcBlockRef.CreationTime; exact native object/owner/current-document round-trips; target/owner/RegApp metadata and revision unchanged across two reads";
    internal sealed record Observation([property: System.Text.Json.Serialization.JsonIgnore] LibraryBlockCreationStamp Stamp, BlockLibraryNativeCreationTime Receipt);
    static void Need([DoesNotReturnIf(false)] bool value, string message)
    { if (!value) throw new DomainException("NATIVE_BLOCK_LIBRARY_MISMATCH", message); }
    static string Id(ObjectId id) => Validation.NormalizeHandle(id.Handle.ToString());
    static bool Live(ObjectId id) => !id.IsNull && id.IsValid && !id.IsErased && !id.IsEffectivelyErased;
    static string Hex(long value) => unchecked((ulong)value).ToString("X16", CultureInfo.InvariantCulture);

    internal static Observation Read(string bridge, string document, string sourceHandle, JsonElement sourceXdata, BlockReference target, JsonElement before,
        DocumentState state, Transaction transaction, Action cancel, Func<DBObject, JsonElement> metadata)
    {
        string targetHandle = Id(target.ObjectId), ownerHandle = Id(target.OwnerId); string? appHandle = null, mcIdentity = null, mcOwner = null, mcTop = null;
        long? first = null, second = null; string phase = "context"; object? identityFacts = null;
        var wrappers = new List<McObject>();
        T Keep<T>(T value) where T : McObject { wrappers.Add(value); return value; }
        string[] cleanupErrors = [];
        string[] CloseWrappers()
        {
            var unique = wrappers.Distinct<McObject>(ReferenceEqualityComparer.Instance).ToArray(); wrappers.Clear();
            var errors = new List<string>();
            foreach (var wrapper in unique) try { wrapper.Dispose(); } catch (Exception error) { errors.Add(error.GetType().FullName + ": " + error.Message); }
            return errors.ToArray();
        }
        var database = state.Document.Database; var targetId = target.ObjectId; var ownerId = target.OwnerId;
        void Current()
        {
            cancel(); Need(ReferenceEquals(Application.DocumentManager.MdiActiveDocument, state.Document) &&
                target.Database?.UnmanagedObject == database.UnmanagedObject && Live(targetId) && target.ObjectId == targetId && target.OwnerId == ownerId &&
                target.GetType() == typeof(BlockReference), "Creation-time corroboration lost its exact active local native block-reference identity.");
        }
        try
        {
            phase = "creation_time_layout"; _ = LibraryBlockCreationTime.ParseAdded(sourceXdata, before.GetProperty("xdata"));
            phase = "context";
            Current(); Need(Live(ownerId) && transaction.GetObject(ownerId, OpenMode.ForRead) is BlockTableRecord,
                "The new block-reference creation-time field has no actual local definition owner.");
            var owner = (BlockTableRecord)transaction.GetObject(ownerId, OpenMode.ForRead);
            Need(owner.Database?.UnmanagedObject == database.UnmanagedObject && !owner.IsLayout && !owner.IsFromExternalReference,
                "Creation-time corroboration requires the actual local static definition owner.");
            var table = (RegAppTable)transaction.GetObject(database.RegAppTableId, OpenMode.ForRead);
            Need(table.Has(LibraryBlockCreationTime.Application), "The native creation-time application is not actually registered.");
            var appId = table[LibraryBlockCreationTime.Application]; Need(Live(appId), "The native creation-time application ID is unavailable.");
            var app = transaction.GetObject(appId, OpenMode.ForRead) as RegAppTableRecord;
            Need(app != null && app.GetType() == typeof(RegAppTableRecord) && app.ObjectId == appId && app.OwnerId == database.RegAppTableId &&
                app.Database?.UnmanagedObject == database.UnmanagedObject && app.Name == LibraryBlockCreationTime.Application,
                "The creation-time application has a different native registration, type or owner.");
            appHandle = Id(appId); var ownerBefore = metadata(owner); var appBefore = metadata(app); string revision = state.Revision;
            phase = "native_identity";
            // Public SDK conversion from the actual platform ObjectId, not a
            // handle/name search or a cast of an unmanaged object pointer.
            var nativeId = McObjectId.FromOldIdPtr(targetId.OldIdPtr);
            Need(!nativeId.IsNull && nativeId.ToOldIdPtr() == targetId.OldIdPtr,
                "The native block-reference ID did not round-trip to the exact platform ObjectId.");
            var native = Keep(nativeId.GetObjectOfType<McBlockRef>() ?? throw new DomainException("NATIVE_BLOCK_LIBRARY_MISMATCH", "The exact native IMcBlockRef wrapper is unavailable."));
            var entity = Keep(native.DbEntity ?? throw new DomainException("NATIVE_BLOCK_LIBRARY_MISMATCH", "The native block-reference entity interface is unavailable."));
            var host = Keep(entity.Document ?? throw new DomainException("NATIVE_BLOCK_LIBRARY_MISMATCH", "The native containing definition document is unavailable."));
            var active = Keep(McDocument.ActiveDocument ?? throw new DomainException("NATIVE_BLOCK_LIBRARY_MISMATCH", "The actual current MultiCAD document is unavailable."));
            var top = Keep(active.TopDoc ?? throw new DomainException("NATIVE_BLOCK_LIBRARY_MISMATCH", "The current MultiCAD top document is unavailable."));
            var hostTop = Keep(host.TopDoc ?? throw new DomainException("NATIVE_BLOCK_LIBRARY_MISMATCH", "The native owner top document is unavailable."));
            var model = ((BlockTable)transaction.GetObject(database.BlockTableId, OpenMode.ForRead))[BlockTableRecord.ModelSpace];
            mcIdentity = nativeId.ToString(); mcOwner = host.ID.ToString(); mcTop = top.ID.ToString();
            bool objectIdentity = native.ID == nativeId, objectRoundtrip = native.ID.ToOldIdPtr() == targetId.OldIdPtr;
            bool ownerIdentity = host.ID == nativeId.IdHostDoc, ownerRoundtrip = host.ID.ToOldIdPtr() == ownerId.OldIdPtr;
            bool topIdentity = hostTop.ID == top.ID && nativeId.IdHostTopDoc == top.ID, modelRoundtrip = top.ModelSpaceId.ToOldIdPtr() == model.OldIdPtr;
            identityFacts = new
            {
                object_identity_matches = objectIdentity,
                object_platform_roundtrip = objectRoundtrip,
                containing_document_matches = ownerIdentity,
                physical_btr_owner_roundtrip = ownerRoundtrip,
                top_document_matches = topIdentity,
                current_drawing_modelspace_roundtrip = modelRoundtrip
            };
            Need(objectIdentity && objectRoundtrip && ownerIdentity && ownerRoundtrip && topIdentity && modelRoundtrip,
                "The native block-reference, physical BTR owner or current drawing top-document correspondence differs.");
            phase = "native_creation_time_read"; first = native.CreationTime; Current(); second = native.CreationTime;
            Need(first.HasValue && first.GetValueOrDefault() != 0 && first == second, "The actual native block-reference creation-time getter is unavailable or unstable.");
            var stamp = LibraryBlockCreationTime.ReadAdded(sourceXdata, before.GetProperty("xdata"), first.GetValueOrDefault());
            Need(native.ID == nativeId && native.ID.ToOldIdPtr() == targetId.OldIdPtr && host.ID.ToOldIdPtr() == ownerId.OldIdPtr &&
                nativeId.IdHostTopDoc == top.ID, "Native creation-time reading changed an actual object/document correspondence.");
            var activeAfter = Keep(McDocument.ActiveDocument ?? throw new DomainException("NATIVE_BLOCK_LIBRARY_MISMATCH", "The current native document disappeared after the creation-time read."));
            var topAfter = Keep(activeAfter.TopDoc ?? throw new DomainException("NATIVE_BLOCK_LIBRARY_MISMATCH", "The current native top document disappeared after the creation-time read."));
            Need(topAfter.ID == top.ID && topAfter.ModelSpaceId.ToOldIdPtr() == model.OldIdPtr, "Native creation-time reading changed the actual current drawing correspondence.");
            phase = "read_side_effects";
            // Release only these SDK COM wrappers; never a native DBObject or
            // source-side object. All actual stored data is checked afterwards.
            cleanupErrors = CloseWrappers(); Need(cleanupErrors.Length == 0, "A native creation-time interface wrapper did not release normally.");
            Current(); Need(state.Revision == revision && NativeMetadataEquality.Same(before, metadata(target)) &&
                NativeMetadataEquality.Same(ownerBefore, metadata(owner)) && NativeMetadataEquality.Same(appBefore, metadata(app)),
                "Native creation-time corroboration changed observed target/owner/RegApp metadata or the drawing revision.");
            EntityRef Ref(ObjectId id) => new(bridge, document, Id(id));
            return new(stamp, new(sourceHandle, Ref(targetId), Ref(ownerId), Ref(appId), Hex(first.GetValueOrDefault()),
                stamp.raw_records, true, Corroboration));
        }
        catch (Exception error)
        {
            cleanupErrors = cleanupErrors.Concat(CloseWrappers()).ToArray();
            throw new DomainException("NATIVE_BLOCK_LIBRARY_MISMATCH", "The added native block-reference creation-time field could not be independently corroborated.", false,
                new
                {
                    source_handle = sourceHandle,
                    target_handle = targetHandle,
                    target_owner = ownerHandle,
                    registered_application_handle = appHandle,
                    phase,
                    native_object_id = mcIdentity,
                    native_owner_id = mcOwner,
                    native_top_document_id = mcTop,
                    identity_facts = identityFacts,
                    first_native_value_hex16 = first.HasValue ? Hex(first.Value) : null,
                    second_native_value_hex16 = second.HasValue ? Hex(second.Value) : null,
                    actual_xdata = LibraryBlockMetadataComparison.FirstDifference(JsonSerializer.SerializeToElement<object?>(null, Wire.Json), before.GetProperty("xdata"))?.actual,
                    cause = (error as DomainException)?.Error,
                    error_type = error.GetType().FullName,
                    cleanup_errors = cleanupErrors
                });
        }
    }
}
