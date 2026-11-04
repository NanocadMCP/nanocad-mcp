using System.Text.Json;
using NanoCAD.Contracts;
using NanoCAD.Ipc;
using NanoCAD.McpServer;

internal static class VisualCaptureTests
{
    public static async Task Run(Func<string, Func<Task>, Task> test)
    {
        var bridge = Guid.NewGuid().ToString("D"); var document = Guid.NewGuid().ToString("D");
        object Input(int edge = 1600) => new { bridge_instance_id = bridge, document_id = document, expected_revision = "18446744073709551615", max_edge = edge };
        void Deny(Action action, string? code = null) { try { action(); } catch (DomainException e) when (code == null || e.Error.code == code) { return; } throw new Exception("Unsafe visual input accepted"); }
        await test("visual capture input preserves identity precision and rejects unrelated window controls", () =>
        {
            var args = VisualCaptureSchema.Parse(Wire.Element(Input()));
            if (args.BridgeId != bridge || args.DocumentId != document || args.Revision != "18446744073709551615" || args.MaxEdge != 1600 || args.ViewportOnly) throw new Exception("Capture context or default changed");
            foreach (int edge in new[] { 255, 2049 }) Deny(() => VisualCaptureSchema.Parse(Wire.Element(Input(edge))));
            var valid = JsonSerializer.SerializeToNode(Input())!;
            foreach (var key in new[] { "hwnd", "path", "pid", "command", "camera" }) { var bad = valid.DeepClone(); bad[key] = "1"; Deny(() => VisualCaptureSchema.Parse(Wire.Element(bad))); }
            var overflow = valid.DeepClone(); overflow["expected_revision"] = "18446744073709551616"; Deny(() => VisualCaptureSchema.Parse(Wire.Element(overflow)));
            var fraction = valid.DeepClone(); fraction["max_edge"] = 512.5; Deny(() => VisualCaptureSchema.Parse(Wire.Element(fraction))); return Task.CompletedTask;
        });
        await test("visual capture rejects stale document and changes during observation", () =>
        {
            var args = VisualCaptureSchema.Parse(Wire.Element(Input()));
            var before = Wire.Element(new { document_id = document, revision = args.Revision, active_space = "model", path = "own.dwg" });
            VisualCaptureService.RequireContext(args, before); VisualCaptureService.RequireUnchanged(before, before);
            Deny(() => VisualCaptureService.RequireContext(args with { DocumentId = Guid.NewGuid().ToString("D") }, before), "DOCUMENT_NOT_ACTIVE");
            Deny(() => VisualCaptureService.RequireContext(args with { Revision = "1" }, before), "REVISION_MISMATCH");
            foreach (var changed in new[] { Wire.Element(new { document_id = document, revision = "1", active_space = "model", path = "own.dwg" }), Wire.Element(new { document_id = document, revision = args.Revision, active_space = "paper", path = "own.dwg" }) })
                Deny(() => VisualCaptureService.RequireUnchanged(before, changed), "CAPTURE_CONTEXT_CHANGED"); return Task.CompletedTask;
        });
        await test("visual capture image budgets reject oversized or inconsistent PNG", () =>
        {
            var png = Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+a/6sAAAAASUVORK5CYII=");
            VisualCaptureService.RequirePng(png, 1, 1, 256);
            var transferred = VisualCaptureService.DecodeImage(Wire.Element(new { png_base64 = Convert.ToBase64String(png) }), 1, 1, 256);
            if (!transferred.SequenceEqual(png)) throw new Exception("IPC changed image bytes");
            Deny(() => VisualCaptureService.DecodeImage(Wire.Element(new { }), 1, 1, 256), "CAPTURE_UNAVAILABLE");
            Deny(() => VisualCaptureService.DecodeImage(Wire.Element(new { png_base64 = "invalid!" }), 1, 1, 256), "CAPTURE_FAILED");
            Deny(() => VisualCaptureService.DecodeImage(Wire.Element(new { png_base64 = new string('A', 900000) }), 1, 1, 256), "CAPTURE_IMAGE_LIMIT");
            Deny(() => VisualCaptureService.RequirePng(png, 2, 1, 256)); Deny(() => VisualCaptureService.RequirePng(png[..20], 1, 1, 256));
            var over = new byte[VisualCaptureService.MaxPngBytes + 1]; png.CopyTo(over, 0); Deny(() => VisualCaptureService.RequirePng(over, 1, 1, 256)); return Task.CompletedTask;
        });
        await test("desktop capture bridge accepts only opaque bounded internal tokens", () =>
        {
            var token = Guid.NewGuid().ToString("N");
            var request = Wire.Element(new { bridge_instance_id = bridge, capture_id = token, max_edge = 1600 });
            var parsed = VisualCaptureBridgeArguments.Parse(request);
            if (!VisualCaptureBridgeArguments.Parse(Wire.Element(new { bridge_instance_id = bridge, capture_id = token, max_edge = 1600, inline_image = true })).InlineImage) throw new Exception("Inline transport flag lost");
            if (parsed.BridgeId != bridge || parsed.CaptureId != token || parsed.MaxEdge != 1600 || parsed.ViewportOnly ||
               Wire.ValidateBridge("capture_window", request) != bridge) throw new Exception("Internal capture identity changed");
            Deny(() => VisualCaptureBridgeArguments.Parse(Wire.Element(new { bridge_instance_id = bridge, capture_id = token, max_edge = 2049 })));
            Deny(() => VisualCaptureBridgeArguments.Parse(Wire.Element(new { bridge_instance_id = bridge, capture_id = token, max_edge = 1600, hwnd = "1" })));
            Deny(() => VisualCaptureBridgeArguments.Parse(Wire.Element(new { bridge_instance_id = bridge, capture_id = "../frame", max_edge = 1600 })));
            return Task.CompletedTask;
        });
        await test("viewport capture requires the native crop and matching context", () =>
        {
            var input = JsonSerializer.SerializeToNode(Input())!;
            input["viewport_only"] = true;
            var args = VisualCaptureSchema.Parse(Wire.Element(input));
            var relay = Wire.Element(new { bridge_instance_id = bridge, capture_id = Guid.NewGuid().ToString("N"), max_edge = 1600, viewport_only = true });
            if (!args.ViewportOnly || !VisualCaptureBridgeArguments.Parse(relay).ViewportOnly)
                throw new Exception("Viewport choice did not survive parsing");
            input["viewport_only"] = "true";
            Deny(() => VisualCaptureSchema.Parse(Wire.Element(input)));
            Deny(() => VisualCaptureService.RequireCaptureArea(args, Wire.Element(new { })), "CAPTURE_VIEWPORT_UNAVAILABLE");
            Deny(() => VisualCaptureService.RequireCaptureArea(args, Wire.Element(new { viewport_only = true, capture_document_id = document, capture_revision = "1" })), "CAPTURE_CONTEXT_CHANGED");
            VisualCaptureService.RequireCaptureArea(args, Wire.Element(new { viewport_only = true, capture_document_id = document, capture_revision = args.Revision }));
            VisualCaptureService.RequireCaptureArea(args with { ViewportOnly = false }, Wire.Element(new { }));
            return Task.CompletedTask;
        });
        await test("WCS region capture validates bounds and requires bridge camera binding", () =>
        {
            var input = JsonSerializer.SerializeToNode(Input())!;
            input["bounds"] = System.Text.Json.Nodes.JsonNode.Parse("""{"min":[-7,2],"max":[8,3]}""");
            var args = VisualCaptureSchema.Parse(Wire.Element(input));
            var bounds = new CaptureWorldBounds(-7, 2, 8, 3, 0);
            if (!args.RequiresViewport || args.Bounds != bounds) throw new Exception("Bounds or z default lost");
            foreach (var bad in new[] { "null", "{}", """{"min":[1,1],"max":[1,2]}""", """{"min":[0,0,0],"max":[1,1]}""",
                """{"min":[0,0],"max":[1e400,1]}""", """{"min":[0,0],"max":[1,1],"z":"0"}""",
                """{"min":[0,0],"max":[1,1],"z":0,"z":1}""", """{"min":[0,0],"max":[1,1],"pixels":true}""" })
                Deny(() => CaptureWorldBounds.Parse(JsonDocument.Parse(bad).RootElement), "INVALID_ARGUMENT");
            string id = Guid.NewGuid().ToString("N"), camera = new('A', 64);
            var relay = VisualCaptureBridgeArguments.Parse(Wire.Element(new
            {
                bridge_instance_id = bridge,
                capture_id = id,
                max_edge = 1600,
                bounds = bounds.ToWire(),
                expected_camera_token = camera
            }));
            if (relay.Bounds != bounds || relay.CameraToken != camera || !relay.RequiresViewport) throw new Exception("Bounds relay lost camera or region");
            Deny(() => VisualCaptureBridgeArguments.Parse(Wire.Element(new { bridge_instance_id = bridge, capture_id = id, max_edge = 1600, bounds = bounds.ToWire() })), "INVALID_ARGUMENT");
            return Task.CompletedTask;
        });
        await test("WCS region maps logical canvas pixels to physical pixels and excludes layout chrome", () =>
        {
            var native = CaptureViewportPixels.Bind(1136, 479, 1136, 500, 1420, 625);
            if (native != new CaptureViewportPixels(1420, 598, 1.25)) throw new Exception("Incorrect physical pixel scale or drawing extent.");
            var identity = CaptureViewportPixels.Bind(1136, 479, 1136, 500, 1136, 500);
            if (identity != new CaptureViewportPixels(1136, 479, 1)) throw new Exception("100 percent DPI must remain unchanged.");
            Deny(() => CaptureViewportPixels.Bind(750, 479, 1136, 500, 1420, 625), "CAPTURE_BOUNDS_UNAVAILABLE");
            Deny(() => CaptureViewportPixels.Bind(1136, 479, 1136, 500, 1420, 610), "CAPTURE_BOUNDS_UNAVAILABLE");
            return Task.CompletedTask;
        });
        await test("WCS region uses a conservative viewport pixel envelope", () =>
        {
            var crop = CapturePixelRect.Enclose((12.75, 17), (25.5, 5.25), (30, 30.1), (42.75, 18.35), 80, 60);
            if (crop != new CapturePixelRect(12, 5, 43, 31)) throw new Exception("Fractional or rotated bounds were truncated");
            var entire = CapturePixelRect.Enclose((0, 60), (80, 60), (0, 0), (80, 0), 80, 60);
            if (entire != new CapturePixelRect(0, 0, 80, 60)) throw new Exception("Viewport boundary should be allowed");
            Deny(() => CapturePixelRect.Enclose((0, 5), (70, 5), (0, 30), (80.1, 30), 80, 60), "CAPTURE_BOUNDS_OUTSIDE_VIEWPORT");
            Deny(() => CapturePixelRect.Enclose((2, 2), (3, 3), (4, 4), (5, 5), 80, 60), "CAPTURE_BOUNDS_UNAVAILABLE");
            Deny(() => CapturePixelRect.Enclose((double.NaN, 2), (3, 3), (4, 5), (5, 6), 80, 60), "CAPTURE_BOUNDS_UNAVAILABLE");
            return Task.CompletedTask;
        });
        await test("WCS region discards whole viewport, wrong bounds and contradictory worker pixels", () =>
        {
            var args = VisualCaptureSchema.Parse(Wire.Element(Input())) with { Bounds = new(-2, -3, 2, 3, 1) };
            JsonElement Reply(CaptureWorldBounds? bounds, CapturePixelRect crop, int width = 40) => Wire.Element(new
            {
                viewport_only = true,
                capture_document_id = document,
                capture_revision = args.Revision,
                capture_bounds = bounds?.ToWire(),
                crop_pixels = crop,
                viewport_width = 100,
                viewport_height = 60,
                source_width = width,
                source_height = 30
            });
            var region = new CapturePixelRect(10, 15, 50, 45);
            VisualCaptureService.RequireCaptureArea(args, Reply(args.Bounds, region));
            Deny(() => VisualCaptureService.RequireCaptureArea(args, Wire.Element(new { })), "CAPTURE_BOUNDS_UNAVAILABLE");
            Deny(() => VisualCaptureService.RequireCaptureArea(args, Reply(null, region)), "CAPTURE_BOUNDS_UNAVAILABLE");
            Deny(() => VisualCaptureService.RequireCaptureArea(args, Reply(args.Bounds! with { Z = 0 }, region)), "CAPTURE_BOUNDS_UNAVAILABLE");
            Deny(() => VisualCaptureService.RequireCaptureArea(args, Reply(args.Bounds, region, 41)), "CAPTURE_BOUNDS_UNAVAILABLE");
            Deny(() => VisualCaptureService.RequireCaptureArea(args, Reply(args.Bounds, new(80, 15, 120, 45))), "CAPTURE_BOUNDS_UNAVAILABLE");
            return Task.CompletedTask;
        });
        await test("visual capture inline image crosses the real IPC text-page budget", async () =>
        {
            var descriptor = Discovery.Describe(bridge, "capture-transport-test");
            string encoded = Convert.ToBase64String(new byte[600 * 1024]);
            using var listener = new BridgeListener(descriptor, (r, c) => Task.FromResult(Result.Ok(r.requestId, new { png_base64 = encoded })), new JsonLog("capture-test"));
            var request = new Request(Wire.Version, Guid.NewGuid().ToString("D"), "capture_window", 9000,
                Wire.Element(new { bridge_instance_id = bridge, capture_id = Guid.NewGuid().ToString("N"), max_edge = 1600, inline_image = true }));
            var response = await new BridgeClient().CallAsync(descriptor, request, CancellationToken.None);
            if (!response.success || Wire.Element(response.data!).GetProperty("png_base64").GetString() != encoded)
                throw new Exception("Image was rejected or altered by IPC: " + response.error?.code);
        });
        await test("visual capture catalog exposes only the public observation tool", () =>
        {
            var tool = ToolCatalog.Tools.Single(t => t.Name == "nanocad.capture_view");
            if (Wire.Operations.Contains("capture_view") || Wire.Operations.Contains("capture_window") ||
               ToolCatalog.Tools.Any(t => t.Name == "nanocad.capture_window") || tool.Annotations?.ReadOnlyHint != true)
                throw new Exception("Internal capture endpoint became a public tool");
            File.WriteAllText(Path.Combine(Environment.GetEnvironmentVariable("NANOCAD_MCP_HOME")!, "tools-list.json"), JsonSerializer.Serialize(ToolCatalog.Tools, Wire.Json)); return Task.CompletedTask;
        });
    }
}
