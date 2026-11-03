using System.Text.Json;
using System.Text.Json.Nodes;
using Multicad;
using Multicad.Mc3D;
using NanoCAD.Contracts;
using Db=Teigha.DatabaseServices;

namespace NanoCADBridge;

// Supplemental preservation for native sketch controls. The generic graph owns
// every variable/formula check; these snapshots retain the actual relations,
// member roles, anchors, profiles, metadata and native consumer composition.
internal static class ModelSketchParameterGraph411
{
    static void Need(bool ok,string code,string message)=>NativeSketchMembers385.Need(ok,code,message);
    static string Key(McObjectId id)=>id.ToString();
    static bool Same(string? a,string? b)=>string.Equals(a,b,StringComparison.OrdinalIgnoreCase);
    // Renaming a User driver retains the former outside-editor solve path;
    // it has no requested geometric value change requiring the Spline route.
    internal static void Solve(ModelParameterBody body,IReadOnlySet<string> affected,Action cancel)
        =>SolveCore(body,affected,null,null,cancel);
    internal static NativeSketchEditorSolve445.Pending[] Solve(ModelParameterBody body,IReadOnlySet<string> affected,DocumentState document,NativeSketchConstraintState385.Snapshot original,Action cancel)
        =>SolveCore(body,affected,document,original,cancel);
    static NativeSketchEditorSolve445.Pending[] SolveCore(ModelParameterBody body,IReadOnlySet<string> affected,DocumentState? document,NativeSketchConstraintState385.Snapshot? original,Action cancel)
    {
        var transitions=new List<NativeSketchEditorSolve445.Pending>();
        var sketches=ModelInventoryBudget.History(body.Body,cancel).Where(id=>affected.Contains(Key(id))).Select(id=>id.GetObjectOfType<PlanarSketch>()).Where(s=>s!=null).Select(s=>s!).ToArray();
        var pending=sketches.ToDictionary(s=>Key(s.ID),StringComparer.OrdinalIgnoreCase);
        var downstream=sketches.ToDictionary(s=>Key(s.ID),s=>ModelDependencyGraph410.Closure(body.Before,[Key(s.ID)],cancel),StringComparer.OrdinalIgnoreCase);
        while(pending.Count>0)
        {
            cancel();var ready=pending.Values.Where(s=>!pending.Keys.Any(other=>!Same(other,Key(s.ID))&&downstream[other].Contains(Key(s.ID)))).ToArray();
            Need(ready.Length>0,"MODEL_DEPENDENCY_CHANGED","Affected sketch dependencies contain a cycle and have no verified solve order.");
            foreach(var sketch in ready)
            {
                cancel();Need(sketch.ID.GetObjectOfType<PlanarSketch>()?.HostSolidID==body.Body.ID&&!sketch.IsInEditMode&&(sketch.Status.ToString()=="Ok"||ProjectedHistorySources.HealthySuppressed(sketch)),
                    "MODEL_STATE_UNAVAILABLE","An affected sketch is unavailable for its explicit native solve.");
                if(!sketch.IsSuppressed)
                {
                    bool hasRelations,updatedByEditor=false;
                    using(var manager=NativeSketchMembers385.Manager(sketch))
                    {var relations=manager.GetAllConstraints();hasRelations=relations!=null&&relations.Any();}
                    // A geometry-only peer can have no materialized network.
                    // Its existing unavailable observation is retained by the
                    // supplemental snapshot; body rebuild still follows.
                    if(hasRelations)
                    {
                        // Release the outside Auto interface before entering
                        // the SDK's distinct current editor/MCS solve context.
                        if(document!=null&&NativeSketchEditorSolve445.HasOwnedSpline(sketch,cancel))
                        {Need(original!=null,"SKETCH_CORRESPONDENCE_UNAVAILABLE","The original pre-setter sketch design is unavailable.");transitions.Add(NativeSketchEditorSolve445.Solve(sketch,document,original!,cancel));updatedByEditor=true;}
                        else Need(sketch.ApplyConstraints(true),"CONSTRAINT_SOLVE_FAILED","An affected native sketch refused its explicit solve.");
                    }
                    if(!updatedByEditor)sketch.DbEntity.Update();
                }
                pending.Remove(Key(sketch.ID));
            }
        }
        return transitions.ToArray();
    }
    static HashSet<string> Stationary(NativeSketchConstraintState385.Snapshot before,IReadOnlySet<string> affected)
    {
        var result=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach(var id in before.Part.Contents)
        {
            var sketch=id.GetObjectOfType<PlanarSketch>();if(sketch==null)continue;var support=sketch.PlanarEntityID;
            // Explicit planes are stationary. A retained exact same-part face
            // or affected datum can legitimately move its native sketch frame.
            bool moving=!support.IsNull&&(support.IsFace&&support.ParentId==before.Part.Body.ID&&affected.Contains(Key(support.HostFeatureOrParentId))||affected.Contains(Key(support)));
            if(!moving)result.Add(Key(id));
        }
        return result;
    }
    static bool MovingReference(NativeSketchMembers385.Member member,NativeSketchMembers385.Snapshot inputs,McObjectId body,IReadOnlySet<string> affected)
    {
        if(!member.ReferenceOnly||affected.Count==0)return false;
        var projection=member.Id.GetObjectOfType<ProjectionSketchEnt>();
        return projection!=null&&projection.ID==member.Id&&projection.HostSketchID==inputs.Sketch.ID&&projection.HostSolidID==body&&
            projection.SrcObjectID.IsEdge&&projection.SrcObjectID.ParentId==body&&affected.Contains(Key(projection.SrcObjectID.HostFeatureOrParentId))&&projection.DbEntity.Document.ID==inputs.Sketch.DbEntity.Document.ID;
    }
    static double[] Local(NativeSketchMembers385.Snapshot inputs,JsonElement point)
    {
        var p=new Point3(point[0].GetDouble(),point[1].GetDouble(),point[2].GetDouble());var local=DrawingWorkplane.Local(inputs.Plane,p);var normal=DrawingWorkplane.Normal(inputs.Plane);
        return [local.X,local.Y,(p.X-inputs.Plane.Origin.X)*normal.X+(p.Y-inputs.Plane.Origin.Y)*normal.Y+(p.Z-inputs.Plane.Origin.Z)*normal.Z];
    }
    static void Anchors(NativeSketchConstraintState385.Snapshot before,NativeSketchConstraintState385.Snapshot after,IReadOnlySet<string> affected,bool stationary,Action cancel)
    {
        foreach(var prior in before.Inputs.Members.Where(m=>m.ReferenceOnly))
        {
            cancel();var current=after.Inputs.Members.Where(m=>m.Id==prior.Id&&m.ObjectId==prior.ObjectId&&m.Segment==prior.Segment&&m.ReferenceOnly).ToArray();
            Need(current.Length==1,"PROJECTION_REFERENCE_CHANGED","A retained projection lost its exact member binding.");
            if(stationary&&!MovingReference(prior,before.Inputs,before.Part.Body.ID,affected))Need(DrawingProfileGeometry.Same(prior.Curve,current[0].Curve),"PROJECTION_REFERENCE_CHANGED","An immutable drawing/source projection moved.");
        }
        foreach(var prior in before.Inputs.Construction)
        {
            cancel();var current=after.Inputs.Construction.Where(p=>p.Id==prior.Id).ToArray();Need(current.Length==1,"CONSTRAINT_ANCHOR_CHANGED","An actual native construction anchor disappeared.");
            var a=Local(before.Inputs,prior.Geometry.GetProperty("point"));var b=Local(after.Inputs,current[0].Geometry.GetProperty("point"));
            Need(a.Zip(b).All(pair=>ModelParameterSnapshot.Near(pair.First,pair.Second)),"CONSTRAINT_ANCHOR_CHANGED","A native construction anchor moved in its retained sketch frame.");
        }
        // The existing fixed-anchor proof is exact in WCS for stationary
        // sketches. Moving support frames use the same native roles in local
        // coordinates, never a coordinate search for a replacement identity.
        if(stationary)
        {
            var immutable=before.Inputs with{Members=before.Inputs.Members.Where(m=>!MovingReference(m,before.Inputs,before.Part.Body.ID,affected)).ToArray()};
            NativeSketchConstraintState385.FixedAnchors(before with{Inputs=immutable},after,cancel);return;
        }
        foreach(var relation in before.Relations.GetProperty("constraints").EnumerateArray().Where(r=>r.GetProperty("type").GetString()=="GeomFix"))
        {
            cancel();var reference=relation.GetProperty("first");if(reference.ValueKind!=JsonValueKind.Object)continue;string id=reference.GetProperty("object_id").GetString()!;
            var old=before.Inputs.Members.Where(m=>Same(Key(m.Id),id)||Same(Key(m.ObjectId),id)).ToArray();
            foreach(var member in old)
            {
                var current=after.Inputs.Members.Single(m=>m.Id==member.Id);string? role=NativeSketchMembers385.PointRole(before.Inputs,member,reference);
                if(role!=null)Need(DrawingProfileGeometry.Same(NativeSketchMembers385.PointLocal(before.Inputs,member,role),NativeSketchMembers385.PointLocal(after.Inputs,current,role)),"CONSTRAINT_ANCHOR_CHANGED","A fixed native point moved relative to its retained support frame.");
                else Need(DrawingProfileGeometry.Same(member.Curve,current.Curve),"CONSTRAINT_ANCHOR_CHANGED","A fixed native member changed in its retained support frame.");
            }
        }
    }
    static void Frame(JsonNode? node,Action cancel)
    {
        cancel();if(node is JsonObject obj)
        {
            foreach(string field in new[]{"origin_wcs","x_step_wcs","y_step_wcs","plane_normal_wcs","normal","reference_direction"})if(obj.ContainsKey(field))obj[field]=null;
            if(obj["frame"] is JsonObject frame)foreach(string field in new[]{"origin_wcs","x_step_wcs","y_step_wcs","plane_normal_wcs","x_axis_wcs","y_axis_wcs","normal_wcs"})if(frame.ContainsKey(field))frame[field]=null;
            foreach(var field in obj.ToArray())Frame(field.Value,cancel);
        }
        else if(node is JsonArray array)foreach(var value in array)Frame(value,cancel);
    }
    static JsonElement Protected(NativeSketchConstraintState385.Snapshot value,IReadOnlySet<string> affected,IReadOnlySet<string> stationary,
        IReadOnlySet<string> controlledSketches,IEnumerable<ModelFeatureVariable> features,Action cancel)
    {
        var result=JsonNode.Parse(NativeSketchConstraintState385.Protected(value.Part,value.Sketch,value.Inputs,[],null,cancel,affected:affected,stationaryRoots:stationary).GetRawText())!.AsObject();
        var part=result["part"]!.AsObject();part.Remove("variables"); // Generic graph verifies every formula/value/type/owner/dependency.
        part["nodes"]=JsonNode.Parse(ModelDependencyGraph410.ProtectedNodes(Wire.Element(part),affected,features,cancel:cancel).GetRawText());
        // One expression may drive several exact same-part sketch controls.
        // A stationary support frame does not freeze their owned geometry.
        // Normalize only their duplicate numeric geometry observations here;
        // each control's own inputs, solved value, anchors and peers are still
        // independently verified below before the graph edit can commit.
        foreach(var sketch in part["sketches"]!.AsArray())
        {
            cancel();string id=sketch!["id"]!.GetValue<string>();if(!controlledSketches.Contains(id))continue;
            Need(affected.Contains(id),"MODEL_DEPENDENCY_CHANGED","A controlled native sketch is outside its actual affected part graph.");
            NativeSketchGeometry385.Motion(sketch["geometry"],cancel);
            if(sketch["retained"] is JsonObject retained)NativeSketchGeometry385.Motion(retained["geometry"],cancel);
        }
        bool moving=!stationary.Contains(Key(value.Sketch.Sketch.ID));
        var inputs=result["native_inputs"]!.AsObject();
        if(moving)
        {
            inputs["plane"]=null;Frame(result["selected_sketch"]?["geometry"],cancel);
            foreach(var point in inputs["construction"]!.AsArray())
            {
                var id=point!["id"]!.GetValue<string>();var actual=value.Inputs.Construction.Single(c=>Same(Key(c.Id),id));point["geometry"]!["point"]=JsonSerializer.SerializeToNode(Local(value.Inputs,actual.Geometry.GetProperty("point")),Wire.Json);
            }
            foreach(var profile in result["selected_sketch"]!["profiles"]!.AsArray()){Frame(profile?["state"]?["geometry"],cancel);Frame(profile?["state"]?["selected_profile_geometry"],cancel);}
        }
        foreach(var row in inputs["members"]!.AsArray())
        {
            string id=row!["member_id"]!.GetValue<string>();var member=value.Inputs.Members.Single(m=>Same(Key(m.Id),id));
            if(moving&&member.ReferenceOnly||MovingReference(member,value.Inputs,value.Part.Body.ID,affected))NativeSketchGeometry385.Motion(row,cancel);
            if(moving||MovingReference(member,value.Inputs,value.Part.Body.ID,affected))Frame(row["geometry"],cancel);
        }
        var movingSources=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach(var id in value.Part.Contents)
        {
            cancel();var projection=id.GetObjectOfType<ProjectionSketchEnt>();if(projection==null)continue;var source=projection.SrcObjectID;
            if(projection.ID==id&&projection.HostSolidID==value.Part.Body.ID&&projection.DbEntity.Document.ID==value.Part.Body.DbEntity.Document.ID&&
                source.IsEdge&&source.ParentId==value.Part.Body.ID&&affected.Contains(Key(source.HostFeatureOrParentId)))movingSources.Add(Key(source));
        }
        void Bindings(JsonNode? node,JsonElement? original=null)
        {
            if(node is not JsonArray bindings)return;
            foreach(var binding in bindings)if(binding?["source_kind"]?.GetValue<string>()=="native_part_edge")
            {
                cancel();string source=binding["source_object_id"]!.GetValue<string>();
                if(movingSources.Contains(source)){NativeSketchGeometry385.Motion(binding["source_geometry"],cancel);Frame(binding["source_geometry"],cancel);}
                else if(original.HasValue)
                {
                    var prior=original.Value.EnumerateArray().Single(row=>row.GetProperty("id").GetString()==binding["id"]!.GetValue<string>());
                    binding["source_geometry"]=JsonNode.Parse(prior.GetProperty("source_geometry").GetRawText());
                }
            }
        }
        Bindings(result["selected_sketch"]?["projection_bindings"]);
        foreach(var profile in result["selected_sketch"]!["profiles"]!.AsArray())Bindings(profile?["state"]?["projection_bindings"]);
        var originalSketches=value.Part.Preserved.GetProperty("sketches").EnumerateArray().ToDictionary(row=>row.GetProperty("id").GetString()!,StringComparer.OrdinalIgnoreCase);
        foreach(var sketch in part["sketches"]!.AsArray())if(sketch?["retained"] is JsonObject retained)
        {
            var prior=originalSketches[sketch["id"]!.GetValue<string>()].GetProperty("retained");
            if(prior.ValueKind==JsonValueKind.Object)Bindings(retained["projection_bindings"],prior.GetProperty("projection_bindings"));
        }
        var originalProfiles=value.Part.Preserved.GetProperty("profiles").EnumerateArray().ToDictionary(row=>row.GetProperty("profile_id").GetString()!,StringComparer.OrdinalIgnoreCase);
        foreach(var profile in part["profiles"]!.AsArray())Bindings(profile!["projection_bindings"],originalProfiles[profile["profile_id"]!.GetValue<string>()].GetProperty("projection_bindings"));
        return Wire.Element(result);
    }
    static JsonElement PeerIntent(JsonElement observation,IReadOnlySet<string> changed,bool affected,Action cancel)
    {
        var result=JsonNode.Parse((affected?ModelDependencyGraph410.ConstraintPoints(observation,cancel):observation).GetRawText())!.AsObject();
        if(result["constraints"] is JsonArray rows)foreach(var row in rows)if(changed.Contains(row!["id"]!.GetValue<string>())){row["parameter"]=null;row["variable_expression"]=null;}
        return Wire.Element(result);
    }
    internal static string[] Verify(ModelParameterArguments args,DocumentState document,IEnumerable<ModelSketchParameter411.Control> controls,
        IReadOnlyDictionary<string,HashSet<string>> affected,IReadOnlyDictionary<string,string> handles,IReadOnlyDictionary<string,double> expected,
        IEnumerable<ModelFeatureVariable> features,Action cancel,IEnumerable<NativeSketchEditorSolve445.Transition>? transitions=null)
    {
        var completed=transitions?.ToArray()??[];
        var all=controls.ToArray();var warnings=new HashSet<string>(StringComparer.Ordinal);var changed=all.Select(c=>Key(c.Constraint)).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach(var group in all.GroupBy(c=>Key(c.ID),StringComparer.OrdinalIgnoreCase))
        {
            cancel();var first=group.First();var before=NativeSketchSolveExpected445.CanonicalComparisons(first.State,completed,cancel);string oldHandle=Validation.NormalizeHandle(before.Part.Representation.Handle.ToString());var scope=affected[oldHandle];
            NativeSketchConstraintState385.Snapshot after;
            using(var tx=document.Document.Database.TransactionManager.StartTransaction())after=NativeSketchConstraintState385.Capture(handles[oldHandle],Key(first.ID),ModelSketchParameter411.Context(args),document,tx,cancel);
            after=NativeSketchSolveExpected445.CanonicalComparisons(after,completed,cancel);
            NativeSketchMembers385.ValidateSmoothRelations(after.Relations,after.Inputs,cancel);
            var stationary=Stationary(before,scope);
            var controlledSketches=all.Where(control=>control.State.Part.Body.ID==before.Part.Body.ID).Select(control=>Key(control.ID)).ToHashSet(StringComparer.OrdinalIgnoreCase);
            Need(controlledSketches.All(scope.Contains),"MODEL_DEPENDENCY_CHANGED","A native dimensional control is outside its actual affected part graph.");
            warnings.UnionWith(before.Part.Warnings);
            warnings.UnionWith(after.Part.Warnings);
            foreach(var control in group)
            {
                var actual=ModelSketchParameter411.Resolve(after,control.Variable,cancel);double value=expected[Key(control.Variable)];
                Need(actual.Constraint==control.Constraint&&actual.ID==control.ID&&actual.Kind==control.Kind&&ModelParameterSnapshot.Near(actual.Value,value),"MODEL_UPDATE_FAILED","The exact native sketch relation/variable did not retain its requested calculated value.");
                using var manager=NativeSketchMembers385.Manager(after.Sketch.Sketch);using var info=manager.GetConstraintInfo(control.Constraint);Need(info!=null,"CONSTRAINTS_UNAVAILABLE","A retained dimension disappeared.");
                var spec=control.Spec with{Value=value};using var requested=NativeSketchMembers385.Info(spec,after.Inputs);
                Need(NativeSketchMembers385.RequestedReferences(info!,requested,spec,after.Inputs),"CONSTRAINT_SCOPE_MISMATCH","A dimensional relation changed its actual member/point/segment binding.");NativeSketchMembers385.ValidateSolved([spec],after.Inputs);
            }
            Need(before.Part.Body.ID==after.Part.Body.ID&&before.Part.Contents.ToHashSet().SetEquals(after.Part.Contents)&&before.Part.Features.SequenceEqual(after.Part.Features)&&before.Part.Previous.Count==after.Part.Previous.Count&&before.Part.Previous.All(p=>after.Part.Previous.TryGetValue(p.Key,out var value)&&p.Value==value),"MODEL_DEPENDENCY_CHANGED","A parameter edit changed native composition or feature ordering.");
            Need(before.Affected.SetEquals(ModelDependencyGraph410.OwnedClosure(after.Part.Body,before.GraphRoots,cancel,after.Part.Views)),"MODEL_DEPENDENCY_CHANGED","The actual native downstream sketch/profile consumer closure changed.");
            if(!ModelParameterSnapshot.Equivalent(before.Part.Properties,after.Part.Properties))
                throw new DomainException("MODEL_DEPENDENCY_CHANGED","A parameter edit changed retained body display/properties.",false,
                    new{stage="retained_body_display_comparison",body_id=Key(before.Part.Body.ID),sketch_id=Key(first.ID),
                        previous_properties=before.Part.Properties,actual_properties=after.Part.Properties});
            Need(before.Inputs.Members.Select(m=>m.Id).ToHashSet().SetEquals(after.Inputs.Members.Select(m=>m.Id)),"SKETCH_BINDING_UNAVAILABLE","A native member identity changed during persistent expression editing.");
            var oldIntent=NativeSketchConstraintState385.Intent(before.Relations,inputs:before.Inputs);var newIntent=NativeSketchConstraintState385.Intent(after.Relations,inputs:after.Inputs);
            JsonElement Controls(JsonElement relations)
            {var copy=JsonNode.Parse(relations.GetRawText())!.AsObject();foreach(var row in copy["constraints"]!.AsArray())if(changed.Contains(row!["id"]!.GetValue<string>())){row["value"]=null;row["expression"]=null;}return Wire.Element(copy);}
            Need(ModelParameterSnapshot.Equivalent(Controls(oldIntent),Controls(newIntent)),"CONSTRAINT_BASELINE_CHANGED","A prior relation/variable identity or retained native intent changed.");
            Anchors(before,after,scope,stationary.Contains(Key(first.ID)),cancel);
            Need(before.Part.Constraints.Keys.ToHashSet(StringComparer.OrdinalIgnoreCase).SetEquals(after.Part.Constraints.Keys),"CONSTRAINT_BASELINE_CHANGED","A retained peer constraint network disappeared.");
            foreach(var pair in before.Part.Constraints)Need(ModelSketchConstraintObservation353.Equivalent(PeerIntent(pair.Value,changed,scope.Contains(pair.Key),cancel),PeerIntent(after.Part.Constraints[pair.Key],changed,scope.Contains(pair.Key),cancel),cancel),"CONSTRAINT_BASELINE_CHANGED","A retained peer relation/control binding changed.");
            var oldDesign=Protected(before,scope,stationary,controlledSketches,features,cancel);var newDesign=Protected(after,scope,stationary,controlledSketches,features,cancel);
            var storage=ModelSketchConstraintMetadata379.Compare(oldDesign,newDesign,before.Part.BodyMetadata,after.Part.BodyMetadata,before.Storage,after.Storage,cancel);
            bool designRetained=ModelParameterSnapshot.Equivalent(oldDesign,storage.design),bodyMetadataRetained=ModelParameterSnapshot.Equivalent(before.Part.BodyMetadata,storage.body);
            if(!storage.retained||!designRetained||!bodyMetadataRetained)
            {
                // Completed snapshots only; diagnostics do not read native data
                // or change the existing preservation predicates/normalization.
                var identity=new ModelRepresentationDiagnostics377.Identity(args.Operation,Key(before.Part.Body.ID),before.Part.Representation.Handle.ToString(),after.Part.Representation.Handle.ToString(),
                    before.Part.NativeDocument,after.Part.NativeDocument,before.Part.Preserved.GetProperty("variables_owner").GetString(),after.Part.Preserved.GetProperty("variables_owner").GetString(),
                    before.Part.BodyMetadata.GetProperty("root").GetProperty("owner").GetString(),after.Part.BodyMetadata.GetProperty("root").GetProperty("owner").GetString(),
                    before.Part.BodyMetadata.GetProperty("owned").GetArrayLength(),after.Part.BodyMetadata.GetProperty("owned").GetArrayLength());
                throw new DomainException("MODEL_DEPENDENCY_CHANGED","A parameter edit changed retained sketch/profile/consumer design, foreign links or meaningful metadata.",false,
                    new{sketch_id=Key(first.ID),snapshot_diagnostics=ModelRepresentationDiagnostics377.Describe(identity,[
                        new("selected_storage_bindings",storage.retained,storage.reason,Wire.Element(before.Storage),Wire.Element(after.Storage)),
                        new("protected_design",designRetained,"Existing Protected design after the existing exact typed storage comparison",oldDesign,storage.design),
                        new("body_metadata",bodyMetadataRetained,"Existing retained body metadata after the existing exact typed storage comparison",before.Part.BodyMetadata,storage.body)],cancel)});
            }
            if(storage.changed)warnings.Add(ModelSketchConstraintMetadata379.Warning);
        }
        return warnings.ToArray();
    }
}
