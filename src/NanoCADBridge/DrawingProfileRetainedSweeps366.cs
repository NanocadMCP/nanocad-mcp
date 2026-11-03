using System.Text.Json;
using System.Text.Json.Nodes;
using Multicad;
using Multicad.DatabaseServices;
using Multicad.Mc3D;
using NanoCAD.Contracts;
using Db=Teigha.DatabaseServices;

namespace NanoCADBridge;

// Existing ACAD source constraints own the edit. This adapter only admits,
// captures and verifies their native shared-sketch sweep consumers; it never
// clears a consumed profile, writes sketch geometry or recreates a body.
internal static partial class DrawingProfileWriter
{
    private sealed record RetainedSweepBody(McObjectId Body,McObjectId Sketch,string Owner,string SourceId,string ConstraintId,string SourceVariable,double Radius,
        McObjectId[] Projections,string[] Sources,JsonElement Protected,DrawingRetainedShape366[] Shapes,
        DrawingRetainedSweepConsumer366[] Consumers,HashSet<Db.ObjectId> Inventory,ModelViewFollowers.Snapshot Views,
        JsonElement[] Constraints,string[] Warnings);
    private static JsonElement Without366(JsonElement value,params string[] fields)
    {var copy=JsonNode.Parse(value.GetRawText())!.AsObject();foreach(string field in fields)copy.Remove(field);return Wire.Element(copy);}
    private static JsonElement SourceDesign366(JsonElement value)
    {var copy=JsonNode.Parse(value.GetRawText())!.AsArray();foreach(var source in copy)source!.AsObject().Remove("geometry");return Wire.Element(copy);}
    private static JsonElement ProfileDesign366(JsonElement value,Action cancel)
    {
        var design=JsonNode.Parse(ModelProfileRebindState349.ProfileDesign(value,false,cancel).GetRawText())!.AsObject();
        design.Remove("geometry");design.Remove("profile_geometry");design["sources"]=JsonNode.Parse(SourceDesign366(value.GetProperty("sources")).GetRawText());
        return Wire.Element(design);
    }
    private static JsonElement Variables366(JsonElement values)
    {
        var rows=new List<JsonElement>();foreach(var value in values.EnumerateArray())
        {
            var row=JsonNode.Parse(value.GetRawText())!.AsObject();
            foreach(string field in new[]{"downstream_variables","downstream_objects"})
                if(row[field] is JsonArray edges)row[field]=JsonSerializer.SerializeToNode(edges.Select(edge=>edge!.GetValue<string>()).OrderBy(edge=>edge,StringComparer.OrdinalIgnoreCase).ToArray(),Wire.Json);
            rows.Add(Wire.Element(row));
        }
        return Wire.Element(rows.OrderBy(row=>row.GetProperty("mc_id").GetString(),StringComparer.OrdinalIgnoreCase).ToArray());
    }
    private static string[] Dependents366(HistoryItem feature,Mc3dSolid body,McObjectId[] contents,ModelViewFollowers.Snapshot views,bool all,Action cancel)
    {
        var dependents=all?feature.GetAllDependent(false):feature.GetDependent();
        Need(dependents!=null,"DEPENDENCY_INVENTORY_UNAVAILABLE","A retained sweep downstream inventory is unavailable; no empty-consumer claim is made.");
        ModelInventoryBudget.Ids(dependents,cancel);
        foreach(var id in dependents)
        {cancel();Need(id==body.ID||contents.Contains(id)||ModelViewFollowers.OwnsDependent(views,id),"DEPENDENCY_OWNERSHIP_MISMATCH","A retained sweep has a downstream owner outside this part's proved native graph.");}
        return dependents.Select(Key).OrderBy(id=>id,StringComparer.OrdinalIgnoreCase).ToArray();
    }
    private static RetainedSweepBody CaptureRetainedSweeps366(Mc3dSolid body,Db.ObjectId representation,Dictionary<string,Source> selected,
        DocumentState state,Db.Transaction tx,Action cancel)
    {
        Need(selected.Count==1&&selected.Values.Single().Curve.Kind=="circle","OUT_OF_SCOPE","Shared retained sweep editing currently selects one existing Circle source radius/diameter driver.");
        var edited=selected.Values.Single();var drawing=ModelProfileDiscovery348.DrawingDocument(state);var owner=ModelProfileDiscovery348.OwnerDocument(body,state);
        var raw=tx.GetObject(representation,Db.OpenMode.ForRead) as Db.Solid3d;
        Need(raw!=null&&raw.GetType()==typeof(Db.Solid3d)&&!raw.IsNull&&raw.OwnerId==Model(state,tx)&&
            ModelHistoryEditGuard.CurrentRepresentation(body,state,tx,cancel)==representation,"OBJECT_IDENTITY_MISMATCH","Use the unique nonempty current representation of the native part.");Layer(raw,tx);
        var contents=ModelInventoryBudget.History(body,cancel).ToArray();var external=ProjectedHistorySources.Read(body,contents,state,tx,cancel);ProjectedHistorySources.Healthy(contents,external);
        var projectionIds=contents.Where(id=>id.GetObjectOfType<ProjectionSketchEnt>() is {} p&&p.SrcObjectID==edited.Native).ToArray();
        Need(projectionIds.Length>0,"INCOMPLETE_PROFILE_SELECTION","The selected Circle has no actual projection into this part.");
        var sketchIds=projectionIds.Select(id=>id.GetObjectOfType<ProjectionSketchEnt>()!.HostSketchID).Distinct().ToArray();
        Need(sketchIds.Length==1,"OUT_OF_SCOPE","This source edit requires its projections to share one actual retained sketch.");var shared=sketchIds[0];
        var views=ModelViewFollowers.Resolve(body,contents,cancel,includeStandalone:true);var bytes=new ModelInventoryBudget(cancel);
        var shapes=new List<DrawingRetainedShape366>();var sketches=new List<object>();var profiles=new List<object>();var constraints=new List<JsonElement>();
        var sharedSources=new Dictionary<McObjectId,Source>();var sharedProjections=new Dictionary<McObjectId,McObjectId>();
        void Shape(string key,JsonElement geometry,bool driven){var value=new DrawingRetainedShape366(key,geometry,driven);bytes.Add(value);shapes.Add(value);}
        foreach(var id in contents.OrderBy(Key,StringComparer.OrdinalIgnoreCase))
        {
            cancel();var sketch=id.GetObjectOfType<PlanarSketch>();if(sketch==null)continue;
            var snapshot=ModelSketchLifecycleReader362.Capture(body,sketch,state,tx,cancel);constraints.Add(snapshot.Constraints);
            var design=JsonNode.Parse(Without366(snapshot.Stored,"geometry","profiles","constraint_observation").GetRawText())!.AsObject();
            design["sources"]=JsonNode.Parse(SourceDesign366(snapshot.Stored.GetProperty("sources")).GetRawText());
            design["geometry_frame"]=JsonNode.Parse(snapshot.Geometry.GetProperty("frame").GetRawText());sketches.Add(Wire.Element(design));
            foreach(var element in snapshot.Geometry.GetProperty("elements").EnumerateArray())
            {
                cancel();string elementId=element.GetProperty("element_id").GetString()!;var geometry=element.GetProperty("geometry");
                var projection=snapshot.Projections.Ids.SingleOrDefault(p=>Key(p).Equals(elementId,StringComparison.OrdinalIgnoreCase));
                bool driven=!projection.IsNull&&projection.GetObjectOfType<ProjectionSketchEnt>()!.SrcObjectID==edited.Native;
                if(id==shared)
                {
                    Need(!projection.IsNull,"OUT_OF_SCOPE","Every Circle of the selected shared sketch must have its actual retained source projection.");
                    var native=projection.GetObjectOfType<ProjectionSketchEnt>()!.SrcObjectID;
                    var source=snapshot.Projections.Sources.Single(s=>s.id.Equals(Key(native),StringComparison.OrdinalIgnoreCase));
                    var value=SourceOf(CadStructureWriter.Find(state.Document.Database,source.handle),state,tx,false,snapshot.Plane);
                    Need(value.Native==native&&value.Curve.Kind=="circle"&&geometry.GetProperty("kind").GetString()=="circular_arc"&&
                        DrawingProfileGeometry.Near(Math.Abs(geometry.GetProperty("end_angle").GetDouble()-geometry.GetProperty("start_angle").GetDouble()),2*Math.PI),
                        "OUT_OF_SCOPE","This shared-sketch adapter requires independently selected complete projected Circle profiles.");
                    var observed=DrawingWorkplane.Local(snapshot.Plane,new Point3(geometry.GetProperty("center")[0].GetDouble(),geometry.GetProperty("center")[1].GetDouble(),geometry.GetProperty("center")[2].GetDouble()));
                    var normal=DrawingWorkplane.Normal(snapshot.Plane);var arcNormal=geometry.GetProperty("normal");
                    Need(Math.Abs(Math.Abs(normal.X*arcNormal[0].GetDouble()+normal.Y*arcNormal[1].GetDouble()+normal.Z*arcNormal[2].GetDouble())-1)<=1e-8,
                        "PROFILE_PLANE_MISMATCH","An actual projected Circle is not in the shared sketch's physical plane.");
                    Need(DrawingProfileGeometry.Same(observed,value.Curve.Center!)&&DrawingProfileGeometry.Near(geometry.GetProperty("radius").GetDouble(),value.Curve.Radius),
                        "PROFILE_GEOMETRY_MISMATCH","An actual projected Circle does not match its exact retained source geometry.");
                    Need(sharedSources.TryAdd(native,value)&&sharedProjections.TryAdd(native,projection),"OUT_OF_SCOPE","Coincident/duplicate source projections require a separate region ownership adapter.");
                    // A full Circle's reference vector/start angle is a boundary
                    // representation choice; center, oriented normal and radius
                    // are the independently observed physical geometry.
                    geometry=Wire.Element(new{kind="circle",center=geometry.GetProperty("center"),normal=geometry.GetProperty("normal"),radius=geometry.GetProperty("radius")});
                }
                Shape("sketch:"+Key(id)+":"+elementId,geometry,driven);
            }
        }
        Need(sharedSources.ContainsKey(edited.Native),"PROFILE_SOURCE_UNAVAILABLE","The actual shared sketch lacks the selected source binding.");
        var sheet=McDocumentsManager.GetActiveSheet();Need(sheet!=null,"DOCUMENT_CONTEXT_MISMATCH","The current drawing source sheet is unavailable.");
        var allCircleSources=sharedSources.Values.Select(s=>SourceOf(s.Id,state,tx,false,edited.Workplane)).ToDictionary(s=>s.Curve.Id,StringComparer.OrdinalIgnoreCase);
        var sourceNetwork=ReadNetwork(sheet,allCircleSources,state,tx,cancel);Need(sourceNetwork.Available,"CONSTRAINTS_UNAVAILABLE","Read the actual existing ACAD source constraint network before a retained sweep edit.");
        var radial=sourceNetwork.Relations.Where(r=>r.Spec is {} spec&&spec.First==edited.Curve.Id&&spec.Kind is "radius" or "diameter").ToArray();
        Need(radial.Length==1&&!radial[0].Variable.IsNull,"OUT_OF_SCOPE","The selected Circle needs one actual existing native radius/diameter driver; no projected internal solver is substituted.");var driver=radial[0];
        var sourceRelations=sourceNetwork.Relations.Select(r=>new{id=Key(r.Id),r.Type,kind=r.Spec?.Kind,first=r.Spec?.First,second=r.Spec?.Second,first_point=r.Spec?.FirstPoint,second_point=r.Spec?.SecondPoint,
            variable=Key(r.Variable),r.Name,expression=r.Id==driver.Id?null:r.Expression,value=r.Id==driver.Id?null:r.Value,r.References}).ToArray();
        var sourceVariables=DrawingVariablesAccess.Read(sheet,cancel);var sourceVariableRows=sourceVariables.Select(v=>new{id=Key(v.Id),v.Name,v.Type,
            expression=v.Id==driver.Variable?null:v.Expression,value=v.Id==driver.Variable?null:v.Value,v.Angular,host=ModelProfileDiscovery348.Id(v.Host),
            downstream=v.Downstream?.Select(Key).OrderBy(id=>id,StringComparer.OrdinalIgnoreCase).ToArray(),objects=v.Objects?.Select(Key).OrderBy(id=>id,StringComparer.OrdinalIgnoreCase).ToArray(),
            constraints=v.Constraints?.Select(Key).OrderBy(id=>id,StringComparer.OrdinalIgnoreCase).ToArray(),v.MetadataSource}).OrderBy(v=>v.id,StringComparer.OrdinalIgnoreCase).ToArray();
        Need(sourceVariables.Count(v=>v.Id==driver.Variable)==1,"CONSTRAINT_VALUE_UNAVAILABLE","The source driver has no unique actual ACAD variable identity.");
        var consumers=new List<DrawingRetainedSweepConsumer366>();var consumerIds=new HashSet<McObjectId>();var profileToSource=new Dictionary<McObjectId,McObjectId>();
        foreach(var id in contents.OrderBy(Key,StringComparer.OrdinalIgnoreCase))
        {
            cancel();var profile=id.GetObjectOfType<SketchProfile>();if(profile==null)continue;
            var snapshot=ModelProfileLifecycleState340.Capture(profile,body,state,tx,cancel,documentScope:owner);
            profiles.Add(new{id=Key(id),design=ProfileDesign366(snapshot.Stored,cancel),properties=new{layer=profile.DbEntity.Layer,visibility=profile.DbEntity.Visibility},
                metadata=ModelSketchLifecycleReader362.Metadata(id,profile.DbEntity,owner,state,tx,cancel)});
            var retainedContour=snapshot.Contour??throw new DomainException("OUT_OF_SCOPE","This source-driven Circle edit requires an actual single-contour native profile.");
            bool driven=false;JsonElement contour=Wire.Element(retainedContour);
            if(profile.ParentSketchID==shared)
            {
                // Capture already proves one selected native area, no holes and
                // a closed validated boundary. Native circles may be split into
                // arcs; compare complete physical coverage, not Contour.Kind.
                var boundary=ModelProfileLifecycleState340.Boundary(snapshot.ProfileGeometry,snapshot.Plane,cancel);
                Need(boundary.Length>0&&boundary.All(curve=>curve.Kind is "circle" or "arc"),"OUT_OF_SCOPE","Each shared-sketch native profile must select one actual Circle area without holes.");
                var circle=new ModelProfileSpec("circle",boundary[0].Center,boundary[0].Radius,null);
                var matches=sharedSources.Where(pair=>ModelProfileElementGeometry358.SameBoundary(boundary,[pair.Value.Curve],cancel)).ToArray();
                Need(matches.Length==1,"PROFILE_GEOMETRY_MISMATCH","The selected native profile contour lacks one unique actual source/projection Circle binding.");var source=matches[0];
                var feature=profile.Dependent3dFeatureID.GetObjectOfType<ExtrudeAlongPathFeature>();
                Need(feature!=null&&feature.ID==profile.Dependent3dFeatureID&&contents.Contains(feature.ID)&&feature.ProfileID==profile.ID&&feature.HostSolidID==body.ID&&
                    profile.HostSolidID==body.ID&&snapshot.Sketch.HostSolidID==body.ID,"PROFILE_OWNERSHIP_MISMATCH","Each selected Circle profile must retain its exact typed sweep owner in this part.");
                Need(consumerIds.Add(feature.ID)&&profileToSource.TryAdd(profile.ID,source.Key),"PROFILE_OWNERSHIP_MISMATCH","A shared profile or typed sweep has duplicate ownership.");
                var paths=feature.GetPath();var dependencies=feature.GetDependsOn(false,false);
                Need(paths!=null&&paths.Count>0&&paths.All(p=>p!=null&&!p.ID.IsNull)&&dependencies!=null&&dependencies.Contains(profile.ID)&&paths.All(p=>dependencies.Contains(p.ID)),
                    "DEPENDENCY_OWNERSHIP_MISMATCH","A retained sweep lacks complete ordered path/profile dependencies.");
                Need(!feature.TaperAngleParamVarID.IsNull,"MODEL_DRIVER_CHANGED","The retained typed sweep has no native taper-driver identity.");
                driven=source.Key==edited.Native;
                consumers.Add(new(Key(source.Key),source.Value.Curve.Id,Key(sharedProjections[source.Key]),Key(profile.ID),Key(feature.ID),paths.Select(p=>Key(p.ID)).ToArray(),
                    Key(feature.TaperAngleParamVarID),feature.PartOperation.ToString(),source.Value.Curve.Radius,circle.Radius,driven));
                contour=Wire.Element(new{kind="circle",center=circle.Center,radius=circle.Radius});
            }
            Shape("profile:"+Key(id),contour,driven);
        }
        Need(profileToSource.Values.Distinct().Count()==sharedSources.Count&&profileToSource.Count==sharedSources.Count,
            "PROFILE_OWNERSHIP_MISMATCH","The shared sketch requires one distinct retained Circle profile/sweep per actual source; no whole-sketch selection is substituted.");
        void Owner(HistoryItem? value,McObjectId candidate,McObjectId profile)
        {
            Need(value!=null&&value.ID==candidate,"PROFILE_DISCOVERY_UNAVAILABLE","An actual typed forming binding is unavailable.");
            if(!profileToSource.ContainsKey(profile))return;
            Need(value.DbEntity.Document!=null&&value.DbEntity.Document.ID==owner.ID&&value.HostSolidID==body.ID&&consumerIds.Contains(candidate)&&
                candidate.GetObjectOfType<ExtrudeAlongPathFeature>() is {} sweep&&sweep.ProfileID==profile,
                "PROFILE_OWNERSHIP_MISMATCH","A selected Circle profile has another native forming consumer or foreign owner.");
        }
        foreach(var id in ModelProfileDiscovery348.Ids<ExtrudeFeature>(owner,cancel)){cancel();var value=id.GetObjectOfType<ExtrudeFeature>();Owner(value,id,value?.ProfileID??McObjectId.Null);}
        foreach(var id in ModelProfileDiscovery348.Ids<RevolveFeature>(owner,cancel)){cancel();var value=id.GetObjectOfType<RevolveFeature>();Owner(value,id,value?.ProfileID??McObjectId.Null);}
        foreach(var id in ModelProfileDiscovery348.Ids<ExtrudeAlongPathFeature>(owner,cancel)){cancel();var value=id.GetObjectOfType<ExtrudeAlongPathFeature>();Owner(value,id,value?.ProfileID??McObjectId.Null);}
        foreach(var id in ModelProfileDiscovery348.Ids<LoftFeature>(owner,cancel))
        {cancel();var value=id.GetObjectOfType<LoftFeature>();Owner(value,id,value?.ProfileID??McObjectId.Null);Need(value!=null,"PROFILE_DISCOVERY_UNAVAILABLE","A native loft section inventory is unavailable.");for(int i=0;i<value.SectionsCount;i++){var section=value.GetSection(i);Need(section!=null,"PROFILE_DISCOVERY_UNAVAILABLE","A native loft section is unavailable.");Owner(value,id,section.ID);}}
        var sourceStates=new Dictionary<string,object>(StringComparer.OrdinalIgnoreCase);
        void SourceState(string handle,McObjectId native)
        {
            if(sourceStates.ContainsKey(handle))return;cancel();var id=CadStructureWriter.Find(state.Document.Database,handle);var entity=tx.GetObject(id,Db.OpenMode.ForRead) as Db.Entity;
            Need(entity!=null&&entity.OwnerId==Model(state,tx)&&McObjectId.FromHandle(id.Handle.Value,drawing.ID)==native,"PROFILE_SOURCE_UNAVAILABLE","A retained source/path lacks exact native/raw top-level ownership.");
            var detail=Wire.Element(EntityAdapter.Detail(entity,EntityAdapter.Summary(entity,entity.OwnerId,"","",false),CancellationToken.None));
            Shape("source:"+Key(native),detail.GetProperty("geometry"),native==edited.Native);
            sourceStates.Add(handle,new{handle,native_id=Key(native),properties=EntityAdapter.StoredProperties(entity),metadata=ModelProfileElementsState358.Metadata(entity,tx,cancel,false),
                reactors=ModelProfileElementsState358.SourceReactors(entity,native,drawing,owner,shared,contents.Where(p=>p.GetObjectOfType<ProjectionSketchEnt>()!=null).ToArray(),tx,cancel)});
        }
        foreach(var source in external)SourceState(source.handle,contents.Single(id=>Key(id).Equals(source.id,StringComparison.OrdinalIgnoreCase)));
        var actualPaths=new Dictionary<string,McObjectId>(StringComparer.OrdinalIgnoreCase);
        foreach(var id in contents)
        {
            cancel();var sweep=id.GetObjectOfType<ExtrudeAlongPathFeature>();if(sweep==null)continue;var paths=sweep.GetPath();
            Need(paths!=null&&paths.Count>0&&paths.All(p=>p!=null&&!p.ID.IsNull),"NATIVE_PATH_STATE_UNAVAILABLE","A retained typed sweep lacks a complete current path binding inventory.");
            foreach(var path in paths)actualPaths[Key(path.ID)]=path.ID;
        }
        foreach(var id in actualPaths.Values)
        {
            cancel();
            var native=id.GetObjectOfType<Multicad.DatabaseServices.StandardObjects.DbGeometry>();Db.ObjectId rawPath=Db.ObjectId.Null;
            Need(!id.IsNull&&native!=null&&native.ID==id&&id.Handle!=0&&state.Document.Database.TryGetObjectId(new Db.Handle(id.Handle),out rawPath)&&
                McObjectId.FromHandle(rawPath.Handle.Value,drawing.ID)==id&&tx.GetObject(rawPath,Db.OpenMode.ForRead).GetType()==typeof(Db.Line),
                "OUT_OF_SCOPE","This retained-sweep source edit requires actual resident drawing Line paths; generated path storage needs its owning adapter.");SourceState(rawPath.Handle.ToString(),id);
        }
        var read=new Arguments("",state.Id,state.Revision,100,null,null,null,[representation.Handle.ToString()]);
        var history=Wire.Element(ModelHistoryReader.Read(read,state,tx,Model(state,tx),"",CancellationToken.None,cancel)).GetProperty("items")[0];
        Need(history.GetProperty("complete").GetBoolean(),"HISTORY_UNAVAILABLE","The part's current native source/support graph is incomplete.");
        var nodes=history.GetProperty("nodes").EnumerateArray().Select(value=>
        {var node=JsonNode.Parse(value.GetRawText())!.AsObject();node["dependencies"]=JsonSerializer.SerializeToNode(value.GetProperty("dependencies").EnumerateArray().Select(edge=>edge.GetString()).OrderBy(edge=>edge,StringComparer.OrdinalIgnoreCase).ToArray(),Wire.Json);return Wire.Element(node);}).OrderBy(node=>node.GetProperty("mc_id").GetString(),StringComparer.OrdinalIgnoreCase).ToArray();
        var metadata=new List<object>();var followers=new List<object>();
        foreach(var id in contents.OrderBy(Key,StringComparer.OrdinalIgnoreCase))
        {
            cancel();var item=id.GetObjectOfType<HistoryItem>();if(item==null)continue;
            Need(item.ID==id&&item.HostSolidID==body.ID&&item.Status.ToString()=="Ok"&&!item.IsSuppressed&&!item.IsBeyondEndOfPart,"MODEL_STATE_UNAVAILABLE","Retained native history has an unhealthy, inactive or foreign owner.");
            Need(id==body.ID||id.GetObjectOfType<PlanarSketch>()!=null||id.GetObjectOfType<SketchProfile>()!=null||id.GetObjectOfType<ProjectionSketchEnt>()!=null||
                id.GetObjectOfType<ExtrudeFeature>()!=null||id.GetObjectOfType<ExtrudeAlongPathFeature>()!=null||id.GetObjectOfType<DrawingView>()!=null,
                "OUT_OF_SCOPE","This shared Circle-source adapter retains extrusion/sweep construction and associative views; another downstream feature needs its typed source-edit adapter.");
            metadata.Add(new{id=Key(id),properties=new{layer=item.DbEntity.Layer,visibility=item.DbEntity.Visibility},metadata=ModelSketchLifecycleReader362.Metadata(id,item.DbEntity,owner,state,tx,cancel)});
            if(id.GetObjectOfType<ExtrudeAlongPathFeature>() is {} feature)followers.Add(new{id=Key(id),direct=Dependents366(feature,body,contents,views,false,cancel),all=Dependents366(feature,body,contents,views,true,cancel)});
        }
        var variables=Wire.Element(ModelVariablesReader.Read(read,state,tx,Model(state,tx),"",CancellationToken.None,cancel));
        Need(variables.GetProperty("mc_document_id").GetString()==Key(owner.ID),"DOCUMENT_CONTEXT_MISMATCH","Retained MCS variables have another actual owner.");
        foreach(var consumer in consumers)
        {
            var matches=variables.GetProperty("variables").EnumerateArray().Where(v=>string.Equals(v.GetProperty("mc_id").GetString(),consumer.taper_variable_id,StringComparison.OrdinalIgnoreCase)).ToArray();
            Need(matches.Length==1,"MODEL_DRIVER_CHANGED","The retained taper variable has no unique actual MCS inventory identity.");var variable=matches[0];
            Need(string.Equals(variable.GetProperty("host_object_id").GetString(),consumer.feature_id,StringComparison.OrdinalIgnoreCase)&&variable.GetProperty("value_available").GetBoolean(),
                "MODEL_DRIVER_CHANGED","The sweep taper variable lacks its actual current owning feature/value.");
        }
        var protectedState=Wire.Element(new{body=Key(body.ID),owner=Key(owner.ID),drawing=Key(drawing.ID),last=Key(body.LastPartItemID),nodes,
            construction=ModelProfileDiscovery348.ConstructionIds(owner,cancel).Select(Key).OrderBy(id=>id,StringComparer.OrdinalIgnoreCase).ToArray(),
            variables=Variables366(variables.GetProperty("variables")),source_relations=sourceRelations,source_variables=sourceVariableRows,
            source_sheet=Key(sheet.ID),source_constraint=Key(driver.Id),source_driver=Key(driver.Variable),sketches,profiles,metadata,followers,sources=sourceStates.OrderBy(p=>p.Key,StringComparer.OrdinalIgnoreCase).Select(p=>p.Value).ToArray(),
            body_display=EntityAdapter.StoredProperties(raw),body_metadata=ModelProfileElementsState358.Metadata(raw,tx,cancel,true)});bytes.Add(protectedState);
        var warnings=ModelSketchConstraintObservation353.Warnings(constraints).ToList();
        if(sourceVariables.Any(v=>v.Downstream==null||v.Objects==null||v.Constraints==null))warnings.Add("ACAD_SOURCE_PRIVATE_CONSUMERS_UNOBSERVED: Actual source dimension/variable IDs, expressions, available consumer graph and typed native projections/profiles/sweeps are checked; unavailable private ACAD action consumers are not an empty or complete inventory claim.");
        if(ModelProfileElementsState358.Metadata(raw,tx,cancel,true).GetProperty("owned").EnumerateArray().Any(record=>record.TryGetProperty("assoc_manager_observation",out _)))
            warnings.Add("RETAINED_SWEEP_ASSOC_PRIVATE_STATE_UNOBSERVED: Actual resident native storage owners, readable metadata, typed profiles/paths/feature drivers and source ACAD relations are checked; private association-manager internals are not claimed observed or completely preserved.");
        try{using var brep=new Teigha.BoundaryRepresentation.Brep(raw);Need(!brep.IsNull&&brep.CheckEntity(),"INVALID_GEOMETRY","Native B-rep validation reports an invalid retained part.");}
        catch(Teigha.Runtime.Exception){warnings.Add(ModelFeatureReorderState345.ValidityWarning);}catch(NotSupportedException){warnings.Add(ModelFeatureReorderState345.ValidityWarning);}
        return new(body.ID,shared,Key(owner.ID),Key(edited.Native),Key(driver.Id),Key(driver.Variable),edited.Curve.Radius,projectionIds,sharedSources.Values.Select(s=>s.Curve.Id).ToArray(),protectedState,shapes.ToArray(),
            consumers.OrderBy(c=>c.feature_id,StringComparer.OrdinalIgnoreCase).ToArray(),ModelFeatureReorderState345.Inventory(state,tx,cancel),views,constraints.ToArray(),warnings.ToArray());
    }
    private static void RequireRetainedSweepEdit366(DrawingProfileEdit edit,Binding binding,IEnumerable<Consumer> consumers)
    {
        var retained=consumers.Where(c=>c.RetainedSweeps!=null).ToArray();if(retained.Length==0)return;
        Need(edit.Action=="set_drawing_dimension"&&consumers.Count()==1,"OUT_OF_SCOPE","The shared-sketch sweep source route edits one existing Circle radius/diameter in its one owning native part.");
        var relation=FindDimension(edit,binding.Network);var spec=relation.Spec;Need(spec!=null&&spec.Kind is "radius" or "diameter"&&spec.First==binding.Selected.Values.Single().Curve.Id,
            "OUT_OF_SCOPE","Select the actual retained Circle's existing radius/diameter constraint; no internal sketch solver or other dimensional DOF is inferred.");
        var snapshot=retained[0].RetainedSweeps!;Need(snapshot.ConstraintId.Equals(edit.ConstraintId,StringComparison.OrdinalIgnoreCase)&&
            snapshot.SourceVariable.Equals(Key(relation.Variable),StringComparison.OrdinalIgnoreCase),"CONSTRAINT_SCOPE_MISMATCH","The requested dimension is not the actual captured source Circle driver.");
    }
    private static void VerifyRetainedSweeps366(RetainedSweepBody before,RetainedSweepBody after,Db.ObjectId oldRepresentation,Db.ObjectId representation,Action cancel)
    {
        Need(before.Body==after.Body&&before.Sketch==after.Sketch&&before.SourceId==after.SourceId&&before.Owner==after.Owner,
            "OBJECT_IDENTITY_MISMATCH","Source edit changed the native body/shared sketch/source owner identity.");
        if(!ModelProfileRebindState349.Equivalent(before.Protected,after.Protected,cancel))
            throw new DomainException("MODEL_DEPENDENCY_CHANGED","Source edit changed a retained peer, path, feature order, dependency, driver/formula, source relationship or readable owned data.",false,
                DrawingProfileRetainedDiagnostics366.Capture(Key(before.Body),Key(before.Sketch),before.SourceId,before.ConstraintId,before.Protected,after.Protected,cancel));
        Need(DrawingRetainedCircleEdit366.Verify(before.Shapes,after.Shapes,after.Radius,(a,b)=>ModelProfileRebindState349.Equivalent(a,b,cancel),cancel,out string reason),
            "PROFILE_GEOMETRY_MISMATCH",reason);
        Need(before.Constraints.Length==after.Constraints.Length&&before.Constraints.Zip(after.Constraints).All(pair=>ModelSketchConstraintObservation353.Equivalent(pair.First,pair.Second,cancel)),
            "MODEL_DRIVER_CHANGED","A retained native sketch's observed constraint graph changed unexpectedly.");
        Need(after.Inventory.Except(before.Inventory).All(id=>id==representation)&&before.Inventory.Except(after.Inventory).All(id=>id==oldRepresentation)&&
            (representation==oldRepresentation||!after.Inventory.Contains(oldRepresentation)),"OBJECT_IDENTITY_MISMATCH","Source edit changed unrelated drawing membership or left a duplicate body representation.");
        ModelViewFollowers.Verify(after.Body.GetObjectOfType<Mc3dSolid>()!,before.Views,cancel,refreshStandalone:true);
    }
}
