using NanoCAD.Contracts;
using Teigha.DatabaseServices;

namespace NanoCADBridge;

// set_curve_geometry keeps the original primitive IDs. A Hatch with several
// source curves must see the final closed contour, including its unchanged islands.
internal sealed class CadCurveHatchFollowers
{
    private sealed record DimensionFollower(CadDimensionAssociation385.Snapshot Before,
        CadDimensionAssociation385.AssociationConnections Connections);

    private readonly Dictionary<ObjectId, CurveEditGeometry> edited = new();
    private readonly Dictionary<ObjectId, CadLegacyPolyline408.HatchState> hatches = new();
    private readonly Dictionary<ObjectId, DimensionFollower> dimensions = new();
    private readonly Dictionary<(ObjectId Dimension, string Role), string> dimensionSnaps = new();
    private readonly Dictionary<ObjectId, CadHatchAssociationGeometry362.Boundary[]> planned = new();
    private readonly SerializedCapacity357 snapshotBytes = new();
    private static string Handle(ObjectId id) => Validation.NormalizeHandle(id.Handle.ToString());

    internal bool Contains(ObjectId source) => edited.ContainsKey(source);
    internal bool Any => edited.Count > 0;

    private static bool LeavesGeometry(EditSpec operation) => operation is PropertiesSpec or CopySpec
        or Mirror2dSpec { Copy: true } or CopyTransform2d407 or PolarRepeat2d407;

    private static string CurrentSnap(Entity source, CadDimensionAssociation385.PointState point)
    {
        string[] names = point.Snap switch
        {
            ObjectSnapModes.ModeStartpoint => ["start"],
            ObjectSnapModes.ModeEnd => ["end"],
            ObjectSnapModes.ModeMid => ["midpoint"],
            ObjectSnapModes.ModeCenter => ["center"],
            ObjectSnapModes.ModeQuad => ["quadrant_x_positive", "quadrant_y_positive", "quadrant_x_negative", "quadrant_y_negative"],
            _ => []
        };
        var matches = names.Where(name => CadDimensionAssociation385.Snap(source, name).Point.DistanceTo(point.Evaluated) <= 1e-7).ToArray();
        if (matches.Length != 1)
            throw new DomainException("UNSUPPORTED_DIMENSION_ASSOCIATION", "The stored native dimension snap does not resolve to one current point on this boundary.");
        return matches[0];
    }

    internal bool Collect(Entity source, EditSpec[] operations, Transaction tr, Action cancel)
    {
        var type = source.GetType();
        if ((type != typeof(Line) && type != typeof(Arc) && type != typeof(Circle)) ||
            !operations.Any(op => op is CurveGeometrySpec { Workplane: null })) return false;

        using var reactors = source.GetPersistentReactorIds();
        var followers = new List<Hatch>();
        bool dimensionedArc = false;
        foreach (ObjectId id in reactors)
        {
            cancel();
            if (id.IsNull || !id.IsValid || id.IsErased || id.IsEffectivelyErased) continue;
            var value = tr.GetObject(id, OpenMode.ForRead);
            if (value is Hatch hatch) followers.Add(hatch);
            else if (source is Arc && !dimensionedArc)
            {
                using var native = CadNativeDimAssocBinding434.TryBind(value);
                dimensionedArc = native != null;
            }
        }
        if (followers.Count == 0 && !dimensionedArc) return false;
        if (!operations.All(op => op is CurveGeometrySpec { Workplane: null } || LeavesGeometry(op)))
            throw new DomainException("INVALID_OPERATION_ORDER", "Edit the associated contour with its final WCS curve definitions in one request. Apply other changes separately.");
        if (source.HasFields || source.Annotative == AnnotativeStates.True)
            throw new DomainException("UNSUPPORTED_CURVE_DEPENDENCIES", "A field-bearing or annotative boundary needs its owning edit.");
        CadIndependentCurve.RequireModelIndependence(source, source.Database, tr, cancel);
        var associations = new HashSet<ObjectId>();
        foreach (ObjectId id in reactors)
        {
            cancel();
            if (id.IsNull || !id.IsValid || id.IsErased || id.IsEffectivelyErased)
                throw new DomainException("UNSUPPORTED_CURVE_DEPENDENCIES", "One of the source curve's dependencies is unavailable.");
            var value = tr.GetObject(id, OpenMode.ForRead);
            if (value is Hatch || CadDrawingGroups431.OrganizationalReactor(source, id, tr, cancel)) continue;
            using var native = CadNativeDimAssocBinding434.TryBind(value);
            if (native == null)
                throw new DomainException("UNSUPPORTED_CURVE_DEPENDENCIES", "The boundary has a dependency other than an associative Hatch, ordinary dimension or selection group.");
            associations.Add(id);
        }
        bool circleChanges = source is Circle && operations.OfType<CurveGeometrySpec>().Any(op => op.ExpectedGeometry != op.Geometry);
        foreach (var follower in CadDimensionAssociation385.Followers(source, source.Database, tr, cancel, circleChanges))
        {
            associations.Remove(follower.Before.Association);
            var dimension = (Dimension)tr.GetObject(follower.Dimension, OpenMode.ForRead);
            var layer = (LayerTableRecord)tr.GetObject(dimension.LayerId, OpenMode.ForRead);
            if (layer.IsLocked || layer.IsDependent)
                throw new DomainException("LAYER_UNAVAILABLE", "Refreshing an associated dimension requires a local, unlocked dimension layer.");
            if (!dimensions.ContainsKey(follower.Dimension))
            {
                var state = follower.Before;
                var connections = CadDimensionAssociation385.Connections(dimension, state.Association, state.Points, tr, cancel);
                CadDimensionAssociation385.VerifyConnections(connections, "before_contour_edit");
                snapshotBytes.Add(new { state.Dimension, state.Sources, state.Metadata, state.AssociationData, state.Containers, connections });
                dimensions.Add(follower.Dimension, new(state, connections));
            }
            foreach (var point in follower.Before.Points.Where(point => point.Source == source.ObjectId))
                dimensionSnaps[(follower.Dimension, point.Role)] = CurrentSnap(source, point);
        }
        if (associations.Count != 0)
            throw new DomainException("UNSUPPORTED_DIMENSION_ASSOCIATION", "A curve reactor lacks a matching active dimension point on this boundary.");

        foreach (var hatch in followers)
        {
            if (hatches.TryGetValue(hatch.ObjectId, out var saved))
            {
                if (!saved.Groups.Any(group => group.Contains(source.ObjectId)))
                    throw new DomainException("UNSUPPORTED_CURVE_DEPENDENCIES", "The Hatch reactor does not point back to this boundary member.");
            }
            else
            {
                if (CadDrawingGroups431.HasNonOrganizationalReactors(hatch, tr, cancel))
                    throw new DomainException("UNSUPPORTED_CURVE_DEPENDENCIES", "The associated Hatch has additional dependencies requiring their own edit.");
                saved = CadLegacyPolyline408.CaptureHatch(hatch, source, tr, cancel);
                snapshotBytes.Add(saved.Before);
                hatches.Add(hatch.ObjectId, saved);
            }
        }
        edited.Add(source.ObjectId, operations.OfType<CurveGeometrySpec>().Last().Geometry);
        return true;
    }

    internal void CheckBatch(ModifyArguments args, Transaction tr, Action cancel)
    {
        if (!Any) return;
        var targets = args.Operations.Where(op => !LeavesGeometry(op)).SelectMany(op => op.Handles).ToHashSet(StringComparer.Ordinal);
        var allTargets = args.Operations.SelectMany(op => op.Handles).ToHashSet(StringComparer.Ordinal);
        if (hatches.Keys.Concat(dimensions.Keys).Any(id => allTargets.Contains(Handle(id))))
            throw new DomainException("INVALID_OPERATION_ORDER", "Finish the contour edit before editing its Hatch or associated dimensions directly.");
        var dimensionSources = dimensions.Values.SelectMany(follower => follower.Before.Points)
            .Select(point => Handle(point.Source)).ToHashSet(StringComparer.Ordinal);
        DimensionAssociationValidation385.CheckSourceRemovals(args.Operations, dimensionSources);

        foreach (var saved in hatches.Values)
        {
            var hatch = (Hatch)tr.GetObject(saved.Id, OpenMode.ForRead);
            const HatchLoopTypes supported = HatchLoopTypes.External | HatchLoopTypes.Outermost | HatchLoopTypes.Polyline | HatchLoopTypes.Derived;
            if (saved.LoopTypes.Any(type => (type & ~supported) != 0))
                throw new DomainException("UNSUPPORTED_HATCH_BOUNDARY", "Text islands and special Hatch loop types need their owning contour editor.");
            var members = new HashSet<ObjectId>();
            foreach (ObjectId id in saved.Groups.SelectMany(ids => ids))
            {
                cancel();
                if (!members.Add(id))
                    throw new DomainException("INVALID_HATCH_TOPOLOGY", "A boundary source occurs in more than one loop of the same Hatch.");
                var curve = tr.GetObject(id, OpenMode.ForRead) as Curve;
                if (curve == null || curve.ObjectId != id || curve.Database?.UnmanagedObject != hatch.Database.UnmanagedObject || curve.OwnerId != hatch.OwnerId)
                    throw new DomainException("OUT_OF_SCOPE", "Each Hatch loop must retain live boundary curves in its owning drawing space.");
                var layer = (LayerTableRecord)tr.GetObject(curve.LayerId, OpenMode.ForRead);
                if (layer.IsLocked || layer.IsDependent)
                    throw new DomainException("LAYER_UNAVAILABLE", "Refreshing an associative Hatch requires local, unlocked boundary layers.");
                using var links = curve.GetPersistentReactorIds();
                if (links.Cast<ObjectId>().Count(link => link == saved.Id) != 1)
                    throw new DomainException("UNSUPPORTED_CURVE_DEPENDENCIES", "Every retained Hatch source must have its matching reverse association.");
                if (!Contains(id) && targets.Contains(Handle(id)))
                    throw new DomainException("INVALID_OPERATION_ORDER", "Use curve geometry edits for all changing boundaries of this Hatch in the same request.");
            }
            CadHatchAssociationGeometry362.Verify(hatch, Geometry(saved, tr, null, cancel), cancel);
        }
    }

    private CadHatchAssociationGeometry362.Boundary[] Geometry(CadLegacyPolyline408.HatchState saved,
        Transaction tr, IReadOnlyDictionary<string, Entity>? preview, Action cancel)
    {
        var hatch = (Hatch)tr.GetObject(saved.Id, OpenMode.ForRead);
        var identities = new Dictionary<Entity, Entity>(ReferenceEqualityComparer.Instance);
        var groups = saved.Groups.Select(group => group.Select(id =>
        {
            cancel();
            var source = (Entity)tr.GetObject(id, OpenMode.ForRead);
            if (preview == null || !Contains(id)) return source;
            if (!preview.TryGetValue(Handle(id), out var clone))
                throw new DomainException("INVALID_SOURCE_PROVENANCE", "The edited boundary has no matching native preview curve.");
            identities.Add(clone, source);
            return clone;
        }).ToArray()).ToArray();
        return CadHatchAssociationTopology362.Validate(groups, hatch, cancel, identities);
    }

    internal void CheckPlanned(IReadOnlyDictionary<string, Entity> preview, Transaction tr, Action cancel)
    {
        foreach (var hatch in hatches.Values) planned[hatch.Id] = Geometry(hatch, tr, preview, cancel);
    }

    internal void Complete(Transaction tr, Action cancel, List<string>? warnings)
    {
        if (!Any) return;
        foreach (ObjectId id in edited.Keys)
        {
            cancel();
            var source = (Entity)tr.GetObject(id, OpenMode.ForWrite);
            CadDimensionAssociation385.RefreshFollowers(source, source.Database, tr, cancel, notice => warnings?.Add(notice));
        }
        // Source notifications may already have regenerated the native loops.
        // Reconcile each Hatch once after all of its sources reach final geometry.
        foreach (var saved in hatches.Values)
            CadLegacyPolyline408.RefreshHatch(saved, planned[saved.Id], tr, cancel, preserveLoopFlags: true);
        Verify(tr, cancel, verifyCurrentSources: false);
        if (dimensions.Count > 0) warnings?.Add(CadDimensionAssociation385.Warning);
    }

    internal void VerifyCommitted(Database db, Action cancel)
    {
        using var tr = db.TransactionManager.StartTransaction();
        Verify(tr, cancel, verifyCurrentSources: true);
        tr.Commit();
    }

    private void Verify(Transaction tr, Action cancel, bool verifyCurrentSources)
    {
        foreach (var (id, follower) in dimensions)
        {
            cancel();
            var before = follower.Before;
            var dimension = (Dimension)tr.GetObject(id, OpenMode.ForRead);
            var after = CadDimensionAssociation385.Capture(dimension, dimension.Database, tr, cancel);
            if (after.Association != before.Association || after.Flags != before.Flags || after.Points.Length != before.Points.Length ||
                before.Points.Any(point => !after.Points.Any(actual => actual.Role == point.Role &&
                    actual.Source == point.Source && actual.Snap == point.Snap)))
                throw new DomainException("NATIVE_DIMENSION_ASSOCIATION_MISMATCH", "The contour edit changed an existing dimension association, role or source.");
            CadDimensionAssociation385.MatchPoints(dimension, after.Points);
            CadDimensionAssociation385.CheckCircleDiameter(dimension, after, tr);
            var links = CadDimensionAssociation385.Connections(dimension, after.Association, after.Points, tr, cancel);
            var originalLinks = follower.Connections;
            if (links.owner_handle != originalLinks.owner_handle || links.owned_by_dimension != originalLinks.owned_by_dimension ||
                links.dimension_reverse_count != originalLinks.dimension_reverse_count ||
                !links.source_reverse_links.ToHashSet().SetEquals(originalLinks.source_reverse_links))
                throw new DomainException("NATIVE_DIMENSION_ASSOCIATION_MISMATCH", "The contour edit changed the dimension's native ownership or notification links.",
                    false, new { before = originalLinks, after = links });
            CadDimensionAssociation385.VerifyDefinition(before, after, true);
            CadDimensionAssociation385.Retained(before.Metadata, after.Metadata, after.Association, tr, dimension.Database, cancel, before.Containers);
            foreach (var point in after.Points)
            {
                if (!dimensionSnaps.TryGetValue((id, point.Role), out string? snap)) continue;
                var source = (Entity)tr.GetObject(point.Source, OpenMode.ForRead);
                if (CadDimensionAssociation385.Snap(source, snap).Point.DistanceTo(point.Evaluated) > 1e-7)
                    throw new DomainException("NATIVE_DIMENSION_ASSOCIATION_MISMATCH", "The retained dimension snap missed the final boundary geometry.");
            }
        }
        foreach (var (id, geometry) in edited)
            CadCurveGeometryEditor.Require((Entity)tr.GetObject(id, OpenMode.ForRead), geometry, "NATIVE_GEOMETRY_MISMATCH", cancel);
        foreach (var saved in hatches.Values)
        {
            var hatch = (Hatch)tr.GetObject(saved.Id, OpenMode.ForRead);
            CadLegacyPolyline408.VerifyHatchProperties(saved, hatch, tr, cancel, "NATIVE_HATCH_ASSOCIATION_MISMATCH");
            CadHatchAssociationGeometry362.Verify(hatch, planned[saved.Id], cancel);
            if (verifyCurrentSources)
                CadHatchAssociationGeometry362.Verify(hatch, Geometry(saved, tr, null, cancel), cancel);
            var roles = saved.Groups.SelectMany((group, index) => group.Select(id => (id, role: saved.LoopTypes[index]))).ToDictionary(item => item.id, item => item.role);
            const HatchLoopTypes derived = HatchLoopTypes.Polyline | HatchLoopTypes.Derived;
            for (int index = 0; index < hatch.NumberOfLoops; index++)
            {
                cancel();
                using var members = hatch.GetAssociatedObjectIdsAt(index);
                var loopType = hatch.GetLoopAt(index).LoopType;
                foreach (ObjectId id in members)
                {
                    var source = (Entity)tr.GetObject(id, OpenMode.ForRead);
                    using var links = source.GetPersistentReactorIds();
                    if ((loopType & ~derived) != (roles[id] & ~derived) || links.Cast<ObjectId>().Count(link => link == saved.Id) != 1)
                        throw new DomainException("NATIVE_HATCH_ASSOCIATION_MISMATCH", "A retained Hatch source lost its native loop role or reverse association.");
                }
            }
        }
    }
}
