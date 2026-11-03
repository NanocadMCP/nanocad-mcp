using System.Globalization;
using System.Runtime.CompilerServices;
using NanoCAD.Contracts;
using Teigha.DatabaseServices;
using Teigha.Geometry;

namespace NanoCADBridge;

internal static class CameraObjectFraming
{
    internal static CameraView Frame(DocumentState state,CameraView view,string[] handles,double padding,Action cancel)
    {
        using var transaction=state.Document.Database.TransactionManager.StartTransaction();
        var db=state.Document.Database;
        var model=((BlockTable)transaction.GetObject(db.BlockTableId,OpenMode.ForRead))[BlockTableRecord.ModelSpace];
        var space=db.CurrentSpaceId;
        if(transaction.GetObject(space,OpenMode.ForRead) is not BlockTableRecord owner||!owner.IsLayout||owner.OwnerId!=db.BlockTableId||
            db.TileMode&&space!=model||!db.TileMode&&(space==model||view.viewport_number!=1))
            throw new DomainException("OUT_OF_SCOPE","Frame only the active Model space or the Paper sheet itself.");
        var bounds=new List<CameraBounds>();
        foreach(var handle in handles)
        {
            cancel();ulong bits=ulong.Parse(handle,NumberStyles.HexNumber,CultureInfo.InvariantCulture);
            if(!state.Document.Database.TryGetObjectId(new Handle(unchecked((long)bits)),out var id)||id.IsNull||!id.IsValid)
                throw new DomainException("OBJECT_NOT_FOUND","Framing handle was not found.",details:new{handle});
            if(id.IsErased||id.IsEffectivelyErased)throw new DomainException("OBJECT_ERASED","Framing source was erased.",details:new{handle});
            if(transaction.GetObject(id,OpenMode.ForRead) is not Entity entity||entity.OwnerId!=space)
                throw new DomainException("OUT_OF_SCOPE","Frame only existing top-level objects in the active native space.",details:new{handle});
            using var nativeDimension=CadNativeDimensionBinding357.Bind(entity);entity=nativeDimension.Value;
            var type=entity.GetType();
            if(type!=typeof(Line)&&type!=typeof(Arc)&&type!=typeof(Circle)&&type!=typeof(Polyline)&&type!=typeof(Polyline2d)&&type!=typeof(Polyline3d)&&type!=typeof(DBPoint)&&type!=typeof(Solid3d)&&
                type!=typeof(Ellipse)&&type!=typeof(Spline)&&type!=typeof(BlockReference)&&type!=typeof(DBText)&&type!=typeof(MText)&&
                type!=typeof(Table)&&type!=typeof(MLeader)&&type!=typeof(FeatureControlFrame)&&type!=typeof(Hatch)&&type!=typeof(Region)&&type!=typeof(Helix)&&
                type!=typeof(MInsertBlock)&&type!=typeof(RasterImage)&&type!=typeof(DwfReference)&&type!=typeof(DgnReference)&&type!=typeof(PdfReference)&&
                !DimensionGeometryReader.Supported(type)&&!(entity is Viewport sheetViewport&&type==typeof(Viewport)&&!db.TileMode&&sheetViewport.Number>1)&&!IsDrawingView(state,entity))
                throw new DomainException("UNSUPPORTED_ENTITY_TYPE","This entity type has no bounded framing adapter.",details:new{handle,type=type.FullName});
            var layer=(LayerTableRecord)transaction.GetObject(entity.LayerId,OpenMode.ForRead);
            if(!entity.Visible||layer.IsOff||layer.IsFrozen||entity is RasterImage raster&&!raster.ShowImage||entity is UnderlayReference underlay&&!underlay.IsOn)
                throw new DomainException("OBJECT_NOT_VISIBLE","Framing does not unhide objects or thaw their layers.",details:new{handle});
            try
            {
                var box=type==typeof(MInsertBlock)?CadMInsertBounds407.Read((MInsertBlock)entity,cancel):entity.GeometricExtents;
                bounds.Add(new(new(box.MinPoint.X,box.MinPoint.Y,box.MinPoint.Z),new(box.MaxPoint.X,box.MaxPoint.Y,box.MaxPoint.Z)));
            }
            catch(Teigha.Runtime.Exception error)
            {throw new DomainException("FRAME_BOUNDS_UNAVAILABLE","Native display bounds are unavailable; no objects were omitted.",details:new{handle,native_error=error.ErrorStatus.ToString()});}
            catch(DomainException error)when(type==typeof(MInsertBlock))
            {throw new DomainException("FRAME_BOUNDS_UNAVAILABLE","Conservative whole-array bounds are unavailable; seed bounds were not substituted.",details:new{handle,cause=error.Error});}
        }
        cancel();
        // Match the host's DCS convention instead of guessing an up axis.
        var direction=new Vector3d(view.direction_wcs[0],view.direction_wcs[1],view.direction_wcs[2]);
        var plane=Matrix3d.PlaneToWorld(direction);var x=Vector3d.XAxis.TransformBy(plane);var y=Vector3d.YAxis.TransformBy(plane);
        var frame=CameraFraming.Calculate(bounds.ToArray(),new(x.X,x.Y,x.Z),new(y.X,y.Y,y.Z),view.width/view.height,padding);
        // nanoCAD may retain ViewTarget and compensate through ViewCenter.
        // Express the requested frame in that existing target from the start,
        // avoiding a false mismatch without relaxing native readback equality.
        frame=CameraFraming.WithTarget(frame,new(view.target_wcs[0],view.target_wcs[1],view.target_wcs[2]),new(x.X,x.Y,x.Z),new(y.X,y.Y,y.Z));
        return view with{target_wcs=new[]{frame.Target.X,frame.Target.Y,frame.Target.Z},center_view=new[]{frame.Center.X,frame.Center.Y},width=frame.Width,height=frame.Height};
    }
    private static bool IsDrawingView(DocumentState state,Entity entity)
    {
        // Do not broaden framing to arbitrary proxy curves. The native bridge
        // must already be loaded and resolve this exact handle as a healthy view.
        if(entity.GetType().FullName!="Teigha.DatabaseServices.ImpCurve")return false;
        var loaded=AppDomain.CurrentDomain.GetAssemblies().Select(a=>a.GetName().Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        return loaded.Contains("mapimgd") && loaded.Contains("MapiBaseTypes") && MatchDrawingView(state,entity);
    }
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static bool MatchDrawingView(DocumentState state,Entity entity)
    {
        var doc=Multicad.DatabaseServices.McDocument.ActiveDocument;
        if(doc==null || !state.Document.IsNamedDrawing || !string.Equals(Path.GetFullPath(doc.Path),Path.GetFullPath(state.Document.Name),StringComparison.OrdinalIgnoreCase))
            throw new DomainException("DOCUMENT_CONTEXT_MISMATCH","Drawing-view framing requires the matching named MultiCAD document.");
        var id=Multicad.McObjectId.FromHandle(entity.Handle.Value,doc.ID);
        var view=id.IsNull?null:id.GetObjectOfType<Multicad.Mc3D.DrawingView>();
        if(view==null)return false;
        if(view.ID!=id || view.IsSuppressed || view.Status.ToString()!="Ok")
            throw new DomainException("FRAME_BOUNDS_UNAVAILABLE","Drawing view must have a valid identity and be healthy and unsuppressed.");
        return true;
    }
}
