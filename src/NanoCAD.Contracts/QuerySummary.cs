using System.Text.Json.Serialization;

namespace NanoCAD.Contracts;

public readonly record struct QueryEnvelope(double MinX, double MinY, double MinZ, double MaxX, double MaxY, double MaxZ)
{
    public bool IsFiniteXY => double.IsFinite(MinX) && double.IsFinite(MinY) && double.IsFinite(MaxX) &&
        double.IsFinite(MaxY) && MinX <= MaxX && MinY <= MaxY;

    internal bool IsFiniteXYZ => IsFiniteXY && double.IsFinite(MinZ) && double.IsFinite(MaxZ) && MinZ <= MaxZ;

    public bool AvailableFor(QueryBounds407 filter) => filter.Min.Length == 3 ? IsFiniteXYZ : IsFiniteXY;

    public bool Matches(QueryBounds407 filter)
    {
        if (!AvailableFor(filter)) return false;
        bool Interval(double min, double max, int axis) => filter.Mode == "within"
            ? min >= filter.Min[axis] && max <= filter.Max[axis]
            : min <= filter.Max[axis] && max >= filter.Min[axis];
        return Interval(MinX, MaxX, 0) && Interval(MinY, MaxY, 1) &&
            (filter.Min.Length == 2 || Interval(MinZ, MaxZ, 2));
    }

    internal QueryEnvelope Union(QueryEnvelope other) => new(
        Math.Min(MinX, other.MinX), Math.Min(MinY, other.MinY), Math.Min(MinZ, other.MinZ),
        Math.Max(MaxX, other.MaxX), Math.Max(MaxY, other.MaxY), Math.Max(MaxZ, other.MaxZ));
}

public sealed class QuerySummary
{
    private readonly SortedDictionary<string, long> _types = new(StringComparer.Ordinal);
    private readonly SortedDictionary<string, long> _layers = new(StringComparer.OrdinalIgnoreCase);
    private readonly SortedDictionary<string, long> _nameMatches = new(StringComparer.Ordinal);
    private QueryEnvelope? _bounds;
    private long _total;
    private long _unavailable;
    private long _spatiallyExcluded;

    public void Add(string type, string layer, QueryEnvelope? envelope, QueryBounds407? filter, string? nameMatchSource = null)
    {
        if (filter != null)
        {
            if (envelope is not { } candidate || !candidate.AvailableFor(filter)) { _spatiallyExcluded++; return; }
            if (!candidate.Matches(filter)) return;
        }
        _total++;
        _types[type] = _types.GetValueOrDefault(type) + 1;
        _layers[layer] = _layers.GetValueOrDefault(layer) + 1;
        if (nameMatchSource != null) _nameMatches[nameMatchSource] = _nameMatches.GetValueOrDefault(nameMatchSource) + 1;
        if (envelope is { IsFiniteXYZ: true } finite) _bounds = _bounds?.Union(finite) ?? finite;
        else _unavailable++;
    }

    private sealed record Page(bool summary_only, long total_count, object by_type, object by_layer,
        object? bounds, long bounds_unavailable_count, long spatial_excluded_unavailable_bounds_count,
        bool complete, [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? next_cursor,
        string aggregation_scope, string space,
        [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] int? page_solids_excluded_unavailable_name,
        [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] object? by_body_name_match_source);

    public object Result(string space, string? nextCursor = null, bool continuation = false, int? unavailableBodyNames = null) => new Page(
        true, _total, _types.Select(count => new { type = count.Key, count = count.Value }).ToArray(),
        _layers.Select(count => new { layer = count.Key, count = count.Value }).ToArray(),
        _bounds is { } bounds ? new
        {
            min = new[] { bounds.MinX, bounds.MinY, bounds.MinZ },
            max = new[] { bounds.MaxX, bounds.MaxY, bounds.MaxZ },
            coordinate_system = "WCS",
            kind = "combined_candidate_envelope"
        } : null, _unavailable, _spatiallyExcluded, nextCursor == null, nextCursor,
        nextCursor == null && !continuation ? "entire_query" : "this_page", space, unavailableBodyNames,
        unavailableBodyNames == null ? null : _nameMatches.Select(pair => new { source = pair.Key, count = pair.Value }).ToArray());
}
