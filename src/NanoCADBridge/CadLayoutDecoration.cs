using System.Text.Json;
using NanoCAD.Contracts;
using Teigha.DatabaseServices;

namespace NanoCADBridge;

internal static class CadLayoutDecoration
{
    internal sealed record Attribute(ObjectId Id, ObjectId Dictionary, string Tag, JsonElement State);
    internal sealed record Item(ObjectId Id, ObjectId Dictionary, JsonElement State, Attribute[] Attributes);
    internal sealed record Prepared(Entity Entity, AttributeReference[] Attributes);

    private static void Need(bool ok, string message)
    {
        if (!ok) throw new DomainException("LAYOUT_DECORATION_UNAVAILABLE", message);
    }

    private static void Independent(Entity entity, Transaction tr, string role)
    {
        using var xdata = entity.XData;
        using var reactors = entity.GetPersistentReactorIds();
        var dictionary = entity.ExtensionDictionary.IsNull ? null : tr.GetObject(entity.ExtensionDictionary, OpenMode.ForRead) as DBDictionary;
        bool emptyDictionary = entity.ExtensionDictionary.IsNull || dictionary is { Count: 0 } && dictionary.OwnerId == entity.ObjectId;
        if (entity.HasFields || entity.Annotative == AnnotativeStates.True || xdata != null || !emptyDictionary || reactors.Count > 0)
            throw new DomainException("LAYOUT_DECORATION_UNAVAILABLE", "The sheet item has attached data requiring its owning operation.", false,
                new
                {
                    role,
                    handle = entity.Handle.ToString(),
                    owner = entity.OwnerId.Handle.ToString(),
                    type = entity.GetType().Name,
                    fields = entity.HasFields,
                    annotative = entity.Annotative.ToString(),
                    extension_count = dictionary?.Count,
                    applications = xdata?.AsArray().Where(v => v.TypeCode == 1001).Select(v => v.Value?.ToString()).ToArray(),
                    reactors = reactors.Cast<ObjectId>().Select(id => id.IsNull ? null : id.Handle.ToString()).ToArray()
                });
    }

    private static Item Observe(ObjectId id, ObjectId owner, string role, Transaction tr, Action cancel)
    {
        cancel();
        var entity = tr.GetObject(id, OpenMode.ForRead) as Entity;
        if (entity == null || entity.OwnerId != owner || !id.IsValid || id.IsErased || id.IsEffectivelyErased)
            throw new DomainException("OUT_OF_SCOPE", "Every selected decoration must belong directly to the base paper layout.");
        var type = entity.GetType();
        Need(type == typeof(Line) || type == typeof(Polyline) || type == typeof(Arc) || type == typeof(Circle) ||
            type == typeof(DBText) || type == typeof(MText) || type == typeof(BlockReference),
            "Select frame curves, ordinary text or a static local title-block insertion.");
        Need(CadLibraryBlockDwg.ExactNativeClass(entity, type), "The selected decoration is a custom native object.");
        Independent(entity, tr, role);
        var attributes = new List<Attribute>();
        object geometry;
        if (entity is BlockReference block)
        {
            var definition = (BlockTableRecord)tr.GetObject(block.BlockTableRecord, OpenMode.ForRead);
            Need(!definition.IsLayout && !definition.IsAnonymous && !definition.IsDynamicBlock && !block.IsDynamicBlock &&
                definition.OwnerId == entity.Database.BlockTableId && !definition.IsDependent &&
                !definition.IsFromExternalReference && !definition.IsFromOverlayReference,
                "The title block must use a local static definition in this drawing.");
            foreach (ObjectId attrId in block.AttributeCollection)
            {
                cancel();
                var attr = tr.GetObject(attrId, OpenMode.ForRead) as AttributeReference;
                if (attr == null || attr.OwnerId != id || !CadLibraryBlockDwg.ExactNativeClass(attr, typeof(AttributeReference)))
                    throw new DomainException("LAYOUT_DECORATION_UNAVAILABLE", "A selected title-block attribute has an unexpected native class or owner.");
                Independent(attr, tr, role + "_attribute");
                attributes.Add(new(attrId, attr.ExtensionDictionary, attr.Tag, Wire.Element(CadBlockMover.AttributeSnapshot(attr, cancel))));
            }
            geometry = new { definition = block.BlockTableRecord.Handle.ToString(), matrix = block.BlockTransform.ToArray() };
        }
        else geometry = CadBlockDefinitionGeometry388.StoredGeometry(entity, cancel);
        return new(id, entity.ExtensionDictionary, Wire.Element(new
        {
            geometry,
            layer = entity.LayerId.Handle.ToString(),
            linetype = entity.LinetypeId.Handle.ToString(),
            properties = CadBlockMover.PreservedProperties(entity),
            entity.Visible
        }), attributes.ToArray());
    }

    internal static Item[] Resolve(string[]? handles, ObjectId owner, Database db, Transaction tr, Action cancel)
    {
        return (handles ?? []).Select(handle => Observe(CadStructureWriter.Find(db, handle), owner, "source", tr, cancel)).ToArray();
    }

    private static EntityRef Ref(EntityRef context, ObjectId id) => context with { handle = Validation.NormalizeHandle(id.Handle.ToString()) };

    internal static LayoutDecorationCopy[]? Project(Item[] items, EntityRef context)
    {
        if (items.Length == 0) return null;
        var future = context with { handle = "FFFFFFFFFFFFFFFF" };
        return items.Select(item => new LayoutDecorationCopy(Ref(context, item.Id), future,
            item.Attributes.Select(a => new LayoutDecorationAttribute(Ref(context, a.Id), future, a.Tag)).ToArray())).ToArray();
    }

    internal static Prepared[] Prepare(Item[] items, Transaction tr, List<DBObject> owned, Action cancel)
    {
        return items.Select(item =>
        {
            cancel();
            var source = (Entity)tr.GetObject(item.Id, OpenMode.ForRead);
            Prepared copy;
            if (source is BlockReference block)
            {
                // This is the same native occurrence and attribute preparation used
                // by the static block adapters. The definition stays in this database.
                var detached = CadLibraryBlockDwg.Detached(block, tr, cancel);
                copy = new(detached.Reference, detached.Attributes);
            }
            else copy = new((Entity)source.Clone(), []);
            owned.Add(copy.Entity);
            owned.AddRange(copy.Attributes);
            Need(copy.Entity.ObjectId.IsNull && copy.Entity.OwnerId.IsNull && copy.Entity.GetType() == source.GetType() &&
                copy.Attributes.All(a => a.ObjectId.IsNull && a.OwnerId.IsNull), "A selected sheet copy is not detached.");
            return copy;
        }).ToArray();
    }

    private static void PreserveEmptyDictionary(ObjectId original, DBObject copy, Transaction tr)
    {
        if (original.IsNull) return;
        if (copy.ExtensionDictionary.IsNull) copy.CreateExtensionDictionary();
        Need(copy.ExtensionDictionary != original &&
            tr.GetObject(copy.ExtensionDictionary, OpenMode.ForRead) is DBDictionary dictionary &&
            dictionary.Count == 0 && dictionary.OwnerId == copy.ObjectId,
            "An empty decoration dictionary must belong to its new object.");
    }

    internal static LayoutDecorationCopy[]? Copy(Item[] items, Prepared[] prepared, ObjectId sourceOwner, BlockTableRecord target,
        EntityRef context, Transaction tr, Action cancel)
    {
        if (items.Length == 0) return null;
        var result = new List<LayoutDecorationCopy>();
        var roots = new HashSet<ObjectId>();
        for (int i = 0; i < items.Length; i++)
        {
            cancel();
            var item = items[i];
            var prototype = prepared[i];
            var root = target.AppendEntity(prototype.Entity);
            tr.AddNewlyCreatedDBObject(prototype.Entity, true);
            Need(!root.IsNull && root != item.Id && prototype.Entity.OwnerId == target.ObjectId && roots.Add(root),
                "A copied sheet root has an unexpected identity or paper-space owner.");
            PreserveEmptyDictionary(item.Dictionary, prototype.Entity, tr);
            var attributeIds = new List<ObjectId>();
            if (prototype.Entity is BlockReference block)
            {
                for (int j = 0; j < prototype.Attributes.Length; j++)
                {
                    cancel();
                    var attr = prototype.Attributes[j];
                    var id = block.AttributeCollection.AppendAttribute(attr);
                    tr.AddNewlyCreatedDBObject(attr, true);
                    Need(!id.IsNull && id != item.Attributes[j].Id && attr.OwnerId == root,
                        "A copied attribute has an unexpected identity or owner.");
                    PreserveEmptyDictionary(item.Attributes[j].Dictionary, attr, tr);
                    attributeIds.Add(id);
                }
            }
            var original = Observe(item.Id, sourceOwner, "retained_source", tr, cancel);
            var copy = Observe(root, target.ObjectId, "new_sheet", tr, cancel);
            Need(item.Dictionary == original.Dictionary && BlockMoveGeometry.SameProperties(item.State, original.State) &&
                BlockMoveGeometry.SameProperties(item.State, copy.State) && item.Attributes.Length == original.Attributes.Length &&
                attributeIds.SequenceEqual(copy.Attributes.Select(a => a.Id)),
                "Sheet geometry, display properties or attribute membership changed during copying.");
            var mappings = new List<LayoutDecorationAttribute>();
            for (int j = 0; j < item.Attributes.Length; j++)
            {
                var a = item.Attributes[j]; var before = original.Attributes[j]; var b = copy.Attributes[j];
                Need(before.Id == a.Id && before.Dictionary == a.Dictionary && a.Tag == b.Tag &&
                    BlockMoveGeometry.SameProperties(a.State, before.State) && BlockMoveGeometry.SameProperties(a.State, b.State),
                    "The title-block copy lost an owned attribute or its value.");
                mappings.Add(new(Ref(context, a.Id), Ref(context, b.Id), a.Tag));
            }
            result.Add(new(Ref(context, item.Id), Ref(context, root), mappings.ToArray()));
        }
        Need(target.Cast<ObjectId>().ToHashSet().SetEquals(roots), "The new paper space contains unselected objects.");
        return result.ToArray();
    }
}
