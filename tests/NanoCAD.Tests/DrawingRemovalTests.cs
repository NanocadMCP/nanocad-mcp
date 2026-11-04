using NanoCAD.Contracts;
using NanoCAD.McpServer;
using System.Text.Json.Nodes;

internal static class DrawingRemovalTests
{
    internal static async Task Run(Func<string, Func<Task>, Task> test)
    {
        var id = Guid.NewGuid().ToString(); var native = Guid.NewGuid().ToString("B");
        JsonObject Input(JsonObject operation) => new() { ["bridge_instance_id"] = id, ["document_id"] = id, ["expected_revision"] = "1", ["operation_id"] = id, ["units"] = "Millimeters", ["operations"] = new JsonArray(operation) };
        void Deny(Action f) { try { f(); } catch (DomainException) { return; } throw new Exception("Unsafe removal contract accepted"); }
        await test("drawing removal requires explicit bounded unique constraint IDs and sole operation", () =>
        {
            var row = new JsonObject { ["operation"] = "remove_drawing_constraints", ["handles"] = new JsonArray("A", "B"), ["constraint_ids"] = new JsonArray(native) }; var input = Input(row);
            var edit = (DrawingProfileEdit)ModifyValidation.Parse(Wire.Element(input)).Operations.Single(); if (edit.ConstraintIds!.Single() != native.ToUpperInvariant()) throw new Exception("Constraint identity lost");
            row["constraint_ids"]!.AsArray().Add(native.ToUpperInvariant()); Deny(() => ModifyValidation.Parse(Wire.Element(input)));
            row["constraint_ids"] = new JsonArray(); Deny(() => ModifyValidation.Parse(Wire.Element(input))); row["constraint_ids"] = new JsonArray("A"); Deny(() => ModifyValidation.Parse(Wire.Element(input)));
            row["constraint_ids"] = new JsonArray(native); input["operations"]!.AsArray().Add(row.DeepClone()); Deny(() => ModifyValidation.Parse(Wire.Element(input))); return Task.CompletedTask;
        });
        await test("drawing removal preserves exact unused variable preconditions and rejects absent values", () =>
        {
            var row = new JsonObject { ["operation"] = "remove_drawing_variable", ["handles"] = new JsonArray("A"), ["variable_id"] = native, ["expected_name"] = "AngleDegrees", ["expected_expression"] = "180-75", ["expected_value"] = 105 }; var input = Input(row);
            var edit = (DrawingProfileEdit)ModifyValidation.Parse(Wire.Element(input)).Operations.Single(); if (edit.ExpectedName != "AngleDegrees" || edit.ExpectedExpression != "180-75" || edit.ExpectedValue != 105) throw new Exception("Variable guard lost");
            row.Remove("expected_name"); Deny(() => ModifyValidation.Parse(Wire.Element(input)));
            var schema = ToolCatalog.Tools.Single(t => t.Name == "nanocad.modify_entities").InputSchema.GetRawText();
            var help = AuditServerContractSupport.Help("modify_entities", "remove_drawing_variable");
            if (!schema.Contains("remove_drawing_constraints") || !schema.Contains("remove_drawing_variable") || !help.Contains("no other consumers")) throw new Exception("Removal preconditions absent from focused guidance"); return Task.CompletedTask;
        });
    }
}
