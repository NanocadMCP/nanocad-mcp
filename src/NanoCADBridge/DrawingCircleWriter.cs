using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using Multicad;
using Multicad.DatabaseServices;
using Multicad.DatabaseServices.Constraints;
using Multicad.Mc3D;
using NanoCAD.Contracts;
using NanoCAD.Ipc;
using Db = Teigha.DatabaseServices;
using MPoint = Multicad.Geometry.Point3d;
using MPlane = Multicad.Geometry.Plane3d;

namespace NanoCADBridge;

// Ordinary ACAD driving circle -> explicit projection -> native extrusion.
// This is distinct from the still-unverified internal MCS sketch constraint route.
internal static class DrawingCircleWriter
{
    internal static bool Handles(ModifyArguments args) => args.Operations.Any(e => e is DrawingCircleEdit);
    private static void Need([DoesNotReturnIf(false)] bool ok, string code, string message) { if (!ok) throw new DomainException(code, message); }
    private static bool Near(double a, double b) => double.IsFinite(a) && double.IsFinite(b) && Math.Abs(a - b) <= Math.Max(1e-7, Math.Abs(b) * 1e-9);
    private sealed record Source(Db.ObjectId Id, McObjectId Native, McDocument Sheet, Point3 Center, double Radius, string Layer);
    private sealed record Consumer(Mc3dSolid Body, Db.ObjectId Id, PlanarSketch Sketch, ProjectionSketchEnt Projection, ExtrudeFeature Feature,
        McObjectId ProfileId, McObjectId LastItem, McObjectId[] Contents, double Distance, FeatureExtentDirection Direction, PartFeatureOperation Operation);
    private static Db.ObjectId Model(Db.Database db, Db.Transaction tx) => ((Db.BlockTable)tx.GetObject(db.BlockTableId, Db.OpenMode.ForRead))[Db.BlockTableRecord.ModelSpace];
    private static void Layer(Db.Entity entity, Db.Transaction tx)
    {
        var layer = (Db.LayerTableRecord)tx.GetObject(entity.LayerId, Db.OpenMode.ForRead);
        Need(!layer.IsLocked && !layer.IsOff && !layer.IsFrozen && !layer.IsDependent, "LAYER_UNAVAILABLE", "Source or consuming body layer is unavailable.");
    }
    internal static void Preflight(ModifyArguments args, DocumentState state, Db.Transaction tx, Action? cancel = null)
    {
        var loaded = AppDomain.CurrentDomain.GetAssemblies().Select(a => a.GetName().Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        Need(loaded.Contains("mapimgd") && loaded.Contains("MapiBaseTypes"), "CAPABILITY_UNAVAILABLE", "MultiCAD must already be loaded.");
        var edit = (DrawingCircleEdit)args.Operations.Single(); var source = Resolve(edit, state, tx);
        if (edit.Action is "add_drawing_radius" or "set_drawing_radius")
            RadiusPlan(source, edit, cancel ?? (() => { }));
        if (edit.Action == "set_drawing_radius")
        {
            Consumers(source, state, tx, cancel ?? (() => { }));
        }
    }
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static Source Resolve(DrawingCircleEdit edit, DocumentState state, Db.Transaction tx)
    {
        var db = state.Document.Database; var model = Model(db, tx);
        Need(db.CurrentSpaceId == model, "OUT_OF_SCOPE", "Activate Model Space.");
        var doc = McDocument.ActiveDocument; var sheet = McDocumentsManager.GetActiveSheet();
        Need(doc != null && sheet != null && state.Document.IsNamedDrawing && string.Equals(Path.GetFullPath(doc.Path), Path.GetFullPath(state.Document.Name), StringComparison.OrdinalIgnoreCase), "DOCUMENT_CONTEXT_MISMATCH", "Matching named drawing required.");
        var id = CadStructureWriter.Find(db, edit.Handles.Single()); var raw = tx.GetObject(id, Db.OpenMode.ForRead);
        Need(raw.GetType() == typeof(Db.Circle), "UNSUPPORTED_ENTITY_TYPE", "Use an exact native Circle."); var circle = (Db.Circle)raw;
        Need(circle.OwnerId == model && Math.Abs(circle.Center.Z) < 1e-9 && Math.Abs(circle.Thickness) < 1e-9 && (circle.Normal - Teigha.Geometry.Vector3d.ZAxis).Length < 1e-9, "OUT_OF_SCOPE", "Use a top-level Circle in WCSXY/Z0 with +Z normal and zero thickness.");
        Layer(circle, tx); Need(Near(circle.Radius, edit.ExpectedRadius), "EXPECTED_GEOMETRY_MISMATCH", "Source radius differs from expected_radius.");
        var identity = McObjectId.FromHandle(id.Handle.Value, doc.ID);
        var native = identity.GetObjectOfType<Multicad.DatabaseServices.StandardObjects.DbCircle>();
        Need(native != null && native.ID == identity, "OBJECT_IDENTITY_MISMATCH", "Circle has no matching native identity.");
        return new(id, identity, sheet, new(circle.Center.X, circle.Center.Y, 0), circle.Radius, circle.Layer);
    }
    private static McObjectId[] Attached(Constraints2D manager, Source source, Action? cancel = null)
    {
        var inventory = NativeConstraintInventory.Read(manager, () => new[] { source.Native }, cancel ?? (() => { }));
        Need(inventory.Complete, "CONSTRAINTS_UNAVAILABLE", "Complete native constraint inventory required.");
        var attached = new List<McObjectId>();
        foreach (var id in inventory.Ids)
        {
            cancel?.Invoke();
            using var info = manager.GetConstraintInfo(id); Need(info != null, "CONSTRAINTS_UNAVAILABLE", "Constraint details unavailable.");
            if (new[] { info.FirstObject, info.SecondObject, info.ThirdObject }.Any(p => p != null && p.ObjectId == source.Native)) attached.Add(id);
        }
        return attached.ToArray();
    }
    private static (SketchMobilityReport Report, bool PriorAvailable) RadiusPlan(Source source, DrawingCircleEdit edit, Action cancel)
    {
        string handle = source.Id.Handle.ToString();
        var curve = new SketchConstraintCurve(handle, "circle", Center: new(source.Center.X, source.Center.Y), Radius: source.Radius);
        var drawingSource = new DrawingProfileWriter.Source(source.Id, source.Native, curve, source.Layer, DrawingWorkplane.XY);
        var prior = new List<SketchMobilityRelation>();
        using var manager = source.Sheet.Get2dConstraintsManager(Constraint2dNetType.ACAD, source.Sheet.ID, MPlane.XYPlane);
        Need(manager == null || manager.DocumentId == source.Sheet.ID && manager.PlanarSketchId.IsNull && manager.ConstraintsNetworkType == Constraint2dNetType.ACAD,
            "CONSTRAINT_SCOPE_MISMATCH", "The radius constraint manager resolved another drawing or network.");
        var inventory = manager == null ? null : NativeConstraintInventory.Read(manager, () => new[] { source.Native }, cancel);
        bool hasAttached = false;
        foreach (var id in inventory?.Ids ?? new())
        {
            cancel();
            using var info = manager!.GetConstraintInfo(id);
            Need(info != null, "CONSTRAINTS_UNAVAILABLE", "A native constraint has no observable operands.");
            var refs = new[] { info.FirstObject, info.SecondObject, info.ThirdObject }.Where(p => p != null && !p.ObjectId.IsNull).ToArray();
            if (!refs.Any(p => p!.ObjectId == source.Native)) continue;
            hasAttached = true;
            if (edit.Action == "set_drawing_radius" && id.ToString().Equals(edit.ConstraintId, StringComparison.OrdinalIgnoreCase)) continue;
            SketchConstraintSpec? spec = null;
            if (refs.Length == 1 && info.FirstObject?.ObjectId == source.Native)
            {
                double? value = null;
                if (info.IsDimensional && !info.VarId.IsNull && DrawingVariablesAccess.TryConstraintValue(source.Sheet, info.VarId, info.VarName,
                        info.VarExpression, false, out var actual)) value = actual;
                if (info.Type == ConstraintType.GeomFix || value.HasValue && (info.Type is ConstraintType.DimRadial or ConstraintType.DimDiametrical))
                {
                    try { spec = DrawingProfileWriter.DecodeConstraint(info, value, _ => drawingSource, DrawingWorkplane.XY); }
                    catch (DomainException error) when (error.Error.code is "OUT_OF_SCOPE" or "CONSTRAINT_SCOPE_MISMATCH" or "DRAWING_RELATION_MISMATCH") { }
                }
            }
            prior.Add(new(id.ToString(), spec, [handle]));
        }
        var proposed = new[] { new SketchConstraintSpec("radius", handle, null, null, null, edit.Radius ?? source.Radius) };
        bool replacing = edit.Action == "set_drawing_radius";
        var report = SketchMobility.Plan([curve], prior, proposed, inventory?.Complete == true, cancel: cancel, replacingDrivers: replacing);
        if (edit.Action == "set_drawing_radius")
        {
            report.RequireCompatibleReplacement();
            Need(manager != null, "CONSTRAINTS_UNAVAILABLE", "The existing driving radius is unavailable.");
            FindConstraint(manager, source, edit, cancel);
            prior.Add(new(edit.ConstraintId!, proposed[0] with { Value = source.Radius }));
            var before = SketchMobility.Plan([curve], prior, inventoryComplete: inventory?.Complete == true, cancel: cancel);
            report = DrawingProfileWriter.CompareDriverState(before, report);
        }
        else
        {
            report.EffectiveCandidates(proposed, edit.RequireVerifiedConstraints);
            Need(!hasAttached, "OUT_OF_SCOPE", "The single-circle radius command requires an unattached source. Use add_drawing_constraints with the entire connected selection.");
        }
        return (report, inventory?.Complete == true);
    }
    private static McObjectId FindConstraint(Constraints2D manager, Source source, DrawingCircleEdit edit, Action? cancel = null)
    {
        var attached = Attached(manager, source, cancel);
        Need(attached.Length == 1 && attached[0].ToString().Equals(edit.ConstraintId, StringComparison.OrdinalIgnoreCase), "OUT_OF_SCOPE", "The source must have only the exact selected driving radius; other attached constraints are outside this initial scope.");
        using var info = manager.GetConstraintInfo(attached[0]);
        Need(info != null && info.Type == ConstraintType.DimRadial && info.FirstObject.ObjectId == source.Native && !info.VarId.IsNull, "CONSTRAINT_SCOPE_MISMATCH", "Selected relation is not the source circle's driving radius.");
        Need(info.VarExpression == edit.ExpectedExpression, "EXPECTED_EXPRESSION_MISMATCH", "Driver expression changed.");
        using var variables = DrawingVariablesAccess.Manager(source.Sheet);
        Need(variables != null && variables.GetVariableValue(info.VarId, out double value) && Near(value, source.Radius), "CONSTRAINT_VALUE_UNAVAILABLE", "Native driver value must match the current circle radius.");
        var ids = variables.GetAllVariables(); Need(ids != null, "VARIABLES_UNAVAILABLE", "Complete native variable inventory required.");
        ModelInventoryBudget.Ids(ids!, cancel); var budget = new ModelInventoryBudget(cancel);
        Need(!string.IsNullOrWhiteSpace(info.VarName), "VARIABLES_UNAVAILABLE", "Driver name unavailable.");
        var reference = new Regex(@"(?<![\p{L}\p{N}_])" + Regex.Escape(info.VarName) + @"(?![\p{L}\p{N}_])", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase, TimeSpan.FromMilliseconds(50));
        foreach (var id in ids.Where(id => id != info.VarId))
        {
            var variable = variables.GetVariableInfo(id); Need(variable != null && variable.Expression != null && variable.Expression.Length <= 8192, "VARIABLES_UNAVAILABLE", "Variable expression unavailable or too large.");
            budget.Add(new { id = id.ToString(), variable.Name, variable.Expression });
            Need(!reference.IsMatch(variable.Expression), "OUT_OF_SCOPE", "Other drawing variables depend on this driver; bounded circle update cannot claim their rebuild.");
        }
        return attached[0];
    }
    private static ModelFeatureOracle.Metrics Expected(Source source, double radius, double height) => new(Math.PI * radius * radius * Math.Abs(height),
        new(source.Center.X, source.Center.Y, height / 2), new(source.Center.X - radius, source.Center.Y - radius, Math.Min(0, height)), new(source.Center.X + radius, source.Center.Y + radius, Math.Max(0, height)));
    private static Consumer BindConsumer(Source source, Mc3dSolid body, Db.ObjectId id, McObjectId[] contents, DocumentState state, Db.Transaction tx, Action? cancel = null)
    {
        Need(contents.Length > 0, "HISTORY_UNAVAILABLE", "Consuming model history is empty."); ModelInventoryBudget.Ids(contents, cancel);
        var external = ProjectedHistorySources.Read(body, contents, state, tx, cancel);
        ProjectedHistorySources.Healthy(contents, external);
        var linked = external.SingleOrDefault(s => s.id.Equals(source.Native.ToString(), StringComparison.OrdinalIgnoreCase));
        Need(linked != null && linked.projection_ids.Length == 1, "OUT_OF_SCOPE", "This circle must have one exact source projection in each consuming body.");
        var projection = contents.Select(v => v.GetObjectOfType<ProjectionSketchEnt>()).SingleOrDefault(p => p != null && p.SrcObjectID == source.Native);
        Need(projection != null && projection.ID.ToString() == linked!.projection_ids[0], "OBJECT_IDENTITY_MISMATCH", "Native circle projection is not the selected drawing source.");
        var sketch = projection!.HostSketchID.GetObjectOfType<PlanarSketch>();
        Need(sketch != null && sketch.HostSolidID == body.ID && !sketch.IsInEditMode && contents.Contains(sketch.ID), "OBJECT_IDENTITY_MISMATCH", "Source sketch does not belong to the consuming body.");
        var profiles = contents.Select(v => v.GetObjectOfType<SketchProfile>()).Where(p => p != null && p.ParentSketchID == sketch!.ID).ToArray();
        Need(profiles.Length == 1, "OUT_OF_SCOPE", "Source sketch must have one native profile.");
        var features = contents.Select(v => v.GetObjectOfType<ExtrudeFeature>()).Where(f => f != null && f.ProfileID == profiles[0]!.ID).ToArray();
        Need(features.Length == 1, "OUT_OF_SCOPE", "Source profile must drive one native extrusion feature.");
        var feature = features[0]!;
        Need(feature.HostSolidID == body.ID && feature.Extent.ToString() == "Dist" && feature.Distance >= .001 && feature.Distance <= 1e6 &&
            Near(feature.Angle, 0) && Near(feature.Distance2, 0) && feature.Direction is FeatureExtentDirection.Positive or FeatureExtentDirection.Negative &&
            feature.PartOperation is PartFeatureOperation.Join or PartFeatureOperation.Cut,
            "OUT_OF_SCOPE", "Circle consumer must be a healthy untapered join/cut distance extrusion.");
        var solid = (Db.Solid3d)tx.GetObject(id, Db.OpenMode.ForRead); Layer(solid, tx);
        ModelFeatureOracle.Read(solid);
        return new(body, id, sketch, projection, feature, profiles[0]!.ID, body.LastPartItemID, contents, feature.Distance, feature.Direction, feature.PartOperation);
    }
    private static List<Consumer> Consumers(Source source, DocumentState state, Db.Transaction tx, Action cancel)
    {
        var result = new List<Consumer>();
        foreach (Db.ObjectId id in (Db.BlockTableRecord)tx.GetObject(Model(state.Document.Database, tx), Db.OpenMode.ForRead))
        {
            cancel();
            if (tx.GetObject(id, Db.OpenMode.ForRead) is not Db.Solid3d) continue;
            var body = McObjectId.FromHandle(id.Handle.Value, McDocument.ActiveDocument.ID).GetObjectOfType<Mc3dSolid>(); if (body == null) continue;
            var contents = ModelInventoryBudget.History(body, cancel);
            if (!contents.Contains(source.Native)) continue;

            result.Add(BindConsumer(source, body, id, contents.ToArray(), state, tx, cancel));
        }
        return result;
    }
    private static void VerifySource(Source source, DocumentState state, double radius)
    {
        using var tx = state.Document.Database.TransactionManager.StartTransaction(); var circle = (Db.Circle)tx.GetObject(source.Id, Db.OpenMode.ForRead);
        Need(Near(circle.Radius, radius) && Near(circle.Center.X, source.Center.X) && Near(circle.Center.Y, source.Center.Y) && Near(circle.Center.Z, 0) && circle.Layer == source.Layer, "NATIVE_GEOMETRY_MISMATCH", "Source circle radius, center or layer differs from the request.");
    }
    private static List<Consumer> VerifyConsumers(Source source, DocumentState state, IEnumerable<Consumer> bodies, Action? cancel = null)
    {
        using var tx = state.Document.Database.TransactionManager.StartTransaction();
        var ids = ((Db.BlockTableRecord)tx.GetObject(Model(state.Document.Database, tx), Db.OpenMode.ForRead)).Cast<Db.ObjectId>().ToArray();

        var verified = new List<Consumer>();
        foreach (var body in bodies)
        {
            cancel?.Invoke();
            var matches = ids.Where(id => tx.GetObject(id, Db.OpenMode.ForRead) is Db.Solid3d &&
                McObjectId.FromHandle(id.Handle.Value, McDocument.ActiveDocument.ID) == body.Body.ID).ToArray();
            Need(matches.Length == 1 && (matches[0] == body.Id || !ids.Contains(body.Id)), "OBJECT_IDENTITY_MISMATCH", "Consuming model has no unique current representation.");
            var current = body.Body.ID.GetObjectOfType<Mc3dSolid>();
            Need(current != null, "OBJECT_IDENTITY_MISMATCH", "Consuming native body is unavailable after rebuild.");
            var contents = ModelInventoryBudget.History(current!, cancel);
            Need(contents != null && contents.Count == body.Contents.Length && contents.ToHashSet().SetEquals(body.Contents) &&
                current.LastPartItemID == body.LastItem, "MODEL_STATE_UNAVAILABLE", "Radius edit changed the consumer history inventory or terminal item.");
            var observed = BindConsumer(source, current, matches[0], contents.ToArray(), state, tx, cancel);
            Need(observed.Sketch.ID == body.Sketch.ID && observed.Projection.ID == body.Projection.ID &&
                observed.Feature.ID == body.Feature.ID && observed.ProfileId == body.ProfileId &&
                Near(observed.Distance, body.Distance) && observed.Direction == body.Direction && observed.Operation == body.Operation,
                "MODEL_STATE_UNAVAILABLE", "Radius edit changed a consumer's projection, feature, operation or distance.");
            verified.Add(observed);
        }
        return verified;
    }
    internal static Result Execute(ModifyArguments args, DocumentState state, WriteJob job, ReadContext context, JsonLog log)
    {
        bool started = false, initialInventoryUnavailable = false; string stage = "preparation"; McObjectId constraint = McObjectId.Null, variable = McObjectId.Null; Consumer? created = null;
        void Trace(string next) { stage = next; log.Write(job.Operation, job.OriginalRequestId, "drawing_circle_checkpoint", transaction: next, operationId: job.OperationId); }
        try
        {
            var edit = (DrawingCircleEdit)args.Operations.Single(); Source source; List<Consumer> consumers;
            using (var tx = state.Document.Database.TransactionManager.StartTransaction())
            { source = Resolve(edit, state, tx); consumers = edit.Action == "set_drawing_radius" ? Consumers(source, state, tx, job.CheckCancellation) : new(); }
            // Match the accepted native143 command lifecycle. McUndoPoint controls
            // reactors as well as Undo state; no recovery guarantee is offered here.
            double radius = edit.Radius ?? source.Radius; job.CheckCancellation();
            SketchMobilityReport? plan = null;
            if (edit.Action is "add_drawing_radius" or "set_drawing_radius")
            {
                var assessment = RadiusPlan(source, edit, job.CheckCancellation);
                plan = assessment.Report; initialInventoryUnavailable = !assessment.PriorAvailable;
            }
            if (edit.Action == "extrude_drawing_circle")
            {
                started = true; Trace("projection_create_before"); var body = new Mc3dSolid(); body.DbEntity.Layer = source.Layer; body.DbEntity.AddToCurrentDocument();
                var sketch = body.AddPlanarSketch(); sketch.Name = edit.Name + " Sketch"; sketch.DbEntity.Visibility = 0; McObjectManager.Add2Document(sketch.DbEntity); sketch.SetPlane(MPlane.XYPlane);
                var projection = sketch.ProjectObject(source.Native); Need(projection != null && projection.SrcObjectID == source.Native && projection.HostSketchID == sketch.ID, "MODEL_UPDATE_FAILED", "Native projection did not retain the source identity.");
                McObjectManager.UpdateAll(); var profile = sketch.CreateProfile(); profile.DbEntity.Visibility = 0; McObjectManager.Add2Document(profile.DbEntity);
                Need(profile.AutoProcessExternalContours(), "MODEL_UPDATE_FAILED", "Projected circle did not produce a closed profile.");
                using var plane = sketch.Plane; bool positive = edit.Direction == "positive"; if (plane.SignedDistanceTo(new MPoint(0, 0, 1)) < 0) positive = !positive;
                var feature = body.AddExtrudeFeature(profile.ID, edit.Distance!.Value, 0, positive ? FeatureExtentDirection.Positive : FeatureExtentDirection.Negative); Need(feature != null, "MODEL_UPDATE_FAILED", "Native extrusion failed."); feature.Name = edit.Name;
                McObjectManager.UpdateAll(); using var tx = state.Document.Database.TransactionManager.StartTransaction();
                var ids = ((Db.BlockTableRecord)tx.GetObject(Model(state.Document.Database, tx), Db.OpenMode.ForRead)).Cast<Db.ObjectId>().Where(id => tx.GetObject(id, Db.OpenMode.ForRead) is Db.Solid3d && McObjectId.FromHandle(id.Handle.Value, McDocument.ActiveDocument.ID) == body.ID).ToArray();
                Need(ids.Length == 1, "OBJECT_IDENTITY_MISMATCH", "New body has no unique native representation.");
                var contents = ModelInventoryBudget.History(body, job.CheckCancellation);
                created = BindConsumer(source, body, ids[0], contents.ToArray(), state, tx, job.CheckCancellation); consumers.Add(created);
            }
            else
            {
                // Constructor may initialize network data: it belongs only here,
                // after marking the write started, never in the read/preflight path.
                started = true; Trace("constraint_write_before");
                using var manager = new Constraints2D(MPlane.XYPlane);
                Need(manager.DocumentId == source.Sheet.ID && manager.PlanarSketchId.IsNull && manager.ConstraintsNetworkType == Constraint2dNetType.ACAD, "CONSTRAINT_SCOPE_MISMATCH", "Wrong constraint network.");
                if (edit.Action == "add_drawing_radius")
                {
                    // In a drawing with no initialized constraints the SDK returns
                    // null even after constructing the manager. Do not call that
                    // an empty inventory. Verify the actual created association
                    // and unambiguous resulting source inventory instead.
                    if (!initialInventoryUnavailable) Need(Attached(manager, source, job.CheckCancellation).Length == 0, "OUT_OF_SCOPE", "Source already has drawing constraints.");
                    constraint = manager.AddDimRadial(source.Native); Need(!constraint.IsNull, "MODEL_UPDATE_FAILED", "Native radius creation failed.");
                }
                else
                { constraint = FindConstraint(manager, source, edit, job.CheckCancellation); Need(manager.SetConstraintParamValue(constraint, radius) && manager.ApplyAllConstraints(), "MODEL_UPDATE_FAILED", "Native radius update or solve failed."); }
                McObjectManager.UpdateAll(); using var info = manager.GetConstraintInfo(constraint);
                Need(info != null && info.Type == ConstraintType.DimRadial && info.FirstObject.ObjectId == source.Native && !info.VarId.IsNull, "CONSTRAINT_SCOPE_MISMATCH", "Written radius has a different source or type."); variable = info.VarId;
                using var variables = DrawingVariablesAccess.Manager(source.Sheet);
                Need(variables != null && variables.GetVariableValue(variable, out double value) && Near(value, radius), "NATIVE_GEOMETRY_MISMATCH", "Stored driver differs from requested radius.");
            }
            // Dispose native constraint/variable managers before resolving model
            // dependencies, exactly as in the successful source-projection probe.
            McObjectManager.UpdateAll();
            if (edit.Action == "set_drawing_radius")
            {
                foreach (var consumer in consumers)
                {
                    job.CheckCancellation(); var current = consumer.Body.ID.GetObjectOfType<Mc3dSolid>();
                    Need(current != null && current.RebuildPart(), "MODEL_UPDATE_FAILED", "Dependent body rebuild rejected.");
                }
                McObjectManager.UpdateAll();
            }
            Trace("verify"); job.CheckCancellation(); VerifySource(source, state, radius); consumers = VerifyConsumers(source, state, consumers, job.CheckCancellation);
            if (created != null) created = consumers.Single(c => c.Body.ID == created.Body.ID);
            SketchMobilityReport? observed = null;
            if (plan != null)
            {
                using var network = source.Sheet.Get2dConstraintsManager(Constraint2dNetType.ACAD, source.Sheet.ID, MPlane.XYPlane);
                Need(network != null && network.DocumentId == source.Sheet.ID && network.PlanarSketchId.IsNull && network.ConstraintsNetworkType == Constraint2dNetType.ACAD,
                    "CONSTRAINT_SCOPE_MISMATCH", "Could not verify final ACAD radius scope.");
                var attached = Attached(network, source, job.CheckCancellation);
                Need(attached.Length == 1 && attached[0] == constraint, "CONSTRAINT_SCOPE_MISMATCH", "The resulting native radius scope changed.");
                var handle = source.Id.Handle.ToString();
                var curve = new SketchConstraintCurve(handle, "circle", Center: new(source.Center.X, source.Center.Y), Radius: radius);
                var actual = new SketchMobilityRelation(constraint.ToString(), new("radius", handle, null, null, null, radius));
                observed = SketchMobility.Plan([curve], [actual], cancel: job.CheckCancellation);
            }
            job.BeginCommit();
            EntityRef Ref(Db.ObjectId id) => new(args.BridgeId, args.DocumentId, id.Handle.ToString()); var sourceRef = Ref(source.Id); var rebuilt = consumers.Select(v => Ref(v.Id)).ToArray();
            var change = new DrawingCircleChange(edit.Action, sourceRef, constraint.IsNull ? null : constraint.ToString(), variable.IsNull ? null : variable.ToString(), radius, created == null ? null : Ref(created.Id), created?.Sketch.ID.ToString(), created?.Projection.ID.ToString(), created?.Feature.ID.ToString(), edit.Action == "set_drawing_radius" ? rebuilt : Array.Empty<EntityRef>(), plan, observed);
            var warnings = initialInventoryUnavailable ? new[] { WriteRecovery.Warning, "CONSTRAINT_INVENTORY_INITIALIZED: Prior native inventory was unavailable. The created driver and sole resulting source association were verified; no prior-empty-network claim is made." } : new[] { WriteRecovery.Warning };
            var result = Result.Ok(job.OriginalRequestId, new ModifiedEntities(args.OperationId, "committed", false, edit.Action == "extrude_drawing_circle" ? Array.Empty<EntityRef>() : new[] { sourceRef }.Concat(rebuilt).ToArray(), Array.Empty<EntityRef>(), created == null ? Array.Empty<EntityRef>() : new[] { Ref(created.Id) }, Array.Empty<CopyMapping>(), "not_guaranteed", drawing_circle_changes: new[] { change }), context, warnings);
            job.RecordCommit(result); Trace("commit_recorded"); return result;
        }
        catch (Exception error)
        {
            if (job.CommittedResult is { } result) return result;
            bool unknown = started; string cause = error is DomainException d ? d.Error.code : error is OperationCanceledException ? "CANCELLED" : "MODEL_UPDATE_FAILED";
            return job.Error(unknown ? "WRITE_OUTCOME_UNKNOWN" : cause, unknown ? "Drawing circle operation was not verified; inspect partial source/model changes. No rollback is claimed; inspect the affected source and model before dependent edits." : error.Message, unknown ? "unknown" : "not_started", context, new { cause, stage, message = error.Message, verification_details = (error as DomainException)?.Error.details, constraint_id = constraint.IsNull ? null : constraint.ToString() }) with { warnings = new[] { WriteRecovery.Warning } };
        }
    }
}
