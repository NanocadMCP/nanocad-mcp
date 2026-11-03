using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using NanoCAD.Contracts;
using Teigha.DatabaseServices;
using Teigha.Geometry;

namespace NanoCADBridge;

// SDK Ray/Xline constructors/BasePoint/UnitDir and inherited transforms.
// Infinite geometry is observed directly, never sampled or given fake bounds.
// Resident deep-clone preservation follows the accepted native Point route.
internal static class CadConstructionCurve443
{
    private sealed record Snapshot(JsonElement Geometry, JsonElement Metadata, JsonElement Display, JsonElement? Owned);
    private static void Need([DoesNotReturnIf(false)] bool condition, string message, string code = "NATIVE_CONSTRUCTION_CURVE_MISMATCH")
    { if (!condition) throw new DomainException(code, message); }
    private static bool Exact(object? first, object? second) => JsonSerializer.Serialize(first, Wire.Json) == JsonSerializer.Serialize(second, Wire.Json);
    private static string? Id(ObjectId id) => id.IsNull ? null : Validation.NormalizeHandle(id.Handle.ToString());
    private static bool Near(double first, double second) => Math.Abs(first - second) <= Math.Max(1e-7, 16 * 2.2204460492503131e-16 * Math.Max(Math.Abs(first), Math.Abs(second)));
    private static bool SamePosition(Point3d first, Point3d second) => Near(first.X, second.X) && Near(first.Y, second.Y) && Near(first.Z, second.Z);
    private static Point3d Native(Point3 point) => new(point.X, point.Y, point.Z);
    internal static bool Is(Entity source)=>source.GetType()==typeof(Ray)||source.GetType()==typeof(Xline);
    internal static bool Ordinary(EditSpec edit)=>edit is MoveSpec or CopySpec or RotateSpec or PropertiesSpec or EraseSpec;
    static Point3d Base(Entity source)=>source is Ray ray?ray.BasePoint:((Xline)source).BasePoint;
    static Vector3d Direction(Entity source)=>source is Ray ray?ray.UnitDir:((Xline)source).UnitDir;
    internal static object Geometry(Entity source)=>source is Ray ray?CadImportedGeometry407.RayGeometry(ray):CadImportedGeometry407.XlineGeometry((Xline)source);
    internal static void CheckGeometry(Entity source,Action cancel)
    {
        cancel();Need(Is(source),"Use an exact native Ray or Xline.","UNSUPPORTED_ENTITY_TYPE");
        var p=Base(source);var d=Direction(source);
        Need(new[]{p.X,p.Y,p.Z,d.X,d.Y,d.Z}.All(double.IsFinite)&&Near(d.Length,1),"Native construction curve needs a finite base and unit direction.","INVALID_GEOMETRY");
    }
    internal static void CheckSource(Entity source, IReadOnlyCollection<EditSpec> edits, Database database, Transaction transaction, Action cancel)
    {
        Need(edits.Count > 0 && edits.All(Ordinary), "Ray/Xline support basic move, rotate, copy, erase and ordinary properties; other operations need their owning curve adapter.", "UNSUPPORTED_ENTITY_TYPE");
        CheckGeometry(source, cancel);
        Need(!source.ObjectId.IsNull && source.Database == database && !source.ObjectId.IsErased && !source.ObjectId.IsEffectivelyErased,
            "Construction-curve source must be a live native entity in the requested document.", "OBJECT_NOT_FOUND");
        Need(!source.OwnerId.IsNull && transaction.GetObject(source.OwnerId, OpenMode.ForRead) is BlockTableRecord { IsLayout: true },
            "Nested or definition-owned construction curves require their owning definition route.", "OUT_OF_SCOPE");
        // This shared ownership guard observes Fields, reactors and native model
        // history references; it accepts Entity and does not assume Curve shape.
        try { CadIndependentCurve.Require(source, database, transaction, cancel); }
        catch (DomainException exception) when (exception.Error.code == "UNSUPPORTED_CURVE_DEPENDENCIES")
        { throw new DomainException("UNSUPPORTED_CONSTRUCTION_CURVE_DEPENDENCIES", "Construction curve belongs to native dependencies or model history; use its owning adapter.", false, exception.Error); }
        Need(source.Annotative != AnnotativeStates.True, "Construction-curve annotation contexts require their owning adapter.", "UNSUPPORTED_CONSTRUCTION_CURVE_DEPENDENCIES");
        _ = Capture((Entity)source, transaction, cancel);
    }
    internal static Entity Detached(Entity source)
    {
        var candidate=source.Clone();var copy=candidate as Entity;
        if(copy==null||copy.GetType()!=source.GetType()||!copy.AutoDelete||!copy.ObjectId.IsNull||!copy.OwnerId.IsNull||copy.UnmanagedObject==IntPtr.Zero||copy.UnmanagedObject==source.UnmanagedObject)
        {
            if(candidate is Teigha.Runtime.DisposableWrapper rejected&&!ReferenceEquals(rejected,source))GC.SuppressFinalize(rejected);
            throw new DomainException("UNSAFE_NATIVE_CLONE","Construction-curve clone ownership/identity is unproved; it is not adopted or disposed.");
        }
        return copy;
    }
    private static JsonElement XData(Entity point, Action cancel)
    {
        using var data = point.XData; var budget = new ModelInventoryBudget(cancel);
        return JsonSerializer.SerializeToElement(data == null ? null : NativeOwnedMetadata.Buffer(data, cancel, budget.Add), Wire.Json);
    }
    private static Snapshot Capture(Entity point, Transaction transaction, Action cancel)
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
    private static bool GeometryMatches(JsonElement first,JsonElement second)
    {
        foreach(string name in new[]{"base_point","unit_direction"})
            for(int i=0;i<3;i++)if(!Near(first.GetProperty(name)[i].GetDouble(),second.GetProperty(name)[i].GetDouble()))return false;
        return first.GetProperty("extent").GetString()==second.GetProperty("extent").GetString();
    }
    internal static Entity Create(ConstructionCurve443 spec)
    {
        Entity source=spec.NativeType=="Ray"?new Ray():new Xline();
        try
        {
            var point=Native(spec.BasePoint);var direction=new Vector3d(spec.UnitDirection.X,spec.UnitDirection.Y,spec.UnitDirection.Z);
            if(source is Ray ray){ray.BasePoint=point;ray.UnitDir=direction;}
            else{var line=(Xline)source;line.BasePoint=point;line.UnitDir=direction;}
            CheckGeometry(source,()=>{});return source;
        }
        catch{source.Dispose();throw;}
    }
    internal static void VerifyCreated(Entity source,ConstructionCurve443 spec,Action cancel)
    {
        CheckGeometry(source,cancel);Need(SamePosition(Base(source),Native(spec.BasePoint))&&
            Direction(source).IsEqualTo(new Vector3d(spec.UnitDirection.X,spec.UnitDirection.Y,spec.UnitDirection.Z)),"Created Ray/Xline differs from its requested base/direction.");
    }
    internal static void Transform(Entity point, EditSpec edit, Transaction transaction, Action cancel)
    {
        var before = Capture(point, transaction, cancel); var matrix = CadModifier.Matrix(edit);
        var expectedPosition=Base(point).TransformBy(matrix);var expectedDirection=Direction(point).TransformBy(matrix).GetNormal();JsonElement expectedGeometry, expectedData;
        using (var expected = Detached(point)) { cancel(); expected.TransformBy(matrix); CheckGeometry(expected, cancel); expectedGeometry = Wire.Element(Geometry(expected)); }
        using (var dataCopy = Detached(point)) { cancel(); dataCopy.XDataTransformBy(matrix); expectedData = XData(dataCopy, cancel); }
        Need(Exact(before, Capture(point, transaction, cancel)), "Construction-curve transform preparation changed the source.", "NATIVE_CONSTRUCTION_CURVE_SOURCE_CHANGED");
        cancel(); point.TransformBy(matrix); var actualData = XData(point, cancel);
        if (!Exact(actualData, expectedData) && Exact(actualData, before.Metadata.GetProperty("xdata"))) point.XDataTransformBy(matrix);
        if (!point.ObjectId.IsNull) point.RecordGraphicsModified(true);
        var after = Capture(point, transaction, cancel);
        Need(SamePosition(Base(point), expectedPosition)&&Direction(point).IsEqualTo(expectedDirection) && GeometryMatches(expectedGeometry, after.Geometry) &&
            Exact(WithoutXData(before.Metadata), WithoutXData(after.Metadata)) && Exact(expectedData, after.Metadata.GetProperty("xdata")) &&
            Exact(before.Display, after.Display) && Exact(before.Owned, after.Owned),
            "Native construction curve transform changed retained identity/display/owned data or differs from its complete native geometry and requested position.");
    }
    internal static Entity Copy(Entity source, EditSpec edit, ObjectId owner, Transaction transaction, Action cancel)
    {
        var before = Capture(source, transaction, cancel);
        using var ids = new ObjectIdCollection(new[] { source.ObjectId }); using var mapping = new IdMapping();
        source.Database.DeepCloneObjects(ids, owner, mapping, false); cancel();
        Need(mapping.Contains(source.ObjectId), "Native construction curve DeepClone returned no requested root mapping.", "UNSAFE_NATIVE_CLONE");
        var root = mapping.Lookup(source.ObjectId); var id = root.Value;
        Need(root.IsCloned && root.Key == source.ObjectId && id != source.ObjectId && !id.IsNull && id.IsValid && !id.IsErased && !id.IsEffectivelyErased,
            "Native construction curve copy must return a distinct live mapped root.", "UNSAFE_NATIVE_CLONE");
        var copy = transaction.GetObject(id, OpenMode.ForWrite) as Entity;
        Need(copy != null && copy.GetType() == source.GetType() && copy.Database == source.Database && copy.OwnerId == owner &&
            copy.UnmanagedObject != IntPtr.Zero && copy.UnmanagedObject != source.UnmanagedObject,
            "Native construction curve copy has a different type/owner or aliases its source.", "UNSAFE_NATIVE_CLONE");
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (IdPair pair in mapping)
        {
            cancel(); if (!pair.Key.IsNull && !pair.Value.IsNull) map[Id(pair.Key)!] = Id(pair.Value)!;
            if (pair.IsCloned && pair.Value != id && transaction.GetObject(pair.Value, OpenMode.ForRead) is Entity child)
                Need(child.OwnerId != owner, "Native construction curve copy produced an additional unreported top-level entity.");
        }
        var observed = Capture(copy, transaction, cancel);
        Need(GeometryMatches(before.Geometry, observed.Geometry) && Exact(before.Display, observed.Display) &&
            Exact(CadAnnotationMirrorEditor340.Remap(before.Metadata, map, cancel), observed.Metadata) &&
            before.Owned.HasValue == observed.Owned.HasValue && (!before.Owned.HasValue ||
                Exact(CadAnnotationMirrorEditor340.Remap(before.Owned.Value, map, cancel), observed.Owned!.Value)),
            "Native construction curve copy did not retain its complete geometry/display and mapped ordinary owned data.");
        Transform(copy, edit, transaction, cancel);
        Need(Exact(before, Capture(source, transaction, cancel)), "Native construction curve copy changed the retained original.", "NATIVE_CONSTRUCTION_CURVE_SOURCE_CHANGED");
        // Resident clones and their owned records belong to the transaction.
        return copy;
    }
}
