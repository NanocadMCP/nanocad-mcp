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
    private sealed record Projection(McObjectId ViewId, int Marker, Point3d First, Point3d Second,
        Point3d? Center = null, string Kind = "aligned", Point3d? Interior = null, double Sweep = 0,
        double[]? ObservedParameters = null);
    private sealed record Binding(Projection Projection, Point3d Placement, string LayerName,
        McDocument Owner, Db.ObjectId Space, Db.ObjectId Layer, McGeomParam[] Anchors);

    private static bool IsProjectedDimension(EntitySpec item) =>
        item is ProjectedAlignedDimensionSpec or ProjectedDiametricDimensionSpec or ProjectedRadialDimensionSpec or
            ProjectedArcLengthDimensionSpec or ProjectedAngularDimensionSpec;

    internal static bool Handles(CreateArguments args) => args.Entities.Any(IsProjectedDimension);

    private static void Need([System.Diagnostics.CodeAnalysis.DoesNotReturnIf(false)] bool condition,
        string code, string message)
    {
        if (!condition) throw new DomainException(code, message);
    }

    private static G.Point3d Native(Point3d point) => new(point.X, point.Y, point.Z);
    private static Point3d Point(G.Point3d point) => new(point.X, point.Y, point.Z);
    private static bool Same(G.Point3d point, Point3d expected) => Point(point).DistanceTo(expected) <= 1e-7;

    private static McGeomParam[] Anchors(Projection projection, McDocument owner)
    {
        var parameters = new double[2];
        var data = new long[2];
        var first = projection.First;
        var second = projection.Second;
        int status;
        if (projection.Interior is { } interior)
            status = NativeModelHistory421.LoadProjectedCircularArcAnchors()(owner.ID.ToString(), projection.ViewId.ToString(), projection.Marker,
                [first.X, first.Y, first.Z, second.X, second.Y, second.Z, interior.X, interior.Y, interior.Z],
                projection.ObservedParameters ?? [double.NaN, double.NaN], parameters, data);
        else
        {
            var read = projection.Center.HasValue ? NativeModelHistory421.LoadProjectedDiameterAnchors() :
                NativeModelHistory421.LoadProjectedDimensionAnchors();
            status = read(owner.ID.ToString(), projection.ViewId.ToString(), projection.Marker,
                [first.X, first.Y, first.Z, second.X, second.Y, second.Z], parameters, data);
        }
        Need(status == 0, "DRAWING_VIEW_ASSOCIATION_UNAVAILABLE",
            $"The SDK did not verify both native anchors on the selected view primitive (HRESULT 0x{unchecked((uint)status):X8}).");
        var anchors = new McGeomParam[2];
        for (int i = 0; i < anchors.Length; i++)
        {
            var anchor = new McGeomParam { ID = projection.ViewId, GSMarker = projection.Marker, Param = parameters[i], Data = data[i] };
            var evaluated = new G.Point3d();
            Need(anchor.CalculatePoint(ref evaluated) && Same(evaluated, i == 0 ? first : second),
                "DRAWING_VIEW_ASSOCIATION_UNAVAILABLE", "The managed native anchor did not preserve the SDK point.");
            anchors[i] = anchor;
        }
        return anchors;
    }

    private static Binding[] Resolve(CreateArguments args, DocumentState state, Db.Transaction tx, Action cancel)
    {
        Need(args.Entities.All(IsProjectedDimension),
            "OUT_OF_SCOPE", "Create projected dimensions as their own group in the active drawing space.");
        var database = state.Document.Database;
        bool paper = args.TargetSpace == "paper";
        var space = paper ? CadBlockSpace.RequireWriteSpace(database, tx, "paper", cancel) :
            CadWriteSpace.Resolve(database, tx, args.TargetSpace);
        var owner = paper ? PaperDrawingViewScope356.RequireSheet(CadDrawingViewEditState.Context(state), space) :
            ModelExtrusionFactory.ModelDocument();
        var layers = (Db.LayerTable)tx.GetObject(database.LayerTableId, Db.OpenMode.ForRead);
        return args.Entities.Select(spec =>
        {
            cancel();
            var (handle, point) = spec switch
            {
                ProjectedAlignedDimensionSpec aligned => (aligned.ViewHandle, aligned.DimLinePoint),
                ProjectedDiametricDimensionSpec diametric => (diametric.ViewHandle, diametric.DimLinePoint),
                ProjectedRadialDimensionSpec radial => (radial.ViewHandle, radial.DimLinePoint),
                ProjectedArcLengthDimensionSpec arcLength => (arcLength.ViewHandle, arcLength.DimLinePoint),
                ProjectedAngularDimensionSpec angular => (angular.ViewHandle, angular.DimLinePoint),
                _ => throw new DomainException("OUT_OF_SCOPE", "Expected a projected dimension.")
            };
            var selected = CadStructureWriter.Find(database, handle);
            var entity = tx.GetObject(selected, Db.OpenMode.ForRead) as Db.Entity;
            Need(entity != null && entity.OwnerId == space, "OUT_OF_SCOPE", "Select a view in the active target space.");
            var placement = new Point3d(point.X, point.Y, 0);
            Projection projection;
            if (spec is ProjectedAlignedDimensionSpec alignedSpec)
            {
                var line = DrawingViewsReader.ResolveProjectedLine(entity, state, tx, args.BridgeId, alignedSpec.LineIndex, cancel);
                var direction = line.End - line.Start;
                Need(line.Start.Z == 0 && line.End.Z == 0 && direction.Length > 1e-7 &&
                    direction.CrossProduct(placement - line.Start).Length / direction.Length >= .001,
                    "INVALID_GEOMETRY", "Select a nonzero XY view line and put the dimension line off its endpoints.");
                projection = new(line.ViewId, line.Marker, line.Start, line.End);
            }
            else if (spec is ProjectedDiametricDimensionSpec diametric)
            {
                Need(paper, "OUT_OF_SCOPE", "Projected diameter dimensions require an active paper sheet.");
                var circle = DrawingViewsReader.ResolveProjectedCircle(entity, state, tx, args.BridgeId, diametric.ArcIndex, cancel);
                var direction = new Teigha.Geometry.Vector3d(placement.X - circle.Center.X, placement.Y - circle.Center.Y, 0);
                Need(direction.Length >= circle.Radius + .001,
                    "INVALID_GEOMETRY", "Put dimline_point outside the selected projected circle.");
                var radius = direction.GetNormal() * circle.Radius;
                projection = new(circle.ViewId, circle.Marker, circle.Center + radius, circle.Center - radius, circle.Center, "diametric");
            }
            else
            {
                Need(paper, "OUT_OF_SCOPE", "Projected circular dimensions require an active paper sheet.");
                int arcIndex = spec switch
                {
                    ProjectedRadialDimensionSpec radial => radial.ArcIndex,
                    ProjectedArcLengthDimensionSpec arcLength => arcLength.ArcIndex,
                    ProjectedAngularDimensionSpec angular => angular.ArcIndex,
                    _ => throw new DomainException("OUT_OF_SCOPE", "Expected a projected circular dimension.")
                };
                string kind = spec is ProjectedRadialDimensionSpec ? "radial" :
                    spec is ProjectedArcLengthDimensionSpec ? "arc_length" : "angular";
                var arc = DrawingViewsReader.ResolveProjectedCircularArc(entity, state, tx, args.BridgeId, arcIndex, cancel);
                var direction = placement - arc.Center;
                Need(direction.Length >= arc.Radius + .001, "INVALID_GEOMETRY", "Put dimline_point outside the selected circular projection.");
                Point3d first = arc.Start, second = arc.End;
                if (arc.FullCircle)
                {
                    Need(kind == "radial", "INVALID_GEOMETRY", "Arc length and angular dimensions require a finite non-full projected arc.");
                    var radius = direction.GetNormal() * arc.Radius;
                    first = arc.Center + radius; second = arc.Center - radius;
                }
                else if (kind != "radial")
                {
                    double start = Math.Atan2(first.Y - arc.Center.Y, first.X - arc.Center.X);
                    double pick = Math.Atan2(direction.Y, direction.X) - start;
                    if (pick < 0) pick += 2 * Math.PI;
                    Need(pick > 1e-6 && pick < arc.Sweep - 1e-6, "INVALID_GEOMETRY",
                        "Put dimline_point strictly inside the selected counterclockwise arc sector.");
                }
                projection = new(arc.ViewId, arc.Marker, first, second, arc.Center, kind, arc.Midpoint, arc.Sweep,
                    arc.FullCircle ? [double.NaN, double.NaN] : [arc.StartParameter, arc.EndParameter]);
            }
            var view = projection.ViewId.GetObjectOfType<DrawingView>();
            Need(view != null && view.DbEntity.DocumentID == owner.ID && view.DbEntity.Document?.ID == owner.ID &&
                view.DbEntity.DScale == 1 && view.DbEntity.Scale == 1,
                "OUT_OF_SCOPE", "Select an ordinary unit-scale view owned by the active Model space or paper sheet.");
            Need(layers.Has(spec.Layer), "LAYER_UNAVAILABLE", "The requested dimension layer is unavailable.");
            var layer = (Db.LayerTableRecord)tx.GetObject(layers[spec.Layer], Db.OpenMode.ForRead);
            Need(!layer.IsLocked && !layer.IsOff && !layer.IsFrozen && !layer.IsDependent,
                "LAYER_UNAVAILABLE", "Use a local visible writable dimension layer.");
            return new Binding(projection, placement, spec.Layer, owner, space, layer.ObjectId, Anchors(projection, owner));
        }).ToArray();
    }

    internal static void Preflight(CreateArguments args, DocumentState state, Db.Transaction tx, Action cancel) =>
        _ = Resolve(args, state, tx, cancel);

    private static McDimension Prepare(Binding binding)
    {
        var projection = binding.Projection;
        McDimension dimension = projection.Kind switch
        {
            "diametric" => new McDiametralDimension(),
            "radial" => new McRadialDimension(),
            "arc_length" => new McArcDimension(),
            "angular" => new McAngularDimension(),
            _ => new McLinearDimension()
        };
        Need(dimension.ID.Handle == 0 && (dimension.DbEntity.Document == null || dimension.DbEntity.DocumentID == binding.Owner.ID),
            "OBJECT_IDENTITY_MISMATCH", "The native dimension must be detached before creation.");
        if (dimension is McDiametralDimension diameter && projection.Center is { } center)
        {
            diameter.Center = Native(center);
            Need(diameter.SetPosition(0, Native(projection.First)).IsSuccess() &&
                diameter.SetPosition(1, Native(projection.Second)).IsSuccess(),
                "DRAWING_VIEW_ASSOCIATION_UNAVAILABLE", "The detached native diameter did not accept both chord positions.");
            diameter.Symbol = McDimensionSymbol.Diameter;
            diameter.DimensionScale = 1;
            // IMcDimension::setTextPos is the explicit position of the text;
            // setLinePos has different placement semantics for this subtype.
            diameter.TextPosition = Native(binding.Placement);
            Need(diameter.Symbol == McDimensionSymbol.Diameter && diameter.DimensionScale == 1 && Same(diameter.Center, center) &&
                Same(diameter.GetPosition(0), projection.First) && Same(diameter.GetPosition(1), projection.Second),
                "DRAWING_VIEW_ASSOCIATION_UNAVAILABLE", "The detached native diameter did not retain its two chord points and unit scale.");
            Need(!diameter.IsTextPositionDefault && Same(diameter.TextPosition, binding.Placement),
                "DRAWING_VIEW_ASSOCIATION_UNAVAILABLE", "The detached native diameter did not retain the requested text position.");
        }
        else if (dimension is McRadialDimension radial && projection.Center is { } radialCenter)
        {
            radial.Center = Native(radialCenter); radial.Position = Native(projection.First);
            radial.DimensionScale = 1; radial.TextPosition = Native(binding.Placement);
            Need(Same(radial.Center, radialCenter) && Same(radial.Position, projection.First),
                "DRAWING_VIEW_ASSOCIATION_UNAVAILABLE", "The detached native radius did not retain its center and chord point.");
        }
        else if (dimension is McArcDimension arc && projection.Center is { } arcCenter)
        {
            arc.Center = Native(arcCenter);
            Need(arc.SetPosition(0, Native(projection.First)).IsSuccess() && arc.SetPosition(1, Native(projection.Second)).IsSuccess(),
                "DRAWING_VIEW_ASSOCIATION_UNAVAILABLE", "The detached native arc length did not accept its endpoints.");
            arc.DimensionScale = 1; arc.LinePosition = Native(binding.Placement); arc.TextPosition = Native(binding.Placement);
        }
        else if (dimension is McAngularDimension angular && projection.Center is { } angularCenter)
        {
            angular.Center = Native(angularCenter);
            Need(angular.SetPosition(0, Native(projection.First)).IsSuccess() && angular.SetPosition(1, Native(projection.Second)).IsSuccess(),
                "DRAWING_VIEW_ASSOCIATION_UNAVAILABLE", "The detached native angle did not accept its arc endpoint rays.");
            angular.DimensionScale = 1; angular.LinePosition = Native(binding.Placement); angular.TextPosition = Native(binding.Placement);
        }
        else
        {
            var linear = (McLinearDimension)dimension;
            linear.Type = McLinearDimensionType.Aligned;
            Need(linear.SetPosition(0, Native(projection.First)).IsSuccess() &&
                linear.SetPosition(1, Native(projection.Second)).IsSuccess() &&
                Same(linear.GetPosition(0), projection.First) && Same(linear.GetPosition(1), projection.Second),
                "DRAWING_VIEW_ASSOCIATION_UNAVAILABLE", "The detached native aligned dimension did not retain the selected endpoints.");
            linear.LinePosition = Native(binding.Placement);
        }
        dimension.DbEntity.Layer = binding.LayerName;
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
            using var nativeBinding = CadNativeDimensionBinding357.Bind(dimension);
            dimension = (Db.Dimension)nativeBinding.Value;
            Need(result.IsNull && dimension.OwnerId == binding.Space && dimension.LayerId == binding.Layer,
                "OBJECT_IDENTITY_MISMATCH", "Expected exactly one new dimension in the selected drawing space and layer.");
            var projection = binding.Projection;
            bool SamePoint(Point3d a, Point3d b) => a.DistanceTo(b) <= 1e-7;
            bool Endpoints(Point3d first, Point3d second) =>
                SamePoint(first, projection.First) && SamePoint(second, projection.Second) ||
                SamePoint(second, projection.First) && SamePoint(first, projection.Second);
            if (projection.Kind == "diametric")
            {
                Need(dimension is Db.DiametricDimension, "OBJECT_IDENTITY_MISMATCH",
                    $"The native diameter route materialized {dimension.GetType().Name}.");
                var diameter = (Db.DiametricDimension)dimension;
                Need(Endpoints(diameter.ChordPoint, diameter.FarChordPoint),
                    "NATIVE_DIMENSION_ASSOCIATION_MISMATCH", "The resident diameter did not retain the two opposite points of the projected circle.");
                Need(!diameter.UsingDefaultTextPosition && SamePoint(diameter.TextPosition, binding.Placement),
                    "NATIVE_DIMENSION_ASSOCIATION_MISMATCH", "The resident diameter did not retain the requested text position.");
                var definition = Wire.Element(DimensionGeometryReader.Definition(diameter));
                var (measurement, reason) = DimensionMeasurementReader.MeasureDetached(diameter, definition, cancel);
                Need(measurement.HasValue && Math.Abs(measurement.Value - projection.First.DistanceTo(projection.Second)) <= 1e-7,
                    "NATIVE_DIMENSION_ASSOCIATION_MISMATCH", $"The resident diameter did not measure twice the projected circle radius ({reason ?? "value mismatch"}).");
            }
            else if (projection.Center is { } center)
            {
                double radius = projection.First.DistanceTo(center);
                double expected;
                if (projection.Kind == "radial")
                {
                    Need(dimension is Db.RadialDimension radial && SamePoint(radial.Center, center) &&
                        Math.Abs(radial.ChordPoint.DistanceTo(center) - radius) <= 1e-7,
                        "NATIVE_DIMENSION_ASSOCIATION_MISMATCH", "The resident radius did not retain the projected center and circular locus.");
                    var chord = ((Db.RadialDimension)dimension).ChordPoint - center;
                    if (projection.Sweep < 2 * Math.PI - 1e-8)
                    {
                        double angle = Math.Atan2(chord.Y, chord.X) - Math.Atan2(projection.First.Y - center.Y, projection.First.X - center.X);
                        if (angle < 0) angle += 2 * Math.PI;
                        Need(angle <= projection.Sweep + 1e-7, "NATIVE_DIMENSION_ASSOCIATION_MISMATCH",
                            "The resident radius chord is outside the selected finite arc.");
                    }
                    expected = radius;
                }
                else if (projection.Kind == "arc_length")
                {
                    Need(dimension is Db.ArcDimension arc && !arc.IsPartial && SamePoint(arc.CenterPoint, center) &&
                        Endpoints(arc.XLine1Point, arc.XLine2Point), "NATIVE_DIMENSION_ASSOCIATION_MISMATCH",
                        "The resident arc length did not retain the selected full finite arc.");
                    expected = radius * projection.Sweep;
                }
                else
                {
                    bool correctRays = dimension switch
                    {
                        Db.Point3AngularDimension angular => SamePoint(angular.CenterPoint, center) &&
                            Endpoints(angular.XLine1Point, angular.XLine2Point),
                        Db.LineAngularDimension2 angular => SamePoint(angular.XLine1Start, center) &&
                            SamePoint(angular.XLine2Start, center) && Endpoints(angular.XLine1End, angular.XLine2End),
                        _ => false
                    };
                    Need(correctRays, "NATIVE_DIMENSION_ASSOCIATION_MISMATCH",
                        "The resident angle did not retain the projected arc center and endpoint rays.");
                    expected = projection.Sweep;
                }
                var definition = Wire.Element(DimensionGeometryReader.Definition(dimension));
                var (measurement, reason) = DimensionMeasurementReader.MeasureDetached(dimension, definition, cancel);
                Need(measurement.HasValue && Math.Abs(measurement.Value - expected) <= 1e-7,
                    "NATIVE_DIMENSION_ASSOCIATION_MISMATCH", $"The resident {projection.Kind} did not retain the selected measurement ({reason ?? "value mismatch"}).");
                Need(!dimension.UsingDefaultTextPosition && SamePoint(dimension.TextPosition, binding.Placement),
                    "NATIVE_DIMENSION_ASSOCIATION_MISMATCH", "The resident circular dimension did not retain the requested text position.");
            }
            else
            {
                var points = dimension switch
                {
                    Db.RotatedDimension rotated => (rotated.XLine1Point, rotated.XLine2Point, rotated.DimLinePoint),
                    Db.AlignedDimension aligned => (aligned.XLine1Point, aligned.XLine2Point, aligned.DimLinePoint),
                    _ => throw new DomainException("OBJECT_IDENTITY_MISMATCH", "The native result is not a supported linear dimension.")
                };
                var direction = projection.Second - projection.First;
                bool correctDirection = dimension is not Db.RotatedDimension rotation ||
                    Math.Abs(direction.X * Math.Sin(rotation.Rotation) - direction.Y * Math.Cos(rotation.Rotation)) / direction.Length <= 1e-7;
                Need(Endpoints(points.Item1, points.Item2) && correctDirection &&
                    direction.CrossProduct(points.Item3 - binding.Placement).Length / direction.Length <= 1e-7,
                    "NATIVE_DIMENSION_ASSOCIATION_MISMATCH", "The resident linear dimension did not retain the expected endpoints and dimension line.");
            }
            using var reactors = dimension.GetPersistentReactorIds();
            Need(reactors.Cast<Db.ObjectId>().Any(link => link.IsValid && !link.IsErased && link.Database == database),
                "NATIVE_DIMENSION_ASSOCIATION_MISMATCH", "The resident dimension did not retain local native dependency storage.");
            result = id;
        }
        Need(!result.IsNull, "OBJECT_IDENTITY_MISMATCH", "The native creation did not append a resident dimension.");
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
                    "DRAWING_VIEW_ASSOCIATION_UNAVAILABLE", "The native dimension did not accept the view geometry parameters.");
                dimension.DbEntity.Update();
                McObjectManager.UpdateAll();
                // Observe the newly appended DWG dimension rather than the constructor wrapper.
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
                with
            { warnings = [WriteRecovery.Warning] };
        }
    }
}
