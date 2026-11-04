namespace NanoCAD.Contracts;

public readonly record struct QueryEnvelope(double MinX, double MinY, double MinZ, double MaxX, double MaxY, double MaxZ)
{
    public bool IsFiniteXY => double.IsFinite(MinX) && double.IsFinite(MinY) && double.IsFinite(MaxX) &&
        double.IsFinite(MaxY) && MinX <= MaxX && MinY <= MaxY;

    internal bool IsFiniteXYZ => IsFiniteXY && double.IsFinite(MinZ) && double.IsFinite(MaxZ) && MinZ <= MaxZ;

    public bool Matches(QueryBounds407 filter) => filter.Mode == "within"
        ? MinX >= filter.Min[0] && MinY >= filter.Min[1] && MaxX <= filter.Max[0] && MaxY <= filter.Max[1]
        : MinX <= filter.Max[0] && MinY <= filter.Max[1] && MaxX >= filter.Min[0] && MaxY >= filter.Min[1];

    internal QueryEnvelope Union(QueryEnvelope other) => new(
        Math.Min(MinX, other.MinX), Math.Min(MinY, other.MinY), Math.Min(MinZ, other.MinZ),
        Math.Max(MaxX, other.MaxX), Math.Max(MaxY, other.MaxY), Math.Max(MaxZ, other.MaxZ));
}

public sealed class QuerySummary
{
    private readonly SortedDictionary<string, long> _types = new(StringComparer.Ordinal);
    private readonly SortedDictionary<string, long> _layers = new(StringComparer.OrdinalIgnoreCase);
    private QueryEnvelope? _bounds;
    private long _total;
    private long _unavailable;
    private long _spatiallyExcluded;

    public void Add(string type, string layer, QueryEnvelope? envelope, QueryBounds407? filter)
    {
        if (filter != null)
        {
            if (envelope is not { IsFiniteXY: true } candidate) { _spatiallyExcluded++; return; }
            if (!candidate.Matches(filter)) return;
        }
        _total++;
        _types[type] = _types.GetValueOrDefault(type) + 1;
        _layers[layer] = _layers.GetValueOrDefault(layer) + 1;
        if (envelope is { IsFiniteXYZ: true } finite) _bounds = _bounds?.Union(finite) ?? finite;
        else _unavailable++;
    }

    public object Result(string space) => new
    {
        summary_only = true,
        total_count = _total,
        by_type = _types.Select(count => new { type = count.Key, count = count.Value }).ToArray(),
        by_layer = _layers.Select(count => new { layer = count.Key, count = count.Value }).ToArray(),
        bounds = _bounds is { } bounds ? new
        {
            min = new[] { bounds.MinX, bounds.MinY, bounds.MinZ },
            max = new[] { bounds.MaxX, bounds.MaxY, bounds.MaxZ },
            coordinate_system = "WCS",
            kind = "combined_candidate_envelope"
        } : null,
        bounds_unavailable_count = _unavailable,
        spatial_excluded_unavailable_bounds_count = _spatiallyExcluded,
        complete = true,
        space
    };
}
