using System.Text.Json;
using NanoCAD.Contracts;
using NanoCAD.McpServer;

internal static class DrawingViewsTests
{
    public static async Task Run(Func<string, Func<Task>, Task> test)
    {
        Dictionary<string, object> Input() => new() { ["bridge_instance_id"] = Guid.NewGuid().ToString("D"), ["document_id"] = Guid.NewGuid().ToString("D"), ["expected_revision"] = "12", ["layout_handle"] = "000aB" };
        await test("group placement compacts scattered views, clears title graphics and accounts for attached dimensions", () =>
        {
            var border = new SheetBox(0, 0, 40, 25);
            List<SheetBox> strokes = [new(0,0,0,25), new(40,0,40,25), new(0,0,40,0), new(0,25,40,25),
                new(22, 15, 22, 25), new(22, 15, 40, 15), new(30, 15, 30, 25)];
            var frameParts = SheetGroupPlacement.FrameObstacles(border, strokes, () => { });
            if (!frameParts.Contains(new(22, 15, 40, 25)) || frameParts.Contains(border)) throw new Exception("Blank ruled cells must be reserved and the blank frame interior released.");
            if (SheetGroupPlacement.FrameObstacles(border, strokes.Skip(1).ToList(), () => { }).Single() != border)
                throw new Exception("An incomplete perimeter cannot prove a frame.");
            var root = new SheetGroupMember("A", -1, "root", new(0, 0), new(-5, -5, 9, 5)); // dimension protrudes right
            var right = new SheetGroupMember("B", 0, "right", new(150, 0), new(147, -3, 153, 3));
            var top = new SheetGroupMember("C", 0, "above", new(0, 1), new(-4, -2, 4, 4)); // overlaps root
            SheetGroupMember[] members = [root, right, top];
            var frame = new SheetBox(0, 0, 40, 25);
            SheetBox[] obstacles = [new(22, 15, 40, 25)];
            var fit = SheetGroupPlacement.Fit(members, obstacles, frame, new(10, 8), 2, () => { });
            if (fit == null || fit[0] != new Point2(10, 8) || fit[1] != new Point2(24, 8) || fit[2] != new Point2(10, 18))
                throw new Exception("A side view can sit beside the title block while the top view sits above the root.");
            if (SheetGroupPlacement.Fit(members, obstacles, new(0, 0, 15, 15), new(10, 8), 2, () => { }) != null)
                throw new Exception("Insufficient space accepted.");
            SheetGroupMember[] spread = [root, right with { Origin = new(30, 0), Envelope = new(27, -3, 33, 3) }];
            var same = SheetGroupPlacement.Fit(spread, [], new(-20, -20, 45, 40), new(0, 0), 2, () => { });
            var compact = SheetGroupPlacement.Fit(spread, [], new(-20, -20, 45, 40), new(0, 0), 2, () => { }, compactSpacing: true);
            if (same?[1].X != 30 || compact?[1].X != 14) throw new Exception("Existing readable spacing requires an explicit compaction request.");
            var input = Input();
            input["sheet_placement"] = new { view_handle = "A", include_aligned_group = true, annotation_margin = 3, reserved_areas = new[] { new[] { 3, 4, 9, 10 } }, member_sides = new { B = "right" } };
            var request = Validation.Parse("get_drawing_views", Wire.Element(input)).SheetPlacement;
            if (request is not { IncludeAlignedGroup: true, AnnotationMargin: 3 } || request.MemberSides["B"] != "right" || request.ReservedAreas.Single() != new SheetBox(3, 4, 9, 10))
                throw new Exception("Group parameters lost in the public parser.");
            return Task.CompletedTask;
        });
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
            foreach (object bad in new object[] { new { view_handle = "3", available_area = new[] { 3, 1, 2, 4 } }, new { view_handle = "3", clearance = -1 }, new { view_handle = "3", apply = true }, new { view_handle = "3", annotation_margin = 2 }, new { view_handle = "3", include_aligned_group = 1 }, new { view_handle = "3", include_aligned_group = true, member_sides = new { A = "front" } } })
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
