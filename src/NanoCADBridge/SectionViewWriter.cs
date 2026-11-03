using System.Text.Json;
using Multicad;
using Multicad.DatabaseServices;
using Multicad.Geometry;
using Multicad.Mc3D;
using NanoCAD.Contracts;
using NanoCAD.Ipc;
using Db=Teigha.DatabaseServices;
using static NanoCADBridge.SectionViewState;

namespace NanoCADBridge;
internal static class SectionViewWriter
{
    internal static bool Handles(StructureWriteArguments args)=>args.Operations.Length==1 && args.Operations[0] is CreateSectionViewEdit;
    internal static void Preflight(StructureWriteArguments args,DocumentState state,Db.Transaction tx,Action cancel)
    {
        var loaded=AppDomain.CurrentDomain.GetAssemblies().Select(a=>a.GetName().Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        Need(loaded.Contains("mapimgd") && loaded.Contains("MapiBaseTypes"),"MultiCAD runtime not already loaded.","CAPABILITY_UNAVAILABLE");
        var db=state.Document.Database;
        var table=(Db.BlockTable)tx.GetObject(db.BlockTableId,Db.OpenMode.ForRead);
        Need(state.Document.IsNamedDrawing && db.CurrentSpaceId==table[Db.BlockTableRecord.ModelSpace],"Named Model Space drawing required.","OUT_OF_SCOPE");
        var layer=(Db.LayerTableRecord)tx.GetObject(db.Clayer,Db.OpenMode.ForRead);
        Need(layer.Name=="0" && !layer.IsLocked && !layer.IsFrozen && !layer.IsOff && !layer.IsDependent,"Current layer0 must be available.","LAYER_UNAVAILABLE");
        var layers=(Db.LayerTable)tx.GetObject(db.LayerTableId,Db.OpenMode.ForRead);
        Need(layers.Has("VIEWS"),"Pre-create VIEWS in a separate committed create_layer request.","LAYER_UNAVAILABLE");
        var viewLayer=(Db.LayerTableRecord)tx.GetObject(layers["VIEWS"],Db.OpenMode.ForRead);
        Need(!viewLayer.IsLocked && !viewLayer.IsFrozen && !viewLayer.IsOff && !viewLayer.IsDependent,"VIEWS must be available.","LAYER_UNAVAILABLE");
        var spec=(CreateSectionViewEdit)args.Operations[0];var snapshot=Capture(state.Document,cancel);ValidateSources(spec,snapshot);
        SectionSourceSelection357.RequireSupportedScope(spec,snapshot,cancel);
        ResolveSources(spec,state,tx,cancel);
    }
    private static McObjectId[] ResolveSources(CreateSectionViewEdit spec,DocumentState state,Db.Transaction tx,Action cancel)
    {
        var mc=CadDrawingViewEditState.Context(state);var db=state.Document.Database;
        var table=(Db.BlockTable)tx.GetObject(db.BlockTableId,Db.OpenMode.ForRead);
        var model=table[Db.BlockTableRecord.ModelSpace];var sources=new List<McObjectId>();var measured=new ModelInventoryBudget(cancel);
        foreach(var handle in spec.SourceHandles)
        {
            cancel();Need(db.TryGetObjectId(new Db.Handle(unchecked((long)Convert.ToUInt64(handle,16))),out var id)&&!id.IsNull&&id.IsValid&&!id.IsErased&&!id.IsEffectivelyErased,"Source body not found.","OBJECT_NOT_FOUND");
            var body=tx.GetObject(id,Db.OpenMode.ForRead) as Db.Solid3d;
            Need(body!=null&&body.GetType()==typeof(Db.Solid3d)&&body.OwnerId==model,"Selected source must be an exact current top-level Model solid.","SOURCE_SCOPE_MISMATCH");
            var sourceLayer=(Db.LayerTableRecord)tx.GetObject(body!.LayerId,Db.OpenMode.ForRead);
            Need(!sourceLayer.IsLocked && !sourceLayer.IsFrozen && !sourceLayer.IsOff && !sourceLayer.IsDependent,"Every source layer must be available.","LAYER_UNAVAILABLE");
            var source=McObjectId.FromHandle(id.Handle.Value,mc.ID);Need(!source.IsNull,"Selected source has no native projection identity.","SOURCE_SCOPE_MISMATCH");
            measured.Add(source.ToString());sources.Add(source);
        }
        Need(sources.Count==spec.SourceHandles.Length&&sources.Distinct().Count()==sources.Count,"Selected source identities are incomplete/duplicated.","SOURCE_SCOPE_MISMATCH");
        return sources.ToArray();
    }
    private static void ValidateSources(CreateSectionViewEdit spec,JsonElement snapshot)
    {
        var common=snapshot.GetProperty("common");
        var sections=snapshot.GetProperty("sections").EnumerateArray().ToArray();var views=common.GetProperty("views").EnumerateArray().ToArray();
        
        Need(sections.All(s=>s.GetProperty("data").GetProperty("status").GetString()=="Ok" && !s.GetProperty("data").GetProperty("live_section_enabled").GetBoolean()) &&
            views.All(v=>v.GetProperty("status").GetString()=="Ok" && !v.GetProperty("suppressed").GetBoolean()),"Existing sections/views must be healthy, unsuppressed and not live.","MODEL_STATE_UNAVAILABLE");
        Need(!views.Any(v=>string.Equals(v.GetProperty("name").GetString(),spec.Name,StringComparison.OrdinalIgnoreCase))&&
            !common.GetProperty("paper_views").EnumerateArray().Any(v=>string.Equals(v.GetProperty("metadata").GetProperty("name").GetString(),spec.Name,StringComparison.OrdinalIgnoreCase)),"View name already exists.","NAME_COLLISION");
        var bodies=common.GetProperty("bodies").EnumerateArray().ToArray();
        var chosen=spec.SourceHandles.ToHashSet(StringComparer.Ordinal);
        var selected=bodies.Where(body=>chosen.Contains(body.GetProperty("handle").GetString()!)).ToArray();
        Need(selected.Length==chosen.Count&&selected.All(body=>body.GetProperty("projection_id").ValueKind==JsonValueKind.String),
            "source_handles must identify current addressable top-level direct/native Model solids.","SOURCE_SCOPE_MISMATCH");
        bool Crosses(JsonElement body)
        {
            var bounds=body.GetProperty("geometry");var n=Coordinates(spec.ViewingDirection);var p=Coordinates(spec.SectionLine[0]);double low=0,high=0;
            for(int i=0;i<3;i++)
            {
                double a=(bounds.GetProperty("min")[i].GetDouble()-p[i])*n[i],b=(bounds.GetProperty("max")[i].GetDouble()-p[i])*n[i];
                low+=Math.Min(a,b);high+=Math.Max(a,b);
            }
            return low<0 && high>0;
        }
        Need(selected.Any(Crosses),"Cut plane must lie strictly inside at least one selected source body's bounds.","OUT_OF_SCOPE");
    }
    private static double[] Coordinates(Point3 p)=>new[]{p.X,p.Y,p.Z};
    private static JsonElement[] Added(JsonElement before,JsonElement after)
    {
        var oldHandles=before.EnumerateArray().Select(v=>v.GetProperty("handle").GetString()!).ToHashSet(StringComparer.Ordinal);
        return after.EnumerateArray().Where(v=>!oldHandles.Contains(v.GetProperty("handle").GetString()!)).ToArray();
    }
    private static void Retained(JsonElement before,JsonElement after)
    {
        var handles=before.EnumerateArray().Select(v=>v.GetProperty("handle").GetString()!).ToHashSet(StringComparer.Ordinal);
        Need(Same(before,Wire.Element(after.EnumerateArray().Where(v=>handles.Contains(v.GetProperty("handle").GetString()!)).ToArray())),"Existing section/view state changed.");
    }
    private static void Verify(JsonElement before,JsonElement after,CreateSectionViewEdit spec,Action cancel)
    {
        var old=before.GetProperty("common");var current=after.GetProperty("common");
        Retained(before.GetProperty("sections"),after.GetProperty("sections"));Retained(old.GetProperty("views"),current.GetProperty("views"));Retained(before.GetProperty("view_links"),after.GetProperty("view_links"));
        var sections=Added(before.GetProperty("sections"),after.GetProperty("sections"));var views=Added(old.GetProperty("views"),current.GetProperty("views"));var links=Added(before.GetProperty("view_links"),after.GetProperty("view_links"));
        Need(sections.Length==1 && views.Length==1 && links.Length==1,"Expected exactly one added Section and one added view.");
        var section=sections[0];var data=section.GetProperty("data");var view=views[0];var link=links[0];
        SectionSourceSelection357.Retained(before.GetProperty("section_settings"),after.GetProperty("section_settings"),section.GetProperty("handle").GetString()!,cancel);
        var requested=spec.SourceHandles.ToHashSet(StringComparer.Ordinal);
        var sourceIds=old.GetProperty("bodies").EnumerateArray().Where(body=>requested.Contains(body.GetProperty("handle").GetString()!))
            .Select(b=>b.GetProperty("projection_id").GetString()!).OrderBy(s=>s,StringComparer.Ordinal).ToArray();
        Need(Same(view.GetProperty("sources"),Wire.Element(sourceIds)),"Native section view source set differs from requested Model bodies.");
        // The fresh Section settings select the same sources in each native
        // generation mode; processing and actual view participation are verified separately.
        var processing=data.GetProperty("top_source_ids").EnumerateArray().Select(item=>item.GetString()).ToArray();
        Need(processing.All(id=>!string.IsNullOrEmpty(id))&&processing.Distinct(StringComparer.Ordinal).Count()==processing.Length,"Section processing inventory contains invalid/duplicate identities.");
        Need(data.GetProperty("state").GetString()=="Plane" && data.GetProperty("status").GetString()=="Ok" && !data.GetProperty("live_section_enabled").GetBoolean() && data.GetProperty("associated_plane_id").ValueKind==JsonValueKind.Null,"Section state changed.");
        Need(Same(data.GetProperty("vertices"),Wire.Element(spec.SectionLine.Select(Coordinates).ToArray())) && Same(data.GetProperty("viewing_direction"),Wire.Element(Coordinates(spec.ViewingDirection))) && Same(data.GetProperty("vertical_direction"),Wire.Element(Coordinates(spec.VerticalDirection))),"Section placement/direction differs from request.");
        Need(data.GetProperty("view_ids").GetArrayLength()==1 && data.GetProperty("view_ids")[0].GetString()==view.GetProperty("mc_id").GetString() && link.GetProperty("section_id").GetString()==data.GetProperty("mc_id").GetString() && link.GetProperty("is_section").GetBoolean() && link.GetProperty("is_root_section").GetBoolean() && !link.GetProperty("is_slice").GetBoolean(),"Section-view association mismatch.");
        Need(view.GetProperty("status").GetString()=="Ok" && view.GetProperty("name").GetString()==spec.Name && view.GetProperty("scale").GetDouble()==spec.Scale && view.GetProperty("design_scale").GetDouble()==1 && view.GetProperty("root").GetString()==view.GetProperty("mc_id").GetString() && view.GetProperty("children").GetArrayLength()==0 && view.GetProperty("direction").GetString()=="kViewFront" && Same(view.GetProperty("origin"),Wire.Element(new[]{spec.Origin.X,spec.Origin.Y,0d})),"Section view properties differ from request.");
        foreach(var field in new[]{"bodies","blocks","layers","current_layer","view_layer","paper_views"})Need(Same(old.GetProperty(field),current.GetProperty(field)),"Unrelated observed state changed: "+field);
        var handles=new[]{section.GetProperty("handle").GetString()!,view.GetProperty("handle").GetString()!}.ToHashSet(StringComparer.Ordinal);
        var entities=current.GetProperty("entities").EnumerateArray().ToArray();
        Need(handles.Count==2 && entities.Length==old.GetProperty("entities").GetArrayLength()+2 && Same(old.GetProperty("entities"),Wire.Element(entities.Where(e=>!handles.Contains(e.GetProperty("handle").GetString()!)).ToArray())),"Unexpected model entity changes.");
        Need(entities.Single(e=>e.GetProperty("handle").GetString()==section.GetProperty("handle").GetString()).GetProperty("layer").GetString()==old.GetProperty("current_layer").GetString() && entities.Single(e=>e.GetProperty("handle").GetString()==view.GetProperty("handle").GetString()).GetProperty("layer").GetString()==old.GetProperty("view_layer").GetString(),"Created object layer differs.");
    }
    internal static Result Execute(StructureWriteArguments args,DocumentState state,WriteJob job,ReadContext context,JsonLog log)
    {
        McUndoPoint? tracker=null;bool started=false,commitAttempted=false;JsonElement before=default;
        var geometryResources=new HashSet<IDisposable>(ReferenceEqualityComparer.Instance);
        Exception? ReleaseGeometry()
        {
            var failures=new List<Exception>();var resources=geometryResources.ToArray();geometryResources.Clear();
            foreach(var resource in resources)try{resource.Dispose();}catch(Exception error){failures.Add(error);}
            return failures.Count==0?null:new AggregateException("Section projection resource release failed.",failures);
        }
        void Trace(string stage)=>log.Write(job.Operation,job.OriginalRequestId,"section_write_checkpoint",transaction:stage,operationId:job.OperationId);
        void Close(){var current=tracker;tracker=null;if(current==null)return;try{current.Stop();}finally{current.Dispose();}}
        try
        {
            var spec=(CreateSectionViewEdit)args.Operations[0];before=Capture(state.Document,job.CheckCancellation);ValidateSources(spec,before);
            SectionSourceSelection357.RequireSupportedScope(spec,before,job.CheckCancellation);
            McObjectId[] sourceIds;McDocument sheet;
            using(var selector=state.Document.Database.TransactionManager.StartTransaction())
            {
                sourceIds=ResolveSources(spec,state,selector,job.CheckCancellation);
                var lookup=CadDrawingViewEditState.Context(state);var blocks=(Db.BlockTable)selector.GetObject(state.Document.Database.BlockTableId,Db.OpenMode.ForRead);
                var model=blocks[Db.BlockTableRecord.ModelSpace];sheet=McDocumentsManager.GetActiveSheet()??throw new DomainException("DOCUMENT_CONTEXT_MISMATCH","The active native Model sheet is unavailable.");
                Need(sheet.IsModel&&McObjectId.FromHandle(model.Handle.Value,lookup.ID)==sheet.ID&&state.Document.Database.CurrentSpaceId==model,
                    "Section creation requires the exact active native Model sheet identity.","DOCUMENT_CONTEXT_MISMATCH");
            }
#if DRAWING_VIEW_WRITE_PROBE
            DrawingViewWriteProbe.RecordProductSnapshot(args,state,"before",before);
#endif
            job.CheckCancellation();tracker=new McUndoPoint(false);tracker.Start();started=true;Trace("tracking_started");
            var section=new Section();var sectionId=section.ID;
            Need(!sectionId.IsNull&&(section.DbEntity.DocumentID.IsNull||section.DbEntity.DocumentID==sheet.ID),"Fresh Section has invalid/foreign native identity.","OBJECT_IDENTITY_MISMATCH");
            Need(McObjectManager.Add2Document(section.DbEntity,sheet)&&section.ID==sectionId&&section.DbEntity.DocumentID==sheet.ID,
                "Fresh Section registration rejected or changed native identity/document.","OBJECT_IDENTITY_MISMATCH");
            Need(section.SetVertices(spec.SectionLine.Select(p=>new Point3d(p.X,p.Y,p.Z)).ToArray()),"Section vertices rejected.");
            section.State=Section.StateType.Plane;section.LiveSectionEnabled=false;
            section.VerticalDirection=new Vector3d(spec.VerticalDirection.X,spec.VerticalDirection.Y,spec.VerticalDirection.Z);
            section.ViewingDirection=new Vector3d(spec.ViewingDirection.X,spec.ViewingDirection.Y,spec.ViewingDirection.Z);section.DbEntity.Update();
            Trace("section_source_configuration_before");var selection=SectionSourceSelection357.Configure(section,sheet,sourceIds,before.GetProperty("section_settings"),state,job.CheckCancellation);Trace("section_source_configuration_returned");
            job.CheckCancellation();var view=new DrawingView();var viewId=view.ID;
            Need(!viewId.IsNull&&viewId!=sectionId&&(view.DbEntity.DocumentID.IsNull||view.DbEntity.DocumentID==sheet.ID),"Fresh section view has invalid/foreign native identity.","OBJECT_IDENTITY_MISMATCH");
            Need(McObjectManager.Add2Document(view.DbEntity,sheet)&&view.ID==viewId&&view.DbEntity.DocumentID==sheet.ID,
                "Fresh section view registration rejected or changed native identity/document.","OBJECT_IDENTITY_MISMATCH");
            // Set the section relationship/basis on the resident native object.
            // One assignment each: no second setter, recreation or retry.
            view.SectionID=section.ID;view.SetIsSection(true);view.DirectionType=DrawingView.ViewDirectionType.kViewFront;
            var up=new Vector3d(spec.VerticalDirection.X,spec.VerticalDirection.Y,spec.VerticalDirection.Z);
            var normal=new Vector3d(spec.ViewingDirection.X,spec.ViewingDirection.Y,spec.ViewingDirection.Z);
            // Section direction and view projection are separate native properties.
            // Set both explicitly; assigning SectionID does not define the view basis.
            view.ProjPlane=new Plane3d(Point3d.Origin,up.CrossProduct(normal),up);
            view.ViewPlane=new Plane3d(new Point3d(spec.Origin.X,spec.Origin.Y,0),Vector3d.XAxis,Vector3d.YAxis);
            view.Name=spec.Name;view.DbEntity.DScale=spec.Scale;view.DbEntity.Scale=1;
            // Apply selected Model source IDs after resident registration and fresh
            // Section configuration. Its native processing inventory is separately verified.
            // One native assignment, with source reactors, never a retry or an
            // oracle waiver. Both resident and regenerated sets stay exact.
            view.Set3dEntsIds(sourceIds,true);
            Trace("update_before");McObjectManager.UpdateAll();Trace("update_returned");job.CheckCancellation();
            Need(view.ID==viewId&&view.DbEntity.DocumentID==sheet.ID&&view.SectionID==sectionId&&view.IsBasedOnSection&&view.IsRootSectionView,
                "Resident native section view did not retain the exact Section association/document identity.");
            SectionSourceSelection357.Verify(selection,section,view,state,job.CheckCancellation);
            var geometry=view.GetViewGeometry(true,0);
            if(geometry!=null)foreach(var item in geometry)if(item is IDisposable resource)geometryResources.Add(resource);
            Need(geometry!=null && geometry.Count>0,"Section projection has no geometry.");
            var changed=Capture(state.Document,job.CheckCancellation);Verify(before,changed,spec,job.CheckCancellation);
#if DRAWING_VIEW_WRITE_PROBE
            DrawingViewWriteProbe.RecordProductGeometry(args,state,view,geometry!);
            DrawingViewWriteProbe.RecordProductSnapshot(args,state,"changed",changed);
            DrawingViewWriteProbe.CheckProductFailure(args,state);
#endif
            if(ReleaseGeometry() is {} releaseError)throw releaseError;
            var addedSection=Added(before.GetProperty("sections"),changed.GetProperty("sections")).Single();
            var addedView=Added(before.GetProperty("common").GetProperty("views"),changed.GetProperty("common").GetProperty("views")).Single();
            var changes=new[]{new StructureChange(0,"create_section_view",new(args.BridgeId,args.DocumentId,addedSection.GetProperty("handle").GetString()!),"section","Section",true),new StructureChange(0,"create_section_view",new(args.BridgeId,args.DocumentId,addedView.GetProperty("handle").GetString()!),"drawing_view",spec.Name,true)};
            var actualNormal=addedView.GetProperty("projection_plane").GetProperty("normal");
            var warnings=Same(actualNormal,Wire.Element(Coordinates(spec.ViewingDirection)))?Array.Empty<string>():new[]{
                "SECTION_VIEW_BASIS_REQUIRES_VISUAL_CHECK: Section viewing_direction is verified, but native DrawingView projection normal differs (requested="+
                Wire.Element(Coordinates(spec.ViewingDirection)).GetRawText()+", observed="+actualNormal.GetRawText()+"). Inspect get_drawing_views and the rendered section before relying on its orientation."};
            warnings=warnings.Append("Native Section settings select the explicit requested Model sources in each fresh generation mode; complete selected view participants are verified separately from the broader configuration-derived Section processing inventory and sliced intersections. Existing Section settings and model sources remain checked. Inspect the intended section through MCP; projected indices are not persistent annotation edges.").ToArray();
            var result=Result.Ok(job.OriginalRequestId,new ModifiedStructure(args.OperationId,"committed",false,changes,"native_command_grouping_not_yet_accepted"),context,warnings);
            job.CheckCancellation();job.BeginCommit();commitAttempted=true;Close();SectionSourceSelection357.Verify(selection,section,view,state,()=>{});Need(Same(changed,Capture(state.Document,()=>{})),"Observed state changed after ending tracking.");job.RecordCommit(result);Trace("commit_recorded");return result;
        }
        catch(Exception error)
        {
            if(job.CommittedResult is {} committed)return committed;
            bool rolledBack=false;Exception? cleanup=null;
            try
            {
                if(started && !commitAttempted && tracker!=null)
                {
                    tracker.Undo(true);Close();McObjectManager.UpdateAll();var restored=Capture(state.Document,()=>{});rolledBack=Same(before,restored);
#if DRAWING_VIEW_WRITE_PROBE
                    DrawingViewWriteProbe.RecordProductSnapshot(args,state,"restored",restored);
#endif
                }
                else Close();
            }
            catch(Exception e){cleanup=e;}
            bool unknown=commitAttempted || cleanup!=null || started&&!rolledBack;
            var code=error is DomainException domain?domain.Error.code:error is OperationCanceledException?"CANCELLED":"MODEL_UPDATE_FAILED";
            return job.Error(unknown?"WRITE_OUTCOME_UNKNOWN":code,unknown?"Section outcome unverified; inspect the affected section and model history. Replay only the identical request.":"Section creation failed; inspect the structured outcome.",unknown?"unknown":rolledBack?"rolled_back":"not_started",context,new{error_type=error.GetType().Name,cause=code,message=error.Message,native_details=(error as DomainException)?.Error.details,cleanup_error=cleanup?.GetType().Name});
        }
        finally{if(ReleaseGeometry() is {} releaseError)log.Write(job.Operation,job.OriginalRequestId,"section_geometry_release_failed",transaction:releaseError.GetType().Name,operationId:job.OperationId);}
    }
}
