using System.Globalization;
using NanoCAD.Contracts;
using Teigha.DatabaseServices;
using Teigha.Geometry;

namespace NanoCADBridge;

// Request-local detached section. Only numerical summaries travel in the authenticated cloud cursor.
internal sealed class CloudSectionProfile : IDisposable
{
    private sealed record Edge(Curve Curve, double NativeStart, double Length, long First, long Count)
    {
        internal Point3d At(double localDistance) => Curve.GetPointAtDist(NativeStart + localDistance);
    }

    private sealed class Observations
    {
        internal ulong Count, Signed, FirstPoint, WorstPoint;
        internal double Min, Max, Mean;
    }

    // Each summary uses exactly seven 64-bit values. Reserve room for counters, double base64
    // encoding, HMAC and the existing scanner fields within its 8192-character cursor.
    private const int CursorGroupBytes = 3000;
    private readonly CadPlanarSection _section;
    private readonly Edge[] _edges;
    private readonly long _total;
    private readonly CloudProfileComparison _spec;
    private readonly PointCloudRegion _region;
    private readonly PointCloudSection _slab;
    private readonly Observations[] _bins;
    private readonly Action _check;
    private ulong _outsideReach, _ambiguous;
    internal string GeometryKey { get; }

    internal static CloudSectionProfile Open(PointCloudAnalysisArguments args, DocumentState state, Transaction tx,
        ObjectId model, CancellationToken cancellation)
    {
        var spec = args.Comparison!;
        var bits = ulong.Parse(spec.SolidHandle, NumberStyles.HexNumber, CultureInfo.InvariantCulture);
        var db = state.Document.Database;
        if (!db.TryGetObjectId(new Handle(unchecked((long)bits)), out var id) || id.IsNull || !id.IsValid || id.IsErased || id.IsEffectivelyErased)
            throw new DomainException("OBJECT_NOT_FOUND", "The requested profile solid is unavailable in this drawing.");
        if (tx.GetObject(id, OpenMode.ForRead) is not Solid3d body || body.OwnerId != model || body.GetType() != typeof(Solid3d))
            throw new DomainException("OUT_OF_SCOPE", "Select a native top-level Model Solid3d for the profile.");
        var slab = args.Section!;
        static Point3 P(double[] a) => new(a[0], a[1], a[2]);
        var n = new Vector3d(slab.Normal[0], slab.Normal[1], slab.Normal[2]);
        var x = new Vector3d(slab.XAxis[0], slab.XAxis[1], slab.XAxis[2]);
        var y = n.CrossProduct(x);
        var section = CadPlanarSection.Open(body, new(P(slab.Origin), P(slab.XAxis), new(y.X, y.Y, y.Z)), cancellation.ThrowIfCancellationRequested);
        try { return new(section, spec, args.Region!, slab, cancellation.ThrowIfCancellationRequested); }
        catch { section.Dispose(); throw; }
    }

    private CloudSectionProfile(CadPlanarSection section, CloudProfileComparison spec, PointCloudRegion region, PointCloudSection slab, Action check)
    {
        _section = section; _spec = spec; _region = region; _slab = slab; _check = check;
        var edges = new List<Edge>();
        long total = 0;
        foreach (var curve in section.BoundaryCurves())
        {
            check();
            double start = curve.GetDistanceAtParameter(curve.StartParam);
            double length = curve.GetDistanceAtParameter(curve.EndParam) - start;
            double count = Math.Max(1, Math.Ceiling(length / spec.SegmentLength));
            if (!double.IsFinite(start) || !double.IsFinite(length) || length <= 0 || count >= (double)long.MaxValue ||
                total > long.MaxValue - (long)count)
                throw new DomainException("ANALYSIS_NUMERIC_RANGE", "Native boundary length or requested segment numbering is outside representable range.");
            edges.Add(new(curve, start, length, total, (long)count));
            total += (long)count;
        }
        if (spec.StartSegment > 0 && spec.StartSegment >= total)
            throw new DomainException("INVALID_ARGUMENT", "start_segment exceeds the section's arclength segments.");
        _edges = edges.ToArray(); _total = total;
        _bins = Enumerable.Range(0, (int)Math.Min(total - spec.StartSegment, CursorGroupBytes / (7 * sizeof(ulong))))
            .Select(_ => new Observations()).ToArray();
        // An edge ordinal is local to this observed section, never a persistent subentity identity.
        GeometryKey = PointCloudReader.Digest(_edges.Select(e => new
        {
            type = e.Curve.GetType().Name,
            e.Length,
            e.Count,
            stations = new[] { 0d, 0.25, 0.5, 0.75, 1 }.Select(t => _section.Local(e.At(e.Length * t))).ToArray()
        }).ToArray());
    }

    internal void Observe(string indexText, double[] uv)
    {
        if (_edges.Length == 0) return;
        var p = _section.World(new(uv[0], uv[1]));
        double nearest = double.PositiveInfinity, second = double.PositiveInfinity;
        Edge? best = null;
        Point3d closest = default;
        bool ambiguousStation = false;
        foreach (var e in _edges)
        {
            _check();
            // At a circular centre every station is equally near. Some kernels choose one
            // arbitrary point there, and others cannot provide a closest point.
            double? centreDistance = e.Curve switch
            {
                Circle circle when p.DistanceTo(circle.Center) <= Tolerance.Global.EqualPoint => circle.Radius,
                Arc arc when p.DistanceTo(arc.Center) <= Tolerance.Global.EqualPoint => arc.Radius,
                _ => null
            };
            var projected = centreDistance.HasValue ? default : e.Curve.GetClosestPointTo(p, false);
            double d = centreDistance ?? projected.DistanceTo(p);
            if (!double.IsFinite(d)) throw new DomainException("SECTION_PROJECTION_UNAVAILABLE", "Native closest-point calculation returned a non-finite distance.");
            if (d < nearest) { second = nearest; nearest = d; best = e; closest = projected; ambiguousStation = centreDistance.HasValue; }
            else second = Math.Min(second, d);
        }
        if (nearest > _spec.CaptureDistance) { _outsideReach++; return; }
        if (ambiguousStation || second - nearest <= Tolerance.Global.EqualPoint) { _ambiguous++; return; }
        var edge = best!;
        var along = edge.Curve.GetDistanceAtParameter(edge.Curve.GetParameterAtPoint(closest)) - edge.NativeStart;
        if (!double.IsFinite(along)) throw new DomainException("SECTION_PROJECTION_UNAVAILABLE", "Native projection did not yield a finite curve station.");
        long local = Math.Clamp((long)Math.Floor(along / edge.Length * edge.Count), 0, edge.Count - 1);
        long i = edge.First + local - _spec.StartSegment;
        if (i < 0 || i >= _bins.Length) return;
        var obs = _bins[(int)i];
        ulong index = ulong.Parse(indexText, CultureInfo.InvariantCulture);
        if (obs.Count++ == 0) obs.FirstPoint = index;
        string side;
        try { side = _section.Classify(p); }
        catch (DomainException e) when (e.Error.code == "NATIVE_CONTAINMENT_MISMATCH") { side = "undetermined"; }
        catch (Teigha.Runtime.Exception) { side = "undetermined"; }
        double? residual = side switch { "material" => -nearest, "void" => nearest, "boundary" => 0d, _ => null };
        if (residual is not double value) return;
        if (obs.Signed++ == 0) { obs.Min = obs.Max = obs.Mean = value; obs.WorstPoint = index; return; }
        if (Math.Abs(value) > Math.Max(Math.Abs(obs.Min), Math.Abs(obs.Max))) obs.WorstPoint = index;
        obs.Min = Math.Min(value, obs.Min); obs.Max = Math.Max(value, obs.Max);
        double weight = 1d / obs.Signed;
        obs.Mean = obs.Mean * (1 - weight) + value * weight;
    }

    internal void Restore(string? encoded)
    {
        if (encoded == null) return;
        using var stream = new MemoryStream(Convert.FromBase64String(encoded));
        using var r = new BinaryReader(stream);
        if (stream.Length != (2L + _bins.Length * 7L) * sizeof(ulong))
            throw new DomainException("INVALID_CURSOR", "Section summary no longer matches the native curve traversal.");
        _outsideReach = r.ReadUInt64(); _ambiguous = r.ReadUInt64();
        foreach (var b in _bins)
        {
            b.Count = r.ReadUInt64(); b.Signed = r.ReadUInt64(); b.FirstPoint = r.ReadUInt64(); b.WorstPoint = r.ReadUInt64();
            b.Min = r.ReadDouble(); b.Max = r.ReadDouble(); b.Mean = r.ReadDouble();
        }
    }

    internal string Save()
    {
        using var s = new MemoryStream();
        using var w = new BinaryWriter(s);
        w.Write(_outsideReach); w.Write(_ambiguous);
        foreach (var b in _bins)
        {
            w.Write(b.Count); w.Write(b.Signed); w.Write(b.FirstPoint); w.Write(b.WorstPoint);
            w.Write(b.Min); w.Write(b.Max); w.Write(b.Mean);
        }
        return Convert.ToBase64String(s.ToArray());
    }

    internal object Report(bool complete, EntityRef solid)
    {
        var rows = new List<object>();
        foreach (var e in _edges)
        {
            for (long i = Math.Max(_spec.StartSegment, e.First); i < Math.Min(_spec.StartSegment + _bins.Length, e.First + e.Count); i++)
            {
                _check();
                long j = i - e.First;
                double start = e.Length * ((double)j / e.Count), end = e.Length * ((double)(j + 1) / e.Count);
                var b = _bins[(int)(i - _spec.StartSegment)];
                rows.Add(new
                {
                    segment = i,
                    curve_type = e.Curve.GetType().Name,
                    edge_first_segment = e.First,
                    station_from = start,
                    station_to = end,
                    from_uv = _section.Local(e.At(start)),
                    to_uv = _section.Local(e.At(end)),
                    midpoint_uv = _section.Local(e.At(start / 2 + end / 2)),
                    coverage = b.Count > 0 ? "observed" : complete ? "unobserved_in_subset" : "scan_incomplete",
                    observed_points = b.Count.ToString(CultureInfo.InvariantCulture),
                    signed_points = b.Signed.ToString(CultureInfo.InvariantCulture),
                    first_point_index = b.Count > 0 ? b.FirstPoint.ToString(CultureInfo.InvariantCulture) : null,
                    largest_absolute_residual_point_index = b.Signed > 0 ? b.WorstPoint.ToString(CultureInfo.InvariantCulture) : null,
                    signed_residual = b.Signed > 0 ? new { min = b.Min, max = b.Max, mean = b.Mean } : null
                });
            }
        }
        long next = _spec.StartSegment + _bins.Length;
        return new
        {
            solid_ref = solid,
            section_status = _section.Status,
            section = new { origin = _slab.Origin, normal = _slab.Normal, x_axis = _slab.XAxis, thickness = _slab.Thickness },
            observation_region_wcs = new { min = _region.Min, max = _region.Max },
            segment_length = _spec.SegmentLength,
            capture_distance = _spec.CaptureDistance,
            start_segment = _spec.StartSegment,
            total_segments = _total,
            next_start_segment = complete && next < _total ? (long?)next : null,
            outside_capture_distance = _outsideReach.ToString(CultureInfo.InvariantCulture),
            ambiguous_points = _ambiguous.ToString(CultureInfo.InvariantCulture),
            method = "Project actual native cloud vertices onto the section plane and assign each to the nearest finite native boundary curve. Ties between edges and nonunique stations at circular centres are unassigned within native tolerance. Negative residuals are in material, positive residuals in void and zero is on the native boundary. Points without a reliable native side count as observed but contribute no signed value. One witness indicates observation somewhere in its arclength bin; empty bins carry no residual. The WCS region filters the cloud, while the full model boundary is reported. Each segment group scans the same cloud subset; scan and rejection counts are cumulative within that group.",
            segments = rows
        };
    }

    public void Dispose() => _section.Dispose();
}
