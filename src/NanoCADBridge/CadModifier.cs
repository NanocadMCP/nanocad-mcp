using System.Globalization;
using NanoCAD.Contracts;
using NanoCAD.Ipc;
using Teigha.DatabaseServices;
using Teigha.Geometry;

namespace NanoCADBridge;

internal static class CadModifier
{
    internal sealed record PropertyIds(ObjectId Layer, ObjectId Linetype);
    internal sealed record Resolved(ObjectId Model, Dictionary<string, ObjectId> Sources, Dictionary<int, PropertyIds> Properties,
        Dictionary<string, CadBlockMover.Resolved> Blocks, HashSet<string> DocumentedSolids, HashSet<string> PointClouds)
    {
        internal CadLegacyPolyline408.Plan? LegacyPolyline { get; init; }
        internal CadSolidReplacement409.Plan? SolidReplacement { get; init; }
    }

    public static Resolved Resolve(ModifyArguments args, Database db, Transaction tr, Action cancel)
    {
        WriteSpace.Check(args);
        var model = CadWriteSpace.Resolve(db, tr, args.TargetSpace);
        var layers = (LayerTable)tr.GetObject(db.LayerTableId, OpenMode.ForRead);
        var linetypes = (LinetypeTable)tr.GetObject(db.LinetypeTableId, OpenMode.ForRead);
        var sources = new Dictionary<string, ObjectId>(StringComparer.Ordinal);
        var curveBytes = new CadContourBudget344(cancel); var blockBytes = new SerializedCapacity357(cancel: cancel);
        var blockSources = new Dictionary<string, CadBlockMover.Resolved>(StringComparer.Ordinal);
        var solids = new HashSet<string>(StringComparer.Ordinal);
        var documentedSolids = new HashSet<string>(StringComparer.Ordinal);
        var pointClouds = new HashSet<string>(StringComparer.Ordinal);
        var legacyStates = new Dictionary<string, CadLegacyPolyline408.State>(StringComparer.Ordinal);
        foreach (var handle in args.Operations.SelectMany(EditReferences.All).Distinct(StringComparer.Ordinal))
        {
            cancel();
            ulong bits = ulong.Parse(handle, NumberStyles.HexNumber, CultureInfo.InvariantCulture);
            if (!db.TryGetObjectId(new Handle(unchecked((long)bits)), out var id) || id.IsNull || !id.IsValid)
                throw new DomainException("OBJECT_NOT_FOUND", "Source handle was not found in this document.", false, new { handle });
            if (id.IsErased || id.IsEffectivelyErased) throw new DomainException("OBJECT_ERASED", "Source entity has been erased.", false, new { handle });
            if (tr.GetObject(id, OpenMode.ForRead) is not Entity entity || entity.OwnerId != model)
                throw new DomainException("OUT_OF_SCOPE", "Only top-level entities in the requested active space can be edited.", false, new { handle });
            using var nativeBinding = CadNativeDimensionBinding357.Bind(entity); entity = nativeBinding.Value;
            if (PointCloudDiscovery.IsInsertion(id))
            {
                if (args.TargetSpace != "model" || !args.Operations.Any(op => op.Handles.Contains(handle)) ||
                    args.Operations.Where(op => op.Handles.Contains(handle)).Any(op => !CadPointCloudPlacement.Placement(op)))
                    throw new DomainException("UNSUPPORTED_POINT_CLOUD_EDIT", "Point cloud insertions support move, rotate and transform_point_cloud placement only; point data and definitions are not edited.");
                CheckLayer((LayerTableRecord)tr.GetObject(entity.LayerId, OpenMode.ForRead));
                CadPointCloudPlacement.CheckSource(entity, tr, cancel);
                foreach (var edit in args.Operations.Where(op => op.Handles.Contains(handle))) _ = CadPointCloudPlacement.Matrix(edit);
                pointClouds.Add(handle); sources.Add(handle, id); continue;
            }
            if (args.Operations.Any(op => op is PointCloudPlacementSpec && op.Handles.Contains(handle)))
                throw new DomainException("UNSUPPORTED_ENTITY_TYPE", "transform_point_cloud requires a native PointCloud insertion.");
            var sourceOperations = args.Operations.Where(op => op.Handles.Contains(handle)).ToArray();
            if (args.Operations.OfType<SolidReplacement409>().Any(op => op.SourceHandle == handle))
            {
                if (entity.GetType() != typeof(Solid3d)) throw new DomainException("UNSUPPORTED_ENTITY_TYPE", "Replacement input must be an exact native Solid3d.");
                CadSolidEditor.CheckSource((Solid3d)entity, db, cancel); sources.Add(handle, id); continue;
            }
            if (CadConstructionCurve443.Is(entity))
            {
                CheckLayer((LayerTableRecord)tr.GetObject(entity.LayerId, OpenMode.ForRead), allowHidden: sourceOperations.All(op => op is EraseSpec or PropertiesSpec));
                CadConstructionCurve443.CheckSource(entity, sourceOperations, db, tr, cancel); sources.Add(handle, id); continue;
            }
            if (entity.GetType() == typeof(DBPoint))
            {
                CheckLayer((LayerTableRecord)tr.GetObject(entity.LayerId, OpenMode.ForRead), allowHidden: sourceOperations.All(op => op is EraseSpec or PropertiesSpec));
                CadPointEditor408.CheckSource(entity, sourceOperations, db, tr, cancel);
                sources.Add(handle, id); continue;
            }
            if (sourceOperations.Any(op => op is PointPositionEdit408))
                throw new DomainException("UNSUPPORTED_ENTITY_TYPE", "set_point_position requires an exact independent native Point.");
            if (entity.GetType() == typeof(Wipeout))
            {
                CheckLayer((LayerTableRecord)tr.GetObject(entity.LayerId, OpenMode.ForRead), allowHidden: sourceOperations.All(op => op is EraseSpec or PropertiesSpec));
                CadWipeout431.CheckSource(entity, sourceOperations, db, tr, cancel); sources.Add(handle, id); continue;
            }
            if (sourceOperations.Any(op => op is WipeoutBoundaryEdit431)) throw new DomainException("UNSUPPORTED_ENTITY_TYPE", "set_wipeout_boundary requires an exact native Wipeout.");
            if (CadLegacyPolyline408.IsLegacy(entity))
            {
                CheckLayer((LayerTableRecord)tr.GetObject(entity.LayerId, OpenMode.ForRead), allowHidden: sourceOperations.All(op => op is EraseSpec or PropertiesSpec));
                var observed = CadLegacyPolyline408.CheckSource((Polyline2d)entity, sourceOperations, db, tr, cancel);
                curveBytes.Add(CadLegacyPolyline408.Geometry(observed.Geometry)); legacyStates.Add(handle, observed);
                sources.Add(handle, id); continue;
            }
            if (sourceOperations.Any(op => op is LegacyPolylineEdit408))
                throw new DomainException("UNSUPPORTED_ENTITY_TYPE", "set_legacy_polyline requires an exact ordinary native Polyline2d.");
            var planeTransformFrame = CadPlaneCurveTransforms394.Frame(args.Operations, handle);
            if (planeTransformFrame != null) CadPlaneCurveTransforms394.CheckSource(entity, planeTransformFrame, db, tr, cancel);
            bool nativeBlock = entity.GetType() == typeof(BlockReference) || entity.GetType() == typeof(MInsertBlock);
            bool basicBlock = nativeBlock && args.Operations.Where(op => op.Handles.Contains(handle)).All(op => op is EraseSpec or PropertiesSpec);
            bool splinePlacement = entity.GetType() == typeof(Spline) && NativeSplinePlacement407.OnlyPlacement(sourceOperations);
            if (sourceOperations.Any(CopyTransforms2d407.Handles) && !Supported(entity.GetType()) && !nativeBlock)
                throw new DomainException("UNSUPPORTED_ENTITY_TYPE", "Grouped transformed copies accept ordinary planar curves, TEXT/MTEXT and static native BlockReference/MInsert. Other owning families use their specialized copy operations.");
            if (sourceOperations.Any(CopyTransforms2d407.Handles) && !nativeBlock)
            {
                CadIndependentCurve.Require(entity, db, tr, cancel);
                if (entity.Annotative == AnnotativeStates.True)
                    throw new DomainException("UNSUPPORTED_COPY_DEPENDENCIES", "Annotative contexts require their owning copy route.");
                if (!entity.ExtensionDictionary.IsNull)
                {
                    var dictionary = tr.GetObject(entity.ExtensionDictionary, OpenMode.ForRead) as DBDictionary;
                    if (dictionary == null || dictionary.Count != 0 || dictionary.HasFields)
                        throw new DomainException("UNSUPPORTED_COPY_DEPENDENCIES", "Copies of objects with owned extension records require a resident deep-clone route; detached copies cannot certify the owned data.");
                }
            }
            bool nativeOffsetOnly = args.Operations.Where(op => op.Handles.Contains(handle)).All(op => op is NativeOffsetCurveSpec341) && args.Operations.Any(op => op.Handles.Contains(handle));
            bool nativeFinishingOnly = args.Operations.Count() == 1 && args.Operations[0] is NativeContourFinishingSpec342 && args.Operations[0].Handles.Contains(handle);
            bool nativeCornerOnly = args.Operations.Length == 1 && args.Operations[0] is NativeCornerCurveSpec343 && args.Operations[0].Handles.Contains(handle);
            bool nativeStretchOnly = args.Operations.Length == 1 && (args.Operations[0] is NativeStretchEdit345 or NativeWindowStretchEdit387) && args.Operations[0].Handles.Contains(handle);
            if (args.TargetSpace == "paper" && entity is Solid3d)
                throw new DomainException("OUT_OF_SCOPE", "Native3D bodies belong to Model; Paper supports ordinary2D entities and owned block occurrences.");
            if (args.TargetSpace == "paper" && nativeBlock && !basicBlock)
                CadBlockSpace.RequireWriteSpace(db, tr, args.TargetSpace, cancel);
            if (!args.Operations.Any(op => op.Handles.Contains(handle)))
            {
                // Cutting boundaries are read-only references, including on locked layers.
                if (args.Operations.OfType<SolidBooleanEditSpec>().Any(op => op.ToolHandles.Contains(handle))) CadSolidBooleanEditor.CheckTool(entity, cancel);
                else if (args.Operations.OfType<RegionBooleanEditSpec>().Any(op => op.ToolHandles.Contains(handle))) CadRegionBooleanEditor.CheckTool(entity, cancel);
                else if (args.Operations[0] is NativeContourFinishingSpec342) { CadNativeContourFinishing342.CheckBoundary(entity, cancel); sources.Add(handle, id); continue; }
                else if (args.Operations[0] is SolidFaceEditSpec { PathHandle: not null }) { CadSolidFacePath389.CheckPath(entity, db, model, cancel); sources.Add(handle, id); continue; }
                else CadContourEditor.Capture(entity, cancel);
                if (entity is Curve) curveBytes.Add(CadContourBudget344.Geometry(entity, cancel));
                sources.Add(handle, id); continue;
            }
            if (args.Operations.Any(op => op is Mirror2dSpec && op.Handles.Contains(handle)))
            {
                if (nativeBlock) { /* The owning block route below preserves native occurrences and attributes. */ }
                else if (CadAnnotationMirrorEditor340.Supported(entity.GetType())) CadAnnotationMirrorEditor340.CheckSource(entity, tr, cancel);
                else
                {
                    if (entity is not Curve) throw new DomainException("UNSUPPORTED_ENTITY_TYPE", "Mirror accepts independent planar curves and supported native annotations; body mirror uses transform_solid.");
                    CadIndependentCurve.Require(entity, db, tr, cancel);
                }
            }
            foreach (var contour in args.Operations.Where(op => CadContourEditor.Handles(op) && op.Handles.Contains(handle)))
                CadContourEditor.CheckSource(entity, contour, cancel, tr);
            var contourAssembly = args.Operations.OfType<ContourAssemblySpec>().SingleOrDefault(op => op.Handles.Contains(handle));
            if (contourAssembly != null)
            { CadIndependentCurve.Require(entity, db, tr, cancel); if (contourAssembly.EraseSources) CadContourBudget344.Replacement(entity, tr, cancel); CadContourAssemblyEditor.Capture(entity, cancel, contourAssembly.Workplane); }
            // Reject custom subclasses/proxies even if they inherit a supported class.
            bool leaderInstanceOnly = args.Operations.Length == 1 && args.Operations[0] is SetMLeaderInstance442 && args.Operations[0].Handles.Contains(handle);
            bool leaderTopologyOnly = args.Operations.Length == 1 && args.Operations[0] is MLeaderTopologyEdit431 && args.Operations[0].Handles.Contains(handle);
            bool leaderGeometryOnly = args.Operations.Length == 1 && args.Operations[0] is MLeaderGeometryEdit388 && args.Operations[0].Handles.Contains(handle);
            bool leaderOnly = entity.GetType() == typeof(MLeader) && args.Operations.Where(op => op.Handles.Contains(handle)).All(op => op is ReplaceMLeaderTextSpec);
            if (args.Operations.Any(op => op is ReplaceMLeaderTextSpec && op.Handles.Contains(handle)) && !leaderOnly)
                throw new DomainException("UNSUPPORTED_MLEADER_CONTENT", "MLeader text targets require only dedicated text edits on the exact native leader.");
            bool tableOnly = entity.GetType() == typeof(Table) && args.Operations.Where(op => op.Handles.Contains(handle)).All(op => op is ReplaceTableTextSpec or ResizeTableSpec or TableEditingSpec or TableContentsEditing364 or TableValueEditingSpec or TableFormulaEditingSpec343 or TableCellLayoutEdit340 or TableGridLayoutEdit341);
            bool mtextFormatOnly = entity.GetType() == typeof(MText) && args.Operations.Where(op => op.Handles.Contains(handle)).All(op => op is SetMTextFormatSpec);
            if (args.Operations.Any(op => op is SetMTextFormatSpec && op.Handles.Contains(handle)) && !mtextFormatOnly)
                throw new DomainException("UNSUPPORTED_MTEXT_CONTENT", "MText format changes require an exact native MText and one dedicated token-bound edit per target.");
            bool toleranceOnly = entity.GetType() == typeof(FeatureControlFrame) && args.Operations.Where(op => op.Handles.Contains(handle)).All(op => op is ToleranceEdit356);
            if (args.Operations.Any(op => op is ToleranceEdit356 && op.Handles.Contains(handle)) && !toleranceOnly) throw new DomainException("UNSUPPORTED_TOLERANCE_CONTENT", "set_tolerance requires its dedicated native frame target.");
            bool dimensionOnly = CadDimensionTextEditor.Supported(entity.GetType()) && args.Operations.Where(op => op.Handles.Contains(handle)).All(op => op is ReplaceDimensionTextSpec or SetDimensionLineSpec or SetDimensionTextPositionSpec);
            bool dimensionEditingOnly = DimensionGeometryReader.Supported(entity.GetType()) && args.Operations.Where(op => op.Handles.Contains(handle)).All(op => op is DimensionDisplayEdit341 or DimensionDefinitionEdit341);
            if (args.Operations.Any(op => op.Handles.Contains(handle) && (op is DimensionDisplayEdit341 or DimensionDefinitionEdit341)) && !dimensionEditingOnly)
                throw new DomainException("UNSUPPORTED_DIMENSION_CONTENT", "Dimension display/definition changes require their dedicated operation on an exact ordinary dimension.");
            bool blockOnly = nativeBlock && sourceOperations.All(op => op is MoveSpec or RotateSpec or ScaleSpec or EraseSpec or CopySpec or PropertiesSpec or Mirror2dSpec or CopyTransform2d407 or PolarRepeat2d407);
            bool hatchOnly = entity.GetType() == typeof(Hatch) && args.Operations.Where(op => op.Handles.Contains(handle)).All(op => op is SetHatchPatternSpec);
            bool hatchPropertiesOnly = entity.GetType() == typeof(Hatch) && args.Operations.Where(op => op.Handles.Contains(handle)).All(op => op is SetHatchPropertiesSpec);
            bool regionBooleanOnly = entity.GetType() == typeof(Region) && args.Operations.Where(op => op.Handles.Contains(handle)).All(op => op is RegionBooleanEditSpec);
            if (args.Operations.Any(op => op is RegionBooleanEditSpec && op.Handles.Contains(handle)) && !regionBooleanOnly)
                throw new DomainException("UNSUPPORTED_ENTITY_TYPE", "boolean_region requires an exact native Region and its dedicated operation.");
            if (args.Operations.Any(op => op is SetHatchPropertiesSpec && op.Handles.Contains(handle)) && !hatchPropertiesOnly)
                throw new DomainException("UNSUPPORTED_HATCH_CONTENT", "Stored hatch property edits require their dedicated operation on an exact native Hatch.");
            bool basicOnly = BasicTarget(entity.GetType()) && args.Operations.Where(op => op.Handles.Contains(handle)).All(op => op is EraseSpec or PropertiesSpec);
            bool formatOnly = DimensionGeometryReader.Supported(entity.GetType()) && args.Operations.Where(op => op.Handles.Contains(handle)).All(op => op is DimensionFormatSpec);
            bool annotationPlacement = CadAnnotationTransformEditor.Supported(entity.GetType()) &&
                args.Operations.Where(op => op.Handles.Contains(handle)).All(op => CadAnnotationTransformEditor.Placement(op) || op is Mirror2dSpec or EraseSpec or PropertiesSpec) &&
                args.Operations.Any(op => op.Handles.Contains(handle) && (CadAnnotationTransformEditor.Placement(op) || op is Mirror2dSpec));
            if (args.Operations.Any(op => op is DimensionFormatSpec && op.Handles.Contains(handle)) && !formatOnly)
                throw new DomainException("UNSUPPORTED_DIMENSION_CONTENT", "Dimension formatting uses only format operations on an ordinary native dimension target.");
            bool solidOnly = entity.GetType() == typeof(Solid3d) && args.Operations.Where(op => op.Handles.Contains(handle)).All(op => op is TransformSolidSpec or SliceSolidSpec or SeparateSolidSpec or SolidFinishingEditSpec or SolidOffsetEdit353 or SolidBooleanEditSpec or SolidFaceEditSpec or SolidDrillEditSpec or SolidReplacement409 or MoveSpec or RotateSpec or ScaleSpec or CopySpec or EraseSpec or PropertiesSpec);
            if (args.Operations.Any(op => (op is TransformSolidSpec or SliceSolidSpec or SeparateSolidSpec or SolidFinishingEditSpec or SolidOffsetEdit353 or SolidBooleanEditSpec or SolidFaceEditSpec or SolidDrillEditSpec or SolidReplacement409) && op.Handles.Contains(handle)) && !solidOnly)
                throw new DomainException("UNSUPPORTED_ENTITY_TYPE", "Direct solid edits require an exact native Solid3d and compatible operations.");
            if (args.Operations.Any(op => (op is PolylineVerticesSpec || CadPolylineTopologyEditor.Handles(op)) && op.Handles.Contains(handle)) && entity.GetType() != typeof(Polyline))
                throw new DomainException("UNSUPPORTED_ENTITY_TYPE", "Vertex edits require an exact native Polyline.");
            var curveFrame = CadExplicitCurveGeometry388.Frame(args.Operations, handle);
            var polylineFrame = PolylineEditFrames431.Frame(args.Operations, handle);
            if (polylineFrame != null) { CadPolylineWorkplane431.Check(entity, polylineFrame, cancel); CadIndependentCurve.Require(entity, db, tr, cancel); }
            if (curveFrame != null) CadIndependentCurve.Require(entity, db, tr, cancel);
            if (curveFrame == null && args.Operations.Any(op => op is CurveGeometrySpec && op.Handles.Contains(handle)) && (entity.GetType() == typeof(Line) || entity.GetType() == typeof(Circle)))
                CadDimensionAssociation385.PreflightFollowers(entity, db, tr, cancel);
            if (args.Operations.Any(op => op is CurveGeometrySpec && op.Handles.Contains(handle)) && entity.GetType() != typeof(Line) && entity.GetType() != typeof(Circle) && entity.GetType() != typeof(Arc) && entity.GetType() != typeof(Ellipse) && entity.GetType() != typeof(Spline))
                throw new DomainException("UNSUPPORTED_ENTITY_TYPE", "Geometry replacement requires an exact native Line, Circle, Arc, Ellipse or Spline.");
            if (args.Operations.Any(op => op.Handles.Contains(handle) && (CadPolylineTopologyEditor.Handles(op) || op is CurveGeometrySpec && (entity.GetType() == typeof(Arc) || entity.GetType() == typeof(Ellipse) || entity.GetType() == typeof(Spline)) || op is PolylineVerticesSpec p && PolylineEditFrames431.ChangesSegments(p))))
                CadIndependentCurve.Require(entity, db, tr, cancel);
            if (args.Operations.Any(op => op is SetHatchPatternSpec && op.Handles.Contains(handle)) && !hatchOnly)
                throw new DomainException("UNSUPPORTED_HATCH_CONTENT", "Pattern edits require only dedicated operations on an exact native Hatch.");
            if (args.Operations.Any(op => (op is ReplaceDimensionTextSpec or SetDimensionLineSpec or SetDimensionTextPositionSpec) && op.Handles.Contains(handle)) && !dimensionOnly)
                throw new DomainException("UNSUPPORTED_DIMENSION_CONTENT", "Dimension text targets cannot also be transformed, erased or edited as another entity family.");
            if (args.Operations.Any(op => (op is ReplaceTableTextSpec or ResizeTableSpec or TableEditingSpec or TableContentsEditing364 or TableValueEditingSpec or TableFormulaEditingSpec343 or TableCellLayoutEdit340 or TableGridLayoutEdit341) && op.Handles.Contains(handle)) && entity.GetType() != typeof(Table))
                throw new DomainException("UNSUPPORTED_TABLE_CONTENT", "Table edits require an exact native Table source.");
            if (!Supported(entity.GetType()) && !leaderInstanceOnly && !leaderTopologyOnly && !leaderGeometryOnly && !leaderOnly && !tableOnly && !dimensionOnly && !dimensionEditingOnly && !toleranceOnly && !blockOnly && !hatchOnly && !hatchPropertiesOnly && !basicOnly && !solidOnly && !formatOnly && !annotationPlacement && !regionBooleanOnly) throw new DomainException("UNSUPPORTED_ENTITY_TYPE", "Native curves and text support ordinary transforms. Independent native Table, MLeader, seven dimension kinds, Hatch, Region and Helix support move/rotate/uniform scale/copy and basic edits; annotation content and associative sources require their owning adapters.", false, new { handle });
            CheckLayer((LayerTableRecord)tr.GetObject(entity.LayerId, OpenMode.ForRead), allowHidden: basicOnly || formatOnly || dimensionEditingOnly || hatchPropertiesOnly || mtextFormatOnly);
            if (blockOnly && !basicOnly)
            {
                if (args.Operations.Any(op => op is PropertiesSpec && op.Handles.Contains(handle)) &&
                    args.Operations.Any(op => op is not PropertiesSpec && op.Handles.Contains(handle)))
                    throw new DomainException("UNSUPPORTED_BLOCK_CONTENT", "Block root property edits require only set_properties operations on that source in the same batch.");
                var block = CadBlockMover.Resolve((BlockReference)entity, db, tr, model, cancel); blockSources.Add(handle, block);
                foreach (var observed in block.Graph!.States) { cancel(); blockBytes.Add(observed.Value); }
                foreach (var attributeId in block.Attributes) { cancel(); blockBytes.Add(CadBlockMover.AttributeSnapshot((AttributeReference)tr.GetObject(attributeId, OpenMode.ForRead), cancel)); }
                if (sourceOperations.Any(IsCopy)) CadBlockCopier.CheckSource((BlockReference)entity, block, tr, cancel);
            }
            else if (solidOnly)
            {

                solids.Add(handle);
                if (CadSolidEditor.CheckSource((Solid3d)entity, db, cancel)) documentedSolids.Add(handle);
            }
            else if (leaderInstanceOnly) CadMLeaderInstance442.CheckSource(entity, (SetMLeaderInstance442)args.Operations[0], tr, cancel);
            else if (leaderTopologyOnly) CadMLeaderTopology431.CheckSource(entity, (MLeaderTopologyEdit431)args.Operations[0], tr, cancel);
            else if (leaderGeometryOnly) CadMLeaderGeometry388.CheckSource(entity, (MLeaderGeometryEdit388)args.Operations[0], tr, cancel);
            else if (leaderOnly) CadMLeaderTextEditor.Capture((MLeader)entity, cancel);
            else if (annotationPlacement) CadAnnotationTransformEditor.CheckSource(entity, tr, cancel);
            else if (regionBooleanOnly) CadRegionBooleanEditor.CheckSource((Region)entity, db, tr, cancel);
            else if (mtextFormatOnly)
            {
                var edit = args.Operations.OfType<SetMTextFormatSpec>().Single(op => op.Handles.Contains(handle));
                CadMTextFormatEditor.CheckSource((MText)entity, edit, db, tr, cancel);
            }
            else if (basicOnly) { /* Display properties and native erase do not require shape/content editing preflight. */ }
            else if (nativeOffsetOnly)
                CadNativeContourOffset341.CheckSource(entity, args.Operations.OfType<NativeOffsetCurveSpec341>().Single(), db, tr, cancel);
            else if (nativeFinishingOnly)
                CadNativeContourFinishing342.CheckSource(entity, (NativeContourFinishingSpec342)args.Operations[0], db, tr, cancel);
            else if (nativeStretchOnly)
            {
                if (args.Operations[0] is NativeWindowStretchEdit387 windowStretchEdit) CadNativeWindowStretch387.CheckSource(entity, handle, windowStretchEdit, db, tr, cancel);
                else CadNativeStretch345.CheckSource(entity, (NativeStretchEdit345)args.Operations[0], db, tr, cancel);
            }
            else if (nativeCornerOnly)
                CadIndependentContourCorners343.CheckSource(entity, (NativeCornerCurveSpec343)args.Operations[0], db, tr, cancel);
            else if (toleranceOnly) CadToleranceFrame356.Resolve((FeatureControlFrame)entity, args.Operations.OfType<ToleranceEdit356>().Single(op => op.Handles.Contains(handle)), db, tr, cancel);
            else if (dimensionEditingOnly)
                CadDimensionEditing341.Resolve(entity, args.Operations.Single(op => op.Handles.Contains(handle)), db, tr, cancel);
            else if (formatOnly) CadDimensionFormatEditor.CheckSource(entity);
            else if (hatchOnly) CadHatchPatternEditor.Capture((Hatch)entity, cancel);
            else if (hatchPropertiesOnly)
            {
                var edit = args.Operations.OfType<SetHatchPropertiesSpec>().Single(op => op.Handles.Contains(handle));
                var state = CadHatchPropertiesEditor.CheckSource((Hatch)entity, edit, db, tr, cancel);
                if (args.Operations.SelectMany(op => op.Handles).Any(state.Stored.SourceHandles.Contains))
                    throw new DomainException("INVALID_OPERATION_ORDER", "An associative Hatch boundary cannot be mutated in the same request as its token-bound property edit; edit the boundary first, then refresh hatch_token.");
            }
            else if (tableOnly)
            {
                if (args.Operations.OfType<TableFormulaEditingSpec343>().SingleOrDefault() is { } formula)
                    CadTableFormulaEditor343.CheckSource((Table)entity, formula, db, tr, cancel);
                else if (args.Operations.OfType<TableGridLayoutEdit341>().SingleOrDefault() is { } grid)
                    CadTableGridLayoutEditor341.CheckSource((Table)entity, grid, db, tr, cancel);
                else if (args.Operations.OfType<TableCellLayoutEdit340>().SingleOrDefault() is { } layout)
                    CadTableCellLayoutEditor340.CheckSource((Table)entity, layout, db, tr, cancel);
                else if (args.Operations.OfType<TableContentsEditing364>().SingleOrDefault() is { } contents)
                    CadTableContentsEditor364.CheckSource((Table)entity, contents, db, tr, cancel);
                else if (args.Operations.OfType<TableValueEditingSpec>().SingleOrDefault() is { } values)
                    CadTableValueEditor336.CheckSource((Table)entity, values, db, tr, cancel);
                else if (args.Operations.OfType<TableEditingSpec>().SingleOrDefault() is { } lifecycle)
                    CadTableLifecycleEditor.CheckSource((Table)entity, lifecycle, db, tr, cancel);
                else if (args.Operations.Where(op => op.Handles.Contains(handle)).All(op => op is ResizeTableSpec))
                {
                    if (CadTableIndexedSize371.UsesIndexed((Table)entity, cancel)) CadTableIndexedSize371.Capture((Table)entity, db, tr, cancel);
                    else CadTableSizeEditor.Capture((Table)entity, cancel);
                }
                else CadTableTextEditor.Capture((Table)entity, cancel);
                if (args.Operations.Any(op => op is ResizeTableSpec && op.Handles.Contains(handle)) && !CadTableIndexedSize371.UsesIndexed((Table)entity, cancel))
                    CadTableSizeEditor.CheckSource((Table)entity, cancel);
            }
            else if (dimensionOnly)
            {
                if (args.Operations.Any(op => op is SetDimensionTextPositionSpec && op.Handles.Contains(handle)))
                {
                    if (args.Operations.Any(op => op is not SetDimensionTextPositionSpec && op.Handles.Contains(handle)))
                        throw new DomainException("INVALID_OPERATION_ORDER", "Explicit label placement cannot share its target with other dimension edits in the same batch.");
                    CadDimensionTextPositionEditor.CheckSource(entity);
                }
                else CadDimensionTextEditor.CheckSource(entity);
                if (args.Operations.Any(op => op is SetDimensionLineSpec && op.Handles.Contains(handle))) CadDimensionLineEditor.CheckSource(entity);
            }
            else if (planeTransformFrame != null) CadPlaneCurveTransforms394.Check(entity, planeTransformFrame, cancel);
            else if (polylineFrame != null) CadPolylineWorkplane431.Check(entity, polylineFrame, cancel);
            else if (splinePlacement) NativeSplinePlacement407.CheckSource(entity, sourceOperations, db, tr, cancel);
            else if (contourAssembly == null) CheckGeometry(entity, cancel, args.Operations.Where(op => op.Handles.Contains(handle)).All(op => op is Mirror2dSpec), curveFrame);
            if (args.Operations.Any(op => op is ReplaceTextSpec && op.Handles.Contains(handle))) CheckTextSource(entity);
            if (args.Operations.Any(op => op is ReplaceMTextSpec && op.Handles.Contains(handle))) CadMTextEditor.CheckSource(entity);
            // Dedicated native routes have their own complete source ledgers.
            // Other curve previews retain their existing geometry admission,
            // while the aggregate bound measures actual snapshots, not vertices.
            if (!basicOnly && !nativeOffsetOnly && !nativeFinishingOnly && !nativeStretchOnly && entity is Curve)
                curveBytes.Add(splinePlacement ? NativeSplinePlacement407.CheckGeometry(entity, cancel) : CadContourBudget344.Geometry(entity, cancel));
            foreach (var op in args.Operations.Where(o => o.Handles.Contains(handle)))
            {
                if (op is DimensionFormatSpec format) CadDimensionFormatEditor.Resolve(entity, format, db, tr);
                if (op is ReplaceMLeaderTextSpec ml && entity is MLeader leader) { using var text = leader.MText; FontCoverage.Require(tr, text.TextStyleId, ml.Text); }
                if (op is ReplaceTableTextSpec tb && entity is Table table) foreach (var cell in tb.Cells) FontCoverage.Require(tr, table.GetTextStyleId(cell.Row, cell.Column, 0), cell.Text);
                if (op is ReplaceMTextSpec rm && entity is MText mt) CadMTextEditor.CheckFont(mt, rm, tr);
                if (op is ReplaceTextSpec rt && entity is DBText dt) FontCoverage.Require(tr, dt.TextStyleId, rt.Text);
                if (op is ReplaceDimensionTextSpec rd && entity is Dimension dim) FontCoverage.Require(tr, FontCoverage.DimensionStyle(dim), rd.Text);
            }
            sources.Add(handle, id);
        }

        int blockCopies = 0, copyAttributes = 0, copyVertices = 0;
        foreach (var handle in args.Operations.OfType<CopySpec>().SelectMany(op => op.Handles))
            if (blockSources.TryGetValue(handle, out var block))
            { blockCopies++; copyAttributes += block.Attributes.Length; copyVertices += block.Definition.PreviewVertices; BlockMoveGeometry.Budget(blockCopies, copyAttributes, copyVertices); }
        foreach (var facePath in args.Operations.OfType<SolidFaceEditSpec>().Where(edit => edit.PathHandle != null))
        {
            var body = (Solid3d)tr.GetObject(sources[facePath.Handles[0]], OpenMode.ForRead);
            CadSolidFacePath389.Preflight(facePath, body, new(args.BridgeId, args.DocumentId, facePath.Handles[0]),
                (Entity)tr.GetObject(sources[facePath.PathHandle!], OpenMode.ForRead), db, model, cancel);
        }
        var properties = new Dictionary<int, PropertyIds>();
        for (int i = 0; i < args.Operations.Length; i++)
        {
            cancel();
            if (args.Operations[i] is not PropertiesSpec edit) continue;
            ObjectId layer = ObjectId.Null, linetype = ObjectId.Null;
            if (edit.Properties.Layer is { } name)
            {
                if (!layers.Has(name)) throw new DomainException("LAYER_UNAVAILABLE", "Target layer does not exist.", false, new { operation_index = i, layer = name });
                layer = layers[name]; CheckLayer((LayerTableRecord)tr.GetObject(layer, OpenMode.ForRead), allowHidden: true);
            }
            if (edit.Properties.Linetype is { } pattern)
            {
                if (!linetypes.Has(pattern)) throw new DomainException("LINETYPE_UNAVAILABLE", "Linetype must already exist.", false, new { operation_index = i, linetype = pattern });
                linetype = linetypes[pattern];
                if (((LinetypeTableRecord)tr.GetObject(linetype, OpenMode.ForRead)).IsDependent)
                    throw new DomainException("LINETYPE_UNAVAILABLE", "Dependent linetypes cannot be assigned.");
            }
            properties.Add(i, new(layer, linetype));
        }
        if (documentedSolids.Count > 0 && args.Operations.Any(op => op is CopySpec or EraseSpec && op.Handles.Any(documentedSolids.Contains)))
            throw new DomainException("UNSUPPORTED_SOLID_DEPENDENCIES", "A documented-body edit cannot copy or delete sources implicitly. Separate those ownership changes from in-place machining.");
        var resolved = new Resolved(model, sources, properties, blockSources, documentedSolids, pointClouds);
        if (args.Operations.OfType<SolidReplacement409>().SingleOrDefault() is { } replacement)
            resolved = resolved with { SolidReplacement = CadSolidReplacement409.Prepare(replacement, db, tr, sources[replacement.Handles[0]], sources[replacement.SourceHandle], cancel) };
        return legacyStates.Count == 0 ? resolved : resolved with { LegacyPolyline = CadLegacyPolyline408.Prepare(args, resolved, tr, cancel, legacyStates) };
    }
    private static bool Supported(Type type) => type == typeof(Line) || type == typeof(Circle) || type == typeof(Arc) ||
        type == typeof(Ellipse) || type == typeof(Spline) || type == typeof(Polyline) || type == typeof(DBText) || type == typeof(MText);
    private static bool IsCopy(EditSpec edit) => edit is CopySpec or Mirror2dSpec { Copy: true } or CopyTransform2d407 or PolarRepeat2d407;
    private static bool BasicTarget(Type type) => Supported(type) || type == typeof(Ray) || type == typeof(Xline) || type == typeof(DBPoint) || type == typeof(Wipeout) || type == typeof(BlockReference) || type == typeof(MInsertBlock) || type == typeof(Hatch) || type == typeof(Table) || type == typeof(MLeader) ||
        type == typeof(Region) || type == typeof(Helix) || type == typeof(AlignedDimension) || type == typeof(RotatedDimension) ||
        type == typeof(RadialDimension) || type == typeof(DiametricDimension) || type == typeof(Point3AngularDimension) ||
        type == typeof(LineAngularDimension2) || type == typeof(OrdinateDimension) || CadExtendedDimensions356.Supported(type) || type == typeof(FeatureControlFrame);
    private static void CheckLayer(LayerTableRecord layer, bool allowHidden = false)
    {
        if (layer.IsLocked || layer.IsDependent || !allowHidden && (layer.IsFrozen || layer.IsOff))
            throw new DomainException("LAYER_UNAVAILABLE", "Layer must be unlocked and independent; geometry edits also require a visible/thawed source.");
    }
    private static Point3d Point(Point2 p) => new(p.X, p.Y, 0);
    internal static Matrix3d Matrix(EditSpec operation)
    {
        if (CadPlaneCurveTransforms394.Frame(operation) != null) return CadPlaneCurveTransforms394.Matrix(operation);
        return operation switch
        {
            Mirror2dSpec mirror => MirrorMatrix(mirror),
            MoveSpec move => Matrix3d.Displacement(new(move.Displacement.X, move.Displacement.Y, 0)),
            CopySpec copy => Matrix3d.Displacement(new(copy.Displacement.X, copy.Displacement.Y, 0)),
            CopyTransform2d407 copy => Matrix3d.Displacement(new(copy.Translation.X, copy.Translation.Y, 0)) * Matrix3d.Rotation(copy.Rotation, Vector3d.ZAxis, Point(copy.Center)) * Matrix3d.Scaling(copy.Scale, Point(copy.Center)),
            RotateSpec rotate => Matrix3d.Rotation(rotate.Angle, Vector3d.ZAxis, Point(rotate.Center)),
            ScaleSpec scale => Matrix3d.Scaling(scale.Factor, Point(scale.Center)),
            TransformSolidSpec solid => CadSolidEditor.Matrix(solid.Transform),
            _ => throw new InvalidOperationException("No transform for this operation.")
        };
    }
    private static Matrix3d MirrorMatrix(Mirror2dSpec s)
    {
        using var plane = new Plane(Point(s.Start), new Vector3d(-(s.End.Y - s.Start.Y), s.End.X - s.Start.X, 0).GetNormal()); return Matrix3d.Mirroring(plane);
    }
    private static void Transform(Entity entity, EditSpec operation, Transaction tr, Action cancel, List<string>? warnings = null, string? diagnosticSourceHandle = null)
    {
        using var nativeBinding = CadNativeDimensionBinding357.Bind(entity); entity = nativeBinding.Value;
        if (entity.GetType() == typeof(Wipeout)) { CadWipeout431.Transform((Wipeout)entity, operation, tr, cancel); return; }
        if (CadConstructionCurve443.Is(entity)) { CadConstructionCurve443.Transform(entity, operation, tr, cancel); return; }
        if (entity.GetType() == typeof(DBPoint)) { CadPointEditor408.Transform((DBPoint)entity, operation, tr, cancel); return; }
        if (CadPlaneCurveTransforms394.Frame(operation) != null) { CadPlaneCurveTransforms394.Transform(entity, operation, tr, cancel); return; }
        if (operation is Mirror2dSpec mirror && CadAnnotationMirrorEditor340.Supported(entity.GetType()))
        { var notices = CadAnnotationMirrorEditor340.Apply(entity, mirror, tr, cancel, diagnosticSourceHandle); warnings?.AddRange(notices); return; }
        if (CadAnnotationTransformEditor.Supported(entity.GetType())) { CadAnnotationTransformEditor.Apply(entity, operation, cancel); return; }
        if (entity is Solid3d solid)
        {
            double factor = operation is ScaleSpec scale ? scale.Factor : operation is TransformSolidSpec edit ? edit.Transform.Factor : 1;
            CadSolidEditor.Transform(solid, Matrix(operation), factor, cancel);
        }
        else entity.TransformBy(Matrix(operation));
    }
    internal static void ApplyProperties(Entity entity, PropertiesSpec edit, PropertyIds ids)
    {
        if (!ids.Layer.IsNull) entity.LayerId = ids.Layer;
        if (edit.Properties.ColorAci is { } aci) entity.ColorIndex = (short)aci;
        if (!ids.Linetype.IsNull) entity.LinetypeId = ids.Linetype;
        if (edit.Properties.LineweightHundredthMm is { } weight) entity.LineWeight = (LineWeight)weight;
        if (edit.Properties.LinetypeScale is { } scale) entity.LinetypeScale = scale;
        if (edit.Properties.Visible is { } visible) entity.Visible = visible;
        CadAppearanceProperties.Apply(entity, edit.Properties.ColorRgb, edit.Properties.Transparency);
    }
    private static void CheckTextSource(Entity entity) => CadTextEditor407.CheckSource(entity);
    // Preview every ordered edit on detached clones, before any original is
    // opened ForWrite. Native TransformBy rejection still rolls back at apply.
    public static void Preview(ModifyArguments args, Resolved resolved, Transaction tr, Action cancel)
    {
        CadContourBudget344.CheckCopyCapacity(args, cancel);
        if (args.Operations.Length == 1 && args.Operations[0] is SetMLeaderInstance442)
        { CadMLeaderInstance442.Preview(args, resolved, tr, cancel); return; }
        if (args.Operations.Length == 1 && args.Operations[0] is MLeaderTopologyEdit431)
        { CadMLeaderTopology431.Preview(args, resolved, tr, cancel); return; }
        if (args.Operations.Length == 1 && args.Operations[0] is MLeaderGeometryEdit388)
        { CadMLeaderGeometry388.Preview(args, resolved, tr, cancel); return; }
        if (args.Operations.Length == 1 && args.Operations[0] is NativeWindowStretchEdit387)
        { CadNativeWindowStretch387.Preview(args, resolved, tr, cancel); return; }
        if (args.Operations.Length == 1 && args.Operations[0] is NativeStretchEdit345)
        { CadNativeStretch345.Preview(args, resolved, tr, cancel); return; }
        if (args.Operations.Length == 1 && args.Operations[0] is NativeCornerCurveSpec343)
        { CadIndependentContourCorners343.Preview(args, resolved, tr, cancel); return; }
        if (args.Operations.Length == 1 && args.Operations[0] is NativeContourFinishingSpec342 native)
        {
            var source = (Entity)tr.GetObject(resolved.Sources[native.Handles[0]], OpenMode.ForRead);
            CadNativeContourFinishing342.Preview(args, resolved, source.Database, tr, cancel);
            return;
        }
        var tolerancePreview = new List<ToleranceFrameChange356>(); foreach (var edit in args.Operations.OfType<ToleranceEdit356>()) foreach (var handle in edit.Handles) { var f = (FeatureControlFrame)tr.GetObject(resolved.Sources[handle], OpenMode.ForRead); tolerancePreview.Add(CadToleranceFrame356.Preview(f, edit, f.Database, tr, cancel)); }
        if (System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(tolerancePreview, Wire.Json).Length > Wire.PayloadBudget - 16384) throw new DomainException("RESPONSE_LIMIT_EXCEEDED", "Planned complete tolerance changes exceed actual response bytes; no frame was edited.");
        foreach (var edit in args.Operations.Where(op => op is DimensionDisplayEdit341 or DimensionDefinitionEdit341))
            foreach (var handle in edit.Handles)
            {
                var dimension = (Entity)tr.GetObject(resolved.Sources[handle], OpenMode.ForRead);
                CadDimensionEditing341.Preview(dimension, edit, dimension.Database, tr, cancel);
            }
        // Slicing is executed once inside the write transaction. Source type,
        // ownership, dependencies and finite body geometry were checked by Resolve.
        if (args.Operations.Length == 1 && args.Operations[0] is SliceSolidSpec or SeparateSolidSpec or SolidFaceEditSpec or SolidReplacement409) return;
        if (args.Operations.Length == 1 && args.Operations[0] is TableEditingSpec lifecycle && (lifecycle.Operation == "set_table_cell_format" || TableTopologyValidation354.Handles(lifecycle.Operation))) return;
        // Native scalar/style layout needs the resident database. Resolve has checked
        // the exact source/token; detached previews cannot establish availability.
        if (args.Operations.Length == 1 && args.Operations[0] is TableContentsEditing364) return;
        if (args.Operations.Length == 1 && args.Operations[0] is TableValueEditingSpec) return;
        if (args.Operations.Length == 1 && args.Operations[0] is TableFormulaEditingSpec343) return;
        if (args.Operations.Length == 1 && args.Operations[0] is TableCellLayoutEdit340) return;
        if (args.Operations.Length == 1 && args.Operations[0] is TableGridLayoutEdit341) return;
        if (args.Operations.All(op => op is ResizeTableSpec) && args.Operations.Any(op => CadTableIndexedSize371.UsesIndexed(
            (Table)tr.GetObject(resolved.Sources[op.Handles[0]], OpenMode.ForRead), cancel)))
        {
            // Actual indexed ownership/raw slots were read in Resolve. Plan
            // ordered expected sizes without detached database/metadata guesses.
            foreach (var source in resolved.Sources)
            {
                cancel(); var table = (Table)tr.GetObject(source.Value, OpenMode.ForRead);
                CadTableIndexedSize371.Preflight(table, args.Operations.OfType<ResizeTableSpec>().Where(edit => edit.Handles.Contains(source.Key)), table.Database, tr, cancel);
            }
            return;
        }
        // The native factory runs once on the resident read-only curve during Apply.
        // Resolve checks its exact geometry/ownership; a detached factory is not an
        // availability oracle for resident open/concave/curved contours.
        if (args.Operations.Length == 1 && args.Operations[0] is NativeOffsetCurveSpec341) return;
        if (args.Operations.Length == 1 && args.Operations[0] is RegionBooleanEditSpec regionBoolean)
        {
            var source = (Region)tr.GetObject(resolved.Sources[regionBoolean.Handles[0]], OpenMode.ForRead);
            CadRegionBooleanEditor.Preview(regionBoolean, source, h => (Region)tr.GetObject(resolved.Sources[h], OpenMode.ForRead), source.Database, tr, cancel);
            return;
        }
        if (args.Operations.Length == 1 && args.Operations[0] is ContourAssemblySpec assembly)
        { CadContourAssemblyEditor.Preview(assembly, assembly.Handles.Select(h => (Entity)tr.GetObject(resolved.Sources[h], OpenMode.ForRead)).ToArray(), cancel, tr); return; }
        var clones = new Dictionary<string, Entity>(StringComparer.Ordinal);
        var blocks = new Dictionary<string, CadBlockMover.Preview>(StringComparer.Ordinal);
        var contourCounts = new Dictionary<int, int>(); var projectedFormats = new List<MTextFormatChange>();
        var planeSources = CadPlaneCurveTransforms394.CaptureSources(args, resolved, tr, cancel);
        try
        {
            foreach (var source in resolved.Sources)
            {
                cancel();
                // Placement needs the resident cloud insertion, never a detached clone.
                if (resolved.PointClouds.Contains(source.Key)) continue;
                // Resolve planned the resident Vertex2d graph. Detached child
                // enumeration is not an availability test for this native route.
                if (resolved.LegacyPolyline?.Sources.ContainsKey(source.Key) == true) continue;
                if (args.Operations.Any(op => op.Handles.Contains(source.Key)) &&
                    args.Operations.Where(op => op.Handles.Contains(source.Key)).All(op => op is DimensionDisplayEdit341 or DimensionDefinitionEdit341 or ToleranceEdit356)) continue;
                if (args.Operations.Any(op => op.Handles.Contains(source.Key)) &&
                    args.Operations.Where(op => op.Handles.Contains(source.Key)).All(op => op is SetMTextFormatSpec)) continue;
                // Pattern lookup and evaluation need the resident database. Resolve already
                // validated the exact stored loops/token; no detached Hatch availability gate.
                if (args.Operations.Where(op => op.Handles.Contains(source.Key)).All(op => op is SetHatchPropertiesSpec) &&
                    args.Operations.Any(op => op.Handles.Contains(source.Key))) continue;
                // Display properties and erasure have no transformed geometry to preview. Do not
                // clone associative hatches and their database-owned boundary links.
                if (BasicTarget(tr.GetObject(source.Value, OpenMode.ForRead).GetType()) && args.Operations.Where(op => op.Handles.Contains(source.Key)).All(op => op is EraseSpec or PropertiesSpec) &&
                    args.Operations.Any(op => op.Handles.Contains(source.Key)) &&
                    !args.Operations.Any(op => EditReferences.All(op).Contains(source.Key) && !op.Handles.Contains(source.Key))) continue;
                if (resolved.Blocks.TryGetValue(source.Key, out var block))
                    blocks.Add(source.Key, CadBlockMover.Prepare((BlockReference)tr.GetObject(source.Value, OpenMode.ForRead), block, tr, cancel));
                else
                {
                    var sourceEntity = (Entity)tr.GetObject(source.Value, OpenMode.ForRead);
                    clones.Add(source.Key, CadConstructionCurve443.Is(sourceEntity) ? CadConstructionCurve443.Detached(sourceEntity) : sourceEntity.GetType() == typeof(DBPoint) ? CadPointEditor408.Detached((DBPoint)sourceEntity) : sourceEntity is Dimension ? CadNativeDimensionBinding357.Clone(sourceEntity) : CadAnnotationMirrorEditor340.Supported(sourceEntity.GetType())
                        ? CadAnnotationMirrorEditor340.Detached(sourceEntity) : sourceEntity is Curve ? CadContourBudget344.Detached(sourceEntity) : (sourceEntity.GetType() == typeof(Wipeout) ? CadWipeout431.Detached((Wipeout)sourceEntity) : (Entity)sourceEntity.Clone()));
                }
            }
            for (int i = 0; i < args.Operations.Length; i++)
            {
                var operation = args.Operations[i];
                foreach (var handle in operation.Handles)
                {
                    if (resolved.PointClouds.Contains(handle)) continue;
                    if (resolved.LegacyPolyline?.Sources.ContainsKey(handle) == true) continue;
                    cancel();
                    if (operation is SetMTextFormatSpec note && args.Operations.Any(CadContourEditor.Handles))
                    {
                        var original = (MText)tr.GetObject(resolved.Sources[handle], OpenMode.ForRead);
                        var snapshot = CadMTextFormatEditor.CheckSource(original, note, original.Database, tr, cancel);
                        projectedFormats.Add(CadContourBudget344.ProjectedFormat(args, handle, snapshot.Warnings)); continue;
                    }
                    if (operation is SetHatchPropertiesSpec or SetMTextFormatSpec or DimensionDisplayEdit341 or DimensionDefinitionEdit341 or ToleranceEdit356) continue;
                    if (operation is EraseSpec) continue;
                    if (operation is PropertiesSpec && BasicTarget(tr.GetObject(resolved.Sources[handle], OpenMode.ForRead).GetType()) &&
                        args.Operations.Where(op => op.Handles.Contains(handle)).All(op => op is EraseSpec or PropertiesSpec)) continue;
                    if (blocks.TryGetValue(handle, out var block))
                    {
                        if (CopyTransforms2d407.Handles(operation))
                            foreach (var transform in CopyTransforms2d407.Expand(operation, cancel)) CadBlockCopier.PreviewMatrix(block, resolved.Blocks[handle], Matrix(transform), tr, cancel);
                        else if (operation is Mirror2dSpec { Copy: true }) CadBlockCopier.PreviewMatrix(block, resolved.Blocks[handle], Matrix(operation), tr, cancel, true);
                        else if (operation is CopySpec copyBlock) CadBlockCopier.Preview(block, resolved.Blocks[handle], copyBlock, tr, cancel);
                        else if (operation is PropertiesSpec blockProperties) CadBlockPropertyEditor.Preview(block, blockProperties, resolved.Properties[i], tr, cancel);
                        else if (operation is not EraseSpec) CadBlockMover.Transform(block.Reference, block.Attributes, resolved.Blocks[handle], operation, tr, cancel, true);
                        continue;
                    }
                    using var nativeBinding = CadNativeDimensionBinding357.Bind(clones[handle]); var entity = nativeBinding.Value;
                    if (operation is EraseSpec) continue;
                    if (CopyTransforms2d407.Handles(operation))
                    {
                        foreach (var transform in CopyTransforms2d407.Expand(operation, cancel))
                        {
                            using var copy = CadConstructionCurve443.Is(entity) ? CadConstructionCurve443.Detached(entity) : entity.GetType() == typeof(DBPoint) ? CadPointEditor408.Detached((DBPoint)entity) : entity is Curve ? CadContourBudget344.Detached(entity) : (entity.GetType() == typeof(Wipeout) ? CadWipeout431.Detached((Wipeout)entity) : (Entity)entity.Clone());
                            Transform(copy, transform, tr, cancel); CheckOperationGeometry(copy, transform, cancel);
                        }
                        continue;
                    }
                    if (CadContourEditor.Handles(operation))
                    {
                        var plan = CadContourEditor.Plan(entity, operation, boundary => clones[boundary], cancel);
                        contourCounts[i] = plan.Length;
                        if (operation is ExtendCurveSpec) CadContourEditor.Extend(entity, plan.Single(), cancel);
                        else foreach (var geometry in plan) { using var generated = CadContourEditor.Create(entity, geometry, cancel); }
                        continue;
                    }
                    if (operation is CopySpec or Mirror2dSpec { Copy: true })
                    {
                        using var copy = CadConstructionCurve443.Is(entity) ? CadConstructionCurve443.Detached(entity) : entity.GetType() == typeof(DBPoint) ? CadPointEditor408.Detached((DBPoint)entity) : entity is Dimension ? CadNativeDimensionBinding357.Clone(entity) : CadAnnotationMirrorEditor340.Supported(entity.GetType())
                            ? CadAnnotationMirrorEditor340.Detached(entity) : entity is Curve ? CadContourBudget344.Detached(entity) : (entity.GetType() == typeof(Wipeout) ? CadWipeout431.Detached((Wipeout)entity) : (Entity)entity.Clone());
                        Transform(copy, operation, tr, cancel, diagnosticSourceHandle: handle); CheckOperationGeometry(copy, operation, cancel);
                    }
                    else
                    {
                        if (operation is SolidOffsetEdit353 offset) CadSolidStockEditor353.Offset(offset, (Solid3d)entity, new(args.BridgeId, args.DocumentId, handle), cancel);
                        else if (operation is SolidBooleanEditSpec boolean) CadSolidBooleanEditor.Apply(boolean, (Solid3d)entity, h => (Solid3d)tr.GetObject(resolved.Sources[h], OpenMode.ForRead), cancel);
                        else if (operation is SolidFinishingEditSpec finish) SolidFinisher.Apply(finish.AsCopySpec(entity.Layer), (Solid3d)entity, new(args.BridgeId, args.DocumentId, handle), cancel: cancel);
                        else if (operation is SolidFaceEditSpec face) CadSolidFaceEditor.Apply(face, (Solid3d)entity, new(args.BridgeId, args.DocumentId, handle), cancel);
                        else if (operation is SolidDrillEditSpec drill) SolidCreator.Drill(drill.AsCopySpec(entity.Layer), (Solid3d)entity);
                        else if (operation is WipeoutBoundaryEdit431 maskBoundary) CadWipeout431.SetBoundary((Wipeout)entity, maskBoundary, tr, cancel, resident: false);
                        else if (operation is PointPositionEdit408 pointPosition) CadPointEditor408.SetPosition(entity, pointPosition, tr, cancel);
                        else if (operation is CurveGeometrySpec curve) CadCurveGeometryEditor.Apply(entity, curve, cancel);
                        else if (operation is PolylineVerticesSpec vertices) CadPolylineVertexEditor.Apply((Polyline)entity, vertices, cancel);
                        else if (CadPolylineTopologyEditor.Handles(operation)) CadPolylineTopologyEditor.Apply((Polyline)entity, operation, cancel);
                        else if (operation is DimensionFormatSpec format) CadDimensionFormatEditor.Apply(entity, format, entity.Database ?? tr.GetObject(resolved.Model, OpenMode.ForRead).Database, tr, cancel, false);
                        else if (operation is SetHatchPatternSpec hatch) CadHatchPatternEditor.Apply((Hatch)entity, hatch, cancel, false);
                        else if (operation is ReplaceMLeaderTextSpec leaderText) CadMLeaderTextEditor.Apply((MLeader)entity, leaderText, cancel);
                        else if (operation is TableEditingSpec tableEdit) CadTableLifecycleEditor.Apply((Table)entity, tableEdit, entity.Database ?? tr.GetObject(resolved.Model, OpenMode.ForRead).Database, tr, cancel, false, false);
                        else if (operation is ReplaceTableTextSpec tableText) CadTableTextEditor.Apply((Table)entity, tableText, cancel, false);
                        else if (operation is ResizeTableSpec tableSize) CadTableSizeEditor.Apply((Table)entity, tableSize, cancel, false);
                        else if (operation is SetDimensionTextPositionSpec label) CadDimensionTextPositionEditor.Apply(entity, label, cancel, false);
                        else if (operation is SetDimensionLineSpec dimensionLine) CadDimensionLineEditor.Apply(entity, dimensionLine, cancel, false);
                        else if (operation is ReplaceDimensionTextSpec dimensionText) CadDimensionTextEditor.Apply(entity, dimensionText, cancel, false);
                        else if (operation is ReplaceTextSpec text) CadTextEditor407.Apply(entity, text, tr.GetObject(resolved.Model, OpenMode.ForRead).Database, cancel);
                        else if (operation is ReplaceMTextSpec mtext) CadMTextEditor.Apply(entity, mtext, cancel);
                        else if (operation is PropertiesSpec properties) ApplyProperties(entity, properties, resolved.Properties[i]);
                        else Transform(entity, operation, tr, cancel);
                        if (operation is not PropertiesSpec and not DimensionFormatSpec and not TableEditingSpec and not ReplaceMLeaderTextSpec and not ReplaceTableTextSpec and not ResizeTableSpec and not ReplaceDimensionTextSpec and not SetDimensionLineSpec and not SetDimensionTextPositionSpec and not SetHatchPatternSpec) CheckOperationGeometry(entity, operation, cancel);
                    }
                }
            }
            CadContourBudget344.BatchOutput(args, contourCounts, projectedFormats.ToArray(), cancel, tolerancePreview.ToArray());
            if (resolved.LegacyPolyline is { } legacy) CadLegacyPolyline408.VerifyInitial(legacy, tr, cancel);
        }
        finally
        {
            System.Exception? cleanup = null;
            foreach (var block in blocks.Values) try { block.Dispose(); } catch (System.Exception e) { cleanup ??= e; }
            foreach (var clone in clones.Values) try { clone.Dispose(); } catch (System.Exception e) { cleanup ??= e; }
            CadPlaneCurveTransforms394.VerifySources(planeSources, tr, cancel);
            if (cleanup != null) throw new InvalidOperationException("Detached preview cleanup failed.", cleanup);
        }
    }
    public static ModifiedEntities Apply(ModifyArguments args, Resolved resolved, Transaction tr, List<Entity> ownedCopies, WriteJob job, List<string>? nativeFormatWarnings = null)
    {
        if (args.Operations.Any(operation => operation is SolidReplacement409))
            throw new DomainException("INVALID_OPERATION_ORDER", "Direct solid replacement must execute through its owning native writer outside a managed transaction.");
        if (args.Operations.Length == 1 && args.Operations[0] is SetMLeaderInstance442)
            return CadMLeaderInstance442.Apply(args, resolved, tr, job, nativeFormatWarnings);
        if (args.Operations.Length == 1 && args.Operations[0] is MLeaderTopologyEdit431)
            return CadMLeaderTopology431.Apply(args, resolved, tr, job, nativeFormatWarnings);
        if (args.Operations.Length == 1 && args.Operations[0] is MLeaderGeometryEdit388)
            return CadMLeaderGeometry388.Apply(args, resolved, tr, job, nativeFormatWarnings);
        if (args.Operations.Length == 1 && args.Operations[0] is NativeWindowStretchEdit387)
            return CadNativeWindowStretch387.Apply(args, resolved, tr, job, nativeFormatWarnings);
        if (args.Operations.Length == 1 && args.Operations[0] is NativeStretchEdit345)
            return CadNativeStretch345.Apply(args, resolved, tr, job, nativeFormatWarnings);
        if (args.Operations.Length == 1 && args.Operations[0] is NativeCornerCurveSpec343)
            return CadIndependentContourCorners343.Apply(args, resolved, tr, ownedCopies, job, nativeFormatWarnings);
        if (args.Operations.Length == 1 && args.Operations[0] is NativeContourFinishingSpec342)
            return CadNativeContourFinishing342.Apply(args, resolved, tr, ownedCopies, job, nativeFormatWarnings);
        if (args.Operations.Length == 1 && args.Operations[0] is ContourAssemblySpec)
            return CadContourAssemblyEditor.Apply(args, resolved, tr, ownedCopies, job, nativeFormatWarnings);
        var modified = new List<string>(); var erased = new List<string>(); var copies = new List<CopyMapping>();
        var contourResults = new List<ContourMapping>();
        var slices = new List<SolidSliceMapping>();
        var separations = new List<SolidSeparationMapping>();
        var booleanChanges = new List<SolidBooleanChange>();
        var stockChanges = new List<SolidStockChange353>(); var toleranceChanges = new List<ToleranceFrameChange356>();
        var topologyChanges = new List<TableTopologyChange354>();
        var regionChanges = new List<RegionBooleanChange>();
        var mtextChanges = new List<MTextFormatChange>();
        var modifiedSet = new HashSet<string>(StringComparer.Ordinal);
        EntityRef Ref(string handle) => new(args.BridgeId, args.DocumentId, handle);
        for (int i = 0; i < args.Operations.Length; i++)
        {
            var operation = args.Operations[i];
            foreach (var handle in operation.Handles)
            {
                job.CheckCancellation();
                var opened = (Entity)tr.GetObject(resolved.Sources[handle], (IsCopy(operation) || operation is OffsetCurveSpec or NativeOffsetCurveSpec341) ? OpenMode.ForRead : OpenMode.ForWrite);
                using var nativeBinding = CadNativeDimensionBinding357.Bind(opened); var entity = nativeBinding.Value;
                if (resolved.PointClouds.Contains(handle))
                {
                    CadPointCloudPlacement.Apply(entity, operation, tr, job.CheckCancellation);
                    if (modifiedSet.Add(handle)) modified.Add(handle);
                    continue;
                }
                if (resolved.LegacyPolyline?.Sources.ContainsKey(handle) == true)
                {
                    var created = CadLegacyPolyline408.ApplyOperation((Polyline2d)entity, operation, resolved.Model, tr,
                        resolved.Properties.TryGetValue(i, out var propertyIds) ? propertyIds : null, job.CheckCancellation, nativeFormatWarnings);
                    foreach (var copy in created) copies.Add(new(i, Ref(handle), Ref(Validation.NormalizeHandle(copy.Handle.ToString()))));
                    if (operation is EraseSpec) erased.Add(handle);
                    else if (!IsCopy(operation) && modifiedSet.Add(handle)) modified.Add(handle);
                    continue;
                }
                if (operation is NativeOffsetCurveSpec341 nativeOffset)
                {
                    var generated = CadNativeContourOffset341.Create(entity, nativeOffset, entity.Database, tr, job.CheckCancellation);
                    // Adopt every factory result before any other fallible step.
                    ownedCopies.AddRange(generated.Curves);
                    CadNativeContourOffset341.CheckProjectedResultBudget(generated, i, Ref(handle), job.CheckCancellation);
                    nativeFormatWarnings?.AddRange(generated.Warnings);
                    var references = new List<EntityRef>();
                    var model = (BlockTableRecord)tr.GetObject(resolved.Model, OpenMode.ForWrite);
                    for (int resultIndex = 0; resultIndex < generated.Curves.Length; resultIndex++)
                    {
                        job.CheckCancellation(); var result = generated.Curves[resultIndex];
                        model.AppendEntity(result); tr.AddNewlyCreatedDBObject(result, true);
                        CadNativeContourOffset341.VerifyAppended(result, generated.Shapes[resultIndex], generated.Display, resolved.Model, job.CheckCancellation);
                        references.Add(Ref(Validation.NormalizeHandle(result.Handle.ToString())));
                    }
                    CadNativeContourOffset341.VerifySource(entity, generated.Source, entity.Database, tr, job.CheckCancellation, nativeOffset.Workplane);
                    contourResults.Add(new(i, operation.Operation, Ref(handle), references.ToArray(), false));
                    continue;
                }
                if (operation is SeparateSolidSpec)
                {
                    var source = (Solid3d)entity;
                    var added = source.SeparateBody() ?? Array.Empty<Solid3d>();
                    // Own the entire native return immediately; no unappended piece is lost on error.
                    ownedCopies.AddRange(added);
                    var parts = new List<EntityRef> { Ref(handle) };
                    var model = (BlockTableRecord)tr.GetObject(resolved.Model, OpenMode.ForWrite);
                    CadSolidEditor.CheckGeometry(source, job.CheckCancellation);
                    foreach (var part in added)
                    {
                        job.CheckCancellation(); part.SetPropertiesFrom(source);
                        CadSolidEditor.CheckGeometry(part, job.CheckCancellation);
                        model.AppendEntity(part); tr.AddNewlyCreatedDBObject(part, true);
                        parts.Add(Ref(Validation.NormalizeHandle(part.Handle.ToString())));
                    }
                    separations.Add(new(i, Ref(handle), parts.ToArray()));
                    if (added.Length > 0 && modifiedSet.Add(handle)) modified.Add(handle); continue;
                }
                if (operation is SliceSolidSpec slice)
                {
                    var negative = CadSolidSlicer.Apply((Solid3d)entity, slice, job.CheckCancellation); ownedCopies.Add(negative);
                    var model = (BlockTableRecord)tr.GetObject(resolved.Model, OpenMode.ForWrite);
                    model.AppendEntity(negative); tr.AddNewlyCreatedDBObject(negative, true);
                    slices.Add(new(i, Ref(handle), Ref(handle), Ref(Validation.NormalizeHandle(negative.Handle.ToString()))));
                    if (modifiedSet.Add(handle)) modified.Add(handle); continue;
                }
                if (CadContourEditor.Handles(operation))
                {
                    var contourBefore = CadContourBudget344.Source(entity, job.CheckCancellation);
                    var plan = CadContourEditor.Plan(entity, operation, boundary => (Entity)tr.GetObject(resolved.Sources[boundary], OpenMode.ForRead), job.CheckCancellation, tr);
                    var projected = operation is ExtendCurveSpec ? Array.Empty<EntityRef>() : CadContourBudget344.Projected(args, plan.Length, job.CheckCancellation);
                    var beforeCreated = copies.Select(c => (Index: c.operation_index, Reference: c.created))
                        .Concat(separations.SelectMany(c => c.parts.Skip(1).Select(p => (Index: c.operation_index, Reference: p))))
                        .Concat(slices.Select(c => (Index: c.operation_index, Reference: c.negative)))
                        .Concat(contourResults.SelectMany(c => c.generated.Select(r => (Index: c.operation_index, Reference: r))))
                        .OrderBy(c => c.Index).Select(c => c.Reference).ToArray();
                    var projectedErased = erased.Concat(operation is TrimCurveSpec ? new[] { handle } : Array.Empty<string>()).ToArray();
                    var projectedModified = modified.Concat(operation is ExtendCurveSpec && !modifiedSet.Contains(handle) ? new[] { handle } : Array.Empty<string>())
                        .Where(h => !projectedErased.Contains(h)).Select(Ref).ToArray();
                    CadContourBudget344.Output(new(args.OperationId, "committed", false, projectedModified, projectedErased.Select(Ref).ToArray(),
                        beforeCreated.Concat(projected).ToArray(), copies.ToArray(), "single_command",
                        contourResults.Append(new(i, operation.Operation, Ref(handle), projected, operation is TrimCurveSpec)).ToArray(),
                        solid_slice_results: slices.Count == 0 ? null : slices.ToArray(), solid_separation_results: separations.Count == 0 ? null : separations.ToArray(),
                        solid_boolean_results: booleanChanges.Count == 0 ? null : booleanChanges.ToArray(), region_boolean_changes: regionChanges.Count == 0 ? null : regionChanges.ToArray(),
                        mtext_format_changes: mtextChanges.Count == 0 ? null : mtextChanges.ToArray(), solid_stock_changes: stockChanges.Count == 0 ? null : stockChanges.ToArray(), table_topology_changes: topologyChanges.Count == 0 ? null : topologyChanges.ToArray(), tolerance_changes: toleranceChanges.Count == 0 ? null : toleranceChanges.ToArray()), job.CheckCancellation);
                    if (operation is ExtendCurveSpec)
                    {
                        CadContourEditor.Extend(entity, plan.Single(), job.CheckCancellation);
                        CadContourBudget344.VerifyProperties(entity, contourBefore, job.CheckCancellation);
                        CadContourBudget344.RetentionWarning(new[] { contourBefore }, nativeFormatWarnings);
                        if (modifiedSet.Add(handle)) modified.Add(handle);
                        contourResults.Add(new(i, operation.Operation, Ref(handle), Array.Empty<EntityRef>(), false));
                    }
                    else
                    {
                        var generated = new List<EntityRef>(); var prepared = new List<Entity>();
                        foreach (var geometry in plan)
                        {
                            job.CheckCancellation(); var result = CadContourEditor.Create(entity, geometry, job.CheckCancellation); ownedCopies.Add(result);
                            prepared.Add(result);
                        }
                        var model = (BlockTableRecord)tr.GetObject(resolved.Model, OpenMode.ForWrite);
                        for (int part = 0; part < prepared.Count; part++)
                        {
                            job.CheckCancellation(); var result = prepared[part];
                            model.AppendEntity(result); tr.AddNewlyCreatedDBObject(result, true); CadContourEditor.Verify(result, plan[part], job.CheckCancellation);
                            var reference = Ref(Validation.NormalizeHandle(result.Handle.ToString())); generated.Add(reference);
                        }
                        CadContourBudget344.VerifySource(entity, contourBefore, job.CheckCancellation);
                        bool remove = operation is TrimCurveSpec; if (remove) { entity.Erase(true); erased.Add(handle); }
                        if (!remove) CadContourBudget344.RetentionWarning(new[] { contourBefore }, nativeFormatWarnings);
                        contourResults.Add(new(i, operation.Operation, Ref(handle), generated.ToArray(), remove));
                    }
                    continue;
                }
                if (resolved.Blocks.TryGetValue(handle, out var block))
                {
                    if (operation is PropertiesSpec blockProperties)
                    { CadBlockPropertyEditor.Apply((BlockReference)entity, block, blockProperties, resolved.Properties[i], tr, job.CheckCancellation); if (modifiedSet.Add(handle)) modified.Add(handle); continue; }
                    if (operation is CopySpec copyBlock)
                    { var created = CadBlockCopier.Apply((BlockReference)entity, block, copyBlock, resolved.Model, tr, ownedCopies, job.CheckCancellation); copies.Add(new(i, Ref(handle), Ref(Validation.NormalizeHandle(created.Handle.ToString())))); continue; }
                    if (CopyTransforms2d407.Handles(operation))
                    {
                        foreach (var transform in CopyTransforms2d407.Expand(operation, job.CheckCancellation))
                        {
                            var created = CadBlockCopier.ApplyMatrix((BlockReference)entity, block, Matrix(transform), resolved.Model, tr, ownedCopies, job.CheckCancellation);
                            copies.Add(new(i, Ref(handle), Ref(Validation.NormalizeHandle(created.Handle.ToString()))));
                        }
                        continue;
                    }
                    if (operation is Mirror2dSpec { Copy: true })
                    { var created = CadBlockCopier.ApplyMatrix((BlockReference)entity, block, Matrix(operation), resolved.Model, tr, ownedCopies, job.CheckCancellation, true); copies.Add(new(i, Ref(handle), Ref(Validation.NormalizeHandle(created.Handle.ToString())))); continue; }
                    if (operation is EraseSpec)
                    { CadBlockEraser.Apply((BlockReference)entity, block, tr, job.CheckCancellation); erased.Add(handle); continue; }
                    CadBlockMover.Apply((BlockReference)entity, block, operation, tr, job.CheckCancellation);
                    if (modifiedSet.Add(handle)) modified.Add(handle); continue;
                }
                if (CopyTransforms2d407.Handles(operation))
                {
                    if (entity.GetType() == typeof(DBPoint))
                    {
                        foreach (var transform in CopyTransforms2d407.Expand(operation, job.CheckCancellation))
                        {
                            var created = CadPointEditor408.Copy((DBPoint)entity, transform, resolved.Model, tr, job.CheckCancellation);
                            copies.Add(new(i, Ref(handle), Ref(Validation.NormalizeHandle(created.Handle.ToString()))));
                        }
                        continue;
                    }
                    var before = entity is Spline ? NativeSplinePlacement407.CheckGeometry(entity, job.CheckCancellation) : null;
                    foreach (var transform in CopyTransforms2d407.Expand(operation, job.CheckCancellation))
                    {
                        var copy = entity is Curve ? CadContourBudget344.Detached(entity) : (entity.GetType() == typeof(Wipeout) ? CadWipeout431.Detached((Wipeout)entity) : (Entity)entity.Clone()); ownedCopies.Add(copy);
                        Transform(copy, transform, tr, job.CheckCancellation, nativeFormatWarnings); CheckOperationGeometry(copy, transform, job.CheckCancellation);
                        var model = (BlockTableRecord)tr.GetObject(resolved.Model, OpenMode.ForWrite);
                        model.AppendEntity(copy); tr.AddNewlyCreatedDBObject(copy, true);
                        copies.Add(new(i, Ref(handle), Ref(Validation.NormalizeHandle(copy.Handle.ToString()))));
                    }
                    if (before != null) NativeSplinePlacement407.VerifyUnchanged((Spline)entity, before, job.CheckCancellation);
                    continue;
                }
                if (operation is CopySpec or Mirror2dSpec { Copy: true })
                {
                    if (CadConstructionCurve443.Is(entity))
                    {
                        var created = CadConstructionCurve443.Copy(entity, operation, resolved.Model, tr, job.CheckCancellation); copies.Add(new(i, Ref(handle), Ref(Validation.NormalizeHandle(created.Handle.ToString())))); continue;
                    }
                    if (entity.GetType() == typeof(DBPoint))
                    {
                        var created = CadPointEditor408.Copy((DBPoint)entity, operation, resolved.Model, tr, job.CheckCancellation);
                        copies.Add(new(i, Ref(handle), Ref(Validation.NormalizeHandle(created.Handle.ToString())))); continue;
                    }
                    if (CadPlaneCurveTransforms394.Frame(operation) != null)
                    {
                        var created = CadPlaneCurveTransforms394.Copy(entity, operation, resolved.Model, tr, job.CheckCancellation);
                        copies.Add(new(i, Ref(handle), Ref(Validation.NormalizeHandle(created.Handle.ToString())))); continue;
                    }
                    if (operation is Mirror2dSpec annotationMirror && CadAnnotationMirrorEditor340.Supported(entity.GetType()))
                    {
                        var created = CadAnnotationMirrorEditor340.Copy(entity, annotationMirror, resolved.Model, tr, job.CheckCancellation,
                            notice => nativeFormatWarnings?.Add(notice));
                        copies.Add(new(i, Ref(handle), Ref(Validation.NormalizeHandle(created.Handle.ToString())))); continue;
                    }
                    if (operation is CopySpec annotationCopy && CadAnnotationTransformEditor.Supported(entity.GetType()))
                    {
                        var created = CadAnnotationTransformEditor.Copy(entity, annotationCopy, resolved.Model, tr, job.CheckCancellation);
                        copies.Add(new(i, Ref(handle), Ref(Validation.NormalizeHandle(created.Handle.ToString())))); continue;
                    }
                    var before = entity is Spline ? NativeSplinePlacement407.CheckGeometry(entity, job.CheckCancellation) : null;
                    var copy = entity is Curve ? CadContourBudget344.Detached(entity) : (entity.GetType() == typeof(Wipeout) ? CadWipeout431.Detached((Wipeout)entity) : (Entity)entity.Clone()); ownedCopies.Add(copy);
                    Transform(copy, operation, tr, job.CheckCancellation, nativeFormatWarnings); CheckOperationGeometry(copy, operation, job.CheckCancellation);
                    var model = (BlockTableRecord)tr.GetObject(resolved.Model, OpenMode.ForWrite);
                    model.AppendEntity(copy); tr.AddNewlyCreatedDBObject(copy, true);
                    copies.Add(new(i, Ref(handle), Ref(Validation.NormalizeHandle(copy.Handle.ToString()))));
                    if (before != null) NativeSplinePlacement407.VerifyUnchanged((Spline)entity, before, job.CheckCancellation);
                }
                else if (operation is EraseSpec) { entity.Erase(true); erased.Add(handle); }
                else
                {
                    if (operation is RegionBooleanEditSpec regionBoolean)
                    {
                        var outcome = CadRegionBooleanEditor.Apply(regionBoolean, (Region)entity, h => (Region)tr.GetObject(resolved.Sources[h], OpenMode.ForRead), entity.Database, tr, job.CheckCancellation);
                        regionChanges.Add(new(i, regionBoolean.Operator, Ref(handle), regionBoolean.ToolHandles.Select(Ref).ToArray(), outcome.AreaBefore, outcome.AreaAfter, outcome.Plane, outcome.TopologyAfter));
                    }
                    else if (operation is SolidOffsetEdit353 offset)
                    {
                        var observed = CadSolidStockEditor353.Offset(offset, (Solid3d)entity, Ref(handle), job.CheckCancellation);
                        stockChanges.Add(CadSolidStockEditor353.OffsetChange(i, offset, Ref(handle), observed.Before, observed.After));
                    }
                    else if (operation is SolidBooleanEditSpec boolean)
                    {
                        var outcome = CadSolidBooleanEditor.Apply(boolean, (Solid3d)entity, h => (Solid3d)tr.GetObject(resolved.Sources[h], OpenMode.ForRead), job.CheckCancellation);
                        booleanChanges.Add(new(i, boolean.Operator, Ref(handle), boolean.ToolHandles.Select(Ref).ToArray(), outcome.BeforeVolume, outcome.AfterVolume));
                    }
                    else if (operation is SolidFinishingEditSpec finish)
                    {
                        var before = finish.Kind == "shell_solid" ? CadSolidStockEditor353.Measure((Solid3d)entity, job.CheckCancellation) : null;
                        var after = SolidFinisher.Apply(finish.AsCopySpec(entity.Layer), (Solid3d)entity, Ref(handle), cancel: job.CheckCancellation);
                        if (before != null && after != null) stockChanges.Add(CadSolidStockEditor353.ShellChange(i, finish, Ref(handle), before, after));
                    }
                    else if (operation is SolidFaceEditSpec { PathHandle: not null } facePath)
                        CadSolidFacePath389.Apply(facePath, (Solid3d)entity, Ref(handle), (Entity)tr.GetObject(resolved.Sources[facePath.PathHandle!], OpenMode.ForRead), resolved.Model, entity.Database, job.CheckCancellation, nativeFormatWarnings);
                    else if (operation is SolidFaceEditSpec face) CadSolidFaceEditor.Apply(face, (Solid3d)entity, Ref(handle), job.CheckCancellation);
                    else if (operation is SolidDrillEditSpec drill) SolidCreator.Drill(drill.AsCopySpec(entity.Layer), (Solid3d)entity);
                    else if (operation is WipeoutBoundaryEdit431 maskBoundary) CadWipeout431.SetBoundary((Wipeout)entity, maskBoundary, tr, job.CheckCancellation);
                    else if (operation is PointPositionEdit408 pointPosition) CadPointEditor408.SetPosition(entity, pointPosition, tr, job.CheckCancellation);
                    else if (operation is CurveGeometrySpec curve)
                    { CadCurveGeometryEditor.Apply(entity, curve, job.CheckCancellation); if (curve.Workplane == null && (entity.GetType() == typeof(Line) || entity.GetType() == typeof(Circle))) { if (CadDimensionAssociation385.RefreshFollowers(entity, entity.Database, tr, job.CheckCancellation)) nativeFormatWarnings?.Add(CadDimensionAssociation385.Warning); } }
                    else if (operation is PolylineVerticesSpec vertices) CadPolylineVertexEditor.Apply((Polyline)entity, vertices, job.CheckCancellation);
                    else if (CadPolylineTopologyEditor.Handles(operation)) CadPolylineTopologyEditor.Apply((Polyline)entity, operation, job.CheckCancellation);
                    else if (operation is ToleranceEdit356 tolerance) { toleranceChanges.Add(CadToleranceFrame356.Apply(i, (FeatureControlFrame)entity, tolerance, entity.Database, tr, Ref(handle), job.CheckCancellation)); nativeFormatWarnings?.Add(CadToleranceFrame356.Warning); }
                    else if (operation is DimensionFormatSpec format) CadDimensionFormatEditor.Apply(entity, format, entity.Database, tr, job.CheckCancellation, true);
                    else if (operation is DimensionDisplayEdit341 or DimensionDefinitionEdit341)
                        CadDimensionEditing341.Apply(entity, operation, entity.Database, tr, job.CheckCancellation, true, nativeFormatWarnings);
                    else if (operation is SetHatchPatternSpec hatch) CadHatchPatternEditor.Apply((Hatch)entity, hatch, job.CheckCancellation, true);
                    else if (operation is SetHatchPropertiesSpec hatchProperties) CadHatchPropertiesEditor.Apply((Hatch)entity, hatchProperties, entity.Database, tr, job.CheckCancellation, true);
                    else if (operation is ReplaceMLeaderTextSpec leaderText) CadMLeaderTextEditor.Apply((MLeader)entity, leaderText, job.CheckCancellation);
                    else if (operation is SetMTextFormatSpec mtextFormat) mtextChanges.Add(CadMTextFormatEditor.Apply((MText)entity, mtextFormat, entity.Database, tr, job.CheckCancellation, Ref(handle)));
                    else if (operation is TableGridLayoutEdit341 tableGrid)
                    {
                        var tableWarnings = CadTableGridLayoutEditor341.Apply((Table)entity, tableGrid, entity.Database, tr, job.CheckCancellation);
                        nativeFormatWarnings?.AddRange(tableWarnings);
                    }
                    else if (operation is TableCellLayoutEdit340 tableLayout)
                    {
                        var tableWarnings = CadTableCellLayoutEditor340.Apply((Table)entity, tableLayout, entity.Database, tr, job.CheckCancellation);
                        nativeFormatWarnings?.AddRange(tableWarnings);
                    }
                    else if (operation is TableFormulaEditingSpec343 tableFormula)
                    {
                        var tableWarnings = CadTableFormulaEditor343.Apply((Table)entity, tableFormula, entity.Database, tr, job.CheckCancellation);
                        nativeFormatWarnings?.AddRange(tableWarnings);
                    }
                    else if (operation is TableContentsEditing364 contentsEdit)
                    {
                        var tableWarnings = CadTableContentsEditor364.Apply((Table)entity, contentsEdit, entity.Database, tr, job.CheckCancellation);
                        nativeFormatWarnings?.AddRange(tableWarnings);
                    }
                    else if (operation is TableValueEditingSpec tableValues)
                    {
                        var tableWarnings = CadTableValueEditor336.Apply((Table)entity, tableValues, entity.Database, tr, job.CheckCancellation);
                        nativeFormatWarnings?.AddRange(tableWarnings);
                    }
                    else if (operation is TableEditingSpec tableTopology && TableTopologyValidation354.Handles(tableTopology.Operation))
                        topologyChanges.Add(CadTableTopologyEditor354.Apply((Table)entity, tableTopology, entity.Database, tr, job.CheckCancellation, i, Ref(handle), nativeFormatWarnings));
                    else if (operation is TableEditingSpec tableEdit) CadTableLifecycleEditor.Apply((Table)entity, tableEdit, entity.Database, tr, job.CheckCancellation, true, nativeFormatWarnings: nativeFormatWarnings);
                    else if (operation is ReplaceTableTextSpec tableText) CadTableTextEditor.Apply((Table)entity, tableText, job.CheckCancellation, true, nativeFormatWarnings);
                    else if (operation is ResizeTableSpec tableSize)
                    {
                        if (CadTableIndexedSize371.UsesIndexed((Table)entity, job.CheckCancellation))
                        {
                            var sizeWarnings = CadTableIndexedSize371.Apply((Table)entity, tableSize, entity.Database, tr, job.CheckCancellation);
                            nativeFormatWarnings?.AddRange(sizeWarnings);
                        }
                        else CadTableSizeEditor.Apply((Table)entity, tableSize, job.CheckCancellation, true);
                    }
                    else if (operation is SetDimensionTextPositionSpec label) CadDimensionTextPositionEditor.Apply(entity, label, job.CheckCancellation, true);
                    else if (operation is SetDimensionLineSpec dimensionLine) CadDimensionLineEditor.Apply(entity, dimensionLine, job.CheckCancellation, true);
                    else if (operation is ReplaceDimensionTextSpec dimensionText) CadDimensionTextEditor.Apply(entity, dimensionText, job.CheckCancellation, true);
                    else if (operation is ReplaceTextSpec text) CadTextEditor407.Apply(entity, text, entity.Database, job.CheckCancellation);
                    else if (operation is ReplaceMTextSpec mtext) CadMTextEditor.Apply(entity, mtext, job.CheckCancellation, notice => nativeFormatWarnings?.Add(notice));
                    else if (operation is PropertiesSpec properties) ApplyProperties(entity, properties, resolved.Properties[i]);
                    else Transform(entity, operation, tr, job.CheckCancellation, nativeFormatWarnings);
                    if (operation is not ToleranceEdit356 and not SetMTextFormatSpec and not TableContentsEditing364 and not TableValueEditingSpec and not TableFormulaEditingSpec343 and not TableCellLayoutEdit340 and not TableGridLayoutEdit341 and not DimensionDisplayEdit341 and not DimensionDefinitionEdit341 and not RegionBooleanEditSpec and not PropertiesSpec and not DimensionFormatSpec and not TableEditingSpec and not ReplaceMLeaderTextSpec and not ReplaceTableTextSpec and not ResizeTableSpec and not ReplaceDimensionTextSpec and not SetDimensionLineSpec and not SetDimensionTextPositionSpec and not SetHatchPatternSpec and not SetHatchPropertiesSpec) CheckOperationGeometry(entity, operation, job.CheckCancellation);
                    if (modifiedSet.Add(handle)) modified.Add(handle);
                }
            }
        }
        if (resolved.LegacyPolyline is { } legacyPlan) CadLegacyPolyline408.Complete(legacyPlan, tr, job.CheckCancellation, nativeFormatWarnings);
        var repetitions = args.Operations.Select((op, index) => (op, index)).Where(pair => CopyTransforms2d407.Handles(pair.op))
            .Select(pair => new RepetitionResult407(pair.index, pair.op.Operation, false, pair.op is PolarRepeat2d407 polar ? polar.Count : 2,
                pair.op.Handles.Select(Ref).ToArray(), copies.Where(copy => copy.operation_index == pair.index).Select(copy => copy.created).ToArray())).ToArray();
        return new(args.OperationId, "committed", false, modified.Where(h => !erased.Contains(h)).Select(Ref).ToArray(),
            erased.Select(Ref).ToArray(), copies.Select(c => (Index: c.operation_index, Reference: c.created))
                .Concat(separations.SelectMany(c => c.parts.Skip(1).Select(p => (Index: c.operation_index, Reference: p))))
                .Concat(slices.Select(c => (Index: c.operation_index, Reference: c.negative)))
                .Concat(contourResults.SelectMany(change => change.generated.Select(reference => (Index: change.operation_index, Reference: reference))))
                .OrderBy(created => created.Index).Select(created => created.Reference).ToArray(), copies.ToArray(), "single_command", contourResults.Count == 0 ? null : contourResults.ToArray(), solid_slice_results: slices.Count == 0 ? null : slices.ToArray(), solid_separation_results: separations.Count == 0 ? null : separations.ToArray(), solid_boolean_results: booleanChanges.Count == 0 ? null : booleanChanges.ToArray(), region_boolean_changes: regionChanges.Count == 0 ? null : regionChanges.ToArray(), mtext_format_changes: mtextChanges.Count == 0 ? null : mtextChanges.ToArray(), solid_stock_changes: stockChanges.Count == 0 ? null : stockChanges.ToArray(), table_topology_changes: topologyChanges.Count == 0 ? null : topologyChanges.ToArray(), tolerance_changes: toleranceChanges.Count == 0 ? null : toleranceChanges.ToArray(), repetition_results: repetitions.Length == 0 ? null : repetitions);
    }
    private static void CheckOperationGeometry(Entity entity, EditSpec operation, Action cancel)
    {
        if (entity.GetType() == typeof(Wipeout)) { _ = CadWipeout431.Geometry((Wipeout)entity, cancel); return; }
        if (CadConstructionCurve443.Is(entity)) CadConstructionCurve443.CheckGeometry(entity, cancel);
        else if (entity.GetType() == typeof(DBPoint)) CadPointEditor408.CheckGeometry(entity, cancel);
        else if (PolylineEditFrames431.Frame(operation) is { } polylineFrame) CadPolylineWorkplane431.Check(entity, polylineFrame, cancel);
        else if (CadPlaneCurveTransforms394.Frame(operation) is { } frame) CadPlaneCurveTransforms394.Check(entity, frame, cancel);
        else if (entity.GetType() == typeof(Spline) && NativeSplinePlacement407.Placement(operation)) NativeSplinePlacement407.CheckGeometry(entity, cancel);
        else CheckGeometry(entity, cancel, operation is Mirror2dSpec, (operation as CurveGeometrySpec)?.Workplane);
    }
    internal static void CheckGeometry(Entity entity, Action cancel, bool mirroredCurve = false, SketchPlaneSpec? workplane = null)
    {
        if (entity.GetType() == typeof(Wipeout)) { _ = CadWipeout431.Geometry((Wipeout)entity, cancel); return; }
        if (CadConstructionCurve443.Is(entity)) { CadConstructionCurve443.CheckGeometry(entity, cancel); return; }
        if (entity.GetType() == typeof(DBPoint)) { CadPointEditor408.CheckGeometry(entity, cancel); return; }
        if (workplane != null) { CadExplicitCurveGeometry388.Check(entity, workplane, cancel); return; }
        using var nativeBinding = CadNativeDimensionBinding357.Bind(entity); entity = nativeBinding.Value;
        if (CadAnnotationTransformEditor.Supported(entity.GetType())) { CadAnnotationTransformEditor.CheckGeometry(entity, cancel); return; }
        if (entity is Solid3d solid) { CadSolidEditor.CheckGeometry(solid, cancel); return; }
        void Number(double value, bool positive = false)
        {
            if (!double.IsFinite(value) || Math.Abs(value) > 1e9 || (positive && value < 1e-9))
                throw new DomainException("INVALID_GEOMETRY", "Stored geometry exceeds the finite 2D coordinate/size bounds.");
        }
        void Plane(double z)
        {
            if (!double.IsFinite(z) || Math.Abs(z) > 1e-9) throw new DomainException("OUT_OF_SCOPE", "Initial edits support WCS XY at Z=0 with zero thickness.");
        }
        void Position(Point3d p) { Number(p.X); Number(p.Y); Plane(p.Z); }
        void Normal(Vector3d n) { Plane(n.X); Plane(n.Y); Plane((mirroredCurve ? Math.Abs(n.Z) : n.Z) - 1); }
        void TextLength(string text)
        {
            if (text.Length > 16384) throw new DomainException("ENTITY_TOO_LARGE", "Editing supports text fields up to 16384 characters.");
        }
        switch (entity)
        {
            case Line line:
                Position(line.StartPoint); Position(line.EndPoint); Normal(line.Normal); Plane(line.Thickness);
                Number(line.StartPoint.DistanceTo(line.EndPoint), true); break;
            case Circle circle: Position(circle.Center); Number(circle.Radius, true); Normal(circle.Normal); Plane(circle.Thickness); break;
            case Ellipse ellipse:
                if (mirroredCurve) { var g = CadEllipseReader.Read(ellipse, cancel); EllipseVerification.Verify(g); Position(ellipse.Center); Normal(ellipse.Normal); Plane(ellipse.MajorAxis.Z); }
                else CurvedEditScope.Require(CadEllipseReader.Read(ellipse, cancel));
                var bounds = ellipse.GeometricExtents; Position(bounds.MinPoint); Position(bounds.MaxPoint); break;
            case Spline spline:
                if (spline.Closed)
                {
                    if (spline.HasFields || CadDrawingGroups431.HasNonOrganizationalReactors(spline, cancel: cancel)) throw new DomainException("UNSUPPORTED_SPLINE", "Closed spline edits require no fields or geometric persistent dependencies; selection Groups are retained.");
                }
                CurvedEditScope.Require(CadSplineReader.Read(spline, cancel)); break;
            case Arc arc: Position(arc.Center); Position(arc.StartPoint); Position(arc.EndPoint); Number(arc.Radius, true); Normal(arc.Normal); Plane(arc.Thickness); break;
            case Polyline polyline:
                if (polyline.NumberOfVertices < 2) throw new DomainException("INVALID_GEOMETRY", "Editing requires at least two polyline vertices.");
                Normal(polyline.Normal); Plane(polyline.Elevation); Plane(polyline.Thickness); Number(polyline.ConstantWidth);
                for (int i = 0; i < polyline.NumberOfVertices; i++)
                { cancel(); Position(polyline.GetPoint3dAt(i)); Number(polyline.GetBulgeAt(i)); Number(polyline.GetStartWidthAt(i)); Number(polyline.GetEndWidthAt(i)); }
                _ = CadContourBudget344.Geometry(polyline, cancel);
                break;
            case DBText text:
                Position(text.Position); Position(text.AlignmentPoint); Normal(text.Normal); Plane(text.Thickness);
                Number(text.Height, true); Number(text.WidthFactor, true); Number(text.Rotation); TextLength(text.TextString); break;
            case MText text:
                Position(text.Location); Normal(text.Normal); Plane(text.Direction.Z);
                Number(text.TextHeight, true); Number(text.Width); TextLength(text.Contents); break;
            default: throw new DomainException("UNSUPPORTED_ENTITY_TYPE", "Unsupported edit type.");
        }
    }
}

