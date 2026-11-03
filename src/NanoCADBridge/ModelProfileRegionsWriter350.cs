using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text.Json;
using Multicad;
using Multicad.Mc3D;
using Multicad.DatabaseServices;
using NanoCAD.Contracts;
using NanoCAD.Ipc;
using G=Multicad.Geometry;
using Db=Teigha.DatabaseServices;

namespace NanoCADBridge;

// Native region selection in the existing sketch/profile. Addition creates
// real exact-source projections; no existing source geometry or owner is set.
internal static class ModelProfileRegionsWriter350
{
    sealed record Binding(ModelProfileRegionsSpec350 Edit,ModelProfileRegionsState350.Snapshot Before,
        DrawingProfileWriter.Source[] Additions,ModelCircleRegion350[] Desired);
    static void Need([System.Diagnostics.CodeAnalysis.DoesNotReturnIf(false)] bool ok,string code,string message)=>ModelProfileRegionsState350.Need(ok,code,message);
    internal static bool Handles(ModifyArguments args)=>args.Operations.Any(edit=>edit is ModelProfileRegionsSpec350);
    [MethodImpl(MethodImplOptions.NoInlining)]
    static Binding Resolve(ModifyArguments args,DocumentState state,Db.Transaction tx,Action cancel)
    {
        Need(args.Operations.Length==1&&args.Operations[0] is ModelProfileRegionsSpec350,"INVALID_OPERATION_ORDER","Supply one retained native profile region edit.");
        Need(args.Units=="Millimeters"&&args.TargetSpace=="model"&&(int)state.Document.Database.Insunits==4,"OUT_OF_SCOPE","Use Millimeters in Model Space.");
        var loaded=AppDomain.CurrentDomain.GetAssemblies().Select(value=>value.GetName().Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        Need(loaded.Contains("mapimgd")&&loaded.Contains("MapiBaseTypes"),"CAPABILITY_UNAVAILABLE","MultiCAD must already be loaded.");
        var edit=(ModelProfileRegionsSpec350)args.Operations[0];var drawing=ModelFeatureReorderState345.Scope(state,tx);
        var representation=CadStructureWriter.Find(state.Document.Database,edit.Handles.Single());var raw=tx.GetObject(representation,Db.OpenMode.ForRead);
        Need(raw.GetType()==typeof(Db.Solid3d)&&raw.OwnerId==ModelFeatureReorderState345.Model(state.Document.Database,tx),"OUT_OF_SCOPE","Select an existing top-level native part representation.");
        var id=McObjectId.FromHandle(representation.Handle.Value,drawing.ID);var body=id.GetObjectOfType<Mc3dSolid>();
        Need(body!=null&&body.ID==id&&ModelHistoryEditGuard.CurrentRepresentation(body,state,tx,cancel)==representation,"OBJECT_IDENTITY_MISMATCH","Select the unique current native body representation.");
        Need(body.LastPartItemID.ToString().Equals(edit.ExpectedLastItemId,StringComparison.OrdinalIgnoreCase),"PRECONDITION_FAILED","Refresh this body's native last_item_id.");
        var contents=ModelInventoryBudget.History(body,cancel);var featureId=contents.SingleOrDefault(value=>value.ToString().Equals(edit.FeatureId,StringComparison.OrdinalIgnoreCase));
        var feature=featureId.IsNull?null:featureId.GetObjectOfType<ExtrudeFeature>();
        Need(feature!=null&&feature.ID==featureId&&feature.Status.ToString()=="Ok"&&!feature.IsSuppressed&&!feature.IsBeyondEndOfPart,
            "OUT_OF_SCOPE","Select a healthy current native extrusion.");
        var before=ModelProfileRegionsState350.Capture(body,feature,edit,args,state,tx,cancel,includeStandalone:true);
        Need(before.Selected.Sketch.ID.ToString().Equals(edit.ExpectedSketchId,StringComparison.OrdinalIgnoreCase)&&before.Selected.Token==edit.ExpectedRegionToken,
            "PRECONDITION_FAILED","The exact region/profile/parent/source/constraint state changed; refresh profile_region_ids.");
        var additions=new List<DrawingProfileWriter.Source>();var loops=ModelProfileRegionsReader350.SourceKeys(before.Selected.Loops).ToList();
        foreach(string handle in edit.AddSourceHandles)
        {
            cancel();var source=DrawingProfileWriter.SourceOf(CadStructureWriter.Find(state.Document.Database,handle),state,tx,false,before.Selected.Plane);
            Need(source.Curve.Kind=="circle"&&loops.All(loop=>!loop.source_object_id.Equals(source.Native.ToString(),StringComparison.OrdinalIgnoreCase)),
                "OUT_OF_SCOPE","Add distinct exact ordinary Circle sources not already in this sketch.");
            loops.Add(new(source.Native.ToString(),source.Curve.Center!,source.Curve.Radius,source.Native.ToString()));additions.Add(source);
        }
        var points=edit.RegionPoints.Select(point=>{cancel();return DrawingWorkplane.Local(before.Selected.Plane,point);}).ToArray();
        var desired=ModelCircleRegions350.Select(loops.ToArray(),points,cancel);
        Need(additions.Count>0||!ModelProfileRegionsState350.Equal(Wire.Element(desired),Wire.Element(ModelProfileRegionsReader350.SourceKeys(before.Selected.Regions,before.Selected.Loops)),cancel),
            "NO_CHANGE","The same regions are already selected; no native toggle is needed.");return new(edit,before,additions.ToArray(),desired);
    }
    internal static void Preflight(ModifyArguments args,DocumentState state,Db.Transaction tx,Action? cancel=null)=>Resolve(args,state,tx,cancel??(()=>{}));
    internal static ModelProfileRegionsRead350[] Read(string bodyHandle,string[] profileIds,Arguments args,DocumentState state,Db.Transaction tx,Action cancel)
    {
        var drawing=ModelFeatureReorderState345.Scope(state,tx);var raw=CadStructureWriter.Find(state.Document.Database,bodyHandle);
        Need(tx.GetObject(raw,Db.OpenMode.ForRead).GetType()==typeof(Db.Solid3d)&&tx.GetObject(raw,Db.OpenMode.ForRead).OwnerId==ModelFeatureReorderState345.Model(state.Document.Database,tx),
            "OUT_OF_SCOPE","Region metadata requires an exact current top-level native part.");
        var id=McObjectId.FromHandle(raw.Handle.Value,drawing.ID);var body=id.GetObjectOfType<Mc3dSolid>();
        Need(body!=null&&body.ID==id&&ModelHistoryEditGuard.CurrentRepresentation(body,state,tx,cancel)==raw,"OBJECT_IDENTITY_MISMATCH","The native part representation changed.");
        var owner=ModelProfileDiscovery348.OwnerDocument(body,state);var result=new List<ModelProfileRegionsRead350>();
        foreach(string profileId in profileIds)
        {
            cancel();var profile=ModelProfileDiscovery348.Resolve(profileId,owner,cancel);var feature=profile.Dependent3dFeatureID.GetObjectOfType<ExtrudeFeature>();
            Need(feature!=null&&feature.ProfileID==profile.ID&&feature.HostSolidID==body.ID,"OUT_OF_SCOPE","Region metadata requires this actual consumed extrusion profile.");
            var value=ModelProfileRegionsReader350.Capture(body,profile,state,tx,cancel);result.Add(ModelProfileRegionsReader350.Describe(value,feature));
        }
        // The existing CadReader measures the complete public result jointly.
        return result.ToArray();
    }
    static ModelProfileRegionsState350.Snapshot Current(Binding binding,ModifyArguments args,DocumentState state,Action cancel)
    {
        using var tx=state.Document.Database.TransactionManager.StartTransaction();ModelFeatureReorderState345.Scope(state,tx,binding.Before.DrawingDocument);
        var feature=binding.Before.Feature.ID.GetObjectOfType<ExtrudeFeature>();Need(feature!=null,"MODEL_STATE_UNAVAILABLE","The retained native extrusion disappeared.");
        return ModelProfileRegionsState350.Capture(binding.Before.Body,feature,binding.Edit,args,state,tx,cancel,includeStandalone:true);
    }
    static Result Outcome(Binding binding,ModifyArguments args,WriteJob job,ReadContext context,ModelProfileRegionsState350.Snapshot after,McObjectId[] projections,bool projected)
    {
        var source=new EntityRef(args.BridgeId,args.DocumentId,binding.Edit.Handles.Single());var body=new EntityRef(args.BridgeId,args.DocumentId,
            projected?"FFFFFFFFFFFFFFFF":Validation.NormalizeHandle(after.Representation.Handle.ToString()));bool replaced=projected||source.handle!=body.handle;
        var projectionIds=projected?binding.Additions.Select(_=>"{FFFFFFFF-FFFF-FFFF-FFFF-FFFFFFFFFFFF}").ToArray():projections.Select(id=>id.ToString()).ToArray();
        var regions=projected?binding.Desired:ModelProfileRegionsReader350.SourceKeys(after.Selected.Regions,after.Selected.Loops);
        var mapping=new ModelProfileRegionsChange350(0,source,body,after.Body.ID.ToString(),after.Feature.ID.ToString(),after.Selected.Profile.ID.ToString(),after.Selected.Sketch.ID.ToString(),
            after.Selected.Token,after.OwnerDocument,binding.Before.Views.LastHistoryItem.ToString(),replaced,regions.Length,regions.Sum(region=>region.hole_element_ids.Length),
            binding.Additions.Select(addition=>new EntityRef(args.BridgeId,args.DocumentId,Validation.NormalizeHandle(addition.Id.Handle.ToString()))).ToArray(),projectionIds,
            binding.Before.Downstream.Length,Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(after.Downstream,Wire.Json))),after.Selected.Constraints);
        var data=new ModelProfileRegionsOutput350(args.OperationId,"committed",false,[body],replaced?[source]:[],replaced?[body]:[],[],"not_guaranteed",[mapping]);
        var warnings=binding.Before.Warnings.Concat(after.Warnings).Append(WriteRecovery.Warning)
            .Append("NATIVE_PROFILE_REGIONS_CHANGED: Original Circle sources and existing sketch elements remain retained. Refresh exact profile_region_ids, body/history/variables and inspect the rebuilt engineering result and linked drawings through MCP.")
            .Concat(projected?[ModelFeatureReorderState345.ValidityWarning]:Array.Empty<string>()).Distinct(StringComparer.Ordinal).ToArray();
        return NanoCAD.Contracts.Result.Ok(job.OriginalRequestId,data,context,warnings);
    }
    static void Budget(Result result,Action cancel)
    {cancel();Need(JsonSerializer.SerializeToUtf8Bytes(result,Wire.Json).Length<=Wire.PayloadBudget,"RESPONSE_LIMIT_EXCEEDED","The complete region-edit receipt exceeds actual transport bytes; no CAD count ceiling is imposed.");}
    [MethodImpl(MethodImplOptions.NoInlining)]
    internal static Result Execute(ModifyArguments args,DocumentState state,WriteJob job,ReadContext context,JsonLog log)
    {
        Binding? binding=null;McUndoPoint? tracker=null;bool started=false;string stage="preparation";var added=new List<McObjectId>();var observed=new List<McObjectId>();
        void Trace(string next){stage=next;log.Write(job.Operation,job.OriginalRequestId,"model_profile_regions350_checkpoint",transaction:next,operationId:job.OperationId);}
        void Close(){var value=tracker;tracker=null;if(value==null)return;try{value.Stop();}finally{value.Dispose();}}
        try
        {
            using(var tx=state.Document.Database.TransactionManager.StartTransaction())binding=Resolve(args,state,tx,job.CheckCancellation);
            var fresh=Current(binding,args,state,job.CheckCancellation);
            Need(binding.Before.Representation==fresh.Representation&&binding.Before.Contents.ToHashSet().SetEquals(fresh.Contents)&&
                binding.Before.Construction.ToHashSet().SetEquals(fresh.Construction)&&binding.Before.Inventory.SetEquals(fresh.Inventory)&&
                binding.Before.Selected.Token==fresh.Selected.Token&&ModelProfileRegionsState350.Equal(binding.Before.Parameters,fresh.Parameters,job.CheckCancellation)&&
                ModelProfileRegionsState350.Equal(binding.Before.Sources,fresh.Sources,job.CheckCancellation)&&ModelProfileRegionsState350.Equal(binding.Before.OtherProfiles,fresh.OtherProfiles,job.CheckCancellation)&&
                binding.Before.DrawingDocument==fresh.DrawingDocument&&binding.Before.OwnerDocument==fresh.OwnerDocument&&
                binding.Before.FeatureOrder.SequenceEqual(fresh.FeatureOrder,StringComparer.OrdinalIgnoreCase)&&
                binding.Before.DirectDownstream.SequenceEqual(fresh.DirectDownstream,StringComparer.OrdinalIgnoreCase)&&
                binding.Before.Downstream.SequenceEqual(fresh.Downstream,StringComparer.OrdinalIgnoreCase)&&
                binding.Before.Owners.SequenceEqual(fresh.Owners,StringComparer.OrdinalIgnoreCase)&&
                binding.Before.BodyMetadata.GetRawText()==fresh.BodyMetadata.GetRawText()&&binding.Before.Display.GetRawText()==fresh.Display.GetRawText(),
                "PRECONDITION_FAILED","Native profile, retained source, owner, driver or downstream state changed before the write.");
            Budget(ModelViewFollowers.ReserveWarnings(Outcome(binding,args,job,context,fresh,[],true),binding.Before.Views),job.CheckCancellation);job.CheckCancellation();ModelViewFollowers.CheckStandalone(binding.Before.Body,binding.Before.Views,job.CheckCancellation);tracker=new McUndoPoint(false);tracker.Start();started=true;
            var sketch=binding.Before.Selected.Sketch;var profile=binding.Before.Selected.Profile;var owner=binding.Before.Body.DbEntity.Document;
            sketch.TryModify((uint)McDbObject.ObjChangesType.Geom);
            foreach(var addition in binding.Additions)
            {
                job.CheckCancellation();Trace("add_native_source_projection");var projection=sketch.ProjectObject(addition.Native,true);
                if(projection!=null&&!projection.ID.IsNull)observed.Add(projection.ID);
                Need(projection!=null&&projection.SrcObjectID==addition.Native&&projection.HostSketchID==sketch.ID,"MODEL_UPDATE_FAILED","Native sketch did not return the exact new source projection.");
                var projectedId=projection.ID;
                var projectionDocument=projection.DbEntity.Document;
                Need(!projectedId.IsNull&&!binding.Before.Construction.Contains(projectedId)&&!binding.Before.Contents.Contains(projectedId)&&!added.Contains(projectedId)&&
                    (projectionDocument==null||projectionDocument.ID==owner.ID),"PROFILE_OWNERSHIP_MISMATCH",
                    "Returned projection is not a fresh native object in this verified sketch owner scope; no re-registration, owner reset or disposal is attempted.");
                // Explicit verified owner registration, never CURRENT-document
                // fallback. HostSolid is readonly and is verified after rebuild.
                McObjectManager.Add2Document(projection.DbEntity,owner);projection.DbEntity.Update();added.Add(projection.ID);
            }
            McObjectManager.UpdateAll();profile.TryModify((uint)McDbObject.ObjChangesType.Geom);Trace("clear_selected_native_regions");
            Need(profile.Clear()&&profile.IsEmpty,"MODEL_UPDATE_FAILED","Native profile did not clear its selected region set.");
            var normal=DrawingWorkplane.Normal(binding.Before.Selected.Plane);
            foreach(var point in binding.Edit.RegionPoints)
            {
                job.CheckCancellation();Trace("select_native_region_by_line");using var ray=new G.Line3d(new G.Point3d(point.X,point.Y,point.Z),new G.Vector3d(normal.X,normal.Y,normal.Z));
                // Installed PE signature accepts Line3d, despite old CHM's ref.
                Need(profile.AddOrRemoveRegionByLine(ray),"MODEL_UPDATE_FAILED","Native profile could not select the explicit bounded region.");
            }
            profile.DbEntity.Update();McObjectManager.UpdateAll();Need(binding.Before.Body.RebuildPart(),"MODEL_UPDATE_FAILED","Native existing part rebuild rejected the selected regions.");McObjectManager.UpdateAll();Trace("native_rebuild_returned");
            var after=Current(binding,args,state,job.CheckCancellation);ModelProfileRegionsState350.Verify(binding.Before,after,binding.Desired,added.ToArray(),job.CheckCancellation);
            Trace("native_regions_verified");job.BeginCommit();Close();after=Current(binding,args,state,job.CheckCancellation);
            ModelProfileRegionsState350.Verify(binding.Before,after,binding.Desired,added.ToArray(),job.CheckCancellation);
            var result=Outcome(binding,args,job,context,after,added.ToArray(),false);Budget(result,job.CheckCancellation);result=ModelViewFollowers.WithWarnings(result,binding.Before.Views);job.RecordCommit(result);Trace("commit_recorded");return result;
        }
        catch(Exception error)
        {
            if(job.CommittedResult is {} committed)return committed;Exception? cleanup=null;try{Close();}catch(Exception e){cleanup=e;}
            bool unknown=started||cleanup!=null;string cause=error is DomainException domain?domain.Error.code:error is OperationCanceledException?"CANCELLED":"MODEL_PROFILE_REGIONS_FAILED";
            string message=error.Message;if(message.Length>512)message=message[..512];
            return job.Error(unknown?"WRITE_OUTCOME_UNKNOWN":cause,unknown?
                "Native region edit started but remains unverified. Partial projections or selected regions may remain; inspect current profile/body state before another write. Replay only the identical request in this bridge lifetime.":message,
                unknown?"unknown":"not_started",context,new{cause,stage,message,body_mc_id=binding?.Before.Body.ID.ToString(),feature_id=binding?.Edit.FeatureId,
                    profile_id=binding?.Edit.ProfileId,sketch_id=binding?.Edit.ExpectedSketchId,added_projection_ids=added.Select(id=>id.ToString()).ToArray(),
                    observed_projection_ids=observed.Select(id=>id.ToString()).ToArray(),cleanup_error=cleanup?.GetType().Name})
                with{warnings=[WriteRecovery.Warning]};
        }
    }
}
