namespace NanoCAD.Contracts;

public static class SelectedContours
{
    public const string Mode = "selected_contours";
    public sealed record Source(string Handle, Point2 Start, Point2 End, bool Closed);
    public sealed record Step(string handle, bool reverse);
    public sealed record End(string handle, string end, Point2 point);
    public sealed record Node(End[] ends, bool coincident);
    public sealed record Component(string status, string[] handles, Step[]? order, Node[] junctions);
    public sealed record Gap(End from, End nearest, double distance);
    public sealed record Selection(Component[] Components, Gap[] Gaps);
    private static double Distance(Point2 a, Point2 b) => Math.Sqrt(Math.Pow(a.X - b.X, 2) + Math.Pow(a.Y - b.Y, 2));

    // A proximity chain A~B~C is ambiguous when A and C differ by more than the tolerance.
    // Each vertex keeps all actual endpoints; no coordinates are snapped or averaged.
    public static Selection Build(IEnumerable<Source> selection, double tolerance, Action check)
    {
        var sources = selection.OrderBy(s => s.Handle, StringComparer.Ordinal).ToArray();
        var ends = sources.Where(s => !s.Closed)
            .SelectMany(s => new[] { new End(s.Handle, "start", s.Start), new End(s.Handle, "end", s.End) })
            .OrderBy(e => e.point.X).ToArray();
        var parent = Enumerable.Range(0, ends.Length).ToArray();
        int Find(int a)
        {
            while (parent[a] != a) { parent[a] = parent[parent[a]]; a = parent[a]; }
            return a;
        }
        for (int i = 0; i < ends.Length; i++)
        {
            check();
            for (int j = i + 1; j < ends.Length && ends[j].point.X - ends[i].point.X <= tolerance; j++)
            {
                check();
                if (Distance(ends[i].point, ends[j].point) <= tolerance) parent[Find(j)] = Find(i);
            }
        }
        var nodes = Enumerable.Range(0, ends.Length).GroupBy(Find).Select(group =>
        {
            var members = group.Select(i => ends[i]).ToArray();
            bool consistent = members.All(a => members.All(b => { check(); return Distance(a.point, b.point) <= tolerance; }));
            return new Node(members, consistent);
        }).ToArray();
        var endpointNode = nodes.SelectMany(n => n.ends.Select(e => (e, n))).ToDictionary(x => (x.e.handle, x.e.end), x => x.n);
        var components = new List<Component>();
        var visited = new HashSet<string>(StringComparer.Ordinal);
        foreach (var source in sources)
        {
            check();
            if (!visited.Add(source.Handle)) continue;
            if (source.Closed)
            {
                components.Add(new("loop", [source.Handle], [new(source.Handle, false)], []));
                continue;
            }
            var pending = new Stack<string>(); pending.Push(source.Handle);
            var group = new List<string>();
            var vertices = new HashSet<Node>();
            while (pending.TryPop(out var current))
            {
                check(); group.Add(current);
                foreach (string side in new[] { "start", "end" })
                {
                    var node = endpointNode[(current, side)];
                    if (!vertices.Add(node)) continue;
                    foreach (var e in node.ends) if (visited.Add(e.handle)) pending.Push(e.handle);
                }
            }
            var open = vertices.Where(node => node.ends.Length == 1).ToArray();
            string status = vertices.Any(n => !n.coincident) ? "ambiguous_coincidence" :
                vertices.Any(n => n.ends.Length > 2) ? "branched" : open.Length == 0 ? "loop" : "open";
            var junctions = vertices.Where(n => n.ends.Length != 2 || !n.coincident).ToArray();
            var sorted = group.OrderBy(v => v, StringComparer.Ordinal).ToArray();
            List<Step>? order = null;
            if (status is "loop" or "open")
            {
                order = new List<Step>();
                var taken = new HashSet<string>(StringComparer.Ordinal);
                var entry = status == "loop" ? endpointNode[(sorted[0], "start")].ends.First(e => e.handle == sorted[0] && e.end == "start")
                    : open.Select(n => n.ends[0]).OrderBy(e => e.handle, StringComparer.Ordinal).ThenBy(e => e.end, StringComparer.Ordinal).First();
                while (entry != null && taken.Add(entry.handle))
                {
                    check();
                    bool reverse = entry.end == "end";
                    order.Add(new(entry.handle, reverse));
                    entry = endpointNode[(entry.handle, reverse ? "start" : "end")].ends.FirstOrDefault(e => !taken.Contains(e.handle));
                }
            }
            components.Add(new(status, sorted, order?.ToArray(), junctions));
        }
        var loose = nodes.Where(n => n.ends.Length == 1).Select(n => n.ends[0]).ToArray();
        var gaps = new List<Gap>();
        foreach (var from in loose)
        {
            check();
            if (loose.Length < 2) break;
            var distances = loose.Where(to => !ReferenceEquals(to, from)).Select(to =>
            {
                check(); return new Gap(from, to, Distance(from.point, to.point));
            }).ToArray();
            double minimum = distances.Min(g => g.distance);
            gaps.AddRange(distances.Where(g => g.distance == minimum));
        }
        return new(components.ToArray(), gaps.ToArray());
    }
}
