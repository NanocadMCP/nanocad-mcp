using System.Text.Json;
using System.Text.RegularExpressions;
using NanoCAD.Contracts;

internal static class DrawingViewSectionRetirementTests
{
    const string First = "{a1b2c3d4-1111-2222-3333-444444444444}";
    const string Second = "{b1c2d3e4-1111-2222-3333-444444444444}";
    static readonly string Token = new('a', 64);

    static void Need(bool condition, string message)
    { if (!condition) throw new Exception(message); }

    static object View(string handle = "000a1") => new { layout_handle = "000b1", handle, expected_name = "Current native view" };
    static object Section(string id = First, string? token = null) => new { section_id = id, section_token = token ?? Token };
    static Dictionary<string, object?> Delete(object? sections = null, bool includeSections = true)
    {
        var operation = new Dictionary<string, object?> { ["operation"] = "delete_drawing_views", ["targets"] = new[] { View() } };
        if (includeSections) operation["section_targets"] = sections;
        return operation;
    }
    static JsonElement Payload(params object[] operations) => Wire.Element(new
    {
        bridge_instance_id = "11111111-1111-1111-1111-111111111111",
        document_id = "22222222-2222-2222-2222-222222222222",
        expected_revision = "12",
        operation_id = "33333333-3333-3333-3333-333333333333",
        units = "Millimeters",
        operations
    });
    static StructureWriteArguments Parse(object operation) => StructureWriteValidation.Parse(Payload(operation));
    static void Reject(object operation, string? expectedPath = null, string code = "INVALID_ARGUMENT")
    {
        try { Parse(operation); }
        catch (DomainException error) when (error.Error.code == code)
        {
            if (expectedPath != null)
                Need(Wire.Element(error.Error.details ?? throw new Exception("Section retirement error details are missing.")).GetProperty("path").GetString() == expectedPath,
                    "The Section retirement error did not identify the actual invalid field.");
            return;
        }
        throw new Exception("Invalid Section retirement request was accepted.");
    }

    internal static async Task Run(Func<string, Func<Task>, Task> test)
    {
        await test("retirement normalization excludes only exact local valid erased original references", () =>
        {
            var kept = Guid.Parse("a1111111-1111-4111-8111-111111111111");
            var erased = Guid.Parse("a2222222-2222-4222-8222-222222222222");
            var effective = Guid.Parse("a3333333-3333-4333-8333-333333333333");
            var permitted = new HashSet<Guid> { erased, effective };
            var observed = new[]
            {
                (Id: kept, State: new NativePersistentReferenceState(false, true, true, false, false)),
                (Id: erased, State: new NativePersistentReferenceState(false, true, true, true, false)),
                (Id: effective, State: new NativePersistentReferenceState(false, true, true, false, true))
            };
            var retained = observed.Where(item => !NativePersistentReferences.ExcludeKnownErased(item.Id, item.State, permitted)).Select(item => item.Id).ToArray();
            Need(retained.SequenceEqual(new[] { kept }) && observed.Length == 3 && permitted.SetEquals(new[] { erased, effective }),
                "Known inactive entries were not normalized exactly or the actual inventory/provenance was mutated.");
            Need(!NativePersistentReferences.ExcludeKnownErased(erased, new(false, true, true, false, false), permitted),
                "A still-live originally selected reference was hidden instead of remaining observable for the strict comparison.");
            return Task.CompletedTask;
        });
        await test("persistent reference state never treats null invalid foreign or unproven erased IDs as harmless", () =>
        {
            var known = Guid.Parse("b1111111-1111-4111-8111-111111111111");
            var other = Guid.Parse("b2222222-2222-4222-8222-222222222222");
            var permitted = new HashSet<Guid> { known };
            void Refuse(Action read, string reason)
            {
                try { read(); }
                catch (DomainException error) when (error.Error.code == "UNSUPPORTED_PERSISTENT_REFERENCE")
                {
                    Need(Wire.Element(error.Error.details!).GetProperty("reason").GetString() == reason,
                        "The actual invalid/reference-state reason was lost."); return;
                }
                throw new Exception("An unavailable or unproven persistent reference was waived.");
            }
            Refuse(() => NativePersistentReferences.ExcludeKnownErased(known, new(true, true, true, true, false), permitted), "null_reference");
            Refuse(() => NativePersistentReferences.ExcludeKnownErased(known, new(false, false, true, true, false), permitted), "invalid_reference");
            Refuse(() => NativePersistentReferences.ExcludeKnownErased(known, new(false, true, false, true, false), permitted), "foreign_database");
            Refuse(() => NativePersistentReferences.ExcludeKnownErased(other, new(false, true, true, true, false), permitted), "unpermitted_erased_reference");
            Need(NativePersistentReferences.IsInactive(new(false, true, true, true, false)) &&
                NativePersistentReferences.IsInactive(new(false, true, true, false, true)) &&
                !NativePersistentReferences.IsInactive(new(false, true, true, false, false)),
                "The direct-owner guard lost the actual native erased/effectively-erased versus live distinction.");
            return Task.CompletedTask;
        });
        await test("view deletion retains legacy construction and absent versus empty Section retirement", () =>
        {
            var legacy = Parse(Delete(includeSections: false));
            var explicitEmpty = Parse(Delete(Array.Empty<object>()));
            var originalConstructor = new DeleteDrawingViewsEdit([new("B1", "A1", "Current native view")]);
            Need(originalConstructor.Sections.Length == 0 &&
                legacy.Operations.Single() is DeleteDrawingViewsEdit { Sections.Length: 0 } &&
                legacy.Fingerprint() == explicitEmpty.Fingerprint() && StructureWriteValidation.IsSettingsOnly(legacy.Operations),
                "Legacy view-only deletion or empty optional Section semantics changed.");
            Need(((DeleteDrawingViewsEdit)legacy.Operations.Single()).Targets.Single() == originalConstructor.Targets.Single(),
                "The existing layout/view/name normalization changed.");
            return Task.CompletedTask;
        });
        await test("Section retirement normalizes complete exact IDs and tokens without a section-count ceiling", () =>
        {
            var parsed = Parse(Delete(new[] { Section(Second), Section() }));
            var operation = (DeleteDrawingViewsEdit)parsed.Operations.Single();
            Need(operation.Sections.Select(section => section.SectionId).SequenceEqual(new[] { First.ToUpperInvariant(), Second.ToUpperInvariant() }) &&
                operation.Sections.All(section => section.SectionToken == Token.ToUpperInvariant()),
                "Native Section identity/token normalization or deterministic ordering was lost.");
            var reordered = Parse(Delete(new[] { Section(First.ToUpperInvariant(), Token.ToUpperInvariant()), Section(Second.ToUpperInvariant(), Token.ToUpperInvariant()) }));
            Need(parsed.Fingerprint() == reordered.Fingerprint(), "Equivalent explicit Section sets have different fingerprints.");
            var many = Enumerable.Range(1, 129).Select(index => Section("{" + index.ToString("X8") + "-1111-2222-3333-444444444444}")).ToArray();
            Need(((DeleteDrawingViewsEdit)Parse(Delete(many)).Operations.Single()).Sections.Length == many.Length,
                "A native Section set was truncated or capped independently of request bytes.");
            return Task.CompletedTask;
        });
        await test("Section retirement refuses malformed identity freshness and unknown fields at exact paths", () =>
        {
            foreach (var invalid in new object?[] { null, new { section_id = First }, "Section A" })
                Reject(Delete(invalid), "$.operations[0].section_targets");
            foreach (var id in new[] { First.Trim('{', '}'), "{00000000-0000-0000-0000-000000000000}", "A1", "{" + new string('G', 36) + "}" })
                Reject(Delete(new[] { Section(id) }), "$.operations[0].section_targets[0].section_id");
            foreach (var token in new[] { "", "stale", new string('g', 64), new string('A', 65) })
                Reject(Delete(new[] { Section(token: token) }), "$.operations[0].section_targets[0].section_token");
            Reject(Delete(new[] { new { section_id = First, section_token = Token, force = true } }),
                "$.operations[0].section_targets[0].force");
            // JsonElement retains duplicate JSON fields for the production Shape guard.
            var duplicate = JsonDocument.Parse("{\"operation\":\"delete_drawing_views\",\"targets\":[{\"layout_handle\":\"B1\",\"handle\":\"A1\",\"expected_name\":\"Current native view\"}],\"section_targets\":[{\"section_id\":\"" + First + "\",\"section_id\":\"" + Second + "\",\"section_token\":\"" + Token + "\"}]}");
            using (duplicate)
                Reject(duplicate.RootElement, "$.operations[0].section_targets[0].section_id");
            return Task.CompletedTask;
        });
        await test("Section retirement rejects repeated exact IDs and preserves explicit nonempty view and sole-operation scope", () =>
        {
            Reject(Delete(new[] { Section(), Section(First.ToUpperInvariant()) }), "$.operations[0].section_targets[1].section_id");
            Reject(Delete(new[] { Section(), Section(First.ToUpperInvariant(), new string('B', 64)) }), "$.operations[0].section_targets[1].section_id");
            var noViews = Delete(new[] { Section() }); noViews["targets"] = Array.Empty<object>();
            Reject(noViews, "$.operations[0].targets");
            var duplicateView = Delete(new[] { Section() }); duplicateView["targets"] = new[] { View(), View("A1") };
            Reject(duplicateView, "$.operations[0].targets");
            try { StructureWriteValidation.Parse(Payload(Delete(new[] { Section() }), new { operation = "create_layer", name = "Other" })); }
            catch (DomainException error) when (error.Error.code == "INVALID_OPERATION_ORDER") { return Task.CompletedTask; }
            throw new Exception("Section retirement was admitted in a mixed native operation group.");
        });
        await test("Section retirement fingerprints bind the selected native identity and every freshness token", () =>
        {
            var baseline = Parse(Delete(new[] { Section() })).Fingerprint();
            Need(Parse(Delete(new[] { Section(Second) })).Fingerprint() != baseline &&
                Parse(Delete(new[] { Section(token: new string('b', 64)) })).Fingerprint() != baseline &&
                Parse(Delete(includeSections: false)).Fingerprint() != baseline,
                "A Section identity, token or explicit retirement choice was omitted from request identity.");
            return Task.CompletedTask;
        });
        await test("view deletion publishes optional strict Section targets while retaining required explicit views", () =>
        {
            var schema = AuditServerContractSupport.Branch("modify_structure", "delete_drawing_views");
            var required = schema.GetProperty("required").EnumerateArray().Select(value => value.GetString()).ToArray();
            var properties = schema.GetProperty("properties");
            var sections = properties.GetProperty("section_targets"); var item = sections.GetProperty("items");
            var fields = item.GetProperty("properties"); var id = fields.GetProperty("section_id");
            Need(required.Contains("targets") && !required.Contains("section_targets") &&
                properties.GetProperty("targets").GetProperty("minItems").GetInt32() == 1 &&
                sections.GetProperty("minItems").GetInt32() == 0 && !sections.TryGetProperty("maxItems", out _) &&
                !item.GetProperty("additionalProperties").GetBoolean() &&
                item.GetProperty("required").EnumerateArray().Select(value => value.GetString()).Order().SequenceEqual(new[] { "section_id", "section_token" }),
                "Optional Section shape, nonempty view scope or transport-only size policy was not published.");
            Need(Regex.IsMatch(First, id.GetProperty("pattern").GetString()!) &&
                !Regex.IsMatch(First.Trim('{', '}'), id.GetProperty("pattern").GetString()!) &&
                id.GetProperty("not").GetProperty("const").GetString() == "{00000000-0000-0000-0000-000000000000}" &&
                Regex.IsMatch(Token, fields.GetProperty("section_token").GetProperty("pattern").GetString()!),
                "Published Section IDs or fresh-token representation differs from actual parsing.");
            return Task.CompletedTask;
        });
    }
}
