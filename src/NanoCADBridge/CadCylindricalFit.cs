using NanoCAD.Contracts;
using Teigha.DatabaseServices;
using Teigha.Geometry;
using Brep = Teigha.BoundaryRepresentation.Brep;
using BoundaryLoop = Teigha.BoundaryRepresentation.BoundaryLoop;
using Edge = Teigha.BoundaryRepresentation.Edge;
using Face = Teigha.BoundaryRepresentation.Face;

namespace NanoCADBridge;

internal static partial class CadSolidMeasurement409
{
    private sealed record CylinderBand(AnalyticSupport Cylinder, Point3d Start, Point3d End, Vector3d ProbeDirection, bool IsShaft = false);
    private sealed record RimEdge(double Station, double Start, double Sweep);

    private static object ReadCylindricalFit(SolidFaceSelection409[] faces, Dictionary<string, Solid3d> bodies,
        Func<Solid3d, EntityRef> reference, object units, Action check)
    {
        CylinderBand Read(SolidFaceSelection409 selected)
        {
            var body = bodies[selected.Handle];
            CylinderBand? band = null;
            using (var brep = new Brep(body))
            {
                Need(!brep.IsNull, "Native body B-rep is unavailable.", "OBJECT_READ_FAILED");
                foreach (Face face in brep.Faces)
                    using (face)
                    {
                        check();
                        if (Identity(face, body, SubentityType.Face) != selected.NativeId) continue;
                        Need(band == null, "Duplicate selected native face identity.", "SUBENTITY_IDENTITY_UNAVAILABLE");
                        using var surface = face.Surface;
                        band = ReadCylinderBand(body, face, ReadCylinder(surface, selected.NativeId, check), check);
                    }
            }
            if (band == null) throw new DomainException("SUBENTITY_NOT_FOUND", "Selected cylinder face no longer belongs to its body.", false, new { face = selected });
            return band with { IsShaft = MaterialInside(body, band, selected, check) };
        }

        var shaft = Read(faces[0]);
        var bore = Read(faces[1]);
        if (!shaft.IsShaft || bore.IsShaft)
            throw new DomainException("CYLINDER_ROLE_MISMATCH", "Select the external shaft face first and the internal bore face second.", false,
                new { first_role = shaft.IsShaft ? "shaft" : "bore", second_role = bore.IsShaft ? "shaft" : "bore" });
        var direction = shaft.Cylinder.Direction;
        var pointTolerance = Tolerance.Global.EqualPoint;
        var vectorTolerance = Tolerance.Global.EqualVector;
        var angleSine = Number(direction.CrossProduct(bore.Cylinder.Direction).Length);
        double shaftRadius = shaft.Cylinder.Diameter!.Value / 2, boreRadius = bore.Cylinder.Diameter!.Value / 2;
        var longestBand = Math.Max(shaft.Start.DistanceTo(shaft.End), bore.Start.DistanceTo(bore.End));
        Need(angleSine <= vectorTolerance && angleSine * Math.Max(longestBand, Math.Max(shaftRadius, boreRadius)) <= pointTolerance,
            "The selected finite cylinders require parallel axes throughout their measured lengths.", "NONPARALLEL_CYLINDER_AXES");
        double Station(Point3d p) => Number((p - shaft.Cylinder.Origin).DotProduct(direction));
        var limits = (CylinderBand band) => (Math.Min(Station(band.Start), Station(band.End)), Math.Max(Station(band.Start), Station(band.End)));
        var (shaftStart, shaftEnd) = limits(shaft);
        var (boreStart, boreEnd) = limits(bore);
        // Lateral offset is convex along a straight axis. Measuring both ends
        // of the common interval captures its greatest offset even when the
        // axes differ within native tolerance. No remote support origin is used.
        double OffsetAt(double z)
        {
            var p = bore.Start + bore.Cylinder.Direction * ((z - Station(bore.Start)) / bore.Cylinder.Direction.DotProduct(direction));
            return Number((p - shaft.Cylinder.Origin).CrossProduct(direction).Length);
        }
        double overlapStart = Math.Max(shaftStart, boreStart), overlapEnd = Math.Min(shaftEnd, boreEnd);
        var offset = overlapEnd >= overlapStart ? Math.Max(OffsetAt(overlapStart), OffsetAt(overlapEnd))
            : OffsetAt(boreStart + (boreEnd - boreStart) / 2);
        var fit = CylindricalFit.Measure(shaftStart, shaftEnd, boreStart, boreEnd, shaftRadius, boreRadius, offset, pointTolerance);
        object Band(SolidFaceSelection409 sel, CylinderBand band, double start, double end) => new
        {
            face = new { body_ref = reference(bodies[sel.Handle]), native_id = sel.NativeId },
            role = band.IsShaft ? "shaft" : "bore",
            diameter = band.Cylinder.Diameter!.Value,
            axis_origin = Point(band.Cylinder.Origin),
            axis_direction = Point(band.Cylinder.Direction),
            start_station = start,
            end_station = end
        };
        return new
        {
            mode = "cylindrical_fit",
            coordinate_system = "WCS",
            solver = "native_trimmed_cylindrical_faces",
            units,
            shaft = Band(faces[0], shaft, shaftStart, shaftEnd),
            bore = Band(faces[1], bore, boreStart, boreEnd),
            fit.axial_overlap,
            fit.axial_gap,
            fit.minimum_radial_clearance,
            fit.fit_status,
            axis_offset = offset,
            point_tolerance = pointTolerance,
            vector_tolerance = vectorTolerance,
            result_scope = "Selected finite cylindrical bands. Stations use the shaft origin and direction. Axis offset is greatest over their overlap, or measured at the bore midpoint for separated bands. Radial margin uses axes parallel within the reported tolerances and applies only to their overlap; other faces and end stops are outside the measurement."
        };
    }

    private static CylinderBand ReadCylinderBand(Solid3d body, Face face, AnalyticSupport cylinder, Action check)
    {
        const string unsupported = "UNSUPPORTED_CYLINDER_TRIM";
        var origin = cylinder.Origin;
        var axis = cylinder.Direction;
        var radius = cylinder.Diameter!.Value / 2;
        double tol = Tolerance.Global.EqualPoint;
        double directionTolerance = Tolerance.Global.EqualVector;
        Need(radius > tol, "Cylinder radius is indistinguishable from zero at native point tolerance.", "DEGENERATE_GEOMETRY");
        var x = axis.GetPerpendicularVector().GetNormal();
        var y = axis.CrossProduct(x);
        double Station(Point3d p) => Number((p - origin).DotProduct(axis));
        var rims = new List<RimEdge>();
        var seams = new List<(Point3d Start, Point3d End)>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (BoundaryLoop loop in face.Loops)
            using (loop)
                foreach (Edge edge in loop.Edges)
                    using (edge)
                    {
                        check();
                        if (!seen.Add(Identity(edge, body, SubentityType.Edge))) continue;
                        var copy = body.CopyEdge(edge.SubentityPath.SubentId);
                        if (copy == null || !copy.ObjectId.IsNull || !copy.OwnerId.IsNull || copy.UnmanagedObject == IntPtr.Zero || copy.UnmanagedObject == body.UnmanagedObject)
                        {
                            if (copy != null) GC.SuppressFinalize(copy);
                            throw new DomainException("UNSAFE_NATIVE_CLONE", "Cylindrical rim did not produce an independent detached edge.");
                        }
                        using (copy)
                        {
                            if (copy is Line seam)
                            {
                                seams.Add((seam.StartPoint, seam.EndPoint));
                                continue;
                            }
                            Point3d center;
                            Vector3d normal;
                            double edgeRadius, start, sweep;
                            if (copy is Circle c)
                            {
                                center = c.Center; normal = c.Normal; edgeRadius = c.Radius; start = 0; sweep = Math.Tau;
                            }
                            else if (copy is Arc arc)
                            {
                                center = arc.Center; normal = arc.Normal; edgeRadius = arc.Radius;
                                var radial = arc.StartPoint - center;
                                start = Math.Atan2(radial.DotProduct(y), radial.DotProduct(x));
                                sweep = Math.CopySign(arc.TotalAngle, normal.DotProduct(axis));
                            }
                            else throw new DomainException(unsupported, "The selected cylindrical band has a noncircular trim edge.");
                            double z = Station(center);
                            Need(Math.Abs(edgeRadius - radius) <= tol && center.DistanceTo(origin + axis * z) <= tol &&
                                normal.GetNormal().CrossProduct(axis).Length <= directionTolerance,
                                "Cylindrical band rims must be coaxial circles perpendicular to its axis.", unsupported);
                            rims.Add(new(z, start, sweep));
                        }
                    }

        Need(rims.Count > 0, "No circular end rims were found on the selected cylindrical face.", unsupported);
        double low = rims.Min(r => r.Station), high = rims.Max(r => r.Station);
        Need(high - low > 2 * tol && rims.All(r => r.Station - low <= tol || high - r.Station <= tol),
            "Cylindrical band must have two distinct end rings without intermediate trimmed boundaries.", unsupported);
        foreach (var z in new[] { low, high })
        {
            var arcs = rims.Where(r => Math.Abs(r.Station - z) <= tol).Select(r => (r.Start, r.Sweep));
            Need(CylindricalFit.CoversRim(arcs, directionTolerance), "End rim does not cover a complete circle exactly once.", unsupported);
        }
        Vector3d? seamRadius = null;
        foreach (var seam in seams)
        {
            var a = seam.Start;
            var b = seam.End;
            if (Station(a) > Station(b)) (a, b) = (b, a);
            var radial = a - (origin + axis * low);
            Need(Math.Abs(Station(a) - low) <= tol && Math.Abs(Station(b) - high) <= tol &&
                b.DistanceTo(a + axis * (high - low)) <= tol && Math.Abs(radial.Length - radius) <= tol &&
                (seamRadius == null || (radial - seamRadius.Value).Length <= tol),
                "Straight trim edges must describe a single longitudinal cylinder seam.", unsupported);
            seamRadius = radial;
        }

        var radialWitness = seamRadius?.CrossProduct(axis).GetNormal() ?? x;
        return new(cylinder, origin + axis * low, origin + axis * high, radialWitness);
    }

    private static bool MaterialInside(Solid3d body, CylinderBand band, SolidFaceSelection409 selected, Action check)
    {
        // On nanoCAD 26 the sense of an external cylindrical surface and face
        // classified both a solid cylinder and a drilled hole alike. Instead,
        // witness which side of this boundary actually contains solid material.
        var center = band.Start + (band.End - band.Start) * 0.5;
        var x = band.ProbeDirection;
        var axis = band.Cylinder.Direction;
        var y = axis.CrossProduct(x).GetNormal();
        double radius = band.Cylinder.Diameter!.Value / 2;
        double tol = Tolerance.Global.EqualPoint;
        var frame = new SketchPlaneSpec(new(center.X, center.Y, center.Z), new(x.X, x.Y, x.Z), new(y.X, y.Y, y.Z));
        using var slice = CadPlanarSection.Open(body, frame, check);
        var witness = center + x * radius;
        double safeDistance = radius;
        bool foundRim = false;
        foreach (var curve in slice.BoundaryCurves())
        {
            check();
            var nearest = curve.GetClosestPointTo(witness, false);
            var distance = Number(nearest.DistanceTo(witness));
            bool thisCircle = curve switch
            {
                Circle c => c.Center.DistanceTo(center) <= tol && Math.Abs(c.Radius - radius) <= tol,
                Arc a => a.Center.DistanceTo(center) <= tol && Math.Abs(a.Radius - radius) <= tol,
                _ => false
            };
            if (thisCircle)
            {
                foundRim |= distance <= tol;
                continue;
            }
            safeDistance = Math.Min(safeDistance, distance);
        }
        // Never sample across another boundary, even on a very thin wall.
        double step = safeDistance / 2;
        if (!foundRim || step <= tol)
            throw new DomainException("CYLINDER_ROLE_UNAVAILABLE", "The selected circle and two distinct sides could not be resolved in a normal material section.",
                false, new { face = selected, rim_found = foundRim, clearance_to_next_boundary = safeDistance });
        var inside = slice.Classify(witness - x * step);
        var outside = slice.Classify(witness + x * step);
        if (inside == "material" && outside == "void") return true;
        if (inside == "void" && outside == "material") return false;
        throw new DomainException("CYLINDER_ROLE_UNAVAILABLE", "Native section material does not establish opposite sides of the selected circular boundary.",
            false, new { face = selected, radially_inside = inside, radially_outside = outside, sample_step = step });
    }
}
