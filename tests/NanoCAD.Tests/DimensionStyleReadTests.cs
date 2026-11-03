using System.Text.Json;
using NanoCAD.Contracts;
using NanoCAD.McpServer;

internal static class DimensionStyleReadTests
{
    private static readonly EntityRef Owner = new("11111111-1111-4111-8111-111111111111", "22222222-2222-4222-8222-222222222222", "45");
    private static DimensionDependency Null(string kind) => new(kind, "null", null, null, null);
    private static DimensionColor Color(int native) => new(DimensionStyleMetadata.ColorMethod(native), native,
        native == 195 ? (short)256 : null, native == 194 ? new(17, 34, 51) : null, false, "", "");
    private static DimensionStyleProperties Example() => new(false, 1, 2.5, 2.5,
        new() { dimgap = -0.625, dimlfac = -2, dimdsep = 44, dimpost = "<> mm", dimapost = "[] in" },
        new(Null("block"), Null("block"), Null("block"), Null("block"), Null("linetype"), Null("linetype"), Null("linetype"),
            new("text_style", "resolved", Owner with { handle = "42" }, "P4D_Text", null)),
        new(Color(192), Color(194), Color(195), Color(200)),
        new(new("by_layer", -1), new("default", -3)), "not_applicable", 2, false, false, false, false);
    private static DimensionStyleProperties Validate(DimensionStyleProperties value, string name = "P4D_Base") => DimensionStyleMetadata.Validate(name, Owner, value);
    public static StructurePage ProtocolData(string bridge, string document)
    {
        var owner = Owner with { bridge_instance_id = bridge, document_id = document };
        var value = Example();
        value = value with { references = value.references with { text_style = value.references.text_style with { @ref = owner with { handle = "42" } } } };
        DimensionStyleMetadata.Validate("P4D_Base", owner, value);
        return new("dimension_styles", new[] { new StructureRecord(owner, "dimension_styles", "P4D_Base", value) }, 1, 1, true, null);
    }
    private static void Check(bool value, string message) { if (!value) throw new Exception(message); }
    private static void Domain(string code, Action action)
    {
        try { action(); throw new Exception("Expected " + code); }
        catch (DomainException error) { Check(error.Error.code == code, error.Error.code + " instead of " + code); }
    }
    public static async Task Run(Func<string, Func<Task>, Task> test)
    {
        await test("dimension draft maps all 73 selected SDK fields to exact DTO types", () =>
        {
            using var catalog = JsonDocument.Parse(File.ReadAllText("docs/research/dimension-style-read-catalog.json"));
            int count = 0, variables = 0;
            foreach (var field in catalog.RootElement.GetProperty("fields").EnumerateArray())
            {
                Type type = typeof(DimensionStyleProperties);
                foreach (string segment in field.GetProperty("path").GetString()!.Split('.'))
                    type = type.GetProperty(segment)?.PropertyType ?? throw new Exception("Missing " + segment);
                Type expected = field.GetProperty("native_type").GetString() switch {
                    "Int32" or "Char" => typeof(int), "Double" => typeof(double), "Boolean" => typeof(bool), "String" => typeof(string),
                    "Teigha.DatabaseServices.ObjectId" => typeof(DimensionDependency), "Teigha.Colors.Color" => typeof(DimensionColor),
                    "Teigha.DatabaseServices.LineWeight" => typeof(DimensionLineweight), _ => throw new Exception("Unmapped SDK type") };
                Check(type == expected, field.ToString()); count++;
                if (field.GetProperty("category").GetString() == "variable") variables++;
            }
            Check(count == 73 && variables == 56 && typeof(DimensionVariables).GetProperties().Length == 56, "complete reviewed variable surface");
            
            return Task.CompletedTask;
        });
        await test("dimension draft preserves signed values and rejects every nonfinite numeric field", () =>
        {
            var baseline = Example(); Check(Validate(baseline) == baseline, "no normalization");
            foreach (Type type in new[] { typeof(DimensionStyleProperties), typeof(DimensionVariables) })
            foreach (var property in type.GetProperties().Where(p => p.PropertyType == typeof(double)))
            {
                foreach (double number in new[] { 0.0, -1.25, double.Epsilon, double.MaxValue, double.NaN, double.PositiveInfinity, double.NegativeInfinity })
                {
                    var value = Example(); property.SetValue(type == typeof(DimensionVariables) ? value.variables : value, number);
                    if (double.IsFinite(number)) Validate(value);
                    else Domain("OBJECT_READ_FAILED", () => Validate(value));
                }
            }
            Validate(baseline with { variables = baseline.variables with { dimadec = int.MinValue, dimzin = int.MaxValue } });
            return Task.CompletedTask;
        });
        await test("dimension text bounds count UTF-16 and separator preserves every code unit", () =>
        {
            var value = Example();
            foreach (int code in new[] { 0, 44, 0xD800, 0xDC00, 65535 }) Validate(value with { variables = value.variables with { dimdsep = code } });
            foreach (int code in new[] { -1, 65536 }) Domain("OBJECT_READ_FAILED", () => Validate(value with { variables = value.variables with { dimdsep = code } }));
            string astral = string.Concat(Enumerable.Repeat("\U0001F600", 2048));
            foreach (string text in new[] { "", "  <> mm\\P[]  ", new string('a', 4096), astral })
                Check(Validate(value with { variables = value.variables with { dimpost = text, dimapost = text } }).variables.dimpost == text, "exact text");
            foreach (string text in new[] { new string('a', 4097), astral + "x" })
                Domain("ENTITY_TOO_LARGE", () => Validate(value with { variables = value.variables with { dimapost = text } }));
            foreach (string text in new[] { "\uD800", "\uDC00", "A\uD800B" })
                Domain("OBJECT_READ_FAILED", () => Validate(value with { variables = value.variables with { dimpost = text } }));
            return Task.CompletedTask;
        });
        await test("dimension references never assert an identity for unavailable or foreign dependencies", () =>
        {
            var value = Example();
            foreach (string reason in DimensionStyleMetadata.DependencyReasons)
                Validate(value with { references = value.references with { arrow = new("block", "unavailable", null, null, reason) } });
            var reference = value.references.text_style;
            foreach (var bad in new[] { reference with { target_kind = "block" }, reference with { reason = "wrong_owner" },
                reference with { @ref = null }, reference with { name = null }, reference with { state = "guessed" },
                reference with { @ref = reference.@ref! with { document_id = Guid.NewGuid().ToString("D") } },
                reference with { @ref = reference.@ref! with { bridge_instance_id = Guid.NewGuid().ToString("D") } },
                reference with { @ref = reference.@ref! with { handle = "00A" } }, reference with { @ref = reference.@ref! with { handle = "0" } },
                new("text_style", "null", reference.@ref, null, null), new("text_style", "unavailable", null, null, "missing_guess") })
                Domain("OBJECT_READ_FAILED", () => Validate(value with { references = value.references with { text_style = bad } }));
            return Task.CompletedTask;
        });
        await test("dimension enum labels retain unknown values, named colors and negative lineweights", () =>
        {
            using var catalog = JsonDocument.Parse(File.ReadAllText("docs/research/dimension-style-read-catalog.json"));
            var enums = catalog.RootElement.GetProperty("enums").EnumerateArray().ToDictionary(e => e.GetProperty("name").GetString()!);
            var weights = enums["LineWeight"].GetProperty("constants").EnumerateArray().Select(e => e.GetProperty("value").GetInt32()).ToArray();
            Check(weights.Order().SequenceEqual(DimensionStyleMetadata.ExplicitLineweights.Concat(new[] { -1, -2, -3 }).Order()), "installed lineweight enumeration");
            foreach (int native in weights.Concat(new[] { -4, 1, int.MaxValue }))
            {
                var value = Example(); var weight = new DimensionLineweight(DimensionStyleMetadata.WeightMode(native), native);
                Validate(value with { lineweights = new(weight, weight) });
            }
            foreach (int native in Enumerable.Range(192, 9).Concat(new[] { int.MinValue, 201 }))
            {
                var value = Example(); var color = Color(native) with { has_color_name = true, color_name = "Named colour", book_name = "Book" };
                Check(Validate(value with { colors = new(color, color, color, color) }).colors.text == color, "named/raw color retained");
            }
            foreach (int native in new[] { 0, 1, 2, -1, int.MaxValue }) Validate(Example() with { annotative_native_value = native, annotative_state = DimensionStyleMetadata.Annotative(native) });
            Domain("OBJECT_READ_FAILED", () => Validate(Example() with { annotative_state = "true" }));
            Domain("OBJECT_READ_FAILED", () => Validate(Example() with { lineweights = new(new("explicit", -1), new("default", -3)) }));
            var baseline = Example();
            Domain("OBJECT_READ_FAILED", () => Validate(baseline with { colors = baseline.colors with { dimension = Color(192) with { rgb = new(0, 0, 0) } } }));
            Domain("OBJECT_READ_FAILED", () => Validate(baseline with { colors = baseline.colors with { text = Color(195) with { aci = null } } }));
            return Task.CompletedTask;
        });
        await test("dimension family suffix is a naming observation and draft schema remains standalone", () =>
        {
            foreach (string name in new[] { "Base$0", "Base$123", "NameΩ$0001" }) Check(DimensionStyleMetadata.FamilyNameCandidate(name), name);
            foreach (string name in new[] { "$1", "Base$", "Base$12x", "Base$１２", "Base$-1", "Base$١" }) Check(!DimensionStyleMetadata.FamilyNameCandidate(name), name);
            Validate(Example() with { family_name_candidate = true, has_fields = true, has_xdata = true, has_extension_dictionary = true }, "Base$3");
            var examples = new List<DimensionStyleProperties> { Validate(Example()) };
            foreach (int native in Enumerable.Range(192, 9).Append(201))
            {
                var color = Color(native); examples.Add(Validate(Example() with { colors = new(color, color, color, color) }));
            }
            foreach (int native in DimensionStyleMetadata.ExplicitLineweights.Concat(new[] { -1, -2, -3, -4, 1 }))
            {
                var weight = new DimensionLineweight(DimensionStyleMetadata.WeightMode(native), native);
                examples.Add(Validate(Example() with { lineweights = new(weight, weight) }));
            }
            foreach (string reason in DimensionStyleMetadata.DependencyReasons)
            {
                var value = Example(); examples.Add(Validate(value with { references = value.references with { arrow = new("block", "unavailable", null, null, reason) } }));
            }
            File.WriteAllText(Path.Combine(Environment.GetEnvironmentVariable("NANOCAD_MCP_HOME")!, "dimension-style-read-draft.json"),
                JsonSerializer.Serialize(new { properties = DimensionStyleReadSchema.Properties(), examples,
                    exposed_in_mcp = true, native_executed = false, selected_sdk_fields = 73, typed_variables = 56 }, Wire.Json));
            return Task.CompletedTask;
        });
    }
}











