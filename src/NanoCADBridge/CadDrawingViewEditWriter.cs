using System.Text.Json;
using System.Text.Json.Nodes;
using Multicad;
using Multicad.Geometry;
using Multicad.DatabaseServices;
using Multicad.Mc3D;
using NanoCAD.Contracts;
using NanoCAD.Ipc;
using Db=Teigha.DatabaseServices;
using static NanoCADBridge.CadDrawingViewEditState;

namespace NanoCADBridge;

// MultiCAD owns native view regeneration and erase. Never use a Teigha write
// transaction to replace the projection with primitives or to erase a source.
internal static class CadDrawingViewEditWriter
{
    public static bool Handles(StructureWriteArguments args)=>args.Operations.Length>0&&
        (args.Operations.All(edit=>edit is SetDrawingViewPropertiesEdit)||args.Operations.Length==1&&args.Operations[0] is DeleteDrawingViewsEdit);
    private static DrawingViewTarget[] Targets(StructureWriteArguments args)=>args.Operations[0] is DeleteDrawingViewsEdit delete?
        delete.Targets:args.Operations.Cast<SetDrawingViewPropertiesEdit>().Select(edit=>edit.Target).ToArray();
    private static View[] Select(StructureWriteArguments args,Snapshot snapshot,Db.Transaction tx,Action cancel)
    {
        Need(Handles(args),"Existing drawing-view edits require their own native request.","INVALID_OPERATION_ORDER");
        Tree(snapshot,cancel);var selected=Targets(args).Select(target=>Resolve(target,snapshot,tx,cancel)).ToArray();
        Need(selected.Select(view=>view.Id).Distinct().Count()==selected.Length,"Each native view must be selected once.","INVALID_ARGUMENT");
        var selectedIds=selected.Select(view=>view.Id).ToHashSet();
        if(args.Operations[0] is DeleteDrawingViewsEdit)
        {
            foreach(var view in selected)
            {
                cancel();Plain(view);
                Need(view.Native.ChildViews.All(selectedIds.Contains),"Explicit deletion must include every child/descendant; no implicit view cascade.","DRAWING_VIEW_DEPENDENTS");
                var dependencies=view.Native.GetAllDependent(false);
                Need(dependencies!=null,"Native deletion dependency inventory is unavailable.","DRAWING_VIEW_DEPENDENTS");
                Need(dependencies!.All(id=>selectedIds.Contains(id)),"View has unselected native history dependents; use their owning removal route.","DRAWING_VIEW_DEPENDENTS");
                Need(view.Native.DbEntity.ChildrenIds.All(selectedIds.Contains),"View owns native entities outside the explicitly selected view subtree.","DRAWING_VIEW_DEPENDENTS");
                var parent=view.Native.DbEntity.ParentId;
                Need(parent.IsNull||selectedIds.Contains(parent)||snapshot.Views.Any(item=>item.Id==parent)||
                    snapshot.Bodies.Any(item=>item.Id==parent)&&view.Native.GetTop3dEntitiesInView().Contains(parent),
                    "View is owned by a native entity outside the addressable view group.","DRAWING_VIEW_DEPENDENTS");
            }
        }
        else
        {
            var edits=args.Operations.Cast<SetDrawingViewPropertiesEdit>().ToDictionary(edit=>edit.Target.Handle,StringComparer.Ordinal);
            foreach(var view in selected)
            {
                cancel();var edit=edits[Validation.NormalizeHandle(view.Entity.Handle.ToString())];
                if(edit.Origin==null&&edit.ModelScale==null)continue;Placement(view);
                // Parent placement/scale can change aligned descendants in native
                // update. Their requested values must be explicit in this group.
                var pending=new Stack<(View View,bool Origin,bool Scale)>();pending.Push((view,edit.Origin!=null,edit.ModelScale!=null));
                while(pending.Count>0)
                {
                    var current=pending.Pop();
                    foreach(var childId in current.View.Native.ChildViews)
                    {
                        cancel();var child=snapshot.Views.Single(item=>item.Id==childId);
                        bool childOrigin=current.Origin&&child.Native.AlignOnParent;
                        if(childOrigin||current.Scale)
                        {
                            Need(selectedIds.Contains(childId),"Include affected aligned descendants in this same property group.","DRAWING_VIEW_ALIGNMENT");
                            var childEdit=edits[Validation.NormalizeHandle(child.Entity.Handle.ToString())];
                            Need((!childOrigin||childEdit.Origin!=null)&&(!current.Scale||childEdit.ModelScale!=null),
                                "Supply the corresponding origin/model_scale for each affected aligned descendant.","DRAWING_VIEW_ALIGNMENT");
                        }
                        if(childOrigin||current.Scale)pending.Push((child,childOrigin,current.Scale));
                    }
                }
            }
        }
        return selected;
    }
    public static void Preflight(StructureWriteArguments args,DocumentState state,Db.Transaction tx,Action cancel)
    {
        var snapshot=Capture(state,tx,cancel);Select(args,snapshot,tx,cancel);
        var changes=Targets(args).Select(target=>new StructureChange(0,args.Operations[0].Operation,new(args.BridgeId,args.DocumentId,target.Handle),"drawing_view",target.ExpectedName,false)).ToArray();
        Need(JsonSerializer.SerializeToUtf8Bytes(new ModifiedStructure(args.OperationId,"committed",false,changes,"not_guaranteed"),Wire.Json).Length<=Wire.PayloadBudget-8192,
            "Selected view result exceeds the actual transport-byte budget.","RESPONSE_TOO_LARGE");
    }
    private static int Depth(View view,Snapshot snapshot)
    {
        int depth=0;var parent=view.Native.ParentViewID;
        while(!parent.IsNull){depth++;parent=snapshot.Views.Single(item=>item.Id==parent).Native.ParentViewID;}
        return depth;
    }
    private static void Geometry(View view,Action cancel)
    {
        cancel();var geometry=view.Native.GetViewGeometry(true,0);
        try{Need(geometry!=null&&geometry.Count>0,"Requested view update produced unavailable/empty native projection geometry.","MODEL_UPDATE_FAILED");}
        finally{if(geometry!=null)foreach(var item in geometry.Distinct(ReferenceEqualityComparer.Instance))if(item is IDisposable disposable)disposable.Dispose();}
        cancel();
    }
    private static void Verify(Snapshot before,Snapshot after,Dictionary<McObjectId,JsonElement> expectedViews,HashSet<McObjectId> deleted,Dictionary<McObjectId,McObjectId> previous,Action cancel)
    {
        Need(ModelParameterSnapshot.Equivalent(before.Context,after.Context),"Native edit changed active drawing/layout/space context.","DOCUMENT_CONTEXT_MISMATCH");
        Need(ModelParameterSnapshot.Equivalent(before.Layouts,after.Layouts),"Native update changed layout identity, space ownership or active space.","DRAWING_VIEW_UPDATE_MISMATCH");
        var erasedHandles=before.Views.Where(view=>deleted.Contains(view.Id)).Select(view=>Validation.NormalizeHandle(view.Entity.Handle.ToString())).ToHashSet(StringComparer.Ordinal);
        var expectedEntities=Wire.Element(before.Entities.EnumerateArray().Where(item=>!erasedHandles.Contains(item.GetProperty("id").GetString()!)).ToArray());
        Need(ModelParameterSnapshot.Equivalent(expectedEntities,after.Entities),"Native update changed an unselected entity identity, owner, layer or stored common property.","DRAWING_VIEW_UPDATE_MISMATCH");
        Need(after.Views.Length==expectedViews.Count&&after.Views.All(view=>expectedViews.TryGetValue(view.Id,out var expected)&&
            before.Views.Any(old=>old.Id==view.Id&&old.Entity==view.Entity&&old.Layout==view.Layout&&old.Space==view.Space)&&
            ModelParameterSnapshot.Equivalent(expected,view.Metadata)),"Native view identity, source/parent/section/cut relation or requested property differs from expected state.","DRAWING_VIEW_UPDATE_MISMATCH");
        Need(ModelParameterSnapshot.Equivalent(before.Sections,after.Sections),"Native view update changed a section definition or its associations.","DRAWING_VIEW_UPDATE_MISMATCH");
        Need(before.Bodies.Length==after.Bodies.Length,"Native view edit removed or added a 3D source body.","DRAWING_VIEW_UPDATE_MISMATCH");
        foreach(var body in before.Bodies)
        {
            cancel();var actual=after.Bodies.SingleOrDefault(item=>item.Entity==body.Entity);
            Need(actual!=null&&actual.Id==body.Id&&actual.Model==body.Model&&ModelParameterSnapshot.Equivalent(body.Geometry,actual.Geometry),"Native view edit changed a 3D source identity or solid geometry.","DRAWING_VIEW_UPDATE_MISMATCH");
            var retained=body.Contents.Where(id=>!deleted.Contains(id)).ToHashSet();
            Need(retained.SetEquals(actual!.Contents)&&actual.Last==Skip(body.Last,deleted,previous),"Native erase changed retained source history membership or terminal feature.","DRAWING_VIEW_UPDATE_MISMATCH");
            var expectedNodes=new List<JsonElement>();
            foreach(var node in body.Nodes.EnumerateArray())
            {
                var id=body.Contents.Single(item=>item.ToString()==node.GetProperty("id").GetString());if(deleted.Contains(id))continue;
                var data=JsonNode.Parse(node.GetRawText())!.AsObject();
                if(body.Previous.TryGetValue(id,out var predecessor))data["previous"]=Id(Skip(predecessor,deleted,previous));
                expectedNodes.Add(JsonSerializer.SerializeToElement(data,Wire.Json));
            }
            Need(ModelParameterSnapshot.Equivalent(Wire.Element(expectedNodes),actual.Nodes),"Native view edit changed retained source feature status/order.","DRAWING_VIEW_UPDATE_MISMATCH");
        }
        Tree(after,cancel);
    }
    public static Result Execute(StructureWriteArguments args,DocumentState state,WriteJob job,ReadContext context,JsonLog log)
    {
        McUndoPoint? tracker=null;bool started=false;
        void Close(){var current=tracker;tracker=null;if(current==null)return;try{current.Stop();}finally{current.Dispose();}}
        void Trace(string stage)=>log.Write(job.Operation,job.OriginalRequestId,"drawing_view_edit_checkpoint",transaction:stage,operationId:job.OperationId);
        try
        {
            Snapshot before;View[] selected;
            using(var selector=state.Document.Database.TransactionManager.StartTransaction())
            {before=Capture(state,selector,job.CheckCancellation);selected=Select(args,before,selector,job.CheckCancellation);}
            var deleted=args.Operations[0] is DeleteDrawingViewsEdit?selected.Select(view=>view.Id).ToHashSet():new HashSet<McObjectId>();
            var predecessors=before.Views.ToDictionary(view=>view.Id,view=>view.Native.OrderObjBefore);
            var edits=args.Operations.OfType<SetDrawingViewPropertiesEdit>().ToDictionary(edit=>edit.Target.Handle,StringComparer.Ordinal);
            var expected=before.Views.Where(view=>!deleted.Contains(view.Id)).ToDictionary(view=>view.Id,view=>Expected(view,
                edits.GetValueOrDefault(Validation.NormalizeHandle(view.Entity.Handle.ToString())),deleted,predecessors));
            var depth=selected.ToDictionary(view=>view.Id,view=>Depth(view,before));
            job.CheckCancellation();Context(state);tracker=new McUndoPoint(false);tracker.Start();started=true;
            Trace("native_start_not_guaranteed");
            if(deleted.Count>0)
            {
                foreach(var view in selected.OrderByDescending(view=>depth[view.Id]))
                {job.CheckCancellation();Need(view.Native.DbEntity.Erase(),"Native erase refused the selected DrawingView.","NATIVE_ERASE_FAILED");}
            }
            else
            {
                foreach(var view in selected.OrderBy(view=>depth[view.Id]))
                {
                    job.CheckCancellation();var edit=edits[Validation.NormalizeHandle(view.Entity.Handle.ToString())];
                    // The SDK requires resident state changes to enter its native
                    // change/update queue. Do this only inside native tracking,
                    // never in the selector or by changing Reactor/KM flags.
                    var flags=Multicad.DatabaseServices.McDbObject.ObjChangesType.Geom;
                    if(edit.Origin!=null)flags|=Multicad.DatabaseServices.McDbObject.ObjChangesType.Position;
                    view.Native.TryModify((uint)flags);
                    if(edit.Origin is {} origin)
                    {var plane=view.Native.ViewPlane;view.Native.ViewPlane=new Plane3d(new Point3d(origin.X,origin.Y,plane.Origin.Z),plane.DirU,plane.DirV);}
                    if(edit.ModelScale is {} scale)view.Native.DbEntity.DScale=scale;
                    if(edit.ShowHiddenLines is {} hidden)view.Native.ShowHiddenLines=hidden;
                    if(edit.ShowHatches is {} hatches)view.Native.ShowHatches=hatches;
                    if(edit.ShowTangentEdges is {} tangent)view.Native.ShowTangentEdges=tangent;
                }
            }
            // Existing native creation uses this manager path. Observe all layout
            // view trees/sources before and after, including ordinary Paper views.
            McObjectManager.UpdateAll();job.CheckCancellation();Trace("native_update_returned");
            void Readback()
            {
                Snapshot after;using(var transaction=state.Document.Database.TransactionManager.StartTransaction())after=Capture(state,transaction,job.CheckCancellation);
                Verify(before,after,expected,deleted,predecessors,job.CheckCancellation);
                foreach(var view in selected)
                {
                    if(deleted.Contains(view.Id))Need(Gone(view.Entity),"Erased native view representation still exists.","NATIVE_ERASE_FAILED");
                    else Geometry(after.Views.Single(item=>item.Id==view.Id),job.CheckCancellation);
                }
            }
            Readback();job.CheckCancellation();job.BeginCommit();Close();Readback();
            var changes=new List<StructureChange>();
            if(args.Operations[0] is DeleteDrawingViewsEdit)
                changes.AddRange(selected.Select(view=>new StructureChange(0,"delete_drawing_views",new(args.BridgeId,args.DocumentId,Validation.NormalizeHandle(view.Entity.Handle.ToString())),"drawing_view",view.Metadata.GetProperty("name").GetString()!,false)));
            else for(int index=0;index<args.Operations.Length;index++)
            {
                var edit=(SetDrawingViewPropertiesEdit)args.Operations[index];changes.Add(new(index,edit.Operation,new(args.BridgeId,args.DocumentId,edit.Target.Handle),"drawing_view",edit.Target.ExpectedName,false));
            }
            var result=Result.Ok(job.OriginalRequestId,new ModifiedStructure(args.OperationId,"committed",false,changes.ToArray(),"not_guaranteed"),context,WriteRecovery.Warning);
            job.RecordCommit(result);Trace("commit_recorded");return result;
        }
        catch(System.Exception error)
        {
            if(job.CommittedResult is {} committed)return committed;
            System.Exception? cleanup=null;try{Close();}catch(System.Exception caught){cleanup=caught;}
            var cause=error is DomainException domain?domain.Error.code:error is OperationCanceledException?"CANCELLED":"MODEL_UPDATE_FAILED";
            return job.Error(started?"WRITE_OUTCOME_UNKNOWN":cause,started?
                "Native drawing-view edit started but its complete outcome was not verified. Inspect selected views and retained source/history; replay only the identical request.":
                "Existing native drawing-view request failed before changes started.",started?"unknown":"not_started",context,
                new{cause,error_type=error.GetType().Name,cleanup_error=cleanup?.GetType().Name,undo_history=started?"not_guaranteed":"may_contain_empty_step"});
        }
    }
}
