using System.Text.Json;
using Multicad;
using Multicad.Mc3D;
using NanoCAD.Contracts;
using Teigha.DatabaseServices;
using G = Multicad.Geometry;

namespace NanoCADBridge;

internal static partial class CadSheetPlacement
{
    private sealed record Mark(string Kind, SheetBox Bounds);
    private sealed record Parts(string Status, List<Mark> Marks);
    private sealed record Footprint(string Handle, string Kind, string Source, SheetBox? Box, Parts? Parts = null);

    internal static object Read(SheetPlacementRequest request, Layout layout, BlockTableRecord block, Transaction tx,
        List<(Entity Entity, McObjectId Id, DrawingView View)> views, Action check, int responseRoom, string instance, string documentId)
    {
        if (layout.ModelType) throw new DomainException("OUT_OF_SCOPE", "Sheet placement requires an explicit Paper layout.");
        var target = views.SingleOrDefault(v => v.Entity.Handle.ToString() == request.View);
        if (target.View == null) throw new DomainException("OUT_OF_SCOPE", "Choose a drawing view from this layout.");
        var byId = views.ToDictionary(v => v.Entity.ObjectId);
        var origin = target.View.ViewPlane.Origin;
        var proposed = request.Proposed ?? new Point2(origin.X, origin.Y);
        var delta = new Point2(proposed.X - origin.X, proposed.Y - origin.Y);
        var limits = layout.Limits;
        var extent = new SheetBox(limits.MinPoint.X, limits.MinPoint.Y, limits.MaxPoint.X, limits.MaxPoint.Y);
        var paper = layout.PlotPaperSize;
        SheetBox? area = extent.HasArea ? extent : null;
        if (request.Area is { } requested)
            area = area is { } sheet ? new SheetBox(Math.Max(sheet.Left, requested.Left), Math.Max(sheet.Bottom, requested.Bottom),
                Math.Min(sheet.Right, requested.Right), Math.Min(sheet.Top, requested.Top)) : requested;
        var objects = new List<Footprint>();
        Footprint? selected = null;
        foreach (ObjectId id in block)
        {
            check();
            if (id.IsErased || id.IsEffectivelyErased || tx.GetObject(id, OpenMode.ForRead) is not Entity e) continue;
            var layer = (LayerTableRecord)tx.GetObject(e.LayerId, OpenMode.ForRead);
            if (!e.Visible || layer.IsOff || layer.IsFrozen || e is Viewport { Number: 1 }) continue;
            var handle = e.Handle.ToString();
            Footprint row;
            if (byId.TryGetValue(id, out var entry))
            {
                if (entry.View.IsSuppressed) continue;
                var projection = Ordinary(entry.View) ? Projection(entry.View, check) : null;
                row = projection is { } b ? new(handle, "drawing_view", "projected_wcs_curves", b)
                    : new(handle, "drawing_view", "native_entity_geometric_extents", EntityBounds(e));
            }
            else
            {
                var entityBounds = EntityBounds(e);
                var parts = e is Dimension dimension ? DimensionParts(dimension, check) :
                    e is DBText or MText && entityBounds.HasValue ? new Parts("entity", [new("text", entityBounds.Value)]) : null;
                row = new(handle, id.ObjectClass.DxfName, "native_entity_geometric_extents", entityBounds, parts);
            }
            if (id == target.Entity.ObjectId) selected = row;
            else objects.Add(row);
        }
        if (request.IncludeAlignedGroup)
            return ReadGroup(request, layout, block, tx, views, selected, objects, area, extent, proposed, check, responseRoom, instance, documentId);
        var at = selected?.Box?.Shift(delta.X, delta.Y);
        int unknown = objects.Count(o => o.Box == null);
        var fixedBoxes = objects.Where(o => o.Box.HasValue).Select(o => o.Box!.Value).ToArray();
        int knownConflicts = at == null ? 0 : fixedBoxes.Count(b => at.Value.Conflicts(b, request.Clearance));
        bool? inside = at != null && area != null ? area.Value.HasArea && area.Value.Contains(at.Value) : null;
        string? why = null;
        Point2? suggested = null;
        string axis = "free";
        bool? placementAligned = null;
        if (selected?.Box is not { HasArea: true } bounds) why = "target_visible_bounds_unavailable";
        else if (!Ordinary(target.View)) why = "target_requires_an_ordinary_orthographic_projection";
        else if (area is not { HasArea: true } usable) why = "usable_paper_area_empty_or_unknown";
        else
        {
            var allowed = new SheetBox(usable.Left - bounds.Left, usable.Bottom - bounds.Bottom,
                usable.Right - bounds.Right, usable.Top - bounds.Top);
            var children = target.View.ChildViews;
            if (children == null || children.Any(id => !views.Any(v => v.Id == id && !v.View.AlignOnParent)))
                why = "aligned_descendants_require_a_group_placement";
            else if (target.View.AlignOnParent && !target.View.ParentViewID.IsNull)
            {
                var parent = views.SingleOrDefault(v => v.Id == target.View.ParentViewID).View;
                if (parent == null) why = "parent_view_unavailable";
                else
                {
                    var parentOrigin = parent.ViewPlane.Origin;
                    // Retain the observed drawing axis and side. Projection conventions can reverse placement.
                    double vx = origin.X - parentOrigin.X, vy = origin.Y - parentOrigin.Y;
                    if (Math.Abs(vx) <= 1e-8 && Math.Abs(vy) > 1e-8)
                    {
                        axis = "vertical";
                        placementAligned = Math.Abs(delta.X) <= 1e-8 && (origin.Y + delta.Y - parentOrigin.Y) * vy > 0;
                        allowed = new(Math.Max(0, allowed.Left), vy > 0 ? Math.Max(-vy, allowed.Bottom) : allowed.Bottom,
                            Math.Min(0, allowed.Right), vy < 0 ? Math.Min(-vy, allowed.Top) : allowed.Top);
                    }
                    else if (Math.Abs(vy) <= 1e-8 && Math.Abs(vx) > 1e-8)
                    {
                        axis = "horizontal";
                        placementAligned = Math.Abs(delta.Y) <= 1e-8 && (origin.X + delta.X - parentOrigin.X) * vx > 0;
                        allowed = new(vx > 0 ? Math.Max(-vx, allowed.Left) : allowed.Left, Math.Max(0, allowed.Bottom),
                            vx < 0 ? Math.Min(-vx, allowed.Right) : allowed.Right, Math.Min(0, allowed.Top));
                    }
                    else why = "parent_alignment_axis_unresolved";
                }
            }
            else placementAligned = true;
            if (why == null)
            {
                suggested = SheetPlacementSolver.Nearest(bounds, fixedBoxes, allowed, delta, request.Clearance, check);
                if (suggested == null) why = "no_clear_translation_within_area_and_alignment";
            }
        }
        if (request.Start > objects.Count) throw new DomainException("INVALID_ARGUMENT", "start_index exceeds this layout's obstacle inventory.");
        var rows = new List<object>();
        int bytes = 2048;
        for (int i = request.Start; i < objects.Count && rows.Count < request.Size; i++)
        {
            check();
            var o = objects[i];
            object? annotation = o.Parts == null ? null : new
            {
                source = o.Parts.Status,
                components = o.Parts.Marks.Select(mark => new
                {
                    kind = mark.Kind,
                    bounds = mark.Bounds.Coordinates(),
                    conflicts_at_proposed_origin = at?.Conflicts(mark.Bounds, request.Clearance),
                    overlaps_other_measured_annotation = OtherAnnotationOverlap(objects, o, mark.Bounds, check)
                }).ToArray()
            };
            var row = new
            {
                handle = o.Handle,
                kind = o.Kind,
                bounds = o.Box?.Coordinates(),
                source = o.Source,
                annotation,
                conflicts_at_proposed_origin = at is { } box && o.Box is { } obstacle ? (bool?)box.Conflicts(obstacle, request.Clearance) : null,
                unavailable_reason = o.Box == null ? "native_bounds_unavailable" : null
            };
            int size = JsonSerializer.SerializeToUtf8Bytes(row, Wire.Json).Length + 1;
            if (size > responseRoom - bytes)
            {
                if (rows.Count == 0) throw new DomainException("RESPONSE_LIMIT_EXCEEDED", "Layout metadata leaves insufficient room for one obstacle.");
                break;
            }
            rows.Add(row); bytes += size;
        }
        int next = request.Start + rows.Count;
        bool provenClear = extent.HasArea && at is { HasArea: true } && inside == true && placementAligned == true && knownConflicts == 0 && unknown == 0 && why == null;
        return new
        {
            coordinate_system = "paper_wcs_xy",
            unit = "drawing_units",
            footprint_method = "axis_aligned_envelopes",
            scope = "Visible top-level layout geometry. Whole dimension and insert envelopes are conservative. Detached dimension text has measured glyph envelopes. Filled marks and block references are arrow candidates; line-based ticks are unclassified. Component overlaps use axis-aligned bounds and zero clearance between annotations. Suggestions hold other entities stationary.",
            layout_limits = extent.HasArea ? extent.Coordinates() : null,
            media_size = new[] { paper.X, paper.Y },
            area_source = extent.HasArea ? request.Area != null ? "layout_limits_intersected_with_requested_area" : "layout_limits" : "requested_area_only",
            media_name = layout.CanonicalMediaName,
            media_units = layout.PlotPaperUnits.ToString(),
            media_rotation = layout.PlotRotation.ToString(),
            available_area = area is { HasArea: true } ? area.Value.Coordinates() : null,
            clearance = request.Clearance,
            view_handle = request.View,
            current_origin = new[] { origin.X, origin.Y },
            proposed_origin = new[] { proposed.X, proposed.Y },
            current_bounds = selected?.Box?.Coordinates(),
            bounds_source = selected?.Source,
            proposed_bounds = at?.Coordinates(),
            inside_area = inside,
            alignment = axis,
            preserves_alignment = placementAligned,
            known_conflicts = knownConflicts,
            unknown_obstacles = unknown,
            bounds_clear = provenClear,
            suggested_origin = suggested == null ? null : new[] { origin.X + suggested.X, origin.Y + suggested.Y },
            suggestion_scope = suggested == null ? null : !extent.HasArea ? "conditional_on_requested_area" :
                unknown == 0 ? "checked_against_all_observed_envelopes" : "conditional_on_unavailable_bounds",
            suggestion_unavailable_reason = why,
            total_obstacles = objects.Count,
            start_index = request.Start,
            next_index = next < objects.Count ? (int?)next : null,
            complete = next == objects.Count,
            obstacles = rows
        };
    }

    private static bool Ordinary(DrawingView v) => v.Status.ToString() == "Ok" && !v.IsSuppressed && !v.PreviewMode &&
        v.SectionID.IsNull && !v.IsSlice && !v.IsBasedOnSection && v.OuterCutSketch.IsNull &&
        v.AttachedLocalCuts is { Length: 0 } && v.Attached2dBreaks is { Length: 0 } &&
        v.DirectionType.ToString() is "kViewFront" or "kViewTop" or "kViewBottom" or "kViewLeft" or "kViewRight" or "kViewBack";

    private static SheetBox? EntityBounds(Entity entity)
    {
        try
        {
            var b = entity.GeometricExtents;
            var result = new SheetBox(b.MinPoint.X, b.MinPoint.Y, b.MaxPoint.X, b.MaxPoint.Y);
            return result.Valid ? result : null;
        }
        catch (Exception e) when (e is Teigha.Runtime.Exception or InvalidOperationException or NotSupportedException) { return null; }
    }

    private static Parts DimensionParts(Dimension dimension, Action check)
    {
        using var pieces = new DBObjectCollection();
        var marks = new List<Mark>();
        bool unavailable = false;
        try
        {
            dimension.Explode(pieces);
            foreach (DBObject piece in pieces)
            {
                check();
                if (piece is not Entity item || !item.ObjectId.IsNull) { unavailable = true; continue; }
                string? kind = item switch { DBText or MText => "text", Solid or BlockReference => "arrow_candidate", _ => null };
                if (kind == null) continue;
                if (EntityBounds(item) is { } b) marks.Add(new(kind, b)); else unavailable = true;
            }
            return new(unavailable ? "partial_detached_dimension_graphics" : marks.Count == 0 ? "no_measurable_text_or_arrow_candidates" : "detached_dimension_graphics", marks);
        }
        catch (Exception e) when (e is Teigha.Runtime.Exception or InvalidOperationException or NotSupportedException)
        {
            return new("dimension_graphics_unavailable", marks);
        }
        finally { foreach (DBObject piece in pieces) piece.Dispose(); }
    }

    private static bool OtherAnnotationOverlap(List<Footprint> objects, Footprint current, SheetBox box, Action check)
    {
        foreach (var other in objects)
        {
            check();
            if (other.Handle != current.Handle && other.Parts != null && other.Parts.Marks.Any(m => box.Conflicts(m.Bounds, 0)))
                return true;
        }
        return false;
    }

    // MAPI 3DCore.h getViewGeometry(true,0) returns WCS geometry. Ordinary() excludes breaks and cuts.
    private static SheetBox? Projection(DrawingView view, Action check)
    {
        try
        {
            var elements = view.GetViewGeometry(true, 0);
            if (elements == null) return null;
            double left = double.PositiveInfinity, bottom = double.PositiveInfinity, right = double.NegativeInfinity, top = double.NegativeInfinity;
            void Add(double x, double y)
            {
                left = Math.Min(left, x); right = Math.Max(right, x);
                bottom = Math.Min(bottom, y); top = Math.Max(top, y);
            }
            try
            {
                foreach (var element in elements)
                {
                    check();
                    if (element?.GeometryType == G.EntityGeomType.kLine)
                    {
                        using var line = element.LineSeg;
                        Add(line.StartPoint.X, line.StartPoint.Y); Add(line.EndPoint.X, line.EndPoint.Y);
                    }
                    else if (element?.GeometryType == G.EntityGeomType.kCircArc)
                    {
                        using var arc = element.CircArc;
                        var n = arc.Normal; var u = arc.RefVector; var c = arc.Center;
                        if (Math.Abs(n.X) > 1e-8 || Math.Abs(n.Y) > 1e-8 || Math.Abs(Math.Abs(n.Z) - 1) > 1e-8 || Math.Abs(u.Z) > 1e-8) return null;
                        double lo = Math.Min(arc.StartAng, arc.EndAng), hi = Math.Max(arc.StartAng, arc.EndAng);
                        if (!double.IsFinite(lo) || !double.IsFinite(hi) || !(arc.Radius > 0)) return null;
                        void At(double a) => Add(c.X + arc.Radius * (u.X * Math.Cos(a) - n.Z * u.Y * Math.Sin(a)),
                            c.Y + arc.Radius * (u.Y * Math.Cos(a) + n.Z * u.X * Math.Sin(a)));
                        At(lo); At(hi);
                        foreach (double extremum in new[] { Math.Atan2(-n.Z * u.Y, u.X), Math.Atan2(n.Z * u.X, u.Y) })
                            for (int half = 0; half < 2; half++)
                            {
                                double angle = extremum + half * Math.PI;
                                if (angle + Math.Ceiling((lo - angle) / Math.Tau) * Math.Tau <= hi) At(angle);
                            }
                    }
                    else return null;
                }
                var box = new SheetBox(left, bottom, right, top);
                return box.Valid ? box : null;
            }
            finally
            {
                foreach (var item in elements.Distinct(ReferenceEqualityComparer.Instance))
                    if (item is IDisposable owned) owned.Dispose();
            }
        }
        catch (Exception e) when (e is Teigha.Runtime.Exception or InvalidOperationException or NotSupportedException) { return null; }
    }
}
