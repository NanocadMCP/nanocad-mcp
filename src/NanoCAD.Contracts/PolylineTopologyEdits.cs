using System.Text.Json;

namespace NanoCAD.Contracts;

public sealed record PolylineShapeVertex(Point2 Position, double Bulge, double StartWidth, double EndWidth);
public sealed record PolylineShape(bool Closed, PolylineShapeVertex[] Vertices);
public sealed record PolylineGeometrySpec(string[] Targets, PolylineShape ExpectedGeometry, PolylineShape Geometry,
    [property:System.Text.Json.Serialization.JsonIgnore(Condition=System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)] SketchPlaneSpec? Workplane=null)
    : EditSpec("set_polyline_geometry", Targets);
public sealed record PolylineReverseSpec(string[] Targets, PolylineShape ExpectedGeometry,
    [property:System.Text.Json.Serialization.JsonIgnore(Condition=System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)] SketchPlaneSpec? Workplane=null)
    : EditSpec("reverse_polyline", Targets);
public sealed record PolylineCorner(int Index, string Kind, double Radius, double IncomingDistance, double OutgoingDistance);
public sealed record PolylineCornersSpec(string[] Targets, PolylineShape ExpectedGeometry, PolylineCorner[] Corners,
    [property:System.Text.Json.Serialization.JsonIgnore(Condition=System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)] SketchPlaneSpec? Workplane=null)
    : EditSpec("set_polyline_corners", Targets);

public static partial class ModifyValidation
{
    private static EditSpec ParsePolylineTopology(JsonElement item, string path, string[] handles, string kind)
    {
        if (handles.Length != 1) throw Invalid(path + ".handles", "Choose one native Polyline.");
        Shape(item, path, kind == "set_polyline_geometry" ? ["operation", "handles", "expected_geometry", "geometry", "workplane"] :
            kind == "set_polyline_corners" ? ["operation", "handles", "expected_geometry", "corners", "workplane"] : ["operation", "handles", "expected_geometry", "workplane"]);
        var frame=item.TryGetProperty("workplane",out var workplane)?DrawingWorkplane.Parse(workplane):null;
        PolylineShape Read(string name)
        {
            string at = path + "." + name; var shape = Required(item, name, path);
            Shape(shape, at, "closed", "vertices");
            var closed = Required(shape, "closed", at);
            if (closed.ValueKind is not JsonValueKind.True and not JsonValueKind.False) throw Invalid(at + ".closed", "Expected Boolean.");
            var vertices = Required(shape, "vertices", at);
            if (vertices.ValueKind != JsonValueKind.Array || vertices.GetArrayLength() < 2)
                throw Invalid(at + ".vertices", "Use at least two explicit vertices; do not repeat the first vertex to close a contour.");
            var result = new List<PolylineShapeVertex>();
            foreach (var vertex in vertices.EnumerateArray())
            {
                string vp = at + ".vertices[" + result.Count + "]";
                Shape(vertex, vp, "position", "bulge", "start_width", "end_width");
                result.Add(new(Point(vertex, "position", vp), Number(Required(vertex, "bulge", vp), vp + ".bulge", -1e6, 1e6),
                    Number(Required(vertex, "start_width", vp), vp + ".start_width", 0, 1e6),
                    Number(Required(vertex, "end_width", vp), vp + ".end_width", 0, 1e6)));
            }
            var geometry = new PolylineShape(closed.GetBoolean(), result.ToArray());
            PolylineTopologyGeometry.Validate(geometry); return geometry;
        }
        var expected = Read("expected_geometry");
        if (kind == "set_polyline_geometry") return new PolylineGeometrySpec(handles, expected, Read("geometry"),frame);
        if (kind == "reverse_polyline") return new PolylineReverseSpec(handles, expected,frame);
        var corners = Required(item, "corners", path);
        if (corners.ValueKind != JsonValueKind.Array || corners.GetArrayLength() < 1) throw Invalid(path + ".corners", "Choose distinct original vertex indices.");
        var parsed = new List<PolylineCorner>(); var seen = new HashSet<int>();
        foreach (var corner in corners.EnumerateArray())
        {
            string at = path + ".corners[" + parsed.Count + "]", treatment = Text(corner, "kind", at, 16);
            if (treatment is not "fillet" and not "chamfer") throw Invalid(at + ".kind", "Use fillet or chamfer.");
            Shape(corner, at, treatment == "fillet" ? ["index", "kind", "radius"] : ["index", "kind", "incoming_distance", "outgoing_distance"]);
            var index = Required(corner, "index", at);
            if (index.ValueKind != JsonValueKind.Number || !index.TryGetInt32(out int n) || n < 0 || n >= expected.Vertices.Length || !seen.Add(n)) throw Invalid(at + ".index", "Use distinct indices in expected_geometry.");
            double Size(string name) => Number(Required(corner, name, at), at + "." + name, .001, 1e6);
            parsed.Add(treatment == "fillet" ? new(n, treatment, Size("radius"), 0, 0) : new(n, treatment, 0, Size("incoming_distance"), Size("outgoing_distance")));
        }
        var edits = parsed.OrderBy(c => c.Index).ToArray();
        PolylineTopologyGeometry.Corners(expected, edits); // All geometric refusals before native write dispatch.
        return new PolylineCornersSpec(handles, expected, edits,frame);
    }
}

// Exact stored-segment geometry; no tessellation or automatic topology repair.
public static class PolylineTopologyGeometry
{
    private static DomainException Bad(string message, string code = "INVALID_GEOMETRY") => new(code, message);
    private static Point2 Sub(Point2 a, Point2 b) => new(a.X - b.X, a.Y - b.Y);
    private static Point2 Along(Point2 p, Point2 d, double t) => new(p.X + d.X * t, p.Y + d.Y * t);
    private static double Length(Point2 p) => Math.Sqrt(p.X * p.X + p.Y * p.Y);
    public static void Validate(PolylineShape shape,Action? cancel=null)
    {
        cancel?.Invoke();
        if (shape.Vertices.Length < 2) throw Bad("Polyline geometry requires at least two vertices.");
        var budget=new ContourGeometryBudget344(cancel);
        foreach (var v in shape.Vertices)
        {
            cancel?.Invoke();
            if (!double.IsFinite(v.Position.X) || !double.IsFinite(v.Position.Y) || Math.Abs(v.Position.X) > 1e6 || Math.Abs(v.Position.Y) > 1e6 ||
                !double.IsFinite(v.Bulge) || Math.Abs(v.Bulge) > 1e6 || !double.IsFinite(v.StartWidth) || !double.IsFinite(v.EndWidth) ||
                v.StartWidth < 0 || v.EndWidth < 0 || v.StartWidth > 1e6 || v.EndWidth > 1e6) throw Bad("Polyline coordinates, widths and bulges must be finite and within declared bounds.");
            budget.Add(v);
        }
        for (int i = 0; i < (shape.Closed ? shape.Vertices.Length : shape.Vertices.Length - 1); i++)
        {
            cancel?.Invoke();
            var v = shape.Vertices[i]; double chord = Length(Sub(shape.Vertices[(i + 1) % shape.Vertices.Length].Position, v.Position));
            if (chord < 1e-7) throw Bad("Polyline contains a collapsed segment; closure uses closed=true, not a repeated endpoint.", "DEGENERATE_GEOMETRY");
            if (v.Bulge != 0 && chord * (1 + v.Bulge * v.Bulge) / (4 * Math.Abs(v.Bulge)) > 1e9)
                throw Bad("Polyline arc radius exceeds the supported geometry range.");
        }
        ContourGeometryBudget344.Check(shape,cancel);
    }
    public static bool Equivalent(PolylineShape a, PolylineShape b,Action? cancel=null)
    {
        cancel?.Invoke();if(a.Closed!=b.Closed||a.Vertices.Length!=b.Vertices.Length)return false;
        for(int i=0;i<a.Vertices.Length;i++)
        {
            cancel?.Invoke();var left=a.Vertices[i];var right=b.Vertices[i];
            if(!(Length(Sub(left.Position,right.Position))<=1e-7)||!(Math.Abs(left.Bulge-right.Bulge)<=1e-12)||
               !(Math.Abs(left.StartWidth-right.StartWidth)<=1e-9)||!(Math.Abs(left.EndWidth-right.EndWidth)<=1e-9))return false;
        }
        return true;
    }
    // Use for result geometry and independent contour preconditions. Indexed
    // vertex edits still use Equivalent: their vertex numbering is meaningful.
    public static bool SameLocus(PolylineShape a, PolylineShape b,Action? cancel=null)
    {
        cancel?.Invoke();
        if (a.Closed != b.Closed || a.Vertices.Length != b.Vertices.Length) return false;
        int count=a.Vertices.Length;
        double minX=double.PositiveInfinity,maxX=double.NegativeInfinity,minY=double.PositiveInfinity,maxY=double.NegativeInfinity;
        foreach(var v in a.Vertices){cancel?.Invoke();minX=Math.Min(minX,v.Position.X);maxX=Math.Max(maxX,v.Position.X);minY=Math.Min(minY,v.Position.Y);maxY=Math.Max(maxY,v.Position.Y);}
        double span=Math.Max(1,Math.Max(maxX-minX,maxY-minY));
        double pointTolerance=Math.Max(1e-7,span*1e-9);
        bool Match(PolylineShape x,int shift)
        {
            for(int i=0;i<count;i++)
            {
                cancel?.Invoke();
                var left=x.Vertices[(i+shift)%count];var right=b.Vertices[i];
                if(Length(Sub(left.Position,right.Position))>pointTolerance)return false;
                if(!a.Closed&&i==count-1)continue; // No outgoing segment.
                if(Math.Abs(left.Bulge-right.Bulge)>1e-9||
                   Math.Abs(left.StartWidth-right.StartWidth)>Math.Max(1e-9,span*1e-9)||
                   Math.Abs(left.EndWidth-right.EndWidth)>Math.Max(1e-9,span*1e-9))return false;
            }
            return true;
        }
        if(a.Closed)
        {
            var reversed=Reverse(a,cancel);
            for(int shift=0;shift<count;shift++){cancel?.Invoke();if(Match(a,shift)||Match(reversed,shift))return true;}
            return false;
        }
        return Match(a,0)||Match(Reverse(a,cancel),0);
    }
    public static PolylineShape Reverse(PolylineShape source,Action? cancel=null)
    {
        Validate(source,cancel); int n = source.Vertices.Length; var vertices = new PolylineShapeVertex[n];var budget=new ContourGeometryBudget344(cancel);
        for (int i = 0; i < n; i++)
        {
            cancel?.Invoke();
            int old = n - 1 - i;
            // Open terminal metadata is stored but does not describe a segment.
            var segment = source.Vertices[source.Closed ? (old + n - 1) % n : i == n - 1 ? n - 1 : old - 1];
            vertices[i] = !source.Closed && i == n - 1 ? segment with { Position = source.Vertices[old].Position } :
                new(source.Vertices[old].Position, -segment.Bulge, segment.EndWidth, segment.StartWidth);
            budget.Add(vertices[i]);
        }
        var reversed=new PolylineShape(source.Closed,vertices);ContourGeometryBudget344.Check(reversed,cancel);return reversed;
    }
    public static PolylineShape Corners(PolylineShape source, PolylineCorner[] corners,Action? cancel=null)
    {
        Validate(source,cancel); int n = source.Vertices.Length;
        if(corners.Length<1)throw Bad("Corner edits require distinct indices.");
        var seen=new HashSet<int>();var inputBudget=new ContourGeometryBudget344(cancel);
        foreach(var corner in corners){cancel?.Invoke();if(!seen.Add(corner.Index))throw Bad("Corner edits require distinct indices.");inputBudget.Add(corner);}
        var incoming = new double[n]; var outgoing = new double[n];
        var replacements = new Dictionary<int, (PolylineShapeVertex Before, PolylineShapeVertex After)>();
        foreach (var corner in corners)
        {
            cancel?.Invoke();
            int i = corner.Index;
            if (i < 0 || i >= n || !source.Closed && (i == 0 || i == n - 1)) throw Bad("An open endpoint has no corner.", "CORNER_NOT_FOUND");
            var prev = source.Vertices[(i + n - 1) % n]; var v = source.Vertices[i]; var next = source.Vertices[(i + 1) % n];
            if (prev.Bulge != 0 || v.Bulge != 0 || prev.StartWidth != 0 || prev.EndWidth != 0 || v.StartWidth != 0 || v.EndWidth != 0)
                throw Bad("Fillet/chamfer needs two adjacent straight zero-width segments; other segments are preserved.", "UNSUPPORTED_CORNER");
            var a = Sub(v.Position, prev.Position); var b = Sub(next.Position, v.Position); double la = Length(a), lb = Length(b);
            a = new(a.X / la, a.Y / la); b = new(b.X / lb, b.Y / lb);
            double angle = Math.Atan2(a.X * b.Y - a.Y * b.X, a.X * b.X + a.Y * b.Y);
            if (Math.Abs(angle) < 1e-7 || Math.PI - Math.Abs(angle) < 1e-7) throw Bad("A straight continuation or reversing segment has no bounded corner.", "UNSUPPORTED_CORNER");
            double din, dout, bulge;
            if (corner.Kind == "fillet") { din = dout = corner.Radius * Math.Tan(Math.Abs(angle) / 2); bulge = Math.Tan(angle / 4); }
            else if (corner.Kind == "chamfer") { din = corner.IncomingDistance; dout = corner.OutgoingDistance; bulge = 0; }
            else throw Bad("Unknown corner treatment.");
            if (!double.IsFinite(din) || !double.IsFinite(dout) || din <= 0 || dout <= 0 || din >= la - 1e-7 || dout >= lb - 1e-7)
                throw Bad("Corner size consumes an adjacent segment.", "CORNER_OVERLAP");
            incoming[i] = din; outgoing[i] = dout;
            replacements.Add(i, (new(Along(v.Position, a, -din), bulge, 0, 0), new(Along(v.Position, b, dout), 0, 0, 0)));
        }
        for (int i = 0; i < (source.Closed ? n : n - 1); i++)
        {
            cancel?.Invoke();
            int next = (i + 1) % n;
            if (outgoing[i] + incoming[next] >= Length(Sub(source.Vertices[next].Position, source.Vertices[i].Position)) - 1e-7)
                throw Bad("Neighboring corner treatments overlap or consume their shared segment.", "CORNER_OVERLAP");
        }
        var result = new List<PolylineShapeVertex>();var outputBudget=new ContourGeometryBudget344(cancel);
        for (int i = 0; i < n; i++)
        {
            cancel?.Invoke();
            if(replacements.TryGetValue(i,out var replacement)){outputBudget.Add(replacement.Before);outputBudget.Add(replacement.After);result.Add(replacement.Before);result.Add(replacement.After);}
            else{outputBudget.Add(source.Vertices[i]);result.Add(source.Vertices[i]);}
        }
        var geometry = new PolylineShape(source.Closed, result.ToArray()); Validate(geometry,cancel); return geometry;
    }
}
