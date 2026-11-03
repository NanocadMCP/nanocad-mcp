using System.Text.Json;
using System.Text.Json.Nodes;
using NanoCAD.Contracts;
using NanoCAD.Ipc;
using NanoCAD.McpServer;

internal static class StructureWriteTests
{
    private static readonly string Bridge = Guid.NewGuid().ToString("D"), Document = Guid.NewGuid().ToString("D");
    private static void Check(bool condition, string why) { if (!condition) throw new Exception(why); }
    private static void Domain(string code, Action action)
    { try { action(); throw new Exception("Expected " + code); } catch (DomainException e) { Check(e.Error.code == code, "Expected " + code + ", got " + e.Error.code); } }
    public static Dictionary<string, object?> ProtocolInput(string bridge, string document) => new()
    {
        ["bridge_instance_id"] = bridge, ["document_id"] = document, ["expected_revision"] = "1", ["operation_id"] = Guid.NewGuid().ToString("D"), ["units"] = "Millimeters",
        ["operations"] = new object[]
        {
            new { operation = "create_layer", name = "MCP_New", properties = new { color_aci = 2, lineweight_hundredth_mm = 25 } },
            new { operation = "set_layer_properties", handle = "B1", properties = new { is_locked = false } },
            new { operation = "set_attribute_text", block_handle = "CA", attribute_handle = "CB", expected_text = "Pump A-01", text = "Pump B-02" }
        }
    };
    public static ModifiedStructure ProtocolData(StructureWriteArguments args) => new(args.OperationId, "committed", false,
        new[] {
            new StructureChange(0, "create_layer", new(args.BridgeId, args.DocumentId, "DA"), "layer", "MCP_New", true),
            new StructureChange(1, "set_layer_properties", new(args.BridgeId, args.DocumentId, "B1"), "layer", "Editable", false),
            new StructureChange(2, "set_attribute_text", new(args.BridgeId, args.DocumentId, "CB"), "attribute", "NAME", false)
        }, "single_command");
    private static JsonObject Input() => JsonSerializer.SerializeToNode(ProtocolInput(Bridge, Document))!.AsObject();
    private static StructureWriteArguments Parse(JsonObject input) => StructureWriteValidation.Parse(Wire.Element(input));
    private static JsonObject One(object operation)
    {
        var value = Input(); value["operations"] = new JsonArray(JsonSerializer.SerializeToNode(operation)); return value;
    }
    private static Request Request(JsonObject input) => new(Wire.Version, Guid.NewGuid().ToString("D"), "modify_structure", 10000, Wire.Element(input));
    public static async Task Run(Func<string, Func<Task>, Task> test)
    {
        await test("structure writes canonicalize identity, handles and explicit layer defaults", () =>
        {
            var input = Input(); var baseline = Parse(input);
            input["bridge_instance_id"] = Bridge.ToUpperInvariant(); input["expected_revision"] = "0001";
            input["operations"]![1]!["handle"] = "00b1";
            input["operations"]![2]!["block_handle"] = "000ca"; input["operations"]![2]!["attribute_handle"] = "000cb";
            var normalized = Parse(input); Check(baseline.Fingerprint() == normalized.Fingerprint(), "equivalent input has distinct fingerprint");
            var create = (CreateLayerEdit)normalized.Operations[0];
            Check(create.Properties is { ColorAci: 2, Linetype: "Continuous", IsOff: false, IsFrozen: false, IsLocked: false, IsPlottable: true, LineweightHundredthMm: 25 }, "create defaults");
            var empty = One(new { operation = "set_attribute_text", block_handle = "8000000000000001", attribute_handle = "FFFFFFFFFFFFFFFF", expected_text = "", text = "🛠 Pump" });
            Check(Parse(empty).Operations[0] is SetAttributeEdit { ExpectedText: "", Text: "🛠 Pump" }, "empty/Unicode text or 64-bit handles lost");
            return Task.CompletedTask;
        });
        await test("structure fingerprints retain every typed edit and attribute owner", () =>
        {
            var input = Input(); string original = Parse(input).Fingerprint();
            Action<JsonObject>[] changes = {
                value => value["operations"]![0]!["name"] = "Another",
                value => value["operations"]![0]!["properties"]!["color_aci"] = 3,
                value => value["operations"]![0]!["properties"]!["linetype"] = "DASHED",
                value => value["operations"]![0]!["properties"]!["lineweight_hundredth_mm"] = 50,
                value => value["operations"]![0]!["properties"]!["is_plottable"] = false,
                value => value["operations"]![1]!["handle"] = "B2",
                value => value["operations"]![1]!["properties"]!["is_locked"] = true,
                value => value["operations"]![1]!["properties"]!["is_off"] = true,
                value => value["operations"]![1]!["properties"]!["is_frozen"] = true,
                value => value["operations"]![2]!["block_handle"] = "CC",
                value => value["operations"]![2]!["attribute_handle"] = "CD",
                value => value["operations"]![2]!["expected_text"] = "Different old text",
                value => value["operations"]![2]!["text"] = "Different replacement"
            };
            foreach (var change in changes) { var variant = input.DeepClone().AsObject(); change(variant); Check(Parse(variant).Fingerprint() != original, "typed edit was omitted from fingerprint"); }
            return Task.CompletedTask;
        });
        await test("structure validation rejects unsafe symbols, inherited layer properties and hidden actions", () =>
        {
            foreach (string name in new[] { "0", "DEFPOINTS", "xref|layer", "*anonymous", " padded ", "bad/name", "bad\nname" })
                Domain("INVALID_ARGUMENT", () => Parse(One(new { operation = "create_layer", name })));
            foreach (object property in new object[] { new { color_aci = 256 }, new { color_aci = 0 }, new { lineweight_hundredth_mm = -1 }, new { linetype = "ByBlock" }, new { is_locked = "false" }, new { rename = "other" } })
                Domain("INVALID_ARGUMENT", () => Parse(One(new { operation = "set_layer_properties", handle = "B1", properties = property })));
            Domain("INVALID_ARGUMENT", () => Parse(One(new { operation = "create_layer", name = "Valid", properties = new { is_frozen = true } })));
            Domain("INVALID_ARGUMENT", () => Parse(One(new { operation = "erase_layer", handle = "B1" })));
            var duplicate = Input().ToJsonString().TrimEnd('}') + ",\"units\":\"Millimeters\"}";
            using var json = JsonDocument.Parse(duplicate); Domain("INVALID_ARGUMENT", () => StructureWriteValidation.Parse(json.RootElement));
            var malformed = One(new { operation = "set_attribute_text", block_handle = "CA", attribute_handle = "CB", expected_text = "A", text = "TOKEN" }).ToJsonString().Replace("\"TOKEN\"", "\"\\uD800\"");
            using var invalidUnicode = JsonDocument.Parse(malformed);
            Domain("INVALID_ARGUMENT", () => StructureWriteValidation.Parse(invalidUnicode.RootElement));
            var budget = Input(); budget["operations"] = new JsonArray(Enumerable.Range(0,51).Select(i => JsonSerializer.SerializeToNode(new { operation = "create_layer", name = "Layer" + i })).ToArray());
            Check(Parse(budget).Operations.Length==51,"no arbitrary structure batch ceiling");
            return Task.CompletedTask;
        });
        await test("attribute edit order cannot reparent a handle or overwrite an unexpected staged value", () =>
        {
            var input = One(new { operation = "set_attribute_text", block_handle = "CA", attribute_handle = "CB", expected_text = "A", text = "B" });
            input["operations"]!.AsArray().Add(JsonSerializer.SerializeToNode(new { operation = "set_attribute_text", block_handle = "CA", attribute_handle = "CB", expected_text = "B", text = "C" }));
            Check(Parse(input).Operations.Length == 2, "valid ordered chain rejected");
            input["operations"]![1]!["expected_text"] = "A"; Domain("INVALID_OPERATION_ORDER", () => Parse(input));
            input["operations"]![1]!["expected_text"] = "B"; input["operations"]![1]!["block_handle"] = "CC"; Domain("INVALID_OPERATION_ORDER", () => Parse(input));
            foreach (string value in new[] { "%<field>%", "%%c", "line\nbreak", "tab\tvalue", new string('x',4097) })
                Domain("INVALID_ARGUMENT", () => Parse(One(new { operation = "set_attribute_text", block_handle = "CA", attribute_handle = "CB", expected_text = "A", text = value })));
            var duplicate = Input(); duplicate["operations"] = new JsonArray(JsonSerializer.SerializeToNode(new { operation = "create_layer", name = "Test" }), JsonSerializer.SerializeToNode(new { operation = "create_layer", name = "TEST" }));
            Domain("INVALID_OPERATION_ORDER", () => Parse(duplicate));
            return Task.CompletedTask;
        });
        await test("ordered layer permissions are preflight-only and protect current/dependent layers", () =>
        {
            var original = new LayerWriteState("Editable", false, false, false, false, false);
            var locked = original.Change(new(null, null, null, null, null, true, null));
            Domain("LAYER_UNAVAILABLE", locked.RequireAttributeWrite); original.RequireAttributeWrite();
            var unlocked = locked.Change(new(null, null, null, null, null, false, null)); unlocked.RequireAttributeWrite();
            Check(!original.Locked && locked.Locked && !unlocked.Locked, "preflight mutates its original state");
            foreach (var change in new[] { new LayerChanges(null,null,null,true,null,null,null), new(null,null,null,null,true,null,null), new(null,null,null,null,null,true,null) })
                Domain("CURRENT_LAYER_PROTECTED", () => (original with { Current = true }).Change(change));
            Domain("LAYER_UNAVAILABLE", () => (original with { Dependent = true }).Change(new(2,null,null,null,null,null,null)));
            Check((original with { Name = "0" }).Change(new(2,null,null,null,null,null,null)).Name=="0", "Ordinary properties of layer0 were refused");
            (original with { Current = true }).Change(new(2,null,null,null,null,null,null)).RequireAttributeWrite();
            return Task.CompletedTask;
        });
        await test("structural commits share the ledger and replay typed results across connections", () =>
        {
            using var queue = new WriteQueue(Environment.CurrentManagedThreadId);
            var request = Request(Input()); var args = StructureWriteValidation.Parse(request.arguments);
            var (job, replay) = queue.Enqueue(request); Check(!replay && ReferenceEquals(queue.Take(),job) && job.TryStart(), "not dispatched");
            job.BeginCommit(); var result = Result.Ok(job.OriginalRequestId, ProtocolData(args)); job.RecordCommit(result); job.Complete(result);
            var (same, repeated) = queue.Enqueue(request with { requestId = Guid.NewGuid().ToString("D") });
            var historical = WriteJob.ForCaller(same.Completion.Result, "new-connection", repeated);
            Check(historical.data is ModifiedStructure { replayed: true, changes.Length: 3 } && queue.Take() == null, "structural replay reexecuted or lost typed changes");
            var open = new Request(Wire.Version, Guid.NewGuid().ToString("D"), "manage_document", 10000, Wire.Element(new { bridge_instance_id=Bridge, operation_id=args.OperationId, action="open", path=@"C:\test.dwg" }));
            Domain("OPERATION_ID_CONFLICT", () => queue.Enqueue(open));
            var other = JsonNode.Parse(request.arguments.GetRawText())!.AsObject(); other["operations"]![0]!["name"]="Another";
            Domain("OPERATION_ID_CONFLICT", () => queue.Enqueue(Request(other)));
            return Task.CompletedTask;
        });
        await test("cancelled and unknown structural jobs cannot be silently reexecuted", () =>
        {
            using var queue = new WriteQueue(Environment.CurrentManagedThreadId);
            using var cancel = new CancellationTokenSource();
            var request = Request(Input()); var (job,_) = queue.Enqueue(request,cancel.Token); queue.Take(); cancel.Cancel();
            Check(!job.TryStart() && job.Completion.Result.error!.code == "CANCELLED", "late structural write can still start");
            var (same,replayed)=queue.Enqueue(request); Check(replayed && ReferenceEquals(same,job) && queue.Take()==null,"cancelled operation was rescheduled");
            var unknownRequest=Request(Input()); var (unknown,_) = queue.Enqueue(unknownRequest); queue.Take(); Check(unknown.TryStart(),"not running"); unknown.BeginCommit();
            unknown.Complete(unknown.Error("WRITE_OUTCOME_UNKNOWN","native commit did not return","unknown"));
            var (retained, repeated)=queue.Enqueue(unknownRequest); Check(repeated && retained.Completion.Result.error!.code=="WRITE_OUTCOME_UNKNOWN" && queue.Take()==null,"unknown structural outcome was retried");
            return Task.CompletedTask;
        });
        await test("invalid structural writes never cross the MCP to IPC boundary", async () =>
        {
            int calls=0;
            using var listener = new BridgeListener(Discovery.Describe(Bridge,"structure-test-no-CAD"),(request,_)=>{calls++;return Task.FromResult(Result.Ok(request.requestId,new{}));},new JsonLog("structure-write-test"));
            var input = JsonSerializer.Deserialize<Dictionary<string,JsonElement>>(Input().ToJsonString())!;
            input["command"] = Wire.Element("_UNDO");
            var result = await new ToolService().CallAsync("modify_structure",input,default);
            Check(result.IsError==true && calls==0,"hidden command reached the bridge");
        });
    }
}
