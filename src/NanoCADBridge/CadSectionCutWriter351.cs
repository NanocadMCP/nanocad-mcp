using System.Security.Cryptography;
using System.Text.Json;
using Multicad.DatabaseServices;
using NanoCAD.Contracts;
using NanoCAD.Ipc;
using Db=Teigha.DatabaseServices;
using G=Multicad.Geometry;
using static NanoCADBridge.CadSectionCutState351;

namespace NanoCADBridge;

internal static class CadSectionCutWriter351
{
    const string Warning="NATIVE_SECTION_CUT_CHANGED: The same native Section and complete retained view group were regenerated with the explicit cut line. Each view retains its selected Model sources. Section-global processing IDs are configuration-derived, not a selected-source ownership promise. Inspect the useful section and relevant dimensions through MCP. Recovery and Undo are not guaranteed.";
    const string ProjectionWarning="SECTION_PROJECTION_EMPTY_OR_UNOBSERVED: A retained projection is empty or its native geometry reader is unavailable. The requested cut and healthy identities/settings/source/tree state are verified; an empty cut can be valid outside material. Inspect current get_drawing_views geometry and the affected drawing before relying on displayed cut content.";
    internal static bool Handles(StructureWriteArguments args)=>args.Operations.Any(edit=>edit is SetSectionViewCut351);
    internal static void Preflight(StructureWriteArguments args,DocumentState state,Db.Transaction tx,Action cancel)=>Prepare(args,state,tx,cancel);
    static Result Outcome(Plan plan,StructureWriteArguments args,WriteJob job,ReadContext context,Db.Transaction tx,Action cancel,bool projectionUnobserved=false)
    {
        var section=new EntityRef(args.BridgeId,args.DocumentId,Validation.NormalizeHandle(plan.Section.Entity.Handle.ToString()));
        var views=plan.Views.Select(view=>CadDrawingViewLifecycleState345.Change(view,plan.Drawing,tx,args,cancel)).ToArray();
        var changes=new List<StructureChange>{new(0,args.Operations[0].Operation,section,"section",plan.Section.Native.Name,false)};
        changes.AddRange(plan.Views.Select(view=>new StructureChange(0,args.Operations[0].Operation,new(args.BridgeId,args.DocumentId,Validation.NormalizeHandle(view.Entity.Handle.ToString())),"drawing_view",view.Native.Name,false)));
        var processing=plan.Section.Metadata.GetProperty("top_source_ids").EnumerateArray().Select(value=>value.GetString()!).ToArray();
        var mapping=new SectionCutChange351(0,section,plan.Section.Native.ID.ToString(),plan.Section.Token,views.Select(view=>view.view_ref).ToArray(),processing.Length,
            Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(processing,Wire.Json))),
            "actual_global_section_processing_inventory_separate_from_retained_view_selected_sources");
        return Result.Ok(job.OriginalRequestId,new SectionCutOutput351(args.OperationId,"committed",false,changes.ToArray(),"not_guaranteed",views,[mapping]),context,
            projectionUnobserved?[WriteRecovery.Warning,Warning,ProjectionWarning]:[WriteRecovery.Warning,Warning]);
    }
    static void Bytes(Result result,Action cancel)
    {cancel();Require(JsonSerializer.SerializeToUtf8Bytes(result,Wire.Json).Length<=Wire.PayloadBudget,"RESPONSE_LIMIT_EXCEEDED","Complete Section/view receipt exceeds actual transport bytes; no CAD object count limit is imposed.");}
    static bool Geometry(Plan plan,Action cancel)
    {
        bool unobserved=false;
        foreach(var view in plan.Views)
        {
            cancel();List<G.EntityGeometry>? geometry;
            try{geometry=view.Native.GetViewGeometry(true,0);}
            catch(Teigha.Runtime.Exception){cancel();unobserved=true;continue;}
            catch(NotSupportedException){cancel();unobserved=true;continue;}
            try{if(geometry is not {Count:>0})unobserved=true;}
            finally{if(geometry!=null)foreach(var item in geometry.Distinct(ReferenceEqualityComparer.Instance))if(item is IDisposable disposable)disposable.Dispose();}
        }
        return unobserved;
    }
    internal static Result Execute(StructureWriteArguments args,DocumentState state,WriteJob job,ReadContext context,JsonLog log)
    {
        Plan? before=null;McUndoPoint? tracker=null;bool started=false;string stage="preparation";
        void Close(){var current=tracker;tracker=null;if(current==null)return;try{current.Stop();}finally{current.Dispose();}}
        void Trace(string next){stage=next;log.Write(job.Operation,job.OriginalRequestId,"section_cut351_checkpoint",transaction:next,operationId:job.OperationId);}
        Plan Current(bool admission)
        {using var tx=state.Document.Database.TransactionManager.StartTransaction();return Prepare(args,state,tx,job.CheckCancellation,admission);}
        try
        {
            using(var tx=state.Document.Database.TransactionManager.StartTransaction())
            {before=Prepare(args,state,tx,job.CheckCancellation);Bytes(Outcome(before,args,job,context,tx,job.CheckCancellation,true),job.CheckCancellation);}
            var fresh=Current(true);
            Require(before.Section.Token==fresh.Section.Token&&Same(before.Drawing.Context,fresh.Drawing.Context)&&Same(before.Drawing.Layouts,fresh.Drawing.Layouts)&&
                Same(before.Drawing.Entities,fresh.Drawing.Entities)&&Same(before.Drawing.Sections,fresh.Drawing.Sections)&&
                Same(Wire.Element(before.Drawing.Views.Select(view=>view.Metadata)),Wire.Element(fresh.Drawing.Views.Select(view=>view.Metadata)))&&
                Same(Wire.Element(before.Drawing.Bodies.Select(body=>new{body.Id,body.Entity,body.Model,body.Geometry,body.Contents,body.Last,body.Nodes})),
                    Wire.Element(fresh.Drawing.Bodies.Select(body=>new{body.Id,body.Entity,body.Model,body.Geometry,body.Contents,body.Last,body.Nodes}))),
                "PRECONDITION_FAILED","Native Section/view or drawing state changed before tracking.");
            job.CheckCancellation();tracker=new McUndoPoint(false);tracker.Start();started=true;Trace("native_set_vertices");
            var section=before.Section.Native;var up=section.VerticalDirection;var normal=section.ViewingDirection;
            Require(section.SetVertices(before.Edit.SectionLine.Select(point=>new G.Point3d(point.X,point.Y,point.Z)).ToArray()),"SECTION_UPDATE_FAILED","Native resident Section rejected the explicit cut line.");
            // SDK requires vertices before the corresponding basis setters.
            // Values stay exact; the projection/view planes are never reset.
            section.ViewingDirection=normal;section.VerticalDirection=up;section.DbEntity.Update();McObjectManager.UpdateAll();
            foreach(var view in before.Views){job.CheckCancellation();view.Native.DbEntity.Update();}McObjectManager.UpdateAll();Trace("native_update_returned");
            var after=Current(false);Verify(before,after,job.CheckCancellation);bool projectionUnobserved=Geometry(after,job.CheckCancellation);Trace("cut_and_links_verified");
            job.BeginCommit();Close();after=Current(false);Verify(before,after,job.CheckCancellation);
            using(var tx=state.Document.Database.TransactionManager.StartTransaction())
            {var result=Outcome(after,args,job,context,tx,job.CheckCancellation,projectionUnobserved);Bytes(result,job.CheckCancellation);job.RecordCommit(result);Trace("commit_recorded");return result;}
        }
        catch(Exception error)
        {
            if(job.CommittedResult is {} committed)return committed;Exception? cleanup=null;try{Close();}catch(Exception failure){cleanup=failure;}
            bool unknown=started||cleanup!=null;string cause=error is DomainException domain?domain.Error.code:error is OperationCanceledException?"CANCELLED":"SECTION_CUT_FAILED";
            string message=error.Message;if(message.Length>512)message=message[..512];
            return job.Error(unknown?"WRITE_OUTCOME_UNKNOWN":cause,unknown?
                "Native retained cut editing started but remains unverified. Inspect the same Section/views and source body state before another write; preserve the exact request and do not repeat it with a new operation_id.":message,
                unknown?"unknown":"not_started",context,new{cause,stage,message,section_id=before?.Edit.SectionId,
                    view_ids=before?.Views.Select(view=>view.Id.ToString()).ToArray(),cleanup_error=cleanup?.GetType().Name}) with{warnings=[WriteRecovery.Warning]};
        }
    }
}
