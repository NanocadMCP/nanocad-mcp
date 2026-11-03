using System.Globalization;
using System.Text.Json;
using Multicad;
using Multicad.DatabaseServices;
using Multicad.DatabaseServices.Constraints;
using Multicad.Mc3D;
using NanoCAD.Contracts;
using NanoCAD.Ipc;
using Db=Teigha.DatabaseServices;

namespace NanoCADBridge;

// One native expression/consumer protocol for numeric, single-body and explicit
// multi-body scopes. A body may contain any number of retained finishers.
internal static class ModelParameterGraphWriter410
{
    private sealed record Bound(ModelParameterBody Body,ModelFeatureVariable? Feature=null,ModelSketchParameter411.Control? Sketch=null)
    {
        internal McObjectId ID=>Feature?.ID??Sketch!.ID;
        internal string Unit=>Feature?.Unit??"Millimeters";
    }
    private sealed record Binding(ModelParameterBody[] Bodies,ModelParameterBody Primary,ModelExpressionPlan Plan,
        Dictionary<string,McObjectId> Variables,Dictionary<string,Bound> Controls,
        Dictionary<string,HashSet<string>> Affected,HashSet<Db.ObjectId> Inventory,HashSet<Db.ObjectId> OriginalBodies,Dictionary<string,string> Layers)
    {internal string[] SketchWarnings {get;set;}=[];}
    private sealed record Observed(string Handle,JsonElement State);
    private static void Need(bool ok,string code,string message){if(!ok)throw new DomainException(code,message);}
    private static object FailureObservation(ModelParameterArguments args,Binding? binding)
    {
        if(binding==null)return new{scope="no_resolved_parameter_binding"};
        object expression;
        try
        {
            using var manager=binding.Primary.Body.DbEntity.Document.GetDocumentVariablesManager(Constraint2dNetType.MCS);
            var id=binding.Variables[args.VariableId];var info=manager?.GetVariableInfo(id);string? actual=info?.Expression;
            double value=0;bool available=manager!=null&&manager.GetVariableValue(id,out value)&&double.IsFinite(value);
            expression=new{variable_id=id.ToString(),observation="actual_native_read_not_write_outcome",available=actual!=null,
                matches_requested_text=actual==null?(bool?)null:string.Equals(actual,args.Expression,StringComparison.Ordinal),
                text=actual==null?null:actual[..Math.Min(actual.Length,256)],text_length=actual?.Length,
                text_sha256=actual==null?null:Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(actual))),
                value=available?(double?)value:null,value_available=available};
        }
        catch(Exception error){expression=new{observation="native_expression_read_unavailable",error_type=error.GetType().Name};}
        var parts=new List<object>();int affectedBodies=0;
        foreach(var body in binding.Bodies.Where(body=>binding.Affected[body.Handle].Count>0))
        {
            affectedBodies++;if(parts.Count==8)continue;
            try
            {
                var pendingNodes=new List<object>();var otherNodes=new List<object>();int observed=0,notOk=0,pending=0;
                foreach(var id in ModelInventoryBudget.History(body.Body))
                {
                    var item=id.GetObjectOfType<HistoryItem>();if(item==null)continue;observed++;
                    string status=item.Status.ToString();bool suppressed=item.IsSuppressed;if(status=="NeedUpdate")pending++;
                    if(status=="Ok"&&!suppressed)continue;notOk++;
                    var selected=status=="NeedUpdate"?pendingNodes:otherNodes;
                    if(selected.Count<16)selected.Add(new{id=id.ToString(),status,suppressed});
                }
                var nodes=pendingNodes.Concat(otherNodes).Take(16).ToArray();
                parts.Add(new{body_id=body.Body.ID.ToString(),original_handle=body.Handle,history_items_observed=observed,
                    need_update_count=pending,non_ok_or_suppressed_count=notOk,nodes,omitted_node_count=notOk-nodes.Length});
            }
            catch(Exception error){parts.Add(new{body_id=body.Body.ID.ToString(),original_handle=body.Handle,error_type=error.GetType().Name});}
        }
        return new{expression,parts,omitted_affected_body_count=affectedBodies-parts.Count};
    }
    private static HashSet<Db.ObjectId> Inventory(DocumentState state,Db.Transaction tx,Action? cancel)
    {
        var db=state.Document.Database;var model=((Db.BlockTable)tx.GetObject(db.BlockTableId,Db.OpenMode.ForRead))[Db.BlockTableRecord.ModelSpace];
        var result=new HashSet<Db.ObjectId>();
        foreach(Db.ObjectId id in (Db.BlockTableRecord)tx.GetObject(model,Db.OpenMode.ForRead)){cancel?.Invoke();if(!id.IsErased&&!id.IsEffectivelyErased)result.Add(id);}
        return result;
    }
    internal static void Preflight(ModelParameterArguments args,DocumentState state,Db.Transaction tx,Action? cancel=null,bool numeric=false)
    {
        var loaded=AppDomain.CurrentDomain.GetAssemblies().Select(a=>a.GetName().Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        Need(loaded.Contains("mapimgd")&&loaded.Contains("MapiBaseTypes"),"CAPABILITY_UNAVAILABLE","MultiCAD must already be loaded.");
        Resolve(args,state,tx,cancel??(()=>{}),numeric);
    }
    private static Binding Resolve(ModelParameterArguments args,DocumentState state,Db.Transaction tx,Action cancel,bool numeric)
    {
        Need(args.Units=="Millimeters"&&(int)state.Document.Database.Insunits==4,"OUT_OF_SCOPE","Native control values require a verified Millimeters drawing; this route does not infer a unit conversion.");
        var bodies=ModelParameterBodies.Resolve(args,state,tx,cancel);var primary=bodies.Single(b=>b.Handle==args.Handle);
        var rows=primary.Before.GetProperty("variables").EnumerateArray().ToDictionary(v=>v.GetProperty("mc_id").GetString()!,StringComparer.OrdinalIgnoreCase);
        Need(rows.TryGetValue(args.VariableId,out var target),"OBJECT_NOT_FOUND","Target variable is absent from the owning MCS network.");
        Need(target.GetProperty("expression").GetString()==args.ExpectedExpression,"PRECONDITION_FAILED","Target expression changed.");
        if(numeric)
            Need(double.TryParse(args.ExpectedExpression,NumberStyles.Float,CultureInfo.InvariantCulture,out var literal)&&double.IsFinite(literal),
                "PARAMETER_HAS_EXPRESSION","A numeric request cannot replace a retained formula; use the explicit expression route.");
        var variables=rows.Values.Select(v=>new ModelExpressionVariable(v.GetProperty("mc_id").GetString()!,v.GetProperty("name").GetString()!,v.GetProperty("expression").GetString()!,
            v.GetProperty("value_available").GetBoolean()?v.GetProperty("value").GetDouble():null,
            v.GetProperty("downstream_variables").ValueKind==JsonValueKind.Array?v.GetProperty("downstream_variables").EnumerateArray().Select(x=>x.GetString()!).ToArray():throw new DomainException("VARIABLES_UNAVAILABLE","Variable dependency inventory is unknown."),
            !ModelVariableMetadata.IsUser(v.GetProperty("type").GetString()!),v.GetProperty("value_unit").GetString()=="degrees",
            NativeOwnedControl:!ModelVariableMetadata.IsUser(v.GetProperty("type").GetString()!))).ToArray();
        var plan=ModelExpressionPlanning.Prepare(variables,args.VariableId,args.Expression,args.Propagate,cancel);
        using var manager=primary.Body.DbEntity.Document.GetDocumentVariablesManager(Constraint2dNetType.MCS);
        Need(manager!=null,"CAPABILITY_UNAVAILABLE","MCS variables manager is unavailable.");
        var native=manager!.GetAllVariables();Need(native!=null,"VARIABLES_UNAVAILABLE","Current native variable inventory is unavailable.");ModelInventoryBudget.Ids(native!,cancel);
        var ids=native!.ToDictionary(id=>id.ToString(),StringComparer.OrdinalIgnoreCase);
        var controls=new Dictionary<string,Bound>(StringComparer.OrdinalIgnoreCase);
        var sketches=new Dictionary<string,NativeSketchConstraintState385.Snapshot>(StringComparer.OrdinalIgnoreCase);
        var seeds=bodies.ToDictionary(b=>b.Handle,_=>new HashSet<string>(StringComparer.OrdinalIgnoreCase),StringComparer.OrdinalIgnoreCase);
        foreach(var key in plan.AffectedIds.Concat(plan.ReferencedIds).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            cancel();Need(ids.TryGetValue(key,out var id),"VARIABLES_UNAVAILABLE","A required variable disappeared.");
            Need(manager.GetVariableValue(id,out var current)&&double.IsFinite(current),"VARIABLE_VALUE_UNAVAILABLE","A required variable has no finite actual native value.");
            // Referenced values are read-only. Only affected owners must be in
            // the explicit mutation scope and have a typed writable binding.
            if(!plan.ExpectedValues.ContainsKey(key))continue;
            var host=manager.GetVariableHostObjectId(id);
            if(ModelVariableMetadata.IsUser(rows[key].GetProperty("type").GetString()!))
            {Need(host.IsNull&&!manager.IsAngularValue(id),"CAPABILITY_UNAVAILABLE","User drivers must be document-owned non-angular scalars.");}
            else
            {
                var body=ModelParameterBodies.Owner(bodies,host,cancel);
                if(host.GetObjectOfType<PlanarSketch>()!=null)
                {
                    if(!sketches.TryGetValue(host.ToString(),out var snapshot)){snapshot=ModelSketchParameter411.Capture(args,body,host,state,tx,cancel);sketches.Add(host.ToString(),snapshot);}
                    var control=ModelSketchParameter411.Resolve(snapshot,id,cancel);control.ValidateRequested(plan.ExpectedValues[key]);
                    Need(ModelParameterSnapshot.Near(control.Value,current)&&rows[key].GetProperty("value_unit").GetString()=="Millimeters","MODEL_STATE_UNAVAILABLE","The exact internal dimension and MCS value/units disagree.");
                    controls.Add(key,new(body,Sketch:control));
                }
                else
                {
                    var control=ModelFeatureVariable.Resolve(body.Body,host,id,manager.IsAngularValue(id),allowPlacement:true,allowFinishing:true,cancel:cancel);
                    Need(ModelParameterSnapshot.Near(control.Value,current)&&(!control.IsAngle||rows[key].GetProperty("value_unit").GetString()=="degrees"),
                        "MODEL_STATE_UNAVAILABLE","The exact feature getter and native variable value/units disagree.");
                    control.ValidateRequested(plan.ExpectedValues[key]);
                    // Native formula-loss evidence is confined to its exact
                    // typed control roles; other parameters retain this route.
                    var expression=key.Equals(args.VariableId,StringComparison.OrdinalIgnoreCase)?args.Expression:rows[key].GetProperty("expression").GetString()!;
                    if(control.Role=="shell_thickness")ModelShellParameter412.ValidateExpression(expression,cancel);
                    if(control.IsPlacement)Need(ModelArithmetic.ParseStored(expression,cancel).References.Length==0,"CAPABILITY_UNAVAILABLE","Native placement-reference formulas did not survive the216 rebuild; literal component editing remains available.");
                    controls.Add(key,new(body,Feature:control));
                }
                seeds[body.Handle].Add(host.ToString());
            }
            var consumers=manager.GetDependentOn3dObjects(id);
            Need(consumers!=null,"VARIABLES_UNAVAILABLE","Affected native consumer inventory is unknown.");
            foreach(var consumer in consumers!)
            {
                cancel();var owner=bodies.Where(b=>b.Before.GetProperty("nodes").EnumerateArray().Any(n=>n.GetProperty("mc_id").GetString()!.Equals(consumer.ToString(),StringComparison.OrdinalIgnoreCase))).ToArray();
                if(owner.Length==1){seeds[owner[0].Handle].Add(consumer.ToString());continue;}
                Need(owner.Length==0&&bodies.Any(b=>ModelViewFollowers.OwnsDependent(b.Views,consumer)),"OUT_OF_SCOPE","A native consumer is outside explicit scope_handles or has ambiguous ownership.");
            }
            if(controls.TryGetValue(key,out var bound))Need(consumers!.Contains(bound.ID),"MODEL_STATE_UNAVAILABLE","The native variable does not retain its actual control owner as a consumer.");
        }
        var affected=new Dictionary<string,HashSet<string>>(StringComparer.OrdinalIgnoreCase);
        foreach(var body in bodies)
        {
            cancel();var closure=ModelDependencyGraph410.Closure(body.Before,seeds[body.Handle],cancel);
            var roots=ModelInventoryBudget.History(body.Body,cancel).Where(id=>seeds[body.Handle].Contains(id.ToString())&&id.GetObjectOfType<HistoryItem>()!=null).ToArray();
            closure.UnionWith(ModelDependencyGraph410.OwnedClosure(body.Body,roots,cancel,body.Views));affected.Add(body.Handle,closure);
        }
        var layers=bodies.ToDictionary(b=>b.Handle,b=>((Db.Entity)tx.GetObject(CadStructureWriter.Find(state.Document.Database,b.Handle),Db.OpenMode.ForRead)).Layer,StringComparer.OrdinalIgnoreCase);
        var originalBodies=bodies.Select(b=>CadStructureWriter.Find(state.Document.Database,b.Handle)).ToHashSet();
        return new(bodies,primary,plan,ids,controls,affected,Inventory(state,tx,cancel),originalBodies,layers);
    }
    private static Dictionary<string,Observed> Verify(ModelParameterArguments args,DocumentState state,Binding b,
        IReadOnlyCollection<NativeSketchEditorSolve445.Transition> transitions,Action cancel)
    {
        var result=new Dictionary<string,Observed>(StringComparer.OrdinalIgnoreCase);var representations=new HashSet<Db.ObjectId>();
        var expectedAffected=b.Bodies.ToDictionary(body=>body.Handle,body=>
        {
            var map=NativeSketchSolveExpected445.Mapping(transitions,body.Body.ID,cancel);
            return b.Affected[body.Handle].Select(id=>map.Map(id)).ToHashSet(StringComparer.OrdinalIgnoreCase);
        },StringComparer.OrdinalIgnoreCase);
        foreach(var body in b.Bodies)
        {
            cancel();string handle;
            using(var tx=state.Document.Database.TransactionManager.StartTransaction())
            {
                var representation=ModelHistoryEditGuard.CurrentRepresentation(body.Body,state,tx,cancel);representations.Add(representation);
                var solid=(Db.Solid3d)tx.GetObject(representation,Db.OpenMode.ForRead);
                Need(solid.Layer==b.Layers[body.Handle],"MODEL_STATE_UNAVAILABLE","Native body layer changed during parameter rebuild.");
                ModelFeatureOracle.Read(solid);handle=Validation.NormalizeHandle(representation.Handle.ToString());
            }
            var after=ModelParameterSnapshot.Capture(args with{Handle=handle},state,cancel);ModelParameterSnapshot.Healthy(after);
            ModelViewFollowers.Verify(body.Body,body.Views,cancel,refreshStandalone:false);
            var edited=b.Controls.Values.Where(p=>p.Body==body&&p.Feature!=null).Select(p=>p.Feature!).ToArray();
            bool sketchMotion=b.Controls.Values.Any(p=>p.Body==body&&p.Sketch!=null);
            // Only witnessed native editor ID transitions translate expected
            // identity slots. Original geometry, values and metadata remain
            // the baseline for the existing dependency/preservation checks.
            var expectedBody=NativeSketchSolveExpected445.Body(body,transitions,cancel);
            ModelDependencyGraph410.Verify(expectedBody,after,expectedAffected[body.Handle],edited,cancel,sketchMotion:sketchMotion);
            if(expectedAffected[body.Handle].Count==0)Need(ModelParameterSnapshot.Equivalent(expectedBody.GetProperty("native"),after.GetProperty("native")),"MODEL_STATE_UNAVAILABLE","An unaffected explicitly scoped body changed geometry.");
            result.Add(body.Handle,new(handle,after));
        }
        using(var tx=state.Document.Database.TransactionManager.StartTransaction())
        {
            var current=Inventory(state,tx,cancel);
            Need(current.Except(b.Inventory).All(representations.Contains)&&b.Inventory.Except(current).All(b.OriginalBodies.Contains),"MODEL_STATE_UNAVAILABLE","Unrelated top-level native entities changed membership.");
        }
        VerifyVariables(args,b,result[b.Primary.Handle].State,cancel);
        b.SketchWarnings=ModelSketchParameterGraph411.Verify(args,state,b.Controls.Values.Where(c=>c.Sketch!=null)
            .Select(c=>NativeSketchSolveExpected445.Control(c.Sketch!,transitions,cancel)),expectedAffected,
            result.ToDictionary(p=>p.Key,p=>p.Value.Handle,StringComparer.OrdinalIgnoreCase),b.Plan.ExpectedValues,b.Controls.Values.Where(c=>c.Feature!=null).Select(c=>c.Feature!),cancel,transitions);
        return result;
    }
    private static void VerifyVariables(ModelParameterArguments args,Binding b,JsonElement after,Action cancel)
    {
        var old=b.Primary.Before.GetProperty("variables").EnumerateArray().ToArray();var current=after.GetProperty("variables").EnumerateArray().ToDictionary(v=>v.GetProperty("mc_id").GetString()!,StringComparer.OrdinalIgnoreCase);
        Need(old.Length==current.Count,"MODEL_STATE_UNAVAILABLE","Native variable inventory changed.");
        var requested=ModelArithmetic.Parse(args.Expression);var oldReferences=ModelArithmetic.ParseStored(args.ExpectedExpression,cancel).References.ToHashSet(StringComparer.Ordinal);
        var newReferences=requested.References.ToHashSet(StringComparer.Ordinal);
        foreach(var prior in old)
        {
            cancel();string id=prior.GetProperty("mc_id").GetString()!;Need(current.TryGetValue(id,out var now),"VARIABLES_UNAVAILABLE","An existing native variable disappeared.");bool target=id.Equals(args.VariableId,StringComparison.OrdinalIgnoreCase);
            foreach(string field in new[]{"name","host_object_id","value_unit"})Need(ModelParameterSnapshot.Equivalent(prior.GetProperty(field),now.GetProperty(field)),"MODEL_STATE_UNAVAILABLE","Native variable name, owner or units changed.");
            Need(ModelVariableMetadata.SameTypeOrTargetReference(prior.GetProperty("type").GetString()!,now.GetProperty("type").GetString()!,target,requested.References.Length>0,
                provedSketchDriver:b.Controls.TryGetValue(id,out var proof)&&proof.Sketch!=null),"MODEL_STATE_UNAVAILABLE","Unexpected native variable type change.");
            if(target)
            {
                var observed=ModelArithmetic.ParseStored(now.GetProperty("expression").GetString()!,cancel);
                Need(observed.Canonical==requested.Canonical||requested.References.Length==0&&observed.References.Length==0&&ModelParameterSnapshot.Near(observed.Evaluate(_=>throw new InvalidOperationException()),b.Plan.ExpectedValues[id]),"MODEL_UPDATE_FAILED","The requested expression/formula was not retained.");
            }
            else Need(prior.GetProperty("expression").GetString()==now.GetProperty("expression").GetString(),"MODEL_STATE_UNAVAILABLE","A non-target native formula changed.");
            if(b.Plan.ExpectedValues.TryGetValue(id,out var expected))
            {
                Need(now.GetProperty("value_available").GetBoolean()&&ModelParameterSnapshot.Near(now.GetProperty("value").GetDouble(),expected),"MODEL_UPDATE_FAILED","A planned native variable did not reach the requested value.");
                if(b.Controls.TryGetValue(id,out var bound)&&bound.Feature is {} feature)
                {
                    var control=ModelFeatureVariable.Resolve(bound.Body.Body,feature.ID,b.Variables[id],feature.IsAngle,allowPlacement:true,allowFinishing:true,cancel:cancel);
                    Need(control.Role==feature.Role&&control.PlacementComponent==feature.PlacementComponent&&ModelParameterSnapshot.Near(control.Value,expected),"MODEL_UPDATE_FAILED","The actual owning control did not retain its planned value/binding.");
                }
            }
            else foreach(string field in new[]{"value","value_available"})Need(ModelParameterSnapshot.Equivalent(prior.GetProperty(field),now.GetProperty(field)),"MODEL_STATE_UNAVAILABLE","An unplanned native variable value changed.");
            Need(prior.GetProperty("downstream_variables").ValueKind==JsonValueKind.Array&&now.GetProperty("downstream_variables").ValueKind==JsonValueKind.Array,"VARIABLES_UNAVAILABLE","Native expression consumer inventory is unavailable.");
            var dependencies=prior.GetProperty("downstream_variables").EnumerateArray().Select(x=>x.GetString()!).ToHashSet(StringComparer.OrdinalIgnoreCase);string name=prior.GetProperty("name").GetString()!;
            if(oldReferences.Contains(name))dependencies.Remove(args.VariableId);if(newReferences.Contains(name))dependencies.Add(args.VariableId);
            Need(dependencies.SetEquals(now.GetProperty("downstream_variables").EnumerateArray().Select(x=>x.GetString()!)),"MODEL_UPDATE_FAILED","Native arithmetic references differ from the requested retained variable graph.");
            Need(prior.GetProperty("downstream_objects").ValueKind==JsonValueKind.Array&&now.GetProperty("downstream_objects").ValueKind==JsonValueKind.Array&&
                prior.GetProperty("downstream_objects").EnumerateArray().Select(x=>x.GetString()!).ToHashSet(StringComparer.OrdinalIgnoreCase).SetEquals(now.GetProperty("downstream_objects").EnumerateArray().Select(x=>x.GetString()!)),
                "MODEL_STATE_UNAVAILABLE","Native parameter consumers changed during a value/expression edit.");
        }
    }
    internal static Result Execute(ModelParameterArguments args,DocumentState state,WriteJob job,ReadContext context,JsonLog log,bool numeric=false)
    {
        Binding? binding=null;McUndoPoint? tracker=null;bool started=false;string stage="preparation";
        var pendingSolves=new List<NativeSketchEditorSolve445.Pending>();
        var transitions=new List<NativeSketchEditorSolve445.Transition>();
        void Close(){var value=tracker;tracker=null;if(value!=null)try{value.Stop();}finally{value.Dispose();}}
        Result Failure(Exception error)
        {
            if(job.CommittedResult is {} committed)return committed;Exception? cleanup=null;try{Close();}catch(Exception e){cleanup=e;}
            bool unknown=started||cleanup!=null;string cause=error is DomainException d?d.Error.code:error is OperationCanceledException?"CANCELLED":"MODEL_UPDATE_FAILED";
            log.Write(job.Operation,job.OriginalRequestId,"model_graph410_failure",transaction:stage,operationId:job.OperationId);
            return job.Error(unknown?"WRITE_OUTCOME_UNKNOWN":cause,unknown?"Native linked parameter changes remain unverified. Refresh affected bodies/history/variables before another dependent edit; no rollback is claimed.":error.Message,unknown?"unknown":"not_started",context,new{cause,stage,message=error.Message,affected_variables=binding?.Plan.AffectedIds,cleanup=cleanup?.Message,native_observation=FailureObservation(args,binding),verification_details=error is DomainException observed?observed.Error.details:null}) with{warnings=[WriteRecovery.Warning]};
        }
        try
        {
            using(var tx=state.Document.Database.TransactionManager.StartTransaction())binding=Resolve(args,state,tx,job.CheckCancellation,numeric);
            foreach(var body in binding.Bodies)ModelViewFollowers.CheckStandalone(body.Body,body.Views,job.CheckCancellation);
            tracker=new McUndoPoint(false);tracker.Start();job.CheckCancellation();started=true;stage="expression";
            using(var manager=binding.Primary.Body.DbEntity.Document.GetDocumentVariablesManager(Constraint2dNetType.MCS))Need(manager!=null&&manager.SetVariableExpression(binding.Variables[args.VariableId],args.Expression),"MODEL_UPDATE_FAILED","Native expression update was rejected.");
            stage="update_variable_consumers";McObjectManager.UpdateAll();
            // SDK variable edits use SetVariableExpression + UpdateAll
            // (3dSheetMetal:2398-2408). Solve actual sketches, then let the
            // documented command-end update finish native followers. A pending
            // status inside this command is not a reason to force every feature.
            var sketchBodies=binding.Bodies.Where(body=>binding.Controls.Values.Any(control=>control.Body==body&&control.Sketch!=null)).ToArray();
            stage="solve_affected_sketches";
            foreach(var body in sketchBodies)
            {
                var original=binding.Controls.Values.First(control=>control.Body==body&&control.Sketch!=null).Sketch!.State;
                pendingSolves.AddRange(ModelSketchParameterGraph411.Solve(body,binding.Affected[body.Handle],state,original,job.CheckCancellation));
            }
            McObjectManager.UpdateAll();
            foreach(var body in binding.Bodies)ModelViewFollowers.PrepareStandaloneRefresh(body.Body,body.Views,job.CheckCancellation);
            stage="preserve_native_body_transparency";
            foreach(var pending in pendingSolves)NativeSketchEditorSolve445.PreserveTransparency(pending,state,job.CheckCancellation);
            job.BeginCommit();Close();
            // Final MultiCAD updates occur when the native command ends, not
            // merely when its undo scope closes. Observe once on the next idle.
            return NativePostCommandVerification422.Defer(job,()=>
            {
            stage="finalize_native_editor_identity";
            foreach(var pending in pendingSolves)transitions.Add(NativeSketchEditorSolve445.Complete(pending,state,job.CheckCancellation));
            stage="verify_after_command";var observed=Verify(args,state,binding,transitions,job.CheckCancellation);
            var primary=observed[binding.Primary.Handle];EntityRef Reference(string handle)=>new(args.BridgeId,args.DocumentId,handle);
            var changes=binding.Plan.AffectedIds.Select(id=>{var old=ModelParameterSnapshot.Variable(binding.Primary.Before,id);var now=ModelParameterSnapshot.Variable(primary.State,id);binding.Controls.TryGetValue(id,out var control);
                var sketchControl=control?.Sketch is {} sourceControl?NativeSketchSolveExpected445.Control(sourceControl,transitions,job.CheckCancellation):null;
                return new ModelParameterChange(id,control?.Feature?.ID.ToString(),old.GetProperty("expression").GetString()!,now.GetProperty("expression").GetString()!,old.GetProperty("value").GetDouble(),now.GetProperty("value").GetDouble(),control?.Unit??"native_variable_units",control==null?null:Reference(observed[control.Body.Handle].Handle),owner_kind:control==null?"user":sketchControl==null?"feature":"sketch_dimension",sketch_id:sketchControl?.ID.ToString(),constraint_id:sketchControl?.Constraint.ToString());}).ToArray();
            var target=changes.Single(change=>change.variable_id.Equals(args.VariableId,StringComparison.OrdinalIgnoreCase));
            var data=new ModelParameterResult(args.OperationId,"committed",false,Reference(primary.Handle),target.feature_id,args.VariableId,args.ExpectedExpression,target.expression,target.value,target.value_unit!,primary.State.GetProperty("native").GetProperty("volume").GetDouble(),"not_guaranteed",changes,binding.Bodies.Where(body=>binding.Affected[body.Handle].Count>0).Select(body=>Reference(observed[body.Handle].Handle)).ToArray(),owner_kind:target.owner_kind,sketch_id:target.sketch_id,constraint_id:target.constraint_id);
            var result=Result.Ok(job.OriginalRequestId,data,context,WriteRecovery.Warning,"MODEL_PARAMETERS_REBUILT: Requested controls, retained formulas, native dependencies and healthy stored suppression were verified; inspect the affected geometry and a visual checkpoint for the engineering result.");
            result=ModelViewFollowers.WithWarnings(result,binding.Bodies.Select(body=>body.Views).ToArray());if(binding.SketchWarnings.Length>0)result=result with{warnings=(result.warnings??[]).Concat(binding.SketchWarnings).Distinct(StringComparer.Ordinal).ToArray()};job.RecordCommit(result);return result;
            },Failure);
        }
        catch(Exception error){return Failure(error);}
    }
}
