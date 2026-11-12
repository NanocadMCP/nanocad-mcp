using System.Text.Json;
using static NanoCAD.Contracts.StructureWriteValidation;

namespace NanoCAD.Contracts;

public sealed record LibraryBlockDwgEdit(string Action, string SourcePath, string? Name,
    string? DefinitionHandle, string? ExpectedDefinitionToken, string? ExpectedFileSha256)
    : StructureEdit(Action)
{
    public override object CanonicalValue() => new
    {
        Operation,
        Action,
        SourcePath = SourcePath.ToUpperInvariant(),
        Name,
        DefinitionHandle,
        ExpectedDefinitionToken,
        ExpectedFileSha256
    };
}

public sealed record BlockLibrarySymbolMapping(string source_handle, string source_name, EntityRef target_ref, string target_name);
public sealed record BlockLibraryNativeCreationTime(string source_handle, EntityRef target_ref, EntityRef owner_ref,
    EntityRef registered_application_ref, string native_value_hex16, JsonElement raw_records, bool source_application_absent, string corroboration);
public sealed record BlockLibraryDwgResult(string source_path, string source_sha256, double[] source_base_point,
    double[] target_base_point, int source_root_count, BlockLibrarySymbolMapping[] renamed_symbols,
    EntityRef[] retained_attribute_definitions, EntityRef[] referring_insertions)
{
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public BlockLibraryNativeCreationTime[]? native_creation_times { get; init; }
}

public static class LibraryBlockDwgValidation
{
    public static bool Handles(string operation) => operation is "import_block_dwg" or "redefine_block_from_dwg";

    private static string Token(JsonElement value, string field, string path)
    {
        string token = Text(value, field, path, 64);
        if (token.Length != 64 || token.Any(character => !Uri.IsHexDigit(character)))
            throw Invalid(path + "." + field, "Use the exact 64-hexadecimal SHA-256 token.");
        return token.ToUpperInvariant();
    }

    public static LibraryBlockDwgEdit Parse(JsonElement value, string path)
    {
        string action = Text(value, "operation", path, 64);
        if (!Handles(action)) throw Invalid(path + ".operation", "Select import_block_dwg or redefine_block_from_dwg.");
        bool import = action == "import_block_dwg";
        Shape(value, path, import
            ? ["operation", "path", "name", "expected_file_sha256"]
            : ["operation", "path", "definition_handle", "expected_definition_token", "expected_file_sha256"]);
        string suppliedPath = Text(value, "path", path, 240), source;
        try { source = ExternalReferenceValidation385.NormalizePath(suppliedPath, "xref"); }
        catch (DomainException error) { throw Invalid(path + ".path", error.Error.message, error.Error.code); }
        string? name = null, definition = null, expected = null;
        if (import)
        {
            name = Text(value, "name", path, 255);
            if (!BlockWriteValidation.OrdinaryName(name) || name.StartsWith('*'))
                throw Invalid(path + ".name", "Use a new ordinary non-anonymous local block name.");
        }
        else
        {
            string suppliedHandle = Text(value, "definition_handle", path, 16);
            try { definition = Validation.NormalizeHandle(suppliedHandle); }
            catch (DomainException error) { throw Invalid(path + ".definition_handle", error.Error.message, error.Error.code); }
            expected = Token(value, "expected_definition_token", path);
        }
        string? fileHash = value.TryGetProperty("expected_file_sha256", out _) ? Token(value, "expected_file_sha256", path) : null;
        return new(action, source, name, definition, expected, fileHash);
    }

    public static void Batch(IReadOnlyList<StructureEdit> operations)
    {
        if (operations.Any(operation => operation is LibraryBlockDwgEdit) && operations.Count != 1)
            throw Invalid("$.operations", "Import or redefine one DWG library block in a separate structural request.", "INVALID_OPERATION_ORDER");
    }
}
