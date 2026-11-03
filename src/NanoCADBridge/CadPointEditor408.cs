using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using NanoCAD.Contracts;
using Teigha.DatabaseServices;
using Teigha.Geometry;

namespace NanoCADBridge;

// A Point has a full native position and display frame, but is not a Curve.
// Native transforms/copies retain that representation rather than constructing
// a visual marker or routing Point through curve shape analysis.
internal static class CadPointEditor408
{
    private sealed record Snapshot(JsonElement Geometry, JsonElement Metadata, JsonElement Display, JsonElement? Owned);
    private static void Need([DoesNotReturnIf(false)] bool condition, string message, string code = "NATIVE_POINT_MISMATCH")
    { if (!condition) throw new DomainException(code, message); }
    private static bool Exact(object? first, object? second) => JsonSerializer.Serialize(first, Wire.Json) == JsonSerializer.Serialize(second, Wire.Json);
    private static string? Id(ObjectId id) => id.IsNull ? null : Validation.NormalizeHandle(id.Handle.ToString());
    private static bool Near(double first, double second) => Math.Abs(first - second) <= Math.Max(1e-7, 16 * 2.2204460492503131e-16 * Math.Max(Math.Abs(first), Math.Abs(second)));
    private static bool SamePosition(Point3d first, Point3d second) => Near(first.X, second.X) && Near(first.Y, second.Y) && Near(first.Z, second.Z);
    private static Point3d Native(Point3 point) => new(point.X, point.Y, point.Z);
    internal static bool Ordinary(EditSpec edit) => edit is MoveSpec or CopySpec or RotateSpec or ScaleSpec or Mirror2dSpec or
        PropertiesSpec or EraseSpec or PointPositionEdit408 or CopyTransform2d407 or PolarRepeat2d407;

    internal static object Geometry(DBPoint point)
    {
        CheckGeometry(point, () => { });
        return new
        {
            coordinate_frame = "WCS", position = new[] { point.Position.X, point.Position.Y, point.Position.Z },
            normal = new[] { point.Normal.X, point.Normal.Y, point.Normal.Z }, thickness = point.Thickness, ecs_rotation = point.EcsRotation
        };
    }
    internal static void CheckGeometry(Entity entity, Action cancel)
    {
        cancel(); Need(entity.GetType() == typeof(DBPoint) && entity is DBPoint, "Use an exact native Point.", "UNSUPPORTED_ENTITY_TYPE");
        var point = (DBPoint)entity;
        Need(new[] { point.Position.X, point.Position.Y, point.Position.Z, point.Normal.X, point.Normal.Y, point.Normal.Z,
            point.Thickness, point.EcsRotation }.All(double.IsFinite) && point.Normal.Length > 0 && double.IsFinite(point.Normal.Length),
            "Native Point position, normal, thickness and ECS rotation must be finite, with a nonzero normal.", "INVALID_GEOMETRY");
    }
    internal static void CheckSource(Entity source, IReadOnlyCollection<EditSpec> edits, Database database, Transaction transaction, Action cancel)
    {
        Need(edits.Count > 0 && edits.All(Ordinary), "Native Point accepts ordinary placement, copy, coordinate/property edits and erase; other operations need their own entity representation.", "UNSUPPORTED_ENTITY_TYPE");
        CheckGeometry(source, cancel);
        Need(!source.ObjectId.IsNull && source.Database == database && !source.ObjectId.IsErased && !source.ObjectId.IsEffectivelyErased,
            "Point source must be a live native entity in the requested document.", "OBJECT_NOT_FOUND");
        Need(!source.OwnerId.IsNull && transaction.GetObject(source.OwnerId, OpenMode.ForRead) is BlockTableRecord { IsLayout: true },
            "Nested or definition-owned Points require their owning definition route.", "OUT_OF_SCOPE");
        // This shared ownership guard observes Fields, reactors and native model
        // history references; it accepts Entity and does not assume Curve shape.
        try { CadIndependentCurve.Require(source, database, transaction, cancel); }
        catch (DomainException exception) when (exception.Error.code == "UNSUPPORTED_CURVE_DEPENDENCIES")
        { throw new DomainException("UNSUPPORTED_POINT_DEPENDENCIES", "Point belongs to native dependencies or model history; use its owning adapter.", false, exception.Error); }
        Need(source.Annotative != AnnotativeStates.True, "Point annotation contexts require their owning adapter.", "UNSUPPORTED_POINT_DEPENDENCIES");
        _ = Capture((DBPoint)source, transaction, cancel);
    }
    internal static DBPoint Detached(DBPoint source)
    {
        var clone = source.Clone() as DBPoint;
        Need(clone != null && clone.GetType() == typeof(DBPoint) && clone.ObjectId.IsNull && clone.OwnerId.IsNull &&
            clone.UnmanagedObject != IntPtr.Zero && clone.UnmanagedObject != source.UnmanagedObject,
            "Native Point clone must prove a detached, distinct exact root; an unsafe alias is not adopted or disposed.", "UNSAFE_NATIVE_CLONE");
        return clone;
    }
    private static JsonElement XData(DBPoint point, Action cancel)
    {
        using var data = point.XData; var budget = new ModelInventoryBudget(cancel);
        return JsonSerializer.SerializeToElement(data == null ? null : NativeOwnedMetadata.Buffer(data, cancel, budget.Add), Wire.Json);
    }
    private static Snapshot Capture(DBPoint point, Transaction transaction, Action cancel)
    {
        CheckGeometry(point, cancel); using var reactors = point.GetPersistentReactorIds();
        var links = new List<string?>(); foreach (ObjectId id in reactors) { cancel(); links.Add(Id(id)); }
        var metadata = Wire.Element(new { handle = Id(point.ObjectId), owner_handle = Id(point.OwnerId), runtime_type = point.GetType().FullName,
            point.HasFields, extension_handle = Id(point.ExtensionDictionary), xdata = XData(point, cancel), reactors = links });
        var display = Wire.Element(new { layer_handle = Id(point.LayerId), properties = EntityAdapter.StoredProperties(point) });
        JsonElement? owned = point.ObjectId.IsNull ? null : CadAnnotationMirrorEditor340.Metadata(point, transaction, cancel);
        var snapshot = new Snapshot(Wire.Element(Geometry(point)), metadata, display, owned);
        SerializedCapacity357.Check(snapshot, NativeCurveRead342.SnapshotByteBudget, cancel); return snapshot;
    }
    private static JsonElement WithoutXData(JsonElement metadata) => Wire.Element(metadata.EnumerateObject()
        .Where(property => property.Name != "xdata").ToDictionary(property => property.Name, property => property.Value.Clone(), StringComparer.Ordinal));
    private static bool GeometryMatches(JsonElement first, JsonElement second)
    {
        foreach (string field in new[] { "position", "normal" })
        {
            var before = first.GetProperty(field); var after = second.GetProperty(field);
            for (int index = 0; index < 3; index++) if (!Near(before[index].GetDouble(), after[index].GetDouble())) return false;
        }
        return Near(first.GetProperty("thickness").GetDouble(), second.GetProperty("thickness").GetDouble()) &&
            Near(first.GetProperty("ecs_rotation").GetDouble(), second.GetProperty("ecs_rotation").GetDouble());
    }
    internal static DBPoint Create(PointSpec408 spec)
    {
        var point = new DBPoint(Native(spec.Position)) { Normal = Vector3d.ZAxis, Thickness = 0, EcsRotation = 0 };
        try { CheckGeometry(point, () => { }); return point; } catch { point.Dispose(); throw; }
    }
    internal static void VerifyCreated(DBPoint point, PointSpec408 spec, Action cancel)
    {
        CheckGeometry(point, cancel);
        var expected = Native(spec.Position);
        if (spec.Workplane != null) expected = expected.TransformBy(DrawingWorkplaneNative.Matrix(spec.Workplane));
        Need(SamePosition(point.Position, expected), "Created native Point position differs from the requested complete WCS position.");
    }
    internal static void SetPosition(Entity entity, PointPositionEdit408 edit, Transaction transaction, Action cancel)
    {
        CheckGeometry(entity, cancel); var point = (DBPoint)entity; var before = Capture(point, transaction, cancel);
        Need(SamePosition(point.Position, Native(PointCoordinates408.World(edit.ExpectedPosition, edit.Workplane))),
            "Stored Point position differs from expected_position in the declared coordinate frame.", "GEOMETRY_CHANGED");
        var target = Native(PointCoordinates408.World(edit.Position, edit.Workplane));
        cancel(); point.Position = target; if (!point.ObjectId.IsNull) point.RecordGraphicsModified(true);
        var after = Capture(point, transaction, cancel);
        Need(SamePosition(point.Position, target) && Exact(before.Metadata, after.Metadata) && Exact(before.Display, after.Display) && Exact(before.Owned, after.Owned) &&
            Exact(before.Geometry.GetProperty("normal"), after.Geometry.GetProperty("normal")) &&
            Exact(before.Geometry.GetProperty("thickness"), after.Geometry.GetProperty("thickness")) &&
            Exact(before.Geometry.GetProperty("ecs_rotation"), after.Geometry.GetProperty("ecs_rotation")),
            "Point coordinate edit changed its other native geometry, identity, display or owned data.");
    }
    internal static void Transform(DBPoint point, EditSpec edit, Transaction transaction, Action cancel)
    {
        var before = Capture(point, transaction, cancel); var matrix = CadModifier.Matrix(edit);
        var expectedPosition = point.Position.TransformBy(matrix); JsonElement expectedGeometry, expectedData;
        using (var expected = Detached(point)) { cancel(); expected.TransformBy(matrix); CheckGeometry(expected, cancel); expectedGeometry = Wire.Element(Geometry(expected)); }
        using (var dataCopy = Detached(point)) { cancel(); dataCopy.XDataTransformBy(matrix); expectedData = XData(dataCopy, cancel); }
        Need(Exact(before, Capture(point, transaction, cancel)), "Point transform preparation changed the source.", "NATIVE_POINT_SOURCE_CHANGED");
        cancel(); point.TransformBy(matrix); var actualData = XData(point, cancel);
        if (!Exact(actualData, expectedData) && Exact(actualData, before.Metadata.GetProperty("xdata"))) point.XDataTransformBy(matrix);
        if (!point.ObjectId.IsNull) point.RecordGraphicsModified(true);
        var after = Capture(point, transaction, cancel);
        Need(SamePosition(point.Position, expectedPosition) && GeometryMatches(expectedGeometry, after.Geometry) &&
            Exact(WithoutXData(before.Metadata), WithoutXData(after.Metadata)) && Exact(expectedData, after.Metadata.GetProperty("xdata")) &&
            Exact(before.Display, after.Display) && Exact(before.Owned, after.Owned),
            "Native Point transform changed retained identity/display/owned data or differs from its complete native geometry and requested position.");
    }
    internal static DBPoint Copy(DBPoint source, EditSpec edit, ObjectId owner, Transaction transaction, Action cancel)
    {
        var before = Capture(source, transaction, cancel);
        using var ids = new ObjectIdCollection(new[] { source.ObjectId }); using var mapping = new IdMapping();
        source.Database.DeepCloneObjects(ids, owner, mapping, false); cancel();
        Need(mapping.Contains(source.ObjectId), "Native Point DeepClone returned no requested root mapping.", "UNSAFE_NATIVE_CLONE");
        var root = mapping.Lookup(source.ObjectId); var id = root.Value;
        Need(root.IsCloned && root.Key == source.ObjectId && id != source.ObjectId && !id.IsNull && id.IsValid && !id.IsErased && !id.IsEffectivelyErased,
            "Native Point copy must return a distinct live mapped root.", "UNSAFE_NATIVE_CLONE");
        var copy = transaction.GetObject(id, OpenMode.ForWrite) as DBPoint;
        Need(copy != null && copy.GetType() == typeof(DBPoint) && copy.Database == source.Database && copy.OwnerId == owner &&
            copy.UnmanagedObject != IntPtr.Zero && copy.UnmanagedObject != source.UnmanagedObject,
            "Native Point copy has a different type/owner or aliases its source.", "UNSAFE_NATIVE_CLONE");
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (IdPair pair in mapping)
        {
            cancel(); if (!pair.Key.IsNull && !pair.Value.IsNull) map[Id(pair.Key)!] = Id(pair.Value)!;
            if (pair.IsCloned && pair.Value != id && transaction.GetObject(pair.Value, OpenMode.ForRead) is Entity child)
                Need(child.OwnerId != owner, "Native Point copy produced an additional unreported top-level entity.");
        }
        var observed = Capture(copy, transaction, cancel);
        Need(GeometryMatches(before.Geometry, observed.Geometry) && Exact(before.Display, observed.Display) &&
            Exact(CadAnnotationMirrorEditor340.Remap(before.Metadata, map, cancel), observed.Metadata) &&
            before.Owned.HasValue == observed.Owned.HasValue && (!before.Owned.HasValue ||
                Exact(CadAnnotationMirrorEditor340.Remap(before.Owned.Value, map, cancel), observed.Owned!.Value)),
            "Native Point copy did not retain its complete geometry/display and mapped ordinary owned data.");
        Transform(copy, edit, transaction, cancel);
        Need(Exact(before, Capture(source, transaction, cancel)), "Native Point copy changed the retained original.", "NATIVE_POINT_SOURCE_CHANGED");
        // Resident clones and their owned records belong to the transaction.
        return copy;
    }
}
