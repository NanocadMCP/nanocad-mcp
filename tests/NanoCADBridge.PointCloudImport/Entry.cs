using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using HostMgd.ApplicationServices;
using HostMgd.PointClouds;
using Teigha.DatabaseServices;
using Teigha.Runtime;
using Application = HostMgd.ApplicationServices.Application;

[assembly: ExtensionApplication(typeof(NanoCADBridge.PointCloudImport.Entry))]
[assembly: CommandClass(typeof(NanoCADBridge.PointCloudImport.Entry))]

namespace NanoCADBridge.PointCloudImport;

// Test-only autoload module, never a shipping bridge or MCP capability. The
// authorized runner supplies one short-lived immutable plan after creating and
// saving a new owned empty Model DWG. No transform, cloud edit, save or retry.
public sealed class Entry : IExtensionApplication
{
    private const string Root = @"C:\NanoCAD MCP\artifacts\point-cloud-readonly-block";
    private const string Scratch = @"C:\NanoCAD MCP\artifacts\phase4-documents-candidate\native-files\";
    private const string Command = "NCMCP_POINTCLOUD_READONLY_IMPORT";
    private static readonly string PlanPath = Path.Combine(Root, "import-plan.json");
    private static readonly string ResultPath = Path.Combine(Root, "import-result.json");
    private static Entry? _current;
    private readonly HostWindowState _window = new(Environment.ProcessId);
    private System.Windows.Forms.Timer? _timer;
    private int _uiThread;
    private bool _consumed, _pending, _started;
    private string? _planText, _consumedPath;
    private Document? _scheduledDocument;

    public void Initialize()
    {
        _current = this;
        _uiThread = Environment.CurrentManagedThreadId;
        Application.Idle += Pump;
        _timer = new System.Windows.Forms.Timer { Interval = 100 };
        _timer.Tick += Pump;
        _timer.Start();
    }

    public void Terminate()
    {
        Application.Idle -= Pump;
        if (_timer != null) { _timer.Stop(); _timer.Tick -= Pump; _timer.Dispose(); _timer = null; }
        if (_current == this) _current = null;
    }

    private void Pump(object? sender, EventArgs e)
    {
        if (_consumed || !File.Exists(PlanPath)) return;
        try
        {
            if (Environment.CurrentManagedThreadId != _uiThread || !_window.Capture().available) return;
            var active = Application.DocumentManager.MdiActiveDocument;
            if (active == null || ActiveCommand() != 0 || !string.IsNullOrEmpty(active.CommandInProgress)) return;
            _consumed = true; // Invalid plans also terminate this module's only attempt.
            Need(!File.Exists(ResultPath), "An immutable import result already exists.");
            NoReparse(PlanPath);
            _planText = File.ReadAllText(PlanPath);
            using var parsed = JsonDocument.Parse(_planText);
            var plan = parsed.RootElement;
            Shape(plan);
            var nonce = Text(plan, "nonce");
            Need(Guid.TryParseExact(nonce, "D", out var id) && id != Guid.Empty, "Invalid nonce.");
            _scheduledDocument = Validate(plan, false);
            _consumedPath = Path.Combine(Root, "import-plan.consumed." + nonce + ".json");
            Need(!File.Exists(_consumedPath), "Nonce already consumed.");
            File.Move(PlanPath, _consumedPath); // Persistent consumption BEFORE native changes.
            _pending = true;
            _scheduledDocument.SendStringToExecute(Command + "\n", false, false, false);
        }
        catch (System.Exception error)
        {
            _pending = false;
            TryResult(new { status = "not_started", native_write_started = false, error = error.ToString(), retry = "not_permitted" });
        }
    }

    [CommandMethod(Command, CommandFlags.Modal | CommandFlags.NoHistory)]
    public void ImportCommand() => _current?.ImportOnce();

    private void ImportOnce()
    {
        if (!_pending) return;
        var handles = new List<string>();
        try
        {
            Need(_planText != null && _consumedPath != null, "Consumed plan missing.");
            NoReparse(_consumedPath!);
            Need(File.ReadAllText(_consumedPath!) == _planText, "Consumed plan changed.");
            using var parsed = JsonDocument.Parse(_planText!);
            var plan = parsed.RootElement;
            var doc = Validate(plan, true);
            var source = Path.GetFullPath(Text(plan, "source_path"));
            using var pinnedSource = File.Open(source, FileMode.Open, FileAccess.Read, FileShare.Read);
            Need(Hash(pinnedSource) == Text(plan, "source_sha256"), "Source changed before native import.");
            using var ids = new ObjectIdCollection();
            _started = true;
            // Exact managed SDK PointCloudMgd sample route; source is an owned
            // byte-identical PLY copy. No generated features or placement edits.
            bool imported = PointCloudBridgeMdg.createPointCloudFromFile(doc.UnmanagedObject, source, ids.UnmanagedObject, false);
            foreach (ObjectId id in ids)
                if (!id.IsNull && id.IsValid && !id.IsErased && id.Database == doc.Database) handles.Add(id.Handle.ToString());
            Need(imported && ids.Count > 0, "Native importer did not report insertion identities.");
            var insertions = new List<object>();
            using (var tx = doc.Database.TransactionManager.StartTransaction())
            {
                var table = (BlockTable)tx.GetObject(doc.Database.BlockTableId, OpenMode.ForRead);
                var modelId = table[BlockTableRecord.ModelSpace];
                foreach (ObjectId id in ids)
                {
                    Need(!id.IsNull && id.IsValid && !id.IsErased && id.Database == doc.Database &&
                        PointCloudBridgeMdg.isPointCloudInsertion(id.OldIdPtr), "Imported ID is not a live cloud insertion in the owned document.");
                    var entity = tx.GetObject(id, OpenMode.ForRead) as Entity;
                    Need(entity != null && entity.OwnerId == modelId, "Imported insertion is not top-level Model geometry.");
                    insertions.Add(new { handle = id.Handle.ToString(), native_type = entity!.GetRXClass().Name });
                }
            }
            using var process = Process.GetCurrentProcess();
            TryResult(new
            {
                status = "import_observed", native_write_started = true, nonce = Text(plan, "nonce"),
                process_id = process.Id, process_start_utc_ticks = process.StartTime.ToUniversalTime().Ticks,
                bridge_instance_id = Text(plan, "bridge_instance_id"), document_id = Text(plan, "document_id"),
                document_path = Path.GetFullPath(doc.Name), source_path = source, source_sha256 = Text(plan, "source_sha256"),
                importer_returned = imported, insertion_count = ids.Count, insertions,
                note = "Actual importer identities; no transform or cloud-count oracle. Product read-only API supplies metadata and points."
            });
        }
        catch (System.Exception error)
        {
            TryResult(new { status = _started ? "write_outcome_unknown" : "not_started", native_write_started = _started,
                observed_handles = handles, error = error.ToString(), retry = "not_permitted" });
        }
        finally { _pending = false; }
    }

    private Document Validate(JsonElement plan, bool insideCommand)
    {
        Need(Environment.CurrentManagedThreadId == _uiThread && _window.Capture().available, "UI context unavailable.");
        using var process = Process.GetCurrentProcess();
        Need(process.ProcessName.Equals("nCad", StringComparison.OrdinalIgnoreCase) &&
            plan.GetProperty("process_id").GetInt32() == process.Id &&
            plan.GetProperty("process_start_utc_ticks").GetInt64() == process.StartTime.ToUniversalTime().Ticks, "Wrong process lifetime.");
        var expiry = plan.GetProperty("expires_at_utc").GetDateTimeOffset();
        Need(expiry > DateTimeOffset.UtcNow && expiry <= DateTimeOffset.UtcNow.AddMinutes(10), "Plan expired or validity window excessive.");
        var target = Path.GetFullPath(Text(plan, "document_path"));
        Need(Inside(target, Scratch) && Path.GetExtension(target).Equals(".dwg", StringComparison.OrdinalIgnoreCase), "Target is outside owned scratch DWGs.");
        NoReparse(target);
        // The named saved DWG can still be held open by CAD with write access;
        // this byte-only read shares that handle, like the accepted fixture.
        Need(Hash(target, FileShare.ReadWrite) == Text(plan, "document_sha256"), "Owned blank DWG bytes changed.");
        var doc = Application.DocumentManager.MdiActiveDocument;
        Need(doc != null && doc.IsNamedDrawing && !doc.IsReadOnly &&
            Path.GetFullPath(doc.Name).Equals(target, StringComparison.OrdinalIgnoreCase), "Wrong active named writable document.");
        Need(_scheduledDocument == null || ReferenceEquals(doc, _scheduledDocument), "Active document changed after scheduling.");
        int command = ActiveCommand();
        Need(insideCommand ? (command & ~1) == 0 : command == 0 && string.IsNullOrEmpty(doc!.CommandInProgress), "Host has conflicting native activity.");
        var proofPath = Path.GetFullPath(Text(plan, "ownership_proof_path"));
        Need(Inside(proofPath, Root), "Ownership proof outside run artifacts.");
        NoReparse(proofPath);
        Need(Hash(proofPath) == Text(plan, "ownership_proof_sha256"), "Ownership proof changed.");
        using (var proof = JsonDocument.Parse(File.ReadAllText(proofPath)))
            Need(Text(proof.RootElement, "bridge_instance_id") == Text(plan, "bridge_instance_id") &&
                Text(proof.RootElement, "document_id") == Text(plan, "document_id"), "Owned-new identity does not match pinned plan.");
        var source = Path.GetFullPath(Text(plan, "source_path"));
        Need(Inside(source, Path.Combine(Root, "source")) && Path.GetFileName(source).Equals("Turbine-ply.ply", StringComparison.OrdinalIgnoreCase), "Only the pinned owned PLY copy is permitted.");
        NoReparse(source);
        Need(Hash(source) == Text(plan, "source_sha256"), "PLY source bytes changed.");
        var db = doc!.Database;
        Need(db.TileMode, "Target must be in Model space.");
        using (var tx = db.TransactionManager.StartTransaction())
        {
            var table = (BlockTable)tx.GetObject(db.BlockTableId, OpenMode.ForRead);
            var modelId = table[BlockTableRecord.ModelSpace];
            Need(db.CurrentSpaceId == modelId, "Current space is not Model.");
            var model = (BlockTableRecord)tx.GetObject(modelId, OpenMode.ForRead);
            foreach (ObjectId id in model) Need(id.IsErased, "Owned document Model must be empty before import.");
        }
        return doc!;
    }

    private static void Shape(JsonElement value)
    {
        string[] names = ["nonce", "process_id", "process_start_utc_ticks", "expires_at_utc", "document_path", "document_sha256",
            "source_path", "source_sha256", "ownership_proof_path", "ownership_proof_sha256", "bridge_instance_id", "document_id"];
        Need(value.ValueKind == JsonValueKind.Object, "Plan must be an object.");
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var property in value.EnumerateObject()) Need(names.Contains(property.Name) && seen.Add(property.Name), "Unexpected or duplicate plan field.");
        Need(names.All(seen.Contains), "Plan field missing.");
        foreach (var key in new[] { "bridge_instance_id", "document_id" }) Need(Guid.TryParseExact(Text(value, key), "D", out var id) && id != Guid.Empty, "Invalid owned identity.");
        foreach (var key in new[] { "document_sha256", "source_sha256", "ownership_proof_sha256" })
            Need(Text(value, key).Length == 64 && Text(value, key).All(c => c is >= '0' and <= '9' or >= 'A' and <= 'F'), "SHA256 must be uppercase canonical hex.");
    }

    private static int ActiveCommand() => Application.GetSystemVariable("CMDACTIVE") switch
    {
        int value => value, short value => value, _ => throw new InvalidOperationException("Unexpected CMDACTIVE type.")
    };
    private static string Text(JsonElement value, string key) => value.GetProperty(key).GetString() is { Length: > 0 } text ? text : throw new InvalidOperationException("Missing text " + key + ".");
    private static bool Inside(string path, string root) => path.StartsWith(Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    private static void Need(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    private static string Hash(string path, FileShare sharing = FileShare.Read) { using var stream = File.Open(path, FileMode.Open, FileAccess.Read, sharing); return Hash(stream); }
    private static string Hash(Stream stream) { using var hash = SHA256.Create(); return Convert.ToHexString(hash.ComputeHash(stream)); }
    private static void NoReparse(string path)
    {
        Need((File.GetAttributes(path) & FileAttributes.ReparsePoint) == 0, "Reparse input is forbidden.");
        for (var parent = new DirectoryInfo(Path.GetDirectoryName(path)!); parent != null; parent = parent.Parent)
            Need((parent.Attributes & FileAttributes.ReparsePoint) == 0, "Reparse ancestor is forbidden.");
    }
    private static void TryResult(object result)
    {
        // Do not replace evidence or throw into CAD if an I/O error prevents a
        // receipt. An import with no terminal receipt remains unresolved.
        try
        {
            string prepared = Path.Combine(Root, "import-result.prepared.json");
            using (var stream = new FileStream(prepared, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, FileOptions.WriteThrough))
            {
                JsonSerializer.Serialize(stream, result, new JsonSerializerOptions { WriteIndented = true });
                stream.Flush(true);
            }
            // Publish only a fully flushed terminal receipt, never a partial JSON.
            File.Move(prepared, ResultPath);
        }
        catch (System.Exception) { }
    }
}
