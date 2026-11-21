using System.Text.Json;
using NanoCAD.Contracts;
using NanoCAD.Ipc;
using NanoCAD.McpServer;

internal static class ModelParameterTests
{
    public static async Task Run(Func<string, Func<Task>, Task> test)
    {
        var bridge = Guid.NewGuid().ToString("D"); var document = Guid.NewGuid().ToString("D"); var op = Guid.NewGuid().ToString("D");
        var variable = Guid.NewGuid().ToString("B");
        Dictionary<string, object?> Input() => new()
        {
            ["bridge_instance_id"] = bridge,
            ["document_id"] = document,
            ["operation_id"] = op,
            ["expected_revision"] = "0007",
            ["units"] = "Millimeters",
            ["handle"] = "00150",
            ["variable_id"] = variable,
            ["expected_expression"] = "150",
            ["value"] = 151
        };
        ModelParameterArguments Parse(Dictionary<string, object?> input) => (ModelParameterArguments)WriteValidation.Parse("set_model_parameter", Wire.Element(input));
        void Require(bool condition) { if (!condition) throw new Exception("Model parameter contract assertion failed."); }
        await test("part chain drawing observations participate in write replay identity", () =>
        {
            var input = Input(); var original = Parse(input);
            input["observe_dimension_handles"] = new[] { "0AA", "f" };
            var args = Parse(input);
            Require(args.ObserveDimensionHandles!.SequenceEqual(new[] { "AA", "F" }) && !args.UsesExpressionPlan);
            Require(args.Fingerprint() != original.Fingerprint());
            input["observe_dimension_handles"] = new[] { "aa", "0F" };
            Require(Parse(input).Fingerprint() == args.Fingerprint());
            var tool = ToolCatalog.Tools.Single(t => t.Name == "nanocad.set_model_parameter");
            Require(tool.InputSchema.GetProperty("properties").GetProperty("observe_dimension_handles").GetProperty("type").GetString() == "array");
            foreach (object? invalid in new object?[] { null, Array.Empty<string>(), new[] { "A", "0a" }, new[] { "0" }, new[] { 7 } })
            {
                input["observe_dimension_handles"] = invalid;
                try { Parse(input); } catch (DomainException) { continue; }
                throw new Exception("Invalid dimension observation accepted.");
            }
            return Task.CompletedTask;
        });
        await test("part chain stored anchors and measured values are independent observations", () =>
        {
            NanoCADBridge.ModelParameterDimensionBinding.Snapshot Read(double sheet, int marker = 3, string body = "source",
                string text = "automatic_measurement", bool linked = true, double? nominal = null, double factor = 2,
                bool real = true, double? parameter = 0, bool hasSheet = true)
                => NanoCADBridge.ModelParameterDimensionBinding.Read("D", "B", Wire.Element(new
                {
                    links_complete = linked, stored_anchor_parameters_available = linked, dimension_mc_id = "dimension",
                    links = new[] { new { healthy = true, source_mc_ids = new[] { body }, source_refs = new[] { new { handle = "F" } },
                        view_mc_id = "view", view_ref = new { handle = "A" } } },
                    measurement = new { sheet_geometry = hasSheet ? (double?)sheet : null, sheet_unit = "drawing_units",
                        physical_length_from_projection = hasSheet ? (double?)(sheet * 2) : null, text_mode = text, native_nominal_length = nominal ?? sheet * 2,
                        view_scale_denominator = 2, native_measurement_factor = factor, native_value_is_valid = real,
                        unavailable_reason = hasSheet ? null : "tilted_rotated_dimension_sheet_geometry_unavailable" },
                    anchors = new[] { new { is_null = false, slot = 0, source_mc_id = "view", gs_marker = marker, parameter, data = "0" } }
                }));
            var previous = Read(40);
            var grown = previous.Compare(Read(50));
            Require(grown.association_status == "same_stored_anchors" && grown.previous.projected_model_length == 80 &&
                grown.current is { projected_model_length: 100, sheet_length: 50 });
            Require(grown.current is { nominal_vs_projected_model: "match", factor_vs_view_scale: "match" });
            var wrongNominal = previous.Compare(Read(50, nominal: 50));
            Require(wrongNominal is { association_status: "same_stored_anchors", current: { native_nominal_length: 50, projected_model_length: 100,
                sheet_length: 50, view_scale_denominator: 2, native_measurement_factor: 2, native_value_is_valid: true,
                nominal_vs_projected_model: "mismatch", factor_vs_view_scale: "match" } });
            Require(previous.Compare(Read(50, factor: 1)).current is { nominal_vs_projected_model: "match", factor_vs_view_scale: "mismatch" });
            Require(previous.Compare(Read(50, real: false)).current is { native_nominal_length: 100, nominal_vs_projected_model: "unavailable" });
            Require(previous.Compare(Read(50, nominal: 100.00000009)).current?.nominal_vs_projected_model == "match");
            Require(previous.Compare(Read(50, hasSheet: false)).current is { sheet_length: null, projected_model_length: null,
                nominal_vs_projected_model: "unavailable", sheet_length_unavailable_reason: "tilted_rotated_dimension_sheet_geometry_unavailable" });
            var incomplete = Read(50, parameter: null);
            Require(incomplete.Compare(incomplete) is { association_status: "unavailable", unavailable_code: "ANCHOR_PARAMETER_UNAVAILABLE", current: not null });
            Require(previous.Compare(Read(40)).association_status == "same_stored_anchors");
            Require(previous.Compare(Read(40, marker: 4)).association_status == "stored_anchors_changed");
            Require(previous.Compare(Read(40, body: "other")).association_status == "stored_anchors_changed");
            Require(previous.Compare(Read(50, text: "literal_override")).current?.text_mode == "literal_override");
            Require(previous.Compare(null, "OBJECT_ERASED") is { association_status: "unavailable", current: null, unavailable_code: "OBJECT_ERASED" });
            try { Read(40, linked: false); } catch (DomainException e) when (e.Error.code == "PROJECTED_DIMENSION_ASSOCIATION_UNAVAILABLE") { return Task.CompletedTask; }
            throw new Exception("An independent measurement was accepted as linked.");
        });
        await test("part chain receipt retains the largest complete observation prefix including its warning", () =>
        {
            var reading = new ParameterDimensionReading("B", "A", "F", 40, null, 80, 2, 2, 80, true, "match", "match", "automatic_measurement");
            var rows = Enumerable.Range(0, 12).Select(i => new ParameterDimensionObservation(i.ToString("X"),
                "same_stored_anchors", reading, reading with { sheet_length = 50, projected_model_length = 100, native_nominal_length = 100 }, null)).ToArray();
            var data = new ModelParameterResult(op, "committed", false, new(bridge, document, "150"), null, variable, "80", "100", 100, "Millimeters", 42, "not_tested")
                { observed_dimensions = rows, model_healthy = true };
            var result = Result.Ok("size-check", data, null, "Existing model diagnostic");
            int Size(Result value) => JsonSerializer.SerializeToUtf8Bytes(value, Wire.Json).Length;
            Result Fit(int bytes) => NanoCADBridge.ModelParameterDimensionReceipt.Fit(result, bytes);
            Require(ReferenceEquals(result, Fit(Size(result))));
            // Discover the exact byte boundaries from serialized receipts, including the changing number of digits.
            var limited = Fit(Size(result) - 1);
            var limitedRows = ((ModelParameterResult)limited.data!).observed_dimensions!;
            Require(limitedRows.Length == 11 && limitedRows.SequenceEqual(rows[..11]) && limited.warnings[0] == result.warnings[0]);
            for (int target = 11; target >= 1; target--)
            {
                int boundary = Size(limited);
                Require(((ModelParameterResult)Fit(boundary).data!).observed_dimensions!.Length == target);
                limited = Fit(boundary - 1);
                var kept = ((ModelParameterResult)limited.data!).observed_dimensions!;
                Require(kept.Length == target - 1 && kept.SequenceEqual(rows[..kept.Length]) && Size(limited) <= boundary - 1);
                Require(limited.warnings[^1].Contains($"{kept.Length} of 12", StringComparison.Ordinal));
            }
            return Task.CompletedTask;
        });
        await test("model parameter explicit body scope normalizes bounds and write identity", () =>
        {
            var one = Parse(Input()); var input = Input(); input["scope_handles"] = new[] { "00150", "ab" }; var scoped = Parse(input);
            Require(scoped.UsesExpressionPlan && WriteRecovery.NotGuaranteed(scoped) && scoped.ScopeHandles!.SequenceEqual(new[] { "150", "AB" }) && scoped.Fingerprint() != one.Fingerprint());
            input["scope_handles"] = Enumerable.Range(1, 512).Select(i => i.ToString("X")).ToArray();
            Require(Parse(input).ScopeHandles!.Length == 512);
            foreach (var bad in new object[] { Array.Empty<string>(), new[] { "AB" }, new[] { "150", "0150" }, new[] { "150", "0" }, "150" })
            { input["scope_handles"] = bad; try { Parse(input); } catch (DomainException) { continue; } throw new Exception("Invalid body scope accepted."); }
            var request = Wire.Element(new { bridge_instance_id = bridge, document_id = document, expected_revision = "1", operation_id = op, units = "Millimeters", operations = new[] { new { operation = "name_model_parameter", handles = new[] { "150" }, scope_handles = new[] { "150", "AB" }, variable_id = variable, expected_name = "V", name = "Driver" } } });
            Require(ModifyValidation.Parse(request).Operations.Single() is ModelParameterNameEdit n && n.ScopeHandles!.Length == 2);
            var tool = ToolCatalog.Tools.Single(t => t.Name == "nanocad.set_model_parameter"); Require(tool.InputSchema.GetRawText().Contains("scope_handles") && tool.OutputSchema!.Value.GetRawText().Contains("rebuilt_bodies"));
            return Task.CompletedTask;
        });
        await test("model parameter canonical identity and fingerprint preserve expression precondition", () =>
        {
            var a = Parse(Input()); Require(a.Handle == "150" && a.VariableId == variable.ToUpperInvariant() && a.ExpectedRevision == "7" && a.Expression == "151");
            var b = Input(); b["handle"] = "150"; b["variable_id"] = variable.ToUpperInvariant(); b["expected_revision"] = "7";
            Require(a.Fingerprint() == Parse(b).Fingerprint());
            b["expected_expression"] = "150.0"; Require(a.Fingerprint() != Parse(b).Fingerprint());
            b = Input(); b["value"] = 152; Require(a.Fingerprint() != Parse(b).Fingerprint());
            return Task.CompletedTask;
        });
        await test("model parameter rejects mixed value expression units stale identity and duplicate fields", () =>
        {
            foreach (var change in new (string, object?)[] { ("value", "151"), ("units", "Inches"), ("variable_id", Guid.Empty.ToString("B")), ("handle", "0"), ("expected_expression", ""), ("expected_revision", "18446744073709551616"), ("expression", "150+1"), ("evaluate", true) })
            {
                var input = Input(); input[change.Item1] = change.Item2;
                try { Parse(input); } catch (DomainException) { continue; }
                throw new Exception("Invalid model parameter accepted: " + change.Item1);
            }
            string valid = Wire.Element(Input()).GetRawText(); using var duplicate = JsonDocument.Parse(valid[..^1] + ",\"value\":152}");
            try { ModelParameterValidation.Parse(duplicate.RootElement); } catch (DomainException) { return Task.CompletedTask; }
            throw new Exception("Duplicate field accepted.");
        });
        await test("model parameter exact replay retains committed and unknown outcomes", async () =>
        {
            foreach (bool committed in new[] { true, false })
            {
                using var queue = new WriteQueue(Environment.CurrentManagedThreadId);
                var request = new Request(Wire.Version, Guid.NewGuid().ToString("D"), "set_model_parameter", 8000, Wire.Element(Input()));
                var (job, replay) = queue.Enqueue(request); Require(!replay && queue.Take() == job && job.TryStart());
                Result result;
                if (committed)
                {
                    var verified = new ModelParameterResult(op, "committed", false, new(bridge, document, "150"), Guid.NewGuid().ToString("B"), variable, "150", "151", 151, "Millimeters", 42, "not_tested")
                    {
                        model_healthy = true,
                        downstream_values_changed = false
                    };
                    var legacyData = Wire.Element(verified with { model_healthy = null, downstream_values_changed = null });
                    Require(!legacyData.TryGetProperty("model_healthy", out _) && !legacyData.TryGetProperty("downstream_values_changed", out _));
                    result = Result.Ok(request.requestId, verified);
                    job.BeginCommit(); job.RecordCommit(result);
                }
                else result = job.Error("WRITE_OUTCOME_UNKNOWN", "Native result unavailable.", "unknown");
                job.Complete(result);
                var (same, isReplay) = queue.Enqueue(request with { requestId = Guid.NewGuid().ToString("D") }); Require(same == job && isReplay && queue.Take() == null);
                var replayed = WriteJob.ForCaller(await same.Completion, "replay-request", true);
                Require(replayed.request_id == "replay-request" && (committed
                    ? replayed.data is ModelParameterResult { replayed: true, model_healthy: true, downstream_values_changed: false }
                    : replayed.error?.details is WriteErrorDetails { replayed: true, outcome: "unknown" }));
                if (committed)
                {
                    var data = Wire.Element(replayed).GetProperty("data");
                    Require(data.GetProperty("model_healthy").GetBoolean() && !data.GetProperty("downstream_values_changed").GetBoolean());
                }
                var different = Input(); different["value"] = 152;
                try { queue.Enqueue(request with { arguments = Wire.Element(different) }); } catch (DomainException e) when (e.Error.code == "OPERATION_ID_CONFLICT") { continue; }
                throw new Exception("Operation ID conflict was not retained.");
            }
        });
        await test("model parameter candidate exposes a bounded write contract", () =>
        {
            var tool = ToolCatalog.Tools.Single(t => t.Name == "nanocad.set_model_parameter");
            Require(Wire.WriteOperations.Contains("set_model_parameter") && !Wire.ReadOperations.Contains("set_model_parameter") && tool.Annotations?.ReadOnlyHint == false);
            File.WriteAllText(Path.Combine(Environment.GetEnvironmentVariable("NANOCAD_MCP_HOME")!, "tools-list.json"), JsonSerializer.Serialize(ToolCatalog.Tools, Wire.Json));
            return Task.CompletedTask;
        });
    }
}
