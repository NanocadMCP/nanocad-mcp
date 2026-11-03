using System.Text.Json;
using System.Text.Json.Nodes;
using Multicad;
using Multicad.Geometry;
using Multicad.Mc3D;
using NanoCAD.Contracts;

namespace NanoCADBridge;

// Drawing views are downstream history items on the source body. They do not
// change its 3D mass, but their native identity and association must survive a
// sketch rebuild. Projection/section trees can share a terminal history chain;
// their parent tree must not be confused with their linear history order.
internal static class ModelViewFollowers
{
    internal sealed record Snapshot(McObjectId LastModelItem,McObjectId LastHistoryItem,
        McObjectId[] ViewIds,JsonElement Metadata,ModelStandaloneViewFollowers353.Snapshot? Standalone=null)
    {internal bool HasFollowers=>ViewIds.Length>0||Standalone is {Views.Length:>0};}
    private static void Need(bool ok,string message)
    {if(!ok)throw new DomainException("OUT_OF_SCOPE",message);}
    private static object Plane(Plane3d plane)=>new{
        origin=new[]{plane.Origin.X,plane.Origin.Y,plane.Origin.Z},
        axis_u=new[]{plane.DirU.X,plane.DirU.Y,plane.DirU.Z},
        axis_v=new[]{plane.DirV.X,plane.DirV.Y,plane.DirV.Z},
        normal=new[]{plane.Normal.X,plane.Normal.Y,plane.Normal.Z}};
    private static JsonElement State(Mc3dSolid body,McObjectId[] views,bool inspectGeometry,Action? cancel=null)
    {
        var entries=new List<object>();var budget=new ModelInventoryBudget(cancel);
        foreach(var id in views)
        {
            cancel?.Invoke();
            var view=id.GetObjectOfType<DrawingView>();
            Need(view!=null&&view.ID==id&&view.HostSolidID==body.ID&&
                 view.Status.ToString()=="Ok"&&!view.IsSuppressed&&view.EnableAutoUpdate&&
                 view.Attached2dBreaks is {Length:0},
                 "Only healthy, unbroken associative model views may follow an edited body.");
            var sources=view!.GetTop3dEntitiesInView();
            Need(sources!=null&&sources.Count==1&&sources.Single()==body.ID,
                 "Drawing view must retain this one source body.");
            var children=view.ChildViews;
            Need(children!=null&&children.All(child=>{cancel?.Invoke();return views.Contains(child);}),
                 "Drawing view children must remain inside the retained group.");
            JsonElement? sectionState=null;
            if(!view.SectionID.IsNull)
            {
                var section=view.SectionID.GetObjectOfType<Section>();
                Need(section!=null&&section.Status.ToString()=="Ok"&&!section.LiveSectionEnabled,
                     "The dependent section must be healthy and non-live.");
                var sectionSources=section!.GetTop3dEntitiesForSectioning();
                Need(sectionSources!=null&&sectionSources.Count==1&&sectionSources.Single()==body.ID,
                     "The dependent section must retain this source body.");
                sectionState=SectionViewState.Read(section,cancel??(()=>{}));
            }
            else Need(!view.IsBasedOnSection&&!view.IsSlice&&!view.DependsOnSlice,
                      "Section-derived view has no addressable section definition.");
            if(inspectGeometry)
            {
                cancel?.Invoke();
                var geometry=view.GetViewGeometry(true,0);
                cancel?.Invoke();
                Need(geometry!=null&&geometry.Count>0,
                     "Updated drawing view geometry is empty or unavailable.");
            }
            entries.Add(new{id=id.ToString(),name=view.Name,previous=view.OrderObjBefore.ToString(),
                root=view.RootViewID.ToString(),parent=view.ParentViewID.ToString(),
                children=children!.Select(x=>x.ToString()).OrderBy(x=>x,StringComparer.Ordinal).ToArray(),
                source=sources!.Single().ToString(),direction=view.DirectionType.ToString(),
                model_scale=view.DbEntity.DScale,design_scale=view.DbEntity.Scale,
                view_plane=Plane(view.ViewPlane),projection_plane=Plane(view.ProjPlane),
                hidden=view.ShowHiddenLines,hatches=view.ShowHatches,tangent=view.ShowTangentEdges,
                section=sectionState,is_slice=view.IsSlice,is_based_on_section=view.IsBasedOnSection,
                depends_on_slice=view.DependsOnSlice});
            budget.Add(entries[^1]);
        }
        return Wire.Element(entries);
    }
    internal static Snapshot Resolve(Mc3dSolid body,McObjectId[] contents,Action? cancel=null,bool includeStandalone=false)
    {
        ModelInventoryBudget.Ids(contents,cancel);
        var views=contents.Where(id=>{cancel?.Invoke();return id.GetObjectOfType<DrawingView>()!=null;}).ToArray();
        if(views.Length==0)return new(body.LastPartItemID,body.LastPartItemID,
            Array.Empty<McObjectId>(),Wire.Element(Array.Empty<object>()),includeStandalone?ModelStandaloneViewFollowers353.Capture(body,views,cancel):null);
        var set=views.ToHashSet();var seen=new HashSet<McObjectId>();
        var last=body.LastPartItemID;
        while(set.Contains(last))
        {
            cancel?.Invoke();
            Need(seen.Add(last),"Drawing view predecessor chain contains a cycle.");
            last=last.GetObjectOfType<DrawingView>()!.OrderObjBefore;
        }
        Need(seen.Count==views.Length&&!last.IsNull&&contents.Contains(last),
             "Drawing views must form a terminal history chain after the model feature.");
        var roots=views.Where(id=>id.GetObjectOfType<DrawingView>()!.ParentViewID.IsNull).ToArray();
        Need(roots.Length>0,"Drawing views require a root projection or section.");
        foreach(var id in views)
        {
            cancel?.Invoke();
            var view=id.GetObjectOfType<DrawingView>()!;
            var parents=new HashSet<McObjectId>();var current=id;
            while(!current.GetObjectOfType<DrawingView>()!.ParentViewID.IsNull)
            {
                cancel?.Invoke();
                Need(parents.Add(current),"Drawing view parent tree contains a cycle.");
                current=current.GetObjectOfType<DrawingView>()!.ParentViewID;
                Need(set.Contains(current),"Drawing view parent is outside the retained group.");
            }
            Need(view.RootViewID==current,"Drawing view root and parent tree disagree.");
            Need(view.ChildViews!=null&&view.ChildViews.ToHashSet().SetEquals(
                views.Where(child=>{cancel?.Invoke();return child.GetObjectOfType<DrawingView>()!.ParentViewID==id;})),
                "Drawing view child references are incomplete.");
        }
        return new(last,body.LastPartItemID,views,State(body,views,false,cancel),
            includeStandalone?ModelStandaloneViewFollowers353.Capture(body,views,cancel):null);
    }
    internal static void Verify(Mc3dSolid body,Snapshot before,Action? cancel=null,bool refreshStandalone=false)
    {
        if(before.Standalone!=null)ModelStandaloneViewFollowers353.Verify(body,before.Standalone,before.ViewIds,cancel,refreshStandalone);
        if(before.ViewIds.Length==0)return;
        var current=ModelInventoryBudget.History(body,cancel);
        Need(current!=null&&body.LastPartItemID==before.LastHistoryItem&&
             before.ViewIds.All(current.Contains),"Drawing view history membership changed.");
        var after=State(body,before.ViewIds,true,cancel);
        Need(ModelParameterSnapshot.Equivalent(before.Metadata,after),
             "Drawing view identities, settings or source links changed after model rebuild.");
    }

    internal static void CheckStandalone(Mc3dSolid body,Snapshot before,Action? cancel=null)
    {if(before.Standalone!=null)ModelStandaloneViewFollowers353.Verify(body,before.Standalone,before.ViewIds,cancel);}
    internal static void PrepareStandaloneRefresh(Mc3dSolid body,Snapshot before,Action? cancel=null)
    {if(before.Standalone!=null)ModelStandaloneViewFollowers353.PrepareRefresh(body,before.Standalone,cancel);}
    internal static void VerifyConstruction(Mc3dSolid body,Snapshot before,McObjectId construction,Action? cancel=null,bool refreshStandalone=false)
    {
        if(before.Standalone!=null)ModelStandaloneViewFollowers353.Verify(body,before.Standalone,before.ViewIds,cancel,refreshStandalone);
        var current=ModelInventoryBudget.History(body,cancel);var item=construction.GetObjectOfType<PlanarSketch>();
        Need(item!=null&&item.ID==construction&&item.HostSolidID==body.ID&&current.Contains(construction)&&
            (body.LastPartItemID==before.LastHistoryItem||body.LastPartItemID==construction),"Only the exact new owned sketch may extend construction history.");
        var views=current.Where(id=>{cancel?.Invoke();return id.GetObjectOfType<DrawingView>()!=null;}).ToHashSet();
        Need(views.SetEquals(before.ViewIds),"Creating supported construction changed the retained drawing view inventory.");
        if(before.ViewIds.Length>0)Need(ModelParameterSnapshot.Equivalent(before.Metadata,State(body,before.ViewIds,true,cancel)),"Creating supported construction changed retained drawing view identities/settings/geometry/source links.");
    }
    internal static Result WithWarnings(Result result,params Snapshot[] snapshots)=>ModelStandaloneViewFollowers353.Warnings(result,snapshots);
    internal static Result ReserveWarnings(Result result,params Snapshot[] snapshots)=>ModelStandaloneViewFollowers353.Reserve(result,snapshots);
    internal static bool OwnsDependent(Snapshot views,McObjectId id)=>ModelStandaloneViewFollowers353.OwnsDependent(views.Standalone,id);

    // Shared insertion protocol. The caller starts native tracking before
    // moving End-of-Part and retains ownership of failure recovery.
    internal static void PrepareInsertion(Snapshot before,bool tryOnly)
    {
        if(before.ViewIds.Length==0)return;
        var anchor=before.LastModelItem.GetObjectOfType<HistoryItem>();
        Need(anchor!=null&&anchor.SetAsLastPartItem(tryOnly),
             "Native End-of-Part cannot move before the drawing view chain.");
    }

    internal static void CompleteInsertion(Snapshot before,McObjectId featureId)
    {
        if(before.ViewIds.Length==0)return;
        var terminal=before.LastHistoryItem.GetObjectOfType<HistoryItem>();
        Need(terminal!=null&&terminal.SetAsLastPartItem(false),
             "Native End-of-Part could not restore the terminal drawing view.");
        var firstId=before.Metadata.EnumerateArray().Single(node=>node.GetProperty("previous").GetString()==before.LastModelItem.ToString()).GetProperty("id").GetString();
        var root=before.ViewIds.Single(id=>id.ToString()==firstId)
            .GetObjectOfType<DrawingView>()!;
        Need(root.SetOrderObjBefore(featureId,false),
             "Drawing view did not accept the inserted feature as predecessor.");
    }

    internal static void VerifyAfterInsertion(Mc3dSolid body,Snapshot before,McObjectId featureId,Action? cancel=null,bool refreshStandalone=false)
    {
        if(before.Standalone!=null)ModelStandaloneViewFollowers353.Verify(body,before.Standalone,before.ViewIds,cancel,refreshStandalone);
        if(before.ViewIds.Length==0)
        {Need(body.LastPartItemID==featureId,"Inserted feature must remain the actual history terminal; standalone views are not appended to it.");return;}
        var contents=ModelInventoryBudget.History(body,cancel);
        var current=Resolve(body,contents.ToArray(),cancel);
        Need(current.LastModelItem==featureId&&current.LastHistoryItem==before.LastHistoryItem&&
             current.ViewIds.ToHashSet().SetEquals(before.ViewIds),
             "New feature did not become the predecessor of the unchanged terminal view chain.");
        var root=before.Metadata.EnumerateArray().Single(node=>node.GetProperty("previous").GetString()==before.LastModelItem.ToString()).GetProperty("id").GetString();
        JsonElement WithoutRootPrevious(JsonElement value)
        {
            var nodes=JsonNode.Parse(value.GetRawText())!.AsArray();
            foreach(var node in nodes)
                if(node!["id"]!.GetValue<string>()==root)node["previous"]=null;
            return Wire.Element(nodes);
        }
        var updated=State(body,before.ViewIds,true,cancel);
        Need(ModelParameterSnapshot.Equivalent(WithoutRootPrevious(before.Metadata),WithoutRootPrevious(updated)),
             "Drawing view identity, settings, source or projection chain changed after insertion.");
    }
}
