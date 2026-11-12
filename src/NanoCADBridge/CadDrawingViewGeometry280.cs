using Multicad;
using Multicad.DatabaseServices;
using Multicad.Mc3D;
using G = Multicad.Geometry;

namespace NanoCADBridge;

// A successful native clipping provider may return no edges. This observation
// proves the current closed-polyline outer contour; it does not certify the
// visual quality of that crop or turn a missing provider result into geometry.
internal static class CadDrawingViewGeometry280
{
    internal static bool AllowsEmptyOuterProjection(DrawingView view, DocumentState state, Action cancel)
    {
        cancel();
        var drawing = CadDrawingViewEditState.Context(state); var top = drawing.TopDoc;
        string revision = state.Revision;
        var viewId = view.ID; var owner = view.DbEntity.DocumentID; var cutId = view.OuterCutSketch;
        if (viewId.IsNull || owner.IsNull || cutId.IsNull || top == null || top.ID.IsNull ||
            view.PreviewMode || view.Status.ToString() != "Ok" || view.IsSuppressed ||
            (view.DbEntity.StatusFlags & McDbObjectStateFlags.Erased) != 0) return false;
        var children = view.DbEntity.ChildrenIds?.ToArray();
        if (children == null || children.Count(id => id == cutId) != 1) return false;
        var sketch = cutId.GetObjectOfType<PlanarSketch>();
        if (sketch == null || sketch.ID != cutId || sketch.Status.ToString() != "Ok" || sketch.IsSuppressed ||
            sketch.IsBeyondEndOfPart || sketch.IsInEditMode ||
            (sketch.DbEntity.StatusFlags & McDbObjectStateFlags.Erased) != 0 ||
            sketch.DbEntity.DocumentID != owner || sketch.DbEntity.ParentId != viewId ||
            !sketch.IsDrawingViewCutSketch || sketch.ViewIdForOuterCut != viewId ||
            (!cutId.IdHostDoc.IsNull && cutId.IdHostDoc != owner) ||
            (!cutId.IdHostTopDoc.IsNull && cutId.IdHostTopDoc != top.ID)) return false;

        var inputs = sketch.GetContainingObjects(true, true, true)?.ToArray();
        var geometry = sketch.GetAllItemsGeometry(false);
        if (geometry == null) return false;
        try
        {
            // The complete map must be exactly one closed native polyline,
            // rather than an inferred loop assembled from disconnected edges.
            if (inputs is not [var input] || input.IsNull || !sketch.ContainsObject(input) ||
                geometry.Count != 1 || !geometry.TryGetValue(input, out var mapped) ||
                mapped == null || mapped.GeometryType != G.EntityGeomType.kPolyline) return false;
            using var item = sketch.GetItemGeometry(input, false);
            if (item == null || item.GeometryType != G.EntityGeomType.kPolyline) return false;
            using var polyline = item.Polyline;
            if (!polyline.IsClosed() || !ModelParameterSnapshot.Equivalent(
                NativeSketchPolylineGeometry.Read(mapped, cancel), NativeSketchPolylineGeometry.Read(item, cancel))) return false;

            cancel(); var current = CadDrawingViewEditState.Context(state);
            return state.Revision == revision && current.ID == drawing.ID && current.TopDoc?.ID == top.ID &&
                view.ID == viewId && view.DbEntity.DocumentID == owner && view.OuterCutSketch == cutId &&
                view.DbEntity.ChildrenIds != null && view.DbEntity.ChildrenIds.SequenceEqual(children) &&
                !view.PreviewMode && view.Status.ToString() == "Ok" && !view.IsSuppressed &&
                (view.DbEntity.StatusFlags & McDbObjectStateFlags.Erased) == 0 &&
                sketch.ID == cutId && sketch.DbEntity.DocumentID == owner && sketch.DbEntity.ParentId == viewId &&
                sketch.Status.ToString() == "Ok" && !sketch.IsSuppressed && !sketch.IsBeyondEndOfPart && !sketch.IsInEditMode &&
                (sketch.DbEntity.StatusFlags & McDbObjectStateFlags.Erased) == 0 &&
                sketch.IsDrawingViewCutSketch && sketch.ViewIdForOuterCut == viewId &&
                sketch.GetContainingObjects(true, true, true) is { } currentInputs && currentInputs.SequenceEqual(inputs);
        }
        finally
        {
            foreach (var item in geometry.Values.Distinct(ReferenceEqualityComparer.Instance))
                if (item is IDisposable owned) owned.Dispose();
        }
    }
}
