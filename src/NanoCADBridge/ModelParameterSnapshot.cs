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
        var body=Multicad.McObjectId.FromHandle(id.Handle.Value,Multicad.DatabaseServices.McDocument.ActiveDocument.ID).GetObjectOfType<Multicad.Mc3D.Mc3dSolid>();
        Require(body!=null,"Model body identity unavailable.");var contents=ModelInventoryBudget.History(body!,cancel);
        var external=ProjectedHistorySources.Read(body,contents.ToArray(),state,tx,cancel);ProjectedHistorySources.Healthy(contents.ToArray(),external);
        var result = Wire.Element(new { handles, native, owner = variables.GetProperty("mc_document_id"),
            external_sources=external,native_source_ids=NativePathSources.Read(contents,cancel).OrderBy(id=>id,StringComparer.Ordinal).ToArray(),
            variables = variables.GetProperty("variables").EnumerateArray().OrderBy(x => x.GetProperty("mc_id").GetString(), StringComparer.Ordinal).ToArray(),
            body = history.GetProperty("mc_id"), last_item = history.GetProperty("last_item_id"),
            nodes = ModelDependencyGraph410.Nodes(Wire.Element(history.GetProperty("nodes").EnumerateArray().OrderBy(x => x.GetProperty("mc_id").GetString(), StringComparer.Ordinal).ToArray()),contents.ToArray(),cancel) });
        new ModelInventoryBudget(cancel).Add(result);
        cancel?.Invoke();
        return result;
    }
    public static JsonElement Variable(JsonElement state, string id) => state.GetProperty("variables").EnumerateArray().Single(x => x.GetProperty("mc_id").GetString() == id);
    public static void Healthy(JsonElement state)
    {
        var external=state.TryGetProperty("external_sources",out var sources)?sources.EnumerateArray().Select(s=>s.GetProperty("id").GetString()).ToHashSet(StringComparer.OrdinalIgnoreCase):new HashSet<string?>(StringComparer.OrdinalIgnoreCase);
        var nativeSources=state.TryGetProperty("native_source_ids",out var paths)?paths.EnumerateArray().Select(id=>id.GetString()).ToHashSet(StringComparer.OrdinalIgnoreCase):new HashSet<string?>(StringComparer.OrdinalIgnoreCase);
        Require(state.GetProperty("nodes").EnumerateArray().All(n =>
            (n.GetProperty("status").GetString()=="Ok"&&n.GetProperty("suppressed").ValueKind==JsonValueKind.False)||
            (n.GetProperty("suppressed").ValueKind==JsonValueKind.True&&n.GetProperty("status").GetString() is "Ok" or "Suppressed")||
            n.GetProperty("status").ValueKind==JsonValueKind.Null&&external.Contains(n.GetProperty("mc_id").GetString())||
            n.GetProperty("status").ValueKind==JsonValueKind.Null&&nativeSources.Contains(n.GetProperty("mc_id").GetString())),
            "Model history needs update or reports an error; intentional healthy suppression is retained.");
        var volume = state.GetProperty("native").GetProperty("volume");
        Require(volume.ValueKind == JsonValueKind.Number && double.IsFinite(volume.GetDouble()) && volume.GetDouble() > 0, "Solid volume is unavailable or empty.");
    }
    public static bool Equivalent(JsonElement a, JsonElement b)
    {
        if (a.ValueKind != b.ValueKind) return false;
        return a.ValueKind switch
        {
            JsonValueKind.Object => SameObject(a,b),
            JsonValueKind.Array => a.GetArrayLength() == b.GetArrayLength() && a.EnumerateArray().Zip(b.EnumerateArray()).All(p => Equivalent(p.First, p.Second)),
            JsonValueKind.Number => Near(a.GetDouble(), b.GetDouble()),
            _ => a.ToString() == b.ToString()
        };
    }
    private static bool SameObject(JsonElement a,JsonElement b)
    {
        // Sampling time and the former 500 ms budget notice describe the
        // inspection, not CAD state. Their variation cannot invalidate a
        // completed parameter edit or an unaffected neighboring body.
        static bool StateField(string name) => name is not ("inspection_duration_ms" or "budget_notice");
        var left=a.EnumerateObject().Where(p=>StateField(p.Name)).ToArray();
        var right=b.EnumerateObject().Where(p=>StateField(p.Name)).ToArray();
        return left.Length==right.Length&&left.All(p=>b.TryGetProperty(p.Name,out var value)&&Equivalent(p.Value,value));
    }
    public static bool Near(double a, double b) => double.IsFinite(a) && double.IsFinite(b) && Math.Abs(a-b) <= 1e-8 * Math.Max(1, Math.Max(Math.Abs(a), Math.Abs(b)));
    private static void Require([System.Diagnostics.CodeAnalysis.DoesNotReturnIf(false)] bool condition, string message) { if (!condition) throw new DomainException("MODEL_STATE_UNAVAILABLE", message); }
}
