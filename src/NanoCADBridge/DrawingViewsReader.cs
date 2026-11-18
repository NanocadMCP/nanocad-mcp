using System.Diagnostics;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Nodes;
using Multicad;
using NanoCAD.Contracts;
using Teigha.DatabaseServices;
using G = Multicad.Geometry;

namespace NanoCADBridge;

internal static class DrawingViewsReader
{
    private sealed record LocalSource(McObjectId Id, EntityRef Ref);
    internal sealed record ProjectedLine(McObjectId ViewId, int Marker,
        Teigha.Geometry.Point3d Start, Teigha.Geometry.Point3d End);
    internal sealed record ProjectedCircle(McObjectId ViewId, int Marker, Teigha.Geometry.Point3d Center, double Radius);
    internal sealed record ProjectedCircularArc(McObjectId ViewId, int Marker, Teigha.Geometry.Point3d Center,
        double Radius, Teigha.Geometry.Point3d Start, Teigha.Geometry.Point3d End,
        Teigha.Geometry.Point3d Midpoint, double Sweep, bool FullCircle, double StartParameter, double EndParameter);
    private sealed record SourceTopology(McObjectId Id, string RefKey);
    private sealed record OriginalMarkerTopology(string original_marker, string native_subelement_id, string native_subelement_kind,
        string native_ref_key, string? creating_feature_id, ProjectedFeatureSource.Feature? subelement_creator);
    private sealed record ProjectionSource(string status, string? unavailable_reason, string? view_marker,
        EntityRef? source_ref, string? native_edge_id, string? native_ref_key, string? creating_feature_id = null,
        string? native_subelement_id = null, string? native_subelement_kind = null)
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

    private sealed record ProjectionIndex(Dictionary<int, LineEnds?> Markers, Dictionary<LineEnds, int> Occurrences,
        Dictionary<int, CircularArcShape?> CircularMarkers, List<CircularArcShape> CircularShapes)
    {
        internal bool ContainsUnique(int marker, LineEnds line) =>
            Markers.TryGetValue(marker, out var ends) && ends == line && Occurrences.GetValueOrDefault(line) == 1;

        internal bool ContainsUnique(int marker, CircularArcShape arc, Action cancel)
        {
            if (!CircularMarkers.TryGetValue(marker, out var found) || found == null || !found.Matches(arc)) return false;
            int matches = 0;
            // Shapes are sorted once per inventory. Only nearby centers can coincide.
            int low = 0, high = CircularShapes.Count;
            double lowerX = arc.Center.X - 1e-7, upperX = arc.Center.X + 1e-7;
            while (low < high)
            {
                int middle = low + (high - low) / 2;
                if (CircularShapes[middle].Center.X < lowerX) low = middle + 1; else high = middle;
            }
            for (int i = low; i < CircularShapes.Count && CircularShapes[i].Center.X <= upperX; i++)
            {
                cancel();
                if (arc.Matches(CircularShapes[i]) && ++matches > 1) return false;
            }
            return matches == 1;
        }
    }

    private static T? NativeProjection<T>(Func<T?> read) where T : class
    {
        try { return read(); }
        catch (Exception e) when (e is NotSupportedException or NotImplementedException or Teigha.Runtime.Exception) { return null; }
    }

    private static ProjectionIndex IndexLines(IEnumerable<G.EntityGeometry> geometry, Action cancel, bool includeCircular = false)
    {
        var index = new ProjectionIndex(new(), new(), new(), []);
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
            if (includeCircular)
            {
                var arc = CircularArcShape.Read(item);
                if (arc != null) index.CircularShapes.Add(arc);
                if (item.GSMarker >= 0 && !index.CircularMarkers.TryAdd(item.GSMarker, arc)) index.CircularMarkers[item.GSMarker] = null;
            }
        }
        index.CircularShapes.Sort((left, right) => left.Center.X.CompareTo(right.Center.X));
        return index;
    }

    private sealed record LocalVisibility(ProjectionIndex Whole, ProjectionIndex Visible, double PaperDenominator)
    {
        internal bool Matches(int marker, bool line, Action cancel)
        {
            if (line)
            {
                if (!Whole.Markers.TryGetValue(marker, out var ends) || ends is not { } sheet ||
                    !Visible.Markers.TryGetValue(marker, out var other) || other is not { } model ||
                    !Whole.ContainsUnique(marker, sheet) || !Visible.ContainsUnique(marker, model)) return false;
                bool Near((double X, double Y, double Z) a, (double X, double Y, double Z) b) =>
                    new Teigha.Geometry.Point3d(a.X, a.Y, a.Z).DistanceTo(new(b.X / PaperDenominator, b.Y / PaperDenominator, b.Z / PaperDenominator)) <= 1e-7;
                return Near(sheet.Start, model.Start) && Near(sheet.End, model.End);
            }
            return Whole.CircularMarkers.TryGetValue(marker, out var found) && found is { } arc &&
                Visible.CircularMarkers.TryGetValue(marker, out var otherArc) && otherArc != null &&
                Whole.ContainsUnique(marker, arc, cancel) && Visible.ContainsUnique(marker, otherArc, cancel) &&
                arc.Matches(otherArc.ToSheet(PaperDenominator));
        }
        internal object Evidence(int marker, bool line)
        {
            static double[] P(Teigha.Geometry.Point3d p) => [p.X, p.Y, p.Z];
            object? Locus(ProjectionIndex i)
            {
                if (line)
                {
                    if (!i.Markers.TryGetValue(marker, out var ends) || ends == null) return null;
                    var (a, b) = ends.Value;
                    return new { start = new[] { a.X, a.Y, a.Z }, end = new[] { b.X, b.Y, b.Z } };
                }
                if (!i.CircularMarkers.TryGetValue(marker, out var arc) || arc == null) return null;
                return new { center = P(arc.Center), radius = arc.Radius, start = P(arc.Start), end = P(arc.End), sweep = arc.Sweep };
            }
            return new
            {
                marker,
                coordinate_system = "view_lcs",
                whole_view = Locus(Whole),
                source_visible = Locus(Visible),
                whole_view_units = "sheet_drawing_units",
                source_visible_units = "unscaled_view_units",
                sheet_scale_denominator = PaperDenominator,
                whole_marker_count = Whole.Markers.Count,
                visible_marker_count = Visible.Markers.Count
            };
        }
    }

    private static LocalVisibility? ReadLocalVisibility(Multicad.Mc3D.DrawingView view, McObjectId source,
        Action cancel, bool includeCircular)
    {
        // The two APIs share view axes. On a Paper 1:2 view the whole-view
        // output contains R5, while the source-only output reports the same marker at R10.
        double denominator = view.DbEntity.Scale == 1 ? view.DbEntity.DScale : 1;
        if (!double.IsFinite(denominator) || denominator <= 0) return null;
        var whole = NativeProjection(() => view.GetViewGeometry(false, 0));
        if (whole == null) return null;
        try
        {
            var visible = NativeProjection(() => view.GetProjectedGeometryForObject(source, false, true));
            if (visible == null) return null;
            try { return new(IndexLines(whole, cancel, includeCircular), IndexLines(visible, cancel, includeCircular), denominator); }
            finally
            {
                foreach (var item in visible.Distinct(ReferenceEqualityComparer.Instance))
                    if (item is IDisposable owned) owned.Dispose();
            }
        }
        finally
        {
            foreach (var item in whole.Distinct(ReferenceEqualityComparer.Instance))
                if (item is IDisposable owned) owned.Dispose();
        }
    }

    private static void RequireLocalVisibility(Multicad.Mc3D.DrawingView view, McObjectId source, int marker,
        bool line, Action cancel)
    {
        var local = ReadLocalVisibility(view, source, cancel, !line);
        if (local == null || !local.Matches(marker, line, cancel))
            throw new DomainException("VIEW_GEOMETRY_UNAVAILABLE",
                "The selected native marker is not a unique visible primitive of this source in the view's local coordinates.",
                false, local?.Evidence(marker, line));
    }

    private const string SourceMismatch = "native_marker_source_not_exact_local_solid";
    private const string NonUniqueLine = "no_unique_matching_visible_projection_marker";

    private static (SourceTopology? Topology, string? Reason) ReadProjectionSource(Multicad.Mc3D.DrawingView view, LocalSource source, int marker, bool requireSourceObject = false)
    {
        using var projected = NativeProjection(() => view.GetProjectedObjectByGsm(marker));
        bool hasProjectedId = projected != null && !projected.IsNull;
        if (requireSourceObject && !hasProjectedId) return (null, "native_marker_source_object_unavailable");
        if (hasProjectedId && (projected!.IsXref || projected.Objects is not { Count: 1 } path ||
            path[0] != source.Id || projected.LeafId != source.Id))
            return (null, SourceMismatch);
        bool needsEdgeIdentity = hasProjectedId && (projected!.TopId != source.Id || projected.TargetId != source.Id);
        (SourceTopology?, string) UnavailableEdge(string reason) => (null, needsEdgeIdentity ? SourceMismatch : reason);

        // Pages establish visibility separately. A direct lookup requires the native
        // source object as well as the reversible subentity key.
        using var key = NativeProjection(() => view.GetProjectedFevRefKeyByGsm(marker));
        if (key == null || key.IsNull) return UnavailableEdge("native_projection_edge_key_unavailable");
        McObjectId edge;
        try { edge = McObjectId.FromRefKey(source.Id, key); }
        catch (Exception e) when (e is NotSupportedException or NotImplementedException or Teigha.Runtime.Exception)
        { return UnavailableEdge("native_projection_edge_cannot_resolve_exactly"); }
        if (edge.IsNull || !(edge.IsEdge || edge.IsFace || edge.IsVertex) || edge.IsInexactFEV)
            return UnavailableEdge("native_projection_subelement_cannot_resolve_exactly");
        if (needsEdgeIdentity && ((projected!.TopId != source.Id && projected.TopId != edge) ||
            (projected.TargetId != source.Id && projected.TargetId != edge))) return (null, SourceMismatch);
        if (edge.ParentId != source.Id) return (null, SourceMismatch);
        using var resolvedKey = NativeProjection(() => edge.RefKey);
        if (resolvedKey == null || resolvedKey.IsNull || !resolvedKey.Equals(key))
            return UnavailableEdge("native_projection_edge_key_does_not_round_trip");
        return (new(edge, key.ToString()), null);
    }

    private static ProjectionSource ObserveTopology(Multicad.Mc3D.DrawingView view, LocalSource source, int marker,
        Lazy<HashSet<McObjectId>?> members, bool requireSourceObject, out McObjectId creator)
    {
        var binding = ReadProjectionSource(view, source, marker, requireSourceObject);
        creator = McObjectId.Null;
        if (binding.Topology is not { } topology) return ProjectionSource.Unavailable(binding.Reason!);
        return DescribeTopology(source, marker, members, topology, out creator);
    }

    private static ProjectionSource DescribeTopology(LocalSource source, int marker, Lazy<HashSet<McObjectId>?> members,
        SourceTopology topology, out McObjectId creator)
    {
        creator = McObjectId.Null;
        try
        {
            var feature = Multicad.Mc3D.Service.GetFeatureObjectIdForFEV(topology.Id);
            if (!feature.IsNull && members.Value?.Contains(feature) == true) creator = feature;
        }
        catch (Exception error) when (error is NotImplementedException or NotSupportedException or Teigha.Runtime.Exception) { }
        var id = topology.Id;
        return new("observed", null, marker.ToString(CultureInfo.InvariantCulture), source.Ref, id.IsEdge ? id.ToString() : null,
            topology.RefKey, creator.IsNull ? null : creator.ToString(), id.ToString(), id.IsEdge ? "edge" : id.IsFace ? "face" : "vertex");
    }

    private static ProjectionSource ObserveEdge(Multicad.Mc3D.DrawingView view, LocalSource source, int marker,
        Lazy<HashSet<McObjectId>?> members, bool requireSourceObject = false) =>
        ObserveTopology(view, source, marker, members, requireSourceObject, out _);

    private static Lazy<HashSet<McObjectId>?> Members(LocalSource? source) => new(() =>
    {
        var body = source?.Id.GetObjectOfType<Multicad.Mc3D.Mc3dSolid>();
        return body != null && body.ID == source!.Id ? body.GetPartContents(false)?.ToHashSet() : null;
    });

    internal static object[] ReadStoredMarkerSources(Multicad.Mc3D.DrawingView view, ICollection<McObjectId> sourceIds,
        EntityRef[] sourceRefs, IEnumerable<int> markers, double? observedDiameter, Action cancel)
    {
        var source = NativeProjection(() => EligibleSource(view, sourceIds, sourceRefs, view.Attached2dBreaks is { Length: 0 }, true));
        var members = Members(source);
        var visibility = new Lazy<LocalVisibility?>(() => source == null ? null : ReadLocalVisibility(view, source.Id, cancel, true));
        return markers.OrderBy(value => value).Select(marker =>
        {
            cancel();
            McObjectId creator = McObjectId.Null;
            var observation = source == null ? ProjectionSource.Unavailable("requires_healthy_single_local_source_view")
                : marker < 0 ? ProjectionSource.Unavailable("stored_anchor_has_no_marker")
                : NativeProjection(() => ObserveTopology(view, source, marker, members, true, out creator))
                    ?? ProjectionSource.Unavailable("native_marker_lookup_unavailable");
            var evidence = observation.source_ref != null ? "native_projected_object_and_reversible_key" :
                observation.unavailable_reason is "native_projection_edge_key_unavailable" or "native_projection_edge_cannot_resolve_exactly" or
                    "native_projection_subelement_cannot_resolve_exactly" or "native_projection_edge_key_does_not_round_trip"
                ? "native_projected_object" : null;
            if (source != null && marker >= 0 && observation.unavailable_reason == "native_marker_source_object_unavailable" &&
                NativeProjection(() => visibility.Value) is { } local && (local.Matches(marker, true, cancel) || local.Matches(marker, false, cancel)))
            {
                evidence = "unique_marker_on_visible_body_projection";
                observation = NativeProjection(() => ObserveTopology(view, source, marker, members, false, out creator))
                    ?? ProjectionSource.Unavailable("native_marker_lookup_unavailable");
            }
            OriginalMarkerTopology? originalTopology = null;
            if (source != null && marker >= 0 && observation.unavailable_reason == "native_projection_edge_key_unavailable" &&
                NativeProjection(() => visibility.Value) is { } bodyProjection &&
                (bodyProjection.Matches(marker, true, cancel) || bodyProjection.Matches(marker, false, cancel)))
            {
                // getSrcGsm identifies the original primitive's marker. On an axial
                // section it can resolve to a vertex even for a long straight line.
                // Only the drawing view's projected FEV key identifies projected topology.
                originalTopology = NativeProjection(() => OriginalTopology(view, source, marker, members));
            }
            var feature = creator.IsNull || source == null ? null : NativeProjection(() => ProjectedFeatureSource.Read(creator, source.Id));
            var driver = feature != null && observedDiameter is { } diameter && view.SectionID.IsNull &&
                NativeProjection(() => visibility.Value) is { } projected && projected.Matches(marker, false, cancel) &&
                projected.Whole.CircularMarkers[marker] is { FullCircle: true } circle
                ? NativeProjection(() => ProjectedFeatureSource.HoleDiameter(creator, observation.native_subelement_id,
                    observation.native_subelement_kind, diameter, 2 * circle.Radius * projected.PaperDenominator)) : null;
            return (object)new
            {
                marker,
                projection_source = observation,
                design_source = new
                {
                    view_source_ref = sourceIds.Count == 1 && sourceRefs.Length == 1 ? sourceRefs[0] : null,
                    section_mc_id = source != null && !view.SectionID.IsNull ? view.SectionID.ToString() : null,
                    marker_source_evidence = evidence,
                    original_marker_topology = originalTopology,
                    creating_feature = feature,
                    feature_unavailable_reason = feature != null ? null : observation.unavailable_reason ??
                        (creator.IsNull ? "native_subelement_creator_unavailable" : "native_feature_details_unavailable"),
                    measurement_driver = driver,
                    measurement_driver_unavailable_reason = driver == null ? "native_view_anchor_does_not_identify_a_model_parameter" : null
                }
            };
        }).ToArray();
    }

    private static OriginalMarkerTopology? OriginalTopology(Multicad.Mc3D.DrawingView view, LocalSource source, int marker,
        Lazy<HashSet<McObjectId>?> members)
    {
        var original = NativeModelHistory421.OriginalProjectionMarker(view.DbEntity.DocumentID.ToString(), view.ID.ToString(), marker);
        if (original == null) return null;
        var id = McObjectId.FromGsMarker(source.Id, original.Value);
        if (id.IsNull || id.IsInexactFEV || !(id.IsEdge || id.IsFace || id.IsVertex) || id.ParentId != source.Id || id.GsMarker != original)
            return null;
        using var key = id.RefKey;
        if (key == null || key.IsNull || McObjectId.FromRefKey(source.Id, key) != id) return null;
        var related = DescribeTopology(source, marker, members, new(id, key.ToString()), out var creator);
        return new(original.Value.ToString(CultureInfo.InvariantCulture), id.ToString(), related.native_subelement_kind!,
            related.native_ref_key!, related.creating_feature_id,
            creator.IsNull ? null : NativeProjection(() => ProjectedFeatureSource.Read(creator, source.Id)));
    }

    private static LocalSource? OrdinarySource(Multicad.Mc3D.DrawingView view, ICollection<McObjectId>? nativeSources,
        EntityRef[]? refs, bool noAttachedBreaks) => EligibleSource(view, nativeSources, refs, noAttachedBreaks, false);

    private static LocalSource? EligibleSource(Multicad.Mc3D.DrawingView view, ICollection<McObjectId>? nativeSources,
        EntityRef[]? refs, bool noAttachedBreaks, bool allowPlaneSection)
    {
        if (nativeSources is not { Count: 1 } || refs is not [var sourceRef] ||
            view.Status.ToString() != "Ok" || view.IsSuppressed ||
            !noAttachedBreaks || view.AttachedLocalCuts is not { Length: 0 } || !view.OuterCutSketch.IsNull)
            return null;
        var source = nativeSources.Single();
        if (!view.SectionID.IsNull || view.IsBasedOnSection)
        {
            if (!allowPlaneSection || !view.IsRootSectionView || !view.IsBasedOnSection || !view.ParentViewID.IsNull ||
                view.IsSlice || view.DependsOnSlice || !view.EnableAutoUpdate || view.ShowHiddenLines || view.SectionID.IsNull)
                return null;
            var section = view.SectionID.GetObjectOfType<Multicad.Mc3D.Section>();
            var top = view.DbEntity.Document?.TopDoc;
            if (section == null || section.ID != view.SectionID || top == null || section.DbEntity.Document?.TopDoc?.ID != top.ID ||
                section.Status.ToString() != "Ok" || section.IsSuppressed || section.State.ToString() != "Plane" ||
                section.LiveSectionEnabled || !section.AssociatedPlanarObjectID.IsNull || section.GetVertices() is not { Count: 2 } ||
                section.GetViews()?.Contains(view.ID) != true ||
                section.GetTop3dEntitiesForSectioning() is not { Count: 1 } cutSources || cutSources.Single() != source)
                return null;
        }
        return new(source, sourceRef);
    }

    private static (Multicad.Mc3D.DrawingView View, McObjectId ViewId, LocalSource Source) ResolveProjectedSource(
        Entity entity, DocumentState state, Transaction tx, string instance, Action cancel, bool allowPlaneSection = false)
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
        var source = EligibleSource(view, nativeSources, sourceRefs.Refs, view.Attached2dBreaks is { Length: 0 }, allowPlaneSection)
            ?? throw new DomainException("DRAWING_VIEW_SOURCE_UNAVAILABLE", allowPlaneSection
                ? "Select a healthy single-solid projection or an automatically updated root Plane section with visible lines and one verified local source."
                : "Select a healthy ordinary projection of one local model solid.");
        return (view, viewId, source);
    }

    internal static ProjectedLine ResolveProjectedLine(Entity entity, DocumentState state, Transaction tx,
        string instance, int index, Action cancel, bool allowPlaneSection = false)
    {
        var (view, viewId, source) = ResolveProjectedSource(entity, state, tx, instance, cancel, allowPlaneSection);
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
            if (marker < 0 || !IndexLines(geometry, cancel).ContainsUnique(marker, canonical))
                throw new DomainException("VIEW_GEOMETRY_UNAVAILABLE", "The selected line has no unique visible native projection marker.");
            // The section computes its own cut edges. Its reciprocal section/source
            // links, visible-only result and original native endpoint anchors identify
            // the line in this view, including cuts without a 3D edge of their own.
            if (view.SectionID.IsNull) RequireLocalVisibility(view, source.Id, marker, true, cancel);
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

    private readonly record struct CircleShape(Teigha.Geometry.Point3d Center, double Radius)
    {
        internal bool Matches(CircleShape other) =>
            Center.DistanceTo(other.Center) <= 1e-7 && Math.Abs(Radius - other.Radius) <= 1e-7;

        internal static CircleShape? Read(G.EntityGeometry item)
        {
            if (item.GeometryType != G.EntityGeomType.kCircArc) return null;
            using var arc = item.CircArc;
            var center = arc.Center;
            var normal = arc.Normal;
            double radius = Finite(arc.Radius);
            double sweep = Math.Abs(Finite(arc.EndAng) - Finite(arc.StartAng));
            if (radius <= 1e-7 || Math.Abs(sweep - 2 * Math.PI) > 1e-8 ||
                Math.Abs(Finite(center.Z)) > 1e-7 || Math.Abs(Finite(normal.X)) > 1e-8 ||
                Math.Abs(Finite(normal.Y)) > 1e-8 || Math.Abs(Math.Abs(Finite(normal.Z)) - 1) > 1e-8)
                return null;
            return new CircleShape(new(Finite(center.X), Finite(center.Y), center.Z), radius);
        }
    }

    private static bool ContainsCircle(IEnumerable<G.EntityGeometry> geometry, int marker,
        CircleShape circle, bool rejectCoincident, Action cancel)
    {
        bool found = false;
        foreach (var item in geometry)
        {
            cancel();
            if (item == null) continue;
            if (item.GSMarker == marker)
            {
                if (found || CircleShape.Read(item) is not { } shape || !shape.Matches(circle)) return false;
                found = true;
            }
            else if (rejectCoincident && CircleShape.Read(item) is { } other && other.Matches(circle)) return false;
        }
        return found;
    }

    internal sealed record ProjectedCenter(McObjectId ViewId, int Marker, Teigha.Geometry.Point3d Center,
        Teigha.Geometry.Point3d Rim, double CenterParameter);

    internal static ProjectedCenter ResolveProjectedCenter(Entity entity, DocumentState state, Transaction tx,
        string instance, int marker, Action cancel)
    {
        var (view, viewId, source) = ResolveProjectedSource(entity, state, tx, instance, cancel);
        if (!view.EnableAutoUpdate || !view.SectionID.IsNull)
            throw new DomainException("OUT_OF_SCOPE", "Select an ordinary auto-updating Paper view.");
        var geometry = view.GetViewGeometry(true, 0);
        if (geometry == null) throw new DomainException("VIEW_GEOMETRY_UNAVAILABLE", "The view has no current projected geometry.");
        try
        {
            var selected = geometry.Where(item => { cancel(); return item != null && item.GSMarker == marker; }).ToArray();
            if (selected.Length != 1 || CircleShape.Read(selected[0]) is not { } circle ||
                !ContainsCircle(geometry, marker, circle, true, cancel))
                throw new DomainException("VIEW_GEOMETRY_UNAVAILABLE", "Select one unique visible full circle for each marker.");
            RequireLocalVisibility(view, source.Id, marker, false, cancel);
            var binding = ReadProjectionSource(view, source, marker);
            if (binding.Reason == SourceMismatch)
                throw new DomainException("DRAWING_VIEW_SOURCE_UNAVAILABLE", "The stored projection source conflicts with the local part.");
            var center = new G.Point3d(circle.Center.X, circle.Center.Y, circle.Center.Z);
            double parameter = double.NaN;
            if (selected[0].GetParamAtPoint(center, out var observed) && double.IsFinite(observed) &&
                selected[0].GetPointAtParam(observed, out var point) && (point - center).Length <= 1e-7) parameter = observed;
            return new(viewId, marker, circle.Center, new(circle.Center.X + circle.Radius, circle.Center.Y, circle.Center.Z),
                parameter);
        }
        finally
        {
            foreach (var item in geometry.Distinct(ReferenceEqualityComparer.Instance))
                if (item is IDisposable owned) owned.Dispose();
        }
    }

    internal static ProjectedCircle ResolveProjectedCircle(Entity entity, DocumentState state, Transaction tx,
        string instance, int index, Action cancel)
    {
        var (view, viewId, source) = ResolveProjectedSource(entity, state, tx, instance, cancel);
        if (!view.EnableAutoUpdate)
            throw new DomainException("OUT_OF_SCOPE", "Enable automatic updates for the selected paper view before adding an associative diameter.");
        cancel();
        var geometry = view.GetViewGeometry(true, 0);
        if (geometry == null)
            throw new DomainException("VIEW_GEOMETRY_UNAVAILABLE", "Selected native drawing view has no projected geometry.");
        try
        {
            if (index < 0 || index >= geometry.Count || geometry[index] is not { } item ||
                CircleShape.Read(item) is not { } circle)
                throw new DomainException("VIEW_GEOMETRY_UNAVAILABLE", "The selected projection index is not a full native circle in the sheet plane.");
            int marker = item.GSMarker;
            if (marker < 0 || !ContainsCircle(geometry, marker, circle, true, cancel))
                throw new DomainException("VIEW_GEOMETRY_UNAVAILABLE", "The selected circle has no unique native projection marker.");
            RequireLocalVisibility(view, source.Id, marker, false, cancel);
            if (ReadProjectionSource(view, source, marker).Reason == SourceMismatch)
                throw new DomainException("DRAWING_VIEW_SOURCE_UNAVAILABLE", "The native projection marker reports a conflicting source identity.");
            return new(viewId, marker, circle.Center, circle.Radius);
        }
        finally
        {
            foreach (var item in geometry.Distinct(ReferenceEqualityComparer.Instance))
                if (item is IDisposable owned) owned.Dispose();
        }
    }

    private sealed record CircularArcShape(Teigha.Geometry.Point3d Center, double Radius,
        Teigha.Geometry.Point3d Start, Teigha.Geometry.Point3d End, Teigha.Geometry.Point3d Midpoint,
        double Sweep, bool FullCircle)
    {
        internal CircularArcShape ToSheet(double denominator)
        {
            if (denominator == 1) return this;
            Teigha.Geometry.Point3d Scale(Teigha.Geometry.Point3d p) => new(p.X / denominator, p.Y / denominator, p.Z / denominator);
            return new(Scale(Center), Radius / denominator, Scale(Start), Scale(End), Scale(Midpoint), Sweep, FullCircle);
        }
        internal bool Matches(CircularArcShape other) => Center.DistanceTo(other.Center) <= 1e-7 &&
            Math.Abs(Radius - other.Radius) <= 1e-7 && FullCircle == other.FullCircle &&
            (FullCircle || Math.Abs(Sweep - other.Sweep) <= 1e-8 &&
                Start.DistanceTo(other.Start) <= 1e-7 && End.DistanceTo(other.End) <= 1e-7);

        internal static CircularArcShape? Read(G.EntityGeometry item)
        {
            if (item.GeometryType != G.EntityGeomType.kCircArc) return null;
            using var arc = item.CircArc;
            var center = arc.Center; var normal = arc.Normal; var reference = arc.RefVector;
            double radius = Finite(arc.Radius), first = Finite(arc.StartAng), last = Finite(arc.EndAng);
            double span = last - first, sweep = Math.Abs(span);
            if (radius <= 1e-7 || sweep <= 1e-8 || sweep > 2 * Math.PI + 1e-8 ||
                Math.Abs(Finite(center.Z)) > 1e-7 || Math.Abs(Finite(normal.X)) > 1e-8 ||
                Math.Abs(Finite(normal.Y)) > 1e-8 || Math.Abs(Math.Abs(Finite(normal.Z)) - 1) > 1e-8 ||
                Math.Abs(Finite(reference.Z)) > 1e-8 ||
                Math.Abs(Finite(reference.X) * reference.X + Finite(reference.Y) * reference.Y - 1) > 1e-8)
                return null;
            var origin = new Teigha.Geometry.Point3d(Finite(center.X), Finite(center.Y), center.Z);
            Teigha.Geometry.Point3d At(double angle) => new(
                origin.X + radius * (reference.X * Math.Cos(angle) - normal.Z * reference.Y * Math.Sin(angle)),
                origin.Y + radius * (reference.Y * Math.Cos(angle) + normal.Z * reference.X * Math.Sin(angle)), origin.Z);
            var start = At(first); var end = At(last);
            // Ordinary DWG arc dimensions use the counterclockwise sheet sector.
            // Keep the exact native primitive; only its endpoint order changes.
            if (span * normal.Z < 0) (start, end) = (end, start);
            return new(origin, radius, start, end, At((first + last) / 2), sweep,
                Math.Abs(sweep - 2 * Math.PI) <= 1e-8);
        }
    }

    internal static ProjectedCircularArc ResolveProjectedCircularArc(Entity entity, DocumentState state,
        Transaction tx, string instance, int index, Action cancel)
    {
        var (view, viewId, source) = ResolveProjectedSource(entity, state, tx, instance, cancel);
        if (!view.EnableAutoUpdate)
            throw new DomainException("OUT_OF_SCOPE", "Enable automatic updates for the selected view before adding an associative circular dimension.");
        cancel();
        var geometry = view.GetViewGeometry(true, 0);
        if (geometry == null)
            throw new DomainException("VIEW_GEOMETRY_UNAVAILABLE", "Selected native drawing view has no projected geometry.");
        try
        {
            if (index < 0 || index >= geometry.Count || geometry[index] is not { } item ||
                CircularArcShape.Read(item) is not { } arc)
                throw new DomainException("VIEW_GEOMETRY_UNAVAILABLE", "Select a finite native circular arc or circle in the sheet plane.");
            int marker = item.GSMarker;
            bool Contains(IEnumerable<G.EntityGeometry> values, bool rejectCoincident)
            {
                bool found = false;
                foreach (var value in values)
                {
                    cancel(); if (value == null) continue;
                    if (value.GSMarker == marker)
                    {
                        if (found || CircularArcShape.Read(value) is not { } selected || !selected.Matches(arc)) return false;
                        found = true;
                    }
                    else if (rejectCoincident && CircularArcShape.Read(value) is { } other && other.Matches(arc)) return false;
                }
                return found;
            }
            if (marker < 0 || !Contains(geometry, true))
                throw new DomainException("VIEW_GEOMETRY_UNAVAILABLE", "The selected circular arc has no unique native projection marker.");
            RequireLocalVisibility(view, source.Id, marker, false, cancel);
            if (ReadProjectionSource(view, source, marker).Reason == SourceMismatch)
                throw new DomainException("DRAWING_VIEW_SOURCE_UNAVAILABLE", "The native projection marker reports a conflicting source identity.");
            double startParameter = 0, endParameter = 0;
            G.Point3d Native(Teigha.Geometry.Point3d point) => new(point.X, point.Y, point.Z);
            if (!item.GetParamAtPoint(Native(arc.Start), out startParameter) ||
                !item.GetParamAtPoint(Native(arc.End), out endParameter) ||
                !double.IsFinite(startParameter) || !double.IsFinite(endParameter) ||
                !item.GetPointAtParam(startParameter, out var roundtripStart) ||
                !item.GetPointAtParam(endParameter, out var roundtripEnd) ||
                new Teigha.Geometry.Point3d(roundtripStart.X, roundtripStart.Y, roundtripStart.Z).DistanceTo(arc.Start) > 1e-7 ||
                new Teigha.Geometry.Point3d(roundtripEnd.X, roundtripEnd.Y, roundtripEnd.Z).DistanceTo(arc.End) > 1e-7)
                throw new DomainException("DRAWING_VIEW_ASSOCIATION_UNAVAILABLE", "The native circular arc did not round-trip its unique endpoint parameters.");
            return new(viewId, marker, arc.Center, arc.Radius, arc.Start, arc.End, arc.Midpoint, arc.Sweep, arc.FullCircle, startParameter, endParameter);
        }
        finally
        {
            foreach (var value in geometry.Distinct(ReferenceEqualityComparer.Instance))
                if (value is IDisposable owned) owned.Dispose();
        }
    }

    private static object ReadGeometry(Multicad.Mc3D.DrawingView view, Entity entity, DocumentState state, string instance, string document, int start, int pageSize,
        bool includeSources, LocalSource? source, Action budget)
    {
        budget();
        var geometry = view.GetViewGeometry(true, 0);
        if (geometry == null)
            throw new DomainException("VIEW_GEOMETRY_UNAVAILABLE", "Selected native drawing view has no projected geometry.");
        try
        {
            if (geometry.Count == 0 && !CadDrawingViewGeometry280.AllowsEmptyOuterProjection(view, state, budget))
                throw new DomainException("VIEW_GEOMETRY_UNAVAILABLE", "Selected native drawing view has no projected geometry or proven closed outer-cut contour.");
            if (start > geometry.Count) throw new DomainException("INVALID_ARGUMENT", "geometry_start_index exceeds the selected projection inventory; refresh the current view/revision.");
            var items = new List<object>(); bool complete = true; long bytes = 2048;
            int end = start + Math.Min(pageSize, geometry.Count - start);
            ProjectionIndex? allLines = null;
            LocalVisibility? visible = null;
            var members = Members(source);
            if (includeSources && source != null && start < end)
            {
                bool circles = geometry.Skip(start).Take(end - start).Any(item => item?.GeometryType == G.EntityGeomType.kCircArc);
                visible = ReadLocalVisibility(view, source.Id, budget, circles);
                if (visible != null) allLines = IndexLines(geometry, budget, circles);
            }
            for (int index = start; index < end; index++)
            {
                budget(); var item = geometry[index]; object? detail = null;
                LineEnds? lineEnds = null;
                if (item?.GeometryType == G.EntityGeomType.kLine)
                { using var line = item.LineSeg; detail = SketchGeometryReader.Line(line); if (includeSources) lineEnds = LineEnds.Read(line); }
                else if (item?.GeometryType == G.EntityGeomType.kCircArc)
                { using var arc = item.CircArc; detail = SketchGeometryReader.Arc(arc); }
                object entry = new
                {
                    index,
                    view_marker = item is { GSMarker: >= 0 } ? item.GSMarker.ToString(CultureInfo.InvariantCulture) : null,
                    native_geometry_type = item?.GeometryType.ToString(),
                    status = detail == null ? "unsupported_geometry" : "read",
                    geometry = detail,
                    unavailable_reason = detail == null ? "native_geometry_type_not_serialized" : (string?)null
                };
                if (includeSources)
                {
                    ProjectionSource observation;
                    int marker = item?.GSMarker ?? -1;
                    if (source == null) observation = ProjectionSource.Unavailable("requires_healthy_ordinary_projection_of_one_local_solid");
                    else if (visible == null) observation = ProjectionSource.Unavailable("native_visible_projection_unavailable");
                    else
                    {
                        var arc = item == null || lineEnds != null ? null : CircularArcShape.Read(item);
                        bool unique = lineEnds is { } ends ? allLines!.ContainsUnique(marker, ends.Canonical()) && visible.Matches(marker, true, budget) :
                            arc != null && allLines!.ContainsUnique(marker, arc, budget) && visible.Matches(marker, false, budget);
                        observation = marker >= 0 && unique ? ObserveEdge(view, source, marker, members) :
                            ProjectionSource.Unavailable(lineEnds == null && arc == null ? "unsupported_projected_curve" : NonUniqueLine);
                    }
                    var row = JsonSerializer.SerializeToNode(entry, Wire.Json)!.AsObject();
                    row["projection_source"] = JsonSerializer.SerializeToNode(observation, Wire.Json);
                    if (pageSize == 1 && visible != null && marker >= 0)
                        row["projection_visibility"] = JsonSerializer.SerializeToNode(visible.Evidence(marker, lineEnds.HasValue), Wire.Json);
                    if (pageSize == 1 && lineEnds is { } selectedLine && marker >= 0 && source != null)
                        row["dimension_edge_binding"] = JsonSerializer.SerializeToNode(CadProjectedLineSubentity.Observe(state, entity, marker,
                            new(selectedLine.Start.X, selectedLine.Start.Y, selectedLine.Start.Z),
                            new(selectedLine.End.X, selectedLine.End.Y, selectedLine.End.Z)), Wire.Json);
                    entry = row;
                }
                int size = JsonSerializer.SerializeToUtf8Bytes(entry, Wire.Json).Length + 1;
                if (bytes + size > Wire.PayloadBudget - 8192)
                {
                    if (items.Count == 0) throw new DomainException("RESPONSE_LIMIT_EXCEEDED", "One native projected element exceeds the actual serialized-byte budget.");
                    break;
                }
                bytes += size; items.Add(entry); if (detail == null) complete = false;
            }
            return new
            {
                view_ref = new EntityRef(instance, document, entity.Handle.ToString()),
                coordinate_system = "WCS",
                unit = "drawing_units",
                geometry_details_complete = complete,
                element_identity = "projection_order_only_not_persistent_subentity_ids",
                geometry_scope = "requested_projection_page",
                total_elements = geometry.Count,
                start_index = start,
                next_index = start + items.Count < geometry.Count ? (int?)(start + items.Count) : null,
                complete = start + items.Count == geometry.Count,
                items
            };
        }
        finally
        {
            foreach (var item in geometry.Distinct(ReferenceEqualityComparer.Instance))
                if (item is IDisposable disposable) disposable.Dispose();
        }
    }
    public static object Read(Arguments args, DocumentState state, Transaction tx, string instance, CancellationToken cancellation)
    {
        var loaded = AppDomain.CurrentDomain.GetAssemblies().Select(a => a.GetName().Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (!loaded.Contains("mapimgd") || !loaded.Contains("MapiBaseTypes")) throw new DomainException("CAPABILITY_UNAVAILABLE", "MultiCAD runtime is not already loaded.");
        var db = state.Document.Database;
        long bits = unchecked((long)ulong.Parse(args.Handles![0], NumberStyles.HexNumber, CultureInfo.InvariantCulture));
        if (!db.TryGetObjectId(new Handle(bits), out var id) || id.IsNull || !id.IsValid) throw new DomainException("OBJECT_NOT_FOUND", "Layout not found.");
        if (id.IsErased || id.IsEffectivelyErased) throw new DomainException("OBJECT_ERASED", "Layout erased.");
        if (tx.GetObject(id, OpenMode.ForRead) is not Layout layout || layout.OwnerId != db.LayoutDictionaryId)
            throw new DomainException("OUT_OF_SCOPE", "layout_handle must identify a layout in the requested drawing.");
        var block = (BlockTableRecord)tx.GetObject(layout.BlockTableRecordId, OpenMode.ForRead);
        if (!block.IsLayout || block.OwnerId != db.BlockTableId) throw new DomainException("OUT_OF_SCOPE", "Invalid layout block record.");
        return ReadMultiCad(args, state, tx, layout, block, instance, cancellation);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static object ReadMultiCad(Arguments args, DocumentState state, Transaction tx, Layout layout, BlockTableRecord block, string instance, CancellationToken cancellation)
    {
        var watch = Stopwatch.StartNew(); int scanned = 0;
        void Budget() { cancellation.ThrowIfCancellationRequested(); }
        var document = Multicad.DatabaseServices.McDocument.ActiveDocument;
        if (document == null || !state.Document.IsNamedDrawing || !string.Equals(Path.GetFullPath(document.Path), Path.GetFullPath(state.Document.Name), StringComparison.OrdinalIgnoreCase))
            throw new DomainException("DOCUMENT_CONTEXT_MISMATCH", "MultiCAD active document differs from the requested document.");
        string? Id(Multicad.McObjectId id) => id.IsNull ? null : id.ToString();
        string[]? Ids(ICollection<Multicad.McObjectId>? ids)
        {
            if (ids == null) return null;

            return ids.Select(x => x.ToString()).ToArray();
        }
        double[] Vector(Multicad.Geometry.Vector3d p) => new[] { Finite(p.X), Finite(p.Y), Finite(p.Z) };
        double[] Point(Multicad.Geometry.Point3d p) => new[] { Finite(p.X), Finite(p.Y), Finite(p.Z) };
        object Plane(Multicad.Geometry.Plane3d plane) => new { origin = Point(plane.Origin), axis_u = Vector(plane.DirU), axis_v = Vector(plane.DirV), normal = Vector(plane.Normal) };
        var items = new List<object>();
        var found = new List<(Entity Entity, Multicad.McObjectId Id, Multicad.Mc3D.DrawingView View)>();
        var layoutDimensions = args.MarkerDimensions == null ? null : new List<ObjectId>();
        var layoutHandles = new Dictionary<Multicad.McObjectId, List<string>>();
        LocalSource? selectedSource = null;
        foreach (ObjectId nativeId in block)
        {
            Budget(); scanned++;
            if (nativeId.IsErased || nativeId.IsEffectivelyErased || nativeId.ObjectClass.DxfName.Equals("3DSOLID", StringComparison.OrdinalIgnoreCase)) continue;
            if (tx.GetObject(nativeId, OpenMode.ForRead) is not Entity entity || entity.OwnerId != block.ObjectId)
                throw new DomainException("OBJECT_READ_FAILED", "Invalid layout member.");
            if (entity is Dimension) layoutDimensions?.Add(nativeId);
            var id = Multicad.McObjectId.FromHandle(entity.Handle.Value, document.ID);
            if (id.IsNull) continue;
            if (!layoutHandles.TryGetValue(id, out var handles)) { handles = new List<string>(); layoutHandles.Add(id, handles); }
            handles.Add(entity.Handle.ToString());
            var view = id.GetObjectOfType<Multicad.Mc3D.DrawingView>();
            if (view == null) continue;
            if (view.ID != id) throw new DomainException("OBJECT_IDENTITY_MISMATCH", "DrawingView lookup returned a different identity.");

            found.Add((entity, id, view));
        }
        foreach (var entry in found)
        {
            Budget(); var entity = entry.Entity; var id = entry.Id; var view = entry.View;
            var children = Ids(view.ChildViews) ?? Array.Empty<string>();
            var nativeSources = view.GetTop3dEntitiesInView();
            var sources = Ids(nativeSources); Budget();
            var freshness = CadDrawingViewLifecycleState345.TryToken(view, entity, layout.ObjectId, tx, Budget);
            var sourceRefs = CadDrawingViewLifecycleState345.SourceRefs(nativeSources, state.Document.Database, tx, instance, state.Id, Budget);
            var attached = view.Attached2dBreaks;
            if (attached == null) throw new DomainException("MODEL_STATE_UNAVAILABLE", "Native break inventory is unavailable.");
            if ((args.IncludeGeometrySources || args.GeometrySourceMarker.HasValue) && Validation.NormalizeHandle(entity.Handle.ToString()) == args.GeometryViewHandle)
                selectedSource = OrdinarySource(view, nativeSources, sourceRefs.Refs, attached.Length == 0);

            var breakData = DrawingViewBreakReader.Read(attached, id, layoutHandles, instance, state.Id, Budget);
            object? sectionData = null; SectionCutRead351? sectionCut = null; string? sectionCutReason = null;
            if (!view.SectionID.IsNull)
            {
                var section = view.SectionID.GetObjectOfType<Multicad.Mc3D.Section>();
                if (section == null || section.ID != view.SectionID) throw new DomainException("OBJECT_IDENTITY_MISMATCH", "View section identity unavailable.");
                var metadata = SectionViewState.Read(section, Budget);


                sectionData = metadata;
                (sectionCut, sectionCutReason) = CadSectionCutState351.Describe(section, state, tx, instance, Budget);
            }
            items.Add(new
            {
                @ref = new EntityRef(instance, state.Id, entity.Handle.ToString()),
                mc_id = id.ToString(),
                name = Text(view.Name),
                status = view.Status.ToString(),
                suppressed = view.IsSuppressed,
                direction = view.DirectionType.ToString(),
                native_owner_document_id = Id(view.DbEntity.DocumentID),
                section_id = Id(view.SectionID),
                section = sectionData,
                section_cut = sectionCut,
                section_cut_unavailable_reason = sectionCutReason,
                parent_view_id = Id(view.ParentViewID),
                root_view_id = Id(view.RootViewID),
                child_view_ids = children,
                attached_break_ids = attached.Select(i => i.ToString()).ToArray(),
                breaks = breakData.Items,
                breaks_unavailable_reason = breakData.UnavailableReason,
                top_source_ids = sources,
                source_inventory_available = sources != null,
                view_plane = Plane(view.ViewPlane),
                projection_plane = Plane(view.ProjPlane),
                view_token = freshness.Token,
                view_token_unavailable_reason = freshness.Reason,
                source_refs = sourceRefs.Refs,
                source_refs_unavailable_reason = sourceRefs.Reason,
                model_scale = Finite(view.DbEntity.DScale),
                design_scale = Finite(view.DbEntity.Scale),
                is_slice = view.IsSlice,
                is_based_on_section = view.IsBasedOnSection,
                is_root_section_view = view.IsRootSectionView,
                depends_on_slice = view.DependsOnSlice,
                auto_update_enabled = view.EnableAutoUpdate,
                preview_mode = view.PreviewMode,
                align_on_parent = view.AlignOnParent,
                attached_local_cut_ids = view.AttachedLocalCuts?.Select(cut => cut.ToString()).ToArray(),
                outer_cut_sketch_id = Id(view.OuterCutSketch),
                show_hidden_lines = view.ShowHiddenLines,
                show_hatches = view.ShowHatches,
                show_tangent_edges = view.ShowTangentEdges
            });
        }
        object? selectedGeometry = null;
        object? selectedMarker = null;
        if (args.GeometryViewHandle != null)
        {
            var selected = found.SingleOrDefault(x => Validation.NormalizeHandle(x.Entity.Handle.ToString()) == args.GeometryViewHandle);
            if (selected.View == null) throw new DomainException("OUT_OF_SCOPE", "geometry_view_handle is not a native drawing view in the requested layout.");
            if (args.GeometrySourceMarker is int marker)
            {
                var binding = selectedSource == null ? ProjectionSource.Unavailable("requires_healthy_ordinary_projection_of_one_local_solid")
                    : ObserveEdge(selected.View, selectedSource, marker, Members(selectedSource), true);
                selectedMarker = new
                {
                    view_handle = args.GeometryViewHandle,
                    marker,
                    scope = "native_marker_source_object_and_subelement_key",
                    visible_uniqueness_evaluated = false,
                    projection_source = binding
                };
            }
            else selectedGeometry = ReadGeometry(selected.View, selected.Entity, state, instance, state.Id, args.GeometryStartIndex, args.GeometryPageSize,
                args.IncludeGeometrySources, selectedSource, Budget);
        }
        Budget();
        var result = new
        {
            layout_ref = new EntityRef(instance, state.Id, args.Handles![0]),
            layout_name = Text(layout.LayoutName),
            space = layout.ModelType ? "model" : "paper",
            identity_scope = "bridge_document_revision",
            reference_scope = "opaque_native_ids_no_external_traversal",
            coordinate_system = "native_view_planes",
            geometry_state = selectedGeometry == null ? "stored_metadata_not_regenerated" : "selected_native_projection_without_regeneration",
            selected_geometry = selectedGeometry,
            selected_marker_source = selectedMarker,
            scanned_entities = scanned,
            complete = true,
            items
        };
        if (args.MarkerDimensions is { } page)
        {
            var output = JsonSerializer.SerializeToNode(result, Wire.Json)!.AsObject();
            var selected = found.Single(item => Validation.NormalizeHandle(item.Entity.Handle.ToString()) == args.GeometryViewHandle);
            int room = Wire.PayloadBudget - 8192 - JsonSerializer.SerializeToUtf8Bytes(output, Wire.Json).Length;
            output["marker_dimensions"] = JsonSerializer.SerializeToNode(CadViewMarkerDimensions.Read(layoutDimensions!, page,
                selected.Entity, selected.Id, args.GeometrySourceMarker!.Value, state, tx, instance, Budget, room), Wire.Json);
            CadDrawingViewLifecycleState345.Bytes(output, Budget, Wire.PayloadBudget - 8192);
            return Wire.Element(output);
        }
        if (args.SheetPlacement is { } placement)
        {
            var output = JsonSerializer.SerializeToNode(result, Wire.Json)!.AsObject();
            int room = Wire.PayloadBudget - 8192 - JsonSerializer.SerializeToUtf8Bytes(output, Wire.Json).Length;
            output["sheet_placement"] = JsonSerializer.SerializeToNode(
                CadSheetPlacement.Read(placement, layout, block, tx, found, Budget, room), Wire.Json);
            CadDrawingViewLifecycleState345.Bytes(output, Budget, Wire.PayloadBudget - 8192);
            return Wire.Element(output);
        }
        if (args.DrawingViewSourceIdentityHandle is { } sourceHandle)
        {
            var selected = found.SingleOrDefault(item => Validation.NormalizeHandle(item.Entity.Handle.ToString()) == sourceHandle);
            if (selected.View == null) throw new DomainException("OUT_OF_SCOPE", "source_identity_view_handle is not a native view in the explicitly selected layout.");
            SectionSourceSelectionRead357? selection = null;
            if (!selected.View.SectionID.IsNull)
            {
                var section = selected.View.SectionID.GetObjectOfType<Multicad.Mc3D.Section>();
                if (section == null || section.ID != selected.View.SectionID) throw new DomainException("OBJECT_IDENTITY_MISMATCH", "Selected view Section identity is unavailable.");
                selection = SectionSourceSelection357.Read(section, state, tx, Budget);
            }
            var detail = new { view_ref = new EntityRef(instance, state.Id, selected.Entity.Handle.ToString()), identity = DrawingViewSources356.Capture(selected.View, Budget), section_source_selection = selection };
            var output = System.Text.Json.Nodes.JsonNode.Parse(System.Text.Json.JsonSerializer.Serialize(result, Wire.Json))!.AsObject();
            output["selected_source_identity"] = System.Text.Json.JsonSerializer.SerializeToNode(detail, Wire.Json);
            var expanded = Wire.Element(output); CadDrawingViewLifecycleState345.Bytes(expanded, Budget, Wire.PayloadBudget - 8192); return expanded;
        }
        CadDrawingViewLifecycleState345.Bytes(result, Budget, Wire.PayloadBudget - 8192); return result;
    }
    private static string Text(string value) => System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(value, Wire.Json).Length <= Wire.PayloadBudget - 8192 ? value :
        throw new DomainException("RESPONSE_LIMIT_EXCEEDED", "Drawing view text exceeds the actual serialized-byte budget.");
    private static double Finite(double value) => double.IsFinite(value) ? value : throw new DomainException("INVALID_GEOMETRY", "Non-finite drawing view metadata.");
}
