using System.Text.Json;
using NanoCAD.Contracts;
using NanoCAD.McpServer;

internal static class DrawingCaptureSourceTests
{
    public static async Task Run(Func<string, Func<Task>, Task> test)
    {
        var bridge = Guid.NewGuid().ToString("D");
        var document = Guid.NewGuid().ToString("D");
        string camera = new('A', 64);
        var context = Wire.Element(new { document_id = document, revision = "23", path = "part.dwg", view = new { camera_token = camera } });
        VisualCaptureArguments Args(string source) => VisualCaptureSchema.Parse(Wire.Element(new
        {
            bridge_instance_id = bridge,
            document_id = document,
            expected_revision = "23",
            source
        }));
        JsonElement Remote(string source, string? hash, long? length, string? token = null) => Wire.Element(new
        {
            offscreen_observation = new
            {
                source_kind = source,
                renderer = "native_layout_bitmap",
                document_id = document,
                revision = "23",
                drawing_path = "part.dwg",
                camera = new { camera_token = token ?? camera },
                rendered_bytes_sha256 = hash,
                rendered_bytes_length = length
            }
        });
        static void Reject(Action check)
        {
            try { check(); }
            catch (DomainException error) when (error.Error.code == "CAPTURE_CONTEXT_CHANGED") { return; }
            throw new Exception("An unverified drawing receipt was accepted.");
        }
        await test("drawing sources imply a viewport and maintain separate saved and current geometry provenance", () =>
        {
            foreach (var source in new[] { "saved_drawing", "current_drawing" })
            {
                var args = Args(source);
                if (!args.RequiresViewport || !args.NativeGraphics || args.ViewportOnly || args.CameraToken != null) throw new Exception();
                var receipt = source == "saved_drawing" ? Remote(source, new('b', 64), 101) : Remote(source, null, null);
                if (VisualCaptureService.ReadDrawingObservation(args, receipt, context)?.GetProperty("source_kind").GetString() != source)
                    throw new Exception("The server did not read the bridge receipt.");
                Reject(() => VisualCaptureService.ReadDrawingObservation(args, Wire.Element(new { width = 800, height = 480 }), context));
            }
            if (VisualCaptureService.ReadDrawingObservation(Args("windows_graphics_capture"), Wire.Element(new { }), context) != null)
                throw new Exception("The default window path acquired drawing provenance.");
            return Task.CompletedTask;
        });
        await test("drawing receipts reject cross-source frames, camera mismatches and undefined saved-file digests", () =>
        {
            var saved = Args("saved_drawing");
            Reject(() => VisualCaptureService.ReadDrawingObservation(saved, Remote("current_drawing", null, null), context));
            Reject(() => VisualCaptureService.ReadDrawingObservation(saved, Remote("saved_drawing", null, 101), context));
            Reject(() => VisualCaptureService.ReadDrawingObservation(saved, Remote("saved_drawing", new('b', 64), 0), context));
            Reject(() => VisualCaptureService.ReadDrawingObservation(saved, Remote("saved_drawing", new('b', 64), 101, new('C', 64)), context));
            Reject(() => VisualCaptureService.ReadDrawingObservation(Args("current_drawing"), Remote("current_drawing", new('b', 64), 101), context));
            return Task.CompletedTask;
        });
        await test("missing external resource pixels carry an explicit frame warning", () =>
        {
            JsonElement Status(string value) => Wire.Element(new { referenced_definitions = new { frame_completeness = value } });
            if (VisualCaptureService.IncompleteReferenceWarning(Status("incomplete_external_references")) is not { } warning ||
                !warning.Contains("CAPTURE_EXTERNAL_REFERENCES_INCOMPLETE", StringComparison.Ordinal) ||
                VisualCaptureService.IncompleteReferenceWarning(Status("placed_external_resources_resolved")) != null ||
                VisualCaptureService.IncompleteReferenceWarning(Wire.Element(new { })) != null) throw new Exception();
            return Task.CompletedTask;
        });
        await test("drawing capture forwards the placed raster orientation warning", () =>
        {
            JsonElement Status(string? warning) => Wire.Element(new { referenced_definitions = new { raster_orientation_warning = warning } });
            const string message = "Check the image against its native pixel frame.";
            if (VisualCaptureService.RasterOrientationWarning(Status(message)) != "CAPTURE_RASTER_ORIENTATION_UNVERIFIED: " + message ||
                VisualCaptureService.RasterOrientationWarning(Status(null)) != null ||
                VisualCaptureService.RasterOrientationWarning(Wire.Element(new { })) != null) throw new Exception();
            return Task.CompletedTask;
        });
    }
}
