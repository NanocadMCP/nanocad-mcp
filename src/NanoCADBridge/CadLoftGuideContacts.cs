using NanoCAD.Contracts;
using Teigha.DatabaseServices;
using Teigha.Geometry;

namespace NanoCADBridge;

internal static class CadLoftGuideContacts
{
    private sealed record SectionContacts(EntityRef section, string status, double[][] points, string? reason);

    internal static object Read(GeometryAnalysisArguments args, DocumentState state, Transaction tx,
        ObjectId model, string instance, Action cancel)
    {
        var db = state.Document.Database;
        var guide = (Curve)CadLoftSource409.Resolve(args.GuideHandle!, false, db, tx, model, cancel);
        if (guide is Ray or Xline)
            throw new DomainException("UNSUPPORTED_ENTITY_TYPE", "Select a finite loft guide.");
        var budget = new SerializedCapacity357(Wire.PayloadBudget - 8192, cancel);
        var sections = new List<SectionContacts>();
        foreach (var handle in args.Read.Handles!)
        {
            cancel();
            var reference = new EntityRef(instance, state.Id, handle);
            SectionContacts row;
            try
            {
                var section = CadLoftSource409.Resolve(handle, true, db, tx, model, cancel);
                if (section is Curve { IsPlanar: false })
                    throw new DomainException("LOFT_SECTION_NONPLANAR", "The selected closed curve is not a planar loft section.");
                var points = ContactPoints(guide, section, cancel);
                row = new(reference, points.Length == 0 ? "no_points_reported" : "points_reported", points, null);
            }
            catch (Teigha.Runtime.Exception error)
            {
                cancel();
                row = new(reference, "unavailable", [], error.ErrorStatus.ToString());
            }
            catch (DomainException error) when (error.Error.code is "SECTION_BOUNDARY_UNAVAILABLE" or
                "LOFT_CONTACT_UNVERIFIED" or "LOFT_SECTION_NONPLANAR" or "REGION_GEOMETRY_UNAVAILABLE" or "INVALID_GEOMETRY" or "UNSUPPORTED_ENTITY_TYPE" or "EMPTY_GEOMETRY")
            {
                row = new(reference, "unavailable", [], error.Error.code + ": " + error.Message);
            }
            budget.Add(row);
            sections.Add(row);
        }
        return new
        {
            mode = LoftGuideContacts.Mode,
            coordinate_system = "WCS",
            solver = "native_kernel",
            units = new { name = db.Insunits.ToString(), value = (int)db.Insunits },
            guide = new EntityRef(instance, state.Id, args.GuideHandle!),
            sections,
            overlap_evaluated = false
        };
    }

    private static double[][] ContactPoints(Curve guide, Entity section, Action cancel)
    {
        Curve[] curves;
        DBObjectCollection? owned = null;
        if (section is Region region) (curves, owned) = CadPlanarSection.ReadBoundaries(region, cancel);
        else curves = [(Curve)section];
        try
        {
            var witnesses = new HashSet<(double X, double Y, double Z)>();
            var values = new List<double[]>();
            var budget = new SerializedCapacity357(Wire.PayloadBudget - 8192, cancel);
            double tolerance = Tolerance.Global.EqualPoint;
            foreach (var curve in curves)
            {
                cancel();
                using var hits = new Point3dCollection();
                guide.IntersectWith(curve, Intersect.OnBothOperands, hits, IntPtr.Zero, IntPtr.Zero);
                foreach (Point3d hit in hits)
                {
                    cancel();
                    if (!double.IsFinite(hit.X) || !double.IsFinite(hit.Y) || !double.IsFinite(hit.Z))
                        throw new DomainException("LOFT_CONTACT_UNVERIFIED", "The intersection returned a nonfinite WCS point.");
                    double guideDistance = hit.DistanceTo(guide.GetClosestPointTo(hit, false));
                    double boundaryDistance = hit.DistanceTo(curve.GetClosestPointTo(hit, false));
                    if (!double.IsFinite(guideDistance) || !double.IsFinite(boundaryDistance))
                        throw new DomainException("LOFT_CONTACT_UNVERIFIED", "A finite-curve closest-point distance was unavailable.");
                    if (guideDistance > tolerance || boundaryDistance > tolerance)
                        throw new DomainException("LOFT_CONTACT_UNVERIFIED", FormattableString.Invariant(
                            $"WCS intersection ({hit.X:R}, {hit.Y:R}, {hit.Z:R}): distance to finite guide {guideDistance:R}, distance to finite section boundary {boundaryDistance:R}, point tolerance {tolerance:R} (drawing units)."));
                    // A shared Region vertex can be returned by two trims.
                    if (!witnesses.Add((hit.X, hit.Y, hit.Z))) continue;
                    double[] value = [hit.X, hit.Y, hit.Z];
                    budget.Add(value);
                    values.Add(value);
                }
            }
            return values.ToArray();
        }
        finally { if (owned != null) CadPlanarSection.DisposeBoundaryObjects(owned); }
    }
}
