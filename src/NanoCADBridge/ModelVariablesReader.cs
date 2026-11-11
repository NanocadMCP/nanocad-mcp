using System.Globalization;
using System.Runtime.CompilerServices;
using NanoCAD.Contracts;
using Teigha.DatabaseServices;

namespace NanoCADBridge;

internal static class ModelVariablesReader
{
    public static object Read(Arguments args, DocumentState state, Transaction transaction, ObjectId modelId, string instance, CancellationToken cancellationToken, Action? cancel = null)
    {
        var loaded = AppDomain.CurrentDomain.GetAssemblies().Select(a => a.GetName().Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (!loaded.Contains("mapimgd") || !loaded.Contains("MapiBaseTypes"))
            throw new DomainException("CAPABILITY_UNAVAILABLE", "MultiCAD runtime is not already loaded.");
        var bits = unchecked((long)ulong.Parse(args.Handles![0], NumberStyles.HexNumber, CultureInfo.InvariantCulture));
        if (!state.Document.Database.TryGetObjectId(new Handle(bits), out var id) || id.IsNull || !id.IsValid)
            throw new DomainException("OBJECT_NOT_FOUND", "The variable source body was not found.");
        if (id.IsErased || id.IsEffectivelyErased) throw new DomainException("OBJECT_ERASED", "The variable source body was erased.");
        if (transaction.GetObject(id, OpenMode.ForRead) is not Entity entity || entity.OwnerId != modelId)
            throw new DomainException("OUT_OF_SCOPE", "Variable source must be a top-level Model Space body.");
        return ReadMultiCad(state, bits, new EntityRef(instance, state.Id, args.Handles[0]), cancellationToken, cancel);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static object ReadMultiCad(DocumentState state, long handle, EntityRef source, CancellationToken cancellationToken, Action? cancel)
    {
        void Budget()
        {
            cancel?.Invoke();
            cancellationToken.ThrowIfCancellationRequested();
        }
        var active = Multicad.DatabaseServices.McDocument.ActiveDocument;
        if (active == null || !state.Document.IsNamedDrawing || !string.Equals(Path.GetFullPath(active.Path), Path.GetFullPath(state.Document.Name), StringComparison.OrdinalIgnoreCase))
            throw new DomainException("DOCUMENT_CONTEXT_MISMATCH", "MultiCAD active document does not match the requested named DWG.");
        var bodyId = Multicad.McObjectId.FromHandle(handle, active.ID);
        var body = bodyId.GetObjectOfType<Multicad.Mc3D.Mc3dSolid>();
        if (body == null) throw new DomainException("UNSUPPORTED_ENTITY_TYPE", "Variable source is not a Mc3dSolid.");
        if (body.ID != bodyId) throw new DomainException("OBJECT_IDENTITY_MISMATCH", "MultiCAD returned a different body identity.");
        // SDK HelloMultiCAD/3dSheetMetal uses the solid's document: the
        // active drawing container need not own the MCS variable network.
        var document = body.DbEntity.Document;
        if (document == null) throw new DomainException("DOCUMENT_CONTEXT_MISMATCH", "The body owner document is unavailable.");
        using var manager = document.GetDocumentVariablesManager(Multicad.DatabaseServices.Constraints.Constraint2dNetType.MCS);
        if (manager == null) throw new DomainException("CAPABILITY_UNAVAILABLE", "The MCS variables manager is unavailable.");
        var ids = manager.GetAllVariables(); Budget();
        if (ids == null) throw new DomainException("VARIABLES_UNAVAILABLE", "The SDK did not return a variable inventory; no empty-inventory claim is made.");
        string? Id(Multicad.McObjectId id) => id.IsNull ? null : id.ToString();
        string[]? References(List<Multicad.McObjectId>? values)
        {
            if (values == null) return null; // SDK null is unknown, not an empty dependency set.
            return values.Select(x => x.ToString()).ToArray();
        }
        var variables = new List<object>(); var bytes = new ModelInventoryBudget(Budget);
        var dimensions = new Dictionary<Multicad.McObjectId, IReadOnlyDictionary<Multicad.McObjectId, ModelSketchParameter411.UnitBinding>>();
        var featureBindings = new Dictionary<Multicad.McObjectId, Dictionary<Multicad.McObjectId, (string Role, string Unit, double Value)>>();
        var nativeHoleBindings = new Dictionary<Multicad.McObjectId, ModelHoleNativeControl.Binding>();
        var bindingNotes = new List<object>();
        foreach (var id in ids)
        {
            Budget();
            var info = manager.GetVariableInfo(id);
            if (info == null) throw new DomainException("VARIABLES_UNAVAILABLE", "Variable metadata is unavailable.");
            bool available = manager.GetVariableValue(id, out double value);
            if (available && !double.IsFinite(value)) throw new DomainException("INVALID_GEOMETRY", "Variable value is non-finite.");
            var host = manager.GetVariableHostObjectId(id); string unit = "native_variable_units";
            if (!host.IsNull && !featureBindings.ContainsKey(host))
            {
                var bindings = new Dictionary<Multicad.McObjectId, (string Role, string Unit, double Value)>();
                try
                {
                    if (host.GetObjectOfType<Multicad.Mc3D.ThicknessFeature>() is { } shell)
                        foreach (var binding in ModelShellParameter412.Observe(shell, Budget)) bindings.Add(binding.Variable, (binding.Role, binding.Unit, binding.Value));
                    else if (host.GetObjectOfType<Multicad.Mc3D.HoleFeature>() is { } hole)
                    {
                        foreach (var binding in ModelHoleParameter412.Observe(hole, Budget)) bindings.Add(binding.Variable, (binding.Role, binding.Unit, binding.Value));
                        foreach (var binding in ModelHoleNativeControl.Observe(hole, Budget, (variable, error) =>
                        {
                            var note = new { host_object_id = host.ToString(), variable_id = variable.ToString(), code = error.Error.code, reason = error.Message };
                            bytes.Add(note); bindingNotes.Add(note);
                        }))
                        {
                            if (bindings.ContainsKey(binding.Variable)) continue;
                            bindings.Add(binding.Variable, (binding.Role, binding.Unit, binding.Value));
                            nativeHoleBindings.Add(binding.Variable, binding);
                        }
                    }
                }
                catch (DomainException error)
                {
                    // A missing helper or unsupported role must not hide the
                    // readable owner network or disable unrelated families.
                    var note = new { host_object_id = host.ToString(), code = error.Error.code, reason = error.Message }; bytes.Add(note); bindingNotes.Add(note);
                }
                featureBindings.Add(host, bindings);
            }
            (string Role, string Unit, double Value)? typed = null;
            if (featureBindings.TryGetValue(host, out var known) && known.TryGetValue(id, out var control) && available && ModelParameterSnapshot.Near(control.Value, value)) typed = control;
            if (typed?.Role == "shell_thickness")
            {
                var note = new { host_object_id = host.ToString(), code = "SHELL_NAMED_FORMULA_UNAVAILABLE", reason = ModelShellParameter412.NamedFormulaUnavailableReason }; bytes.Add(note); bindingNotes.Add(note);
            }
            var taper = host.IsNull ? null : ModelTaperParameter412.Observe(host, id, document, Budget);
            if (taper != null && available && ModelParameterSnapshot.Near(taper.Degrees, value)) typed = (taper.Role, "degrees", taper.Degrees);
            ModelSketchParameter411.UnitBinding? dimension = null;
            if (info.Type.ToString().Split(',', StringSplitOptions.TrimEntries).Contains("Dim2DConstraint"))
            {
                if (!dimensions.TryGetValue(host, out var bindings)) { bindings = ModelSketchParameter411.ReadUnits(body, host, Budget); dimensions.Add(host, bindings); }
                bindings.TryGetValue(id, out dimension);
            }
            //215: RotateX/Y/Z variables store degrees but IsAngularValue is false.
            // Bind exact public IDs; names and the generic angular flag are insufficient.
            var rotation = host.IsNull ? null : host.GetObjectOfType<Multicad.Mc3D.AssocRotateFeature>();
            bool placementAngle = rotation != null && new[] { rotation.RotateXParamVarID, rotation.RotateYParamVarID, rotation.RotateZParamVarID }.Contains(id);
            if (dimension != null) unit = (int)state.Document.Database.Insunits == 4 ? "Millimeters" : "native_variable_units";
            else if (typed != null) unit = typed.Value.Unit == "Millimeters" && (int)state.Document.Database.Insunits != 4 ? "native_variable_units" : typed.Value.Unit;
            else if (placementAngle) unit = "degrees";
            else if (manager.IsAngularValue(id))
            {
                //194 native evidence: RevolveFeature.Angle=pi/2 while its MCS
                // variable expression/value are90. IsAngularValue is not a
                // radians guarantee for arbitrary native variables.
                var revolution = host.IsNull ? null : host.GetObjectOfType<Multicad.Mc3D.RevolveFeature>();
                var circular = host.IsNull ? null : host.GetObjectOfType<Multicad.Mc3D.CircularPatternFeature>();
                unit = (revolution != null && revolution.RotationAngleParamVarID == id) || (circular != null && circular.AngleParamVarID == id) ? "degrees" : "native_angular_units_unverified";
            }
            var observed = new
            {
                mc_id = id.ToString(),
                name = info.Name,
                type = info.Type.ToString(),
                expression = info.Expression,
                value = available ? (double?)value : null,
                value_available = available,
                value_unit = unit,
                host_object_id = Id(host),
                owner_kind = dimension != null ? "sketch_dimension" : typed != null ? "feature" : null,
                sketch_id = dimension?.SketchId,
                constraint_id = dimension?.ConstraintId,
                control_kind = dimension?.Kind ?? typed?.Role,
                native_control = typed?.Role == ModelHoleNativeControl.Role && nativeHoleBindings.TryGetValue(id, out var nativeHole) ? ModelHoleNativeControl.Observation(nativeHole) : null,
                downstream_variables = References(manager.GetDependentOnVariables(id)),
                downstream_objects = References(manager.GetDependentOn3dObjects(id))
            };
            bytes.Add(observed); variables.Add(observed);
        }
        Budget();
        return new
        {
            network = "MCS",
            identity_scope = "bridge_document_revision",
            dependency_direction = "source_variable_to_direct_consumers",
            value_scope = "native_value_without_requested_recalculation_not_solver_validation",
            mc_document_id = document.ID.ToString(),
            source_ref = source,
            variables,
            parameter_binding_notes = bindingNotes
        };
    }
}
