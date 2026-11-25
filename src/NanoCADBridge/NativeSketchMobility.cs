using Multicad;
using Multicad.DatabaseServices.Constraints;
using Multicad.DatabaseServices.DocumentVariables;
using NanoCAD.Contracts;

namespace NanoCADBridge;

// Convert actual sketch-local geometry and native operands into the same
// engineering model that is used for proposals on a drawing workplane.
internal static class NativeSketchMobility
{
    internal static SketchMobilityReport Plan(NativeSketchMembers385.Snapshot inputs, Constraints2D manager,
        IReadOnlyList<McObjectId> ids, bool complete, IReadOnlyList<SketchConstraintSpec>? requested,
        Action cancel, IReadOnlySet<McObjectId>? replace = null, IReadOnlyList<SketchMobilityMotion>? preserve = null, bool geometryOnly = false)
    {
        var members = inputs.Members.ToDictionary(m => m.Id);
        var segments = inputs.Members.Where(m => m.Segment.HasValue).ToLookup(m => (m.ObjectId, m.Segment!.Value));
        var geometry = inputs.Members.Select(m => m.Curve).ToList();
        var fixedGeometry = inputs.Members.Where(m => m.ReferenceOnly).Select(m => m.Curve.Id).ToArray();
        var existing = new List<SketchMobilityRelation>();
        foreach (var point in inputs.Construction)
        {
            var p = point.Geometry.GetProperty("point");
            var local = DrawingWorkplane.Local(inputs.Plane, new(p[0].GetDouble(), p[1].GetDouble(), p[2].GetDouble()));
            geometry.Add(new(point.Id.ToString(), "point", Center: local));
        }
        var currentIds = geometry.Select(g => g.Id).ToHashSet(StringComparer.Ordinal);
        foreach (var spec in requested ?? [])
            if (new[] { spec.First, spec.Second, spec.Third }.OfType<string>().Any(id => !currentIds.Contains(id)))
                throw new DomainException("INVALID_ARGUMENT", "A proposed relation uses a member outside this sketch's current native geometry.");
        foreach (var motion in preserve ?? [])
            if (motion.object_ids.Any(id => !currentIds.Contains(id)))
                throw new DomainException("INVALID_ARGUMENT", "A protected motion uses a member outside this sketch's current native geometry.");
        foreach (var group in inputs.Members.Where(m => m.Segment.HasValue).GroupBy(m => m.ObjectId))
        {
            cancel();
            using var item = inputs.Sketch.GetItemGeometry(group.Key, false);
            if (item?.GeometryType != Multicad.Geometry.EntityGeomType.kPolyline)
            {
                existing.Add(new("native_polyline_topology", null, group.Select(m => m.Curve.Id).ToArray()));
                continue;
            }
            using var polyline = item.Polyline;
            var ordered = group.OrderBy(m => m.Segment).ToArray();
            for (int i = 1; i < ordered.Length + (polyline.IsClosed() ? 1 : 0); i++)
            {
                var first = ordered[i - 1]; var second = ordered[i % ordered.Length];
                existing.Add(new($"polyline:{group.Key}:{i}", new("coincident", first.Curve.Id, second.Curve.Id,
                    NativeSketchMembers385.EndpointName(inputs, first, "end"), NativeSketchMembers385.EndpointName(inputs, second, "start"), null)));
            }
        }
        foreach (var member in inputs.Members.Where(m => !geometryOnly && !m.Owned && !m.ReferenceOnly))
            existing.Add(new("unresolved_native_ownership", null, [member.Curve.Id]));

        NativeSketchMembers385.Member? Resolve(IConstraintObjectInfo reference)
        {
            if (members.TryGetValue(reference.ObjectId, out var member)) return member;
            return segments[(reference.ObjectId, reference.PolylineSegmentIndex)].SingleOrDefault();
        }
        var points = inputs.Construction.Select(p => p.Id).ToHashSet();
        var unclassifiedPoints = points.ToHashSet();
        SketchConstraintSpec? Decode(ConstraintInfo info, double? value, IConstraintObjectInfo[] refs)
        {
            string? kind = info.Type switch
            {
                ConstraintType.GeomHorizontal => "horizontal",
                ConstraintType.GeomVertical => "vertical",
                ConstraintType.GeomParallel => "parallel",
                ConstraintType.GeomPerpendicular => "perpendicular",
                ConstraintType.GeomCollinear => "collinear",
                ConstraintType.GeomTangent => "tangent",
                ConstraintType.GeomEqual => "equal",
                ConstraintType.GeomSmooth => "smooth",
                ConstraintType.GeomConcentric => "concentric",
                ConstraintType.GeomCoincident => "coincident",
                ConstraintType.GeomFix => "fix",
                ConstraintType.GeomSymmetric => "symmetric",
                ConstraintType.DimRadial => "radius",
                ConstraintType.DimDiametrical => "diameter",
                ConstraintType.DimAligned => "distance",
                ConstraintType.DimHorizontal => "distance_x",
                ConstraintType.DimVertical => "distance_y",
                ConstraintType.DimAngular => "angle",
                _ => null
            };
            if (kind == null || info.IsDimensional && value == null) return null;
            int count = kind switch { "symmetric" => 3, "fix" or "horizontal" or "vertical" or "radius" or "diameter" => 1, _ => 2 };
            if (refs.Length != count || info.FirstObject == null || info.FirstObject.ObjectId.IsNull) return null;
            var bound = refs.Select(Resolve).ToArray();
            if (refs.Where((r, i) => bound[i] == null && !points.Contains(r.ObjectId)).Any()) return null;
            string Id(int i) => bound[i]?.Curve.Id ?? refs[i].ObjectId.ToString();
            string? Role(int i) => bound[i] is { } m ? NativeSketchMembers385.EndpointName(inputs, m, NativeSketchMembers385.PointRole(inputs, m, refs[i])) : "center";
            string? first = null, second = null;
            if (kind is "coincident" or "distance" or "distance_x" or "distance_y" or "smooth")
            {
                first = Role(0); second = Role(1);
                if (first == null || second == null) return null;
                if (kind == "distance" && Id(0) == Id(1) && bound[0]?.Curve.Kind == "line" && first != second &&
                    first is "start" or "end" && second is "start" or "end")
                    return new("length", Id(0), null, null, null, value);
            }
            if (kind == "fix" && bound[0] != null)
            {
                var point = refs[0].PointRefOnObject;
                bool whole = refs[0].SubentId.IsNull && double.IsNaN(point.X) && double.IsNaN(point.Y) && double.IsNaN(point.Z);
                if (!whole)
                {
                    first = Role(0);
                    if (first == null) return null;
                    kind = "fix_point";
                }
            }
            return new(kind, Id(0), refs.Length >= 2 ? Id(1) : null, first, second,
                kind == "angle" ? value * 180 / Math.PI : value, refs.Length == 3 ? Id(2) : null);
        }

        // A reference dimension measures geometry. Only its actual owner
        // variable can establish that a dimensional descriptor is driving.
        var owner = inputs.Sketch.DbEntity.Document;
        DocumentVariablesManager? variables = null;
        try
        {
            foreach (var id in ids)
            {
                cancel();
                if (replace?.Contains(id) == true) continue;
                using var info = manager.GetConstraintInfo(id);
                if (info == null) { existing.Add(new(id.ToString(), null)); continue; }
                var refs = new[] { info.FirstObject, info.SecondObject, info.ThirdObject }.Where(r => r != null && !r.ObjectId.IsNull).Select(r => r!).ToArray();
                var targets = refs.Select(r => Resolve(r)?.Id.ToString() ?? r.ObjectId.ToString()).Distinct().ToArray();
                double? value = null;
                if (info.IsDimensional && !info.VarId.IsNull)
                {
                    variables ??= owner.GetDocumentVariablesManager(Constraint2dNetType.MCS);
                    if (variables != null && variables.DocumentId == owner.ID)
                    {
                        var variable = variables.GetVariableInfo(info.VarId);
                        if (variable != null)
                        {
                            var flags = variable.Type.ToString().Split(',', StringSplitOptions.TrimEntries);
                            bool? referenceMeasurement = false;
                            if (flags.Contains("Reference"))
                            {
                                // MultiCAD uses the same flag for a measurement and for a driver
                                // calculated from other variables. Match the expression-graph writer.
                                try { referenceMeasurement = ModelArithmetic.ParseStored(variable.Expression, cancel).References.Length == 0; }
                                catch (DomainException) { referenceMeasurement = null; }
                            }
                            if (referenceMeasurement == true) continue;
                            var linked = manager.GetConstraintParamId(id);
                            if (referenceMeasurement == false && flags.Contains("Dim2DConstraint") && (linked.IsNull || linked == info.VarId)
                                && variables.GetVariableHostObjectId(info.VarId) == inputs.Sketch.ID && double.IsFinite(info.Param)
                                && variables.GetVariableValue(info.VarId, out double v) && double.IsFinite(v))
                            {
                                // ConstraintInfo.Param is documented in millimeters/radians. A
                                // separately reported angular variable must agree in a known unit.
                                if (info.Type != ConstraintType.DimAngular) value = v;
                                else if (ModelParameterSnapshot.Near(v, info.Param) || ModelParameterSnapshot.Near(v * Math.PI / 180, info.Param)) value = info.Param;
                            }
                        }
                    }
                }
                var spec = Decode(info, value, refs);
                if (spec?.Kind == "fix" && refs.Length == 1) unclassifiedPoints.Remove(refs[0].ObjectId);
                existing.Add(new(id.ToString(), spec, targets));
            }
        }
        finally { variables?.Dispose(); }
        // A construction point can be the sketch's implicit fixed origin. A
        // position alone does not prove that role, or that the point can move.
        foreach (var id in unclassifiedPoints)
            existing.Add(new("unverified_construction_point_grounding", null, [id.ToString()]));
        string? LocalPoint(string id, string? point)
        {
            if (point == null) return null;
            return Guid.TryParse(id, out var guid) && members.TryGetValue(new McObjectId(guid), out var member)
                ? NativeSketchMembers385.EndpointName(inputs, member, point) : point;
        }
        var normalized = requested?.Select(s => s with
        {
            FirstPoint = LocalPoint(s.First, s.FirstPoint),
            SecondPoint = s.Second == null ? null : LocalPoint(s.Second, s.SecondPoint),
            Value = s.Kind == "angle" ? s.Value * 180 / Math.PI : s.Value
        }).ToArray();
        return SketchMobility.Plan(geometry, existing, normalized, complete, fixedGeometry, cancel, preserve, replacingDrivers: replace != null);
    }

    internal static SketchMobilityReport Plan(NativeSketchConstraintState385.Snapshot value, SketchConstraintSpec[] specs, Action cancel, McObjectId? replace = null, IReadOnlyList<SketchMobilityMotion>? preserve = null)
    {
        using var manager = NativeSketchMembers385.Manager(value.Sketch.Sketch);
        var ids = value.Relations.GetProperty("constraints").EnumerateArray()
            .Select(row => new McObjectId(Guid.Parse(row.GetProperty("id").GetString()!))).ToArray();
        return Plan(value.Inputs, manager, ids, true, specs, cancel, replace.HasValue ? new HashSet<McObjectId> { replace.Value } : null, preserve);
    }
}
