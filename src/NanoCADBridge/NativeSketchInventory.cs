using Multicad;
using Multicad.DatabaseServices.Constraints;
using Multicad.Mc3D;
using NanoCAD.Contracts;

namespace NanoCADBridge;

internal static class NativeSketchInventory
{
    internal static NativeConstraintInventory.Result Read(Constraints2D manager, PlanarSketch? sketch, Action cancel)
    {
        IEnumerable<McObjectId>? Members()
        {
            var selected = sketch ?? manager.PlanarSketchId.GetObjectOfType<PlanarSketch>();
            if (selected == null || selected.ID != manager.PlanarSketchId) return null;
            var context = selected.IsInEditMode ? Multicad.DatabaseServices.McDocumentsManager.GetActiveSheet() : selected.DbEntity.Document;
            if (context == null || context.ID != manager.DocumentId) return null;
            var containing = selected.GetContainingObjects(true, true, true);
            var geometry = selected.GetAllItemsGeometry(false);
            try
            {
                if (containing != null) ModelInventoryBudget.Ids(containing, cancel);
                if (geometry != null) ModelInventoryBudget.Ids(geometry.Keys, cancel);
                return (containing?.AsEnumerable() ?? []).Concat(geometry?.Keys.AsEnumerable() ?? []).Distinct().ToArray();
            }
            finally
            {
                if (geometry != null)
                    foreach (var item in geometry.Values.Distinct(ReferenceEqualityComparer.Instance))
                        if (item is IDisposable disposable) disposable.Dispose();
            }
        }
        // A newly entered editor can expose an unlinked manager. Its primary
        // inventory has the existing creation semantics; an alternate empty
        // dictionary from an unlinked network cannot certify the target.
        if (manager.PlanarSketchId.IsNull || manager.ScopeId.IsNull)
        {
            var ids = manager.GetAllConstraints();
            if (ids != null) ModelInventoryBudget.Ids(ids, cancel);
            return new(ids ?? [], ids != null, "native_unlinked_editor_list", ids == null ? "native_scope_unlinked" : null);
        }
        return NativeConstraintInventory.Read(manager, Members, cancel);
    }

    internal static List<McObjectId>? CompleteIds(Constraints2D manager, Action cancel, PlanarSketch? sketch = null)
    {
        var observed = Read(manager, sketch, cancel);
        return observed.Complete ? observed.Ids : null;
    }
}
