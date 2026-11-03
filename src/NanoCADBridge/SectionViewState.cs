using System.Text.Json;
using HostMgd.ApplicationServices;
using Multicad;
using Multicad.DatabaseServices;
using Multicad.Geometry;
using Multicad.Mc3D;
using NanoCAD.Contracts;
using Db=Teigha.DatabaseServices;

namespace NanoCADBridge;
internal static class SectionViewState
{
    internal static void Need(bool value,string message,string code="MODEL_UPDATE_FAILED")
    {if(!value)throw new DomainException(code,message);}
    private static double Finite(double n){Need(double.IsFinite(n),"Nonfinite Section metadata.");return n;}
    private static double[] P(Point3d p)=>new[]{Finite(p.X),Finite(p.Y),Finite(p.Z)};
    private static double[] V(Vector3d v)=>new[]{Finite(v.X),Finite(v.Y),Finite(v.Z)};
    private static string[] Ids(ICollection<McObjectId>? ids,Action cancel)
    {
        Need(ids!=null,"Section references unavailable.");var result=new List<string>();var seen=new HashSet<McObjectId>();var budget=new ModelInventoryBudget(cancel);
        foreach(var id in ids!){cancel();Need(!id.IsNull&&seen.Add(id),"Section references contain null/duplicate native IDs.");var value=id.ToString();budget.Add(value);result.Add(value);}
        return result.OrderBy(id=>id,StringComparer.Ordinal).ToArray();
    }
    internal static JsonElement Read(Section section,Action cancel)
    {
        cancel();var vertices=section.GetVertices();Need(vertices!=null && vertices.Count>=2,"Section vertices unavailable or invalid.");
        var points=new List<double[]>();var budget=new ModelInventoryBudget(cancel);
        foreach(var vertex in vertices!){cancel();var point=P(vertex);budget.Add(point);points.Add(point);}
        var result=Wire.Element(new{mc_id=section.ID.ToString(),state=section.State.ToString(),status=section.Status.ToString(),vertices=points,vertical_direction=V(section.VerticalDirection),viewing_direction=V(section.ViewingDirection),associated_plane_id=section.AssociatedPlanarObjectID.IsNull?null:section.AssociatedPlanarObjectID.ToString(),depth=Finite(section.Depth),height_above=Finite(section.HeightAboveSection),height_below=Finite(section.HeightBelowSection),live_section_enabled=section.LiveSectionEnabled,view_ids=Ids(section.GetViews(),cancel),top_source_ids=Ids(section.GetTop3dEntitiesForSectioning(),cancel)});
        CadDrawingViewLifecycleState345.Bytes(result,cancel);return result;
    }
    internal static JsonElement Capture(Document doc,Action cancel)
    {
        var common=DrawingViewWriter.Observe(doc,cancel,allowPaperViews:true);var sections=new List<object>();var links=new List<object>();
        var budget=new ModelInventoryBudget(cancel);budget.Add(common);
        using var tx=doc.Database.TransactionManager.StartTransaction();
        var table=(Db.BlockTable)tx.GetObject(doc.Database.BlockTableId,Db.OpenMode.ForRead);
        var model=(Db.BlockTableRecord)tx.GetObject(table[Db.BlockTableRecord.ModelSpace],Db.OpenMode.ForRead);
        foreach(Db.ObjectId native in model)
        {
            cancel();if(native.IsNull||!native.IsValid||native.IsErased || native.IsEffectivelyErased)continue;
            var entity=tx.GetObject(native,Db.OpenMode.ForRead);
            // Unselected block insertions remain retained model entities. This
            // adapter still selects only explicit top-level solid sources.
            var id=McObjectId.FromHandle(native.Handle.Value,McDocument.ActiveDocument.ID);
            var section=id.IsNull?null:id.GetObjectOfType<Section>();
            if(SectionNativeBinding357.IsSection(entity,cancel))Need(section!=null,"Native section has no matching MultiCAD identity.","OUT_OF_SCOPE");
            if(section!=null){Need(section.ID==id,"Section identity mismatch.");sections.Add(new{handle=native.Handle.ToString(),data=Read(section,cancel)});budget.Add(sections[^1]);}
            var view=id.IsNull?null:id.GetObjectOfType<DrawingView>();
            if(view!=null){links.Add(new{handle=native.Handle.ToString(),section_id=view.SectionID.ToString(),is_section=view.IsBasedOnSection,is_root_section=view.IsRootSectionView,is_slice=view.IsSlice});budget.Add(links[^1]);}
        }
        var settings=SectionSourceSelection357.Inventory(doc,tx,cancel);budget.Add(settings);
        var result=Wire.Element(new{common,sections,view_links=links,section_settings=settings});CadDrawingViewLifecycleState345.Bytes(result,cancel);return result;
    }
    internal static bool Same(JsonElement a,JsonElement b)
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
}
