using HostMgd.ApplicationServices;
using NanoCAD.Contracts;
using NanoCAD.Ipc;
using Teigha.DatabaseServices;
using Teigha.Geometry;
using Application = HostMgd.ApplicationServices.Application;

namespace NanoCADBridge;

internal sealed class CadWriter
{
    private readonly string _instance;
    private readonly DocumentRegistry _documents;
    private readonly JsonLog _log;
    private readonly int _uiThread = Environment.CurrentManagedThreadId;
    public CadWriter(string instance, DocumentRegistry documents, JsonLog log) { _instance = instance; _documents = documents; _log = log; }
    public DocumentState Preflight(IWriteArguments args, bool insideCommand, DocumentState? bound = null, Action? cancel = null,
        Action<object?>? observePlan = null)
    {
        if (Environment.CurrentManagedThreadId != _uiThread) throw new DomainException("UI_THREAD_MISMATCH", "Write access requires the host UI thread.");
        if (args.BridgeId != _instance) throw new DomainException("BRIDGE_UNAVAILABLE", "Request addresses another bridge lifetime.");
        if (!HostReadGate.IsWindowAvailable()) throw new DomainException("HOST_BUSY", "The host window is unavailable or disabled by a dialog.", true);
        var doc = Application.DocumentManager.MdiActiveDocument;
        if (doc == null) throw new DomainException("NO_DOCUMENT", "No active document.");
        if (!insideCommand && HostReadGate.IsBusy(doc)) throw new DomainException("HOST_BUSY", "Finish the active command before writing.", true);
        var state = _documents.Get(doc);
        if (state.Id != args.DocumentId || (bound != null && !ReferenceEquals(state, bound)))
            throw new DomainException(_documents.IsOpen(args.DocumentId) ? "DOCUMENT_NOT_ACTIVE" : "DOCUMENT_CLOSED", "Requested document is no longer the active open document.");
        if (state.Revision != args.ExpectedRevision) throw new DomainException("STALE_DOCUMENT", "Document changed since expected_revision.", true);
        if (doc.IsReadOnly) throw new DomainException("DOCUMENT_READ_ONLY", "The document is read-only.");
        bool settingsOnly = args is ModifyArguments settings && settings.Operations.All(op => op is EraseSpec or PropertiesSpec or ModelSketchVisibilitySpec436)
            || args is StructureWriteArguments structureSettings && StructureWriteValidation.IsSettingsOnly(structureSettings.Operations);
        if (!settingsOnly && (int)doc.Database.Insunits != 4) throw new DomainException("UNSUPPORTED_UNITS", "Geometry and dimensional size writes require Millimeters; no implicit scaling is performed. Display/layer settings and erase do not convert coordinates.");
        using var tr = doc.Database.TransactionManager.StartTransaction();
        if (args is CreateArguments create)
        {
            create = MaterializeSnaps(create, doc.Database, tr, cancel ?? (() => { }));
            if (ProjectedDimensionWriter.Handles(create)) ProjectedDimensionWriter.Preflight(create, state, tr, cancel ?? (() => { }));
            else if (ModelExtrusionCreator.Handles(create)) ModelExtrusionCreator.Preflight(create, state, tr);
            else Resolve(create, doc.Database, tr, cancel);
        }
        else if (args is ModifyArguments modify)
        {
            if (ModelBodyEraseWriter429.Handles(modify, state, tr)) ModelBodyEraseWriter429.Preflight(modify, state, tr, cancel);
            else if (CadTextFormat442.Handles(modify)) CadTextFormat442.Preflight(modify, state, tr, cancel);
            else if (CadAssociativeArray431.Handles(modify)) CadAssociativeArray431.Preflight(modify, state, tr, cancel);
            else if (CadDrawOrder407.Handles(modify)) CadDrawOrder407.Preflight(modify, state, tr, cancel);
            else if (CadSolidImprintEditor393.Handles(modify)) CadSolidImprintEditor393.Preflight(modify, state, tr, cancel);
            else if (CadNativeExplode386.Handles(modify)) CadNativeExplode386.Preflight(modify, state, tr, cancel);
            else if (DrawingProfileWriter.Handles(modify)) DrawingProfileWriter.Preflight(modify, state, tr, cancel ?? (() => { }));
            else if (DrawingCircleWriter.Handles(modify)) DrawingCircleWriter.Preflight(modify, state, tr, cancel);
            else if (ModelPlacementWriter.Handles(modify)) ModelPlacementWriter.Preflight(modify, state, tr, cancel);
            else if (ModelRevolutionWriter.Handles(modify)) ModelRevolutionWriter.Preflight(modify, state, tr, cancel);
            else if (ModelFeatureWriter.Handles(modify)) ModelFeatureWriter.Preflight(modify, state, tr, cancel);
            else if (ModelSketchVisibilityWriter436.Handles(modify)) ModelSketchVisibilityWriter436.Preflight(modify, state, tr, cancel);
            else if (ModelSketchLifecycleWriter362.Handles(modify)) ModelSketchLifecycleWriter362.Preflight(modify, state, tr, cancel);
            else if (ModelProfileLifecycleWriter340.Handles(modify)) ModelProfileLifecycleWriter340.Preflight(modify, state, tr, cancel);
            else if (ModelFeatureReorderWriter345.Handles(modify)) ModelFeatureReorderWriter345.Preflight(modify, state, tr, cancel);
            else if (ModelFeatureDeletionWriter378.Handles(modify)) ModelFeatureDeletionWriter378.Preflight(modify, state, tr, cancel);
            else if (ModelSketchConstraintParameterWriter379.Handles(modify)) ModelSketchConstraintParameterWriter379.Preflight(modify, state, tr, cancel);
            else if (NativeSketchConstraintRemoval430.Handles(modify)) NativeSketchConstraintRemoval430.Preflight(modify, state, tr, cancel);
            else if (ModelExtrusionExtentsWriter351.Handles(modify)) ModelExtrusionExtentsWriter351.Preflight(modify, state, tr, cancel);
            else if (ModelProfileElementsWriter358.Handles(modify)) ModelProfileElementsWriter358.Preflight(modify, state, tr, cancel);
            else if (ModelProfileRegionsWriter350.Handles(modify)) ModelProfileRegionsWriter350.Preflight(modify, state, tr, cancel);
            else if (ModelDatumPlaneRevisionWriter354.Handles(modify)) ModelDatumPlaneRevisionWriter354.Preflight(modify, state, tr, cancel);
            else if (ModelFaceDatumPlaneWriter356.Handles(modify)) ModelFaceDatumPlaneWriter356.Preflight(modify, state, tr, cancel);
            else if (ModelDatumPlaneWriter353.Handles(modify)) ModelDatumPlaneWriter353.Preflight(modify, state, tr, cancel);
            else if (ModelProfileRebindWriter349.Handles(modify)) ModelProfileRebindWriter349.Preflight(modify, state, tr, cancel);
            else if (CadHatchFillWriter377.Handles(modify)) CadHatchFillWriter377.Preflight(modify, state, tr, cancel);
            else if (CadDimensionAssociationWriter385.Handles(modify)) CadDimensionAssociationWriter385.Preflight(modify, state, tr, cancel);
            else if (CadHatchAssociationWriter358.Handles(modify)) CadHatchAssociationWriter358.Preflight(modify, state, tr, cancel);
            else if (CadHatchBoundaryWriter349.Handles(modify)) CadHatchBoundaryWriter349.Preflight(modify, state, tr, cancel);
            else if (ModelFeatureRevolutionWriter.Handles(modify)) ModelFeatureRevolutionWriter.Preflight(modify, state, tr, cancel ?? (() => { }));
            else if (ModelSketchSupportWriter411.Handles(modify)) ModelSketchSupportWriter411.Preflight(modify, state, tr, cancel);
            else if (ModelManufacturingWriter.Handles(modify)) ModelManufacturingWriter.Preflight(modify, state, tr, cancel);
            else if (ModelPatternWriter.Handles(modify)) ModelPatternWriter.Preflight(modify, state, tr, cancel);
            else if (ModelPatternElementWriter.Handles(modify)) ModelPatternElementWriter.Preflight(modify, state, tr, cancel);
            else if (ModelShellWriter.Handles(modify)) ModelShellWriter.Preflight(modify, state, tr, cancel);
            else if (ModelShellThicknessWriter.Handles(modify)) ModelShellThicknessWriter.Preflight(modify, state, tr, cancel);
            else if (ModelRebuildWriter419.Handles(modify)) ModelRebuildWriter419.Preflight(modify, state, tr, cancel);
            else if (ModelFeatureSuppressionWriter.Handles(modify)) ModelFeatureSuppressionWriter.Preflight(modify, state, tr, cancel);
            else if (ModelEdgeFeatureWriter.Handles(modify)) ModelEdgeFeatureWriter.Preflight(modify, state, tr, cancel);
            else if (ModelSketchWriter.Handles(modify)) ModelSketchWriter.Preflight(modify, state, tr, cancel);
            else if (SketchConstraintWriter.Handles(modify)) SketchConstraintWriter.Preflight(modify, state, tr, cancel);
            else if (ModelUserVariableWriter.Handles(modify)) ModelUserVariableWriter.Preflight(modify, state, tr, cancel);
            else if (ModelParameterNameWriter.Handles(modify)) ModelParameterNameWriter.Preflight(modify, state, tr, cancel);
            else if (CadTableFormulaRange350.Handles(modify))
            {
                var expanded = CadTableFormulaRange350.Expand(modify, state, tr, cancel ?? (() => { }));
                CadModifier.Resolve(expanded, doc.Database, tr, cancel ?? (() => { }));
            }
            else
            {
                var check = cancel ?? (() => { });
                var resolved = CadModifier.Resolve(modify, doc.Database, tr, check);
                if (observePlan != null)
                {
                    if (modify.Operations is [NativeContourFinishingSpec342])
                        observePlan(CadNativeContourFinishing342.Preview(modify, resolved, doc.Database, tr, check, includeGeometry: true));
                    else CadModifier.Preview(modify, resolved, tr, check);
                }
            }
        }
        else if (args is StructureWriteArguments structure)
        {
            if (CadDrawingGroups431.Handles(structure)) CadDrawingGroups431.Preflight(structure, state, tr, cancel);
            else if (CadBlockFromObjects407.Handles(structure)) CadBlockFromObjects407.Preflight(structure, state, tr, cancel ?? (() => { }));
            else if (CadBlockDefinitionGeometry388.Handles(structure)) CadBlockDefinitionGeometry388.Preflight(structure, state, tr, cancel ?? (() => { }));
            else if (CadBlockDefinitionText391.Handles(structure)) CadBlockDefinitionText391.Preflight(structure, state, tr, cancel ?? (() => { }));
            else if (CadBlockDefinitionMembers392.Handles(structure)) CadBlockDefinitionMembers392.Preflight(structure, state, tr, cancel ?? (() => { }));
            else if (CadNestedAttributeEditor393.Handles(structure)) CadNestedAttributeEditor393.Preflight(structure, state, tr, cancel ?? (() => { }));
            else if (CadBlockDefinitionRename349.Handles(structure)) CadBlockDefinitionRename349.Preflight(structure, state, tr, cancel ?? (() => { }));
            else if (CadSectionCutWriter351.Handles(structure)) CadSectionCutWriter351.Preflight(structure, state, tr, cancel ?? (() => { }));
            else if (SectionViewWriter.Handles(structure)) SectionViewWriter.Preflight(structure, state, tr, cancel ?? (() => { }));
            else if (DrawingViewWriter.Handles(structure)) DrawingViewWriter.Preflight(structure, state, tr, cancel ?? (() => { }));
            else if (CadDrawingViewEditWriter.Handles(structure)) CadDrawingViewEditWriter.Preflight(structure, state, tr, cancel ?? (() => { }));
            else if (DrawingViewLifecycleWriter345.Handles(structure)) DrawingViewLifecycleWriter345.Preflight(structure, state, tr, cancel ?? (() => { }));
            else if (structure.Operations is [StructureLifecycleEdit { Operation: "delete_layout" } layoutDelete])
                CadEmptyLayoutDeletionWriter.Resolve(structure, layoutDelete, doc.Database, tr, cancel ?? (() => { }));
            else CadStructureWriter.Resolve(structure, doc.Database, tr, cancel ?? (() => { }));
        }
        else if (args is ModelParameterArguments parameter)
        { if (parameter.ScopeHandles != null) ModelMultiExpressionWriter.Preflight(parameter, state, tr, cancel); else if (parameter.UsesExpressionPlan) ModelExpressionWriter.Preflight(parameter, state, tr, cancel); else ModelParameterWriter.Preflight(parameter, state, tr, cancel); }
        else throw new DomainException("INVALID_ARGUMENT", "Unknown write payload.");
        return state;
    }

    private static CreateArguments MaterializeSnaps(CreateArguments args, Database database, Transaction tr, Action cancel)
    {
        if (!args.Entities.Any(entity => entity is PointFromNativeSnapSpec or LineFromNativeSnapSpec)) return args;
        var space = CadWriteSpace.Resolve(database, tr, args.TargetSpace);
        return args with
        {
            Entities = args.Entities.Select<EntitySpec, EntitySpec>(entity => entity switch
        {
            PointFromNativeSnapSpec point => CadNativeCurveSnap.ResolvePoint(point, database, tr, space, cancel),
            LineFromNativeSnapSpec line => CadNativeCurveSnap.ResolveLine(line, database, tr, space, cancel),
            _ => entity
        }).ToArray()
        };
    }
    private sealed record Dependencies(ObjectId Model, ObjectId[] Layers, ObjectId[] Styles, ObjectId[] AnnotationStyles, ObjectId[][] SolidSources, CadAssociativeHatch.Boundary?[] HatchBoundaries);
    private static Dependencies Resolve(CreateArguments args, Database db, Transaction tr, Action? cancel = null)
    {
        WriteSpace.Check(args);
        var projected = new List<EntityRef>(); var capacity = new SerializedCapacity357(Wire.PayloadBudget - 16384, cancel);
        foreach (var spec in args.Entities)
        { cancel?.Invoke(); var reference = new EntityRef(args.BridgeId, args.DocumentId, "FFFFFFFFFFFFFFFF"); capacity.Add(reference); projected.Add(reference); }
        SerializedCapacity357.Check(new CreatedEntities(args.OperationId, "committed", false, projected.ToArray(), "not_guaranteed"), Wire.PayloadBudget - 16384, cancel);
        var model = CadWriteSpace.Resolve(db, tr, args.TargetSpace);
        var layers = (LayerTable)tr.GetObject(db.LayerTableId, OpenMode.ForRead);
        var styles = (TextStyleTable)tr.GetObject(db.TextStyleTableId, OpenMode.ForRead);
        var layerIds = new ObjectId[args.Entities.Length]; var styleIds = new ObjectId[args.Entities.Length];
        var annotationStyles = new ObjectId[args.Entities.Length];
        var solidSources = Enumerable.Range(0, args.Entities.Length).Select(_ => Array.Empty<ObjectId>()).ToArray();
        var hatchBoundaries = new CadAssociativeHatch.Boundary?[args.Entities.Length];
        var usedBoundaries = new HashSet<string>(StringComparer.Ordinal);
        for (int i = 0; i < args.Entities.Length; i++)
        {
            var spec = args.Entities[i];
            if (spec is AssociativeHatchSpec linked)
            {
                foreach (string source in linked.SourceHandles())
                {
                    cancel?.Invoke();
                    if (!usedBoundaries.Add(source)) throw new DomainException("INVALID_OPERATION_ORDER", "Associative Hatch creations in one batch require disjoint complete boundary source groups; create shared-source Hatches in successive batches with refreshed document context.");
                }
                hatchBoundaries[i] = CadAssociativeHatch.Resolve(linked, db, tr, model, cancel);
            }
            if (spec is RegionSpec region) solidSources[i] = RegionCreator.Resolve(region, db, tr, model);
            else if (spec is CurveSplineSpec408 curveSpline) solidSources[i] = new[] { CadCurveSplineCreator408.Resolve(curveSpline, db, tr, model, cancel) };
            else if (spec is ProfileSolidSpec profileSolid) solidSources[i] = new[] { ProfileSolidCreator.Resolve(profileSolid, db, tr, model) };
            else if (spec is FaceProfileSolidSpec390 faceProfile) solidSources[i] = CadSolidFaceProfileCreator390.Resolve(faceProfile, db, tr, model, new(args.BridgeId, args.DocumentId, faceProfile.ProfileFace.Handle), cancel ?? (() => { }));
            else if (spec is CurveSolidSpec curveSolid) solidSources[i] = CurveSolidCreator.Resolve(curveSolid, db, tr, model, cancel);
            else if (spec is SolidBooleanSpec boolean) solidSources[i] = SolidCreator.Resolve(boolean, db, tr, model);
            else if (spec is SolidTransformCopySpec copy) solidSources[i] = SolidCreator.Resolve(copy, db, tr, model);
            else if (spec is DrilledSolidCopySpec drill) solidSources[i] = SolidCreator.Resolve(drill, db, tr, model);
            else if (spec is SolidFinishingCopySpec finish) solidSources[i] = SolidCreator.Resolve(finish, db, tr, model, new(args.BridgeId, args.DocumentId, finish.SourceHandle), cancel);
            if (!layers.Has(spec.Layer)) throw new DomainException("LAYER_UNAVAILABLE", "Layer does not exist.", false, new { index = i, layer = spec.Layer });
            var layer = (LayerTableRecord)tr.GetObject(layers[spec.Layer], OpenMode.ForRead);
            if (layer.IsLocked || layer.IsFrozen || layer.IsOff || layer.IsDependent)
                throw new DomainException("LAYER_UNAVAILABLE", "Layer is locked, frozen, off or dependent.", false, new { index = i, layer = spec.Layer });
            layerIds[i] = layer.ObjectId;
            if (spec is TextSpec or MTextSpec)
            {
                string styleName = spec is TextSpec t ? t.TextStyle : ((MTextSpec)spec).TextStyle;
                double height = spec is TextSpec d ? d.Height : ((MTextSpec)spec).Height;
                if (!styles.Has(styleName)) throw new DomainException("TEXT_STYLE_UNAVAILABLE", "Text style does not exist.", false, new { index = i, text_style = styleName });
                var style = (TextStyleTableRecord)tr.GetObject(styles[styleName], OpenMode.ForRead);
                if (style.IsDependent || style.IsVertical || style.IsShapeFile || (style.TextSize > 0 && Math.Abs(style.TextSize - height) > 1e-9))
                    throw new DomainException("TEXT_STYLE_UNAVAILABLE", "Style is dependent, vertical, a shape style or has a different fixed height.", false, new { index = i, text_style = styleName });
                FontCoverage.Require(tr, style.ObjectId, spec is TextSpec plain ? plain.Text : string.Join("", ((MTextSpec)spec).Paragraphs));
                styleIds[i] = style.ObjectId;
            }
            else if (spec is IDimensionCreation dimension)
            {
                var dimensionStyles = (DimStyleTable)tr.GetObject(db.DimStyleTableId, OpenMode.ForRead);
                if (!dimensionStyles.Has(dimension.DimensionStyle)) throw new DomainException("DIMENSION_STYLE_UNAVAILABLE", "Explicit dimension style does not exist.");
                var dimensionStyle = (DimStyleTableRecord)tr.GetObject(dimensionStyles[dimension.DimensionStyle], OpenMode.ForRead);
                if (dimensionStyle.IsDependent) throw new DomainException("DIMENSION_STYLE_UNAVAILABLE", "Dependent dimension styles cannot be used for creation.");
                FontCoverage.Require(tr, dimensionStyle.Dimtxsty, dimension.TextOverride);
                styleIds[i] = dimensionStyle.ObjectId;
                if (spec is DimensionSpec associated) CadDimensionAssociationWriter385.PreflightCreation(associated, db, tr, model, cancel ?? (() => { }));
            }
            else if (spec is ToleranceSpec356 tolerance)
            {
                var table = (DimStyleTable)tr.GetObject(db.DimStyleTableId, OpenMode.ForRead);
                if (!table.Has(tolerance.DimensionStyle)) throw new DomainException("DIMENSION_STYLE_UNAVAILABLE", "Explicit tolerance dimension style does not exist.");
                var record = (DimStyleTableRecord)tr.GetObject(table[tolerance.DimensionStyle], OpenMode.ForRead);
                if (record.GetType() != typeof(DimStyleTableRecord) || record.IsDependent) throw new DomainException("DIMENSION_STYLE_UNAVAILABLE", "Use a native local tolerance dimension style.");
                styleIds[i] = record.ObjectId;
            }
            else if (spec is MLeaderBlockSpec442 blockLeader) annotationStyles[i] = CadMLeaderInstance442.ResolveCreation(blockLeader, db, tr, cancel ?? (() => { }));
            else if (spec is MLeaderSpec leader)
            {
                if (!styles.Has(leader.TextStyle)) throw new DomainException("TEXT_STYLE_UNAVAILABLE", "Explicit leader text style does not exist.");
                var textStyle = (TextStyleTableRecord)tr.GetObject(styles[leader.TextStyle], OpenMode.ForRead);
                if (textStyle.IsDependent || textStyle.IsVertical || textStyle.IsShapeFile) throw new DomainException("TEXT_STYLE_UNAVAILABLE", "Unsupported leader text style.");
                FontCoverage.Require(tr, textStyle.ObjectId, leader.Text);
                styleIds[i] = textStyle.ObjectId;
                annotationStyles[i] = NamedStyle(db.MLeaderStyleDictionaryId, leader.MLeaderStyle, typeof(MLeaderStyle), tr);
            }
            else if (spec is TableSpec tableSpec)
                annotationStyles[i] = NamedStyle(db.TableStyleDictionaryId, tableSpec.TableStyle, typeof(TableStyle), tr);
        }
        return new(model, layerIds, styleIds, annotationStyles, solidSources, hatchBoundaries);
    }
    private static ObjectId NamedStyle(ObjectId dictionaryId, string name, Type kind, Transaction tr)
    {
        var dictionary = (DBDictionary)tr.GetObject(dictionaryId, OpenMode.ForRead);
        if (name.Contains('|') || !dictionary.Contains(name)) throw new DomainException("ANNOTATION_STYLE_UNAVAILABLE", "Explicit local annotation style does not exist.");
        var id = dictionary.GetAt(name); var value = tr.GetObject(id, OpenMode.ForRead);
        if (value.GetType() != kind || value.OwnerId != dictionaryId) throw new DomainException("ANNOTATION_STYLE_UNAVAILABLE", "Unexpected annotation style ownership or native type.");
        return id;
    }
    private static Point3d Point(Point2 p) => new(p.X, p.Y, 0);
    internal static Entity Create(EntitySpec spec, ObjectId style, ObjectId annotationStyle = default, Action? cancel = null)
    {
        var entity = CreateLocal(spec, style, annotationStyle, cancel);
        try { if (spec.Workplane != null) entity.TransformBy(DrawingWorkplaneNative.Matrix(spec.Workplane)); if (spec is PointSpec408 point) CadPointEditor408.VerifyCreated((DBPoint)entity, point, () => { }); return entity; }
        catch { entity.Dispose(); throw; }
    }
    private static Entity CreateLocal(EntitySpec spec, ObjectId style, ObjectId annotationStyle, Action? cancel = null)
    {
        switch (spec)
        {
            case WipeoutSpec431 mask: return CadWipeout431.CreateLocal(mask, cancel);
            case PointSpec408 point: return CadPointEditor408.Create(point);
            case ResolvedNativeSnapLineSpec lineFromSnap: return CadNativeCurveSnap.CreateLine(lineFromSnap);
            case ConstructionCurve443 construction: return CadConstructionCurve443.Create(construction);
            case SmoothPolylineSpec409 smooth: return CadSmoothPolylineCreator409.CreateLocal(smooth, cancel);
            case FitSplineSpec408 fitSpline: return CadFitSplineCreator408.CreateLocal(fitSpline);
            case SpatialFitSplineSpec441 spatial: return CadSpatialCurveCreator441.Create(spatial, cancel);
            case Polyline3dSpec441 spatialPolyline: return CadSpatialCurveCreator441.Create(spatialPolyline, cancel);
            case PeriodicFitSplineSpec441 periodic: return CadPeriodicFitSplineCreator441.CreateLocal(periodic, cancel);
            case MTextSpec mtext: return CadMTextCreator.Create(mtext, style);
            case SolidSpec solid: return SolidCreator.Create(solid);
            case ExtrudedSolidSpec extrusion: return SolidCreator.Create(extrusion);
            case RevolvedSolidSpec revolve: return SolidCreator.Create(revolve);
            case LoftedSolidSpec loft: return SolidCreator.Create(loft);
            case HelixSpec helix: return CurveSolidCreator.Create(helix);
            case SweptSolidSpec sweep: return SolidCreator.Create(sweep);
            case MLeaderSpec or TableSpec or HatchSpec or AssociativeHatchSpec: return AnnotationCreator.Create(spec, style, annotationStyle);
            case ArcLengthDimensionSpec356 or JoggedRadiusDimensionSpec356: return CadExtendedDimensions356.Create(spec, style);
            case ToleranceSpec356 tolerance: return CadToleranceFrame356.Create(tolerance, style);
            case AngularDimensionSpec or OrdinateDimensionSpec: return CadAngularOrdinateCreator.Create(spec, style);
            case DimensionSpec dimension:
                return dimension.Kind switch
                {
                    "aligned" => new AlignedDimension(Point(dimension.First), Point(dimension.Second), Point(dimension.DimLinePoint!), dimension.TextOverride, style),
                    "rotated" => new RotatedDimension(dimension.Rotation, Point(dimension.First), Point(dimension.Second), Point(dimension.DimLinePoint!), dimension.TextOverride, style),
                    "radial" => new RadialDimension(Point(dimension.First), Point(dimension.Second), dimension.LeaderLength, dimension.TextOverride, style),
                    "diametric" => new DiametricDimension(Point(dimension.First), Point(dimension.Second), dimension.LeaderLength, dimension.TextOverride, style),
                    _ => throw new DomainException("INVALID_ARGUMENT", "Unknown dimension kind.")
                };
            case LineSpec line: return new Line(Point(line.Start), Point(line.End));
            case CircleSpec circle: return new Circle(Point(circle.Center), Vector3d.ZAxis, circle.Radius);
            case EllipseSpec ellipse: return CadEllipseCreator.Create(ellipse);
            case SplineSpec spline: return CadSplineCreator.Create(spline);
            case ArcSpec arc: return new Arc(Point(arc.Center), Vector3d.ZAxis, arc.Radius, arc.StartAngle, arc.StartAngle + arc.SweepAngle);
            case PolylineSpec polyline:
                var pl = new Polyline(polyline.Vertices.Length);
                try
                {
                    for (int i = 0; i < polyline.Vertices.Length; i++)
                    {
                        var vertex = polyline.Vertices[i];
                        pl.AddVertexAt(i, new Point2d(vertex.Point.X, vertex.Point.Y), vertex.Bulge, 0, 0);
                    }
                    pl.Closed = polyline.Closed; return pl;
                }
                catch { pl.Dispose(); throw; }
            case TextSpec text:
                var entity = new DBText();
                try
                {
                    entity.TextString = text.Text; entity.Position = Point(text.Position); entity.Height = text.Height;
                    entity.TextStyleId = style; entity.Normal = Vector3d.ZAxis; entity.WidthFactor = 1; entity.Rotation = 0;
                    CadTextFormat442.Create(entity, text, style.Database);
                    return entity;
                }
                catch { entity.Dispose(); throw; }
            default: throw new DomainException("INVALID_ARGUMENT", "Unsupported creation type.");
        }
    }
    public ReadContext Context(DocumentState state) => new(_instance, state.Id, state.Revision, DateTime.UtcNow.ToString("O"));
    public Result Execute(WriteJob job, DocumentState bound)
    {
        var entities = new List<Entity>(); var structuralObjects = new List<DBObject>(); Transaction? tr = null;
        DomainError? failure = null; System.Exception? cleanupFailure = null;
        bool writeStarted = false, commitAttempted = false;
        bool rollbackNotGuaranteed = WriteRecovery.NotGuaranteed(job.Arguments);
        var nativeFormatWarnings = new List<string>();
        Action? verifyAfterCommit = null;
        const string verificationPending = "POST_COMMIT_VERIFICATION_PENDING: Commit is confirmed, but dependent geometry/readback requires transaction disposal. Inspect returned handles before relying on the result.";
        ReadContext? context = null;
#if LAYER_RENAME_FAILURE_PROBE
        LayerRenameFailureProbe? layerProbe=null;
#endif
#if CLOSED_BLOCK_FAILURE_PROBE
        ClosedBlockFailureProbe? closedBlockProbe=null;
#endif
#if CLOSED_SPLINE_FAILURE_PROBE
        ClosedSplineFailureProbe? closedSplineProbe=null;
#endif
#if HATCH_PATTERN_FAILURE_PROBE
        HatchPatternFailureProbe? hatchProbe=null;
#endif
#if BLOCK_SCALE_FAILURE_PROBE
        BlockScaleFailureProbe? blockScaleProbe=null;
#endif
#if BLOCK_ERASE_FAILURE_PROBE
        BlockEraseFailureProbe? blockEraseProbe=null;
#endif
#if BLOCK_COPY_FAILURE_PROBE
        BlockCopyFailureProbe? blockCopyProbe=null;
#endif
#if BLOCK_ROOT_PROPERTY_FAILURE_PROBE
        BlockRootPropertyFailureProbe? blockRootPropertyProbe=null;
#endif
#if BLOCK_TRANSFORM_FAILURE_PROBE
        BlockTransformFailureProbe? blockProbe=null;
#endif
#if TABLE_TEXT_FAILURE_PROBE
        TableTextFailureProbe? tableProbe=null;
#endif
#if MLEADER_TEXT_FAILURE_PROBE
        MLeaderTextFailureProbe? leaderProbe=null;
#endif
#if DIMENSION_TEXT_FAILURE_PROBE
        DimensionTextFailureProbe? dimensionProbe=null;
#endif
        // Persist native progress before the host's command-finalization GC. A
        // stalled SDK finalizer can prevent CommandEnded and the final response.
        // Only fixed phase/error codes are logged, never drawing content.
        bool referenceStages = false;
        void Trace(string phase) => _log.Write(job.Operation, job.OriginalRequestId, "write_checkpoint", transaction: phase, operationId: job.OperationId);
        static string CleanupCode(System.Exception error) => error is Teigha.Runtime.Exception native ? native.ErrorStatus.ToString() : error.GetType().Name;
        try
        {
            job.CheckCancellation();
            var args = (IWriteArguments)job.Arguments;
            referenceStages = args is StructureWriteArguments referenceArguments && referenceArguments.Operations.Any(value => value is ExternalReferenceEdit385);
            if (referenceStages) Trace("reference_context_preflight_before");
            var state = Preflight(args, true, bound, job.CheckCancellation); context = Context(state);
            if (referenceStages) Trace("reference_context_preflight_returned");
            if (args is ModifyArguments rangeArgs && CadTableFormulaRange350.Handles(rangeArgs))
            {
                // Keep job.Arguments as the original request/replay payload;
                // only this invocation's native writer uses expanded anchors.
                using var selector = state.Document.Database.TransactionManager.StartTransaction();
                args = CadTableFormulaRange350.Expand(rangeArgs, state, selector, job.CheckCancellation);
            }
            if (args is CreateArguments modelArgs && ModelExtrusionCreator.Handles(modelArgs)) return ModelExtrusionCreator.Execute(modelArgs, state, job, context, _log);
            if (args is CreateArguments dimensionArgs && ProjectedDimensionWriter.Handles(dimensionArgs)) return ProjectedDimensionWriter.Execute(dimensionArgs, state, job, context, _log);
            if (args is ModifyArguments eraseModelArgs)
            {
                bool nativeErase;
                using (var selector = state.Document.Database.TransactionManager.StartTransaction()) nativeErase = ModelBodyEraseWriter429.Handles(eraseModelArgs, state, selector);
                if (nativeErase) return ModelBodyEraseWriter429.Execute(eraseModelArgs, state, job, context, _log);
            }
            if (args is ModifyArguments orderArgs && CadDrawOrder407.Handles(orderArgs)) return CadDrawOrder407.Execute(orderArgs, state, job, context, _log);
            if (args is ModifyArguments imprintArgs && CadSolidImprintEditor393.Handles(imprintArgs)) return CadSolidImprintEditor393.Execute(imprintArgs, state, job, context, _log);
            if (args is ModifyArguments nativeExplodeArgs && CadNativeExplode386.Handles(nativeExplodeArgs)) return CadNativeExplode386.Execute(nativeExplodeArgs, state, job, context, _log);
            if (args is ModifyArguments placementArgs && ModelPlacementWriter.Handles(placementArgs)) return ModelPlacementWriter.Execute(placementArgs, state, job, context, _log);
            if (args is ModifyArguments revolutionArgs && ModelRevolutionWriter.Handles(revolutionArgs)) return ModelRevolutionWriter.Execute(revolutionArgs, state, job, context, _log);
            if (args is ModifyArguments historyArgs && ModelFeatureWriter.Handles(historyArgs)) return ModelFeatureWriter.Execute(historyArgs, state, job, context, _log);
            if (args is ModifyArguments visibilityArgs && ModelSketchVisibilityWriter436.Handles(visibilityArgs)) return ModelSketchVisibilityWriter436.Execute(visibilityArgs, state, job, context, _log);
            if (args is ModifyArguments sketchLifecycleArgs && ModelSketchLifecycleWriter362.Handles(sketchLifecycleArgs)) return ModelSketchLifecycleWriter362.Execute(sketchLifecycleArgs, state, job, context, _log);
            if (args is ModifyArguments profileLifecycleArgs && ModelProfileLifecycleWriter340.Handles(profileLifecycleArgs)) return ModelProfileLifecycleWriter340.Execute(profileLifecycleArgs, state, job, context, _log);
            if (args is ModifyArguments reorderArgs && ModelFeatureReorderWriter345.Handles(reorderArgs)) return ModelFeatureReorderWriter345.Execute(reorderArgs, state, job, context, _log);
            if (args is ModifyArguments deletionArgs && ModelFeatureDeletionWriter378.Handles(deletionArgs)) return ModelFeatureDeletionWriter378.Execute(deletionArgs, state, job, context, _log);
            if (args is ModifyArguments internalArgs && ModelSketchConstraintParameterWriter379.Handles(internalArgs)) return ModelSketchConstraintParameterWriter379.Execute(internalArgs, state, job, context, _log);
            if (args is ModifyArguments constraintRemovalArgs && NativeSketchConstraintRemoval430.Handles(constraintRemovalArgs)) return NativeSketchConstraintRemoval430.Execute(constraintRemovalArgs, state, job, context, _log);
            if (args is ModifyArguments extentArgs && ModelExtrusionExtentsWriter351.Handles(extentArgs)) return ModelExtrusionExtentsWriter351.Execute(extentArgs, state, job, context, _log);
            if (args is ModifyArguments elementArgs && ModelProfileElementsWriter358.Handles(elementArgs)) return ModelProfileElementsWriter358.Execute(elementArgs, state, job, context, _log);
            if (args is ModifyArguments regionArgs && ModelProfileRegionsWriter350.Handles(regionArgs)) return ModelProfileRegionsWriter350.Execute(regionArgs, state, job, context, _log);
            if (args is ModifyArguments datumRevisionArgs && ModelDatumPlaneRevisionWriter354.Handles(datumRevisionArgs)) return ModelDatumPlaneRevisionWriter354.Execute(datumRevisionArgs, state, job, context, _log);
            if (args is ModifyArguments faceDatumArgs && ModelFaceDatumPlaneWriter356.Handles(faceDatumArgs)) return ModelFaceDatumPlaneWriter356.Execute(faceDatumArgs, state, job, context, _log);
            if (args is ModifyArguments datumArgs && ModelDatumPlaneWriter353.Handles(datumArgs)) return ModelDatumPlaneWriter353.Execute(datumArgs, state, job, context, _log);
            if (args is ModifyArguments rebindArgs && ModelProfileRebindWriter349.Handles(rebindArgs)) return ModelProfileRebindWriter349.Execute(rebindArgs, state, job, context, _log);
            if (args is ModifyArguments fillArgs && CadHatchFillWriter377.Handles(fillArgs)) return CadHatchFillWriter377.Execute(fillArgs, state, job, context, _log);
            if (args is ModifyArguments dimensionAssociationArgs && CadDimensionAssociationWriter385.Handles(dimensionAssociationArgs)) return CadDimensionAssociationWriter385.Execute(dimensionAssociationArgs, state, job, context, _log);
            if (args is ModifyArguments associationArgs && CadHatchAssociationWriter358.Handles(associationArgs)) return CadHatchAssociationWriter358.Execute(associationArgs, state, job, context, _log);
            if (args is ModifyArguments boundaryArgs && CadHatchBoundaryWriter349.Handles(boundaryArgs)) return CadHatchBoundaryWriter349.Execute(boundaryArgs, state, job, context, _log);
            if (args is ModifyArguments featureRevolutionArgs && ModelFeatureRevolutionWriter.Handles(featureRevolutionArgs)) return ModelFeatureRevolutionWriter.Execute(featureRevolutionArgs, state, job, context, _log);
            if (args is ModifyArguments sketchSupportArgs && ModelSketchSupportWriter411.Handles(sketchSupportArgs)) return ModelSketchSupportWriter411.Execute(sketchSupportArgs, state, job, context, _log);
            if (args is ModifyArguments manufacturingArgs && ModelManufacturingWriter.Handles(manufacturingArgs)) return ModelManufacturingWriter.Execute(manufacturingArgs, state, job, context, _log);
            if (args is ModifyArguments patternArgs && ModelPatternWriter.Handles(patternArgs)) return ModelPatternWriter.Execute(patternArgs, state, job, context, _log);
            if (args is ModifyArguments patternElementArgs && ModelPatternElementWriter.Handles(patternElementArgs)) return ModelPatternElementWriter.Execute(patternElementArgs, state, job, context, _log);
            if (args is ModifyArguments shellArgs && ModelShellWriter.Handles(shellArgs)) return ModelShellWriter.Execute(shellArgs, state, job, context, _log);
            if (args is ModifyArguments shellThicknessArgs && ModelShellThicknessWriter.Handles(shellThicknessArgs)) return ModelShellThicknessWriter.Execute(shellThicknessArgs, state, job, context, _log);
            if (args is ModifyArguments rebuildModelArgs && ModelRebuildWriter419.Handles(rebuildModelArgs)) return ModelRebuildWriter419.Execute(rebuildModelArgs, state, job, context, _log);
            if (args is ModifyArguments featureStateArgs && ModelFeatureSuppressionWriter.Handles(featureStateArgs)) return ModelFeatureSuppressionWriter.Execute(featureStateArgs, state, job, context, _log);
            if (args is ModifyArguments edgeArgs && ModelEdgeFeatureWriter.Handles(edgeArgs)) return ModelEdgeFeatureWriter.Execute(edgeArgs, state, job, context, _log);
            if (args is ModifyArguments sketchGeometryArgs && ModelSketchWriter.Handles(sketchGeometryArgs)) return ModelSketchWriter.Execute(sketchGeometryArgs, state, job, context, _log);
            if (args is ModifyArguments sketchArgs && SketchConstraintWriter.Handles(sketchArgs)) return SketchConstraintWriter.Execute(sketchArgs, state, job, context, _log);
            if (args is ModifyArguments circleArgs && DrawingCircleWriter.Handles(circleArgs)) return DrawingCircleWriter.Execute(circleArgs, state, job, context, _log);
            if (args is ModifyArguments profileArgs && DrawingProfileWriter.Handles(profileArgs)) return DrawingProfileWriter.Execute(profileArgs, state, job, context, _log);
            if (args is ModifyArguments userArgs && ModelUserVariableWriter.Handles(userArgs)) return ModelUserVariableWriter.Execute(userArgs, state, job, context, _log);
            if (args is ModifyArguments nameArgs && ModelParameterNameWriter.Handles(nameArgs)) return ModelParameterNameWriter.Execute(nameArgs, state, job, context, _log);
            if (args is ModelParameterArguments parameter) return parameter.ScopeHandles != null ? ModelMultiExpressionWriter.Execute(parameter, state, job, context, _log) : parameter.UsesExpressionPlan
                ? ModelExpressionWriter.Execute(parameter, state, job, context, _log) : ModelParameterWriter.Execute(parameter, state, job, context, _log);
            if (args is ModifyArguments textFormat442 && CadTextFormat442.Handles(textFormat442)) return CadTextFormat442.Execute(textFormat442, state, job, context);
            if (args is ModifyArguments arrayArgs431 && CadAssociativeArray431.Handles(arrayArgs431)) return CadAssociativeArray431.Execute(arrayArgs431, state, job, context, _log);
            if (args is StructureWriteArguments groupArgs && CadDrawingGroups431.Handles(groupArgs)) return CadDrawingGroups431.Execute(groupArgs, state, job, context, _log);
            if (args is StructureWriteArguments blockFromArgs && CadBlockFromObjects407.Handles(blockFromArgs)) return CadBlockFromObjects407.Execute(blockFromArgs, state, job, context, _log);
            if (args is StructureWriteArguments blockGeometryArgs && CadBlockDefinitionGeometry388.Handles(blockGeometryArgs)) return CadBlockDefinitionGeometry388.Execute(blockGeometryArgs, state, job, context, _log);
            if (args is StructureWriteArguments blockTextArgs && CadBlockDefinitionText391.Handles(blockTextArgs)) return CadBlockDefinitionText391.Execute(blockTextArgs, state, job, context, _log);
            if (args is StructureWriteArguments blockMembersArgs && CadBlockDefinitionMembers392.Handles(blockMembersArgs)) return CadBlockDefinitionMembers392.Execute(blockMembersArgs, state, job, context, _log);
            if (args is StructureWriteArguments nestedAttributeArgs && CadNestedAttributeEditor393.Handles(nestedAttributeArgs)) return CadNestedAttributeEditor393.Execute(nestedAttributeArgs, state, job, context, _log);
            if (args is StructureWriteArguments blockRenameArgs && CadBlockDefinitionRename349.Handles(blockRenameArgs)) return CadBlockDefinitionRename349.Execute(blockRenameArgs, state, job, context, _log);
            if (args is StructureWriteArguments cutArgs && CadSectionCutWriter351.Handles(cutArgs)) return CadSectionCutWriter351.Execute(cutArgs, state, job, context, _log);
            if (args is StructureWriteArguments sectionArgs && SectionViewWriter.Handles(sectionArgs)) return SectionViewWriter.Execute(sectionArgs, state, job, context, _log);
            if (args is StructureWriteArguments viewArgs && DrawingViewWriter.Handles(viewArgs)) return DrawingViewWriter.Execute(viewArgs, state, job, context, _log);
            if (args is StructureWriteArguments existingViews && CadDrawingViewEditWriter.Handles(existingViews)) return CadDrawingViewEditWriter.Execute(existingViews, state, job, context, _log);
            if (args is StructureWriteArguments lifecycleViews && DrawingViewLifecycleWriter345.Handles(lifecycleViews)) return DrawingViewLifecycleWriter345.Execute(lifecycleViews, state, job, context, _log);
            if (args is StructureWriteArguments layoutArgs && layoutArgs.Operations is [StructureLifecycleEdit { Operation: "delete_layout" } layoutDelete])
            {
                CadEmptyLayoutDeletionWriter.Resolved layoutSelection;
                using (var selector = state.Document.Database.TransactionManager.StartTransaction())
                    layoutSelection = CadEmptyLayoutDeletionWriter.Resolve(layoutArgs, layoutDelete, state.Document.Database, selector, job.CheckCancellation);
                var change = CadEmptyLayoutDeletionWriter.Apply(0, layoutArgs, layoutSelection, state.Document.Database, job, () =>
                {
                    job.CheckCancellation(); job.BeginCommit(); writeStarted = true; commitAttempted = true;
                });
                var layoutResult = Result.Ok(job.OriginalRequestId, new ModifiedStructure(args.OperationId, "committed", false, new[] { change }, "not_guaranteed"), Context(state), new[] { WriteRecovery.Warning });
                job.RecordCommit(layoutResult);
                return layoutResult;
            }
            tr = state.Document.Database.TransactionManager.StartTransaction();
            object data;
            if (args is CreateArguments create)
            {
                create = MaterializeSnaps(create, state.Document.Database, tr, job.CheckCancellation);
                var dependencies = Resolve(create, state.Document.Database, tr, job.CheckCancellation);
                var fitReports = new List<SmoothPolylineFit409>();
                // Construct every detached entity before opening Model Space for write.
                for (int i = 0; i < create.Entities.Length; i++)
                {
                    job.CheckCancellation();
                    var entity = create.Entities[i] switch
                    {
                        CurveSplineSpec408 curveSpline => CadCurveSplineCreator408.Create(curveSpline, dependencies.SolidSources[i][0], state.Document.Database, tr, dependencies.Model, job.CheckCancellation, nativeFormatWarnings),
                        RegionSpec region => RegionCreator.Create(region, dependencies.SolidSources[i], tr),
                        ProfileSolidSpec profileSolid => ProfileSolidCreator.Create(profileSolid, dependencies.SolidSources[i][0], tr),
                        FaceProfileSolidSpec390 faceProfile => CadSolidFaceProfileCreator390.Create(faceProfile, dependencies.SolidSources[i], tr, new(create.BridgeId, create.DocumentId, faceProfile.ProfileFace.Handle), job.CheckCancellation, nativeFormatWarnings),
                        CurveSolidSpec curveSolid => CurveSolidCreator.Create(curveSolid, dependencies.SolidSources[i], tr, job.CheckCancellation, nativeFormatWarnings),
                        SolidBooleanSpec boolean => SolidCreator.Boolean(boolean, dependencies.SolidSources[i], tr,
                            (stage, elapsed) => _log.Write(job.Operation, job.OriginalRequestId, "solid_boolean_checkpoint", elapsed, stage, job.OperationId)),
                        SolidTransformCopySpec copy => SolidCreator.TransformCopy(copy, dependencies.SolidSources[i][0], tr),
                        DrilledSolidCopySpec drill => SolidCreator.DrillCopy(drill, dependencies.SolidSources[i][0], tr),
                        SolidFinishingCopySpec finish => SolidFinisher.Create(finish, (Solid3d)tr.GetObject(dependencies.SolidSources[i][0], OpenMode.ForRead), new(create.BridgeId, create.DocumentId, finish.SourceHandle), job.CheckCancellation),
                        MLeaderBlockSpec442 blockLeader => CadMLeaderInstance442.Create(blockLeader, dependencies.AnnotationStyles[i], state.Document.Database, tr, job.CheckCancellation),
                        _ => Create(create.Entities[i], dependencies.Styles[i], dependencies.AnnotationStyles[i], job.CheckCancellation)
                    };
                    entities.Add(entity);
                    entity.LayerId = dependencies.Layers[i];
                    if (create.Entities[i] is SmoothPolylineSpec409 smooth) fitReports.Add(CadSmoothPolylineCreator409.VerifyCreated((Polyline)entity, smooth, i, job.CheckCancellation));
                }
                if (fitReports.Count > 0)
                {
                    var projected = entities.Select(_ => new EntityRef(create.BridgeId, create.DocumentId, "FFFFFFFFFFFFFFFF")).ToArray();
                    SerializedCapacity357.Check(new CreatedEntities(create.OperationId, "committed", false, projected, "not_guaranteed", fitReports.ToArray()), Wire.PayloadBudget - 16384, job.CheckCancellation);
                }
                // Inspect every detached annotation's actual font before the
                // first resident append; retain uncertain formatting as warnings.
                foreach (var entity in entities)
                    FontCoverage.RequireEntity(tr, entity, nativeFormatWarnings.Add, job.CheckCancellation);
                job.CheckCancellation(); writeStarted = true;
                _log.Write(job.Operation, job.OriginalRequestId, "append_begin", transaction: "write_open", operationId: job.OperationId);
                var model = (BlockTableRecord)tr.GetObject(dependencies.Model, OpenMode.ForWrite);
                if (create.Entities.Any(e => e is ToleranceSpec356 or ArcLengthDimensionSpec356 or JoggedRadiusDimensionSpec356)) { rollbackNotGuaranteed = true; nativeFormatWarnings.Add(CadToleranceFrame356.Warning); }
                if (create.Entities.Any(e => e is DimensionSpec { Associations: not null })) { rollbackNotGuaranteed = true; nativeFormatWarnings.Add(CadDimensionAssociation385.Warning); }
                var references = new EntityRef[entities.Count];
                for (int i = 0; i < entities.Count; i++)
                {
                    job.CheckCancellation();
                    model.AppendEntity(entities[i]); tr.AddNewlyCreatedDBObject(entities[i], true);
                    if (entities[i] is Dimension dimension) dimension.RecomputeDimensionBlock(true);
                    if (create.Entities[i] is AngularDimensionSpec or OrdinateDimensionSpec) CadAngularOrdinateCreator.Verify((Dimension)entities[i], create.Entities[i], dependencies.Styles[i]);
                    if (create.Entities[i] is ArcLengthDimensionSpec356 or JoggedRadiusDimensionSpec356) CadExtendedDimensions356.VerifyCreated((Dimension)entities[i], create.Entities[i], dependencies.Styles[i], job.CheckCancellation);
                    if (create.Entities[i] is ToleranceSpec356 tolerance) { CadToleranceFrame356.VerifyCreated((FeatureControlFrame)entities[i], tolerance, dependencies.Styles[i], job.CheckCancellation); nativeFormatWarnings.Add(CadToleranceFrame356.Warning); rollbackNotGuaranteed = true; }
                    if (create.Entities[i] is DimensionSpec { Associations: not null } associated)
                    { CadDimensionAssociationWriter385.CompleteCreation((Dimension)entities[i], associated, state.Document.Database, tr, job.CheckCancellation); }
                    AnnotationCreator.Complete(entities[i], create.Entities[i]);
                    if (dependencies.HatchBoundaries[i] is { } boundary) CadAssociativeHatch.Complete((Hatch)entities[i], boundary, tr, job.CheckCancellation);
                    if (create.Entities[i] is MLeaderBlockSpec442 blockLeader) { CadMLeaderInstance442.VerifyCreated((MLeader)entities[i], blockLeader, state.Document.Database, tr, job.CheckCancellation); nativeFormatWarnings.Add(CadMLeaderInstance442.Warning); rollbackNotGuaranteed = true; }
                    if (entities[i] is MText mtext && create.Entities[i] is MTextSpec mtextSpec) CadMTextCreator.Verify(mtext, mtextSpec, dependencies.Styles[i]);
                    if (create.Entities[i] is SmoothPolylineSpec409 smooth)
                        fitReports[fitReports.FindIndex(report => report.entity_index == i)] = CadSmoothPolylineCreator409.VerifyCreated((Polyline)entities[i], smooth, i, job.CheckCancellation);
                    if (create.Entities[i] is PointSpec408 pointSpec) CadPointEditor408.VerifyCreated((DBPoint)entities[i], pointSpec, job.CheckCancellation);
                    if (create.Entities[i] is ResolvedNativeSnapLineSpec snappedLine) CadNativeCurveSnap.VerifyLine((Line)entities[i], snappedLine);
                    if (create.Entities[i] is ConstructionCurve443 construction) CadConstructionCurve443.VerifyCreated(entities[i], construction, job.CheckCancellation);
                    if (create.Entities[i] is FitSplineSpec408 fitSpline) CadFitSplineCreator408.VerifyCreated((Spline)entities[i], fitSpline, job.CheckCancellation);
                    if (create.Entities[i] is SpatialFitSplineSpec441 spatialSpline) CadSpatialCurveCreator441.VerifyCreated((Spline)entities[i], spatialSpline, job.CheckCancellation);
                    if (create.Entities[i] is Polyline3dSpec441 spatialPolyline) CadSpatialCurveCreator441.VerifyCreated((Polyline3d)entities[i], spatialPolyline, job.CheckCancellation);
                    if (create.Entities[i] is PeriodicFitSplineSpec441 periodic) CadPeriodicFitSplineCreator441.VerifyCreated((Spline)entities[i], periodic, job.CheckCancellation);
                    if (create.Entities[i] is EllipseSpec or SplineSpec) CadPlaneCurveGeometry393.VerifyCreated(entities[i], create.Entities[i], job.CheckCancellation);
                    references[i] = new(_instance, state.Id, entities[i].Handle.ToString());
                }
                data = new CreatedEntities(args.OperationId, "committed", false, references, rollbackNotGuaranteed ? "not_guaranteed" : "single_command", fitReports.Count == 0 ? null : fitReports.ToArray());
            }
            else if (args is ModifyArguments modify)
            {
                var resolved = CadModifier.Resolve(modify, state.Document.Database, tr, job.CheckCancellation);
                string[] documentationAdditions = Array.Empty<string>(), documentationErasures = Array.Empty<string>();
                if (resolved.DocumentedSolids.Count > 0)
                {
                    var documentation = DirectSolidDocumentation.Capture(state.Document, modify.Operations.SelectMany(op => op.Handles), job.CheckCancellation);
                    // Resolve already proved these direct bodies have no native
                    // documentation/model consumers. Only explicitly requested
                    // erasures may remove them from a mixed machining batch.
                    var erasableBodies = modify.Operations.OfType<EraseSpec>().SelectMany(op => op.Handles)
                        .Where(h => !resolved.DocumentedSolids.Contains(h) && tr.GetObject(resolved.Sources[h], OpenMode.ForRead) is Solid3d)
                        .ToHashSet(StringComparer.Ordinal);
                    rollbackNotGuaranteed = true;
                    verifyAfterCommit = () => DirectSolidDocumentation.RefreshAndVerify(state.Document, documentation, documentationAdditions, documentationErasures, erasableBodies);
                }
                if (resolved.SolidReplacement is { } replacementPlan)
                {
                    rollbackNotGuaranteed = true; var prior = verifyAfterCommit;
                    verifyAfterCommit = () => { CadSolidReplacement409.Verify(replacementPlan, state.Document.Database, job.CheckCancellation); prior?.Invoke(); };
                }
                rollbackNotGuaranteed |= resolved.Blocks.Any(pair => pair.Value.Attributes.Length > 0 && modify.Operations.Any(op => (op is MoveSpec or RotateSpec or ScaleSpec) && op.Handles.Contains(pair.Key)));
                foreach (var source in resolved.Sources.Values)
                {
                    var sourceObject = tr.GetObject(source, OpenMode.ForRead);
                    if (sourceObject is Hatch { Associative: true } || sourceObject is Entity construction && CadConstructionCurve443.Is(construction)) rollbackNotGuaranteed = true;
                    if (sourceObject is Entity annotation && (CadAnnotationTransformEditor.Supported(annotation.GetType()) || CadNativeDimensionBinding357.Supported(annotation)) &&
                        modify.Operations.Any(op => CadAnnotationTransformEditor.Placement(op) && op.Handles.Contains(Validation.NormalizeHandle(annotation.Handle.ToString()))))
                        rollbackNotGuaranteed = true;
                    using var reactors = sourceObject.GetPersistentReactorIds();
                    if (reactors.Count > 0) rollbackNotGuaranteed = true;
                }
                if (modify.Operations.Any(op => op is Mirror2dSpec && op.Handles.Any(h =>
                    CadAnnotationMirrorEditor340.Supported(tr.GetObject(resolved.Sources[h], OpenMode.ForRead).GetType()))))
                { rollbackNotGuaranteed = true; nativeFormatWarnings.AddRange(CadAnnotationMirrorEditor340.Warnings()); }
                if (modify.Operations.Any(op => op is ToleranceEdit356)) { rollbackNotGuaranteed = true; nativeFormatWarnings.Add(CadToleranceFrame356.Warning); }
                CadModifier.Preview(modify, resolved, tr, job.CheckCancellation);
#if CLOSED_BLOCK_FAILURE_PROBE
                closedBlockProbe=ClosedBlockFailureProbe.Begin(modify,state,tr);
#endif
#if CLOSED_SPLINE_FAILURE_PROBE
                closedSplineProbe=ClosedSplineFailureProbe.Begin(modify,state,resolved,tr);
#endif
#if HATCH_PATTERN_FAILURE_PROBE
                hatchProbe=HatchPatternFailureProbe.Begin(modify,state,resolved,tr);
#endif
#if BLOCK_TRANSFORM_FAILURE_PROBE
                blockProbe=BlockTransformFailureProbe.Begin(modify,state,resolved,tr);
#endif
#if BLOCK_SCALE_FAILURE_PROBE
                blockScaleProbe=BlockScaleFailureProbe.Begin(modify,state,resolved,tr);
#endif
#if BLOCK_ERASE_FAILURE_PROBE
                blockEraseProbe=BlockEraseFailureProbe.Begin(modify,state,resolved,tr);
#endif
#if BLOCK_COPY_FAILURE_PROBE
                blockCopyProbe=BlockCopyFailureProbe.Begin(modify,state,resolved,tr);
#endif
#if BLOCK_ROOT_PROPERTY_FAILURE_PROBE
                blockRootPropertyProbe=BlockRootPropertyFailureProbe.Begin(modify,state,resolved,tr);
#endif
#if TABLE_TEXT_FAILURE_PROBE
                tableProbe=TableTextFailureProbe.Begin(modify,state,resolved,tr);
#endif
#if MLEADER_TEXT_FAILURE_PROBE
                leaderProbe=MLeaderTextFailureProbe.Begin(modify,state,resolved,tr);
#endif
#if DIMENSION_TEXT_FAILURE_PROBE
                dimensionProbe=DimensionTextFailureProbe.Begin(modify,state,resolved,tr);
#endif
                job.CheckCancellation(); writeStarted = true;
                _log.Write(job.Operation, job.OriginalRequestId, "edit_begin", transaction: "write_open", operationId: job.OperationId);
                data = CadModifier.Apply(modify, resolved, tr, entities, job, nativeFormatWarnings);
                if (modify.Operations.Any(edit => CadPlaneCurveTransforms394.Frame(edit) != null))
                {
                    var planeChecks = CadPlaneCurveTransforms394.CaptureCommitted(modify, (ModifiedEntities)data, state.Document.Database, tr, job.CheckCancellation);
                    var precedingPlaneVerification = verifyAfterCommit;
                    verifyAfterCommit = () => { precedingPlaneVerification?.Invoke(); CadPlaneCurveTransforms394.VerifyCommitted(planeChecks, state.Document.Database, job.CheckCancellation); };
                }
                if (((ModifiedEntities)data).tolerance_changes is { Length: > 0 } toleranceChanges)
                { rollbackNotGuaranteed = true; var toleranceCheck = CadToleranceFrame356.CaptureCommitted(toleranceChanges, state.Document.Database, tr, job.CheckCancellation); var priorVerification = verifyAfterCommit; verifyAfterCommit = () => { priorVerification?.Invoke(); CadToleranceFrame356.VerifyCommitted(toleranceCheck, state.Document.Database, job.CheckCancellation); }; }
                documentationAdditions = ((ModifiedEntities)data).created.Select(reference => reference.handle).ToArray();
                documentationErasures = ((ModifiedEntities)data).erased.Select(reference => reference.handle).ToArray();
                if (rollbackNotGuaranteed) data = ((ModifiedEntities)data) with { undo_history = "not_guaranteed" };
#if CLOSED_BLOCK_FAILURE_PROBE
                closedBlockProbe?.Fail(tr);
#endif
#if CLOSED_SPLINE_FAILURE_PROBE
                closedSplineProbe?.Fail(tr);
#endif
#if HATCH_PATTERN_FAILURE_PROBE
                hatchProbe?.Fail(tr);
#endif
#if BLOCK_TRANSFORM_FAILURE_PROBE
                blockProbe?.Fail(tr);
#endif
#if BLOCK_SCALE_FAILURE_PROBE
                blockScaleProbe?.Fail(tr);
#endif
#if BLOCK_ERASE_FAILURE_PROBE
                blockEraseProbe?.Fail(tr);
#endif
#if BLOCK_COPY_FAILURE_PROBE
                blockCopyProbe?.Fail(tr);
#endif
#if BLOCK_ROOT_PROPERTY_FAILURE_PROBE
                blockRootPropertyProbe?.Fail(tr);
#endif
#if TABLE_TEXT_FAILURE_PROBE
                tableProbe?.Fail(tr);
#endif
#if MLEADER_TEXT_FAILURE_PROBE
                leaderProbe?.Fail(tr);
#endif
#if DIMENSION_TEXT_FAILURE_PROBE
                dimensionProbe?.Fail(tr);
#endif
            }
            else if (args is StructureWriteArguments structure)
            {
                if (referenceStages) Trace("reference_structure_resolve_before");
                var resolved = CadStructureWriter.Resolve(structure, state.Document.Database, tr, job.CheckCancellation, nativeFormatWarnings.Add);
                if (referenceStages) Trace("reference_structure_resolve_returned");
                var created = CadStructureWriter.Preview(structure, resolved, tr, state.Document.Database, structuralObjects, job.CheckCancellation, Trace);
#if CLOSED_BLOCK_FAILURE_PROBE
                closedBlockProbe=ClosedBlockFailureProbe.Begin(structure,state,tr);
#endif
#if LAYER_RENAME_FAILURE_PROBE
                layerProbe=LayerRenameFailureProbe.Begin(structure,state,tr);
#endif
                job.CheckCancellation(); writeStarted = true;
                _log.Write(job.Operation, job.OriginalRequestId, "structure_edit_begin", transaction: "write_open", operationId: job.OperationId);
                data = CadStructureWriter.Apply(structure, resolved, created, tr, state.Document.Database, job, Trace, nativeFormatWarnings);
                if (rollbackNotGuaranteed) data = ((ModifiedStructure)data) with { undo_history = "not_guaranteed" };
                if (resolved.ViewportClipping.Count > 0)
                    verifyAfterCommit = () => CadViewportClipping358.VerifyCommitted(resolved.ViewportClipping, state.Document.Database, job.CheckCancellation);
                if (resolved.NativeStyleLifecycle.Count > 0)
                    verifyAfterCommit = () => CadNativeStyleLifecycle352.VerifyCommitted(resolved.NativeStyleLifecycle, state.Document.Database, job.CheckCancellation);
                if (resolved.Viewports.Count > 0)
                    verifyAfterCommit = () => CadViewportWriter.VerifyCommitted(resolved.Viewports, created.Viewports, state.Document.Database, job.CheckCancellation);
                if (resolved.ViewportLifecycle.Count > 0)
                {
                    var lifecycleResult = (ModifiedStructure)data;
                    verifyAfterCommit = () => CadViewportLifecycleWriter.VerifyCommitted(resolved.ViewportLifecycle, created.ViewportLifecycle, lifecycleResult.changes, state.Document.Database, job.CheckCancellation);
                }
                if (resolved.NewViewports.Count > 0)
                {
                    var result = (ModifiedStructure)data;
                    data = result with { undo_history = "not_guaranteed" };
                    verifyAfterCommit = () => CadViewportCreator.VerifyCommitted(resolved.NewViewports, result.changes, state.Document.Database, job.CheckCancellation);
                }
                if (resolved.Blocks.Count > 0)
                {
                    var blockResult = (ModifiedStructure)data; var precedingVerification = verifyAfterCommit;
                    verifyAfterCommit = () =>
                    {
                        precedingVerification?.Invoke();
                        CadBlockWriter.VerifyCommitted(structure, resolved.Blocks, blockResult.changes, state.Document.Database, job.CheckCancellation);
                    };
                }
#if CLOSED_BLOCK_FAILURE_PROBE
                closedBlockProbe?.Fail(tr);
#endif
#if LAYER_RENAME_FAILURE_PROBE
                layerProbe?.Fail(tr);
#endif
            }
            else throw new DomainException("INVALID_ARGUMENT", "Unknown write payload.");
            // Prepare the result before Commit. It remains private until Commit
            // returns, then is journaled before any fallible cleanup/logging.
            var warnings = new List<string>();
            warnings.AddRange(nativeFormatWarnings);
            if (rollbackNotGuaranteed) warnings.Add(WriteRecovery.Warning);
            if (data is ModifiedEntities { mtext_format_changes: { } noteChanges })
                warnings.AddRange(noteChanges.SelectMany(change => change.warnings ?? Array.Empty<string>()).Distinct(StringComparer.Ordinal));
            if (verifyAfterCommit != null) warnings.Add(verificationPending);
            var committed = Result.Ok(job.OriginalRequestId, data, Context(state), warnings.Distinct(StringComparer.Ordinal).ToArray());
            job.BeginCommit(); commitAttempted = true;
            Trace("commit_begin"); tr.Commit();
            job.RecordCommit(committed);
            Trace("commit_recorded");
        }
        catch (DomainException e)
        {
            failure = e.Error;
            // A detected change to an original source is unresolved even
            // when no independently created body has reached resident append.
            if (e.Error.code is "NATIVE_FACE_PROFILE_SOURCE_CHANGED" or "NATIVE_PATH_SOURCE_CHANGED" or "BLOCK_DEFINITION_CHANGED" or "NATIVE_BLOCK_ATTRIBUTE_SOURCE_CHANGED" or "NATIVE_PLANE_TRANSFORM_SOURCE_CHANGED" or "NATIVE_POINT_SOURCE_CHANGED" or "NATIVE_CONSTRUCTION_CURVE_SOURCE_CHANGED" or "NATIVE_HATCH_SOURCE_CHANGED" or "NATIVE_LEGACY_POLYLINE_SOURCE_CHANGED" or "NATIVE_CURVE_SOURCE_CHANGED" or "NATIVE_CURVE_SOLID_SOURCE_CHANGED" or "NATIVE_SOLID_REPLACEMENT_SOURCE_CHANGED")
            { writeStarted = true; rollbackNotGuaranteed = true; }
        }
        catch (OperationCanceledException) { failure = new("CANCELLED", "Write cancelled before Commit."); }
        catch (Teigha.Runtime.Exception e) { failure = new("TRANSACTION_FAILED", "nanoCAD rejected the write operation.", false, new { api_status = e.ErrorStatus.ToString() }); Trace("native_failure_" + e.ErrorStatus); }
        catch (System.Exception e) { failure = new("API_EXCEPTION", "Unexpected write failure; see bridge diagnostics.", false, new { error_type = e.GetType().Name }); }
        finally
        {
            if (failure != null) Trace("failure_" + failure.code);
            Trace("transaction_dispose_begin");
            try { tr?.Dispose(); Trace("transaction_disposed"); }
            catch (System.Exception e) { cleanupFailure = e; Trace("transaction_dispose_failed_" + CleanupCode(e)); }
            foreach (var entity in entities)
                try { entity.Dispose(); } catch (System.Exception e) { cleanupFailure ??= e; }
            foreach (var item in structuralObjects)
            {
                // Fixed stages only: no handles, file paths or drawing content.
                string? releaseStage = referenceStages ? item.GetType() == typeof(RasterImageDef) ? "reference_owned_raster_definition_dispose" :
                    item.GetType() == typeof(RasterImage) ? "reference_owned_raster_image_dispose" : item.GetType() == typeof(BlockReference) ? "reference_owned_block_reference_dispose" : null : null;
                try { if (releaseStage != null) Trace(releaseStage + "_before"); item.Dispose(); if (releaseStage != null) Trace(releaseStage + "_returned"); }
                catch (System.Exception e) { cleanupFailure ??= e; }
            }
            Trace(cleanupFailure == null ? "cleanup_complete" : "cleanup_failed_" + CleanupCode(cleanupFailure));
        }
#if TABLE_TEXT_FAILURE_PROBE
        try{tableProbe?.VerifyRollback();}catch(System.Exception e){cleanupFailure??=e;}
#endif
#if MLEADER_TEXT_FAILURE_PROBE
        try{leaderProbe?.VerifyRollback();}catch(System.Exception e){cleanupFailure??=e;}
#endif
#if DIMENSION_TEXT_FAILURE_PROBE
        try{dimensionProbe?.VerifyRollback();}catch(System.Exception e){cleanupFailure??=e;}
#endif
#if BLOCK_TRANSFORM_FAILURE_PROBE
        try{blockProbe?.VerifyRollback();}catch(System.Exception e){cleanupFailure??=e;}
#endif
#if BLOCK_SCALE_FAILURE_PROBE
        try{blockScaleProbe?.VerifyRollback();}catch(System.Exception e){cleanupFailure??=e;}
#endif
#if BLOCK_ERASE_FAILURE_PROBE
        try{blockEraseProbe?.VerifyRollback();}catch(System.Exception e){cleanupFailure??=e;}
#endif
#if BLOCK_COPY_FAILURE_PROBE
        try{blockCopyProbe?.VerifyRollback();}catch(System.Exception e){cleanupFailure??=e;}
#endif
#if BLOCK_ROOT_PROPERTY_FAILURE_PROBE
        try{blockRootPropertyProbe?.VerifyRollback();}catch(System.Exception e){cleanupFailure??=e;}
#endif
#if CLOSED_BLOCK_FAILURE_PROBE
        try{closedBlockProbe?.VerifyRollback();}catch(System.Exception e){cleanupFailure??=e;}
#endif
#if CLOSED_SPLINE_FAILURE_PROBE
        try{closedSplineProbe?.VerifyRollback();}catch(System.Exception e){cleanupFailure??=e;}
#endif
#if HATCH_PATTERN_FAILURE_PROBE
        try{hatchProbe?.VerifyRollback();}catch(System.Exception e){cleanupFailure??=e;}
#endif
#if LAYER_RENAME_FAILURE_PROBE
        try{layerProbe?.VerifyRollback();}catch(System.Exception e){cleanupFailure??=e;}
#endif
        if (job.CommittedResult is { } success)
        {
            var warnings = success.warnings.ToList();
            if (cleanupFailure != null) warnings.Add("POST_COMMIT_CLEANUP_FAILED: Commit returned; independently inspect the returned handles.");
            else if (verifyAfterCommit != null)
            {
                try { verifyAfterCommit(); warnings.Remove(verificationPending); }
                catch (System.Exception error)
                {
                    warnings.Remove(verificationPending);
                    warnings.Add("POST_COMMIT_VERIFICATION_FAILED: Objects were committed, but their final state could not be verified. Do not create duplicates or assume rollback; inspect returned handles. " + (error is DomainException domain ? domain.Error.code : error.GetType().Name));
                    Trace("post_commit_verification_failed");
                }
            }
            return success with { warnings = warnings.ToArray() };
        }
        bool unknown = WriteRecovery.UnknownFailure(writeStarted, commitAttempted, cleanupFailure != null, rollbackNotGuaranteed);
        return job.Error(unknown ? "WRITE_OUTCOME_UNKNOWN" : failure?.code ?? "API_EXCEPTION",
            unknown ? "Final state is not proven. Rollback may be unavailable and partial changes may remain. Do not retry with a new operation_id." : failure?.message ?? "Write did not complete.",
            unknown ? "unknown" : writeStarted ? "rolled_back" : "not_started", context,
            new { error = failure, cleanup_error = cleanupFailure?.GetType().Name, rollback_guaranteed = !rollbackNotGuaranteed });
    }
}
