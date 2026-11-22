using NanoCAD.Contracts;
using Multicad;
using Multicad.Mc3D;
namespace NanoCADBridge;

internal static class ModelManufacturingHistory
{
    internal sealed record FixedFrame(double[]? Center, double[]? Normal, string? Unavailable);
    internal static FixedFrame FixedPlacement(IppDefinition placement)
    {
        var frame = new Multicad.Geometry.Matrix3d();
        Multicad.hresult status;
        try { status = placement.GetPosition(out frame, 0); }
        catch (Exception error) when (error is not OperationCanceledException) { return new(null, null, error.GetType().Name); }
        if (!status.IsSuccess()) return new(null, null, status.ToString());
        var origin = Multicad.Geometry.Point3d.Origin.TransformBy(frame);
        var normal = new Multicad.Geometry.Point3d(0, 0, 1).TransformBy(frame) - origin;
        double[] center = [origin.X, origin.Y, origin.Z], axis = [normal.X, normal.Y, normal.Z];
        return center.All(double.IsFinite) && axis.All(double.IsFinite) && axis.Any(v => v != 0)
            ? new(center, axis, null) : new(null, null, "nonfinite_or_degenerate_placement");
    }
    private static ModelHolePlacementSlots.Reference[] TwoEdgeReferences(IppDefinition placement)
    {
        var stored = placement.GetAssocParams();
        if (stored == null || stored.Count != 2 || stored.Any(reference => reference == null))
            throw new DomainException("HISTORY_UNAVAILABLE", "The two managed placement reference slots are unavailable.");
        return stored.Select((reference, slot) => ModelHolePlacementSlots.Observe(slot, reference.IsNull,
            reference.ID.IsNull ? null : reference.ID.ToString(), reference.GSMarker, reference.Param, reference.Data)).ToArray();
    }

    static object? Placement(IppDefinition? p, HoleFeature hole, Action? cancel, bool readTwoEdgePlacement)
    {
        if (p == null) return null;
        string placement_type = p.DefType.ToString(), support_face_id = p.TargetPlaneId.ToString();
        if (p.DefType == IppDefinitionType.Concentric) return ModelConcentricHole.Read(p, hole.HostSolidID, hole, cancel);
        if (p.DefType == IppDefinitionType.Fixed)
        {
            var actual = FixedPlacement(p);
            return new { placement_type, support_face_id, center = actual.Center, support_normal_wcs = actual.Normal, position_unavailable_reason = actual.Unavailable };
        }
        if (p.DefType == IppDefinitionType.TwoEdges)
        {
            if (!readTwoEdgePlacement) return new { placement_type, support_face_id, offset1 = p.Param1, offset2 = p.Param2, sector = p.Sector };
            cancel?.Invoke();
            var frame = FixedPlacement(p);
            ModelHolePlacementSlots.Reference[]? references = null;
            string? referenceFailure = null;
            try { references = TwoEdgeReferences(p); }
            catch (DomainException error) { referenceFailure = error.Error.code + ": " + error.Error.message; }
            catch (Exception error) when (error is InvalidOperationException or System.Runtime.InteropServices.COMException or Teigha.Runtime.Exception)
            { referenceFailure = error.GetType().Name; }
            return new
            {
                placement_type,
                support_face_id,
                offset1 = p.Param1,
                offset2 = p.Param2,
                sector = p.Sector,
                reference_slots = references,
                reference_slots_unavailable_reason = referenceFailure,
                complex_path_and_viewport_observed = false,
                center_wcs = frame.Center,
                support_normal_wcs = frame.Normal,
                position_unavailable_reason = frame.Unavailable
            };
        }
        return new { placement_type, support_face_id };
    }
    internal static object? Parameters(McObjectId id, Action? cancel = null, bool readTwoEdgePlacement = false)
    {
        var budget = new ModelInventoryBudget(cancel);
        string? Ref(McObjectId reference) { var value = reference.IsNull ? null : reference.ToString(); budget.Add(value); return value; }
        var point = id.GetObjectOfType<WorkPoint>();
        if (point != null) { var p = point.Position; return new { kind = "construction_point", definition = point.DefinitionType.ToString(), position = new[] { p.X, p.Y, p.Z }, reference_ids = point.GetDefinitionParams().Where(x => !x.IsNull).Select(x => x.ID.ToString()).ToArray() }; }
        var axis = id.GetObjectOfType<WorkAxis>();
        if (axis != null) { using var line = axis.PositionAsLineSeg; return new { kind = "construction_axis", definition = axis.DefinitionType.ToString(), start = new[] { line.StartPoint.X, line.StartPoint.Y, line.StartPoint.Z }, end = new[] { line.EndPoint.X, line.EndPoint.Y, line.EndPoint.Z }, reference_ids = axis.GetDefinitionParams().Where(x => !x.IsNull).Select(x => x.ID.ToString()).ToArray() }; }
        var sketch = id.GetObjectOfType<PlanarSketch>(); if (sketch != null) return ModelSketchSupport.Read(sketch);
        var extrusion = id.GetObjectOfType<ExtrudeFeature>(); if (extrusion != null) return ModelExtrusionExtentEditor.Read(extrusion);
        var loft = id.GetObjectOfType<LoftFeature>();
        if (loft != null)
        {
            var sources = Enumerable.Range(0, loft.SectionsCount).Select(index =>
            {
                cancel?.Invoke(); var section = loft.GetSection(index);
                var item = new
                {
                    index,
                    profile_id = section == null ? null : Ref(section.ID),
                    sketch_id = section == null ? null : Ref(section.ParentSketchID),
                    host_solid_id = section == null ? null : Ref(section.HostSolidID),
                    document_id = section?.DbEntity.Document?.ID.ToString()
                };
                budget.Add(item); return item;
            }).ToArray();
            var sections = sources.Select(section => section.profile_id).ToArray();
            var guides = Enumerable.Range(0, loft.GuideRailsCount).Select(i => { cancel?.Invoke(); return loft.GetGuideRailsParams(i)?.Select(p => { cancel?.Invoke(); if (p == null) { budget.Add<object?>(null); return null; } return Ref(p.ID); }).ToArray(); }).ToArray();
            return new
            {
                kind = "loft",
                profile_id = loft.ProfileID.ToString(),
                section_profile_ids = sections,
                section_sources = sources,
                section_sources_complete = sources.Length >= 2 && sources.All(s => s.profile_id != null && s.sketch_id != null && s.document_id != null),
                sections_complete = sections.Length >= 2 && sections[0] == loft.ProfileID.ToString() && sections.All(s => s != null),
                loft_type = loft.LoftType.ToString(),
                closed = loft.Closed,
                path_id = loft.LoftType == LoftType.WithCenterLine ? loft.Path?.ID.ToString() : null,
                guide_ids = guides,
                guide_bindings_complete = (loft.LoftType != LoftType.WithRails || guides.Length > 0) && guides.All(g => g != null && g.Length > 0 && g.All(s => s != null)),
                first_section_condition = loft.FirstSectionCondition.ToString(),
                last_section_condition = loft.LastSectionCondition.ToString(),
                part_operation = loft.PartOperation.ToString()
            };
        }
        var sweep = id.GetObjectOfType<ExtrudeAlongPathFeature>();
        if (sweep != null)
        {
            var paths = sweep.GetPath(); return new
            {
                kind = "sweep",
                profile_id = sweep.ProfileID.ToString(),
                orientation = sweep.ProfileOrientation.ToString(),
                taper_angle = sweep.Angle,
                part_operation = sweep.PartOperation.ToString(),
                taper_variable_id = sweep.TaperAngleParamVarID.ToString(),
                path_ids = paths?.Select(p => { cancel?.Invoke(); if (p == null) { budget.Add<object?>(null); return null; } return Ref(p.ID); }).ToArray(),
                path_bindings_complete = paths != null && paths.Count > 0 && paths.All(p => p != null && !p.ID.IsNull)
            };
        }
        var h = id.GetObjectOfType<HoleFeature>();
        if (h != null) { using var p = h.GetPlacement(); bool angleActive = !h.IsFullDepth && !h.FlatBottom; return new { kind = "hole", diameter = h.Diameter, through = h.IsFullDepth, depth = h.Depth, seat = h.SeatType.ToString(), seat_diameter = h.SeatDiameter, seat_depth = h.SeatDepth, seat_angle_degrees = h.SeatAngle * 180 / Math.PI, flat_bottom = h.FlatBottom, bottom_angle_applicable = angleActive, bottom_angle_degrees = angleActive ? (double?)(h.BottomAngle * 180 / Math.PI) : null, face_id = h.FaceId.ToString(), terminal_face_id = h.TerminalFaceId.ToString(), placement = Placement(p, h, cancel, readTwoEdgePlacement), thread_id = h.GetThread()?.ID.ToString() }; }
        var t = id.GetObjectOfType<ThreadFeature>();
        if (t != null) return new { kind = "thread", pitch = t.Pitch, right_handed = t.IsRight, @internal = t.IsInternal, full_length = t.IsFullLength, length_applicable = !t.IsFullLength, length = t.Length, minimum_diameter = t.MinDiameter, maximum_diameter = t.MaxDiameter, designation = t.Designation, face_id = t.ThreadFaceId.ToString(), front_edge_id = t.FrontEdgeId.ToString(), pitch_variable_id = t.PitchVarID.ToString(), length_variable_id = t.LengthVarID.ToString(), representation = "native_thread_feature_helical_brep_unverified" };
        var c = id.GetObjectOfType<CircularPatternFeature>();
        if (c != null)
        {
            var axisBinding = NativePatternObservation439.TryRead(c, 0); var excluded = c.GetSuppressedElementsIndices();
            return new
            {
                kind = "circular_pattern",
                count = c.Size,
                angle_degrees = c.Angle * 180 / Math.PI,
                count_variable_id = c.CountParamVarID.ToString(),
                angle_variable_id = c.AngleParamVarID.ToString(),
                axis = axisBinding,
                direction_bindings_complete = axisBinding != null,
                source_item_ids = Enumerable.Range(0, c.SourceItemsCount).Select(index => { cancel?.Invoke(); var value = c.GetSourceItemFeature(index).ToString(); budget.Add(value); return value; }).ToArray(),
                has_sub_feature_items = c.HasSubFeatureItems,
                suppressed_elements = (excluded ?? []).Select(index => { cancel?.Invoke(); return new[] { index.Index1, index.Index2 }; }).ToArray(),
                suppressed_elements_complete = excluded != null
            };
        }
        var m = id.GetObjectOfType<MirrorFeature>();
        if (m != null) return new { kind = "mirror", source_item_ids = Enumerable.Range(0, m.SourceItemsCount).Select(index => { cancel?.Invoke(); var value = m.GetSourceItemFeature(index).ToString(); budget.Add(value); return value; }).ToArray(), has_sub_feature_items = m.HasSubFeatureItems, dependency_ids = (m.GetDependsOn(false, false) ?? []).Select(x => { cancel?.Invoke(); var value = x.ToString(); budget.Add(value); return value; }).ToArray() };
        return null;
    }
}
