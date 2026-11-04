using System.Text.Json;
using NanoCAD.Contracts;
using NanoCAD.McpServer;

internal static class ModelVariablesTests
{
    public static async Task Run(Func<string, Func<Task>, Task> test)
    {
        var bridge = Guid.NewGuid().ToString("D"); var document = Guid.NewGuid().ToString("D");
        object Input() => new { bridge_instance_id = bridge, document_id = document, expected_revision = "18446744073709551615", handle = "00150" };
        void Reject(object input)
        { try { Validation.Parse("get_model_variables", Wire.Element(input)); } catch (DomainException) { return; } throw new Exception("Invalid variables context accepted."); }
        await test("model variables requires explicit context and cannot request evaluation", () =>
        {
            var args = Validation.Parse("get_model_variables", Wire.Element(Input()));
            if (args.BridgeId != bridge || args.DocumentId != document || args.ExpectedRevision != "18446744073709551615" || args.Handles![0] != "150") throw new Exception("Identity changed.");
            Reject(new { document_id = document, expected_revision = "1" }); Reject(new { bridge_instance_id = bridge, document_id = document });
            Reject(new { bridge_instance_id = bridge, document_id = document, expected_revision = "1", evaluate = true });
            Reject(new { bridge_instance_id = bridge, document_id = document, expected_revision = "1", network = "ACAD" });
            Reject(new { bridge_instance_id = bridge, document_id = document, expected_revision = "1" });
            Reject(new { bridge_instance_id = bridge, document_id = document, expected_revision = "1", handle = "0" });
            return Task.CompletedTask;
        });
        await test("model variables catalog preserves downstream direction and native value scope", () =>
        {
            var tool = ToolCatalog.Tools.Single(t => t.Name == "nanocad.get_model_variables");
            if (tool.Annotations?.ReadOnlyHint != true || tool.Annotations.DestructiveHint != false || Wire.WriteOperations.Contains("get_model_variables")) throw new Exception("Write capability leaked.");
            var help = AuditServerContractSupport.Help("get_model_variables");
            if (!help.Contains("consumers, not input dependencies") || !help.Contains("not a universal length unit")) throw new Exception("Variable semantics ambiguous.");
            File.WriteAllText(Path.Combine(Environment.GetEnvironmentVariable("NANOCAD_MCP_HOME")!, "tools-list.json"), JsonSerializer.Serialize(ToolCatalog.Tools, Wire.Json));
            return Task.CompletedTask;
        });
    }
}
