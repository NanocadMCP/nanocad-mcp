using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using Multicad;
using Multicad.DatabaseServices;
using Multicad.Mc3D;
using NanoCAD.Contracts;
using Db=Teigha.DatabaseServices;

namespace NanoCADBridge;

// Document construction is independent of GetPartContents. All filtering and
// exact work-ID resolution use the verified document, never a persistent-Guid
// lookup or an inferred top-level document equivalence.
internal static class ModelProfileDiscovery348
{
    static void Need([DoesNotReturnIf(false)] bool ok,string code,string message){if(!ok)throw new DomainException(code,message);}
    internal static string? Id(McObjectId id)=>id.IsNull?null:id.ToString();
    internal static McDocument DrawingDocument(DocumentState state)
    {
        var doc=McDocument.ActiveDocument;
        Need(doc!=null&&state.Document.IsNamedDrawing&&!string.IsNullOrWhiteSpace(doc.Path)&&
            string.Equals(Path.GetFullPath(doc.Path),Path.GetFullPath(state.Document.Name),StringComparison.OrdinalIgnoreCase),
            "DOCUMENT_CONTEXT_MISMATCH","Use the matching named native model document.");
        return doc;
    }
    internal static McDocument OwnerDocument(Mc3dSolid body,DocumentState state)
    {
        // The drawing container maps DWG handles. AddPlanarSketch instead uses
        // body.DbEntity.Document, which can be a distinct native model document.
        // Its identity is admitted through this already-verified resident body,
        // not through an invented path equality with the drawing container.
        DrawingDocument(state);var doc=body.DbEntity.Document;
        Need(doc!=null&&!doc.ID.IsNull,"DOCUMENT_CONTEXT_MISMATCH","The selected body's actual native owner document is unavailable.");
        return doc;
    }
    internal static McObjectId[] Ids<T>(McDocument doc,Action cancel)
    {
        cancel();var filter=ObjectFilter.Create(false).AddDoc(doc).AddType(typeof(T));
        filter.IncludeChildren=true;filter.IncludeInvisible=true;filter.ExcludeHiddenLayers=false;filter.ExcludeSystemLayers=false;
        var ids=filter.GetObjects();Need(ids!=null,"PROFILE_DISCOVERY_UNAVAILABLE","Native document construction inventory is unavailable.");
        // The SDK returns all matching IDs, but a discovery page only serializes
        // its measured byte slice. Do not apply a full-inventory response ceiling
        // to this transient scan before paging the actual result.
        return ids.Distinct().OrderBy(id=>{cancel();return id.ToString();},StringComparer.OrdinalIgnoreCase).ToArray();
    }
    internal static McObjectId[] ConstructionIds(McDocument doc,Action cancel)
    {
        var ids=Ids<PlanarSketch>(doc,cancel).Concat(Ids<SketchProfile>(doc,cancel)).Concat(Ids<ProjectionSketchEnt>(doc,cancel)).Distinct().ToArray();
        ModelInventoryBudget.Ids(ids,cancel);return ids;
    }
    internal static SketchProfile Resolve(string profileId,McDocument doc,Action cancel)
        =>ModelNativeObjectResolver415.Profile(profileId,doc,cancel);
    internal sealed record ProjectionState(McObjectId[] Ids,ProjectedHistorySources.Source[] Sources,JsonElement Bindings)
    {internal McObjectId[] NativeReferences {get;init;}=[];}
    internal static ProjectionState Projections(PlanarSketch sketch,SketchPlaneSpec plane,DocumentState state,Db.Transaction tx,Action cancel,
        McObjectId[]? candidates=null)
    {
        DrawingDocument(state);var doc=sketch.DbEntity.Document;
        Need(doc!=null&&!doc.ID.IsNull,"DOCUMENT_CONTEXT_MISMATCH","The selected sketch's actual native owner document is unavailable.");
        var selected=new List<ProjectionSketchEnt>();var bytes=new ModelInventoryBudget(cancel);
        foreach(var id in candidates??Ids<ProjectionSketchEnt>(doc,cancel))
        {
            cancel();var projection=id.GetObjectOfType<ProjectionSketchEnt>();
            if(projection==null)
            {
                Need(candidates!=null,"PROFILE_DISCOVERY_UNAVAILABLE","A document projection identity cannot be read.");continue;
            }
            Need(projection.ID==id,"OBJECT_IDENTITY_MISMATCH","A projection lookup returned another native identity.");
            if(projection.HostSketchID!=sketch.ID)continue;
            // SDK: PlanarObjectID is the sketch plane-defining support, which
            // may be Null for an automatic geometry-created plane. It is not
            // generated projection geometry or a source subentity identity.
            Need(projection.DbEntity.Document!=null&&projection.DbEntity.Document.ID==doc.ID&&!projection.SrcObjectID.IsNull&&
                projection.PlanarObjectID==sketch.PlanarEntityID&&projection.Status.ToString()=="Ok"&&!projection.IsSuppressed,
                "MODEL_STATE_UNAVAILABLE","The selected profile has an unavailable native projection binding.");
            selected.Add(projection);
        }
        // Document filtering and body composition have different enumeration
        // orders. A public exact-ID token must match consumer preflight for the
        // same native bindings regardless of which inventory supplied them.
        selected=selected.OrderBy(p=>{cancel();return p.ID.ToString();},StringComparer.OrdinalIgnoreCase).ToList();
        var model=((Db.BlockTable)tx.GetObject(state.Document.Database.BlockTableId,Db.OpenMode.ForRead))[Db.BlockTableRecord.ModelSpace];
        var sources=new List<ProjectedHistorySources.Source>();var bindings=new List<object>();var nativeReferences=new List<McObjectId>();
        foreach(var projection in selected)
        {
            cancel();var depends=projection.GetDependsOn(false,false);
            Need(depends!=null,"HISTORY_UNAVAILABLE","Native projection dependency inventory is unavailable.");ModelInventoryBudget.Ids(depends,cancel);
            var native=projection.SrcObjectID.IsEdge;
            if(native)
            {
                var parent=projection.SrcObjectID.ParentId.GetObjectOfType<Mc3dSolid>();
                Need(parent!=null&&parent.ID==projection.SrcObjectID.ParentId&&parent.DbEntity.Document?.ID==doc.ID&&
                    (sketch.HostSolidID.IsNull||sketch.HostSolidID==parent.ID),"SKETCH_OWNERSHIP_MISMATCH","The retained native reference belongs to a different part/document.");
                nativeReferences.Add(projection.SrcObjectID);
            }
            var binding=new{id=projection.ID.ToString(),host_sketch_id=sketch.ID.ToString(),source_object_id=projection.SrcObjectID.ToString(),
                planar_object_id=Id(projection.PlanarObjectID),dependencies=depends.Select(id=>id.ToString()).OrderBy(id=>id,StringComparer.Ordinal).ToArray(),
                source_kind=native?"native_part_edge":"drawing_object",
                source_geometry=native?(JsonElement?)ModelSketchNativeReferences410.Geometry(projection.SrcObjectID,projection.SrcObjectID.ParentId,cancel):null};
            bytes.Add(binding);bindings.Add(binding);
        }
        foreach(var sourceId in selected.Select(p=>p.SrcObjectID).Where(id=>!id.IsEdge).Distinct().OrderBy(id=>id.ToString(),StringComparer.Ordinal))
        {
            cancel();long handle=sourceId.Handle;bool found=state.Document.Database.TryGetObjectId(new Db.Handle(handle),out var raw);
            Need(handle!=0&&found&&!raw.IsNull&&!raw.IsErased&&!raw.IsEffectivelyErased,
                "PROFILE_SOURCE_UNAVAILABLE","The exact drawing projection source has no current database handle.");
            Need(tx.GetObject(raw,Db.OpenMode.ForRead) is Db.Entity rawEntity&&rawEntity.OwnerId==model,
                "PROFILE_SOURCE_UNAVAILABLE","A profile projection source is absent from current top-level Model Space.");
            var source=DrawingProfileWriter.SourceOf(raw,state,tx,false,plane);
            Need(source.Native==sourceId,"OBJECT_IDENTITY_MISMATCH","The projection source no longer resolves to its exact native identity.");
            var entity=(Db.Entity)tx.GetObject(raw,Db.OpenMode.ForRead);double[] geometry;
            if(entity.GetType()==typeof(Db.Line))
            {var line=(Db.Line)entity;geometry=[line.StartPoint.X,line.StartPoint.Y,line.StartPoint.Z,line.EndPoint.X,line.EndPoint.Y,line.EndPoint.Z,line.Thickness];}
            else if(entity.GetType()==typeof(Db.Arc))
            {var arc=(Db.Arc)entity;geometry=[arc.Center.X,arc.Center.Y,arc.Center.Z,arc.Radius,arc.Normal.X,arc.Normal.Y,arc.Normal.Z,arc.Thickness,arc.StartAngle,arc.EndAngle,arc.StartPoint.X,arc.StartPoint.Y,arc.StartPoint.Z,arc.EndPoint.X,arc.EndPoint.Y,arc.EndPoint.Z];}
            else
            {var circle=(Db.Circle)entity;geometry=[circle.Center.X,circle.Center.Y,circle.Center.Z,circle.Radius,circle.Normal.X,circle.Normal.Y,circle.Normal.Z,circle.Thickness];}
            Need(geometry.All(double.IsFinite),"INVALID_GEOMETRY","Profile source geometry is nonfinite.");
            var item=new ProjectedHistorySources.Source(sourceId.ToString(),raw.Handle.ToString(),entity.Layer,geometry,
                selected.Where(p=>p.SrcObjectID==sourceId).Select(p=>p.ID.ToString()).OrderBy(id=>id,StringComparer.Ordinal).ToArray(),[]);
            bytes.Add(item);sources.Add(item);
        }
        return new(selected.Select(p=>p.ID).ToArray(),sources.ToArray(),Wire.Element(bindings)){NativeReferences=nativeReferences.Distinct().ToArray()};
    }
    internal static ModelProfileOwnership348 Ownership(ModelProfileLifecycleState340.Snapshot snapshot)=>new(
        snapshot.Stored.GetProperty("mc_document_id").GetString()!,Id(snapshot.Profile.HostSolidID),Id(snapshot.Sketch.HostSolidID),
        snapshot.Profile.HostSolidID.IsNull&&snapshot.Sketch.HostSolidID.IsNull?"document_construction":"native_host_affinity");
    internal static ModelProfileDiscoveryRead348 Read(DocumentState state,Db.Transaction tx,Action cancel,
        bool includeInventory,string? cursor,string[]? profileIds,string[] bodyHandles)
    {
        if(!includeInventory&&(profileIds==null||profileIds.Length==0))return new(null,[]);
        var drawing=DrawingDocument(state);var documents=new Dictionary<McObjectId,McDocument>();
        foreach(string handle in bodyHandles)
        {
            cancel();var id=McObjectId.FromHandle(unchecked((long)Convert.ToUInt64(handle,16)),drawing.ID);var body=id.IsNull?null:id.GetObjectOfType<Mc3dSolid>();
            Need(body!=null&&body.ID==id,"OBJECT_IDENTITY_MISMATCH","Document profile scope requires the selected current native bodies.");
            var owner=OwnerDocument(body,state);documents[owner.ID]=owner;
        }
        Need(documents.Count==1,"PROFILE_DOCUMENT_SCOPE_AMBIGUOUS",
            "The selected bodies do not identify one native owner document. Select bodies sharing the intended profile's native document scope; ordinary body history remains available independently.");
        var doc=documents.Values.Single();ModelProfileInventory348? inventory=null;
        if(includeInventory)
        {
            var items=new List<ModelProfileIdentity348>();int used=2;bool complete=true;
            foreach(var id in Ids<SketchProfile>(doc,cancel))
            {
                cancel();if(cursor!=null&&StringComparer.OrdinalIgnoreCase.Compare(id.ToString(),cursor)<=0)continue;
                var profile=Resolve(id.ToString(),doc,cancel);
                var item=new ModelProfileIdentity348(id.ToString(),Id(profile.ParentSketchID),Id(profile.HostSolidID),Id(profile.Dependent3dFeatureID));
                int size=JsonSerializer.SerializeToUtf8Bytes(item,Wire.Json).Length+1;
                if(used+size>128*1024&&items.Count>0){complete=false;break;}
                used+=size;items.Add(item);
            }
            inventory=new(doc.ID.ToString(),"same_document_native_profile_identities_not_body_composition",items.ToArray(),complete,
                complete?null:items[^1].profile_id);
        }
        var profiles=new List<ModelProfileRead340>();var budget=new ModelInventoryBudget(cancel);
        foreach(string id in profileIds??[])
        {
            cancel();var profile=Resolve(id,doc,cancel);var detail=ModelProfileLifecycleState340.Read(profile,null,state,tx,cancel,doc);
            budget.Add(detail);profiles.Add(detail);
        }
        return new(inventory,profiles.ToArray());
    }
}
