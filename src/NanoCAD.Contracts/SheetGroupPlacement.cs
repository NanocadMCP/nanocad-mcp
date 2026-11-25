namespace NanoCAD.Contracts;

public sealed record SheetGroupMember(string Handle, int Parent, string Side, Point2 Origin, SheetBox Envelope);

public static class SheetGroupPlacement
{
    public static List<SheetBox> FrameObstacles(SheetBox frame, List<SheetBox> primitives, Action check)
    {
        // A separately inserted stamp remains a full obstacle in the caller. In a combined frame
        // and stamp, match a complete rectangular perimeter first, then reserve connected ink
        // as a whole so an empty cell inside a ruled title block cannot receive a projection.
        var edges = Enumerable.Range(0, 4).Select(_ => new List<(double Low, double High)>()).ToArray();
        var border = new List<SheetBox>();
        var interior = new List<SheetBox>();
        foreach (var b in primitives)
        {
            check();
            int side = b.Left == frame.Left && b.Right == frame.Left ? 0 :
                b.Left == frame.Right && b.Right == frame.Right ? 1 :
                b.Top == frame.Bottom && b.Bottom == frame.Bottom ? 2 :
                b.Top == frame.Top && b.Bottom == frame.Top ? 3 : -1;
            if (side < 0) { interior.Add(b); continue; }
            edges[side].Add(side < 2 ? (b.Bottom, b.Top) : (b.Left, b.Right));
            border.Add(b);
        }
        for (int i = 0; i < 4; i++)
        {
            double end = i < 2 ? frame.Bottom : frame.Left;
            foreach (var span in edges[i].OrderBy(s => s.Low))
            {
                check();
                if (span.Low > Math.BitIncrement(end)) return [frame];
                end = Math.Max(end, span.High);
            }
            if (end < (i < 2 ? frame.Top : frame.Right)) return [frame];
        }
        var zones = new List<SheetBox>();
        foreach (var part in interior)
        {
            var zone = part;
            int index = zones.Count;
            while (--index >= 0)
            {
                check();
                var b = zones[index];
                if (b.Right < zone.Left || b.Left > zone.Right || b.Top < zone.Bottom || b.Bottom > zone.Top) continue;
                zone = zone.Union(b);
                zones.RemoveAt(index);
                index = zones.Count;
            }
            zones.Add(zone);
        }
        border.AddRange(zones);
        return border;
    }

    // Members are in native parent-before-child order. Each envelope includes its view and measured annotations.
    public static Point2[]? Fit(IReadOnlyList<SheetGroupMember> members, IReadOnlyList<SheetBox> obstacles,
        SheetBox area, Point2 preferred, double gap, Action check, bool compactSpacing = false)
    {
        if (members.Count == 0 || !area.HasArea) return null;
        var local = members.Select(m => m.Envelope.Shift(-m.Origin.X, -m.Origin.Y)).ToArray();
        var root = members[0].Origin;
        var existing = members.Select(m => new Point2(m.Origin.X - root.X, m.Origin.Y - root.Y)).ToArray();
        var existingFit = At(existing);
        if (!compactSpacing && existingFit != null && existingFit[0] == preferred) return existingFit;
        // Compact sibling groups without swapping sides. Fall back to the original spacing when
        // fixed obstacles make the compact arrangement unsuitable, for example an inset title block.
        var compact = new Point2[members.Count];
        compact[0] = new(0, 0);
        var placed = new List<SheetBox> { local[0] };
        for (int i = 1; i < members.Count; i++)
        {
            check();
            var m = members[i];
            var parent = compact[m.Parent];
            var candidate = local[i].Shift(parent.X, parent.Y);
            bool horizontal = m.Side is "left" or "right", positive = m.Side is "above" or "right";
            var parentBox = placed[m.Parent];
            double Displacement(SheetBox b) => m.Side switch
            {
                "right" => b.Right + gap - candidate.Left,
                "left" => b.Left - gap - candidate.Right,
                "above" => b.Top + gap - candidate.Bottom,
                "below" => b.Bottom - gap - candidate.Top,
                _ => throw new ArgumentException("Unknown paper alignment side.")
            };
            double d = Displacement(parentBox);
            // When a projected origin is outside its footprint, a box edge alone does not preserve its side.
            if (positive ? d <= 0 : d >= 0)
                d = horizontal ? existing[i].X - existing[m.Parent].X : existing[i].Y - existing[m.Parent].Y;
            var stops = placed.Select(Displacement).Append(d).Distinct()
                .Where(v => positive ? v >= d && v > 0 : v <= d && v < 0).OrderBy(Math.Abs);
            bool found = false;
            foreach (double stop in stops)
            {
                check();
                var at = candidate.Shift(horizontal ? stop : 0, horizontal ? 0 : stop);
                if (placed.Any(b => at.Conflicts(b, gap))) continue;
                compact[i] = new(parent.X + (horizontal ? stop : 0), parent.Y + (horizontal ? 0 : stop));
                placed.Add(at);
                found = true;
                break;
            }
            if (!found) return existingFit;
        }
        var compactFit = At(compact);
        if (compactSpacing || existingFit == null) return compactFit ?? existingFit;
        if (compactFit == null) return existingFit;
        double Distance(Point2 p) => Math.Pow(p.X - preferred.X, 2) + Math.Pow(p.Y - preferred.Y, 2);
        return Distance(compactFit[0]) < Distance(existingFit[0]) ? compactFit : existingFit;

        Point2[]? At(Point2[] offsets)
        {
            var boxes = offsets.Select((p, i) => local[i].Shift(p.X, p.Y)).ToArray();
            var outer = boxes[0];
            var forbidden = new List<SheetBox>();
            for (int i = 0; i < boxes.Length; i++)
            {
                check();
                if (i > 0)
                {
                    var m = members[i];
                    double x = offsets[i].X - offsets[m.Parent].X;
                    double y = offsets[i].Y - offsets[m.Parent].Y;
                    bool aligned = m.Side switch
                    {
                        "right" => x > 0 && Math.Abs(y) <= 1e-8,
                        "left" => x < 0 && Math.Abs(y) <= 1e-8,
                        "above" => y > 0 && Math.Abs(x) <= 1e-8,
                        "below" => y < 0 && Math.Abs(x) <= 1e-8,
                        _ => false
                    };
                    if (!aligned) return null;
                }
                var b = boxes[i];
                for (int j = 0; j < i; j++)
                    if (b.Conflicts(boxes[j], gap)) return null;
                outer = outer.Union(b);
                foreach (var obstacle in obstacles)
                {
                    check();
                    forbidden.Add(new(obstacle.Left - b.Right, obstacle.Bottom - b.Top,
                        obstacle.Right - b.Left, obstacle.Top - b.Bottom));
                }
            }
            var allowed = new SheetBox(area.Left - outer.Left, area.Bottom - outer.Bottom,
                area.Right - outer.Right, area.Top - outer.Top);
            var rootPosition = SheetPlacementSolver.Nearest(new(0, 0, 0, 0), forbidden, allowed, preferred, gap, check);
            if (rootPosition == null) return null;
            var positions = offsets.Select(p => new Point2(p.X + rootPosition.X, p.Y + rootPosition.Y)).ToArray();
            // Recheck actual paper coordinates after conversion between origin and envelope frames.
            for (int i = 0; i < positions.Length; i++)
            {
                check();
                var at = members[i].Envelope.Shift(positions[i].X - members[i].Origin.X, positions[i].Y - members[i].Origin.Y);
                if (!area.Contains(at) || obstacles.Any(b => at.Conflicts(b, gap))) return null;
            }
            return positions;
        }
    }
}
