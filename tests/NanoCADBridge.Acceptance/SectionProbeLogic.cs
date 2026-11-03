using System.Text.Json;
using HostMgd.ApplicationServices;
using Multicad;
using Multicad.DatabaseServices;
using Multicad.Geometry;
using Multicad.Mc3D;
using NanoCAD.Contracts;
using Db=Teigha.DatabaseServices;

namespace NanoCADBridge;

// Included only in the diagnostic DrawingViewWriteProbe build, never in ordinary packages.
internal static class SectionProbeLogic
{
    private static void Need(bool yes,string message){if(!yes)throw new InvalidOperationException(message);}
    private static double[] P(Point3d p)=>new[]{p.X,p.Y,p.Z};
    private static double[] V(Vector3d v)=>new[]{v.X,v.Y,v.Z};
    private static string[] Ids(ICollection<McObjectId>? ids)=>ids?.Select(x=>x.ToString()).OrderBy(x=>x,StringComparer.Ordinal).ToArray()??Array.Empty<string>();
    private static JsonElement Capture(Document doc)
    {
        var common=DrawingViewWriter.Observe(doc,()=>{});
        var sections=new List<object>();
        using var tx=doc.Database.TransactionManager.StartTransaction();
        var table=(Db.BlockTable)tx.GetObject(doc.Database.BlockTableId,Db.OpenMode.ForRead);
        var model=(Db.BlockTableRecord)tx.GetObject(table[Db.BlockTableRecord.ModelSpace],Db.OpenMode.ForRead);
        foreach(Db.ObjectId native in model)
        {
            if(native.IsErased || native.IsEffectivelyErased)continue;
            var id=McObjectId.FromHandle(native.Handle.Value,McDocument.ActiveDocument.ID);
            var section=id.IsNull?null:id.GetObjectOfType<Section>();if(section==null)continue;
            Need(sections.Count<8 && section.ID==id,"Unexpected Section identity/count");
            var points=section.GetVertices();Need(points!=null && points.Count<=8,"Unexpected Section vertices");
            sections.Add(new{handle=native.Handle.ToString(),mc_id=id.ToString(),state=section.State.ToString(),status=section.Status.ToString(),vertices=points!.Select(P).ToArray(),vertical=V(section.VerticalDirection),viewing=V(section.ViewingDirection),associated_plane=section.AssociatedPlanarObjectID.ToString(),depth=section.Depth,above=section.HeightAboveSection,below=section.HeightBelowSection,live=section.LiveSectionEnabled,views=Ids(section.GetViews()),sources=Ids(section.GetTop3dEntitiesForSectioning())});
        }
        return Wire.Element(new{common,sections});
    }
    private static bool Same(JsonElement a,JsonElement b)
    {
        if(a.ValueKind!=b.ValueKind)return false;
        return a.ValueKind switch
        {
            JsonValueKind.Object=>a.EnumerateObject().Count()==b.EnumerateObject().Count() && a.EnumerateObject().All(p=>b.TryGetProperty(p.Name,out var v)&&Same(p.Value,v)),
            JsonValueKind.Array=>a.GetArrayLength()==b.GetArrayLength() && a.EnumerateArray().Zip(b.EnumerateArray()).All(p=>Same(p.First,p.Second)),
            JsonValueKind.Number=>Math.Abs(a.GetDouble()-b.GetDouble())<=1e-8*Math.Max(1,Math.Max(Math.Abs(a.GetDouble()),Math.Abs(b.GetDouble()))),
            _=>a.ToString()==b.ToString()
        };
    }
    private static void Create(string label,Action<string,object?> log)
    {
        log("section_add_before",new{label});
        var section=new Section();section.DbEntity.AddToCurrentDocument();
        Need(section.SetVertices(new[]{new Point3d(150,-100,0),new Point3d(150,300,0)}),"Section vertices rejected");
        section.State=Section.StateType.Plane;section.LiveSectionEnabled=false;
        section.VerticalDirection=Vector3d.ZAxis;section.ViewingDirection=Vector3d.XAxis;section.DbEntity.Update();
        log("section_initialized",new{label,section_id=section.ID.ToString(),sources=Ids(section.GetTop3dEntitiesForSectioning())});
        var view=new DrawingView();view.SectionID=section.ID;view.SetIsSection(true);view.DirectionType=DrawingView.ViewDirectionType.kViewFront;
        view.ViewPlane=new Plane3d(new Point3d(2000,0,0),Vector3d.XAxis,Vector3d.YAxis);
        view.DbEntity.AddToCurrentDocument();view.Name="MCP "+label+" Section";view.DbEntity.DScale=0.5;view.DbEntity.Scale=1;
        log("section_update_before",new{label});McObjectManager.UpdateAll();log("section_update_returned",new{label});
        Need(Math.Abs(section.ViewingDirection.X-1)<1e-10 && Math.Abs(section.ViewingDirection.Y)<1e-10 && Math.Abs(section.ViewingDirection.Z)<1e-10,"Section viewing direction differs from requested +X");
        Need(view.SectionID==section.ID && view.IsBasedOnSection && section.GetViews().Contains(view.ID),"Section-view association mismatch");
        var geometry=view.GetViewGeometry(true,0)??throw new InvalidOperationException("Section geometry unavailable");Need(geometry.Count is >0 and <=4096,"Empty/excessive section geometry");
        var curves=geometry.Select(g=>g.AsCurve()).Where(c=>c!=null).Select(c=>new{type=c!.GetType().FullName,start=P(c.StartPoint),end=P(c.EndPoint)}).ToArray();
        log("section_geometry",new{label,section_id=section.ID.ToString(),view_id=view.ID.ToString(),count=geometry.Count,types=geometry.Select(g=>g.GetType().FullName).ToArray(),curves,sources=Ids(section.GetTop3dEntitiesForSectioning()),view_sources=Ids(view.GetTop3dEntitiesInView())});
    }
    public static void Execute(Document doc,Action<string,object?> log)
    {
        var before=Capture(doc);Need(before.GetProperty("sections").GetArrayLength()==0 && before.GetProperty("common").GetProperty("views").GetArrayLength()==0,"Existing section/view in probe fixture");
        log("section_baseline",before);
        Exception? creationError=null;
        using(var tracker=new McUndoPoint(false))
        {
            tracker.Start();
            try{Create("Rollback",log);log("section_changed",Capture(doc));}
            catch(Exception e){creationError=e;log("section_create_error",new{error=e.ToString()});}
            finally{log("section_undo_before",null);try{tracker.Undo(true);}finally{tracker.Stop();}}
        }
        McObjectManager.UpdateAll();var restored=Capture(doc);log("section_restored",restored);
        Need(Same(before,restored),"SECTION_ROLLBACK_NOT_VERIFIED: observed state differs; positive branch skipped");
        log("section_rollback_verified",null);
        if(creationError!=null)throw new InvalidOperationException("Section creation failed but observed rollback was verified",creationError);
        using(var tracker=new McUndoPoint(false))
        {
            tracker.Start();
            try{Create("Retained",log);}
            catch{tracker.Undo(true);throw;}
            finally{tracker.Stop();}
        }
        var retained=Capture(doc);log("section_retained",retained);
        Need(retained.GetProperty("sections").GetArrayLength()==1 && retained.GetProperty("common").GetProperty("views").GetArrayLength()==1,"Expected one Section and one retained DrawingView");
        var oldBodies=before.GetProperty("common").GetProperty("bodies").EnumerateArray().ToArray();var newBodies=retained.GetProperty("common").GetProperty("bodies").EnumerateArray().ToArray();
        Need(oldBodies.Length==newBodies.Length && oldBodies.All(b=>newBodies.Any(n=>Same(b.GetProperty("handle"),n.GetProperty("handle")) && Same(b.GetProperty("geometry"),n.GetProperty("geometry")))),"Original body geometry changed");
        log("section_completed",new{rollback_verified=true,positive_retained=true,scope="diagnostic Section semantics; no public Section write acceptance"});
    }
}
