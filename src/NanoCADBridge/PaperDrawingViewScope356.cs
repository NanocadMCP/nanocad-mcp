using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using Multicad;
using Multicad.DatabaseServices;
using NanoCAD.Contracts;
using Db = Teigha.DatabaseServices;

namespace NanoCADBridge;

// Fresh objects only. An existing view is never removed, copied, reparented or
// given a fabricated owner/document to simulate a layout transfer.
internal static class PaperDrawingViewScope356
{
    internal sealed record Scope(McDocument Lookup, McDocument Sheet, Db.ObjectId Space, Db.ObjectId Layout,
        McObjectId[] Sources, CadDrawingViewEditState.Snapshot Before, Dictionary<Db.ObjectId, JsonElement> SourceMetadata);
    internal static void Need([DoesNotReturnIf(false)] bool ok, string message, string code = "PAPER_VIEW_SCOPE_MISMATCH")
    { if (!ok) throw new DomainException(code, message); }
    internal static McDocument RequireSheet(McDocument lookup, Db.ObjectId space)
    {
        var sheet = McDocumentsManager.GetActiveSheet();
        var nativeSpace = McObjectId.FromHandle(space.Handle.Value, lookup.ID);
        Need(sheet != null && !sheet.ID.IsNull && sheet.IsLayout && !sheet.IsModel && nativeSpace == sheet.ID &&
            sheet.TopDoc != null && lookup.TopDoc != null && sheet.TopDoc.ID == lookup.TopDoc.ID,
            "The current native sheet must map to this drawing's exact active Paper block record.", "DOCUMENT_CONTEXT_MISMATCH");
        return sheet;
    }
    internal static Scope Prepare(StructureWriteArguments args, DocumentState state, Db.Transaction tx, Action cancel)
    {
        var spec = (CreateDrawingViewsEdit)args.Operations[0];
        Need(spec.LayoutHandle != null && args.EffectiveTargetSpace == "paper", "Supply layout_handle and target_space paper for native Paper view creation.", "INVALID_ARGUMENT");
        var loaded = AppDomain.CurrentDomain.GetAssemblies().Select(a => a.GetName().Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        Need(loaded.Contains("mapimgd") && loaded.Contains("MapiBaseTypes"), "MultiCAD runtime is not already loaded.", "CAPABILITY_UNAVAILABLE");
        var lookup = CadDrawingViewEditState.Context(state); var db = state.Document.Database;
        var space = CadBlockSpace.RequireWriteSpace(db, tx, "paper", cancel); var selected = CadBlockSpace.RequireSpace(db, tx, space);
        Need(CadDrawingViewLifecycleState345.Handle(selected.Layout) == spec.LayoutHandle, "Activate the exact requested ordinary Paper layout outside its model viewport.", "DOCUMENT_CONTEXT_MISMATCH");
        var sheet = RequireSheet(lookup, space);
        var layer = (Db.LayerTableRecord)tx.GetObject(db.Clayer, Db.OpenMode.ForRead);
        Need(layer.Name == "0" && !layer.IsOff && !layer.IsFrozen && !layer.IsLocked && !layer.IsDependent, "Current layer0 must be available.", "LAYER_UNAVAILABLE");
        var layers = (Db.LayerTable)tx.GetObject(db.LayerTableId, Db.OpenMode.ForRead);
        Need(layers.Has("VIEWS"), "Pre-create the local VIEWS layer before this sole-operation request.", "LAYER_UNAVAILABLE");
        var viewsLayer = (Db.LayerTableRecord)tx.GetObject(layers["VIEWS"], Db.OpenMode.ForRead);
        Need(!viewsLayer.IsOff && !viewsLayer.IsFrozen && !viewsLayer.IsLocked && !viewsLayer.IsDependent, "Local VIEWS layer is unavailable.", "LAYER_UNAVAILABLE");
        var sources = DrawingViewWriter.ResolveSources(spec, state, tx, cancel).ToArray();
        Need(sources.Length > 0 && sources.Distinct().Count() == sources.Length, "Selected source identities must be nonempty and distinct.", "SOURCE_SCOPE_MISMATCH");
        var before = CadDrawingViewLifecycleState345.Capture(state, tx, cancel); CadDrawingViewEditState.Tree(before, cancel);
        var names = PaperDrawingViewWriter356.Placements(spec).Select(item => spec.NamePrefix + " " + item.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        Need(!before.Views.Any(view => names.Contains(view.Native.Name)), "A generated view name already exists.", "NAME_COLLISION");
        var metadata = new Dictionary<Db.ObjectId, JsonElement>(); var budget = new ModelInventoryBudget(cancel);
        foreach (var id in sources)
        {
            cancel(); var body = before.Bodies.SingleOrDefault(body => body.Id == id && body.Model);
            Need(body != null, "A selected native ID has no unique top-level Model solid representation.", "SOURCE_SCOPE_MISMATCH");
            var entity = (Db.Entity)tx.GetObject(body!.Entity, Db.OpenMode.ForRead);
            var sourceLayer = (Db.LayerTableRecord)tx.GetObject(entity.LayerId, Db.OpenMode.ForRead);
            Need(!sourceLayer.IsOff && !sourceLayer.IsFrozen && !sourceLayer.IsLocked && !sourceLayer.IsDependent, "A selected source layer is unavailable.", "LAYER_UNAVAILABLE");
            var observed = CadNativeStyleIdentityMetadata352.Metadata(entity, db, tx, cancel); budget.Add(observed); metadata.Add(body.Entity, observed);
        }
        var plan = new Scope(lookup, sheet!, space, selected.Layout, sources, before, metadata);
        var refs = sources.Select(id => before.Bodies.Single(body => body.Id == id).Entity)
            .Select(id => new EntityRef(args.BridgeId, args.DocumentId, CadDrawingViewLifecycleState345.Handle(id))).ToArray();
        var projected = PaperDrawingViewWriter356.Placements(spec).Select(item => new PaperDrawingViewCreation356(
            new(args.BridgeId, args.DocumentId, "FFFFFFFFFFFFFFFF"), new(args.BridgeId, args.DocumentId, spec.LayoutHandle!), sheet!.ID.ToString(),
            new string('F', 64), null, refs, sources.Select(id => id.ToString()).ToArray(), new string('F', 38), new string('F', 38),
            Enumerable.Repeat(new string('F', 38), names.Count).ToArray(), "native_projection_elements_observed_rendered_image_pending")).ToArray();
        // Count physical response bytes before any native registration; reserve
        // envelope, changes and recovery/observation warnings independently.
        Need(JsonSerializer.SerializeToUtf8Bytes(projected, Wire.Json).Length <= Wire.PayloadBudget - 32768,
            "The complete proposed Paper view response exceeds the actual byte budget.", "RESPONSE_LIMIT_EXCEEDED");
        return plan;
    }
    internal static void Current(Scope scope, DocumentState state, Action cancel)
    {
        cancel(); var lookup = CadDrawingViewEditState.Context(state); var sheet = McDocumentsManager.GetActiveSheet();
        using var tx = state.Document.Database.TransactionManager.StartTransaction();
        Need(lookup.ID == scope.Lookup.ID && sheet != null && sheet.ID == scope.Sheet.ID &&
            CadBlockSpace.RequireWriteSpace(state.Document.Database, tx, "paper", cancel) == scope.Space,
            "Active drawing/native Paper sheet changed during creation.", "DOCUMENT_CONTEXT_MISMATCH");
    }
}
