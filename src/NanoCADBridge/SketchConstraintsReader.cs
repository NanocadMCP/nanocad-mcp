using System.Diagnostics;
using System.Globalization;
using System.Runtime.CompilerServices;
using NanoCAD.Contracts;
using Teigha.DatabaseServices;
using ConstraintType = Multicad.DatabaseServices.Constraints.ConstraintType;

namespace NanoCADBridge;

internal static class SketchConstraintsReader
{
    public static object Read(Arguments args, DocumentState state, Transaction tx, ObjectId modelId, string instance, CancellationToken cancellation)
    {
        var loaded = AppDomain.CurrentDomain.GetAssemblies().Select(a => a.GetName().Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (!loaded.Contains("mapimgd") || !loaded.Contains("MapiBaseTypes")) throw new DomainException("CAPABILITY_UNAVAILABLE", "MultiCAD runtime is not already loaded.");
        long handle = unchecked((long)ulong.Parse(args.Handles![0], NumberStyles.HexNumber, CultureInfo.InvariantCulture));
        if (!state.Document.Database.TryGetObjectId(new Handle(handle), out var id) || id.IsNull || !id.IsValid) throw new DomainException("OBJECT_NOT_FOUND", "Body not found.");
        if (id.IsErased || id.IsEffectivelyErased) throw new DomainException("OBJECT_ERASED", "Body was erased.");
        if (tx.GetObject(id, OpenMode.ForRead) is not Entity entity || entity.OwnerId != modelId) throw new DomainException("OUT_OF_SCOPE", "A top-level Model Space body is required.");
        return ReadMultiCad(args, state, tx, handle, instance, cancellation);
    }
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static object ReadMultiCad(Arguments args, DocumentState state, Transaction tx, long handle, string instance, CancellationToken cancellation)
    {

        void Budget() => cancellation.ThrowIfCancellationRequested();
        var active = Multicad.DatabaseServices.McDocument.ActiveDocument;
        if (active == null || !state.Document.IsNamedDrawing || !string.Equals(Path.GetFullPath(active.Path), Path.GetFullPath(state.Document.Name), StringComparison.OrdinalIgnoreCase))
            throw new DomainException("DOCUMENT_CONTEXT_MISMATCH", "MultiCAD active document differs from the requested document.");
        var bodyId = Multicad.McObjectId.FromHandle(handle, active.ID);
        var body = bodyId.GetObjectOfType<Multicad.Mc3D.Mc3dSolid>();
        if (body == null || body.ID != bodyId) throw new DomainException("UNSUPPORTED_ENTITY_TYPE", "Source is not a matching Mc3dSolid.");
        var contents = body.GetPartContents(false); Budget();
        if (contents == null) throw new DomainException("ANALYSIS_LIMIT_EXCEEDED", "Body history is unavailable.");
        var matches = contents.Where(x => x.ToString() == args.ModelObjectId).ToArray();
        var owner = body.DbEntity.Document;
        Multicad.Mc3D.PlanarSketch? sketch;
        if (matches.Length == 1) sketch = matches[0].GetObjectOfType<Multicad.Mc3D.PlanarSketch>();
        else if (matches.Length == 0 && args.DocumentSketchScope)
        {
            sketch = ModelSketchLifecycleReader362.Resolve(args.ModelObjectId!, owner, Budget);
            if (!sketch.HostSolidID.IsNull) throw new DomainException("OUT_OF_SCOPE", "Choose an independent construction sketch or a sketch from the selected body's current history.");
        }
        else throw new DomainException("OUT_OF_SCOPE", "Sketch is not in this body's current history.");
        if (sketch == null || sketch.ID.ToString() != args.ModelObjectId) throw new DomainException("UNSUPPORTED_ENTITY_TYPE", "Selected item is not the requested PlanarSketch.");
        var sketchId = sketch.ID;
        if (sketch.DbEntity.Document?.ID != owner.ID) throw new DomainException("CONSTRAINT_SCOPE_MISMATCH", "The exact sketch and body owner differ.");
        using var manager = NativeSketchMembers385.Manager(sketch);
        Budget();
        if (manager == null) throw new DomainException("CAPABILITY_UNAVAILABLE", "MCS constraints manager is unavailable.");
        var network = manager.ConstraintsNetworkType; var managerSketch = manager.PlanarSketchId;
        if (managerSketch != sketchId)
            throw new DomainException("CONSTRAINT_SCOPE_MISMATCH", "Native manager does not identify the requested sketch and document scope.", false,
                new
                {
                    requested_sketch_id = sketchId.ToString(),
                    actual_sketch_id = managerSketch.IsNull ? null : managerSketch.ToString(),
                    actual_network = network.ToString(),
                    actual_scope_id = manager.ScopeId.IsNull ? null : manager.ScopeId.ToString(),
                    actual_document_id = manager.DocumentId.IsNull ? null : manager.DocumentId.ToString()
                });
        var inventory = NativeSketchInventory.Read(manager, sketch, Budget);
        var ids = inventory.Ids;
        SketchMobilityReport? mobility = null;
        string? mobilityError = null;
        object? proposalBinding = null;
        if (args.ProposedConstraints is { Length: > 0 } proposed)
        {
            try
            {
                var inputs = NativeSketchMembers385.Capture(sketch, state, tx, Budget, manager, inventory.Ids);
                var memberIds = inputs.Members.Select(m => m.Id.ToString()).ToHashSet(StringComparer.Ordinal);
                bool match = proposed.SelectMany(s => new[] { s.First, s.Second, s.Third }.OfType<string>()).All(memberIds.Contains);
                if (!match) throw new DomainException("SKETCH_BINDING_UNAVAILABLE", "At least one operand has no exact native edit binding in this sketch.");
                NativeSketchMembers385.ValidateInputs(proposed, inputs);
                mobility = NativeSketchMobility.Plan(inputs, manager, inventory.Ids, inventory.Complete, proposed, Budget, preserve: args.PreserveMotion);
                proposalBinding = new { editable = inventory.Complete, reason = inventory.Complete ? null : inventory.Reason };
            }
            catch (DomainException error) when (error.Error.code is not ("RESPONSE_LIMIT_EXCEEDED" or "INVALID_ARGUMENT"))
            { proposalBinding = new { editable = false, reason = error.Message }; }
            catch (Teigha.Runtime.Exception error)
            { proposalBinding = new { editable = false, reason = error.Message }; }
        }
        try { mobility ??= NativeSketchMobilityReader.Read(sketch, manager, inventory, args.ProposedConstraints, args.PreserveMotion, Budget); }
        catch (DomainException error) when (error.Error.code is not ("RESPONSE_LIMIT_EXCEEDED" or "INVALID_ARGUMENT")) { mobilityError = error.Message; }
        catch (Teigha.Runtime.Exception error) { mobilityError = error.Message; }
        bool? nativeFullyConstrained = null;
        try { nativeFullyConstrained = sketch.IsFullyConstrained(); }
        catch (Exception error) when (error is Teigha.Runtime.Exception or NotSupportedException or NotImplementedException or System.Runtime.InteropServices.COMException) { }
        string agreement = nativeFullyConstrained is bool flag && mobility?.remaining_dof is int currentDof
            ? (flag == (currentDof == 0) ? "agrees" : "differs") : "unavailable";
        string? Id(Multicad.McObjectId id) => id.IsNull ? null : id.ToString();
        object? Reference(Multicad.DatabaseServices.Constraints.IConstraintObjectInfo? info)
        {
            if (info == null || info.ObjectId.IsNull) return null;
            var p = info.PointRefOnObject; var point = new[] { p.X, p.Y, p.Z };
            bool finite = point.All(double.IsFinite);
            string? reason = finite ? null : point.All(double.IsNaN) ? "whole_object_or_unspecified_native_point" : "non_finite_native_reference";
            return new
            {
                object_id = info.ObjectId.ToString(),
                subentity_id = Id(info.SubentId),
                polyline_segment_index = info.PolylineSegmentIndex,
                point = finite ? point : null,
                point_unavailable_reason = reason
            };
        }
        var constraints = new List<object>();
        var smooth = args.MeasureSmooth ? new List<object>() : null;
        NativeSketchMembers385.Snapshot? geometry = null;
        string? geometryError = null;
        foreach (var id in ids)
        {
            Budget(); using var info = manager.GetConstraintInfo(id);
            if (info == null && !inventory.Complete) continue;
            if (info == null) throw new DomainException("CONSTRAINTS_UNAVAILABLE", "Constraint details are unavailable.");
            if (smooth != null && info.Type == ConstraintType.GeomSmooth)
            {
                if (geometry == null && geometryError == null)
                {
                    try { geometry = NativeSketchMembers385.Capture(sketch, state, tx, Budget, manager, ids); }
                    catch (DomainException error) when (error.Error.code != "RESPONSE_LIMIT_EXCEEDED") { geometryError = error.Message; }
                    catch (Teigha.Runtime.Exception error) { geometryError = error.Message; }
                }
                if (geometry == null) smooth.Add(new { constraint_id = id.ToString(), status = "unavailable", reason = geometryError });
                else smooth.Add(MeasureSmooth(id.ToString(), geometry, info.FirstObject, info.SecondObject));
            }
            double parameter = info.Param;
            string unit = info.Type switch
            {
                ConstraintType.DimAngular => "radians",
                ConstraintType.DimRadial or ConstraintType.DimDiametrical or ConstraintType.DimAligned or ConstraintType.DimHorizontal or ConstraintType.DimVertical => "drawing_units",
                _ => info.IsDimensional ? "native_constraint_units" : "not_applicable"
            };
            constraints.Add(new
            {
                mc_id = id.ToString(),
                type = info.Type.ToString(),
                is_geometrical = info.IsGeometrical,
                is_dimensional = info.IsDimensional,
                first = Reference(info.FirstObject),
                second = Reference(info.SecondObject),
                third = Reference(info.ThirdObject),
                parameter_value = info.IsDimensional && double.IsFinite(parameter) ? (double?)parameter : null,
                parameter_value_unit = unit,
                variable_id = Id(info.VarId),
                variable_name = Text(info.VarName),
                variable_expression = Text(info.VarExpression),
                dimension_id = Id(info.DimId)
            });
        }
        Budget();
        return new
        {
            source_ref = new EntityRef(instance, state.Id, args.Handles![0]),
            sketch_id = sketchId.ToString(),
            sketch_membership = matches.Length == 1 ? "body_history" : "same_document_construction",
            network = network.ToString(),
            manager_document_id = Id(manager.DocumentId),
            scope_id = Id(manager.ScopeId),
            identity_scope = "bridge_document_revision",
            reference_coordinate_system = "native_constraint_reference",
            solver_status = "not_evaluated",
            constraints_complete = inventory.Complete,
            native_fully_constrained = nativeFullyConstrained,
            native_mobility_agreement = agreement,
            inventory_source = inventory.Source,
            inventory_routes = inventory.Routes,
            proposal_binding = proposalBinding,
            inventory_unavailable_reason = inventory.Reason,
            mobility,
            mobility_unavailable_reason = mobilityError,
            constraints,
            smooth_measurements = smooth,
            presentation = SketchVisibilityState436.Observe(body, sketch, Budget)
        };
    }

    private static object MeasureSmooth(string constraint, NativeSketchMembers385.Snapshot inputs,
        Multicad.DatabaseServices.Constraints.IConstraintObjectInfo? first,
        Multicad.DatabaseServices.Constraints.IConstraintObjectInfo? second)
    {
        (NativeSketchMembers385.Member Member, string Role, string LocalRole, double[] Point) Endpoint(Multicad.DatabaseServices.Constraints.IConstraintObjectInfo? reference)
        {
            if (reference == null || reference.ObjectId.IsNull) throw new DomainException("SMOOTH_ENDPOINT_UNAVAILABLE", "The native constraint has no operand reference.");
            var matches = inputs.Members.Where(m => m.Id == reference.ObjectId || m.ObjectId == reference.ObjectId &&
                (m.Segment == null || m.Segment == reference.PolylineSegmentIndex)).ToArray();
            if (matches.Length != 1) throw new DomainException("SMOOTH_ENDPOINT_UNAVAILABLE", "The native operand does not identify one current sketch member.");
            var member = matches[0];
            var role = NativeSketchMembers385.PointRole(inputs, member, reference);
            if (role is not ("start" or "end")) throw new DomainException("SMOOTH_ENDPOINT_UNAVAILABLE", "The native selector does not identify a unique current endpoint.");
            var p = NativeSketchMembers385.Point(inputs, member, role);
            return (member, role, NativeSketchMembers385.EndpointName(inputs, member, role)!, [p.X, p.Y, p.Z]);
        }
        try
        {
            var a = Endpoint(first); var b = Endpoint(second);
            double gap = Math.Sqrt(a.Point.Zip(b.Point, (x, y) => (x - y) * (x - y)).Sum());
            double? angle = null; string? reason = null;
            try { angle = SplineConstraintGeometry445.ContinuationAngle(a.Member.Curve, a.LocalRole, b.Member.Curve, b.LocalRole); }
            catch (DomainException e) { reason = e.Message; }
            return new
            {
                constraint_id = constraint,
                status = angle == null ? "partial" : "measured",
                reason,
                first = new { member_id = a.Member.Id.ToString(), role = a.Role, point_wcs = a.Point },
                second = new { member_id = b.Member.Id.ToString(), role = b.Role, point_wcs = b.Point },
                endpoint_gap = gap,
                gap_unit = "drawing_units",
                continuation_angle = angle,
                angle_unit = "radians"
            };
        }
        catch (DomainException e) { return new { constraint_id = constraint, status = "unavailable", reason = e.Message }; }
        catch (Teigha.Runtime.Exception e) { return new { constraint_id = constraint, status = "unavailable", reason = e.ErrorStatus.ToString() }; }
    }
    private static string? Text(string? text) => text == null || text.Length <= 8192 ? text : throw new DomainException("ENTITY_TOO_LARGE", "Constraint text exceeds8192 characters.");
}
