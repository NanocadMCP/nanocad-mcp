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
    sealed record Root(string Handle, Db.ObjectId Representation, Mc3dSolid Body, McObjectId[] Contents);
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
            if (!native.IsNull && native.GetObjectOfType<Mc3dSolid>() != null && native.QueryObjectType() != typeof(Mc3dAlienSolid)) return true;
        }
        return false;
    }
    static Plan Capture(ModifyArguments args, DocumentState state, Db.Transaction tx, Action cancel)
    {
        Need(args.TargetSpace == "model", "OUT_OF_SCOPE", "Whole native model deletion requires Model Space.");
        Need(args.Operations.All(op => op is EraseSpec), "INVALID_OPERATION_ORDER", "Batch whole-model erasures separately from other edits; all selected erase targets can share one request.");
        var drawing = ModelProfileDiscovery348.DrawingDocument(state);
        var db = state.Document.Database;
        var space = CadWriteSpace.Resolve(db, tx, "model");
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
            if (raw.GetType() != typeof(Db.Solid3d) || native.IsNull || native.QueryObjectType() == typeof(Mc3dAlienSolid)) continue;
            var body = native.GetObjectOfType<Mc3dSolid>();
            if (body == null) continue;
            Need(body.ID == native, "OBJECT_IDENTITY_MISMATCH", "The selected native model owner is unavailable.", new { handle });
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
            roots.Add(new(handle, rawId, body, owned.ToArray()));
        }
        Need(roots.Count > 0, "OBJECT_IDENTITY_MISMATCH", "No selected native model root remains.");
        var scope = roots.SelectMany(r => r.Contents.Append(r.Body.ID)).Concat(explicitlySelected).ToHashSet();
        var nativeScope = roots.SelectMany(r => r.Contents.Append(r.Body.ID)).ToHashSet();
        var autoErasedInputs = new HashSet<McObjectId>();
        var documents = roots.Select(r => ModelProfileDiscovery348.OwnerDocument(r.Body, state)).Append(drawing).GroupBy(d => d.ID).Select(g => g.First()).ToArray();
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
                started || cleanup != null ? "unknown" : "not_started", context, new { cause, stage, message = error.Message, cleanup = cleanup?.Message, targets = plan?.Targets,
                    verification_details = error is DomainException observed ? observed.Error.details : null }) with { warnings = [WriteRecovery.Warning] };
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
