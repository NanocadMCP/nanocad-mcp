using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using Multicad;
using Multicad.DatabaseServices;
using Multicad.Mc3D;
using NanoCAD.Contracts;
using NanoCAD.Ipc;
using Db=Teigha.DatabaseServices;
using G=Multicad.Geometry;
using HostDocument=HostMgd.ApplicationServices.Document;
using View=NanoCADBridge.CadDrawingViewEditState.View;

namespace NanoCADBridge;

// Resident selected-source groups need not be items in any one source body's
// history. Discover them only for an explicit model write, never on default reads.
internal static class ModelStandaloneViewFollowers353
{
    internal sealed record Entry(View View,JsonElement Stored,JsonElement Owned);
    internal sealed record Source(McObjectId Id,Db.ObjectId Entity,JsonElement Stored,JsonElement Geometry,JsonElement ReadableMetadata);
    internal sealed class Snapshot(HostDocument document,McObjectId drawing,McObjectId body,
        Entry[] views,Source[] sources,Dictionary<McObjectId,JsonElement> sections)
    {
        internal HostDocument Document {get;}=document;
        internal McObjectId Drawing {get;}=drawing;
        internal McObjectId Body {get;}=body;
        internal Entry[] Views {get;}=views;
        internal Source[] Sources {get;}=sources;
        internal Dictionary<McObjectId,JsonElement> Sections {get;}=sections;
        internal HashSet<McObjectId> MutableSources {get;}=[body];
        internal bool Refreshed {get;set;}
        internal HashSet<string> Observations {get;}=new(StringComparer.Ordinal);
    }
    internal const string Warning="NATIVE_SELECTED_SOURCE_VIEW_FOLLOWERS: Automatic resident views selecting an edited part were refreshed through native projection caches, including standalone multi-source groups. Native IDs, selected sources, layout owners, tree, placement and readable stored settings remain verified; opaque owned internals are not certified. Manually frozen or suppressed projections retain that state. Projection element order is not a persistent edge identity. Inspect the affected drawing through MCP before relying on displayed detail; recovery and Undo are not guaranteed.";
    internal const string SelectorWarning="VIEW_SOURCE_SELECTION_UNOBSERVED: Some resident native views did not expose their source selector. Complete selected-source follower coverage is not claimed for those views; no source membership, refresh or preservation is inferred from a null list.";
    static readonly string[] ObservationWarnings=[SelectorWarning,
        "VIEW_AUTO_UPDATE_DISABLED_OR_SUPPRESSED: The stored manual/frozen projection state was retained; its pixels are not claimed updated.",
        "VIEW_PROJECTION_UNOBSERVED: Native projection reader is unavailable; selected sources and healthy view state remain observed.",
        "VIEW_PROJECTION_EMPTY: Native projection is empty; inspect the affected drawing, since a valid section can lie outside material.",
        "VIEW_PROJECTION_PARTIALLY_OBSERVED: Some native projected curve types have no detailed serializer; their actual types/count are observed without a fabricated edge identity."];
    static void Need([DoesNotReturnIf(false)]bool ok,string message,string code="MODEL_VIEW_FOLLOWER_MISMATCH")
    {if(!ok)throw new DomainException(code,message);}
    static bool Same(JsonElement a,JsonElement b)=>ModelParameterSnapshot.Equivalent(a,b);
    static string Handle(Db.ObjectId id)=>Validation.NormalizeHandle(id.Handle.ToString());
    static bool Gone(Db.ObjectId id)=>CadDrawingViewEditState.Gone(id);
    static HostDocument Context(Mc3dSolid body,McObjectId? expected=null)
    {
        var host=HostMgd.ApplicationServices.Application.DocumentManager.MdiActiveDocument;
        var drawing=McDocument.ActiveDocument;
        Need(host!=null&&host.IsNamedDrawing&&drawing!=null&&
            string.Equals(Path.GetFullPath(host.Name),Path.GetFullPath(drawing.Path),StringComparison.OrdinalIgnoreCase),
            "Selected-source documentation needs the same active named host drawing.","DOCUMENT_CONTEXT_MISMATCH");
        Need(expected==null||drawing.ID==expected.Value,"Native drawing lifetime/context changed.","DOCUMENT_CONTEXT_MISMATCH");
        Need(body.DbEntity.Document!=null&&!body.ID.IsNull,"Native part owner identity is unavailable.","OBJECT_IDENTITY_MISMATCH");
        return host;
    }
    static Dictionary<McObjectId,View> Inventory(HostDocument document,McDocument drawing,Db.Transaction tx,Action cancel)
    {
        var db=document.Database;var dictionary=(Db.DBDictionary)tx.GetObject(db.LayoutDictionaryId,Db.OpenMode.ForRead);
        var result=new Dictionary<McObjectId,View>();var bytes=new ModelInventoryBudget(cancel);
        foreach(Db.DBDictionaryEntry item in dictionary)
        {
            cancel();var layout=tx.GetObject(item.Value,Db.OpenMode.ForRead) as Db.Layout;
            Need(layout!=null&&layout.OwnerId==dictionary.ObjectId&&layout.LayoutName==item.Key,"Layout dictionary identity/owner changed.");
            var space=tx.GetObject(layout.BlockTableRecordId,Db.OpenMode.ForRead) as Db.BlockTableRecord;
            Need(space!=null&&space.IsLayout&&space.LayoutId==layout.ObjectId&&space.OwnerId==db.BlockTableId,
                "Layout space has inconsistent ownership.");
            foreach(Db.ObjectId id in space)
            {
                cancel();if(Gone(id))continue;
                // Unrelated ordinary entity contents are not part of this selector.
                if(id.ObjectClass.Name!="AcDbImpCurve")
                {
                    var candidate=tx.GetObject(id,Db.OpenMode.ForRead);
                    if(candidate.GetType().FullName!="Teigha.DatabaseServices.ImpCurve")continue;
                }
                var native=McObjectId.FromHandle(id.Handle.Value,drawing.ID);
                var view=native.IsNull?null:native.GetObjectOfType<DrawingView>();if(view==null)continue;
                var entity=tx.GetObject(id,Db.OpenMode.ForRead) as Db.Entity;
                Need(entity!=null&&entity.GetType().FullName=="Teigha.DatabaseServices.ImpCurve"&&entity.OwnerId==space.ObjectId&&view.ID==native,
                    "DrawingView must have one exact resident layout representation.");
                // No geometry/default getters on unrelated views; selected groups
                // receive the complete snapshot after closing their parent tree.
                Need(result.TryAdd(native,new(native,id,layout.ObjectId,space.ObjectId,view,default)),
                    "Native DrawingView has ambiguous physical representations.");
                bytes.Add(new{id=native.ToString(),handle=Handle(id),layout=Handle(layout.ObjectId)});
            }
        }
        return result;
    }
    static HashSet<McObjectId> Group(Dictionary<McObjectId,View> all,McObjectId body,HashSet<McObjectId> historyViews,HashSet<string> observations,Action cancel)
    {
        var roots=new HashSet<McObjectId>();
        foreach(var view in all.Values)
        {
            cancel();if(historyViews.Contains(view.Id))continue;
            var sources=view.Native.GetTop3dEntitiesInView();
            if(sources==null){observations.Add(SelectorWarning);continue;}
            if(!sources.Contains(body))continue;
            var current=view;var seen=new HashSet<McObjectId>();
            while(!current.Native.ParentViewID.IsNull)
            {cancel();Need(seen.Add(current.Id)&&all.ContainsKey(current.Native.ParentViewID),"Selected view parent chain is incomplete or cyclic.");current=all[current.Native.ParentViewID];}
            roots.Add(current.Id);
        }
        var selected=new HashSet<McObjectId>();var queue=new Queue<McObjectId>(roots);
        while(queue.Count>0)
        {
            cancel();var id=queue.Dequeue();Need(selected.Add(id)&&all.ContainsKey(id),"Selected view child tree is ambiguous or cyclic.");
            var view=all[id];var children=view.Native.ChildViews;
            Need(children!=null&&children.Distinct().Count()==children.Count,"Selected view child inventory is unavailable/duplicated.");
            Need(children.ToHashSet().SetEquals(all.Values.Where(v=>v.Native.ParentViewID==id).Select(v=>v.Id)),"Selected view child/parent backlinks disagree.");
            foreach(var child in children){Need(all.ContainsKey(child),"Selected child is not resident in this drawing.");queue.Enqueue(child);}
        }
        Need(!selected.Overlaps(historyViews),"A standalone selected-source group overlaps the body's terminal view chain.");
        return selected;
    }
    internal static Snapshot Capture(Mc3dSolid body,McObjectId[] historyViews,Action? cancellation=null)
    {
        var cancel=cancellation??(()=>{});var host=Context(body);var drawing=McDocument.ActiveDocument;
        using var tx=host.Database.TransactionManager.StartTransaction();var all=Inventory(host,drawing,tx,cancel);
        var observations=new HashSet<string>(StringComparer.Ordinal);var selected=Group(all,body.ID,historyViews.ToHashSet(),observations,cancel);var entries=new List<Entry>();
        var sourceIds=new HashSet<McObjectId>();var sections=new Dictionary<McObjectId,JsonElement>();var bytes=new ModelInventoryBudget(cancel);
        foreach(var id in selected.OrderBy(id=>id.ToString(),StringComparer.Ordinal))
        {
            cancel();var value=all[id];var view=value.Native;
            Need(view.Status.ToString()=="Ok","Selected follower native state is unhealthy.","MODEL_NOT_READY");
            CadDrawingViewEditState.Placement(value);
            var sources=view.GetTop3dEntitiesInView();var names=CadDrawingViewEditState.Ids(sources,"Selected follower sources");
            Need(names.Length>0,"Selected follower has no native source.");foreach(var source in sources!)sourceIds.Add(source);
            if(!view.HostSolidID.IsNull)
            {
                var owner=view.HostSolidID.GetObjectOfType<Mc3dSolid>();
                Need(owner!=null&&sources.Contains(owner.ID)&&ModelInventoryBudget.History(owner,cancel).Contains(view.ID),
                    "A claimed follower host is not an actual selected source/history owner.");
            }
            else Need(view.OrderObjBefore.IsNull,"An unhosted follower has an unclassified predecessor link.");
            var root=value;var seen=new HashSet<McObjectId>();while(!root.Native.ParentViewID.IsNull)
            {Need(seen.Add(root.Id),"Selected view root chain contains a cycle.");root=all[root.Native.ParentViewID];}
            Need(view.RootViewID==root.Id||view.ID==root.Id&&view.RootViewID.IsNull,"Selected view root differs from the observed tree.");
            if(!view.SectionID.IsNull)
            {
                var section=view.SectionID.GetObjectOfType<Section>();
                Need(section!=null&&section.ID==view.SectionID&&section.Status.ToString()=="Ok"&&!section.LiveSectionEnabled&&section.GetViews().Contains(view.ID),
                    "Selected section follower lacks its healthy actual Section/backlink.");
                // Section-global processing is observed separately. It is not
                // used as a universal equality oracle for this view's sources.
                if(!sections.ContainsKey(section.ID))
                {
                    Need(host.Database.TryGetObjectId(new Db.Handle(section.ID.Handle),out var sectionId)&&!sectionId.IsNull&&!Gone(sectionId)&&
                        McObjectId.FromHandle(sectionId.Handle.Value,drawing.ID)==section.ID,
                        "Follower Section lacks an exact resident representation.","OBJECT_IDENTITY_MISMATCH");
                    var resident=tx.GetObject(sectionId,Db.OpenMode.ForRead) as Db.Section;
                    Need(resident!=null&&resident.GetType()==typeof(Db.Section),"Follower Section representation is not an ordinary native Section.");
                    var owner=tx.GetObject(resident.OwnerId,Db.OpenMode.ForRead) as Db.BlockTableRecord;
                    Need(owner!=null&&owner.IsLayout&&owner.OwnerId==host.Database.BlockTableId&&owner.Cast<Db.ObjectId>().Contains(sectionId),
                        "Follower Section is not a retained local layout member.");
                    var boundary=resident.Boundary;var state=SectionViewState.Read(section,cancel);var points=state.GetProperty("vertices");
                    Need(boundary!=null&&boundary.Count==points.GetArrayLength(),"Section representation and actual native cut vertices disagree.");
                    for(int i=0;i<boundary.Count;i++)
                    {
                        cancel();var point=boundary[i];var actual=new[]{point.X,point.Y,point.Z};
                        Need(actual.All(double.IsFinite)&&actual.Zip(points[i].EnumerateArray()).All(pair=>Math.Abs(pair.First-pair.Second.GetDouble())<=1e-8),
                            "Section representation and native WCS cut points disagree.");
                    }
                    var sectionOwned=NativeOwnedMetadata.Capture(resident,tx,cancel);using var data=resident.XData;using var links=resident.GetPersistentReactorIds();
                    var reactorIds=new List<object>();foreach(Db.ObjectId link in links)
                    {
                        cancel();Need(!link.IsNull&&!Gone(link),"Follower Section has an unavailable persistent reference.");
                        var reactor=tx.GetObject(link,Db.OpenMode.ForRead);Need(reactor.Database==resident.Database,"Follower Section persistent reference belongs to another drawing.");
                        reactorIds.Add(new{handle=Handle(link),owner=reactor.OwnerId.IsNull?null:Handle(reactor.OwnerId),type=reactor.GetType().FullName});
                    }
                    using var indicator=resident.IndicatorFillColor;
                    sections.Add(section.ID,Wire.Element(new{state,handle=Handle(sectionId),owner=Handle(resident.OwnerId),
                        native_document=section.DbEntity.Document.ID.ToString(),layer=Handle(resident.LayerId),linetype=Handle(resident.LinetypeId),properties=EntityAdapter.StoredProperties(resident),
                        name=resident.Name,native_state=resident.State.ToString(),live=resident.IsLiveSectionEnabled,
                        indicator=new{method=indicator.ColorMethod.ToString(),index=indicator.ColorIndex,indicator.Red,indicator.Green,indicator.Blue,indicator.HasColorName,indicator.ColorName,indicator.BookName},resident.IndicatorTransparency,
                        owned=sectionOwned,xdata=data==null?null:NativeOwnedMetadata.Buffer(data,cancel,bytes.Add),reactors=reactorIds.OrderBy(x=>Wire.Element(x).GetProperty("handle").GetString(),StringComparer.Ordinal).ToArray(),
                        settings=CadSectionCutState351.Settings(resident,tx,cancel)}));
                }
            }
            else Need(!view.IsBasedOnSection&&!view.IsSlice&&!view.DependsOnSlice,"Section-derived follower has no addressable native Section.");
            var entity=(Db.Entity)tx.GetObject(value.Entity,Db.OpenMode.ForRead);var layout=(Db.Layout)tx.GetObject(value.Layout,Db.OpenMode.ForRead);
            var metadata=CadDrawingViewEditState.Metadata(view);
            var stored=Wire.Element(new{handle=Handle(value.Entity),owner=Handle(value.Space),layout=Handle(value.Layout),layout_name=layout.LayoutName,
                layout_model=layout.ModelType,native_document=view.DbEntity.Document.ID.ToString(),layer=Handle(entity.LayerId),linetype=Handle(entity.LinetypeId),
                properties=EntityAdapter.StoredProperties(entity),metadata});
            var observed=value with{Metadata=metadata};var owned=CadDrawingViewLifecycleState345.Owned(observed,tx,cancel);
            entries.Add(new(observed,stored,owned));bytes.Add(new{stored,owned});
        }
        var sourcesRead=new List<Source>();var table=(Db.BlockTable)tx.GetObject(host.Database.BlockTableId,Db.OpenMode.ForRead);
        var model=(Db.BlockTableRecord)tx.GetObject(table[Db.BlockTableRecord.ModelSpace],Db.OpenMode.ForRead);
        foreach(Db.ObjectId id in model)
        {
            cancel();if(Gone(id))continue;var native=McObjectId.FromHandle(id.Handle.Value,drawing.ID);if(!sourceIds.Contains(native))continue;
            var entity=tx.GetObject(id,Db.OpenMode.ForRead) as Db.Solid3d;var source=native.GetObjectOfType<Mc3dSolid>();
            Need(entity!=null&&entity.GetType()==typeof(Db.Solid3d)&&entity.OwnerId==model.ObjectId&&source!=null&&source.ID==native,
                "Every selected follower source must retain one current top-level native solid representation.","DRAWING_VIEW_SOURCE_UNAVAILABLE");
            Need(sourcesRead.All(s=>s.Id!=native),"Follower source has duplicate physical representations.");
            var stored=Wire.Element(new{id=native.ToString(),owner_document=source.DbEntity.Document.ID.ToString(),owner=Handle(entity.OwnerId),
                layer=Handle(entity.LayerId),linetype=Handle(entity.LinetypeId),properties=EntityAdapter.StoredProperties(entity)});
            var geometry=Wire.Element(ModelFeatureOracle.Read(entity));var readable=CadNativeStyleIdentityMetadata352.Metadata(entity,host.Database,tx,cancel);
            sourcesRead.Add(new(native,id,stored,geometry,readable));bytes.Add(new{stored,geometry,readable});
        }
        Need(sourcesRead.Count==sourceIds.Count,"Selected follower source IDs are foreign, missing or ambiguous.","DRAWING_VIEW_SOURCE_UNAVAILABLE");
        foreach(var value in sections.Values)bytes.Add(value);
        var result=new Snapshot(host,drawing.ID,body.ID,entries.ToArray(),sourcesRead.OrderBy(s=>s.Id.ToString(),StringComparer.Ordinal).ToArray(),sections);
        result.Observations.UnionWith(observations);return result;
    }
    internal static bool OwnsDependent(Snapshot? snapshot,McObjectId id)=>snapshot!=null&&
        (snapshot.Views.Any(item=>item.View.Id==id)||snapshot.Sections.ContainsKey(id));
    internal static void PrepareRefresh(Mc3dSolid body,Snapshot before,Action? cancellation=null)
    {
        var cancel=cancellation??(()=>{});Need(Context(body,before.Drawing)==before.Document&&body.ID==before.Body,"Follower host/body context changed.");
        if(!before.Refreshed)
        {
            // The model writer has already marked its operation started. Never
            // invoke this branch from read/preflight or after native tracking ends.
            bool dirty=false;
            foreach(var item in before.Views)
            {
                cancel();var view=item.View.Id.GetObjectOfType<DrawingView>();Need(view!=null,"Follower disappeared before native refresh.");
                if(!view.EnableAutoUpdate||view.IsSuppressed)
                {before.Observations.Add("VIEW_AUTO_UPDATE_DISABLED_OR_SUPPRESSED: The stored manual/frozen projection state was retained; its pixels are not claimed updated.");continue;}
                view.GeomChanged=true;dirty=true;
            }
            if(dirty){cancel();McObjectManager.UpdateAll();}
            before.Refreshed=true;
        }
    }
    internal static void Verify(Mc3dSolid body,Snapshot before,McObjectId[] historyViews,Action? cancellation=null,bool refresh=false)
    {
        var cancel=cancellation??(()=>{});Need(Context(body,before.Drawing)==before.Document&&body.ID==before.Body,"Follower host/body context changed.");
        if(refresh)PrepareRefresh(body,before,cancel);
        var after=Capture(body,historyViews,cancel);
        before.Observations.UnionWith(after.Observations);
        Need(before.Views.Select(v=>v.View.Id).ToHashSet().SetEquals(after.Views.Select(v=>v.View.Id)),"Selected-source follower group membership changed.");
        foreach(var entry in before.Views)
        {
            cancel();var current=after.Views.Single(v=>v.View.Id==entry.View.Id);
            Need(current.View.Entity==entry.View.Entity&&current.View.Layout==entry.View.Layout&&current.View.Space==entry.View.Space&&Same(entry.Stored,current.Stored)&&Same(entry.Owned,current.Owned),
                "Follower identity, owner, source/tree/history, placement/settings or owned data changed.");
            if(before.Refreshed&&current.View.Native.EnableAutoUpdate&&!current.View.Native.IsSuppressed)ObserveGeometry(current.View.Native,before,cancel);
        }
        Need(before.Sections.Count==after.Sections.Count&&before.Sections.All(s=>after.Sections.TryGetValue(s.Key,out var value)&&Same(s.Value,value)),
            "Follower Section identity, geometry, basis, support or observed processing/backlinks changed.");
        Need(before.Sources.Select(s=>s.Id).ToHashSet().SetEquals(after.Sources.Select(s=>s.Id)),"Follower selected Model source identities changed.");
        foreach(var source in before.Sources)
        {
            cancel();var actual=after.Sources.Single(s=>s.Id==source.Id);Need(Same(source.Stored,actual.Stored),"A selected source native owner/display identity changed.");
            if(!before.MutableSources.Contains(source.Id))Need(source.Entity==actual.Entity&&Same(source.Geometry,actual.Geometry)&&Same(source.ReadableMetadata,actual.ReadableMetadata),"An unedited selected source representation, native geometry or readable owned/reference metadata changed.");
        }
    }
    static void ObserveGeometry(DrawingView view,Snapshot result,Action cancel)
    {
        cancel();List<G.EntityGeometry>? geometry;
        try{geometry=view.GetViewGeometry(true,0);}
        catch(NotSupportedException){cancel();result.Observations.Add("VIEW_PROJECTION_UNOBSERVED: Native projection reader is unavailable; selected sources and healthy view state remain observed.");return;}
        catch(Teigha.Runtime.Exception error)when(error.ErrorStatus.ToString() is "NotImplemented" or "NotImplementedYet")
        {cancel();result.Observations.Add("VIEW_PROJECTION_UNOBSERVED: Native projection reader is unavailable; selected sources and healthy view state remain observed.");return;}
        // Native/cancellation failures are not re-labelled as mere observation limits.
        try
        {
            if(geometry==null||geometry.Count==0){result.Observations.Add("VIEW_PROJECTION_EMPTY: Native projection is empty; inspect the affected drawing, since a valid section can lie outside material.");return;}
            var bytes=new ModelInventoryBudget(cancel);
            foreach(var item in geometry)
            {
                cancel();Need(item!=null,"Native projection contains a null geometry element.","INVALID_GEOMETRY");object? value=null;
                if(item.GeometryType==G.EntityGeomType.kLine){using var line=item.LineSeg;value=SketchGeometryReader.Line(line);}
                else if(item.GeometryType==G.EntityGeomType.kCircArc){using var arc=item.CircArc;value=SketchGeometryReader.Arc(arc);}
                if(value==null)result.Observations.Add("VIEW_PROJECTION_PARTIALLY_OBSERVED: Some native projected curve types have no detailed serializer; their actual types/count are observed without a fabricated edge identity.");
                bytes.Add(new{type=item.GeometryType.ToString(),geometry=value});
            }
        }
        finally{if(geometry!=null)foreach(var item in geometry.Distinct(ReferenceEqualityComparer.Instance))if(item is IDisposable disposable)disposable.Dispose();}
    }
    internal static Result Warnings(Result result,IEnumerable<ModelViewFollowers.Snapshot> snapshots)
    {
        var groups=snapshots.Select(v=>v.Standalone).Where(v=>v!=null&&(v.Views.Length>0||v.Observations.Count>0)).ToArray();
        if(groups.Length==0||!result.success)return result;
        var warnings=result.warnings.Concat(groups.Any(v=>v!.Views.Length>0)?[Warning]:Array.Empty<string>()).Concat(groups.SelectMany(v=>v!.Observations)).Distinct(StringComparer.Ordinal).ToArray();
        var observed=result with{warnings=warnings};Need(JsonSerializer.SerializeToUtf8Bytes(observed,Wire.Json).Length<=Wire.PayloadBudget,"The complete native model/follower receipt exceeds actual transport bytes.","RESPONSE_LIMIT_EXCEEDED");return observed;
    }
    internal static Result Reserve(Result result,IEnumerable<ModelViewFollowers.Snapshot> snapshots)
    {
        var groups=snapshots.Select(value=>value.Standalone).Where(value=>value!=null&&(value.Views.Length>0||value.Observations.Count>0)).ToArray();
        if(groups.Length==0)return result;
        var reserve=groups.Any(value=>value!.Views.Length>0)?new[]{Warning}.Concat(ObservationWarnings):new[]{SelectorWarning};
        return result with{warnings=result.warnings.Concat(reserve).Distinct(StringComparer.Ordinal).ToArray()};
    }
}
