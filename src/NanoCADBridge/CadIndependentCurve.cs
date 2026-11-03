using System.Runtime.CompilerServices;
using NanoCAD.Contracts;
using Teigha.DatabaseServices;

namespace NanoCADBridge;

// New curvature edits must not silently bypass a drawing solver or an owning
// native model. Existing legacy geometric operations keep their prior scope.
internal static class CadIndependentCurve
{
    internal static void Require(Entity source,Database db,Transaction tx,Action cancel)
    {
        if(source.HasFields||CadDrawingGroups431.HasNonOrganizationalReactors(source,tx,cancel))
            throw new DomainException("UNSUPPORTED_CURVE_DEPENDENCIES","Direct curvature edits require an independent curve; use its constraints/model adapter for a linked source.");
        RequireModelIndependence(source,db,tx,cancel);
    }
    internal static void RequireModelIndependence(Entity source,Database db,Transaction tx,Action cancel)
    {
        var loaded=AppDomain.CurrentDomain.GetAssemblies().Select(a=>a.GetName().Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        if(loaded.Contains("mapimgd")&&loaded.Contains("MapiBaseTypes"))CheckModels(source,db,tx,cancel);
    }
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void CheckModels(Entity source,Database db,Transaction tx,Action cancel)
    {
        var document=Multicad.DatabaseServices.McDocument.ActiveDocument;
        if(document==null||!string.IsNullOrEmpty(db.Filename)&&!string.Equals(Path.GetFullPath(db.Filename),Path.GetFullPath(document.Path),StringComparison.OrdinalIgnoreCase))
            throw new DomainException("DOCUMENT_CONTEXT_MISMATCH","Cannot establish the matching MultiCAD document for curvature editing.");
        var sourceId=Multicad.McObjectId.FromHandle(source.Handle.Value,document.ID);if(sourceId.IsNull)return;
        var model=((BlockTable)tx.GetObject(db.BlockTableId,OpenMode.ForRead))[BlockTableRecord.ModelSpace];
        var inventory=new CadContourBudget344(cancel);
        foreach(ObjectId id in (BlockTableRecord)tx.GetObject(model,OpenMode.ForRead))
        {
            cancel();if(id.IsNull||!id.IsValid||id.IsErased||id.IsEffectivelyErased)continue;
            inventory.Add(id.Handle.ToString());
            if(tx.GetObject(id,OpenMode.ForRead) is not Solid3d)continue;
            var body=Multicad.McObjectId.FromHandle(id.Handle.Value,document.ID).GetObjectOfType<Multicad.Mc3D.Mc3dSolid>();if(body==null)continue;
            var history=body.GetPartContents(false);
            if(history==null)throw new DomainException("HISTORY_UNAVAILABLE","Cannot prove curve independence from an unavailable native model history.");
            foreach(var item in history)
            {
                cancel();inventory.Add(new{body=id.Handle.ToString(),history_item=item.ToString()});
                if(item==sourceId||item.GetObjectOfType<Multicad.Mc3D.ProjectionSketchEnt>() is {} p&&p.SrcObjectID==sourceId||
                    item.GetObjectOfType<Multicad.Mc3D.RevolveFeature>() is {} f&&f.Axis.ID==sourceId)
                    throw new DomainException("UNSUPPORTED_CURVE_DEPENDENCIES","Curve belongs to a native model; use drawing dimensions or the model sketch adapter.");
            }
            foreach(var reference in NativePathSources.ReferencedIds(history,cancel))
            {
                inventory.Add(reference);
                if(string.Equals(reference,sourceId.ToString(),StringComparison.OrdinalIgnoreCase))
                    throw new DomainException("UNSUPPORTED_CURVE_DEPENDENCIES","Curve supports a native model sketch/path; use its owning edit route.");
            }
        }
    }
}
