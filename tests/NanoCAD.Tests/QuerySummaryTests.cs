using System.Text.Json;
using NanoCAD.Contracts;
using NanoCAD.McpServer;

internal static class QuerySummaryTests
{
    internal static async Task Run(Func<string, Func<Task>, Task> test)
    {
        await test("query summary preserves list defaults and uses the same filters without pagination", () =>
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
            var args = Parse(new { document_id = id, summary_only = true, space = "paper", types = new[] { "Line" }, layers = new[] { "0" },
                bounds = new { min = new[] { -1, -1 }, max = new[] { 1, 1 }, mode = "within" } });
            if (!args.SummaryOnly || args.QueryBounds?.Mode != "within" || args.Types?.Single() != "Line" || args.Layers?.Single() != "0" || args.ReadSpace != "paper")
                throw new Exception("Summary did not preserve ordinary filters");
            Deny(new { document_id = id, summary_only = "true" }, "summary_only");
            Deny(new { document_id = id, summary_only = true, page_size = 1 }, "page_size");
            Deny(new { document_id = id, summary_only = true, cursor = "page" }, "cursor");
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
