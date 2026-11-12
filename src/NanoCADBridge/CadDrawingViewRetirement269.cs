using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Nodes;
using Multicad;
using Multicad.DatabaseServices;
using Multicad.DatabaseServices.StandardObjects;
using Multicad.Mc3D;
using NanoCAD.Contracts;
using Db = Teigha.DatabaseServices;
using G = Multicad.Geometry;
using static NanoCADBridge.CadDrawingViewEditState;

namespace NanoCADBridge;

// Read current ObjectId state and the actual local target, including erased
// targets. No persistent reactor is removed or changed by this observation.
internal static class CadPersistentReferenceObservation
{
    internal sealed record Observation(NativePersistentReferenceState State, object Facts);
    internal static Observation Read(Db.Entity root, Db.ObjectId id, Db.Transaction transaction, Action cancel)
    {
        cancel(); var database = root.Database;
        string rootHandle = Validation.NormalizeHandle(root.Handle.ToString());
        string rootDatabase = database.UnmanagedObject.ToInt64().ToString("X16", CultureInfo.InvariantCulture);
        bool? isNull = null, valid = null, local = null, erased = null, effectivelyErased = null;
        string? handle = null, token = null, reactorDatabase = null, runtime = null, owner = null;
        bool targetRead = false;
        object Facts() => new
        {
            root_handle = rootHandle,
            root_database_identity = rootDatabase,
            reactor_handle = handle,
            reactor_object_id = token,
            reactor_database_identity = reactorDatabase,
            reactor_is_null = isNull,
            reactor_is_valid = valid,
            reactor_same_database = local,
            reactor_is_erased = erased,
            reactor_is_effectively_erased = effectivelyErased,
            reactor_target_read = targetRead,
            reactor_runtime_type = runtime,
            reactor_owner_handle = owner,
            reactor_type_unavailable_reason = targetRead ? null : valid != true ? "null_or_invalid_identity" : local != true ? "foreign_target_not_opened" : "target_read_failed"
        };
        try
        {
            isNull = id.IsNull;
            if (!isNull.Value)
            {
                token = id.OldIdPtr.ToInt64().ToString("X16", CultureInfo.InvariantCulture); valid = id.IsValid;
                if (valid.Value)
                {
                    handle = Validation.NormalizeHandle(id.Handle.ToString());
                    var targetDatabase = id.Database;
                    reactorDatabase = targetDatabase?.UnmanagedObject.ToInt64().ToString("X16", CultureInfo.InvariantCulture);
                    local = targetDatabase != null && targetDatabase.UnmanagedObject == database.UnmanagedObject;
                    erased = id.IsErased; effectivelyErased = id.IsEffectivelyErased;
                    if (local.Value)
                    {
                        var target = transaction.GetObject(id, Db.OpenMode.ForRead, true);
                        if (target.ObjectId != id || target.Database?.UnmanagedObject != database.UnmanagedObject)
                            throw new DomainException("OBJECT_IDENTITY_MISMATCH", "A persistent-reference target resolved to another actual object/database.");
                        targetRead = true; runtime = target.GetType().FullName;
                        owner = target.OwnerId.IsNull ? null : Validation.NormalizeHandle(target.OwnerId.Handle.ToString());
                    }
                }
            }
            return new(new(isNull == true, valid == true, local == true, erased == true, effectivelyErased == true), Facts());
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception error)
        {
            throw new DomainException("DEPENDENCY_INVENTORY_UNAVAILABLE", "An actual persistent-reference target could not be completely observed.", false,
                new
                {
                    observation = Facts(),
                    error_type = error.GetType().FullName,
                    native_error = error is Teigha.Runtime.Exception native ? native.ErrorStatus.ToString() : null,
                    cause = error is DomainException domain ? domain.Error.code : null
                });
        }
    }
}

// SDK IMcDbObject.erase owns erasure. Section processing/source membership is
// not ownership; no SectionID/source setter or private reactor removal is used.
internal static class CadDrawingViewRetirement269
{
    internal sealed record Owner(McObjectId Id, McDbEntity Native, McObjectId Document, McObjectId Parent,
        Db.ObjectId Physical, string Kind, string Name);
    internal sealed record NativeState(McObjectId Id, JsonElement Data);
    internal sealed record RootState(Db.ObjectId Id, JsonElement Data, Db.ObjectId[] OriginalReactors, Db.ObjectId[] RetiredReactors);
    sealed record ReactorEntry(Db.ObjectId Id, bool Excluded, object Facts);
    sealed record RootObservation(JsonElement Data, Db.ObjectId[] Reactors, ReactorEntry[] Entries, int ExcludedErased);
    internal sealed record Plan(HashSet<McObjectId> Deleted, Owner[] Sections, Owner[] Breaks,
        Dictionary<McObjectId, McObjectId> Previous, NativeState[] Native, RootState[] Roots,
        JsonElement Settings, Db.ObjectId[] FormerSettings, HashSet<Db.ObjectId> Physical, NativeState[] BreakStates,
        string[] Warnings);

    static void Require([DoesNotReturnIf(false)] bool condition, string message, string code = "DRAWING_VIEW_DEPENDENTS", object? details = null)
    { if (!condition) throw new DomainException(code, message, false, details); }
    static string H(Db.ObjectId id) => Validation.NormalizeHandle(id.Handle.ToString());
    static bool Erased(McDbEntity entity) => (entity.StatusFlags & McDbObjectStateFlags.Erased) != 0;
    static bool Live(HistoryItem item) => item.Status.ToString() != "Erased" && !Erased(item.DbEntity);
    internal static bool Needed(DeleteDrawingViewsEdit edit, View[] views) => edit.Sections.Length > 0 || views.Any(view =>
        !view.Native.SectionID.IsNull || view.Native.IsSlice || view.Native.IsBasedOnSection || view.Native.IsRootSectionView ||
        view.Native.DependsOnSlice || view.Native.Attached2dBreaks is not { Length: 0 });

    static Dictionary<McObjectId, Db.ObjectId> Representations(DocumentState state, Db.Transaction tx, Action cancel)
    {
        var db = state.Document.Database; var drawing = Context(state); var result = new Dictionary<McObjectId, Db.ObjectId>();
        var blocks = (Db.BlockTable)tx.GetObject(db.BlockTableId, Db.OpenMode.ForRead);
        foreach (Db.ObjectId blockId in blocks)
        {
            cancel(); var block = (Db.BlockTableRecord)tx.GetObject(blockId, Db.OpenMode.ForRead);
            if (block.IsDependent || block.IsFromExternalReference || block.IsFromOverlayReference) continue;
            foreach (Db.ObjectId raw in block)
            {
                cancel(); if (Gone(raw)) continue; var id = McObjectId.FromHandle(raw.Handle.Value, drawing.ID); if (id.IsNull) continue;
                Require(result.TryAdd(id, raw), "A native identity has ambiguous current resident representations.", "OBJECT_IDENTITY_MISMATCH");
            }
        }
        return result;
    }
    static IEnumerable<McDocument> Documents(Snapshot snapshot, DocumentState state)
    {
        var drawing = Context(state); yield return drawing;
        if (drawing.TopDoc != null) yield return drawing.TopDoc;
        foreach (var view in snapshot.Views) if (view.Native.DbEntity.Document != null) yield return view.Native.DbEntity.Document;
        foreach (var body in snapshot.Bodies)
        {
            var native = body.Id.IsNull ? null : body.Id.GetObjectOfType<Mc3dSolid>();
            if (native?.DbEntity.Document != null) yield return native.DbEntity.Document;
        }
        foreach (var section in snapshot.Sections.EnumerateArray())
        {
            var id = snapshot.Views.Select(view => view.Native.SectionID).FirstOrDefault(id => id.ToString() == section.GetProperty("id").GetString());
            if (!id.IsNull && id.GetObjectOfType<Section>()?.DbEntity.Document is { } owner) yield return owner;
        }
    }
    static HistoryItem[] Items(Snapshot snapshot, DocumentState state, Action cancel)
    {
        var ids = Documents(snapshot, state).GroupBy(document => document.ID).Select(group => group.First())
            .SelectMany(document => ModelProfileDiscovery348.Ids<HistoryItem>(document, cancel)).ToHashSet();
        foreach (var view in snapshot.Views) ids.Add(view.Id);
        foreach (var body in snapshot.Bodies) foreach (var id in body.Contents) if (id.GetObjectOfType<HistoryItem>() != null) ids.Add(id);
        var result = new List<HistoryItem>();
        foreach (var id in ids)
        {
            cancel(); var item = id.GetObjectOfType<HistoryItem>();
            Require(item != null && item.ID == id, "An actual native construction identity is unavailable.", "DEPENDENCY_INVENTORY_UNAVAILABLE");
            if (Live(item)) result.Add(item);
        }
        return result.ToArray();
    }
    static void Dependencies(HistoryItem item, HashSet<McObjectId> deleted, Action cancel)
    {
        cancel(); var dependents = item.GetAllDependent(false);
        Require(dependents != null, "The actual native dependent inventory is unavailable.", "DEPENDENCY_INVENTORY_UNAVAILABLE");
        foreach (var id in dependents)
        {
            cancel(); if (deleted.Contains(id)) continue;
            var dependent = id.GetObjectOfType<HistoryItem>();
            Require(dependent != null && dependent.ID == id && !Live(dependent),
                "A native retirement target has a retained dependent outside the explicit owned group.", details: new { target_id = item.ID.ToString(), dependent_id = id.ToString() });
        }
    }
    static CadDrawingViewLocalCutDataReader.Data ReadCutData(PlanarSketch sketch, DocumentState state, Db.Transaction tx,
        IReadOnlyDictionary<McObjectId, Db.ObjectId> representations, Action cancel)
    {
        return CadDrawingViewLocalCutDataReader.Read(sketch, state, tx, representations, cancel);
    }
    static object? CutData(PlanarSketch sketch, DocumentState state, Db.Transaction tx,
        IReadOnlyDictionary<McObjectId, Db.ObjectId> representations, Action cancel)
    {
        if (!sketch.IsLocalCutSketch) return null;
        var data = ReadCutData(sketch, state, tx, representations, cancel);
        return new
        {
            target = Id(data.TargetView),
            depth_view = Id(data.DepthView),
            point = new[] { data.DepthPoint.X, data.DepthPoint.Y, data.DepthPoint.Z },
            depth = data.Depth
        };
    }
    static JsonElement Native(HistoryItem item, DocumentState state, Db.Transaction tx,
        IReadOnlyDictionary<McObjectId, Db.ObjectId> representations, Action cancel)
    {
        cancel(); var inputs = item.GetDependsOn(false, false); var critical = item.GetCriticalDependsOn(); var dependents = item.GetDependent();
        bool directAvailable = dependents != null; object? emptyDirectProof = null;
        if (!directAvailable)
        {
            var runtime = item.GetType();
            var directOwner = runtime.GetMethod(nameof(HistoryItem.GetDependent), Type.EmptyTypes)?.DeclaringType;
            var completeOwner = runtime.GetMethod(nameof(HistoryItem.GetAllDependent), [typeof(bool)])?.DeclaringType;
            bool supportedDispatch = directOwner == typeof(McCustom3dFeature) && completeOwner == typeof(HistoryItem);
            List<McObjectId>? complete = null;
            if (supportedDispatch) { cancel(); complete = item.GetAllDependent(false); }
            bool provedEmpty = complete is { Count: 0 };
            // The installed McCustom3dFeature wrapper returns null for both a
            // successful empty set and failure. Only the published, successful
            // complete downstream inventory can positively prove direct-empty.
            if (provedEmpty) dependents = [];
            emptyDirectProof = new
            {
                direct_declaring_type = directOwner?.FullName,
                complete_declaring_type = completeOwner?.FullName,
                supported_dispatch = supportedDispatch,
                complete_downstream_attempted = supportedDispatch,
                complete_downstream_available = complete != null,
                complete_downstream_count = complete?.Count,
                direct_empty_proven = provedEmpty
            };
        }
        Require(inputs != null && critical != null && dependents != null, "Native reference inventories are unavailable.", "DEPENDENCY_INVENTORY_UNAVAILABLE",
            new
            {
                native_id = item.ID.ToString(),
                runtime_type = item.GetType().FullName,
                inputs_available = inputs != null,
                critical_inputs_available = critical != null,
                dependents_available = directAvailable,
                empty_direct_dependency_proof = emptyDirectProof
            });
        var sketch = item as PlanarSketch;
        object? geometry = sketch == null ? null : SketchGeometryReader.Read(sketch, cancel);
        var result = Wire.Element(new
        {
            id = item.ID.ToString(),
            type = item.GetType().FullName,
            document = Id(item.DbEntity.DocumentID),
            parent = Id(item.DbEntity.ParentId),
            children = Ids(item.DbEntity.ChildrenIds, "Native children"),
            host = Id(item.HostSolidID),
            previous = Id(item.OrderObjBefore),
            status = item.Status.ToString(),
            suppressed = item.IsSuppressed,
            inputs = Ids(inputs, "Native inputs"),
            critical = Ids(critical, "Critical native inputs"),
            dependents = Ids(dependents, "Native dependents"),
            planar = sketch == null ? null : Id(sketch.PlanarEntityID),
            profiles = sketch == null ? null : Ids(sketch.GetProfiles(), "Sketch profiles"),
            autoerase = sketch == null ? (bool?)null : sketch.AutoeraseOfInclObjects,
            containing = sketch == null ? null : Ids(sketch.GetContainingObjects(true, true, true), "Sketch members"),
            local_cut = sketch == null ? null : CutData(sketch, state, tx, representations, cancel),
            outer_cut = sketch == null ? null : Id(sketch.ViewIdForOuterCut),
            geometry
        });
        CadDrawingViewLifecycleState345.Bytes(result, cancel); return result;
    }
    static JsonElement NormalizeNative(JsonElement data, HashSet<McObjectId> deleted, Dictionary<McObjectId, McObjectId> previous)
    {
        var removed = deleted.Select(id => id.ToString()).ToHashSet(StringComparer.Ordinal);
        var node = JsonNode.Parse(data.GetRawText())!.AsObject();
        foreach (var field in new[] { "children", "dependents" })
            node[field] = JsonSerializer.SerializeToNode(data.GetProperty(field).EnumerateArray().Where(value => !removed.Contains(value.GetString()!)).ToArray(), Wire.Json);
        var current = data.GetProperty("previous").GetString();
        if (current != null)
        {
            var selected = deleted.FirstOrDefault(id => id.ToString() == current);
            if (!selected.IsNull) node["previous"] = Id(Skip(selected, deleted, previous));
        }
        return Wire.Element(node);
    }
    static RootObservation Root(Db.Entity entity, Db.Transaction tx, Action cancel, bool nativeBody,
        IReadOnlySet<Db.ObjectId>? permittedOriginal = null)
    {
        cancel(); var bytes = new ModelInventoryBudget(cancel);
        var owned = NativeOwnedMetadata.Capture(entity, tx, cancel, observeAssocManager: nativeBody, observeNativeProxy: nativeBody);
        using var xdata = entity.XData; using var reactors = entity.GetPersistentReactorIds();
        var links = new List<string>(); var actualIds = new List<Db.ObjectId>(); var entries = new List<ReactorEntry>(); int excluded = 0;
        var permitted = permittedOriginal ?? new HashSet<Db.ObjectId>();
        foreach (Db.ObjectId id in reactors)
        {
            cancel(); var observed = CadPersistentReferenceObservation.Read(entity, id, tx, cancel);
            bool inactive;
            try { inactive = NativePersistentReferences.ExcludeKnownErased(id, observed.State, permitted); }
            catch (DomainException error)
            {
                throw new DomainException("DRAWING_VIEW_DEPENDENTS", "A retained root has an unavailable, foreign or unpermitted erased persistent reference.", false,
                    new { observation = observed.Facts, policy = error.Error });
            }
            actualIds.Add(id);
            entries.Add(new(id, inactive, observed.Facts));
            if (inactive) excluded++; else links.Add(H(id));
        }
        object? geometry = null;
        var table = (Db.BlockTable)tx.GetObject(entity.Database.BlockTableId, Db.OpenMode.ForRead);
        var summary = EntityAdapter.Summary(entity, table[Db.BlockTableRecord.ModelSpace], "retirement", "current");
        if (summary.detail_support == "supported" && entity is not Db.Solid3d)
            geometry = EntityAdapter.Detail(entity, summary, CancellationToken.None, includeAppearance: false).geometry;
        var result = Wire.Element(new
        {
            handle = H(entity.ObjectId),
            owner = H(entity.OwnerId),
            type = entity.GetType().FullName,
            properties = EntityAdapter.StoredProperties(entity),
            geometry,
            entity.HasFields,
            owned,
            xdata = xdata == null ? null : NativeOwnedMetadata.Buffer(xdata, cancel, bytes.Add),
            reactors = links.OrderBy(value => value, StringComparer.Ordinal).ToArray()
        });
        CadDrawingViewLifecycleState345.Bytes(result, cancel); return new(result, actualIds.ToArray(), entries.ToArray(), excluded);
    }
    static JsonElement WithoutRootReactors(JsonElement data, IEnumerable<string> removed)
    {
        var set = removed.ToHashSet(StringComparer.Ordinal); var node = JsonNode.Parse(data.GetRawText())!.AsObject();
        node["reactors"] = JsonSerializer.SerializeToNode(data.GetProperty("reactors").EnumerateArray().Where(value => !set.Contains(value.GetString()!)).ToArray(), Wire.Json);
        return Wire.Element(node);
    }
    static void RetainedReferences(JsonElement data, HashSet<string> removed)
    {
        if (data.ValueKind == JsonValueKind.Array) { foreach (var item in data.EnumerateArray()) RetainedReferences(item, removed); return; }
        if (data.ValueKind != JsonValueKind.Object) return;
        string? reference = null;
        if (data.TryGetProperty("native_type", out var kind) && kind.ValueKind == JsonValueKind.String && kind.GetString() == "ObjectId" &&
            data.TryGetProperty("handle", out var objectId) && objectId.ValueKind == JsonValueKind.String) reference = objectId.GetString();
        if (data.TryGetProperty("code", out var code) && code.ValueKind == JsonValueKind.Number && code.TryGetInt32(out int group) && group == 1005 &&
            data.TryGetProperty("value", out var value))
        {
            if (value.ValueKind == JsonValueKind.String) reference = value.GetString();
            else if (value.ValueKind == JsonValueKind.Object && value.TryGetProperty("native_type", out var nativeType) && nativeType.ValueKind == JsonValueKind.String && nativeType.GetString() == "Handle" &&
                value.TryGetProperty("handle", out var handle) && handle.ValueKind == JsonValueKind.String) reference = handle.GetString();
        }
        if (data.TryGetProperty("owned", out var owned) && owned.ValueKind == JsonValueKind.False && data.TryGetProperty("runtime_type", out _) &&
            data.TryGetProperty("owner_handle", out _) && data.TryGetProperty("handle", out var entry) && entry.ValueKind == JsonValueKind.String) reference = entry.GetString();
        if (reference != null)
        {
            Require(reference.Length is > 0 and <= 16 && reference.All(Uri.IsHexDigit) &&
                ulong.TryParse(reference, NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out _), "An actual typed native handle reference is unavailable.");
            ulong bits = ulong.Parse(reference, NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture);
            // A zero native handle is real null-reference data, not an entity
            // selector. Preserve it exactly instead of normalizing/rejecting it.
            Require(bits == 0 || !removed.Contains(bits.ToString("X", CultureInfo.InvariantCulture)),
                "Retained native user data contains an actual typed reference to a retirement target; its owning relationship must be handled explicitly.");
        }
        foreach (var field in data.EnumerateObject()) RetainedReferences(field.Value, removed);
    }
    static void NativeReactors(McDbEntity entity, HashSet<McObjectId> allowed, Action cancel)
    {
        cancel(); var reactors = entity.GetReactors();
        Require(reactors != null, "The actual native permanent-reactor inventory is unavailable.", "DEPENDENCY_INVENTORY_UNAVAILABLE");
        foreach (var id in reactors) { cancel(); Require(allowed.Contains(id), "A documentation owner has a foreign/unselected native permanent reactor.", details: new { native_id = entity.Entity.ID.ToString(), reactor_id = id.ToString() }); }
    }
    [MethodImpl(MethodImplOptions.NoInlining)]
    static Owner[] BreakOwners(View[] selected, Dictionary<McObjectId, Db.ObjectId> representations, DocumentState state, Db.Transaction tx, Action cancel)
    {
        var result = new List<Owner>();
        foreach (var view in selected) foreach (var id in view.Native.Attached2dBreaks)
        {
            cancel(); var value = id.GetObjectOfType<Mapinet.Break2d>();
            Require(value != null && value.ID == id && value.GetType() == typeof(Mapinet.Break2d) && value.TargetDrawingViewId == view.Id &&
                !Erased(value.DbEntity) && value.DbEntity.DocumentID == view.Native.DbEntity.DocumentID &&
                (value.DbEntity.ParentId.IsNull || value.DbEntity.ParentId == view.Id) && value.DbEntity.ChildrenIds.Count == 0,
                "A break lacks the exact supported native adapter, reciprocal target or local ownership.", "DRAWING_VIEW_BREAK_OWNERSHIP");
            Require(representations.TryGetValue(id, out var physical) && tx.GetObject(physical, Db.OpenMode.ForRead).OwnerId == view.Space,
                "The native break is not uniquely resident in its target view's actual local sheet.", "DRAWING_VIEW_BREAK_OWNERSHIP");
            Require(!result.Any(owner => owner.Id == id), "A native break is attached more than once.", "DRAWING_VIEW_BREAK_OWNERSHIP");
            var inputs = value.GetDependsOn(false, false);
            Require(inputs != null && inputs.Count == 1 && inputs[0] == view.Id, "The actual supported break dependency differs from its target.", "DRAWING_VIEW_BREAK_OWNERSHIP");
            result.Add(new(id, value.DbEntity, value.DbEntity.DocumentID, value.DbEntity.ParentId, physical, "drawing_view_break", ""));
        }
        return result.ToArray();
    }
    [MethodImpl(MethodImplOptions.NoInlining)]
    static NativeState[] RetainedBreaks(Snapshot snapshot, DocumentState state, HashSet<McObjectId> deleted, Action cancel)
    {
        var result = new Dictionary<McObjectId, NativeState>();
        foreach (var document in Documents(snapshot, state).GroupBy(document => document.ID).Select(group => group.First()))
            foreach (var id in ModelProfileDiscovery348.Ids<Mapinet.Break2d>(document, cancel))
            {
                cancel(); if (deleted.Contains(id)) continue; var value = id.GetObjectOfType<Mapinet.Break2d>();
                Require(value != null && value.ID == id, "A retained break's actual native identity is unavailable.", "DEPENDENCY_INVENTORY_UNAVAILABLE");
                if (Erased(value.DbEntity)) continue;
                Require(!deleted.Contains(value.TargetDrawingViewId), "A retained break targets a retired view.");
                var lines = value.GetShiftedBreakLines();
                Require(lines != null, "A retained supported break has no complete shifted-line geometry.", "DRAWING_VIEW_STATE_UNAVAILABLE");
                try
                {
                    var shapes = new List<object>();
                    foreach (var geometry in lines)
                    {
                        cancel(); Require(geometry != null, "A retained break returned an unavailable shifted line.");
                        if (geometry.GeometryType == G.EntityGeomType.kLine)
                        { using var line = geometry.LineSeg; shapes.Add(SketchGeometryReader.Line(line)); }
                        else if (geometry.GeometryType == G.EntityGeomType.kCircArc)
                        { using var arc = geometry.CircArc; shapes.Add(SketchGeometryReader.Arc(arc)); }
                        else if (geometry.GeometryType == G.EntityGeomType.kPolyline) shapes.Add(BreakPolyline(geometry, cancel));
                        else throw new DomainException("DRAWING_VIEW_STATE_UNAVAILABLE", "A retained break's actual shifted-line geometry needs its supported serializer; no empty geometry was inferred.");
                    }
                    var skew = value.VectSkew;
                    var data = Wire.Element(new
                    {
                        id = id.ToString(),
                        target = Id(value.TargetDrawingViewId),
                        parent = Id(value.DbEntity.ParentId),
                        document = Id(value.DbEntity.DocumentID),
                        children = Ids(value.DbEntity.ChildrenIds, "Break children"),
                        kind = value.BreakType.ToString(),
                        value.BreakTypeIndex,
                        value.GapBetweenLines,
                        skew = new[] { skew.X, skew.Y, skew.Z },
                        coordinate_scope = "native_shifted_break_lines_DV_LCS",
                        geometry = shapes
                    });
                    CadDrawingViewLifecycleState345.Bytes(data, cancel); result[id] = new(id, data);
                }
                finally { foreach (var geometry in lines.Distinct(ReferenceEqualityComparer.Instance)) if (geometry is IDisposable owned) owned.Dispose(); }
            }
        return result.Values.OrderBy(item => item.Id.ToString(), StringComparer.Ordinal).ToArray();
    }
    static object BreakPolyline(G.EntityGeometry geometry, Action cancel)
    {
        using var polyline = geometry.Polyline; using var segments = polyline.Segments;
        Require(segments.Count >= 0, "A native break polyline has an invalid segment inventory.");
        var parts = new List<object>(); var bytes = new ModelInventoryBudget(cancel);
        for (uint index = 0; index < segments.Count; index++)
        {
            cancel(); object shape;
            switch (segments.SegType(index))
            {
                case G.SegPlType.sgLine:
                    using (var line = segments.GetLineSegAt(index)) shape = SketchGeometryReader.Line(line);
                    break;
                case G.SegPlType.sgArc:
                    using (var arc = segments.GetArcSegAt(index)) shape = SketchGeometryReader.Arc(arc);
                    break;
                case G.SegPlType.sgNurb:
                    using (var spline = segments.GetNurbSegAt(index)) shape = NativeSketchCurveGeometry445.Spline(spline, cancel);
                    break;
                case G.SegPlType.sgEllipse:
                    using (var ellipse = segments.GetEllipsSegAt(index)) shape = NativeSketchCurveGeometry445.Ellipse(ellipse, cancel);
                    break;
                default: throw new DomainException("DRAWING_VIEW_STATE_UNAVAILABLE", "A native break polyline segment has no supported geometry serializer.");
            }
            var item = new { index, geometry = shape }; bytes.Add(item); parts.Add(item);
        }
        return new { kind = "polyline", closed = polyline.IsClosed(), segments = parts };
    }
    internal static Plan Prepare(DeleteDrawingViewsEdit edit, Snapshot snapshot, View[] selected, DocumentState state, Db.Transaction tx, Action cancel)
    {
        var deleted = selected.Select(view => view.Id).ToHashSet(); var sections = new List<Owner>();
        var representations = Representations(state, tx, cancel);
        var settings = SectionSourceSelection357.Inventory(state.Document, tx, cancel); var erasedSettings = new List<Db.ObjectId>();
        foreach (var target in edit.Sections)
        {
            cancel(); var related = snapshot.Views.Where(view => view.Native.SectionID.ToString().Equals(target.SectionId, StringComparison.OrdinalIgnoreCase)).ToArray();
            Require(related.Length > 0, "The explicit Section has no current associated view.", "SECTION_NOT_FOUND");
            var section = related[0].Native.SectionID.GetObjectOfType<Section>();
            Require(section != null && section.ID == related[0].Native.SectionID, "The explicit Section identity is unavailable.", "OBJECT_IDENTITY_MISMATCH");
            var current = CadSectionCutState351.Section(section, state, tx, cancel);
            Require(current.Token == target.SectionToken, "Section state changed; refresh section_cut before retirement.", "PRECONDITION_FAILED");
            var backlinks = section.GetViews();
            Require(backlinks != null && backlinks.ToHashSet().SetEquals(related.Select(view => view.Id)) && backlinks.All(deleted.Contains),
                "Explicit Section retirement must include its complete reciprocal view group.", "SECTION_DEPENDENTS");
            Require(section.DbEntity.ParentId.IsNull && section.HostSolidID.IsNull,
                "Retire an explicitly selected independent Section; a body/support-owned Section requires its owning history route.", "OUT_OF_SCOPE");
            var row = settings.EnumerateArray().Single(value => value.GetProperty("section_handle").GetString() == H(current.Entity));
            var settingsHandle = row.GetProperty("settings_handle").GetString();
            if (settingsHandle != null)
            {
                // Settings may be system/shared storage. Never erase them by
                // hand; retained users are checked through the full inventory.
                erasedSettings.Add(CadStructureWriter.Find(state.Document.Database, settingsHandle));
            }
            sections.Add(new(section.ID, section.DbEntity, section.DbEntity.DocumentID, section.DbEntity.ParentId, current.Entity, "section", section.Name));
            Require(deleted.Add(section.ID), "A Section retirement target is repeated.", "INVALID_ARGUMENT");
        }
        foreach (var view in selected)
        {
            cancel(); Require(view.Native.Attached2dBreaks != null, "Native break inventory is unavailable.");
            Require(view.Native.ChildViews.All(deleted.Contains), "Explicit deletion must include every view descendant.");
            var parent = view.Native.DbEntity.ParentId;
            Require(parent.IsNull || deleted.Contains(parent) || snapshot.Views.Any(item => item.Id == parent) ||
                snapshot.Bodies.Any(body => body.Id == parent) && view.Native.GetTop3dEntitiesInView().Contains(parent),
                "The selected view has an unclassified external native owner.");
        }
        Owner[] breaks = [];
        if (selected.Any(view => view.Native.Attached2dBreaks.Length > 0))
        {
            Require(AppDomain.CurrentDomain.GetAssemblies().Any(assembly => assembly.GetName().Name == "mapinet"), "The actual native break adapter is not already loaded.", "CAPABILITY_UNAVAILABLE");
            breaks = BreakOwners(selected, representations, state, tx, cancel); foreach (var owner in breaks) deleted.Add(owner.Id);
        }
        var all = Items(snapshot, state, cancel);
        foreach (var view in selected)
        {
            Require(view.Native.DbEntity.ChildrenIds.All(deleted.Contains), "A selected view owns an unselected native child.");
            Dependencies(view.Native, deleted, cancel);
        }
        foreach (var owner in sections)
        {
            Require(owner.Native.ChildrenIds.All(deleted.Contains), "A selected Section owner has an unclassified native child outside the retirement closure.");
            Dependencies(owner.Id.GetObjectOfType<HistoryItem>()!, deleted, cancel);
        }
        var nativeAllowed = deleted.Concat(selected.SelectMany(view => view.Native.GetTop3dEntitiesInView()))
            .Concat(selected.Select(view => view.Native.SectionID)).Concat(selected.Select(view => view.Native.ParentViewID))
            .Concat(selected.Select(view => view.Native.RootViewID)).Where(id => !id.IsNull).ToHashSet();
        foreach (var view in selected) NativeReactors(view.Native.DbEntity, nativeAllowed, cancel);
        foreach (var owner in sections.Concat(breaks)) NativeReactors(owner.Native, nativeAllowed, cancel);
        bool Affected(McObjectId id) => !id.IsNull && (deleted.Contains(id) || deleted.Contains(id.GetParentId(false)) || deleted.Contains(id.GetHostFeatureOrParentId(false)));
        foreach (var item in all.Where(item => !deleted.Contains(item.ID)))
        {
            cancel(); var inputs = item.GetDependsOn(false, false); var critical = item.GetCriticalDependsOn();
            Require(inputs != null && critical != null, "A retained native input inventory is unavailable.", "DEPENDENCY_INVENTORY_UNAVAILABLE");
            Require(!inputs.Concat(critical).Any(Affected), "A retained native object consumes the selected retirement group.", details: new { dependent_id = item.ID.ToString() });
            if (item is PlanarSketch sketch)
            {
                var data = sketch.IsLocalCutSketch ? ReadCutData(sketch, state, tx, representations, cancel) : null;
                Require(!Affected(sketch.PlanarEntityID) && !Affected(sketch.ViewIdForLocalCut) && !Affected(sketch.ViewIdForOuterCut) &&
                    (data == null || !Affected(data.TargetView) && !Affected(data.DepthView)),
                    "A retained sketch uses a retired view, depth-reference view or support.", details: new { dependent_id = item.ID.ToString() });
            }
            if (item is SketchProfile profile) Require(!Affected(profile.ParentSketchID) && !Affected(profile.Dependent3dFeatureID), "A retained profile uses the retirement group.");
            if (item is ProjectionSketchEnt projection) Require(!Affected(projection.SrcObjectID), "A retained projection uses the retirement group.");
        }
        var breakStates = AppDomain.CurrentDomain.GetAssemblies().Any(assembly => assembly.GetName().Name == "mapinet") ? RetainedBreaks(snapshot, state, deleted, cancel) : [];
        var physical = selected.Select(view => view.Entity).Concat(sections.Select(owner => owner.Physical)).Concat(breaks.Where(owner => !owner.Physical.IsNull).Select(owner => owner.Physical)).ToHashSet();
        foreach (var id in deleted)
        {
            if (representations.TryGetValue(id, out var raw)) physical.Add(raw);
        }
        foreach (var id in physical)
        {
            var raw = tx.GetObject(id, Db.OpenMode.ForRead); using var reactors = raw.GetPersistentReactorIds();
            foreach (Db.ObjectId reactor in reactors)
            {
                cancel(); var native = McObjectId.FromHandle(reactor.Handle.Value, Context(state).ID);
                Require(physical.Contains(reactor) || !native.IsNull && nativeAllowed.Contains(native), "A retirement root has a foreign/unselected persistent association.", details: new { target_handle = H(id), reactor_handle = H(reactor) });
            }
        }
        var previous = all.ToDictionary(item => item.ID, item => item.OrderObjBefore);
        var nativeStates = all.Where(item => !deleted.Contains(item.ID)).Select(item => new NativeState(item.ID, Native(item, state, tx, representations, cancel))).ToArray();
        var roots = new List<RootState>(); var bodyIds = snapshot.Bodies.Where(body => !body.Id.IsNull).Select(body => body.Entity).ToHashSet();
        var removedHandles = physical.Select(H).ToHashSet(StringComparer.Ordinal);
        foreach (var entity in snapshot.Entities.EnumerateArray())
        {
            cancel(); var rawId = CadStructureWriter.Find(state.Document.Database, entity.GetProperty("id").GetString()!);
            if (physical.Contains(rawId)) continue; var raw = (Db.Entity)tx.GetObject(rawId, Db.OpenMode.ForRead);
            var native = McObjectId.FromHandle(rawId.Handle.Value, Context(state).ID);
            var allowed = physical.Where(id => selected.Any(view => view.Entity == id && (view.Native.GetTop3dEntitiesInView().Contains(native) || view.Native.ParentViewID == native ||
                    view.Native.SectionID == native && native.GetObjectOfType<Section>()!.GetViews().Contains(view.Id))) ||
                sections.Any(section => section.Physical == id && section.Id.GetObjectOfType<Section>()!.GetTop3dEntitiesForSectioning().Contains(native)))
                .ToHashSet();
            var root = Root(raw, tx, cancel, bodyIds.Contains(rawId));
            RetainedReferences(root.Data.GetProperty("owned"), removedHandles); RetainedReferences(root.Data.GetProperty("xdata"), removedHandles);
            Require(root.Reactors.All(link => !physical.Contains(link) || allowed.Contains(link)),
                "A retained object has an unclassified persistent link to a retirement target.", details: new { retained_handle = H(rawId) });
            // Record actual original attachments, not merely possible source
            // relations or same-looking handles. Only these IDs may become
            // inactive entries in this operation's final observation.
            roots.Add(new(rawId, root.Data, root.Reactors, root.Reactors.Where(allowed.Contains).ToArray()));
        }
        var warnings = NativeOwnedMetadata.NativeProxyWarnings(roots.Select(root => root.Data).ToArray());
        var result = new Plan(deleted, sections.ToArray(), breaks, previous, nativeStates, roots.ToArray(), settings,
            erasedSettings.Distinct().ToArray(), physical, breakStates, warnings);
        CadDrawingViewLifecycleState345.Bytes(new
        {
            roots = roots.Select(root => root.Data),
            native = nativeStates.Select(item => item.Data),
            settings,
            deleted = deleted.Select(id => id.ToString())
        }, cancel);
        return result;
    }
    [MethodImpl(MethodImplOptions.NoInlining)]
    internal static void EraseBreaks(Plan plan, DocumentState state, Action cancel)
    {
        // The installed Break2d.OnErase detaches this exact target while it is
        // still live. Erase before the view, never dispatch a queued child twice.
        foreach (var owner in plan.Breaks)
        {
            cancel(); Context(state); var current = owner.Id.GetObjectOfType<Mapinet.Break2d>();
            Require(current != null && current.GetType() == typeof(Mapinet.Break2d) && current.ID == owner.Id && !Erased(current.DbEntity) && current.DbEntity.DocumentID == owner.Document &&
                current.DbEntity.ParentId == owner.Parent, "The selected native break changed identity/owner before erasure.", "OBJECT_IDENTITY_MISMATCH");
            Require(current.DbEntity.Erase(), "Native erase refused the explicitly attached supported break.", "NATIVE_ERASE_FAILED");
        }
    }
    internal static void EraseSections(Plan plan, DocumentState state, Action cancel)
    {
        // Explicit independent Sections are separate native retirement roots.
        foreach (var owner in plan.Sections)
        {
            cancel(); Context(state); if (Erased(owner.Native) || !owner.Physical.IsNull && Gone(owner.Physical)) continue;
            var current = owner.Id.GetObjectOfType<HistoryItem>();
            Require(current is Section && current.ID == owner.Id && Live(current) && current.DbEntity.DocumentID == owner.Document &&
                (current.DbEntity.ParentId == owner.Parent || current.DbEntity.ParentId.IsNull), "An originally owned retirement target lost its exact native identity/context.", "OBJECT_IDENTITY_MISMATCH");
            Require(current.DbEntity.Erase(), "Native erase refused an explicitly selected/proven-owned documentation owner.", "NATIVE_ERASE_FAILED");
        }
    }
    internal static JsonElement ExpectedSections(Plan plan, JsonElement before)
    {
        var removed = plan.Deleted.Select(id => id.ToString()).ToHashSet(StringComparer.Ordinal);
        return Wire.Element(before.EnumerateArray().Where(section => !removed.Contains(section.GetProperty("id").GetString()!)).Select(section =>
        {
            var node = JsonNode.Parse(section.GetRawText())!.AsObject();
            node["views"] = JsonSerializer.SerializeToNode(section.GetProperty("views").EnumerateArray().Where(id => !removed.Contains(id.GetString()!)).ToArray(), Wire.Json);
            return node;
        }).ToArray());
    }
    internal static string[] Verify(Plan plan, Snapshot after, DocumentState state, Db.Transaction tx, Action cancel)
    {
        foreach (var id in plan.Physical) { cancel(); Require(Gone(id), "A retired documentation object remains resident.", "NATIVE_ERASE_FAILED", new { handle = H(id) }); }
        foreach (var owner in plan.Sections.Concat(plan.Breaks))
        { cancel(); Require(Erased(owner.Native), "A retired native documentation owner did not report its erased state.", "NATIVE_ERASE_FAILED", new { native_id = owner.Id.ToString() }); }
        var representations = Representations(state, tx, cancel);
        var removedSections = plan.Sections.Select(section => H(section.Physical)).ToHashSet(StringComparer.Ordinal);
        var expectedSettings = Wire.Element(plan.Settings.EnumerateArray().Where(row => !removedSections.Contains(row.GetProperty("section_handle").GetString()!)).ToArray());
        Require(NativeMetadataEquality.Same(expectedSettings, SectionSourceSelection357.Inventory(state.Document, tx, cancel)),
            "Native retirement changed retained Section/settings membership, ownership or user data.", "DRAWING_VIEW_UPDATE_MISMATCH");
        var items = Items(after, state, cancel).Where(item => !plan.Deleted.Contains(item.ID)).ToDictionary(item => item.ID);
        Require(items.Count == plan.Native.Length, "Native retirement added/removed an independent construction object.", "DRAWING_VIEW_UPDATE_MISMATCH");
        foreach (var before in plan.Native)
        {
            cancel(); Require(items.TryGetValue(before.Id, out var current), "An independent native construction identity disappeared.", "DRAWING_VIEW_UPDATE_MISMATCH");
            Require(NativeMetadataEquality.Same(NormalizeNative(before.Data, plan.Deleted, plan.Previous), Native(current, state, tx, representations, cancel)),
                "Native retirement changed an independent object's geometry, inputs, driver or owner.", "DRAWING_VIEW_UPDATE_MISMATCH", new { native_id = before.Id.ToString() });
        }
        var bodyIds = after.Bodies.Where(body => !body.Id.IsNull).Select(body => body.Entity).ToHashSet();
        int inactiveEntries = 0;
        foreach (var before in plan.Roots)
        {
            cancel(); Require(!Gone(before.Id), "A retained drawing root disappeared.", "DRAWING_VIEW_UPDATE_MISMATCH");
            var entity = (Db.Entity)tx.GetObject(before.Id, Db.OpenMode.ForRead);
            var actual = Root(entity, tx, cancel, bodyIds.Contains(before.Id), before.RetiredReactors.ToHashSet());
            inactiveEntries += actual.ExcludedErased;
            var expected = WithoutRootReactors(before.Data, before.RetiredReactors.Select(H));
            if (!NativeMetadataEquality.Same(expected, actual.Data))
            {
                var expectedIds = before.OriginalReactors.Where(id => !before.RetiredReactors.Contains(id)).GroupBy(id => id).ToDictionary(group => group.Key, group => group.Count());
                var current = actual.Entries.Where(entry => !entry.Excluded).ToArray();
                var counts = current.GroupBy(entry => entry.Id).ToDictionary(group => group.Key, group => group.Count());
                var unexpected = current.FirstOrDefault(entry => !expectedIds.TryGetValue(entry.Id, out int count) || counts[entry.Id] > count);
                object? reactor = unexpected?.Facts; bool? attached = unexpected == null ? null : true;
                if (unexpected == null)
                {
                    var missing = expectedIds.Keys.FirstOrDefault(id => !counts.TryGetValue(id, out int count) || count < expectedIds[id]);
                    if (!missing.IsNull)
                    {
                        attached = false;
                        try { reactor = CadPersistentReferenceObservation.Read(entity, missing, tx, cancel).Facts; }
                        catch (OperationCanceledException) { throw; }
                        catch (DomainException error) { reactor = error.Error.details; }
                    }
                }
                throw new DomainException("DRAWING_VIEW_UPDATE_MISMATCH", "Native retirement changed an independent drawing root's geometry, properties or user data.", false,
                    new
                    {
                        root_handle = H(before.Id),
                        first_difference = LibraryBlockMetadataComparison.FirstDifference(expected, actual.Data),
                        reactor_currently_attached = attached,
                        reactor_observation = reactor
                    });
            }
        }
        var breakStates = plan.BreakStates.Length == 0 ? [] : RetainedBreaks(after, state, plan.Deleted, cancel);
        Require(breakStates.Length == plan.BreakStates.Length && plan.BreakStates.All(before => breakStates.Any(actual => actual.Id == before.Id && NativeMetadataEquality.Same(before.Data, actual.Data))),
            "Native retirement changed a retained break's actual identity, target, parameters or shifted-line geometry.", "DRAWING_VIEW_UPDATE_MISMATCH");
        int retainedStorage = plan.FormerSettings.Count(id => !Gone(id)); var warnings = new List<string>();
        if (retainedStorage != 0) warnings.Add("NATIVE_SECTION_SETTINGS_STORAGE_RETAINED: Native erase retained " + retainedStorage + " former settings record(s); all surviving Section/settings consumers and their data remain checked. No settings/manager record was manually erased.");
        if (inactiveEntries != 0) warnings.Add("NATIVE_ERASED_REACTOR_ENTRIES_OBSERVED: " + inactiveEntries + " stored entries refer only to exact originally attached, permitted retirement targets with actual local valid erased states. Reactor storage was not modified.");
        return warnings.ToArray();
    }
    internal static StructureChange[] Changes(Plan plan, StructureWriteArguments args) => plan.Sections.Concat(plan.Breaks.Where(owner => !owner.Physical.IsNull))
        .Select(owner => new StructureChange(0, "delete_drawing_views", new(args.BridgeId, args.DocumentId, H(owner.Physical)), owner.Kind, owner.Name, false)).ToArray();
}
