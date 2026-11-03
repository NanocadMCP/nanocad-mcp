using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text.Json;
using Multicad;
using Multicad.DatabaseServices;
using Multicad.DatabaseServices.Constraints;
using Multicad.DatabaseServices.DocumentVariables;
using Multicad.Mc3D;
using NanoCAD.Contracts;
using NanoCAD.Ipc;
using Teigha.DatabaseServices;

namespace NanoCADBridge;

internal static class ModelUserVariableWriter
{
    internal static bool Handles(ModifyArguments args)=>args.Operations.Any(e=>e is ModelUserVariableEdit);
    private static void Need(bool ok,string code,string message){if(!ok)throw new DomainException(code,message);}
    private sealed record Binding(Mc3dSolid Body,ModelParameterArguments ReadArgs,JsonElement Before);
    internal static void Preflight(ModifyArguments args,DocumentState state,Transaction tx,Action? cancel=null)
    {
        var loaded=AppDomain.CurrentDomain.GetAssemblies().Select(a=>a.GetName().Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        Need(loaded.Contains("mapimgd")&&loaded.Contains("MapiBaseTypes"),"CAPABILITY_UNAVAILABLE","MultiCAD must already be loaded.");
        Resolve(args,state,tx,cancel);
    }
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static Binding Resolve(ModifyArguments args,DocumentState state,Transaction tx,Action? cancel=null)
    {
        var edit=(ModelUserVariableEdit)args.Operations.Single();var db=state.Document.Database;
        var model=((BlockTable)tx.GetObject(db.BlockTableId,OpenMode.ForRead))[BlockTableRecord.ModelSpace];
        Need(db.CurrentSpaceId==model,"OUT_OF_SCOPE","Activate Model Space.");
        var active=McDocument.ActiveDocument;
        Need(state.Document.IsNamedDrawing&&active!=null&&!string.IsNullOrWhiteSpace(active.Path)&&string.Equals(Path.GetFullPath(active.Path),Path.GetFullPath(state.Document.Name),StringComparison.OrdinalIgnoreCase),"DOCUMENT_CONTEXT_MISMATCH","Use the matching named drawing.");
        var source=CadStructureWriter.Find(db,edit.Handles.Single());var solid=tx.GetObject(source,OpenMode.ForRead) as Solid3d;
        Need(solid!=null&&solid.GetType()==typeof(Solid3d)&&solid.OwnerId==model&&!solid.IsNull,"UNSUPPORTED_ENTITY_TYPE","Use a top-level native model solid as network context.");
        var bodyId=McObjectId.FromHandle(source.Handle.Value,active!.ID);var body=bodyId.GetObjectOfType<Mc3dSolid>();
        Need(body!=null&&body.ID==bodyId,"OBJECT_IDENTITY_MISMATCH","Model identity is unavailable.");
        var readArgs=new ModelParameterArguments(args.BridgeId,args.DocumentId,args.ExpectedRevision,args.OperationId,args.Units,edit.Handles.Single(),"unused","unused_read_context",1);
        var before=ModelParameterSnapshot.Capture(readArgs,state,cancel);ModelParameterSnapshot.Healthy(before);
        // Observe every existing variable before adding one. This local budget
        // complements the complete Capture transport budget and preserves all
        // current native expressions, identities and consumer inventories.
        var variableBudget=new ModelInventoryBudget(cancel);
        foreach(var existing in before.GetProperty("variables").EnumerateArray())variableBudget.Add(existing);
        var variables=before.GetProperty("variables").EnumerateArray().ToArray();
        Need(!variables.Any(v=>string.Equals(v.GetProperty("name").GetString(),edit.Name,StringComparison.OrdinalIgnoreCase)),"NAME_CONFLICT","Another native variable has that name.");
        using var manager=body!.DbEntity.Document.GetDocumentVariablesManager(Constraint2dNetType.MCS);
        // ValidateName is an existing-variable check; the null-ID call was rejected in131.
        // The parser and current inventory validate syntax/collisions. AddVariable is
        // the documented creation API and its returned identity/value are verified.
        Need(manager!=null,"CAPABILITY_UNAVAILABLE","MCS variable manager unavailable.");
        return new(body,readArgs,before);
    }
    private static JsonElement Verify(ModelUserVariableEdit edit,DocumentState state,Binding binding,McObjectId id,Action? cancel=null)
    {
        Need(!id.IsNull,"MODEL_UPDATE_FAILED","Native variable creation returned no identity.");
        var after=ModelParameterSnapshot.Capture(binding.ReadArgs,state,cancel);ModelParameterSnapshot.Healthy(after);
        foreach(var field in new[]{"handles","native","owner","body","last_item","nodes","external_sources"})
            Need(ModelParameterSnapshot.Equivalent(binding.Before.GetProperty(field),after.GetProperty(field)),"MODEL_STATE_UNAVAILABLE","Creating an unused variable changed model geometry or history.");
        var current=after.GetProperty("variables").EnumerateArray().ToDictionary(v=>v.GetProperty("mc_id").GetString()!,StringComparer.OrdinalIgnoreCase);
        var prior=binding.Before.GetProperty("variables").EnumerateArray().ToArray();
        Need(current.Count==prior.Length+1,"MODEL_STATE_UNAVAILABLE","Exactly one new variable must appear.");
        foreach(var old in prior)Need(current.TryGetValue(old.GetProperty("mc_id").GetString()!,out var value)&&ModelParameterSnapshot.Equivalent(old,value),"MODEL_STATE_UNAVAILABLE","An existing variable changed while creating an unused driver.");
        Need(current.TryGetValue(id.ToString(),out var added),"MODEL_STATE_UNAVAILABLE","Created identity is absent from the document inventory.");
        Need(added.GetProperty("name").GetString()==edit.Name&&added.GetProperty("type").GetString()=="User"&&added.GetProperty("host_object_id").ValueKind==JsonValueKind.Null,"MODEL_STATE_UNAVAILABLE","New variable does not have the expected name, User type and document ownership.");
        Need(added.GetProperty("value_available").GetBoolean()&&ModelParameterSnapshot.Near(added.GetProperty("value").GetDouble(),edit.Value),"MODEL_UPDATE_FAILED","New variable value differs from the request.");
        var expression=ModelArithmetic.ParseStored(added.GetProperty("expression").GetString()!,cancel);
        Need(expression.References.Length==0&&ModelParameterSnapshot.Near(expression.Evaluate(_=>throw new InvalidOperationException()),edit.Value),"MODEL_UPDATE_FAILED","New variable expression is not the requested constant.");
        foreach(var field in new[]{"downstream_variables","downstream_objects"})
            Need(added.GetProperty(field).ValueKind==JsonValueKind.Array&&added.GetProperty(field).GetArrayLength()==0,"MODEL_STATE_UNAVAILABLE","An unused new variable has unknown or unexpected consumers.");
        Need(added.GetProperty("value_unit").GetString()=="native_variable_units","MODEL_STATE_UNAVAILABLE","New variable must be non-angular.");
        return added;
    }
    internal static Result Execute(ModifyArguments args,DocumentState state,WriteJob job,ReadContext context,JsonLog log)
    {
        McUndoPoint? tracker=null;bool started=false;string stage="preparation";McObjectId variable=McObjectId.Null;
        void Close(){var value=tracker;tracker=null;if(value!=null)try{value.Stop();}finally{value.Dispose();}}
        void Trace(string point){stage=point;log.Write(job.Operation,job.OriginalRequestId,"model_user_variable_checkpoint",transaction:point,operationId:job.OperationId);}
        try
        {
            var edit=(ModelUserVariableEdit)args.Operations.Single();Binding binding;using(var tx=state.Document.Database.TransactionManager.StartTransaction())binding=Resolve(args,state,tx,job.CheckCancellation);
            job.CheckCancellation();tracker=new McUndoPoint(false);tracker.Start();
            using(var manager=binding.Body.DbEntity.Document.GetDocumentVariablesManager(Constraint2dNetType.MCS))
            {Need(manager!=null,"CAPABILITY_UNAVAILABLE","MCS manager unavailable.");job.CheckCancellation();started=true;Trace("create_before");variable=manager!.AddVariable(edit.Name,edit.Value.ToString("R",CultureInfo.InvariantCulture));}
            Trace("create_returned");McObjectManager.UpdateAll();job.CheckCancellation();Verify(edit,state,binding,variable,job.CheckCancellation);
            job.BeginCommit();Close();var added=Verify(edit,state,binding,variable,job.CheckCancellation);
            var reference=new EntityRef(args.BridgeId,args.DocumentId,edit.Handles.Single());
            var result=Result.Ok(job.OriginalRequestId,new ModifiedEntities(args.OperationId,"committed",false,Array.Empty<EntityRef>(),Array.Empty<EntityRef>(),Array.Empty<EntityRef>(),Array.Empty<CopyMapping>(),"not_guaranteed",
                model_variables_created:new[]{new ModelUserVariableChange(reference,variable.ToString(),edit.Name,added.GetProperty("expression").GetString()!,edit.Value,"native_variable_units")}),context,WriteRecovery.Warning);
            job.RecordCommit(result);Trace("commit_recorded");return result;
        }
        catch(Exception error)
        {
            if(job.CommittedResult is {} committed)return committed;
            Exception? cleanup=null;try{Close();}catch(Exception e){cleanup=e;}
            bool unknown=started||cleanup!=null;
            string cause=error is DomainException d?d.Error.code:error is OperationCanceledException?"CANCELLED":"MODEL_UPDATE_FAILED";
            return job.Error(unknown?"WRITE_OUTCOME_UNKNOWN":cause,unknown?"Native user variable creation was not verified; no rollback is claimed. Inspect the affected variable network before dependent edits.":error.Message,
                unknown?"unknown":"not_started",context,new{cause,stage,message=error.Message,variable_id=variable.IsNull?null:variable.ToString(),cleanup_error=cleanup?.GetType().Name}) with{warnings=new[]{WriteRecovery.Warning}};
        }
    }
}
