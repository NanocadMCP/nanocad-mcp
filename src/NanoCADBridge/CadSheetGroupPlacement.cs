using System.Text.Json;
using Multicad;
using Multicad.Mc3D;
using NanoCAD.Contracts;
using Teigha.DatabaseServices;

namespace NanoCADBridge;

internal static partial class CadSheetPlacement
{
    private sealed record GroupView(Entity Entity, McObjectId Id, DrawingView View, Footprint Footprint, int Parent, string Side);
    private sealed record GroupDimension(Footprint Footprint, int Owner);

    private static object ReadGroup(SheetPlacementRequest request, Layout layout, BlockTableRecord block, Transaction tx,
        List<(Entity Entity, McObjectId Id, DrawingView View)> views, Footprint? selected, List<Footprint> objects,
        SheetBox? area, SheetBox extent, Point2 preferred, Action check, int room, string instance, string documentId)
    {
        var reasons = new HashSet<string>(StringComparer.Ordinal);
        var rows = objects.ToDictionary(o => o.Handle);
        if (selected != null) rows.Add(selected.Handle, selected);
        var root = views.Single(v => v.Entity.Handle.ToString() == request.View);
        if (!root.View.ParentViewID.IsNull && root.View.AlignOnParent) reasons.Add("select_the_parent_of_this_aligned_view");
        var group = new List<GroupView>();
        var byId = views.ToDictionary(v => v.Id);
        var seen = new HashSet<McObjectId>();
        var pending = new Queue<(McObjectId Id, int Parent)>();
        pending.Enqueue((root.Id, -1));
        while (pending.TryDequeue(out var next))
        {
            check();
            if (!seen.Add(next.Id) || !byId.TryGetValue(next.Id, out var entry))
            { reasons.Add("child_inventory_inconsistent"); continue; }
            var handle = entry.Entity.Handle.ToString();
            if (!rows.TryGetValue(handle, out var footprint) || footprint.Box is not { HasArea: true })
            { reasons.Add("member_bounds_unavailable:" + handle); continue; }
            if (!Ordinary(entry.View) || !entry.View.EnableAutoUpdate) reasons.Add("member_requires_an_ordinary_updating_projection:" + handle);
            var layer = (LayerTableRecord)tx.GetObject(entry.Entity.LayerId, OpenMode.ForRead);
            if (entry.Entity.HasFields || layer.IsLocked || layer.IsDependent || string.IsNullOrWhiteSpace(entry.View.Name))
                reasons.Add("member_properties_unavailable:" + handle);
            string side = "root";
            if (next.Parent >= 0)
            {
                var parent = group[next.Parent].View;
                var origin = entry.View.ViewPlane.Origin; var p = parent.ViewPlane.Origin;
                double x = origin.X - p.X, y = origin.Y - p.Y;
                var requested = request.MemberSides.GetValueOrDefault(handle);
                side = Math.Abs(x) <= 1e-8 && Math.Abs(y) <= 1e-8 ? requested ?? "unknown" :
                    Math.Abs(x) <= 1e-8 ? y < 0 ? "below" : "above" :
                    Math.Abs(y) <= 1e-8 ? x < 0 ? "left" : "right" : "unknown";
                if (side == "unknown" || requested != null && requested != side) reasons.Add("alignment_axis_or_side_unresolved:" + handle);
            }
            int index = group.Count;
            group.Add(new(entry.Entity, entry.Id, entry.View, footprint, next.Parent, side));
            var children = entry.View.ChildViews;
            if (children == null) { reasons.Add("child_inventory_unavailable:" + handle); continue; }
            foreach (var child in children)
            {
                check();
                if (!byId.TryGetValue(child, out var linked) || linked.View.ParentViewID != entry.Id)
                { reasons.Add("child_inventory_inconsistent:" + handle); continue; }
                if (linked.View.AlignOnParent) pending.Enqueue((child, index));
            }
            if (views.Any(v => v.View.ParentViewID == entry.Id && v.View.AlignOnParent && !children.Contains(v.Id)))
                reasons.Add("child_inventory_inconsistent:" + handle);
        }
        foreach (string h in request.MemberSides.Keys)
            if (!group.Any(g => g.Parent >= 0 && g.Footprint.Handle == h)) reasons.Add("paper_side_supplied_for_nonchild:" + h);
        var indexByHandle = group.Select((g, index) => (g.Footprint.Handle, index)).ToDictionary(p => p.Handle, p => p.index);
        var dimensions = new List<GroupDimension>();
        var fixedItems = new List<(Footprint Item, List<SheetBox> Boxes)>();
        bool frameSeen = false;
        var table = (BlockTable)tx.GetObject(block.Database.BlockTableId, OpenMode.ForRead);
        foreach (ObjectId id in block)
        {
            check();
            string h = id.Handle.ToString();
            if (!rows.TryGetValue(h, out var item) || indexByHandle.ContainsKey(h)) continue;
            var e = (Entity)tx.GetObject(id, OpenMode.ForRead);
            if (item.Box == null) { reasons.Add("obstacle_bounds_unavailable:" + h); continue; }
            if (e is Dimension dim)
            {
                var summary = EntityAdapter.Summary(dim, table[BlockTableRecord.ModelSpace], instance, documentId, true);
                var association = CadProjectedDimensionAssociationReader.ReadLoaded(dim, summary, check, includeDesignSource: false);
                if (association != null)
                {
                    var stored = Wire.Element(association);
                    if (!stored.GetProperty("links_complete").GetBoolean())
                        reasons.Add("dimension_view_anchors_unavailable:" + h);
                    else
                    {
                        var attached = stored.GetProperty("links").EnumerateArray()
                            .Select(link => link.GetProperty("view_ref").GetProperty("handle").GetString()!).Distinct().ToArray();
                        if (attached.Length == 1 && indexByHandle.TryGetValue(attached[0], out int owner))
                        { dimensions.Add(new(item, owner)); continue; }
                        if (attached.Any(indexByHandle.ContainsKey)) reasons.Add("dimension_spans_multiple_views:" + h);
                    }
                }
            }
            bool requestedFrame = request.FrameHandle == h && e is BlockReference;
            frameSeen |= requestedFrame;
            fixedItems.Add((item, ObstacleBoxes(e, item.Box.Value, requestedFrame || item.Box.Value.Contains(extent) && extent.HasArea, tx, check)));

        }
        if (request.FrameHandle != null && !frameSeen) reasons.Add("frame_handle_is_not_a_visible_fixed_block:" + request.FrameHandle);
        var members = group.Select((g, index) =>
        {
            var origin = g.View.ViewPlane.Origin;
            var reserve = g.Footprint.Box!.Value.Expand(request.AnnotationMargin);
            foreach (var d in dimensions.Where(d => d.Owner == index)) reserve = reserve.Union(d.Footprint.Box!.Value);
            return new SheetGroupMember(g.Footprint.Handle, g.Parent, g.Side, new(origin.X, origin.Y), reserve);
        }).ToArray();
        if (area is not { HasArea: true } || !extent.HasArea) reasons.Add("usable_sheet_extent_unavailable");
        var obstacles = fixedItems.SelectMany(item => item.Boxes).Concat(request.ReservedAreas).ToArray();
        var plan = reasons.Count == 0 ? SheetGroupPlacement.Fit(members, obstacles, area!.Value, preferred, request.Clearance, check, request.CompactSpacing) : null;
        if (plan == null && reasons.Count == 0) reasons.Add("no_fit_with_compact_or_current_spacing");
        bool changed = plan != null && members.Where((m, i) => Math.Abs(plan[i].X - m.Origin.X) > 1e-8 || Math.Abs(plan[i].Y - m.Origin.Y) > 1e-8).Any();
        var annotations = GroupLabels(dimensions, group, members, plan, obstacles, area, request.Clearance, check);
        var operations = !changed ? [] : group.Select((g, i) => new
        {
            operation = "set_drawing_view_properties",
            layout_handle = layout.Handle.ToString(),
            handle = g.Footprint.Handle,
            expected_name = g.View.Name,
            origin = new[] { plan![i].X, plan[i].Y }
        }).ToArray();
        if (request.Start > fixedItems.Count) throw new DomainException("INVALID_ARGUMENT", "start_index exceeds the fixed sheet objects.");
        var result = new Dictionary<string, object?>
        {
            ["mode"] = "aligned_group",
            ["coordinate_system"] = "paper_wcs_xy",
            ["view_handle"] = request.View,
            ["available_area"] = area?.Coordinates(),
            ["reserved_areas"] = request.ReservedAreas.Select(b => b.Coordinates()).ToArray(),
            ["clearance"] = request.Clearance,
            ["annotation_margin"] = request.AnnotationMargin,
            ["fit_found"] = plan != null,
            ["unavailable_reasons"] = reasons.OrderBy(value => value, StringComparer.Ordinal).ToArray(),
            ["placement_changed"] = changed,
            ["suggested_operations"] = operations,
            ["members"] = group.Select((g, i) => new
            {
                handle = g.Footprint.Handle,
                parent_handle = g.Parent < 0 ? null : group[g.Parent].Footprint.Handle,
                side = g.Side,
                current_origin = new[] { members[i].Origin.X, members[i].Origin.Y },
                suggested_origin = plan == null ? null : new[] { plan[i].X, plan[i].Y },
                current_view_bounds = g.Footprint.Box!.Value.Coordinates(),
                current_reserved_bounds = members[i].Envelope.Coordinates(),
                attached_dimensions = dimensions.Where(d => d.Owner == i).Select(d => d.Footprint.Handle).ToArray()
            }).ToArray(),
            ["label_conflicts"] = annotations,
            ["total_fixed_objects"] = fixedItems.Count,
            ["start_index"] = request.Start,
            ["scope"] = "Ordinary aligned views at their existing scale. Compact or retain spacing while preserving alignment axes and sides. Projected dimensions with verified same-view anchors reserve their measured envelope; independent paper annotations remain fixed. Suggested label positions use measured text bounds and require separate inspection; native regeneration can reflow text. Verify sheet placement again and inspect capture_view after grouped moves and model edits."
        };
        int budget = room - JsonSerializer.SerializeToUtf8Bytes(result, Wire.Json).Length - 256;
        if (budget <= 0) throw new DomainException("RESPONSE_LIMIT_EXCEEDED", "Complete group members and property operations exceed the available response bytes.");
        var fixedPage = new List<object>(); int nextIndex = request.Start;
        for (; nextIndex < fixedItems.Count && fixedPage.Count < request.Size; nextIndex++)
        {
            check(); var o = fixedItems[nextIndex];
            var row = new { handle = o.Item.Handle, kind = o.Item.Kind, bounds = o.Item.Box!.Value.Coordinates(), measured_obstacle_parts = o.Boxes.Count };
            int bytes = JsonSerializer.SerializeToUtf8Bytes(row, Wire.Json).Length + 1;
            if (bytes > budget)
            {
                if (fixedPage.Count == 0) throw new DomainException("RESPONSE_LIMIT_EXCEEDED", "One fixed sheet item exceeds the remaining response space.");
                break;
            }
            budget -= bytes; fixedPage.Add(row);
        }
        result["fixed_objects"] = fixedPage;
        result["next_index"] = nextIndex < fixedItems.Count ? nextIndex : null;
        result["complete"] = nextIndex == fixedItems.Count;
        return result;
    }
}
