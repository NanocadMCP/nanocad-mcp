using System.Text.Json;
using NanoCAD.Contracts;
using NanoCAD.McpServer;

internal static class DrawingViewsTests
{
    public static async Task Run(Func<string, Func<Task>, Task> test)
    {
        Dictionary<string, object> Input() => new() { ["bridge_instance_id"] = Guid.NewGuid().ToString("D"), ["document_id"] = Guid.NewGuid().ToString("D"), ["expected_revision"] = "12", ["layout_handle"] = "000aB" };
        await test("drawing views require explicit current layout context and reject write controls", () =>
        {
            var args = Validation.Parse("get_drawing_views", Wire.Element(Input()));
            if (args.Handles![0] != "AB" || args.IncludeGeometrySources) throw new Exception("Layout context or geometry-source default changed.");
            var selected = Input(); selected["geometry_view_handle"] = "000cd";
            if (Validation.Parse("get_drawing_views", Wire.Element(selected)).GeometryViewHandle != "CD")
                throw new Exception("Selected projection view handle not normalized.");
            selected["geometry_view_handle"] = "invalid";
            try { Validation.Parse("get_drawing_views", Wire.Element(selected)); throw new Exception("Invalid projection handle accepted."); }
            catch (DomainException) { }
            foreach (string name in new[] { "bridge_instance_id", "document_id", "expected_revision", "layout_handle" })
            {
                var value = Input(); value.Remove(name);
                try { Validation.Parse("get_drawing_views", Wire.Element(value)); } catch (DomainException) { continue; }
                throw new Exception("Missing context accepted: " + name);
            }
            foreach (string name in new[] { "handle", "regenerate", "save", "apply" })
            {
                var value = Input(); value[name] = true;
                try { Validation.Parse("get_drawing_views", Wire.Element(value)); } catch (DomainException) { continue; }
                throw new Exception("Unsupported control accepted: " + name);
            }
            return Task.CompletedTask;
        });
        await test("drawing view source observations require an explicit selected geometry page", () =>
        {
            var input = Input();
            input["include_geometry_sources"] = true;
            try { Validation.Parse("get_drawing_views", Wire.Element(input)); throw new Exception("Geometry source observation was accepted without a selected view."); }
            catch (DomainException) { }
            input["geometry_view_handle"] = "CD";
            if (!Validation.Parse("get_drawing_views", Wire.Element(input)).IncludeGeometrySources)
                throw new Exception("Explicit geometry source observation was dropped.");
            input["include_geometry_sources"] = "true";
            try { Validation.Parse("get_drawing_views", Wire.Element(input)); throw new Exception("A non-boolean geometry source option was accepted."); }
            catch (DomainException) { }
            var output = AuditServerContractSupport.Output("get_drawing_views").GetRawText();
            if (!output.Contains("projection_source") || !output.Contains("native_ref_key"))
                throw new Exception("The optional native source observation schema is missing.");
            return Task.CompletedTask;
        });
        await test("drawing views catalog exposes stored read-only metadata and excludes quarantined capabilities", () =>
        {
            var tool = ToolCatalog.Tools.Single(t => t.Name == "nanocad.get_drawing_views");
            if (tool.Annotations is not { ReadOnlyHint: true, DestructiveHint: false }) throw new Exception("Unsafe annotations.");
            var help = AuditServerContractSupport.Help("get_drawing_views");
            if (!help.Contains("geometry_view_handle") || !help.Contains("No regeneration") ||
               !help.Contains("dimensions do not automatically follow")) throw new Exception("Selected projection scope omitted.");
            if (!ToolCatalog.Tools.Any(t => t.Name is "nanocad.inspect_sketch_constraints")) throw new Exception("Wrong capability catalog.");
            File.WriteAllText(Path.Combine(Environment.GetEnvironmentVariable("NANOCAD_MCP_HOME")!, "tools-list.json"), JsonSerializer.Serialize(ToolCatalog.Tools, Wire.Json));
            return Task.CompletedTask;
        });
    }
}
