using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using System.Text.Json.Nodes;
using Multicad;
using Multicad.DatabaseServices;
using Multicad.DatabaseServices.Constraints;
using Multicad.Mc3D;
using NanoCAD.Contracts;
using G = Multicad.Geometry;

namespace NanoCADBridge;

// Exact caller-selected native Hole control, not an inferred shape-slot ID.
// SDK 3DCore.h:6277,9421,9461 categories and9740-9870 native Hole interface.
// Effects stay local to one verified write; no ID/name/value alias is retained.
internal static class ModelHoleNativeControl
{
    internal const string Role = "hole_native_scalar";
    internal const string Unit = "native_variable_units";
    internal const string Scope = "actual_native_Hole_control; operation_local_effect_only; no_shape_slot_alias_or_unit_conversion";
    private static readonly HashSet<string> ShapeNumbers = new(StringComparer.Ordinal)
    { "diameter", "depth", "seat_diameter", "seat_depth", "seat_angle_degrees", "bottom_angle_degrees" };

    internal sealed record Snapshot(McObjectId Body, McObjectId Feature, McObjectId Variable, McObjectId Document,
        uint Category, int Extent, double Value, JsonElement Modes, McGeomParam[]? References);

    private static void Need([DoesNotReturnIf(false)] bool condition, string code, string message)
    { if (!condition) throw new DomainException(code, message); }

    internal sealed record Binding(McObjectId Variable, double Value, uint Category, int Extent)
    { internal string Role => ModelHoleNativeControl.Role; internal string Unit => ModelHoleNativeControl.Unit; }

    private static bool SupportedMode(HoleFeature hole)
    {
        if (hole.HoleType != HoleFeatureType.Simple || hole.IsSuppressed || hole.Status.ToString() != "Ok") return false;
        using var placement = hole.GetPlacement();
        return placement != null && placement.DefType == IppDefinitionType.Fixed;
    }

    internal static Binding[] Observe(HoleFeature hole, Action? cancel = null, Action<McObjectId, DomainException>? unavailable = null)
    {
        try
        {
            cancel?.Invoke(); if (!SupportedMode(hole)) return [];
            var body = hole.HostSolidID.GetObjectOfType<Mc3dSolid>();
            Need(body != null, "OBJECT_IDENTITY_MISMATCH", "The native Hole owner body is unavailable.");
            var document = body!.DbEntity.Document;
            Need(document != null, "VARIABLE_SCOPE_MISMATCH", "The native Hole owner document is unavailable.");
            using var manager = document!.GetDocumentVariablesManager(Constraint2dNetType.MCS);
            Need(manager != null && manager.DocumentId == document.ID && manager.ConstraintsNetworkType == Constraint2dNetType.MCS,
                "VARIABLE_SCOPE_MISMATCH", "The native Hole owner MCS network is unavailable.");
            var ids = manager!.GetAllVariables();
            Need(ids != null, "VARIABLES_UNAVAILABLE", "The native Hole variable inventory is unavailable.");
            ModelInventoryBudget.Ids(ids!, cancel); var bindings = new List<Binding>();
            foreach (var variable in ids!)
            {
                cancel?.Invoke(); if (manager.GetVariableHostObjectId(variable) != hole.ID) continue;
                var info = manager.GetVariableInfo(variable);
                if (info == null || !info.Type.ToString().Split(',', StringSplitOptions.TrimEntries).Contains("Model3D")) continue;
                try
                {
                    var snapshot = Capture(body, hole, variable, cancel);
                    bindings.Add(new(variable, snapshot.Value, snapshot.Category, snapshot.Extent));
                }
                catch (OperationCanceledException) { throw; }
                catch (Exception error) when (error is DomainException or NotSupportedException or NotImplementedException or Teigha.Runtime.Exception)
                { unavailable?.Invoke(variable, ReadFailure(error)); }
            }
            return bindings.ToArray();
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception error) when (error is NotSupportedException or NotImplementedException or Teigha.Runtime.Exception)
        { throw ReadFailure(error); }
    }

    private static DomainException ReadFailure(Exception error) => error is DomainException domain ? domain :
        new("VARIABLE_BINDING_UNAVAILABLE", "The native Hole control observation is unavailable: " + error.GetType().Name + ".");

    internal static object Observation(Binding binding) => new
    { native_value_category = binding.Category, native_extent = binding.Extent, value_unit = Unit, scope = Scope };

    private static Snapshot Capture(Mc3dSolid body, HoleFeature hole, McObjectId variable, Action? cancel)
    {
        cancel?.Invoke();
        var document = body.DbEntity.Document;
        var active = McDocument.ActiveDocument;
        var host = HostMgd.ApplicationServices.Application.DocumentManager.MdiActiveDocument;
        Need(document != null && active != null && host != null && host.IsNamedDrawing && !string.IsNullOrWhiteSpace(active.Path) &&
            string.Equals(Path.GetFullPath(active.Path), Path.GetFullPath(host.Name), StringComparison.OrdinalIgnoreCase) &&
            document.TopDoc != null && active.TopDoc != null && document.TopDoc.ID == active.TopDoc.ID,
            "DOCUMENT_CONTEXT_MISMATCH", "The native Hole owner must belong to the exact active named drawing.");
        Need(document != null && hole.ID != McObjectId.Null && !variable.IsNull && hole.HostSolidID == body.ID &&
            hole.DbEntity.Document?.ID == document.ID && ModelInventoryBudget.History(body, cancel).Contains(hole.ID),
            "OBJECT_IDENTITY_MISMATCH", "The selected native Hole/control has a different real body/document owner.");
        Need(hole.HoleType == HoleFeatureType.Simple && !hole.IsSuppressed && hole.Status.ToString() == "Ok",
            "OUT_OF_SCOPE", "Native scalar control editing requires a healthy active Simple Hole.");
        using var placement = hole.GetPlacement();
        Need(placement != null && placement.DefType == IppDefinitionType.Fixed,
            "OUT_OF_SCOPE", "Native scalar control editing preserves fixed placement; other placement roles keep their existing typed adapters.");
        using var manager = document!.GetDocumentVariablesManager(Constraint2dNetType.MCS);
        Need(manager != null && manager.DocumentId == document.ID && manager.ConstraintsNetworkType == Constraint2dNetType.MCS,
            "VARIABLE_SCOPE_MISMATCH", "The actual owning native MCS network is unavailable.");
        var ids = manager!.GetAllVariables();
        Need(ids != null && ids.Contains(variable) && manager.GetVariableHostObjectId(variable) == hole.ID,
            "VARIABLE_SCOPE_MISMATCH", "Select an actual existing native variable owned by this exact Hole feature.");
        ModelInventoryBudget.Ids(ids!, cancel);
        var info = manager.GetVariableInfo(variable);
        var flags = info?.Type.ToString().Split(',', StringSplitOptions.TrimEntries).ToHashSet(StringComparer.Ordinal);
        Need(info != null && flags != null && flags.Contains("Model3D") && flags.All(flag => flag is "Model3D" or "Reference"),
            "CAPABILITY_UNAVAILABLE", "The selected existing control must retain native Model3D ownership; unrelated variable families are protected.");
        var consumers = manager.GetDependentOn3dObjects(variable);
        Need(consumers != null && consumers.Contains(hole.ID), "VARIABLE_BINDING_UNAVAILABLE",
            "The existing native variable does not report the actual Hole as a direct consumer.");
        Need(manager.GetVariableValue(variable, out double current) && double.IsFinite(current),
            "VARIABLE_VALUE_UNAVAILABLE", "The exact native control has no finite readable value.");
        int status = NativeModelHistory421.LoadHoleControlState()(document.ID.ToString(), body.ID.ToString(),
            hole.ID.ToString(), variable.ToString(), out uint category, out int extent);
        Need(status == 0 && category <= 6 && extent is >= 0 and <= 3, "VARIABLE_BINDING_UNAVAILABLE",
            "The public native feature API did not recognize this exact variable as a linked control with a declared SDK value category.");
        var frame = new G.Matrix3d();
        Need(placement!.GetPosition(out frame, 0).IsSuccess(), "MODEL_STATE_UNAVAILABLE", "The native fixed Hole placement frame is unavailable.");
        var references = placement.GetAssocParams()?.Select(reference => (McGeomParam)reference.Clone()).ToArray();
        var modes = Wire.Element(new
        {
            feature_id = hole.ID.ToString(),
            host_solid_id = body.ID.ToString(),
            document_id = document.ID.ToString(),
            hole_type = hole.HoleType.ToString(),
            hole.IsFullDepth,
            seat = hole.SeatType.ToString(),
            hole.FlatBottom,
            face_id = hole.FaceId.ToString(),
            terminal_face_id = hole.TerminalFaceId.ToString(),
            thread_id = hole.GetThread()?.ID.ToString(),
            native_extent = extent,
            placement = new
            {
                kind = placement.DefType.ToString(),
                support = placement.TargetPlaneId.ToString(),
                limit = placement.LimitPlaneId.ToString(),
                first_part = placement.FirstPartID.ToString(),
                parts = placement.PartsIDs?.Select(id => id.ToString()).ToArray(),
                user_parts = placement.GetUserPartIds()?.Select(id => id.ToString()).ToArray(),
                placement.Param1,
                placement.Param2,
                placement.Sector,
                placement.NumPositionsDim1,
                placement.NumPositionsDim2,
                fixed_frame = frame.ToArray()
            }
        });
        return new(body.ID, hole.ID, variable, document.ID, category, extent, current, modes, references);
    }

    internal static ModelFeatureVariable? Resolve(Mc3dSolid body, HoleFeature hole, McObjectId variable, Action? cancel)
    {
        if (!SupportedMode(hole)) return null;
        var snapshot = Capture(body, hole, variable, cancel);
        // IsAngle remains false because this opaque route does no angular-unit
        // conversion. Category is disclosed separately; all values remain in
        // the native variable's units, not an inferred degree/mm convention.
        return new ModelFeatureVariable(hole.ID, variable, false, Role: Role) { NativeHoleSnapshot = snapshot };
    }

    internal static double Value(McObjectId feature, McObjectId variable)
    {
        var hole = feature.GetObjectOfType<HoleFeature>();
        Need(hole != null && hole.ID == feature, "OBJECT_IDENTITY_MISMATCH", "The exact native Hole control owner disappeared.");
        var body = hole!.HostSolidID.GetObjectOfType<Mc3dSolid>();
        Need(body != null, "OBJECT_IDENTITY_MISMATCH", "The native Hole host body disappeared.");
        return Capture(body!, hole, variable, null).Value;
    }

    internal static void Validate(ModelFeatureVariable control, double requested)
    {
        Need(control.NativeHoleSnapshot is { }, "VARIABLE_BINDING_UNAVAILABLE", "No exact native Hole control snapshot exists.");
        var proof = control.NativeHoleSnapshot!;
        var hole = proof.Feature.GetObjectOfType<HoleFeature>();
        var body = proof.Body.GetObjectOfType<Mc3dSolid>();
        Need(hole != null && body != null, "OBJECT_IDENTITY_MISMATCH", "The witnessed native Hole/body is unavailable.");
        var current = Capture(body!, hole!, proof.Variable, null);
        Need(current.Category == proof.Category && current.Extent == proof.Extent && double.IsFinite(requested),
            "MODEL_STATE_UNAVAILABLE", "The native Hole control category or extent changed before mutation.");
        if (current.Category == 3)
            Need(requested == Math.Truncate(requested), "INVALID_GEOMETRY", "The native SDK count category requires an integer.");
        Need(hole!.IsValidVarValForObject(proof.Variable, requested), "INVALID_GEOMETRY",
            "The exact native owning Hole rejected the requested value for this real linked variable.");
    }

    private static JsonElement Node(JsonElement snapshot, McObjectId feature) => snapshot.GetProperty("nodes").EnumerateArray()
        .Single(node => string.Equals(node.GetProperty("mc_id").GetString(), feature.ToString(), StringComparison.OrdinalIgnoreCase));

    internal static ModelFeatureVariable Witness(ModelFeatureVariable control, JsonElement before, JsonElement after,
        double expectedValue, Action cancel)
    {
        Need(control.Role == Role && control.NativeHoleSnapshot is { }, "VARIABLE_BINDING_UNAVAILABLE", "No original native Hole control proof exists.");
        var original = control.NativeHoleSnapshot!;
        var hole = original.Feature.GetObjectOfType<HoleFeature>();
        var body = original.Body.GetObjectOfType<Mc3dSolid>();
        Need(hole != null && body != null, "OBJECT_IDENTITY_MISMATCH", "The native Hole/body identity changed during expression evaluation.");
        var current = Capture(body!, hole!, original.Variable, cancel);
        Need(current.Category == original.Category && current.Extent == original.Extent &&
            ModelParameterSnapshot.Equivalent(original.Modes, current.Modes) &&
            (original.References == null && current.References == null || original.References != null && current.References != null &&
                original.References.Length == current.References.Length && original.References.Zip(current.References).All(pair => pair.First.Equals(pair.Second))),
            "MODEL_STATE_UNAVAILABLE", "Native expression evaluation changed Hole mode, extent, fixed placement, support/thread IDs or original associative references.");
        Need(ModelParameterSnapshot.Near(current.Value, expectedValue), "MODEL_UPDATE_FAILED", "The actual native Hole variable did not reach its planned value.");
        var prior = Node(before, original.Feature).GetProperty("parameters");
        var actual = Node(after, original.Feature).GetProperty("parameters");
        Need(prior.GetProperty("kind").GetString() == "hole" && actual.GetProperty("kind").GetString() == "hole",
            "OBJECT_IDENTITY_MISMATCH", "The actual owning history item stopped being the same native Hole.");
        var observations = new List<HoleNativeShapeChange>();
        Need(prior.EnumerateObject().Select(field => field.Name).ToHashSet(StringComparer.Ordinal)
            .SetEquals(actual.EnumerateObject().Select(field => field.Name)), "MODEL_STATE_UNAVAILABLE", "The Hole parameter observation schema changed.");
        foreach (var field in prior.EnumerateObject())
        {
            cancel(); var value = actual.GetProperty(field.Name);
            if (ModelParameterSnapshot.Equivalent(field.Value, value)) continue;
            Need(ShapeNumbers.Contains(field.Name) && field.Value.ValueKind == JsonValueKind.Number && value.ValueKind == JsonValueKind.Number &&
                double.IsFinite(field.Value.GetDouble()) && double.IsFinite(value.GetDouble()),
                "MODEL_STATE_UNAVAILABLE", "A nonnumeric Hole mode, relationship or stored design field changed.");
            observations.Add(new(field.Name, field.Value.GetDouble(), value.GetDouble()));
        }
        Need(ModelParameterSnapshot.Near(original.Value, expectedValue) || observations.Count > 0,
            "MODEL_UPDATE_FAILED", "The native value changed without an observed Hole shape parameter change; no geometry-control claim is made.");
        // This is a single operation's observed effect, not a saved ID-to-slot
        // alias. Existing graph protection still checks every unmasked field.
        return control with { NativeHoleEffect = new(current.Category, current.Extent, Unit, observations.ToArray(), Scope) };
    }

    internal static void RemoveWitnessed(JsonObject parameters, ModelFeatureVariable control)
    {
        // Without a parameter-write witness mask nothing: naming/lifecycle
        // verifiers must compare the complete original Hole parameters.
        if (control.NativeHoleEffect is not { } effect) return;
        Need(effect.value_unit == Unit && control.NativeHoleSnapshot is { } proof &&
            effect.native_value_category == proof.Category && effect.native_extent == proof.Extent,
            "VARIABLE_BINDING_UNAVAILABLE", "The witnessed native Hole effect has a different source category or extent.");
        foreach (var change in effect.shape_changes)
        {
            Need(ShapeNumbers.Contains(change.field) && parameters[change.field] is JsonValue,
                "MODEL_STATE_UNAVAILABLE", "Only witnessed numeric Hole observations can vary.");
            double actual = parameters[change.field]!.GetValue<double>();
            Need(double.IsFinite(actual) && (ModelParameterSnapshot.Near(actual, change.previous_value) || ModelParameterSnapshot.Near(actual, change.value)),
                "MODEL_STATE_UNAVAILABLE", "A Hole observation differs from both exact witnessed states.");
            parameters.Remove(change.field);
        }
    }
}
