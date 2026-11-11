using System.Text.Json;
using System.Text.Json.Nodes;
using ModelContextProtocol.Protocol;
using NanoCAD.Contracts;
using NanoCAD.McpServer;

internal static class LibraryBlockDwgTests
{
    private const string Bridge = "11111111-1111-4111-8111-111111111111";
    private const string Document = "22222222-2222-4222-8222-222222222222";
    private static readonly string Hash = new('a', 64);
    private static JsonObject Import() => new()
    { ["operation"] = "import_block_dwg", ["path"] = "C:/Library/Valve.dwg", ["name"] = "Valve" };
    private static JsonObject Redefine() => new()
    {
        ["operation"] = "redefine_block_from_dwg",
        ["path"] = "C:/Library/Valve.dwg",
        ["definition_handle"] = "000ad",
        ["expected_definition_token"] = Hash
    };
    private static StructureWriteArguments Parse(params JsonObject[] operations) => StructureWriteValidation.Parse(Wire.Element(new
    {
        bridge_instance_id = Bridge,
        document_id = Document,
        expected_revision = "7",
        operation_id = "33333333-3333-4333-8333-333333333333",
        units = "Millimeters",
        operations
    }));
    private static void Need(bool condition, string message)
    { if (!condition) throw new Exception(message); }
    private static DomainException Reject(Action action, string? code = null)
    {
        try { action(); }
        catch (DomainException error)
        { Need(code is null || error.Error.code == code, "Wrong library-block refusal: " + error.Error.code); return error; }
        throw new Exception("Invalid DWG library-block request was accepted.");
    }

    internal static async Task Run(Func<string, Func<Task>, Task> test)
    {
        await test("library metadata difference identifies the actual nested field and preserves missing versus null", () =>
        {
            var expected = Wire.Element(new { rounding = 1d, owned = new[] { new { common = new { owner_handle = "AD" } } } });
            var actual = Wire.Element(new { rounding = 1d, owned = new[] { new { common = new { owner_handle = "AF" } } } });
            var difference = LibraryBlockMetadataComparison.FirstDifference(expected, actual) ?? throw new Exception("Missing metadata mismatch evidence.");
            Need(difference.path == "$/owned/0/common/owner_handle" && difference.reason == "value" &&
                difference.expected.value?.GetString() == "AD" && difference.actual.value?.GetString() == "AF" &&
                difference.expected.value_included && difference.actual.value_included,
                "Metadata diagnostics lost the actual first differing field/value.");
            var reordered = JsonSerializer.Deserialize<JsonElement>("{\"owned\":[{\"common\":{\"owner_handle\":\"AD\"}}],\"rounding\":1}");
            Need(LibraryBlockMetadataComparison.FirstDifference(expected, reordered) == null, "Object field order became a metadata difference.");
            var missing = LibraryBlockMetadataComparison.FirstDifference(Wire.Element(new { extension = (string?)null }), Wire.Element(new { }))!;
            Need(missing.path == "$/extension" && missing.reason == "missing_actual_property" && missing.expected.kind == "Null" && missing.actual.kind == "missing",
                "An absent metadata field was confused with an actual native null value.");
            return Task.CompletedTask;
        });
        await test("library metadata mismatch receipts bound actual UTF8 values and retain complete serialized hashes", () =>
        {
            var expected = Wire.Element(new { payload = new string('\u0416', 3000) });
            var actual = Wire.Element(new { payload = new string('\u0416', 2999) + "X" });
            var difference = LibraryBlockMetadataComparison.FirstDifference(expected, actual) ?? throw new Exception("Missing large metadata mismatch evidence.");
            byte[] expectedBytes = JsonSerializer.SerializeToUtf8Bytes(expected.GetProperty("payload"), Wire.Json);
            byte[] actualBytes = JsonSerializer.SerializeToUtf8Bytes(actual.GetProperty("payload"), Wire.Json);
            Need(difference.path == "$/payload" && !difference.expected.value_included && !difference.actual.value_included &&
                difference.expected.value == null && difference.actual.value == null &&
                difference.expected.json_utf8_bytes == expectedBytes.Length && difference.actual.json_utf8_bytes == actualBytes.Length &&
                difference.expected.json_sha256 == Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(expectedBytes)) &&
                difference.actual.json_sha256 == Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(actualBytes)) &&
                JsonSerializer.SerializeToUtf8Bytes(difference, Wire.Json).Length < 2048,
                "A large metadata value leaked into the error receipt, used character count as UTF8 bytes, or lost its complete digest.");
            return Task.CompletedTask;
        });
        await test("library DXF1005 maps actual typed and string handles while preserving zero and unsigned64 bits", () =>
        {
            var mapping = new Dictionary<string, string>(StringComparer.Ordinal)
            { ["FFFFFFFFFFFFFFFF"] = "8000000000000000", ["AD"] = "123456789ABCDEF0", ["0"] = "FF" };
            var typed = Wire.Element(new { code = 1005, value = NativeMetadataValues.Handle(-1), annotation = "Unchanged" });
            string before = typed.GetRawText();
            var translated = LibraryBlockMetadataReferences.MapHandleReference(typed, mapping);
            Need(LibraryBlockMetadataReferences.HandleReference(typed) == "FFFFFFFFFFFFFFFF" &&
                translated.GetProperty("code").GetInt32() == 1005 &&
                translated.GetProperty("value").GetProperty("native_type").GetString() == "Handle" &&
                translated.GetProperty("value").GetProperty("handle").GetString() == "8000000000000000" &&
                translated.GetProperty("annotation").GetString() == "Unchanged" && typed.GetRawText() == before,
                "The actual native mapping lost high handle bits, changed typed/literal form or mutated its source metadata.");
            var text = Wire.Element(new { code = 1005, value = "000ad" });
            var textResult = LibraryBlockMetadataReferences.MapHandleReference(text, mapping);
            Need(LibraryBlockMetadataReferences.HandleReference(text) == "AD" &&
                textResult.GetProperty("value").ValueKind == JsonValueKind.String &&
                textResult.GetProperty("value").GetString() == "123456789ABCDEF0",
                "Native string handle correspondence was guessed, truncated or changed into a different runtime representation.");
            foreach (var zero in new[] { Wire.Element(new { code = 1005, value = NativeMetadataValues.Handle(0) }), Wire.Element(new { code = 1005, value = "0000" }) })
            {
                var actual = LibraryBlockMetadataReferences.MapHandleReference(zero, mapping);
                Need(LibraryBlockMetadataReferences.HandleReference(actual) == "0" &&
                    actual.GetProperty("value").ValueKind == zero.GetProperty("value").ValueKind,
                    "The native null handle was mapped to another object or lost its actual representation.");
            }
            return Task.CompletedTask;
        });
        await test("library handle translation preserves DXF1000 literals and refuses missing native DXF1005 correspondence", () =>
        {
            var mapping = new Dictionary<string, string>(StringComparer.Ordinal) { ["DEADBEEF"] = "B1" };
            var literal = Wire.Element(new { code = 1000, value = "DEADBEEF" });
            Need(LibraryBlockMetadataReferences.HandleReference(literal) == null &&
                LibraryBlockMetadataReferences.MapHandleReference(literal, mapping).GetRawText() == literal.GetRawText(),
                "Hexadecimal literal text became a guessed cross-database reference.");
            foreach (var missing in new[] { Wire.Element(new { code = 1005, value = "000ad" }), Wire.Element(new { code = 1005, value = NativeMetadataValues.Handle(0xAD) }) })
            {
                string original = missing.GetRawText();
                var error = Reject(() => LibraryBlockMetadataReferences.MapHandleReference(missing, mapping), "NATIVE_BLOCK_LIBRARY_MISMATCH");
                Need(Wire.Element(error.Error.details!).GetProperty("source_handle").GetString() == "AD" && missing.GetRawText() == original,
                    "Missing correspondence omitted a reference, hid its actual source handle or rewrote original data.");
                Need(LibraryBlockMetadataReferences.HandleReference(LibraryBlockMetadataReferences.MapHandleReference(missing)) == "AD",
                    "Canonical comparison without translation changed the actual source handle.");
            }
            Reject(() => LibraryBlockMetadataReferences.HandleReference(Wire.Element(new { code = 1005, value = new { native_type = "ObjectId", handle = "AD" } })),
                "UNSUPPORTED_BLOCK_LIBRARY");
            return Task.CompletedTask;
        });
        await test("library DWG contracts retain native source target and token roles without unit bypass", () =>
        {
            var imported = Import(); imported["expected_file_sha256"] = Hash;
            var import = Parse(imported); var redefine = Parse(Redefine());
            Need(import.Operations.Single() is LibraryBlockDwgEdit
            { Action: "import_block_dwg", SourcePath: @"C:\Library\Valve.dwg", Name: "Valve", DefinitionHandle: null, ExpectedDefinitionToken: null } first &&
                first.ExpectedFileSha256 == Hash.ToUpperInvariant(), "Import source, new name or optional file hash was reinterpreted.");
            Need(redefine.Operations.Single() is LibraryBlockDwgEdit
            { Action: "redefine_block_from_dwg", SourcePath: @"C:\Library\Valve.dwg", Name: null, DefinitionHandle: "AD", ExpectedFileSha256: null } second &&
                second.ExpectedDefinitionToken == Hash.ToUpperInvariant(), "Redefinition lost its existing native target or current membership token.");
            Need(!StructureWriteValidation.IsSettingsOnly(import.Operations) && !StructureWriteValidation.IsSettingsOnly(redefine.Operations),
                "Library geometry cloning bypassed the existing Millimeters drawing guard.");
            var ordinaryName = Import(); ordinaryName["name"] = "Defpoints";
            Need(((LibraryBlockDwgEdit)Parse(ordinaryName).Operations.Single()).Name == "Defpoints", "Layer-only special names leaked into block naming.");
            return Task.CompletedTask;
        });
        await test("library DWG input rejects unsafe file paths wrong formats and ambiguous replacement fields", () =>
        {
            foreach (string path in new[]
            {
                "Valve.dwg", "../Valve.dwg", @"\\server\library\Valve.dwg", @"\\?\C:\Library\Valve.dwg",
                "C:/Library/../Valve.dwg", "C:/Library/Valve.dxf", "C:/Library/NUL.dwg",
                "C:/Library/Valve.dwg:stream", "C:/Library/Folder./Valve.dwg"
            })
            {
                var input = Import(); input["path"] = path;
                var error = Reject(() => Parse(input));
                Need(Wire.Element(error.Error.details ?? throw new Exception("Missing error field details.")).GetProperty("path").GetString() == "$.operations[0].path", "Source path refusal lost its exact input field.");
            }
            foreach (string name in new[] { "", "*Anonymous", "Foreign|Valve", "Valve/Other", " Valve" })
            { var input = Import(); input["name"] = name; Reject(() => Parse(input)); }
            foreach (bool import in new[] { true, false })
            {
                var source = import ? Import() : Redefine();
                string[] forbidden = import ? ["definition_handle", "expected_definition_token", "base_point", "replace_existing"]
                    : ["name", "base_point", "preserve_originals", "attribute_values"];
                foreach (string field in forbidden)
                {
                    var input = source.DeepClone().AsObject(); input[field] = "unexpected";
                    var error = Reject(() => Parse(input), "INVALID_ARGUMENT");
                    Need(Wire.Element(error.Error.details ?? throw new Exception("Missing error field details.")).GetProperty("path").GetString() == "$.operations[0]." + field,
                        "Unknown library-block field lost its exact input path.");
                }
                foreach (string hash in new[] { "", new string('A', 63), new string('A', 65), new string('G', 64), "SHA256:" + Hash })
                { var input = source.DeepClone().AsObject(); input["expected_file_sha256"] = hash; Reject(() => Parse(input)); }
                var nullHash = source.DeepClone().AsObject(); nullHash["expected_file_sha256"] = null; Reject(() => Parse(nullHash));
            }
            foreach (string field in new[] { "definition_handle", "expected_definition_token" })
            { var input = Redefine(); input.Remove(field); Reject(() => Parse(input)); }
            foreach (string handle in new[] { "0", "name", "0xAD" })
            { var input = Redefine(); input["definition_handle"] = handle; Reject(() => Parse(input)); }
            var invalidToken = Redefine(); invalidToken["expected_definition_token"] = new string('G', 64); Reject(() => Parse(invalidToken));
            return Task.CompletedTask;
        });
        await test("library DWG cloning requires a standalone structural request", () =>
        {
            foreach (var operations in new[]
            {
                new[] { Import(), Import() }, new[] { Redefine(), Import() },
                new[] { Import(), new JsonObject { ["operation"] = "create_layer", ["name"] = "LibraryLayer" } }
            }) Reject(() => Parse(operations), "INVALID_OPERATION_ORDER");
            Need(Parse(Import()).Operations.Length == 1 && Parse(Redefine()).Operations.Length == 1, "A valid standalone native clone request was lost.");
            return Task.CompletedTask;
        });
        await test("library DWG replay identity includes file hash source target and snapshot intent", () =>
        {
            foreach (bool import in new[] { true, false })
            {
                var input = import ? Import() : Redefine(); input["expected_file_sha256"] = Hash;
                var baseline = Parse(input);
                var equivalent = input.DeepClone().AsObject(); equivalent["path"] = @"c:\library\VALVE.DWG";
                equivalent["expected_file_sha256"] = Hash.ToUpperInvariant();
                if (!import) { equivalent["definition_handle"] = "AD"; equivalent["expected_definition_token"] = Hash.ToUpperInvariant(); }
                Need(Parse(equivalent).Fingerprint() == baseline.Fingerprint(), "Equivalent Windows path/handle/token spelling changed replay intent.");
                var changes = new List<Action<JsonObject>>
                {
                    value => value["path"] = "C:/Library/Other.dwg", value => value["expected_file_sha256"] = new string('B', 64),
                    value => value.Remove("expected_file_sha256")
                };
                if (import) changes.Add(value => value["name"] = "ValveNext");
                else
                {
                    changes.Add(value => value["definition_handle"] = "AE");
                    changes.Add(value => value["expected_definition_token"] = new string('B', 64));
                }
                foreach (var change in changes)
                {
                    var changed = input.DeepClone().AsObject(); change(changed);
                    Need(Parse(changed).Fingerprint() != baseline.Fingerprint(), "Library source or replacement guards were omitted from replay identity.");
                }
            }
            Need(Parse(Import()).Fingerprint() != Parse(Redefine()).Fingerprint(), "Import and shared redefinition semantics collided.");
            return Task.CompletedTask;
        });
        await test("library DWG focused schemas and command routes expose exact import and redefine inputs", async () =>
        {
            foreach (string operation in new[] { "import_block_dwg", "redefine_block_from_dwg" })
            {
                var schema = AuditServerContractSupport.Branch("modify_structure", operation);
                var properties = schema.GetProperty("properties");
                var required = schema.GetProperty("required").EnumerateArray().Select(value => value.GetString()).ToHashSet(StringComparer.Ordinal);
                string[] expected = operation == "import_block_dwg" ? ["operation", "path", "name"]
                    : ["operation", "path", "definition_handle", "expected_definition_token"];
                Need(required.SetEquals(expected) && !schema.GetProperty("additionalProperties").GetBoolean() &&
                    properties.GetProperty("operation").GetProperty("const").GetString() == operation &&
                    properties.GetProperty("expected_file_sha256").GetProperty("minLength").GetInt32() == 64,
                    "Focused library schema changed required native references or file guard shape.");
                Need(!properties.TryGetProperty("entities", out _) && !properties.TryGetProperty("attribute_values", out _) &&
                    !properties.TryGetProperty("base_point", out _), "Library cloning acquired coordinate reconstruction or attribute synchronization inputs.");
            }
            var result = await new ToolService().CallAsync("get_guidance", new Dictionary<string, JsonElement>
            { ["topic"] = Wire.Element("commands"), ["section"] = Wire.Element("2d") }, default);
            using var navigation = JsonDocument.Parse(result.Content.OfType<TextContentBlock>().Single().Text);
            var routes = navigation.RootElement.GetProperty("routes").EnumerateArray().Single(route => route.GetProperty("tool").GetString() == "nanocad.modify_structure");
            var names = routes.GetProperty("variants").EnumerateArray().Select(value => value.GetString()).ToHashSet(StringComparer.Ordinal);
            Need(names.Contains("import_block_dwg") && names.Contains("redefine_block_from_dwg"), "Library operations are absent from useful 2D navigation.");
        });
        await test("library DWG results keep source-file handles separate from native destination references", () =>
        {
            EntityRef Ref(string handle) => new(Bridge, Document, handle);
            var old = new ModifiedStructure("operation", "committed", false, [], "not_guaranteed");
            Need(!Wire.Element(old).TryGetProperty("block_library_results", out _), "Legacy structure receipts acquired a required library result.");
            var source = new BlockLibraryDwgResult(@"C:\Library\Valve.dwg", Hash.ToUpperInvariant(), [10, 20, 0], [30, 40, 0], 3,
                [new("B1", "LibraryLayer", Ref("AD"), "$0$LibraryLayer")], [Ref("B2")], [Ref("C3")]);
            var receipt = Wire.Element(old with { block_library_results = [source] }).GetProperty("block_library_results")[0];
            var symbol = receipt.GetProperty("renamed_symbols")[0];
            Need(receipt.GetProperty("source_path").GetString() == source.source_path &&
                receipt.GetProperty("source_sha256").GetString() == source.source_sha256 &&
                symbol.GetProperty("source_handle").ValueKind == JsonValueKind.String && symbol.GetProperty("source_handle").GetString() == "B1" &&
                symbol.GetProperty("target_ref").GetProperty("handle").GetString() == "AD" &&
                receipt.GetProperty("retained_attribute_definitions")[0].GetProperty("handle").GetString() == "B2" &&
                receipt.GetProperty("referring_insertions")[0].GetProperty("handle").GetString() == "C3",
                "Source-file provenance was confused with current destination object identity.");
            var output = AuditServerContractSupport.Output("modify_structure");
            IEnumerable<JsonElement> Branches(JsonElement node)
            {
                if (node.TryGetProperty("anyOf", out var alternatives))
                    return alternatives.EnumerateArray().SelectMany(Branches);
                return [node];
            }
            var data = Branches(output.GetProperty("properties").GetProperty("data"))
                .Single(branch => branch.TryGetProperty("properties", out var fields) && fields.TryGetProperty("changes", out _));
            var schema = data.GetProperty("properties");
            Need(schema.TryGetProperty("block_library_results", out var library) &&
                !data.GetProperty("required").EnumerateArray().Any(field => field.GetString() == "block_library_results"),
                "The library result is missing from publication or became mandatory on old structure receipts.");
            var mapping = library.GetProperty("items").GetProperty("properties").GetProperty("renamed_symbols").GetProperty("items").GetProperty("properties");
            Need(mapping.GetProperty("source_handle").GetProperty("type").GetString() == "string" &&
                mapping.GetProperty("target_ref").GetProperty("properties").TryGetProperty("document_id", out _) &&
                mapping.GetProperty("target_ref").GetProperty("properties").TryGetProperty("bridge_instance_id", out _),
                "The output schema confuses file-scoped source handles with live target references.");
            return Task.CompletedTask;
        });
    }
}
