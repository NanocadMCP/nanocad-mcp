using System.Text.Json;
using NanoCAD.Contracts;
using Teigha.DatabaseServices;

namespace NanoCADBridge;

internal static class ModelParameterSnapshot
{
    public static JsonElement Capture(ModelParameterArguments args, DocumentState state, Action? cancel = null)
    {
        cancel?.Invoke();
        using var tx = state.Document.Database.TransactionManager.StartTransaction();
        var table = (BlockTable)tx.GetObject(state.Document.Database.BlockTableId, OpenMode.ForRead);
        var modelId = table[BlockTableRecord.ModelSpace];
        var model = (BlockTableRecord)tx.GetObject(modelId, OpenMode.ForRead);
        var handles = DocumentInventory.Fingerprint(model);

        var read = new Arguments(args.BridgeId, args.DocumentId, args.ExpectedRevision, 100, null, null, null, new[] { args.Handle });
        var variables = Wire.Element(ModelVariablesReader.Read(read, state, tx, modelId, args.BridgeId, CancellationToken.None, cancel));
        var history = Wire.Element(ModelHistoryReader.Read(read, state, tx, modelId, args.BridgeId, CancellationToken.None, cancel)).GetProperty("items")[0];
        var id = state.Document.Database.GetObjectId(false, new Handle(unchecked((long)Convert.ToUInt64(args.Handle, 16))), 0);
        var native = Wire.Element(SolidGeometryReader.Read((Solid3d)tx.GetObject(id, OpenMode.ForRead), CancellationToken.None));
        var body = Multicad.McObjectId.FromHandle(id.Handle.Value, Multicad.DatabaseServices.McDocument.ActiveDocument.ID).GetObjectOfType<Multicad.Mc3D.Mc3dSolid>();
        Require(body != null, "Model body identity unavailable."); var contents = ModelInventoryBudget.History(body!, cancel);
        var external = ProjectedHistorySources.Read(body, contents.ToArray(), state, tx, cancel); ProjectedHistorySources.Healthy(contents.ToArray(), external);
        var result = Wire.Element(new
        {
            handles,
            native,
            owner = variables.GetProperty("mc_document_id"),
            external_sources = external,
            native_source_ids = NativePathSources.Read(contents, cancel).OrderBy(id => id, StringComparer.Ordinal).ToArray(),
            variables = variables.GetProperty("variables").EnumerateArray().OrderBy(x => x.GetProperty("mc_id").GetString(), StringComparer.Ordinal).ToArray(),
            body = history.GetProperty("mc_id"),
            last_item = history.GetProperty("last_item_id"),
            nodes = ModelDependencyGraph410.Nodes(Wire.Element(history.GetProperty("nodes").EnumerateArray().OrderBy(x => x.GetProperty("mc_id").GetString(), StringComparer.Ordinal).ToArray()), contents.ToArray(), cancel)
        });
        new ModelInventoryBudget(cancel).Add(result);
        cancel?.Invoke();
        return result;
    }
    public static JsonElement Variable(JsonElement state, string id) => state.GetProperty("variables").EnumerateArray().Single(x => x.GetProperty("mc_id").GetString() == id);
    public static void Healthy(JsonElement state)
    {
        var external = state.TryGetProperty("external_sources", out var sources) ? sources.EnumerateArray().Select(s => s.GetProperty("id").GetString()).ToHashSet(StringComparer.OrdinalIgnoreCase) : new HashSet<string?>(StringComparer.OrdinalIgnoreCase);
        var nativeSources = state.TryGetProperty("native_source_ids", out var paths) ? paths.EnumerateArray().Select(id => id.GetString()).ToHashSet(StringComparer.OrdinalIgnoreCase) : new HashSet<string?>(StringComparer.OrdinalIgnoreCase);
        Require(state.GetProperty("nodes").EnumerateArray().All(n =>
            (n.GetProperty("status").GetString() == "Ok" && n.GetProperty("suppressed").ValueKind == JsonValueKind.False) ||
            (n.GetProperty("suppressed").ValueKind == JsonValueKind.True && n.GetProperty("status").GetString() is "Ok" or "Suppressed") ||
            n.GetProperty("status").ValueKind == JsonValueKind.Null && external.Contains(n.GetProperty("mc_id").GetString()) ||
            n.GetProperty("status").ValueKind == JsonValueKind.Null && nativeSources.Contains(n.GetProperty("mc_id").GetString())),
            "Model history needs update or reports an error; intentional healthy suppression is retained.");
        var volume = state.GetProperty("native").GetProperty("volume");
        Require(volume.ValueKind == JsonValueKind.Number && double.IsFinite(volume.GetDouble()) && volume.GetDouble() > 0, "Solid volume is unavailable or empty.");
    }
    public static bool Equivalent(JsonElement a, JsonElement b)
    {
        if (a.ValueKind != b.ValueKind) return false;
        return a.ValueKind switch
        {
            JsonValueKind.Object => SameObject(a, b),
            JsonValueKind.Array => a.GetArrayLength() == b.GetArrayLength() && a.EnumerateArray().Zip(b.EnumerateArray()).All(p => Equivalent(p.First, p.Second)),
            JsonValueKind.Number => Near(a.GetDouble(), b.GetDouble()),
            _ => a.ToString() == b.ToString()
        };
    }
    private static bool StateField(string name) => name is not ("inspection_duration_ms" or "budget_notice");
    private static bool SameObject(JsonElement a, JsonElement b)
    {
        // Sampling time and the former 500 ms budget notice describe the
        // inspection, not CAD state. Their variation cannot invalidate a
        // completed parameter edit or an unaffected neighboring body.
        var left = a.EnumerateObject().Where(p => StateField(p.Name)).ToArray();
        var right = b.EnumerateObject().Where(p => StateField(p.Name)).ToArray();
        return left.Length == right.Length && left.All(p => b.TryGetProperty(p.Name, out var value) && Equivalent(p.Value, value));
    }
    internal static object ChangedNode(JsonElement previous, JsonElement current, IReadOnlySet<string> affected, Action? cancel)
    {
        if (previous.GetArrayLength() != current.GetArrayLength())
            return new { field = "nodes", previous_count = previous.GetArrayLength(), current_count = current.GetArrayLength() };
        object? Scalar(JsonElement value) => value.ValueKind switch
        {
            JsonValueKind.Number or JsonValueKind.True or JsonValueKind.False => value,
            JsonValueKind.String when value.GetString()!.Length <= 128 => value.GetString(),
            _ => value.ValueKind.ToString()
        };
        object Locate(string id, string path, JsonElement a, JsonElement b)
        {
            cancel?.Invoke();
            if (a.ValueKind == JsonValueKind.Object && b.ValueKind == JsonValueKind.Object)
            {
                foreach (var name in a.EnumerateObject().Concat(b.EnumerateObject()).Select(p => p.Name).Where(StateField).Distinct(StringComparer.Ordinal))
                {
                    bool had = a.TryGetProperty(name, out var left), has = b.TryGetProperty(name, out var right);
                    if (!had || !has || !Equivalent(left, right)) return Locate(id, path + "/" + name, left, right);
                }
            }
            if (a.ValueKind == JsonValueKind.Array && b.ValueKind == JsonValueKind.Array && a.GetArrayLength() == b.GetArrayLength())
                for (int i = 0; i < a.GetArrayLength(); i++)
                    if (!Equivalent(a[i], b[i])) return Locate(id, path + "/" + i, a[i], b[i]);
            return new { feature_id = id, in_dependency_closure = affected.Contains(id), field = path, before = Scalar(a), after = Scalar(b) };
        }
        for (int i = 0; i < previous.GetArrayLength(); i++)
            if (!Equivalent(previous[i], current[i])) return Locate(previous[i].GetProperty("mc_id").GetString()!, "", previous[i], current[i]);
        return new { field = "nodes" };
    }
    public static bool Near(double a, double b) => double.IsFinite(a) && double.IsFinite(b) && Math.Abs(a - b) <= 1e-8 * Math.Max(1, Math.Max(Math.Abs(a), Math.Abs(b)));
    private static void Require([System.Diagnostics.CodeAnalysis.DoesNotReturnIf(false)] bool condition, string message) { if (!condition) throw new DomainException("MODEL_STATE_UNAVAILABLE", message); }
}
