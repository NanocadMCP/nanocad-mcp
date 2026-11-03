using System.Text.Json;
using NanoCAD.Contracts;
using NanoCAD.Ipc;
using Teigha.DatabaseServices;

namespace NanoCADBridge;

// The selected entities are never opened for geometry/content writes. Ordering
// is one resident native Sortents table operation for their exact owner space.
internal static class CadDrawOrder407
{
    internal const string Warning="NATIVE_DRAW_ORDER: Changes only the selected entities' native drawing order in their owning space. Entity geometry, content, source associations and shared definitions are retained. Undo/rollback is not guaranteed; unknown outcome must be inspected rather than repeated.";
    sealed record Plan(SetDrawOrder407 Edit,ObjectId[] Selected,ObjectId Anchor,ObjectId Owner,ObjectId Table);
    static void Need(bool condition,string message,string code="OUT_OF_SCOPE")
    {if(!condition)throw new DomainException(code,message);}
    static bool Live(ObjectId id)=>!id.IsNull&&id.IsValid&&!id.IsErased&&!id.IsEffectivelyErased;
    internal static bool Handles(ModifyArguments args)=>args.Operations.Any(edit=>edit is SetDrawOrder407);
    static Plan[] Resolve(ModifyArguments args,Database db,Transaction tr,Action cancel)
    {
        DrawOrderValidation407.Batch(args.Operations);WriteSpace.Check(args);var owner=CadWriteSpace.Resolve(db,tr,args.TargetSpace);
        var record=(BlockTableRecord)tr.GetObject(owner,OpenMode.ForRead);
        Need(record.IsLayout&&!record.IsFromExternalReference,"Drawing order requires a local Model or active ordinary Paper owner.");
        ObjectId table=record.DrawOrderTableId;
        Need(Live(table)&&tr.GetObject(table,OpenMode.ForRead) is DrawOrderTable,"Native drawing-order table is unavailable.","NATIVE_DRAW_ORDER_UNAVAILABLE");
        ObjectId ResolveEntity(string handle,bool target)
        {
            cancel();var id=CadStructureWriter.Find(db,handle);var value=tr.GetObject(id,OpenMode.ForRead);
            Need(value is Entity&&value.Database==db&&value.OwnerId==owner,"Every selected/relative entity must remain top-level in the requested actual space.");
            if(target)
            {
                var layer=(LayerTableRecord)tr.GetObject(((Entity)value).LayerId,OpenMode.ForRead);
                Need(!layer.IsLocked&&!layer.IsDependent,"Selected drawing-order targets must belong to local unlocked layers.","LAYER_UNAVAILABLE");
            }
            return id;
        }
        return args.Operations.Cast<SetDrawOrder407>().Select(edit=>new Plan(edit,
            edit.Handles.Select(handle=>ResolveEntity(handle,true)).ToArray(),edit.RelativeTo==null?ObjectId.Null:ResolveEntity(edit.RelativeTo,false),owner,table)).ToArray();
    }
    internal static void Preflight(ModifyArguments args,DocumentState state,Transaction tr,Action? cancel=null)=>Resolve(args,state.Document.Database,tr,cancel??(()=>{}));
    static ObjectId[] Order(DrawOrderTable table,ObjectId owner,Transaction tr,Action cancel)
    {
        using var full=table.GetFullDrawOrder(0);var result=new List<ObjectId>();var seen=new HashSet<ObjectId>();
        foreach(ObjectId id in full)
        {
            cancel();if(!Live(id))continue;
            Need(seen.Add(id)&&tr.GetObject(id,OpenMode.ForRead) is Entity entity&&entity.OwnerId==owner,
                "Native drawing order contains duplicate or foreign identities.","NATIVE_DRAW_ORDER_MISMATCH");result.Add(id);
        }
        return result.ToArray();
    }
    static void Verify(Plan plan,ObjectId[] before,ObjectId[] after,DrawOrderTable table,Action cancel)
    {
        var selected=plan.Selected.ToHashSet();
        Need(before.Length==after.Length&&before.ToHashSet().SetEquals(after)&&plan.Selected.All(id=>after.Contains(id)),
            "Native drawing-order change lost, added or substituted entity identities.","NATIVE_DRAW_ORDER_MISMATCH");
        Need(before.Where(id=>!selected.Contains(id)).SequenceEqual(after.Where(id=>!selected.Contains(id))),
            "Native drawing-order change reordered unselected entities.","NATIVE_DRAW_ORDER_MISMATCH");
        // Relative native comparisons avoid treating numeric sort handles or
        // geometry extents as the semantic order. No full order is transported.
        if(plan.Edit.Position is "above" or "below")
            foreach(var id in plan.Selected)
            {
                cancel();bool beforeAnchor=table.FirstEntityIsDrawnBeforeSecond(id,plan.Anchor);
                Need(beforeAnchor==(plan.Edit.Position=="below"),"Native order does not place every selected entity on the requested side of relative_to.","NATIVE_DRAW_ORDER_MISMATCH");
            }
        else
        {
            var unselected=after.Where(id=>!selected.Contains(id)).ToArray();if(unselected.Length==0)return;
            bool bottom=plan.Edit.Position=="bottom";
            // GetFullDrawOrder returns the effective native sequence. Check
            // only its two adjacent selection boundaries, then compare the
            // actual native before/after predicate for those boundaries.
            int[] positions=after.Select((id,index)=>(id,index)).Where(value=>selected.Contains(value.id)).Select(value=>value.index).ToArray();
            if(bottom)
            {
                Need(positions.Max()<Array.FindIndex(after,id=>!selected.Contains(id)),"Native bottom order interleaves selected and unselected entities.","NATIVE_DRAW_ORDER_MISMATCH");
                Need(table.FirstEntityIsDrawnBeforeSecond(after[positions.Max()],unselected[0]),"Native selected bottom boundary is not drawn first.","NATIVE_DRAW_ORDER_MISMATCH");
            }
            else
            {
                Need(positions.Min()>Array.FindLastIndex(after,id=>!selected.Contains(id)),"Native top order interleaves selected and unselected entities.","NATIVE_DRAW_ORDER_MISMATCH");
                Need(table.FirstEntityIsDrawnBeforeSecond(unselected[^1],after[positions.Min()]),"Native selected top boundary is not drawn last.","NATIVE_DRAW_ORDER_MISMATCH");
            }
        }
    }
    static Result Outcome(ModifyArguments args,WriteJob job,ReadContext context)
    {
        var refs=args.Operations.SelectMany(edit=>edit.Handles).Distinct(StringComparer.Ordinal).Select(handle=>new EntityRef(args.BridgeId,args.DocumentId,handle)).ToArray();
        var result=Result.Ok(job.OriginalRequestId,new ModifiedEntities(args.OperationId,"committed",false,refs,[],[],[],"not_guaranteed"),context,[WriteRecovery.Warning,Warning]);
        if(JsonSerializer.SerializeToUtf8Bytes(result,Wire.Json).Length>Wire.PayloadBudget-4096)
            throw new DomainException("RESPONSE_LIMIT_EXCEEDED","The complete selected drawing-order receipt exceeds actual response bytes; reduce the requested group before writing.");
        return result;
    }
    internal static Result Execute(ModifyArguments args,DocumentState state,WriteJob job,ReadContext context,JsonLog log)
    {
        Transaction? tr=null;Exception? failure=null;bool started=false;string stage="preparation";
        try
        {
            var db=state.Document.Database;tr=db.TransactionManager.StartTransaction();var plans=Resolve(args,db,tr,job.CheckCancellation);
            var outcome=Outcome(args,job,context);
            foreach(var plan in plans)
            {
                job.CheckCancellation();var read=(DrawOrderTable)tr.GetObject(plan.Table,OpenMode.ForRead);var before=Order(read,plan.Owner,tr,job.CheckCancellation);
                Need(plan.Selected.All(id=>before.Contains(id))&&(plan.Anchor.IsNull||before.Contains(plan.Anchor)),"Selected/relative entity is absent from the actual native order.","NATIVE_DRAW_ORDER_MISMATCH");
                var table=(DrawOrderTable)tr.GetObject(plan.Table,OpenMode.ForWrite);using var ids=new ObjectIdCollection(plan.Selected);
                stage="native_draw_order";job.CheckCancellation();started=true;
                switch(plan.Edit.Position)
                {
                    case "top":table.MoveToTop(ids);break;case "bottom":table.MoveToBottom(ids);break;
                    case "above":table.MoveAbove(ids,plan.Anchor);break;case "below":table.MoveBelow(ids,plan.Anchor);break;
                }
                job.CheckCancellation();Verify(plan,before,Order(table,plan.Owner,tr,job.CheckCancellation),table,job.CheckCancellation);
            }
            stage="native_commit";job.BeginCommit();tr.Commit();job.RecordCommit(outcome);
        }
        catch(Exception error){failure=error;}
        Exception? cleanup=null;try{tr?.Dispose();}catch(Exception error){cleanup=error;}
        if(job.CommittedResult is {} committed)
        {
            if(cleanup!=null)committed=committed with{warnings=committed.warnings.Append("POST_COMMIT_CLEANUP_FAILED: Drawing-order commit is recorded; refresh current state before further writes.").ToArray()};
            job.RecordCommit(committed);return committed;
        }
        string code=failure is DomainException domain?domain.Error.code:failure is OperationCanceledException?"CANCELLED":"NATIVE_DRAW_ORDER_FAILED";bool unknown=started||cleanup!=null;
        return job.Error(unknown?"WRITE_OUTCOME_UNKNOWN":code,unknown?"Native drawing-order mutation started or cleanup failed; inspect the current visible result and preserve the exact request. Do not submit another operation_id.":failure?.Message??"Drawing-order change did not complete.",
            unknown?"unknown":"not_started",context,new{cause=code,stage,native_error_status=failure is Teigha.Runtime.Exception native?native.ErrorStatus.ToString():null,cleanup_error=cleanup?.GetType().Name})
            with{warnings=[WriteRecovery.Warning,Warning]};
    }
}
