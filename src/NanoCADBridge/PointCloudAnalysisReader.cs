using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text.Json;
using NanoCAD.Contracts;
using Teigha.DatabaseServices;
using Teigha.Geometry;
using Cloud = HostMgd.PointClouds;
using static NanoCADBridge.PointCloudReader;

namespace NanoCADBridge;

internal static class PointCloudAnalysisReader
{
    // Yield before the existing five-second native read queue deadline. This is a page
    // budget, never a limit on the number of points in a cloud or requested subset.
    private const long PageMilliseconds = 1600;
    private const int ItemByteBudget = Wire.PayloadBudget - 65536;
    private static readonly byte[] CursorKey = RandomNumberGenerator.GetBytes(32);

    public static object Read(PointCloudAnalysisArguments args, DocumentState state, Transaction tx, ObjectId model, string instance, CancellationToken cancellation)
    {
        var id = Resolve(args.Read, state, tx, model, cancellation);
        return ReadLoaded(args, id, state, tx, instance, cancellation);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static object ReadLoaded(PointCloudAnalysisArguments args, ObjectId id, DocumentState state, Transaction tx, string instance, CancellationToken cancellation)
    {
        var watch = Stopwatch.StartNew();
        using var source = Source.Open(id, state, tx, instance, cancellation);
        source.RequireLoaded();
        if (args.Mode == "measure" && args.Measurement!.Kind == "feature_diameter")
            return MeasureFeature(args, source, cancellation);
        object result;
        PointCloudReadScope402.BindSource(source);
        var iterator = source.AllIterator(cancellation);
        try
        {
            var access = ObserveIteratorAccess(iterator);
            using var info = NativeRead("iterator_cloud_info", () => iterator.CloudInfo());
            if (info == null) throw new DomainException("POINT_CLOUD_DATA_UNAVAILABLE", "The native point iterator did not provide CloudInfo.");
            var frame = new Frame(info, access);
            result = args.Mode == "measure"
                ? MeasurePoints(args, source, iterator, frame, cancellation)
                : PagePoints(args, source, iterator, info, frame, watch, cancellation);
        }
        finally
        {
            NativeRead("iterator_release", iterator.Dispose);
            PointCloudReadScope402.IteratorReleased();
        }
        // False update-on-release is explicit; still verify the actual source after release.
        NativeRead("source_verification_after_iterator_release", () => source.Verify(cancellation));
        NativeRead("source_observation_verification_after_iterator_release", () => PointCloudReadScope402.SourceVerified(source, cancellation));
        return result;
    }

    internal static object ReadFeatures(PointCloudInspectArguments args, Source source, CancellationToken cancellation)
    {
        var watch = Stopwatch.StartNew();
        source.RequireLoaded();
        var info = source.Info ?? throw new DomainException("POINT_CLOUD_DATA_UNAVAILABLE", "Loaded insertion did not provide native feature metadata.");
        var key = Digest(new { operation = "features", source.Fingerprint, source.SourceRef, source.State.Revision,
            args.Read.BridgeId, args.Read.DocumentId, args.Read.ExpectedRevision, args.PageSize });
        var page = OpenCursor(args.Cursor, key, "features");
        uint featureId = page.NextFeature ?? NativeRead("feature_first", () => info.getFirstGeometricFeature(global::FeatureType.AnyType));
        if (featureId != uint.MaxValue && !NativeRead("feature_enumeration_validity", () => info.isGeometricFeatureValid(featureId)))
        {
            // The public null sentinel is uint.MaxValue, but an imported source can
            // return another invalid first identity. Accept empty only when the SDK
            // confirms zero stored features, never for an invalid observed cursor or
            // a positive count. This does not soften explicit feature measurements.
            uint count = NativeRead("feature_count", () => info.getGeometricFeaturesCount(global::FeatureType.AnyType));
            if (page.NextFeature != null || count != 0)
                throw new DomainException("POINT_CLOUD_ITERATOR_FAILED", "Native feature enumeration returned an invalid identity despite an observed cursor or nonempty feature count.", details: new
                { native_stage = "feature_enumeration_validity", feature_id = featureId, stored_feature_count = count, resumed = page.NextFeature != null });
            NativeRead("source_verification", () => source.Verify(cancellation));
            return source.Metadata("features", Array.Empty<FeatureRecord>(), true, null);
        }
        page.FirstFeature ??= featureId;
        var items = new List<FeatureRecord>();
        var seen = new HashSet<uint>();
        int bytes = 0;
        int featureByteBudget = Math.Min(ItemByteBudget, Wire.PayloadBudget - 16384 -
            JsonSerializer.SerializeToUtf8Bytes(source.Metadata("features", Array.Empty<FeatureRecord>(), false, null), Wire.Json).Length);
        while (featureId != uint.MaxValue)
        {
            cancellation.ThrowIfCancellationRequested();
            if (items.Count > 0 && watch.ElapsedMilliseconds >= PageMilliseconds) break;
            if (!seen.Add(featureId)) throw new DomainException("POINT_CLOUD_ITERATOR_FAILED", "Native feature enumeration repeated an identity within this page.");
            var item = Feature(info, featureId);
            int size = JsonSerializer.SerializeToUtf8Bytes(item, Wire.Json).Length + 1;
            if (bytes + size > featureByteBudget)
            {
                if (items.Count == 0) throw new DomainException("ENTITY_TOO_LARGE", "One native feature exceeds the response page byte budget.");
                break;
            }
            items.Add(item); bytes += size;
            page.Scanned = Increment(page.Scanned); page.Matched = Increment(page.Matched);
            uint next = info.getNextGeometricFeature(featureId);
            if (next == featureId || next != uint.MaxValue && next == page.FirstFeature)
                throw new DomainException("POINT_CLOUD_ITERATOR_FAILED", "Native feature enumeration did not advance to a new identity.");
            featureId = next;
            if (items.Count >= args.PageSize) break;
        }
        bool complete = featureId == uint.MaxValue;
        page.NextFeature = complete ? null : featureId;
        NativeRead("source_verification", () => source.Verify(cancellation));
        return source.Metadata("features", items, complete, complete ? null : MakeCursor(page));
    }

    private static object PagePoints(PointCloudAnalysisArguments args, Source source, Cloud.PointCloudVertexIterator iterator,
        Cloud.PointCloudInfo info, Frame frame, Stopwatch watch, CancellationToken cancellation)
    {
        var key = Digest(new { operation = "analysis", source.Fingerprint, source.SourceRef, source.State.Revision,
            args.Read.BridgeId, args.Read.DocumentId, args.Read.ExpectedRevision, args.Mode, args.Units,
            args.Section, args.Region, args.PageSize, args.QueryPoint, args.IncludeAttributes,
            coordinate_basis = "native_insertion_iterator_vertex",
            transform = Matrix(frame.Transform), back_transform = Matrix(frame.BackTransform),
            scheme = Decimal(info.DataScheme), metadata_count = Decimal(info.PointsCount) });
        var page = OpenCursor(args.Cursor, key, "points");
        bool positioned;
        if (args.Cursor == null)
            positioned = First(iterator);
        else
        {
            if (!page.NextPoint.HasValue) throw InvalidCursor();
            Seek(iterator, page.NextPoint.Value);
            positioned = true;
        }
        // An iterator's actual end/empty state determines scan completion, never a
        // CloudInfo counter. Metadata zero is retained in the result as an observation.
        if (positioned && !NativeRead("iterator_xyz_data_valid", () => info.IsDataValid(1)))
            throw new DomainException("POINT_CLOUD_DATA_UNAVAILABLE", "This native iterator has no valid XYZ data.");
        var items = new List<PointRecord>();
        ulong pageScanned = 0, pageMatched = 0;
        int bytes = 0;
        while (positioned)
        {
            cancellation.ThrowIfCancellationRequested();
            if (pageScanned > 0 && watch.ElapsedMilliseconds >= PageMilliseconds) break;
            var point = ReadPoint(iterator, frame, args.Section, args.IncludeAttributes, info);
            bool included = Included(point, args.Section, args.Region);
            if (included && args.Mode != "statistics")
            {
                int size = JsonSerializer.SerializeToUtf8Bytes(point, Wire.Json).Length + 1;
                if (bytes + size > ItemByteBudget)
                {
                    if (items.Count == 0) throw new DomainException("ENTITY_TOO_LARGE", "One point record exceeds the response page byte budget.");
                    break; // This point is unconsumed; resume at its actual native index.
                }
                items.Add(point); bytes += size;
            }
            pageScanned = Increment(pageScanned); page.Scanned = Increment(page.Scanned);
            page.FrameChecked = Increment(page.FrameChecked);
            page.VertexAgreement = CombineAgreement(page.VertexAgreement, frame.LastVertexAgreement);
            page.BackAgreement = CombineAgreement(page.BackAgreement, frame.LastBackAgreement);
            if (included)
            {
                pageMatched = Increment(pageMatched); page.Matched = Increment(page.Matched);
                if (args.Mode == "statistics") Accumulate(page.Statistics, point, args.QueryPoint);
            }
            positioned = Advance(iterator);
            if (pageMatched >= (ulong)args.PageSize) break;
        }
        bool complete = !positioned;
        page.NextPoint = positioned ? NativeRead("iterator_continuation_index", () => iterator.Index) : null;
        var next = complete ? null : MakeCursor(page);
        NativeRead("source_verification", () => source.Verify(cancellation));
        return Analysis(args, source, items,
            new Scan(Decimal(pageScanned), Decimal(page.Scanned), Decimal(page.Matched), complete, next,
                source.Info == null ? null : Decimal(source.Info.PointsCount)),
            Observation(page.FrameChecked, page.VertexAgreement, page.BackAgreement, frame.AccessObservation), args.Mode == "statistics" ? Statistics(page.Statistics, args, complete) : null, null);
    }

    private static object MeasurePoints(PointCloudAnalysisArguments args, Source source, Cloud.PointCloudVertexIterator iterator,
        Frame frame, CancellationToken cancellation)
    {
        var measurement = args.Measurement!;
        using var info = NativeRead("measurement_iterator_cloud_info", () => iterator.CloudInfo());
        if (info == null || !NativeRead("iterator_xyz_data_valid", () => info.IsDataValid(1)))
            throw new DomainException("POINT_CLOUD_DATA_UNAVAILABLE", "This native iterator has no valid XYZ data for point measurement.");
        cancellation.ThrowIfCancellationRequested();
        Seek(iterator, measurement.PointIndex!.Value);
        var first = ReadPoint(iterator, frame, null, false, info);
        PointRecord? second = null;
        double value;
        double? signed = null;
        ReferencePlane? plane = null;
        ulong checkedPoints = 1;
        if (measurement.Kind == "point_distance")
        {
            cancellation.ThrowIfCancellationRequested();
            Seek(iterator, measurement.SecondPointIndex!.Value);
            second = ReadPoint(iterator, frame, null, false, info);
            value = Distance(first.point_wcs, second.point_wcs);
            checkedPoints = 2;
        }
        else
        {
            plane = new(measurement.PlaneOrigin!, measurement.PlaneNormal!);
            signed = Dot(Subtract(first.point_wcs, plane.origin), plane.normal);
            value = Math.Abs(signed.Value);
        }
        NativeRead("source_verification", () => source.Verify(cancellation));
        return Analysis(args, source, Array.Empty<PointRecord>(),
            new Scan(Decimal(checkedPoints), Decimal(checkedPoints), Decimal(checkedPoints), true, null,
                source.Info == null ? null : Decimal(source.Info.PointsCount)), Observation(checkedPoints, frame.VertexAgreement, frame.BackAgreement, frame.AccessObservation), null,
            new MeasurementRecord(measurement.Kind, "available", value, signed, null, first, second, plane, null,
                measurement.Kind == "point_distance" ? "Distance between two observed native cloud indices." :
                "Distance from one observed native cloud index to the explicitly supplied reference plane.", "WCS", null));
    }

    private static object MeasureFeature(PointCloudAnalysisArguments args, Source source, CancellationToken cancellation)
    {
        cancellation.ThrowIfCancellationRequested();
        var info = source.Info ?? throw new DomainException("POINT_CLOUD_DATA_UNAVAILABLE", "Loaded insertion did not provide native feature metadata.");
        var feature = Feature(info, args.Measurement!.FeatureId!.Value);
        double? native = feature.geometry?.radius is { } radius ? Finite(2 * radius) : null;
        string? reason = null;
        if (feature.native_type is not (2 or 7))
            reason = "Diameter is defined here only for an existing native Cylinder or Sphere; no fit was performed.";
        else if (native == null || native <= 0)
            reason = "This stored feature has no positive finite native diameter.";
        else if (!Matrix((Matrix3d)info.Transformation).SequenceEqual(Matrix(Matrix3d.Identity)))
            reason = "The stored feature frame is not established for this nonidentity insertion transform; native radius is not labeled as a WCS diameter.";
        NativeRead("source_verification", () => source.Verify(cancellation));
        return Analysis(args, source, Array.Empty<PointRecord>(), new Scan("0", "0", "0", true, null, Decimal(info.PointsCount)),
            Observation(0), null, new MeasurementRecord("feature_diameter", reason == null ? "available" : "unavailable",
                reason == null ? native : null, null, reason, null, null, null, feature,
                "Stored native Cylinder/Sphere radius; no raw-point fitting or manufacturing tolerance is inferred.", "WCS", native));
    }

    private static object Analysis(PointCloudAnalysisArguments args, Source source, IReadOnlyList<PointRecord> items,
        Scan scan, FrameObservation observation, object? statistics, MeasurementRecord? measurement) => new
    {
        mode = args.Mode, source_ref = source.SourceRef, definition_ref = source.DefinitionRef,
        units = "drawing_units", drawing_unit = new { name = source.State.Document.Database.Insunits.ToString(), value = (int)source.State.Document.Database.Insunits },
        source_units_known = false, source_units_note = SourceUnitsNote,
        coordinate_frame = "WCS", selection_method = args.Mode == "measure" && args.Measurement!.Kind == "feature_diameter"
            ? "native_feature_identity" : "native_all_iterator_exact_predicate",
        items, scan, frame_observation = observation, statistics, measurement
    };

    private static FeatureRecord Feature(Cloud.PointCloudInfo info, uint id)
    {
        if (id == uint.MaxValue || !info.isGeometricFeatureValid(id))
            throw new DomainException("POINT_CLOUD_FEATURE_NOT_FOUND", "Native feature identity is not valid in this cloud.");
        using var feature = info.getGeometricFeature(id);
        if (feature == null || feature.Feature != id)
            throw new DomainException("OBJECT_IDENTITY_MISMATCH", "Native feature wrapper did not retain the requested feature identity.");
        FeatureGeometry? geometry = null;
        switch ((int)feature.Type)
        {
            case 1:
                using (var plane = Cloud.PlaneFeature.asPlaneFeature(feature))
                    geometry = new(origin: Point(plane.Origin), normal: Vector(plane.Normal));
                break;
            case 2:
                using (var cylinder = Cloud.CylinderFeature.asCylinderFeature(feature))
                    geometry = new(origin: Point(cylinder.Origin), guide: Vector(cylinder.Guide), radius: Finite(cylinder.Radius), height: Finite(cylinder.Height));
                break;
            case 3:
                using (var intersection = Cloud.Planes2IntersectionFeature.asPlanes2IntersectionFeature(feature))
                    geometry = new(start: Point(intersection.Start), end: Point(intersection.End), plane_ids: PlaneIds(intersection.Plane1, intersection.Plane2));
                break;
            case 4:
                using (var intersection = Cloud.Planes3IntersectionFeature.asPlanes3IntersectionFeature(feature))
                    geometry = new(point: Point(intersection.Point), plane_ids: PlaneIds(intersection.Plane1, intersection.Plane2, intersection.Plane3));
                break;
            case 7:
                using (var sphere = Cloud.SphereFeature.asSphereFeature(feature))
                    geometry = new(center: Point(sphere.Center), radius: Finite(sphere.Radius));
                break;
            case 9:
                using (var cone = Cloud.ConeFeature.asConeFeature(feature))
                    geometry = new(origin: Point(cone.Origin), guide: Vector(cone.Guide), radius1: Finite(cone.Radius1), radius2: Finite(cone.Radius2), height: Finite(cone.Height));
                break;
            case 10:
                using (var torus = Cloud.TorusFeature.asTorusFeature(feature))
                    geometry = new(origin: Point(torus.Origin), guide: Vector(torus.Guide), major_radius: Finite(torus.MajorRadius), minor_radius: Finite(torus.MinorRadius));
                break;
        }
        return new(id, feature.Type.ToString(), (int)feature.Type, Text(feature.Name), Decimal(feature.Population), Finite(feature.Deviation), geometry);
    }

    private static uint[]? PlaneIds(params uint[] ids) => ids.Any(id => id == uint.MaxValue) ? null : ids;

    private static string ObserveIteratorAccess(Cloud.PointCloudVertexIterator iterator)
    {
        // IsReadOnly is observed, not treated as an undocumented factory precondition.
        // This reader invokes no point/feature/clip setters regardless of this flag.
        string observation;
        try { observation = "Native iterator IsReadOnly=" + iterator.IsReadOnly.ToString().ToLowerInvariant() + "."; }
        catch (Teigha.Runtime.Exception exception) { observation = "Native iterator IsReadOnly is unavailable: " + exception.ErrorStatus + "."; }
        catch (System.Exception exception) { observation = "Native iterator IsReadOnly is unavailable: " + exception.GetType().Name + "."; }
        return "Iterator creation uses a scoped insertion access-mode lease; any acquired write-open is restored before traversal; updateOnReleasing=false; getters-only point access. " + observation;
    }

    private static bool First(Cloud.PointCloudVertexIterator iterator)
    {
        if (NativeRead("iterator_first_empty", () => iterator.IsEmpty)) return false;
        bool moved = NativeRead("iterator_first", iterator.MoveFirst);
        if (NativeRead("iterator_end", () => iterator.IsEnd)) return false;
        if (!moved) throw new DomainException("POINT_CLOUD_ITERATOR_FAILED", "Native iterator could not move to its first point.");
        return true;
    }

    private static void Seek(Cloud.PointCloudVertexIterator iterator, ulong index)
    {
        if (!NativeRead("iterator_seek_validity", () => iterator.IsIndexValid(index)) || !NativeRead("iterator_seek", () => iterator.SetIteratorTo(index)) ||
            NativeRead("iterator_seek_end", () => iterator.IsEnd) || NativeRead("iterator_seek_index", () => iterator.Index) != index)
            throw new DomainException("POINT_CLOUD_POINT_NOT_FOUND", "Native point index is unavailable in the current cloud and revision; obtain a fresh point page.");
    }

    private static bool Advance(Cloud.PointCloudVertexIterator iterator)
    {
        ulong previous = NativeRead("iterator_next_previous_index", () => iterator.Index);
        bool advanced = NativeRead("iterator_next", iterator.Next);
        if (NativeRead("iterator_end", () => iterator.IsEnd)) return false;
        if (!advanced || NativeRead("iterator_next_index", () => iterator.Index) == previous)
            throw new DomainException("POINT_CLOUD_ITERATOR_FAILED", "Native iterator did not advance to another point index.");
        return true;
    }

    private sealed class Frame
    {
        internal readonly Matrix3d Transform;
        internal readonly Matrix3d BackTransform;
        internal readonly string AccessObservation;
        internal bool? LastVertexAgreement { get; private set; }
        internal bool? LastBackAgreement { get; private set; }
        internal bool? VertexAgreement { get; private set; } = true;
        internal bool? BackAgreement { get; private set; } = true;
        internal Frame(Cloud.PointCloudInfo info, string accessObservation)
        {
            AccessObservation = accessObservation;
            Transform = NativeRead("iterator_transformation", () => (Matrix3d)info.Transformation);
            BackTransform = NativeRead("iterator_back_transformation", () => (Matrix3d)info.BackTransformation);
            Matrix(Transform); Matrix(BackTransform);
        }
        internal (double[] World, double[] Base) Read(Cloud.PointCloudVertexIterator iterator)
        {
            var nativeBase = NativeRead("point_base_vertex", () => iterator.BaseVertex);
            var nativeVertex = NativeRead("point_vertex", () => iterator.Vertex);
            var basePoint = Point(nativeBase);
            var vertex = Point(nativeVertex);
            // Vertex is the actual getter of this insertion-scoped native iterator.
            // CloudInfo matrices can contain source recentering; applying that matrix
            // here shifted the observed native point outside the insertion's WCS bounds.
            // Keep both matrix relationships as evaluated diagnostics, not a geometry gate.
            LastVertexAgreement = CompareTransformed(nativeBase, Transform, vertex);
            LastBackAgreement = CompareTransformed(nativeVertex, BackTransform, basePoint);
            VertexAgreement = CombineAgreement(VertexAgreement, LastVertexAgreement);
            BackAgreement = CombineAgreement(BackAgreement, LastBackAgreement);
            return (vertex, basePoint);
        }
    }

    private static bool? CompareTransformed(Point3d point, Matrix3d transform, double[] observed)
    {
        try { return SamePoint(Point(point.TransformBy(transform)), observed); }
        catch (DomainException) { return null; }
        catch (Teigha.Runtime.Exception) { return null; }
    }

    // False is a witnessed disagreement. Null means an evaluated relationship was
    // unavailable; otherwise all consumed points must agree across every cursor page.
    private static bool? CombineAgreement(bool? previous, bool? current) =>
        previous == false || current == false ? false : previous == null || current == null ? null : true;

    private static bool SamePoint(double[] first, double[] second)
    {
        // This tolerance describes the optional matrix relationship observation only.
        // It is not a tolerance for selecting a point into a requested slab/region.
        double scale = Math.Max(1, first.Concat(second).Max(v => Math.Abs(v)));
        return Enumerable.Range(0, 3).All(i => Math.Abs(first[i] - second[i]) <= scale * 1e-10);
    }

    private static PointRecord ReadPoint(Cloud.PointCloudVertexIterator iterator, Frame frame, PointCloudSection? section,
        bool includeAttributes, Cloud.PointCloudInfo info)
    {
        var (world, basePoint) = frame.Read(iterator);
        double[]? local = null; double? signed = null;
        if (section != null)
        {
            var delta = Subtract(world, section.Origin);
            local = [Dot(delta, section.XAxis), Dot(delta, Cross(section.Normal, section.XAxis))];
            signed = Dot(delta, section.Normal);
        }
        PointAttributes? attributes = includeAttributes ? new(
            info.IsDataValid(2) ? NativeRead("point_intensity", () => iterator.Intensity) : null,
            info.IsDataValid(4) ? NativeRead("point_classification", () => iterator.Class) : null,
            info.IsDataValid(256) ? NativeRead("point_rgb", () => iterator.ColorRGB) : null) : null;
        return new(Decimal(NativeRead("point_index", () => iterator.Index)), world, basePoint, local, signed, attributes);
    }

    private static bool Included(PointRecord point, PointCloudSection? section, PointCloudRegion? region)
    {
        if (region != null && Enumerable.Range(0, 3).Any(i => point.point_wcs[i] < region.Min[i] || point.point_wcs[i] > region.Max[i])) return false;
        return section == null || Math.Abs(point.signed_section_distance!.Value) <= section.Thickness / 2;
    }

    private static FrameObservation Observation(ulong count, bool? vertexAgreement = null, bool? backAgreement = null,
        string? accessObservation = null) => new(Decimal(count), count == 0 ? null : vertexAgreement, count == 0 ? null : backAgreement,
        (count == 0 ? "No native point coordinate frame has been observed in this response." :
        "Point coordinates are the actual native insertion iterator Vertex, without another CloudInfo transformation. " +
        "The diagnostic booleans describe cumulative evaluated CloudInfo Transform(BaseVertex)/BackTransform(Vertex) agreements; " +
        "false is observed disagreement, null is unavailable. These are not admission conditions. " +
        "This getter basis is corroborated by observed insertion bounds and source coordinates; moved/rotated insertion semantics are not independently assessed.") +
        (accessObservation == null ? "" : " " + accessObservation));

    private static void Accumulate(StatisticsState statistics, PointRecord point, double[]? query)
    {
        statistics.Count = Increment(statistics.Count);
        if (statistics.Min == null)
        {
            statistics.Min = (double[])point.point_wcs.Clone(); statistics.Max = (double[])point.point_wcs.Clone();
            statistics.Mean = (double[])point.point_wcs.Clone(); statistics.M2 = [0, 0, 0];
        }
        else
        {
            double weight = 1.0 / statistics.Count;
            for (int axis = 0; axis < 3; axis++)
            {
                statistics.Min[axis] = Math.Min(statistics.Min[axis], point.point_wcs[axis]);
                statistics.Max![axis] = Math.Max(statistics.Max[axis], point.point_wcs[axis]);
                double previous = statistics.Mean![axis];
                double mean = Finite(previous * (1 - weight) + point.point_wcs[axis] * weight);
                statistics.Mean[axis] = mean;
                if (statistics.M2 != null)
                {
                    double moment = statistics.M2[axis] + (point.point_wcs[axis] - previous) * (point.point_wcs[axis] - mean);
                    if (!double.IsFinite(moment)) statistics.M2 = null;
                    else statistics.M2[axis] = moment;
                }
            }
        }
        if (point.section_coordinates != null)
        {
            if (statistics.SectionMin == null)
            {
                statistics.SectionMin = (double[])point.section_coordinates.Clone(); statistics.SectionMax = (double[])point.section_coordinates.Clone();
                statistics.DistanceMin = point.signed_section_distance; statistics.DistanceMax = point.signed_section_distance;
            }
            else
            {
                for (int axis = 0; axis < 2; axis++)
                {
                    statistics.SectionMin[axis] = Math.Min(statistics.SectionMin[axis], point.section_coordinates[axis]);
                    statistics.SectionMax![axis] = Math.Max(statistics.SectionMax[axis], point.section_coordinates[axis]);
                }
                statistics.DistanceMin = Math.Min(statistics.DistanceMin!.Value, point.signed_section_distance!.Value);
                statistics.DistanceMax = Math.Max(statistics.DistanceMax!.Value, point.signed_section_distance!.Value);
            }
        }
        if (query != null)
        {
            double distance = Distance(point.point_wcs, query);
            if (statistics.NearestDistance == null || distance < statistics.NearestDistance.Value)
            { statistics.NearestDistance = distance; statistics.Nearest = point; }
        }
    }

    private static object Statistics(StatisticsState state, PointCloudAnalysisArguments args, bool complete) => new
    {
        count = Decimal(state.Count),
        bounds_wcs = state.Min == null ? null : new { min = state.Min, max = state.Max },
        centroid_wcs = state.Mean,
        standard_deviation_wcs = state.M2 == null || state.Count == 0 ? null : state.M2.Select(m => Finite(Math.Sqrt(Math.Max(0, m) / state.Count))).ToArray(),
        section_bounds = state.SectionMin == null ? null : new { min = state.SectionMin, max = state.SectionMax },
        signed_section_distance_range = state.DistanceMin == null ? null : new { min = state.DistanceMin.Value, max = state.DistanceMax!.Value },
        query_point = args.QueryPoint, nearest_point = state.Nearest, nearest_distance = state.NearestDistance,
        scope = (complete ? "Complete native All iterator traversal of the requested subset; original file density is not independently established." : "Partial cumulative scan; continue the cursor before treating statistics or nearest point as final.") +
            (state.Count > 0 && state.M2 == null ? " Population standard deviation is unavailable because its intermediate moment exceeds floating-point range." : " Population standard deviation is computed from observed points; no fit is performed.")
    };

    private static double[] Subtract(double[] first, double[] second) => Enumerable.Range(0, 3).Select(i => Finite(first[i] - second[i])).ToArray();
    private static double Dot(double[] first, double[] second) => Finite(Enumerable.Range(0, 3).Sum(i => first[i] * second[i]));
    private static double[] Cross(double[] first, double[] second) => [first[1] * second[2] - first[2] * second[1], first[2] * second[0] - first[0] * second[2], first[0] * second[1] - first[1] * second[0]];
    private static double Distance(double[] first, double[] second)
    {
        var delta = Subtract(first, second);
        double scale = delta.Max(v => Math.Abs(v));
        return scale == 0 ? 0 : Finite(scale * Math.Sqrt(delta.Sum(v => (v / scale) * (v / scale))));
    }
    private static ulong Increment(ulong count) => count == ulong.MaxValue ?
        throw new DomainException("ANALYSIS_NUMERIC_RANGE", "Native scan counter exceeds UInt64 representation.") : count + 1;

    private static PageState OpenCursor(string? cursor, string query, string kind)
    {
        if (cursor == null) return new PageState { Query = query, Kind = kind };
        try
        {
            if (cursor.Length > 8192) throw InvalidCursor();
            string[] parts = cursor.Split('.');
            if (parts.Length != 2) throw InvalidCursor();
            byte[] bytes = Convert.FromBase64String(parts[0]), signature = Convert.FromBase64String(parts[1]);
            using var hmac = new HMACSHA256(CursorKey);
            if (!CryptographicOperations.FixedTimeEquals(signature, hmac.ComputeHash(bytes))) throw InvalidCursor();
            var state = JsonSerializer.Deserialize<PageState>(bytes, Wire.Json);
            if (state == null || state.Query != query || state.Kind != kind || state.Version != 1)
                throw InvalidCursor();
            return state;
        }
        catch (FormatException) { throw InvalidCursor(); }
        catch (JsonException) { throw InvalidCursor(); }
    }

    private static string MakeCursor(PageState state)
    {
        byte[] bytes = JsonSerializer.SerializeToUtf8Bytes(state, Wire.Json);
        using var hmac = new HMACSHA256(CursorKey);
        string cursor = Convert.ToBase64String(bytes) + "." + Convert.ToBase64String(hmac.ComputeHash(bytes));
        if (cursor.Length > 8192) throw new DomainException("RESULT_TOO_LARGE", "Numerical continuation exceeds the cursor byte budget.");
        return cursor;
    }
    private static DomainException InvalidCursor() => new("INVALID_CURSOR", "Point-cloud continuation is invalid, stale, belongs to another source/query/revision, or comes from another bridge lifetime. Obtain fresh context and start a new read.");

    // Only authenticated finite numeric state and actual native indices cross pages.
    private sealed class PageState
    {
        public PageState() { }
        public int Version { get; set; } = 1;
        public string Query { get; set; } = "";
        public string Kind { get; set; } = "";
        public ulong? NextPoint { get; set; }
        public uint? NextFeature { get; set; }
        public uint? FirstFeature { get; set; }
        public ulong Scanned { get; set; }
        public ulong Matched { get; set; }
        public ulong FrameChecked { get; set; }
        public bool? VertexAgreement { get; set; } = true;
        public bool? BackAgreement { get; set; } = true;
        public StatisticsState Statistics { get; set; } = new();
    }
    private sealed class StatisticsState
    {
        public StatisticsState() { }
        public ulong Count { get; set; }
        public double[]? Min { get; set; }
        public double[]? Max { get; set; }
        public double[]? Mean { get; set; }
        public double[]? M2 { get; set; }
        public double[]? SectionMin { get; set; }
        public double[]? SectionMax { get; set; }
        public double? DistanceMin { get; set; }
        public double? DistanceMax { get; set; }
        public PointRecord? Nearest { get; set; }
        public double? NearestDistance { get; set; }
    }
    private sealed record PointAttributes(ushort? intensity, byte? classification, uint? rgb);
    private sealed record PointRecord(string point_index, double[] point_wcs, double[] base_point, double[]? section_coordinates,
        double? signed_section_distance, PointAttributes? attributes);
    private sealed record Scan(string page_scanned_count, string total_scanned_count, string total_matched_count,
        bool complete, string? next_cursor, string? metadata_point_count);
    private sealed record FrameObservation(string checked_point_count, bool? vertex_matches_transformed_base,
        bool? back_transform_matches_base, string status);
    private sealed record ReferencePlane(double[] origin, double[] normal, string provenance = "explicit_input_reference");
    private sealed record MeasurementRecord(string kind, string status, double? value, double? signed_value, string? reason,
        PointRecord? point, PointRecord? second_point, ReferencePlane? reference_plane, FeatureRecord? feature,
        string provenance, string coordinate_frame, double? native_value);
}
