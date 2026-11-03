using System.Text.Json;
using NanoCAD.Contracts;
using static NanoCADBridge.CadTableEditingReader;

namespace NanoCADBridge;

// Physical sizes use the established geometric tolerance. Retained native
// scalar data uses exact serialization, including its observed Double bits.
internal static class CadTableSizeState361
{
    internal sealed record State(Snapshot Contents,JsonElement Sizes);
    private static JsonElement Stored(Snapshot value)=>Wire.Element(new{identity=value.Identity,common=value.Common,
        rows=value.Rows,columns=value.Columns,cells=value.Cells,native_contents=value.Values,cell_overrides=value.CellOverrides});
    internal static void Verify(State before,State after,JsonElement expectedSizes,Action cancel)
    {
        cancel();TableSizeVerification.Verify(expectedSizes,after.Sizes);
        var prior=Stored(before.Contents);cancel();var current=Stored(after.Contents);cancel();
        if(prior.GetRawText()!=current.GetRawText())
            throw new DomainException("NATIVE_TABLE_MISMATCH","Native table resizing changed retained identity, placement, raw scalar value/type/unit/bits, cell appearance/protection or override data.");
    }
}
