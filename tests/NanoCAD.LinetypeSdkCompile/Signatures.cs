using Teigha.DatabaseServices;
using Teigha.Geometry;
namespace NanoCAD.LinetypeSdkCompile;
// Compile-only API assignments. No extension application, command or NETLOAD.
public static class Signatures
{
    public static void Verify(LinetypeTableRecord source, int index)
    {
        double length = source.DashLengthAt(index), rotation = source.ShapeRotationAt(index), scale = source.ShapeScaleAt(index), total = source.PatternLength;
        int number = source.ShapeNumberAt(index), count = source.NumDashes;
        string text = source.TextAt(index), description = source.AsciiDescription;
        Vector2d offset = source.ShapeOffsetAt(index); ObjectId style = source.ShapeStyleAt(index);
        bool ucs = source.ShapeIsUcsOrientedAt(index), fit = source.IsScaledToFit;
        _ = (length, rotation, scale, total, number, count, text, description, offset, style, ucs, fit);
    }
    // Next capability research only. This method is compiled, never executed.
    public static ObjectId VerifySimpleWrite(LinetypeTable table, Transaction transaction)
    {
        using var record = new LinetypeTableRecord();
        record.Name = "MCP_COMPILE_ONLY";
        record.AsciiDescription = "Dash and gap";
        record.NumDashes = 2;
        record.SetDashLengthAt(0, 5.0);
        record.SetDashLengthAt(1, -2.0);
        record.PatternLength = 7.0;
        record.IsScaledToFit = false;
        ObjectId id = table.Add(record);
        transaction.AddNewlyCreatedDBObject(record, true);
        return id;
    }
}
