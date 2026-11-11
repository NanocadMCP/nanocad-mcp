using System.Text.Json;
using System.Text.Json.Nodes;
using System.Reflection;
using NanoCAD.Contracts;
using NanoCAD.McpServer;

internal static class ProjectedDimensionCreateTests
{
    private static readonly string[] NewKinds = ["projected_radial", "projected_arc_length", "projected_angular"];

    private static JsonObject Entity(string kind) => new()
    {
        ["type"] = "Dimension",
        ["kind"] = kind,
        ["view_handle"] = "000a1",
        ["arc_index"] = 3,
        ["dimline_point"] = new JsonArray(42.5, 90)
    };

    private static CreateArguments Parse(JsonObject entity, string space = "paper") => CreateValidation.Parse(Wire.Element(new
    {
        bridge_instance_id = "11111111-1111-4111-8111-111111111111",
        document_id = "22222222-2222-4222-8222-222222222222",
        operation_id = "33333333-3333-4333-8333-333333333333",
        expected_revision = "7",
        units = "Millimeters",
        target_space = space,
        entities = new[] { entity }
    }));

    private static void Need(bool condition, string reason)
    { if (!condition) throw new Exception(reason); }

    private static void Reject(Action action, string? expectedCode = null)
    {
        try { action(); }
        catch (DomainException error)
        {
            Need(expectedCode is null || error.Error.code == expectedCode, "Wrong refusal: " + error.Error.code);
            return;
        }
        throw new Exception("Invalid projected dimension input was accepted.");
    }

    private static JsonObject SchemaBranch(string kind) => new()
    {
        ["type"] = "object",
        ["additionalProperties"] = false,
        ["properties"] = new JsonObject
        {
            ["type"] = new JsonObject { ["const"] = "Dimension" },
            ["kind"] = new JsonObject { ["type"] = "string", ["const"] = kind },
            ["arc_index"] = new JsonObject { ["type"] = "integer", ["minimum"] = 0, ["maximum"] = int.MaxValue },
            ["view_handle"] = new JsonObject { ["type"] = "string", ["pattern"] = "^[0-9A-Fa-f]{1,16}$" }
        },
        ["required"] = new JsonArray("type", "kind", "arc_index", "view_handle")
    };

    private static JsonElement Schema(params JsonObject[] branches) => JsonSerializer.SerializeToElement(new JsonObject
    {
        ["type"] = "object",
        ["additionalProperties"] = false,
        ["properties"] = new JsonObject
        {
            ["entities"] = new JsonObject
            {
                ["type"] = "array",
                ["minItems"] = 1,
                ["items"] = new JsonObject { ["oneOf"] = new JsonArray(branches.Select(branch => (JsonNode?)branch).ToArray()) }
            }
        }
    });

    private static JsonElement Share(JsonElement input) => (JsonElement)typeof(ToolCatalog).Assembly
        .GetType("NanoCAD.McpServer.SchemaCompactor")!.GetMethod("ShareCreationKindAlternatives", BindingFlags.Public | BindingFlags.Static)!
        .Invoke(null, [input])!;

    internal static async Task Run(Func<string, Func<Task>, Task> test)
    {
        await test("projected circular dimensions retain actual primitive index and explicit paper placement", () =>
        {
            foreach (string kind in NewKinds)
            {
                var args = Parse(Entity(kind));
                var spec = args.Entities.Single();
                Need(args.TargetSpace == "paper" && spec.Type == "Dimension" && spec.Layer == "0", "Default native drawing identity changed.");
                bool expected = kind switch
                {
                    "projected_radial" => spec is ProjectedRadialDimensionSpec { ViewHandle: "A1", ArcIndex: 3, DimLinePoint: { X: 42.5, Y: 90 } },
                    "projected_arc_length" => spec is ProjectedArcLengthDimensionSpec { ViewHandle: "A1", ArcIndex: 3, DimLinePoint: { X: 42.5, Y: 90 } },
                    _ => spec is ProjectedAngularDimensionSpec { ViewHandle: "A1", ArcIndex: 3, DimLinePoint: { X: 42.5, Y: 90 } }
                };
                Need(expected && spec.Workplane is null && spec is not IDimensionCreation, "The projected route became independent coordinate construction.");
                var highest = Entity(kind); highest["arc_index"] = int.MaxValue;
                Need(Parse(highest).Entities.Length == 1, "An arbitrary geometry-index ceiling was introduced.");
                Reject(() => Parse(Entity(kind), "model"), "OUT_OF_SCOPE");
            }
            return Task.CompletedTask;
        });
        await test("projected circular dimensions reject coordinate aliases and malformed primitive selectors", () =>
        {
            foreach (string kind in NewKinds)
            {
                Action<JsonObject>[] malformed =
                [
                    item => item["arc_index"] = -1,
                    item => item["arc_index"] = .5,
                    item => item["arc_index"] = (long)int.MaxValue + 1,
                    item => item["arc_index"] = "3",
                    item => item.Remove("arc_index"),
                    item => item["view_handle"] = "VIEW_A",
                    item => item["view_handle"] = "0",
                    item => item.Remove("view_handle"),
                    item => item["dimline_point"] = new JsonArray(1, 2, 0),
                    item => item["dimline_point"] = new JsonArray(1),
                    item => item["dimline_point"] = new JsonArray(1e10, 2),
                    item => item.Remove("dimline_point"),
                    item => item["line_index"] = 3,
                    item => item["center"] = new JsonArray(0, 0),
                    item => item["dimension_style"] = "Standard",
                    item => item["text_override"] = "<>",
                    item => item["dimline_point_snap"] = new JsonObject { ["handle"] = "B1" },
                    item => item["workplane"] = new JsonObject(),
                    item => item["source_edge_key"] = "3"
                ];
                foreach (var change in malformed)
                {
                    var item = Entity(kind); change(item); Reject(() => Parse(item));
                }
            }
            return Task.CompletedTask;
        });
        await test("projected dimension replay distinguishes radius diameter length and angle semantics", () =>
        {
            var kinds = new[] { "projected_diametric" }.Concat(NewKinds).ToArray();
            var fingerprints = kinds.Select(kind => Parse(Entity(kind)).Fingerprint()).ToArray();
            Need(fingerprints.Distinct(StringComparer.Ordinal).Count() == kinds.Length,
                "Same-shaped projected records lost their different native kinds in replay identity.");
            foreach (string kind in kinds)
            {
                var baseline = Parse(Entity(kind));
                var normalized = new JsonObject
                {
                    ["dimline_point"] = new JsonArray(42.5, 90d),
                    ["arc_index"] = 3,
                    ["view_handle"] = "a1",
                    ["layer"] = "0",
                    ["kind"] = kind,
                    ["type"] = "Dimension"
                };
                Need(Parse(normalized).Fingerprint() == baseline.Fingerprint(), "Equivalent field order/default/handle spelling changed replay intent.");
                Action<JsonObject>[] changes =
                [
                    item => item["view_handle"] = "A2", item => item["arc_index"] = 4,
                    item => item["dimline_point"]![0] = 43, item => item["dimline_point"]![1] = 91,
                    item => item["layer"] = "Dimensions"
                ];
                foreach (var change in changes)
                {
                    var item = Entity(kind); change(item);
                    Need(Parse(item).Fingerprint() != baseline.Fingerprint(), "Projected identity or requested placement was omitted from replay intent.");
                }
            }
            return Task.CompletedTask;
        });
        await test("projected circular dimension focused help retains native selectors without ordinary snap inputs", () =>
        {
            foreach (string kind in NewKinds)
            {
                var item = AuditServerContractSupport.Branch("create_entities", kind);
                var fields = item.GetProperty("properties");
                Need(fields.GetProperty("kind").GetProperty("const").GetString() == kind &&
                    fields.GetProperty("arc_index").GetProperty("maximum").GetInt32() == int.MaxValue &&
                    fields.TryGetProperty("view_handle", out _) && fields.TryGetProperty("dimline_point", out _) &&
                    !fields.EnumerateObject().Any(field => field.Name.EndsWith("_snap", StringComparison.Ordinal)),
                    "Focused projected fields changed or acquired unrelated coordinate bindings.");
                var required = item.GetProperty("required").EnumerateArray().Select(field => field.GetString()).ToHashSet(StringComparer.Ordinal);
                Need(required.SetEquals(["type", "kind", "view_handle", "arc_index", "dimline_point"]) &&
                    !item.GetProperty("additionalProperties").GetBoolean(), "Projected item closure or mandatory native references were lost.");
                string description = item.GetProperty("description").GetString()!;
                Need(description.Contains("1:1", StringComparison.Ordinal) && description.Contains("Paper", StringComparison.Ordinal) &&
                    description.Contains("original-view", StringComparison.Ordinal), "Native view association/owner scope is undiscoverable.");
            }
            return Task.CompletedTask;
        });
        await test("creation kind sharing preserves exact closed alternatives and all callable constraints", () =>
        {
            var distinct = SchemaBranch("different"); distinct["properties"]!["arc_index"]!["maximum"] = 12;
            var original = Schema(SchemaBranch("radius"), distinct, SchemaBranch("length"), SchemaBranch("angle"));
            string untouched = original.GetRawText();
            var published = Share(original);
            var branches = published.GetProperty("properties").GetProperty("entities").GetProperty("items").GetProperty("oneOf");
            Need(branches.GetArrayLength() == 2, "Identical nonadjacent creation variants were not shared.");
            var kinds = branches[0].GetProperty("properties").GetProperty("kind");
            Need(!kinds.TryGetProperty("const", out _) && kinds.GetProperty("type").GetString() == "string" &&
                kinds.GetProperty("enum").EnumerateArray().Select(kind => kind.GetString()).SequenceEqual(["radius", "length", "angle"]),
                "Publication lost the callable discriminator names/type.");
            Need(branches[0].GetProperty("properties").GetProperty("arc_index").GetProperty("maximum").GetInt32() == int.MaxValue &&
                !branches[0].GetProperty("additionalProperties").GetBoolean() &&
                branches[0].GetProperty("required").EnumerateArray().Any(field => field.GetString() == "kind"),
                "Sharing dropped field constraints, required kind or item closure.");
            var restored = AuditServerContractSupport.RestoreCreationKindAlternatives(original, published);
            Need(JsonNode.DeepEquals(JsonNode.Parse(original.GetRawText()), JsonNode.Parse(restored.GetRawText())) &&
                original.GetRawText() == untouched, "Sharing changed exact alternatives, the request envelope or the source contract.");
            Need(published.GetRawText().Length < original.GetRawText().Length && !published.GetRawText().Contains("\"$ref\"", StringComparison.Ordinal),
                "Sharing did not reduce the catalog or introduced unresolved references.");
            return Task.CompletedTask;
        });
        await test("creation kind sharing retains ambiguous missing open or conflicting discriminator semantics", () =>
        {
            foreach (string mode in new[] { "duplicate", "missing", "open", "patterned", "range", "enum" })
            {
                var first = SchemaBranch("radius"); var second = SchemaBranch("length");
                switch (mode)
                {
                    case "duplicate": second["properties"]!["kind"]!["const"] = "radius"; break;
                    case "missing": second["required"]!.AsArray().RemoveAt(1); break;
                    case "open": second["additionalProperties"] = true; break;
                    case "patterned": second["patternProperties"] = new JsonObject(); break;
                    case "range": second["properties"]!["arc_index"]!["maximum"] = 12; break;
                    case "enum": second["properties"]!["kind"]!["enum"] = new JsonArray("length"); break;
                }
                var original = Schema(first, second); var published = Share(original);
                Need(JsonNode.DeepEquals(JsonNode.Parse(original.GetRawText()), JsonNode.Parse(published.GetRawText())),
                    "Creation kind sharing changed a non-equivalent or ambiguous schema: " + mode);
            }
            return Task.CompletedTask;
        });
    }
}
