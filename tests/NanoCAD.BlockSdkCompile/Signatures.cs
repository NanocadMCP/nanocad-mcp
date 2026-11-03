using Teigha.DatabaseServices;
using Teigha.Geometry;

namespace NanoCAD.BlockSdkCompile;

// Compile-only evidence of installed managed signatures. This project has no
// command, IExtensionApplication or entry point. Never NETLOAD or invoke it.
internal static class Signatures
{
    private static ObjectId CompileOnly(Database db, Transaction transaction, ObjectId layerId, ObjectId styleId)
    {
        var blocks = (BlockTable)transaction.GetObject(db.BlockTableId, OpenMode.ForWrite);
        using var definition = new BlockTableRecord
        {
            Name = "COMPILE_ONLY", Origin = new Point3d(10, 20, 0), Units = UnitsValue.Millimeters,
            BlockScaling = BlockScaling.Any, Explodable = true
        };
        ObjectId definitionId = blocks.Add(definition);
        transaction.AddNewlyCreatedDBObject(definition, true);
        using var line = new Line(new Point3d(10, 20, 0), new Point3d(20, 20, 0)) { LayerId = layerId };
        definition.AppendEntity(line);
        transaction.AddNewlyCreatedDBObject(line, true);
        using var template = new AttributeDefinition
        {
            Tag = "NAME", Prompt = "NAME", TextString = "Name", Position = new Point3d(12, 23, 0),
            Height = 2, TextStyleId = styleId, Normal = Vector3d.ZAxis, WidthFactor = 1, Oblique = 0,
            Rotation = 0, HorizontalMode = TextHorizontalMode.TextLeft, VerticalMode = TextVerticalMode.TextBase,
            Constant = false, Invisible = false, Verifiable = false, Preset = false, LockPositionInBlock = true,
            LayerId = layerId
        };
        definition.AppendEntity(template);
        transaction.AddNewlyCreatedDBObject(template, true);
        using var insertion = new BlockReference(new Point3d(100, 200, 0), definitionId)
        { LayerId = layerId, Normal = Vector3d.ZAxis, ScaleFactors = new Scale3d(2), Rotation = Math.PI / 2 };
        var model = (BlockTableRecord)transaction.GetObject(blocks[BlockTableRecord.ModelSpace], OpenMode.ForWrite);
        ObjectId insertId = model.AppendEntity(insertion);
        transaction.AddNewlyCreatedDBObject(insertion, true);
        using var attribute = new AttributeReference();
        // Installed assembly accepts reference-class parameters without the C#
        // ref keyword printed by the supplied CHM generator.
        attribute.SetAttributeFromBlock(template, insertion.BlockTransform);
        attribute.TextString = "Value";
        insertion.AttributeCollection.AppendAttribute(attribute);
        transaction.AddNewlyCreatedDBObject(attribute, true);
        return insertId;
    }
}
