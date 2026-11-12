using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using Multicad;
using Multicad.DatabaseServices;
using Multicad.Mc3D;
using NanoCAD.Contracts;
using NanoCAD.Ipc;
using Db = Teigha.DatabaseServices;

namespace NanoCADBridge;

// Additive exact-WorkPlane branch of set_model_sketch_support. The accepted
// GCS offset creation/edit branch continues through its established writer.
internal static class ModelSketchSupportWriter411
{
    sealed record Binding(ModelManufacturingEdit Edit, Mc3dSolid Body, PlanarSketch Sketch, McDocument Owner,
        ModelSketchSupportSpec396 Request, ModelSketchSupportState396.Binding Support,
        ModelDatumPlaneState353.Snapshot? Previous, ModelDatumDependents411.Snapshot Before, string Token);
    static void Need([System.Diagnostics.CodeAnalysis.DoesNotReturnIf(false)] bool ok, string code, string message)
        => ModelDatumPlaneState353.Need(ok, code, message);
    static string Key(McObjectId id) => id.ToString();
    internal static bool Handles(ModifyArguments args) => args.Operations.Any(edit => edit is ModelManufacturingEdit { Kind: "set_model_sketch_support" } m &&
        m.Definition.TryGetProperty("kind", out var kind) && kind.GetString() == "workplane");
    static string Token(ModelDatumDependents411.Snapshot part, ModelSketchSupportState396.Binding support)
        => Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(new
        {
            part.Part.Preserved,
            part.Part.Properties,
            part.Part.BodyMetadata,
            support = support.Stored,
            inputs = part.Inputs.OrderBy(pair => pair.Key, StringComparer.OrdinalIgnoreCase).Select(pair => new { id = pair.Key, state = pair.Value.Stored }).ToArray(),
            part.Storage
        }, Wire.Json)));
    static Binding Resolve(ModifyArguments args, DocumentState state, Db.Transaction tx, Action cancel)
    {
        Need(args.Operations.Length == 1 && args.Operations[0] is ModelManufacturingEdit, "INVALID_OPERATION_ORDER", "Bind one retained sketch to its exact existing WorkPlane per request.");
        Need(args.Units == "Millimeters" && args.TargetSpace == "model" && (int)state.Document.Database.Insunits == 4, "OUT_OF_SCOPE", "Use Model Space and verified Millimeters.");
        var edit = (ModelManufacturingEdit)args.Operations[0]; var body = ModelDatumPlaneRevisionReader354.Body(edit.Handles.Single(), state, tx, cancel, true);
        Need(Key(body.LastPartItemID).Equals(edit.ExpectedLastItemId, StringComparison.OrdinalIgnoreCase), "PRECONDITION_FAILED", "Refresh the selected part's current history terminal.");
        var owner = ModelProfileDiscovery348.OwnerDocument(body, state); var sketch = ModelSketchLifecycleReader362.Resolve(edit.FeatureId!, owner, cancel);
        DrawingViewSketchWriteScope282.RequireSupported(sketch);
        var contents = ModelInventoryBudget.History(body, cancel);
        Need(sketch.HostSolidID == body.ID && contents.Contains(sketch.ID) && !sketch.IsInEditMode && sketch.Status.ToString() == "Ok" && !sketch.IsSuppressed && !sketch.IsBeyondEndOfPart,
            "SKETCH_OWNERSHIP_MISMATCH", "Choose a healthy active sketch of this actual part; another part's sketch is not rebound.");
        var request = ModelSketchSupportValidation396.Parse(edit.Definition, "definition", allowRecovery: true);
        Need(request.Kind == "workplane", "INVALID_ARGUMENT", "This adapter binds an existing exact WorkPlane.");
        var representation = ModelHistoryEditGuard.CurrentRepresentation(body, state, tx, cancel);
        var support = ModelSketchSupportState396.Capture(request, body, representation, owner, state, tx, args.BridgeId, cancel);
        Need(sketch.PlanarEntityID != support.Support, "NO_CHANGE", "The sketch already retains this exact WorkPlane support.");
        var nodes = support.Stored.GetProperty("nodes").EnumerateArray().SelectMany(row => row.GetProperty("state").TryGetProperty("source_feature_id", out var feature) ? new[] { row.GetProperty("id").GetString()!, feature.GetString()! } : new[] { row.GetProperty("id").GetString()! }).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var before = ModelDatumDependents411.Capture(body, representation, args, state, tx, [sketch.ID], cancel);
        Need(!nodes.Overlaps(before.Affected), "CYCLIC_DEPENDENCY", "The requested WorkPlane depends on this sketch or one of its current native consumers.");
        ModelDatumPlaneState353.Snapshot? previous = null;
        if (!sketch.PlanarEntityID.IsNull && sketch.PlanarEntityID.GetObjectOfType<WorkPlane>() is { } plane)
            previous = ModelDatumPlaneState353.Capture(plane, body, state, tx, cancel);
        return new(edit, body, sketch, owner, request, support, previous, before, Token(before, support));
    }
    internal static void Preflight(ModifyArguments args, DocumentState state, Db.Transaction tx, Action? cancel = null)
    {
        if (ModelSketchSupportRecovery440.Handles(args)) { ModelSketchSupportRecovery440.Preflight(args, state, tx, cancel); return; }
        Resolve(args, state, tx, cancel ?? (() => { }));
    }
    static Result Outcome(Binding binding, ModelDatumDependents411.Snapshot after, ModelSketchSupportState396.Binding support,
        ModifyArguments args, WriteJob job, ReadContext context, IEnumerable<string>? verification = null)
    {
        var source = new EntityRef(args.BridgeId, args.DocumentId, binding.Edit.Handles.Single());
        var body = new EntityRef(args.BridgeId, args.DocumentId, Validation.NormalizeHandle(after.Part.Representation.Handle.ToString())); bool replaced = source.handle != body.handle;
        var parameters = JsonNode.Parse(Wire.Element(ModelSketchSupport.Read(binding.Sketch)).GetRawText())!.AsObject();
        parameters["native_support"] = JsonSerializer.SerializeToNode(ModelSketchSupportState396.Describe(support, binding.Body, binding.Owner, ModelSketchSupportState396.Frame(binding.Sketch), binding.Sketch), Wire.Json);
        var change = new ModelManufacturingChange(binding.Edit.Kind, body, Key(binding.Body.ID), Key(binding.Sketch.ID), Wire.Element(parameters), support.Datum?.Read);
        var result = Result.Ok(job.OriginalRequestId, new ModifiedEntities(args.OperationId, "committed", false, [body], replaced ? [source] : [], replaced ? [body] : [], [], "not_guaranteed", manufacturing_changes: [change]), context,
            binding.Before.Part.Warnings.Concat(after.Part.Warnings).Concat(verification ?? []).Append(WriteRecovery.Warning)
            .Append("NATIVE_SKETCH_SUPPORT_CHANGED: The same sketch/profile/features now retain the exact existing WorkPlane. Actual native hosts, source links, local owned geometry and retained controls were checked after rebuild. Reacquire the returned part/support state and review the intended shape and drawings through MCP; no coordinate recreation or rollback is claimed.").Distinct(StringComparer.Ordinal).ToArray());
        return ModelViewFollowers.WithWarnings(result, binding.Before.Part.Views);
    }
    static void Budget(Result result, Action cancel)
    { cancel(); Need(JsonSerializer.SerializeToUtf8Bytes(result, Wire.Json).Length <= Wire.PayloadBudget, "RESPONSE_LIMIT_EXCEEDED", "The complete support receipt exceeds transport bytes."); }
    internal static Result Execute(ModifyArguments args, DocumentState state, WriteJob job, ReadContext context, JsonLog log)
    {
        if (ModelSketchSupportRecovery440.Handles(args)) return ModelSketchSupportRecovery440.Execute(args, state, job, context, log);
        Binding? binding = null; McUndoPoint? tracker = null; bool started = false; string stage = "preparation";
        void Trace(string value) { stage = value; log.Write(job.Operation, job.OriginalRequestId, "model_sketch_support411_checkpoint", transaction: value, operationId: job.OperationId); }
        void Close() { var value = tracker; tracker = null; if (value != null) try { value.Stop(); } finally { value.Dispose(); } }
        try
        {
            using (var tx = state.Document.Database.TransactionManager.StartTransaction()) binding = Resolve(args, state, tx, job.CheckCancellation);
            Budget(Outcome(binding, binding.Before, binding.Support, args, job, context), job.CheckCancellation);
            using (var tx = state.Document.Database.TransactionManager.StartTransaction())
            { var current = Resolve(args, state, tx, job.CheckCancellation); Need(current.Token == binding.Token, "PRECONDITION_FAILED", "The exact part, datum, source or control state changed before support binding."); }
            ModelViewFollowers.CheckStandalone(binding.Body, binding.Before.Part.Views, job.CheckCancellation); job.CheckCancellation(); tracker = new McUndoPoint(false); tracker.Start(); started = true;
            Trace("bind_existing_native_workplane"); binding.Sketch.TryModify((uint)McDbObject.ObjChangesType.Geom); binding.Sketch.PlanarEntityID = binding.Support.Support; binding.Sketch.DbEntity.Update(); McObjectManager.UpdateAll();
            Need(binding.Body.RebuildPart(), "MODEL_UPDATE_FAILED", "Native part rebuild rejected the selected existing WorkPlane support."); McObjectManager.UpdateAll(); Trace("native_rebuild_returned");
            var oldSupport = binding.Before.Part.Preserved.GetProperty("sketches").EnumerateArray().Single(row => row.GetProperty("id").GetString() == Key(binding.Sketch.ID)).GetProperty("support");
            var transition = new ModelDatumDependents411.SupportChange(Key(binding.Sketch.ID), oldSupport.ValueKind == JsonValueKind.Null ? null : oldSupport.GetString(), Key(binding.Support.Support), binding.Support.Support);
            (ModelDatumDependents411.Snapshot Part, ModelSketchSupportState396.Binding Support, string[] Warnings) Verify()
            {
                using var tx = state.Document.Database.TransactionManager.StartTransaction(); var representation = ModelHistoryEditGuard.CurrentRepresentation(binding.Body, state, tx, job.CheckCancellation);
                var support = ModelSketchSupportState396.Capture(binding.Request, binding.Body, representation, binding.Owner, state, tx, args.BridgeId, job.CheckCancellation, false);
                ModelSketchSupportState396.RequireAttached(support, binding.Body, binding.Owner, binding.Sketch, job.CheckCancellation);
                ModelSketchSupportState396.Retained(binding.Support, support, Key(binding.Sketch.ID), state, tx, job.CheckCancellation, binding.Before.Affected);
                var after = ModelDatumDependents411.Capture(binding.Body, representation, args, state, tx, binding.Before.Roots, job.CheckCancellation);
                var warnings = ModelDatumDependents411.Verify(binding.Before, after, job.CheckCancellation, transition);
                if (binding.Previous != null)
                {
                    var prior = ModelDatumPlaneState353.Capture(binding.Previous.Plane, binding.Body, state, tx, job.CheckCancellation);
                    ModelDatumDependents411.DetachedSupport(binding.Previous, prior, Key(binding.Sketch.ID), binding.Before.Affected, job.CheckCancellation);
                }
                return (after, support, warnings);
            }
            var observed = Verify(); Budget(Outcome(binding, observed.Part, observed.Support, args, job, context, observed.Warnings), job.CheckCancellation); Trace("native_support_and_consumers_verified");
            job.BeginCommit(); Close(); observed = Verify(); var result = Outcome(binding, observed.Part, observed.Support, args, job, context, observed.Warnings); Budget(result, job.CheckCancellation); job.RecordCommit(result); Trace("commit_recorded"); return result;
        }
        catch (Exception error)
        {
            if (job.CommittedResult is { } committed) return committed; Exception? cleanup = null; try { Close(); } catch (Exception e) { cleanup = e; }
            bool unknown = started || cleanup != null;
            string cause = error is DomainException domain ? domain.Error.code : error is OperationCanceledException ? "CANCELLED" : "MODEL_SKETCH_SUPPORT_FAILED"; string message = error.Message; if (message.Length > 512) message = message[..512];
            return job.Error(unknown ? "WRITE_OUTCOME_UNKNOWN" : cause, unknown ? "Native support binding started but remains unverified. Inspect the retained part/sketch/support before dependent work; no rollback is claimed." : message,
                unknown ? "unknown" : "not_started", context, new { cause, stage, message, body_mc_id = binding == null ? null : Key(binding.Body.ID), sketch_id = binding == null ? null : Key(binding.Sketch.ID), support_id = binding == null ? null : Key(binding.Support.Support), native_details = (error as DomainException)?.Error.details, cleanup_error = cleanup?.GetType().Name }) with
            { warnings = [WriteRecovery.Warning] };
        }
    }
}
