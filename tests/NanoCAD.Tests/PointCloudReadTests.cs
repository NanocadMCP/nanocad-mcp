using System.Text.Json;
using NanoCAD.Contracts;
using NanoCAD.McpServer;

internal static class PointCloudReadTests
{
    public static async Task Run(Func<string, Func<Task>, Task> test)
    {
        var bridge = Guid.NewGuid().ToString("D"); var doc = Guid.NewGuid().ToString("D");
        await test("point cloud metadata requires exact context and one canonical handle", () =>
        {
            var input = new { bridge_instance_id = bridge, document_id = doc, expected_revision = "18446744073709551615", handle = "000aF" };
            var args = PointCloudReadValidation.Parse(Wire.Element(input));
            if (args.Handles!.Single() != "AF" || args.ExpectedRevision != "18446744073709551615") throw new Exception("Identity precision lost.");
            void Reject(object value)
            {
                try { PointCloudReadValidation.Parse(Wire.Element(value)); }
                catch (DomainException) { return; }
                throw new Exception("Invalid cloud input accepted.");
            }
            Reject(new { document_id = doc, expected_revision = "1", handle = "AF" });
            Reject(new { bridge_instance_id = bridge, document_id = doc, handle = "AF" });
            Reject(new { bridge_instance_id = bridge, document_id = doc, expected_revision = "1", handle = "0" });
            Reject(new { bridge_instance_id = bridge, document_id = doc, expected_revision = "1", handle = "AF", load = true });
            using var duplicate = JsonDocument.Parse(JsonSerializer.Serialize(input)[..^1] + ",\"handle\":\"B0\"}");
            try { PointCloudReadValidation.Parse(duplicate.RootElement); throw new Exception("Duplicate identity accepted."); }
            catch (DomainException) { }
            return Task.CompletedTask;
        });
        await test("cloud profile comparison uses one explicit frame, observation box, reach and distinct solid", () =>
        {
            var request = new Dictionary<string, object>
            {
                ["bridge_instance_id"] = bridge,
                ["document_id"] = doc,
                ["expected_revision"] = "7",
                ["handle"] = "1A",
                ["mode"] = "compare_section",
                ["units"] = "drawing_units",
                ["section"] = new { origin = new[] { 0, 0, 5 }, normal = new[] { 0, 0, 2 }, x_axis = new[] { 3, 0, 0 }, thickness = .1 },
                ["region"] = new { min = new[] { -5, -5, 4 }, max = new[] { 5, 5, 6 } },
                ["comparison"] = new { solid_handle = "000bc", segment_length = 1.2, capture_distance = .3, start_segment = 20 }
            };
            var result = PointCloudAnalysisValidation.Parse(Wire.Element(request));
            if (result.Comparison != new CloudProfileComparison("BC", 1.2, .3, 20) || result.Section?.Normal[2] != 1)
                throw new Exception("Cloud profile query or normal normalization changed.");
            void Reject(string key, object? value)
            {
                var altered = new Dictionary<string, object>(request);
                if (value == null) altered.Remove(key); else altered[key] = value;
                try { PointCloudAnalysisValidation.Parse(Wire.Element(altered)); }
                catch (DomainException) { return; }
                throw new Exception("Invalid profile comparison accepted: " + key);
            }
            Reject("region", null); Reject("section", null); Reject("comparison", null);
            Reject("mode", "statistics"); Reject("include_attributes", true);
            Reject("comparison", new { solid_handle = "1A", segment_length = 1, capture_distance = .3 });
            Reject("comparison", new { solid_handle = "BC", segment_length = .1, capture_distance = 0 });
            return Task.CompletedTask;
        });
        await test("point cloud inspection and analysis are published as read-only operations", () =>
        {
            foreach (string name in new[] { "inspect_point_cloud", "analyze_point_cloud" })
            {
                var tool = ToolCatalog.Tools.Single(t => t.Name == "nanocad." + name);
                if (!Wire.ReadOperations.Contains(name) || Wire.WriteOperations.Contains(name) || tool.Annotations is not { ReadOnlyHint: true, DestructiveHint: false }) throw new Exception("Point cloud read scope or annotation changed.");
            }
            File.WriteAllText(Path.Combine(Environment.GetEnvironmentVariable("NANOCAD_MCP_HOME")!, "tools-list.json"), JsonSerializer.Serialize(ToolCatalog.Tools, Wire.Json));
            return Task.CompletedTask;
        });
    }
}
