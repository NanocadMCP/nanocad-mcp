using System.Globalization;
using System.Runtime.CompilerServices;
using NanoCAD.Contracts;
using Teigha.DatabaseServices;

namespace NanoCADBridge;

internal static class ModelHistoryReader
{
    public static object Read(Arguments args, DocumentState state, Transaction transaction, ObjectId modelId,
        string instance, CancellationToken cancellationToken, Action? cancel = null,bool includeProfileEditing=false)
    {
        // Keep optional mixed-mode types out of the core dispatcher/JIT path.
        // Do not load a modeling module as a side effect of a read request.
        var loaded = AppDomain.CurrentDomain.GetAssemblies().Select(a => a.GetName().Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (!loaded.Contains("mapimgd") || !loaded.Contains("MapiBaseTypes"))
            throw new DomainException("CAPABILITY_UNAVAILABLE", "MultiCAD runtime is not already loaded in this nanoCAD session.");
        foreach (string handle in args.Handles!)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var bits = ulong.Parse(handle, NumberStyles.HexNumber, CultureInfo.InvariantCulture);
            if (!state.Document.Database.TryGetObjectId(new Handle(unchecked((long)bits)), out var id) || id.IsNull || !id.IsValid)
                throw new DomainException("OBJECT_NOT_FOUND", "History source handle was not found.");
            if (id.IsErased || id.IsEffectivelyErased) throw new DomainException("OBJECT_ERASED", "History source was erased.");
            if (transaction.GetObject(id, OpenMode.ForRead) is not Entity entity || entity.OwnerId != modelId)
                throw new DomainException("OUT_OF_SCOPE", "History inspection requires top-level Model Space entities.");
            // A MultiCAD solid can use a native Solid3d as its DWG representation.
            // Resolve the MultiCAD identity below; the Teigha wrapper type alone
            // does not tell whether the object has MultiCAD history.
        }
        return ReadMultiCad(args, state, transaction, instance, cancellationToken, cancel,includeProfileEditing);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static object ReadMultiCad(Arguments args, DocumentState state, Transaction transaction, string instance, CancellationToken cancellationToken, Action? cancel,bool includeProfileEditing)
    {
        void Budget()
        {
            cancel?.Invoke();
            cancellationToken.ThrowIfCancellationRequested();
        }
        var document = Multicad.DatabaseServices.McDocument.ActiveDocument;
        if (document == null || !state.Document.IsNamedDrawing ||
            !string.Equals(Path.GetFullPath(document.Path), Path.GetFullPath(state.Document.Name), StringComparison.OrdinalIgnoreCase))
            throw new DomainException("DOCUMENT_CONTEXT_MISMATCH", "MultiCAD active document does not match the requested named DWG.");
        string? Id(Multicad.McObjectId id) => id.IsNull ? null : id.ToString();
        var items = new List<object>();var byteBudget=new ModelInventoryBudget(Budget);
        var historyNotices=new List<string>();var distinctNotices=new HashSet<string>(StringComparer.Ordinal);
        void Notice(string value)
        {Budget();if(distinctNotices.Add(value)){byteBudget.Add(value);historyNotices.Add(value);}}
        void ReadNotices(Multicad.Mc3D.HistoryItem node,string handle,Multicad.McObjectId partId,Multicad.McObjectId nodeId)
        {
            string scope=$"part_handle={handle}; part_id={Id(partId)}; node_id={Id(nodeId)}";
            try
            {
                Budget();var status=node.StatusInfo;
                if(status==null){Notice($"NATIVE_HISTORY_NOTICES_UNAVAILABLE: {scope}; native_status_info=null");return;}
                // SDK null/empty Records means no retained record information;
                // it is not proof that no transient UI notification occurred.
                var records=status.Records;if(records==null)return;
                for(int index=0;index<records.Count;index++)
                {
                    Budget();var record=records[index];
                    if(record==null){Notice($"NATIVE_HISTORY_NOTICES_UNAVAILABLE: {scope}; record_index={index}; native_record=null");continue;}
                    Notice($"NATIVE_HISTORY_NOTICE: {scope}; severity={record.Severity}({(int)record.Severity}); description={record.Description}");
                }
            }
            catch(Exception error)when(error is not (OperationCanceledException or DomainException or OutOfMemoryException))
            {Notice($"NATIVE_HISTORY_NOTICES_UNAVAILABLE: {scope}; error_type={error.GetType().Name}");}
        }
        var requestedSketches = new HashSet<string>(args.SketchGeometryIds ?? Array.Empty<string>(), StringComparer.OrdinalIgnoreCase);
        var foundSketches = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var handle in args.Handles!)
        {
            Budget();
            long bits = unchecked((long)ulong.Parse(handle, NumberStyles.HexNumber, CultureInfo.InvariantCulture));
            var id = Multicad.McObjectId.FromHandle(bits, document.ID);
            // FromHandle may return a GUID identity for a managed MultiCAD
            // object. Its internal Handle/IdHostDoc fields are not a DWG
            // roundtrip. Use the documented conversion with the verified
            // active document, then require lookup to retain that identity.
            if (id.IsNull)
                throw new DomainException("OBJECT_IDENTITY_MISMATCH", "DWG handle could not be mapped to this MultiCAD document.");
            var solid = id.GetObjectOfType<Multicad.Mc3D.Mc3dSolid>();
            if (solid == null) throw new DomainException("UNSUPPORTED_ENTITY_TYPE", "Source is not a MultiCAD Mc3dSolid.");
            if (solid.ID != id) throw new DomainException("OBJECT_IDENTITY_MISMATCH", "MultiCAD lookup returned a different object identity.");
            var contents = ModelInventoryBudget.History(solid,Budget);
            Budget();
            if (contents == null) throw new DomainException("HISTORY_UNAVAILABLE", "MultiCAD did not return the part contents; no empty-history claim is made.");
            var nodes = new List<object>();
            bool complete = true;
            var nativeSources=NativePathSources.Inspect(contents,Budget);
            ProjectedHistorySources.Source[] projected=[];
            string? projectedFailure=null;
            try{projected=ProjectedHistorySources.Read(solid,contents.ToArray(),state,transaction,Budget);}
            catch(DomainException error){projectedFailure=error.Error.code+": "+error.Message;}
            var projectedSources=projected.ToDictionary(s=>s.id,StringComparer.OrdinalIgnoreCase);
            foreach (var nodeId in contents)
            {
                Budget();
                var node = nodeId.GetObjectOfType<Multicad.Mc3D.HistoryItem>();
                if (node == null)
                {
                    bool verified=nativeSources.Details.TryGetValue(nodeId.ToString(),out var sourceDetail);
                    if(!verified&&projectedSources.TryGetValue(nodeId.ToString(),out var source)){
                        verified=true;sourceDetail=new{kind="projected_source",source.id,source.handle,source.projection_ids,source.revolution_axis_ids,source.rotation_anchor_ids,verification="current_native_identity_and_projection_owner"};
                    }
                    string? reason=nativeSources.Failures.TryGetValue(nodeId.ToString(),out var failure)?failure:verified?null:projectedFailure??"not_a_history_item_or_verified_source";
                    if(reason!=null)complete=false;
                    nodes.Add(new { mc_id = nodeId.ToString(), runtime_type = nodeId.QueryObjectType()?.FullName ?? "Unknown", name = (string?)null,
                        status = (string?)null, suppressed = (bool?)null, host_solid_id = (string?)null, native_order_before_id = (string?)null,
                        dependencies = Array.Empty<string>(), parameters = sourceDetail, detail_unavailable_reason = reason });
                    byteBudget.Add(nodes[^1]);
                    continue;
                }
                if(includeProfileEditing)ReadNotices(node,handle,id,nodeId);
                var dependencies = node.GetDependsOn(false, false);
                if (dependencies == null) throw new DomainException("HISTORY_UNAVAILABLE", "MultiCAD did not return dependencies for a history item.");
                object? parameters = null;
                if (requestedSketches.Contains(nodeId.ToString()))
                {
                    var sketch = nodeId.GetObjectOfType<Multicad.Mc3D.PlanarSketch>();
                    if (sketch == null || sketch.ID != nodeId || node.HostSolidID != id)
                        throw new DomainException("OUT_OF_SCOPE", "Selected sketch must belong to the requested body's current history.");
                    parameters = SketchGeometryReader.Read(sketch, Budget);
                    foundSketches.Add(nodeId.ToString());
                }
                // GetObjectOfType<HistoryItem> deliberately casts to the base
                // wrapper. Ask for the specific SDK interface to read subtype
                // parameters; CLR `is` on the base wrapper loses that subtype.
                // Public editable-profile detail is dynamic selected geometry. Keep
                // the old private preservation snapshot stable for existing writers.
                var profile=includeProfileEditing?nodeId.GetObjectOfType<Multicad.Mc3D.SketchProfile>():null;
                if(parameters==null&&profile!=null)
                    parameters=ModelProfileLifecycleState340.Read(profile,solid,state,transaction,Budget);
                var extrusion = nodeId.GetObjectOfType<Multicad.Mc3D.ExtrudeFeature>();
                if (parameters == null && extrusion != null)
                    parameters = new { kind = "extrusion", profile_id = Id(extrusion.ProfileID), part_operation=extrusion.PartOperation.ToString(),extent = extrusion.Extent.ToString(),
                        profile_rebind=includeProfileEditing?ModelProfileRebindReader349.Describe(extrusion):null,
                        native_order_before_id=Id(extrusion.OrderObjBefore),terminal=ModelExtrusionExtentEditor.Read(extrusion),
                        direction = extrusion.Direction.ToString(), distance = Finite(extrusion.Distance), taper_angle = Finite(extrusion.Angle),
                        extent2 = extrusion.Extent2.ToString(), distance2 = Finite(extrusion.Distance2), taper_angle2 = Finite(extrusion.Angle2),
                        distance_variable_id = Id(extrusion.DistanceParamVarID), taper_angle_variable_id = Id(extrusion.TaperAngleParamVarID),
                        distance2_variable_id = Id(extrusion.Distance2ParamVarID), taper_angle2_variable_id = Id(extrusion.TaperAngle2ParamVarID),
                        distance_unit = "drawing_units", angle_unit = "radians" };
                else if (parameters == null)
                {
                    var revolution=nodeId.GetObjectOfType<Multicad.Mc3D.RevolveFeature>();
                    var projection=nodeId.GetObjectOfType<Multicad.Mc3D.ProjectionSketchEnt>();
                    var pattern=nodeId.GetObjectOfType<Multicad.Mc3D.RectangularPatternFeature>();
                    var shell=nodeId.GetObjectOfType<Multicad.Mc3D.ThicknessFeature>();
                    var suppressed=pattern?.GetSuppressedElementsIndices();
                    var directionX=pattern==null?null:NativePatternObservation439.TryRead(pattern,0);
                    var directionY=pattern==null?null:NativePatternObservation439.TryRead(pattern,1);
                    // For subfeature arrays the SDK's source-item IDs identify
                    // internal copies. Report the original history dependency
                    // separately so callers do not mistake a copy for its source.
                    parameters=shell!=null?new{kind="shell",surface_id=Id(shell.SurfaceId),direction=shell.Direction.ToString(),
                        open_face_ids=(shell.OutFaces??[]).Select(face=>face.ToString()).ToArray(),
                        open_faces_complete=shell.OutFaces!=null,
                        host_feature_id=Id(shell.HostFeatureID),
                        thickness=Finite(shell.GetFaceThickness(Multicad.McObjectId.Null)),thickness_unit="drawing_units"}
                        :pattern!=null?new{kind="rectangular_pattern",native_source_item_ids=Enumerable.Range(0,pattern.SourceItemsCount).Select(index=>{Budget();return Id(pattern.GetSourceItemFeature(index));}).ToArray(),
                        source_history_feature_id=ModelPatternSources416.LegacySource(pattern,contents.ToArray(),Budget),
                        count_x=pattern.SizeX,count_y=pattern.SizeY,step_x=Finite(pattern.DistancesX),step_y=Finite(pattern.DistancesY),
                        count_x_variable_id=Id(pattern.SizeXParamVarID),count_y_variable_id=Id(pattern.SizeYParamVarID),
                        step_x_variable_id=Id(pattern.DistancesXParamVarID),step_y_variable_id=Id(pattern.DistancesYParamVarID),
                        direction_x=directionX,direction_y=directionY,direction_bindings_complete=directionX!=null&&directionY!=null,
                        source_items_count=pattern.SourceItemsCount,source_items_complete=true,has_sub_feature_items=pattern.HasSubFeatureItems,
                        suppressed_elements=(suppressed??[]).Select(index=>{Budget();return new[]{index.Index1,index.Index2};}).ToArray(),
                        suppressed_elements_complete=suppressed!=null}:revolution!=null?new{kind="revolution",profile_id=Id(revolution.ProfileID),axis_object_id=Id(revolution.Axis.ID),part_operation=revolution.PartOperation.ToString(),full_turn=revolution.IsFull,
                        extent=revolution.Extent.ToString(),extent2=revolution.Extent2.ToString(),terminal=ModelRevolutionTerminationReader377.Read(revolution),direction=revolution.Direction.ToString(),angle=Finite(revolution.Angle),angle2=Finite(revolution.Angle2),angle_unit="radians",angle_variable_id=Id(revolution.RotationAngleParamVarID),angle2_variable_id=Id(revolution.RotationAngle2ParamVarID)}:projection!=null
                        ?new { kind="projection",source_object_id=Id(projection.SrcObjectID),host_sketch_id=Id(projection.HostSketchID),planar_object_id=Id(projection.PlanarObjectID) }
                        :ModelManufacturingHistory.Parameters(nodeId,Budget)??ModelEdgeFeatureHistory.Parameters(nodeId)??ModelPlacementHistory.Parameters(nodeId)??SheetMetalHistory.Read(nodeId,id);
                }
                string? nativeReferenceFailure=nativeSources.Failures.TryGetValue(nodeId.ToString(),out var referenceFailure)?referenceFailure:null;
                if(nativeReferenceFailure!=null)complete=false;
                nodes.Add(new { mc_id = nodeId.ToString(), runtime_type = nodeId.QueryObjectType()?.FullName ?? node.GetType().FullName!, name = Limit(node.Name),
                    status = node.Status.ToString(), suppressed = node.IsSuppressed, host_solid_id = Id(node.HostSolidID), native_order_before_id=Id(node.OrderObjBefore),
                    dependencies = dependencies.Select(x => x.ToString()).ToArray(), parameters, detail_unavailable_reason = nativeReferenceFailure });
                byteBudget.Add(nodes[^1]);
            }
            items.Add(new { @ref = new EntityRef(instance, state.Id, handle), mc_id = id.ToString(), mc_document_id = document.ID.ToString(),
                last_item_id = Id(solid.LastPartItemID), complete, nodes,edit_routes=ModelEditRoutes.Describe(nodes) });
        }
        Budget();
        if (!requestedSketches.SetEquals(foundSketches))
            throw new DomainException("OUT_OF_SCOPE", "One or more selected sketches are absent from the requested bodies' current history.");
        ModelProfileDiscoveryRead348? discovery = includeProfileEditing &&
            (args.IncludeDocumentProfiles || args.ModelProfileIds?.Length > 0)
            ? ModelProfileDiscovery348.Read(state, transaction, Budget, args.IncludeDocumentProfiles,
                args.DocumentProfileCursor, args.ModelProfileIds, args.Handles!)
            : null;
        var sketches=includeProfileEditing&&(args.IncludeDocumentSketches||args.ModelSketchIds?.Length>0)
            ?ModelSketchLifecycleReader362.Read(state,transaction,Budget,args.IncludeDocumentSketches,args.DocumentSketchCursor,args.ModelSketchIds,args.Handles!,instance):null;
        var elements=includeProfileEditing&&args.ModelProfileElementIds?.Length>0
            ?ModelProfileElementsWriter358.Read(args.Handles!.Single(),args.ModelProfileElementIds,args,state,transaction,Budget):null;
        var regions=includeProfileEditing&&args.ModelProfileRegionIds?.Length>0
            ?ModelProfileRegionsWriter350.Read(args.Handles!.Single(),args.ModelProfileRegionIds,args,state,transaction,Budget):null;
        var extents=includeProfileEditing&&args.ModelExtrusionExtentIds?.Length>0
            ?ModelExtrusionExtentsReader351.Read(args.Handles!.Single(),args.ModelExtrusionExtentIds,args,state,transaction,Budget):null;
        var datums=includeProfileEditing&&(args.IncludeDocumentDatumPlanes||args.ModelDatumPlaneIds?.Length>0)
            ?ModelDatumPlaneReader353.Read(state,transaction,Budget,args.IncludeDocumentDatumPlanes,args.DocumentDatumCursor,args.ModelDatumPlaneIds,args.Handles!):null;
        var parameterEdits=includeProfileEditing&&args.SketchConstraintParameterIds?.Length>0
            ?ModelSketchConstraintParameterReader379.Read(args,state,transaction,Budget):null;
        var deletions=includeProfileEditing&&args.ModelFeatureDeletionIds?.Length>0
            ?ModelFeatureDeletionReader378.Read(args,state,transaction,Budget):null;
        var datumRevisions=includeProfileEditing&&args.ModelDatumPlaneRevisionIds?.Length>0
            ?ModelDatumPlaneRevisionReader354.Read(args.Handles!.Single(),args.ModelDatumPlaneRevisionIds,args,state,transaction,Budget):null;
        if (discovery == null&&sketches==null&&elements==null&&regions==null&&extents==null&&datums==null&&datumRevisions==null&&deletions==null&&parameterEdits==null)
        {
            object plain=new { identity_scope = "bridge_document_revision_no_persistent_face_identity",
                graph_scope = "part_contents_and_direct_dependencies_no_recursive_closure_or_order_claim", items };
            return includeProfileEditing?new ModelSketchConstraintObservation353.HistoryOutcome(plain,historyNotices.ToArray()):plain;
        }
        object rich = new { identity_scope = "bridge_document_revision_no_persistent_face_identity",
            graph_scope = "part_contents_and_direct_dependencies_no_recursive_closure_or_order_claim", items,
            document_sketch_inventory=sketches?.Inventory,document_sketches=sketches?.Sketches??[],document_profile_inventory = discovery?.Inventory, document_profiles = discovery?.Profiles??[],profile_regions=regions??[],profile_elements=elements??[],extrusion_extents=extents??[],document_datum_plane_inventory=datums?.Inventory,datum_planes=datums?.Planes??[],datum_plane_revisions=datumRevisions??[],feature_deletions=deletions??[],sketch_constraint_parameter_edits=parameterEdits??[] };
        var observationWarnings=ModelSketchConstraintObservation353.Warnings((extents??[]).SelectMany(value=>value.constraint_observations).Concat((regions??[]).Select(value=>value.constraint_observation)).Concat((elements??[]).Select(value=>value.constraint_observation)));
        return new ModelSketchConstraintObservation353.HistoryOutcome(rich,observationWarnings.Concat(NativeAssocManagerObservation356.Warnings((extents??[]).SelectMany(value=>value.owned_metadata_observations))).Concat(historyNotices).Distinct(StringComparer.Ordinal).ToArray());
    }
    private static string Limit(string text) => text; // Complete native names are accounted by ModelInventoryBudget with the whole item.
    private static double Finite(double value) => double.IsFinite(value) ? value : throw new DomainException("INVALID_GEOMETRY", "Non-finite feature parameter.");
}
