using System.Collections;
using System.Reflection;
using System.Reflection.Emit;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using System.Text.Json;
using NanoCAD.Contracts;
using NanoCAD.McpServer;

internal static class StyleReadTests
{
    private static void Check(bool value, string why) { if (!value) throw new Exception(why); }
    private static void Domain(string code, Action action)
    { try { action(); throw new Exception("Expected " + code); } catch (DomainException e) { Check(e.Error.code == code, "Expected " + code + ", got " + e.Error.code); } }
    private static TextStyleFacts Facts() => new("Standard", false, "txt.shx", "", 0, 1, false, false,
        new(null, false, false, 0, 0), 0, 0, 2, false, false, TextFontMetadata.From(null));
    public static TextStyleProperties Properties() => TextStyleMetadata.Describe(Facts());
    public static StructurePage ProtocolData(string bridge, string document) => new("text_styles",
        new[] { new StructureRecord(new(bridge, document, "AF"), "text_styles", "Standard", Properties()) }, 1, 1, true, null);
    private sealed class NativeLikeEnumerator : IEnumerator, IDisposable
    {
        private readonly object[] _values; private int _index = -1;
        public int Moves, Reads, ThrowsOnMove = -1, ThrowsOnRead = -1; public bool Disposed;
        public NativeLikeEnumerator(params object[] values) => _values = values;
        public bool MoveNext() { Moves++; if (Moves == ThrowsOnMove) throw new IOException("native move failed"); return ++_index < _values.Length; }
        public object Current { get { Reads++; if (Reads == ThrowsOnRead) throw new IOException("native current failed"); return _values[_index]; } }
        public void Reset() => throw new NotSupportedException();
        public void Dispose() => Disposed = true;
    }
    private static object[] Tags() => new object[] { new FontMetadataValue(1001, "ACAD"), new FontMetadataValue(1000, "Arial"), new FontMetadataValue(1071, 33554466) };
    // Installed ResultBufferEnumerator implements IEnumerator but not IDisposable;
    // its containing ResultBuffer remains the native adapter's owned resource.
    private sealed class NonDisposableEnumerator : IEnumerator
    {
        private readonly object[] _values; private int _index = -1;
        public NonDisposableEnumerator(object[] values) => _values = values;
        public object Current => _values[_index];
        public bool MoveNext() => ++_index < _values.Length;
        public void Reset() => throw new NotSupportedException();
    }
    public static async Task Run(Func<string, Func<Task>, Task> test)
    {
        await test("style metadata retains fixture properties and explains unsupported base modes", () =>
        {
            using var oracle = JsonDocument.Parse(File.ReadAllText("tests/fixtures/phase4-style-write.expected.json"));
            var output = new List<object>();
            foreach (var entry in oracle.RootElement.GetProperty("text_styles").EnumerateObject())
            {
                var value = entry.Value; var font = value.GetProperty("extended_font");
                var xdata = value.GetProperty("acad_xdata");
                var metadata = xdata.ValueKind == JsonValueKind.Null ? TextFontMetadata.From(null)
                    : TextFontMetadata.From(new("ACAD", xdata[0][1].GetString()!, xdata[1][1].GetInt32()));
                var facts = Facts() with { Name = entry.Name, FontFile = value.GetProperty("font_file").GetString()!,
                    BigFontFile = value.GetProperty("big_font_file").GetString()!, Height = value.GetProperty("height").GetDouble(),
                    WidthFactor = value.GetProperty("width_factor").GetDouble(), ObliqueAngle = value.GetProperty("oblique_radians").GetDouble(),
                    GenerationFlags = value.GetProperty("generation_flags").GetInt32(), IsVertical = value.GetProperty("table_flags").GetInt32() == 4,
                    Font = new(font[0].GetString(), font[2].GetBoolean(), font[1].GetBoolean(), 0, 0), FontMetadata = metadata };
                // Character set/pitch/annotative/extension/fields values are
                // synthetic here; this is not an assertion about native getters.
                var properties = TextStyleMetadata.Describe(facts);
                string[] expected = entry.Name switch { "P4S_VERTICAL" => new[] { "vertical" }, "P4S_BACKWARD" => new[] { "generation_flags" },
                    "P4S_OBLIQUE" => new[] { "oblique" }, _ => Array.Empty<string>() };
                Check(properties.base_eligibility.reasons.SequenceEqual(expected) && properties.base_eligibility.eligible == (expected.Length == 0), "fixture eligibility " + entry.Name);
                Check(properties.height == facts.Height && properties.width_factor == facts.WidthFactor && properties.oblique_angle == facts.ObliqueAngle &&
                    properties.font_file == facts.FontFile && properties.font_metadata == facts.FontMetadata, "unmodified style values");
                output.Add(new { name = entry.Name, properties });
            }
            output.Add(new { name = "UnknownMetadata", properties = TextStyleMetadata.Describe(Facts() with { AnnotativeNativeValue = 99, FontMetadata = TextFontMetadata.Unsupported() }) });
            File.WriteAllText(Path.Combine(Environment.GetEnvironmentVariable("NANOCAD_MCP_HOME")!, "style-read-draft-schemas.json"),
                JsonSerializer.Serialize(new { properties = StyleReadSchema.TextStyleProperties(), examples = output, exposed_in_mcp = true,
                    native_acceptance = false, synthetic_fields = new[] { "font_descriptor.character_set", "font_descriptor.pitch_and_family", "annotative_native_value", "has_extension_dictionary", "has_fields" } }, Wire.Json));
            return Task.CompletedTask;
        });
        await test("style eligibility rejects every restricted feature without discarding readable metadata", () =>
        {
            var cases = new (string Reason, TextStyleFacts Facts)[] {
                ("qualified_name", Facts() with { Name = "xref|style" }), ("dependent", Facts() with { IsDependent = true }),
                ("vertical", Facts() with { IsVertical = true }), ("shape", Facts() with { IsShapeFile = true }),
                ("generation_flags", Facts() with { GenerationFlags = int.MinValue }), ("oblique", Facts() with { ObliqueAngle = 1e-12 }),
                ("annotative", Facts() with { AnnotativeNativeValue = 0 }), ("extension_dictionary", Facts() with { HasExtensionDictionary = true }),
                ("fields", Facts() with { HasFields = true }), ("font_metadata", Facts() with { FontMetadata = TextFontMetadata.Unsupported() }),
                ("big_font", Facts() with { BigFontFile = "bigfont.shx" }), ("height", Facts() with { Height = -1 }),
                ("width_factor", Facts() with { WidthFactor = 0 }), ("font_source", Facts() with { FontFile = "" }) };
            foreach (var item in cases)
            {
                var description = TextStyleMetadata.Describe(item.Facts);
                Check(!description.base_eligibility.eligible && description.base_eligibility.reasons.SequenceEqual(new[] { item.Reason }), item.Reason);
            }
            Check(cases.Select(item => item.Reason).SequenceEqual(TextStyleMetadata.ReasonCodes), "stable complete reason ordering");
            var combined = TextStyleMetadata.Describe(Facts() with { IsDependent = true, HasFields = true, WidthFactor = 101 });
            Check(combined.base_eligibility.reasons.SequenceEqual(new[] { "dependent", "fields", "width_factor" }), "multiple reasons retained");
            Check(TextStyleMetadata.Describe(Facts() with { Height = 2.5, FontFile = "", Font = new("Arial", true, false, 204, 34) }).base_eligibility.eligible, "fixed height and inherited typeface allowed");
            foreach (var (value, state, eligible) in new[] { (0, "true", false), (1, "false", true), (2, "not_applicable", true), (-1, "unknown", false), (99, "unknown", false) })
            { var actual = TextStyleMetadata.Describe(Facts() with { AnnotativeNativeValue = value }); Check(actual.annotative_state == state && actual.annotative_native_value == value && actual.base_eligibility.eligible == eligible, "native enum mapping " + value); }
            return Task.CompletedTask;
        });
        await test("style metadata reports invalid numbers and Unicode without normalization or truncation", () =>
        {
            foreach (var facts in new[] { Facts() with { Height = double.NaN }, Facts() with { WidthFactor = double.PositiveInfinity },
                Facts() with { ObliqueAngle = double.NegativeInfinity }, Facts() with { FontFile = "\uD800" }, Facts() with { Font = new("\uD800", false, false, 0, 0) } })
                Domain("OBJECT_READ_FAILED", () => TextStyleMetadata.Describe(facts));
            Domain("ENTITY_TOO_LARGE", () => TextStyleMetadata.Describe(Facts() with { FontFile = new string('x', 16385) }));
            Domain("ENTITY_TOO_LARGE", () => TextStyleMetadata.Describe(Facts() with { Font = new(new string('x', 256), false, false, 0, 0) }));
            foreach (var metadata in new[] { new TextFontMetadata("unknown", null, null), new("empty", "Arial", null), new("acad_font", "Arial", null) })
                Domain("OBJECT_READ_FAILED", () => TextStyleMetadata.Describe(Facts() with { FontMetadata = metadata }));
            var emptyFace = TextStyleMetadata.Describe(Facts() with { Font = new("", false, false, 0, 0) });
            Check(emptyFace.font_descriptor.typeface == null && emptyFace.base_eligibility.eligible, "empty descriptor typeface denotes absence, not read failure");
            return Task.CompletedTask;
        });
        await test("native-style enumeration never reads or converts the fourth XData payload", () =>
        {
            foreach (int extra in new[] { 0, 1 })
            {
                var iterator = new NativeLikeEnumerator(extra == 0 ? Tags() : Tags().Append(new object()).ToArray());
                iterator.ThrowsOnRead = 4; int conversions = 0, checks = 0;
                FontMetadataSignature? Read() => StyleFontMetadata.Read(iterator, value => { conversions++; return (FontMetadataValue)value; }, () => checks++);
                if (extra == 0) Check(Read() == new FontMetadataSignature("ACAD", "Arial", 33554466), "successful typed conversion");
                else Domain("UNSUPPORTED_STYLE_TYPE", () => Read());
                Check(iterator.Moves == 4 && iterator.Reads == 3 && conversions == 3 && checks == 4 && iterator.Disposed, "bounded native allocation/disposal");
            }
            var empty = new NativeLikeEnumerator();
            Check(StyleFontMetadata.Read(empty, _ => throw new Exception("empty current"), () => { }) == null && empty.Moves == 1 && empty.Reads == 0 && empty.Disposed, "empty native metadata");
            Check(StyleFontMetadata.Read(new NonDisposableEnumerator(Tags()), value => (FontMetadataValue)value, () => { }) == new FontMetadataSignature("ACAD", "Arial", 33554466), "native IEnumerator without IDisposable");
            return Task.CompletedTask;
        });
        await test("native-style enumeration disposes on cancellation, read faults and conversion failures", () =>
        {
            foreach (string failure in new[] { "move", "read", "convert", "cancel" })
            {
                var iterator = new NativeLikeEnumerator(Tags()); int checks = 0;
                if (failure == "move") iterator.ThrowsOnMove = 2;
                if (failure == "read") iterator.ThrowsOnRead = 2;
                try
                {
                    StyleFontMetadata.Read(iterator, value => failure == "convert" ? throw new IOException("conversion failed") : (FontMetadataValue)value,
                        () => { if (++checks == 2 && failure == "cancel") throw new OperationCanceledException(); });
                    throw new Exception("Expected iterator failure");
                }
                catch (IOException) { Check(failure != "cancel", "unexpected cancellation mapping"); }
                catch (OperationCanceledException) { Check(failure == "cancel", "unexpected cancellation"); }
                Check(iterator.Disposed, "leaked iterator on " + failure);
                if (failure == "cancel") Check(iterator.Moves == 1 && iterator.Reads == 1, "cancel before next native call");
            }
            return Task.CompletedTask;
        });
        await test("native style metadata is shared only by structural reads/style writes and contains no mutations", () =>
        {
            AuditPreparedReader();
            return Task.CompletedTask;
        });
    }

    private static void AuditPreparedReader()
    {
        using var file = File.OpenRead("src/NanoCADBridge/bin/Release/net6.0-windows/NanoCADBridge.dll");
        using var pe = new PEReader(file); var metadata = pe.GetMetadataReader();
        var opcodes = typeof(OpCodes).GetFields(BindingFlags.Public | BindingFlags.Static).Select(field => (OpCode)field.GetValue(null)!).ToDictionary(code => (ushort)code.Value);
        string TypeName(EntityHandle handle) => handle.Kind switch {
            HandleKind.TypeDefinition => metadata.GetString(metadata.GetTypeDefinition((TypeDefinitionHandle)handle).Name),
            HandleKind.TypeReference => metadata.GetString(metadata.GetTypeReference((TypeReferenceHandle)handle).Name), _ => handle.Kind.ToString() };
        bool ReaderType(TypeDefinitionHandle handle)
        {
            var type = metadata.GetTypeDefinition(handle);
            return metadata.GetString(type.Name) == "TextStyleMetadataReader" || (!type.GetDeclaringType().IsNil && ReaderType(type.GetDeclaringType()));
        }
        string RootType(TypeDefinitionHandle handle)
        {
            var type = metadata.GetTypeDefinition(handle);
            return type.GetDeclaringType().IsNil ? metadata.GetString(type.Name) : RootType(type.GetDeclaringType());
        }
        (string Type, string Method) Target(EntityHandle handle)
        {
            if (handle.Kind == HandleKind.MethodSpecification) return Target(metadata.GetMethodSpecification((MethodSpecificationHandle)handle).Method);
            if (handle.Kind == HandleKind.MethodDefinition) { var method = metadata.GetMethodDefinition((MethodDefinitionHandle)handle); return (TypeName(method.GetDeclaringType()), metadata.GetString(method.Name)); }
            var member = metadata.GetMemberReference((MemberReferenceHandle)handle); return (TypeName(member.Parent), metadata.GetString(member.Name));
        }
        int outsideReferences = 0, readerMethods = 0; var calls = new SortedSet<string>(StringComparer.Ordinal);
        var callers = new SortedSet<string>(StringComparer.Ordinal);
        foreach (var handle in metadata.MethodDefinitions)
        {
            var method = metadata.GetMethodDefinition(handle); if (method.RelativeVirtualAddress == 0) continue;
            bool reader = ReaderType(method.GetDeclaringType()); if (reader) readerMethods++;
            var il = pe.GetMethodBody(method.RelativeVirtualAddress).GetILBytes()!;
            for (int offset = 0; offset < il.Length;)
            {
                ushort value = il[offset++]; if (value == 0xfe) value = (ushort)(0xfe00 | il[offset++]); var opcode = opcodes[value];
                if (opcode.OperandType == OperandType.InlineMethod)
                {
                    var target = Target(MetadataTokens.EntityHandle(BitConverter.ToInt32(il, offset)));
                    if (!reader && target.Type == "TextStyleMetadataReader") { outsideReferences++; callers.Add(RootType(method.GetDeclaringType())); }
                    if (reader)
                    {
                        calls.Add(target.Type + "." + target.Method);
                        Check(!target.Method.StartsWith("set_", StringComparison.Ordinal) && target.Method is not ("Clone" or "CopyFrom" or "GetObject" or "Add" or "Commit" or "LockDocument" or "StartTransaction" or "SendStringToExecute"), "unexpected write/context call in metadata reader: " + target);
                    }
                }
                int size = opcode.OperandType switch {
                    OperandType.InlineNone => 0,
                    OperandType.ShortInlineBrTarget or OperandType.ShortInlineI or OperandType.ShortInlineVar => 1,
                    OperandType.InlineVar => 2,
                    OperandType.InlineI8 or OperandType.InlineR => 8,
                    OperandType.InlineSwitch => 4 + 4 * BitConverter.ToInt32(il, offset), _ => 4 };
                offset += size; Check(offset <= il.Length, "invalid IL operand boundary");
            }
        }
        Check(readerMethods > 0 && outsideReferences >= 2 && callers.SetEquals(new[] { "StructureReader", "CadStyleWriter" }), "unexpected or missing product style metadata callers");
        Check(!calls.Contains("ResultBuffer.op_Equality"), "null test must not invoke an additional native wrapper operator");
        foreach (string getter in new[] { "TextStyleTableRecord.get_Font", "DBObject.get_XData", "DBObject.get_Annotative" })
            Check(calls.Contains(getter), "missing compiled SDK getter " + getter);
        File.WriteAllText(Path.Combine(Environment.GetEnvironmentVariable("NANOCAD_MCP_HOME")!, "style-read-adapter-audit.json"),
            JsonSerializer.Serialize(new { reader_methods = readerMethods, outside_references = outsideReferences, caller_roots = callers, calls, native_execution = false,
                scope = "Compiled IL call inspection only; no CAD assemblies loaded into test process. Does not prove transitive getter side effects." }, Wire.Json));
    }
}
