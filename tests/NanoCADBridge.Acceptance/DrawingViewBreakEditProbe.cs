using System.Text.Json;
using HostMgd.ApplicationServices;
using Multicad;
using Multicad.DatabaseServices;
using Multicad.Geometry;
using Multicad.Mc3D;
using NanoCAD.Contracts;
using Db=Teigha.DatabaseServices;

namespace NanoCADBridge;

// Private native proof only. No product command or arbitrary property writer.
internal static class DrawingViewBreakEditProbe
{
    private static void Need(bool value,string message){if(!value)throw new InvalidOperationException(message);}
    private static double[] P(Point3d p)=>new[]{p.X,p.Y,p.Z};
    private static double[] V(Vector3d p)=>new[]{p.X,p.Y,p.Z};
    private static object[] Curves(IEnumerable<EntityGeometry> geometry)
    {
        var items=geometry.Take(4097).ToArray();Need(items.Length is >0 and <=4096,"Bounded native geometry required");
        return items.Select(g=>
        {
            // SDK Break2d.OnDraw explicitly skips null geometry placeholders.
            // Preserve those entries in the observation instead of treating them as curves.
            if(g.GeometryType==EntityGeomType.kNull)return (object)new{type="kNull",segments=Array.Empty<object>()};
            var curve=g.AsCurve();
            var segments=curve!=null?new[]{curve}:g.GeometryType==EntityGeomType.kPolyline?g.Polyline.Explode().ToArray():Array.Empty<Curve3d>();
            Need(segments.Length is >0 and <=256,"Unsupported or excessive geometry: "+g.GeometryType);
            var parts=segments.Select(c=>
            {
                c.GetSamplePoints(9,out List<Point3d> points);Need(points!=null && points.Count is >0 and <=9,"Curve sample budget");
                return new{type=c.GetType().FullName,start=P(c.StartPoint),end=P(c.EndPoint),samples=points!.Select(P).ToArray()};
            }).ToArray();
            return (object)new{type=g.GeometryType.ToString(),segments=parts};
        }).ToArray();
    }
    private static JsonElement Capture(Document doc)
    {
        var common=SectionViewState.Capture(doc,()=>{});var breaks=new List<object>();var views=new List<object>();
        using var tx=doc.Database.TransactionManager.StartTransaction();
        var table=(Db.BlockTable)tx.GetObject(doc.Database.BlockTableId,Db.OpenMode.ForRead);
        var model=(Db.BlockTableRecord)tx.GetObject(table[Db.BlockTableRecord.ModelSpace],Db.OpenMode.ForRead);
        foreach(Db.ObjectId native in model)
        {
            if(native.IsErased || native.IsEffectivelyErased)continue;
            var id=McObjectId.FromHandle(native.Handle.Value,McDocument.ActiveDocument.ID);
            var value=id.IsNull?null:id.GetObjectOfType<Mapinet.Break2d>();
            if(value!=null)
            {
                Need(breaks.Count<8 && value.ID==id,"Unexpected break inventory");
                var gap=value.GapBetweenLines;var skew=value.VectSkew;
                var lines=Curves(value.GetShiftedBreakLines());
                Need(gap==value.GapBetweenLines && skew==value.VectSkew,"Geometry evaluation normalized stored break parameters");
                breaks.Add(new{handle=native.Handle.ToString(),mc_id=id.ToString(),target_id=value.TargetDrawingViewId.ToString(),type=value.BreakType.ToString(),index=value.BreakTypeIndex,
                    gap,skew=V(skew),cut_distance=value.GetDistBetweenBreakLines(),lines});
            }
            var view=id.IsNull?null:id.GetObjectOfType<DrawingView>();
            if(view!=null)views.Add(new{handle=native.Handle.ToString(),attached=view.Attached2dBreaks.Select(i=>i.ToString()).ToArray(),curves=Curves(view.GetViewGeometry(true,0))});
        }
        return Wire.Element(new{common,breaks,views});
    }
    public static void Execute(Document doc,Action<string,object?> log)
    {
        var before=Capture(doc);log("break_edit_baseline",before);
        Need(before.GetProperty("breaks").GetArrayLength()==2 && before.GetProperty("views").GetArrayLength()==4,"Expected two existing breaks and four views");
        Need(before.GetProperty("breaks").EnumerateArray().All(b=>b.GetProperty("lines").EnumerateArray().Any(g=>g.GetProperty("type").GetString()!="kNull")),"Break geometry is unavailable before write");
        Exception? failure=null;
        using(var tracker=new McUndoPoint(false))
        {
            tracker.Start();
            try
            {
                foreach(var row in before.GetProperty("breaks").EnumerateArray())
                {
                    var id=McObjectId.FromHandle(Convert.ToInt64(row.GetProperty("handle").GetString(),16),McDocument.ActiveDocument.ID);
                    var value=id.GetObjectOfType<Mapinet.Break2d>();Need(value!=null && value.ID==id,"Break identity changed");
                    var wasLinear=row.GetProperty("type").GetString()=="Linear";
                    log("break_edit_before",new{handle=row.GetProperty("handle").GetString()});
                    value!.BreakType=wasLinear?Mapinet.Break2d.BreakTypes.Curved:Mapinet.Break2d.BreakTypes.Linear;
                    Need(value.setGapBetweenLines(wasLinear?9:3),"Native gap setter rejected requested change");
                    value.DbEntity.Update();
                }
                log("break_edit_update_before",null);McObjectManager.UpdateAll();log("break_edit_update_returned",null);
                var changed=Capture(doc);log("break_edit_changed",changed);
                Need(!SectionViewState.Same(before.GetProperty("breaks"),changed.GetProperty("breaks")),"Break edit had no observed effect");
                Need(SectionViewState.Same(before.GetProperty("common"),changed.GetProperty("common")),"Break edit changed unrelated model/view metadata");
            }
            catch(Exception e){failure=e;log("break_edit_error",new{error=e.ToString()});}
            finally{log("break_edit_undo_before",null);try{tracker.Undo(true);}finally{tracker.Stop();}}
        }
        McObjectManager.UpdateAll();var restored=Capture(doc);log("break_edit_restored",restored);
        Need(SectionViewState.Same(before,restored),"BREAK_EDIT_ROLLBACK_NOT_VERIFIED");
        log("break_edit_rollback_verified",null);
        if(failure!=null)throw new InvalidOperationException("Break edit failed; full observed rollback verified",failure);
        log("break_edit_completed",new{rollback_verified=true,scope="Existing known Break2d type/gap native change and geometry rollback; product write remains disabled"});
    }
}
