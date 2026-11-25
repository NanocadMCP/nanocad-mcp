using System.Text.Json;
using NanoCAD.Contracts;
using NanoCAD.McpServer;

internal static class ModelHistoryTests
{
    public static async Task Run(Func<string, Func<Task>, Task> test)
    {
        var bridge = Guid.NewGuid().ToString("D"); var doc = Guid.NewGuid().ToString("D");
        object Input(string[] handles) => new { bridge_instance_id = bridge, document_id = doc, expected_revision = "18446744073709551615", handles };
        void Reject(object input)
        {
            try { Validation.Parse("inspect_model_history", Wire.Element(input)); }
            catch (DomainException) { return; }
            throw new Exception("Invalid history request accepted.");
        }
        await test("rebuild diagnosis reports nearest observed errors through actual dependency paths", () =>
        {
            HistoryIssueDiagnosis.Node Item(string id, string status, params string[] inputs) => new(id, id, status, true, inputs, null);
            var sketch = Item("sketch", "Error", "face");
            var nodes = new[] { sketch, Item("profile", "Error", "sketch"), Item("helper", "Ok", "profile"),
                Item("cut", "Error", "helper", "cut2"), Item("cut2", "Error"), Item("warning", "Warn", "sketch"),
                Item("update", "NeedUpdate"), Item("disabled", "Error") with { Active = false } }.ToDictionary(n => n.Id);
            var brokenSupport = new HistoryIssueDiagnosis.Support("face", "face", "unavailable", new { reason = "bound_geometry_unavailable" });
            var evidence = new Dictionary<string, HistoryIssueDiagnosis.Evidence>
            {
                ["sketch"] = new([new("Error", "Plane unavailable")], "read", brokenSupport)
            };
            var report = HistoryIssueDiagnosis.Explain("body", nodes, evidence, () => { }, _ => { });
            string[] ExpectedPath(string parent, string child) => report.error_links.Single(link =>
                link.upstream_error_id == parent && link.downstream_error_id == child).dependency_path;
            if (report.state != "observed_errors" || report.error_links.Count != 3 ||
                !ExpectedPath("sketch", "profile").SequenceEqual(["sketch", "profile"]) ||
                !ExpectedPath("profile", "cut").SequenceEqual(["profile", "helper", "cut"]) ||
                !ExpectedPath("cut2", "cut").SequenceEqual(["cut2", "cut"]) ||
                report.issues.Single(x => x.id == "sketch") is not { role: "first_observed_error", native_evidence.sketch_support.id: "face" } ||
                report.issues.Single(x => x.id == "cut").role != "downstream_error" ||
                report.issues.Single(x => x.id == "warning").role != "warning" ||
                report.issues.Single(x => x.id == "update").role != "update_requested" ||
                report.issues.Any(x => x.id == "disabled") ||
                report.unresolved_edges.Single() != new HistoryIssueDiagnosis.Boundary("sketch", "face", "outside_part_history"))
                throw new Exception("The diagnostic did not preserve the upstream/downstream identities and scope of the fault.");
            return Task.CompletedTask;
        });
        await test("rebuild diagnosis retains unknown and cyclic dependencies without inventing a root", () =>
        {
            HistoryIssueDiagnosis.Node Item(string id, string status, params string[] inputs) => new(id, id, status, true, inputs, null);
            var nodes = new[] { Item("a", "Error", "b"), Item("b", "Error", "a"), Item("consumer", "Error", "b"),
                Item("other", "Error", "unknown") with { InventoryProblem = "native_dependency_list_unavailable" },
                Item("unknown", "Unknown") }.ToDictionary(n => n.Id);
            var report = HistoryIssueDiagnosis.Explain("body", nodes, new Dictionary<string, HistoryIssueDiagnosis.Evidence>(), () => { }, _ => { });
            if (!report.issues.Where(n => n.role == "cyclic_error").Select(n => n.id).ToHashSet().SetEquals(["a", "b"]) ||
                report.issues.Single(n => n.id == "consumer").role != "downstream_error" ||
                report.issues.Single(n => n.id == "other").role != "unresolved_error" ||
                report.unresolved_edges.Count != 2 || !report.unknown_status_ids.SequenceEqual(["unknown"]))
                throw new Exception("An unresolved or cyclic failure was attributed to an unrelated feature.");
            var unowned = new HistoryIssueDiagnosis.Node("foreign", null, null, true, [], "part_ownership_unverified");
            var helper = new HistoryIssueDiagnosis.Node("helper", null, null, true, [], "not_a_history_item");
            var partial = HistoryIssueDiagnosis.Explain("body", new[] { unowned, helper }.ToDictionary(n => n.Id),
                new Dictionary<string, HistoryIssueDiagnosis.Evidence>(), () => { }, _ => { });
            if (partial.state != "status_incomplete" || !partial.unknown_status_ids.SequenceEqual(["foreign"]))
                throw new Exception("A helper with no status must remain distinct from a feature of unverified ownership.");
            return Task.CompletedTask;
        });
        await test("part chain two stored hole slots can use the same parent with distinct markers", () =>
        {
            var first = NanoCADBridge.ModelHolePlacementSlots.Observe(0, false, "A", 12, 0.25, 3);
            var second = NanoCADBridge.ModelHolePlacementSlots.Observe(1, false, "A", 17, 0.75, 20000000001L);
            if (first is not { slot: 0, object_id: "A", gs_marker: 12, parameter: 0.25 } ||
                second is not { slot: 1, object_id: "A", gs_marker: 17, data: "20000000001" })
                throw new Exception("Geometric placement slots were collapsed or did not preserve order.");
            return Task.CompletedTask;
        });
        await test("lost support preview takes one explicit sketch and WorkPlane and keeps frame references by default", () =>
        {
            var sketch = Guid.NewGuid().ToString("B");
            var plane = Guid.NewGuid().ToString("B");
            object Request(string? focus, string[] handles, object preview) => new
            {
                bridge_instance_id = bridge,
                document_id = doc,
                expected_revision = "19",
                handles,
                history_focus = focus,
                support_recovery_preview = preview
            };
            var selected = new { plane_id = plane };
            var args = Validation.Parse("inspect_model_history", Wire.Element(Request(sketch, ["A"], selected)));
            if (args.SupportRecoveryPreview != new ModelSupportRecoveryPreview(plane.ToUpperInvariant(), "preserve"))
                throw new Exception("Read preview lost the selected WorkPlane or applied an implicit frame change.");
            var explicitFrame = new { plane_id = plane, frame_reference_policy = "use_target_datum" };
            if (Validation.Parse("inspect_model_history", Wire.Element(Request(sketch, ["A"], explicitFrame))).SupportRecoveryPreview?.FrameReferencePolicy != "use_target_datum")
                throw new Exception("Lost explicit frame replacement.");
            Reject(Request("issues", ["A"], selected));
            Reject(Request(sketch, ["A", "B"], selected));
            Reject(Request(sketch, ["A"], new { plane_id = "nearby" }));
            Reject(Request(sketch, ["A"], new { plane_id = plane, frame_reference_policy = "automatic" }));
            Reject(Request(sketch, ["A"], new { x = 0, y = 0, z = 1 }));
            return Task.CompletedTask;
        });
        await test("part chain follows only a selected native history ID in one part", () =>
        {
            var sketchId = Guid.NewGuid().ToString("B");
            object Request(string? focus, string[] handles) => new
            {
                bridge_instance_id = bridge,
                document_id = doc,
                expected_revision = "9",
                handles,
                history_focus = focus,
                include_downstream_history = true
            };
            var args = Validation.Parse("inspect_model_history", Wire.Element(Request(sketchId, ["A"])));
            if (!args.IncludeDownstreamHistory || args.HistoryFocus != sketchId.ToUpperInvariant()) throw new Exception("Lost the selected native source.");
            if (Validation.Parse("inspect_model_history", Wire.Element(Input(["A"]))).IncludeDownstreamHistory) throw new Exception("Unexpected traversal.");
            Reject(Request("issues", ["A"])); Reject(Request(sketchId, ["A", "B"])); Reject(Request(null, ["A"]));
            return Task.CompletedTask;
        });
        await test("model history requires explicit context and bounded canonical handles", () =>
        {
            var args = Validation.Parse("inspect_model_history", Wire.Element(Input(new[] { "00af", "B0" })));
            if (!args.Handles!.SequenceEqual(new[] { "AF", "B0" }) || args.ExpectedRevision != "18446744073709551615") throw new Exception("History identity changed.");
            Reject(Input(Array.Empty<string>())); Validation.Parse("inspect_model_history", Wire.Element(Input(new[] { "1", "2", "3", "4", "5" })));
            Reject(new { document_id = doc, expected_revision = "9", handles = new[] { "1" } });
            Reject(new { bridge_instance_id = bridge, document_id = doc, handles = new[] { "1" } });
            Reject(new { bridge_instance_id = bridge, document_id = doc, expected_revision = "9", handles = new[] { "1" }, rebuild = true });
            return Task.CompletedTask;
        });
        await test("model history catalog is read only and separates graph scope from geometry", () =>
        {
            var tool = ToolCatalog.Tools.Single(t => t.Name == "nanocad.inspect_model_history");
            if (tool.Annotations?.ReadOnlyHint != true || tool.Annotations.DestructiveHint != false || Wire.WriteOperations.Contains("inspect_model_history")) throw new Exception("Read annotation changed.");
            if (tool.InputSchema.GetProperty("properties").GetProperty("handles").TryGetProperty("maxItems", out _)) throw new Exception("Unbounded or misleading graph contract.");
            File.WriteAllText(Path.Combine(Environment.GetEnvironmentVariable("NANOCAD_MCP_HOME")!, "tools-list.json"), JsonSerializer.Serialize(ToolCatalog.Tools, Wire.Json));
            return Task.CompletedTask;
        });
        await test("model history selective sketch geometry bounds identity and keeps metadata default", () =>
        {
            var sketch = Guid.NewGuid().ToString("B");
            var args = Validation.Parse("inspect_model_history", Wire.Element(new { bridge_instance_id = bridge, document_id = doc, expected_revision = "9", handles = new[] { "A" }, sketch_geometry_ids = new[] { sketch } }));
            if (args.SketchGeometryIds?.Single() != sketch.ToUpperInvariant()) throw new Exception("Sketch identity not canonicalized.");
            if (Validation.Parse("inspect_model_history", Wire.Element(Input(new[] { "A" }))).SketchGeometryIds != null) throw new Exception("Ordinary history unexpectedly requests geometry.");
            foreach (var ids in new[] { Array.Empty<string>(), new[] { sketch, sketch.ToUpperInvariant() }, new[] { "A" }, new[] { Guid.Empty.ToString("B") } })
                Reject(new { bridge_instance_id = bridge, document_id = doc, expected_revision = "9", handles = new[] { "A" }, sketch_geometry_ids = ids });
            var many = Enumerable.Range(0, 5).Select(_ => Guid.NewGuid().ToString("B")).ToArray();
            if (Validation.Parse("inspect_model_history", Wire.Element(new { bridge_instance_id = bridge, document_id = doc, expected_revision = "9", handles = new[] { "A" }, sketch_geometry_ids = many })).SketchGeometryIds!.Length != 5) throw new Exception("A former sketch count ceiling remains.");
            Reject(new { bridge_instance_id = bridge, document_id = doc, expected_revision = "9", handles = new[] { "A" }, sketch_geometry_ids = new[] { sketch }, solve = true });
            return Task.CompletedTask;
        });
    }
}
