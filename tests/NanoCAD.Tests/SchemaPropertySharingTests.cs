using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using NanoCAD.McpServer;

internal static class SchemaPropertySharingTests
{
    private static JsonObject Input()
    {
        JsonObject Snap() => new()
        {
            ["type"] = "object",
            ["additionalProperties"] = false,
            ["properties"] = new JsonObject
            {
                ["handle"] = new JsonObject { ["type"] = "string", ["pattern"] = "^[0-9A-Fa-f]{1,16}$" },
                ["expected_curve_token"] = new JsonObject { ["type"] = "string", ["pattern"] = "^[0-9A-Fa-f]{64}$" },
                ["snap"] = new JsonObject { ["enum"] = new JsonArray("start", "end", "mid", "center", "nearest") },
                ["reference_point_wcs"] = new JsonObject
                {
                    ["type"] = "array",
                    ["minItems"] = 3,
                    ["maxItems"] = 3,
                    ["items"] = new JsonObject { ["type"] = "number" }
                }
            },
            ["required"] = new JsonArray("handle", "expected_curve_token", "snap"),
            ["allOf"] = new JsonArray(new JsonObject
            {
                ["if"] = new JsonObject { ["properties"] = new JsonObject { ["snap"] = new JsonObject { ["const"] = "nearest" } } },
                ["then"] = new JsonObject { ["required"] = new JsonArray("reference_point_wcs") },
                ["else"] = new JsonObject { ["properties"] = new JsonObject { ["reference_point_wcs"] = false } }
            })
        };
        JsonObject Point(int dimensions) => new()
        {
            ["type"] = "array",
            ["minItems"] = dimensions,
            ["maxItems"] = dimensions,
            ["items"] = new JsonObject { ["type"] = "number", ["minimum"] = -1e9, ["maximum"] = 1e9 }
        };
        JsonObject Branch(string name, int dimensions, bool snapped) => new()
        {
            ["type"] = "object",
            ["additionalProperties"] = false,
            ["properties"] = snapped ? new JsonObject
            {
                ["type"] = new JsonObject { ["const"] = name },
                ["center"] = Point(dimensions),
                ["center_snap"] = Snap(),
                ["bounded"] = new JsonObject { ["type"] = "number", ["minimum"] = 0, ["maximum"] = dimensions }
            } : new JsonObject { ["type"] = new JsonObject { ["const"] = name } },
            ["required"] = new JsonArray("type"),
            ["allOf"] = snapped ? new JsonArray(new JsonObject
            {
                ["oneOf"] = new JsonArray(
                new JsonObject { ["required"] = new JsonArray("center") },
                new JsonObject { ["required"] = new JsonArray("center_snap") })
            }) : new JsonArray()
        };
        return new JsonObject
        {
            ["type"] = "object",
            ["additionalProperties"] = false,
            ["properties"] = new JsonObject
            {
                ["entities"] = new JsonObject
                {
                    ["type"] = "array",
                    ["items"] = new JsonObject
                    { ["oneOf"] = new JsonArray(Branch("A", 2, true), Branch("B", 3, true), Branch("C", 2, false)) }
                }
            }
        };
    }

    private static JsonElement Publish(JsonObject source)
    {
        var publish = typeof(ToolCatalog).Assembly.GetType("NanoCAD.McpServer.SchemaCompactor")!
            .GetMethod("PublishInput", BindingFlags.Public | BindingFlags.Static)!;
        return (JsonElement)publish.Invoke(null, ["create_entities", JsonSerializer.SerializeToElement(source)])!;
    }

    private static void Need(bool condition, string message)
    { if (!condition) throw new Exception(message); }

    private static void ExactBranches(JsonObject source, JsonElement output)
    {
        var original = source["properties"]!["entities"]!["items"]!["oneOf"]!.AsArray();
        var items = output.GetProperty("properties").GetProperty("entities").GetProperty("items");
        int index = 0;
        foreach (var branch in items.GetProperty("oneOf").EnumerateArray())
        {
            var restored = AuditServerContractSupport.RestoreSharedItemProperties(items, branch);
            Need(JsonNode.DeepEquals(original[index++], AuditServerRoutingTests.WithoutDescriptions(restored)),
                "Shared schema publication changed a field, required alternative or branch closure.");
        }
        Need(index == original.Count, "Shared schema publication changed the available variants.");
    }

    public static async Task Run(Func<string, Func<Task>, Task> test)
    {
        await test("schema property sharing retains exact branch validation and discoverable snap inputs", () =>
        {
            var source = Input(); var output = Publish(source);
            var items = output.GetProperty("properties").GetProperty("entities").GetProperty("items");
            var shared = items.GetProperty("properties");
            Need(shared.GetProperty("center_snap").GetProperty("properties").TryGetProperty("expected_curve_token", out _),
                "The current-token field disappeared from the callable item properties.");
            Need(!shared.TryGetProperty("center", out _) && !shared.TryGetProperty("bounded", out _),
                "Different XY/XYZ or numeric-range contracts were combined by property name.");
            var first = items.GetProperty("oneOf")[0]; var third = items.GetProperty("oneOf")[2];
            Need(first.GetProperty("properties").GetProperty("center_snap").GetProperty("type").GetString() == "object" &&
                !third.GetProperty("properties").TryGetProperty("center_snap", out _) &&
                !third.GetProperty("additionalProperties").GetBoolean(),
                "Sharing hid the snap type or admitted a snap to an unrelated closed variant.");
            ExactBranches(source, output);
            Need(!output.GetRawText().Contains("\"$ref\"", StringComparison.Ordinal), "Input sharing added unresolved client references.");
            return Task.CompletedTask;
        });
        await test("schema property sharing skips open patterned or conflicting alternatives", () =>
        {
            foreach (string mode in new[] { "open", "patterned", "conflicting", "missing_conditions" })
            {
                var source = Input(); var branch = source["properties"]!["entities"]!["items"]!["oneOf"]![1]!.AsObject();
                if (mode == "open") branch["additionalProperties"] = true;
                if (mode == "patterned") branch["patternProperties"] = new JsonObject();
                if (mode == "conflicting") branch["properties"]!["center_snap"]!["required"]!.AsArray().Add("different");
                if (mode == "missing_conditions") branch["properties"]!["center_snap"]!.AsObject().Remove("allOf");
                var output = Publish(source);
                var items = output.GetProperty("properties").GetProperty("entities").GetProperty("items");
                Need(!items.TryGetProperty("properties", out var shared) || !shared.TryGetProperty("center_snap", out _),
                    "Sharing changed an unconstrained occurrence or erased a distinct snap contract: " + mode);
                ExactBranches(source, output);
            }
            return Task.CompletedTask;
        });
        await test("schema snap pattern sharing retains complete callable fields and exact mode constraints", () =>
        {
            JsonObject DifferentNames()
            {
                var source = Input();
                var second = source["properties"]!["entities"]!["items"]!["oneOf"]![1]!.AsObject();
                var fields = second["properties"]!.AsObject(); var snap = fields["center_snap"]!.DeepClone();
                fields.Remove("center_snap"); fields["target_snap"] = snap;
                second["allOf"]![0]!["oneOf"]![1]!["required"] = new JsonArray("target_snap");
                return source;
            }
            var source = DifferentNames(); var output = Publish(source);
            var items = output.GetProperty("properties").GetProperty("entities").GetProperty("items");
            var shared = items.GetProperty("patternProperties").EnumerateObject().Single();
            Need(System.Text.RegularExpressions.Regex.IsMatch("center_snap", shared.Name) &&
                System.Text.RegularExpressions.Regex.IsMatch("target_snap", shared.Name) &&
                !System.Text.RegularExpressions.Regex.IsMatch("prefix_center_snap", shared.Name) &&
                !System.Text.RegularExpressions.Regex.IsMatch("center_snap_suffix", shared.Name), "Shared snap validation is not anchored to exact existing field names.");
            Need(shared.Value.GetProperty("allOf")[0].GetProperty("then").GetProperty("required")[0].GetString() == "reference_point_wcs" &&
                shared.Value.GetProperty("properties").GetProperty("expected_curve_token").GetProperty("pattern").GetString() == "^[0-9A-Fa-f]{64}$",
                "Native mode requirements or geometry token format were removed.");
            foreach (var branch in items.GetProperty("oneOf").EnumerateArray().Take(2))
            {
                var snap = branch.GetProperty("properties").EnumerateObject().Single(field => field.Name.EndsWith("_snap", StringComparison.Ordinal)).Value;
                Need(snap.GetProperty("properties").GetProperty("expected_curve_token").GetProperty("type").GetString() == "string" &&
                    snap.GetProperty("required").GetArrayLength() == 3 && !snap.GetProperty("additionalProperties").GetBoolean(),
                    "Sharing hid callable nested fields, required inputs or closure.");
            }
            ExactBranches(source, output);
            var conflict = DifferentNames();
            conflict["properties"]!["entities"]!["items"]!["oneOf"]![1]!["properties"]!["target_snap"]!["properties"]!["expected_curve_token"]!["pattern"] = "different";
            var unchanged = Publish(conflict);
            Need(!unchanged.GetProperty("properties").GetProperty("entities").GetProperty("items").TryGetProperty("patternProperties", out _),
                "Distinct snap contracts were constrained by a shared pattern.");
            ExactBranches(conflict, unchanged);
            return Task.CompletedTask;
        });
    }
}
