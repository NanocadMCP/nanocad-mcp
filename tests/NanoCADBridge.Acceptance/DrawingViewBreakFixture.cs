using System.Text.Json;
using HostMgd.ApplicationServices;
using Multicad;
using Multicad.DatabaseServices;
using Multicad.Geometry;
using Multicad.Mc3D;
using NanoCAD.Contracts;

namespace NanoCADBridge;

// The existing fixed-file/lifetime/one-use diagnostic gate owns this command.
internal static class DrawingViewBreakFixture
{
    private static void Need(bool value,string message){if(!value)throw new InvalidOperationException(message);}
    public static void Execute(Document doc,Action<string,object?> log)
    {
        var before=SectionViewState.Capture(doc,()=>{});var old=before.GetProperty("common");
        Need(before.GetProperty("sections").GetArrayLength()==0 && old.GetProperty("views").GetArrayLength()==4,"Expected four accepted source views");
        log("break_fixture_baseline",before);
        var made=new List<object>();bool stopped=false;
        using(var tracker=new McUndoPoint(false))
        {
            tracker.Start();
            try
            {
                foreach(var direction in new[]{"kViewFront","kViewTop"})
                {
                    var row=old.GetProperty("views").EnumerateArray().Single(v=>v.GetProperty("direction").GetString()==direction);
                    var id=McObjectId.FromHandle(Convert.ToInt64(row.GetProperty("handle").GetString(),16),McDocument.ActiveDocument.ID);
                    var view=id.GetObjectOfType<DrawingView>();Need(view!=null && view.ID==id && view.Attached2dBreaks.Length==0,"Target view already has a break");
                    var p=view!.ViewPlane.Origin;bool front=direction=="kViewFront";
                    log("break_add_before",new{direction});var value=new Mapinet.Break2d();value.DbEntity.AddToCurrentDocument();
                    value.TargetDrawingViewId=id;
                    value.SetPoints=new List<Point3d>{new(p.X-10,p.Y+100,0),new(p.X-10,p.Y-100,0),new(p.X+10,p.Y+100,0),new(p.X+10,p.Y-100,0)};
                    value.BreakType=front?Mapinet.Break2d.BreakTypes.Linear:Mapinet.Break2d.BreakTypes.Curved;
                    value.GapBetweenLines=front?5:7;
                    Need(value.TargetDrawingViewId==id && view.Attached2dBreaks.Contains(value.ID),"Native break/view relation missing");
                    made.Add(new{mc_id=value.ID.ToString(),target_view_id=id.ToString(),target_direction=direction,break_type=value.BreakType.ToString(),gap=value.GapBetweenLines});
                }
                log("break_update_before",null);McObjectManager.UpdateAll();log("break_update_returned",null);
                var after=SectionViewState.Capture(doc,()=>{});log("break_fixture_changed",new{snapshot=after,created=made});
                foreach(var field in new[]{"layers","blocks","current_layer","view_layer"})Need(SectionViewState.Same(old.GetProperty(field),after.GetProperty("common").GetProperty(field)),"Unexpected fixture change: "+field);
                var oldBodies=old.GetProperty("bodies").EnumerateArray().ToArray();var newBodies=after.GetProperty("common").GetProperty("bodies").EnumerateArray().ToArray();
                Need(oldBodies.Length==newBodies.Length && oldBodies.All(b=>newBodies.Any(n=>b.GetProperty("handle").GetString()==n.GetProperty("handle").GetString() && SectionViewState.Same(b.GetProperty("geometry"),n.GetProperty("geometry")))),"Fixture changed source geometry");
                tracker.Stop();stopped=true;
            }
            catch
            {
                if(!stopped){try{tracker.Undo(true);}finally{tracker.Stop();}McObjectManager.UpdateAll();var restored=SectionViewState.Capture(doc,()=>{});log("break_fixture_restored",restored);Need(SectionViewState.Same(before,restored),"BREAK_FIXTURE_ROLLBACK_NOT_VERIFIED");}
                throw;
            }
        }
        log("break_fixture_completed",new{created=made,scope="Private fixture creation only; read-only acceptance pending"});
    }
}
