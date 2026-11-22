using Multicad;
using Multicad.DatabaseServices;
using Multicad.Dimensions;
using Multicad.Mc3D;
using NanoCAD.Contracts;
using NanoCAD.Ipc;
using System.Text.Json;
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
        McDocument Owner, Db.ObjectId Space, Db.ObjectId Layer, double ViewScaleDenominator, McGeomParam[] Anchors, bool IsSection);

    private static bool IsProjectedDimension(EntitySpec item) =>
        item is ProjectedAlignedDimensionSpec or ProjectedCircleCentersSpec or ProjectedDiametricDimensionSpec or ProjectedRadialDimensionSpec or
            ProjectedArcLengthDimensionSpec or ProjectedAngularDimensionSpec or ProjectedCornerDimensionSpec;

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

    private static McGeomParam CenterAnchor(DrawingViewsReader.ProjectedCenter circle, McDocument owner)
    {
        var p = circle.Center;
        var rim = circle.Rim;
        double[] coordinates = [p.X, p.Y, p.Z, p.X, p.Y, p.Z];
        var parameters = new double[2];
        var data = new long[2];
        string document = owner.ID.ToString(), view = circle.ViewId.ToString();
        int direct = NativeModelHistory421.LoadProjectedDiameterAnchors()(document, view, circle.Marker, coordinates, parameters, data);
        if (direct != 0)
        {
            int seeded = NativeModelHistory421.LoadProjectedCircularArcAnchors()(document, view, circle.Marker,
                [.. coordinates, rim.X, rim.Y, rim.Z], [circle.CenterParameter, circle.CenterParameter], parameters, data);
            Need(seeded == 0, "DRAWING_VIEW_ASSOCIATION_UNAVAILABLE",
                $"The SDK did not resolve the center of original circle marker {circle.Marker} (direct 0x{unchecked((uint)direct):X8}, seeded 0x{unchecked((uint)seeded):X8}).");
        }
        var anchor = new McGeomParam { ID = circle.ViewId, GSMarker = circle.Marker, Param = parameters[0], Data = data[0] };
        var evaluated = new G.Point3d();
        Need(double.IsFinite(parameters[0]) && parameters[0] == parameters[1] && data[0] == data[1] &&
            anchor.CalculatePoint(ref evaluated) && Same(evaluated, p), "DRAWING_VIEW_ASSOCIATION_UNAVAILABLE",
            "The original view reference did not reproduce the selected circle center.");
        return anchor;
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
                ProjectedCircleCentersSpec pair => (pair.ViewHandle, pair.DimLinePoint),
                ProjectedAlignedDimensionSpec aligned => (aligned.ViewHandle, aligned.DimLinePoint),
                ProjectedDiametricDimensionSpec diametric => (diametric.ViewHandle, diametric.DimLinePoint),
                ProjectedRadialDimensionSpec radial => (radial.ViewHandle, radial.DimLinePoint),
                ProjectedArcLengthDimensionSpec arcLength => (arcLength.ViewHandle, arcLength.DimLinePoint),
                ProjectedAngularDimensionSpec angular => (angular.ViewHandle, angular.DimLinePoint),
                ProjectedCornerDimensionSpec corner => (corner.ViewHandle, corner.DimLinePoint),
                _ => throw new DomainException("OUT_OF_SCOPE", "Expected a projected dimension.")
            };
            var selected = CadStructureWriter.Find(database, handle);
            var entity = tx.GetObject(selected, Db.OpenMode.ForRead) as Db.Entity;
            Need(entity != null && entity.OwnerId == space, "OUT_OF_SCOPE", "Select a view in the active target space.");
            var placement = new Point3d(point.X, point.Y, 0);
            Projection projection;
            McGeomParam[]? selectedAnchors = null;
            if (spec is ProjectedCircleCentersSpec centers)
            {
                Need(paper, "OUT_OF_SCOPE", "Projected circle centers require the active Paper sheet.");
                var first = DrawingViewsReader.ResolveProjectedCenter(entity, state, tx, args.BridgeId, centers.FirstMarker, cancel);
                var second = DrawingViewsReader.ResolveProjectedCenter(entity, state, tx, args.BridgeId, centers.SecondMarker, cancel);
                Need(first.ViewId == second.ViewId && first.Marker != second.Marker,
                    "DRAWING_VIEW_SOURCE_UNAVAILABLE", "Select two different current circle markers on the same native projection.");
                var direction = second.Center - first.Center;
                Need(direction.Length > 1e-7 && direction.CrossProduct(placement - first.Center).Length / direction.Length >= .001,
                    "INVALID_GEOMETRY", "Select separated centers and put the dimension line off their common axis.");
                _ = NativeModelHistory421.LoadProjectedDimensionAssociation();
                selectedAnchors = [CenterAnchor(first, owner), CenterAnchor(second, owner)];
                projection = new(first.ViewId, first.Marker, first.Center, second.Center, Kind: "aligned_centers");
            }
            else if (spec is ProjectedAlignedDimensionSpec alignedSpec)
            {
                var line = DrawingViewsReader.ResolveProjectedLine(entity, state, tx, args.BridgeId, alignedSpec.LineIndex, cancel,
                    allowPlaneSection: paper);
                var direction = line.End - line.Start;
                Need(line.Start.Z == 0 && line.End.Z == 0 && direction.Length > 1e-7 &&
                    direction.CrossProduct(placement - line.Start).Length / direction.Length >= .001,
                    "INVALID_GEOMETRY", "Select a nonzero XY view line and put the dimension line off its endpoints.");
                projection = new(line.ViewId, line.Marker, line.Start, line.End);
            }
            else if (spec is ProjectedCornerDimensionSpec cornerSpec)
            {
                Need(paper, "OUT_OF_SCOPE", "Projected angular dimensions require an active Paper sheet.");
                var first = DrawingViewsReader.ResolveProjectedLine(entity, state, tx, args.BridgeId, cornerSpec.FirstLineIndex, cancel);
                var second = DrawingViewsReader.ResolveProjectedLine(entity, state, tx, args.BridgeId, cornerSpec.SecondLineIndex, cancel);
                Need(first.ViewId == second.ViewId && first.Marker != second.Marker &&
                    first.Start.Z == 0 && first.End.Z == 0 && second.Start.Z == 0 && second.End.Z == 0,
                    "DRAWING_VIEW_ASSOCIATION_UNAVAILABLE", "Select two distinct visible straight primitives on the same XY projection.");
                Point2 XY(Point3d p) => new(p.X, p.Y);
                var corner = ProjectedCornerGeometry.Resolve(XY(first.Start), XY(first.End), XY(second.Start), XY(second.End), point);
                var f = Anchors(new(first.ViewId, first.Marker, first.Start, first.End), owner);
                var s = Anchors(new(second.ViewId, second.Marker, second.Start, second.End), owner);
                _ = NativeModelHistory421.LoadProjectedDimensionAssociation();
                var a = corner.FirstVertex == 0 ? first.End : first.Start;
                var b = corner.SecondVertex == 0 ? second.End : second.Start;
                var vertex = corner.FirstVertex == 0 ? first.Start : first.End;
                var aSnap = f[1 - corner.FirstVertex]; var bSnap = s[1 - corner.SecondVertex];
                // McDimensionData has three point slots. Both native curves
                // independently evaluated the shared tip. The vertex stays
                // attached to the first named curve, in either sector.
                selectedAnchors = corner.ReverseRays ? [bSnap, aSnap, f[corner.FirstVertex]] : [aSnap, bSnap, f[corner.FirstVertex]];
                projection = new(first.ViewId, first.Marker, corner.ReverseRays ? b : a, corner.ReverseRays ? a : b, vertex, "angular", Sweep: corner.Sweep);
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
                view.EnableAutoUpdate && view.DbEntity.Scale == 1 && double.IsFinite(view.DbEntity.DScale) && view.DbEntity.DScale > 0 &&
                (paper || view.DbEntity.DScale == 1),
                "OUT_OF_SCOPE", "Use a local Paper drawing view with a positive drawing-scale denominator and design scale 1, or a unit-scale Model view.");
            Need(layers.Has(spec.Layer), "LAYER_UNAVAILABLE", "The requested dimension layer is unavailable.");
            var layer = (Db.LayerTableRecord)tx.GetObject(layers[spec.Layer], Db.OpenMode.ForRead);
            Need(!layer.IsLocked && !layer.IsOff && !layer.IsFrozen && !layer.IsDependent,
                "LAYER_UNAVAILABLE", "Use a local visible writable dimension layer.");
            return new Binding(projection, placement, spec.Layer, owner, space, layer.ObjectId, view.DbEntity.DScale,
                selectedAnchors ?? Anchors(projection, owner), !view.SectionID.IsNull);
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
            diameter.DimensionScale = binding.ViewScaleDenominator;
            // IMcDimension::setTextPos is the explicit position of the text;
            // setLinePos has different placement semantics for this subtype.
            diameter.TextPosition = Native(binding.Placement);
            Need(diameter.Symbol == McDimensionSymbol.Diameter && diameter.DimensionScale == binding.ViewScaleDenominator && Same(diameter.Center, center) &&
                Same(diameter.GetPosition(0), projection.First) && Same(diameter.GetPosition(1), projection.Second),
                "DRAWING_VIEW_ASSOCIATION_UNAVAILABLE", "The detached native diameter did not retain its two chord points and native measurement scale.");
            Need(!diameter.IsTextPositionDefault && Same(diameter.TextPosition, binding.Placement),
                "DRAWING_VIEW_ASSOCIATION_UNAVAILABLE", "The detached native diameter did not retain the requested text position.");
        }
        else if (dimension is McRadialDimension radial && projection.Center is { } radialCenter)
        {
            radial.Center = Native(radialCenter); radial.Position = Native(projection.First);
            radial.DimensionScale = binding.ViewScaleDenominator; radial.TextPosition = Native(binding.Placement);
            Need(Same(radial.Center, radialCenter) && Same(radial.Position, projection.First),
                "DRAWING_VIEW_ASSOCIATION_UNAVAILABLE", "The detached native radius did not retain its center and chord point.");
        }
        else if (dimension is McArcDimension arc && projection.Center is { } arcCenter)
        {
            arc.Center = Native(arcCenter);
            Need(arc.SetPosition(0, Native(projection.First)).IsSuccess() && arc.SetPosition(1, Native(projection.Second)).IsSuccess(),
                "DRAWING_VIEW_ASSOCIATION_UNAVAILABLE", "The detached native arc length did not accept its endpoints.");
            arc.DimensionScale = binding.ViewScaleDenominator; arc.LinePosition = Native(binding.Placement); arc.TextPosition = Native(binding.Placement);
        }
        else if (dimension is McAngularDimension angular && projection.Center is { } angularCenter)
        {
            angular.Center = Native(angularCenter);
            Need(angular.SetPosition(0, Native(projection.First)).IsSuccess() && angular.SetPosition(1, Native(projection.Second)).IsSuccess(),
                "DRAWING_VIEW_ASSOCIATION_UNAVAILABLE", "The detached native angle did not accept its arc endpoint rays.");
            angular.DimensionScale = 1; angular.LinePosition = Native(binding.Placement); angular.TextPosition = Native(binding.Placement);
            if (binding.IsSection || binding.Anchors.Length == 3)
                Need(Same(angular.Center, angularCenter) && Same(angular.GetPosition(0), projection.First) &&
                    Same(angular.GetPosition(1), projection.Second) && Same(angular.LinePosition, binding.Placement),
                    "DRAWING_VIEW_ASSOCIATION_UNAVAILABLE", "The detached angular dimension did not retain the two projected rays, vertex and selected sector.");
        }
        else
        {
            var linear = (McLinearDimension)dimension;
            linear.Type = McLinearDimensionType.Aligned;
            Need(linear.SetPosition(0, Native(projection.First)).IsSuccess() &&
                linear.SetPosition(1, Native(projection.Second)).IsSuccess() &&
                Same(linear.GetPosition(0), projection.First) && Same(linear.GetPosition(1), projection.Second),
                "DRAWING_VIEW_ASSOCIATION_UNAVAILABLE", "The detached native aligned dimension did not retain the selected endpoints.");
            if (binding.ViewScaleDenominator != 1 || projection.Kind == "aligned_centers") linear.DimensionScale = binding.ViewScaleDenominator;
            // IMcLinearDimension stores the measurement direction separately from its two picks.
            // The host can keep the initial X axis for a vertical or oblique projected edge.
            var along = (projection.Second - projection.First).GetNormal();
            linear.Direction = new G.Vector3d(along.X, along.Y, along.Z);
            linear.LinePosition = Native(binding.Placement);
        }
        VerifyPhysicalMeasurement(dimension, binding);
        dimension.DbEntity.Layer = binding.LayerName;
        return dimension;
    }

    private static void VerifyPhysicalMeasurement(McDimension dimension, Binding binding)
    {
        // IMcDimension::getDimScale is the measurement scale. DScale is the
        // drawing denominator: a 1:2 view has DScale=2 and a 10 mm sheet diameter
        // represents 20 mm. Check the native nominal value before the first write
        // and again on the identified resident object after association/update.
        if (binding.Projection.Kind == "angular" || binding.ViewScaleDenominator == 1 && binding.Projection.Kind != "aligned_centers") return;
        var projection = binding.Projection;
        var sheetLength = projection.Kind switch
        {
            "radial" => projection.First.DistanceTo(projection.Center!.Value),
            "arc_length" => projection.First.DistanceTo(projection.Center!.Value) * projection.Sweep,
            _ => projection.First.DistanceTo(projection.Second)
        };
        double expected = sheetLength * binding.ViewScaleDenominator;
        double nominal = dimension.Value;
        Need(dimension.DimensionScale == binding.ViewScaleDenominator && ModelParameterDimensionBinding.NominalMatches(nominal, expected),
            "NATIVE_DIMENSION_SCALE_MISMATCH", $"The native dimension's nominal measurement {nominal} does not match the physical length {expected} at sheet scale 1:{binding.ViewScaleDenominator}.");
    }

    private static Db.ObjectId VerifyResident(NativeOperationChanges changes, Binding binding, Db.Database database, CreateArguments args, Action cancel)
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
                var (measurement, reason) = DimensionMeasurementReader.MeasureProjectedSheet(diameter, cancel);
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
                var (measurement, reason) = DimensionMeasurementReader.MeasureProjectedSheet(dimension, cancel);
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
            if (binding.Anchors.Length == 3 || projection.Kind == "aligned_centers")
            {
                var table = (Db.BlockTable)tx.GetObject(database.BlockTableId, Db.OpenMode.ForRead);
                var summary = EntityAdapter.Summary(dimension, table[Db.BlockTableRecord.ModelSpace], args.BridgeId, args.DocumentId, true);
                var observed = CadProjectedDimensionAssociationReader.ReadLoaded(dimension, summary, cancel, includeDesignSource: false);
                Need(observed != null, "NATIVE_DIMENSION_ASSOCIATION_MISMATCH", "The stored projected dimension anchor inventory is unavailable.");
                var actual = Wire.Element(observed);
                Need(actual.GetProperty("native_associative").ValueKind == System.Text.Json.JsonValueKind.True &&
                    actual.GetProperty("links_complete").GetBoolean() && actual.GetProperty("links").GetArrayLength() == 1 &&
                    actual.GetProperty("drawing_view_mc_id").GetString() == projection.ViewId.ToString(),
                    "NATIVE_DIMENSION_ASSOCIATION_MISMATCH", "The resident dimension did not retain its associative view identity.");
                var anchors = actual.GetProperty("anchors");
                Point3d[] expectedPoints = binding.IsSection || projection.Kind == "aligned_centers" ? [projection.First, projection.Second] : [projection.First, projection.Second, projection.Center!.Value];
                Need(anchors.EnumerateArray().Count(row => !row.GetProperty("is_null").GetBoolean()) == expectedPoints.Length,
                    "NATIVE_DIMENSION_ASSOCIATION_MISMATCH", "The stored anchor count differs from the selected dimension.");
                bool Matches(JsonElement row, McGeomParam expected, Point3d point)
                {
                    var wcs = row.GetProperty("evaluated_point_wcs");
                    return !row.GetProperty("is_null").GetBoolean() &&
                        row.GetProperty("source_mc_id").GetString() == expected.ID.ToString() && row.GetProperty("gs_marker").GetInt32() == expected.GSMarker &&
                        row.GetProperty("parameter").ValueKind == JsonValueKind.Number &&
                        row.GetProperty("parameter").GetDouble() == expected.Param &&
                        row.GetProperty("data").GetString() == expected.Data.ToString(System.Globalization.CultureInfo.InvariantCulture) &&
                        wcs.ValueKind == JsonValueKind.Array && wcs.GetArrayLength() == 3 &&
                        SamePoint(new(wcs[0].GetDouble(), wcs[1].GetDouble(), wcs[2].GetDouble()), point);
                }
                // nanoCAD can reorder the two ray anchors to normalize its DWG
                // angular sector. Each actual parameter identity
                // must still match exactly one distinct resident storage slot.
                var usedSlots = new HashSet<int>();
                for (int role = 0; role < expectedPoints.Length; role++)
                {
                    var matching = anchors.EnumerateArray().Where(row => Matches(row, binding.Anchors[role], expectedPoints[role]))
                        .Select(row => row.GetProperty("slot").GetInt32()).ToArray();
                    Need(matching.Length == 1 && matching[0] is >= 0 and < 3 && usedSlots.Add(matching[0]),
                        "NATIVE_DIMENSION_ASSOCIATION_MISMATCH", $"Anchor role {role} has no unique resident slot retaining its original view marker, point and parameter.");
                }
            }
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
                var resident = VerifyResident(changes, binding, database, args, job.CheckCancellation);
                if (binding.Projection.Kind == "aligned_centers" || binding.ViewScaleDenominator != 1 && binding.Projection.Kind != "angular")
                {
                    var observed = dimension.ID.GetObject() as McDimension;
                    Need(observed != null && observed.ID == dimension.ID && observed.ID.Handle == resident.Handle.Value &&
                        observed.DbEntity.DocumentID == binding.Owner.ID, "OBJECT_IDENTITY_MISMATCH",
                        "The scaled resident dimension must resolve to its actual owner and DWG handle.");
                    VerifyPhysicalMeasurement(observed, binding);
                }
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
