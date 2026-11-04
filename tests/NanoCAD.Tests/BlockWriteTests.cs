using System.Text.Json;
using System.Text.Json.Nodes;
using NanoCAD.Contracts;
using NanoCAD.McpServer;
using NanoCAD.Ipc;

internal static class BlockWriteTests
{
    private static void Check(bool condition, string why) { if (!condition) throw new Exception(why); }
    private static void Domain(string code, Action action)
    { try { action(); throw new Exception("Expected " + code); } catch (DomainException error) { Check(error.Error.code == code, "Expected " + code + ", got " + error.Error.code); } }
    private static JsonObject Oracle() => JsonNode.Parse(File.ReadAllText("tests/fixtures/phase4-block-write.expected.json"))!.AsObject();
    private static JsonObject Definition()
    {
        var oracle = Oracle();
        return new JsonObject
        {
            ["operation"] = "define_block",
            ["name"] = "MCP_Block_Pump",
            ["base_point"] = oracle["base_point"]!.DeepClone(),
            ["entities"] = oracle["members"]!.DeepClone(),
            ["attributes"] = oracle["attributes"]!.DeepClone()
        };
    }
    private static JsonObject Insertion() => new()
    { ["operation"] = "insert_block", ["definition_handle"] = "000af", ["position"] = new JsonArray(100, 200), ["layer"] = "P4B_TARGET" };
    private static DefineBlockEdit Define(JsonObject input) => BlockWriteValidation.ParseDefine(Wire.Element(input));
    private static InsertBlockEdit Insert(JsonObject input) => BlockWriteValidation.ParseInsert(Wire.Element(input));
    private static StructureWriteArguments Arguments(params StructureEdit[] edits) => new("11111111-1111-4111-8111-111111111111",
        "22222222-2222-4222-8222-222222222222", "1", "33333333-3333-4333-8333-333333333333", "Millimeters", edits);
    public static Dictionary<string, object?> ProtocolInput(string bridge, string document, bool insert)
    {
        var root = StructureWriteTests.ProtocolInput(bridge, document);
        root["operations"] = new[] { insert ? Insertion() : Definition() };
        return root;
    }
    public static ModifiedStructure ProtocolData(StructureWriteArguments args)
    {
        bool define = args.Operations[0] is DefineBlockEdit;
        EntityRef Ref(string handle) => new(args.BridgeId, args.DocumentId, handle);
        var children = define
            ? Enumerable.Range(0, 5).Select(i => new StructureChild(Ref((0xDB + i).ToString("X")), "entity", i, null))
                .Concat(new[] { new StructureChild(Ref("E0"), "attribute_definition", 0, "NAME"), new(Ref("E1"), "attribute_definition", 1, "CODE") }).ToArray()
            : new[] { new StructureChild(Ref("F1"), "attribute", 0, "NAME"), new(Ref("F2"), "attribute", 1, "CODE") };
        return new(args.OperationId, "committed", false, new[] { new StructureChange(0, args.Operations[0].Operation,
            Ref(define ? "DA" : "F0"), define ? "block_definition" : "block_insert", "MCP_Block_Pump", true, children) }, "single_command");
    }
    public static async Task Run(Func<string, Func<Task>, Task> test)
    {
        await test("block contract parses five local primitives, explicit base point and variable attribute defaults", () =>
        {
            var definition = Define(Definition());
            Check(definition.BasePoint == new Point2(10, 20) && definition.Entities.Length == 5 && definition.Attributes.Length == 2, "definition oracle shape");
            Check(definition.Entities.Select(e => e.Type).SequenceEqual(new[] { "Line", "Circle", "Arc", "Polyline", "Text" }), "primitive types");
            Check(definition.Entities.All(e => e.Layer == "0") && definition.Attributes.All(a => a.TextStyle == "Standard" && a.Rotation == 0), "explicit defaults");
            var layerSpecialName = Definition(); layerSpecialName["name"] = "Defpoints";
            Check(Define(layerSpecialName).Name == "Defpoints", "layer-only restrictions leaked into block names");
            var empty = Definition(); empty["attributes"]![0]!["default_text"] = "";
            Check(Define(empty).Attributes[0].DefaultText == "", "empty default text");
            var fullInsert = Insertion(); fullInsert["rotation"] = -Math.PI / 2; fullInsert["scale"] = 0.5;
            fullInsert["attribute_values"] = new JsonObject { ["NAME"] = "Pump B", ["CODE"] = "" };
            File.WriteAllText(Path.Combine(Environment.GetEnvironmentVariable("NANOCAD_MCP_HOME")!, "block-write-schemas.json"),
                JsonSerializer.Serialize(new
                {
                    define = BlockWriteSchema.Define(),
                    insert = BlockWriteSchema.Insert(),
                    inputs = new[] { Definition(), empty, Insertion(), fullInsert },
                    exposed_in_mcp = true
                }, Wire.Json));
            return Task.CompletedTask;
        });
        await test("block fingerprints retain nested geometry, attribute fields and operation order", () =>
        {
            var baseline = Define(Definition()); var input = Definition();
            string original = Arguments(baseline).Fingerprint();
            Action<JsonObject>[] changes = {
                x => x["base_point"]![0] = 11, x => x["name"] = "Other",
                x => x["entities"]![0]!["end"]![0] = 21,
                x => x["entities"]![1]!["radius"] = 3,
                x => x["entities"]![2]!["sweep_angle"] = 2,
                x => x["entities"]![3]!["vertices"]![0]!["bulge"] = 0.5,
                x => x["entities"]![4]!["text"] = "Other", x => x["entities"]![4]!["text_style"] = "OtherStyle",
                x => x["attributes"]![0]!["tag"] = "OTHER", x => x["attributes"]![0]!["default_text"] = "Other",
                x => x["attributes"]![0]!["height"] = 2, x => x["attributes"]![0]!["position"]![1] = 24,
                x => x["attributes"]![0]!["rotation"] = 1, x => x["attributes"]![0]!["text_style"] = "OtherStyle" };
            foreach (var change in changes)
            {
                var copy = input.DeepClone().AsObject(); change(copy);
                Check(Arguments(Define(copy)).Fingerprint() != original, "nested content omitted from canonical fingerprint");
            }
            var other = baseline with { Name = "Other" };
            Check(Arguments(baseline, other).Fingerprint() != Arguments(other, baseline).Fingerprint(), "operation order omitted");
            return Task.CompletedTask;
        });
        await test("block insertion canonicalizes numeric handles, defaults, rotations and attribute map order", () =>
        {
            var input = Insertion(); var minimal = Insert(input);
            input["definition_handle"] = "AF"; input["rotation"] = Math.PI * 2; input["scale"] = 1; input["attribute_values"] = new JsonObject();
            Check(Arguments(minimal).Fingerprint() == Arguments(Insert(input)).Fingerprint(), "insertion canonical defaults");
            input["attribute_values"] = new JsonObject { ["NAME"] = "Pump", ["CODE"] = "" };
            var first = Insert(input);
            input["attribute_values"] = new JsonObject { ["CODE"] = "", ["NAME"] = "Pump" };
            Check(Arguments(first).Fingerprint() == Arguments(Insert(input)).Fingerprint(), "map enumeration order changed fingerprint");
            Check(BlockWriteValidation.NormalizeRotation(-Math.PI / 2) == Math.PI * 1.5, "negative rotation normalization");
            input["definition_handle"] = "FFFFFFFFFFFFFFFF";
            Check(Insert(input).DefinitionHandle == "FFFFFFFFFFFFFFFF", "uint64 reference lost");
            return Task.CompletedTask;
        });
        await test("block draft rejects qualified names, nested members, fields, duplicate tags and hidden options", () =>
        {
            foreach (string name in new[] { "*Anonymous", "xref|NAME", " padded ", "bad/name" })
            { var input = Definition(); input["name"] = name; Domain("INVALID_ARGUMENT", () => Define(input)); }
            var layered = Definition(); layered["entities"]![0]!["layer"] = "P4B_TARGET";
            Domain("UNSUPPORTED_BLOCK_CONTENT", () => Define(layered));
            var nested = Definition(); nested["entities"]![0]!["type"] = "BlockReference";
            Domain("INVALID_ARGUMENT", () => Define(nested));
            foreach (string text in new[] { "%<field>%", "%%d", "line\nbreak" })
            { var input = Definition(); input["attributes"]![0]!["default_text"] = text; Domain("INVALID_ARGUMENT", () => Define(input)); }
            foreach (string tag in new[] { "lowercase", "1FIRST", "NAME WITH SPACE", "CODE" })
            { var input = Definition(); input["attributes"]![0]!["tag"] = tag; Domain("INVALID_ARGUMENT", () => Define(input)); }
            var hidden = Definition(); hidden["attributes"]![0]!["constant"] = true;
            Domain("INVALID_ARGUMENT", () => Define(hidden));
            var malformed = Definition(); malformed["entities"]![4]!["text"] = "TOKEN";
            using var json = JsonDocument.Parse(malformed.ToJsonString().Replace("\"TOKEN\"", "\"\\uD800\""));
            Domain("INVALID_ARGUMENT", () => BlockWriteValidation.ParseDefine(json.RootElement));
            var duplicate = Insertion().ToJsonString().TrimEnd('}') + ",\"attribute_values\":{\"NAME\":\"A\",\"NAME\":\"B\"}}";
            using var repeated = JsonDocument.Parse(duplicate);
            Domain("INVALID_ARGUMENT", () => BlockWriteValidation.ParseInsert(repeated.RootElement));
            return Task.CompletedTask;
        });
        await test("block draft counts native objects and preserves unique definition names without arbitrary ceilings", () =>
        {
            var basic = Define(Definition()); var inserted = Insert(Insertion());
            BlockWriteValidation.ValidateBatch(Enumerable.Range(0, 5).Select(i => basic with { Name = "B" + i }));
            BlockWriteValidation.ValidateBatch(Enumerable.Repeat(inserted, 33));
            Domain("INVALID_OPERATION_ORDER", () => BlockWriteValidation.ValidateBatch(new[] { basic, basic with { Name = basic.Name.ToUpperInvariant() } }));
            var large = basic with
            {
                Entities = Enumerable.Repeat(basic.Entities[0], 100).ToArray(),
                Attributes = Enumerable.Range(0, 16).Select(i => basic.Attributes[0] with { Tag = "A" + i }).ToArray()
            };
            var operations = Enumerable.Range(0, 4).Select(i => (StructureEdit)(large with { Name = "B" + i })).ToList();
            operations.AddRange(Enumerable.Range(0, 44).Select(i => new CreateLayerEdit("L" + i, new(null, null, null, null, null, null, null))));
            Check(BlockWriteValidation.ValidateBatch(operations) == 512, "exact 512-object boundary");
            operations.Add(new CreateLayerEdit("extra", new(null, null, null, null, null, null, null)));
            Check(BlockWriteValidation.ValidateBatch(operations) == 513, "native object count above old ceiling");
            BlockWriteValidation.CheckObjectBudget(513); Domain("INVALID_ARGUMENT", () => BlockWriteValidation.CheckObjectBudget(-1));
            var poly = new PolylineSpec(Enumerable.Range(0, 1000).Select(i => new Vertex2(new(i, 0), 0)).ToArray(), false, "0");
            BlockWriteValidation.ValidateBatch(new[] { basic with { Entities = Enumerable.Repeat<EntitySpec>(poly, 11).ToArray() } });
            return Task.CompletedTask;
        });
        await test("block attribute values preserve explicit empty text and deterministic defaults while rejecting unknown tags", () =>
        {
            var definition = Define(Definition());
            var values = BlockWriteValidation.ResolveAttributeValues(definition.Attributes, new Dictionary<string, string> { ["NAME"] = "" });
            Check(values.SequenceEqual(new[] { "", "K-1" }), "defaults or explicit empty value lost");
            Domain("ATTRIBUTE_TAG_MISMATCH", () => BlockWriteValidation.ResolveAttributeValues(definition.Attributes, new Dictionary<string, string> { ["UNKNOWN"] = "X" }));
            Domain("UNSUPPORTED_BLOCK_CONTENT", () => BlockWriteValidation.ResolveAttributeValues(new[] { definition.Attributes[0], definition.Attributes[0] }, new Dictionary<string, string>()));
            Domain("UNSUPPORTED_BLOCK_CONTENT", () => BlockWriteValidation.ResolveAttributeValues(new[] { definition.Attributes[0] with { DefaultText = "%<field>%" } }, new Dictionary<string, string>()));
            return Task.CompletedTask;
        });
        await test("block placement matches independent nonzero-origin rotation and uniform-scale WCS oracle", () =>
        {
            var oracle = Oracle();
            foreach (var node in oracle["placements"]!.AsArray())
            {
                var position = node!["position"]!.AsArray();
                var placement = new BlockPlacement(new(10, 20), new(position[0]!.GetValue<double>(), position[1]!.GetValue<double>()), node["rotation"]!.GetValue<double>(), node["scale"]!.GetValue<double>());
                var expectedMatrix = node["matrix_to_wcs"]!.AsArray().Select(v => v!.GetValue<double>()).ToArray();
                Check(placement.Matrix().Zip(expectedMatrix).All(p => Math.Abs(p.First - p.Second) < 1e-8), "matrix oracle");
                foreach (var point in oracle["analytic_and_ezdxf_checks"]!.AsArray().Where(p => p!["role"]!.GetValue<string>() == node["role"]!.GetValue<string>()))
                {
                    var local = point!["local_point"]!.AsArray(); var wcs = point["wcs"]!.AsArray();
                    var result = placement.Transform(new(local[0]!.GetValue<double>(), local[1]!.GetValue<double>()));
                    Check(Math.Abs(result.X - wcs[0]!.GetValue<double>()) < 1e-8 && Math.Abs(result.Y - wcs[1]!.GetValue<double>()) < 1e-8, "WCS point oracle");
                }
                Check(placement.Size(2) == node["circle_world"]!["radius"]!.GetValue<double>(), "radius uniform scale");
            }
            return Task.CompletedTask;
        });
        await test("block placement rejects overflow, zero or reflected scale", () =>
        {
            foreach (double scale in new[] { -1.0, 0, 1e-7, 1e7 })
            { var input = Insertion(); input["scale"] = scale; Domain("INVALID_GEOMETRY", () => Insert(input)); }
            Domain("INVALID_GEOMETRY", () => BlockWriteValidation.NormalizeRotation(double.NaN));
            Domain("INVALID_GEOMETRY", () => new BlockPlacement(new(0, 0), new(1e9, 0), 0, 2).Transform(new(1e9, 0)));
            Domain("INVALID_GEOMETRY", () => new BlockPlacement(new(0, 0), new(0, 0), 0, 2).Size(1e9));
            Domain("INVALID_GEOMETRY", () => new BlockPlacement(new(0, 0), new(0, 0), 0, 0.5).Size(1e-9));
            return Task.CompletedTask;
        });
        await test("public block parser preserves nested geometry and enforces aggregate limits before IPC", () =>
        {
            var identity = Arguments();
            var root = ProtocolInput(identity.BridgeId, identity.DocumentId, false);
            var parsed = StructureWriteValidation.Parse(Wire.Element(root));
            Check(parsed.Operations.Single() is DefineBlockEdit { Entities.Length: 5, Attributes.Length: 2 }, "root lost block fields");
            root["operations"] = Enumerable.Range(0, 5).Select(i => { var d = Definition(); d["name"] = "B" + i; return d; }).ToArray();
            Check(StructureWriteValidation.Parse(Wire.Element(root)).Operations.Length == 5, "Former definition count ceiling retained");
            root["operations"] = Enumerable.Range(0, 33).Select(_ => Insertion()).ToArray();
            Check(StructureWriteValidation.Parse(Wire.Element(root)).Operations.Length == 33, "Former insertion count ceiling retained");
            root["operations"] = new[] { Definition(), Definition() };
            Domain("INVALID_OPERATION_ORDER", () => StructureWriteValidation.Parse(Wire.Element(root)));
            return Task.CompletedTask;
        });
        await test("block ledger retains child references and detects nested payload conflicts", () =>
        {
            foreach (bool insert in new[] { false, true })
            {
                var identity = Arguments(); var input = ProtocolInput(identity.BridgeId, identity.DocumentId, insert);
                var request = new Request(Wire.Version, Guid.NewGuid().ToString("D"), "modify_structure", 10000, Wire.Element(input));
                var args = StructureWriteValidation.Parse(request.arguments);
                using var queue = new WriteQueue(Environment.CurrentManagedThreadId);
                var (job, _) = queue.Enqueue(request); queue.Take(); Check(job.TryStart(), "block did not start");
                job.BeginCommit(); var result = Result.Ok(request.requestId, ProtocolData(args)); job.RecordCommit(result); job.Complete(result);
                var (same, repeated) = queue.Enqueue(request with { requestId = Guid.NewGuid().ToString("D") });
                var replay = (ModifiedStructure)WriteJob.ForCaller(same.Completion.Result, "caller", repeated).data!;
                Check(replay.replayed && replay.changes[0].children!.Length == (insert ? 2 : 7) && queue.Take() == null, "children lost or replay rescheduled");
                var changed = JsonNode.Parse(request.arguments.GetRawText())!.AsObject();
                if (insert) changed["operations"]![0]!["position"]![0] = 101;
                else changed["operations"]![0]!["entities"]![0]!["end"]![0] = 99;
                Domain("OPERATION_ID_CONFLICT", () => queue.Enqueue(request with { arguments = Wire.Element(changed) }));
            }
            return Task.CompletedTask;
        });
        await test("cancelled and unknown block writes cannot be reexecuted with the same identity", () =>
        {
            foreach (bool insert in new[] { false, true }) foreach (bool unknown in new[] { false, true })
            {
                var identity = Arguments(); var input = ProtocolInput(identity.BridgeId, identity.DocumentId, insert);
                var request = new Request(Wire.Version, Guid.NewGuid().ToString("D"), "modify_structure", 10000, Wire.Element(input));
                using var queue = new WriteQueue(Environment.CurrentManagedThreadId); using var cancel = new CancellationTokenSource();
                var (job, _) = queue.Enqueue(request, cancel.Token); queue.Take();
                if (unknown) { Check(job.TryStart(), "not started"); job.BeginCommit(); job.Complete(job.Error("WRITE_OUTCOME_UNKNOWN", "commit did not return", "unknown")); }
                else { cancel.Cancel(); Check(!job.TryStart(), "cancelled block started"); }
                var (same, repeated) = queue.Enqueue(request);
                Check(repeated && ReferenceEquals(job, same) && queue.Take() == null && same.Completion.Result.error!.code == (unknown ? "WRITE_OUTCOME_UNKNOWN" : "CANCELLED"), "uncertain block reexecuted");
            }
            return Task.CompletedTask;
        });
    }
}
