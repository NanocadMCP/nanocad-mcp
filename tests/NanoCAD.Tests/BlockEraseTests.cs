using System.Text.Json;
using NanoCAD.Contracts;
using NanoCAD.McpServer;

internal static class BlockEraseTests
{
    public static async Task Run(Func<string, Func<Task>, Task> test)
    {
        void Check(bool ok) { if (!ok) throw new Exception("Block erase expectation failed."); }
        ModifyArguments Parse(params object[] ops) => ModifyValidation.Parse(Wire.Element(new { bridge_instance_id = "11111111-1111-1111-1111-111111111111", document_id = "22222222-2222-2222-2222-222222222222", operation_id = "33333333-3333-3333-3333-333333333333", expected_revision = "1", units = "Millimeters", operations = ops }));
        object Erase(params string[] handles) => new { operation = "erase", handles };
        await test("block erase preserves ordered transform intent rejects reuse and binds erased identities", () =>
        {
            var move = new { operation = "move", handles = new[] { "a" }, displacement = new[] { 2, 3 } };
            var parsed = Parse(move, Erase("A", "b"));
            Check(parsed.Operations[0] is MoveSpec && parsed.Operations[1] is EraseSpec && parsed.Operations[1].Handles.SequenceEqual(new[] { "A", "B" }));
            Check(parsed.Fingerprint() == Parse(move, Erase("a", "B")).Fingerprint() && parsed.Fingerprint() != Parse(move, Erase("A", "C")).Fingerprint());
            foreach (var ops in new[] { new object[] { Erase("A"), move }, new object[] { Erase("A"), Erase("a") } })
            { try { Parse(ops); } catch (DomainException e) when (e.Error.code == "INVALID_OPERATION_ORDER") { continue; } throw new Exception("Use after erase accepted."); }
            return Task.CompletedTask;
        });
        await test("block erase catalog retains explicit evidence and dependency requirements", () =>
        {
            var tool = ToolCatalog.Tools.Single(t => t.Name == "nanocad.modify_entities");
            var help = AuditServerContractSupport.Help("modify_entities", "erase");
            Check(tool.Annotations?.ReadOnlyHint == false && help.Contains("erase_assessment", StringComparison.Ordinal) && help.Contains("consumers", StringComparison.Ordinal) && help.Contains("authorization", StringComparison.Ordinal));
            var v = AuditServerContractSupport.Branch("modify_entities", "erase");
            Check(v.GetProperty("required").GetArrayLength() == 2 && !v.GetProperty("additionalProperties").GetBoolean());
            File.WriteAllText(Path.Combine(Environment.GetEnvironmentVariable("NANOCAD_MCP_HOME")!, "tools-list.json"), JsonSerializer.Serialize(ToolCatalog.Tools, Wire.Json)); return Task.CompletedTask;
        });
    }
}
