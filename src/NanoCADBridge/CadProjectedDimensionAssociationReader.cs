using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using System.Globalization;
using System.Text.Json;
using Multicad;
using Multicad.DatabaseServices;
using Multicad.Dimensions;
using Multicad.Mc3D;
using NanoCAD.Contracts;
using Db = Teigha.DatabaseServices;

namespace NanoCADBridge;

// Read public native McDimensionData separately from ordinary DWG DimAssoc.
// Managed ReferenceItems are not the dimension's stored geometry parameters.
internal static class CadProjectedDimensionAssociationReader
{
    internal static object? TryRead(Db.Dimension source, EntitySummary summary, object ordinaryAssociation, Action cancel)
    {
        cancel();
        var loaded = AppDomain.CurrentDomain.GetAssemblies().Select(assembly => assembly.GetName().Name)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (!loaded.Contains("mapimgd") || !loaded.Contains("MapiBaseTypes")) return null;
        // A proved ordinary association continues to use its existing reader.
        var ordinary = Wire.Element(ordinaryAssociation);
        if (ordinary.TryGetProperty("active", out var active) && active.ValueKind == JsonValueKind.True &&
            ordinary.TryGetProperty("association_handle", out var handle) && handle.ValueKind == JsonValueKind.String) return null;
        return ReadLoaded(source, summary, cancel);
    }

    private static void Need([DoesNotReturnIf(false)] bool condition, string message)
    { if (!condition) throw new DomainException("PROJECTED_DIMENSION_ASSOCIATION_UNAVAILABLE", message); }

    private static string? Reason(Exception error)
    {
        string value = error is DomainException domain ? domain.Error.code + ": " + domain.Error.message : error.GetType().Name + ": " + error.Message;
        return value.Length <= 1024 ? value : value[..1024];
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static object? ReadLoaded(Db.Dimension source, EntitySummary summary, Action cancel)
    {
        bool recognized = false;
        bool storedAvailable = false;
        bool? nativeAssociative = null, nativeViewClassifier = null;
        string? classification = null;
        string? dimensionId = null, ownerId = null, drawingViewId = null;
        int? dimensionType = null, nativeViewSnapIndicator = null;
        var anchors = new List<object>();
        var links = new List<object>();
        object Observation(bool complete, string? reason) => new
        {
            kind = "mcs_dimension_view",
            active = nativeAssociative == true,
            status = complete ? "observed" : "unavailable",
            native_associative = nativeAssociative,
            native_view_classifier = nativeViewClassifier,
            classification,
            dimension_mc_id = dimensionId,
            owner_document_id = ownerId,
            links_complete = complete,
            drawing_view_mc_id = drawingViewId,
            native_view_snap_indicator = nativeViewSnapIndicator,
            dimension_type = dimensionType,
            anchors = anchors.ToArray(),
            links = links.ToArray(),
            unavailable_reason = reason,
            stored_anchor_parameters_available = storedAvailable,
            stored_anchor_parameters_unavailable_reason = storedAvailable ? null : reason,
            scope = "Actual public McDimensionData stored anchors and local view/source ownership. Active reports native getAssociative independently of stored-anchor classification; it does not establish regeneration. No inferred 3D source-edge identity."
        };
        try
        {
            cancel();
            var database = source.Database;
            var host = HostMgd.ApplicationServices.Application.DocumentManager.MdiActiveDocument;
            var lookup = McDocument.ActiveDocument;
            if (database == null || host == null || host.Database.UnmanagedObject != database.UnmanagedObject ||
                !host.IsNamedDrawing || lookup == null || lookup.ID.IsNull ||
                !string.Equals(Path.GetFullPath(lookup.Path), Path.GetFullPath(host.Name), StringComparison.OrdinalIgnoreCase)) return null;
            var nativeId = McObjectId.FromHandle(source.Handle.Value, lookup.ID);
            if (nativeId.IsNull) return null;
            // The actual managed Arc/Angular families inherit McDimension.
            // Generic IdentifiesObjectOfType and Associative are not a public
            // guarantee that every subtype's stored view anchors are absent.
            var dimension = nativeId.GetObject() as McDimension;
            if (dimension == null) return null;
            dimensionId = nativeId.ToString();
            var budget = new ModelInventoryBudget(cancel);
            var transaction = database.TransactionManager.TopTransaction;
            Need(transaction != null && !source.ObjectId.IsNull && source.ObjectId.IsValid && !source.IsErased,
                "The dimension has no current resident read context.");
            Need(dimension.ID == nativeId && dimension.ID.Handle == source.Handle.Value &&
                Validation.NormalizeHandle(source.Handle.ToString()) == summary.@ref.handle,
                "The native dimension differs from the requested physical object.");
            var owner = dimension.DbEntity.Document;
            ownerId = owner?.ID.ToString();
            Need(owner != null && !owner.ID.IsNull && dimension.DbEntity.DocumentID == owner.ID &&
                owner.TopDoc != null && lookup.TopDoc != null && owner.TopDoc.ID == lookup.TopDoc.ID,
                "The native dimension has a foreign or unavailable owning document.");
            Need(transaction!.GetObject(source.OwnerId, Db.OpenMode.ForRead) is Db.BlockTableRecord { IsLayout: true } rawOwner &&
                rawOwner.OwnerId == database.BlockTableId && McObjectId.FromHandle(source.OwnerId.Handle.Value, lookup.ID) == owner.ID,
                "The native dimension owner does not match its physical Model/Paper block record.");
            var read = NativeModelHistory421.LoadProjectedDimensionAssociation();
            var state = new int[5]; var flags = new int[12]; var markers = new int[3];
            var parameters = new double[3]; var data = new long[3]; var points = new double[18];
            var lengths = new int[10]; var text = Array.Empty<char>();
            int status = read(owner.ID.ToString(), dimensionId, state, flags, markers, parameters, data,
                points, lengths, text, 0, out int requiredText);
            recognized = state[0] == 1;
            if (!recognized) return null;
            Need(state[3] is 0 or 1 && state[4] is 0 or 1, "The native association classifiers are malformed.");
            nativeAssociative = state[3] == 1; nativeViewClassifier = state[4] == 1;
            classification = nativeViewClassifier == true ? "native_drawing_view_classifier" : "stored_native_view_anchors";
            dimensionType = state[1]; nativeViewSnapIndicator = state[2];
            Need(status == unchecked((int)0x8007007A) || status == 0,
                "The public native dimension-data read failed (HRESULT 0x" + unchecked((uint)status).ToString("X8") + ").");
            if (requiredText < 0 || requiredText > (Wire.PayloadBudget - 8192) / sizeof(char))
                throw new DomainException("RESPONSE_LIMIT_EXCEEDED", "Stored native dimension references exceed the response byte budget.");
            text = new char[requiredText];
            status = read(owner.ID.ToString(), dimensionId, state, flags, markers, parameters, data,
                points, lengths, text, text.Length, out int actualRequired);
            Need(status == 0 && state[0] == 1 && actualRequired == text.Length,
                "The public native dimension-data snapshot could not be read completely (HRESULT 0x" + unchecked((uint)status).ToString("X8") + ").");
            Need(state[3] is 0 or 1 && state[4] is 0 or 1 && nativeAssociative == (state[3] == 1) && nativeViewClassifier == (state[4] == 1),
                "The exact native association classification changed during readback.");
            dimensionType = state[1]; nativeViewSnapIndicator = state[2];
            var strings = new string?[10]; int offset = 0;
            for (int index = 0; index < strings.Length; index++)
            {
                Need(lengths[index] >= 0 && lengths[index] <= text.Length - offset, "The native reference-text snapshot is malformed.");
                strings[index] = lengths[index] == 0 ? null : new string(text, offset, lengths[index]);
                offset += lengths[index];
            }
            Need(offset == text.Length, "The native reference-text snapshot has trailing data.");
            storedAvailable = true;
            McObjectId Identity(string? value)
            {
                Need(value != null && Guid.TryParseExact(value, "B", out var parsed) && parsed != Guid.Empty,
                    "A stored native reference has no exact nonempty work ID.");
                var result = new McObjectId(Guid.ParseExact(value!, "B"));
                Need(!result.IsNull && string.Equals(result.ToString(), value, StringComparison.OrdinalIgnoreCase),
                    "A stored native work ID could not be represented exactly.");
                return result;
            }
            var viewIdentity = Identity(strings[0]); drawingViewId = viewIdentity.ToString();
            var sourceCounts = new Dictionary<McObjectId, int>();
            var sourceMarkers = new Dictionary<McObjectId, HashSet<int>>();
            bool nonlocalAnchor = false;
            for (int slot = 0; slot < 3; slot++)
            {
                cancel(); Need(flags[slot * 4] is 0 or 1 && flags[slot * 4 + 1] is 0 or 1, "A native anchor state is malformed.");
                bool isNull = flags[slot * 4] == 1;
                double[]? Point(int kind) => flags[slot * 4 + 2 + kind] == 0 ? points.Skip(slot * 6 + kind * 3).Take(3).ToArray() : null;
                var evaluated = isNull ? null : Point(0); var dimensionSnap = isNull ? null : Point(1);
                Need((evaluated == null || evaluated.All(double.IsFinite)) && (dimensionSnap == null || dimensionSnap.All(double.IsFinite)),
                    "A native anchor point contains nonfinite coordinates.");
                string? evaluationReason = isNull ? null : !double.IsFinite(parameters[slot]) ? "The stored native parameter is nonfinite." :
                    evaluated == null || dimensionSnap == null ? "Native anchor point evaluation is unavailable (getPoint HRESULT 0x" +
                    unchecked((uint)flags[slot * 4 + 2]).ToString("X8") + "; GetSnapPoint HRESULT 0x" +
                    unchecked((uint)flags[slot * 4 + 3]).ToString("X8") + ")." : null;
                var row = new
                {
                    slot,
                    is_null = isNull,
                    source_mc_id = strings[1 + slot],
                    gs_marker = isNull ? (int?)null : markers[slot],
                    parameter = !isNull && double.IsFinite(parameters[slot]) ? (double?)parameters[slot] : null,
                    data = isNull ? null : data[slot].ToString(CultureInfo.InvariantCulture),
                    complex_reference = strings[4 + slot],
                    viewport_reference = strings[7 + slot],
                    evaluated_point_wcs = evaluated,
                    dimension_snap_point = dimensionSnap,
                    dimension_snap_point_scope = "Native McDimensionData.GetSnapPoint output without coordinate conversion; its coordinate frame is not specified by the public SDK.",
                    evaluation_unavailable_reason = evaluationReason
                };
                budget.Add(row); anchors.Add(row);
                if (isNull) continue;
                if (flags[slot * 4 + 1] != 0 || strings[4 + slot] != null || strings[7 + slot] != null)
                { nonlocalAnchor = true; continue; }
                var referenced = Identity(strings[1 + slot]);
                sourceCounts[referenced] = sourceCounts.GetValueOrDefault(referenced) + 1;
                if (!sourceMarkers.TryGetValue(referenced, out var observedMarkers)) sourceMarkers[referenced] = observedMarkers = new HashSet<int>();
                observedMarkers.Add(markers[slot]);
            }
            Need(!nonlocalAnchor, "A stored anchor has a complex or viewport reference outside the local view observation route.");
            Need(sourceCounts.ContainsKey(viewIdentity), "The classified drawing view is absent from the dimension's actual stored anchor IDs.");
            var table = (Db.BlockTable)transaction.GetObject(database.BlockTableId, Db.OpenMode.ForRead);
            var model = table[Db.BlockTableRecord.ModelSpace];
            EntityRef Ref(Db.ObjectId id) => new(summary.@ref.bridge_instance_id, summary.@ref.document_id, Validation.NormalizeHandle(id.Handle.ToString()));
            Db.Entity Physical(McObjectId id, Db.ObjectId expectedOwner)
            {
                cancel(); Need(id.Handle != 0, "A native dependency has no addressable physical handle.");
                var raw = CadStructureWriter.Find(database, unchecked((ulong)id.Handle).ToString("X"));
                Need(transaction.GetObject(raw, Db.OpenMode.ForRead) is Db.Entity entity && entity.Database?.UnmanagedObject == database.UnmanagedObject &&
                    entity.OwnerId == expectedOwner && !entity.IsErased &&
                    (McObjectId.FromHandle(raw.Handle.Value, lookup.ID) == id || McObjectId.FromHandle(raw.Handle.Value, owner.ID) == id),
                    "A dependency's native identity does not match its physical database object and owner.");
                return (Db.Entity)transaction.GetObject(raw, Db.OpenMode.ForRead);
            }
            foreach (var pair in sourceCounts)
            {
                cancel(); var view = pair.Key.GetObjectOfType<DrawingView>();
                Need(view != null && view.ID == pair.Key && view.DbEntity.DocumentID == owner.ID &&
                    view.DbEntity.Document?.ID == owner.ID,
                    "A dimension reference is not a DrawingView in the dimension's actual owning space.");
                var rawView = Physical(pair.Key, source.OwnerId);
                var sources = view.GetTop3dEntitiesInView();
                Need(sources != null, "The referenced view's source inventory is unavailable.");
                var sourceRefs = new List<EntityRef>(); var nativeSources = new HashSet<McObjectId>();
                foreach (var body in sources!)
                {
                    cancel(); Need(!body.IsNull && nativeSources.Add(body), "The view has an empty or repeated native source identity.");
                    var physical = Physical(body, model);
                    Need(physical.GetType() == typeof(Db.Solid3d), "A referenced view source is not a current top-level local Model solid.");
                    var reference = Ref(physical.ObjectId); budget.Add(reference); sourceRefs.Add(reference);
                }
                Need(sourceRefs.Count != 0, "The referenced view has no current local solid source.");
                double scale = view.DbEntity.Scale, drawingScale = view.DbEntity.DScale;
                Need(double.IsFinite(scale) && double.IsFinite(drawingScale), "The referenced view has unavailable scale metadata.");
                var link = new
                {
                    view_ref = Ref(rawView.ObjectId),
                    view_mc_id = pair.Key.ToString(),
                    owner_document_id = owner.ID.ToString(),
                    native_anchor_count = pair.Value,
                    source_refs = sourceRefs.ToArray(),
                    source_mc_ids = nativeSources.Select(id => id.ToString()).ToArray(),
                    view_status = view.Status.ToString(),
                    is_suppressed = view.IsSuppressed,
                    auto_update_enabled = view.EnableAutoUpdate,
                    scale,
                    drawing_scale = drawingScale,
                    healthy = view.Status.ToString() == "Ok" && !view.IsSuppressed,
                    source_gs_marker = sourceMarkers[pair.Key].Count == 1 ? (int?)sourceMarkers[pair.Key].Single() : null,
                    source_gs_marker_unavailable_reason = sourceMarkers[pair.Key].Count == 1 ? null :
                        "The stored anchors identify more than one GS marker; no single source marker is implied."
                };
                budget.Add(link); links.Add(link);
            }
            var result = Observation(true, null);
            SerializedCapacity357.Check(result, Wire.PayloadBudget - 8192, cancel);
            return result;
        }
        catch (OperationCanceledException) { throw; }
        catch (DomainException error) when (error.Error.code == "RESPONSE_LIMIT_EXCEEDED") { throw; }
        catch (Exception error)
        {
            cancel();
            return recognized ? Observation(false, Reason(error)) : null;
        }
    }
}
