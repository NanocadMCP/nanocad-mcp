using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using System.Text.Json;
using Multicad;
using Multicad.DatabaseServices;
using Multicad.DatabaseServices.Constraints;
using Multicad.Mc3D;
using NanoCAD.Contracts;
using NanoCAD.Ipc;
using Db=Teigha.DatabaseServices;

namespace NanoCADBridge;

// SDK ConstraintsSamples.DeleteAllConstraints demonstrates DeleteConstraint.
// Here its manager and selected IDs belong to one actual retained part sketch;
// no active-sheet/default-network deletion or editor-generated ID is used.
internal static class NativeSketchConstraintRemoval430
{
    sealed record Variable(string Id,string Name,string Expression,string Type,string? Host);
    sealed record Binding(ModelSketchConstraintRemoval430 Edit,Mc3dSolid Body,PlanarSketch Sketch,Db.ObjectId Representation,
        NativeSketchMembers385.Snapshot Inputs,JsonElement Relations,McObjectId[] Selected,Variable[] Variables,HashSet<string> Retirable,
        JsonElement Links,McObjectId LastItem,string NetworkScope);
    static string Key(McObjectId id)=>id.ToString();
    static void Need([DoesNotReturnIf(false)]bool ok,string code,string message,object? details=null)
    {if(!ok)throw new DomainException(code,message,false,details);}
    internal static bool Handles(ModifyArguments args)=>args.Operations.Any(op=>op is ModelSketchConstraintRemoval430);
    static Variable[] Variables(McDocument owner,Action cancel)
    {
        using var manager=owner.GetDocumentVariablesManager(Constraint2dNetType.MCS);
        Need(manager!=null&&manager.DocumentId==owner.ID&&manager.ConstraintsNetworkType==Constraint2dNetType.MCS,"VARIABLES_UNAVAILABLE","The selected sketch's owning variable network is unavailable.");
        var ids=manager.GetAllVariables();Need(ids!=null,"VARIABLES_UNAVAILABLE","The actual owning variable inventory is unavailable.");
        var result=new List<Variable>();var bytes=new ModelInventoryBudget(cancel);
        foreach(var id in ids)
        {
            cancel();var info=manager.GetVariableInfo(id);Need(info!=null,"VARIABLES_UNAVAILABLE","An existing variable is unreadable.");
            var host=manager.GetVariableHostObjectId(id);var row=new Variable(Key(id),info.Name,info.Expression,info.Type.ToString(),host.IsNull?null:Key(host));
            bytes.Add(row);result.Add(row);
        }
        return result.OrderBy(v=>v.Id,StringComparer.OrdinalIgnoreCase).ToArray();
    }
    static JsonElement Links(Mc3dSolid body,Action cancel)
    {
        var rows=new List<object>();var budget=new ModelInventoryBudget(cancel);
        foreach(var id in ModelInventoryBudget.History(body,cancel).OrderBy(Key,StringComparer.OrdinalIgnoreCase))
        {
            cancel();var item=id.GetObjectOfType<HistoryItem>();if(item==null)continue;
            var profiles=new List<string>();string? sketch=null,support=null;
            // A HistoryItem getter returns the base managed wrapper. Query the
            // exact native ID for each public subtype before reading its links.
            var extrusion=id.GetObjectOfType<ExtrudeFeature>();if(extrusion!=null)profiles.Add(Key(extrusion.ProfileID));
            var revolution=id.GetObjectOfType<RevolveFeature>();if(revolution!=null)profiles.Add(Key(revolution.ProfileID));
            var sweep=id.GetObjectOfType<ExtrudeAlongPathFeature>();if(sweep!=null)profiles.Add(Key(sweep.ProfileID));
            var loft=id.GetObjectOfType<LoftFeature>();if(loft!=null)
            {profiles.Add(Key(loft.ProfileID));for(int index=0;index<loft.SectionsCount;index++){var section=loft.GetSection(index);if(section!=null)profiles.Add(Key(section.ID));}}
            var profile=id.GetObjectOfType<SketchProfile>();if(profile!=null)sketch=Key(profile.ParentSketchID);
            var plane=id.GetObjectOfType<PlanarSketch>();if(plane!=null)support=plane.PlanarEntityID.IsNull?null:Key(plane.PlanarEntityID);
            var row=new{id=Key(id),runtime_type=id.QueryObjectType()?.FullName,host=item.HostSolidID.IsNull?null:Key(item.HostSolidID),suppressed=item.IsSuppressed,beyond_end=item.IsBeyondEndOfPart,profiles,sketch,support};budget.Add(row);rows.Add(row);
        }
        return Wire.Element(rows);
    }
    static void VerifyInputs(NativeSketchMembers385.Snapshot before,NativeSketchMembers385.Snapshot after,Action cancel)
    {
        Need(before.Sketch.ID==after.Sketch.ID&&ModelParameterSnapshot.Equivalent(Wire.Element(before.Plane),Wire.Element(after.Plane)),
            "SKETCH_GEOMETRY_CHANGED","Relation deletion changed the selected native sketch or its actual frame.");
        Need(ModelProfileElementGeometry358.SameBoundary(before.Members.Select(member=>member.Curve).ToArray(),after.Members.Select(member=>member.Curve).ToArray(),cancel),
            "SKETCH_GEOMETRY_CHANGED","Relation deletion changed the retained boundary in native sketch coordinates.");
        var current=after.Members.ToDictionary(member=>member.Id);
        Need(current.Count==before.Members.Length,"SKETCH_BINDING_CHANGED","Relation deletion changed the actual native input inventory.");
        foreach(var prior in before.Members)
        {
            cancel();Need(current.TryGetValue(prior.Id,out var actual)&&actual.ObjectId==prior.ObjectId&&actual.Segment==prior.Segment&&
                actual.NativeType==prior.NativeType&&actual.Owned==prior.Owned&&actual.ReferenceOnly==prior.ReferenceOnly&&
                ModelParameterSnapshot.Equivalent(prior.Geometry,actual.Geometry),"SKETCH_BINDING_CHANGED",
                "Relation deletion replaced a native member, changed its ownership/reference role, or moved its actual WCS geometry.",new{member_id=Key(prior.Id)});
        }
        JsonElement Sources(NativeSketchMembers385.Snapshot snapshot)=>Wire.Element(snapshot.Stored.GetProperty("objects").EnumerateArray()
            .Where(row=>row.TryGetProperty("source_object_id",out _)).Select(row=>new{id=row.GetProperty("id").GetString(),source=row.GetProperty("source_object_id").GetString()})
            .OrderBy(row=>row.id,StringComparer.OrdinalIgnoreCase).ToArray());
        Need(ModelParameterSnapshot.Equivalent(before.Stored.GetProperty("projection_ids"),after.Stored.GetProperty("projection_ids"))&&
            ModelParameterSnapshot.Equivalent(Sources(before),Sources(after)),"PROJECTION_REFERENCE_CHANGED",
            "Relation deletion changed an actual retained projection or its source identity.");
        NativeSketchRelations410.ImmutableReferences(before,after,cancel);
    }
    // Removing the final relation can leave no linked scope. Only this exact
    // all-selected/actually-empty observation may admit its missing linkage;
    // all ordinary managers and every nonempty/partly-retained network remain
    // subject to the existing strict selected-sketch scope checks.
    static JsonElement FinalRelations(Binding before,NativeSketchMembers385.Snapshot inputs,Action cancel)
    {
        var sketch=before.Sketch;var owner=sketch.DbEntity.Document;
        Need(!sketch.IsInEditMode&&owner!=null&&Key(owner.ID)==before.Inputs.Stored.GetProperty("owner_document_id").GetString()&&
            ModelNativeObjectResolver415.Sketch(sketch.ID,owner,cancel).ID==sketch.ID,"CONSTRAINT_SCOPE_MISMATCH",
            "The exact persistent sketch and owner changed before final relation observation.");
        using var plane=sketch.Plane;
        using var manager=owner.Get2dConstraintsManager(Constraint2dNetType.Auto,sketch.ID,plane);
        Need(manager!=null&&manager.DocumentId==owner.ID&&manager.ConstraintsNetworkType.ToString()==before.Relations.GetProperty("network").GetString(),
            "CONSTRAINT_SCOPE_MISMATCH","The final native relation manager is not the selected sketch's actual owner/network.");
        var ids=manager.GetAllConstraints();Need(ids!=null,"CONSTRAINTS_UNAVAILABLE","The final selected-sketch relation inventory is unavailable, not observed empty.");
        ModelInventoryBudget.Ids(ids,cancel);
        bool allSelected=before.Relations.GetProperty("constraints").GetArrayLength()==before.Selected.Length;
        if(ids.Count==0&&allSelected)
        {
            // The acquisition above explicitly selects this exact native
            // sketch. A different nonnull linkage is never an empty result.
            Need(manager.PlanarSketchId==sketch.ID||manager.PlanarSketchId.IsNull&&manager.ScopeId.IsNull,
                "CONSTRAINT_SCOPE_MISMATCH","The empty native network is linked to another sketch or an unexplained scope.");
            return NativeSketchConstraintState385.Relations(manager,cancel,inventory:ids);
        }
        NativeSketchMembers385.RequireManagerScope(manager,sketch,owner);
        Need(Key(manager.ScopeId)==before.NetworkScope,"CONSTRAINT_SCOPE_MISMATCH","The retained native relation network changed identity.");
        return NativeSketchRelations410.Capture(manager,inputs,cancel,ids).Relations;
    }
    static Binding Resolve(ModifyArguments args,DocumentState state,Db.Transaction tx,Action cancel)
    {
        Need(args.Operations.Length==1&&args.Operations[0] is ModelSketchConstraintRemoval430,"INVALID_OPERATION_ORDER","Select one existing native sketch and its relation IDs in one operation.");
        var edit=(ModelSketchConstraintRemoval430)args.Operations[0];var drawing=ModelProfileDiscovery348.DrawingDocument(state);
        var raw=CadStructureWriter.Find(state.Document.Database,edit.Handles.Single());var solid=tx.GetObject(raw,Db.OpenMode.ForRead) as Db.Solid3d;
        Need(args.TargetSpace=="model"&&solid!=null&&solid.GetType()==typeof(Db.Solid3d)&&solid.OwnerId==CadWriteSpace.Resolve(state.Document.Database,tx,"model"),"OUT_OF_SCOPE","Select the current top-level native part in Model Space.");
        var layer=(Db.LayerTableRecord)tx.GetObject(solid.LayerId,Db.OpenMode.ForRead);Need(!layer.IsLocked&&!layer.IsDependent,"LAYER_UNAVAILABLE","The selected part's layer is locked or externally owned.");
        var id=McObjectId.FromHandle(raw.Handle.Value,drawing.ID);var body=id.GetObjectOfType<Mc3dSolid>();
        Need(body!=null&&body.ID==id&&ModelHistoryEditGuard.CurrentRepresentation(body,state,tx,cancel)==raw,"OBJECT_IDENTITY_MISMATCH","The selected native part is not the actual current representation.");
        var owner=ModelProfileDiscovery348.OwnerDocument(body,state);var sketch=ModelNativeObjectResolver415.Sketch(new McObjectId(Guid.Parse(edit.SketchId)),owner,cancel);
        Need(!sketch.IsInEditMode,"HOST_BUSY","Finish editing the sketch before modifying its persistent relations.");
        var contents=ModelInventoryBudget.History(body,cancel);
        bool used=contents.Contains(sketch.ID)||contents.Select(x=>x.GetObjectOfType<SketchProfile>()).Any(p=>p!=null&&p.ParentSketchID==sketch.ID&&p.HostSolidID==body.ID);
        Need(used&&(sketch.HostSolidID.IsNull||sketch.HostSolidID==body.ID),"SKETCH_OWNERSHIP_MISMATCH","The selected sketch is not acquired or consumed by this part.");
        var inputs=NativeSketchMembers385.Capture(sketch,state,tx,cancel);
        Need(inputs.Token.Equals(edit.ExpectedBindingToken,StringComparison.Ordinal),"PRECONDITION_FAILED","Refresh the selected sketch's native_inputs.binding_token.");
        using var manager=NativeSketchMembers385.Manager(sketch);var ids=manager.GetAllConstraints();Need(ids!=null,"CONSTRAINTS_UNAVAILABLE","The current selected-sketch relation inventory is unavailable.");
        var selected=edit.ConstraintIds.Select(text=>new McObjectId(Guid.Parse(text))).ToArray();
        Need(selected.All(ids.Contains),"PRECONDITION_FAILED","One selected relation is not in this sketch's current native network.");
        var relations=NativeSketchRelations410.Capture(manager,inputs,cancel,ids).Relations;
        var variables=Variables(owner,cancel);var controlVariables=new HashSet<McObjectId>();var selectedHelpers=selected.ToHashSet();
        foreach(var relation in selected)
        {
            cancel();using var info=manager.GetConstraintInfo(relation);Need(info!=null,"CONSTRAINTS_UNAVAILABLE","A selected native relation is unreadable.");
            if(info.IsDimensional&&!info.VarId.IsNull)controlVariables.Add(info.VarId);
            if(!info.DimId.IsNull)selectedHelpers.Add(info.DimId);
            if(!info.VarId.IsNull)selectedHelpers.Add(info.VarId);
        }
        var retirable=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if(controlVariables.Count>0)
        {
            using var values=owner.GetDocumentVariablesManager(Constraint2dNetType.MCS);Need(values!=null&&values.DocumentId==owner.ID&&values.ConstraintsNetworkType==Constraint2dNetType.MCS,"VARIABLES_UNAVAILABLE","The selected dimensional controls need their actual variable ownership.");
            var selectedIds=selected.Select(Key).ToHashSet(StringComparer.OrdinalIgnoreCase);
            var retainedVariables=relations.GetProperty("constraints").EnumerateArray().Where(row=>!selectedIds.Contains(row.GetProperty("id").GetString()!))
                .Select(row=>row.GetProperty("variable").GetString()).Where(id=>id!=null).ToHashSet(StringComparer.OrdinalIgnoreCase);
            var exclusiveOwners=selectedHelpers.Append(sketch.ID).ToHashSet();
            var candidates=new HashSet<McObjectId>();
            foreach(var variable in controlVariables)
            {
                cancel();var host=values.GetVariableHostObjectId(variable);var info=values.GetVariableInfo(variable);Need(info!=null,"VARIABLES_UNAVAILABLE","A selected control's actual variable is unreadable.");
                Need(!retainedVariables.Contains(Key(variable)),"CONSTRAINT_VARIABLE_HAS_DEPENDENTS","An unselected native relation still uses this dimensional variable; remove only an exclusive selected control.",new{variable_id=Key(variable)});
                // A named user input with no host is preserved, never retired
                // merely because one relation referred to it.
                var flags=info.Type.ToString().Split(',',StringSplitOptions.TrimEntries);
                bool nativeControl=flags.Any(flag=>flag is "Dim2DConstraint" or "Model3D")&&flags.All(flag=>flag is "Dim2DConstraint" or "Model3D" or "Reference");
                if(host.IsNull||!exclusiveOwners.Contains(host)||!nativeControl)continue;
                candidates.Add(variable);
            }
            foreach(var variable in candidates)
            {
                cancel();
                var dependentVariables=values.GetDependentOnVariables(variable);var dependentObjects=values.GetDependentOn3dObjects(variable);
                Need(dependentVariables!=null&&dependentObjects!=null,"VARIABLES_UNAVAILABLE","The selected control variable's actual consumers are unavailable.");
                Need(dependentVariables.All(candidates.Contains)&&dependentObjects.All(exclusiveOwners.Contains),"CONSTRAINT_VARIABLE_HAS_DEPENDENTS","A selected control variable has another retained formula or model consumer; resolve that dependency before removing the control.",new{variable_id=Key(variable)});
                retirable.Add(Key(variable));
            }
        }
        return new(edit,body,sketch,raw,inputs,relations,selected,variables,retirable,Links(body,cancel),body.LastPartItemID,Key(manager.ScopeId));
    }
    internal static void Preflight(ModifyArguments args,DocumentState state,Db.Transaction tx,Action? cancel=null)=>Resolve(args,state,tx,cancel??(()=>{}));
    [MethodImpl(MethodImplOptions.NoInlining)]
    internal static Result Execute(ModifyArguments args,DocumentState state,WriteJob job,ReadContext context,JsonLog log)
    {
        Binding? before=null;McUndoPoint? tracker=null;bool started=false,rebuild=false;string stage="preparation";
        void Close(){var value=tracker;tracker=null;if(value==null)return;try{value.Stop();}finally{value.Dispose();}}
        Result Failure(Exception error)
        {
            if(job.CommittedResult is{} committed)return committed;Exception? cleanup=null;try{Close();}catch(Exception e){cleanup=e;}
            string code=error is DomainException d?d.Error.code:error is OperationCanceledException?"CANCELLED":"SKETCH_CONSTRAINT_DELETE_FAILED";bool unknown=started||cleanup!=null;
            return job.Error(unknown?"WRITE_OUTCOME_UNKNOWN":code,unknown?"Selected sketch relation deletion needs actual-state inspection; no rollback is claimed.":error.Message,unknown?"unknown":"not_started",context,
                new{cause=code,stage,message=error.Message,cleanup=cleanup?.Message,sketch_id=before?.Edit.SketchId,constraint_ids=before?.Edit.ConstraintIds,verification_details=error is DomainException observedFailure?observedFailure.Error.details:null}) with{warnings=[WriteRecovery.Warning]};
        }
        try
        {
            using(var tx=state.Document.Database.TransactionManager.StartTransaction())before=Resolve(args,state,tx,job.CheckCancellation);
            tracker=new McUndoPoint(false);tracker.Start();
            using(var manager=NativeSketchMembers385.Manager(before.Sketch))
            {
                Need(Key(manager.ScopeId)==before.NetworkScope,"PRECONDITION_FAILED","The selected native sketch network changed before deletion.");
                foreach(var id in before.Selected)
                {
                    job.CheckCancellation();stage="native_delete_constraint";started=true;
                    log.Write(job.Operation,job.OriginalRequestId,"native_sketch_constraint_delete",transaction:Key(id),operationId:job.OperationId);
                    bool accepted=manager.DeleteConstraint(id);var remaining=manager.GetAllConstraints();
                    Need(remaining!=null&&!remaining.Contains(id),"SKETCH_CONSTRAINT_DELETE_FAILED","The native sketch manager retained a requested relation.",new{constraint_id=Key(id),native_accepted=accepted});
                }
            }
            stage="native_update";before.Sketch.DbEntity.Update();McObjectManager.UpdateAll();job.CheckCancellation();
            // Removing a relation can release a conflict; an unrelated existing
            // failure does not turn actual deletion into an unavailable command.
            stage="native_rebuild";rebuild=NativeModelHistory421.Rebuild(before.Body);McObjectManager.UpdateAll();
            job.BeginCommit();Close();stage="post_command_verification";
            return NativePostCommandVerification422.Defer(job,()=>
            {
                Db.ObjectId raw;NativeSketchMembers385.Snapshot inputs;JsonElement relations;
                using(var tx=state.Document.Database.TransactionManager.StartTransaction())
                {
                    raw=ModelHistoryEditGuard.CurrentRepresentation(before.Body,state,tx,job.CheckCancellation);
                    CadSolidEditor.CheckGeometry((Db.Solid3d)tx.GetObject(raw,Db.OpenMode.ForRead),job.CheckCancellation);
                    inputs=NativeSketchMembers385.Capture(before.Sketch,state,tx,job.CheckCancellation);
                    relations=FinalRelations(before,inputs,job.CheckCancellation);
                }
                var selected=before.Edit.ConstraintIds.ToHashSet(StringComparer.OrdinalIgnoreCase);
                Need(!relations.GetProperty("constraints").EnumerateArray().Any(row=>selected.Contains(row.GetProperty("id").GetString()!)),"SKETCH_CONSTRAINT_DELETE_FAILED","A selected native relation remains after command completion.");
                var baseline=NativeSketchRelations410.Capture(NativeSketchRelations410.Without(before.Relations,selected),before.Inputs,job.CheckCancellation);
                NativeSketchRelations410.Retained(baseline,relations,inputs,job.CheckCancellation);
                VerifyInputs(before.Inputs,inputs,job.CheckCancellation);
                Need(before.Body.LastPartItemID==before.LastItem&&Links(before.Body,job.CheckCancellation).GetRawText()==before.Links.GetRawText(),"MODEL_STATE_UNAVAILABLE","Relation deletion changed retained profiles, features, supports or suppression choices.");
                var afterVariables=Variables(before.Sketch.DbEntity.Document,job.CheckCancellation);var byId=afterVariables.ToDictionary(v=>v.Id,StringComparer.OrdinalIgnoreCase);
                foreach(var variable in before.Variables.Where(v=>!before.Retirable.Contains(v.Id)))
                    Need(byId.TryGetValue(variable.Id,out var actual)&&actual==variable,"MODEL_STATE_UNAVAILABLE","Relation deletion changed a retained variable, formula, name or owner.",new{variable_id=variable.Id});
                var unresolved=ModelInventoryBudget.History(before.Body,job.CheckCancellation).Where(id=>{var item=id.GetObjectOfType<HistoryItem>();return item!=null&&!item.IsSuppressed&&item.Status.ToString()!="Ok";}).Select(Key).ToArray();
                bool healthy=before.Body.Status.ToString()=="Ok"&&unresolved.Length==0;
                var oldRef=new EntityRef(args.BridgeId,args.DocumentId,before.Edit.Handles.Single());var current=new EntityRef(args.BridgeId,args.DocumentId,Validation.NormalizeHandle(raw.Handle.ToString()));bool replaced=oldRef.handle!=current.handle;
                var change=new ModelSketchConstraintDeletion430(Key(before.Sketch.ID),before.Edit.ConstraintIds,before.Retirable.Where(id=>!byId.ContainsKey(id)).ToArray(),before.Retirable.Where(byId.ContainsKey).ToArray(),rebuild,healthy,unresolved);
                var data=new ModelSketchConstraintRemovalOutput430(args.OperationId,"committed",false,[current],replaced?[oldRef]:[],replaced?[current]:[],[],"not_guaranteed",[change]);
                var warnings=new List<string>{WriteRecovery.Warning,"NATIVE_SKETCH_RELATIONS_REMOVED: Selected relations are absent; retained sketch geometry, other relations, profile/feature/support identities and independent formulas were observed. Freed geometry can be changed by a subsequent supported edit."};
                if(!healthy)warnings.Add("MODEL_NEEDS_CORRECTION: Selected relations were removed, but remaining history is not fully solved; inspect reported unresolved IDs before dependent changes.");
                var result=Result.Ok(job.OriginalRequestId,data,context,warnings.ToArray());Need(JsonSerializer.SerializeToUtf8Bytes(result,Wire.Json).Length<=Wire.PayloadBudget,"RESPONSE_LIMIT_EXCEEDED","The actual deletion receipt exceeds response bytes.");job.RecordCommit(result);return result;
            },Failure);
        }
        catch(Exception error){return Failure(error);}
    }
}
