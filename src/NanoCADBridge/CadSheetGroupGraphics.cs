using NanoCAD.Contracts;
using Teigha.DatabaseServices;

namespace NanoCADBridge;

internal static partial class CadSheetPlacement
{
    // A border and a title block often share one DWG block. Reserve their visible primitives
    // in paper WCS so the blank interior of a rectangular sheet frame can carry views.
    private static List<SheetBox> ObstacleBoxes(Entity entity, SheetBox fallback, bool inspectFrame, Transaction tx, Action check)
    {
        if (entity is Viewport { On: false, NonRectClipOn: false })
        {
            // An off rectangular viewport can display its frame but has no projected contents.
            return [new(fallback.Left, fallback.Bottom, fallback.Right, fallback.Bottom),
                new(fallback.Left, fallback.Top, fallback.Right, fallback.Top),
                new(fallback.Left, fallback.Bottom, fallback.Left, fallback.Top),
                new(fallback.Right, fallback.Bottom, fallback.Right, fallback.Top)];
        }
        if (entity is not (BlockReference or Polyline)) return [fallback];
        // Preserve the occupied rectangle of ordinary inserted symbols and standalone title blocks,
        // including their unfilled cells. Only open the interior of a verified enclosing sheet frame.
        if (entity is BlockReference && !inspectFrame) return [fallback];
        var boxes = new List<SheetBox>();
        var path = new HashSet<ObjectId>();
        bool Read(Entity e)
        {
            check();
            if (!e.Visible) return true;
            bool polyline = e is Polyline p && Enumerable.Range(0, p.NumberOfVertices)
                .All(i => p.GetStartWidthAt(i) == 0 && p.GetEndWidthAt(i) == 0);
            var reference = e as BlockReference;
            if (!polyline && (reference == null || !ReferenceEquals(e, entity)))
            {
                if (EntityBounds(e) is not { } b) return false;
                boxes.Add(b); return true;
            }
            if (reference != null && (reference is MInsertBlock || reference.BlockTableRecord.IsNull || !path.Add(reference.BlockTableRecord)))
                return false;
            try
            {
                if (reference != null)
                {
                    var definition = (BlockTableRecord)tx.GetObject(reference.BlockTableRecord, OpenMode.ForRead);
                    if (definition.IsFromExternalReference || definition.IsFromOverlayReference || definition.IsLayout) return false;
                    foreach (ObjectId attr in reference.AttributeCollection)
                    {
                        check();
                        if (tx.GetObject(attr, OpenMode.ForRead) is AttributeReference { Invisible: false } a && !Read(a)) return false;
                    }
                }
                using var pieces = new DBObjectCollection();
                try
                {
                    e.Explode(pieces);
                    if (pieces.Count == 0) return false;
                    foreach (DBObject piece in pieces)
                    {
                        if (piece is not Entity child || !child.ObjectId.IsNull || !Read(child)) return false;
                    }
                    return true;
                }
                finally { foreach (DBObject piece in pieces) piece.Dispose(); }
            }
            finally { if (reference != null) path.Remove(reference.BlockTableRecord); }
        }
        try
        {
            if (!Read(entity) || boxes.Count == 0) return [fallback];
            return entity is BlockReference ? SheetGroupPlacement.FrameObstacles(fallback, boxes, check) : boxes;
        }
        catch (Exception e) when (e is Teigha.Runtime.Exception or InvalidOperationException or NotSupportedException)
        { return [fallback]; }
    }

    private static List<object> GroupLabels(List<GroupDimension> dimensions, List<GroupView> views, SheetGroupMember[] members,
        Point2[]? positions, SheetBox[] obstacles, SheetBox? area, double clearance, Action check)
    {
        SheetBox Placed(SheetBox b, int owner) => positions == null ? b :
            b.Shift(positions[owner].X - members[owner].Origin.X, positions[owner].Y - members[owner].Origin.Y);
        var occupied = views.Select((v, i) => (Handle: v.Footprint.Handle, Box: Placed(v.Footprint.Box!.Value, i))).ToList();
        var labels = dimensions.SelectMany(d => (d.Footprint.Parts?.Marks ?? []).Where(mark => mark.Kind == "text")
            .Select(mark => (d.Footprint.Handle, Box: Placed(mark.Bounds, d.Owner)))).ToArray();
        var arrows = dimensions.SelectMany(d => (d.Footprint.Parts?.Marks ?? []).Where(mark => mark.Kind == "arrow_candidate")
            .Select(mark => (d.Footprint.Handle, Box: Placed(mark.Bounds, d.Owner)))).ToArray();
        var output = new List<object>();
        foreach (var d in dimensions.Where(d => d.Footprint.Parts == null || !d.Footprint.Parts.Marks.Any(m => m.Kind == "text")))
            output.Add(new { dimension_handle = d.Footprint.Handle, reason = "dimension_text_envelope_unavailable" });
        var reserved = new List<SheetBox>();
        foreach (var label in labels)
        {
            check();
            var others = occupied.Concat(arrows).Concat(labels.Where(l => l.Handle != label.Handle)).ToArray();
            var conflicts = others.Where(o => label.Box.Conflicts(o.Box, 0)).Select(o => o.Handle).Distinct().ToArray();
            if (conflicts.Length == 0 && !obstacles.Any(b => label.Box.Conflicts(b, 0))) continue;
            Point2? shift = null;
            if (area is { HasArea: true } sheet)
            {
                var allowed = new SheetBox(sheet.Left - label.Box.Left, sheet.Bottom - label.Box.Bottom,
                    sheet.Right - label.Box.Right, sheet.Top - label.Box.Top);
                shift = SheetPlacementSolver.Nearest(label.Box, others.Select(o => o.Box).Concat(obstacles).Concat(reserved).ToArray(),
                    allowed, new(0, 0), clearance, check);
                if (shift != null) reserved.Add(label.Box.Shift(shift.X, shift.Y));
            }
            output.Add(new
            {
                dimension_handle = label.Handle,
                reason = "dimension_text_envelope_overlap",
                conflicts_with = conflicts,
                overlaps_fixed_graphics = obstacles.Any(b => label.Box.Conflicts(b, 0)),
                measured_or_predicted_text_bounds = label.Box.Coordinates(),
                suggested_paper_displacement = shift == null ? null : new[] { shift.X, shift.Y },
                edit_operation = "set_dimension_text_position"
            });
        }
        return output;
    }
}
