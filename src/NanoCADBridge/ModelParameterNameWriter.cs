using System.Runtime.CompilerServices;
using System.Text.Json;
using Multicad;
using Multicad.DatabaseServices;
using Multicad.DatabaseServices.Constraints;
using Multicad.Mc3D;
using NanoCAD.Contracts;
using NanoCAD.Ipc;
using Teigha.DatabaseServices;

namespace NanoCADBridge;

internal static class ModelParameterNameWriter
{
    internal static bool Handles(ModifyArguments args)=>args.Operations.Any(e=>e is ModelParameterNameEdit);
    private static void Need(bool ok,string code,string message){if(!ok)throw new DomainException(code,message);}
    private sealed record Binding(Mc3dSolid Body,McObjectId Variable,ModelParameterArguments ReadArgs,JsonElement Before,Dictionary<string,string> Expressions,ModelParameterBody[] Bodies,
        ModelSketchParameter411.Control[] Sketches,ModelFeatureVariable[] Features,Dictionary<string,HashSet<string>> Affected,HashSet<ObjectId> Inventory,HashSet<ObjectId> Representations)
    {internal Dictionary<string,string> CurrentHandles {get;}=new(StringComparer.OrdinalIgnoreCase);internal string[] Warnings {get;set;}=[];}
    internal static void Preflight(ModifyArguments args,DocumentState state,Transaction tx,Action? cancel=null)
    {
        var loaded=AppDomain.CurrentDomain.GetAssemblies().Select(a=>a.GetName().Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        Need(loaded.Contains("mapimgd")&&loaded.Contains("MapiBaseTypes"),"CAPABILITY_UNAVAILABLE","MultiCAD must already be loaded.");
        Resolve(args,state,tx,cancel);
    }
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static Binding Resolve(ModifyArguments args,DocumentState state,Transaction tx,Action? cancel=null)
    {
        var edit=(ModelParameterNameEdit)args.Operations.Single();var db=state.Document.Database;
        var model=((BlockTable)tx.GetObject(db.BlockTableId,OpenMode.ForRead))[BlockTableRecord.ModelSpace];
        Need(db.CurrentSpaceId==model,"OUT_OF_SCOPE","Activate Model Space.");
        var active=McDocument.ActiveDocument;
        Need(state.Document.IsNamedDrawing&&active!=null&&!string.IsNullOrWhiteSpace(active.Path)&&string.Equals(Path.GetFullPath(active.Path),Path.GetFullPath(state.Document.Name),StringComparison.OrdinalIgnoreCase),"DOCUMENT_CONTEXT_MISMATCH","Use the matching named drawing.");
        var source=CadStructureWriter.Find(db,edit.Handles.Single());var solid=tx.GetObject(source,OpenMode.ForRead) as Solid3d;
        Need(solid!=null&&solid.GetType()==typeof(Solid3d)&&solid.OwnerId==model&&!solid.IsNull,"UNSUPPORTED_ENTITY_TYPE","Use a top-level native model solid.");
        var layer=(LayerTableRecord)tx.GetObject(solid!.LayerId,OpenMode.ForRead);
        Need(!layer.IsLocked&&!layer.IsOff&&!layer.IsFrozen&&!layer.IsDependent,"LAYER_UNAVAILABLE","Model layer is unavailable.");
        var bodyId=McObjectId.FromHandle(source.Handle.Value,active!.ID);var body=bodyId.GetObjectOfType<Mc3dSolid>();
        Need(body!=null&&body.ID==bodyId,"OBJECT_IDENTITY_MISMATCH","Model identity is unavailable.");
        var readArgs=new ModelParameterArguments(args.BridgeId,args.DocumentId,args.ExpectedRevision,args.OperationId,args.Units,edit.Handles.Single(),edit.VariableId,"unused_read_context",1,ScopeHandles:edit.ScopeHandles);
        var bodies=ModelParameterBodies.Resolve(readArgs,state,tx,cancel);var before=bodies.Single(b=>b.Handle==edit.Handles.Single()).Before;
        var variables=before.GetProperty("variables").EnumerateArray().ToDictionary(v=>v.GetProperty("mc_id").GetString()!,StringComparer.OrdinalIgnoreCase);
        Need(variables.TryGetValue(edit.VariableId,out var target),"OBJECT_NOT_FOUND","Parameter ID is absent.");
        Need(target.GetProperty("name").GetString()==edit.ExpectedName,"PRECONDITION_FAILED","Parameter name changed.");
        Need(!variables.Values.Any(v=>string.Equals(v.GetProperty("name").GetString(),edit.Name,StringComparison.OrdinalIgnoreCase)),"NAME_CONFLICT","Another native variable has that name.");
        using var manager=body!.DbEntity.Document.GetDocumentVariablesManager(Constraint2dNetType.MCS);
        Need(manager!=null,"CAPABILITY_UNAVAILABLE","MCS manager unavailable.");
        var nativeIds=manager!.GetAllVariables();Need(nativeIds!=null,"VARIABLES_UNAVAILABLE","Variable inventory is unavailable or excessive.");
        var ids=nativeIds!.ToDictionary(id=>id.ToString(),StringComparer.OrdinalIgnoreCase);
        Need(ids.TryGetValue(edit.VariableId,out var native)&&manager.ValidateName(native,edit.Name),"INVALID_ARGUMENT","Native manager rejected the new name.");
        var affected=new HashSet<string>(StringComparer.OrdinalIgnoreCase){edit.VariableId};var queue=new Queue<string>();queue.Enqueue(edit.VariableId);
        while(queue.Count>0)
        {
            cancel?.Invoke();
            var id=queue.Dequeue();Need(variables.ContainsKey(id),"VARIABLES_UNAVAILABLE","Dependent variable is absent.");
            var children=variables[id].GetProperty("downstream_variables");Need(children.ValueKind==JsonValueKind.Array,"VARIABLES_UNAVAILABLE","Dependent inventory is unknown.");
            foreach(var child in children.EnumerateArray())if(affected.Add(child.GetString()!))
            {queue.Enqueue(child.GetString()!);}
        }
        var expressions=new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase);
        var sketchControls=new List<ModelSketchParameter411.Control>();var featureControls=new List<ModelFeatureVariable>();
        var sketchStates=new Dictionary<McObjectId,NativeSketchConstraintState385.Snapshot>();var seeds=bodies.ToDictionary(b=>b.Handle,_=>new HashSet<string>(StringComparer.OrdinalIgnoreCase),StringComparer.OrdinalIgnoreCase);
        foreach(var id in affected)
        {
            cancel?.Invoke();
            Need(ids.TryGetValue(id,out var variable),"VARIABLES_UNAVAILABLE","Related variable disappeared.");
            expressions[id]=ModelArithmetic.ParseStored(variables[id].GetProperty("expression").GetString()!,cancel).RenameReference(edit.ExpectedName,edit.Name);
            var featureId=manager.GetVariableHostObjectId(variable);
            if(ModelVariableMetadata.IsUser(variables[id].GetProperty("type").GetString()!))
            {Need(featureId.IsNull&&!manager.IsAngularValue(variable),"CAPABILITY_UNAVAILABLE","User variables must be non-angular and document-owned.");continue;}
            var boundBody=ModelParameterBodies.Owner(bodies,featureId,cancel);
            if(featureId.GetObjectOfType<PlanarSketch>()!=null)
            {
                if(!sketchStates.TryGetValue(featureId,out var snapshot)){snapshot=ModelSketchParameter411.Capture(readArgs,boundBody,featureId,state,tx,cancel??(()=>{}));sketchStates.Add(featureId,snapshot);}
                sketchControls.Add(ModelSketchParameter411.Resolve(snapshot,variable,cancel??(()=>{})));
            }
            else
            {
                var feature=ModelFeatureVariable.Resolve(boundBody.Body,featureId,variable,manager.IsAngularValue(variable),allowPlacement:true,allowFinishing:true,cancel:cancel);
                if(feature.Role=="shell_thickness")ModelShellParameter412.ValidateExpression(expressions[id],cancel);
                Need(!feature.IsAngle||variables[id].GetProperty("value_unit").GetString()=="degrees","MODEL_STATE_UNAVAILABLE","Angle variable units must be proven degrees.");featureControls.Add(feature);
            }
            seeds[boundBody.Handle].Add(featureId.ToString());
        }
        foreach(var id in affected)
        {
            var consumers=manager.GetDependentOn3dObjects(ids[id]);
            Need(consumers!=null&&(ModelVariableMetadata.IsUser(variables[id].GetProperty("type").GetString()!)||consumers.Count>0),"CAPABILITY_UNAVAILABLE","A renamed/related variable has unknown consumers.");
            foreach(var consumer in consumers!)
            {
                var owners=bodies.Where(b=>b.Before.GetProperty("nodes").EnumerateArray().Any(n=>string.Equals(n.GetProperty("mc_id").GetString(),consumer.ToString(),StringComparison.OrdinalIgnoreCase))).ToArray();
                Need(owners.Length==1||owners.Length==0&&bodies.Any(b=>ModelViewFollowers.OwnsDependent(b.Views,consumer)),"OUT_OF_SCOPE","A renamed variable consumer lies outside explicit scope_handles.");
                if(owners.Length==1)seeds[owners[0].Handle].Add(consumer.ToString());
            }
            if(!ModelVariableMetadata.IsUser(variables[id].GetProperty("type").GetString()!))Need(consumers!.Contains(manager.GetVariableHostObjectId(ids[id])),"MODEL_STATE_UNAVAILABLE","The affected variable does not retain its actual typed control owner as a consumer.");
        }
        Need(sketchControls.Count==0||ModelVariableMetadata.IsUser(target.GetProperty("type").GetString()!),"OUT_OF_SCOPE","This extension renames named User drivers with internal sketch-formula consumers; it does not rename a native dimensional relation descriptor.");
        var closures=new Dictionary<string,HashSet<string>>(StringComparer.OrdinalIgnoreCase);
        foreach(var scoped in bodies)
        {
            var closure=ModelDependencyGraph410.Closure(scoped.Before,seeds[scoped.Handle],cancel);var roots=ModelInventoryBudget.History(scoped.Body,cancel).Where(id=>seeds[scoped.Handle].Contains(id.ToString())&&id.GetObjectOfType<HistoryItem>()!=null);
            closure.UnionWith(ModelDependencyGraph410.OwnedClosure(scoped.Body,roots,cancel??(()=>{}),scoped.Views));closures.Add(scoped.Handle,closure);
        }
        return new(body,native,readArgs,before,expressions,bodies,sketchControls.ToArray(),featureControls.ToArray(),closures,ModelFeatureReorderState345.Inventory(state,tx,cancel??(()=>{})),bodies.Select(b=>CadStructureWriter.Find(db,b.Handle)).ToHashSet());
    }
    private static string[] Verify(ModelParameterNameEdit edit,DocumentState state,Binding binding,Action? cancel=null)
    {
        var states=new Dictionary<string,JsonElement>(StringComparer.OrdinalIgnoreCase);var currentRoots=new HashSet<ObjectId>();
        foreach(var scoped in binding.Bodies)
        {
            string handle=scoped.Handle;
            if(binding.Sketches.Length>0)using(var tx=state.Document.Database.TransactionManager.StartTransaction())
            {var representation=ModelHistoryEditGuard.CurrentRepresentation(scoped.Body,state,tx,cancel);currentRoots.Add(representation);handle=Validation.NormalizeHandle(representation.Handle.ToString());}
            binding.CurrentHandles[scoped.Handle]=handle;
            var observed=ModelParameterSnapshot.Capture(binding.ReadArgs with{Handle=handle},state,cancel);ModelParameterSnapshot.Healthy(observed);states.Add(scoped.Handle,observed);
            if(binding.Sketches.Length==0)foreach(string field in new[]{"handles","native","owner","body","last_item","nodes","external_sources"})
                Need(ModelParameterSnapshot.Equivalent(scoped.Before.GetProperty(field),observed.GetProperty(field)),"MODEL_STATE_UNAVAILABLE","Naming changed a scoped body's geometry or history.");
            else
            {
                Need(ModelParameterSnapshot.Equivalent(scoped.Before.GetProperty("native"),observed.GetProperty("native")),"MODEL_STATE_UNAVAILABLE","Renaming a User driver changed the actual model geometry.");
                ModelDependencyGraph410.Verify(scoped.Before,observed,binding.Affected[scoped.Handle],binding.Features,cancel,sketchMotion:true);
                ModelViewFollowers.Verify(scoped.Body,scoped.Views,cancel??(()=>{}),refreshStandalone:true);
            }
        }
        var after=states[edit.Handles.Single()];
        var current=after.GetProperty("variables").EnumerateArray().ToDictionary(v=>v.GetProperty("mc_id").GetString()!,StringComparer.OrdinalIgnoreCase);
        var prior=binding.Before.GetProperty("variables").EnumerateArray().ToArray();Need(current.Count==prior.Length,"MODEL_STATE_UNAVAILABLE","Variable inventory changed.");
        var rewritten=new List<string>();
        foreach(var old in prior)
        {
            var id=old.GetProperty("mc_id").GetString()!;Need(current.TryGetValue(id,out var value),"MODEL_STATE_UNAVAILABLE","Variable identity changed.");
            foreach(var property in old.EnumerateObject().Where(p=>p.Name is not "name" and not "expression"))
                Need(ModelParameterSnapshot.Equivalent(property.Value,value.GetProperty(property.Name)),"MODEL_STATE_UNAVAILABLE","Variable value, type, unit, owner or dependencies changed.");
            var expectedName=id.Equals(edit.VariableId,StringComparison.OrdinalIgnoreCase)?edit.Name:old.GetProperty("name").GetString();
            Need(value.GetProperty("name").GetString()==expectedName,"MODEL_UPDATE_FAILED","Native variable name differs from the request.");
            var expression=value.GetProperty("expression").GetString()!;
            if(binding.Expressions.TryGetValue(id,out var expected))
            {
                Need(ModelArithmetic.ParseStored(expression,cancel).Canonical==expected,"MODEL_UPDATE_FAILED","A dependent expression did not retain its reference after renaming.");
                if(ModelArithmetic.ParseStored(old.GetProperty("expression").GetString()!,cancel).Canonical!=expected)rewritten.Add(id);
            }
            else Need(expression==old.GetProperty("expression").GetString(),"MODEL_STATE_UNAVAILABLE","An unrelated expression changed.");
        }
        if(binding.Sketches.Length>0)
        {
            using(var tx=state.Document.Database.TransactionManager.StartTransaction())
            {var inventory=ModelFeatureReorderState345.Inventory(state,tx,cancel??(()=>{}));Need(inventory.Except(binding.Inventory).All(currentRoots.Contains)&&binding.Inventory.Except(inventory).All(binding.Representations.Contains),"MODEL_STATE_UNAVAILABLE","Renaming changed unrelated top-level drawing membership.");}
            var expected=binding.Sketches.ToDictionary(control=>control.Variable.ToString(),control=>control.Value,StringComparer.OrdinalIgnoreCase);
            binding.Warnings=ModelSketchParameterGraph411.Verify(binding.ReadArgs,state,binding.Sketches,binding.Affected,binding.CurrentHandles,expected,binding.Features,cancel??(()=>{}));
        }
        return rewritten.ToArray();
    }
    internal static Result Execute(ModifyArguments args,DocumentState state,WriteJob job,ReadContext context,JsonLog log)
    {
        McUndoPoint? tracker=null;bool started=false;Binding? binding=null;string stage="preparation";
        void Close(){var value=tracker;tracker=null;if(value!=null)try{value.Stop();}finally{value.Dispose();}}
        void Trace(string point){stage=point;log.Write(job.Operation,job.OriginalRequestId,"model_parameter_name_checkpoint",transaction:point,operationId:job.OperationId);}
        try
        {
            var edit=(ModelParameterNameEdit)args.Operations.Single();using(var tx=state.Document.Database.TransactionManager.StartTransaction())binding=Resolve(args,state,tx,job.CheckCancellation);
            if(binding.Sketches.Length>0)foreach(var scoped in binding.Bodies)ModelViewFollowers.CheckStandalone(scoped.Body,scoped.Views,job.CheckCancellation);
            job.CheckCancellation();tracker=new McUndoPoint(false);tracker.Start();
            using(var manager=binding.Body.DbEntity.Document.GetDocumentVariablesManager(Constraint2dNetType.MCS))
            {Need(manager!=null,"CAPABILITY_UNAVAILABLE","MCS manager unavailable.");job.CheckCancellation();started=true;Trace("rename_before");Need(manager!.SetVariableName(binding.Variable,edit.Name),"MODEL_UPDATE_FAILED","Native rename rejected.");}
            Trace("rename_returned");McObjectManager.UpdateAll();job.CheckCancellation();
            if(binding.Sketches.Length>0)foreach(var scoped in binding.Bodies.Where(b=>binding.Affected[b.Handle].Count>0))
            {ModelSketchParameterGraph411.Solve(scoped,binding.Affected[scoped.Handle],job.CheckCancellation);Need(scoped.Body.RebuildPart(),"MODEL_UPDATE_FAILED","A retained part refused its native rebuild after User-driver renaming.");}
            McObjectManager.UpdateAll();Verify(edit,state,binding,job.CheckCancellation);
            job.BeginCommit();Close();var rewritten=Verify(edit,state,binding,job.CheckCancellation);
            var reference=new EntityRef(args.BridgeId,args.DocumentId,binding.CurrentHandles[edit.Handles.Single()]);
            var modified=binding.Sketches.Length==0?new[]{reference}:binding.Bodies.Where(b=>binding.Affected[b.Handle].Count>0).Select(b=>new EntityRef(args.BridgeId,args.DocumentId,binding.CurrentHandles[b.Handle])).Append(reference).DistinctBy(r=>r.handle,StringComparer.OrdinalIgnoreCase).ToArray();
            var result=Result.Ok(job.OriginalRequestId,new ModifiedEntities(args.OperationId,"committed",false,modified,Array.Empty<EntityRef>(),Array.Empty<EntityRef>(),Array.Empty<CopyMapping>(),"not_guaranteed",
                model_parameter_names:new[]{new ModelParameterNameChange(reference,edit.VariableId,edit.ExpectedName,edit.Name,rewritten)}),context,WriteRecovery.Warning);
            if(binding.Warnings.Length>0)result=result with{warnings=(result.warnings??[]).Concat(binding.Warnings).Distinct(StringComparer.Ordinal).ToArray()};job.RecordCommit(result);Trace("commit_recorded");return result;
        }
        catch(Exception error)
        {
            if(job.CommittedResult is {} committed)return committed;
            Exception? cleanup=null;try{Close();}catch(Exception e){cleanup=e;}
            bool unknown=started||cleanup!=null;
            string cause=error is DomainException d?d.Error.code:error is OperationCanceledException?"CANCELLED":"MODEL_UPDATE_FAILED";
            return job.Error(unknown?"WRITE_OUTCOME_UNKNOWN":cause,unknown?"Native parameter naming was not verified; no rollback is claimed. Inspect affected variables and models before dependent edits.":error.Message,
                unknown?"unknown":"not_started",context,new{cause,stage,message=error.Message,cleanup_error=cleanup?.GetType().Name}) with{warnings=new[]{WriteRecovery.Warning}};
        }
    }
}
