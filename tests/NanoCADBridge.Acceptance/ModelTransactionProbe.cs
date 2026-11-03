using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using HostMgd.ApplicationServices;
using Multicad;
using Multicad.DatabaseServices;
using Multicad.DatabaseServices.Constraints;
using Multicad.Mc3D;
using NanoCAD.Contracts;
using Teigha.DatabaseServices;
using Application = HostMgd.ApplicationServices.Application;

namespace NanoCADBridge;

// Explicit diagnostic build only. No MCP tool, arbitrary command or arbitrary
// file write. One fixed fixture, one consumed plan, one native command per host.
internal static class ModelTransactionProbe
{
    internal const string CommandName = "NCMCP_MODELTRANSACTION_PROBE";
    private const string Run = @"C:\NanoCAD MCP\artifacts\phase8-model-transaction-v3-native";
    private const string Target = @"C:\NanoCAD MCP\artifacts\phase4-documents-candidate\native-files\phase8-model-transaction-v3-native\spider-probe.dwg";
    private const string Source = @"C:\NanoCAD MCP\tests\fixtures\phase7-multicad-spider.dwg";
    private const string Variable = "{B37975CA-E533-41D3-A01C-70EAE2626935}";
    private const string Feature = "{5A48548A-D38F-4661-9A86-0017C4978938}";
    private static bool _consumed;
    private static string? _plan;
    public static bool Pending { get; private set; }

    public static void Pump(Descriptor descriptor)
    {
        if (_consumed || !File.Exists(Path.Combine(Run, "plan.json"))) return;
        _consumed = true; // Failure or timeout never schedules a new attempt.
        try
        {
            _plan = File.ReadAllText(Path.Combine(Run, "plan.json"));
            var document = Validate(descriptor);
            Log("scheduled", new { descriptor.bridge_instance_id, descriptor.process_id, target = Target });
            Pending = true;
            document.SendStringToExecute(CommandName + "\n", false, false, false);
        }
        catch (System.Exception e) { Pending = false; Log("schedule_failed", new { error = e.ToString() }); }
    }

    private static Document Validate(Descriptor? descriptor)
    {
        Need(descriptor != null && _plan != null && _plan.Length < 4096, "No bound plan");
        Need(File.ReadAllText(Path.Combine(Run, "plan.json")) == _plan, "Plan changed");
        using var json = JsonDocument.Parse(_plan!);
        var p = json.RootElement;
        Need(Guid.TryParse(p.GetProperty("nonce").GetString(), out _), "Missing nonce");
        using var process = Process.GetCurrentProcess();
        Need(p.GetProperty("bridge_instance_id").GetString() == descriptor!.bridge_instance_id &&
             p.GetProperty("process_id").GetInt32() == process.Id &&
             p.GetProperty("process_start_utc_ticks").GetInt64() == process.StartTime.ToUniversalTime().Ticks, "Wrong host lifetime");
        var expiry = p.GetProperty("expires_at_utc").GetDateTimeOffset();
        Need(expiry > DateTimeOffset.UtcNow && expiry < DateTimeOffset.UtcNow.AddHours(1), "Expired or excessive plan window");
        var document = Application.DocumentManager.MdiActiveDocument;
        Need(document != null && document.IsNamedDrawing && !document.IsReadOnly &&
             string.Equals(Path.GetFullPath(document.Name), Target, StringComparison.OrdinalIgnoreCase), "Wrong active fixture");
        Need(_insideCommand || !HostReadGate.IsBusy(document!), "Host busy");
        for (var parent = new DirectoryInfo(Path.GetDirectoryName(Target)!); parent != null; parent = parent.Parent)
            Need((parent.Attributes & FileAttributes.ReparsePoint) == 0, "Reparse point in fixture path");
        Need((File.GetAttributes(Target) & FileAttributes.ReparsePoint) == 0, "Reparse fixture");
        // The CAD host has this writable DWG open. Our read handle must allow
        // that existing writer; File.ReadAllBytes uses incompatible FileShare.Read.
        using var stream = File.Open(Target, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        using var sha = SHA256.Create();
        var hash = Convert.ToHexString(sha.ComputeHash(stream));
        Need(hash == p.GetProperty("source_sha256").GetString() && hash == Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(Source))), "Fixture bytes changed");
        return document!;
    }

    public static void Execute(Descriptor? descriptor)
    {
        if (!Pending) return;
        try
        {
            // The native command itself is now active; validate without the
            // command-busy check used by Pump (all identity checks still apply).
            var document = ValidateInCommand(descriptor);
            Log("baseline_before");
            var baseline = Snapshot(document);
            Log("baseline", baseline);
            Need(baseline.GetProperty("variables").GetArrayLength() == 52 &&
                 baseline.GetProperty("nodes").GetArrayLength() == 54 &&
                 baseline.GetProperty("native").GetProperty("handles").GetArrayLength() == 60, "Unexpected fixture inventory");
            Need(CurrentVariable(baseline).GetProperty("expression").GetString() == "150" &&
                 CurrentVariable(baseline).GetProperty("value").GetDouble() == 150, "Unexpected starting parameter");

            JsonElement changed;
            using (var undo = new McUndoPoint(false))
            {
                Log("tracking_start_before"); undo.Start(); Log("tracking_started");
                try
                {
                    ApplyChange(document);
                    changed = Snapshot(document); Log("changed_before_rollback", changed);
                    Need(CurrentVariable(changed).GetProperty("value").GetDouble() == 151, "Variable did not change");
                    Log("controlled_failure", new { reason = "rollback requested after successful mutation and update" });
                }
                finally
                {
                    // No claim of rollback until the post-dispose read agrees.
                    Log("undo_before"); undo.Undo(true); Log("undo_returned");
                    Log("tracking_stop_before"); undo.Stop(); Log("tracking_stopped");
                }
            }
            Log("tracking_disposed");
            // Undo restores the variable network but may leave affected history
            // queued as NeedUpdate. Flush the native rebuild before evaluating
            // the rollback result, rather than relying on later command-end work.
            Log("rollback_update_before"); McObjectManager.UpdateAll(); Log("rollback_update_returned");
            var restored = Snapshot(document); Log("after_rollback", restored);
            bool restoredAll = Equivalent(baseline, restored);
            bool geometryChanged = !Equivalent(baseline.GetProperty("native").GetProperty("geometry"), changed.GetProperty("native").GetProperty("geometry"));
            Log("rollback_comparison", new { restored_all_observed_state = restoredAll, geometry_changed = geometryChanged });
            Need(restoredAll, "ROLLBACK_NOT_VERIFIED: observed state differs; positive phase skipped");
            Need(geometryChanged, "NO_GEOMETRIC_IMPACT: positive phase skipped");

            using (var undo = new McUndoPoint(false))
            {
                Log("positive_start_before"); undo.Start();
                try
                {
                    ApplyChange(document);
                    var positive = Snapshot(document); Log("positive_before_stop", positive);
                    Need(Equivalent(positive, changed), "Positive update differs from first update");
                }
                catch
                {
                    Log("positive_exception_undo_before"); undo.Undo(true); Log("positive_exception_undo_returned");
                    throw;
                }
                finally { undo.Stop(); }
            }
            var retained = Snapshot(document); Log("positive_retained", retained);
            Need(Equivalent(retained, changed), "Positive state changed after tracker disposal");
            Log("completed", new { rollback_observed = true, positive_retained = true,
                scope = "single known variable; no invalid expression, cycle, general solver or user Undo acceptance" });
        }
        catch (System.Exception e) { Log("failed", new { error = e.ToString(), outcome = "consult last completed checkpoints; no inferred rollback" }); }
        finally { Pending = false; }
    }

    private static bool _insideCommand;
    private static Document ValidateInCommand(Descriptor? descriptor)
    {
        _insideCommand = true;
        try { return Validate(descriptor); }
        finally { _insideCommand = false; }
    }

    private static Mc3dSolid Body(Document document)
    {
        var active = McDocument.ActiveDocument;
        Need(active != null && string.Equals(Path.GetFullPath(active.Path), Path.GetFullPath(document.Name), StringComparison.OrdinalIgnoreCase), "MC context mismatch");
        var id = McObjectId.FromHandle(0x150, active!.ID);
        var body = id.GetObjectOfType<Mc3dSolid>();
        Need(body != null && body.ID == id, "Wrong MC body");
        return body!;
    }

    private static void ApplyChange(Document document)
    {
        var body = Body(document);
        using var manager = body.DbEntity.Document.GetDocumentVariablesManager(Constraint2dNetType.MCS);
        Need(manager != null, "MCS manager unavailable");
        var id = manager!.GetAllVariables().Single(x => x.ToString() == Variable);
        Need(manager.GetVariableHostObjectId(id).ToString() == Feature, "Variable host changed");
        Log("set_expression_before");
        Need(manager.SetVariableExpression(id, "151"), "SetVariableExpression returned false");
        Log("set_expression_returned");
        Log("update_all_before"); McObjectManager.UpdateAll(); Log("update_all_returned");
    }

    private static JsonElement Snapshot(Document document)
    {
        // Core ForRead handles never stay open across MultiCAD mutations.
        object native;
        using (var tx = document.Database.TransactionManager.StartTransaction())
        {
            var table = (BlockTable)tx.GetObject(document.Database.BlockTableId, OpenMode.ForRead);
            var model = (BlockTableRecord)tx.GetObject(table[BlockTableRecord.ModelSpace], OpenMode.ForRead);
            var handles = model.Cast<ObjectId>().Where(id => !id.IsErased).Select(id => id.Handle.ToString()).OrderBy(x => x, StringComparer.Ordinal).ToArray();
            Need(document.Database.TryGetObjectId(new Handle(0x150), out var id), "Body not found");
            var solid = (Solid3d)tx.GetObject(id, OpenMode.ForRead);
            var mass = solid.MassProperties; var extents = solid.GeometricExtents;
            native = new { handles, geometry = new { volume = mass.Volume,
                centroid = new[] { mass.Centroid.X, mass.Centroid.Y, mass.Centroid.Z },
                min = new[] { extents.MinPoint.X, extents.MinPoint.Y, extents.MinPoint.Z },
                max = new[] { extents.MaxPoint.X, extents.MaxPoint.Y, extents.MaxPoint.Z } } };
        }
        var body = Body(document);
        using var manager = body.DbEntity.Document.GetDocumentVariablesManager(Constraint2dNetType.MCS);
        Need(manager != null, "MCS manager unavailable");
        var ids = manager!.GetAllVariables(); Need(ids != null && ids.Count <= 256, "Unbounded variables");
        var variables = ids!.OrderBy(x => x.ToString(), StringComparer.Ordinal).Select(id =>
        {
            var info = manager.GetVariableInfo(id); Need(info != null, "Variable info unavailable");
            bool available = manager.GetVariableValue(id, out var value);
            return new { id = id.ToString(), name = info!.Name, type = info.Type.ToString(), expression = info.Expression,
                value = available ? (double?)value : null, angular = manager.IsAngularValue(id), host = manager.GetVariableHostObjectId(id).ToString(),
                downstream_variables = Sorted(manager.GetDependentOnVariables(id)), downstream_objects = Sorted(manager.GetDependentOn3dObjects(id)) };
        }).ToArray();
        var contents = body.GetPartContents(false); Need(contents != null && contents.Count <= 128, "Unbounded history");
        var nodes = contents!.OrderBy(x => x.ToString(), StringComparer.Ordinal).Select(id =>
        {
            var node = id.GetObjectOfType<HistoryItem>(); Need(node != null, "Unknown history item");
            var extrusion = id.GetObjectOfType<ExtrudeFeature>();
            return new { id = id.ToString(), type = id.QueryObjectType()?.FullName, name = node!.Name, status = node.Status.ToString(),
                suppressed = node.IsSuppressed, host = node.HostSolidID.ToString(), dependencies = Sorted(node.GetDependsOn(false, false)),
                extrusion = extrusion == null ? null : new { profile = extrusion.ProfileID.ToString(), direction = extrusion.Direction.ToString(),
                    extent = extrusion.Extent.ToString(), distance = extrusion.Distance, angle = extrusion.Angle,
                    extent2 = extrusion.Extent2.ToString(), distance2 = extrusion.Distance2, angle2 = extrusion.Angle2 } };
        }).ToArray();
        return JsonSerializer.SerializeToElement(new { body = body.ID.ToString(), last_item = body.LastPartItemID.ToString(), native, variables, nodes });
    }

    private static string[]? Sorted(List<McObjectId>? ids) => ids?.Select(x => x.ToString()).OrderBy(x => x, StringComparer.Ordinal).ToArray();
    private static JsonElement CurrentVariable(JsonElement state) => state.GetProperty("variables").EnumerateArray().Single(x => x.GetProperty("id").GetString() == Variable);
    private static bool Equivalent(JsonElement a, JsonElement b)
    {
        if (a.ValueKind != b.ValueKind) return false;
        return a.ValueKind switch
        {
            JsonValueKind.Object => a.EnumerateObject().Count() == b.EnumerateObject().Count() &&
                a.EnumerateObject().All(p => b.TryGetProperty(p.Name, out var v) && Equivalent(p.Value, v)),
            JsonValueKind.Array => a.GetArrayLength() == b.GetArrayLength() && a.EnumerateArray().Zip(b.EnumerateArray()).All(p => Equivalent(p.First, p.Second)),
            JsonValueKind.Number => Math.Abs(a.GetDouble() - b.GetDouble()) <= 1e-8 * Math.Max(1, Math.Max(Math.Abs(a.GetDouble()), Math.Abs(b.GetDouble()))),
            _ => a.ToString() == b.ToString()
        };
    }
    private static void Need(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
    private static void Log(string stage, object? data = null)
    {
        var bytes = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new { utc = DateTime.UtcNow, stage, data }) + "\n");
        using var stream = new FileStream(Path.Combine(Run, "probe.jsonl"), FileMode.Append, FileAccess.Write, FileShare.Read, 4096, FileOptions.WriteThrough);
        stream.Write(bytes); stream.Flush(true);
    }
}
