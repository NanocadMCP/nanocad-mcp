using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace NanoCAD.Contracts;

// DXF1005 is a native handle reference. Same-looking text in DXF1000 is
// literal data and never participates in a cross-database identity mapping.
public static class LibraryBlockMetadataReferences
{
    public static string? HandleReference(JsonElement record)
    {
        if (record.ValueKind != JsonValueKind.Object || !record.TryGetProperty("code", out var code) || code.ValueKind != JsonValueKind.Number || !code.TryGetInt32(out int group) || group != 1005) return null;
        string? text = null;
        if (record.TryGetProperty("value", out var value))
        {
            if (value.ValueKind == JsonValueKind.String) text = value.GetString();
            else if (value.ValueKind == JsonValueKind.Object && value.TryGetProperty("native_type", out var kind) && kind.ValueKind == JsonValueKind.String && kind.GetString() == "Handle" &&
                value.TryGetProperty("handle", out var handle) && handle.ValueKind == JsonValueKind.String) text = handle.GetString();
        }
        if (text == null || text.Length is 0 or > 16 || text.Any(character => !Uri.IsHexDigit(character)) || !ulong.TryParse(text, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out ulong bits))
            throw new DomainException("UNSUPPORTED_BLOCK_LIBRARY", "DXF1005 requires the actual hexadecimal native string/Handle representation.", false, new { code = 1005 });
        return bits.ToString("X", CultureInfo.InvariantCulture);
    }
    public static JsonElement MapHandleReference(JsonElement record, IReadOnlyDictionary<string, string>? mapping = null)
    {
        string? source = HandleReference(record); if (source == null) return record.Clone();
        string target = source;
        if (source != "0" && mapping != null && !mapping.TryGetValue(source, out target!))
            throw new DomainException("NATIVE_BLOCK_LIBRARY_MISMATCH", "A DXF1005 source handle has no actual native clone correspondence.", false, new { code = 1005, source_handle = source });
        var result = JsonNode.Parse(record.GetRawText())!.AsObject();
        if (result["value"] is JsonObject native) native["handle"] = target; else result["value"] = target;
        return Wire.Element(result);
    }
}
