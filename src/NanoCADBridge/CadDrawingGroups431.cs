using System.Security.Cryptography;
using System.Text.Json;
using NanoCAD.Contracts;
using NanoCAD.Ipc;
using Teigha.DatabaseServices;

namespace NanoCADBridge;

// Native AcDbGroup is selection organization, never cloned geometry or a
// substitute for a retained pattern. SDK Group.Append/Remove and the owning
// Database.GroupDictionaryId supply its complete lifecycle.
internal static class CadDrawingGroups431
{
    internal sealed record Snapshot(ObjectId Id,string Name,string Description,bool Selectable,ObjectId[] Members,string[] Handles,string Token);
    sealed record Plan(int Index,DrawingGroupEdit431 Edit,Snapshot? Before,ObjectId[] Add,ObjectId[] Remove);
    static string Handle(ObjectId id)=>Validation.NormalizeHandle(id.Handle.ToString());
    static bool Live(ObjectId id)=>!id.IsNull&&id.IsValid&&!id.IsErased&&!id.IsEffectivelyErased;
    static void Need(bool ok,string message,string code="OUT_OF_SCOPE")
    {if(!ok)throw new DomainException(code,message);}
    internal static bool Handles(StructureWriteArguments args)=>args.Operations.Any(e=>e is DrawingGroupEdit431);
    // Group reactors organize selection only. They must not be mistaken for
    // dimensional/solver/model consumers that prevent an ordinary member edit.
    internal static bool OrganizationalReactor(DBObject source,ObjectId reactor,Transaction tr,Action cancel)
    {
        cancel();var db=source.Database;
        if(db==null||source.ObjectId.IsNull||!Live(reactor))return false;
        if(tr.GetObject(reactor,OpenMode.ForRead) is not Group group||group.GetType()!=typeof(Group)||group.OwnerId!=db.GroupDictionaryId)return false;
        var dictionary=(DBDictionary)tr.GetObject(db.GroupDictionaryId,OpenMode.ForRead);
        if(!dictionary.Contains(reactor))return false;
        var members=group.GetAllEntityIds();return members!=null&&members.Contains(source.ObjectId);
    }
    internal static bool HasNonOrganizationalReactors(DBObject source,Transaction? transaction=null,Action? cancel=null)
    {
        using var ids=source.GetPersistentReactorIds();if(ids.Count==0)return false;
        var tr=transaction??source.Database?.TransactionManager.TopTransaction;
        if(tr==null)return true;
        foreach(ObjectId id in ids)if(!OrganizationalReactor(source,id,tr,cancel??(()=>{})))return true;
        return false;
    }
    internal static Snapshot Capture(Group group,string name,Database db,Transaction tr,Action cancel)
    {
        Need(Live(group.ObjectId)&&group.GetType()==typeof(Group)&&group.OwnerId==db.GroupDictionaryId,"The native Group does not belong to this document's actual group dictionary.","GROUP_UNAVAILABLE");
        var members=group.GetAllEntityIds();Need(members!=null,"Native Group membership is unavailable, not empty.","GROUP_MEMBERS_UNAVAILABLE");
        var handles=new List<string>();var seen=new HashSet<ObjectId>();var budget=new ModelInventoryBudget(cancel);
        foreach(var id in members!)
        {
            cancel();Need(!id.IsNull&&id.IsValid&&seen.Add(id),"The native Group contains an unavailable or duplicate member identity.","GROUP_MEMBERS_UNAVAILABLE");
            var value=Handle(id);budget.Add(value);handles.Add(value);
        }
        string description=group.Description??"";bool selectable=group.Selectable;
        string token=Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(new{handle=Handle(group.ObjectId),name,description,selectable,member_handles=handles},Wire.Json)));
        return new(group.ObjectId,name,description,selectable,members!,handles.ToArray(),token);
    }
    internal static object Read(Group group,string name,Database db,Transaction tr,Action cancel)
    {
        var row=Capture(group,name,db,tr,cancel);
        return new{native_type="Group",description=row.Description,selectable=row.Selectable,member_handles=row.Handles,member_count=row.Handles.Length,group_token=row.Token,members_complete=true};
    }
    static Plan[] Resolve(StructureWriteArguments args,DocumentState state,Transaction tr,Action cancel)
    {
        DrawingGroups431.Batch(args.Operations);var db=state.Document.Database;
        var dictionary=(DBDictionary)tr.GetObject(db.GroupDictionaryId,OpenMode.ForRead);
        var currentSpace=CadWriteSpace.Resolve(db,tr,args.EffectiveTargetSpace);
        var plans=new List<Plan>();var budget=new ModelInventoryBudget(cancel);
        for(int index=0;index<args.Operations.Length;index++)
        {
            cancel();var edit=(DrawingGroupEdit431)args.Operations[index];Snapshot? prior=null;
            if(edit.Handle!=null)
            {
                var id=CadStructureWriter.Find(db,edit.Handle);var group=tr.GetObject(id,OpenMode.ForRead) as Group;
                Need(group!=null&&dictionary.Contains(edit.ExpectedName!)&&dictionary.GetAt(edit.ExpectedName!)==id,"The selected Group/name does not match this native dictionary.","PRECONDITION_FAILED");
                prior=Capture(group!,edit.ExpectedName!,db,tr,cancel);
                Need(prior.Token.Equals(edit.ExpectedToken,StringComparison.Ordinal),"Group membership or properties changed; refresh get_structure groups.","PRECONDITION_FAILED");
            }
            if(edit.Name!=null&&(prior==null||!edit.Name.Equals(prior.Name,StringComparison.Ordinal)))
                Need(!dictionary.Contains(edit.Name)||prior!=null&&dictionary.GetAt(edit.Name)==prior.Id,"The requested destination Group name belongs to another group.","GROUP_EXISTS");
            var add=new List<ObjectId>();var remove=new List<ObjectId>();
            foreach(var handle in edit.AddHandles)
            {
                cancel();var id=CadStructureWriter.Find(db,handle);var entity=tr.GetObject(id,OpenMode.ForRead) as Entity;
                Need(entity!=null&&entity.Database==db&&entity.OwnerId==currentSpace,"Added Group members must be existing top-level entities in the requested Model/Paper space.");
                Need(prior==null||!prior.Members.Contains(id),"The requested added entity already belongs to this Group.","PRECONDITION_FAILED");add.Add(id);
            }
            foreach(var handle in edit.RemoveHandles)
            {
                cancel();var id=CadStructureWriter.Find(db,handle);
                Need(prior!=null&&prior.Members.Contains(id),"The requested removed entity is not a current Group member.","PRECONDITION_FAILED");remove.Add(id);
            }
            budget.Add(new{edit,prior_handles=prior?.Handles});plans.Add(new(index,edit,prior,add.ToArray(),remove.ToArray()));
        }
        return plans.ToArray();
    }
    internal static void Preflight(StructureWriteArguments args,DocumentState state,Transaction tr,Action? cancel=null)=>_=Resolve(args,state,tr,cancel??(()=>{}));
    internal static Result Execute(StructureWriteArguments args,DocumentState state,WriteJob job,ReadContext context,JsonLog log)
    {
        Transaction? tr=null;bool started=false;string stage="preflight";Exception? failure=null;
        try
        {
            var db=state.Document.Database;tr=db.TransactionManager.StartTransaction();var plans=Resolve(args,state,tr,job.CheckCancellation);
            var changes=new List<StructureChange>();var groups=new List<DrawingGroupChange431>();var dictionary=(DBDictionary)tr.GetObject(db.GroupDictionaryId,OpenMode.ForWrite);
            EntityRef Ref(ObjectId id)=>new(args.BridgeId,args.DocumentId,Handle(id));
            foreach(var plan in plans)
            {
                job.CheckCancellation();var edit=plan.Edit;var name=edit.Name??plan.Before!.Name;Group group;ObjectId identity;
                // Recording lifetime before the first native mutation prevents
                // an unexpected failure from becoming a false not_started.
                stage=edit.Operation;started=true;
                if(plan.Before==null)
                {
                    group=new Group(edit.Description??"",edit.Selectable??true);identity=dictionary.SetAt(name,group);tr.AddNewlyCreatedDBObject(group,true);
                }
                else {identity=plan.Before.Id;group=(Group)tr.GetObject(identity,OpenMode.ForWrite);}
                var original=(plan.Before?.Members??Array.Empty<ObjectId>()).ToArray();
                var liveOriginal=original.Where(Live).Select(id=>new{id,owner=tr.GetObject(id,OpenMode.ForRead).OwnerId}).ToArray();
                if(edit.Operation=="dissolve_group")
                {
                    dictionary.Remove(plan.Before!.Name);group.Erase();
                    Need(!dictionary.Contains(plan.Before.Name)&&group.IsErased,"The native Group was not dissolved.","GROUP_CHANGE_MISMATCH");
                    groups.Add(new(plan.Index,edit.Operation,Ref(identity),plan.Before.Name,plan.Before.Handles,null,true));
                }
                else
                {
                    foreach(var id in plan.Remove){job.CheckCancellation();group.Remove(id);}
                    foreach(var id in plan.Add){job.CheckCancellation();group.Append(id);}
                    if(edit.Description!=null)group.Description=edit.Description;
                    if(edit.Selectable.HasValue)group.Selectable=edit.Selectable.Value;
                    if(plan.Before!=null&&name!=plan.Before.Name)dictionary.SetName(plan.Before.Name,name);
                    Need(dictionary.Contains(name)&&dictionary.GetAt(name)==identity,"The Group dictionary lost the retained identity.","GROUP_CHANGE_MISMATCH");
                    var actual=Capture(group,name,db,tr,job.CheckCancellation);
                    var expected=original.Except(plan.Remove).Concat(plan.Add).ToArray();
                    Need(actual.Members.Length==expected.Length&&actual.Members.ToHashSet().SetEquals(expected),"Native Group membership differs from the requested selection.","GROUP_CHANGE_MISMATCH");
                    Need(actual.Description==(edit.Description??plan.Before?.Description??"")&&actual.Selectable==(edit.Selectable??plan.Before?.Selectable??true),"Native Group properties differ from the requested change.","GROUP_CHANGE_MISMATCH");
                    groups.Add(new(plan.Index,edit.Operation,Ref(identity),name,actual.Handles,actual.Token,false));
                }
                foreach(var member in liveOriginal)
                {job.CheckCancellation();Need(Live(member.id)&&tr.GetObject(member.id,OpenMode.ForRead).OwnerId==member.owner,"Group editing unexpectedly removed or re-owned a member entity.","GROUP_MEMBER_CHANGED");}
                changes.Add(new(plan.Index,edit.Operation,Ref(identity),"group",name,plan.Before==null));
            }
            var output=new DrawingGroupResult431(args.OperationId,"committed",false,changes.ToArray(),"not_guaranteed",groups.ToArray());
            SerializedCapacity357.Check(output,Wire.PayloadBudget-8192,job.CheckCancellation);
            var result=Result.Ok(job.OriginalRequestId,output,context,WriteRecovery.Warning);
            job.CheckCancellation();stage="commit";job.BeginCommit();tr.Commit();job.RecordCommit(result);
        }
        catch(Exception error){failure=error;}
        Exception? cleanup=null;try{tr?.Dispose();}catch(Exception error){cleanup=error;}
        if(job.CommittedResult is{} committed)
        {if(cleanup!=null)committed=committed with{warnings=committed.warnings.Append("POST_COMMIT_CLEANUP_FAILED: Group commit recorded; refresh its native membership before further work.").ToArray()};job.RecordCommit(committed);return committed;}
        bool unknown=started||cleanup!=null;string code=failure is DomainException d?d.Error.code:failure is OperationCanceledException?"CANCELLED":"NATIVE_GROUP_FAILED";
        return job.Error(unknown?"WRITE_OUTCOME_UNKNOWN":code,unknown?"Native Group editing started; inspect the actual Group and retain the exact request. No rollback or new-ID retry is claimed.":failure?.Message??"Group change did not start.",unknown?"unknown":"not_started",context,new{cause=code,stage,native_error=failure is Teigha.Runtime.Exception native?native.ErrorStatus.ToString():null,cleanup=cleanup?.Message}) with{warnings=[WriteRecovery.Warning]};
    }
}
