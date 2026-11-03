using System.Text.Json;
using NanoCAD.Contracts;
using Teigha.DatabaseServices;

namespace NanoCADBridge;

// Literal occurrence text editing does not redefine, explode or rebuild a block.
internal static class CadBlockAttributeEditor
{
    internal sealed record Resolved(ObjectId Block, ObjectId Attribute, string Tag);
    internal static void RequireLiteral(AttributeReference attribute,string kind,Action check)
    {if(kind=="mtext")NativeAttributeMText377.RequireLiteral(attribute,check);else Plain(attribute,check);}
    internal static string Contents(AttributeReference attribute,Action check)
        =>attribute.IsMTextAttribute?NativeAttributeMText377.Contents(attribute,check):attribute.TextString;
    internal static void ApplyValue(AttributeReference attribute,SetAttributeEdit edit,Database db,Transaction tr,Action check)
        =>Edit(attribute,edit,db,tr,check);
    private static void Plain(AttributeReference attribute, Action check)
    {
        check();
        if (attribute.GetType() != typeof(AttributeReference) || attribute.IsMTextAttribute || attribute.IsConstant || attribute.HasFields || attribute.Annotative == AnnotativeStates.True ||
            attribute.HorizontalMode is not (TextHorizontalMode.TextLeft or TextHorizontalMode.TextCenter or TextHorizontalMode.TextRight) ||
            !Enum.IsDefined(typeof(TextVerticalMode), attribute.VerticalMode) || !double.IsFinite(attribute.Oblique) || Math.Abs(attribute.Oblique) >= Math.PI / 2 ||
            !StructureWriteValidation.IsPlainAttribute(attribute.TextString) || attribute.Tag.Length > 255)
            throw new DomainException("UNSUPPORTED_ATTRIBUTE_TYPE", "Literal edits require a bounded native variable single-line attribute with left/center/right alignment; fields, fit/aligned and multiline content remain unsupported.");
        using var reactors = attribute.GetPersistentReactorIds();
        if (reactors.Count != 0) throw new DomainException("UNSUPPORTED_ATTRIBUTE_TYPE", "Attribute text has persistent dependencies; edit it through its owning association.");
        CadModifier.CheckGeometry(attribute, check);
    }
    private static BlockReference Root(ObjectId id, Database db, Transaction tr, ObjectId space)
    {
        if (tr.GetObject(id, OpenMode.ForRead) is not BlockReference reference || reference.GetType() != typeof(BlockReference) || reference.OwnerId != space)
            throw new DomainException("OUT_OF_SCOPE", "Attribute edits address one exact native top-level insertion in the requested active Model or paper layout.");
        if (CadBlockSpace.RequireTopLevel(reference, db, tr).Space != space)
            throw new DomainException("OUT_OF_SCOPE", "Attribute insertion belongs to a different layout space.");
        var definition = tr.GetObject(reference.BlockTableRecord, OpenMode.ForRead);
        if (definition is not BlockTableRecord block || block.GetType() != typeof(BlockTableRecord) || block.OwnerId != db.BlockTableId ||
            reference.IsDynamicBlock || reference.Annotative == AnnotativeStates.True || block.IsDynamicBlock || block.IsAnonymous || block.IsLayout || block.IsDependent || block.IsFromExternalReference || block.IsFromOverlayReference)
            throw new DomainException("UNSUPPORTED_BLOCK_TYPE", "Literal occurrence edits require an ordinary static local insertion; definitions, dynamic/anonymous and external-reference blocks remain protected.");
        return reference;
    }
    private static ObjectId[] Attributes(BlockReference reference, Transaction tr, Action check)
    {
        var ids = new List<ObjectId>(); long bytes = 0;
        foreach (ObjectId id in reference.AttributeCollection)
        {
            check();
            if (id.IsNull || !id.IsValid || id.IsErased || id.IsEffectivelyErased || tr.GetObject(id, OpenMode.ForRead) is not AttributeReference attribute ||
                attribute.GetType() != typeof(AttributeReference) || attribute.OwnerId != reference.ObjectId)
                throw new DomainException("ATTRIBUTE_NOT_IN_BLOCK", "Insertion contains an erased, custom or foreign attribute member.");
            bytes += id.Handle.ToString().Length + 3;
            if (bytes > Wire.PayloadBudget - 8192) throw new DomainException("ENTITY_TOO_LARGE", "Owned attribute identities exceed the actual bounded byte budget.");
            ids.Add(id);
        }
        return ids.ToArray();
    }
    public static Resolved Resolve(SetAttributeEdit edit, Database db, Transaction tr, ObjectId space,
        Func<ObjectId, LayerWriteState> layerState, IDictionary<ObjectId, string> stagedText, Action check)
    {
        check(); var rootId = CadStructureWriter.Find(db, edit.BlockHandle);
        var reference = Root(rootId, db, tr, space);
        var target = CadStructureWriter.Find(db, edit.AttributeHandle);
        if (!Attributes(reference, tr, check).Contains(target))
            throw new DomainException("ATTRIBUTE_NOT_IN_BLOCK", "attribute_handle is not owned by the specified top-level insertion.");
        var attribute = (AttributeReference)tr.GetObject(target, OpenMode.ForRead); if(edit.ContentType == "mtext") NativeAttributeMText377.RequireLiteral(attribute,check); else Plain(attribute, check);
        layerState(reference.LayerId).RequireAttributeWrite(); layerState(attribute.LayerId).RequireAttributeWrite();
        NativeAttributeMText377.CheckReplacementFont(attribute, edit, tr, check);
        string actual = stagedText.TryGetValue(target, out var preceding) ? preceding : edit.ContentType == "mtext" ? NativeAttributeMText377.Contents(attribute,check) : attribute.TextString;
        if (actual != edit.ExpectedText) throw new DomainException("ATTRIBUTE_TEXT_MISMATCH", "Stored occurrence text differs from expected_text; refresh inspect_block.");
        stagedText[target] = edit.Text;
        return new(rootId, target, attribute.Tag);
    }
    private static JsonElement Snapshot(AttributeReference attribute)
    {
        bool defaultAlignment = attribute.HorizontalMode == TextHorizontalMode.TextLeft && attribute.VerticalMode == TextVerticalMode.TextBase;
        var anchor = defaultAlignment ? attribute.Position : attribute.AlignmentPoint;
        string? Handle(ObjectId id) => id.IsNull ? null : id.Handle.ToString();
        return Wire.Element(new
        {
            text = attribute.TextString, attribute.Tag,
            anchor = new[] { anchor.X, anchor.Y, anchor.Z },
            // Non-default Position is a glyph-derived point; alignment remains the fixed anchor.
            unused_alignment = defaultAlignment ? new[] { attribute.AlignmentPoint.X, attribute.AlignmentPoint.Y, attribute.AlignmentPoint.Z } : null,
            attribute.Height, attribute.Rotation, attribute.WidthFactor, attribute.Oblique, attribute.Thickness,
            horizontal = (int)attribute.HorizontalMode, vertical = (int)attribute.VerticalMode,
            normal = new[] { attribute.Normal.X, attribute.Normal.Y, attribute.Normal.Z },
            attribute.Invisible, attribute.IsConstant, attribute.IsMTextAttribute, attribute.LockPositionInBlock, attribute.IsMirroredInX, attribute.IsMirroredInY,
            style = Handle(attribute.TextStyleId), properties = CadBlockMover.PreservedProperties(attribute),
            extension = Handle(attribute.ExtensionDictionary)
        });
    }
    private static void Edit(AttributeReference attribute, SetAttributeEdit edit, Database db, Transaction tr, Action check)
    {
        if(edit.ContentType == "mtext") { NativeAttributeMText377.Apply(attribute,edit,tr,check); return; }
        Plain(attribute, check);
        if (attribute.TextString != edit.ExpectedText) throw new DomainException("ATTRIBUTE_TEXT_MISMATCH", "Attribute text changed before native literal replacement.");
        var id = attribute.ObjectId; var owner = attribute.OwnerId;
        var before = Snapshot(attribute);
        var expected = before.EnumerateObject().ToDictionary(p => p.Name, p => p.Value.Clone(), StringComparer.Ordinal);
        expected["text"] = Wire.Element(edit.Text);
        check(); attribute.TextString = edit.Text;
        if (attribute.HorizontalMode != TextHorizontalMode.TextLeft || attribute.VerticalMode != TextVerticalMode.TextBase)
        {
            // Documented native DBText alignment update preserves the existing
            // anchor while deriving the new glyph origin from the replacement.
            check(); attribute.AdjustAlignment(db);
        }
        Plain(attribute, check);
        if (attribute.ObjectId != id || attribute.OwnerId != owner || !BlockMoveGeometry.SameProperties(Wire.Element(expected), Snapshot(attribute)))
            throw new DomainException("ATTRIBUTE_TEXT_MISMATCH", "Native literal replacement changed attribute ownership, alignment anchor or unrequested formatting/properties.");
    }
    public static void Preview(AttributeReference detached, SetAttributeEdit edit, Database db, Transaction tr, Action check)
    {
        if (!detached.ObjectId.IsNull) throw new DomainException("API_EXCEPTION", "Attribute preview must be detached.");
        Edit(detached, edit, db, tr, check);
    }
    public static void Apply(SetAttributeEdit edit, ObjectId rootId, ObjectId attributeId, ObjectId space, Database db, Transaction tr, Action check)
    {
        var reference = Root(rootId, db, tr, space); var beforeIds = Attributes(reference, tr, check);
        if (!beforeIds.Contains(attributeId)) throw new DomainException("ATTRIBUTE_NOT_IN_BLOCK", "Attribute ownership changed before apply.");
        var definition = reference.BlockTableRecord; var owner = reference.OwnerId;
        var attribute = (AttributeReference)tr.GetObject(attributeId, OpenMode.ForWrite);
        Edit(attribute, edit, db, tr, check);
        if (reference.ObjectId != rootId || reference.OwnerId != owner || reference.BlockTableRecord != definition ||
            definition.IsErased || definition.IsEffectivelyErased || !beforeIds.SequenceEqual(Attributes(reference, tr, check)))
            throw new DomainException("ATTRIBUTE_TEXT_MISMATCH", "Literal replacement changed the block owner, definition or attribute collection.");
    }
}
