using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using Multicad;
using Multicad.DatabaseServices;
using Multicad.Mc3D;
using NanoCAD.Contracts;
using NanoCAD.Ipc;
using Db = Teigha.DatabaseServices;

namespace NanoCADBridge;

// Erase a complete native part through its owning entity, never its raw B-rep
// representation or a fabricated per-feature cascade. Undo is informational.
internal static class ModelBodyEraseWriter429
{
    sealed record AlienData(McObjectId Id, McDbEntity Entity);
    sealed record NativeEraseState(string native_id, uint? status_flags, bool erased, string? error);
    sealed record Root(string Handle, Db.ObjectId Representation, McObjectId NativeId, Mc3dSolid Body, McObjectId[] Contents)
    {
        internal McDocument? DirectOwner { get; init; }
        internal McDbEntity? DirectEntity { get; init; }
        internal AlienData[] OwnedAlienData { get; init; } = [];
    }
    sealed record Plan(Root[] Roots, string[] Targets, Dictionary<string, Db.ObjectId> RawTargets, McObjectId[] Scope, ModifyArguments Ordinary)
    {
        // Public read evidence includes these proven-owned sketch inputs. The
        // existing post-write auxiliary-history observation keeps its own scope.
        internal McObjectId[] AutoErasedInputs { get; init; } = [];
    }
    static void Need([DoesNotReturnIf(false)] bool ok, string code, string message, object? details = null)
    { if (!ok) throw new DomainException(code, message, false, details); }

    internal static bool Handles(ModifyArguments args, DocumentState state, Db.Transaction tx)
    {
        if (!args.Operations.Any(op => op is EraseSpec)) return false;
        var loaded = AppDomain.CurrentDomain.GetAssemblies().Select(a => a.GetName().Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        return loaded.Contains("mapimgd") && loaded.Contains("MapiBaseTypes") && HasNative(args, state, tx);
    }
    [MethodImpl(MethodImplOptions.NoInlining)]
    static bool HasNative(ModifyArguments args, DocumentState state, Db.Transaction tx)
    {
        var drawing = McDocument.ActiveDocument;
        if (drawing == null) return false;
        foreach (var handle in args.Operations.OfType<EraseSpec>().SelectMany(op => op.Handles))
        {
            var id = CadStructureWriter.Find(state.Document.Database, handle);
            if (tx.GetObject(id, Db.OpenMode.ForRead) is not Db.Solid3d) continue;
            var native = McObjectId.FromHandle(id.Handle.Value, drawing.ID);
            if (!native.IsNull && native.GetObjectOfType<Mc3dSolid>() != null) return true;
        }
        return false;
    }
    static Plan Capture(ModifyArguments args, DocumentState state, Db.Transaction tx, Action cancel)
    {
        Need(args.TargetSpace == "model", "OUT_OF_SCOPE", "Whole native model deletion requires Model Space.");
        Need(args.Operations.All(op => op is EraseSpec), "INVALID_OPERATION_ORDER", "Batch whole-model erasures separately from other edits; all selected erase targets can share one request.");
        var db = state.Document.Database;
        var space = CadWriteSpace.Resolve(db, tx, "model");
        // A direct solid also exists in an unnamed drawing. Prove the current
        // platform database through the actual native Model Space identity;
        // parametric roots retain their existing named-document checks below.
        var drawing = McDocument.ActiveDocument;
        var top = drawing?.TopDoc;
        Need(HostMgd.ApplicationServices.Application.DocumentManager.MdiActiveDocument == state.Document &&
            drawing != null && top != null && top.ModelSpaceId.ToOldIdPtr() == space.OldIdPtr,
            "DOCUMENT_CONTEXT_MISMATCH", "The current native drawing does not match the requested Model Space.");
        var targets = args.Operations.SelectMany(op => op.Handles).Distinct(StringComparer.Ordinal).ToArray();
        var roots = new List<Root>(); var explicitlySelected = new HashSet<McObjectId>();
        var rawTargets = new Dictionary<string, Db.ObjectId>(StringComparer.Ordinal);
        foreach (var handle in targets)
        {
            cancel(); var rawId = CadStructureWriter.Find(db, handle);
            var raw = tx.GetObject(rawId, Db.OpenMode.ForRead) as Db.Entity;
            Need(raw != null && raw.OwnerId == space, "OUT_OF_SCOPE", "Erase selects existing top-level entities in the requested Model Space.", new { handle });
            rawTargets.Add(handle, rawId);
            var layer = (Db.LayerTableRecord)tx.GetObject(raw.LayerId, Db.OpenMode.ForRead);
            Need(!layer.IsLocked && !layer.IsDependent, "LAYER_UNAVAILABLE", "The selected entity's layer is locked or externally owned.", new { handle });
            var native = McObjectId.FromHandle(rawId.Handle.Value, drawing.ID);
            if (!native.IsNull) explicitlySelected.Add(native);
            if (raw.GetType() != typeof(Db.Solid3d) || native.IsNull) continue;
            var body = native.GetObjectOfType<Mc3dSolid>();
            if (body == null) continue;
            Need(body.ID == native, "OBJECT_IDENTITY_MISMATCH", "The selected native model owner is unavailable.", new { handle });
            if (native.QueryObjectType() == typeof(Mc3dAlienSolid))
            {
                roots.Add(DirectRoot(handle, rawId, (Db.Solid3d)raw, body, drawing, top, space, state, tx, cancel));
                continue;
            }
            Need(ModelHistoryEditGuard.CurrentRepresentation(body, state, tx, cancel) == rawId,
                "OBJECT_IDENTITY_MISMATCH", "Select the unique current representation of the complete native model.", new { handle });
            Need(body.HostBooleanFeatureID.IsNull, "OUT_OF_SCOPE", "Select a standalone complete model rather than an operand owned by another Boolean feature.", new { handle });
            var owner = ModelProfileDiscovery348.OwnerDocument(body, state);
            var owned = new List<McObjectId>();
            foreach (var itemId in ModelInventoryBudget.History(body, cancel).Distinct())
            {
                cancel(); var item = itemId.GetObjectOfType<HistoryItem>();
                // Part composition can also list drawing curves, native sketch
                // inputs and foreign supports. Referencing them does not own them.
                if (item == null) continue;
                Need(item.ID == itemId, "OBJECT_IDENTITY_MISMATCH", "A model history lookup returned another native identity.", new { handle, native_id = itemId.ToString() });
                if (itemId != body.ID && item.HostSolidID != body.ID) continue;
                Need(item.DbEntity.Document?.ID == owner.ID, "DEPENDENCY_OWNERSHIP_MISMATCH", "An owned model history item has another native document.", new { handle, native_id = itemId.ToString() });
                owned.Add(itemId);
            }
            roots.Add(new(handle, rawId, native, body, owned.ToArray()));
        }
        Need(roots.Count > 0, "OBJECT_IDENTITY_MISMATCH", "No selected native model root remains.");
        var scope = roots.SelectMany(r => r.Contents.Append(r.Body.ID).Concat(r.OwnedAlienData.Select(data => data.Id))).Concat(explicitlySelected).ToHashSet();
        var nativeScope = roots.SelectMany(r => r.Contents.Append(r.Body.ID).Concat(r.OwnedAlienData.Select(data => data.Id))).ToHashSet();
        var autoErasedInputs = new HashSet<McObjectId>();
        var documents = roots.Select(r => r.DirectOwner ?? ModelProfileDiscovery348.OwnerDocument(r.Body, state)).Append(drawing).Append(top).GroupBy(d => d.ID).Select(g => g.First()).ToArray();
        var allItems = documents.SelectMany(d => ModelProfileDiscovery348.Ids<HistoryItem>(d, cancel)).Distinct().ToArray();
        var survivorIds = allItems.Where(id => !nativeScope.Contains(id) && !explicitlySelected.Contains(id)).ToArray();
        var survivorSketches = survivorIds.Select(id => id.GetObjectOfType<PlanarSketch>()).Where(s => s != null && s.Status.ToString() != "Erased").ToArray();
        foreach (var root in roots)
            foreach (var id in root.Contents.Append(root.Body.ID).Distinct())
            {
                cancel(); var item = id.GetObjectOfType<HistoryItem>();
                Need(item != null, "DEPENDENCY_INVENTORY_UNAVAILABLE", "A selected native item disappeared before erasure.");
                var dependents = item.GetDependent();
                Need(dependents != null, "DEPENDENCY_INVENTORY_UNAVAILABLE", "Native dependent inventory is unavailable.");
                foreach (var dependent in dependents)
                {
                    cancel(); if (scope.Contains(dependent)) continue;
                    var actual = dependent.GetObjectOfType<HistoryItem>();
                    Need(actual != null && actual.ID == dependent && actual.Status.ToString() == "Erased", "MODEL_HAS_EXTERNAL_DEPENDENTS", "A retained native object depends on the selected model; include the intended dependent model or retain this source.", new { handle = root.Handle, dependent_id = dependent.ToString() });
                }
                if (item is not PlanarSketch sketch || !sketch.AutoeraseOfInclObjects) continue;
                var included = sketch.GetContainingObjects(true, false, true);
                Need(included != null, "DEPENDENCY_INVENTORY_UNAVAILABLE", "The selected sketch's auto-erased input inventory is unavailable.");
                foreach (var member in included)
                {
                    cancel(); if (scope.Contains(member)) continue;
                    var value = member.GetObject();
                    bool child = value is McEntity entity && entity.ID == member && entity.DbEntity.ParentId == sketch.ID && entity.DbEntity.Document?.ID == sketch.DbEntity.Document.ID;
                    bool abstractInput = value == null && sketch.ContainsObject(member);
                    if (abstractInput) { using var geometry = sketch.GetItemGeometry(member, false); abstractInput = geometry != null; }
                    Need((child || abstractInput) && !survivorSketches.Any(peer => peer!.ContainsObject(member)), "MODEL_ERASE_SHARED_INPUT", "Native sketch erasure would also remove an unselected external or shared source. Select that source only if its removal is intended.", new { handle = root.Handle, sketch_id = sketch.ID.ToString(), source_id = member.ToString() });
                    scope.Add(member);
                    autoErasedInputs.Add(member);
                }
            }
        // GetDependent documents only history reactors. Check actual surviving
        // consumers as well, including critical and subentity-owner references.
        bool Affected(McObjectId input)
        {
            if (input.IsNull) return false;
            if (scope.Contains(input)) return true;
            // A face may have an external creating feature while its current
            // body parent is selected. Observe both identities.
            var parent = input.GetParentId(false);
            var creator = input.GetHostFeatureOrParentId(false);
            return !parent.IsNull && scope.Contains(parent) || !creator.IsNull && scope.Contains(creator);
        }
        // Drawing documentation is an independent native family; do not rely
        // on it also appearing in the HistoryItem filter or reactor inventory.
        // Scan every actual local block record as well as native child objects,
        // including retained Paper/nested views and associated sections.
        var documentation = documents.SelectMany(document => ModelProfileDiscovery348.Ids<DrawingView>(document, cancel)
            .Concat(ModelProfileDiscovery348.Ids<Section>(document, cancel))).ToHashSet();
        var blocks = (Db.BlockTable)tx.GetObject(db.BlockTableId, Db.OpenMode.ForRead);
        foreach (Db.ObjectId blockId in blocks)
        {
            cancel(); var block = (Db.BlockTableRecord)tx.GetObject(blockId, Db.OpenMode.ForRead);
            if (block.IsDependent || block.IsFromExternalReference || block.IsFromOverlayReference) continue;
            foreach (Db.ObjectId entityId in block)
            {
                cancel(); if (entityId.IsErased || entityId.IsEffectivelyErased) continue;
                var id = McObjectId.FromHandle(entityId.Handle.Value, drawing.ID);
                if (!id.IsNull && (id.GetObjectOfType<DrawingView>() != null || id.GetObjectOfType<Section>() != null)) documentation.Add(id);
            }
        }
        foreach (var id in documentation.Where(id => !scope.Contains(id)))
        {
            cancel(); var view = id.GetObjectOfType<DrawingView>(); var section = id.GetObjectOfType<Section>();
            Need(view != null || section != null, "DEPENDENCY_INVENTORY_UNAVAILABLE", "A retained documentation object disappeared before erasure.");
            var item = (HistoryItem?)view ?? section!;
            Need(item.ID == id, "OBJECT_IDENTITY_MISMATCH", "Documentation lookup returned another native identity.");
            if (item.Status.ToString() == "Erased") continue;
            var sources = view != null ? view.GetTop3dEntitiesInView() : section!.GetTop3dEntitiesForSectioning();
            Need(sources != null, "DEPENDENCY_INVENTORY_UNAVAILABLE", "A retained view or section source inventory is unavailable.");
            foreach (var source in sources)
                Need(!Affected(source), "MODEL_HAS_EXTERNAL_DEPENDENTS", "Retained drawing documentation refers to the selected model. Explicitly retire its supported views and section/cut owners before erasing this source.",
                    new { dependent_id = id.ToString(), source_id = source.ToString(), documentation_type = view != null ? "drawing_view" : "section" });
        }
        foreach (var id in survivorIds)
        {
            cancel(); var item = id.GetObjectOfType<HistoryItem>();
            if (item == null || item.Status.ToString() == "Erased") continue;
            var inputs = item.GetDependsOn(false, false); var critical = item.GetCriticalDependsOn();
            Need(inputs != null && critical != null, "DEPENDENCY_INVENTORY_UNAVAILABLE", "A retained native object's input inventory is unavailable.");
            foreach (var input in inputs.Concat(critical))
            {
                cancel();
                Need(!Affected(input), "MODEL_HAS_EXTERNAL_DEPENDENTS", "A retained native object consumes the selected model or one of its inputs.", new { dependent_id = id.ToString(), source_id = input.ToString() });
            }
            if (item is ProjectionSketchEnt projection)
                Need(!Affected(projection.SrcObjectID), "MODEL_HAS_EXTERNAL_DEPENDENTS", "A retained native projection refers to the selected model.", new { dependent_id = id.ToString() });
            if (item is PlanarSketch retainedSketch)
                Need(!Affected(retainedSketch.PlanarEntityID), "MODEL_HAS_EXTERNAL_DEPENDENTS", "A retained native sketch uses a selected support.", new { dependent_id = id.ToString() });
            if (item is SketchProfile retainedProfile)
                Need(!Affected(retainedProfile.ParentSketchID) && !Affected(retainedProfile.Dependent3dFeatureID), "MODEL_HAS_EXTERNAL_DEPENDENTS", "A retained native profile is linked to the selected model.", new { dependent_id = id.ToString() });
        }
        var nativeHandles = roots.Select(r => r.Handle).ToHashSet(StringComparer.Ordinal);
        var ordinary = args with { Operations = targets.Where(h => !nativeHandles.Contains(h)).Select(h => (EditSpec)new EraseSpec([h])).ToArray() };
        // Run the existing ordinary erasure admission before any native write;
        // an unsupported ordinary target must not leave a partially erased batch.
        if (ordinary.Operations.Length > 0)
        {
            var resolved = CadModifier.Resolve(ordinary, db, tx, cancel);
            CadModifier.Preview(ordinary, resolved, tx, cancel);
        }
        return new(roots.ToArray(), targets, rawTargets, nativeScope.ToArray(), ordinary) { AutoErasedInputs = autoErasedInputs.ToArray() };
    }

    static Root DirectRoot(string handle, Db.ObjectId rawId, Db.Solid3d raw, Mc3dSolid body, McDocument drawing,
        McDocument top, Db.ObjectId space, DocumentState state, Db.Transaction tx, Action cancel)
    {
        Need(body.ID.IdHostTopDoc == top.ID, "DOCUMENT_CONTEXT_MISMATCH", "The direct source belongs to another native drawing.", new { handle });
        var matches = ((Db.BlockTableRecord)tx.GetObject(space, Db.OpenMode.ForRead)).Cast<Db.ObjectId>().Where(id =>
        {
            cancel(); return !id.IsErased && !id.IsEffectivelyErased && tx.GetObject(id, Db.OpenMode.ForRead) is Db.Solid3d &&
                McObjectId.FromHandle(id.Handle.Value, drawing.ID) == body.ID;
        }).ToArray();
        Need(matches.Length == 1 && matches[0] == rawId, "OBJECT_IDENTITY_MISMATCH", "The direct source has no unique current Model Space representation.", new { handle });
        var alien = body.ID.GetObjectOfType<Mc3dAlienSolid>();
        Need(alien != null && alien.ID == body.ID && body.HostBooleanFeatureID.IsNull && alien.HostSmFlattenFeature.IsNull,
            "UNSUPPORTED_SOLID_HISTORY", "A direct erase requires a standalone solid rather than a Boolean operand or sheet-unfold result.", new { handle });
        Need(!raw.RecordHistory && !raw.ShowHistory && !raw.HasFields, "UNSUPPORTED_SOLID_HISTORY",
            "Direct erasure requires an exact native Solid3d without enabled history or fields.", new { handle });
        CadSolidEditor.CheckGeometry(raw, cancel);
        var entity = body.DbEntity; var owner = entity?.Document;
        Need(entity != null && owner != null && !owner.ID.IsNull && owner.TopDoc?.ID == top.ID,
            "DEPENDENCY_OWNERSHIP_MISMATCH", "The direct source has no actual local native owner document.", new { handle });
        var children = entity.ChildrenIds; var dataIds = entity.GetChildrenIdsEx(ChildrenSysFilterType.ChildrenSysFilter_AlienSolidData);
        Need(children != null && dataIds != null && children.Count == children.Distinct().Count() && dataIds.Count == dataIds.Distinct().Count() &&
            children.ToHashSet().SetEquals(dataIds), "DEPENDENCY_OWNERSHIP_MISMATCH", "The direct source has unavailable or unclassified owned child objects.", new { handle });
        ModelInventoryBudget.Ids(dataIds, cancel);
        var owned = dataIds.Append(body.ID).ToHashSet();
        var data = dataIds.Select(id => ReadOwnedAlienData(handle, id, body.ID, owner.ID, top.ID,
            drawing.ID, rawId, state, owned, cancel)).ToArray();
        // The SDK filter proves the data role independently of the composition
        // list. Only those exact owned IDs may accompany the non-parametric self;
        // another feature, datum or drawing consumer remains external history.
        var self = body.ID.GetObjectOfType<HistoryItem>(); var contents = ModelInventoryBudget.History(body, cancel);
        Need(self != null && self.ID == body.ID && !self.IsSuppressed && self.Status.ToString() == "Ok" &&
            contents.Contains(body.ID) && contents.Distinct().Count() == contents.Count && contents.All(owned.Contains) && owned.Contains(body.LastPartItemID),
            "MODEL_HAS_EXTERNAL_DEPENDENTS", "The direct source has retained drawing documentation or other native history outside its exact owned self/data scope.", new { handle });
        var inputs = self.GetDependsOn(false, false); var critical = self.GetCriticalDependsOn(); var dependents = self.GetDependent();
        Need(inputs != null && critical != null && dependents != null && inputs.Concat(critical).Concat(dependents).All(owned.Contains),
            "MODEL_HAS_EXTERNAL_DEPENDENTS", "Retained native dependencies refer to the direct source. Explicitly retire its supported documentation owners before source erasure.", new { handle });
        using var reactors = raw.GetPersistentReactorIds();
        foreach (Db.ObjectId id in reactors)
        {
            cancel(); var observed = CadPersistentReferenceObservation.Read(raw, id, tx, cancel);
            bool inactive;
            try { inactive = NativePersistentReferences.IsInactive(observed.State); }
            catch (DomainException error)
            {
                throw new DomainException("UNSUPPORTED_SOLID_DEPENDENCIES", "The direct source has an unavailable or foreign persistent reference.", false,
                    new { handle, observation = observed.Facts, policy = error.Error });
            }
            Need(inactive, "UNSUPPORTED_SOLID_DEPENDENCIES", "The direct source has a live persistent reactor requiring its owning route.",
                new { handle, observation = observed.Facts });
        }
        return new(handle, rawId, body.ID, body, [body.ID]) { DirectOwner = owner, DirectEntity = entity, OwnedAlienData = data };
    }

    static AlienData ReadOwnedAlienData(string handle, McObjectId id, McObjectId parent, McObjectId document, McObjectId topDocument,
        McObjectId drawingDocument, Db.ObjectId rootPhysical, DocumentState state, HashSet<McObjectId> owned, Action cancel)
    {
        // SDK MCSTypes.h1302 and 3DCore.h6119-6129 define the data role and its
        // IMc3dSolid inheritance. QueryObjectTypeID reports Class.TypeID through
        // managed registration; an unavailable result is not a failed native QI.
        var expectedClass = new Guid("0000001B-3D3D-AAAA-AAAA-050B00000000");
        string phase = "native_identity"; Guid? actualClass = null;
        string? reportedType = null, objectType = null, objectId = null, solidType = null, solidId = null, typeObservationError = null;
        object? genericEntity = null, solidEntity = null, solidAfterAssociation = null, ownerAssociation = null;
        object Facts() => new
        {
            source_handle = handle,
            native_id = id.ToString(),
            phase,
            sdk_data_role_id = expectedClass.ToString("B"),
            observed_registered_type_id = actualClass?.ToString("B"),
            registered_type_available = actualClass.HasValue && actualClass.Value != Guid.Empty,
            registered_type_observation_error = typeObservationError,
            role_source = "IMcDbEntity.GetChildrenIdsEx(ChildrenSysFilter_AlienSolidData)",
            reported_managed_type = reportedType,
            returned_object_type = objectType,
            returned_object_id = objectId,
            solid_interface_type = solidType,
            solid_interface_id = solidId,
            expected_parent_id = parent.ToString(),
            expected_document_id = document.ToString(),
            expected_top_document_id = topDocument.ToString(),
            generic_entity = genericEntity,
            solid_entity = solidEntity,
            native_owner_association = ownerAssociation,
            solid_entity_after_association = solidAfterAssociation
        };
        (bool Complete, bool Consistent) ObserveEntity(McDbEntity? entity, Action<object> record)
        {
            string? entityId = null, actualParent = null, directDocument = null, resolvedDocument = null, hostDocument = null, hostTopDocument = null, error = null;
            bool? entityMatches = null, parentMatches = null, directDocumentMatches = null, resolvedDocumentMatches = null, hostDocumentMatches = null, hostTopDocumentMatches = null;
            bool? complete = null, consistent = null;
            uint? flags = null;
            try
            {
                if (entity == null) { complete = false; consistent = true; return (false, true); }
                // McDbEntity has no public ID property. Its public Entity getter
                // returns the actual native entity, whose complete ID must agree.
                var actualEntity = entity.Entity;
                Need(actualEntity != null, "CAPABILITY_UNAVAILABLE", "The owned data database view has no actual native entity identity.");
                var actualId = actualEntity.ID; entityId = actualId.ToString(); entityMatches = actualId == id;
                var nativeParent = entity.ParentId; actualParent = nativeParent.IsNull ? null : nativeParent.ToString(); parentMatches = nativeParent == parent;
                // These are actual native document identities, not a document
                // reconstructed from a name, physical handle or parent alone.
                var nativeHost = actualId.IdHostDoc; hostDocument = nativeHost.IsNull ? null : nativeHost.ToString(); hostDocumentMatches = nativeHost.IsNull ? null : nativeHost == document;
                var nativeTop = actualId.IdHostTopDoc; hostTopDocument = nativeTop.IsNull ? null : nativeTop.ToString(); hostTopDocumentMatches = nativeTop.IsNull ? null : nativeTop == topDocument;
                var nativeDocument = entity.DocumentID;
                directDocument = nativeDocument.IsNull ? null : nativeDocument.ToString(); directDocumentMatches = nativeDocument.IsNull ? null : nativeDocument == document;
                var managedDocument = entity.Document;
                if (managedDocument != null)
                { resolvedDocument = managedDocument.ID.ToString(); resolvedDocumentMatches = !managedDocument.ID.IsNull && managedDocument.ID == document; }
                flags = (uint)entity.StatusFlags;
                // An internal data object can lack a resolvable managed
                // Document. Locality still needs both positive host-ID proofs;
                // every available direct/resolved document must also agree.
                consistent = entityMatches == true && parentMatches == true && hostDocumentMatches != false && hostTopDocumentMatches != false &&
                    directDocumentMatches != false && resolvedDocumentMatches != false && (flags.Value & (uint)McDbObjectStateFlags.Erased) == 0;
                complete = consistent == true && hostDocumentMatches == true && hostTopDocumentMatches == true;
                return (complete.Value, consistent.Value);
            }
            catch (Exception failure) { error = failure.GetType().FullName; throw; }
            finally
            {
                record(new
                {
                    available = entity != null,
                    entity_id = entityId,
                    entity_identity_matches = entityMatches,
                    actual_parent_id = actualParent,
                    parent_matches = parentMatches,
                    native_document_id = directDocument,
                    native_document_matches = directDocumentMatches,
                    resolved_document_id = resolvedDocument,
                    resolved_document_matches = resolvedDocumentMatches,
                    host_document_id = hostDocument,
                    host_document_matches = hostDocumentMatches,
                    host_top_document_id = hostTopDocument,
                    host_top_document_matches = hostTopDocumentMatches,
                    positive_locality_complete = complete,
                    known_values_consistent = consistent,
                    status_flags = flags,
                    observation_error = error
                });
            }
        }
        try
        {
            cancel();
            try { actualClass = id.QueryObjectTypeID(); }
            catch (OperationCanceledException) { throw; }
            catch (Exception error) when (error is not OutOfMemoryException) { typeObservationError = error.GetType().FullName; }
            try { reportedType = id.QueryObjectType()?.FullName; }
            catch (OperationCanceledException) { throw; }
            catch (Exception error) when (error is not OutOfMemoryException) { typeObservationError ??= error.GetType().FullName; }
            var value = id.GetObject(); objectType = value?.GetType().FullName; objectId = value?.ID.ToString();
            // The documented generic entity view also exposes ownership when
            // no concrete solid wrapper is available. These are observations,
            // not a substitute for the required solid QI/identity checks below.
            phase = "generic_entity_observation";
            var genericLocality = ObserveEntity(value?.Cast<McEntity>(false)?.DbEntity, observation => genericEntity = observation);
            phase = "native_identity";
            Need(!id.IsNull && (!actualClass.HasValue || actualClass.Value == Guid.Empty || actualClass.Value == expectedClass),
                "DEPENDENCY_OWNERSHIP_MISMATCH", "The public AlienSolidData child filter conflicts with a known incompatible registered object type.");
            Need(value != null && value.ID == id, "CAPABILITY_UNAVAILABLE",
                "The actual native AlienSolidData object has no supported managed object wrapper.");
            phase = "public_solid_interface";
            // The public filter supplies the native data role. Public Cast
            // independently requires the SDK's genuine IMc3dSolid QueryInterface;
            // an empty/unavailable registered Class.TypeID never supplies it.
            // GetObjectOfType first checks a registered concrete type and can
            // reject this unregistered data class before that supported QI.
            var actual = value.Cast<Mc3dSolid>(false); solidType = actual?.GetType().FullName; solidId = actual?.ID.ToString();
            Need(actual != null && actual.ID == id, "CAPABILITY_UNAVAILABLE",
                "The actual AlienSolidData object does not expose the public native IMc3dSolid wrapper.");
            phase = "native_ownership"; var entity = actual.DbEntity;
            Need(entity != null, "CAPABILITY_UNAVAILABLE", "The actual AlienSolidData entity interface is unavailable.");
            var solidLocality = ObserveEntity(entity, observation => solidEntity = observation);
            Need(genericLocality.Consistent && solidLocality.Consistent,
                "DEPENDENCY_OWNERSHIP_MISMATCH", "The exact native AlienSolidData child has a conflicting actual identity, parent or document, or is erased.");
            if (!solidLocality.Complete)
            {
                // Reopened internal data can lack every document/host-ID view.
                // Only the actual current owner's public canonical Data lookup
                // supplies the missing positive association; nulls alone never do.
                phase = "native_owner_association";
                var current = McDocument.ActiveDocument;
                Need(HostMgd.ApplicationServices.Application.DocumentManager.MdiActiveDocument == state.Document &&
                    current != null && current.ID == drawingDocument && current.TopDoc?.ID == topDocument &&
                    rootPhysical.IsValid && !rootPhysical.IsErased && !rootPhysical.IsEffectivelyErased &&
                    rootPhysical.Database?.UnmanagedObject == state.Document.Database.UnmanagedObject &&
                    McObjectId.FromHandle(rootPhysical.Handle.Value, current.ID) == parent,
                    "DOCUMENT_CONTEXT_MISMATCH", "The current physical/native root context changed before the owning data lookup.");
                string beforeRevision = state.Revision;
                var read = NativeModelHistory421.LoadOwnedAlienDataAssociation();
                Need(state.Revision == beforeRevision, "STALE_DOCUMENT", "Loading the owning data reader changed the document revision; no erase may start.",
                    new { before_revision = beforeRevision, after_revision = state.Revision });
                var buffer = new char[NativeOwnedDataAssociation.BufferLength];
                int status = read(drawingDocument.ToString(), document.ToString(), topDocument.ToString(),
                    unchecked((ulong)rootPhysical.Handle.Value), parent.ToString(), id.ToString(), buffer, buffer.Length, out int nativePhase);
                ownerAssociation = new
                {
                    native_hresult = "0x" + unchecked((uint)status).ToString("X8"),
                    native_phase = nativePhase,
                    before_revision = beforeRevision,
                    after_revision = state.Revision
                };
                Need(state.Revision == beforeRevision, "STALE_DOCUMENT", "The owning data lookup changed the document revision; no erase may start.",
                    new { before_revision = beforeRevision, after_revision = state.Revision, native_status = ownerAssociation });
                var association = NativeOwnedDataAssociation.Parse(status, nativePhase, buffer,
                    new(drawingDocument.ToString(), topDocument.ToString(), document.ToString(), parent.ToString(), id.ToString()));
                ownerAssociation = association;
                current = McDocument.ActiveDocument;
                Need(HostMgd.ApplicationServices.Application.DocumentManager.MdiActiveDocument == state.Document &&
                    current != null && current.ID == drawingDocument && current.TopDoc?.ID == topDocument &&
                    rootPhysical.IsValid && !rootPhysical.IsErased && !rootPhysical.IsEffectivelyErased &&
                    rootPhysical.Database?.UnmanagedObject == state.Document.Database.UnmanagedObject &&
                    McObjectId.FromHandle(rootPhysical.Handle.Value, current.ID) == parent && actual.ID == id,
                    "OBJECT_IDENTITY_MISMATCH", "The physical root or current data identity changed during the owning association read.");
                var afterEntity = actual.DbEntity;
                Need(afterEntity != null, "CAPABILITY_UNAVAILABLE", "The required native data entity view is unavailable after the owning association read.");
                var observedAfter = ObserveEntity(afterEntity, observation => solidAfterAssociation = observation);
                Need(observedAfter.Consistent,
                    "DEPENDENCY_OWNERSHIP_MISMATCH", "The verified owning data association conflicts with its current native entity state.");
                Need(state.Revision == beforeRevision, "STALE_DOCUMENT", "The final owning data observation changed the document revision; no erase may start.",
                    new { before_revision = beforeRevision, after_revision = state.Revision, association });
            }
            phase = "native_dependencies";
            var consumed = actual.GetDependent(); var inputs = actual.GetDependsOn(false, false); var critical = actual.GetCriticalDependsOn();
            Need(consumed != null && inputs != null && critical != null, "DEPENDENCY_INVENTORY_UNAVAILABLE",
                "The actual AlienSolidData native dependency inventory is unavailable.");
            Need(consumed.Concat(inputs).Concat(critical).All(owned.Contains), "MODEL_HAS_EXTERNAL_DEPENDENTS",
                "Owned direct-solid data has an external native dependency.");
            return new(id, entity);
        }
        catch (OperationCanceledException) { throw; }
        catch (DomainException error)
        { throw new DomainException(error.Error.code, error.Error.message, error.Error.retryable, new { observation = Facts(), cause = error.Error.details }); }
        catch (Exception error)
        {
            throw new DomainException("DEPENDENCY_INVENTORY_UNAVAILABLE", "The actual native AlienSolidData child could not be completely observed.", false,
            new { observation = Facts(), error_type = error.GetType().FullName });
        }
    }
    internal static void Preflight(ModifyArguments args, DocumentState state, Db.Transaction tx, Action? cancel = null) => Capture(args, state, tx, cancel ?? (() => { }));

    // Same capture as actual erase admission, with no tracker, job, write,
    // commit or post-command mutation. Keep mixed-mode SDK types behind the
    // already-loaded runtime check performed by Handles.
    [MethodImpl(MethodImplOptions.NoInlining)]
    internal static EraseAssessment444 ReadAssessment(ModifyArguments args, DocumentState state, Db.Transaction tx, Action cancel)
    {
        var plan = Capture(args, state, tx, cancel);
        var roots = plan.Roots.ToDictionary(root => root.Handle, StringComparer.Ordinal);
        var targets = plan.Targets.Select(handle =>
        {
            cancel();
            var entity = (Db.Entity)tx.GetObject(plan.RawTargets[handle], Db.OpenMode.ForRead);
            roots.TryGetValue(handle, out var root);
            return new EraseAssessmentTarget444(new(args.BridgeId, args.DocumentId, handle),
                EntityAdapter.Summary(entity, entity.OwnerId, args.BridgeId, args.DocumentId).type,
                root == null ? "ordinary" : "native_owner", root?.Body.ID.ToString(),
                root?.Contents.Select(id => id.ToString()).ToArray() ?? []);
        }).ToArray();
        var scope = plan.Scope.Concat(plan.AutoErasedInputs).Distinct().Select(id => { cancel(); return id.ToString(); }).ToArray();
        return EraseAssessment444.Eligible(args.Operations.SelectMany(op => op.Handles).ToArray(), targets, scope);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    internal static Result Execute(ModifyArguments args, DocumentState state, WriteJob job, ReadContext context, JsonLog log)
    {
        McUndoPoint? tracker = null; Plan? plan = null; bool started = false; string stage = "preparation";
        void Close() { var value = tracker; tracker = null; if (value == null) return; try { value.Stop(); } finally { value.Dispose(); } }
        Result Failure(Exception error)
        {
            if (job.CommittedResult is { } committed) return committed;
            Exception? cleanup = null; try { Close(); } catch (Exception e) { cleanup = e; }
            string cause = error is DomainException domain ? domain.Error.code : "NATIVE_MODEL_ERASE_FAILED";
            return job.Error(started || cleanup != null ? "WRITE_OUTCOME_UNKNOWN" : cause, started ? "Whole native model erasure needs current-state inspection; no rollback is claimed." : error.Message,
                started || cleanup != null ? "unknown" : "not_started", context, new
                {
                    cause,
                    stage,
                    message = error.Message,
                    cleanup = cleanup?.Message,
                    targets = plan?.Targets,
                    verification_details = error is DomainException observed ? observed.Error.details : null
                }) with
            { warnings = [WriteRecovery.Warning] };
        }
        try
        {
            using (var tx = state.Document.Database.TransactionManager.StartTransaction()) plan = Capture(args, state, tx, job.CheckCancellation);
            tracker = new McUndoPoint(false); tracker.Start();
            foreach (var root in plan.Roots)
            {
                // Another explicitly selected root can already have retired
                // this representation through its owning native cascade.
                if (root.Representation.IsErased || root.Representation.IsEffectivelyErased) continue;
                job.CheckCancellation(); stage = "native_owner_erase"; started = true;
                log.Write(job.Operation, job.OriginalRequestId, "native_model_erase", transaction: root.Handle, operationId: job.OperationId);
                Need(root.Body.DbEntity.Erase(), "NATIVE_MODEL_ERASE_FAILED", "The owning native model refused erasure.", new { handle = root.Handle });
            }
            McObjectManager.UpdateAll(); job.CheckCancellation(); stage = "explicit_ordinary_targets";
            using (var tx = state.Document.Database.TransactionManager.StartTransaction())
            {
                var remaining = plan.Ordinary.Operations.Where(op =>
                {
                    // Find deliberately rejects erased objects. A selected
                    // ordinary source may already be erased by native ownership.
                    var id = plan.RawTargets[op.Handles[0]];
                    return !id.IsErased && !id.IsEffectivelyErased;
                }).ToArray();
                if (remaining.Length > 0)
                {
                    var ordinary = plan.Ordinary with { Operations = remaining };
                    var resolved = CadModifier.Resolve(ordinary, state.Document.Database, tx, job.CheckCancellation);
                    CadModifier.Preview(ordinary, resolved, tx, job.CheckCancellation);
                    CadModifier.Apply(ordinary, resolved, tx, new List<Db.Entity>(), job);
                }
                tx.Commit();
            }
            job.BeginCommit(); Close(); stage = "post_command_verification";
            return NativePostCommandVerification422.Defer(job, () =>
            {
                foreach (var handle in plan.Targets)
                {
                    job.CheckCancellation();
                    // Verify the exact preflight identity after erasure, without
                    // resolving it through the live-only structural lookup.
                    var id = plan.RawTargets[handle];
                    Need(id.IsErased || id.IsEffectivelyErased, "NATIVE_MODEL_ERASE_FAILED", "A requested erase target remains resident.", new { handle });
                }
                foreach (var root in plan.Roots.Where(root => root.DirectEntity != null))
                {
                    // These retained SDK interface wrappers expose the native
                    // erased-state flag; absence of a later live lookup alone
                    // would not establish retirement of the owned data.
                    var observations = new[] { new AlienData(root.NativeId, root.DirectEntity!) }.Concat(root.OwnedAlienData).Select(data =>
                    {
                        job.CheckCancellation();
                        try
                        {
                            var flags = data.Entity.StatusFlags;
                            return new NativeEraseState(data.Id.ToString(), (uint)flags, (flags & McDbObjectStateFlags.Erased) != 0, null);
                        }
                        catch (OperationCanceledException) { throw; }
                        catch (Exception error)
                        {
                            return new NativeEraseState(data.Id.ToString(), null, false, error.GetType().FullName + ": " + error.Message);
                        }
                    }).ToArray();
                    var observation = new
                    {
                        root_handle = root.Handle,
                        native_owner_id = root.NativeId.ToString(),
                        owner = observations[0],
                        owned_alien_data = observations.Skip(1).ToArray()
                    };
                    Need(observations.All(value => value.erased), "NATIVE_MODEL_ERASE_FAILED",
                        "The exact native direct owner or its proven-owned AlienSolidData did not report an erased state.", observation);
                    // Optional trace cannot change an otherwise verified native
                    // result. Failed verification always retains the same facts
                    // in the structured error, without relying on the log sink.
                    try
                    {
                        log.Write(job.Operation, job.OriginalRequestId, "native_direct_owner_erased",
                        transaction: System.Text.Json.JsonSerializer.Serialize(observation, Wire.Json), operationId: job.OperationId);
                    }
                    catch (Exception) { }
                }
                int residual = 0, unavailable = 0;
                foreach (var id in plan.Scope)
                {
                    job.CheckCancellation();
                    try { var item = id.GetObjectOfType<HistoryItem>(); if (item != null && item.Status.ToString() != "Erased") residual++; }
                    catch (OperationCanceledException) { throw; }
                    catch (Exception) { unavailable++; } // Optional auxiliary observation after proved requested erasure.
                }
                var data = new ModifiedEntities(args.OperationId, "committed", false, [], plan.Targets.Select(h => new EntityRef(args.BridgeId, args.DocumentId, h)).ToArray(), [], [], "not_guaranteed");
                var warnings = new List<string> { WriteRecovery.Warning, "NATIVE_MODEL_ERASED: Complete selected model roots were erased through their native owners; source entities were independently removed only when explicitly selected." };
                if (plan.Roots.Any(root => root.DirectEntity != null)) warnings.Add("NATIVE_DIRECT_OWNER_ERASED: Actual selected non-parametric owners and their proven-owned AlienSolidData reported native erased states after command completion.");
                if (residual > 0) warnings.Add("NATIVE_MODEL_AUXILIARIES_RETAINED: Native erase retained history storage objects: " + residual + ".");
                if (unavailable > 0) warnings.Add("NATIVE_MODEL_AUXILIARIES_UNAVAILABLE: Post-erasure history storage could not be observed for " + unavailable + " native IDs.");
                var result = Result.Ok(job.OriginalRequestId, data, context, warnings.ToArray());
                Need(System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(result, Wire.Json).Length <= Wire.PayloadBudget, "RESPONSE_LIMIT_EXCEEDED", "The complete erasure result exceeds the response byte budget.");
                job.RecordCommit(result); return result;
            }, Failure);
        }
        catch (Exception error) { return Failure(error); }
    }
}
