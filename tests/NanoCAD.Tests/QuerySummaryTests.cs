using System.Text.Json;
using NanoCAD.Contracts;
using NanoCAD.McpServer;

internal static class QuerySummaryTests
{
    internal static async Task Run(Func<string, Func<Task>, Task> test)
    {
        await test("body name query and inspection publish the native name source and group identity", () =>
        {
            var summary = AuditServerContractSupport.Output("query_objects").GetProperty("$defs").GetProperty("summary");
            var inspected = AuditServerContractSupport.Output("inspect_objects").GetProperty("$defs");
            foreach (var item in new[] { summary, inspected.GetProperty("entity"), inspected.GetProperty("compact_entity") })
            {
                var fields = item.GetProperty("properties");
                var sources = fields.GetProperty("body_name_source").GetProperty("anyOf").EnumerateArray().Single(s => s.TryGetProperty("enum", out _));
                foreach (var value in new[] { "native_part_name", "native_tree", "dwg_group_description", "ambiguous_dwg_group" })
                    if (!sources.GetProperty("enum").EnumerateArray().Any(source => source.GetString() == value))
                        throw new Exception("Missing body name source: " + value);
                var group = fields.GetProperty("body_name_group").GetProperty("anyOf").EnumerateArray()
                    .Single(option => option.TryGetProperty("properties", out _)).GetProperty("properties");
                if (!fields.TryGetProperty("part_name", out _) || !fields.TryGetProperty("native_tree_name", out _) ||
                    !fields.TryGetProperty("body_name_group_ambiguous", out _) || !group.TryGetProperty("name", out _) ||
                    !group.TryGetProperty("description", out _) ||
                    !group.GetProperty("ref").GetProperty("properties").TryGetProperty("handle", out _))
                    throw new Exception("Missing tree label or named group reference.");
            }
            return Task.CompletedTask;
        });
        await test("part designation and existing body aliases remain separately searchable", () =>
        {
            var body = new EntityRef("bridge", "drawing", "2A");
            var group = new ModelBodyNameGroup("FORMER_LABEL", body with { handle = "3B" }) { description = "Former label" };
            var item = new EntitySummary(body, "Solid3d", "Solid3d", null, "model", "supported")
            {
                body_name = "Mounting plate",
                body_name_source = "native_part_name",
                part_name = "Mounting plate",
                native_tree_name = "Box 1",
                body_name_group = group
            };
            if (item.BodyNameMatchSource("MOUNTING PLATE") != "native_part_name" || item.BodyNameMatchSource("Box 1") != null ||
                item.BodyNameMatchSource("Former label") != "dwg_group_description" || item.BodyNameMatchSource(group.name) != null ||
                item.BodyNameMatchSource("Plate") != null || (item with { body_name_group = null }).BodyNameMatchSource("Box 1") != "native_tree")
                throw new Exception("A part name, previous effective label or its source was misidentified.");
            var aggregate = new QuerySummary();
            aggregate.Add("Solid3d", "0", null, null, "native_part_name");
            aggregate.Add("Solid3d", "0", null, null, "dwg_group_description");
            var counts = Wire.Element(aggregate.Result("model", unavailableBodyNames: 0)).GetProperty("by_body_name_match_source");
            if (counts.GetArrayLength() != 2 || counts.EnumerateArray().Any(c => c.GetProperty("count").GetInt32() != 1))
                throw new Exception("Name-filtered counts conflated part captions and legacy aliases.");
            return Task.CompletedTask;
        });
        await test("native face query filters supporting geometry and preserves distinct axis and plane distances", () =>
        {
            var cylinder = ModelFaceQuery.Parse(Wire.Element(new
            {
                surface = "cylinder",
                direction = new[] { 0, 0, -5 },
                min_radius = 2,
                max_radius = 6,
                datum_point = new[] { 3, 4, 100 },
                max_distance = 5
            }));
            if (!cylinder.Accept([0, 0, 3], [0, 0, 1], 4, out var distance) || distance != 5 ||
                cylinder.Accept([0, 0, 3], [0, 0, 1], 7, out _) || cylinder.Accept([0, 0, 3], [0, 0, 1], null, out _))
                throw new Exception("Unexpected cylinder axis or radius filter.");
            var plane = ModelFaceQuery.Parse(Wire.Element(new { surface = "plane", datum_point = new[] { 100, 200, 7 }, max_distance = 1 }));
            if (!plane.Accept([0, 0, 6], [0, 0, -1], null, out distance) || distance != 1)
                throw new Exception("Unexpected supporting plane distance.");
            return Task.CompletedTask;
        });
        await test("direct face queries distinguish feature identities from editable B-rep faces", () =>
        {
            var search = ModelFaceQuery.Parse(Wire.Element(new { surface = "cylinder", identity_space = "brep", cylinder_role = "bore", adjacent_to_face_id = "7" }));
            if (!search.DirectBrep || search.CylinderRole != "bore" || search.AdjacentTo != "7" ||
                ModelFaceQuery.Parse(Wire.Element(new { surface = "cylinder" })).DirectBrep)
                throw new Exception("The explicit direct identity space and default model space must remain distinct.");
            foreach (var invalid in new object[]
            {
                new { surface = "plane", identity_space = "brep", cylinder_role = "external" },
                new { surface = "cylinder", cylinder_role = "bore" },
                new { surface = "cylinder", identity_space = "brep", adjacent_to_face_id = "{current-model-guid}" },
                new { surface = "cylinder", identity_space = "brep", cylinder_role = "unknown" }
            })
            {
                try { ModelFaceQuery.Parse(Wire.Element(invalid)); }
                catch (DomainException e) when (e.Error.code == "INVALID_ARGUMENT") { continue; }
                throw new Exception("An incompatible direct query passed validation.");
            }
            return Task.CompletedTask;
        });
        await test("native solid names are searchable across pages and optional in basic observations", () =>
        {
            var doc = Guid.NewGuid().ToString("D");
            const string name = "Guide bushing \u00D824";
            var args = Validation.Parse("query_objects", Wire.Element(new { document_id = doc, body_name = name, page_size = 1 }));
            var other = args with { BodyName = "Cover" };
            if (args.BodyName != name || args.FilterKey("query_objects") == other.FilterKey("query_objects"))
                throw new Exception("The body-name filter must preserve the requested label and bind a cursor to that query.");

            var summary = new EntitySummary(new(doc, doc, "2A"), "Solid3d", "Solid3d", null, "model", "supported");
            if (Wire.Element(summary).TryGetProperty("body_name", out _) ||
                Wire.Element(summary with { body_name = name }).GetProperty("body_name").GetString() != name)
                throw new Exception("Only available native names should be emitted.");
            var page = Wire.Element(new QuerySummary().Result("model", unavailableBodyNames: 2));
            if (page.GetProperty("page_solids_excluded_unavailable_name").GetInt32() != 2)
                throw new Exception("A name-filtered summary must disclose candidates without an available native name.");
            return Task.CompletedTask;
        });
        await test("query summary preserves list defaults and accepts paged spatial aggregation", () =>
        {
            var id = Guid.NewGuid().ToString("D");
            Arguments Parse(object input) => Validation.Parse("query_objects", Wire.Element(input));
            void Deny(object input, string field)
            {
                try { Parse(input); }
                catch (DomainException e) when (e.Error.code == "INVALID_ARGUMENT" && Wire.Element(e.Error.details!).GetProperty("path").GetString() == "$." + field) { return; }
                throw new Exception("Expected summary input error at " + field);
            }
            if (Parse(new { document_id = id }).SummaryOnly || Parse(new { document_id = id, summary_only = false, page_size = 1 }).PageSize != 1)
                throw new Exception("Existing object pagination changed");
            var args = Parse(new
            {
                document_id = id,
                summary_only = true,
                space = "paper",
                types = new[] { "Line" },
                layers = new[] { "0" },
                bounds = new { min = new[] { -1, -1 }, max = new[] { 1, 1 }, mode = "within" }
            });
            if (!args.SummaryOnly || args.QueryBounds?.Mode != "within" || args.Types?.Single() != "Line" || args.Layers?.Single() != "0" || args.ReadSpace != "paper")
                throw new Exception("Summary did not preserve ordinary filters");
            Deny(new { document_id = id, summary_only = "true" }, "summary_only");
            if (Parse(new { document_id = id, summary_only = true, page_size = 1, cursor = "page" }).PageSize != 1)
                throw new Exception("Summary pagination is unavailable.");
            var box = Parse(new { document_id = id, bounds = new { min = new[] { 0, 0, 10 }, max = new[] { 10, 10, 20 }, mode = "within" } }).QueryBounds!;
            if (new QueryEnvelope(0, 0, 0, 10, 10, 5).Matches(box) || !new QueryEnvelope(1, 1, 12, 9, 9, 19).Matches(box))
                throw new Exception("Z-separated envelopes were not distinguished.");
            var unavailable = new QuerySummary();
            unavailable.Add("Line", "0", new QueryEnvelope(0, 0, double.NaN, 1, 1, 12), box);
            var partial = Wire.Element(unavailable.Result("model", "opaque"));
            if (partial.GetProperty("complete").GetBoolean() || partial.GetProperty("aggregation_scope").GetString() != "this_page" ||
                partial.GetProperty("spatial_excluded_unavailable_bounds_count").GetInt32() != 1) throw new Exception("Partial XYZ summary lost its unavailable count.");
            var tool = ToolCatalog.Tools.Single(tool => tool.Name == "nanocad.query_objects");
            if (tool.InputSchema.GetProperty("properties").GetProperty("summary_only").GetProperty("type").GetString() != "boolean" ||
                tool.OutputSchema is not { } output || !output.GetRawText().Contains("spatial_excluded_unavailable_bounds_count", StringComparison.Ordinal))
                throw new Exception("Summary switch or result missing from the query tool contract");
            return Task.CompletedTask;
        });
        await test("query summary groups types and layers and reports finite WCS extents", () =>
        {
            var summary = new QuerySummary();
            var empty = Wire.Element(summary.Result("model"));
            if (empty.GetProperty("total_count").GetInt32() != 0 || empty.GetProperty("bounds").ValueKind != JsonValueKind.Null)
                throw new Exception("Empty summary should have no bounds");
            summary.Add("Line", "Model", new(-5, -4, -3, 4, 5, 6), null);
            summary.Add("Line", "model", new(7, 8, 9, 10, 11, 12), null);
            summary.Add("Ray", "0", null, null);
            summary.Add("Circle", "0", new(0, 0, double.NaN, 1, 1, 0), null);
            var result = Wire.Element(summary.Result("model"));
            var bounds = result.GetProperty("bounds");
            if (result.GetProperty("total_count").GetInt32() != 4 || result.GetProperty("bounds_unavailable_count").GetInt32() != 2 ||
                result.GetProperty("spatial_excluded_unavailable_bounds_count").GetInt32() != 0 ||
                result.GetProperty("by_layer").GetArrayLength() != 2 || result.GetProperty("by_type").GetArrayLength() != 3 ||
                result.GetProperty("by_layer")[1].GetProperty("count").GetInt32() != 2 ||
                !bounds.GetProperty("min").EnumerateArray().Select(value => value.GetDouble()).SequenceEqual(new double[] { -5, -4, -3 }) ||
                !bounds.GetProperty("max").EnumerateArray().Select(value => value.GetDouble()).SequenceEqual(new double[] { 10, 11, 12 }) ||
                !result.GetProperty("complete").GetBoolean() || result.TryGetProperty("items", out _) || result.TryGetProperty("next_cursor", out _))
                throw new Exception("Summary counts, bounds or pagination metadata are incorrect");
            return Task.CompletedTask;
        });
        await test("query summary separates excluded XY extents from matched objects without XYZ bounds", () =>
        {
            var summary = new QuerySummary();
            var intersects = new QueryBounds407([-2, -2], [2, 2], "intersects");
            var within = new QueryBounds407([-2, -2], [2, 2], "within");
            summary.Add("Line", "0", new(2, 0, -1, 3, 1, 0), intersects); // Inclusive edge contact.
            summary.Add("Line", "0", new(2, 0, -1, 3, 1, 0), within); // Outside containment.
            summary.Add("Circle", "0", new(0, 0, double.PositiveInfinity, 1, 1, 0), within);
            summary.Add("Ray", "0", null, within);
            summary.Add("Line", "0", new(2, 0, 0, 1, 1, 0), within); // Reversed native extent.
            var result = Wire.Element(summary.Result("paper"));
            if (result.GetProperty("total_count").GetInt32() != 2 || result.GetProperty("bounds_unavailable_count").GetInt32() != 1 ||
                result.GetProperty("spatial_excluded_unavailable_bounds_count").GetInt32() != 2 ||
                result.GetProperty("bounds").GetProperty("max")[0].GetDouble() != 3 || result.GetProperty("space").GetString() != "paper")
                throw new Exception("Spatial query did not preserve matching and unavailable-bound semantics");
            return Task.CompletedTask;
        });
    }
}
