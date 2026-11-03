using System.Text.Json;
using System.Text.Json.Nodes;
using NanoCAD.Contracts;
using NanoCAD.McpServer;
using NanoCAD.Ipc;

internal static class StyleWriteTests
{
    private static void Check(bool value, string why) { if (!value) throw new Exception(why); }
    private static void Domain(string code, Action action)
    { try { action(); throw new Exception("Expected " + code); } catch (DomainException e) { Check(e.Error.code == code, "Expected " + code + ", got " + e.Error.code); } }
    private static JsonObject Input() => new() { ["operation"] = "create_text_style", ["name"] = "MCP_Text", ["base_style_handle"] = "000af" };
    private static CreateTextStyleEdit Parse(JsonObject value) => StyleWriteValidation.ParseCreate(Wire.Element(value));
    private static CreateTextStyleEdit Raw(string properties)
    {
        using var json = JsonDocument.Parse("{\"operation\":\"create_text_style\",\"name\":\"New\",\"base_style_handle\":\"A\",\"properties\":" + properties + "}");
        return StyleWriteValidation.ParseCreate(json.RootElement);
    }
    private static StructureWriteArguments Args(params StructureEdit[] edits) => new("11111111-1111-4111-8111-111111111111",
        "22222222-2222-4222-8222-222222222222", "1", "33333333-3333-4333-8333-333333333333", "Millimeters", edits);
    private static FontMetadataValue[] Font(string family = "Arial", int flags = 33554466) =>
        new[] { new FontMetadataValue(1001, "ACAD"), new FontMetadataValue(1000, family), new FontMetadataValue(1071, flags) };
    public static Dictionary<string, object?> ProtocolInput(string bridge, string document)
    {
        var root = StructureWriteTests.ProtocolInput(bridge, document); root["operations"] = new[] { Input() }; return root;
    }
    public static ModifiedStructure ProtocolData(StructureWriteArguments args) => new(args.OperationId, "committed", false,
        new[] { new StructureChange(0, "create_text_style", new(args.BridgeId, args.DocumentId, "FA"), "text_style", ((CreateTextStyleEdit)args.Operations[0]).Name, true) }, "single_command");

    public static async Task Run(Func<string, Func<Task>, Task> test)
    {
        await test("style draft inherits absent properties and keeps explicit variable height", () =>
        {
            var baseline = Parse(Input());
            Check(baseline.BaseStyleHandle == "AF" && baseline.Properties == new TextStyleChanges(null, null), "defaults/handle normalization");
            Check(StyleWriteValidation.ResolveProperties(baseline.Properties, 2.5, 0.8) == new EffectiveTextStyleProperties(2.5, 0.8), "inherit both");
            Check(StyleWriteValidation.ResolveProperties(new(0, null), 2.5, 0.8) == new EffectiveTextStyleProperties(0, 0.8), "explicit variable height");
            Check(StyleWriteValidation.ResolveProperties(new(null, 2), 2.5, 0.8) == new EffectiveTextStyleProperties(2.5, 2), "inherit fixed height");
            foreach (string name in new[] { "0", "Defpoints", "Text 中文" })
            { var value = Input(); value["name"] = name; Check(Parse(value).Name == name, "style table name semantics"); }
            var wide = Input(); wide["base_style_handle"] = "ffffffffffffffff";
            Check(Parse(wide).BaseStyleHandle == "FFFFFFFFFFFFFFFF", "full uint64 handle");
            var fixedStyle = Input(); fixedStyle["properties"] = new JsonObject { ["height"] = 2.5, ["width_factor"] = 0.8 };
            var variable = Input(); variable["properties"] = new JsonObject { ["height"] = 0 };
            File.WriteAllText(Path.Combine(Environment.GetEnvironmentVariable("NANOCAD_MCP_HOME")!, "style-write-draft-schemas.json"),
                JsonSerializer.Serialize(new { create = StyleWriteSchema.CreateTextStyle(), inputs = new[] { Input(), fixedStyle, variable, wide },
                    exposed_in_mcp = true, native_acceptance = false }, Wire.Json));
            return Task.CompletedTask;
        });
        await test("style draft rejects ambiguous shapes, font paths and malformed names", () =>
        {
            Action<JsonObject>[] invalid = {
                x => x.Remove("name"), x => x.Remove("base_style_handle"), x => x.Remove("operation"),
                x => x["operation"] = "set_text_style", x => x["name"] = " bad", x => x["name"] = "A|B",
                x => x["name"] = new string('A', 256), x => x["name"] = "Bad\nName", x => x["name"] = "",
                x => x["properties"] = null, x => x["properties"] = new JsonObject(), x => x["properties"] = new JsonArray(),
                x => x["font_file"] = "arbitrary.shx", x => x["typeface"] = "Arial", x => x["set_current"] = true,
                x => x["properties"] = new JsonObject { ["height"] = 2, ["font_file"] = "x" } };
            foreach (var change in invalid) { var value = Input(); change(value); Domain("INVALID_ARGUMENT", () => Parse(value)); }
            foreach (string handle in new[] { "0", "0000", "GG" })
            { var value = Input(); value["base_style_handle"] = handle; Domain("INVALID_HANDLE", () => Parse(value)); }
            foreach (string raw in new[] { "null", "[]", "{\"height\":null}", "{\"height\":\"0\"}", "{\"height\":false}", "{\"height\":0,\"height\":2}" })
                Domain("INVALID_ARGUMENT", () => Raw(raw));
            using var badUnicode = JsonDocument.Parse("{\"operation\":\"create_text_style\",\"name\":\"\\uD800\",\"base_style_handle\":\"A\"}");
            Domain("INVALID_ARGUMENT", () => StyleWriteValidation.ParseCreate(badUnicode.RootElement));
            using var duplicate = JsonDocument.Parse("{\"operation\":\"create_text_style\",\"name\":\"A\",\"name\":\"B\",\"base_style_handle\":\"A\"}");
            Domain("INVALID_ARGUMENT", () => StyleWriteValidation.ParseCreate(duplicate.RootElement));
            return Task.CompletedTask;
        });
        await test("style draft rejects numeric underflow and invalid base values before overrides", () =>
        {
            foreach (string height in new[] { "-1", "1e-10", "1000001", "1e999", "1e-999", "-1e-999" })
                Domain("INVALID_ARGUMENT", () => Raw("{\"height\":" + height + "}"));
            foreach (string width in new[] { "0", "0.009", "100.01", "1e999", "1e-999" })
                Domain("INVALID_ARGUMENT", () => Raw("{\"width_factor\":" + width + "}"));
            foreach (string height in new[] { "0", "-0.0", "0e-999", "1e-9", "1e6" })
                Check(StyleWriteValidation.ValidHeight(Raw("{\"height\":" + height + "}").Properties.Height!.Value), "height boundary " + height);
            foreach (double invalid in new[] { double.NaN, double.PositiveInfinity, -1, 1e-10 })
                Domain("UNSUPPORTED_STYLE_TYPE", () => StyleWriteValidation.ResolveProperties(new(2, 1), invalid, 1));
            foreach (double invalid in new[] { double.NaN, double.NegativeInfinity, 0, 101 })
                Domain("UNSUPPORTED_STYLE_TYPE", () => StyleWriteValidation.ResolveProperties(new(2, 1), 0, invalid));
            Domain("INVALID_ARGUMENT", () => StyleWriteValidation.ResolveProperties(new(double.NaN, null), 0, 1));
            Domain("INVALID_ARGUMENT", () => StyleWriteValidation.ResolveProperties(new(null, 0), 0, 1));
            Check(BitConverter.DoubleToInt64Bits(Raw("{\"height\":-0.0}").Properties.Height!.Value) == 0, "canonical positive zero");
            return Task.CompletedTask;
        });
        await test("style fingerprints retain inheritance intent and normalize equivalent JSON", () =>
        {
            var baseline = Parse(Input()); string fingerprint = Args(baseline).Fingerprint();
            foreach (var change in new[] { baseline with { Name = "Other" }, baseline with { BaseStyleHandle = "B" },
                baseline with { Properties = new(0, null) }, baseline with { Properties = new(null, 1) }, baseline with { Properties = new(2, 0.8) } })
                Check(Args(change).Fingerprint() != fingerprint, "style payload omitted");
            var input = Input(); input["base_style_handle"] = "AF";
            Check(Args(Parse(input)).Fingerprint() == fingerprint, "equivalent handle");
            Check(Args(Raw("{\"height\":-0.0,\"width_factor\":1}")).Fingerprint() == Args(Raw("{\"width_factor\":1.0,\"height\":0}")).Fingerprint(), "numeric/property-order canonicalization");
            var other = baseline with { Name = "Other" };
            Check(Args(baseline, other).Fingerprint() != Args(other, baseline).Fingerprint(), "operation order");
            return Task.CompletedTask;
        });
        await test("style mixed batches enforce style, operation and total object budgets", () =>
        {
            var template = Parse(Input());
            var styles = Enumerable.Range(0, 16).Select(i => (StructureEdit)(template with { Name = "Style" + i })).ToArray();
            Check(StyleWriteValidation.ValidateBatch(styles) == 16, "16 styles");
            Domain("INVALID_ARGUMENT", () => StyleWriteValidation.ValidateBatch(styles.Append(template)));
            Domain("INVALID_OPERATION_ORDER", () => StyleWriteValidation.ValidateBatch(new[] { template, template with { Name = template.Name.ToLowerInvariant() } }));
            Domain("INVALID_ARGUMENT", () => StyleWriteValidation.ValidateBatch(Array.Empty<StructureEdit>()));
            var changes = new LayerChanges(1, null, null, null, null, null, null);
            var full = Enumerable.Range(0, 4).Select(i => (StructureEdit)new DefineBlockEdit("B" + i, new(0, 0),
                Enumerable.Range(0, 100).Select(j => (EntitySpec)new LineSpec(new(0, j), new(1, j), "0")).ToArray(),
                Enumerable.Range(0, 16).Select(j => new BlockAttributeSpec("A" + j, "", new(0, j), 2, "Standard", 0)).ToArray()))
                .Concat(styles).Concat(Enumerable.Range(0, 28).Select(i => new CreateLayerEdit("L" + i, changes))).ToArray();
            Check(full.Length == 48 && StyleWriteValidation.ValidateBatch(full) == 512, "combined 512 object boundary");
            Domain("INVALID_ARGUMENT", () => StyleWriteValidation.ValidateBatch(full.Append(new CreateLayerEdit("Extra", changes))));
            var fifty = Enumerable.Range(0, 50).Select(i => (StructureEdit)new CreateLayerEdit("L" + i, changes)).ToArray();
            Check(StyleWriteValidation.ValidateBatch(fifty) == 50, "50 operations");
            Domain("INVALID_ARGUMENT", () => StyleWriteValidation.ValidateBatch(fifty.Append(template)));
            Check(StyleWriteValidation.ValidateBatch(new StructureEdit[] { template, new CreateLayerEdit(template.Name, changes) }) == 2, "separate symbol table namespaces");
            return Task.CompletedTask;
        });
        await test("font metadata preserves Unicode family and all signed int32 bits", () =>
        {
            Check(StyleFontMetadata.Read(null) == null && StyleFontMetadata.Read(Array.Empty<FontMetadataValue>()) == null, "empty metadata");
            foreach (int bits in new[] { 33554466, 0, -1, int.MinValue, int.MaxValue })
                Check(StyleFontMetadata.Read(Font("Font 中文", bits)) == new FontMetadataSignature("ACAD", "Font 中文", bits), "metadata preservation");
            using var oracle = JsonDocument.Parse(File.ReadAllText("tests/fixtures/phase4-style-write.expected.json"));
            // DXF group codes and the raw signed integer are checked independently
            // of the native FontDescriptor, which still needs a host acceptance run.
            var tags = oracle.RootElement.GetProperty("text_styles").GetProperty("P4S_TTF").GetProperty("acad_xdata");
            var fixtureValues = new[] { new FontMetadataValue(1001, "ACAD"),
                new FontMetadataValue(tags[0][0].GetInt32(), tags[0][1].GetString()),
                new FontMetadataValue(tags[1][0].GetInt32(), tags[1][1].GetInt32()) };
            Check(StyleFontMetadata.Read(fixtureValues) == new FontMetadataSignature("ACAD", "Arial", 33554466), "fixture font metadata");
            return Task.CompletedTask;
        });
        await test("font metadata rejects unknown shapes with bounded and disposed enumeration", () =>
        {
            var invalid = new List<FontMetadataValue[]> { Font().Take(1).ToArray(), Font().Take(2).ToArray(), Font().Reverse().ToArray(), Font().Append(new(1001, "Other")).ToArray() };
            foreach (var item in new[] { new FontMetadataValue(1001, "acad"), new(1000, "ACAD"), new(1001, null) })
            { var copy = Font(); copy[0] = item; invalid.Add(copy); }
            foreach (string family in new[] { "", new string('A', 256), "A\nB", "\uD800" }) invalid.Add(Font(family));
            foreach (object? bits in new object?[] { 1L, (short)1, 1d, true, "1", null, Wire.Element(1) })
            { var copy = Font(); copy[2] = new(1071, bits); invalid.Add(copy); }
            foreach (var value in invalid) Domain("UNSUPPORTED_STYLE_TYPE", () => StyleFontMetadata.Read(value));
            int moves = 0; bool disposed = false;
            IEnumerable<FontMetadataValue> Unbounded()
            {
                try { foreach (var item in Font()) { moves++; yield return item; } moves++; yield return new(1001, "Other"); throw new Exception("Read beyond fourth item"); }
                finally { disposed = true; }
            }
            Domain("UNSUPPORTED_STYLE_TYPE", () => StyleFontMetadata.Read(Unbounded()));
            Check(moves == 4 && disposed, "enumeration must stop and dispose at fourth entry");
            return Task.CompletedTask;
        });
        await test("public style parser retains typed payload and enforces aggregate limits", () =>
        {
            var root = ProtocolInput(Args().BridgeId, Args().DocumentId);
            var parsed = StructureWriteValidation.Parse(Wire.Element(root));
            Check(parsed.Operations.Single() == Parse(Input()), "typed public style route retained in linetype read candidate");
            root["operations"] = Enumerable.Range(0, 17).Select(i => { var value = Input(); value["name"] = "Style" + i; return value; }).ToArray();
            Check(StructureWriteValidation.Parse(Wire.Element(root)).Operations.Length==17,"no arbitrary style count ceiling");
            root["operations"] = new[] { Input(), Input() };
            Domain("INVALID_OPERATION_ORDER", () => StructureWriteValidation.Parse(Wire.Element(root)));
            return Task.CompletedTask;
        });
        await test("style clone verification preserves every observed field and detects unexpected mutations", () =>
        {
            var source = StyleReadTests.Properties();
            var expected = StyleCloneValidation.Expected(source, new(2.5, 0.8));
            Check(expected.height == 2.5 && expected.width_factor == 0.8 && expected.font_descriptor == source.font_descriptor, "bounded overrides");
            StyleCloneValidation.RequireSame(expected, expected with { base_eligibility = new(true, Array.Empty<string>()) });
            var changes = new[] { expected with { font_file = "Other.ttf" }, expected with { big_font_file = "Big.shx" },
                expected with { height = 3 }, expected with { width_factor = 0.9 }, expected with { is_dependent = true },
                expected with { is_vertical = true }, expected with { is_shape_file = true }, expected with { oblique_angle = 0.01 },
                expected with { generation_flags = 2 }, expected with { annotative_state = "true", annotative_native_value = 0 },
                expected with { has_fields = true }, expected with { has_extension_dictionary = true },
                expected with { font_metadata = new("acad_font", "Arial", 33554466) },
                expected with { font_descriptor = source.font_descriptor with { typeface = "Other" } },
                expected with { font_descriptor = source.font_descriptor with { bold = true } },
                expected with { font_descriptor = source.font_descriptor with { italic = true } },
                expected with { font_descriptor = source.font_descriptor with { character_set = 204 } },
                expected with { font_descriptor = source.font_descriptor with { pitch_and_family = 34 } },
                expected with { base_eligibility = new(false, new[] { "fields" }) } };
            foreach (var value in changes) Domain("API_EXCEPTION", () => StyleCloneValidation.RequireSame(expected, value));
            Domain("UNSUPPORTED_STYLE_TYPE", () => StyleCloneValidation.Expected(source with { base_eligibility = new(false, new[] { "fields" }) }, new(0, 1)));
            return Task.CompletedTask;
        });
        await test("style commit replay retains its table reference and rejects changed clone intent", () =>
        {
            var input = ProtocolInput(Args().BridgeId, Args().DocumentId);
            var request = new Request(Wire.Version, Guid.NewGuid().ToString("D"), "modify_structure", 10000, Wire.Element(input));
            var args = StructureWriteValidation.Parse(request.arguments);
            using var queue = new WriteQueue(Environment.CurrentManagedThreadId);
            var (job, _) = queue.Enqueue(request); queue.Take(); Check(job.TryStart(), "style job not started");
            job.BeginCommit(); var result = Result.Ok(request.requestId, ProtocolData(args)); job.RecordCommit(result); job.Complete(result);
            var (same, repeated) = queue.Enqueue(request with { requestId = Guid.NewGuid().ToString("D") });
            var replay = (ModifiedStructure)WriteJob.ForCaller(same.Completion.Result, "replay", repeated).data!;
            Check(replay.replayed && replay.changes[0].kind == "text_style" && replay.changes[0].@ref.handle == "FA" && replay.changes[0].children == null && queue.Take() == null, "style replay reexecuted/lost reference");
            var changed = JsonNode.Parse(request.arguments.GetRawText())!.AsObject(); changed["operations"]![0]!["properties"] = new JsonObject { ["height"] = 0 };
            Domain("OPERATION_ID_CONFLICT", () => queue.Enqueue(request with { arguments = Wire.Element(changed) }));
            return Task.CompletedTask;
        });
        await test("cancelled and uncertain style writes remain non-reexecuting ledger entries", () =>
        {
            foreach (bool unknown in new[] { false, true })
            {
                var input = ProtocolInput(Args().BridgeId, Args().DocumentId);
                var request = new Request(Wire.Version, Guid.NewGuid().ToString("D"), "modify_structure", 10000, Wire.Element(input));
                using var queue = new WriteQueue(Environment.CurrentManagedThreadId); using var cancel = new CancellationTokenSource();
                var (job, _) = queue.Enqueue(request, cancel.Token); queue.Take();
                if (unknown) { Check(job.TryStart(), "style job not started"); job.BeginCommit(); job.Complete(job.Error("WRITE_OUTCOME_UNKNOWN", "commit did not return", "unknown")); }
                else { cancel.Cancel(); Check(!job.TryStart(), "cancelled style started"); }
                var (same, repeated) = queue.Enqueue(request);
                Check(repeated && ReferenceEquals(same, job) && queue.Take() == null && same.Completion.Result.error!.code == (unknown ? "WRITE_OUTCOME_UNKNOWN" : "CANCELLED"), "uncertain style write repeated");
            }
            return Task.CompletedTask;
        });
        await test("invalid style operations are rejected before any MCP to IPC call", async () =>
        {
            string bridge = Guid.NewGuid().ToString("D"), document = Guid.NewGuid().ToString("D"); int calls = 0;
            using var listener = new BridgeListener(Discovery.Describe(bridge, "style-test-no-CAD"),
                (request, _) => { calls++; return Task.FromResult(Result.Ok(request.requestId, new { })); }, new JsonLog("style-write-test"));
            foreach (string mode in new[] { "font", "height", "handle", "duplicate_names" })
            {
                var root = JsonSerializer.SerializeToNode(ProtocolInput(bridge, document))!.AsObject();
                if (mode == "font") root["operations"]![0]!["font_file"] = "unrequested.ttf";
                if (mode == "height") root["operations"]![0]!["properties"] = new JsonObject { ["height"] = -1 };
                if (mode == "handle") root["operations"]![0]!["base_style_handle"] = "0";
                if (mode == "duplicate_names") root["operations"] = new JsonArray(Enumerable.Range(0,2).Select(i => (JsonNode?)Input()).ToArray());
                var input = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(root.ToJsonString())!;
                var result = await new ToolService().CallAsync("modify_structure", input, default);
                Check(result.IsError == true && calls == 0, "invalid style reached the bridge: " + mode);
            }
        });
    }
}











