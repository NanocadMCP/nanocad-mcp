using System.Diagnostics;
using System.Text.Json;
using NanoCAD.Contracts;

namespace NanoCADBridge;

// Private, two-document, one-shot observation. No new product operation or CAD write.
internal static class CameraReadProbe
{
    private const string Run = @"C:\NanoCAD MCP\artifacts\core-camera-native-104";
    private const string Scratch = @"C:\NanoCAD MCP\artifacts\phase4-documents-candidate\native-files\core-camera-native-104";
    private static readonly HashSet<string> Consumed = new(StringComparer.Ordinal);

    internal static void Observe(string bridge, DocumentState state, Request request, Action cancel)
    {
        string plan = Path.Combine(Run, "plan.json");
        if (request.operation != "get_document" || !File.Exists(plan)) return;
        string path = Path.GetFullPath(state.Document.Name);
        string phase = Path.GetFileNameWithoutExtension(path);
        if (phase is not ("camera-2d" or "camera-3d") ||
            !string.Equals(path, Path.Combine(Scratch, phase + ".dxf"), StringComparison.OrdinalIgnoreCase)) return;
        if (Consumed.Contains(phase)) throw new InvalidOperationException("Camera probe already consumed for this fixture.");
        string text = File.ReadAllText(plan);
        if (text.Length > 4096) throw new InvalidOperationException("Excessive camera probe plan.");
        using var parsed = JsonDocument.Parse(text);
        var p = parsed.RootElement;
        using var process = Process.GetCurrentProcess();
        var expiry = p.GetProperty("expires_at_utc").GetDateTimeOffset();
        if (p.GetProperty("bridge_instance_id").GetString() != bridge ||
            p.GetProperty("document_id").GetString() != state.Id ||
            p.GetProperty("phase").GetString() != phase ||
            p.GetProperty("process_id").GetInt32() != process.Id ||
            p.GetProperty("process_start_utc_ticks").GetInt64() != process.StartTime.ToUniversalTime().Ticks ||
            expiry <= DateTimeOffset.UtcNow || expiry > DateTimeOffset.UtcNow.AddMinutes(10))
            throw new InvalidOperationException("Camera probe lifetime mismatch.");
        cancel(); Consumed.Add(phase);
        File.Move(plan, Path.Combine(Run, phase + ".plan.consumed.json"));
        void Save(string step, object value)
        {
            using var f = new FileStream(Path.Combine(Run, phase + "-" + step + ".json"), FileMode.CreateNew, FileAccess.Write, FileShare.Read);
            JsonSerializer.Serialize(f, value, Wire.Json); f.Flush(true);
        }
        var editor = state.Document.Editor;
        var watch = Stopwatch.StartNew();
        Save("before", new { document_id = state.Id, revision = state.Revision, utc = DateTime.UtcNow.ToString("O") });
        // Installed Editor.GetCurrentView IL constructs RXObject.Create(pointer,true):
        // the returned wrapper owns its native reference. Dispose once after reading.
        using (var view = editor.GetCurrentView())
        {
            Save("identity", new { object_id_null = view.ObjectId.IsNull, is_new = view.IsNewObject, write_enabled = view.IsWriteEnabled, auto_delete = view.AutoDelete });
            if (!view.ObjectId.IsNull || !view.IsNewObject || !view.AutoDelete)
                throw new InvalidOperationException("Expected an owned detached view record.");
            cancel();
            var viewport = editor.CurrentViewportObjectId;
            Save("view", new {
                viewport_handle = viewport.IsNull ? null : viewport.Handle.ToString(),
                target_wcs = new[] {view.Target.X, view.Target.Y, view.Target.Z},
                direction_wcs = new[] {view.ViewDirection.X, view.ViewDirection.Y, view.ViewDirection.Z},
                center_view = new[] {view.CenterPoint.X, view.CenterPoint.Y},
                width = view.Width, height = view.Height, twist_radians = view.ViewTwist,
                perspective = view.PerspectiveEnabled,
                front_clip_enabled = view.FrontClipEnabled, back_clip_enabled = view.BackClipEnabled,
                front_clip_distance = view.FrontClipDistance, back_clip_distance = view.BackClipDistance
            });
        }
        cancel();
        Save("complete", new { observed = true, disposed = true, elapsed_ms = watch.ElapsedMilliseconds,
            document_id = state.Id, revision = state.Revision, scope = "View record read and disposed; outer read audit and geometry comparison still required" });
    }
}
