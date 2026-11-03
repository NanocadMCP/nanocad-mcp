using Multicad;
using Multicad.DatabaseServices;
using Multicad.DatabaseServices.Constraints;
using Multicad.Mc3D;
using NanoCAD.Contracts;
using NanoCAD.Ipc;
using Db=Teigha.DatabaseServices;
using MPoint=Multicad.Geometry.Point3d;
using MPlane=Multicad.Geometry.Plane3d;

namespace NanoCADBridge;

internal static partial class DrawingProfileWriter
{
    private static McObjectId Add(Constraints2D manager,SketchConstraintSpec spec,Dictionary<string,Source> sources)
    {
        if(spec.Kind=="smooth")spec=SplineConstraintGeometry445.NativeOrder(spec,
            DrawingConstraintOperands431.Resolve(sources[spec.First].Curve,spec.FirstSegmentIndex).Kind,
            DrawingConstraintOperands431.Resolve(sources[spec.Second!].Curve,spec.SecondSegmentIndex).Kind);
        var a=sources[spec.First];var b=spec.Second==null?null:sources[spec.Second];
        McObjectId Operand(Source s,int? segment)
        {if(!segment.HasValue)return s.Native;_=DrawingConstraintOperands431.Resolve(s.Curve,segment);return ConstraintInfo.MakePolylineSegmentId(s.Native,segment.Value);}
        MPoint Point(Source s,string? name,int? segment){var p=DrawingConstraintOperands431.Point(s.Curve,segment,name);return DrawingWorkplaneNative.Point(s.Workplane,p);}
        var first=Operand(a,spec.FirstSegmentIndex);var second=b==null?McObjectId.Null:Operand(b,spec.SecondSegmentIndex);
        MPoint Sector(){var p=DrawingAngularGeometry.Measure(DrawingConstraintOperands431.Resolve(a.Curve,spec.FirstSegmentIndex),DrawingConstraintOperands431.Resolve(b!.Curve,spec.SecondSegmentIndex)).Annotation;return DrawingWorkplaneNative.Point(a.Workplane,p);}
        return spec.Kind switch
        {
            "horizontal"=>manager.AddGeomHorizontal(first),"vertical"=>manager.AddGeomVertical(first),
            "parallel"=>manager.AddGeomParallel(first,second),"perpendicular"=>manager.AddGeomPerpendicular(first,second),
            "equal"=>manager.AddGeomEqual(first,second),"concentric"=>manager.AddGeomConcentric(first,second),
            "collinear"=>manager.AddGeomCollinear(first,second),"tangent"=>manager.AddGeomTangent(first,second),
            "fix"=>manager.AddGeomFix(first),
            "symmetric"=>manager.AddGeomSymmetric(first,second,Operand(sources[spec.Third!],spec.ThirdSegmentIndex)),
            "fix_point"=>manager.AddGeomFix(first,Point(a,spec.FirstPoint,spec.FirstSegmentIndex)),
            "coincident"=>manager.AddGeomCoincident(first,second,Point(a,spec.FirstPoint,spec.FirstSegmentIndex),Point(b!,spec.SecondPoint,spec.SecondSegmentIndex)),
            "smooth"=>manager.AddGeomSmooth(first,second,Point(a,spec.FirstPoint,spec.FirstSegmentIndex),Point(b!,spec.SecondPoint,spec.SecondSegmentIndex)),
            "radius"=>manager.AddDimRadial(first),"diameter"=>manager.AddDimDiametrical(first),
            "length"=>manager.AddDimAligned(first,first,Point(a,"start",spec.FirstSegmentIndex),Point(a,"end",spec.FirstSegmentIndex)),
            "distance"=>manager.AddDimAligned(first,second,Point(a,spec.FirstPoint,spec.FirstSegmentIndex),Point(b!,spec.SecondPoint,spec.SecondSegmentIndex)),
            "distance_x"=>manager.AddDimHorizontal(first,second,Point(a,spec.FirstPoint,spec.FirstSegmentIndex),Point(b!,spec.SecondPoint,spec.SecondSegmentIndex)),
            "distance_y"=>manager.AddDimVertical(first,second,Point(a,spec.FirstPoint,spec.FirstSegmentIndex),Point(b!,spec.SecondPoint,spec.SecondSegmentIndex)),
            "angle"=>manager.AddDimAngular(first,second,Sector()),
            _=>throw new DomainException("INVALID_ARGUMENT","Unsupported drawing relation.")
        };
    }
    private static Network VerifyNetwork(DrawingProfileEdit edit,Binding before,McObjectId[] added,DocumentState state,Action cancel,VariablePlan? variables=null)
    {
        using var tx=state.Document.Database.TransactionManager.StartTransaction();
        var selected=before.Selected.Values.Select(s=>SourceOf(s.Id,state,tx,true,s.Workplane,cancel)).ToDictionary(s=>s.Curve.Id,StringComparer.OrdinalIgnoreCase);
        var after=ReadNetwork(before.Sheet,selected,state,tx,cancel);Need(after.Available,"CONSTRAINTS_UNAVAILABLE","Resulting network inventory is unavailable.");
        var previous=before.Network.Relations.Where(r=>edit.ConstraintIds==null||!edit.ConstraintIds.Contains(Key(r.Id),StringComparer.OrdinalIgnoreCase)).ToDictionary(r=>r.Id);var current=after.Relations.ToDictionary(r=>r.Id);
        Need(added.Distinct().Count()==added.Length&&current.Keys.ToHashSet().SetEquals(previous.Keys.Concat(added)),"CONSTRAINTS_UNAVAILABLE","Native relation inventory differs from previous plus requested additions.");
        foreach(var old in previous.Values)
        {
            var now=current[old.Id];bool changed=edit.Action=="set_drawing_dimension"&&old.Id.ToString().Equals(edit.ConstraintId,StringComparison.OrdinalIgnoreCase);
            string? expectedName=variables!=null&&!old.Variable.IsNull?variables.Names[Key(old.Variable)]:old.Name;
            Need(old.Type==now.Type&&old.References==now.References&&old.Variable==now.Variable&&expectedName==now.Name&&(old.Spec==null&&now.Spec==null||old.Spec!=null&&now.Spec!=null&&SameRelation(old.Spec,now.Spec,selected)),"CONSTRAINT_SCOPE_MISMATCH","Existing relation identity or point binding changed.");
            if(variables!=null&&!old.Variable.IsNull)
                Need(now.Value.HasValue&&DrawingProfileGeometry.Near(now.Value.Value,variables.Values[Key(old.Variable)])&&now.Expression!=null&&ModelArithmetic.ParseStored(now.Expression,cancel).Canonical==ModelArithmetic.ParseStored(variables.Expressions[Key(old.Variable)],cancel).Canonical,"NATIVE_GEOMETRY_MISMATCH","Dimension did not follow its planned variable expression/value.");
            else Need(changed?now.Value.HasValue&&DrawingProfileGeometry.Near(now.Value.Value,edit.Value!.Value):old.Expression==now.Expression&&(!old.Value.HasValue&&!now.Value.HasValue||old.Value.HasValue&&now.Value.HasValue&&DrawingProfileGeometry.Near(old.Value.Value,now.Value.Value)),"NATIVE_GEOMETRY_MISMATCH","Unexpected driving value/expression after solve.");
        }
        for(int i=0;i<added.Length;i++)
        {
            var actual=current[added[i]];var requested=edit.Constraints![i];
            if(actual.Spec==null||!SameRelation(actual.Spec,requested,selected))throw new DomainException("CONSTRAINT_SCOPE_MISMATCH","Created relation has different type/sources/point references.",
                details:new{constraint_id=added[i].ToString(),requested,observed=actual.Spec});
            if(requested.Value.HasValue&&(!actual.Value.HasValue||!DrawingProfileGeometry.Near(actual.Value.Value,requested.Value.Value)))
                throw new DomainException("NATIVE_GEOMETRY_MISMATCH","Created dimension value differs from current geometry.",details:new{kind=requested.Kind,expected=requested.Value,observed=actual.Value,native_expression=actual.Expression});
        }
        foreach(var old in before.Network.Sources.Values)
        {
            Need(after.Sources.TryGetValue(old.Curve.Id,out var now)&&now.Native==old.Native&&now.Layer==old.Layer,"OBJECT_IDENTITY_MISMATCH","Observed source identity or layer changed.");
            Need(DrawingConstraints431.ParentPreserved(old,now),"OBJECT_IDENTITY_MISMATCH","Native solver changed the retained Polyline frame, topology or widths.");
            if(edit.Action is "add_drawing_constraints" or "create_drawing_variable" or "name_drawing_parameter"||IsRemoval(edit)||!before.Selected.ContainsKey(old.Curve.Id))Need(DrawingProfileGeometry.Same(old.Curve,now.Curve),"NATIVE_GEOMETRY_MISMATCH","Constraint write changed geometry that should remain unchanged.");
        }
        foreach(var relation in after.Relations.Where(r=>r.Spec!=null))
        {
            var spec=relation.Spec!;
            if(spec.Kind=="fix_point")
            {
                // Existing anchors remain fixed even if other DOFs are free.
                var old=before.Selected[spec.First].Curve;Need(DrawingProfileGeometry.Same(DrawingConstraintOperands431.Point(old,spec.FirstSegmentIndex,spec.FirstPoint),DrawingConstraintOperands431.Point(selected[spec.First].Curve,spec.FirstSegmentIndex,spec.FirstPoint)),"NATIVE_GEOMETRY_MISMATCH","A fixed point moved.");
            }
            if(spec.Kind=="fix")Need(DrawingProfileGeometry.Same(before.Selected[spec.First].Curve,selected[spec.First].Curve),"NATIVE_GEOMETRY_MISMATCH","A fixed complete drawing object changed geometry.");
            DrawingProfileGeometry.Validate(new[]{spec},selected.Values.Select(s=>s.Curve).ToArray());
        }
        return after;
    }
    internal static Result Execute(ModifyArguments args,DocumentState state,WriteJob job,ReadContext context,JsonLog log)
    {
        bool started=false;string stage="preparation";var added=new List<McObjectId>();
        void Trace(string value){stage=value;log.Write(job.Operation,job.OriginalRequestId,"drawing_profile_checkpoint",transaction:value,operationId:job.OperationId);}
        try
        {
            var edit=(DrawingProfileEdit)args.Operations.Single();Binding binding;List<Consumer> consumers;
            using(var tx=state.Document.Database.TransactionManager.StartTransaction())
            {binding=Resolve(edit,state,tx,job.CheckCancellation);consumers=edit.Action is "extrude_drawing_profile" or "revolve_drawing_profile"?new():Consumers(binding.Selected,state,tx,job.CheckCancellation);}
            var previousConsumers=consumers.ToArray();
            RequireRetainedSweepEdit366(edit,binding,consumers);
            Consumer? created=null;Network? after=null;var changes=new List<DrawingRelationChange>();
            var variables=DrawingVariablePlanning.IsAction(edit.Action)?PrepareVariables(edit,binding,state,job.CheckCancellation):null;
            var removal=IsRemoval(edit)?PrepareRemoval(edit,binding,job.CheckCancellation):null;string[]? removedVariables=null;
            McObjectId createdVariable=McObjectId.Null;DrawingVariableChange[]? variableChanges=null;job.CheckCancellation();
            if(edit.Action is "extrude_drawing_profile" or "revolve_drawing_profile")
            {started=true;Trace("profile_create_before");created=CreateProfile(edit,binding,state,job.CheckCancellation);consumers.Add(created);}
            else
            {
                started=true;Trace("constraints_before");
                if(removal!=null)WriteRemoval(binding,removal,job.CheckCancellation);
                else if(variables!=null)createdVariable=WriteVariables(edit,binding,variables,state);
                else using(var plane=DrawingWorkplaneNative.Plane(binding.Selected.Values.First().Workplane))using(var manager=new Constraints2D(plane))
                {
                    Need(manager.DocumentId==binding.Sheet.ID&&manager.PlanarSketchId.IsNull&&manager.ConstraintsNetworkType==Constraint2dNetType.ACAD,"CONSTRAINT_SCOPE_MISMATCH","Constructor resolved another network.");
                    if(edit.Action=="add_drawing_constraints")
                        foreach(var spec in edit.Constraints!)
                        {job.CheckCancellation();var id=Add(manager,spec,binding.Selected);Need(!id.IsNull,"CONSTRAINT_REJECTED","Native solver rejected "+spec.Kind+"; some earlier additions may remain.");added.Add(id);}
                    else
                    {
                        var relation=FindDimension(edit,binding.Network);Need(manager.SetConstraintParamValue(relation.Id,edit.Value!.Value),"CONSTRAINT_SOLVE_FAILED","Native dimension update failed.");
                        Need(manager.ApplyAllConstraints(),"CONSTRAINT_SOLVE_FAILED","Native constraint solve failed.");
                    }
                    // AddGeom*/AddDim* perform their own application, as in the
                    // official ordinary-drawing samples. Do not run a second
                    // blanket ApplyAll over already-applied additions. Verify
                    // actual native relations, dimensions and geometry below.
                }
                // Same lifecycle as accepted146: dispose solver/variable wrappers
                // before acquiring fresh model wrappers and rebuilding consumers.
                McObjectManager.UpdateAll();Trace("rebuild_before");
                foreach(var consumer in edit.Action is "create_drawing_variable" or "name_drawing_parameter"||IsRemoval(edit)?new List<Consumer>():consumers)
                {job.CheckCancellation();var body=consumer.Native.GetObjectOfType<Mc3dSolid>();Need(body!=null&&body.RebuildPart(),"MODEL_UPDATE_FAILED","Dependent profile rebuild rejected.");}
                McObjectManager.UpdateAll();Trace("network_verify");after=VerifyNetwork(edit,binding,added.ToArray(),state,job.CheckCancellation,variables);
                if(variables!=null)variableChanges=VerifyVariables(edit,binding,variables,createdVariable,job.CheckCancellation);
                if(removal!=null)removedVariables=VerifyRemoval(binding,removal,job.CheckCancellation);
                foreach(var relation in after.Relations.Where(r=>added.Contains(r.Id)||edit.Action=="set_drawing_dimension"&&r.Id.ToString().Equals(edit.ConstraintId,StringComparison.OrdinalIgnoreCase)||variables!=null&&variables.Affected.Contains(Key(r.Variable))))
                {var s=relation.Spec!;changes.Add(new(relation.Id.ToString(),s.Kind,s.First,s.Second,relation.Variable.IsNull?null:relation.Variable.ToString(),relation.Value,s.Third,s.FirstSegmentIndex,s.SecondSegmentIndex,s.ThirdSegmentIndex));}
            }
            Dictionary<string,Source> selected;
            using(var tx=state.Document.Database.TransactionManager.StartTransaction())
            {selected=binding.Selected.Values.Select(s=>SourceOf(s.Id,state,tx,true,s.Workplane,job.CheckCancellation)).ToDictionary(s=>s.Curve.Id,StringComparer.OrdinalIgnoreCase);}
            if(created!=null)foreach(var old in binding.Selected.Values)Need(DrawingProfileGeometry.Same(old.Curve,selected[old.Curve.Id].Curve)&&old.Layer==selected[old.Curve.Id].Layer,"NATIVE_GEOMETRY_MISMATCH","Projection creation changed a source.");
            Trace("models_verify");consumers=VerifyModels(consumers,selected,state,job.CheckCancellation);job.CheckCancellation();job.BeginCommit();
            if(created!=null)created=consumers.Single(c=>c.Native==created.Native);
            EntityRef Ref(Db.ObjectId id)=>new(args.BridgeId,args.DocumentId,id.Handle.ToString());
            var sourceRefs=selected.Values.Select(s=>Ref(s.Id)).ToArray();var bodyRefs=consumers.Select(c=>Ref(c.Id)).ToArray();
            var retained=consumers.Where(c=>c.RetainedSweeps!=null).Select(c=>
            {
                var before=previousConsumers.Single(old=>old.Native==c.Native).RetainedSweeps!;var after=c.RetainedSweeps!;
                return new DrawingRetainedSweepPart366(Ref(c.Id),Key(c.Native),after.Owner,Key(after.Sketch),edit.ConstraintId!,after.SourceVariable,after.SourceId,before.Radius,after.Radius,after.Consumers,
                    "actual_ACAD_source_dimension_and_same_native_part_sketch_profiles_sweeps; ordered_paths_drivers_metadata_and_peers_retained; private_sketch_solver_internals_not_claimed");
            }).ToArray();
            var change=new DrawingProfileChange(edit.Action,sourceRefs,changes.ToArray(),created==null?null:Ref(created.Id),created?.Sketch.ToString(),created?.Projections.Select(p=>p.ToString()).ToArray()??Array.Empty<string>(),created?.Feature.ToString(),created==null?bodyRefs:Array.Empty<EntityRef>(),variableChanges,removal?.Relations.Select(r=>Key(r.Id)).ToArray(),removedVariables,retained.Length==0?null:retained);
            var replaced=previousConsumers.Where(old=>consumers.Any(now=>now.Native==old.Native&&now.Id!=old.Id)).ToArray();
            var newRepresentations=consumers.Where(now=>replaced.Any(old=>old.Native==now.Native)).Select(c=>Ref(c.Id)).ToArray();
            var data=new ModifiedEntities(args.OperationId,"committed",false,created==null?sourceRefs.Concat(bodyRefs).ToArray():Array.Empty<EntityRef>(),replaced.Select(c=>Ref(c.Id)).ToArray(),created==null?newRepresentations:new[]{Ref(created.Id)},Array.Empty<CopyMapping>(),"not_guaranteed",drawing_profile_changes:new[]{change});
            var warnings=new List<string>{WriteRecovery.Warning,"DRAWING_PROFILE_SCOPE: Native relations, their values and dependent model history were checked. Full constraint status is not inferred; an underconstrained component may move within its remaining degrees of freedom. Inspect the rebuilt shape and an MCP image after dimensional edits."};
            if(edit.Action=="add_drawing_constraints"&&!binding.Network.Available)warnings.Add("CONSTRAINT_INVENTORY_INITIALIZED: Prior inventory unavailable; resulting network contains exactly the returned new relations. No prior-empty-network claim.");
            warnings.AddRange(consumers.Where(c=>c.RetainedSweeps!=null).SelectMany(c=>c.RetainedSweeps!.Warnings).Distinct(StringComparer.Ordinal));
            var result=Result.Ok(job.OriginalRequestId,data,context,warnings.ToArray());job.RecordCommit(result);Trace("commit_recorded");return result;
        }
        catch(Exception error)
        {
            if(job.CommittedResult is {} committed)return committed;
            string cause=error is DomainException d?d.Error.code:error is OperationCanceledException?"CANCELLED":"DRAWING_PROFILE_WRITE_FAILED";
            return job.Error(started?"WRITE_OUTCOME_UNKNOWN":cause,started?"Drawing profile result was not verified; inspect affected sources and models before another dependent edit. No rollback is claimed.":error.Message,started?"unknown":"not_started",context,
                new{cause,stage,message=error.Message,verification_details=(error as DomainException)?.Error.details,added_constraint_ids=added.Select(v=>v.ToString()).ToArray()}) with{warnings=new[]{WriteRecovery.Warning}};
        }
    }
}
