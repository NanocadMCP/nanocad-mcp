using System.Text.Json;
using NanoCAD.Contracts;

namespace NanoCADBridge;

// Completed managed snapshots only; never read native state while forming an
// error, invent an allowed app/group, or treat a metadata mismatch as rollback.
internal static class CadDimensionMetadataDiagnostics358
{
    internal static object XData(JsonElement before,JsonElement after,Action cancel)
    {
        cancel();var old=before.GetProperty("xdata");var actual=after.GetProperty("xdata");
        if(old.GetRawText()==actual.GetRawText())return new{changed=false,scope="captured_typed_XData_only"};
        int priorBytes=JsonSerializer.SerializeToUtf8Bytes(old,Wire.Json).Length;cancel();
        int currentBytes=JsonSerializer.SerializeToUtf8Bytes(actual,Wire.Json).Length;cancel();
        if((long)priorBytes+currentBytes>16384)
            return new{changed=true,complete=false,expected_bytes=priorBytes,current_bytes=currentBytes,
                scope="captured_typed_XData_only",unavailable_reason="Complete changed XData exceeds its actual16KiB diagnostic budget; component fingerprints remain available."};
        return new{changed=true,complete=true,expected_bytes=priorBytes,current_bytes=currentBytes,
            expected=old.Clone(),current=actual.Clone(),scope="captured_typed_XData_only; no_normalization_or_rollback_claim"};
    }
}
