using Multicad.Mc3D;
using NanoCAD.Contracts;

namespace NanoCADBridge;

// Apply only to the selected mutation target. Read-only sketch inspection and
// retained-peer observations continue to use their existing native readers.
internal static class DrawingViewSketchWriteScope282
{
    internal static void RequireSupported(PlanarSketch sketch)
    {
        bool local = sketch.IsLocalCutSketch, outer = sketch.IsDrawingViewCutSketch;
        var localView = sketch.ViewIdForLocalCut; var outerView = sketch.ViewIdForOuterCut;
        var parentId = sketch.DbEntity.ParentId;
        bool parentIsView = !local && !outer && localView.IsNull && outerView.IsNull && !parentId.IsNull &&
            (parentId.IdentifiesObjectOfType<DrawingView>() || parentId.GetObjectOfType<DrawingView>() != null);
        if (local || outer || !localView.IsNull || !outerView.IsNull || parentIsView)
            throw new DomainException("UNSUPPORTED_DRAWING_VIEW_CUT_WRITE", "Writes to local/outer-cut or drawing-view-owned sketches are excluded.", false,
                new
                {
                    sketch_id = sketch.ID.ToString(),
                    is_local_cut = local,
                    is_outer_cut = outer,
                    local_view_id = localView.IsNull ? null : localView.ToString(),
                    outer_view_id = outerView.IsNull ? null : outerView.ToString(),
                    parent_id = parentId.IsNull ? null : parentId.ToString(),
                    parent_is_drawing_view = parentIsView
                });
    }
}
