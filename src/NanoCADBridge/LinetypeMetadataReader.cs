using NanoCAD.Contracts;
using Teigha.DatabaseServices;

namespace NanoCADBridge;

// Caller owns the UI-thread ForRead transaction. No pattern generation/loading.
internal static class LinetypeMetadataReader
{
    public static LinetypeProperties Read(LinetypeTableRecord source, Transaction tr, Database db,
        Func<ObjectId, EntityRef> reference, Action check, Action countDependency)
    {
        T Get<T>(Func<T> getter) { check(); var value = getter(); check(); return value; }
        if (source.GetType() != typeof(LinetypeTableRecord) || Get(() => source.OwnerId) != Get(() => db.LinetypeTableId))
            throw new DomainException("OBJECT_READ_FAILED", "Expected an exact linetype in the active database table.");
        var identity = Get(() => reference(source.ObjectId));
        return ReadCore(source, identity, tr, db, reference, check, countDependency);
    }
    // Context validates lifetime only, and never becomes the new record identity.
    public static LinetypeProperties ReadDetached(LinetypeTableRecord source, EntityRef context, Transaction tr, Database db,
        Func<ObjectId, EntityRef> reference, Action check, Action countDependency)
    {
        check();
        if (source.GetType() != typeof(LinetypeTableRecord) || !source.ObjectId.IsNull)
            throw new DomainException("OBJECT_READ_FAILED", "Expected an exact detached linetype preview.");
        check(); return ReadCore(source, context, tr, db, reference, check, countDependency);
    }
    private static LinetypeProperties ReadCore(LinetypeTableRecord source, EntityRef identity, Transaction tr, Database db,
        Func<ObjectId, EntityRef> reference, Action check, Action countDependency)
    {
        T Get<T>(Func<T> getter) { check(); var value = getter(); check(); return value; }
        int count = LinetypeMetadata.Count(Get(() => source.NumDashes));
        var elements = new List<LinetypeElement>();var capacity=new SerializedCapacity357(cancel:check);
        for (int index = 0; index < count; index++)
        {
            var length = Get(() => source.DashLengthAt(index));
            var text = LinetypeMetadata.Text(Get(() => source.TextAt(index)), LinetypeMetadata.MaxElementText);
            var number = Get(() => source.ShapeNumberAt(index));
            var style = Style(Get(() => source.ShapeStyleAt(index)), tr, db, reference, check, countDependency);
            var offset = Get(() => source.ShapeOffsetAt(index));
            var element = new LinetypeElement(index, length, text, number, style, new[] { offset.X, offset.Y },
                Get(() => source.ShapeRotationAt(index)), Get(() => source.ShapeScaleAt(index)), Get(() => source.ShapeIsUcsOrientedAt(index)));capacity.Add(element);elements.Add(element);
        }
        check(); bool hasXdata;
        using (var data = source.XData)
        {
            check();
            hasXdata = data is not null && DimensionMetadataEnumeration.HasAny(data.GetEnumerator(), check);
        }
        var extension = Get(() => source.ExtensionDictionary);
        var result = LinetypeMetadata.Validate(identity, new(Get(() => source.IsDependent),
            LinetypeMetadata.Text(Get(() => source.AsciiDescription), LinetypeMetadata.MaxDescription), Get(() => source.PatternLength), count,
            Get(() => source.IsScaledToFit), elements.ToArray(), Get(() => !extension.IsNull), Get(() => source.HasFields), hasXdata));
        check();SerializedCapacity357.Check(result,cancel:check); return result;
    }
    private static LinetypeStyleReference Style(ObjectId id, Transaction tr, Database db, Func<ObjectId, EntityRef> reference,
        Action check, Action countDependency)
    {
        T Get<T>(Func<T> getter) { check(); var value = getter(); check(); return value; }
        LinetypeStyleReference Unavailable(string reason) => new("text_style", "unavailable", null, null, reason);
        if (Get(() => id.IsNull)) return new("text_style", "null", null, null, null);
        if (!Get(() => id.IsValid)) return Unavailable("invalid_id");
        if (Get(() => id.IsErased)) return Unavailable("erased");
        try
        {
            countDependency();
            var target = Get(() => tr.GetObject(id, OpenMode.ForRead));
            if (target is not TextStyleTableRecord style || target.GetType() != typeof(TextStyleTableRecord)) return Unavailable("wrong_type");
            if (Get(() => target.OwnerId) != Get(() => db.TextStyleTableId)) return Unavailable("wrong_owner");
            return new("text_style", "resolved", Get(() => reference(id)), LinetypeMetadata.Text(Get(() => style.Name), LinetypeMetadata.MaxDescription), null);
        }
        catch (Teigha.Runtime.Exception) { check(); return Unavailable("read_failed"); }
    }
}
