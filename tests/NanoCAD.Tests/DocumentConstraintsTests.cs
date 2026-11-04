using System.Text.Json.Nodes;
using NanoCAD.Contracts;
using NanoCAD.McpServer;

internal static class DocumentConstraintsTests
{
    internal static async Task Run(Func<string, Func<Task>, Task> test)
    {
        var id = Guid.NewGuid().ToString("D");
        JsonObject Input() => new() { ["bridge_instance_id"] = id, ["document_id"] = id, ["expected_revision"] = "7", ["handles"] = new JsonArray("aB", "00cD") };
        void Deny(JsonObject input) { try { Validation.Parse("inspect_constraints", Wire.Element(input)); } catch (DomainException) { return; } throw new Exception("Invalid drawing-constraint context accepted"); }
        await test("drawing constraints read binds explicit current context and selected handles", () =>
        {
            var a = Validation.Parse("inspect_constraints", Wire.Element(Input()));
            if (a.BridgeId != id || a.DocumentId != id || a.ExpectedRevision != "7" || !a.Handles!.SequenceEqual(new[] { "AB", "CD" })) throw new Exception("Read context changed");
            foreach (var field in new[] { "bridge_instance_id", "document_id", "expected_revision", "handles" }) { var p = Input(); p.Remove(field); Deny(p); }
            foreach (var handles in new[] { new JsonArray("AB", "00ab"), new JsonArray("0"), new JsonArray() })
            { var p = Input(); p["handles"] = handles; Deny(p); }
            var extra = Input(); extra["solve"] = true; Deny(extra);
            return Task.CompletedTask;
        });
        await test("drawing constraints tool exposes read-only selected WCS XY network metadata", () =>
        {
            var t = ToolCatalog.Tools.Single(t => t.Name == "nanocad.inspect_constraints");
            if (t.Annotations?.ReadOnlyHint != true || t.Annotations.DestructiveHint != false || t.InputSchema.GetProperty("additionalProperties").GetBoolean() ||
                t.InputSchema.GetProperty("properties").GetProperty("handles").TryGetProperty("maxItems", out _) ||
                !t.InputSchema.GetProperty("properties").TryGetProperty("workplane", out _) || !AuditServerContractSupport.Output("inspect_constraints").GetRawText().Contains("references_outside_selection")) throw new Exception("Missing constraint scope or metadata");
            return Task.CompletedTask;
        });
    }
}
