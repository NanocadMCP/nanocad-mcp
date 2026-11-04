using System.Diagnostics;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Nodes;
using Multicad;
using NanoCAD.Contracts;
using Teigha.DatabaseServices;
using G=Multicad.Geometry;

namespace NanoCADBridge;

internal static class DrawingViewsReader
{
    private sealed record LocalSource(McObjectId Id, EntityRef Ref);
    internal sealed record ProjectedLine(McObjectId ViewId, int Marker,
        Teigha.Geometry.Point3d Start, Teigha.Geometry.Point3d End);
    private sealed record SourceEdge(McObjectId Id, string RefKey);
    private sealed record ProjectionSource(string status, string? unavailable_reason, string? view_marker,
        EntityRef? source_ref, string? native_edge_id, string? native_ref_key)
    {
        internal static ProjectionSource Unavailable(string reason) => new("unavailable", reason, null, null, null, null);
    }

    private readonly record struct LineEnds((double X, double Y, double Z) Start, (double X, double Y, double Z) End)
    {
        internal static LineEnds Read(G.LineSeg3d line)
        {
            var start = line.StartPoint;
            var end = line.EndPoint;
            return new((Finite(start.X), Finite(start.Y), Finite(start.Z)), (Finite(end.X), Finite(end.Y), Finite(end.Z)));
        }

        internal LineEnds Canonical() => Start.CompareTo(End) <= 0 ? this : new(End, Start);
    }

    private sealed record LineIndex(Dictionary<int, LineEnds?> Markers, Dictionary<LineEnds, int> Occurrences)
    {
        internal bool ContainsUnique(int marker, LineEnds line) =>
            Markers.TryGetValue(marker, out var ends) && ends == line && Occurrences.GetValueOrDefault(line) == 1;
    }

    private static T? NativeProjection<T>(Func<T?> read) where T : class
    {
        try { return read(); }
        catch (Exception e) when (e is NotSupportedException or NotImplementedException or Teigha.Runtime.Exception) { return null; }
    }

    private static LineIndex IndexLines(IEnumerable<G.EntityGeometry> geometry, Action cancel)
    {
        var index = new LineIndex(new(), new());
        foreach (var item in geometry)
        {
            cancel();
            if (item == null) continue;
            LineEnds? ends = null;
            if (item.GeometryType == G.EntityGeomType.kLine)
            {
                using var line = item.LineSeg;
                var canonical = LineEnds.Read(line).Canonical();
                index.Occurrences[canonical] = index.Occurrences.GetValueOrDefault(canonical) + 1;
                ends = canonical;
            }
            if (item.GSMarker >= 0 && !index.Markers.TryAdd(item.GSMarker, ends)) index.Markers[item.GSMarker] = null;
        }
        return index;
    }

    private static LineIndex? ReadVisibleMarkers(Multicad.Mc3D.DrawingView view, McObjectId source, Action cancel)
    {
        // The managed SDK copies these native projection items and applies the requested WCS transform.
        var visible = NativeProjection(() => view.GetProjectedGeometryForObject(source, true, true));
        if (visible == null) return null;
        try
        {
            return IndexLines(visible, cancel);
        }
        finally
        {
            foreach (var item in visible.Distinct(ReferenceEqualityComparer.Instance))
                if (item is IDisposable owned) owned.Dispose();
        }
    }

    private const string SourceMismatch = "native_marker_source_not_exact_local_solid";
    private const string NonUniqueLine = "no_unique_matching_visible_projection_marker";

    private static (SourceEdge? Edge, string? Reason) ReadProjectionSource(Multicad.Mc3D.DrawingView view, LocalSource source, int marker)
    {
        using var projected = NativeProjection(() => view.GetProjectedObjectByGsm(marker));
        bool hasProjectedId = projected != null && !projected.IsNull;
        if (hasProjectedId && (projected!.IsXref || projected.Objects is not { Count: 1 } path ||
            path[0] != source.Id || projected.LeafId != source.Id))
            return (null, SourceMismatch);
        bool needsEdgeIdentity = hasProjectedId && (projected!.TopId != source.Id || projected.TargetId != source.Id);
        (SourceEdge?, string) UnavailableEdge(string reason) => (null, needsEdgeIdentity ? SourceMismatch : reason);

        // Source-specific visible geometry already established ownership of this marker.
        // The native subentity key lookup is independent of the optional object lookup.
        using var key = NativeProjection(() => view.GetProjectedFevRefKeyByGsm(marker));
        if (key == null || key.IsNull) return UnavailableEdge("native_projection_edge_key_unavailable");
        McObjectId edge;
        try { edge = McObjectId.FromRefKey(source.Id, key); }
        catch (Exception e) when (e is NotSupportedException or NotImplementedException or Teigha.Runtime.Exception)
        { return UnavailableEdge("native_projection_edge_cannot_resolve_exactly"); }
        if (edge.IsNull || !edge.IsEdge || edge.IsInexactFEV) return UnavailableEdge("native_projection_edge_cannot_resolve_exactly");
        if (needsEdgeIdentity && ((projected!.TopId != source.Id && projected.TopId != edge) ||
            (projected.TargetId != source.Id && projected.TargetId != edge))) return (null, SourceMismatch);
        if (edge.ParentId != source.Id) return (null, SourceMismatch);
        using var resolvedKey = NativeProjection(() => edge.RefKey);
        if (resolvedKey == null || resolvedKey.IsNull || !resolvedKey.Equals(key))
            return UnavailableEdge("native_projection_edge_key_does_not_round_trip");
        return (new(edge, key.ToString()), null);
    }

    private static LocalSource? OrdinarySource(Multicad.Mc3D.DrawingView view, ICollection<McObjectId>? nativeSources,
        EntityRef[]? refs, bool noAttachedBreaks)
    {
        if (nativeSources is not { Count: 1 } || refs is not [var sourceRef] ||
            view.Status.ToString() != "Ok" || view.IsSuppressed || !view.SectionID.IsNull || view.IsBasedOnSection ||
            !noAttachedBreaks || view.AttachedLocalCuts is not { Length: 0 } || !view.OuterCutSketch.IsNull)
            return null;
        return new(nativeSources.Single(), sourceRef);
    }

    internal static ProjectedLine ResolveProjectedLine(Entity entity, DocumentState state, Transaction tx,
        string instance, int index, Action cancel)
    {
        var database = state.Document.Database;
        var document = Multicad.DatabaseServices.McDocument.ActiveDocument;
        if (document == null || !state.Document.IsNamedDrawing ||
            !string.Equals(Path.GetFullPath(document.Path), Path.GetFullPath(state.Document.Name), StringComparison.OrdinalIgnoreCase))
            throw new DomainException("DOCUMENT_CONTEXT_MISMATCH", "MultiCAD active document differs from the requested document.");
        if (entity.Database != database || entity.IsErased || entity.OwnerId.IsNull ||
            tx.GetObject(entity.OwnerId, OpenMode.ForRead) is not BlockTableRecord { IsLayout: true } block || block.OwnerId != database.BlockTableId)
            throw new DomainException("OUT_OF_SCOPE", "Select a current DrawingView in a layout of the requested drawing.");
        var viewId = McObjectId.FromHandle(entity.Handle.Value, document.ID);
        var view = viewId.IsNull ? null : viewId.GetObjectOfType<Multicad.Mc3D.DrawingView>();
        if (view == null || view.ID != viewId)
            throw new DomainException("OBJECT_IDENTITY_MISMATCH", "The selected DrawingView identity is unavailable.");
        var nativeSources = view.GetTop3dEntitiesInView();
        var sourceRefs = CadDrawingViewLifecycleState345.SourceRefs(nativeSources, database, tx, instance, state.Id, cancel);
        var source = OrdinarySource(view, nativeSources, sourceRefs.Refs, view.Attached2dBreaks is { Length: 0 })
            ?? throw new DomainException("DRAWING_VIEW_SOURCE_UNAVAILABLE", "Select a healthy ordinary projection of one local model solid.");
        cancel();
        var geometry = view.GetViewGeometry(true, 0);
        if (geometry == null || geometry.Count == 0)
            throw new DomainException("VIEW_GEOMETRY_UNAVAILABLE", "Selected native drawing view has no projected geometry.");
        try
        {
            if (index < 0 || index >= geometry.Count || geometry[index] is not { GeometryType: G.EntityGeomType.kLine } item)
                throw new DomainException("VIEW_GEOMETRY_UNAVAILABLE", "The selected projection index is not a current native line.");
            using var line = item.LineSeg;
            var ends = LineEnds.Read(line);
            var canonical = ends.Canonical();
            int marker = item.GSMarker;
            if (marker < 0 || !IndexLines(geometry, cancel).ContainsUnique(marker, canonical) ||
                ReadVisibleMarkers(view, source.Id, cancel)?.ContainsUnique(marker, canonical) != true)
                throw new DomainException("VIEW_GEOMETRY_UNAVAILABLE", "The selected line has no unique visible native projection marker.");
            var binding = ReadProjectionSource(view, source, marker);
            if (binding.Reason == SourceMismatch)
                throw new DomainException("DRAWING_VIEW_SOURCE_UNAVAILABLE", "The native projection marker reports a conflicting source identity.");
            return new(viewId, marker,
                new(ends.Start.X, ends.Start.Y, ends.Start.Z), new(ends.End.X, ends.End.Y, ends.End.Z));
        }
        finally
        {
            foreach (var item in geometry.Distinct(ReferenceEqualityComparer.Instance))
                if (item is IDisposable owned) owned.Dispose();
        }
    }

    private static object ReadGeometry(Multicad.Mc3D.DrawingView view,Entity entity,string instance,string document,int start,int pageSize,
        bool includeSources, LocalSource? source, Action budget)
    {
        budget();
        var geometry=view.GetViewGeometry(true,0);
        if(geometry==null||geometry.Count==0)
            throw new DomainException("VIEW_GEOMETRY_UNAVAILABLE","Selected native drawing view has no projected geometry.");
        try
        {
            if(start>geometry.Count)throw new DomainException("INVALID_ARGUMENT","geometry_start_index exceeds the selected projection inventory; refresh the current view/revision.");
            var items=new List<object>();bool complete=true;long bytes=2048;
            int end=start+Math.Min(pageSize,geometry.Count-start);
            LineIndex? allLines = null;
            LineIndex? visible = null;
            if (includeSources && source != null && start < end)
            {
                visible = ReadVisibleMarkers(view, source.Id, budget);
                if (visible != null) allLines = IndexLines(geometry, budget);
            }
            for(int index=start;index<end;index++)
            {
                budget();var item=geometry[index];object? detail=null;
                LineEnds? lineEnds = null;
                if(item?.GeometryType==G.EntityGeomType.kLine)
                {using var line=item.LineSeg;detail=SketchGeometryReader.Line(line);if(includeSources)lineEnds=LineEnds.Read(line);}
                else if(item?.GeometryType==G.EntityGeomType.kCircArc)
                {using var arc=item.CircArc;detail=SketchGeometryReader.Arc(arc);}
                object entry=new{index,native_geometry_type=item?.GeometryType.ToString(),
                    status=detail==null?"unsupported_geometry":"read",geometry=detail,
                    unavailable_reason=detail==null?"native_geometry_type_not_serialized":(string?)null};
                if (includeSources)
                {
                    ProjectionSource observation;
                    int marker = item?.GSMarker ?? -1;
                    if (source == null) observation = ProjectionSource.Unavailable("requires_healthy_ordinary_projection_of_one_local_solid");
                    else if (lineEnds == null) observation = ProjectionSource.Unavailable("only_visible_native_line_is_supported");
                    else if (visible == null) observation = ProjectionSource.Unavailable("native_visible_projection_unavailable");
                    else if (marker < 0 || !allLines!.ContainsUnique(marker, lineEnds.Value.Canonical()) ||
                        !visible.ContainsUnique(marker, lineEnds.Value.Canonical()))
                        observation = ProjectionSource.Unavailable(NonUniqueLine);
                    else
                    {
                        var binding = ReadProjectionSource(view, source, marker);
                        observation = binding.Edge is { } edge
                            ? new("observed", null, marker.ToString(CultureInfo.InvariantCulture), source.Ref, edge.Id.ToString(), edge.RefKey)
                            : ProjectionSource.Unavailable(binding.Reason!);
                    }
                    var row = JsonSerializer.SerializeToNode(entry, Wire.Json)!.AsObject();
                    row["projection_source"] = JsonSerializer.SerializeToNode(observation, Wire.Json);
                    entry = row;
                }
                int size=JsonSerializer.SerializeToUtf8Bytes(entry,Wire.Json).Length+1;
                if(bytes+size>Wire.PayloadBudget-8192)
                {
                    if(items.Count==0)throw new DomainException("RESPONSE_LIMIT_EXCEEDED","One native projected element exceeds the actual serialized-byte budget.");
                    break;
                }
                bytes+=size;items.Add(entry);if(detail==null)complete=false;
            }
            return new{view_ref=new EntityRef(instance,document,entity.Handle.ToString()),
                coordinate_system="WCS",unit="drawing_units",geometry_details_complete=complete,
                element_identity="projection_order_only_not_persistent_subentity_ids",geometry_scope="requested_projection_page",
                total_elements=geometry.Count,start_index=start,next_index=start+items.Count<geometry.Count?(int?)(start+items.Count):null,
                complete=start+items.Count==geometry.Count,items};
        }
        finally
        {
            foreach(var item in geometry.Distinct(ReferenceEqualityComparer.Instance))
                if(item is IDisposable disposable)disposable.Dispose();
        }
    }
    public static object Read(Arguments args,DocumentState state,Transaction tx,string instance,CancellationToken cancellation)
    {
        var loaded=AppDomain.CurrentDomain.GetAssemblies().Select(a=>a.GetName().Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        if(!loaded.Contains("mapimgd") || !loaded.Contains("MapiBaseTypes"))throw new DomainException("CAPABILITY_UNAVAILABLE","MultiCAD runtime is not already loaded.");
        var db=state.Document.Database;
        long bits=unchecked((long)ulong.Parse(args.Handles![0],NumberStyles.HexNumber,CultureInfo.InvariantCulture));
        if(!db.TryGetObjectId(new Handle(bits),out var id) || id.IsNull || !id.IsValid)throw new DomainException("OBJECT_NOT_FOUND","Layout not found.");
        if(id.IsErased || id.IsEffectivelyErased)throw new DomainException("OBJECT_ERASED","Layout erased.");
        if(tx.GetObject(id,OpenMode.ForRead) is not Layout layout || layout.OwnerId!=db.LayoutDictionaryId)
            throw new DomainException("OUT_OF_SCOPE","layout_handle must identify a layout in the requested drawing.");
        var block=(BlockTableRecord)tx.GetObject(layout.BlockTableRecordId,OpenMode.ForRead);
        if(!block.IsLayout || block.OwnerId!=db.BlockTableId)throw new DomainException("OUT_OF_SCOPE","Invalid layout block record.");
        return ReadMultiCad(args,state,tx,layout,block,instance,cancellation);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static object ReadMultiCad(Arguments args,DocumentState state,Transaction tx,Layout layout,BlockTableRecord block,string instance,CancellationToken cancellation)
    {
        var watch=Stopwatch.StartNew();int scanned=0;
        void Budget(){cancellation.ThrowIfCancellationRequested();}
        var document=Multicad.DatabaseServices.McDocument.ActiveDocument;
        if(document==null || !state.Document.IsNamedDrawing || !string.Equals(Path.GetFullPath(document.Path),Path.GetFullPath(state.Document.Name),StringComparison.OrdinalIgnoreCase))
            throw new DomainException("DOCUMENT_CONTEXT_MISMATCH","MultiCAD active document differs from the requested document.");
        string? Id(Multicad.McObjectId id)=>id.IsNull?null:id.ToString();
        string[]? Ids(ICollection<Multicad.McObjectId>? ids)
        {
            if(ids==null)return null;
            
            return ids.Select(x=>x.ToString()).ToArray();
        }
        double[] Vector(Multicad.Geometry.Vector3d p)=>new[]{Finite(p.X),Finite(p.Y),Finite(p.Z)};
        double[] Point(Multicad.Geometry.Point3d p)=>new[]{Finite(p.X),Finite(p.Y),Finite(p.Z)};
        object Plane(Multicad.Geometry.Plane3d plane)=>new{origin=Point(plane.Origin),axis_u=Vector(plane.DirU),axis_v=Vector(plane.DirV),normal=Vector(plane.Normal)};
        var items=new List<object>();
        var found=new List<(Entity Entity,Multicad.McObjectId Id,Multicad.Mc3D.DrawingView View)>();
        var layoutHandles=new Dictionary<Multicad.McObjectId,List<string>>();
        LocalSource? selectedSource = null;
        foreach(ObjectId nativeId in block)
        {
            Budget();scanned++;
            if(nativeId.IsErased || nativeId.IsEffectivelyErased || nativeId.ObjectClass.DxfName.Equals("3DSOLID",StringComparison.OrdinalIgnoreCase))continue;
            if(tx.GetObject(nativeId,OpenMode.ForRead) is not Entity entity || entity.OwnerId!=block.ObjectId)
                throw new DomainException("OBJECT_READ_FAILED","Invalid layout member.");
            var id=Multicad.McObjectId.FromHandle(entity.Handle.Value,document.ID);
            if(id.IsNull)continue;
            if(!layoutHandles.TryGetValue(id,out var handles)){handles=new List<string>();layoutHandles.Add(id,handles);}
            handles.Add(entity.Handle.ToString());
            var view=id.GetObjectOfType<Multicad.Mc3D.DrawingView>();
            if(view==null)continue;
            if(view.ID!=id)throw new DomainException("OBJECT_IDENTITY_MISMATCH","DrawingView lookup returned a different identity.");
            
            found.Add((entity,id,view));
        }
        foreach(var entry in found)
        {
            Budget();var entity=entry.Entity;var id=entry.Id;var view=entry.View;
            var children=Ids(view.ChildViews)??Array.Empty<string>();
            var nativeSources = view.GetTop3dEntitiesInView();
            var sources=Ids(nativeSources);Budget();
            var freshness=CadDrawingViewLifecycleState345.TryToken(view,entity,layout.ObjectId,tx,Budget);
            var sourceRefs=CadDrawingViewLifecycleState345.SourceRefs(nativeSources,state.Document.Database,tx,instance,state.Id,Budget);
            var attached=view.Attached2dBreaks;
            if(attached==null)throw new DomainException("MODEL_STATE_UNAVAILABLE","Native break inventory is unavailable.");
            if (args.IncludeGeometrySources && Validation.NormalizeHandle(entity.Handle.ToString()) == args.GeometryViewHandle)
                selectedSource = OrdinarySource(view, nativeSources, sourceRefs.Refs, attached.Length == 0);
            
            var breakData=DrawingViewBreakReader.Read(attached,id,layoutHandles,instance,state.Id,Budget);
            object? sectionData=null;SectionCutRead351? sectionCut=null;string? sectionCutReason=null;
            if(!view.SectionID.IsNull)
            {
                var section=view.SectionID.GetObjectOfType<Multicad.Mc3D.Section>();
                if(section==null || section.ID!=view.SectionID)throw new DomainException("OBJECT_IDENTITY_MISMATCH","View section identity unavailable.");
                var metadata=SectionViewState.Read(section,Budget);
                
                
                sectionData=metadata;
                (sectionCut,sectionCutReason)=CadSectionCutState351.Describe(section,state,tx,instance,Budget);
            }
            items.Add(new{@ref=new EntityRef(instance,state.Id,entity.Handle.ToString()),mc_id=id.ToString(),name=Text(view.Name),
                status=view.Status.ToString(),suppressed=view.IsSuppressed,direction=view.DirectionType.ToString(),
                native_owner_document_id=Id(view.DbEntity.DocumentID),
                section_id=Id(view.SectionID),section=sectionData,section_cut=sectionCut,section_cut_unavailable_reason=sectionCutReason,parent_view_id=Id(view.ParentViewID),root_view_id=Id(view.RootViewID),child_view_ids=children,
                attached_break_ids=attached.Select(i=>i.ToString()).ToArray(),breaks=breakData.Items,breaks_unavailable_reason=breakData.UnavailableReason,
                top_source_ids=sources,source_inventory_available=sources!=null,view_plane=Plane(view.ViewPlane),projection_plane=Plane(view.ProjPlane),
                view_token=freshness.Token,view_token_unavailable_reason=freshness.Reason,
                source_refs=sourceRefs.Refs,source_refs_unavailable_reason=sourceRefs.Reason,
                model_scale=Finite(view.DbEntity.DScale),design_scale=Finite(view.DbEntity.Scale),
                is_slice=view.IsSlice,is_based_on_section=view.IsBasedOnSection,is_root_section_view=view.IsRootSectionView,
                depends_on_slice=view.DependsOnSlice,auto_update_enabled=view.EnableAutoUpdate,
                align_on_parent=view.AlignOnParent,attached_local_cut_ids=view.AttachedLocalCuts?.Select(cut=>cut.ToString()).ToArray(),outer_cut_sketch_id=Id(view.OuterCutSketch),
                show_hidden_lines=view.ShowHiddenLines,show_hatches=view.ShowHatches,show_tangent_edges=view.ShowTangentEdges});
        }
        object? selectedGeometry=null;
        if(args.GeometryViewHandle!=null)
        {
            var selected=found.SingleOrDefault(x=>Validation.NormalizeHandle(x.Entity.Handle.ToString())==args.GeometryViewHandle);
            if(selected.View==null)throw new DomainException("OUT_OF_SCOPE","geometry_view_handle is not a native drawing view in the requested layout.");
            selectedGeometry=ReadGeometry(selected.View,selected.Entity,instance,state.Id,args.GeometryStartIndex,args.GeometryPageSize,
                args.IncludeGeometrySources,selectedSource,Budget);
        }
        Budget();
        var result=new{layout_ref=new EntityRef(instance,state.Id,args.Handles![0]),layout_name=Text(layout.LayoutName),space=layout.ModelType?"model":"paper",
            identity_scope="bridge_document_revision",reference_scope="opaque_native_ids_no_external_traversal",coordinate_system="native_view_planes",
            geometry_state=selectedGeometry==null?"stored_metadata_not_regenerated":"selected_native_projection_without_regeneration",
            selected_geometry=selectedGeometry,scanned_entities=scanned,complete=true,items};
        if(args.DrawingViewSourceIdentityHandle is {} sourceHandle)
        {
            var selected=found.SingleOrDefault(item=>Validation.NormalizeHandle(item.Entity.Handle.ToString())==sourceHandle);
            if(selected.View==null)throw new DomainException("OUT_OF_SCOPE","source_identity_view_handle is not a native view in the explicitly selected layout.");
            SectionSourceSelectionRead357? selection=null;
            if(!selected.View.SectionID.IsNull)
            {
                var section=selected.View.SectionID.GetObjectOfType<Multicad.Mc3D.Section>();
                if(section==null||section.ID!=selected.View.SectionID)throw new DomainException("OBJECT_IDENTITY_MISMATCH","Selected view Section identity is unavailable.");
                selection=SectionSourceSelection357.Read(section,state,tx,Budget);
            }
            var detail=new{view_ref=new EntityRef(instance,state.Id,selected.Entity.Handle.ToString()),identity=DrawingViewSources356.Capture(selected.View,Budget),section_source_selection=selection};
            var output=System.Text.Json.Nodes.JsonNode.Parse(System.Text.Json.JsonSerializer.Serialize(result,Wire.Json))!.AsObject();
            output["selected_source_identity"]=System.Text.Json.JsonSerializer.SerializeToNode(detail,Wire.Json);
            var expanded=Wire.Element(output);CadDrawingViewLifecycleState345.Bytes(expanded,Budget,Wire.PayloadBudget-8192);return expanded;
        }
        CadDrawingViewLifecycleState345.Bytes(result,Budget,Wire.PayloadBudget-8192);return result;
    }
    private static string Text(string value)=>System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(value,Wire.Json).Length<=Wire.PayloadBudget-8192?value:
        throw new DomainException("RESPONSE_LIMIT_EXCEEDED","Drawing view text exceeds the actual serialized-byte budget.");
    private static double Finite(double value)=>double.IsFinite(value)?value:throw new DomainException("INVALID_GEOMETRY","Non-finite drawing view metadata.");
}
