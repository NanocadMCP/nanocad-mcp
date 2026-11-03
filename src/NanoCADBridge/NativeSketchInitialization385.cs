using Multicad;
using Multicad.DatabaseServices;
using Multicad.DatabaseServices.StandardObjects;
using Multicad.Mc3D;
using NanoCAD.Contracts;
using System.Text.Json;
using System.Text.Json.Nodes;
using G=Multicad.Geometry;
using Db=Teigha.DatabaseServices;

namespace NanoCADBridge;

internal static class NativeSketchInitialization385
{
    internal sealed record Created(NativeSketchCreated385[] Objects,McObjectId[] ObjectIds,McObjectId[] ConstraintIds,McObjectId[] DimensionIds,McObjectId[] VariableIds,SketchConstraintChange[] Changes,
        NativeSketchRelations410.Baseline Baseline,NativeSketchMembers385.Snapshot InitialInputs);
    internal static Created Create(PlanarSketch sketch,NanoCAD.Contracts.NativeSketchInitialization385 input,DocumentState state,Action cancel,Action<McObjectId> observed)
    {
        bool editing=false;var ids=new List<McObjectId>();var transientRelations=new List<McObjectId>();var objects=new List<NativeSketchCreated385>();var nativeSplines=new Dictionary<McObjectId,NativeSketchPrimitive385>();
        string ObservationFailure(Exception error)=>error is DomainException d?d.Error.code+": "+error.Message:error.GetType().Name+" (0x"+error.HResult.ToString("X8")+"): "+error.Message;
        try
        {
            editing=true;NativeSketchMembers385.Need(sketch.EnterEditMode(),"SKETCH_EDIT_UNAVAILABLE","Native sketch edit mode is unavailable.");
            var sheet=McDocumentsManager.GetActiveSheet();NativeSketchMembers385.Need(sheet!=null&&!sheet.ID.IsNull,"CONSTRAINT_SCOPE_MISMATCH","The current sketch edit sheet is unavailable.");
            foreach(var source in input.Geometry)
            {
                cancel();G.Point3d Point(Point3 p)=>new(p.X,p.Y,p.Z);DbGeometry value;
                if(source.Kind=="spline")
                {
                    // AddGeometry can materialize another canonical item ID in
                    // the editor. The supplied GUID is only an attempt nonce;
                    // one complete native creation delta identifies the result.
                    sketch.DbEntity.Update();var beforeSpline=NativeSketchInitializationIdentity445.Capture(sketch,state,cancel);
                    var id=McObjectId.NewID();NativeSketchMembers385.Need(!id.IsNull&&id.GetObject()==null,"SKETCH_BINDING_UNAVAILABLE","Native Spline geometry requires a fresh unique identity.");
                    using var spline=NativeSketchCurveGeometry445.CreateSpline(source);using var geometry=new G.EntityGeometry(spline);
                    bool splineAdded=sketch.AddGeometry(id,geometry,true);
                    if(!splineAdded)throw new DomainException("SKETCH_BINDING_UNAVAILABLE","The native sketch rejected AddGeometry for the new Spline.",false,
                        new{stage="input_native_spline_add",key=source.Key,requested_nonce=id.ToString(),sketch_id=sketch.ID.ToString(),sketch_edit_mode=sketch.IsInEditMode,add_geometry_returned=splineAdded,geometry_coordinate_basis="WCS"});
                    sketch.DbEntity.Update();var afterSpline=NativeSketchInitializationIdentity445.Capture(sketch,state,cancel);
                    var actualId=NativeSketchInitializationIdentity445.CreatedSpline(sketch,beforeSpline,afterSpline,source,id,cancel);ids.Add(actualId);observed(actualId);
                    nativeSplines.Add(actualId,source);objects.Add(new(source.Key,actualId.ToString(),[actualId.ToString()]));continue;
                }
                if(source.Kind=="line"){using var line=new G.LineSeg3d(Point(source.Points[0]),Point(source.Points[1]));DbLine native=line;value=native;}
                else if(source.Kind=="arc"){using var arc=new G.CircArc3d(Point(source.Points[0]),Point(source.Points[1]),Point(source.Points[2]));DbCircArc native=arc;value=native;}
                else if(source.Kind=="ellipse")value=NativeSketchCurveGeometry445.Create(source);
                else
                {
                    // Sample3dObject creates a local XY circle, transforms the
                    // complete 3D geometry, then assigns DbGeometry.Geometry.
                    // A point-on-sketch transform is not a normal-vector API.
                    using var circle=new G.CircArc3d(sketch.TransformPointWorldToSketch(Point(source.Points[0])),G.Vector3d.ZAxis,source.Radius!.Value);
                    circle.TransformBy(sketch.GetSketchToWorldTransform());
                    value=new DbGeometry();value.Geometry=circle;
                }
                NativeSketchMembers385.Need(value.DbEntity.AddToCurrentDocument(),"MODEL_UPDATE_FAILED","Native input registration failed.");ids.Add(value.ID);observed(value.ID);value.DbEntity.Update();
                if(value.ID.IsNull||value.DbEntity.Document?.ID!=sheet.ID)
                    throw new DomainException("SKETCH_BINDING_UNAVAILABLE","The registered input lacks exact current edit-sheet residency; AddObject was not called.",false,new{stage="input_residency",key=source.Key,input_id=value.ID.ToString(),actual_native_document_id=value.DbEntity.Document?.ID.ToString(),edit_sheet_id=sheet.ID.ToString(),sketch_id=sketch.ID.ToString(),sketch_edit_mode=sketch.IsInEditMode});
                bool added=sketch.AddObject(value.ID),contained=sketch.ContainsObject(value.ID);
                if(!added||!contained)
                    throw new DomainException("SKETCH_BINDING_UNAVAILABLE","The registered input lacks verified native AddObject and exact sketch membership.",false,new{stage="input_sketch_membership",key=source.Key,input_id=value.ID.ToString(),actual_native_document_id=value.DbEntity.Document?.ID.ToString(),edit_sheet_id=sheet.ID.ToString(),sketch_id=sketch.ID.ToString(),sketch_edit_mode=sketch.IsInEditMode,add_object_returned=added,contains_object=contained});
                objects.Add(new(source.Key,value.ID.ToString(),[value.ID.ToString()]));
            }
            sketch.DbEntity.Update();NativeSketchMembers385.Snapshot members;
            using(var tx=state.Document.Database.TransactionManager.StartTransaction())members=NativeSketchMembers385.Capture(sketch,state,tx,cancel);
            NativeSketchMembers385.Need(members.Members.All(m=>m.Owned||m.ReferenceOnly)&&members.Members.Where(m=>m.Owned).Select(m=>m.ObjectId).ToHashSet().SetEquals(ids),"SKETCH_BINDING_UNAVAILABLE","Fresh owned input membership differs from returned IDs, or a projection lacks its immutable reference role.");
            RequireNativeSplines(sketch,nativeSplines,members.Plane,cancel);
            RequireRequested(input,objects.ToArray(),members,cancel);var specs=BindSpecs(input,objects.ToArray(),members,cancel);NativeSketchMembers385.ValidateInputs(specs,members);
            NativeSketchRelations410.Baseline baseline;NativeSketchInitializationIdentity445.ExitWitness? exitWitness=null;
            if(specs.Length>0)using(var manager=NativeSketchMembers385.Manager(sketch,allowUnlinkedEditScope:true))
            {
                var preAddInventory=manager.GetAllConstraints();
                if(preAddInventory!=null)
                {
                    NativeSketchMembers385.Snapshot scoped;using(var tx=state.Document.Database.TransactionManager.StartTransaction())scoped=NativeSketchMembers385.Capture(sketch,state,tx,cancel,manager,preAddInventory);
                    PreserveBeforeSolve(members,scoped,input,objects.ToArray(),cancel);members=scoped;
                }
                baseline=preAddInventory==null?NativeSketchRelations410.Unavailable(members,"native_GetAllConstraints_returned_null_before_first_requested_Add_false"):
                    NativeSketchRelations410.Capture(manager,members,cancel,preAddInventory);
                foreach(var id in baseline.NativeIds)observed(id);
                foreach(var spec in specs)
                {
                    cancel();using var info=NativeSketchMembers385.Info(spec,members);
                    var request=NativeSketchRelations410.AddRequest(manager,info,spec,members,sheet.ID,ModelProfileDiscovery348.DrawingDocument(state).ID,transientRelations.Count,baseline);
                    var id=manager.AddConstraint(info,false);if(!id.IsNull){transientRelations.Add(id);observed(id);}else throw new DomainException("CONSTRAINT_REJECTED","Native relation creation returned no ID.",false,request);
                    NativeSketchMembers385.RequireEditBatchContext(manager,sketch,sheet);
                    if(!baseline.Complete)
                    {
                        // Fresh initializer only. The official managed sample
                        // writes its first requested relation before inventory
                        // enumeration. A pre-add null remains unobserved; it
                        // never becomes an empty-network preservation claim.
                        var materializedInventory=manager.GetAllConstraints();
                        NativeSketchMembers385.Need(materializedInventory!=null&&materializedInventory.Contains(id),"CONSTRAINTS_UNAVAILABLE","The native relation inventory did not materialize after the first requested Add(false); no solve was requested.");
                        NativeSketchMembers385.Snapshot materialized;using(var tx=state.Document.Database.TransactionManager.StartTransaction())materialized=NativeSketchMembers385.Capture(sketch,state,tx,cancel,manager,materializedInventory);
                        PreserveBeforeSolve(members,materialized,input,objects.ToArray(),cancel);members=materialized;
                        var actual=NativeSketchRelations410.Capture(manager,materialized,cancel,materializedInventory);
                        using var receipt=manager.GetConstraintInfo(id);
                        NativeSketchMembers385.Need(receipt!=null&&NativeSketchMembers385.RequestedReferences(receipt,info,spec,materialized),"CONSTRAINT_SCOPE_MISMATCH","The first requested relation lacks its exact native type/member binding.");
                        NativeSketchRelations410.RequirePointReceipt(spec,receipt,materialized);
                        foreach(var nativeId in actual.NativeIds)observed(nativeId);
                        baseline=actual with{Relations=NativeSketchRelations410.Without(actual.Relations,new HashSet<string>(StringComparer.OrdinalIgnoreCase){id.ToString()}),
                            ObservationStage="materialized_after_first_requested_add_before_solve",PreAddUnavailableReason=baseline.UnavailableReason};
                    }
                }
                if(specs.Length>0)NativeSketchMembers385.Need(manager.ApplyAllConstraints(),"CONSTRAINT_SOLVE_FAILED","Native initialization solve failed.");
                NativeSketchMembers385.Snapshot solved;using(var tx=state.Document.Database.TransactionManager.StartTransaction())solved=NativeSketchMembers385.Capture(sketch,state,tx,cancel,manager);
                var solvedRelations=NativeSketchConstraintState385.Relations(manager,cancel);
                NativeSketchRelations410.ImmutableReferences(members,solved,cancel);
                var retainedBaseline=NativeSketchRelations410.Retained(baseline,solvedRelations,solved,cancel).ToHashSet(StringComparer.OrdinalIgnoreCase);
                if(nativeSplines.Count>0)
                {
                    // Only the prior baseline has old-edit identity promises.
                    // New request IDs/names are not published until the exact
                    // persistent spec/driver matching below has succeeded.
                    baseline=NativeSketchInitializationIdentity445.CapturePriorBaseline(manager,solved,retainedBaseline,cancel) with{ObservationStage=baseline.ObservationStage,PreAddUnavailableReason=baseline.PreAddUnavailableReason};
                    exitWitness=NativeSketchInitializationIdentity445.CaptureExit(solved,baseline.Relations,cancel);
                }
            }
            else
            {
                try{using var manager=NativeSketchMembers385.Manager(sketch,allowUnlinkedEditScope:true);baseline=NativeSketchRelations410.Capture(manager,members,cancel);}
                catch(OperationCanceledException){throw;}
                catch(Exception error)
                {baseline=NativeSketchRelations410.Unavailable(members,ObservationFailure(error));}
                if(nativeSplines.Count>0)
                {
                    NativeSketchMembers385.Snapshot exiting;using(var tx=state.Document.Database.TransactionManager.StartTransaction())exiting=NativeSketchMembers385.Capture(sketch,state,tx,cancel);
                    if(baseline.Complete)NativeSketchRelations410.Retained(baseline,baseline.Relations,exiting,cancel);
                    exitWitness=NativeSketchInitializationIdentity445.CaptureExit(exiting,baseline.Complete?baseline.Relations:null,cancel);
                }
            }
            sketch.ExitEditMode();editing=false;NativeSketchMembers385.Need(!sketch.IsInEditMode,"MODEL_UPDATE_FAILED","Native sketch did not leave edit mode.");sketch.DbEntity.Update();McObjectManager.UpdateAll();
            NativeSketchMembers385.Snapshot current;
            using(var tx=state.Document.Database.TransactionManager.StartTransaction())
                current=NativeSketchMembers385.Capture(sketch,state,tx,cancel);
            if(exitWitness!=null)
            {
                NativeSketchCorrespondence410.Mapping mapping;
                if(exitWitness.Relations!=null)
                {
                    using var manager=NativeSketchMembers385.Manager(sketch);var actual=NativeSketchConstraintState385.Relations(manager,cancel);
                    mapping=NativeSketchInitializationIdentity445.ResolveExit(exitWitness,current,actual,cancel,manager);
                }
                else mapping=NativeSketchInitializationIdentity445.ResolveExit(exitWitness,current,null,cancel);
                ids=ids.Select(mapping.Map).ToList();objects=objects.Select(row=>row with{object_id=mapping.Map(row.object_id),member_ids=row.member_ids.Select(value=>mapping.Map(value)).ToArray()}).ToList();
                specs=specs.Select(spec=>NativeSketchCorrespondence410.Spec(spec,mapping)).ToArray();members=NativeSketchCorrespondence410.Inputs(members,mapping,cancel);
                nativeSplines=nativeSplines.ToDictionary(pair=>mapping.Map(pair.Key),pair=>pair.Value);
                baseline=baseline with{Relations=baseline.Complete?NativeSketchCorrespondence410.Relations(baseline.Relations,mapping,cancel):baseline.Relations,
                    NativeIds=baseline.NativeIds.Select(mapping.Map).ToArray(),Inputs=baseline.Inputs==null?null:NativeSketchCorrespondence410.Inputs(baseline.Inputs,mapping,cancel)};
                foreach(var id in ids.Concat(baseline.NativeIds).Distinct())observed(id);
            }
            foreach(var pair in nativeSplines)
            {
                cancel();if(current.Members.Count(member=>member.Id==pair.Key&&member.ObjectId==pair.Key&&member.Segment==null&&member.Owned&&!member.ReferenceOnly)!=1)
                    throw new DomainException("SKETCH_BINDING_UNAVAILABLE","Leaving native edit mode changed the new Spline identity or ownership; no coordinate-based alias is inferred.",false,
                        new{stage="native_spline_identity_after_exit",key=pair.Value.Key,expected_input_id=pair.Key.ToString(),sketch_id=sketch.ID.ToString(),membership_basis="actual_native_geometry_ID_and_WCS_getters"});
            }
            if(specs.Length==0)RequireRequested(input,objects.ToArray(),current,cancel);NativeSketchMembers385.ValidateSolved(specs,current);NativeSketchMembers385.FixedRequested(specs,members,current);NativeSketchRelations410.ImmutableReferences(members,current,cancel);
            if(specs.Length==0)
            {
                try
                {
                    using var manager=NativeSketchMembers385.Manager(sketch);var observedBaseline=NativeSketchRelations410.Capture(manager,current,cancel);
                    if(baseline.Complete)NativeSketchRelations410.Retained(baseline,observedBaseline.Relations,current,cancel,allowNetworkTransfer:true);
                    baseline=observedBaseline;
                }
                catch(OperationCanceledException){throw;}
                catch(Exception error)
                {baseline=NativeSketchRelations410.Unavailable(current,ObservationFailure(error));}
                foreach(var id in baseline.NativeIds)observed(id);
                return new(objects.ToArray(),ids.ToArray(),[],[],[],[],baseline,members);
            }
            using var retainedManager=NativeSketchMembers385.Manager(sketch);var retained=NativeSketchConstraintState385.Relations(retainedManager,cancel);
            var baselineIds=NativeSketchRelations410.Retained(baseline,retained,current,cancel,allowNetworkTransfer:true).ToHashSet(StringComparer.OrdinalIgnoreCase);
            var relations=new List<McObjectId>();var dimensions=new List<McObjectId>();var variables=new List<McObjectId>();var changes=new List<SketchConstraintChange>();
            foreach(var spec in specs)
            {
                cancel();using var expected=NativeSketchMembers385.Info(spec,current);var candidates=new List<McObjectId>();
                foreach(var id in retainedManager.GetAllConstraints()??throw new DomainException("CONSTRAINTS_UNAVAILABLE","The persistent relation inventory is unavailable."))
                {
                    if(baselineIds.Contains(id.ToString()))continue;using var info=retainedManager.GetConstraintInfo(id);
                    if(info==null||!NativeSketchMembers385.RequestedReferences(info,expected,spec,current))continue;
                    if(spec.Kind is "fix_point" or "coincident" or "smooth" or "distance" or "distance_x" or "distance_y" or "length")
                    {if(!NativeSketchMembers385.RequestedReferences(info,expected,spec,current,comparePoints:true))continue;}
                    if(spec.Value.HasValue&&(!info.IsDimensional||info.VarId.IsNull||retainedManager.GetConstraintParamId(id)!=info.VarId||!ModelParameterSnapshot.Near(retainedManager.GetConstraintParamValue(id),spec.Value.Value)))continue;
                    candidates.Add(id);
                }
                if(!(candidates.Count==1&&!relations.Contains(candidates[0])))
                    throw new DomainException("CONSTRAINT_PERSISTENCE_UNAVAILABLE","The requested relation did not transfer uniquely to the persistent sketch network with its exact point/control binding.",false,new{
                        stage="requested_relation_persistence",requested=spec,candidate_count=candidates.Count,
                        expected=NativeSketchMembers385.DescribeRelation(expected),
                        actual_same_type=retained.GetProperty("constraints").EnumerateArray().Where(row=>row.GetProperty("type").GetString()==expected.Type.ToString()).ToArray()});
                var actual=candidates[0];relations.Add(actual);observed(actual);using var receipt=retainedManager.GetConstraintInfo(actual);NativeSketchMembers385.Need(receipt!=null,"CONSTRAINTS_UNAVAILABLE","A persistent requested relation lost its readable native receipt.");
                NativeSketchRelations410.RequirePointReceipt(spec,receipt,current);
                if(nativeSplines.Count>0&&spec.Value.HasValue)NativeSketchInitializationIdentity445.VerifyNewDriver(retainedManager,actual,receipt,current,spec,cancel);
                if(!receipt.DimId.IsNull){dimensions.Add(receipt.DimId);observed(receipt.DimId);}if(!receipt.VarId.IsNull){variables.Add(receipt.VarId);observed(receipt.VarId);}
                changes.Add(new(sketch.ID.ToString(),actual.ToString(),spec.Kind,spec.First,spec.Second,receipt.VarId.IsNull?null:receipt.VarId.ToString(),spec.Value,spec.Third));
            }
            var allowed=baselineIds.Concat(relations.Select(id=>id.ToString())).ToHashSet(StringComparer.OrdinalIgnoreCase);
            NativeSketchMembers385.Need(retained.GetProperty("constraints").EnumerateArray().All(row=>allowed.Contains(row.GetProperty("id").GetString()!)),"CONSTRAINT_BASELINE_CHANGED","The persistent sketch network contains an unrelated or unobserved relation.");
            var baselineNode=JsonNode.Parse(retained.GetRawText())!.AsObject();var baselineRows=baselineNode["constraints"]!.AsArray();
            for(int i=baselineRows.Count-1;i>=0;i--)if(!baselineIds.Contains(baselineRows[i]!["id"]!.GetValue<string>()))baselineRows.RemoveAt(i);
            var finalBaseline=(nativeSplines.Count>0?NativeSketchInitializationIdentity445.CapturePriorBaseline(retainedManager,current,baselineIds,cancel):
                NativeSketchRelations410.Capture(retainedManager,current,cancel) with{Relations=Wire.Element(baselineNode)}) with{ObservationStage=baseline.ObservationStage,PreAddUnavailableReason=baseline.PreAddUnavailableReason};
            return new(objects.ToArray(),ids.ToArray(),relations.ToArray(),dimensions.ToArray(),variables.Distinct().ToArray(),changes.ToArray(),finalBaseline,members);
        }
        finally{if(editing)sketch.ExitEditMode();}
    }
    static void RequireNativeSplines(PlanarSketch sketch,IReadOnlyDictionary<McObjectId,NativeSketchPrimitive385> requested,SketchPlaneSpec plane,Action cancel)
    {
        if(requested.Count==0)return;
        var geometry=sketch.GetAllItemsGeometry(false);NativeSketchMembers385.Need(geometry!=null,"SKETCH_GEOMETRY_UNAVAILABLE","Native Spline initialization requires the actual complete WCS element inventory.");
        try
        {
            ModelInventoryBudget.Ids(geometry.Keys,cancel);
            foreach(var pair in requested)
            {
                cancel();NativeSketchMembers385.Need(geometry.TryGetValue(pair.Key,out var actual)&&actual!=null&&actual.GeometryType==G.EntityGeomType.kSpline,
                    "SKETCH_BINDING_UNAVAILABLE","The exact new Spline ID is absent from the native WCS element inventory.");
                using var spline=geometry[pair.Key].Nurb;
                NativeSketchMembers385.Need(NativeSketchCurveGeometry445.Requested(pair.Value,NativeSketchCurveGeometry445.Spline(spline,cancel),plane,cancel),
                    "SKETCH_GEOMETRY_MISMATCH","The full native AddGeometry Spline differs from its requested WCS definition before constraints.");
            }
        }
        finally{foreach(var value in geometry.Values.Distinct(ReferenceEqualityComparer.Instance))if(value is IDisposable disposable)disposable.Dispose();}
    }
    static void PreserveBeforeSolve(NativeSketchMembers385.Snapshot before,NativeSketchMembers385.Snapshot after,
        NanoCAD.Contracts.NativeSketchInitialization385 input,NativeSketchCreated385[] objects,Action cancel)
    {
        NativeSketchMembers385.Need(before.Members.Length==after.Members.Length&&before.Members.All(prior=>after.Members.Any(current=>current.Id==prior.Id&&current.ObjectId==prior.ObjectId&&current.Segment==prior.Segment&&current.NativeType==prior.NativeType&&current.Owned==prior.Owned&&current.ReferenceOnly==prior.ReferenceOnly))&&
            before.Stored.GetProperty("owner_document_id").GetString()==after.Stored.GetProperty("owner_document_id").GetString()&&before.Stored.GetProperty("member_context_document_id").GetString()==after.Stored.GetProperty("member_context_document_id").GetString()&&
            ModelFeatureReorderState345.Equivalent(Wire.Element(before.Plane),Wire.Element(after.Plane),cancel),
            "SKETCH_BINDING_UNAVAILABLE","Native manager acquisition changed an existing input ID/type/owner/frame/reference role before solve.");
        RequireRequested(input,objects,after,cancel);NativeSketchRelations410.ImmutableReferences(before,after,cancel);
    }
    internal static void VerifyCreated(PlanarSketch sketch,NanoCAD.Contracts.NativeSketchInitialization385 input,Created created,DocumentState state,Db.Transaction tx,Action cancel)
    {
        var current=NativeSketchMembers385.Capture(sketch,state,tx,cancel);
        NativeSketchMembers385.Need(current.Members.All(m=>m.Owned||m.ReferenceOnly)&&current.Members.Where(m=>m.Owned).Select(m=>m.ObjectId).ToHashSet().SetEquals(created.ObjectIds),"SKETCH_BINDING_UNAVAILABLE","Created internal inputs changed native membership or ownership.");
        var specs=BindSpecs(input,created.Objects,current,cancel);
        if(specs.Length==0)RequireRequested(input,created.Objects,current,cancel);NativeSketchMembers385.ValidateSolved(specs,current);NativeSketchMembers385.FixedRequested(specs,created.InitialInputs,current);NativeSketchRelations410.ImmutableReferences(created.InitialInputs,current,cancel);
        VerifyRelations(sketch,specs,current,created.ConstraintIds,created.Changes,cancel,verifyNewDrivers:input.Geometry.Any(item=>item.Kind=="spline"));
        if(created.Baseline.Complete)
        {
            using var manager=NativeSketchMembers385.Manager(sketch);var actual=NativeSketchConstraintState385.Relations(manager,cancel);var baseline=NativeSketchRelations410.Retained(created.Baseline,actual,current,cancel).ToHashSet(StringComparer.OrdinalIgnoreCase);baseline.UnionWith(created.ConstraintIds.Select(id=>id.ToString()));
            NativeSketchMembers385.Need(actual.GetProperty("constraints").EnumerateArray().All(row=>baseline.Contains(row.GetProperty("id").GetString()!)),"CONSTRAINT_BASELINE_CHANGED","A relation appeared outside the captured baseline and requested persistent relations.");
        }
        else NativeSketchMembers385.Need(specs.Length==0&&created.ConstraintIds.Length==0,"CONSTRAINTS_UNAVAILABLE","Requested relation writes require an observed complete baseline; geometry-only initialization does not infer constraint preservation.");
    }
    internal static SketchConstraintSpec[] BindSpecs(NanoCAD.Contracts.NativeSketchInitialization385 input,NativeSketchCreated385[] objects,NativeSketchMembers385.Snapshot members,Action cancel)
    {
        var bindings=objects.ToDictionary(row=>row.key,row=>row.object_id,StringComparer.Ordinal);
        foreach(string sourceId in input.ReferenceIds??[])
        {
            cancel();var candidates=new List<NativeSketchMembers385.Member>();
            foreach(var member in members.Members.Where(member=>member.ReferenceOnly&&!member.Owned&&member.Segment==null&&member.Id==member.ObjectId))
            {
                var projection=member.ObjectId.GetObjectOfType<ProjectionSketchEnt>();
                if(projection==null||!projection.SrcObjectID.ToString().Equals(sourceId,StringComparison.OrdinalIgnoreCase))continue;
                NativeSketchMembers385.Need(projection.ID==member.ObjectId&&projection.HostSketchID==members.Sketch.ID&&members.Stored.GetProperty("projection_ids").EnumerateArray().Any(id=>id.GetString()!.Equals(projection.ID.ToString(),StringComparison.OrdinalIgnoreCase))&&projection.Status.ToString()=="Ok"&&!projection.IsSuppressed&&projection.DbEntity.Document?.ID==members.Sketch.DbEntity.Document.ID,
                    "SKETCH_BINDING_UNAVAILABLE","An initial native reference lost its exact source/projected member/sketch/owner binding.");candidates.Add(member);
            }
            NativeSketchMembers385.Need(candidates.Count==1&&!bindings.ContainsKey(sourceId),"SKETCH_BINDING_UNAVAILABLE","An initial source reference must map to one exact current immutable projection in this sketch.");
            bindings.Add(sourceId,candidates[0].Id.ToString());
        }
        string Bound(string key)
        {NativeSketchMembers385.Need(bindings.TryGetValue(key,out var id),"SKETCH_BINDING_UNAVAILABLE","An initial constraint operand is neither a created geometry key nor a verified declared native projection source.");return id!;}
        return input.Constraints.Select(spec=>spec with{First=Bound(spec.First),Second=spec.Second==null?null:Bound(spec.Second),Third=spec.Third==null?null:Bound(spec.Third)}).ToArray();
    }
    internal static void RequireRequested(NanoCAD.Contracts.NativeSketchInitialization385 input,NativeSketchCreated385[] objects,NativeSketchMembers385.Snapshot current,Action cancel)
    {
        bool Point(System.Text.Json.JsonElement value,Point3 expected)=>ModelParameterSnapshot.Near(value[0].GetDouble(),expected.X)&&ModelParameterSnapshot.Near(value[1].GetDouble(),expected.Y)&&ModelParameterSnapshot.Near(value[2].GetDouble(),expected.Z);
        foreach(var requested in input.Geometry)
        {
            cancel();string id=objects.Single(o=>o.key==requested.Key).object_id;var member=current.Members.Single(m=>m.ObjectId.ToString()==id&&m.Segment==null);var actual=member.Geometry;bool matches;
            if(requested.Kind=="line")matches=actual.GetProperty("kind").GetString()=="line"&&Point(actual.GetProperty("start"),requested.Points[0])&&Point(actual.GetProperty("end"),requested.Points[1]);
            else if(requested.Kind=="arc")
            {
                var expected=NativeSketchArcGeometry390.Curve(requested.Points,current.Plane,"native_geometry."+requested.Key);
                matches=DrawingProfileGeometry.Same(member.Curve,expected)&&DrawingProfileGeometry.Same(NativeSketchMembers385.PointLocal(current,member,"start"),DrawingWorkplane.Local(current.Plane,requested.Points[0]))&&DrawingProfileGeometry.Same(NativeSketchMembers385.PointLocal(current,member,"end"),DrawingWorkplane.Local(current.Plane,requested.Points[2]));
            }
            else if(requested.Kind is "spline" or "ellipse")matches=NativeSketchCurveGeometry445.Requested(requested,actual,current.Plane,cancel);
            else{var normal=DrawingWorkplane.Normal(current.Plane);var observed=actual.GetProperty("normal");matches=actual.GetProperty("kind").GetString()=="circular_arc"&&Point(actual.GetProperty("center"),requested.Points[0])&&ModelParameterSnapshot.Near(actual.GetProperty("radius").GetDouble(),requested.Radius!.Value)&&ModelParameterSnapshot.Near(Math.Abs(actual.GetProperty("end_angle").GetDouble()-actual.GetProperty("start_angle").GetDouble()),2*Math.PI)&&Math.Abs(observed[0].GetDouble()*normal.X+observed[1].GetDouble()*normal.Y+observed[2].GetDouble()*normal.Z-1)<=1e-8;}
            NativeSketchMembers385.Need(matches,"SKETCH_GEOMETRY_MISMATCH","The actual new native input differs from its requested WCS geometry before solve; no local-frame convention is guessed.");
        }
    }
    internal static void VerifyRelations(PlanarSketch sketch,SketchConstraintSpec[] specs,NativeSketchMembers385.Snapshot members,McObjectId[] ids,SketchConstraintChange[] changes,Action cancel,bool verifyNewDrivers=false)
    {
        if(specs.Length==0)return;using var manager=NativeSketchMembers385.Manager(sketch);var actual=manager.GetAllConstraints();
        NativeSketchMembers385.Need(actual!=null&&ids.All(actual.Contains)&&ids.Length==specs.Length&&changes.Length==specs.Length,"CONSTRAINT_SCOPE_MISMATCH","Requested persistent relations are absent from the current sketch network.");
        for(int i=0;i<specs.Length;i++)
        {cancel();using var info=manager.GetConstraintInfo(ids[i]);using var expected=NativeSketchMembers385.Info(specs[i],members);NativeSketchMembers385.Need(info!=null&&NativeSketchMembers385.RequestedReferences(info,expected,specs[i],members),"CONSTRAINT_SCOPE_MISMATCH","A created relation changed type/member binding after solve.");NativeSketchRelations410.RequirePointReceipt(specs[i],info,members);if(specs[i].Value.HasValue){NativeSketchMembers385.Need(info.IsDimensional&&!info.VarId.IsNull&&info.VarId.ToString()==changes[i].variable_id&&manager.GetConstraintParamId(ids[i])==info.VarId&&ModelParameterSnapshot.Near(manager.GetConstraintParamValue(ids[i]),specs[i].Value!.Value),"CONSTRAINT_PARAMETER_UNAVAILABLE","A created control changed persistent dimensional/variable binding/value after solve.");if(verifyNewDrivers)NativeSketchInitializationIdentity445.VerifyNewDriver(manager,ids[i],info,members,specs[i],cancel);}}
    }
}
