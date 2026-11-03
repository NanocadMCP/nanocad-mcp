using System.Runtime.CompilerServices;
using Multicad;
using Multicad.DatabaseServices;
using Multicad.Mc3D;
using NanoCAD.Contracts;
using NanoCAD.Ipc;
using Teigha.DatabaseServices;

namespace NanoCADBridge;

// A native history feature, not a collection of independently moved copies.
// Directions consume actual native construction/curve/edge bindings. A fixed
// selector explicitly creates a native WorkAxis, never independent copies.
internal static class ModelPatternWriter
{
    internal static bool Handles(ModifyArguments args)=>args.Operations.Any(e=>e is ModelPatternSpec);
    private static void Need([System.Diagnostics.CodeAnalysis.DoesNotReturnIf(false)] bool condition,string code,string message)
    {if(!condition)throw new DomainException(code,message);}
    private sealed record Binding(Mc3dSolid Body,ObjectId Source,string Layer,McObjectId[] Contents,
        McObjectId[] SourceFeatures,McObjectId Predecessor,McObjectId DirectionX,McObjectId DirectionY,
        McObjectId PatternId,McObjectId[] Dependencies,ModelFeatureOracle.Metrics Before,
        HashSet<ObjectId> Inventory,ModelViewFollowers.Snapshot Views,System.Text.Json.JsonElement? ProtectedHistory,
        System.Text.Json.JsonElement? RetainedFeatures,McObjectId[] ControlIds,Dictionary<McObjectId,string> RetainedExpressions,
        McObjectId[] OriginalSources,bool Retarget,HashSet<(int X,int Y)>? Suppression,
        ModelPatternReferences439.Selection? X,ModelPatternReferences439.Selection? Y,
        ModelPatternDirectionBinding439[] OldDirections,bool RetargetDirections,int OriginalCountX,int OriginalCountY,
        McObjectId[] NativeSourcesBefore,HashSet<McObjectId> ReferenceScope,Dictionary<McObjectId,System.Text.Json.JsonElement> ReferenceGeometry,
        McObjectId[] OrderedBefore);
    private static ObjectId Model(Database db,Transaction tx)=>((BlockTable)tx.GetObject(db.BlockTableId,OpenMode.ForRead))[BlockTableRecord.ModelSpace];
    private static HashSet<ObjectId> TopLevel(DocumentState state)
    {
        using var tx=state.Document.Database.TransactionManager.StartTransaction();
        var ids=((BlockTableRecord)tx.GetObject(Model(state.Document.Database,tx),OpenMode.ForRead)).Cast<ObjectId>().ToHashSet();
        return ids;
    }
    private static bool Near(double a,double b)=>ModelParameterSnapshot.Near(a,b);
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static Binding Resolve(ModelPatternSpec edit,ModifyArguments args,DocumentState state,Transaction tx,Action? cancel=null)
    {
        Need(args.Operations.Length==1,"INVALID_ARGUMENT","Native pattern must be the sole operation.");
        bool add=ModelPatternRequest439.Add(edit),circular=ModelPatternRequest439.Circular(edit);
        var loaded=AppDomain.CurrentDomain.GetAssemblies().Select(a=>a.GetName().Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        Need(loaded.Contains("mapimgd")&&loaded.Contains("MapiBaseTypes"),"CAPABILITY_UNAVAILABLE","MultiCAD must already be loaded.");
        var db=state.Document.Database;var model=Model(db,tx);
        Need(db.CurrentSpaceId==model,"OUT_OF_SCOPE","Activate Model Space.");
        var active=McDocument.ActiveDocument;
        Need(state.Document.IsNamedDrawing&&active!=null&&!string.IsNullOrWhiteSpace(active.Path)&&
            string.Equals(Path.GetFullPath(active.Path),Path.GetFullPath(state.Document.Name),StringComparison.OrdinalIgnoreCase),
            "DOCUMENT_CONTEXT_MISMATCH","Native pattern requires this exact named document.");
        var source=CadStructureWriter.Find(db,edit.Handles[0]);
        var solid=tx.GetObject(source,OpenMode.ForRead) as Solid3d;
        Need(solid!=null&&solid.GetType()==typeof(Solid3d)&&solid.OwnerId==model&&!solid.IsNull,
            "UNSUPPORTED_ENTITY_TYPE","Select one top-level native Mc3dSolid representation.");
        var layer=(LayerTableRecord)tx.GetObject(solid!.LayerId,OpenMode.ForRead);
        Need(!layer.IsLocked&&!layer.IsFrozen&&!layer.IsOff&&!layer.IsDependent,"LAYER_UNAVAILABLE","Body layer is unavailable.");
        var bodyId=McObjectId.FromHandle(source.Handle.Value,active!.ID);var body=bodyId.GetObjectOfType<Mc3dSolid>();
        Need(body!=null&&body.ID==bodyId&&body.LastPartItemID.ToString().Equals(edit.ExpectedLastItemId,StringComparison.OrdinalIgnoreCase),
            "PRECONDITION_FAILED","Body identity or last history item changed.");
        var contents=ModelInventoryBudget.History(body!,cancel);
        Need(contents.Count>0,"HISTORY_UNAVAILABLE","Pattern needs a healthy existing history.");
        var ids=contents!.ToArray();
        var views=ModelViewFollowers.Resolve(body,ids,cancel,includeStandalone:true);
        var external=ProjectedHistorySources.Read(body,ids,state,tx,cancel);ProjectedHistorySources.Healthy(ids,external);
        McObjectId predecessor=McObjectId.Null,dx=McObjectId.Null,dy=McObjectId.Null,patternId=McObjectId.Null;
        McObjectId[] sourceFeatures=[],controlIds=[];var retainedExpressions=new Dictionary<McObjectId,string>();
        McObjectId[] dependencies=[],originalSources=[];bool retarget=false;HashSet<(int X,int Y)>? suppression=null;
        ModelPatternDirectionBinding439[] oldDirections=[];McObjectId[] nativeSourcesBefore=[];
        ModelPatternReferences439.Selection? selectedX=null,selectedY=null;int originalX=edit.CountX,originalY=edit.CountY;
        var referenceScope=new HashSet<McObjectId>();var referenceGeometry=new Dictionary<McObjectId,System.Text.Json.JsonElement>();
        bool retargetDirections=!add&&(edit.DirectionX!=null||edit.DirectionY!=null);
        if(add)
        {
            ModelViewFollowers.PrepareInsertion(views,true);
            predecessor=views.LastModelItem;
            sourceFeatures=ModelPatternSources416.Resolve(edit.SourceFeatureIds??[edit.SourceFeatureId!],body,ids,predecessor,McObjectId.Null,cancel);
        }
        else
        {
            patternId=ids.SingleOrDefault(id=>id.ToString().Equals(edit.FeatureId,StringComparison.OrdinalIgnoreCase));
            var pattern=ModelPatternRequest439.Pattern(patternId,circular);
            Need(!patternId.IsNull&&pattern!=null&&pattern.HostSolidID==body.ID&&
                !pattern.IsSuppressed&&pattern.Status.ToString()=="Ok"&&pattern.SourceItemsCount>0&&pattern.HasSubFeatureItems,
                "OUT_OF_SCOPE","Select one healthy native subfeature pattern of this part.");
            // Native source-item IDs can identify internal copies. Original
            // group inputs are explicit preconditions; predecessor is ordering.
            predecessor=pattern!.OrderObjBefore;
            if(edit.ExpectedSourceFeatureIds==null&&!circular)
            {
                Need(pattern.SourceItemsCount==1&&ids.Contains(predecessor)&&predecessor.GetObjectOfType<ExtrudeFeature>()!=null,
                    "OUT_OF_SCOPE","The legacy count/step editor requires its one original extrusion; supply expected_source_feature_ids for a source-group pattern.");
                sourceFeatures=[predecessor];
            }
            else if(edit.ExpectedSourceFeatureIds!=null)sourceFeatures=ModelPatternSources416.Resolve(edit.ExpectedSourceFeatureIds,body,ids,predecessor,patternId,cancel);
            if(sourceFeatures.Length>0)ModelPatternSources416.Verify(pattern,body,sourceFeatures,predecessor,cancel,edit.ExpectedSourceFeatureIds==null?null:ids);
            nativeSourcesBefore=ModelPatternRequest439.NativeSources(pattern,cancel);
            originalSources=sourceFeatures;
            if(edit.SourceFeatureIds!=null)
            {
                sourceFeatures=ModelPatternSources416.Resolve(edit.SourceFeatureIds,body,ids,predecessor,patternId,cancel);
                retarget=!sourceFeatures.SequenceEqual(originalSources);
                if(retarget)ModelDependencyGraph410.OwnedClosure(body,[patternId],cancel??(()=>{}),views);
            }
            dependencies=ModelPatternSources416.Dependencies(pattern,cancel);
            suppression=ModelPatternEditState430.Suppression(pattern,cancel);
            var values=ModelPatternRequest439.Values(pattern);originalX=values.X;originalY=values.Y;
            Need((edit.ExpectedCountX==null||values.X==edit.ExpectedCountX)&&(edit.ExpectedCountY==null||values.Y==edit.ExpectedCountY)&&
                 (edit.ExpectedStepX==null||Near(values.StepX,edit.ExpectedStepX.Value))&&(edit.ExpectedStepY==null||Near(values.StepY,edit.ExpectedStepY.Value)),
                 "PRECONDITION_FAILED","Pattern count or step differs from the expected current values.");
            oldDirections=Enumerable.Range(0,circular?1:2).Select(dimension=>NativePatternObservation439.Read(pattern,dimension)).ToArray();
            Need(oldDirections.All(direction=>direction.spacing_mode=="step"),"OUT_OF_SCOPE","This step editor retains native step mode; a native range/fill pattern must not be interpreted as a step pattern.");
            if(edit.ExpectedDirectionXId!=null)Need(oldDirections[0].native_id.Equals(edit.ExpectedDirectionXId,StringComparison.OrdinalIgnoreCase),"PRECONDITION_FAILED","The current native first axis/direction ID differs from expected.");
            if(edit.ExpectedDirectionYId!=null)Need(oldDirections[1].native_id.Equals(edit.ExpectedDirectionYId,StringComparison.OrdinalIgnoreCase),"PRECONDITION_FAILED","The current native second direction ID differs from expected.");
            foreach(var direction in oldDirections)
            {
                var reference=ModelPatternReferences439.Reference(direction);
                if(!reference.ID.IsEdge){referenceScope.Add(reference.ID);referenceGeometry[reference.ID]=ModelPatternReferences439.CaptureTarget(reference,cancel??(()=>{}));}
            }
            if(retargetDirections)ModelDependencyGraph410.OwnedClosure(body,[patternId],cancel??(()=>{}),views);
            var controls=ModelPatternRequest439.Controls(pattern,edit);
            controlIds=controls.Select(control=>control.Id).ToArray();
            Need(controlIds.All(id=>!id.IsNull)&&controlIds.Distinct().Count()==controlIds.Length,"MODEL_DRIVER_CHANGED","The pattern needs distinct actual native count/spacing drivers.");
            foreach(var control in controls)
            {
                ModelNumericDriverEditor.Validate(pattern,control.Id,control.Value,edit.ReplaceDriverFormulas);
                ModelNumericDriverEditor.ValidateChangeScope(pattern,control.Id,control.Value,body,ids,views,cancel);
                if(ModelNumericDriverEditor.RetainedExpression(pattern,control.Id,control.Value) is {} expression)retainedExpressions.Add(control.Id,expression);
            }
        }
        ModelPatternReferences439.Selection? Select(System.Text.Json.JsonElement? selector,string? legacy)
        {
            if(selector==null&&legacy!=null)selector=Wire.Element(new{kind="line",handle=legacy});
            if(selector==null)return null;
            var selection=ModelPatternReferences439.Resolve(selector.Value,body,patternId,source,state,tx,args.BridgeId,circular,cancel);
            if(!selection.Id.IsNull&&!selection.Id.IsEdge)
            {
                referenceScope.Add(selection.Id);
                referenceGeometry[selection.Id]=ModelPatternReferences439.CaptureTarget(new McGeomParam{ID=selection.Id},cancel??(()=>{}));
            }
            return selection;
        }
        selectedX=Select(edit.DirectionX,edit.DirectionXHandle);if(!circular)selectedY=Select(edit.DirectionY,edit.DirectionYHandle);
        McObjectId[] orderedBefore=[];
        if(retargetDirections)
        {
            cancel?.Invoke();orderedBefore=(body.GetPartContents(true)??throw new DomainException("MODEL_ORDER_UNAVAILABLE","The native ordered part composition is unavailable before axis retarget.")).ToArray();
            ModelInventoryBudget.Ids(orderedBefore,cancel);
            Need(orderedBefore.Distinct().Count()==orderedBefore.Length&&orderedBefore.ToHashSet().SetEquals(ids),"MODEL_ORDER_UNAVAILABLE","The ordered and ordinary native inventories disagree before axis retarget.");
        }
        return new(body,source,solid.Layer,ids,sourceFeatures,predecessor,dx,dy,patternId,dependencies,
            ModelFeatureOracle.Read(solid),TopLevel(state),views,patternId.IsNull?null:ModelPatternEditState430.History(args,state,patternId,retarget||retargetDirections,cancel:cancel,referenceIds:referenceScope),
            patternId.IsNull?ModelHistoryEditGuard.FeatureNodes(body,state,ids.Where(id=>id.GetObjectOfType<FeatureEntity>()!=null).ToArray(),cancel):null,
            controlIds,retainedExpressions,originalSources,retarget,suppression,selectedX,selectedY,oldDirections,retargetDirections,originalX,originalY,nativeSourcesBefore,referenceScope,referenceGeometry,orderedBefore);
    }
    internal static void Preflight(ModifyArguments args,DocumentState state,Transaction tx,Action? cancel=null)=>Resolve((ModelPatternSpec)args.Operations.Single(),args,state,tx,cancel);
    [MethodImpl(MethodImplOptions.NoInlining)]
    internal static Result Execute(ModifyArguments args,DocumentState state,WriteJob job,ReadContext context,JsonLog log)
    {
        McUndoPoint? tracker=null;Binding? binding=null;McObjectId patternId=McObjectId.Null;bool started=false;string stage="preparation";
        var createdAxes=new HashSet<McObjectId>();
        void Trace(string value){stage=value;log.Write(job.Operation,job.OriginalRequestId,"model_pattern_checkpoint",transaction:value,operationId:job.OperationId);}
        void Close(){var value=tracker;tracker=null;if(value==null)return;try{value.Stop();}finally{value.Dispose();}}
        try
        {
            var edit=(ModelPatternSpec)args.Operations.Single();
            bool add=ModelPatternRequest439.Add(edit),circular=ModelPatternRequest439.Circular(edit);
            using(var tx=state.Document.Database.TransactionManager.StartTransaction())binding=Resolve(edit,args,state,tx,job.CheckCancellation);
            job.CheckCancellation();ModelViewFollowers.CheckStandalone(binding.Body,binding.Views,job.CheckCancellation);tracker=new McUndoPoint(false);tracker.Start();started=true;
            if(binding.X!=null)binding=binding with{X=ModelPatternReferences439.Materialize(binding.X,binding.Body,createdAxes,job.CheckCancellation)};
            if(binding.Y!=null)binding=binding with{Y=ModelPatternReferences439.Materialize(binding.Y,binding.Body,createdAxes,job.CheckCancellation)};
            foreach(var selection in new[]{binding.X,binding.Y}.Where(value=>value!=null).Select(value=>value!))
                if(!selection.Id.IsEdge)
                {
                    binding.ReferenceScope.Add(selection.Id);
                    binding.ReferenceGeometry[selection.Id]=ModelPatternReferences439.CaptureTarget(new McGeomParam{ID=selection.Id},job.CheckCancellation);
                }
            if(binding.ProtectedHistory is {} previousHistory)binding=binding with{ProtectedHistory=ModelPatternEditState430.WithReferenceScope(previousHistory,binding.ReferenceScope)};
            if(add)
            {
                ModelViewFollowers.PrepareInsertion(binding.Views,false);
                Trace("append_before");
                var x=new McGeomParam{ID=binding.X!.Id};
                ArrayFeature? created=circular?binding.Body.AddCircularPatternFeature(binding.SourceFeatures,x,edit.CountX,edit.StepX*Math.PI/180):
                    binding.Body.AddRectangularPatternFeature(binding.SourceFeatures,x,edit.CountX,edit.StepX,new McGeomParam{ID=binding.Y!.Id},edit.CountY,edit.StepY);
                Need(created!=null,"MODEL_UPDATE_FAILED","Native parametric pattern creation was rejected.");
                patternId=created!.ID;
                if(created.OrderObjBefore!=binding.Predecessor)
                    Need(created.SetOrderObjBefore(binding.Predecessor,false),"MODEL_UPDATE_FAILED","Pattern predecessor link was rejected.");
                ModelViewFollowers.CompleteInsertion(binding.Views,patternId);
            }
            else
            {
                patternId=binding.PatternId;var pattern=ModelPatternRequest439.Pattern(patternId,circular);
                Need(pattern!=null,"MODEL_STATE_UNAVAILABLE","Terminal pattern disappeared before update.");
                if(binding.Retarget)
                {Trace("sources_before");ModelPatternEditState430.ReplaceSources(pattern!,binding.SourceFeatures,job.CheckCancellation);}
                if(binding.X!=null)
                    Need(circular?((CircularPatternFeature)pattern!).SetAxis(new McGeomParam{ID=binding.X.Id}):((RectangularPatternFeature)pattern!).SetDirectionX(new McGeomParam{ID=binding.X.Id}),"MODEL_UPDATE_FAILED","Native first axis/direction replacement was rejected.");
                if(binding.Y!=null)Need(((RectangularPatternFeature)pattern!).SetDirectionY(new McGeomParam{ID=binding.Y.Id}),"MODEL_UPDATE_FAILED","Native second direction replacement was rejected.");
                Trace("parameters_before");
                foreach(var control in ModelPatternRequest439.Controls(pattern!,edit))
                    ModelNumericDriverEditor.Set(pattern!,control.Id,control.Value,edit.ReplaceDriverFormulas);
                pattern.DbEntity.Update();
            }
            Trace("update_before");McObjectManager.UpdateAll();
            if(!add)
            {Need(binding.Body.RebuildPart(),"MODEL_UPDATE_FAILED","Native pattern rebuild failed.");McObjectManager.UpdateAll();}
            Trace("update_returned");
            (EntityRef Body,ModelFeatureOracle.Metrics Metrics,bool MeasurableEffect,McObjectId[] NativeSources,bool SuppressionVerified,bool AxisOrderRelease) Verify()
            {
                job.CheckCancellation();
                var contents=ModelInventoryBudget.History(binding.Body,job.CheckCancellation);
                Need(contents!=null&&binding.Contents.Where(id=>!binding.ReferenceScope.Contains(id)).All(contents.Contains)&&contents.Contains(patternId)&&
                    binding.Body.LastPartItemID==(!add?binding.Views.LastHistoryItem:binding.Views.ViewIds.Length==0?patternId:binding.Views.LastHistoryItem),"MODEL_STATE_UNAVAILABLE","Native pattern or preceding history is missing.");
                if(binding.Views.HasFollowers)
                {
                    if(add)ModelViewFollowers.VerifyAfterInsertion(binding.Body,binding.Views,patternId,job.CheckCancellation,refreshStandalone:true);
                    else ModelViewFollowers.Verify(binding.Body,binding.Views,job.CheckCancellation,refreshStandalone:true);
                }
                bool suppressionVerified=true;
                if(!add)
                    Need(contents.Where(id=>!binding.ReferenceScope.Contains(id)).ToHashSet().SetEquals(binding.Contents.Where(id=>!binding.ReferenceScope.Contains(id))),
                        "MODEL_STATE_UNAVAILABLE","Editing the array changed original history identities.");
                var pattern=ModelPatternRequest439.Pattern(patternId,circular);
                Need(pattern!=null,"MODEL_STATE_UNAVAILABLE","The native pattern is unavailable after update.");
                var directions=Enumerable.Range(0,circular?1:2).Select(dimension=>NativePatternObservation439.Read(pattern,dimension)).ToArray();
                var predecessor=pattern.OrderObjBefore;bool axisOrderRelease=false;
                if(predecessor!=binding.Predecessor)
                {
                    // Only explicit release of the exact former WorkAxis may
                    // remove/replace the pattern's assigned order anchor. The
                    // native ordered projection of every other ID must remain.
                    var orderedAfter=binding.RetargetDirections?binding.Body.GetPartContents(true):null;
                    if(orderedAfter!=null)ModelInventoryBudget.Ids(orderedAfter,job.CheckCancellation);
                    axisOrderRelease=binding.RetargetDirections&&binding.ReferenceGeometry.TryGetValue(binding.Predecessor,out var formerAxis)&&
                        formerAxis.TryGetProperty("native_axis_state",out _)&&orderedAfter!=null&&
                        ModelPatternAxisOrder439.ProvesRelease(patternId.ToString(),binding.Predecessor.IsNull?null:binding.Predecessor.ToString(),predecessor.IsNull?null:predecessor.ToString(),
                            binding.OldDirections.Select(direction=>direction.native_id).ToArray(),directions.Select(direction=>direction.native_id).ToArray(),
                            binding.OrderedBefore.Select(id=>id.ToString()).ToArray(),orderedAfter.Select(id=>id.ToString()).ToArray());
                    if(!axisOrderRelease)throw new DomainException("MODEL_STATE_UNAVAILABLE","The array's assigned predecessor changed outside the proved selected-axis release.",details:new{
                        previous_native_order_before_id=binding.Predecessor.IsNull?null:binding.Predecessor.ToString(),native_order_before_id=predecessor.IsNull?null:predecessor.ToString(),
                        retarget_directions=binding.RetargetDirections,ordered_before_count=binding.OrderedBefore.Length,ordered_after_count=orderedAfter?.Count,
                        selected_axis_release_proved=false});
                }
                if(binding.SourceFeatures.Length>0)
                    ModelPatternSources416.Resolve(binding.SourceFeatures.Select(id=>id.ToString()).ToArray(),binding.Body,contents.ToArray(),predecessor,patternId,job.CheckCancellation);
                McObjectId[] nativeSources;
                if(binding.SourceFeatures.Length>0)nativeSources=ModelPatternSources416.Verify(pattern,binding.Body,binding.SourceFeatures,predecessor,job.CheckCancellation,
                    edit.SourceFeatureIds!=null||edit.ExpectedSourceFeatureIds!=null?contents.ToArray():null);
                else
                {
                    nativeSources=ModelPatternRequest439.NativeSources(pattern,job.CheckCancellation);
                    Need(pattern.HostSolidID==binding.Body.ID&&pattern.OrderObjBefore==predecessor&&pattern.Status.ToString()=="Ok"&&!pattern.IsSuppressed&&
                        pattern.DbEntity.Document?.ID==binding.Body.DbEntity.Document.ID&&nativeSources.SequenceEqual(binding.NativeSourcesBefore),"PATTERN_SOURCE_BINDING_MISMATCH","Editing the circular array changed its retained native source items/owner/health/predecessor; supply explicit original source IDs to retarget.");
                }
                var values=ModelPatternRequest439.Values(pattern);
                Need(values.X==edit.CountX&&values.Y==edit.CountY&&Near(values.StepX,edit.StepX)&&Near(values.StepY,edit.StepY),
                    "MODEL_UPDATE_FAILED","Native pattern did not retain requested counts and steps.");
                for(int dimension=0;dimension<directions.Length;dimension++)
                {
                    var selected=dimension==0?binding.X:binding.Y;
                    if(selected!=null)ModelPatternReferences439.VerifySelection(selected,directions[dimension],job.CheckCancellation);
                    if(!add)
                    {
                        var prior=binding.OldDirections[dimension];
                        Need(selected==null?ModelPatternReferences439.SameBinding(prior,directions[dimension]):
                            prior.spacing_mode==directions[dimension].spacing_mode&&prior.reversed==directions[dimension].reversed,
                            "PATTERN_BINDING_CHANGED","A retained native array direction, spacing mode or reversal changed outside the request.");
                    }
                    else Need(directions[dimension].spacing_mode=="step","PATTERN_BINDING_CHANGED","The created native array is not in requested step mode.");
                }
                foreach(var reference in binding.ReferenceGeometry)
                    ModelPatternReferences439.VerifyTarget(reference.Value,new McGeomParam{ID=reference.Key},binding.Body,pattern,job.CheckCancellation,
                        released:binding.RetargetDirections&&binding.OldDirections.Any(old=>old.native_id.Equals(reference.Key.ToString(),StringComparison.OrdinalIgnoreCase))&&
                            directions.All(current=>!current.native_id.Equals(reference.Key.ToString(),StringComparison.OrdinalIgnoreCase)));
                if(!add)
                {
                    ModelPatternEditState430.VerifyDependencies(pattern,binding.Dependencies,binding.OriginalSources,
                        binding.SourceFeatures,predecessor,binding.Retarget,job.CheckCancellation,
                        binding.RetargetDirections?binding.OldDirections.Select(d=>ModelPatternReferences439.Id(d.native_id)).ToArray():null,
                        binding.RetargetDirections?directions.Select(d=>ModelPatternReferences439.Id(d.native_id)).ToArray():null);
                    suppressionVerified=ModelPatternEditState430.VerifySuppression(pattern,binding.Suppression,binding.OriginalCountX,
                        binding.OriginalCountY,edit.CountX,edit.CountY,job.CheckCancellation);
                }
                if(binding.ControlIds.Length>0)
                    Need(ModelPatternRequest439.Controls(pattern,edit).Select(control=>control.Id).SequenceEqual(binding.ControlIds),
                        "MODEL_DRIVER_CHANGED","Pattern count/step driver identities changed.");
                ModelNumericDriverEditor.VerifyRetained(pattern,binding.RetainedExpressions);
                if(binding.RetainedFeatures is {} retained)
                    Need(ModelParameterSnapshot.Equivalent(retained,ModelHistoryEditGuard.FeatureNodes(binding.Body,state,binding.Contents.Where(id=>id.GetObjectOfType<FeatureEntity>()!=null).ToArray(),job.CheckCancellation)),
                        "MODEL_STATE_UNAVAILABLE","Pattern creation changed a retained original forming feature's parameters or dependencies.");
                var inventory=TopLevel(state);
                using var tx=state.Document.Database.TransactionManager.StartTransaction();
                var matches=inventory.Where(id=>tx.GetObject(id,OpenMode.ForRead) is Solid3d&&
                    McObjectId.FromHandle(id.Handle.Value,McDocument.ActiveDocument.ID)==binding.Body.ID).ToArray();
                Need(matches.Length==1&&(matches[0]==binding.Source||!inventory.Contains(binding.Source)),
                    "OBJECT_IDENTITY_MISMATCH","Pattern body has no unique current native representation.");
                bool NewAxis(ObjectId id)=>binding.ReferenceScope.Contains(McObjectId.FromHandle(id.Handle.Value,McDocument.ActiveDocument.ID))&&
                    McObjectId.FromHandle(id.Handle.Value,McDocument.ActiveDocument.ID).GetObjectOfType<WorkAxis>()?.DbEntity.Document?.ID==binding.Body.DbEntity.Document.ID;
                Need(inventory.Except(binding.Inventory).All(id=>id==matches[0]||NewAxis(id))&&
                    binding.Inventory.Except(inventory).All(id=>id==binding.Source),
                    "MODEL_STATE_UNAVAILABLE","Pattern unexpectedly created or removed top-level entities.");
                var solid=(Solid3d)tx.GetObject(matches[0],OpenMode.ForRead);
                var metrics=ModelFeatureOracle.Read(solid);
                Need(solid.Layer==binding.Layer,"MODEL_STATE_UNAVAILABLE","Pattern changed the body's layer.");
                bool measurableEffect=true;
                if(add)
                {
                    var extrusion=binding.SourceFeatures.Length==1?binding.SourceFeatures[0].GetObjectOfType<ExtrudeFeature>():null;
                    double delta=extrusion==null?Math.Abs(metrics.Volume-binding.Before.Volume):extrusion.PartOperation==PartFeatureOperation.Cut?binding.Before.Volume-metrics.Volume:metrics.Volume-binding.Before.Volume;
                    measurableEffect=delta>Math.Max(1e-8,binding.Before.Volume*1e-10);
                }
                if(binding.ProtectedHistory is {} protectedHistory)ModelPatternEditState430.VerifyHistory(protectedHistory,args,state,patternId,
                    binding.Retarget||binding.RetargetDirections,Validation.NormalizeHandle(matches[0].Handle.ToString()),job.CheckCancellation,binding.ReferenceScope,
                    normalizeAxisOrder:axisOrderRelease,originalAnchor:binding.Predecessor.ToString());
                return(new(args.BridgeId,args.DocumentId,Validation.NormalizeHandle(matches[0].Handle.ToString())),metrics,measurableEffect,nativeSources,suppressionVerified,axisOrderRelease);
            }
            Result Complete()
            {
            var verified=Verify();Trace("pattern_verified");
            var sourceRef=new EntityRef(args.BridgeId,args.DocumentId,edit.Handles[0]);
            bool replaced=sourceRef.handle!=verified.Body.handle;
            EntityRef? Direction(string? handle)=>handle==null?null:new(args.BridgeId,args.DocumentId,handle);
            var change=new ModelPatternChange(edit.Kind,sourceRef,verified.Body,binding.Body.ID.ToString(),patternId.ToString(),
                binding.SourceFeatures.Length==1?binding.SourceFeatures[0].ToString():null,Direction(edit.DirectionXHandle),Direction(edit.DirectionYHandle),
                edit.CountX,edit.CountY,edit.StepX,edit.StepY,binding.Before.Volume,verified.Metrics.Volume,replaced,
                binding.SourceFeatures.Select(id=>id.ToString()).ToArray(),verified.NativeSources.Select(id=>id.ToString()).ToArray(),ModelPatternSources416.Scope,
                !add?binding.OriginalSources.Select(id=>id.ToString()).ToArray():null,
                !add?binding.Retarget:null,NativePatternObservation439.Read(ModelPatternRequest439.Pattern(patternId,circular)!,0),
                circular?null:NativePatternObservation439.Read(ModelPatternRequest439.Pattern(patternId,false)!,1));
            ModifiedEntities data;
            if(circular)
            {
                var observedParameters=ModelManufacturingHistory.Parameters(patternId);
                Need(observedParameters!=null,"MODEL_STATE_UNAVAILABLE","The completed circular pattern parameters are unavailable.");
                var parameters=System.Text.Json.Nodes.JsonNode.Parse(Wire.Element(observedParameters).GetRawText())!.AsObject();
                if(binding.SourceFeatures.Length>0)parameters["source_feature_ids"]=System.Text.Json.JsonSerializer.SerializeToNode(binding.SourceFeatures.Select(id=>id.ToString()),Wire.Json);
                parameters["source_binding_scope"]=ModelPatternSources416.Scope;
                parameters["sources_retargeted"]=binding.Retarget;
                data=new(args.OperationId,"committed",false,[verified.Body],replaced?[sourceRef]:[],replaced?[verified.Body]:[],[],"not_guaranteed",
                    manufacturing_changes:[new(edit.Kind,verified.Body,binding.Body.ID.ToString(),patternId.ToString(),Wire.Element(parameters))]);
            }
            else data=new(args.OperationId,"committed",false,[verified.Body],
                replaced?[sourceRef]:[],replaced?[verified.Body]:[],[],"not_guaranteed",model_pattern_changes:[change]);
            var warnings=new List<string>{WriteRecovery.Warning,
                "NATIVE_PATTERN_CHANGED: Refresh history and source refs; inspect the repeated geometry through MCP capture_view. Fixed-axis selectors create retained native WorkAxis construction without added geometric constraints. There is no independent per-instance mate claim."};
            if(!verified.MeasurableEffect)warnings.Add("MATERIAL_EFFECT_UNVERIFIED: The native pattern retains its source, counts and steps, but mass integration did not establish added or removed material. Inspect the resulting repeats and dimensions through MCP.");
            if(!verified.SuppressionVerified)warnings.Add("PATTERN_SUPPRESSION_UNVERIFIED: Native source, count and step changes were observed, but the SDK did not expose a complete before/after suppression inventory. Inspect retained excluded positions.");
            if(verified.AxisOrderRelease)warnings.Add("NATIVE_PATTERN_AXIS_ORDER_RELEASED: Explicit direction replacement released the former WorkAxis assigned predecessor. The actual native ordered projection of every other retained history ID was preserved; refresh current native bindings/order.");
            var result=Result.Ok(job.OriginalRequestId,data,context,warnings.ToArray());
            result=ModelViewFollowers.WithWarnings(result,binding.Views);job.RecordCommit(result);Trace("commit_recorded");return result;
            }
            if(add)
            {Verify();job.CheckCancellation();job.BeginCommit();Close();return Complete();}
            // Native MCS variables and their consumers finalize at command end.
            // Match the generic named-parameter route before verifying count,
            // step, source and follower invariants on the next native idle.
            job.CheckCancellation();job.BeginCommit();Close();
            return NativePostCommandVerification422.Defer(job,Complete,Failure);
        }
        catch(Exception error){return Failure(error);}
        Result Failure(Exception error)
        {
            if(job.CommittedResult is {} committed)return committed;
            Exception? cleanup=null;try{Close();}catch(Exception e){cleanup=e;}
            bool unknown=started||cleanup!=null;
            string cause=error is DomainException d?d.Error.code:error is OperationCanceledException?"CANCELLED":"MODEL_PATTERN_FAILED";
            return job.Error(unknown?"WRITE_OUTCOME_UNKNOWN":cause,
                unknown?"Native pattern outcome is unverified; inspect the affected body and history before dependent work.":error.Message,
                unknown?"unknown":"not_started",context,new{cause,stage,message=error.Message,
                    body_mc_id=binding?.Body.ID.ToString(),pattern_id=patternId.ToString(),verification_details=(error as DomainException)?.Error.details,cleanup_error=cleanup?.GetType().Name})
                with{warnings=[WriteRecovery.Warning]};
        }
    }
}
