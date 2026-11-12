using System.Text.Json;
using System.Text.Json.Nodes;
using Multicad;
using Multicad.DatabaseServices;
using Multicad.Mc3D;
using NanoCAD.Contracts;
using NanoCAD.Ipc;
using G = Multicad.Geometry;
using Db = Teigha.DatabaseServices;

namespace NanoCADBridge;

// One coherent construction lifecycle. No guessed HostSolid setter, temporary
// feature, source erase or transfer of an already consumed profile.
internal static partial class ModelSketchLifecycleWriter362
{
    sealed record Binding(EditSpec Edit, Mc3dSolid Body, McDocument Drawing, McDocument Owner, Db.ObjectId Representation, string Layer,
        McObjectId[] Contents, McObjectId[] Construction, JsonElement Parameters, JsonElement Display, JsonElement Metadata, JsonElement Inventory,
        ModelViewFollowers.Snapshot Views, DrawingProfileWriter.Source[] Additions, string[] SourceHandles, JsonElement Sources,
        ModelSketchLifecycleReader362.Snapshot? Sketch, ModelProfileLifecycleState340.Snapshot? Profile, McObjectId[] Paths, ModelSketchSupportState396.Binding? Support)
    { internal McObjectId[] NativeReferences { get; init; } = []; internal ModelProfileConsumers410.Snapshot? Consumers { get; init; } internal DrawingProfileWriter.Source? Axis { get; init; } internal Section446[] Sections446 { get; init; } = []; internal McObjectId[][] LoftRails446 { get; init; } = []; }
    static void Need([System.Diagnostics.CodeAnalysis.DoesNotReturnIf(false)] bool value, string code, string message) => ModelSketchLifecycleReader362.Need(value, code, message);
    static string Key(McObjectId id) => id.ToString();
    static bool Equal(JsonElement a, JsonElement b, Action cancel) => ModelProfileRebindState349.Equivalent(a, b, cancel);
    static bool Same(Point3 a, Point3 b) => Math.Abs(a.X - b.X) <= 1e-7 && Math.Abs(a.Y - b.Y) <= 1e-7 && Math.Abs(a.Z - b.Z) <= 1e-7;
    internal static bool Handles(ModifyArguments args) => args.Operations.Any(edit => edit is ModelSketchCreate362 or ModelSketchProfileCreate362 or ModelProfileSweep362 or ModelProfileRevolution411 or ModelProfileLoft446);
    internal static void Preflight(ModifyArguments args, DocumentState state, Db.Transaction tx, Action? cancel = null) => Resolve(args, state, tx, cancel ?? (() => { }));
    static string Last(EditSpec edit) => edit switch { ModelSketchCreate362 value => value.ExpectedLastItemId, ModelSketchProfileCreate362 value => value.ExpectedLastItemId, ModelProfileSweep362 value => value.ExpectedLastItemId, ModelProfileRevolution411 value => value.ExpectedLastItemId, ModelProfileLoft446 value => value.ExpectedLastItemId, _ => throw new DomainException("INVALID_ARGUMENT", "Unknown sketch lifecycle request.") };
    static McObjectId FeatureProfile(HistoryItem feature) => feature switch { ExtrudeAlongPathFeature sweep => sweep.ProfileID, RevolveFeature revolution => revolution.ProfileID, LoftFeature loft => loft.ProfileID, _ => throw new DomainException("PROFILE_OWNERSHIP_MISMATCH", "Unsupported exact-profile forming feature.") };
    static McObjectId[] FeatureReferences(Binding binding) => binding.Paths.Concat(binding.Axis == null ? [] : [binding.Axis.Native]).ToArray();
    static JsonElement Inventory(DocumentState state, Db.Transaction tx, McDocument drawing, McDocument owner, McObjectId[] allowed, Action cancel)
    {
        var model = (Db.BlockTableRecord)tx.GetObject(ModelFeatureReorderState345.Model(state.Document.Database, tx), Db.OpenMode.ForRead); var rows = new List<object>(); var bytes = new ModelInventoryBudget(cancel);
        foreach (Db.ObjectId id in model)
        {
            cancel(); if (id.IsErased || id.IsEffectivelyErased) continue; var value = tx.GetObject(id, Db.OpenMode.ForRead); string? native = null;
            foreach (var doc in new[] { drawing, owner }.DistinctBy(doc => doc.ID))
            { var matched = McObjectId.FromHandle(id.Handle.Value, doc.ID); if (allowed.Contains(matched)) { native = Key(matched); break; } }
            var row = new { handle = id.Handle.ToString(), runtime_type = value.GetType().FullName, owner_handle = value.OwnerId.IsNull ? null : value.OwnerId.Handle.ToString(), native_projection_id = native }; bytes.Add(row); rows.Add(row);
        }
        return Wire.Element(rows.OrderBy(row => Wire.Element(row).GetProperty("handle").GetString(), StringComparer.OrdinalIgnoreCase));
    }
    static JsonElement Sources(IEnumerable<string> handles, DocumentState state, Db.Transaction tx, McDocument drawing, McDocument owner, PlanarSketch? sketch, McObjectId[] projections, Action cancel)
    {
        var result = new List<object>(); var budget = new ModelInventoryBudget(cancel);
        foreach (string handle in handles.Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(value => value, StringComparer.OrdinalIgnoreCase))
        {
            cancel(); var id = CadStructureWriter.Find(state.Document.Database, handle); var entity = tx.GetObject(id, Db.OpenMode.ForRead) as Db.Entity;
            Need(entity != null && entity.OwnerId == ModelFeatureReorderState345.Model(state.Document.Database, tx), "PROFILE_SOURCE_UNAVAILABLE", "A retained source is absent from current top-level Model Space.");
            var native = McObjectId.FromHandle(id.Handle.Value, drawing.ID); Need(!native.IsNull, "OBJECT_IDENTITY_MISMATCH", "A retained source has no exact drawing-native identity.");
            var detail = Wire.Element(EntityAdapter.Detail(entity, EntityAdapter.Summary(entity, entity.OwnerId, "", "", false), CancellationToken.None));
            var row = Wire.Element(new
            {
                handle = id.Handle.ToString(),
                native_id = Key(native),
                geometry = detail.GetProperty("geometry"),
                properties = EntityAdapter.StoredProperties(entity),
                metadata = ModelProfileElementsState358.Metadata(entity, tx, cancel, false),
                reactor_observation = ModelProfileElementsState358.SourceReactors(entity, native, drawing, owner, sketch?.ID ?? McObjectId.Null, projections, tx, cancel)
            }); budget.Add(row); result.Add(row);
        }
        return Wire.Element(result);
    }
    static Binding Resolve(ModifyArguments args, DocumentState state, Db.Transaction tx, Action cancel)
    {
        Need(args.Operations.Length == 1 && Handles(args), "INVALID_OPERATION_ORDER", "Supply one native sketch/profile/forming lifecycle operation.");
        Need(args.Units == "Millimeters" && args.TargetSpace == "model" && (int)state.Document.Database.Insunits == 4, "OUT_OF_SCOPE", "Use the named Millimeters Model Space part.");
        var loaded = AppDomain.CurrentDomain.GetAssemblies().Select(value => value.GetName().Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        Need(loaded.Contains("mapimgd") && loaded.Contains("MapiBaseTypes"), "CAPABILITY_UNAVAILABLE", "MultiCAD must already be loaded.");
        var drawing = ModelFeatureReorderState345.Scope(state, tx); var edit = args.Operations[0]; var representation = CadStructureWriter.Find(state.Document.Database, edit.Handles.Single());
        var raw = tx.GetObject(representation, Db.OpenMode.ForRead); Need(raw.GetType() == typeof(Db.Solid3d) && raw.OwnerId == ModelFeatureReorderState345.Model(state.Document.Database, tx), "OUT_OF_SCOPE", "Select an existing top-level native part representation.");
        var id = McObjectId.FromHandle(representation.Handle.Value, drawing.ID); var body = id.GetObjectOfType<Mc3dSolid>();
        Need(body != null && body.ID == id && ModelHistoryEditGuard.CurrentRepresentation(body, state, tx, cancel) == representation, "OBJECT_IDENTITY_MISMATCH", "Select the unique current native body representation.");
        var solid = (Db.Solid3d)raw; var layer = (Db.LayerTableRecord)tx.GetObject(solid.LayerId, Db.OpenMode.ForRead);
        Need(!layer.IsLocked && !layer.IsOff && !layer.IsFrozen && !layer.IsDependent, "LAYER_UNAVAILABLE", "The target body layer is unavailable.");
        Need(Key(body.LastPartItemID).Equals(Last(edit), StringComparison.OrdinalIgnoreCase), "PRECONDITION_FAILED", "Refresh the selected body's native last_item_id.");
        var owner = ModelProfileDiscovery348.OwnerDocument(body, state); var contents = ModelInventoryBudget.History(body, cancel).ToArray();
        var parameters = ModelParameterSnapshot.Capture(new(args.BridgeId, args.DocumentId, args.ExpectedRevision, args.OperationId, args.Units, edit.Handles.Single(), "", "", 0), state, cancel); ModelParameterSnapshot.Healthy(parameters);
        var views = ModelViewFollowers.Resolve(body, contents, cancel, includeStandalone: true); if (edit is ModelProfileSweep362 or ModelProfileRevolution411 or ModelProfileLoft446) ModelViewFollowers.PrepareInsertion(views, true);
        ModelSketchLifecycleReader362.Snapshot? sketch = null; ModelProfileLifecycleState340.Snapshot? profile = null; var additions = new List<DrawingProfileWriter.Source>(); McObjectId[] paths = []; ModelSketchSupportState396.Binding? support = null; DrawingProfileWriter.Source? axis = null; Section446[] sections446 = []; McObjectId[][] loftRails446 = [];
        if (edit is ModelSketchCreate362 create)
        {
            if (create.Support != null) support = ModelSketchSupportState396.Capture(create.Support, body, representation, owner, state, tx, args.BridgeId, cancel);
            var frame = support?.Plane ?? create.Plane; Need(frame != null, "INVALID_ARGUMENT", "Supply exactly one native support or explicit sketch plane.");
            if (create.NativeGeometry != null) NativeSketchValidation385.RequirePlane(create.NativeGeometry, frame, "native_geometry");
            foreach (string handle in create.SourceHandles) { cancel(); additions.Add(DrawingProfileWriter.SourceOf(CadStructureWriter.Find(state.Document.Database, handle), state, tx, false, frame)); }
        }
        else if (edit is ModelSketchProfileCreate362 select)
        {
            sketch = ModelSketchLifecycleReader362.Capture(body, ModelSketchLifecycleReader362.Resolve(select.SketchId, owner, cancel), state, tx, cancel);
            Need(sketch.Token == select.ExpectedSketchToken, "PRECONDITION_FAILED", "The sketch, peer profiles, geometry, source bindings or observed constraints changed; refresh sketch_ids.");
            var normal = DrawingWorkplane.Normal(sketch.Plane); var delta = new Point3(select.RegionPoint.X - sketch.Plane.Origin.X, select.RegionPoint.Y - sketch.Plane.Origin.Y, select.RegionPoint.Z - sketch.Plane.Origin.Z);
            Need(new[] { select.RegionPoint.X, select.RegionPoint.Y, select.RegionPoint.Z }.All(double.IsFinite) && Math.Abs(DrawingWorkplane.Dot(delta, normal)) < 1e-7, "INVALID_ARGUMENT", "Select a finite WCS region point on the actual sketch plane.");
            // The native ray selects an atomic region of the current sketch.
            // Open projected references must not be mistaken for its boundary;
            // the actual selected contours are verified after native selection.
        }
        else if (edit is ModelProfileLoft446 loft)
        {
            sections446 = ReadSections446(loft, body, owner, state, tx, cancel); profile = sections446[0].Profile; sketch = sections446[0].Sketch;
            var rails = loft.GuideHandles.Concat(loft.PathHandle == null ? [] : new[] { new[] { loft.PathHandle } });
            loftRails446 = rails.Select(chain => ModelSweepFactory.ResolvePaths(chain, state, tx)).ToArray(); paths = loftRails446.SelectMany(chain => chain).Distinct().ToArray();
            foreach (var path in paths) { cancel(); Need(path.GetObjectOfType<Multicad.DatabaseServices.StandardObjects.DbGeometry>() is { } curve && curve.ID == path, "OBJECT_IDENTITY_MISMATCH", "A loft rail must resolve to its exact current native drawing curve."); }
        }
        else
        {
            var request = edit switch { ModelProfileSweep362 sweepSpec => (sweepSpec.ProfileId, sweepSpec.ExpectedSketchId, sweepSpec.ExpectedProfileToken), ModelProfileRevolution411 revolutionSpec => (revolutionSpec.ProfileId, revolutionSpec.ExpectedSketchId, revolutionSpec.ExpectedProfileToken), _ => throw new DomainException("INVALID_ARGUMENT", "Unknown profile consumer request.") };
            var selected = ModelProfileDiscovery348.Resolve(request.ProfileId, owner, cancel); ModelProfileConsumerOwnership362.RequireUnused(selected, owner, cancel);
            profile = ModelProfileLifecycleState340.Capture(selected, body, state, tx, cancel, allowCircularHoles: true); sketch = ModelSketchLifecycleReader362.Capture(body, profile.Sketch, state, tx, cancel);
            Need(Key(profile.Sketch.ID).Equals(request.ExpectedSketchId, StringComparison.OrdinalIgnoreCase) && profile.Token == request.ExpectedProfileToken, "PRECONDITION_FAILED", "The exact profile, parent, selected boundary or source bindings changed; refresh profile_ids.");
            if (edit is ModelProfileSweep362 sweep)
            {
                paths = ModelSweepFactory.ResolvePaths(sweep.PathHandles, state, tx); Need(paths.Distinct().Count() == paths.Length, "INVALID_ARGUMENT", "Sweep path IDs must be distinct.");
                foreach (var path in paths) { cancel(); Need(path.GetObjectOfType<Multicad.DatabaseServices.StandardObjects.DbGeometry>() is { } curve && curve.ID == path, "OBJECT_IDENTITY_MISMATCH", "A retained sweep path must resolve to its actual native drawing curve."); }
            }
            else axis = ModelProfileRevolutionNative411.ResolveAxis((ModelProfileRevolution411)edit, profile.Plane, drawing, state, tx, cancel);
        }
        if (sketch != null) DrawingViewSketchWriteScope282.RequireSupported(sketch.Sketch);
        foreach (var section in sections446) DrawingViewSketchWriteScope282.RequireSupported(section.Sketch.Sketch);
        var handles = ProjectedHistorySources.Read(body, contents, state, tx, cancel).Select(source => source.handle)
            .Concat(additions.Select(source => source.Id.Handle.ToString())).Concat(sketch?.Projections.Sources.Select(source => source.handle) ?? []).Concat(edit is ModelProfileSweep362 sweepRequest ? sweepRequest.PathHandles : []).Concat(axis == null ? [] : [axis.Id.Handle.ToString()]).Concat(sections446.SelectMany(section => section.Profile.Sources.Select(source => source.handle))).Concat(edit is ModelProfileLoft446 loftHandles ? loftHandles.GuideHandles.SelectMany(chain => chain).Concat(loftHandles.PathHandle == null ? [] : [loftHandles.PathHandle]) : []).ToHashSet(StringComparer.OrdinalIgnoreCase);
        void RetainDrawingPath(McObjectId native)
        {
            cancel(); if (native.IsNull || native.Handle == 0 || !state.Document.Database.TryGetObjectId(new Db.Handle(native.Handle), out var rawPath) || rawPath.IsNull || rawPath.IsErased || rawPath.IsEffectivelyErased) return;
            if (McObjectId.FromHandle(rawPath.Handle.Value, drawing.ID) == native && tx.GetObject(rawPath, Db.OpenMode.ForRead) is Db.Curve curve && curve.OwnerId == ModelFeatureReorderState345.Model(state.Document.Database, tx)) handles.Add(rawPath.Handle.ToString());
        }
        foreach (var item in contents)
        {
            cancel(); var sweep = item.GetObjectOfType<ExtrudeAlongPathFeature>(); if (sweep != null) foreach (var path in sweep.GetPath() ?? []) RetainDrawingPath(path.ID);
            var loft = item.GetObjectOfType<LoftFeature>(); if (loft == null) continue; if (loft.LoftType == LoftType.WithCenterLine && loft.Path != null) RetainDrawingPath(loft.Path.ID);
            for (int index = 0; index < loft.GuideRailsCount; index++) foreach (var path in loft.GetGuideRailsParams(index) ?? []) RetainDrawingPath(path.ID);
        }
        var sources = Sources(handles, state, tx, drawing, owner, sketch?.Sketch, sketch?.Projections.Ids ?? [], cancel); var construction = ModelProfileDiscovery348.ConstructionIds(owner, cancel);
        var nativeReferences = edit is ModelSketchCreate362 nativeRequest ? ModelSketchNativeReferences410.Resolve(nativeRequest.ReferenceIds ?? [], body, representation, state, args.BridgeId, cancel) : [];
        var consumers = profile == null ? null : ModelProfileConsumers410.Capture(profile.Profile, owner, cancel);
        if (consumers != null) ModelProfileConsumers410.RequireBody(consumers, body);
        return new(edit, body, drawing, owner, representation, solid.Layer, contents, construction, parameters, Wire.Element(EntityAdapter.StoredProperties(solid)), ModelProfileElementsState358.Metadata(solid, tx, cancel, true),
            Inventory(state, tx, drawing, owner, construction, cancel), views, additions.ToArray(), handles.ToArray(), sources, sketch, profile, paths, support)
        { NativeReferences = nativeReferences, Consumers = consumers, Axis = axis, Sections446 = sections446, LoftRails446 = loftRails446 };
    }
    static JsonElement Without(JsonElement value, params string[] fields)
    { var node = JsonNode.Parse(value.GetRawText())!.AsObject(); foreach (string field in fields) node.Remove(field); return Wire.Element(node); }
    static void RequireProfileDesign(JsonElement before, JsonElement after, bool acquiredSketch, bool acquiredProfile, bool consumed, Action cancel)
    {
        var fields = new List<string>(); if (acquiredSketch) fields.AddRange(["sketch_host_solid_id", "sketch_previous"]); if (acquiredProfile) fields.AddRange(["profile_host_solid_id", "profile_previous"]); if (consumed) fields.Add("dependent_feature_id");
        Need(Equal(Without(before, fields.ToArray()), Without(after, fields.ToArray()), cancel), "PROFILE_GEOMETRY_MISMATCH", "Native consumption changed a retained profile's selected geometry, source, parent, support, constraint observation or other relationship.");
    }
    static void RequireMetadata(JsonElement before, JsonElement after, string identity, string[] added, Action cancel)
    {
        Need(before.GetProperty("scope").GetString() == after.GetProperty("scope").GetString(), "PROFILE_SOURCE_CHANGED", "Native construction owned-metadata observation scope changed.");
        if (!before.TryGetProperty("metadata", out var oldMetadata))
        { Need(before.GetRawText() == after.GetRawText(), "PROFILE_SOURCE_CHANGED", "Native construction metadata availability changed."); return; }
        JsonElement Row(JsonElement observation) => Wire.Element(new { handle = identity, native_id = identity, metadata = observation.GetProperty("metadata"), reactor_observation = observation.GetProperty("reactor_observation") });
        var previous = Wire.Element(new[] { Row(before) }); var current = Wire.Element(new[] { Row(after) });
        var owned = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase) { { identity, added } };
        if (!ModelProfileSourcePreservation360.Retained(previous, current, identity, new Dictionary<string, string[]>(), owned, new HashSet<string>(), new HashSet<string>(), (a, b) => Equal(a, b, cancel), cancel, out string reason, out var old, out var next))
            throw new DomainException("PROFILE_SOURCE_CHANGED", "Retained native construction owned metadata or unrelated reactors changed.", false, ModelProfileSourcePreservation360.Describe(previous, current, old, next, reason, cancel));
    }
    static void RequireSketch(Binding before, ModelSketchLifecycleReader362.Snapshot after, SketchProfile? added, HistoryItem? feature, Action cancel)
    {
        if (before.Sketch == null) return; var fields = new List<string> { "profiles" }; bool acquired = feature != null && before.Sketch.Stored.GetProperty("host_solid_id").ValueKind == JsonValueKind.Null;
        if (acquired) fields.AddRange(["host_solid_id", "previous"]);
        var sketchLinks = new List<string>(); if (added != null) sketchLinks.Add(Key(added.ID)); if (acquired) sketchLinks.Add(Key(before.Body.ID));
        RequireMetadata(before.Sketch.Stored.GetProperty("native_metadata"), after.Stored.GetProperty("native_metadata"), Key(after.Sketch.ID), sketchLinks.ToArray(), cancel); fields.Add("native_metadata");
        // Existing native containing membership must persist. Only the newly
        // selected profile can be an added containing object, with ContainsObject
        // proof. All geometry/projection/support/source identities stay exact.
        var oldMembers = before.Sketch.Containing.ToHashSet(); var newMembers = after.Containing.ToHashSet();
        Need(oldMembers.IsSubsetOf(newMembers) && newMembers.Except(oldMembers).All(id => added != null && id == added.ID && after.Sketch.ContainsObject(id)), "SKETCH_OWNERSHIP_MISMATCH", "Unexpected native sketch containing-membership change."); fields.Add("containing_ids");
        Need(Equal(Without(before.Sketch.Stored, fields.ToArray()), Without(after.Stored, fields.ToArray()), cancel), "SKETCH_GEOMETRY_MISMATCH", "Retained sketch geometry, source bindings, support, metadata or observed constraints changed.");
        var actual = after.Profiles.EnumerateArray().ToDictionary(item => item.GetProperty("profile_id").GetString()!, StringComparer.OrdinalIgnoreCase);
        foreach (var peer in before.Sketch.Profiles.EnumerateArray())
        {
            cancel(); string identity = peer.GetProperty("profile_id").GetString()!; Need(actual.TryGetValue(identity, out var current), "PROFILE_OWNERSHIP_MISMATCH", "A peer profile disappeared from its retained sketch.");
            Need(peer.GetProperty("properties").GetRawText() == current.GetProperty("properties").GetRawText(), "PROFILE_SOURCE_CHANGED", "Existing native profile display properties changed.");
            bool selected = feature != null && FeatureProfiles446(feature, cancel).Any(id => identity.Equals(Key(id), StringComparison.OrdinalIgnoreCase));
            bool acquiredProfile = feature != null && peer.GetProperty("state").GetProperty("profile_host_solid_id").ValueKind == JsonValueKind.Null && current.GetProperty("state").GetProperty("profile_host_solid_id").GetString() == Key(before.Body.ID);
            // Capture already proves every nonnull peer host through exact
            // selected-body composition; an unused sibling keeps its consumer.
            var peerLinks = new List<string>(); if (selected) peerLinks.Add(Key(feature!.ID)); if (acquiredProfile) peerLinks.Add(Key(before.Body.ID));
            RequireMetadata(peer.GetProperty("native_metadata"), current.GetProperty("native_metadata"), identity, peerLinks.ToArray(), cancel);
            RequireProfileDesign(Without(peer.GetProperty("state"), "containing_ids"), Without(current.GetProperty("state"), "containing_ids"), acquired, acquiredProfile, selected, cancel);
        }
        var existing = before.Sketch.Profiles.EnumerateArray().Select(item => item.GetProperty("profile_id").GetString()!).ToHashSet(StringComparer.OrdinalIgnoreCase);
        Need(actual.Keys.Except(existing, StringComparer.OrdinalIgnoreCase).All(id => added != null && id.Equals(Key(added.ID), StringComparison.OrdinalIgnoreCase)), "PROFILE_OWNERSHIP_MISMATCH", "An unrelated profile appeared on this sketch.");
    }
    static void RequireParameters(Binding before, JsonElement after, PlanarSketch sketch, SketchProfile? profile, HistoryItem? feature, Action cancel)
    {
        Need(feature == null || profile != null && before.Sketch != null, "PROFILE_OWNERSHIP_MISMATCH", "Forming parameter preservation requires the exact selected profile and immutable parent sketch snapshot.");
        string? selectedProfileId = profile == null ? null : Key(profile.ID);
        var actual = after.GetProperty("nodes").EnumerateArray().ToDictionary(node => node.GetProperty("mc_id").GetString()!, StringComparer.OrdinalIgnoreCase);
        string? viewRoot = before.Views.ViewIds.Length == 0 ? null : before.Views.Metadata.EnumerateArray().Single(node => node.GetProperty("previous").GetString() == Key(before.Views.LastModelItem)).GetProperty("id").GetString();
        JsonElement Normalize(JsonElement node)
        {
            var copy = JsonNode.Parse(node.GetRawText())!.AsObject(); string identity = copy["mc_id"]!.GetValue<string>();
            foreach (string dependencyField in new[] { "dependencies", "stable_dependencies" })
                if (copy[dependencyField] is JsonArray dependencies)
                {
                    // Only the exact newly proved part-owned sketch can become a
                    // root part dependency during supported construction.
                    var ids = dependencies.Select(id => id!.GetValue<string>());
                    if ((before.Support != null || before.NativeReferences.Length > 0) && identity == Key(before.Body.ID)) ids = ids.Where(id => !id.Equals(Key(sketch.ID), StringComparison.OrdinalIgnoreCase));
                    copy[dependencyField] = new JsonArray(ids.OrderBy(id => id, StringComparer.OrdinalIgnoreCase).Select(id => (JsonNode?)JsonValue.Create(id)).ToArray());
                }
            if (feature != null && identity == viewRoot) copy["native_order_before_id"] = null;
            bool acquired = feature != null && before.Sketch!.Stored.GetProperty("host_solid_id").ValueKind == JsonValueKind.Null;
            if (feature != null && (identity == Key(sketch.ID) || identity == selectedProfileId))
            {
                if (acquired) { copy["host_solid_id"] = null; copy["native_order_before_id"] = null; }
                if (copy["parameters"] is JsonObject parameters)
                { parameters.Remove("profile_editing"); if (identity == selectedProfileId) { parameters.Remove("dependent_feature_id"); parameters.Remove("ownership"); parameters.Remove("forward_consumers"); } }
            }
            if (copy["parameters"] is JsonObject fields && fields["edge_ids"] is JsonArray edges) { fields["edge_count"] = edges.Count; fields.Remove("edge_ids"); }
            // A previously retained drawing Line may already be an axis of an
            // older revolution. Only the exact new feature's witnessed use is
            // added; all existing axis consumers remain part of the snapshot.
            if (feature is RevolveFeature && before.Axis != null && identity == Key(before.Axis.Native) && copy["parameters"] is JsonObject source && source["revolution_axis_ids"] is JsonArray axes)
                source["revolution_axis_ids"] = new JsonArray(axes.Where(id => id?.GetValue<string>() != Key(feature.ID)).Select(id => id == null ? null : JsonNode.Parse(id.ToJsonString())).ToArray());
            NormalizeAdditionalSections446(copy, before, feature); return Wire.Element(copy);
        }
        foreach (var node in before.Parameters.GetProperty("nodes").EnumerateArray())
        { cancel(); string id = node.GetProperty("mc_id").GetString()!; Need(actual.TryGetValue(id, out var current) && Equal(Normalize(node), Normalize(current), cancel), "MODEL_STATE_UNAVAILABLE", "An existing history item changed identity, design parameters, consumer or native order."); }
        var variables = after.GetProperty("variables").EnumerateArray().ToDictionary(value => value.GetProperty("mc_id").GetString()!, StringComparer.OrdinalIgnoreCase);
        var addedConsumers = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (feature != null) addedConsumers.Add(Key(feature.ID));
        if (before.Support != null || before.NativeReferences.Length > 0) addedConsumers.Add(Key(sketch.ID));
        foreach (var value in before.Parameters.GetProperty("variables").EnumerateArray())
        { cancel(); Need(variables.TryGetValue(value.GetProperty("mc_id").GetString()!, out var current) && ModelProfileConsumers410.RetainedVariable(value, current, addedConsumers, cancel), "MODEL_STATE_UNAVAILABLE", "An existing native variable, scalar, formula, owner or consumer changed."); }
    }
    static void RegisterFresh(McDbObject value, McObjectId identity, Binding binding, Action cancel)
    {
        cancel(); Need(!identity.IsNull && !binding.Construction.Contains(identity) && !binding.Contents.Contains(identity), "OBJECT_IDENTITY_MISMATCH", "Native creation did not return a fresh construction identity.");
        var actual = value.Document; Need(actual == null || actual.ID == binding.Owner.ID, "SKETCH_OWNERSHIP_MISMATCH", "New construction belongs to a different native document; no owner transfer is attempted.");
        if (actual == null) Need(McObjectManager.Add2Document(value, binding.Owner), "SKETCH_OWNERSHIP_MISMATCH", "Native construction registration in the explicit actual owner document failed.");
        Need(value.Document != null && value.Document.ID == binding.Owner.ID, "SKETCH_OWNERSHIP_MISMATCH", "New native construction lacks actual verified document residency.");
    }
    internal static Result Execute(ModifyArguments args, DocumentState state, WriteJob job, ReadContext context, JsonLog log)
    {
        Binding? binding = null; McUndoPoint? tracker = null; PlanarSketch? sketch = null; SketchProfile? profile = null; HistoryItem? feature = null; var projections = new List<McObjectId>(); bool started = false; string stage = "preparation"; NativeSketchInitialization385.Created? nativeCreated = null; var observedNativeInputs = new List<McObjectId>(); SketchPlaneSpec? supportedFrame = null; SketchConstraintCurve[]? supportedSourceCurves = null; ModelSketchNativeSupport396? supportReceipt = null; ModelProfileConsumed446[]? consumedReceipt446 = null;
        void Trace(string value) { stage = value; log.Write(job.Operation, job.OriginalRequestId, "model_sketch362_checkpoint", transaction: value, operationId: job.OperationId); }
        void Close() { var value = tracker; tracker = null; if (value == null) return; try { value.Stop(); } finally { value.Dispose(); } }
        try
        {
            using (var tx = state.Document.Database.TransactionManager.StartTransaction()) binding = Resolve(args, state, tx, job.CheckCancellation);
            // Reserve actual receipt bytes before starting native mutation.
            var reservation = Wire.Element(new ModelSketchOutput362(args.OperationId, "committed", false, [], [], [], [], "not_guaranteed", [new(0, binding.Edit.Operation, new(args.BridgeId, args.DocumentId, binding.Edit.Handles[0]), new(args.BridgeId, args.DocumentId, "FFFFFFFFFFFFFFFF"), Key(binding.Body.ID), Key(binding.Owner.ID), "{FFFFFFFF-FFFF-FFFF-FFFF-FFFFFFFFFFFF}", new string('F', 64), Key(binding.Body.LastPartItemID), binding.SourceHandles.Select(handle => new EntityRef(args.BridgeId, args.DocumentId, handle)).ToArray(), true, "{FFFFFFFF-FFFF-FFFF-FFFF-FFFFFFFFFFFF}", new string('F', 64), "{FFFFFFFF-FFFF-FFFF-FFFF-FFFFFFFFFFFF}", new(Key(binding.Owner.ID), Key(binding.Body.ID), Key(binding.Body.ID), "native_host_affinity"))]));
            var nativeReservation = binding.Edit is ModelSketchCreate362 { NativeGeometry: { } initialization } ? Wire.Element(new
            {
                native_objects = initialization.Geometry.Select(g => new NativeSketchCreated385(g.Key, "{FFFFFFFF-FFFF-FFFF-FFFF-FFFFFFFFFFFF}", ["{FFFFFFFF-FFFF-FFFF-FFFF-FFFFFFFFFFFF}"])),
                constraint_changes = initialization.Constraints.Select(c => new SketchConstraintChange("{FFFFFFFF-FFFF-FFFF-FFFF-FFFFFFFFFFFF}", "{FFFFFFFF-FFFF-FFFF-FFFF-FFFFFFFFFFFF}", c.Kind, "{FFFFFFFF-FFFF-FFFF-FFFF-FFFFFFFFFFFF}", c.Second == null ? null : "{FFFFFFFF-FFFF-FFFF-FFFF-FFFFFFFFFFFF}", "{FFFFFFFF-FFFF-FFFF-FFFF-FFFFFFFFFFFF}", c.Value))
            }) : Wire.Element(new { });
            Need(JsonSerializer.SerializeToUtf8Bytes(reservation, Wire.Json).Length + JsonSerializer.SerializeToUtf8Bytes(nativeReservation, Wire.Json).Length + JsonSerializer.SerializeToUtf8Bytes(SectionBaseline446(binding), Wire.Json).Length + 16384 < Wire.PayloadBudget, "RESPONSE_LIMIT_EXCEEDED", "The complete lifecycle receipt exceeds transport bytes.");
            using (var tx = state.Document.Database.TransactionManager.StartTransaction())
            {
                var current = Resolve(args, state, tx, job.CheckCancellation);
                Need(current.Representation == binding.Representation && current.Drawing.ID == binding.Drawing.ID && current.Owner.ID == binding.Owner.ID && Equal(current.Parameters, binding.Parameters, job.CheckCancellation) && Equal(current.Sources, binding.Sources, job.CheckCancellation) && current.Construction.ToHashSet().SetEquals(binding.Construction) && Equal(current.Inventory, binding.Inventory, job.CheckCancellation) && Equal(current.Metadata, binding.Metadata, job.CheckCancellation) &&
                    (current.Consumers == null && binding.Consumers == null || current.Consumers is { } currentConsumers && binding.Consumers is { } previousConsumers && Equal(Wire.Element(currentConsumers), Wire.Element(previousConsumers), job.CheckCancellation)) && current.Axis?.Native == binding.Axis?.Native && current.Profile?.Token == binding.Profile?.Token && current.Sketch?.Token == binding.Sketch?.Token && Equal(Wire.Element(SectionBaseline446(current)), Wire.Element(SectionBaseline446(binding)), job.CheckCancellation) &&
                    (current.Support == null && binding.Support == null || current.Support != null && binding.Support != null && current.Support.Token == binding.Support.Token && Equal(current.Support.Stored, binding.Support.Stored, job.CheckCancellation)), "PRECONDITION_FAILED", "Native part/construction/source/support/consumer state changed before the first setter.");
            }
            ModelViewFollowers.CheckStandalone(binding.Body, binding.Views, job.CheckCancellation); job.CheckCancellation(); tracker = new McUndoPoint(false); tracker.Start(); started = true;
            if (binding.Edit is ModelSketchCreate362 create)
            {
                Trace("create_native_sketch"); sketch = binding.Body.AddPlanarSketch(); Need(sketch != null, "MODEL_UPDATE_FAILED", "Native standalone sketch was not created."); RegisterFresh(sketch.DbEntity, sketch.ID, binding, job.CheckCancellation);
                sketch.Name = create.Name; sketch.DbEntity.Layer = binding.Layer;
                sketch.DbEntity.Visibility = 0;
                if (binding.Support != null)
                {
                    Trace("bind_actual_part_sketch_support"); sketch.PlanarEntityID = binding.Support.Support; sketch.DbEntity.Update();
                    ModelSketchLifecycleReader362.Resolve(Key(sketch.ID), binding.Owner, job.CheckCancellation);
                    supportedFrame = ModelSketchSupportState396.RequireAttached(binding.Support, binding.Body, binding.Owner, sketch, job.CheckCancellation);
                    if (create.NativeGeometry != null) NativeSketchValidation385.RequirePlane(create.NativeGeometry, supportedFrame, "native_geometry");
                    if (create.SourceHandles.Length > 0)
                    {
                        using var tx = state.Document.Database.TransactionManager.StartTransaction();
                        supportedSourceCurves = create.SourceHandles.Select(handle => DrawingProfileWriter.SourceOf(CadStructureWriter.Find(state.Document.Database, handle), state, tx, false, supportedFrame).Curve).ToArray();
                    }
                }
                else { Need(create.Plane != null, "INVALID_ARGUMENT", "The standalone sketch requires an explicit plane."); using var plane = DrawingWorkplaneNative.Plane(create.Plane); Need(sketch.SetPlane(plane, false), "MODEL_UPDATE_FAILED", "Native sketch rejected the explicit source plane and coordinate system."); sketch.DbEntity.Update(); }
                foreach (var addition in binding.Additions)
                {
                    job.CheckCancellation(); Trace("project_retained_source"); var projection = sketch.ProjectObject(addition.Native, true); if (projection != null && !projection.ID.IsNull) projections.Add(projection.ID);
                    Need(projection != null && projection.HostSketchID == sketch.ID && projection.SrcObjectID == addition.Native && !binding.Construction.Contains(projection.ID) && projections.Count(id => id == projection.ID) == 1, "SKETCH_OWNERSHIP_MISMATCH", "Native sketch did not retain one fresh exact source projection."); RegisterFresh(projection.DbEntity, projection.ID, binding, job.CheckCancellation);
                    projection.DbEntity.Visibility = 0; projection.DbEntity.Update();
                }
                foreach (var reference in binding.NativeReferences)
                {
                    job.CheckCancellation(); Trace("project_retained_part_reference"); var projection = sketch.ProjectObject(reference, true);
                    if (projection != null && !projection.ID.IsNull) projections.Add(projection.ID);
                    Need(projection != null && projection.HostSketchID == sketch.ID && projection.SrcObjectID == reference && !binding.Construction.Contains(projection.ID) && projections.Count(id => id == projection.ID) == 1,
                        "SKETCH_OWNERSHIP_MISMATCH", "Native sketch did not retain its exact same-part edge projection.");
                    RegisterFresh(projection.DbEntity, projection.ID, binding, job.CheckCancellation); projection.DbEntity.Visibility = 0; projection.DbEntity.Update();
                }
                if (create.NativeGeometry != null)
                {
                    Need((create.NativeGeometry.ReferenceIds ?? []).ToHashSet(StringComparer.OrdinalIgnoreCase).SetEquals(create.ReferenceIds ?? []), "SKETCH_BINDING_UNAVAILABLE", "Initial constraint references must be exactly the declared native sources validated and projected into this sketch.");
                    Trace("initialize_native_sketch_geometry"); nativeCreated = NativeSketchInitialization385.Create(sketch, create.NativeGeometry, state, job.CheckCancellation, id => observedNativeInputs.Add(id));
                }
                sketch.DbEntity.Update();
            }
            else if (binding.Edit is ModelSketchProfileCreate362 select)
            {
                sketch = binding.Sketch!.Sketch; Trace("create_selected_native_profile"); profile = sketch.CreateProfile(); Need(profile != null && profile.ParentSketchID == sketch.ID, "SKETCH_OWNERSHIP_MISMATCH", "Native creation returned no profile on the exact retained sketch."); RegisterFresh(profile.DbEntity, profile.ID, binding, job.CheckCancellation);
                profile.Name = select.Name; profile.DbEntity.Layer = sketch.DbEntity.Layer; profile.DbEntity.Visibility = 0;
                Need(profile.Clear() && profile.IsEmpty, "MODEL_UPDATE_FAILED", "The fresh native profile did not start with an empty selected region set."); var normal = DrawingWorkplane.Normal(binding.Sketch.Plane);
                using var ray = new G.Line3d(new G.Point3d(select.RegionPoint.X, select.RegionPoint.Y, select.RegionPoint.Z), new G.Vector3d(normal.X, normal.Y, normal.Z));
                Need(profile.AddOrRemoveRegionByLine(ray), "MODEL_UPDATE_FAILED", "Native profile did not select the requested bounded WCS region."); profile.DbEntity.Update();
            }
            else if (binding.Edit is ModelProfileSweep362 sweep)
            {
                profile = binding.Profile!.Profile; sketch = binding.Profile.Sketch; ModelProfileConsumerOwnership362.RequireUnused(profile, binding.Owner, job.CheckCancellation);
                ModelViewFollowers.PrepareInsertion(binding.Views, false); Trace("consume_exact_profile_sweep"); var created = binding.Body.AddExtrudeAlongPathFeature(profile.ID, binding.Paths.Select(id => new McGeomParam { ID = id }).ToList()); feature = created;
                Need(created != null, "MODEL_UPDATE_FAILED", "Native sweep from the exact retained profile was not created."); created.Name = sweep.Name; created.PartOperation = sweep.PartOperation == "cut" ? PartFeatureOperation.Cut : PartFeatureOperation.Join;
                created.ProfileOrientation = sweep.Orientation == "normal_to_path" ? SweepProfileOrientation.NormalToPath : SweepProfileOrientation.ParallelToOriginalProfile; ModelSweepFactory.SetTaper(created, sweep.TaperAngle, true); created.DbEntity.Update();
                if (created.OrderObjBefore != binding.Views.LastModelItem) Need(created.SetOrderObjBefore(binding.Views.LastModelItem, false), "MODEL_UPDATE_FAILED", "Native sweep rejected its retained predecessor."); ModelViewFollowers.CompleteInsertion(binding.Views, created.ID);
            }
            else if (binding.Edit is ModelProfileLoft446 loftRequest)
            {
                profile = binding.Profile!.Profile; sketch = binding.Profile.Sketch; ModelViewFollowers.PrepareInsertion(binding.Views, false); Trace("consume_exact_profiles_loft");
                feature = ModelProfileLoftNative446.Append(binding.Body, binding.Sections446.Select(s => s.Profile.Profile).ToArray(), loftRequest, binding.LoftRails446, job.CheckCancellation);
                if (feature.OrderObjBefore != binding.Views.LastModelItem) Need(feature.SetOrderObjBefore(binding.Views.LastModelItem, false), "MODEL_UPDATE_FAILED", "Native loft rejected its retained predecessor."); ModelViewFollowers.CompleteInsertion(binding.Views, feature.ID);
            }
            else
            {
                var revolution = (ModelProfileRevolution411)binding.Edit; profile = binding.Profile!.Profile; sketch = binding.Profile.Sketch;
                Need(binding.Axis != null, "OBJECT_IDENTITY_MISMATCH", "The exact retained revolution axis is unavailable.");
                ModelViewFollowers.PrepareInsertion(binding.Views, false); Trace("consume_exact_profile_revolution");
                feature = ModelProfileRevolutionNative411.Append(binding.Body, profile, binding.Axis.Native, revolution, job.CheckCancellation);
                if (feature.OrderObjBefore != binding.Views.LastModelItem) Need(feature.SetOrderObjBefore(binding.Views.LastModelItem, false), "MODEL_UPDATE_FAILED", "Native revolution rejected its retained predecessor."); ModelViewFollowers.CompleteInsertion(binding.Views, feature.ID);
            }
            Trace("update_native_construction"); McObjectManager.UpdateAll(); if (feature != null) { Need(binding.Body.RebuildPart(), "MODEL_UPDATE_FAILED", "Native forming part rebuild failed."); McObjectManager.UpdateAll(); }
            (EntityRef Body, ModelSketchLifecycleReader362.Snapshot Sketch, ModelProfileLifecycleState340.Snapshot? Profile) Verify()
            {
                job.CheckCancellation(); using var tx = state.Document.Database.TransactionManager.StartTransaction(); var drawing = ModelFeatureReorderState345.Scope(state, tx, binding.Drawing.ID.ToString()); var owner = ModelProfileDiscovery348.OwnerDocument(binding.Body, state);
                Need(owner.ID == binding.Owner.ID, "DOCUMENT_CONTEXT_MISMATCH", "The selected part's actual native owner changed."); var representation = ModelHistoryEditGuard.CurrentRepresentation(binding.Body, state, tx, job.CheckCancellation); var solid = (Db.Solid3d)tx.GetObject(representation, Db.OpenMode.ForRead);
                var actualDisplay = Wire.Element(EntityAdapter.StoredProperties(solid)); var actualMetadata = ModelProfileElementsState358.Metadata(solid, tx, job.CheckCancellation, true);
                bool emptyBody = solid.IsNull, displayChanged = actualDisplay.GetRawText() != binding.Display.GetRawText(), metadataChanged = !Equal(actualMetadata, binding.Metadata, job.CheckCancellation);
                if (emptyBody || displayChanged)
                    throw new DomainException("MODEL_STATE_UNAVAILABLE", emptyBody ? "The retained body is empty." : "The retained body changed display properties.", false,
                        ModelSketchBodyDiagnostics363.Capture(emptyBody, Key(binding.Body.ID), binding.Representation.Handle.ToString(), representation.Handle.ToString(), binding.Display, actualDisplay, binding.Metadata, actualMetadata, displayChanged, metadataChanged, job.CheckCancellation));
                var after = ModelSketchLifecycleReader362.Capture(binding.Body, sketch!, state, tx, job.CheckCancellation); var selected = profile == null ? null : ModelProfileLifecycleState340.Capture(profile, binding.Body, state, tx, job.CheckCancellation, allowCircularHoles: true);
                var contents = ModelInventoryBudget.History(binding.Body, job.CheckCancellation).ToArray();
                var allowed = new HashSet<McObjectId> { sketch!.ID }; AllowSections446(binding, allowed, job.CheckCancellation); allowed.UnionWith(after.Projections.Ids); if (nativeCreated != null) { allowed.UnionWith(nativeCreated.ObjectIds); allowed.UnionWith(nativeCreated.ConstraintIds); allowed.UnionWith(nativeCreated.DimensionIds); allowed.UnionWith(nativeCreated.VariableIds); allowed.UnionWith(nativeCreated.Baseline.NativeIds); }
                // Capture has proved each source through the actual selected
                // projection and its exact resident drawing roundtrip. Retain
                // that current SrcObjectID; another document GUID lookup can
                // lose its native identity when document construction joins a body.
                allowed.UnionWith(ModelSketchLifecycleMembership366.ProjectionSources(after, job.CheckCancellation));
                allowed.UnionWith(after.Projections.NativeReferences);
                if (!sketch.PlanarEntityID.IsNull) allowed.Add(sketch.PlanarEntityID); if (profile != null) allowed.Add(profile.ID); if (feature != null) allowed.Add(feature.ID); allowed.UnionWith(FeatureReferences(binding));
                if (!binding.Contents.All(contents.Contains) || !contents.Except(binding.Contents).All(allowed.Contains))
                    throw new DomainException("MODEL_STATE_UNAVAILABLE", !binding.Contents.All(contents.Contains) ? "An existing native body-history identity disappeared." : "Unexpected native history objects appeared outside the requested lifecycle.", false,
                        ModelSketchLifecycleMembership366.Describe("selected_body_history", binding.Contents, contents, allowed, binding.Body.ID, sketch.ID, profile?.ID, feature?.ID, drawing, owner, state, tx, job.CheckCancellation));
                var construction = ModelProfileDiscovery348.ConstructionIds(owner, job.CheckCancellation);
                if (!binding.Construction.All(construction.Contains) || !construction.Except(binding.Construction).All(allowed.Contains))
                    throw new DomainException("SKETCH_OWNERSHIP_MISMATCH", "Existing document construction disappeared or unrelated construction appeared.", false,
                        ModelSketchLifecycleMembership366.Describe("actual_owner_document_construction", binding.Construction, construction, allowed, binding.Body.ID, sketch.ID, profile?.ID, feature?.ID, drawing, owner, state, tx, job.CheckCancellation));
                if (binding.Edit is ModelSketchCreate362 create)
                {
                    Need(after.Profiles.GetArrayLength() == 0, "SKETCH_OWNERSHIP_MISMATCH", "Sketch-only creation unexpectedly created a profile.");
                    Need(after.Projections.Ids.ToHashSet().SetEquals(projections) && after.Projections.Sources.Select(source => source.id).ToHashSet(StringComparer.OrdinalIgnoreCase).SetEquals(binding.Additions.Select(source => Key(source.Native))), "SKETCH_OWNERSHIP_MISMATCH", "Sketch source/projected identity inventory differs from the request.");
                    Need(after.Projections.NativeReferences.ToHashSet().SetEquals(binding.NativeReferences), "SKETCH_OWNERSHIP_MISMATCH", "The same-part projected references changed identity.");
                    if (create.NativeGeometry == null && binding.NativeReferences.Length == 0) Need(ModelProfileBoundary340.Same(ModelSketchLifecycleReader362.Curves(after, job.CheckCancellation), supportedSourceCurves ?? binding.Additions.Select(source => source.Curve).ToArray(), job.CheckCancellation), "SKETCH_GEOMETRY_MISMATCH", "The actual projected sketch differs from its retained source geometry.");
                    else if (create.NativeGeometry != null) { Need(nativeCreated != null, "SKETCH_BINDING_UNAVAILABLE", "Native initialization receipt is unavailable."); NativeSketchInitialization385.VerifyCreated(sketch, create.NativeGeometry, nativeCreated, state, tx, job.CheckCancellation); }

                    if (binding.Support != null)
                    {
                        Need(supportedFrame != null && create.Support != null, "SUPPORT_DEFINITION_UNAVAILABLE", "The actual supported initialization frame is unavailable.");
                        var currentSupport = ModelSketchSupportState396.Capture(create.Support, binding.Body, representation, owner, state, tx, args.BridgeId, job.CheckCancellation, false);
                        ModelSketchSupportState396.RequireAttached(currentSupport, binding.Body, owner, sketch, job.CheckCancellation); ModelSketchSupportState396.Retained(binding.Support, currentSupport, Key(sketch.ID), state, tx, job.CheckCancellation);
                        Need(Same(after.Plane.Origin, supportedFrame.Origin) && Same(after.Plane.XAxis, supportedFrame.XAxis) && Same(after.Plane.YAxis, supportedFrame.YAxis), "SKETCH_FRAME_UNAVAILABLE", "Native supported sketch frame changed during initialization.");
                        supportReceipt = ModelSketchSupportState396.Describe(currentSupport, binding.Body, owner, after.Plane, sketch);
                    }
                    else { Need(create.Plane != null, "INVALID_ARGUMENT", "The standalone sketch plane is unavailable."); Need(Same(after.Plane.Origin, create.Plane.Origin) && Same(after.Plane.XAxis, create.Plane.XAxis) && Same(after.Plane.YAxis, create.Plane.YAxis), "SKETCH_FRAME_UNAVAILABLE", "Native sketch frame differs from the requested oriented source plane."); }
                }
                else RequireSketch(binding, after, binding.Edit is ModelSketchProfileCreate362 ? profile : null, feature, job.CheckCancellation);
                if (binding.Edit is ModelSketchProfileCreate362 selectedRequest)
                {
                    Need(selected != null && selected.Profile.Dependent3dFeatureID.IsNull && selected.Profile.ParentSketchID == sketch.ID, "PROFILE_GEOMETRY_MISMATCH", "The new native profile did not retain its selected parent or unexpectedly acquired a consumer.");
                    ModelProfileLifecycleState340.RequireRegionPoint(selected.Profile, after.Plane, selectedRequest.RegionPoint, job.CheckCancellation);
                }
                if (feature != null)
                {
                    var dependencies = feature.GetDependsOn(false, false);
                    Need(feature.Status.ToString() == "Ok" && !feature.IsSuppressed && !feature.IsBeyondEndOfPart && feature.HostSolidID == binding.Body.ID && feature.DbEntity.Document != null && feature.DbEntity.Document.ID == owner.ID && contents.Contains(feature.ID) &&
                        selected != null && FeatureProfile(feature) == profile!.ID && profile.HostSolidID == binding.Body.ID && sketch.HostSolidID == binding.Body.ID && contents.Contains(profile.ID) && contents.Contains(sketch.ID) &&
                        after.Projections.Ids.All(id => contents.Contains(id) && id.GetObjectOfType<ProjectionSketchEnt>() is { } projection && projection.HostSolidID == binding.Body.ID) &&
                        dependencies != null && dependencies.Contains(profile.ID) && FeatureReferences(binding).All(dependencies.Contains) && feature.OrderObjBefore == binding.Views.LastModelItem,
                        "PROFILE_OWNERSHIP_MISMATCH", "Native feature/profile/sketch/reference composition, ownership, predecessor or forward dependencies differ from the request.");
                    if (feature is ExtrudeAlongPathFeature swept && binding.Edit is ModelProfileSweep362 sweep)
                    {
                        var path = swept.GetPath(); Need(path != null && path.Select(item => item.ID).SequenceEqual(binding.Paths) && swept.PartOperation == (sweep.PartOperation == "cut" ? PartFeatureOperation.Cut : PartFeatureOperation.Join) &&
                            swept.ProfileOrientation == (sweep.Orientation == "normal_to_path" ? SweepProfileOrientation.NormalToPath : SweepProfileOrientation.ParallelToOriginalProfile) && ModelParameterSnapshot.Near(swept.Angle, sweep.TaperAngle) && !swept.TaperAngleParamVarID.IsNull,
                            "PROFILE_OWNERSHIP_MISMATCH", "Native sweep path, operation, orientation or taper differs from the request.");
                    }
                    else if (feature is RevolveFeature revolved && binding.Edit is ModelProfileRevolution411 revolution)
                        ModelProfileRevolutionNative411.Verify(revolved, profile, binding.Axis!.Native, revolution, job.CheckCancellation);
                    else if (feature is LoftFeature loftFeature && binding.Edit is ModelProfileLoft446 loftRequest)
                    {
                        ModelProfileLoftNative446.Verify(loftFeature, binding.Sections446.Select(s => s.Profile.Profile).ToArray(), loftRequest, binding.LoftRails446, job.CheckCancellation);
                        consumedReceipt446 = VerifySections446(binding, loftFeature, selected!, after, contents, state, tx, job.CheckCancellation);
                    }
                    else throw new DomainException("PROFILE_OWNERSHIP_MISMATCH", "The returned typed native feature differs from the selected operation.");
                    Need(binding.Consumers != null, "PROFILE_OWNERSHIP_MISMATCH", "The profile's actual consumer baseline is unavailable.");
                    ModelProfileConsumers410.VerifyRetained(binding.Consumers, profile!, owner, job.CheckCancellation, addedConsumer: feature.ID);
                    bool acquiredProfile = binding.Profile!.Stored.GetProperty("profile_host_solid_id").ValueKind == JsonValueKind.Null; RequireProfileDesign(binding.Profile.Stored, selected.Stored, binding.Sketch!.Stored.GetProperty("host_solid_id").ValueKind == JsonValueKind.Null, acquiredProfile, true, job.CheckCancellation);
                }
                // The returned feature is now proved through actual typed
                // owner/composition/profile/path relationships. Admit only its
                // exact new resident storage and new private owned descendants.
                // Existing body records, root XData/reactors and sibling data
                // stay exact; an unproved storage map keeps the original guard.
                ModelSketchBodyStorage364.Proof[] storageProofs = feature switch
                {
                    null => [],
                    ExtrudeAlongPathFeature swept => [ModelSketchBodyStorageNative364.Bind(swept, binding.Body, owner, drawing, representation, binding.Construction, binding.Contents, state, tx, job.CheckCancellation)],
                    RevolveFeature revolved => [ModelSketchBodyStorageNative364.Bind(revolved, binding.Body, owner, drawing, representation, binding.Construction, binding.Contents, state, tx, job.CheckCancellation)],
                    LoftFeature lofted => [ModelSketchBodyStorageNative364.Bind(lofted, binding.Body, owner, drawing, representation, binding.Construction, binding.Contents, state, tx, job.CheckCancellation)],
                    _ => throw new DomainException("PROFILE_OWNERSHIP_MISMATCH", "Unsupported returned feature storage type.")
                };
                string[] newReactorTargets = [];
                if (nativeCreated != null || binding.Support != null)
                {
                    var ids = projections.Append(sketch.ID).Concat(nativeCreated == null ? [] : nativeCreated.ObjectIds.Concat(nativeCreated.ConstraintIds).Concat(nativeCreated.DimensionIds).Concat(nativeCreated.VariableIds).Concat(nativeCreated.Baseline.NativeIds));
                    ids = ids.Where(id => !binding.Construction.Contains(id) && !binding.Contents.Contains(id));
                    var proofs = ModelSketchConstraintMetadata379.Bind(ids, representation, owner, drawing, state, tx, job.CheckCancellation);
                    storageProofs = proofs.Select(p => new ModelSketchBodyStorage364.Proof(p.native_id, p.owner_document_id, p.raw_handle, p.owner_path, p.proved, p.reason)).ToArray();
                    // Bind sets raw_handle only after exact native-ID roundtrip
                    // in this document. Reactor targets may be document-owned;
                    // they do not grant a body-owned payload/storage waiver.
                    newReactorTargets = proofs.Where(p => p.raw_handle != null).Select(p => p.raw_handle!).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
                }
                if (!ModelSketchBodyStorage364.Retained(binding.Metadata, actualMetadata, storageProofs, job.CheckCancellation, out string storageReason, out _, newReactorTargets))
                    throw new DomainException("MODEL_STATE_UNAVAILABLE", "The retained body changed readable owned metadata outside proved new native consumer storage.", false,
                        ModelSketchBodyStorage364.Describe(binding.Metadata, actualMetadata, storageProofs, storageReason,
                            ModelSketchBodyDiagnostics363.Capture(emptyBody, Key(binding.Body.ID), binding.Representation.Handle.ToString(), representation.Handle.ToString(), binding.Display, actualDisplay, binding.Metadata, actualMetadata, displayChanged, metadataChanged, job.CheckCancellation)));
                Need(binding.Support != null ? binding.Body.LastPartItemID == binding.Views.LastHistoryItem || binding.Body.LastPartItemID == sketch.ID :
                    binding.Body.LastPartItemID == (feature == null || binding.Views.ViewIds.Length > 0 ? binding.Views.LastHistoryItem : feature.ID), "MODEL_STATE_UNAVAILABLE", "Native terminal history differs from the requested lifecycle insertion.");
                if (feature != null && binding.Views.HasFollowers) ModelViewFollowers.VerifyAfterInsertion(binding.Body, binding.Views, feature.ID, job.CheckCancellation, refreshStandalone: true);
                else if (binding.Support != null)
                {
                    if (sketch.HostSolidID == binding.Body.ID)
                        ModelViewFollowers.VerifyConstruction(binding.Body, binding.Views, sketch.ID, job.CheckCancellation, refreshStandalone: true);
                    else
                    {
                        // A supported native sketch can remain same-document
                        // construction until its profile is consumed. It has
                        // not extended the body's history or terminal item.
                        Need(sketch.HostSolidID.IsNull && sketch.DbEntity.Document?.ID == owner.ID && !contents.Contains(sketch.ID) && construction.Contains(sketch.ID) &&
                            binding.Body.LastPartItemID == binding.Views.LastHistoryItem,
                            "SKETCH_OWNERSHIP_MISMATCH", "Supported document construction must retain its actual owner, null native host, separate construction membership and unchanged body terminal.");
                        ModelViewFollowers.Verify(binding.Body, binding.Views, job.CheckCancellation, refreshStandalone: true);
                    }
                }
                else ModelViewFollowers.Verify(binding.Body, binding.Views, job.CheckCancellation, refreshStandalone: true);
                var inventory = Inventory(state, tx, drawing, owner, allowed.ToArray(), job.CheckCancellation); var newOwned = allowed.Except(binding.Construction).Select(Key).ToHashSet(StringComparer.OrdinalIgnoreCase);
                if (!ModelProfileDrawingMembership360.Retained(binding.Inventory, inventory, binding.Representation.Handle.ToString(), representation.Handle.ToString(), new HashSet<string>(), newOwned, job.CheckCancellation, out string inventoryReason))
                    throw new DomainException("OBJECT_IDENTITY_MISMATCH", "Drawing membership changed outside actual operation-owned construction/body replacement.", false, ModelProfileDrawingMembership360.Describe(binding.Inventory, inventory, binding.Representation.Handle.ToString(), representation.Handle.ToString(), inventoryReason, job.CheckCancellation));
                var sources = Sources(binding.SourceHandles, state, tx, drawing, owner, sketch, after.Projections.Ids, job.CheckCancellation); var links = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase);
                foreach (var source in after.Projections.Sources) { var added = source.projection_ids.Where(id => projections.Select(Key).Contains(id)).ToArray(); if (added.Length > 0) links[source.id] = added; }
                if (feature != null) foreach (var native in FeatureReferences(binding)) { string key = Key(native); links[key] = (links.TryGetValue(key, out var existing) ? existing : []).Append(Key(feature.ID)).ToArray(); }
                if (!ModelProfileSourcePreservation360.Retained(binding.Sources, sources, Key(sketch.ID), new Dictionary<string, string[]>(), links, binding.Sketch?.Projections.Sources.Select(source => source.id).ToHashSet(StringComparer.OrdinalIgnoreCase) ?? new HashSet<string>(), after.Projections.Sources.Select(source => source.id).ToHashSet(StringComparer.OrdinalIgnoreCase), (a, b) => Equal(a, b, job.CheckCancellation), job.CheckCancellation, out string reason, out var oldSources, out var newSources))
                    throw new DomainException("PROFILE_SOURCE_CHANGED", "A retained source changed outside proved operation-owned native projection/path associations.", false, ModelProfileSourcePreservation360.Describe(binding.Sources, sources, oldSources, newSources, reason, job.CheckCancellation));
                var parameters = ModelParameterSnapshot.Capture(new(args.BridgeId, args.DocumentId, args.ExpectedRevision, args.OperationId, args.Units, representation.Handle.ToString(), "", "", 0), state, job.CheckCancellation); ModelParameterSnapshot.Healthy(parameters); RequireParameters(binding, parameters, sketch, profile, feature, job.CheckCancellation);
                if (feature == null) Need(Equal(binding.Parameters.GetProperty("native"), parameters.GetProperty("native"), job.CheckCancellation), "MODEL_STATE_UNAVAILABLE", "Construction-only lifecycle changed existing physical body geometry.");
                var oldVariables = binding.Parameters.GetProperty("variables").EnumerateArray().Select(value => value.GetProperty("mc_id").GetString()!).ToHashSet(StringComparer.OrdinalIgnoreCase);
                var newDriverOwners = projections.Select(Key).ToHashSet(StringComparer.OrdinalIgnoreCase);
                if (binding.Edit is ModelSketchCreate362) newDriverOwners.Add(Key(sketch.ID));
                if (nativeCreated != null) newDriverOwners.UnionWith(nativeCreated.ObjectIds.Concat(nativeCreated.ConstraintIds).Concat(nativeCreated.DimensionIds).Select(Key));
                if (binding.Edit is ModelSketchProfileCreate362) newDriverOwners.Add(Key(profile!.ID));
                if (feature != null) newDriverOwners.Add(Key(feature.ID));
                var returnedNativeVariables = nativeCreated?.VariableIds.Select(Key).ToHashSet(StringComparer.OrdinalIgnoreCase);
                foreach (var variable in parameters.GetProperty("variables").EnumerateArray())
                { if (oldVariables.Contains(variable.GetProperty("mc_id").GetString()!)) continue; Need((returnedNativeVariables == null || returnedNativeVariables.Contains(variable.GetProperty("mc_id").GetString()!)) && variable.GetProperty("host_object_id").GetString() is { } variableHost && newDriverOwners.Contains(variableHost), "MODEL_STATE_UNAVAILABLE", "A new variable lacks its actual returned dimensional binding or newly created native construction/feature host."); }
                if (feature is ExtrudeAlongPathFeature sweptFeature) { var variable = ModelParameterSnapshot.Variable(parameters, Key(sweptFeature.TaperAngleParamVarID)); Need(variable.GetProperty("host_object_id").GetString() == Key(feature.ID) && variable.GetProperty("value_available").GetBoolean() && ModelParameterSnapshot.Near(variable.GetProperty("value").GetDouble(), ((ModelProfileSweep362)binding.Edit).TaperAngle * 180 / Math.PI), "MODEL_STATE_UNAVAILABLE", "Sweep taper variable has the wrong actual owner or degree value."); }
                else if (feature is RevolveFeature revolvedFeature) ModelProfileRevolutionNative411.VerifyVariable(revolvedFeature, (ModelProfileRevolution411)binding.Edit, parameters, job.CheckCancellation);
                return (new(args.BridgeId, args.DocumentId, Validation.NormalizeHandle(representation.Handle.ToString())), after, selected);
            }
            var verified = Verify(); Trace("native_lifecycle_verified"); job.CheckCancellation(); job.BeginCommit(); Close(); verified = Verify();
            var source = new EntityRef(args.BridgeId, args.DocumentId, binding.Edit.Handles.Single()); bool replaced = source.handle != verified.Body.handle;
            var mapping = new ModelSketchChange362(0, binding.Edit.Operation, source, verified.Body, Key(binding.Body.ID), Key(binding.Owner.ID), Key(sketch!.ID), verified.Sketch.Token, Key(binding.Body.LastPartItemID),
                verified.Sketch.Projections.Sources.Select(value => new EntityRef(args.BridgeId, args.DocumentId, value.handle)).ToArray(), replaced, profile == null ? null : Key(profile.ID), verified.Profile?.Token, feature == null ? null : Key(feature.ID), verified.Profile == null ? null : ModelProfileDiscovery348.Ownership(verified.Profile), nativeCreated?.Objects, nativeCreated?.Changes, supportReceipt,
                angle_variable_id: feature is RevolveFeature revolvedResult ? Key(revolvedResult.RotationAngleParamVarID) : null, angle_degrees: binding.Edit is ModelProfileRevolution411 revolutionResult ? revolutionResult.AngleDegrees : null,
                axis_ref: binding.Axis == null ? null : new(args.BridgeId, args.DocumentId, Validation.NormalizeHandle(binding.Axis.Id.Handle.ToString())), consumed_profiles: consumedReceipt446);
            var data = new ModelSketchOutput362(args.OperationId, "committed", false, [verified.Body], replaced ? [source] : [], replaced ? [verified.Body] : [], [], "not_guaranteed", [mapping]);
            var warnings = new List<string> { WriteRecovery.Warning, "MODEL_SKETCH_LIFECYCLE: Native sketch/profile IDs and actual ownership are retained. Refresh sketch_ids/profile_ids, body history and variables. Profile sharing preserves all observed forward consumers; sharing is not an implicit transfer. Inspect the engineering result after the meaningful group." };
            if (nativeCreated?.Baseline is { Complete: false } unobserved) warnings.Add("SKETCH_BASELINE_UNOBSERVED: Geometry-only inputs were retained; the native relation baseline was unavailable, not observed empty or preserved. " + unobserved.UnavailableReason);
            if (nativeCreated?.Baseline.PreAddUnavailableReason != null) warnings.Add("SKETCH_NETWORK_MATERIALIZED: The pre-add relation inventory was unavailable. The fresh network was observed after the first requested relation and before solving; preservation of an unobserved pre-add network is not claimed.");
            if (!verified.Sketch.Constraints.GetProperty("constraints_complete").GetBoolean()) warnings.Add(ModelSketchConstraintObservation353.Warning);
            var result = ModelViewFollowers.WithWarnings(Result.Ok(job.OriginalRequestId, data, context, warnings.ToArray()), binding.Views); Need(JsonSerializer.SerializeToUtf8Bytes(result, Wire.Json).Length <= Wire.PayloadBudget, "RESPONSE_LIMIT_EXCEEDED", "The complete lifecycle result exceeds actual transport bytes."); job.RecordCommit(result); Trace("commit_recorded"); return result;
        }
        catch (Exception error)
        {
            if (job.CommittedResult is { } committed) return committed; Exception? cleanup = null; try { Close(); } catch (Exception value) { cleanup = value; }
            bool unknown = started || cleanup != null; string cause = error is DomainException domain ? domain.Error.code : error is OperationCanceledException ? "CANCELLED" : "MODEL_SKETCH_LIFECYCLE_FAILED";
            return job.Error(unknown ? "WRITE_OUTCOME_UNKNOWN" : cause, unknown ? "Native sketch/profile/forming lifecycle started but remains unverified. Partial construction or feature history may remain; inspect actual state before another dependent write." : "Native sketch lifecycle did not start.", unknown ? "unknown" : "not_started", context,
                ModelSketchLifecycleDiagnostics362.Capture(cause, stage, error.Message, binding == null ? null : Key(binding.Body.ID), sketch == null ? null : Key(sketch.ID), profile == null ? null : Key(profile.ID), feature == null ? null : Key(feature.ID), projections.Select(Key).ToArray(), new { observed_native_input_ids = observedNativeInputs.Select(Key).ToArray(), verification_details = (error as DomainException)?.Error.details }, cleanup?.GetType().Name)) with
            { warnings = [WriteRecovery.Warning] };
        }
    }
}
