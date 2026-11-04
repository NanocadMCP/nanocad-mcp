using NanoCAD.Contracts;
using NanoCAD.McpServer;
using System.Text.Json;
using System.Text.Json.Nodes;
internal static class ModelManufacturingTests
{
    public static async Task Run(Func<string, Func<Task>, Task> test)
    {
        string id = Guid.NewGuid().ToString(), a = Guid.NewGuid().ToString("B"), b = Guid.NewGuid().ToString("B"), token = new('A', 64);
        JsonObject Edit(string kind, bool add)
        {
            JsonObject d = kind switch
            {
                "hole" => new() { ["diameter"] = 8, ["through"] = false, ["depth"] = 15, ["seat"] = "counterbore", ["seat_diameter"] = 12, ["seat_depth"] = 3, ["seat_angle_degrees"] = 90, ["flat_bottom"] = false, ["bottom_angle_degrees"] = 118, ["offset1"] = 10, ["offset2"] = 20, ["sector"] = 0 },
                "thread" => new() { ["pitch"] = 1.25, ["right_handed"] = true, ["internal"] = true, ["full_length"] = false, ["length"] = 10 },
                "circular_pattern" => new() { ["count"] = 100, ["angle_degrees"] = 360 },
                _ => new() { ["plane_id"] = a }
            };
            if (add && kind is "hole" or "thread") { d["face_token"] = token; d["face_id"] = a; if (kind == "hole") { d.Remove("offset1"); d.Remove("offset2"); d.Remove("sector"); d["center"] = new JsonArray(10, 20, 0); } else { d["edge_token"] = token; d["front_edge_id"] = b; } }
            if (add && kind is "mirror" or "circular_pattern") d["source_feature_ids"] = new JsonArray(a);
            if (add && kind == "circular_pattern") { d["edge_token"] = token; d["axis_edge_id"] = b; }
            var e = new JsonObject { ["operation"] = (add ? "add_model_" : "set_model_") + kind, ["handles"] = new JsonArray("AB"), ["expected_last_item_id"] = a, ["definition"] = d }; if (!add) e["feature_id"] = b; return e;
        }
        ModifyArguments Parse(JsonObject e) => ModifyValidation.Parse(Wire.Element(new { bridge_instance_id = id, document_id = id, expected_revision = "1", operation_id = id, units = "Millimeters", operations = new[] { e } }));
        void Reject(JsonObject e) { try { Parse(e); } catch (DomainException) { return; } throw new Exception("Invalid manufacturing input accepted."); }
        await test("manufacturing block publishes and parses all eight feature branches", () =>
        {
            var topo = SolidTopologyValidation.Parse(Wire.Element(new { bridge_instance_id = id, document_id = id, expected_revision = "1", handle = "AB", include_native_model_faces = true, include_native_model_edges = true }));
            if (!topo.IncludeNativeModelFaces || !topo.IncludeNativeModelEdges) throw new Exception("Native topology flags missing");
            var tool = ToolCatalog.Tools.Single(t => t.Name == "nanocad.modify_entities");
            var branches = tool.InputSchema.GetProperty("properties").GetProperty("operations").GetProperty("items").GetProperty("oneOf").EnumerateArray().ToArray();
            foreach (var kind in new[] { "hole", "thread", "circular_pattern", "mirror" }) foreach (bool add in new[] { true, false })
            {
                var e = Edit(kind, add); var args = Parse(e); if (args.Operations[0] is not ModelManufacturingEdit || !WriteRecovery.NotGuaranteed(args)) throw new Exception("Wrong route/recovery policy");
                var schema = branches.Single(x => x.GetProperty("properties").GetProperty("operation").GetProperty("const").GetString() == e["operation"]!.GetValue<string>() && e["definition"]!.AsObject().All(field => x.GetProperty("properties").GetProperty("definition").GetProperty("properties").TryGetProperty(field.Key, out _)));
                var fields = schema.GetProperty("properties").GetProperty("definition").GetProperty("properties");
                if (e["definition"]!.AsObject().Any(x => !fields.TryGetProperty(x.Key, out _))) throw new Exception("Definition schema differs from request.");
            }
            if (!tool.OutputSchema!.Value.GetRawText().Contains("manufacturing_changes")) throw new Exception("Output missing"); return Task.CompletedTask;
        });
        await test("manufacturing block operating instructions are published without helix overclaim", () =>
        {
            if (!ServerGuidance.Instructions.Contains("native hole, thread, circular-pattern and mirror") || !ServerGuidance.Instructions.Contains("not proof of a physical helix")) throw new Exception("Embedded guidance missing");
            return Task.CompletedTask;
        });
        await test("manufacturing hole creation preserves the unavailable TwoEdges boundary", () =>
        {
            var e = Edit("hole", true); var definition = e["definition"]!.AsObject(); definition.Remove("center");
            definition["offset1"] = 10; definition["offset2"] = 20; definition["sector"] = 0;
            definition["edge_token"] = token; definition["first_edge_id"] = a; definition["second_edge_id"] = b;
            try { Parse(e); throw new Exception("Unavailable TwoEdges creation was accepted."); }
            catch (DomainException error) when (error.Error.code == "CAPABILITY_UNAVAILABLE") { }
            return Task.CompletedTask;
        });
        await test("manufacturing hole requires only the selected seat fields", () =>
        {
            foreach (bool add in new[] { true, false })
            {
                var edit = Edit("hole", add);
                var definition = edit["definition"]!.AsObject();
                definition["seat"] = "none";
                definition.Remove("seat_diameter");
                definition.Remove("seat_depth");
                definition.Remove("seat_angle_degrees");
                Parse(edit);

                definition["seat"] = "counterbore";
                definition["seat_diameter"] = 12;
                Reject(edit);
                definition["seat_depth"] = 3;
                Parse(edit);

                definition["seat"] = "countersink";
                definition.Remove("seat_depth");
                Reject(edit);
                definition["seat_angle_degrees"] = 90;
                Parse(edit);
            }

            var branches = ToolCatalog.Tools.Single(t => t.Name == "nanocad.modify_entities").InputSchema
                .GetProperty("properties").GetProperty("operations").GetProperty("items").GetProperty("oneOf").EnumerateArray();
            foreach (var branch in branches.Where(branch => branch.GetProperty("properties").GetProperty("operation").GetProperty("const").GetString() is "add_model_hole" or "set_model_hole"))
            {
                var definition = branch.GetProperty("properties").GetProperty("definition");
                if (definition.GetProperty("required").EnumerateArray().Any(field => field.GetString()!.StartsWith("seat_", StringComparison.Ordinal)) ||
                    definition.GetProperty("allOf").GetArrayLength() != 2)
                    throw new Exception("Published hole branch requires unused seat fields or lacks seat conditions.");
            }
            return Task.CompletedTask;
        });
        await test("manufacturing block rejects malformed identities and impossible definitions", () =>
        {
            var e = Edit("hole", true); e["definition"]!["center"] = new JsonArray("10", 20, 0); Reject(e);
            e = Edit("hole", true); e["definition"]!["face_id"] = "invented"; Reject(e);
            e = Edit("hole", true); e["definition"]!["seat_diameter"] = 7; Reject(e);
            e = Edit("thread", false); e["definition"]!["pitch"] = 0; Reject(e);
            e = Edit("circular_pattern", true); e["definition"]!["count"] = 0; Reject(e);
            e = Edit("mirror", true); e["definition"]!["plane_id"] = "invented"; Reject(e);
            return Task.CompletedTask;
        });
    }
}
