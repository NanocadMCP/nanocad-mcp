using System.Text.Json;
using NanoCAD.Contracts;
using NanoCAD.McpServer;

internal static class DrawingViewsTests
{
    public static async Task Run(Func<string, Func<Task>, Task> test)
    {
        Dictionary<string, object> Input() => new() { ["bridge_instance_id"] = Guid.NewGuid().ToString("D"), ["document_id"] = Guid.NewGuid().ToString("D"), ["expected_revision"] = "12", ["layout_handle"] = "000aB" };
        await test("dimension lookup requires both a native view and its current marker", () =>
        {
            var input = Input();
            input["geometry_view_handle"] = "F1";
            input["marker_dimensions"] = new { start_index = 4, page_size = 2 };
            try { Validation.Parse("get_drawing_views", Wire.Element(input)); throw new Exception("Missing marker was accepted."); }
            catch (DomainException error) when (error.Error.code == "INVALID_ARGUMENT") { }
            input["geometry_source_marker"] = 0;
            if (Validation.Parse("get_drawing_views", Wire.Element(input)).MarkerDimensions is not { StartIndex: 4, PageSize: 2 })
                throw new Exception("Dimension scan page was lost.");
            return Task.CompletedTask;
        });
        await test("sheet footprint placement uses extents, bounds and the allowed movement axis", () =>
        {
            var input = Input();
            input["sheet_placement"] = new { view_handle = "00cc", proposed_origin = new[] { 1.0, 2.0 }, available_area = new[] { 0, 0, 100, 100 }, clearance = 2.0 };
            if (Validation.Parse("get_drawing_views", Wire.Element(input)).SheetPlacement is not { View: "CC", Clearance: 2 }) throw new Exception("Invalid placement contract.");
            var view = new SheetBox(10, 10, 20, 20);
            SheetBox[] fixedBoxes = [view.Shift(.001, 0), new(10, 22, 20, 31)];
            if (!view.Conflicts(fixedBoxes[0], 0)) throw new Exception("Nearly coincident origins must overlap.");
            var delta = SheetPlacementSolver.Nearest(view, fixedBoxes, new(0, -10, 0, 40), new(0, 0), 1, () => { });
            if (delta is not { X: 0, Y: 22 }) throw new Exception("Retained-axis solution did not clear both obstacles.");
            if (SheetPlacementSolver.Nearest(view, fixedBoxes, new(0, -10, 0, 10), new(0, 0), 1, () => { }) != null) throw new Exception("No opening fits in this area.");
            if (!view.Conflicts(new(15, 15, 15, 17), 0) || view.Conflicts(new(20, 15, 20, 17), 0)) throw new Exception("Line inside and line at allowed contact differ.");
            var target = new SheetBox(103.599, 1, 190, 5);
            var obstacle = new SheetBox(180, 1, 250.105, 5);
            double x = 145.81 + (obstacle.Right + .5 - target.Left);
            var actual = target.Shift(x - 145.81, 0);
            if (actual.Conflicts(obstacle, .5) || !actual.Shift(-.000001, 0).Conflicts(obstacle, .5))
                throw new Exception("An absolute suggested origin must still clear the requested gap after coordinate roundoff.");
            foreach (object bad in new object[] { new { view_handle = "3", available_area = new[] { 3, 1, 2, 4 } }, new { view_handle = "3", clearance = -1 }, new { view_handle = "3", apply = true } })
            {
                input["sheet_placement"] = bad;
                try { Validation.Parse("get_drawing_views", Wire.Element(input)); } catch (DomainException) { continue; }
                throw new Exception("Invalid placement request accepted.");
            }
            return Task.CompletedTask;
        });
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
