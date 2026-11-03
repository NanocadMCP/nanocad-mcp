using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text.Json;
using Multicad;
using Multicad.DatabaseServices;
using Multicad.Mc3D;
using NanoCAD.Contracts;
using NanoCAD.Ipc;
using Teigha.DatabaseServices;

namespace NanoCADBridge;

internal static class ModelEdgeFeatureWriter
{
    internal static bool Handles(ModifyArguments args)=>args.Operations.Any(e=>e is ModelEdgeFeatureSpec);
    private static void Need([System.Diagnostics.CodeAnalysis.DoesNotReturnIf(false)] bool valid,string code,string message){if(!valid)throw new DomainException(code,message);}
    private sealed record Binding(Mc3dSolid Body,ObjectId Source,string Layer,McObjectId[] Contents,JsonElement Topology,
        ModelFeatureOracle.Metrics Before,HashSet<ObjectId> Inventory,ModelViewFollowers.Snapshot Views);
    private static HashSet<ObjectId> Inventory(DocumentState state)
    {
        using var tx=state.Document.Database.TransactionManager.StartTransaction();
        var db=state.Document.Database;var model=((BlockTable)tx.GetObject(db.BlockTableId,OpenMode.ForRead))[BlockTableRecord.ModelSpace];
        var ids=((BlockTableRecord)tx.GetObject(model,OpenMode.ForRead)).Cast<ObjectId>().ToHashSet();
        return ids;
    }
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static Binding Resolve(ModelEdgeFeatureSpec edit,ModifyArguments args,DocumentState state,Transaction tx,Action? cancel=null)
    {
        Need(args.Operations.Length==1,"INVALID_ARGUMENT","Native edge feature must be the sole operation.");
        var loaded=AppDomain.CurrentDomain.GetAssemblies().Select(a=>a.GetName().Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        Need(loaded.Contains("mapimgd")&&loaded.Contains("MapiBaseTypes"),"CAPABILITY_UNAVAILABLE","MultiCAD runtime must already be loaded.");
        var db=state.Document.Database;var model=((BlockTable)tx.GetObject(db.BlockTableId,OpenMode.ForRead))[BlockTableRecord.ModelSpace];
        Need(db.CurrentSpaceId==model,"OUT_OF_SCOPE","Activate Model Space for native feature editing.");
        var source=CadStructureWriter.Find(db,edit.Handles.Single());
        var solid=tx.GetObject(source,OpenMode.ForRead) as Solid3d;
        Need(solid!=null&&solid.GetType()==typeof(Solid3d)&&solid.OwnerId==model&&!solid.IsNull,
            "UNSUPPORTED_ENTITY_TYPE","Select one nonempty top-level native model representation.");
        var layer=(LayerTableRecord)tx.GetObject(solid!.LayerId,OpenMode.ForRead);
        Need(!layer.IsLocked&&!layer.IsFrozen&&!layer.IsOff&&!layer.IsDependent,"LAYER_UNAVAILABLE","Model layer is unavailable.");
        var reference=new EntityRef(args.BridgeId,args.DocumentId,Validation.NormalizeHandle(source.Handle.ToString()));
        var topology=Wire.Element(SolidTopologyReader.Capture(solid,reference,CancellationToken.None,cancel));
        Need(topology.GetProperty("snapshot_token").GetString()?.Equals(edit.TopologyToken,StringComparison.OrdinalIgnoreCase)==true,
            "STALE_TOPOLOGY","Native model B-rep changed; inspect_solid_topology again.");
        var chosen=edit.EdgeIds.Select(edge=>topology.GetProperty("edges").EnumerateArray()
            .SingleOrDefault(item=>item.GetProperty("native_id").GetString()==edge)).ToArray();
        Need(chosen.All(e=>e.ValueKind==JsonValueKind.Object),"SUBENTITY_NOT_FOUND","Selected B-rep edge does not belong to the current model snapshot.");
        static double[]? Point(JsonElement edge,string name){var value=edge.GetProperty(name);return value.ValueKind==JsonValueKind.Array?value.EnumerateArray().Select(v=>v.GetDouble()).ToArray():null;}
        foreach(var edge in chosen)
        {
            if(edge.TryGetProperty("circle",out var circle)&&circle.ValueKind==JsonValueKind.Object)continue;
            var a=Point(edge,"start_vertex");var b=Point(edge,"end_vertex");
            Need(a!=null&&b!=null,"SUBENTITY_GEOMETRY_UNAVAILABLE","This edge has no two explicit vertices for native identity mapping.");
            double length=Math.Sqrt(a!.Zip(b!).Sum(p=>(p.First-p.Second)*(p.First-p.Second)));
            Need(length>1e-8,"SUBENTITY_GEOMETRY_UNAVAILABLE","Selected edge has no usable span for native identity mapping.");
        }
        var active=McDocument.ActiveDocument!;var bodyId=McObjectId.FromHandle(source.Handle.Value,active.ID);
        var body=bodyId.GetObjectOfType<Mc3dSolid>();
        Need(body!=null&&body.ID==bodyId&&body.LastPartItemID.ToString().Equals(edit.ExpectedLastItemId,StringComparison.OrdinalIgnoreCase),
            "OBJECT_IDENTITY_MISMATCH","Body identity or history anchor changed.");
        var contents=ModelInventoryBudget.History(body!,cancel);
        Need(contents.Count>0,"HISTORY_UNAVAILABLE","Require healthy existing native history.");
        var views=ModelViewFollowers.Resolve(body,contents.ToArray(),cancel,includeStandalone:true);
        ModelViewFollowers.PrepareInsertion(views,true);
        var external=ProjectedHistorySources.Read(body,contents.ToArray(),state,tx,cancel);
        ProjectedHistorySources.Healthy(contents.ToArray(),external);
        var before=ModelFeatureOracle.Read(solid);
        return new(body,source,solid.Layer,contents!.ToArray(),topology,before,Inventory(state),views);
    }
    internal static void Preflight(ModifyArguments args,DocumentState state,Transaction tx,Action? cancel=null)=>Resolve((ModelEdgeFeatureSpec)args.Operations.Single(),args,state,tx,cancel);
    [MethodImpl(MethodImplOptions.NoInlining)]
    internal static Result Execute(ModifyArguments args,DocumentState state,WriteJob job,ReadContext context,JsonLog log)
    {
        McUndoPoint? tracker=null;Binding? binding=null;McObjectId featureId=McObjectId.Null;bool started=false;string stage="preparation";
        void Trace(string point){stage=point;log.Write(job.Operation,job.OriginalRequestId,"model_edge_feature_checkpoint",transaction:point,operationId:job.OperationId);}
        void Close(){var value=tracker;tracker=null;if(value==null)return;try{value.Stop();}finally{value.Dispose();}}
        try
        {
            var edit=(ModelEdgeFeatureSpec)args.Operations.Single();
            using(var tx=state.Document.Database.TransactionManager.StartTransaction())binding=Resolve(edit,args,state,tx,job.CheckCancellation);
            job.CheckCancellation();
            ModelViewFollowers.CheckStandalone(binding.Body,binding.Views,job.CheckCancellation);tracker=new McUndoPoint(false);tracker.Start();Trace("native_edge_mapping_before");started=true;
            var native=ModelEdgeReader.Capture(binding.Source,state,args.BridgeId,CancellationToken.None,job.CheckCancellation);
            Need(native.last_item_id.Equals(edit.ExpectedLastItemId,StringComparison.OrdinalIgnoreCase),
                "STALE_TOPOLOGY","Native history anchor changed before edge mapping.");
            bool Near(double a,double b)=>Math.Abs(a-b)<=1e-6*Math.Max(1,Math.Max(Math.Abs(a),Math.Abs(b)));
            bool Same(double[] a,double[] b)=>a.Zip(b).All(p=>Near(p.First,p.Second));
            var selected=new List<McObjectId>();
            foreach(string edgeId in edit.EdgeIds)
            {
                var edge=binding.Topology.GetProperty("edges").EnumerateArray().Single(e=>e.GetProperty("native_id").GetString()==edgeId);
                ModelEdgeReader.Edge[] matches;
                if(edge.TryGetProperty("circle",out var circle)&&circle.ValueKind==JsonValueKind.Object)
                {
                    var center=circle.GetProperty("center").EnumerateArray().Select(p=>p.GetDouble()).ToArray();
                    var normal=circle.GetProperty("normal").EnumerateArray().Select(p=>p.GetDouble()).ToArray();
                    double radius=circle.GetProperty("radius").GetDouble();
                    matches=native.edges.Where(e=>e.circle is {} c&&Near(c.radius,radius)&&Same(c.center,center)&&
                        (Same(c.normal,normal)||Same(c.normal,normal.Select(v=>-v).ToArray()))).ToArray();
                }
                else
                {
                double[] a=edge.GetProperty("start_vertex").EnumerateArray().Select(x=>x.GetDouble()).ToArray();
                double[] b=edge.GetProperty("end_vertex").EnumerateArray().Select(x=>x.GetDouble()).ToArray();
                double length=Math.Sqrt(a.Zip(b).Sum(p=>(p.First-p.Second)*(p.First-p.Second)));
                matches=native.edges.Where(e=>e.circle==null&&Near(e.length,length)&&
                    ((Same(e.start,a)&&Same(e.end,b))||(Same(e.start,b)&&Same(e.end,a)))).ToArray();
                }
                Need(matches.Length==1,"SUBENTITY_IDENTITY_UNAVAILABLE","B-rep edge has no unique native MultiCAD edge with matching endpoints and length.");
                var marker=long.Parse(matches[0].marker,CultureInfo.InvariantCulture);
                var mapped=McObjectId.FromGsMarker(binding.Body.ID,marker);
                Need(!mapped.IsNull&&mapped.IsEdge&&mapped.ParentId==binding.Body.ID&&mapped.ToString()==matches[0].model_edge_id,
                    "SUBENTITY_IDENTITY_UNAVAILABLE","Matched native edge identity changed before feature creation.");
                selected.Add(mapped);
            }
            Need(selected.Distinct().Count()==selected.Count,"SUBENTITY_IDENTITY_UNAVAILABLE","Several B-rep selections mapped to the same native edge.");
            ModelViewFollowers.PrepareInsertion(binding.Views,false);
            Trace("append_before");
            HistoryItem? feature=edit.Kind=="add_model_fillet"?binding.Body.AddFilletFeature(selected,edit.Size):binding.Body.AddChamferFeature(selected,edit.Size);
            Need(feature!=null,"MODEL_UPDATE_FAILED","Native edge feature creation failed.");featureId=feature!.ID;
            // Some SDK builds move LastPartItemID during AddFeature; use the
            // exact preflight anchor rather than trusting the mutable property.
            var anchor=binding.Views.LastModelItem;
            if(feature.OrderObjBefore!=anchor)Need(feature.SetOrderObjBefore(anchor,false),"MODEL_UPDATE_FAILED","Native feature predecessor was rejected.");
            Need(feature.OrderObjBefore==anchor,"MODEL_UPDATE_FAILED","Native feature order differs from its requested predecessor.");
            ModelViewFollowers.CompleteInsertion(binding.Views,featureId);
            Trace("update_before");McObjectManager.UpdateAll();Trace("update_returned");
            (EntityRef Body,ModelFeatureOracle.Metrics Metrics,McObjectId Variable) Verify()
            {
                job.CheckCancellation();
                var history=ModelInventoryBudget.History(binding.Body,job.CheckCancellation);
                Need(history!=null&&binding.Contents.All(history.Contains)&&history.Contains(featureId)&&
                     binding.Body.LastPartItemID==(binding.Views.ViewIds.Length==0?featureId:binding.Views.LastHistoryItem),"MODEL_STATE_UNAVAILABLE","Prior history or new edge feature is missing.");
                if(binding.Views.HasFollowers)ModelViewFollowers.VerifyAfterInsertion(binding.Body,binding.Views,featureId,job.CheckCancellation,refreshStandalone:true);
                var node=featureId.GetObjectOfType<HistoryItem>();
                Need(node!=null&&node.HostSolidID==binding.Body.ID&&!node.IsSuppressed&&node.Status.ToString()=="Ok"&&node.OrderObjBefore==anchor,
                    "MODEL_STATE_UNAVAILABLE","Native edge feature status, owner or order differs.");
                McObjectId variable;McObjectId[] actualEdges;
                if(edit.Kind=="add_model_fillet")
                {
                    var fillet=featureId.GetObjectOfType<FilletFeature>();
                    Need(fillet!=null&&Math.Abs(fillet.Radius-edit.Size)<1e-7,"MODEL_STATE_UNAVAILABLE","Fillet radius differs from request.");
                    variable=fillet!.RadiusParamVarID;actualEdges=fillet.GetEdges()?.ToArray()??[];
                }
                else
                {
                    var chamfer=featureId.GetObjectOfType<ChamferFeature>();
                    Need(chamfer!=null&&Math.Abs(chamfer.Distance-edit.Size)<1e-7,"MODEL_STATE_UNAVAILABLE","Chamfer width differs from request.");
                    variable=chamfer!.DistanceParamVarID;actualEdges=chamfer.GetEdges()?.ToArray()??[];
                }
                Need(!variable.IsNull&&actualEdges.Length==selected.Count&&actualEdges.ToHashSet().SetEquals(selected),
                    "MODEL_STATE_UNAVAILABLE","Native feature lost its bound size variable or selected edges.");
                var after=Inventory(state);
                using var tx=state.Document.Database.TransactionManager.StartTransaction();
                var matches=after.Where(id=>tx.GetObject(id,OpenMode.ForRead) is Solid3d&&McObjectId.FromHandle(id.Handle.Value,McDocument.ActiveDocument.ID)==binding.Body.ID).ToArray();
                Need(matches.Length==1&&(matches[0]==binding.Source||!after.Contains(binding.Source)),
                    "OBJECT_IDENTITY_MISMATCH","Cannot identify exactly one resulting body representation.");
                Need(after.Except(binding.Inventory).All(id=>id==matches[0])&&binding.Inventory.Except(after).All(id=>id==binding.Source),
                    "MODEL_STATE_UNAVAILABLE","Unexpected top-level entities were created or removed.");
                var body=(Solid3d)tx.GetObject(matches[0],OpenMode.ForRead);var metrics=ModelFeatureOracle.Read(body);
                Need(body.Layer==binding.Layer&&metrics.Volume>0,
                    "NATIVE_GEOMETRY_MISMATCH","Finished model is empty or no longer on its original layer.");
                return(new EntityRef(args.BridgeId,args.DocumentId,Validation.NormalizeHandle(matches[0].Handle.ToString())),metrics,variable);
            }
            var verified=Verify();Trace("feature_verified");job.BeginCommit();Close();verified=Verify();
            var sourceRef=new EntityRef(args.BridgeId,args.DocumentId,edit.Handles[0]);bool replaced=sourceRef.handle!=verified.Body.handle;
            var change=new ModelEdgeFeatureChange(sourceRef,verified.Body,binding.Body.ID.ToString(),featureId.ToString(),verified.Variable.ToString(),
                edit.Kind,selected.Select(i=>i.ToString()).ToArray(),edit.Size,binding.Before.Volume,verified.Metrics.Volume,replaced);
            var data=new ModifiedEntities(args.OperationId,"committed",false,[verified.Body],replaced?[sourceRef]:[],replaced?[verified.Body]:[],[],"not_guaranteed",model_edge_features:[change]);
            var result=Result.Ok(job.OriginalRequestId,data,context,WriteRecovery.Warning,
                "MODEL_EDGE_FEATURE_APPENDED: Refresh model history, edge inventory, variables and drawing views. Native rebuild can update dependents; verify the engineering model visually.");
            result=ModelViewFollowers.WithWarnings(result,binding.Views);job.RecordCommit(result);Trace("commit_recorded");return result;
        }
        catch(Exception error)
        {
            if(job.CommittedResult is {} committed)return committed;
            Exception? cleanup=null;try{Close();}catch(Exception e){cleanup=e;}
            bool unknown=started||cleanup!=null;
            string cause=error is DomainException d?d.Error.code:error is OperationCanceledException?"CANCELLED":"MODEL_EDGE_FEATURE_FAILED";
            return job.Error(unknown?"WRITE_OUTCOME_UNKNOWN":cause,unknown?"Native edge feature result is unverified; partial history may remain. Inspect this model and refresh its history before another dependent edit.":"Native edge feature did not start.",
                unknown?"unknown":"not_started",context,new{cause,stage,message=error.Message,feature_id=featureId.ToString(),body_mc_id=binding?.Body.ID.ToString(),cleanup_error=cleanup?.Message})
                with{warnings=[WriteRecovery.Warning]};
        }
    }
}
