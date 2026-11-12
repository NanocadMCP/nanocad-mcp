using System.Text.Json;
using Multicad;
using Multicad.DatabaseServices;
using Multicad.DatabaseServices.Constraints;
using NanoCAD.Contracts;
using NanoCAD.Ipc;
using Db = Teigha.DatabaseServices;

namespace NanoCADBridge;

internal static class NativeSketchConstraintWriter385
{
    const string Warning = "INTERNAL_SKETCH_SOLVED: Native relations/control and rebuilt retained profile consumers were observed. Connected internal geometry may move according to the solver. Inspect the intended useful part through MCP; full private MCS status and native Undo/rollback are not guaranteed.";
    static void Need([System.Diagnostics.CodeAnalysis.DoesNotReturnIf(false)] bool ok, string code, string message) => NativeSketchMembers385.Need(ok, code, message);
    static string Key(McObjectId id) => id.ToString();
    static string Sketch(ModifyArguments args) => args.Operations[0] switch { SketchConstraintsEdit edit => edit.SketchId, ModelSketchConstraintParameterSpec379 edit => edit.SketchId, _ => throw new DomainException("INVALID_ARGUMENT", "Unsupported native sketch request.") };
    static NativeSketchConstraintState385.Snapshot Resolve(ModifyArguments args, DocumentState state, Db.Transaction tx, Action cancel)
    {
        Need(args.Operations.Length == 1, "INVALID_OPERATION_ORDER", "Native sketch constraint edits are sole operations."); var value = NativeSketchConstraintState385.Capture(args.Operations[0].Handles.Single(), Sketch(args), args, state, tx, cancel);
        DrawingViewSketchWriteScope282.RequireSupported(value.Sketch.Sketch);
        if (args.Operations[0] is SketchConstraintsEdit add)
        {
            Need(add.ExpectedBindingToken != null && value.Inputs.Token == add.ExpectedBindingToken, "PRECONDITION_FAILED", "Read the current native_inputs.binding_token from inspect_model_history(sketch_ids).");
            NativeSketchMembers385.ValidateInputs(add.Constraints, value.Inputs);
            using var manager = NativeSketchMembers385.Manager(value.Sketch.Sketch); var currentIds = manager.GetAllConstraints(); Need(currentIds != null, "CONSTRAINTS_UNAVAILABLE", "The exact current constraint inventory is unavailable.");
            foreach (var spec in add.Constraints)
            {
                using var expected = NativeSketchMembers385.Info(spec, value.Inputs);
                foreach (var id in currentIds)
                {
                    cancel(); using var actual = manager.GetConstraintInfo(id); Need(actual != null, "CONSTRAINTS_UNAVAILABLE", "An existing native relation became unreadable.");
                    bool duplicate = NativeSketchMembers385.RequestedReferences(actual, expected, spec, value.Inputs, comparePoints: true);
                    if (!duplicate && spec.Second != null && spec.Kind is "parallel" or "perpendicular" or "collinear" or "tangent" or "smooth" or "equal" or "concentric" or "coincident" or "symmetric")
                    {
                        var reversed = spec with
                        {
                            First = spec.Second,
                            Second = spec.First,
                            FirstPoint = spec.SecondPoint,
                            SecondPoint = spec.FirstPoint,
                            FirstSegmentIndex = spec.SecondSegmentIndex,
                            SecondSegmentIndex = spec.FirstSegmentIndex
                        };
                        using var reversedInfo = NativeSketchMembers385.Info(reversed, value.Inputs);
                        duplicate = NativeSketchMembers385.RequestedReferences(actual, reversedInfo, reversed, value.Inputs, comparePoints: true);
                    }
                    if (duplicate) throw new DomainException("PRECONDITION_FAILED", "An existing same-type relation already binds these members and points; edit its existing control instead of adding a duplicate.", false,
                        new { sketch_id = Key(value.Sketch.Sketch.ID), existing_constraint_id = Key(id), requested_kind = spec.Kind, diagnosis = "duplicate_binding", solver_status = "not_evaluated" });
                }
            }
        }
        else
        {
            var edit = (ModelSketchConstraintParameterSpec379)args.Operations[0]; var control = NativeSketchConstraintState385.ControlOf(value, edit.ConstraintId, cancel);
            Need(edit.ExpectedParameterToken == ParameterToken(control) && edit.ExpectedVariableId.Equals(Key(control.Variable), StringComparison.OrdinalIgnoreCase), "PRECONDITION_FAILED", "Read the fresh actual relation/parameter token and variable ID.");
            Need(edit.Value > 0 && (control.Kind != "angle" || edit.Value < Math.PI), "INVALID_ARGUMENT", "Use a positive millimeter control or an angle strictly between zero and pi radians.");
            ControlSpec(control, edit.Value);
        }
        return value;
    }
    internal static void Preflight(ModifyArguments args, DocumentState state, Db.Transaction tx, Action? cancel = null) => Resolve(args, state, tx, cancel ?? (() => { }));
    internal static string ParameterToken(NativeSketchConstraintState385.Control value) => Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(new { state_token = value.State.Token, constraint_id = Key(value.Constraint), variable_id = Key(value.Variable), value.Kind, value.Value, value.Expression, parameter_variable_id = value.ParameterVariable.IsNull ? null : Key(value.ParameterVariable), parameter_value = value.ParameterValue, parameter_raw_value = value.ParameterRawValue, relation_value = value.RelationValue, relation_expression = value.RelationExpression }, Wire.Json)));
    internal static SketchConstraintSpec ControlSpec(NativeSketchConstraintState385.Control value, double requested)
    {
        using var manager = NativeSketchMembers385.Manager(value.State.Sketch.Sketch); using var info = manager.GetConstraintInfo(value.Constraint); Need(info != null, "CONSTRAINTS_UNAVAILABLE", "The current control relation is unavailable.");
        NativeSketchMembers385.Member Member(IConstraintObjectInfo? reference)
        {
            Need(reference != null && !reference.ObjectId.IsNull, "SKETCH_BINDING_UNAVAILABLE", "The actual dimensional member is unavailable.");
            var matches = value.State.Inputs.Members.Where(m => m.Id == reference.ObjectId || m.ObjectId == reference.ObjectId && m.Segment == reference.PolylineSegmentIndex).ToArray(); Need(matches.Length == 1, "SKETCH_BINDING_UNAVAILABLE", "The actual control reference is not one unique native member/segment."); return matches[0];
        }
        string Point(IConstraintObjectInfo? reference, NativeSketchMembers385.Member member)
        {
            Need(reference != null, "SKETCH_BINDING_UNAVAILABLE", "The dimensional point is unavailable."); string? role = NativeSketchMembers385.PointRole(value.State.Inputs, member, reference);
            Need(role != null, "OUT_OF_SCOPE", "This control requires a decoded native endpoint/center selector or an exact finite WCS pick; arbitrary reference frames and point semantics are not guessed."); return role;
        }
        var first = Member(info.FirstObject); if (value.Kind is "radius" or "diameter") return new(value.Kind, Key(first.Id), null, null, null, requested);
        var second = Member(info.SecondObject); if (value.Kind == "angle") return new("angle", Key(first.Id), Key(second.Id), null, null, requested);
        string a = Point(info.FirstObject, first), b = Point(info.SecondObject, second);
        if (value.Kind == "distance" && first.Curve.Kind == "line" && first.Id == second.Id && a == "start" && b == "end") return new("length", Key(first.Id), null, null, null, requested);
        Need(first.Id != second.Id || a != b, "OUT_OF_SCOPE", "This control needs distinct actual point references; its dimensional type is retained."); return new(value.Kind, Key(first.Id), Key(second.Id), a, b, requested);
    }
    internal static ModelSketchConstraintParameterRead379 ReadControl(Arguments args, DocumentState state, Db.Transaction tx, string id, Action cancel)
    {
        try
        {
            var write = new ModifyArguments(args.BridgeId!, args.DocumentId!, args.ExpectedRevision!, "", "Millimeters", []); var value = NativeSketchConstraintState385.Capture(args.Handles!.Single(), args.ModelSketchIds!.Single(), write, state, tx, cancel);
            var control = NativeSketchConstraintState385.ControlOf(value, id, cancel); var spec = ControlSpec(control, control.Value); bool geometryCoherent = true;
            try { NativeSketchMembers385.ValidateSolved([spec], value.Inputs); } catch (DomainException error) when (error.Error.code is "DRAWING_RELATION_MISMATCH" or "CONSTRAINT_SOLVE_FAILED") { geometryCoherent = false; }
            bool? parameterCoherent = control.ParameterValue.HasValue ? ModelParameterSnapshot.Near(control.ParameterValue.Value, control.Value) : null;
            bool descriptorMatches = ModelParameterSnapshot.Near(control.RelationValue, control.Value) && (string.IsNullOrWhiteSpace(control.RelationExpression) || double.TryParse(control.RelationExpression, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double descriptorValue) && ModelParameterSnapshot.Near(descriptorValue, control.Value)); bool resolution = !geometryCoherent || parameterCoherent == false;
            return new(id, true, ParameterToken(control), null, Key(control.Variable), Key(control.Member), spec.Kind, control.Value, control.Kind == "angle" ? "radians" : "millimeters", control.Expression, control.VariableState.GetProperty("host_object_id").GetString(), null,
                value.Sketch.Profiles.EnumerateArray().Select(p => p.GetProperty("profile_id").GetString()!).ToArray(), Consumers(value), NativeSketchConstraintState385.Scope + (resolution ? " The exact retained literal is eligible for native reconciliation; current geometry/bound evaluated parameter differs and is not claimed coherent. A write must verify requested literal, control and solved geometry agree." : ""), Wire.Element(new { value.Relations, member_bindings = value.Inputs.Stored, control_geometry_coherent = geometryCoherent, control_parameter_coherent = parameterCoherent, control_variable_value_coherent = true, needs_native_resolution = resolution, parameter_binding_getter_available = !control.ParameterVariable.IsNull, parameter_value_getter_available = control.ParameterValue.HasValue, parameter_raw_value_available = control.ParameterRawValue.HasValue, parameter_variable_id = control.ParameterVariable.IsNull ? null : Key(control.ParameterVariable), parameter_value = control.ParameterValue, parameter_raw_value = control.ParameterRawValue, raw_parameter_value_matches_literal = control.ParameterRawValue.HasValue ? (bool?)ModelParameterSnapshot.Near(control.ParameterRawValue.Value, control.Value) : null, relation_control_value = control.RelationValue, relation_variable_expression = control.RelationExpression, descriptor_matches_current_variable = descriptorMatches, descriptor_lifetime = "dynamic synchronization with the owner MCS value is not guaranteed", variable_value = control.Value }));
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception error) { return new(id, false, null, error is DomainException d ? d.Error.code + ": " + d.Message : error.GetType().Name, null, null, null, null, "relation_declared_mm_or_radians", null, null, null, [], [], NativeSketchConstraintState385.Scope, error is DomainException failure && failure.Error.code == "CONSTRAINT_PARAMETER_UNAVAILABLE" && failure.Error.details != null ? Wire.Element(new { parameter_read_failure = failure.Error.details }) : null); }
    }
    static string[] Consumers(NativeSketchConstraintState385.Snapshot state)
    {
        var profiles = state.Sketch.Profiles.EnumerateArray().Select(p => p.GetProperty("profile_id").GetString()!).ToHashSet(StringComparer.OrdinalIgnoreCase); var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var id in state.Part.Contents)
        {
            var extrusion = id.GetObjectOfType<Multicad.Mc3D.ExtrudeFeature>(); if (extrusion != null && profiles.Contains(Key(extrusion.ProfileID))) result.Add(Key(id));
            var revolution = id.GetObjectOfType<Multicad.Mc3D.RevolveFeature>(); if (revolution != null && profiles.Contains(Key(revolution.ProfileID))) result.Add(Key(id));
            var sweep = id.GetObjectOfType<Multicad.Mc3D.ExtrudeAlongPathFeature>(); if (sweep != null && profiles.Contains(Key(sweep.ProfileID))) result.Add(Key(id));
            var loft = id.GetObjectOfType<Multicad.Mc3D.LoftFeature>(); if (loft == null) continue;
            if (profiles.Contains(Key(loft.ProfileID))) result.Add(Key(id));
            for (int index = 0; index < loft.SectionsCount; index++) { var section = loft.GetSection(index); if (section != null && profiles.Contains(Key(section.ID))) result.Add(Key(id)); }
        }
        return result.OrderBy(id => id, StringComparer.OrdinalIgnoreCase).ToArray();
    }
    static void Bytes(Result result, Action cancel) { cancel(); Need(JsonSerializer.SerializeToUtf8Bytes(result, Wire.Json).Length <= Wire.PayloadBudget, "RESPONSE_LIMIT_EXCEEDED", "The actual native sketch receipt exceeds transport bytes."); }
    // Persistent Auto.Add(false) returned no ID for this exact request on412.
    // The SDK sample's editor/MCS route remains a different native path; enter
    // it once and witness the actual editable sketch instead of retrying Auto.
    static bool EditorCircleCenterFix(SketchConstraintSpec[] specs, NativeSketchMembers385.Snapshot inputs) => specs.Any(spec =>
        spec.Kind == "fix_point" && spec.FirstPoint == "center" && inputs.Members.Any(member => Key(member.Id).Equals(spec.First, StringComparison.OrdinalIgnoreCase) &&
            member.Owned && !member.ReferenceOnly && member.Id == member.ObjectId && member.Segment == null && member.NativeType == "kCircArc" && member.Curve.Kind == "circle"));
    // SDK3DCore: Auto outside editing returns the internal sketch network.
    // Existing owned inputs already have that complete network. Submit their
    // relation-only change there, without imposing the geometry sample's
    // editor transition; actual native Add/solve/preservation must still pass.
    static NativeSketchRelations410.Baseline? PersistentBaseline(NativeSketchConstraintState385.Snapshot before, Action cancel)
    {
        if (!before.Inputs.Members.All(member => member.Owned && !member.ReferenceOnly)) return null;
        var sketch = before.Sketch.Sketch; var owner = sketch.DbEntity.Document;
        Need(!sketch.IsInEditMode && owner != null && !owner.ID.IsNull && before.Part.Body.DbEntity.Document?.ID == owner.ID &&
            NativeSketchConstraintState385.Same(before.Inputs.Stored.GetProperty("owner_document_id").GetString(), Key(owner.ID)) &&
            NativeSketchConstraintState385.Same(before.Inputs.Stored.GetProperty("member_context_document_id").GetString(), Key(owner.ID)),
            "CONSTRAINT_SCOPE_MISMATCH", "The persistent relation route requires its exact outside-editor sketch/owner/input context.");
        using var manager = NativeSketchMembers385.Manager(sketch); // Auto; never force MCS outside editing.
        Need(manager.DocumentId == owner.ID && manager.PlanarSketchId == sketch.ID && !manager.ScopeId.IsNull,
            "CONSTRAINT_SCOPE_MISMATCH", "The persistent manager lost its exact current sketch scope.");
        var inventory = manager.GetAllConstraints() ?? throw new DomainException("CONSTRAINTS_UNAVAILABLE", "The persistent pre-add inventory is unavailable, not observed empty.");
        var baseline = NativeSketchRelations410.Capture(manager, before.Inputs, cancel, inventory);
        Need(baseline.Complete && ModelFeatureReorderState345.Equivalent(baseline.Relations, before.Relations, cancel),
            "PRECONDITION_FAILED", "The exact complete persistent relation baseline changed before AddConstraint.");
        return baseline;
    }
    internal static Result Execute(ModifyArguments args, DocumentState state, WriteJob job, ReadContext context, JsonLog log)
    {
        NativeSketchConstraintState385.Snapshot? before = null; EntityRef? current = null; McUndoPoint? tracker = null; bool started = false, editing = false, persistentAdd = false, nativeVariableSetterReportedFalse = false; string stage = "preparation"; var added = new List<McObjectId>(); var submittedRelations = new List<McObjectId>(); NativeSketchConstraintState385.Control? selected = null; NativeSketchRelations410.Baseline? writeBaseline = null;
        NativeSketchCorrespondence410.Markers? nativeMarkers = null; NativeSketchCorrespondence410.Mapping? enteredMapping = null; JsonElement? correspondenceReceipt = null; Multicad.Mc3D.PlanarSketch? activeEditor = null;
        NativeSketchConstraintState385.Snapshot? literalExpectedBefore = null;
        NativeSketchEditorSolve445.Pending? pendingLiteralSolve = null;
        NativeSketchEditorSolve445.Transition? completedLiteralTransition = null;
        void Trace(string value) { stage = value; log.Write(job.Operation, job.OriginalRequestId, "native_sketch385_checkpoint", transaction: value, operationId: job.OperationId); }
        void Exit() { if (!editing) return; var sketch = activeEditor ?? before!.Sketch.Sketch; Need(sketch.ExitEditMode(), "MODEL_UPDATE_FAILED", "Native sketch edit mode did not close."); editing = false; Need(!sketch.IsInEditMode && !before!.Sketch.Sketch.IsInEditMode, "MODEL_UPDATE_FAILED", "Native sketch edit mode remains active."); }
        void Close() { var value = tracker; tracker = null; if (value == null) return; try { value.Stop(); } finally { value.Dispose(); } }
        var source = new EntityRef(args.BridgeId, args.DocumentId, args.Operations[0].Handles.Single());
        Result Failure(Exception error)
        {
            if (job.CommittedResult is { } committed) return committed; Exception? cleanup = null; try { Exit(); } catch (Exception e) { cleanup = e; }
            try { Close(); } catch (Exception e) { cleanup ??= e; }
            bool unknown = started || cleanup != null; string cause = error is DomainException d ? d.Error.code : error is OperationCanceledException ? "CANCELLED" : "SKETCH_CONSTRAINT_WRITE_FAILED";
            var details = ModelRepresentationDiagnostics377.Error(cause, stage, error.Message, before == null ? null : Key(before.Part.Body.ID), selected == null ? null : Key(selected.Constraint), cleanup?.Message, new { body_ref = current, sketch_id = before == null ? null : Key(before.Sketch.Sketch.ID), native_add_route = persistentAdd ? "persistent_auto_outside_editor" : "editor_or_existing_control", added_constraint_ids = added.Select(Key).ToArray(), submitted_constraint_ids = submittedRelations.Select(Key).ToArray(), native_item_correspondence = correspondenceReceipt, verification_details = (error as DomainException)?.Error.details }, source.handle);
            return job.Error(unknown ? "WRITE_OUTCOME_UNKNOWN" : cause, unknown ? "Native sketch solver state is unverified; constraints or geometry changes may remain. No rollback is claimed." : error.Message, unknown ? "unknown" : "not_started", context, details) with { warnings = [WriteRecovery.Warning] };
        }
        Result Outcome(NativeSketchConstraintState385.Snapshot after, SketchConstraintChange[] changes, bool project = false, bool storageChanged = false, object? details = null)
        {
            var body = current ?? new EntityRef(args.BridgeId, args.DocumentId, project ? "FFFFFFFFFFFFFFFF" : after.Part.Representation.Handle.ToString()); bool replaced = project || source.handle != body.handle; var warnings = new List<string> { WriteRecovery.Warning, Warning }; warnings.AddRange(after.Part.Warnings);
            if (nativeVariableSetterReportedFalse) warnings.Add("NATIVE_VARIABLE_SETTER_REPORTED_FALSE: The expression setter returned false; the exact requested literal control, solved geometry and retained rebuilt part were independently verified.");
            if (correspondenceReceipt.HasValue && correspondenceReceipt.Value.EnumerateObject().Any(phase => phase.Value.GetProperty("items").EnumerateArray().Any(item => !NativeSketchConstraintState385.Same(item.GetProperty("original_id").GetString(), item.GetProperty("current_id").GetString()))))
                warnings.Add("NATIVE_SKETCH_MEMBER_IDS_UPDATED: The native editor updated sketch input or relation IDs through observed native bindings; inspect current history for persistent input and relation IDs.");
            if (project) warnings.AddRange([ModelFeatureReorderState345.ValidityWarning, ModelSketchConstraintObservation353.Warning, ModelSketchConstraintMetadata379.Warning]); else if (storageChanged) warnings.Add(ModelSketchConstraintMetadata379.Warning);
            if (selected == null) return Result.Ok(job.OriginalRequestId, new ModifiedEntities(args.OperationId, "committed", false, [body], replaced ? [source] : [], replaced ? [body] : [], [], "not_guaranteed", sketch_constraint_changes: changes), context, warnings.Distinct().ToArray());
            var actualMember = project ? selected.Member : NativeSketchConstraintState385.ControlOf(after, Key(selected.Constraint), job.CheckCancellation).Member;
            var edit = (ModelSketchConstraintParameterSpec379)args.Operations[0]; var change = new ModelSketchConstraintParameterChange379(0, source, body, Key(after.Part.Body.ID), Sketch(args), Key(selected.Constraint), Key(selected.Variable), Key(actualMember), selected.Kind, selected.Value, edit.Value, selected.Kind == "angle" ? "radians" : "millimeters", selected.Expression, edit.Value, null, null, after.Sketch.Profiles.EnumerateArray().Select(p => p.GetProperty("profile_id").GetString()!).ToArray(), Consumers(after), replaced, storageChanged ? "unverified_native_storage_changes" : "exact_observed_storage", project ? Wire.Element(new { reservation = new string('X', 5120) }) : details == null ? null : Wire.Element(details));
            return Result.Ok(job.OriginalRequestId, new ModelSketchConstraintParameterOutput379(args.OperationId, "committed", false, [body], replaced ? [source] : [], replaced ? [body] : [], [], "not_guaranteed", [change]), context, warnings.Distinct().ToArray());
        }
        try
        {
            using (var tx = state.Document.Database.TransactionManager.StartTransaction()) before = Resolve(args, state, tx, job.CheckCancellation);
            SketchConstraintSpec[] specs = args.Operations[0] is SketchConstraintsEdit add ? add.Constraints : [ControlSpec(selected = NativeSketchConstraintState385.ControlOf(before, ((ModelSketchConstraintParameterSpec379)args.Operations[0]).ConstraintId, job.CheckCancellation), ((ModelSketchConstraintParameterSpec379)args.Operations[0]).Value)];
            var reserve = specs.Select(s => new SketchConstraintChange(Sketch(args), "{FFFFFFFF-FFFF-FFFF-FFFF-FFFFFFFFFFFF}", s.Kind, s.First, s.Second, "{FFFFFFFF-FFFF-FFFF-FFFF-FFFFFFFFFFFF}", s.Value, s.Third)).ToArray(); Bytes(Outcome(before, reserve, project: true), job.CheckCancellation);
            if (selected == null)
            {
                writeBaseline = EditorCircleCenterFix(specs, before.Inputs) ? null : PersistentBaseline(before, job.CheckCancellation); persistentAdd = writeBaseline != null;
                if (!persistentAdd) { Trace("native_marker_capture_before"); nativeMarkers = NativeSketchCorrespondence410.Capture(before.Inputs, before.Relations, job.CheckCancellation); }
            }
            // Exact current inputs/relations/owner/part state are checked before
            // tracking. No new relation is guessed from a prior failed ID.
            using (var tx = state.Document.Database.TransactionManager.StartTransaction()) Need(Resolve(args, state, tx, job.CheckCancellation).Token == before.Token, "PRECONDITION_FAILED", "Native sketch state changed before its first setter.");
            job.CheckCancellation(); tracker = new McUndoPoint(false); tracker.Start(); started = true;
            if (selected == null && persistentAdd)
            {
                // No editor fallback after this route starts. Its returned
                // IDs must survive as the actual requested persistent rows.
                Need(writeBaseline != null && PersistentBaseline(before, job.CheckCancellation) != null,
                    "CONSTRAINT_BASELINE_CHANGED", "The persistent pre-write baseline is unavailable.");
                using var manager = NativeSketchMembers385.Manager(before.Sketch.Sketch);
                void RequirePersistentContext()
                {
                    if (before.Sketch.Sketch.IsInEditMode) editing = true;
                    Need(!editing, "CONSTRAINT_SCOPE_MISMATCH", "The persistent relation route unexpectedly entered sketch edit mode.");
                    NativeSketchMembers385.RequireManagerScope(manager, before.Sketch.Sketch, before.Sketch.Sketch.DbEntity.Document);
                    Need(Key(manager.ScopeId) == writeBaseline.Relations.GetProperty("scope_id").GetString() && manager.ConstraintsNetworkType.ToString() == writeBaseline.Relations.GetProperty("network").GetString(),
                        "CONSTRAINT_SCOPE_MISMATCH", "Persistent AddConstraint changed its actual native scope/network.");
                }
                RequirePersistentContext();
                foreach (var spec in specs)
                {
                    job.CheckCancellation(); using var info = NativeSketchMembers385.Info(spec, before.Inputs);
                    Trace("persistent_constraint_add_before"); var id = manager.AddConstraint(info, false);
                    if (!id.IsNull) submittedRelations.Add(id); Need(!id.IsNull, "CONSTRAINT_REJECTED", "The persistent sketch manager returned no relation ID.");
                    RequirePersistentContext();
                }
                Trace("native_solve_before"); Need(before.Sketch.Sketch.ApplyConstraints(true), "CONSTRAINT_SOLVE_FAILED", "The persistent native sketch refused its grouped solve.");
            }
            else if (selected == null)
            {
                // SDK examples add in the editor's MCS network. Native markers
                // witness its real item-ID correspondence without shape aliases.
                Need(nativeMarkers != null, "SKETCH_CORRESPONDENCE_UNAVAILABLE", "Pre-entry native marker receipts are unavailable.");
                editing = true; Trace("native_editor_entry_before"); Need(before.Sketch.Sketch.EnterEditMode(), "SKETCH_EDIT_UNAVAILABLE", "Native sketch edit mode is unavailable.");
                var sheet = McDocumentsManager.GetActiveSheet(); Need(sheet != null && !sheet.ID.IsNull, "CONSTRAINT_SCOPE_MISMATCH", "The active sketch edit sheet is unavailable.");
                // The service's no-document overload observes the actual active
                // editor; the selected permanent ID/owner and current sheet are
                // proved separately, without guessing which document hosts it.
                var activeId = Multicad.Mc3D.Service.GetActiveEditSketchID(); activeEditor = Multicad.Mc3D.Service.GetActiveEditSketch();
                if (!(activeEditor != null && activeId == before.Sketch.Sketch.ID && activeEditor.ID == activeId && activeEditor.IsInEditMode && activeEditor.DbEntity.Document?.ID == before.Sketch.Sketch.DbEntity.Document.ID))
                    throw new DomainException("CONSTRAINT_SCOPE_MISMATCH", "The SDK active editable sketch/ID does not identify the selected native sketch and its permanent owner.", false, new
                    {
                        stage = "active_editable_sketch_observation",
                        selected_sketch_id = Key(before.Sketch.Sketch.ID),
                        selected_owner_document_id = Key(before.Sketch.Sketch.DbEntity.Document.ID),
                        edit_sheet_id = Key(sheet.ID),
                        actual_active_sketch_id = activeId.IsNull ? null : Key(activeId),
                        actual_editable_wrapper_id = activeEditor == null ? null : Key(activeEditor.ID),
                        actual_editable_owner_document_id = activeEditor?.DbEntity.Document?.ID.ToString(),
                        actual_edit_mode = activeEditor?.IsInEditMode
                    });
                using var manager = NativeSketchMembers385.Manager(activeEditor, allowUnlinkedEditScope: true);
                var inventory = manager.GetAllConstraints() ?? throw new DomainException("CONSTRAINTS_UNAVAILABLE", "The actual editor sketch baseline is unavailable, not observed empty.");
                NativeSketchMembers385.Snapshot inputs; using (var tx = state.Document.Database.TransactionManager.StartTransaction()) inputs = NativeSketchMembers385.Capture(activeEditor, state, tx, job.CheckCancellation, manager, inventory);
                var editorRelations = NativeSketchConstraintState385.Relations(manager, job.CheckCancellation, inventory: inventory);
                enteredMapping = NativeSketchCorrespondence410.Resolve(nativeMarkers, inputs, editorRelations, job.CheckCancellation, "editor_before_add", preserveGeometry: true, observedManager: manager); correspondenceReceipt = Wire.Element(new { entered = enteredMapping.Receipt });
                writeBaseline = NativeSketchRelations410.Capture(manager, inputs, job.CheckCancellation, inventory);
                var mappedBeforeInputs = NativeSketchCorrespondence410.Inputs(before.Inputs, enteredMapping, job.CheckCancellation);
                var mappedBeforeRelations = NativeSketchCorrespondence410.Relations(before.Relations, enteredMapping, job.CheckCancellation);
                var priorIds = NativeSketchRelations410.Retained(NativeSketchRelations410.Capture(mappedBeforeRelations, mappedBeforeInputs, job.CheckCancellation), writeBaseline.Relations, inputs, job.CheckCancellation, allowNetworkTransfer: true).ToHashSet(StringComparer.OrdinalIgnoreCase);
                foreach (var row in writeBaseline.Relations.GetProperty("constraints").EnumerateArray().Where(row => !priorIds.Contains(row.GetProperty("id").GetString()!)))
                    Need(row.GetProperty("type").GetString() == "GeomFix" && row.GetProperty("first").ValueKind == JsonValueKind.Object && inputs.Construction.Any(point => Key(point.Id) == row.GetProperty("first").GetProperty("object_id").GetString()), "CONSTRAINT_BASELINE_CHANGED", "Editor entry created a relation outside its witnessed native construction point.");
                foreach (var original in specs)
                {
                    var spec = NativeSketchCorrespondence410.Spec(original, enteredMapping);
                    job.CheckCancellation(); using var info = NativeSketchMembers385.Info(spec, inputs); Trace("constraint_add_before"); var id = manager.AddConstraint(info, false);
                    if (!id.IsNull) submittedRelations.Add(id); Need(!id.IsNull, "CONSTRAINT_REJECTED", "The native editor sketch manager returned no relation ID.");
                    Need(Multicad.Mc3D.Service.GetActiveEditSketchID() == activeEditor.ID, "CONSTRAINT_SCOPE_MISMATCH", "The active editable sketch changed during its relation batch.");
                    NativeSketchMembers385.RequireEditBatchContext(manager, activeEditor, sheet);
                }
                Trace("native_solve_before"); Need(manager.ApplyAllConstraints(), "CONSTRAINT_SOLVE_FAILED", "The native editor sketch manager refused its grouped solve."); Exit();
            }
            else
            {
                double requested = ((ModelSketchConstraintParameterSpec379)args.Operations[0]).Value;
                using (var manager = NativeSketchMembers385.Manager(before.Sketch.Sketch))
                {
                    using var info = manager.GetConstraintInfo(selected.Constraint); var parameter = manager.GetConstraintParamId(selected.Constraint);
                    Need(info != null && info.IsDimensional && info.Type == NativeSketchMembers385.Type(selected.Kind) && info.VarId == selected.Variable && (parameter.IsNull || parameter == selected.Variable) && ModelParameterSnapshot.Near(info.Param, selected.RelationValue) && info.VarExpression == selected.RelationExpression, "PRECONDITION_FAILED", "The actual persistent dimensional relation/literal binding changed before its variable setter.");
                }
                var owner = before.Part.Body.DbEntity.Document; using (var variables = owner.GetDocumentVariablesManager(Constraint2dNetType.MCS))
                {
                    Need(variables != null && variables.DocumentId == owner.ID && variables.ConstraintsNetworkType == Constraint2dNetType.MCS, "CONSTRAINT_SCOPE_MISMATCH", "The exact owning MCS variable manager is unavailable before the literal setter.");
                    var inventory = variables.GetAllVariables(); Need(inventory != null && inventory.Count(id => id == selected.Variable) == 1, "CONSTRAINT_PARAMETER_UNAVAILABLE", "The exact selected variable is not unique in its owner MCS inventory.");
                    var observed = variables.GetVariableInfo(selected.Variable); var host = variables.GetVariableHostObjectId(selected.Variable);
                    Need(observed != null && observed.Name == selected.VariableState.GetProperty("name").GetString() && observed.Type.ToString() == selected.VariableState.GetProperty("type").GetString() && observed.Expression == selected.Expression && variables.GetVariableValue(selected.Variable, out double priorValue) && ModelParameterSnapshot.Near(priorValue, selected.Value) && NativeSketchConstraintState385.Same(host.IsNull ? null : Key(host), selected.VariableState.GetProperty("host_object_id").GetString()), "PRECONDITION_FAILED", "The exact owning variable metadata/literal/value/host changed before its setter.");
                    // Follow the SDK and existing model numeric writers: mutate
                    // the actual MCS variable literal, then resolve the sketch.
                    // No second constraint-value setter or failed-write retry.
                    Trace("control_literal_set_before"); nativeVariableSetterReportedFalse = !variables.SetVariableExpression(selected.Variable, requested.ToString("R", System.Globalization.CultureInfo.InvariantCulture));
                    Trace("control_literal_update_before"); McObjectManager.UpdateAll();
                    observed = variables.GetVariableInfo(selected.Variable);
                    bool Literal(string? expression) => double.TryParse(expression, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double number) && ModelParameterSnapshot.Near(number, requested);
                    Need(observed != null && observed.Name == selected.VariableState.GetProperty("name").GetString() && observed.Type.ToString() == selected.VariableState.GetProperty("type").GetString() && Literal(observed.Expression) && variables.GetVariableValue(selected.Variable, out double value) && ModelParameterSnapshot.Near(value, requested), "CONSTRAINT_PARAMETER_REJECTED", "The variable-expression setter did not establish the requested numeric literal on the exact existing MCS variable.");
                }
                Trace("native_solve_before");
                if (NativeSketchEditorSolve445.HasOwnedSpline(before.Sketch.Sketch, job.CheckCancellation))
                {
                    // The editor solve is a pending native transition until
                    // command-end synchronization. Keep original literal and
                    // point intent; complete its actual correspondence below.
                    pendingLiteralSolve = NativeSketchEditorSolve445.Solve(before.Sketch.Sketch, state, before, job.CheckCancellation);
                    correspondenceReceipt = pendingLiteralSolve.Receipt;
                }
                else Need(before.Sketch.Sketch.ApplyConstraints(true), "CONSTRAINT_SOLVE_FAILED", "Native explicit sketch solve failed.");
            }
            if (pendingLiteralSolve == null)
            { before.Sketch.Sketch.DbEntity.Update(); McObjectManager.UpdateAll(); Trace("native_rebuild_before"); Need(before.Part.Body.RebuildPart(), "MODEL_UPDATE_FAILED", "Native retained part rebuild failed."); McObjectManager.UpdateAll(); Trace("native_rebuild_returned"); }
            (NativeSketchConstraintState385.Snapshot State, SketchConstraintChange[] Changes, bool Storage, object? Details) Verify()
            {
                var comparisonBefore = literalExpectedBefore ?? before!;
                NativeSketchConstraintState385.Snapshot after; using (var tx = state.Document.Database.TransactionManager.StartTransaction())
                { var raw = ModelHistoryEditGuard.CurrentRepresentation(comparisonBefore.Part.Body, state, tx, job.CheckCancellation); current = new(args.BridgeId, args.DocumentId, Validation.NormalizeHandle(raw.Handle.ToString())); after = NativeSketchConstraintState385.Capture(current.handle, Sketch(args), args, state, tx, job.CheckCancellation); }
                if (completedLiteralTransition is { } completed)
                {
                    comparisonBefore = NativeSketchSolveExpected445.CanonicalComparisons(comparisonBefore, [completed], job.CheckCancellation);
                    after = NativeSketchSolveExpected445.CanonicalComparisons(after, [completed], job.CheckCancellation);
                }
                NativeSketchCorrespondence410.Mapping? persistentMapping = null;
                var expectedBefore = comparisonBefore; var currentSpecs = specs;
                if (selected == null && !persistentAdd)
                {
                    Need(nativeMarkers != null && enteredMapping != null, "SKETCH_CORRESPONDENCE_UNAVAILABLE", "Native editor identity receipts are unavailable.");
                    persistentMapping = NativeSketchCorrespondence410.Resolve(nativeMarkers, after.Inputs, after.Relations, job.CheckCancellation, "persistent_after_solve", preserveGeometry: false);
                    correspondenceReceipt = Wire.Element(new { entered = enteredMapping.Receipt, persistent = persistentMapping.Receipt });
                    expectedBefore = before with { Inputs = NativeSketchCorrespondence410.Inputs(comparisonBefore.Inputs, persistentMapping, job.CheckCancellation), Relations = NativeSketchCorrespondence410.Relations(comparisonBefore.Relations, persistentMapping, job.CheckCancellation) };
                    currentSpecs = specs.Select(spec => NativeSketchCorrespondence410.Spec(spec, persistentMapping)).ToArray();
                }
                var priorRelations = selected == null ? expectedBefore.Relations : NativeSketchRelations410.Without(comparisonBefore.Relations, new HashSet<string>(StringComparer.OrdinalIgnoreCase) { Key(selected.Constraint) });
                var retainedBaseline = NativeSketchRelations410.Capture(priorRelations, expectedBefore.Inputs, job.CheckCancellation); NativeSketchRelations410.Retained(retainedBaseline, after.Relations, after.Inputs, job.CheckCancellation);
                var baselineIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase); var systemIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase); var systemNative = new HashSet<McObjectId>();
                if (selected == null)
                {
                    Need(writeBaseline != null, "CONSTRAINT_BASELINE_CHANGED", "The actual pre-write relation baseline is unavailable.");
                    if (persistentAdd) baselineIds.UnionWith(NativeSketchRelations410.Retained(writeBaseline, after.Relations, after.Inputs, job.CheckCancellation));
                    else
                    {
                        Need(enteredMapping != null && persistentMapping != null, "CONSTRAINT_BASELINE_CHANGED", "The editor correspondence receipt is unavailable.");
                        var transition = NativeSketchCorrespondence410.Transition(enteredMapping, persistentMapping);
                        var mappedBaseline = writeBaseline with { Relations = NativeSketchCorrespondence410.Relations(writeBaseline.Relations, transition, job.CheckCancellation), NativeIds = writeBaseline.NativeIds.Select(transition.Map).ToArray(), Inputs = writeBaseline.Inputs == null ? null : NativeSketchCorrespondence410.Inputs(writeBaseline.Inputs, transition, job.CheckCancellation) };
                        baselineIds.UnionWith(NativeSketchRelations410.Retained(mappedBaseline, after.Relations, after.Inputs, job.CheckCancellation, allowNetworkTransfer: true));
                        systemIds.UnionWith(baselineIds.Except(expectedBefore.Relations.GetProperty("constraints").EnumerateArray().Select(row => row.GetProperty("id").GetString()!), StringComparer.OrdinalIgnoreCase));
                    }
                    using var persistent = NativeSketchMembers385.Manager(after.Sketch.Sketch); added.Clear(); added.AddRange(NativeSketchRelations410.RequestedPersistent(persistent, currentSpecs, after.Inputs, baselineIds, job.CheckCancellation));
                    if (persistentAdd) Need(added.SequenceEqual(submittedRelations), "CONSTRAINT_PERSISTENCE_UNAVAILABLE", "The persistent AddConstraint result IDs do not match the exact requested actual relations.");
                    else foreach (var construction in after.Inputs.Construction.Where(c => !expectedBefore.Inputs.Construction.Any(prior => prior.Id == c.Id))) systemNative.Add(construction.Id);
                    foreach (var id in persistent.GetAllConstraints() ?? throw new DomainException("CONSTRAINTS_UNAVAILABLE", "Persistent network inventory became unavailable.")) if (systemIds.Contains(Key(id)))
                    { systemNative.Add(id); using var info = persistent.GetConstraintInfo(id); Need(info != null && info.Type == ConstraintType.GeomFix, "CONSTRAINT_BASELINE_CHANGED", "A newly materialized baseline relation is not a native fixed construction anchor."); if (!info.DimId.IsNull) systemNative.Add(info.DimId); if (!info.VarId.IsNull) systemNative.Add(info.VarId); }
                }
                var oldVariables = comparisonBefore.Part.Preserved.GetProperty("variables").EnumerateArray().Select(v => v.GetProperty("mc_id").GetString()!).ToHashSet(StringComparer.OrdinalIgnoreCase); var newVariables = after.Part.Preserved.GetProperty("variables").EnumerateArray().Where(v => !oldVariables.Contains(v.GetProperty("mc_id").GetString()!)).ToArray(); var addedIds = added.Select(Key).ToArray(); var changes = new List<SketchConstraintChange>();
                var dimensions = new List<string>(); var actualCreated = new HashSet<McObjectId>(added); string actualScope; using (var manager = NativeSketchMembers385.Manager(after.Sketch.Sketch))
                {
                    actualScope = Key(manager.ScopeId);
                    for (int i = 0; i < currentSpecs.Length; i++)
                    {
                        var spec = currentSpecs[i]; var id = selected == null ? added[i] : selected.Constraint; using var info = manager.GetConstraintInfo(id); using var expected = NativeSketchMembers385.Info(spec, after.Inputs);
                        Need(info != null && NativeSketchMembers385.RequestedReferences(info, expected, spec, after.Inputs), "CONSTRAINT_SCOPE_MISMATCH", "Native relation type/object/segment binding differs from the requested current inputs."); NativeSketchRelations410.RequirePointReceipt(spec, info, after.Inputs);
                        if (spec.Value.HasValue)
                        {
                            var parameter = manager.GetConstraintParamId(id); double value = manager.GetConstraintParamValue(id);
                            Need(info.IsDimensional && !info.VarId.IsNull && (selected == null ? ModelParameterSnapshot.Near(info.Param, spec.Value.Value) && parameter == info.VarId && ModelParameterSnapshot.Near(value, spec.Value.Value) : info.VarId == selected.Variable && (parameter.IsNull || parameter == info.VarId) && (parameter.IsNull || !double.IsFinite(value) || ModelParameterSnapshot.Near(value, spec.Value.Value))), "CONSTRAINT_PARAMETER_UNAVAILABLE", "The persistent native parametric relation/control does not match the requested value.");
                        }
                        if (!info.DimId.IsNull) { dimensions.Add(Key(info.DimId)); actualCreated.Add(info.DimId); }
                        if (!info.VarId.IsNull) actualCreated.Add(info.VarId); changes.Add(new(Sketch(args), Key(id), spec.Kind, spec.First, spec.Second, info.VarId.IsNull ? null : Key(info.VarId), spec.Value, spec.Third));
                    }
                }
                var driverHosts = new[] { Key(comparisonBefore.Sketch.Sketch.ID), actualScope }.Concat(addedIds).Concat(dimensions).Concat(after.Inputs.Members.Select(m => Key(m.ObjectId))).ToHashSet(StringComparer.OrdinalIgnoreCase);
                var returnedVariables = changes.Where(c => c.variable_id != null).Select(c => c.variable_id!).ToHashSet(StringComparer.OrdinalIgnoreCase);
                Need(newVariables.All(row => returnedVariables.Contains(row.GetProperty("mc_id").GetString()!) && row.GetProperty("host_object_id").ValueKind == JsonValueKind.String && driverHosts.Contains(row.GetProperty("host_object_id").GetString()!)), "DEPENDENCY_OWNERSHIP_MISMATCH", "A new variable has no actual returned dimensional binding or requested native host.");
                if (selected != null)
                {
                    var control = NativeSketchConstraintState385.ControlOf(after, Key(selected.Constraint), job.CheckCancellation); double requested = ((ModelSketchConstraintParameterSpec379)args.Operations[0]).Value;
                    Need(newVariables.Length == 0 && control.Variable == selected.Variable && ModelParameterSnapshot.Near(control.Value, requested) && (!control.ParameterValue.HasValue || ModelParameterSnapshot.Near(control.ParameterValue.Value, requested)), "CONSTRAINT_SCOPE_MISMATCH", "Existing control edit created/rebound a driver or did not reconcile the requested literal/bound evaluated value.");
                }
                NativeSketchMembers385.ValidateSolved(currentSpecs, after.Inputs); NativeSketchMembers385.FixedRequested(currentSpecs, expectedBefore.Inputs, after.Inputs); NativeSketchConstraintState385.FixedAnchors(expectedBefore, after, job.CheckCancellation);
                var oldDesign = NativeSketchConstraintState385.Protected(comparisonBefore.Part, comparisonBefore.Sketch, comparisonBefore.Inputs, [], selected == null ? null : Key(selected.Variable), job.CheckCancellation, affected: comparisonBefore.Affected);
                var expectedContents = comparisonBefore.Part.Contents.Select(id => persistentMapping?.Map(id) ?? id).ToHashSet();
                var expectedAffected = comparisonBefore.Affected.Select(id => persistentMapping?.Map(id) ?? id).ToHashSet(StringComparer.OrdinalIgnoreCase);
                if (persistentMapping != null) oldDesign = NativeSketchCorrespondence410.Identities(oldDesign, persistentMapping, job.CheckCancellation);
                var newObjects = selected == null ? addedIds.Concat(dimensions).Concat(newVariables.Select(v => v.GetProperty("mc_id").GetString()!)).Concat(systemNative.Select(Key)).Where(id => !expectedContents.Any(old => NativeSketchConstraintState385.Same(Key(old), id))).ToHashSet(StringComparer.OrdinalIgnoreCase) : new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                // Freeze the native graph before the setter. A rebuild may
                // update downstream geometry, but cannot silently admit a new
                // consumer or substitute the edited graph roots.
                var afterClosure = ModelDependencyGraph410.OwnedClosure(after.Part.Body, comparisonBefore.GraphRoots, job.CheckCancellation, after.Part.Views);
                afterClosure.ExceptWith(newObjects);
                Need(comparisonBefore.GraphRoots.ToHashSet().SetEquals(after.GraphRoots) && expectedAffected.SetEquals(afterClosure), "MODEL_DEPENDENCY_CHANGED", "Native sketch rebuild changed the observed same-part dependency closure.");
                var newDesign = NativeSketchConstraintState385.Protected(after.Part, after.Sketch, after.Inputs, newVariables.Select(v => v.GetProperty("mc_id").GetString()!).ToArray(), selected == null ? null : Key(selected.Variable), job.CheckCancellation, newObjects.ToArray(), expectedAffected);
                if (persistentMapping != null) newDesign = NativeSketchCorrespondence410.Identities(newDesign, new NativeSketchCorrespondence410.Mapping(new Dictionary<McObjectId, McObjectId>(), Wire.Element(new { })), job.CheckCancellation);
                var actualBindings = after.Storage;
                if (selected == null) using (var tx = state.Document.Database.TransactionManager.StartTransaction())
                { var owner = ModelProfileDiscovery348.OwnerDocument(after.Part.Body, state); var drawingOwner = ModelProfileDiscovery348.DrawingDocument(state); actualBindings = actualBindings.Concat(ModelSketchConstraintMetadata379.Bind(actualCreated, after.Part.Representation, owner, drawingOwner, state, tx, job.CheckCancellation)).DistinctBy(b => b.native_id, StringComparer.OrdinalIgnoreCase).ToArray(); }
                var expectedBindings = persistentMapping == null ? comparisonBefore.Storage : comparisonBefore.Storage.Select(binding => binding with { native_id = persistentMapping.Map(binding.native_id) }).ToArray();
                var storage = ModelSketchConstraintMetadata379.Compare(oldDesign, newDesign, comparisonBefore.Part.BodyMetadata, after.Part.BodyMetadata, expectedBindings, actualBindings, job.CheckCancellation);
                bool intent = ModelFeatureReorderState345.Equivalent(NativeSketchConstraintState385.Intent(expectedBefore.Relations, selected == null ? null : Key(selected.Constraint), inputs: expectedBefore.Inputs), NativeSketchConstraintState385.Intent(after.Relations, selected == null ? null : Key(selected.Constraint), addedIds.Concat(systemIds).ToArray(), after.Inputs), job.CheckCancellation);
                bool design = storage.retained && ModelFeatureReorderState345.Equivalent(oldDesign, storage.design, job.CheckCancellation) && ModelFeatureReorderState345.Equivalent(comparisonBefore.Part.BodyMetadata, storage.body, job.CheckCancellation);
                bool identities = comparisonBefore.Part.Body.ID == after.Part.Body.ID && comparisonBefore.Part.NativeDocument == after.Part.NativeDocument && expectedContents.All(after.Part.Contents.Contains) && after.Part.Contents.Except(expectedContents).All(id => newObjects.Contains(Key(id))) && expectedBefore.Inputs.Members.Select(m => m.Id).ToHashSet().SetEquals(after.Inputs.Members.Select(m => m.Id));
                bool order = comparisonBefore.Part.Features.SequenceEqual(after.Part.Features) && after.Part.Previous.Keys.Except(comparisonBefore.Part.Previous.Keys, StringComparer.OrdinalIgnoreCase).All(newObjects.Contains) && comparisonBefore.Part.Previous.All(p => after.Part.Previous.TryGetValue(p.Key, out var v) && p.Value == v);
                bool IsCreatedRaw(Db.ObjectId raw)
                {
                    if (selected != null) return false; using var tx = state.Document.Database.TransactionManager.StartTransaction(); var drawingOwner = ModelProfileDiscovery348.DrawingDocument(state); var owner = ModelProfileDiscovery348.OwnerDocument(after.Part.Body, state);
                    return new[] { drawingOwner, owner }.Any(doc => newObjects.Contains(Key(McObjectId.FromHandle(raw.Handle.Value, doc.ID))));
                }
                bool drawing = comparisonBefore.Part.Inventory.Except(after.Part.Inventory).All(id => id == comparisonBefore.Part.Representation) && after.Part.Inventory.Except(comparisonBefore.Part.Inventory).All(id => id == after.Part.Representation || IsCreatedRaw(id));
                bool display = comparisonBefore.Part.Properties.GetRawText() == after.Part.Properties.GetRawText();
                bool peers = comparisonBefore.Part.Constraints.Keys.ToHashSet(StringComparer.OrdinalIgnoreCase).SetEquals(after.Part.Constraints.Keys);
                foreach (var pair in comparisonBefore.Part.Constraints)
                {
                    if (NativeSketchConstraintState385.Same(pair.Key, Sketch(args))) continue;
                    if (!after.Part.Constraints.TryGetValue(pair.Key, out var observed)) { peers = false; continue; }
                    var prior = persistentMapping == null ? pair.Value : NativeSketchCorrespondence410.Identities(pair.Value, persistentMapping, job.CheckCancellation);
                    if (comparisonBefore.Affected.Contains(pair.Key))
                    { prior = ModelDependencyGraph410.ConstraintPoints(prior, job.CheckCancellation); observed = ModelDependencyGraph410.ConstraintPoints(observed, job.CheckCancellation); }
                    peers &= ModelSketchConstraintObservation353.Equivalent(prior, observed, job.CheckCancellation);
                }
                object Diagnostic()
                {
                    var identity = new ModelRepresentationDiagnostics377.Identity(args.Operations[0].Operation, Key(comparisonBefore.Part.Body.ID), comparisonBefore.Part.Representation.Handle.ToString(), after.Part.Representation.Handle.ToString(), comparisonBefore.Part.NativeDocument, after.Part.NativeDocument, comparisonBefore.Part.Preserved.GetProperty("variables_owner").GetString(), after.Part.Preserved.GetProperty("variables_owner").GetString(), null, null, comparisonBefore.Part.BodyMetadata.GetProperty("owned").GetArrayLength(), after.Part.BodyMetadata.GetProperty("owned").GetArrayLength());
                    return ModelRepresentationDiagnostics377.Describe(identity, [new("retained_design", design, "intended internal motion; exact topology/ID/source/formula/foreign data", oldDesign, newDesign), new("body_metadata", storage.retained, "prior user data/foreign links; exact typed native storage warning boundary", comparisonBefore.Part.BodyMetadata, after.Part.BodyMetadata), new("native_inputs", identities, "same native object/member/segment IDs; intended numeric geometry motion", comparisonBefore.Inputs.Stored, after.Inputs.Stored), new("relation_intent", intent && peers, "selected control/new actual relations only; all old member/peer/fixed intent retained", comparisonBefore.Relations, after.Relations), new("body_display", display, "exact", comparisonBefore.Part.Properties, after.Part.Properties), new("forming_order", order, "all old forming order/assigned predecessors exact", Wire.Element(comparisonBefore.Part.Features), Wire.Element(after.Part.Features)), new("drawing_inventory", drawing, "actual body replacement or returned native relation/dimension only", null, null), new("selected_storage_bindings", storage.retained, storage.reason, Wire.Element(comparisonBefore.Storage), Wire.Element(actualBindings))], job.CheckCancellation);
                }
                // Bounded snapshots are emitted by the shared error adapter;
                // existing user/source data and links are never masked wholesale.
                if (!identities || !order || !drawing || !display || !intent || !peers || !design) throw new DomainException("MODEL_DEPENDENCY_CHANGED", "Native solve changed a retained identity/order/source/formula/fixed intent, meaningful metadata or foreign link.", false, Diagnostic());
                return (after, changes.ToArray(), storage.changed, storage.changed ? Diagnostic() : null);
            }
            if (pendingLiteralSolve != null)
            {
                // Mark automatic followers while native tracking is active;
                // final observation after command completion must be read-only.
                McObjectManager.UpdateAll();
                ModelViewFollowers.PrepareStandaloneRefresh(before.Part.Body, before.Part.Views, job.CheckCancellation);
                NativeSketchEditorSolve445.PreserveTransparency(pendingLiteralSolve, state, job.CheckCancellation);
                job.CheckCancellation(); job.BeginCommit(); Close();
                return NativePostCommandVerification422.Defer(job, () =>
                {
                    Trace("native_post_command_completion_before");
                    var transition = NativeSketchEditorSolve445.Complete(pendingLiteralSolve, state, job.CheckCancellation);
                    completedLiteralTransition = transition;
                    correspondenceReceipt = Wire.Element(new { entered = transition.Receipt.GetProperty("entered"), applied = transition.Receipt.GetProperty("applied"), persistent = transition.Receipt.GetProperty("persistent") });
                    Need(selected != null, "CONSTRAINT_PARAMETER_UNAVAILABLE", "The original selected literal control is unavailable for final native correspondence.");
                    selected = NativeSketchSolveExpected445.Literal(selected, [transition], job.CheckCancellation);
                    literalExpectedBefore = selected.State;
                    specs = specs.Select(spec => NativeSketchCorrespondence410.Spec(spec, transition.Mapping)).ToArray();
                    var finalized = Verify(); ModelViewFollowers.Verify(finalized.State.Part.Body, before.Part.Views, job.CheckCancellation, refreshStandalone: false);
                    job.CheckCancellation(); var finalResult = Outcome(finalized.State, finalized.Changes, storageChanged: finalized.Storage, details: finalized.Details); Bytes(finalResult, job.CheckCancellation); job.RecordCommit(finalResult); Trace("commit_recorded"); return finalResult;
                }, Failure);
            }
            var verified = Verify(); ModelViewFollowers.Verify(verified.State.Part.Body, before.Part.Views, job.CheckCancellation, refreshStandalone: true); job.CheckCancellation(); job.BeginCommit(); Close(); verified = Verify(); var result = Outcome(verified.State, verified.Changes, storageChanged: verified.Storage, details: verified.Details); Bytes(result, job.CheckCancellation); job.RecordCommit(result); Trace("commit_recorded"); return result;
        }
        catch (Exception error) { return Failure(error); }
    }
}
