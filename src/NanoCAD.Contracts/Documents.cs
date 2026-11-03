using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;

namespace NanoCAD.Contracts;

public static class DwgSaveValidation
{
    // Native control: 255 survives SaveAs/readback; 256 becomes zero on this host.
    public static void RequireLinetypeElementCount(int count)
    {
        if (count < 0) throw new DomainException("OBJECT_READ_FAILED", "Invalid linetype element count; DWG save was not started.");
        if (count > 255) throw new DomainException("UNSUPPORTED_DWG_CONTENT",
            "DWG saving is blocked: a linetype exceeds 255 elements and this host loses its pattern on SaveAs/readback. Source and destination are unchanged.");
    }
}

public interface IOperationArguments
{
    string Operation { get; }
    string BridgeId { get; }
    string OperationId { get; }
    string Fingerprint();
}

public sealed record DocumentInfo(string document_id, string name, string? path, bool is_named,
    bool is_read_only, bool is_active, string revision);
public sealed record DocumentList(string bridge_instance_id, string? active_document_id, DocumentInfo[] documents);
public sealed record DocumentOperationResult(string operation_id, string status, bool replayed, string action,
    DocumentInfo document, string? written_path, long? written_bytes, bool reused_open_document);
public sealed record DocumentArguments(string BridgeId, string OperationId, string Action, string? DocumentId,
    string? ExpectedRevision, string? Path, bool ReadOnly, bool Overwrite, string? LayoutHandle = null) : IOperationArguments
{
    public string Operation => "manage_document";
    public string Fingerprint() => Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(new
    { Operation, BridgeId, Action, DocumentId, ExpectedRevision, Path = Path?.ToUpperInvariant(), ReadOnly, Overwrite, LayoutHandle }, Wire.Json)));
}

public static class DocumentValidation
{
    public static DocumentArguments Parse(JsonElement input)
    {
        if (input.ValueKind != JsonValueKind.Object) throw new DomainException("INVALID_ARGUMENT", "Arguments must be an object.");
        string? Text(string name, int maximum, bool required = false)
        {
            if (!input.TryGetProperty(name, out var item))
            { if (required) throw new DomainException("INVALID_ARGUMENT", name + " is required."); return null; }
            if (item.ValueKind != JsonValueKind.String || item.GetString() is not { Length: > 0 } value || value.Length > maximum)
                throw new DomainException("INVALID_ARGUMENT", name + " must be a bounded nonempty string.");
            return value;
        }
        string? Id(string name, bool required = false)
        {
            var value = Text(name, 36, required);
            if (value == null) return null;
            if (!Guid.TryParseExact(value, "D", out var id) || id == Guid.Empty)
                throw new DomainException("INVALID_ARGUMENT", name + " must be a nonempty UUID.");
            return id.ToString("D");
        }
        bool Flag(string name)
        {
            if (!input.TryGetProperty(name, out var value)) return false;
            if (value.ValueKind is not (JsonValueKind.True or JsonValueKind.False)) throw new DomainException("INVALID_ARGUMENT", name + " must be boolean.");
            return value.GetBoolean();
        }
        var action = Text("action", 16, true)!;
        var allowed = new HashSet<string>(StringComparer.Ordinal) { "bridge_instance_id", "operation_id", "action" };
        switch (action)
        {
            case "new": break;
            case "open": allowed.UnionWith(new[] { "path", "read_only" }); break;
            case "activate": allowed.UnionWith(new[] { "document_id", "expected_revision" }); break;
            case "activate_layout": allowed.UnionWith(new[] { "document_id", "expected_revision", "layout_handle" }); break;
            case "save": allowed.UnionWith(new[] { "document_id", "expected_revision", "path", "overwrite" }); break;
            case "export_dxf": allowed.UnionWith(new[] { "document_id", "expected_revision", "path", "overwrite" }); break;
            default: throw new DomainException("INVALID_ARGUMENT", "action must be new, open, activate, activate_layout, save or export_dxf.");
        }
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var property in input.EnumerateObject())
            if (!seen.Add(property.Name) || !allowed.Contains(property.Name))
                throw new DomainException("INVALID_ARGUMENT", "Unknown or duplicate argument: " + property.Name);
        string? revision = Text("expected_revision", 20, action is not ("new" or "open"));
        if (revision != null)
        {
            if (!revision.All(c => c is >= '0' and <= '9') || !ulong.TryParse(revision, NumberStyles.None, CultureInfo.InvariantCulture, out var number))
                throw new DomainException("INVALID_ARGUMENT", "expected_revision must be an unsigned 64-bit decimal string.");
            revision = number.ToString(CultureInfo.InvariantCulture);
        }
        var path = Text("path", 240, action is "open" or "export_dxf");
        if (path != null) path = NormalizePath(path, action);
        return new(Id("bridge_instance_id", true)!, Id("operation_id", true)!, action,
            Id("document_id", action is not ("new" or "open")), revision, path, Flag("read_only"), Flag("overwrite"),
            action == "activate_layout" ? Validation.NormalizeHandle(Text("layout_handle", 16, true)!) : null);
    }

    public static string NormalizePath(string path, string action)
    {
        // Initial local-file capability: exclude relative/device/UNC paths, ADS,
        // ambiguous trailing dots/spaces, traversal and reserved Win32 names.
        path = path.Replace('/', '\\');
        if (path.Length is < 4 or > 240 || !(path[0] is >= 'A' and <= 'Z' or >= 'a' and <= 'z') || path[1] != ':' || path[2] != '\\' ||
            path.Skip(2).Any(c => c < 32 || ":*?\"<>|".Contains(c)))
            throw new DomainException("INVALID_PATH", "Use an absolute local drive path of at most 240 characters.");
        foreach (var component in path[3..].Split('\\'))
        {
            string stem = component.Split('.')[0].ToUpperInvariant();
            if (component.Length == 0 || component is "." or ".." || component.EndsWith(' ') || component.EndsWith('.') ||
                stem is "CON" or "PRN" or "AUX" or "NUL" ||
                (stem.Length == 4 && (stem.StartsWith("COM", StringComparison.Ordinal) || stem.StartsWith("LPT", StringComparison.Ordinal)) && stem[3] is >= '1' and <= '9'))
                throw new DomainException("INVALID_PATH", "Path contains an ambiguous or reserved Win32 component.");
        }
        string extension = System.IO.Path.GetExtension(path).ToLowerInvariant();
        if (action == "open" ? extension is not (".dwg" or ".dxf") : extension != (action == "export_dxf" ? ".dxf" : ".dwg"))
            throw new DomainException("UNSUPPORTED_FORMAT", "Open accepts DWG/DXF; save writes DWG using the native owning-document configuration; export_dxf writes a DXF copy.");
        return System.IO.Path.GetFullPath(path);
    }
}
