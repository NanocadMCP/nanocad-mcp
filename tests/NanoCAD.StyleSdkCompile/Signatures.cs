using Teigha.DatabaseServices;

namespace NanoCAD.StyleSdkCompile;

// Compile-only signatures. No command/extension/entry point. Never NETLOAD or
// invoke this code; detached cloning and setter behavior still need native tests.
internal static class Signatures
{
    private static ObjectId Text(Database db, Transaction transaction, ObjectId sourceId)
    {
        var source = (TextStyleTableRecord)transaction.GetObject(sourceId, OpenMode.ForRead);
        using var copy = (TextStyleTableRecord)source.Clone();
        _ = copy.ObjectId.IsNull;
        _ = source.Annotative == AnnotativeStates.True;
        _ = (source.ExtensionDictionary, source.HasFields, source.IsDependent, source.OwnerId);
        _ = (source.FileName, source.BigFontFileName, source.FlagBits, source.IsVertical, source.IsShapeFile);
        var font = source.Font;
        _ = (font.TypeFace, font.Bold, font.Italic, font.CharacterSet, font.PitchAndFamily);
        using (var metadata = source.XData)
        {
            if (metadata != null)
            {
                int count = 0;
                foreach (TypedValue value in metadata)
                {
                    if (++count > 3) break;
                    _ = (value.TypeCode, value.Value);
                }
            }
        }
        copy.Name = "COMPILE_ONLY_TEXT";
        copy.TextSize = 0;
        copy.XScale = 0.8;
        copy.ObliquingAngle = 0;
        var table = (TextStyleTable)transaction.GetObject(db.TextStyleTableId, OpenMode.ForWrite);
        ObjectId id = table.Add(copy);
        transaction.AddNewlyCreatedDBObject(copy, true);
        return id;
    }

    private static ObjectId Dimension(Database db, Transaction transaction, ObjectId sourceId, ObjectId textStyle)
    {
        var source = (DimStyleTableRecord)transaction.GetObject(sourceId, OpenMode.ForRead);
        using var copy = (DimStyleTableRecord)source.Clone();
        _ = copy.ObjectId.IsNull;
        _ = (source.Annotative, source.ExtensionDictionary, source.HasFields, source.IsDependent, source.OwnerId);
        _ = (source.Dimblk, source.Dimblk1, source.Dimblk2, source.Dimlfac, source.Dimalt, source.Dimpost, source.Dimapost);
        copy.Name = "COMPILE_ONLY_DIM";
        copy.Dimscale = 2;
        copy.Dimtxt = 2.5;
        copy.Dimasz = 2.5;
        copy.Dimtxsty = textStyle;
        var table = (DimStyleTable)transaction.GetObject(db.DimStyleTableId, OpenMode.ForWrite);
        ObjectId id = table.Add(copy);
        transaction.AddNewlyCreatedDBObject(copy, true);
        return id;
    }

    private static void CopySignature(TextStyleTableRecord target, TextStyleTableRecord source)
    {
        // The CHM prints ref RXObject; this verifies the installed signature.
        target.CopyFrom(source);
    }
}
