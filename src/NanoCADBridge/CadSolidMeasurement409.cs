using System.Globalization;
using NanoCAD.Contracts;
using Teigha.DatabaseServices;
using Teigha.Geometry;
using Brep = Teigha.BoundaryRepresentation.Brep;
using BrepEntity = Teigha.BoundaryRepresentation.BrepEntity;
using Face = Teigha.BoundaryRepresentation.Face;
using Edge = Teigha.BoundaryRepresentation.Edge;

namespace NanoCADBridge;

// Finite native topology is measured without tessellation, bounding-box gaps,
// database face copies or the host's unavailable whole-body point classifier.
internal static class CadSolidMeasurement409
{
    private sealed record Segment(string Id, Line Curve)
    {
        internal Point3d A => Curve.StartPoint;
        internal Point3d B => Curve.EndPoint;
    }
    private sealed record PlanarFace(string Id, Face Native, Point3d Origin, Vector3d Normal, Vector3d Outward, List<Segment> Edges);
    private sealed class Boundary : IDisposable
    {
        internal readonly Solid3d Body;
        internal readonly Brep Brep;
        internal readonly List<PlanarFace> Faces = new();
        internal readonly Dictionary<string, Segment> Edges = new(StringComparer.Ordinal);
        internal Boundary(Solid3d body) { Body = body; Brep = new(body); }
        public void Dispose()
        {
            System.Exception? failure = null;
            foreach (var face in Faces) try { face.Native.Dispose(); } catch (System.Exception error) { failure ??= error; }
            foreach (var edge in Edges.Values) try { edge.Curve.Dispose(); } catch (System.Exception error) { failure ??= error; }
            try { Brep.Dispose(); } catch (System.Exception error) { failure ??= error; }
            if (failure != null) throw failure;
        }
    }
    private sealed record Witness(PlanarFace First, PlanarFace Second, Point3d A, Point3d B, double Distance, string Method, string? FirstEdge = null, string? SecondEdge = null);
    private sealed record AnalyticSupport(Point3d Origin, Vector3d Direction, double? Diameter);

    internal static object Read(GeometryAnalysisArguments args, DocumentState state, Transaction tx, ObjectId modelId, string instance, CancellationToken cancellation)
    {
        var request = args.SolidMeasurement ?? throw new DomainException("INVALID_ARGUMENT", "Solid measurement data is missing.");
        void Check() => cancellation.ThrowIfCancellationRequested();
        var bodies = new Dictionary<string, Solid3d>(StringComparer.Ordinal);
        foreach (string handle in args.Read.Handles!)
        {
            Check(); long bits = unchecked((long)ulong.Parse(handle, NumberStyles.HexNumber, CultureInfo.InvariantCulture));
            if (!state.Document.Database.TryGetObjectId(new Handle(bits), out var id) || id.IsNull || !id.IsValid)
                throw new DomainException("OBJECT_NOT_FOUND", "Solid measurement source was not found.", false, new { handle });
            if (id.IsErased || id.IsEffectivelyErased) throw new DomainException("OBJECT_ERASED", "Solid measurement source is erased.", false, new { handle });
            if (tx.GetObject(id, OpenMode.ForRead) is not Solid3d body || body.GetType() != typeof(Solid3d) || body.OwnerId != modelId)
                throw new DomainException("OUT_OF_SCOPE", "Solid measurement requires exact top-level native Model Space Solid3d bodies.", false, new { handle });
            CadSolidEditor.CheckGeometry(body, Check); bodies.Add(handle, body);
        }
        EntityRef Ref(Solid3d body) => new(instance, state.Id, Validation.NormalizeHandle(body.Handle.ToString()));
        var captured = new Dictionary<string, Boundary>(StringComparer.Ordinal);
        try
        {
            var units = new { name = state.Document.Database.Insunits.ToString(), value = (int)state.Document.Database.Insunits };
            if (SolidMeasurementValidation409.IsAxisMeasurement(request.Mode))
                return ReadAxisMeasurement(request, bodies, Ref, units, Check);
            foreach (var item in bodies)
            {
                var selected = request.Mode == "face_distance" ? request.Faces!.Where(f => f.Handle == item.Key).Select(f => f.NativeId).ToHashSet(StringComparer.Ordinal) : null;
                Check(); captured.Add(item.Key, Capture(item.Value, selected, Check));
            }
            object FaceRef(PlanarFace face, Boundary owner) => new { body_ref = Ref(owner.Body), native_id = face.Id };
            object Accuracy() => new
            {
                point_tolerance = Tolerance.Global.EqualPoint,
                vector_tolerance = Tolerance.Global.EqualVector,
                finite_edge_verification_tolerance = CurveDistanceVerification.AbsoluteTolerance,
                policy = "native finite-edge closest pairs and trimmed-face containment; double-precision planar projections; no tessellation or certified error bound"
            };
            if (request.Mode == "wall_thickness")
            {
                var boundary = captured.Values.Single();
                var first = Find(boundary, request.Faces![0].NativeId); var second = Find(boundary, request.Faces[1].NativeId);
                var p = request.Point!; var start = new Point3d(p[0], p[1], p[2]);
                var end = Wall(boundary, first, second, start, Check); Check();
                return new
                {
                    mode = request.Mode,
                    coordinate_system = "WCS",
                    units,
                    solver = "native_planar_topology_material_chord",
                    first_face = FaceRef(first, boundary),
                    second_face = FaceRef(second, boundary),
                    first_point = Point(start),
                    second_point = Point(end),
                    thickness = Number(start.DistanceTo(end)),
                    direction = Point((end - start).GetNormal()),
                    material_chord_verified = true,
                    global_minimum_evaluated = false,
                    accuracy = Accuracy(),
                    result_scope = "one local normal material chord between selected opposed parallel planar faces of a complete straight-edged planar body",
                    warning = "This is the thickness at the requested first-face interior point. It is not a minimum thickness map, rolling-ball thickness or a curved-wall measurement."
                };
            }
            Boundary firstOwner, secondOwner; Witness? best = null;
            if (request.Mode == "face_distance")
            {
                firstOwner = captured[request.Faces![0].Handle]; secondOwner = captured[request.Faces[1].Handle];
                best = Minimum(Find(firstOwner, request.Faces[0].NativeId), Find(secondOwner, request.Faces[1].NativeId), Check);
            }
            else
            {
                firstOwner = captured[args.Read.Handles![0]]; secondOwner = captured[args.Read.Handles[1]];
                foreach (var first in firstOwner.Faces) foreach (var second in secondOwner.Faces)
                {
                    Check(); var next = Minimum(first, second, Check); if (best == null || next.Distance < best.Distance) best = next;
                }
            }
            if (best == null) throw new DomainException("NATIVE_DISTANCE_MISMATCH", "No complete finite-face distance witness is available.");
            Need(Contains(best.First, best.A, Check) != "outside" && Contains(best.Second, best.B, Check) != "outside", "Native distance witnesses do not belong to both selected finite faces.", "NATIVE_DISTANCE_MISMATCH");
            bool? interference = null; double? clearance = null;
            if (request.Mode == "solid_distance")
            {
                using var a = Detached(firstOwner.Body, new[] { firstOwner.Body.UnmanagedObject, secondOwner.Body.UnmanagedObject });
                using var b = Detached(secondOwner.Body, new[] { firstOwner.Body.UnmanagedObject, secondOwner.Body.UnmanagedObject, a.UnmanagedObject });
                Check(); interference = a.CheckInterference(b); Check(); clearance = interference.Value ? 0 : best.Distance;
            }
            return new
            {
                mode = request.Mode,
                coordinate_system = "WCS",
                units,
                solver = "native_finite_edges_and_trimmed_planar_faces",
                first_face = FaceRef(best.First, firstOwner),
                second_face = FaceRef(best.Second, secondOwner),
                first_point = Point(best.A),
                second_point = Point(best.B),
                distance = best.Distance,
                witness_method = best.Method,
                first_edge_native_id = best.FirstEdge,
                second_edge_native_id = best.SecondEdge,
                solid_interference = interference,
                material_clearance = clearance,
                accuracy = Accuracy(),
                result_scope = request.Mode == "solid_distance" ? "minimum Euclidean distance between complete planar straight-edged body boundaries" : "minimum Euclidean distance between the two bounded trimmed planar straight-edged faces",
                warning = request.Mode == "solid_distance" ? "Distance witnesses lie on body boundaries. Interfering material has zero clearance even if one boundary encloses the other and boundary distance is positive. No penetration depth or contact classification is returned." : "The closest pair may be nonunique. Supporting planes are not extended past their native trimmed faces. Zero distance does not classify overlap or material thickness."
            };
        }
        catch (Teigha.Runtime.Exception error)
        {
            throw new DomainException("NATIVE_SOLID_MEASUREMENT_UNAVAILABLE", "Native geometry could not complete this measurement; no measurement is inferred from bounds.", false,
                new { mode = request.Mode, api_status = error.ErrorStatus.ToString() });
        }
        finally
        {
            System.Exception? failure = null;
            foreach (var boundary in captured.Values) try { boundary.Dispose(); } catch (System.Exception error) { failure ??= error; }
            if (failure != null) throw failure;
        }
    }

    private static object ReadAxisMeasurement(SolidMeasurement409 request, Dictionary<string, Solid3d> bodies,
        Func<Solid3d, EntityRef> reference, object units, Action check)
    {
        var selections = request.Faces!;
        var supports = new Dictionary<SolidFaceSelection409, AnalyticSupport>();
        foreach (var body in bodies)
        {
            var selected = selections.Where(face => face.Handle == body.Key).ToDictionary(face => face.NativeId, StringComparer.Ordinal);
            var seen = new HashSet<string>(StringComparer.Ordinal);
            check();
            using var brep = new Brep(body.Value);
            Need(!brep.IsNull, "Native body B-rep is unavailable.", "OBJECT_READ_FAILED");
            foreach (Face face in brep.Faces)
                using (face)
                {
                    check();
                    var id = Identity(face, body.Value, SubentityType.Face);
                    Need(seen.Add(id), "Native body has duplicate face identities.", "SUBENTITY_IDENTITY_UNAVAILABLE");
                    if (!selected.TryGetValue(id, out var selection)) continue;
                    using var surface = face.Surface;
                    if (request.Mode == "axis_plane_distance" && selection == selections[1])
                    {
                        var plane = ReadPlane(surface, id, check);
                        supports.Add(selection, new(plane.Origin, plane.Normal, null));
                    }
                    else
                        supports.Add(selection, ReadCylinder(surface, id, check));
                }
            Need(selected.Keys.All(seen.Contains), "Selected native face IDs do not belong to the current source body.", "SUBENTITY_NOT_FOUND");
        }

        object FaceReference(SolidFaceSelection409 face) => new { body_ref = reference(bodies[face.Handle]), native_id = face.NativeId };
        object CylinderData(SolidFaceSelection409 face, AnalyticSupport geometry) => new
        {
            face = FaceReference(face),
            axis_origin = Point(geometry.Origin),
            axis_direction = Point(geometry.Direction),
            diameter = geometry.Diameter!.Value
        };
        const string solver = "native_analytic_supporting_surfaces";
        const string coordinate_system = "WCS";
        var first = supports[selections[0]];
        var cylinder = CylinderData(selections[0], first);
        check();
        if (request.Mode == "cylinder_diameter")
            return new { mode = request.Mode, coordinate_system, units, solver, cylinder };

        var second = supports[selections[1]];
        var delta = second.Origin - first.Origin;
        var cross = first.Direction.CrossProduct(second.Direction);
        double sine = Number(cross.Length);
        double cosine = Number(Math.Abs(first.Direction.DotProduct(second.Direction)));
        double vectorTolerance = Number(Tolerance.Global.EqualVector);
        if (request.Mode == "axis_axis_distance")
        {
            bool parallel = sine <= vectorTolerance;
            double distance = parallel ? delta.CrossProduct(first.Direction).Length : Math.Abs(delta.DotProduct(cross * (1 / sine)));
            return new
            {
                mode = request.Mode,
                coordinate_system,
                units,
                solver,
                first_cylinder = cylinder,
                second_cylinder = CylinderData(selections[1], second),
                distance = Number(distance),
                angle_radians = Math.Atan2(sine, cosine),
                parallel_within_native_tolerance = parallel,
                vector_tolerance = vectorTolerance,
                result_scope = "Infinite supporting axes. Within the native vector tolerance, distance is the second axis origin's offset from the first axis."
            };
        }

        bool parallelToPlane = cosine <= vectorTolerance;
        return new
        {
            mode = request.Mode,
            coordinate_system,
            units,
            solver,
            cylinder,
            plane = new { face = FaceReference(selections[1]), origin = Point(second.Origin), normal = Point(second.Direction) },
            distance = parallelToPlane ? Number(Math.Abs(delta.DotProduct(second.Direction))) : 0,
            angle_radians = Math.Atan2(cosine, sine),
            parallel_within_native_tolerance = parallelToPlane,
            vector_tolerance = vectorTolerance,
            result_scope = "Infinite axis and supporting plane. Distance is zero for an intersection; within the native vector tolerance it is the first axis origin's offset from the plane."
        };
    }

    private static AnalyticSupport ReadCylinder(Teigha.Geometry.Surface surface, string faceId, Action check)
    {
        AnalyticSupport Observe(Cylinder cylinder)
        {
            check();
            var origin = cylinder.Origin;
            var axis = cylinder.AxisOfSymmetry;
            double diameter = Number(2 * cylinder.Radius);
            double length = Number(axis.Length);
            Need(length > 0 && diameter > 0, "Native cylindrical face has no nonzero axis or positive diameter.", "DEGENERATE_GEOMETRY");
            check();
            return new(origin, axis.GetNormal(), diameter);
        }

        if (surface is Cylinder native) return Observe(native);
        if (surface is ExternalBoundedSurface bounded && bounded.IsCylinder)
        {
            using var support = ReadOwnedBaseSurface(bounded, faceId, check);
            if (support is Cylinder cylinder) return Observe(cylinder);
        }
        throw new DomainException("UNSUPPORTED_MEASUREMENT_GEOMETRY", "Use a face that exposes a native circular cylinder supporting surface.", false,
            new { face_native_id = faceId, surface_runtime_type = surface.GetType().FullName });
    }

    private static Boundary Capture(Solid3d body, HashSet<string>? selected, Action check)
    {
        var result = new Boundary(body); var seen = new HashSet<string>(StringComparer.Ordinal); var budget = new ModelInventoryBudget(check);
        try
        {
            Need(!result.Brep.IsNull, "Native body B-rep is unavailable.", "OBJECT_READ_FAILED");
            foreach (Face native in result.Brep.Faces)
            {
                bool retained = false;
                try
                {
                    check(); string id = Identity(native, body, SubentityType.Face); Need(seen.Add(id), "Native body has duplicate face identities.", "SUBENTITY_IDENTITY_UNAVAILABLE");
                    if (selected != null && !selected.Contains(id)) continue;
                    using var surface = native.Surface;
                    Need(surface is Plane || surface is ExternalBoundedSurface bounded && bounded.IsPlane || surface is ExternalSurface external && external.IsPlane,
                        "This measurement requires planar selected faces; complete body modes require every face to be planar.", "UNSUPPORTED_MEASUREMENT_GEOMETRY");
                    var edges = new List<Segment>(); var edgeIds = new HashSet<string>(StringComparer.Ordinal);
                    foreach (Teigha.BoundaryRepresentation.BoundaryLoop loop in native.Loops)
                        using (loop) foreach (Edge edge in loop.Edges)
                            using (edge)
                            {
                                check(); string key = Identity(edge, body, SubentityType.Edge); if (!edgeIds.Add(key)) continue;
                                if (!result.Edges.TryGetValue(key, out var segment))
                                {
                                    var copy = body.CopyEdge(edge.SubentityPath.SubentId);
                                    if (copy == null || !copy.ObjectId.IsNull || !copy.OwnerId.IsNull || copy.UnmanagedObject == IntPtr.Zero ||
                                        copy.UnmanagedObject == body.UnmanagedObject || result.Edges.Values.Any(old => old.Curve.UnmanagedObject == copy.UnmanagedObject))
                                    {
                                        if (copy != null) GC.SuppressFinalize(copy);
                                        throw new DomainException("UNSAFE_NATIVE_CLONE", "Native edge copy did not prove an independent detached root; an unproved alias was not disposed.");
                                    }
                                    if (copy is not Line line || copy.GetType() != typeof(Line))
                                    { copy.Dispose(); throw new DomainException("UNSUPPORTED_MEASUREMENT_GEOMETRY", "Finite planar-face measurement requires actual native straight Line edges.", false, new { face_native_id = id, edge_native_id = key }); }
                                    segment = new(key, line); result.Edges.Add(key, segment);
                                    Need(line.StartPoint.DistanceTo(line.EndPoint) > 0, "Native boundary contains a zero-length edge.", "DEGENERATE_GEOMETRY");
                                    budget.Add(new { id = key, start = Point(segment.A), end = Point(segment.B) });
                                }
                                edges.Add(segment);
                            }
                    Need(edges.Count >= 3, "A finite straight planar face has no complete boundary.", "UNSUPPORTED_MEASUREMENT_GEOMETRY");
                    var observedPlane = ReadPlane(surface, id, check); var origin = observedPlane.Origin; var normal = observedPlane.Normal;
                    foreach (var edge in edges)
                    { check(); Need(Math.Abs((edge.A - origin).DotProduct(normal)) <= Tolerance.Global.EqualPoint && Math.Abs((edge.B - origin).DotProduct(normal)) <= Tolerance.Global.EqualPoint, "Native straight edges do not lie on the reported plane.", "INVALID_GEOMETRY"); }
                    var face = new PlanarFace(id, native, origin, normal, native.IsOrientToSurface ? normal : -normal, edges);
                    budget.Add(new { id, origin = Point(origin), normal = Point(normal), edge_ids = edges.Select(e => e.Id).ToArray() }); result.Faces.Add(face); retained = true;
                }
                finally { if (!retained) native.Dispose(); }
            }
            if (selected != null) Need(selected.SetEquals(result.Faces.Select(f => f.Id)), "Selected native face IDs do not belong to the current source body.", "SUBENTITY_NOT_FOUND");
            Need(result.Faces.Count > 0, "Native body has no measurable finite faces.", "UNSUPPORTED_MEASUREMENT_GEOMETRY"); check(); return result;
        }
        catch { result.Dispose(); throw; }
    }

    private static (Point3d Origin, Vector3d Normal) ReadPlane(Teigha.Geometry.Surface surface, string faceId, Action check)
    {
        (Point3d Origin, Vector3d Normal) Observe(Teigha.Geometry.Plane plane)
        {
            check(); var origin = plane.PointOnPlane; var normal = plane.Normal; check();
            Need(double.IsFinite(origin.X) && double.IsFinite(origin.Y) && double.IsFinite(origin.Z) && double.IsFinite(normal.Length) && normal.Length > 0,
                "Native geometric Plane has no finite point and nonzero normal.", "NATIVE_FACE_PLANE_UNAVAILABLE");
            return (origin, normal.GetNormal());
        }
        if (surface is Teigha.Geometry.Plane actual) return Observe(actual);
        if (surface is not ExternalBoundedSurface bounded)
            throw new DomainException("NATIVE_FACE_PLANE_UNAVAILABLE", "This external planar surface does not expose the supported owned BaseSurface Plane path.", false,
                new { face_native_id = faceId, surface_runtime_type = surface.GetType().FullName, api = "ExternalBoundedSurface.BaseSurface" });

        using var baseSurface = ReadOwnedBaseSurface(bounded, faceId, check);
        check(); if (baseSurface is not Teigha.Geometry.Plane plane)
            throw new DomainException("NATIVE_FACE_PLANE_UNAVAILABLE", "The native planar face BaseSurface did not return an actual geometric Plane.", false,
                new { face_native_id = faceId, surface_runtime_type = surface.GetType().FullName, base_surface_runtime_type = baseSurface?.GetType().FullName, api = "ExternalBoundedSurface.BaseSurface" });
        return Observe(plane);
    }

    private static Teigha.Geometry.Surface? ReadOwnedBaseSurface(ExternalBoundedSurface surface, string faceId, Action check)
    {
        // Installed SDK get_BaseSurface obtains OdGeSurface* and calls
        // Wrapped.CreateRaw<OdGeSurface>(pointer, bOwner:true). The resulting
        // geometric object is owned, unlike an arbitrarily borrowed child.
        // Read values and dispose it while its external parent remains alive.
        check(); var baseSurface = surface.BaseSurface;
        if (baseSurface != null && (ReferenceEquals(baseSurface, surface) || baseSurface.UnmanagedObject == IntPtr.Zero ||
            baseSurface.UnmanagedObject == surface.UnmanagedObject || !baseSurface.AutoDelete))
        {
            GC.SuppressFinalize(baseSurface);
            throw new DomainException("NATIVE_SURFACE_OWNERSHIP_UNAVAILABLE", "Native BaseSurface did not prove a distinct owned geometric wrapper; an unproved alias was not disposed.", false,
                new { face_native_id = faceId, api = "ExternalBoundedSurface.BaseSurface" });
        }
        return baseSurface;
    }

    private static Solid3d Detached(Solid3d source, IEnumerable<IntPtr> forbidden)
    {
        var value = source.Clone() as Solid3d;
        if (value == null || value.GetType() != typeof(Solid3d) || !value.ObjectId.IsNull || !value.OwnerId.IsNull ||
            value.UnmanagedObject == IntPtr.Zero || forbidden.Contains(value.UnmanagedObject))
        {
            if (value != null) GC.SuppressFinalize(value);
            throw new DomainException("UNSAFE_NATIVE_CLONE", "Native interference copy did not prove an exact detached distinct body; an unproved alias was not disposed.");
        }
        return value;
    }

    private static string Identity(BrepEntity item, Solid3d body, SubentityType kind)
    {
        var path = item.SubentityPath; var parents = path.GetObjectIds(); long index = path.SubentId.IndexPtr.ToInt64();
        Need(!path.IsNull && parents.Length == 1 && (parents[0].IsNull || parents[0] == body.ObjectId) && path.SubentId.Type == kind && index > 0,
            "Native topology identity does not identify the explicitly opened source and expected feature.", "SUBENTITY_IDENTITY_UNAVAILABLE");
        return index.ToString(CultureInfo.InvariantCulture);
    }
    private static PlanarFace Find(Boundary boundary, string id) => boundary.Faces.SingleOrDefault(f => f.Id == id) ?? throw new DomainException("SUBENTITY_NOT_FOUND", "Selected face is unavailable.");
    private static string Contains(PlanarFace face, Point3d point, Action check)
    {
        if (Math.Abs((point - face.Origin).DotProduct(face.Normal)) > Tolerance.Global.EqualPoint) return "outside";
        check(); using var container = face.Native.GetPointContainment(point, out var classification); check();
        return PointContainment.Classify(classification.ToString(), container == null || container.IsNull);
    }
    private static Point3d Project(Point3d point, PlanarFace face) => point - face.Normal * ((point - face.Origin).DotProduct(face.Normal));
    private static Witness Minimum(PlanarFace first, PlanarFace second, Action check)
    {
        Witness? best = null;
        void Candidate(Point3d a, Point3d b, string method, string? ea = null, string? eb = null)
        {
            check(); double distance = Number(a.DistanceTo(b)); if (best == null || distance < best.Distance) best = new(first, second, a, b, distance, method, ea, eb);
        }
        foreach (var a in first.Edges) foreach (var b in second.Edges)
        {
            check(); var closest = CadCurveDistance.Read(a.Curve, b.Curve, check); Candidate(new(closest.First), new(closest.Second), "native_finite_edge_pair", a.Id, b.Id);
        }
        void Against(Segment edge, PlanarFace target, bool reverse)
        {
            foreach (var point in new[] { edge.A, edge.B })
            {
                check(); var foot = Project(point, target);
                if (Contains(target, foot, check) != "outside")
                { if (reverse) Candidate(foot, point, "vertex_to_trimmed_face", null, edge.Id); else Candidate(point, foot, "vertex_to_trimmed_face", edge.Id); }
            }
            double start = (edge.A - target.Origin).DotProduct(target.Normal), end = (edge.B - target.Origin).DotProduct(target.Normal);
            double denominator = start - end;
            if (denominator != 0)
            {
                double t = start / denominator;
                if (t >= 0 && t <= 1)
                {
                    var hit = edge.A + (edge.B - edge.A) * t;
                    if (Contains(target, hit, check) != "outside")
                    { if (reverse) Candidate(hit, hit, "finite_edge_face_crossing", null, edge.Id); else Candidate(hit, hit, "finite_edge_face_crossing", edge.Id); }
                }
            }
        }
        foreach (var edge in first.Edges) Against(edge, second, false);
        foreach (var edge in second.Edges) Against(edge, first, true);
        return best ?? throw new DomainException("NATIVE_DISTANCE_MISMATCH", "No finite planar-face witness was returned.");
    }

    private static Point3d Wall(Boundary boundary, PlanarFace first, PlanarFace second, Point3d start, Action check)
    {
        Need(first.Normal.CrossProduct(second.Normal).Length <= Tolerance.Global.EqualVector, "Local wall measurement requires parallel selected planes.", "UNSUPPORTED_MEASUREMENT_GEOMETRY");
        Need(Contains(first, start, check) == "inside", "The WCS measurement point must lie strictly inside the first trimmed face.", "POINT_OUTSIDE_MEASUREMENT_FACE");
        var end = Project(start, second); double length = Number(start.DistanceTo(end));
        Need(length > Tolerance.Global.EqualPoint, "The selected faces have no positive resolvable normal separation.", "DEGENERATE_GEOMETRY");
        var direction = (end - start).GetNormal();
        Need(direction.CrossProduct(first.Normal).Length <= Tolerance.Global.EqualVector && direction.DotProduct(first.Outward) < 0 && direction.DotProduct(second.Outward) > 0,
            "The selected oriented faces must enclose material along the first face inward normal and second face outward normal.", "FACES_DO_NOT_BOUND_WALL");
        Need(Contains(second, end, check) == "inside", "The normal chord misses the second trimmed face interior.", "POINT_OUTSIDE_MEASUREMENT_FACE");
        using var chord = new Line(start, end);
        foreach (var face in boundary.Faces)
        {
            check(); if (face == first || face == second) continue;
            double a = (start - face.Origin).DotProduct(face.Normal), b = (end - face.Origin).DotProduct(face.Normal);
            Need(!(Math.Abs(a) <= Tolerance.Global.EqualPoint && Contains(face, start, check) != "outside") && !(Math.Abs(b) <= Tolerance.Global.EqualPoint && Contains(face, end, check) != "outside"),
                "The chord endpoint touches another native face; choose an unambiguous face-interior point.", "AMBIGUOUS_WALL_CHORD");
            if (Math.Abs(a) <= Tolerance.Global.EqualPoint && Math.Abs(b) <= Tolerance.Global.EqualPoint)
            {
                foreach (var edge in face.Edges)
                { check(); Need(CadCurveDistance.Read(chord, edge.Curve, check).Distance > Tolerance.Global.EqualPoint, "The chord runs along or touches another boundary face.", "AMBIGUOUS_WALL_CHORD"); }
            }
            else if (a - b != 0)
            {
                double t = a / (a - b);
                if (t > 0 && t < 1) Need(Contains(face, start + (end - start) * t, check) == "outside",
                    "Another body or cavity boundary interrupts the selected chord; this pair does not measure one wall.", "INTERRUPTED_WALL_CHORD");
            }
        }
        return end;
    }
    private static void Need(bool condition, string message, string code) { if (!condition) throw new DomainException(code, message); }
    private static double Number(double value) => double.IsFinite(value) ? value : throw new DomainException("INVALID_GEOMETRY", "Native measurement contains a non-finite number.");
    private static double[] Point(Point3d point) => [Number(point.X), Number(point.Y), Number(point.Z)];
    private static double[] Point(Vector3d point) => [Number(point.X), Number(point.Y), Number(point.Z)];
}
