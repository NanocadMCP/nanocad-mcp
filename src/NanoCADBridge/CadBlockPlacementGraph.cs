using System.Text.Json;
using NanoCAD.Contracts;
using Teigha.DatabaseServices;
using Teigha.Geometry;

namespace NanoCADBridge;

// An existing placement keeps its shared definition. It does not need the
// restricted template needed to manufacture a new insertion and its attributes.
internal static class CadBlockPlacementGraph
{
    internal sealed record Leaf(ObjectId Id, Matrix3d ToRoot);
    internal sealed record State(ObjectId Id, JsonElement Value);
    internal sealed record Resolved(Leaf[] Leaves, State[] States, ObjectId[] Definitions, int Vertices)
    {
        public void Verify(Transaction tr, Action check)
        {
            foreach (var state in States)
            {
                check();
                if (state.Id.IsErased || state.Id.IsEffectivelyErased ||
                    !BlockMoveGeometry.SameProperties(state.Value, Snapshot(tr.GetObject(state.Id, OpenMode.ForRead), check)))
                    throw new DomainException("BLOCK_DEFINITION_CHANGED", "Placement changed the identity, ownership or stored data of a shared static block definition.");
            }
        }
    }
    private static void Require(bool condition, string message)
    { if (!condition) throw new DomainException("UNSUPPORTED_BLOCK_CONTENT", message); }
    private static string? Id(ObjectId id) => id.IsNull ? null : id.Handle.ToString();
    private static void Independent(DBObject item)
    {
        Require(!item.HasFields&&!CadDrawingGroups431.HasNonOrganizationalReactors(item), "Existing block placement excludes fields and geometric persistent associations in its definition graph; native selection Groups are retained.");
    }
    private static BlockTableRecord Definition(ObjectId id, Database db, Transaction tr)
    {
        Require(!id.IsNull && id.IsValid && !id.IsErased && !id.IsEffectivelyErased, "Shared block definition is unavailable.");
        Require(tr.GetObject(id, OpenMode.ForRead) is BlockTableRecord, "Expected a native shared block definition.");
        var block = (BlockTableRecord)tr.GetObject(id, OpenMode.ForRead);
        Require(block.GetType() == typeof(BlockTableRecord) && block.OwnerId == db.BlockTableId && !block.IsAnonymous && !block.IsDynamicBlock &&
            !block.IsLayout && !block.IsDependent && !block.IsFromExternalReference && !block.IsFromOverlayReference,
            "Existing placement requires ordinary static local definitions; dynamic, anonymous and external-reference graphs remain protected.");
        Independent(block); return block;
    }
    private static void Symbols(Entity entity, Database db, Transaction tr)
    {
        Require(!entity.LayerId.IsNull && tr.GetObject(entity.LayerId, OpenMode.ForRead) is LayerTableRecord layer &&
            layer.GetType() == typeof(LayerTableRecord) && layer.OwnerId == db.LayerTableId && !layer.IsDependent,
            "Definition members require existing native local layers.");
        Require(!entity.LinetypeId.IsNull && tr.GetObject(entity.LinetypeId, OpenMode.ForRead) is LinetypeTableRecord linetype &&
            linetype.GetType() == typeof(LinetypeTableRecord) && linetype.OwnerId == db.LinetypeTableId && !linetype.IsDependent,
            "Definition members require existing native local linetypes.");
        if (entity is DBText text)
        {
            Require(tr.GetObject(text.TextStyleId, OpenMode.ForRead) is TextStyleTableRecord style && style.GetType() == typeof(TextStyleTableRecord) &&
                style.OwnerId == db.TextStyleTableId && !style.IsDependent && !style.IsShapeFile && !style.IsVertical,
                "Definition text requires a local horizontal non-shape text style.");
        }
        else if (entity is MText mtext)
        {
            Require(tr.GetObject(mtext.TextStyleId, OpenMode.ForRead) is TextStyleTableRecord style && style.GetType() == typeof(TextStyleTableRecord) &&
                style.OwnerId == db.TextStyleTableId && !style.IsDependent && !style.IsShapeFile && !style.IsVertical,
                "Definition MText requires a local horizontal non-shape text style.");
        }
    }
    private static bool Primitive(Entity entity)
    {
        var type = entity.GetType();
        return type == typeof(Line) || type == typeof(Circle) || type == typeof(Arc) || type == typeof(Ellipse) ||
            type == typeof(Spline) || type == typeof(Polyline) || type == typeof(DBText) || type == typeof(MText) ||
            type == typeof(AttributeDefinition) || type == typeof(AttributeReference) || type == typeof(Hatch);
    }
    private static void LeafScope(Entity entity, Action check)
    {
        Require(Primitive(entity), "Existing placement supports native planar curves, text, native single/multiline attributes and MText leaves; other definition members require their owning adapter.");
        Require(entity.Annotative != AnnotativeStates.True, "Annotative definition leaves require their owning context adapter.");
        if(entity.GetType()==typeof(Hatch))CadBlockTemplate391.RequireHatch((Hatch)entity,check);
        else if(entity is AttributeReference owned)CadBlockMover.RequireAttributeGeometry(owned);
        else CadModifier.CheckGeometry(entity, check);
    }
    internal static void CheckLeaf(Entity entity, Database db, Transaction tr, Action check)
    { check(); Independent(entity); Symbols(entity, db, tr); LeafScope(entity, check);if(entity is DBText text)NativeBlockMultiline442.Check(text,db,tr,check); }
    private static string[] Members(IEnumerable<ObjectId> ids, Action check)
    {
        var values = new List<string>(); long bytes = 0;
        foreach (var id in ids)
        {
            check(); if (id.IsErased || id.IsEffectivelyErased) continue;
            var handle = Id(id)!; bytes += handle.Length + 3;
            if (bytes > Wire.PayloadBudget - 8192) throw new DomainException("ENTITY_TOO_LARGE", "Shared definition identity data exceeds the actual bounded byte budget.");
            values.Add(handle);
        }
        return values.ToArray();
    }
    private static JsonElement Snapshot(DBObject item, Action check)
    {
        check();
        object content;
        if (item is BlockTableRecord definition)
        {
            var members = Members(definition.Cast<ObjectId>(), check);
            content = new { definition.Name, origin = new[] { definition.Origin.X, definition.Origin.Y, definition.Origin.Z },
                units = (int)definition.Units, scaling = (int)definition.BlockScaling, definition.Explodable, members };
        }
        else
        {
            var entity = (Entity)item;
            var summary = EntityAdapter.Summary(entity, ObjectId.Null, "00000000-0000-0000-0000-000000000000", "00000000-0000-0000-0000-000000000000");
            // Individual geometry is already bounded by CheckGeometry. Check
            // cancellation around the existing read-only native geometry reader.
            var detail = EntityAdapter.Detail(entity, summary, CancellationToken.None);
            content = new { detail, hatch = entity.GetType()==typeof(Hatch)?CadBlockTemplate391.StoredHatch((Hatch)entity,check):null, properties = CadBlockMover.PreservedProperties(entity),
                multiline=entity is DBText text?NativeBlockMultiline442.Capture(text,check):null,
                attributes = entity is BlockReference reference ? Members(reference.AttributeCollection.Cast<ObjectId>(), check) : null,
                tag = entity is AttributeDefinition a ? a.Tag : entity is AttributeReference r ? r.Tag : null,
                invisible = entity is AttributeDefinition ad ? (bool?)ad.Invisible : entity is AttributeReference ar ? ar.Invisible : null,
                constant = entity is AttributeDefinition cd ? (bool?)cd.Constant : entity is AttributeReference cr ? cr.IsConstant : null,
                attribute_definition = entity is AttributeDefinition template ? new { template.Prompt, template.Preset, template.Verifiable, template.LockPositionInBlock } : null,
                lock_attribute = entity is AttributeReference occurrence ? (bool?)occurrence.LockPositionInBlock : null };
        }
        check(); return Wire.Element(new { id = Id(item.ObjectId), owner = Id(item.OwnerId), type = item.GetType().FullName,
            extension = Id(item.ExtensionDictionary), content });
    }
    public static Resolved Resolve(BlockReference root, Database db, Transaction tr, Action check)
    {
        var leaves = new List<Leaf>(); var states = new Dictionary<ObjectId, State>(); var definitions = new HashSet<ObjectId>();
        var path = new HashSet<ObjectId>(); var work = new Stack<(ObjectId Id, Matrix3d ToRoot, bool Exit)>();
        long bytes = 0; int vertices = 0;
        void Charge(int count)
        {
            bytes += count;
            if (bytes > Wire.PayloadBudget - 8192) throw new DomainException("ENTITY_TOO_LARGE", "Existing block placement graph exceeds the actual bounded snapshot-byte budget.");
        }
        void Remember(DBObject item)
        {
            if (states.ContainsKey(item.ObjectId)) return;
            var value = Snapshot(item, check); Charge(JsonSerializer.SerializeToUtf8Bytes(value, Wire.Json).Length);
            states.Add(item.ObjectId, new(item.ObjectId, value));
        }
        void AddLeaf(Entity entity, Matrix3d transform)
        {
            CheckLeaf(entity, db, tr, check); Remember(entity);
            Charge(160); leaves.Add(new(entity.ObjectId, transform));
            vertices += entity is Polyline p ? p.NumberOfVertices : entity is Spline s ? s.NumControlPoints : 0;
        }
        work.Push((root.BlockTableRecord, Matrix3d.Identity, false));
        while (work.Count != 0)
        {
            check(); var frame = work.Pop();
            if (frame.Exit) { path.Remove(frame.Id); continue; }
            Require(path.Add(frame.Id), "Cyclic static block definitions cannot be placed through this adapter.");
            var definition = Definition(frame.Id, db, tr); definitions.Add(frame.Id); Remember(definition);
            work.Push((frame.Id, frame.ToRoot, true));
            foreach (ObjectId id in definition)
            {
                check(); if (id.IsErased || id.IsEffectivelyErased) continue;
                Require(tr.GetObject(id, OpenMode.ForRead) is Entity, "Definition has a non-entity member.");
                var entity = (Entity)tr.GetObject(id, OpenMode.ForRead);
                Require(entity.OwnerId == definition.ObjectId, "Definition member owner does not match its native definition.");
                if (entity.GetType() != typeof(BlockReference) && entity.GetType()!=typeof(MInsertBlock)) { AddLeaf(entity, frame.ToRoot); continue; }
                var nested = (BlockReference)entity; Independent(nested); Symbols(nested, db, tr); CadBlockMover.CheckRoot(nested); Remember(nested);
                foreach (ObjectId attributeId in nested.AttributeCollection)
                {
                    check(); Require(!attributeId.IsErased && !attributeId.IsEffectivelyErased && tr.GetObject(attributeId, OpenMode.ForRead) is AttributeReference,
                        "Nested insertion has an unavailable attribute.");
                    var attribute = (AttributeReference)tr.GetObject(attributeId, OpenMode.ForRead);
                    Require(attribute.OwnerId == nested.ObjectId, "Nested attribute owner does not match its insertion.");
                    // Attribute coordinates belong to the insertion's parent definition.
                    AddLeaf(attribute, frame.ToRoot);
                }
                Charge(160); work.Push((nested.BlockTableRecord, frame.ToRoot * nested.BlockTransform, false));
            }
        }
        Require(leaves.Count != 0, "Existing placement requires at least one supported native leaf.");
        return new(leaves.ToArray(), states.Values.ToArray(), definitions.ToArray(), vertices);
    }
}
