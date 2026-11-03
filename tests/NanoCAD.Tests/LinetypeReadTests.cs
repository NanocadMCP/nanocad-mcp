using System.Text.Json;
using NanoCAD.Contracts;
using NanoCAD.McpServer;

internal static class LinetypeReadTests
{
    private static readonly EntityRef Owner = new("11111111-1111-4111-8111-111111111111", "22222222-2222-4222-8222-222222222222", "AB");
    private static readonly LinetypeStyleReference Null = new("text_style", "null", null, null, null);
    private static void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
    private static void Domain(string code, Action action)
    { try { action(); throw new Exception("Expected " + code); } catch (DomainException e) { Check(e.Error.code == code, e.Error.code + " instead of " + code); } }
    public static LinetypeProperties Example() => new(false, "Dash — GAS", 10, 3, false, new[] {
        new LinetypeElement(0, 5, "", 0, Null, new[] { 0d, 0d }, 0, 0, false),
        new LinetypeElement(1, -2, "GAS", 0, new("text_style", "resolved", Owner with { handle = "2F" }, "Text", null), new[] { .3, -.2 }, Math.PI / 6, .75, false),
        new LinetypeElement(2, -3, "", 0, Null, new[] { 0d, 0d }, 0, 0, false) }, false, false, false);
    public static StructurePage ProtocolData(string bridge, string document)
    {
        var owner = Owner with { bridge_instance_id = bridge, document_id = document };
        var value = Example(); value.elements[1] = value.elements[1] with { style = value.elements[1].style with { @ref = owner with { handle = "2F" } } };
        LinetypeMetadata.Validate(owner, value);
        return new("linetypes", new[] { new StructureRecord(owner, "linetypes", "MCP_Linetype", value) }, 1, 1, true, null);
    }
    public static async Task Run(Func<string, Func<Task>, Task> test)
    {
        await test("linetype metadata preserves signed raw pattern values and complex references", () =>
        {
            var value = Example(); Check(ReferenceEquals(LinetypeMetadata.Validate(Owner, value), value), "reader must not normalize");
            var unusual = value with { pattern_length = -10, elements = value.elements.Select((e, i) => e with { length = i == 0 ? 0 : e.length, scale = -1, shape_number = int.MaxValue, rotation = -7 }).ToArray(), is_scaled_to_fit = true, has_xdata = true, has_fields = true, has_extension_dictionary = true };
            LinetypeMetadata.Validate(Owner, unusual);
            var empty = value with { pattern_length = 0, dash_count = 0, elements = Array.Empty<LinetypeElement>() }; LinetypeMetadata.Validate(Owner, empty);
            var unresolved = value with { elements = value.elements.Select(e => e with { style = new("text_style", "unavailable", null, null, "wrong_owner") }).ToArray() }; LinetypeMetadata.Validate(Owner, unresolved);
            File.WriteAllText(Path.Combine(Environment.GetEnvironmentVariable("NANOCAD_MCP_HOME")!, "linetype-read-examples.json"),
                JsonSerializer.Serialize(new { schema = LinetypeReadSchema.Properties(), examples = new[] { value, unusual, empty, unresolved }, native_executed = false }, Wire.Json));
            return Task.CompletedTask;
        });
        await test("linetype count bounds prevent partial or unordered patterns", () =>
        {
            var value = Example(); var elements = Enumerable.Range(0, 256).Select(i => value.elements[0] with { index = i }).ToArray();
            LinetypeMetadata.Validate(Owner, value with { dash_count = 256, elements = elements });
            LinetypeMetadata.Count(257); Domain("OBJECT_READ_FAILED", () => LinetypeMetadata.Count(-1));
            foreach (var bad in new[] { value with { dash_count = 2 }, value with { elements = null! }, value with { elements = value.elements.Reverse().ToArray() }, value with { elements = new[] { value.elements[0], value.elements[0], value.elements[2] } } })
                Domain("OBJECT_READ_FAILED", () => LinetypeMetadata.Validate(Owner, bad));
            return Task.CompletedTask;
        });
        await test("linetype nested identities reject wrong document and incomplete references", () =>
        {
            var value = Example(); var style = value.elements[1].style;
            var invalid = new[] { style with { @ref = Owner with { document_id = Guid.NewGuid().ToString("D") } }, style with { @ref = Owner with { bridge_instance_id = Guid.Empty.ToString("D") } },
                style with { @ref = Owner with { handle = "00AF" } }, style with { @ref = null }, style with { name = null }, style with { reason = "wrong_owner" }, style with { target_kind = "block" },
                Null with { @ref = Owner }, Null with { reason = "default_font" }, new LinetypeStyleReference("text_style", "unavailable", null, null, null), new("text_style", "unavailable", null, null, "guess") };
            foreach (var reference in invalid)
            { var copy = Example(); copy.elements[1] = copy.elements[1] with { style = reference }; Domain("OBJECT_READ_FAILED", () => LinetypeMetadata.Validate(Owner, copy)); }
            return Task.CompletedTask;
        });
        await test("linetype numeric and Unicode errors are rejected without truncation", () =>
        {
            foreach (double number in new[] { double.NaN, double.PositiveInfinity, double.NegativeInfinity })
            {
                var value = Example(); Domain("OBJECT_READ_FAILED", () => LinetypeMetadata.Validate(Owner, value with { pattern_length = number }));
                foreach (var item in new[] { value.elements[0] with { length = number }, value.elements[0] with { rotation = number }, value.elements[0] with { scale = number }, value.elements[0] with { offset = new[] { 0d, number } } })
                { var copy = Example(); copy.elements[0] = item; Domain("OBJECT_READ_FAILED", () => LinetypeMetadata.Validate(Owner, copy)); }
            }
            Check(LinetypeMetadata.Text("😀", 2) == "😀", "UTF16 supplementary pair");
            Domain("ENTITY_TOO_LARGE", () => LinetypeMetadata.Text("😀", 1));
            Domain("OBJECT_READ_FAILED", () => LinetypeMetadata.Text("\ud800", 10));
            Domain("OBJECT_READ_FAILED", () => LinetypeMetadata.Text(null, 10));
            foreach (var offset in new[] { Array.Empty<double>(), new[] { 1d }, new[] { 1d, 2, 3 } })
            { var value = Example(); value.elements[0] = value.elements[0] with { offset = offset }; Domain("OBJECT_READ_FAILED", () => LinetypeMetadata.Validate(Owner, value)); }
            return Task.CompletedTask;
        });
    }
}
