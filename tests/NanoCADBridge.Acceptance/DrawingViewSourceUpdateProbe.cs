using System.Text.Json;
using HostMgd.ApplicationServices;
using Multicad;
using Multicad.DatabaseServices;
using Multicad.Geometry;
using Multicad.Mc3D;
using NanoCAD.Contracts;

namespace NanoCADBridge;

// Diagnostic only; the fixed-file/lifetime/one-use gate is DrawingViewWriteProbe.
internal static class DrawingViewSourceUpdateProbe
{
    private static void Need(bool value,string message){if(!value)throw new InvalidOperationException(message);}
    private static double[] P(Point3d point)=>new[]{point.X,point.Y,point.Z};
    private static JsonElement Geometry(Document doc,JsonElement snapshot)
    {
        var items=new List<object>();
        foreach(var item in snapshot.GetProperty("common").GetProperty("views").EnumerateArray())
        {
            var id=McObjectId.FromHandle(Convert.ToInt64(item.GetProperty("handle").GetString(),16),McDocument.ActiveDocument.ID);
            var view=id.GetObjectOfType<DrawingView>();Need(view!=null && view.ID==id && view.EnableAutoUpdate,"Expected an auto-updated native view");
            var geometry=view!.GetViewGeometry(true,0);Need(geometry!=null && geometry.Count is >0 and <=4096,"Bounded view geometry required");
            var curves=geometry!.Select(g=>g.AsCurve()).Where(c=>c!=null).Select(c=>new{type=c!.GetType().FullName,start=P(c.StartPoint),end=P(c.EndPoint)}).ToArray();
            Need(curves.Length>0,"No view curves");
            items.Add(new{handle=item.GetProperty("handle").GetString(),origin=P(view.ViewPlane.Origin),center=P(view.CenterPointOf3dEnts),scale=view.DbEntity.DScale,curves});
        }
        return Wire.Element(items);
    }
    private static JsonElement Capture(Document doc)=>SectionViewState.Capture(doc,()=>{});
    public static void Execute(Document doc,Action<string,object?> log)
    {
        var before=Capture(doc);Need(before.GetProperty("sections").GetArrayLength()==1 && before.GetProperty("common").GetProperty("views").GetArrayLength()==1,"Expected one accepted Section and its view");
        var geometry=Geometry(doc,before);log("source_update_baseline",new{snapshot=before,geometry});
        var id=McObjectId.FromHandle(0x71,McDocument.ActiveDocument.ID);var body=id.GetObjectOfType<Mc3dSolid>();Need(body!=null && body.ID==id,"Source identity mismatch");
        Exception? changeError=null;
        using(var tracker=new McUndoPoint(false))
        {
            tracker.Start();
            try
            {
                log("source_transform_before",null);
                var basePoint=new McGeomParam();
                var feature=body!.addAssocScaleFeature(2,basePoint);
                Need(feature!=null && !feature.ID.IsNull,"Associative source scaling rejected");
                log("source_scale_feature_added",new{feature_id=feature!.ID.ToString(),scale=2,base_point="native_source_bounds_center"});
                body.DbEntity.Update();log("source_update_before",null);McObjectManager.UpdateAll();log("source_update_returned",null);
                var changed=Capture(doc);log("source_update_changed",new{snapshot=changed,geometry=Geometry(doc,changed)});
                Need(!SectionViewState.Same(before.GetProperty("common").GetProperty("bodies"),changed.GetProperty("common").GetProperty("bodies")),"Source did not change");
            }
            catch(Exception e){changeError=e;log("source_update_error",new{error=e.ToString()});}
            finally{log("source_update_undo_before",null);try{tracker.Undo(true);}finally{tracker.Stop();}}
        }
        McObjectManager.UpdateAll();var restored=Capture(doc);var restoredGeometry=Geometry(doc,restored);
        log("source_update_restored",new{snapshot=restored,geometry=restoredGeometry});
        Need(SectionViewState.Same(before,restored) && SectionViewState.Same(geometry,restoredGeometry),"SOURCE_UPDATE_ROLLBACK_NOT_VERIFIED");
        log("source_update_rollback_verified",null);
        if(changeError!=null)throw new InvalidOperationException("Source change failed but observed rollback verified",changeError);
        log("source_update_completed",new{rollback_verified=true,scope="diagnostic source transform and dependent projection geometry; offline independent dimensions pending"});
    }
}
