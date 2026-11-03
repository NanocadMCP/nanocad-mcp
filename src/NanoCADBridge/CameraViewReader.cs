using NanoCAD.Contracts;
using HostMgd.ApplicationServices;
using Teigha.Geometry;
using Application = HostMgd.ApplicationServices.Application;

namespace NanoCADBridge;

internal sealed record CameraObservation(CameraView? view, string? view_unavailable_reason);

internal static class CameraViewReader
{
    internal static CameraObservation Read(string bridge, DocumentState state)
    {
        var document = state.Document;
        object number = Application.GetSystemVariable("CVPORT");
        int viewport = number switch { int n => n, short n => n, _ => throw new DomainException("VIEW_UNAVAILABLE", "Unexpected current viewport number.") };
        // An ordinary Paper camera observes the sheet itself. A model view
        // entered through its viewport has a different projection/ownership.
        if (!document.Database.TileMode && viewport != 1) return new(null,"paper_model_viewport_not_verified");
        using var native = document.Editor.GetCurrentView();
        if (!native.ObjectId.IsNull || !native.IsNewObject || !native.AutoDelete)
            throw new DomainException("VIEW_UNAVAILABLE", "Expected an owned detached camera record.");
        if (native.PerspectiveEnabled || native.FrontClipEnabled || native.BackClipEnabled)
            return new(null,"perspective_or_clipping_not_verified");
        var viewportId = document.Editor.CurrentViewportObjectId;
        // DXF ViewTableRecord.Width may retain the saved display aspect ratio.
        // SetCurrentView instead derives width from the current viewport. Report
        // that effective width consistently, including before the first change.
        object screenSize = Application.GetSystemVariable("SCREENSIZE");
        if (screenSize is not Point2d pixels || !double.IsFinite(pixels.X) || !double.IsFinite(pixels.Y) || pixels.X<=0 || pixels.Y<=0)
            throw new DomainException("VIEW_UNAVAILABLE","The active viewport pixel dimensions are unavailable.");
        var visualStyle = native.VisualStyleId;
        var target = native.Target; var direction = native.ViewDirection; var center = native.CenterPoint;
        var view = CameraViews.Create(bridge,state.Id,state.Revision,viewport,viewportId.IsNull ? null : viewportId.Handle.ToString(),
            new[]{target.X,target.Y,target.Z},new[]{direction.X,direction.Y,direction.Z},new[]{center.X,center.Y},
            native.Height*pixels.X/pixels.Y,native.Height,native.ViewTwist,native.FrontClipDistance,native.BackClipDistance,
            Multicad.DatabaseServices.McDocumentsManager.GetActiveSheet().ActiveView.ViewRenderMode.ToString(),visualStyle.IsNull?null:visualStyle.Handle.ToString());
        return new(view,null);
    }
}
