using System.Text.Json;
using Multicad;
using Multicad.DatabaseServices;
using Multicad.Mc3D;
using NanoCAD.Contracts;
using HostMgd.ApplicationServices;
using Db=Teigha.DatabaseServices;

namespace NanoCADBridge;

// Direct machining can retain downstream documentation. Unknown reactors,
// actual feature histories and deletion/copy remain separate ownership cases.
internal static class DirectSolidDocumentation
{
    private static void Need(bool value,string message)
    {if(!value)throw new DomainException("UNSUPPORTED_SOLID_DEPENDENCIES",message);}

    internal static void RequireDependency(McObjectId dependency,McObjectId source)
    {
        var view=dependency.GetObjectOfType<DrawingView>();
        if(view!=null)
        {
            var sources=view.GetTop3dEntitiesInView();
            Need(view.ID==dependency&&view.Status.ToString()=="Ok"&&!view.IsSuppressed&&view.EnableAutoUpdate&&
                 sources!=null&&sources.Contains(source),"A dependent drawing view is unavailable, unhealthy or no longer refers to this body.");
            return;
        }
        var section=dependency.GetObjectOfType<Section>();
        if(section!=null)
        {
            var sources=section.GetTop3dEntitiesForSectioning();
            Need(section.Status.ToString()=="Ok"&&!section.LiveSectionEnabled&&sources!=null&&sources.Contains(source),
                 "A dependent section is unavailable or has a different source.");
            return;
        }
        throw new DomainException("UNSUPPORTED_SOLID_DEPENDENCIES","Direct editing cannot classify a dependent object as a drawing view or section.",false,
            new{dependency_id=dependency.ToString(),runtime_type=dependency.QueryObjectType()?.FullName});
    }

    internal static void CheckReactors(Db.Solid3d solid,Db.ObjectIdCollection reactors)
    {
        var doc=McDocument.ActiveDocument;
        var source=McObjectId.FromHandle(solid.Handle.Value,doc.ID);
        foreach(Db.ObjectId reactor in reactors)
        {
            Need(!reactor.IsNull&&!reactor.IsErased,"An attached reactor cannot be resolved.");
            var id=McObjectId.FromHandle(reactor.Handle.Value,doc.ID);
            if(id.IsNull)throw new DomainException("UNSUPPORTED_SOLID_DEPENDENCIES","An attached reactor is not an addressable native drawing dependency.",false,
                new{reactor_handle=reactor.Handle.ToString(),runtime_type=reactor.ObjectClass.Name});
            RequireDependency(id,source);
        }
    }

    internal sealed record Snapshot(JsonElement Common,Dictionary<string,JsonElement> Sections,HashSet<string> Targets);
    internal static Snapshot Capture(Document doc,IEnumerable<string> targets,Action cancel)
    {
        var common=DrawingViewWriter.Observe(doc,cancel);
        var sections=new Dictionary<string,JsonElement>();
        foreach(var entry in common.GetProperty("views").EnumerateArray())
        {
            cancel();
            var handle=Convert.ToInt64(entry.GetProperty("handle").GetString(),16);
            var view=McObjectId.FromHandle(handle,McDocument.ActiveDocument.ID).GetObjectOfType<DrawingView>();
            Need(view!=null&&view.Status.ToString()=="Ok"&&!view.IsSuppressed&&view.EnableAutoUpdate,"Existing documentation is not ready for automatic updates.");
            if(!view!.SectionID.IsNull)
            {
                var section=view.SectionID.GetObjectOfType<Section>();
                Need(section!=null&&!section.LiveSectionEnabled,"View section is unavailable or live.");
                sections[view.SectionID.ToString()]=SectionViewState.Read(section!,cancel);
            }
        }
        return new(common,sections,targets.ToHashSet(StringComparer.Ordinal));
    }

    private static bool IsGeneratedEntityType(string? type)=>type==typeof(Db.Line).FullName||type==typeof(Db.Arc).FullName||
        type==typeof(Db.Circle).FullName||type==typeof(Db.Polyline).FullName||
        type==typeof(Db.Ellipse).FullName||type==typeof(Db.Spline).FullName||
        type==typeof(Db.DBText).FullName||type==typeof(Db.MText).FullName||type==typeof(Db.BlockReference).FullName||
        CadAnnotationTransformEditor.Supported(type);

    private static Dictionary<string,JsonElement> CaptureCurves(Document doc,IEnumerable<string> handles)
    {
        using var tx=doc.Database.TransactionManager.StartTransaction();
        var table=(Db.BlockTable)tx.GetObject(doc.Database.BlockTableId,Db.OpenMode.ForRead);
        var model=table[Db.BlockTableRecord.ModelSpace];
        var result=new Dictionary<string,JsonElement>(StringComparer.Ordinal);
        foreach(var handle in handles)
        {
            var id=CadStructureWriter.Find(doc.Database,handle);
            var entity=tx.GetObject(id,Db.OpenMode.ForRead) as Db.Entity;
            Need(entity!=null&&entity.OwnerId==model,"A returned addition is no longer a top-level Model Space entity.");
            Need(IsGeneratedEntityType(entity!.GetType().FullName),"A returned entity has no supported read-only comparison.");
            if(CadAnnotationTransformEditor.Supported(entity.GetType()))
            {
                // Native annotation copies use a private stored-definition and
                // bounds snapshot, without text-editor/public response scopes.
                // This does not claim complete Region/hatch B-rep serialization.
                result.Add(handle,CadAnnotationTransformEditor.Snapshot(entity,()=>{}));continue;
            }
            // Reuse the read-only native curve serializers. Only geometry and
            // stored appearance enter this private comparison, not public refs.
            var detail=EntityAdapter.Detail(entity,EntityAdapter.Summary(entity,model,string.Empty,string.Empty),CancellationToken.None);
            result.Add(handle,Wire.Element(new{detail.geometry,detail.properties}));
        }
        return result;
    }

    internal static void RefreshAndVerify(Document doc,Snapshot before,IEnumerable<string>? addedHandles=null,IEnumerable<string>? erasedHandles=null,IEnumerable<string>? permittedErasedBodies=null)
    {
        // Only exact handles returned by the committed action may change the
        // inventory. A mixed batch can create curves or trim its selected curve;
        // never infer allowable changes from counts or the observed difference.
        var additions=(addedHandles??Array.Empty<string>()).ToArray();
        var erasures=(erasedHandles??Array.Empty<string>()).ToArray();
        bool HandlesValid(string[] values)=>values.All(h=>Validation.NormalizeHandle(h)==h)&&
            values.Distinct(StringComparer.Ordinal).Count()==values.Length;
        Need(HandlesValid(additions)&&HandlesValid(erasures),"Returned created or erased handles are invalid or duplicated.");
        var originalEntities=before.Common.GetProperty("entities").EnumerateArray()
            .ToDictionary(x=>x.GetProperty("handle").GetString()!,StringComparer.Ordinal);
        Need(additions.All(h=>!originalEntities.ContainsKey(h)),"A created handle already belonged to the original inventory.");
        var erased=erasures.ToHashSet(StringComparer.Ordinal);
        var erasableBodies=(permittedErasedBodies??Array.Empty<string>()).ToHashSet(StringComparer.Ordinal);
        Need(erasableBodies.All(h=>originalEntities.TryGetValue(h,out var e)&&before.Targets.Contains(h)&&e.GetProperty("type").GetString()==typeof(Db.Solid3d).FullName),
             "The preflight body-erasure permission has an invalid original target.");
        Need(!erased.Overlaps(additions)&&erasures.All(h=>originalEntities.ContainsKey(h)&&before.Targets.Contains(h)),
             "An erased handle was not a selected original entity, or was also returned as created.");
        var protectedHandles=before.Common.GetProperty("bodies").EnumerateArray().Where(x=>!erasableBodies.Contains(x.GetProperty("handle").GetString()!))
            .Concat(before.Common.GetProperty("views").EnumerateArray())
            .Concat(before.Common.GetProperty("sections").EnumerateArray())
            .Select(x=>x.GetProperty("handle").GetString()!).ToHashSet(StringComparer.Ordinal);
        Need(!erased.Overlaps(protectedHandles),"A mixed edit cannot remove native bodies, drawing views or sections through an inventory exception.");
        var expectedHandles=originalEntities.Keys.Where(h=>!erased.Contains(h)).Concat(additions).ToHashSet(StringComparer.Ordinal);
        var geometryTargets=before.Targets.Where(expectedHandles.Contains).Concat(additions).ToHashSet(StringComparer.Ordinal);
        var committed=DrawingViewWriter.Observe(doc,()=>{});
        var committedEntities=committed.GetProperty("entities").EnumerateArray()
            .ToDictionary(x=>x.GetProperty("handle").GetString()!,StringComparer.Ordinal);
        Need(expectedHandles.SetEquals(committedEntities.Keys),"The committed entity inventory differs from the original entities and exact returned changes.");
        Need(additions.All(h=>committedEntities[h].GetProperty("type").GetString()==typeof(Db.Solid3d).FullName||
             IsGeneratedEntityType(committedEntities[h].GetProperty("type").GetString())),"A returned addition is not a supported native solid or copied/contour entity.");
        var addedBodies=additions.Where(h=>committedEntities[h].GetProperty("type").GetString()==typeof(Db.Solid3d).FullName).ToArray();
        var addedCurves=additions.Where(h=>IsGeneratedEntityType(committedEntities[h].GetProperty("type").GetString())).ToArray();
        var committedBodies=committed.GetProperty("bodies").EnumerateArray()
            .Where(b=>geometryTargets.Contains(b.GetProperty("handle").GetString()!))
            .ToDictionary(b=>b.GetProperty("handle").GetString()!,b=>b.GetProperty("geometry").Clone());
        Need(addedBodies.All(committedBodies.ContainsKey),"A returned solid part has no readable committed geometry.");
        // New curves come from already validated native contour/copy routes.
        // Do not impose the public geometry-reader scope on arbitrary existing
        // curves that were only selected for a display-property change.
        var committedCurves=CaptureCurves(doc,addedCurves);
        // A Teigha edit changes the direct body but does not necessarily mark
        // MultiCAD projection caches dirty. UpdateAll alone can retain old views.
        foreach(var handle in before.Targets.Where(h=>!erased.Contains(h)))
        {
            var id=McObjectId.FromHandle(Convert.ToInt64(handle,16),McDocument.ActiveDocument.ID);
            var body=id.IsNull?null:id.GetObjectOfType<Mc3dAlienSolid>();
            if(body!=null)Need(body.RebuildPart(),"Native direct-body documentation rebuild was rejected.");
        }
        foreach(var entry in before.Common.GetProperty("views").EnumerateArray())
        {
            var view=McObjectId.FromHandle(Convert.ToInt64(entry.GetProperty("handle").GetString(),16),McDocument.ActiveDocument.ID).GetObjectOfType<DrawingView>();
            Need(view!=null,"A dependent view disappeared before regeneration.");
            view!.GeomChanged=true;
        }
        McObjectManager.UpdateAll();
        var after=Capture(doc,before.Targets,()=>{});
        bool Same(JsonElement a,JsonElement b)=>ModelParameterSnapshot.Equivalent(a,b);
        foreach(var field in new[]{"views","sections"})
            Need(Same(before.Common.GetProperty(field),after.Common.GetProperty(field)),"Direct edit changed documentation identity, settings, source links or entity inventory.");
        var entities=after.Common.GetProperty("entities").EnumerateArray().ToDictionary(x=>x.GetProperty("handle").GetString()!);
        Need(expectedHandles.SetEquals(entities.Keys),"Entity inventory differs from the original entities and exact returned changes after rebuilding documentation.");
        foreach(var handle in additions)
            Need(Same(committedEntities[handle],entities[handle]),"A created entity's identity or display properties changed during documentation rebuild.");
        foreach(var old in before.Common.GetProperty("entities").EnumerateArray())
        {
            var handle=old.GetProperty("handle").GetString()!;
            if(erased.Contains(handle))continue;
            Need(entities.TryGetValue(handle,out var current)&&Same(old.GetProperty("type"),current.GetProperty("type")),"Entity identity changed.");
            if(!before.Targets.Contains(handle))Need(Same(old,current),"An unrelated entity's properties changed.");
        }
        Need(before.Sections.Count==after.Sections.Count&&before.Sections.All(p=>after.Sections.TryGetValue(p.Key,out var now)&&Same(p.Value,now)),
             "Direct edit changed a section's definition or associations.");
        var bodies=after.Common.GetProperty("bodies").EnumerateArray().ToDictionary(x=>x.GetProperty("handle").GetString()!);
        foreach(var handle in addedBodies)
            Need(bodies.TryGetValue(handle,out var part)&&Same(committedBodies[handle],part.GetProperty("geometry")),
                 "Documentation rebuild changed or removed a committed solid part.");
        foreach(var old in before.Common.GetProperty("bodies").EnumerateArray())
        {
            var handle=old.GetProperty("handle").GetString()!;
            if(erased.Contains(handle)&&erasableBodies.Contains(handle))continue;
            Need(bodies.TryGetValue(handle,out var current),"Direct body identity was lost after rebuilding documentation.");
            foreach(var field in new[]{"mc_id","projection_id","contents"})
                Need(Same(old.GetProperty(field),current.GetProperty(field)),"Direct body history or projection identity changed.");
            if(committedBodies.TryGetValue(handle,out var committedGeometry))
                Need(Same(committedGeometry,current.GetProperty("geometry")),"Documentation rebuild changed the committed source geometry.");
            if(!before.Targets.Contains(handle))Need(Same(old.GetProperty("geometry"),current.GetProperty("geometry")),"An unrelated body changed.");
        }
        var curves=CaptureCurves(doc,addedCurves);
        Need(committedCurves.Count==curves.Count&&committedCurves.All(p=>curves.TryGetValue(p.Key,out var current)&&Same(p.Value,current)),
             "Documentation rebuild changed the committed stored geometry or display properties of a created entity.");
        foreach(var entry in after.Common.GetProperty("views").EnumerateArray())
        {
            var view=McObjectId.FromHandle(Convert.ToInt64(entry.GetProperty("handle").GetString(),16),McDocument.ActiveDocument.ID).GetObjectOfType<DrawingView>()!;
            var geometry=view.GetViewGeometry(true,0);
            Need(geometry!=null&&geometry.Count>0,"An updated projection has no geometry.");
        }
        doc.Editor.Regen();
    }
}
