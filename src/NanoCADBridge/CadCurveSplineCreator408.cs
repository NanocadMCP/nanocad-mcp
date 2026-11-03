using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using NanoCAD.Contracts;
using Teigha.DatabaseServices;
using Teigha.Geometry;

namespace NanoCADBridge;

// Read-only native representation derivation. Never erase, edit, retarget or
// move the source. Existing Fields/history/associations remain on that source.
internal static class CadCurveSplineCreator408
{
    private static void Require([DoesNotReturnIf(false)] bool condition, string message, string code = "NATIVE_CURVE_SPLINE_MISMATCH")
    { if (!condition) throw new DomainException(code, message); }
    private static bool Finite(Point3d p) => double.IsFinite(p.X) && double.IsFinite(p.Y) && double.IsFinite(p.Z);

    private static Curve Source(ObjectId id, Database database, Transaction transaction, ObjectId owner, Action cancel)
    {
        cancel(); Require(!id.IsNull && id.IsValid && !id.IsErased && !id.IsEffectivelyErased,
            "Source curve is no longer a live native entity.", "OBJECT_NOT_FOUND");
        var entity = transaction.GetObject(id, OpenMode.ForRead) as Curve;
        Require(entity != null && (entity.GetType() == typeof(Arc) || entity.GetType() == typeof(Ellipse)),
            "Native curve-to-Spline construction accepts an exact Arc or Ellipse base curve.", "UNSUPPORTED_ENTITY_TYPE");
        Require(entity.Database == database && entity.OwnerId == owner,
            "Source must belong to the requested current drawing space; nested/foreign definitions require their owning read route.", "OUT_OF_SCOPE");
        Require(entity.IsPlanar && Finite(entity.StartPoint) && Finite(entity.EndPoint) &&
            double.IsFinite(entity.StartParam) && double.IsFinite(entity.EndParam) && entity.EndParam > entity.StartParam,
            "Source must report a finite planar base curve with an increasing native parameter interval.", "INVALID_GEOMETRY");
        if (entity is Arc arc)
            Require(double.IsFinite(arc.Radius) && arc.Radius > 0 && double.IsFinite(arc.Normal.Length) && arc.Normal.Length > 0,
                "Arc source requires a positive finite radius and a usable plane normal.", "INVALID_GEOMETRY");
        if (entity is Ellipse ellipse)
            Require(double.IsFinite(ellipse.MajorRadius) && double.IsFinite(ellipse.MinorRadius) && ellipse.MajorRadius > 0 && ellipse.MinorRadius > 0,
                "Ellipse source requires positive finite major/minor radii.", "INVALID_GEOMETRY");
        // Fields/reactors/native model references alone do not forbid deriving
        // independent geometry from this exact read-only source.
        var shape = CadNativeCurveInspection342.Shape(entity, cancel);
        NativeCurveRead342.CheckSerializedBudget(shape, NativeCurveRead342.SnapshotByteBudget, cancel);
        return entity;
    }
    internal static ObjectId Resolve(CurveSplineSpec408 spec, Database database, Transaction transaction, ObjectId owner, Action? check = null)
    {
        var cancel = check ?? (() => { });
        var id = CadStructureWriter.Find(database, spec.SourceHandle);
        _ = Source(id, database, transaction, owner, cancel); return id;
    }

    internal static Spline Create(CurveSplineSpec408 spec, ObjectId sourceId, Database database,
        Transaction transaction, ObjectId owner, Action cancel, List<string>? warnings = null)
    {
        var source = Source(sourceId, database, transaction, owner, cancel);
        Require(Validation.NormalizeHandle(source.Handle.ToString()) == spec.SourceHandle,
            "Resolved conversion source differs from its requested native identity.", "OBJECT_NOT_FOUND");
        var before = CadContourBudget344.Source(source, cancel);
        JsonElement? Owned()
        {
            try { return NativeOwnedMetadata.Capture(source, transaction, cancel); }
            catch (DomainException error) when (error.Error.code == "UNSUPPORTED_OWNED_METADATA")
            {
                // Read-only conversion neither edits nor clones these records.
                // Preserve observed root/ownership identities; do not gate a
                // useful source on the absence of an opaque Field adapter.
                warnings?.Add("SOURCE_OWNED_DATA_PARTIAL: Source root geometry, identity, display, XData and ownership/reactor identities are retained. Inner Field or opaque owned records are not deeply compared by this read-only conversion; the original source and its relationships remain in place.");
                return null;
            }
        }
        var owned = Owned();
        cancel(); var spline = source.Spline;
        // Unsafe source aliases are not adopted or disposed. The managed getter
        // constructs an auto-delete wrapper for its native result; prove it is
        // actually a detached distinct root before taking ownership of it.
        Require(spline != null && spline.GetType() == typeof(Spline) && spline.ObjectId.IsNull && spline.OwnerId.IsNull &&
            spline.UnmanagedObject != IntPtr.Zero && spline.UnmanagedObject != source.UnmanagedObject,
            "Native curve representation did not return a detached distinct exact Spline.", "UNSAFE_NATIVE_CLONE");
        try
        {
            cancel();
            using var reactors = spline.GetPersistentReactorIds();
            Require(!spline.HasFields && spline.ExtensionDictionary.IsNull && reactors.Count == 0,
                "Derived Spline unexpectedly carries source-owned Fields, records or associations.");
            using var derivedData = spline.XData;
            Require(derivedData == null || derivedData.AsArray().Length == 0,
                "Derived Spline unexpectedly transfers source application data; the requested result is independent curve geometry.");
            var geometry = CadSplineReader.ReadReported(spline, cancel);
            Require(geometry.degree >= 1 && geometry.control_points.Length > geometry.degree &&
                geometry.knots.Length == (long)geometry.control_points.Length + geometry.degree + 1 &&
                geometry.end_parameter > geometry.start_parameter && geometry.planar && geometry.closed == source.Closed &&
                geometry.knots.Zip(geometry.knots.Skip(1), (a, b) => a <= b).All(x => x),
                "Derived Spline has an incomplete native definition or changes source closure/planarity.");
            Require(geometry.rational ? geometry.weights.Length == geometry.control_points.Length && geometry.weights.All(w => w > 0) :
                geometry.weights.Length == 0 || geometry.weights.Length == geometry.control_points.Length && geometry.weights.All(w => w > 0),
                "Derived Spline has inconsistent native rational weights.");
            double resolution = 1e-7;
            foreach (var p in geometry.control_points)
            {
                cancel(); resolution = Math.Max(resolution, 32 * 2.2204460492503131e-16 * p.Max(Math.Abs));
            }
            if (!source.Closed)
                Require((spline.StartPoint - source.StartPoint).Length <= resolution &&
                    (spline.EndPoint - source.EndPoint).Length <= resolution,
                    "Native conversion changes the actual open base-curve endpoints.");
            // A closed ellipse may have a different valid periodic seam. The
            // complete closed locus, rather than its arbitrary phase, matters.
            // Native getSpline is the representation conversion, not a sampled
            // fitting algorithm. These significant point observations catch a
            // wrong returned interval/source; they are not a universal proof or
            // a knot/control-identity oracle for every SDK curve family.
            for (int i = 0; i <= 4; i++)
            {
                cancel(); double fraction = i / 4d;
                var originalPoint = source.GetPointAtParameter(source.StartParam + fraction * (source.EndParam - source.StartParam));
                var derivedNearest = spline.GetClosestPointTo(originalPoint, false); cancel();
                var derivedPoint = spline.GetPointAtParameter(spline.StartParam + fraction * (spline.EndParam - spline.StartParam));
                var originalNearest = source.GetClosestPointTo(derivedPoint, false); cancel();
                Require(Finite(originalPoint) && Finite(derivedNearest) && Finite(derivedPoint) && Finite(originalNearest) &&
                    (originalPoint - derivedNearest).Length <= resolution && (derivedPoint - originalNearest).Length <= resolution,
                    "Native Spline representation differs from the actual Arc/Ellipse base curve.");
            }
            try { CadContourBudget344.VerifySource(source, before, cancel); }
            catch (DomainException error) when (error.Error.code == "NATIVE_GEOMETRY_MISMATCH")
            { throw new DomainException("NATIVE_CURVE_SOURCE_CHANGED", "Native representation derivation changed the source's observed geometry, identity, display or root data.", false, error.Error); }
            var afterOwned = Owned();
            if (owned.HasValue)
            {
                Require(afterOwned.HasValue, "Source-owned data was fully observable before conversion but its preservation can no longer be verified.", "NATIVE_CURVE_SOURCE_CHANGED");
                Require(JsonSerializer.Serialize(owned.Value, Wire.Json) == JsonSerializer.Serialize(afterOwned.Value, Wire.Json),
                    "Native representation derivation changed the source's observed owned data.", "NATIVE_CURVE_SOURCE_CHANGED");
            }
            cancel(); return spline;
        }
        catch { spline.Dispose(); throw; }
    }
}
