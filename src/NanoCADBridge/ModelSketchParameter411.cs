using System.Text.Json;
using Multicad;
using Multicad.DatabaseServices.Constraints;
using NanoCAD.Contracts;
using Db = Teigha.DatabaseServices;

namespace NanoCADBridge;

// A dimension is bound through its real scoped relation and VarId. A variable
// name, non-angular flag, or body membership alone is never a writable binding.
internal static class ModelSketchParameter411
{
    internal sealed record Control(NativeSketchConstraintState385.Snapshot State, McObjectId Constraint, McObjectId Variable,
        string Kind, double Value, string Expression, SketchConstraintSpec Spec)
    {
        internal McObjectId ID => State.Sketch.Sketch.ID;
        internal void ValidateRequested(double value) => Need(double.IsFinite(value) && value > 0, "INVALID_GEOMETRY", "A native linear/radial driver requires a positive finite millimeter value.");
    }
    static void Need([System.Diagnostics.CodeAnalysis.DoesNotReturnIf(false)] bool ok, string code, string message) => NativeSketchMembers385.Need(ok, code, message);
    internal static ModifyArguments Context(ModelParameterArguments args) => new(args.BridgeId, args.DocumentId, args.ExpectedRevision, args.OperationId, args.Units, []);
    internal static NativeSketchConstraintState385.Snapshot Capture(ModelParameterArguments args, ModelParameterBody body, McObjectId sketch,
        DocumentState state, Db.Transaction tx, Action cancel)
    {
        var captured = NativeSketchConstraintState385.Capture(body.Handle, sketch.ToString(), Context(args), state, tx, cancel);
        DrawingViewSketchWriteScope282.RequireSupported(captured.Sketch.Sketch); return captured;
    }
    internal static Control Resolve(NativeSketchConstraintState385.Snapshot state, McObjectId variable, Action cancel)
    {
        var sketch = state.Sketch.Sketch; var body = state.Part.Body; var owner = body.DbEntity.Document;
        Need(sketch.HostSolidID == body.ID && state.Part.Contents.Contains(sketch.ID) && sketch.DbEntity.Document.ID == owner.ID,
            "OUT_OF_SCOPE", "A writable internal dimension must belong to the selected part's exact sketch and MCS document.");
        using var manager = NativeSketchMembers385.Manager(sketch); var inventory = manager.GetAllConstraints(); Need(inventory != null, "CONSTRAINTS_UNAVAILABLE", "The exact internal relation inventory is unavailable."); ModelInventoryBudget.Ids(inventory, cancel);
        var matches = new List<McObjectId>();
        foreach (var id in inventory) { cancel(); using var row = manager.GetConstraintInfo(id); Need(row != null, "CONSTRAINTS_UNAVAILABLE", "A current native relation is unreadable."); if (row.VarId == variable) matches.Add(id); }
        Need(!variable.IsNull && matches.Count == 1, "PARAMETER_HAS_SHARED_RELATIONS", "The exact variable must bind one current dimensional relation."); var relation = matches[0]; using var info = manager.GetConstraintInfo(relation); Need(info != null, "CONSTRAINTS_UNAVAILABLE", "The exact dimensional relation is unavailable.");
        var binding = NativeSketchDimensionBinding411.Prove(manager, relation, info, state.Inputs, cancel);
        Need(binding.Kind != "angle", "OUT_OF_SCOPE", "This parameter graph adapter supports millimeter linear, radius and diameter controls; native angular units are not converted.");
        // NaN is documented as a calculated/fixed initial size. It is not
        // silently reclassified as an editable driving parameter.
        Need(double.IsFinite(info.Param) && info.Param > 0, "REFERENCE_DIMENSION_READ_ONLY", "A calculated/reference dimension has no proved positive driving descriptor.");
        var parameter = manager.GetConstraintParamId(relation); double parameterValue = manager.GetConstraintParamValue(relation);
        Need(parameter.IsNull || parameter == variable, "CONSTRAINT_PARAMETER_UNAVAILABLE", "The actual parameter getter binds a different variable.");
        using var variables = owner.GetDocumentVariablesManager(Constraint2dNetType.MCS);
        Need(variables != null && variables.DocumentId == owner.ID && variables.ConstraintsNetworkType == Constraint2dNetType.MCS, "CONSTRAINT_SCOPE_MISMATCH", "The exact owner MCS variable manager is unavailable.");
        var ids = variables.GetAllVariables(); Need(ids != null && ids.Count(id => id == variable) == 1, "VARIABLES_UNAVAILABLE", "The exact dimension variable is not unique in the owner MCS inventory."); ModelInventoryBudget.Ids(ids, cancel);
        double value = double.NaN; var actual = variables.GetVariableInfo(variable); Need(actual != null && variables.GetVariableHostObjectId(variable) == sketch.ID && !variables.IsAngularValue(variable) && variables.GetVariableValue(variable, out value) && double.IsFinite(value) && value > 0,
            "CONSTRAINT_PARAMETER_UNAVAILABLE", "The exact non-angular sketch-owned dimension variable/value is unavailable.");
        var flags = actual.Type.ToString().Split(',', StringSplitOptions.TrimEntries).ToHashSet(StringComparer.Ordinal);
        Need(flags.Contains("Dim2DConstraint") && flags.All(flag => flag is "Dim2DConstraint" or "Reference"), "OUT_OF_SCOPE", "The bound variable has an unsupported dimensional type/flags.");
        var expression = ModelArithmetic.ParseStored(actual.Expression, cancel);
        Need(!flags.Contains("Reference") || expression.References.Length > 0, "REFERENCE_DIMENSION_READ_ONLY", "A literal Reference dimension remains a read-only measurement; no driving-mode API is invented.");
        Need(parameter.IsNull || !double.IsFinite(parameterValue) || ModelParameterSnapshot.Near(parameterValue, value), "CONSTRAINT_PARAMETER_UNAVAILABLE", "The bound native parameter getter and exact owner MCS value disagree.");
        var spec = NativeSketchDimensionBinding411.Spec(info, binding.Kind, state.Inputs, value); NativeSketchMembers385.ValidateInputs([spec], state.Inputs); NativeSketchMembers385.ValidateSolved([spec], state.Inputs);
        return new(state, relation, variable, binding.Kind, value, actual.Expression, spec);
    }
    // Read-only unit/identity evidence. Measurement rows can have mm units;
    // this observation is deliberately not an editable/driving claim.
    internal sealed record UnitBinding(string SketchId, string ConstraintId, string Kind);
    internal static IReadOnlyDictionary<McObjectId, UnitBinding> ReadUnits(Multicad.Mc3D.Mc3dSolid body, McObjectId host, Action cancel)
    {
        var sketch = host.IsNull ? null : host.GetObjectOfType<Multicad.Mc3D.PlanarSketch>();
        // The inventory belongs to one MCS document, and can include variables
        // of another explicitly scoped part in that same network. Unit reads
        // do not grant that other part mutation scope. An unconsumed sketch in
        // this same document already has native relation/VarId unit evidence;
        // first forming consumption must not change these read annotations.
        if (sketch == null || sketch.ID != host || sketch.DbEntity.Document.ID != body.DbEntity.Document.ID) return new Dictionary<McObjectId, UnitBinding>();
        using var manager = NativeSketchMembers385.Manager(sketch); var ids = manager.GetAllConstraints(); if (ids == null) return new Dictionary<McObjectId, UnitBinding>(); ModelInventoryBudget.Ids(ids, cancel); var matches = new List<(McObjectId Variable, UnitBinding Binding)>();
        foreach (var id in ids)
        {
            cancel(); using var info = manager.GetConstraintInfo(id); Need(info != null, "CONSTRAINTS_UNAVAILABLE", "A listed dimension is unreadable."); if (!info.IsDimensional || info.VarId.IsNull) continue;
            string? kind = info.Type switch { ConstraintType.DimAligned => "distance", ConstraintType.DimHorizontal => "distance_x", ConstraintType.DimVertical => "distance_y", ConstraintType.DimRadial => "radius", ConstraintType.DimDiametrical => "diameter", _ => null };
            if (kind != null) matches.Add((info.VarId, new(host.ToString(), id.ToString(), kind)));
        }
        return matches.GroupBy(row => row.Variable).Where(group => group.Count() == 1).ToDictionary(group => group.Key, group => group.Single().Binding);
    }
}
