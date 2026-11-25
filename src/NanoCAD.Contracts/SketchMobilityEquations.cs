namespace NanoCAD.Contracts;

// Each expression keeps its differential for local rank and a separate affine
// identity in named geometric quantities. A dependent differential is never
// sufficient evidence that a nonlinear relation is implied.
internal sealed class MobilityValue
{
    public double Value { get; }
    public Dictionary<int, double> Gradient { get; }
    public Dictionary<int, double>? Identity { get; }
    public bool Affine { get; }

    public MobilityValue(double value, Dictionary<int, double> gradient, Dictionary<int, double>? identity, bool affine)
        => (Value, Gradient, Identity, Affine) = (value, gradient, identity, affine);
    public static implicit operator MobilityValue(double v) => new(v, new(), new(), true);
    public static Dictionary<int, double> Sum(Dictionary<int, double> a, Dictionary<int, double> b, double factor)
    {
        var r = new Dictionary<int, double>(a);
        foreach (var (i, value) in b)
        {
            double v = r.GetValueOrDefault(i) + value * factor;
            if (v == 0) r.Remove(i); else r[i] = v;
        }
        return r;
    }
    private static Dictionary<int, double> Scale(Dictionary<int, double> a, double factor)
        => a.Where(pair => pair.Value * factor != 0).ToDictionary(pair => pair.Key, pair => pair.Value * factor);
    public static MobilityValue operator +(MobilityValue a, MobilityValue b) => new(a.Value + b.Value,
        Sum(a.Gradient, b.Gradient, 1), a.Identity == null || b.Identity == null ? null : Sum(a.Identity, b.Identity, 1), a.Affine && b.Affine);
    public static MobilityValue operator -(MobilityValue a, MobilityValue b) => a + b * -1;
    public static MobilityValue operator *(MobilityValue a, double b) =>
        new(a.Value * b, Scale(a.Gradient, b), a.Identity == null ? null : Scale(a.Identity, b), a.Affine);
    public static MobilityValue Times(MobilityValue a, MobilityValue b)
    {
        if (a.Affine && a.Identity is { Count: 0 }) return b * a.Value;
        if (b.Affine && b.Identity is { Count: 0 }) return a * b.Value;
        return new(a.Value * b.Value, Sum(Scale(a.Gradient, b.Value), b.Gradient, a.Value), null, false);
    }
    public MobilityValue AsFeature(int id) => new(Value, Gradient, new() { [id] = 1 }, Affine);
    public MobilityValue Chain(double value, double slope) => new(value, Scale(Gradient, slope), null, false);
}

internal sealed class MobilityGeometry
{
    internal sealed record P(MobilityValue X, MobilityValue Y)
    {
        public static P operator -(P a, P b) => new(a.X - b.X, a.Y - b.Y);
        public MobilityValue Dot(P other) => MobilityValue.Times(X, other.X) + MobilityValue.Times(Y, other.Y);
        public MobilityValue Cross(P other) => MobilityValue.Times(X, other.Y) - MobilityValue.Times(Y, other.X);
        public MobilityValue Norm()
        {
            var d = Dot(this);
            double length = Math.Sqrt(d.Value);
            if (!(length > 0)) throw new Unsupported();
            return d.Chain(length, .5 / length);
        }
    }
    internal sealed class Unsupported : Exception;
    internal sealed class Member
    {
        public string Kind = "";
        public SketchConstraintCurve? Source;
        public P? Start, End, Center;
        public MobilityValue? Radius, Length, Angle, StartAngle, EndAngle;
        public P At(string? where) => (where switch { "start" => Start, "end" => End, "center" => Center, _ => null }) ?? throw new Unsupported();
        public P Direction => Kind == "line" ? End! - Start! : throw new Unsupported();
    }

    private readonly Dictionary<string, Member> members = new(StringComparer.Ordinal);
    private readonly Dictionary<(string, string), int> features = new();
    internal bool Complete { get; private set; } = true;
    internal int Variables { get; private set; }
    private int Feature(string id, string key)
    {
        if (!features.TryGetValue((id, key), out int column)) features[(id, key)] = column = features.Count;
        return column;
    }

    internal MobilityGeometry(string[] ids, IReadOnlyDictionary<string, SketchConstraintCurve> snapshot, HashSet<string> fixedIds, Action? cancel)
    {
        foreach (string id in ids)
        {
            cancel?.Invoke();
            if (!snapshot.TryGetValue(id, out var c)) { Complete = false; continue; }
            bool fixedMember = fixedIds.Contains(id);
            MobilityValue Variable(string key, double value)
            {
                if (!double.IsFinite(value)) throw new Unsupported();
                if (fixedMember) return value;
                int col = Variables++;
                return new(value, new() { [col] = 1 }, new() { [Feature(id, key)] = 1 }, true);
            }
            P Coordinates(string key, Point2? p) => p == null ? throw new Unsupported() :
                new(Variable(key + ".x", p.X), Variable(key + ".y", p.Y));
            var m = new Member { Kind = c.Kind, Source = c };
            try
            {
                switch (c.Kind)
                {
                    case "point":
                        m.Start = m.Center = Coordinates("center", c.Center ?? c.Start);
                        break;
                    case "line":
                        m.Start = Coordinates("start", c.Start); m.End = Coordinates("end", c.End);
                        var d = m.Direction;
                        m.Length = d.Norm().AsFeature(Feature(id, "length"));
                        var angle = Math.Atan2(d.Y.Value, d.X.Value);
                        var den = m.Length.Value * m.Length.Value;
                        m.Angle = new MobilityValue(angle,
                            MobilityValue.Sum((d.Y * (d.X.Value / den)).Gradient, d.X.Gradient, -d.Y.Value / den),
                            new() { [Feature(id, "angle")] = 1 }, false);
                        if (fixedMember) { m.Length = m.Length.Value; m.Angle = angle; }
                        break;
                    case "circle":
                    case "arc":
                        m.Center = Coordinates("center", c.Center);
                        if (!(c.Radius > 0)) throw new Unsupported();
                        m.Radius = Variable("radius", c.Radius);
                        if (c.Kind == "arc")
                        {
                            if (c.Start == null || c.End == null || !(Math.Abs(c.Sweep) > 0 && Math.Abs(c.Sweep) < 2 * Math.PI))
                                throw new Unsupported();
                            (P, MobilityValue) Endpoint(string name, Point2 pt)
                            {
                                double x = pt.X - m.Center.X.Value, y = pt.Y - m.Center.Y.Value;
                                if (!DrawingProfileGeometry.Near(Math.Sqrt(x * x + y * y), c.Radius)) throw new Unsupported();
                                var a = Variable(name + ".angle", Math.Atan2(y, x));
                                var cos = a.Chain(Math.Cos(a.Value), -Math.Sin(a.Value));
                                var sin = a.Chain(Math.Sin(a.Value), Math.Cos(a.Value));
                                var px = m.Center.X + MobilityValue.Times(m.Radius, cos);
                                var py = m.Center.Y + MobilityValue.Times(m.Radius, sin);
                                return (fixedMember ? new P(pt.X, pt.Y) :
                                    new P(px.AsFeature(Feature(id, name + ".x")), py.AsFeature(Feature(id, name + ".y"))), a);
                            }
                            (m.Start, m.StartAngle) = Endpoint("start", c.Start);
                            (m.End, m.EndAngle) = Endpoint("end", c.End);
                            var expected = new Point2(c.Center!.X + c.Radius * Math.Cos(m.StartAngle.Value + c.Sweep),
                                c.Center.Y + c.Radius * Math.Sin(m.StartAngle.Value + c.Sweep));
                            if (!DrawingProfileGeometry.Same(expected, c.End)) throw new Unsupported();
                        }
                        break;
                    default: throw new Unsupported();
                }
                members.Add(id, m);
            }
            catch (Unsupported) { Complete = false; }
        }
    }

    // A vector tangent to a circle at the current point still changes a
    // fixed center-to-center distance on every finite straight translation.
    internal string Translation(SketchConstraintSpec s, HashSet<string> ids, SketchMobilityMotion motion)
    {
        bool first = ids.Contains(s.First), second = s.Second != null && ids.Contains(s.Second);
        if (!first && !second && (s.Third == null || !ids.Contains(s.Third))) return "permitted";
        if (s.Kind is "fix_point" or "fix") return first ? "blocked" : "permitted";
        if (s.Kind is "horizontal" or "vertical" or "radius" or "diameter" or "length" or "equal" or "parallel" or "perpendicular" or "angle")
            return "permitted";
        if (s.Kind == "symmetric")
        {
            if (s.Third == null || !members.TryGetValue(s.Third, out var axis) || axis.Kind != "line") return "unknown";
            int axisMoves = ids.Contains(s.Third) ? 1 : 0;
            int relA = (first ? 1 : 0) - axisMoves, relB = (second ? 1 : 0) - axisMoves;
            var d = axis.Direction;
            double ux = d.X.Value / axis.Length!.Value, uy = d.Y.Value / axis.Length.Value;
            double along = (ux * motion.dx + uy * motion.dy) * relA;
            double ex = 2 * along * ux - (relA + relB) * motion.dx;
            double ey = 2 * along * uy - (relA + relB) * motion.dy;
            if (ex == 0 && ey == 0) return "permitted";
            return Math.Max(Math.Abs(ex), Math.Abs(ey)) > 1e-10 * Math.Max(Math.Abs(motion.dx), Math.Abs(motion.dy)) ? "blocked" : "unknown";
        }
        if (first == second && s.Third == null) return "permitted";
        if (s.Kind is "coincident" or "concentric" or "distance") return "blocked";
        if (s.Kind == "distance_x") return motion.dx == 0 ? "permitted" : "blocked";
        if (s.Kind == "distance_y") return motion.dy == 0 ? "permitted" : "blocked";
        if (s.Kind is "tangent" or "collinear")
        {
            if (!members.TryGetValue(s.First, out var a) || s.Second == null || !members.TryGetValue(s.Second, out var b))
                return "unknown";
            var line = a.Kind == "line" ? a : b.Kind == "line" ? b : null;
            if (line == null) return "blocked";
            double cross = line.Direction.X.Value * motion.dy - line.Direction.Y.Value * motion.dx;
            if (cross == 0) return "permitted";
            return Math.Abs(cross) > 1e-10 * line.Length!.Value * Math.Max(Math.Abs(motion.dx), Math.Abs(motion.dy))
                ? "blocked" : "unknown";
        }
        return "unknown";
    }

    internal bool Admissible(IReadOnlyDictionary<int, double> delta)
    {
        double At(MobilityValue v) => v.Value + v.Gradient.Sum(p => p.Value * delta.GetValueOrDefault(p.Key));
        foreach (var m in members.Values)
        {
            if (m.Radius != null && !(At(m.Radius) > 0)) return false;
            if (m.Kind == "line" && At(m.Start!.X) == At(m.End!.X) && At(m.Start.Y) == At(m.End.Y)) return false;
            if (m.Kind == "arc" && Math.Abs(At(m.StartAngle!) - At(m.EndAngle!)) != Math.Abs(m.StartAngle!.Value - m.EndAngle!.Value))
                return false; // An affine off-pose proof must not cross a trim seam.
        }
        return true;
    }

    internal sealed record Equations(MobilityValue[] Rows, MobilityValue[] AdditionalFacts);
    internal Equations Build(SketchConstraintSpec s, Func<string, string, string, string, bool> joined)
    {
        if (s.FirstSegmentIndex != null || s.SecondSegmentIndex != null || s.ThirdSegmentIndex != null) throw new Unsupported();
        Member Get(string? id) => id != null && members.TryGetValue(id, out var m) ? m : throw new Unsupported();
        var a = Get(s.First);
        var b = s.Second == null ? null : Get(s.Second);
        double value = s.Value ?? 0;
        if (!double.IsFinite(value)) throw new Unsupported();
        var rows = new List<MobilityValue>();
        var facts = new List<MobilityValue>();
        void Point(P p) { rows.Add(p.X); rows.Add(p.Y); }
        void Fixed(P p) => Point(new(p.X - p.X.Value, p.Y - p.Y.Value));
        void FixedScalar(MobilityValue f) => rows.Add(f - f.Value);
        double Nearest(double actual, double target, double period) => target + period * Math.Round((actual - target) / period);
        MobilityValue Angle(Member m) => m.Angle ?? throw new Unsupported();
        MobilityValue Radius(Member m) => m.Radius ?? throw new Unsupported();
        P Center(Member m) => m.Center ?? throw new Unsupported();
        void Orientation(MobilityValue angle, double target, double period) => rows.Add(angle - Nearest(angle.Value, target, period));
        void CheckArcInterior(Member m, Point2 contact)
        {
            if (m.Kind != "arc") return;
            var curve = m.Source!;
            // Contact on a trim boundary can be lost by an arbitrarily small
            // motion unless an actual endpoint joint was observed.
            if (!DrawingArcContours.OnArc(curve, contact) || DrawingProfileGeometry.Same(curve.Start!, contact) ||
                DrawingProfileGeometry.Same(curve.End!, contact)) throw new Unsupported();
        }
        switch (s.Kind)
        {
            case "horizontal":
            case "vertical":
                bool horizontal = s.Kind == "horizontal";
                rows.Add(horizontal ? a.Direction.Y : a.Direction.X);
                var ang = Angle(a);
                facts.Add(ang - Nearest(ang.Value, horizontal ? 0 : Math.PI / 2, Math.PI));
                var axialSpan = horizontal ? a.Direction.X : a.Direction.Y;
                if (axialSpan.Value != 0) facts.Add(axialSpan - a.Length! * Math.Sign(axialSpan.Value));
                break;
            case "parallel":
            case "perpendicular":
                Orientation(Angle(b!) - Angle(a), s.Kind == "parallel" ? 0 : Math.PI / 2, Math.PI);
                break;
            case "angle":
                var diff = Angle(b!) - Angle(a);
                double signed = Math.IEEERemainder(diff.Value, 2 * Math.PI);
                if (s.Value == null || !(value > 0 && value < 180) || Math.Abs(Math.Sin(signed)) < 1e-12) throw new Unsupported();
                Orientation(diff, Math.Sign(signed) * value * Math.PI / 180, 2 * Math.PI);
                break;
            case "fix_point": Fixed(a.At(s.FirstPoint)); break;
            case "fix":
                if (a.Kind == "line") { Fixed(a.Start!); Fixed(a.End!); }
                else
                {
                    Fixed(Center(a));
                    if (a.Radius != null) FixedScalar(a.Radius);
                    if (a.StartAngle != null) { FixedScalar(a.StartAngle); FixedScalar(a.EndAngle!); }
                }
                break;
            case "coincident": Point(a.At(s.FirstPoint) - b!.At(s.SecondPoint)); break;
            case "concentric": Point(Center(a) - Center(b!)); break;
            case "symmetric":
                var axisLine = Get(s.Third);
                var axisVector = axisLine.Direction;
                var span = axisLine.Length!;
                MobilityValue inverseSquare = span.Gradient.Count == 0 ? 1 / (span.Value * span.Value)
                    : span.Chain(1 / (span.Value * span.Value), -2 / Math.Pow(span.Value, 3));
                P Mirror(P point)
                {
                    var local = point - axisLine.Start!;
                    var projection = MobilityValue.Times(local.Dot(axisVector), inverseSquare) * 2;
                    return new(axisLine.Start!.X + MobilityValue.Times(axisVector.X, projection) - local.X,
                        axisLine.Start.Y + MobilityValue.Times(axisVector.Y, projection) - local.Y);
                }
                if (a.Kind == "circle" && b?.Kind == "circle")
                {
                    Point(Mirror(Center(a)) - Center(b));
                    rows.Add(Radius(a) - Radius(b));
                }
                else if (a.Kind == "line" && b?.Kind == "line")
                {
                    var start = Mirror(a.Start!); var end = Mirror(a.End!);
                    static bool Near(P p, P q) => DrawingProfileGeometry.Near(p.X.Value, q.X.Value) &&
                        DrawingProfileGeometry.Near(p.Y.Value, q.Y.Value);
                    bool same = Near(start, b.Start!) && Near(end, b.End!);
                    bool reverse = Near(start, b.End!) && Near(end, b.Start!);
                    if (same == reverse) throw new Unsupported();
                    Point(start - (same ? b.Start! : b.End!)); Point(end - (same ? b.End! : b.Start!));
                }
                else throw new Unsupported();
                break;
            case "radius":
            case "diameter":
                if (s.Value == null || !(value > 0)) throw new Unsupported();
                rows.Add(Radius(a) - value / (s.Kind == "diameter" ? 2 : 1)); break;
            case "length":
                if (s.Value == null || !(value > 0)) throw new Unsupported();
                rows.Add((a.Length ?? throw new Unsupported()) - value); break;
            case "equal":
                rows.Add(a.Length != null && b!.Length != null ? a.Length - b.Length : Radius(a) - Radius(b!)); break;
            case "distance_x":
            case "distance_y":
            case "distance":
                if (s.Value == null || !(value > 0)) throw new Unsupported();
                P between = a.At(s.FirstPoint) - b!.At(s.SecondPoint);
                if (s.Kind == "distance" && s.First == s.Second && a.Kind == "line" && s.FirstPoint != s.SecondPoint)
                    rows.Add(a.Length! - value);
                else if (s.Kind == "distance") rows.Add(between.Norm() - value);
                else
                {
                    var axis = s.Kind == "distance_x" ? between.X : between.Y;
                    if (axis.Value == 0) throw new Unsupported(); // Absolute separation has no signed branch here.
                    rows.Add(axis - Math.Sign(axis.Value) * value);
                }
                break;
            case "collinear":
                Orientation(Angle(b!) - Angle(a), 0, Math.PI);
                rows.Add(a.Direction.Cross(b!.Start! - a.Start!) * (1 / a.Length!.Value));
                break;
            case "tangent":
                if (a.Kind == "line" || b!.Kind == "line")
                {
                    var line = a.Kind == "line" ? a : b!;
                    var circle = a.Kind == "line" ? b! : a;
                    string lineId = a.Kind == "line" ? s.First : s.Second!;
                    string roundId = a.Kind == "line" ? s.Second! : s.First;
                    var r = Radius(circle);
                    foreach (string cp in new[] { "start", "end" })
                        foreach (string lp in new[] { "start", "end" })
                            if (circle.Kind == "arc" && joined(roundId, cp, lineId, lp))
                            {
                                var normal = cp == "start" ? circle.StartAngle! : circle.EndAngle!;
                                Orientation(Angle(line) - normal, Math.PI / 2, Math.PI);
                                return new(rows.ToArray(), facts.ToArray());
                            }
                    var cross = line.Direction.Cross(Center(circle) - line.Start!);
                    if (cross.Value == 0) throw new Unsupported();
                    if (circle.Kind == "arc")
                    {
                        double length = line.Length!.Value;
                        double t = (Center(circle) - line.Start!).Dot(line.Direction).Value / (length * length);
                        CheckArcInterior(circle, new(line.Start!.X.Value + t * line.Direction.X.Value,
                            line.Start.Y.Value + t * line.Direction.Y.Value));
                    }
                    rows.Add((cross - MobilityValue.Times(line.Length!, r) * Math.Sign(cross.Value)) * (1 / line.Length!.Value));
                }
                else
                {
                    var d = (Center(a) - Center(b!)).Norm();
                    var outer = Radius(a) + Radius(b!);
                    var inner = Radius(a) - Radius(b!);
                    bool external = Math.Abs(d.Value - outer.Value) < Math.Abs(d.Value - Math.Abs(inner.Value));
                    double direction = external || inner.Value >= 0 ? 1 : -1;
                    var betweenCenters = Center(b!) - Center(a);
                    double scale = direction * Radius(a).Value / d.Value;
                    var contact = new Point2(Center(a).X.Value + scale * betweenCenters.X.Value,
                        Center(a).Y.Value + scale * betweenCenters.Y.Value);
                    CheckArcInterior(a, contact); CheckArcInterior(b!, contact);
                    rows.Add(d - (external ? outer : inner * Math.Sign(inner.Value)));
                }
                break;
            default: throw new Unsupported();
        }
        if (rows.Concat(facts).Any(r => !double.IsFinite(r.Value) || r.Gradient.Values.Any(d => !double.IsFinite(d))))
            throw new Unsupported();
        return new(rows.ToArray(), facts.ToArray());
    }
}
