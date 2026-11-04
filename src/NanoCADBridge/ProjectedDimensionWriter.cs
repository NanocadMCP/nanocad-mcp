using Multicad;
using Multicad.DatabaseServices;
using Multicad.Dimensions;
using Multicad.Mc3D;
using NanoCAD.Contracts;
using NanoCAD.Ipc;
using Db = Teigha.DatabaseServices;
using G = Multicad.Geometry;
using Point3d = Teigha.Geometry.Point3d;

namespace NanoCADBridge;

internal static class ProjectedDimensionWriter
{
    private sealed record Binding(ProjectedAlignedDimensionSpec Spec, DrawingViewsReader.ProjectedLine Line,
        McDocument Owner, Db.ObjectId Space, Db.ObjectId Layer, McGeomParam[] Anchors);

    internal static bool Handles(CreateArguments args) => args.Entities.Any(item => item is ProjectedAlignedDimensionSpec);

    private static void Need([System.Diagnostics.CodeAnalysis.DoesNotReturnIf(false)] bool condition,
        string code, string message)
    {
        if (!condition) throw new DomainException(code, message);
    }

    private static G.Point3d Native(Point3d point) => new(point.X, point.Y, point.Z);
    private static Point3d Point(G.Point3d point) => new(point.X, point.Y, point.Z);
    private static bool Same(G.Point3d point, Point3d expected) => Point(point).DistanceTo(expected) <= 1e-7;

    private static McGeomParam[] Anchors(DrawingViewsReader.ProjectedLine line, McDocument owner,
        NativeModelHistory421.ReadProjectedDimensionAnchors read)
    {
        var parameters = new double[2];
        var data = new long[2];
        int status = read(owner.ID.ToString(), line.ViewId.ToString(), line.Marker,
            [line.Start.X, line.Start.Y, line.Start.Z, line.End.X, line.End.Y, line.End.Z], parameters, data);
        Need(status == 0, "DRAWING_VIEW_ASSOCIATION_UNAVAILABLE",
            $"The SDK did not verify both native anchors on the selected view line (HRESULT 0x{unchecked((uint)status):X8}).");
        var anchors = new McGeomParam[2];
        for (int i = 0; i < anchors.Length; i++)
        {
            var anchor = new McGeomParam { ID = line.ViewId, GSMarker = line.Marker, Param = parameters[i], Data = data[i] };
            var evaluated = new G.Point3d();
            Need(anchor.CalculatePoint(ref evaluated) && Same(evaluated, i == 0 ? line.Start : line.End),
                "DRAWING_VIEW_ASSOCIATION_UNAVAILABLE", "The managed native anchor did not preserve the SDK endpoint.");
            anchors[i] = anchor;
        }
        return anchors;
    }

    private static Binding[] Resolve(CreateArguments args, DocumentState state, Db.Transaction tx, Action cancel)
    {
        Need(args.Entities.All(item => item is ProjectedAlignedDimensionSpec),
            "OUT_OF_SCOPE", "Create projected dimensions as their own group in the active drawing space.");
        var database = state.Document.Database;
        bool paper = args.TargetSpace == "paper";
        var space = paper ? CadBlockSpace.RequireWriteSpace(database, tx, "paper", cancel) :
            CadWriteSpace.Resolve(database, tx, args.TargetSpace);
        var owner = paper ? PaperDrawingViewScope356.RequireSheet(CadDrawingViewEditState.Context(state), space) :
            ModelExtrusionFactory.ModelDocument();
        var read = NativeModelHistory421.LoadProjectedDimensionAnchors();
        var layers = (Db.LayerTable)tx.GetObject(database.LayerTableId, Db.OpenMode.ForRead);
        return args.Entities.Cast<ProjectedAlignedDimensionSpec>().Select(spec =>
        {
            cancel();
            var selected = CadStructureWriter.Find(database, spec.ViewHandle);
            var entity = tx.GetObject(selected, Db.OpenMode.ForRead) as Db.Entity;
            Need(entity != null && entity.OwnerId == space, "OUT_OF_SCOPE", "Select a view in the active target space.");
            var line = DrawingViewsReader.ResolveProjectedLine(entity, state, tx, args.BridgeId, spec.LineIndex, cancel);
            var view = line.ViewId.GetObjectOfType<DrawingView>();
            Need(view != null && view.DbEntity.DocumentID == owner.ID && view.DbEntity.Document?.ID == owner.ID &&
                view.DbEntity.DScale == 1 && view.DbEntity.Scale == 1,
                "OUT_OF_SCOPE", "Select an ordinary unit-scale view owned by the active Model space or paper sheet.");
            var offset = new Point3d(spec.DimLinePoint.X, spec.DimLinePoint.Y, 0);
            var direction = line.End - line.Start;
            Need(line.Start.Z == 0 && line.End.Z == 0 && direction.Length > 1e-7 &&
                direction.CrossProduct(offset - line.Start).Length / direction.Length >= .001,
                "INVALID_GEOMETRY", "Select a nonzero XY view line and put the dimension line off its endpoints.");
            Need(layers.Has(spec.Layer), "LAYER_UNAVAILABLE", "The requested dimension layer is unavailable.");
            var layer = (Db.LayerTableRecord)tx.GetObject(layers[spec.Layer], Db.OpenMode.ForRead);
            Need(!layer.IsLocked && !layer.IsOff && !layer.IsFrozen && !layer.IsDependent,
                "LAYER_UNAVAILABLE", "Use a local visible writable dimension layer.");
            return new Binding(spec, line, owner, space, layer.ObjectId, Anchors(line, owner, read));
        }).ToArray();
    }

    internal static void Preflight(CreateArguments args, DocumentState state, Db.Transaction tx, Action cancel) =>
        _ = Resolve(args, state, tx, cancel);

    private static McLinearDimension Prepare(Binding binding)
    {
        var dimension = new McLinearDimension { Type = McLinearDimensionType.Aligned };
        Need(dimension.ID.Handle == 0 && (dimension.DbEntity.Document == null || dimension.DbEntity.DocumentID == binding.Owner.ID) &&
            dimension.SetPosition(0, Native(binding.Line.Start)).IsSuccess() &&
            dimension.SetPosition(1, Native(binding.Line.End)).IsSuccess() &&
            Same(dimension.GetPosition(0), binding.Line.Start) && Same(dimension.GetPosition(1), binding.Line.End),
            "DRAWING_VIEW_ASSOCIATION_UNAVAILABLE", "The detached native aligned dimension did not retain the selected endpoints.");
        dimension.LinePosition = new G.Point3d(binding.Spec.DimLinePoint.X, binding.Spec.DimLinePoint.Y, 0);
        dimension.DbEntity.Layer = binding.Spec.Layer;
        return dimension;
    }

    private static Db.ObjectId VerifyResident(NativeOperationChanges changes, Binding binding, Db.Database database, Action cancel)
    {
        using var tx = database.TransactionManager.StartTransaction();
        Db.ObjectId result = Db.ObjectId.Null;
        foreach (var id in changes.Appended)
        {
            cancel();
            if (tx.GetObject(id, Db.OpenMode.ForRead) is not Db.Dimension dimension) continue;
            Need(result.IsNull && dimension.OwnerId == binding.Space && dimension.LayerId == binding.Layer,
                "OBJECT_IDENTITY_MISMATCH", "Expected exactly one new dimension in the selected drawing space and layer.");
            var points = dimension switch
            {
                Db.RotatedDimension rotated => (rotated.XLine1Point, rotated.XLine2Point, rotated.DimLinePoint),
                Db.AlignedDimension aligned => (aligned.XLine1Point, aligned.XLine2Point, aligned.DimLinePoint),
                _ => throw new DomainException("OBJECT_IDENTITY_MISMATCH", "The native result is not a supported linear dimension.")
            };
            var expected = new Point3d(binding.Spec.DimLinePoint.X, binding.Spec.DimLinePoint.Y, 0);
            var direction = binding.Line.End - binding.Line.Start;
            bool SamePoint(Point3d a, Point3d b) => a.DistanceTo(b) <= 1e-7;
            bool correctEndpoints = SamePoint(points.Item1, binding.Line.Start) && SamePoint(points.Item2, binding.Line.End) ||
                SamePoint(points.Item2, binding.Line.Start) && SamePoint(points.Item1, binding.Line.End);
            bool correctDirection = dimension is not Db.RotatedDimension rotation ||
                Math.Abs(direction.X * Math.Sin(rotation.Rotation) - direction.Y * Math.Cos(rotation.Rotation)) / direction.Length <= 1e-7;
            using var reactors = dimension.GetPersistentReactorIds();
            Need(correctEndpoints && correctDirection &&
                direction.CrossProduct(points.Item3 - expected).Length / direction.Length <= 1e-7 &&
                reactors.Cast<Db.ObjectId>().Any(link => link.IsValid && !link.IsErased && link.Database == database),
                "NATIVE_DIMENSION_ASSOCIATION_MISMATCH", "The resident dimension did not retain the expected endpoints, dimension line and local native dependency storage.");
            result = id;
        }
        Need(!result.IsNull, "OBJECT_IDENTITY_MISMATCH", "The native creation did not append a resident linear dimension.");
        return result;
    }

    internal static Result Execute(CreateArguments args, DocumentState state, WriteJob job, ReadContext context, JsonLog log)
    {
        McUndoPoint? tracker = null;
        var created = new List<McObjectId>();
        bool started = false;
        void Close()
        {
            var undo = tracker;
            tracker = null;
            if (undo != null) try { undo.Stop(); } finally { undo.Dispose(); }
        }

        try
        {
            var database = state.Document.Database;
            Binding[] bindings;
            using (var tx = database.TransactionManager.StartTransaction())
                bindings = Resolve(args, state, tx, job.CheckCancellation);
            var dimensions = bindings.Select(Prepare).ToArray();
            var refs = new List<EntityRef>(dimensions.Length);
            job.CheckCancellation();
            tracker = new McUndoPoint(false);
            tracker.Start();
            started = true;
            for (int index = 0; index < dimensions.Length; index++)
            {
                job.CheckCancellation();
                var dimension = dimensions[index];
                var binding = bindings[index];
                using var changes = new NativeOperationChanges(database);
                created.Add(dimension.ID);
                Need(McObjectManager.Add2Document(dimension.DbEntity, binding.Owner) &&
                    !dimension.ID.IsNull && dimension.DbEntity.DocumentID == binding.Owner.ID,
                    "NATIVE_INSERT_FAILED", "The native dimension was not registered in the view's actual document.");
                created[index] = dimension.ID;
                Need(dimension.SetAssociative(true, binding.Anchors).IsSuccess(),
                    "DRAWING_VIEW_ASSOCIATION_UNAVAILABLE", "The native dimension did not accept the view endpoint parameters.");
                dimension.DbEntity.Update();
                McObjectManager.UpdateAll();
                // MultiCAD materializes this annotation as a DWG linear dimension.
                // Observe the newly appended representation rather than the constructor wrapper.
                var resident = VerifyResident(changes, binding, database, job.CheckCancellation);
                refs.Add(new(args.BridgeId, args.DocumentId, Validation.NormalizeHandle(resident.Handle.ToString())));
            }
            job.BeginCommit();
            Close();
            var result = Result.Ok(job.OriginalRequestId,
                new CreatedEntities(args.OperationId, "committed", false, refs.ToArray(), "not_guaranteed"), context,
                [WriteRecovery.Warning]);
            job.RecordCommit(result);
            return result;
        }
        catch (Exception error)
        {
            if (job.CommittedResult is { } committed) return committed;
            try { Close(); } catch { started = true; }
            var code = error is DomainException domain ? domain.Error.code : error is OperationCanceledException ? "CANCELLED" : "PROJECTED_DIMENSION_FAILED";
            log.Write(job.Operation, job.OriginalRequestId, "projected_dimension_failure", transaction: code, operationId: job.OperationId);
            return job.Error(started ? "WRITE_OUTCOME_UNKNOWN" : code,
                started ? "Native projected dimension creation started. Inspect the current view and dimension before dependent writes." : error.Message,
                started ? "unknown" : "not_started", context,
                new { cause = code, message = error.Message, dimension_ids = created.Select(id => id.ToString()).ToArray() })
                with { warnings = [WriteRecovery.Warning] };
        }
    }
}
