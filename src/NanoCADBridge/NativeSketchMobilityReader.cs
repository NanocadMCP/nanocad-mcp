using Multicad;
using Multicad.DatabaseServices.Constraints;
using Multicad.Mc3D;
using NanoCAD.Contracts;
using System.Text.Json;

namespace NanoCADBridge;

internal static class NativeSketchMobilityReader
{
    // GetAllItemsGeometry includes abstract profile curves that need not be
    // independently editable DbGeometry objects. Read access grants no write token.
    internal static SketchMobilityReport Read(PlanarSketch sketch, Constraints2D manager,
        NativeConstraintInventory.Result inventory, SketchConstraintSpec[]? proposed, SketchMobilityMotion[]? preserve, Action cancel)
    {
        var frame = DrawingWorkplaneNative.FromSketch(sketch);
        var projections = sketch.GetContainingObjects(false, true, false);
        bool complete = inventory.Complete && projections != null;
        var projected = projections?.ToHashSet() ?? [];
        var budget = new ModelInventoryBudget(cancel);
        var geometry = Wire.Element(SketchGeometryReader.Read(sketch, cancel, item => budget.Add(item)));
        var members = new List<NativeSketchMembers385.Member>();
        var construction = new List<NativeSketchMembers385.Construction>();
        var observed = new HashSet<McObjectId>();
        foreach (var row in geometry.GetProperty("elements").EnumerateArray())
        {
            cancel();
            var id = new McObjectId(Guid.Parse(row.GetProperty("element_id").GetString()!));
            observed.Add(id);
            string nativeType = row.GetProperty("geometry_type").GetString() ?? "unknown";
            bool isReference = false;
            if (projected.Contains(id))
            {
                var projection = id.GetObjectOfType<ProjectionSketchEnt>();
                isReference = projection != null && projection.ID == id && projection.HostSketchID == sketch.ID &&
                    projection.DbEntity.Document?.ID == sketch.DbEntity.Document.ID && !projection.SrcObjectID.IsNull && !projection.IsSuppressed && projection.Status.ToString() == "Ok";
                if (!isReference) complete = false;
            }
            var shape = row.GetProperty("geometry");
            void Add(McObjectId member, int? segment, JsonElement detail)
            {
                var curve = detail.ValueKind == JsonValueKind.Object ? NativeSketchMembers385.LocalCurve(member.ToString(), detail, frame, cancel) : new(member.ToString(), "unsupported");
                members.Add(new(member, id, segment, nativeType, detail, curve, false, "geometry_only", isReference));
            }
            if (nativeType is "kPoint" or "kVertex")
            {
                using var actual = sketch.GetItemGeometry(id, false);
                if (actual != null && NativeSketchMembers385.IsPoint(actual))
                {
                    var point = actual.Point;
                    construction.Add(new(id, Wire.Element(new { kind = "point", point = new[] { point.X, point.Y, point.Z } })));
                }
                else complete = false;
            }
            else if (nativeType == "kPolyline" && shape.ValueKind == JsonValueKind.Object)
            {
                foreach (var part in shape.GetProperty("segments").EnumerateArray())
                {
                    int index = part.GetProperty("index").GetInt32();
                    Add(ConstraintInfo.MakePolylineSegmentId(id, index), index, part.GetProperty("geometry"));
                }
            }
            else Add(id, null, shape);
        }
        // A fixed construction point may appear only among constraint
        // operands. Its identity and geometry must belong to this sketch.
        foreach (var id in inventory.Ids)
        {
            cancel();
            using var info = manager.GetConstraintInfo(id);
            if (info?.Type != ConstraintType.GeomFix || info.FirstObject is not { } operand || operand.ObjectId.IsNull || observed.Contains(operand.ObjectId)) continue;
            using var item = sketch.GetItemGeometry(operand.ObjectId, false);
            if (item == null || !NativeSketchMembers385.IsPoint(item)) continue;
            var p = item.Point; observed.Add(operand.ObjectId);
            construction.Add(new(operand.ObjectId, Wire.Element(new { kind = "point", point = new[] { p.X, p.Y, p.Z } })));
        }
        complete &= projected.IsSubsetOf(observed);
        var inputs = new NativeSketchMembers385.Snapshot(sketch, frame, members.ToArray(), default, "", construction.ToArray(), []);
        return NativeSketchMobility.Plan(inputs, manager, inventory.Ids, complete, proposed, cancel, preserve: preserve, geometryOnly: true);
    }
}
