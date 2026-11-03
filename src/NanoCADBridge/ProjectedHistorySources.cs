using Multicad;
using Multicad.DatabaseServices;
using Multicad.Mc3D;
using NanoCAD.Contracts;
using Db=Teigha.DatabaseServices;

namespace NanoCADBridge;

// External projected curves are real source DBObjects, not HistoryItem wrappers.
// Never waive a missing history node merely by runtime name: prove its exact
// projection, owner sketch, host body and native database identity first.
internal static class ProjectedHistorySources
{
    internal sealed record Source(string id,string handle,string layer,double[] geometry,string[] projection_ids,string[] revolution_axis_ids, [property:System.Text.Json.Serialization.JsonIgnore(Condition=System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)] string[]? rotation_anchor_ids=null);
    internal static Source[] Read(Mc3dSolid body,McObjectId[] contents,DocumentState state,Db.Transaction tx,Action? cancel=null)
    {
        void Need(bool ok,string message){if(!ok)throw new DomainException("MODEL_STATE_UNAVAILABLE",message);}
        var projections=contents.Select(id=>id.GetObjectOfType<ProjectionSketchEnt>()).Where(p=>p!=null).ToArray();
        var rotations=contents.Select(id=>id.GetObjectOfType<AssocRotateFeature>()).Where(f=>f!=null).ToArray();
        Need(rotations.All(f=>f!.HostSolidID==body.ID),"Rotation ownership unavailable.");
        var revolutions=contents.Select(id=>id.GetObjectOfType<RevolveFeature>()).Where(f=>f!=null).ToArray();
        Need(revolutions.All(f=>f!.HostSolidID==body.ID&&!f.Axis.ID.IsNull),"Revolution axis binding or body owner is unavailable.");
        var result=new List<Source>();var budget=new ModelInventoryBudget(cancel);
        var model=((Db.BlockTable)tx.GetObject(state.Document.Database.BlockTableId,Db.OpenMode.ForRead))[Db.BlockTableRecord.ModelSpace];
        foreach(var sourceId in projections.Select(p=>p!.SrcObjectID).Concat(revolutions.Select(f=>f!.Axis.ID)).Concat(rotations.Select(f=>f!.BasePointParam.ID).Where(id=>!id.IsNull)).Distinct())
        {
            cancel?.Invoke();
            var anchors=rotations.Where(f=>f!.BasePointParam.ID==sourceId).ToArray();
            var group=projections.Where(p=>p!.SrcObjectID==sourceId).ToArray();var axisFeatures=revolutions.Where(f=>f!.Axis.ID==sourceId).ToArray();
            bool nativePartEdge=sourceId.IsEdge&&sourceId.ParentId==body.ID;
            Need(!sourceId.IsNull&&(contents.Contains(sourceId)||axisFeatures.Length>0||anchors.Length>0||nativePartEdge),"External source absent from the model's current bindings.");
            foreach(var p in group)
            {
                cancel?.Invoke();
                var sketch=p!.HostSketchID.GetObjectOfType<PlanarSketch>();
                Need(sketch!=null&&sketch.HostSolidID==body.ID&&contents.Contains(sketch.ID)&&!sketch.IsInEditMode,"Projection does not belong to an available sketch of this body.");
            }
            // A same-part B-rep edge is an associative model reference, not an
            // external drawing curve with a fabricated DWG handle. Its actual
            // native-reference reader validates the current geometric binding.
            if(nativePartEdge)
            {
                Need(group.Length>0&&axisFeatures.Length==0&&anchors.Length==0,"The part edge is not an ordinary sketch-only projection source.");
                continue;
            }
            long handle=sourceId.Handle;bool found=state.Document.Database.TryGetObjectId(new Db.Handle(handle),out var raw);
            if(anchors.Length>0){raw=ModelPlacementHistory.SourceId(state,tx,sourceId);handle=raw.Handle.Value;found=true;}
            Need(handle!=0&&found&&!raw.IsNull&&!raw.IsErased,"Projected source has no current database handle.");
            Need(McObjectId.FromHandle(handle,McDocument.ActiveDocument.ID)==sourceId,"External source handle resolves to another native identity.");
            var entity=tx.GetObject(raw,Db.OpenMode.ForRead) as Db.Entity;Need(entity!=null&&entity.OwnerId==model,"Projection source is not top-level Model Space geometry.");double[] geometry;
            Need(axisFeatures.Length==0||entity!.GetType()==typeof(Db.Line),"Only a proven exact native Line is supported as an external revolution axis.");
            var workPoint=anchors.Length>0?sourceId.GetObjectOfType<WorkPoint>():null;
            Need(anchors.Length==0||(group.Length==0&&axisFeatures.Length==0&&workPoint!=null&&workPoint.ID==sourceId&&workPoint.DefinitionType==WPTDefinitionType.Fixed),"Rotation anchor must be a separate native fixed WorkPoint.");
            if(anchors.Length>0){var point=workPoint!.Position;geometry=new[]{point.X,point.Y,point.Z};}
            else if(entity!.GetType()==typeof(Db.Line))
            {var line=(Db.Line)entity;geometry=new[]{line.StartPoint.X,line.StartPoint.Y,line.StartPoint.Z,line.EndPoint.X,line.EndPoint.Y,line.EndPoint.Z,line.Thickness};}
            else if(entity.GetType()==typeof(Db.Arc))
            {var c=(Db.Arc)entity;geometry=new[]{c.Center.X,c.Center.Y,c.Center.Z,c.Radius,c.Normal.X,c.Normal.Y,c.Normal.Z,c.Thickness,c.StartAngle,c.EndAngle,c.StartPoint.X,c.StartPoint.Y,c.StartPoint.Z,c.EndPoint.X,c.EndPoint.Y,c.EndPoint.Z};}
            else
            {Need(entity.GetType()==typeof(Db.Circle),"Only exact native Line/Circle external sources are supported here.");var c=(Db.Circle)entity;geometry=new[]{c.Center.X,c.Center.Y,c.Center.Z,c.Radius,c.Normal.X,c.Normal.Y,c.Normal.Z,c.Thickness};}
            Need(geometry.All(double.IsFinite),"External source geometry is nonfinite.");result.Add(new(sourceId.ToString(),raw.Handle.ToString(),entity!.Layer,geometry,group.Select(p=>p!.ID.ToString()).OrderBy(v=>v,StringComparer.Ordinal).ToArray(),axisFeatures.Select(f=>f!.ID.ToString()).OrderBy(v=>v,StringComparer.Ordinal).ToArray(),anchors.Length==0?null:anchors.Select(f=>f!.ID.ToString()).OrderBy(v=>v,StringComparer.Ordinal).ToArray()));
        }
        foreach(var source in result)budget.Add(source);
        return result.OrderBy(v=>v.id,StringComparer.Ordinal).ToArray();
    }
    internal static bool HealthySuppressed(HistoryItem item)=>item.IsSuppressed&&(item.Status.ToString() is "Suppressed" or "Ok");
    internal static void Healthy(McObjectId[] contents,Source[] external,string? repairFeatureId=null,IReadOnlySet<string>? selectedSuppressedIds=null)
    {
        var sources=external.Select(s=>s.id).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var nativeSources=NativePathSources.Read(contents);
        foreach(var id in contents)
        {
            var item=id.GetObjectOfType<HistoryItem>();
            if(sources.Contains(id.ToString())&&item==null)continue;
            // Exact stored path references can be ordinary curves, not history
            // wrappers. A referenced HistoryItem still has to be healthy: a
            // support ID never waives an actual failed/suppressed datum.
            if(item==null&&nativeSources.Contains(id.ToString()))continue;
            bool repairing=item!=null&&repairFeatureId!=null&&id.ToString().Equals(repairFeatureId,StringComparison.OrdinalIgnoreCase)&&
                id.GetObjectOfType<HoleFeature>()!=null&&item.Status.ToString()=="NeedUpdate";
            // Intentional suppression is stored design, not a failed model.
            // Writers retain its flag; it never waives Error/Unknown/NeedUpdate.
            if(item!=null&&HealthySuppressed(item))continue;
            if(item==null||(!repairing&&item.Status.ToString()!="Ok")||item.IsSuppressed)
                throw new DomainException("MODEL_STATE_UNAVAILABLE","History node is unavailable, unhealthy or suppressed.");
        }
    }
}
