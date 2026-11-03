using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using Multicad;
using Multicad.Mc3D;
using Multicad.DatabaseServices.Constraints;
using NanoCAD.Contracts;
using G=Multicad.Geometry;
using Db=Teigha.DatabaseServices;

namespace NanoCADBridge;

// One native part revision: preserve stored design/identity, allow its observed
// generated shape and proved variable descendants to rebuild natively.
internal static class ModelExtrusionExtentsState351
{
    internal sealed record Snapshot(Mc3dSolid Body,ExtrudeFeature Feature,SketchProfile Profile,PlanarSketch Sketch,
        Db.ObjectId Representation,string DrawingDocument,string OwnerDocument,McObjectId[] Contents,
        JsonElement Parameters,JsonElement Sketches,JsonElement Profiles,JsonElement Sources,JsonElement Metadata,
        JsonElement Display,JsonElement Terminal,JsonElement Plane,JsonElement Drivers,HashSet<Db.ObjectId> Inventory,
        ModelViewFollowers.Snapshot Views,string[] FeatureOrder,string[] DirectDownstream,string[] Downstream,
        string[] UnobservedGeometry,string[] Warnings,string Token);
    internal sealed record Plan(Dictionary<string,double> Requested,HashSet<string> Changed,HashSet<string> Affected,
        Dictionary<string,HashSet<string>> RemovedEdges,Dictionary<string,double> Expected);
    internal static void Need([DoesNotReturnIf(false)] bool ok,string code,string message)
    {if(!ok)throw new DomainException(code,message);}
    // Requested owning-driver scalars follow the established absolute numeric edit policy.
    // Formula descendant/snapshot tolerance remains separate.
    internal static bool RequestedEqual(double current,double requested)=>double.IsFinite(current)&&double.IsFinite(requested)&&Math.Abs(current-requested)<=1e-8;
    internal static string Key(McObjectId id)=>id.ToString();
    internal static bool Same(McObjectId id,string value)=>Key(id).Equals(value,StringComparison.OrdinalIgnoreCase);
    internal static bool Equal(JsonElement a,JsonElement b,Action cancel)=>ModelProfileRebindState349.Equivalent(a,b,cancel);
    static string[] Sorted(IEnumerable<McObjectId> ids,Action cancel)=>ids.Select(id=>{cancel();return Key(id);}).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(id=>id,StringComparer.OrdinalIgnoreCase).ToArray();
    internal static McObjectId[] Variables(ExtrudeFeature feature)=>[feature.DistanceParamVarID,feature.TaperAngleParamVarID,feature.Distance2ParamVarID,feature.TaperAngle2ParamVarID];
    static JsonElement Metadata(Db.DBObject value,Db.Transaction tx,Action cancel,bool body)
    {
        using var xdata=value.XData;using var reactors=value.GetPersistentReactorIds();var budget=new ModelInventoryBudget(cancel);var links=new List<string?>();
        foreach(Db.ObjectId id in reactors){cancel();links.Add(id.IsNull?null:id.Handle.ToString());budget.Add(links[^1]);}
        object? data=xdata==null?null:NativeOwnedMetadata.Buffer(xdata,cancel,budget.Add);
        var owned=JsonNode.Parse(NativeOwnedMetadata.Capture(value,tx,cancel,observeAssocManager:true).GetRawText())!.AsArray();
        if(body)foreach(var entry in owned){cancel();if(entry!["common"] is JsonObject common&&common["owner_handle"]?.GetValue<string>()==Validation.NormalizeHandle(value.Handle.ToString()))common["owner_handle"]="$CURRENT_BODY_REPRESENTATION";}
        var result=Wire.Element(new{root=new{type=value.GetType().FullName,owner_handle=body?null:value.OwnerId.Handle.ToString(),value.HasFields,
            extension_handle=value.ExtensionDictionary.IsNull?null:value.ExtensionDictionary.Handle.ToString(),xdata=data,reactors=links.OrderBy(id=>id,StringComparer.OrdinalIgnoreCase)},owned});budget.Add(result);return result;
    }
    static JsonElement Constraints(Mc3dSolid body,PlanarSketch sketch,Action cancel)=>ModelSketchConstraintObservation353.Read(body,sketch,cancel);
    static JsonElement Contours(SketchProfile profile,Action cancel)
    {
        var areas=profile.GetContours(false);Need(areas!=null&&!profile.IsEmpty&&areas.Count>0,"PROFILE_GEOMETRY_UNAVAILABLE","Actual selected native profile areas are unavailable; no parent-sketch contour is substituted.");
        var values=new List<object>();var budget=new ModelInventoryBudget(cancel);
        object Loop(G.Polyline3d loop)
        {
            Need(loop!=null&&loop.IsClosed(),"INVALID_GEOMETRY","A retained selected profile loop is unavailable or open.");using var parts=loop.Segments;Need(parts.Count>0,"INVALID_GEOMETRY","A selected native loop is empty.");var segments=new List<object>();bool complete=true;
            for(uint index=0;index<parts.Count;index++)
            {
                cancel();object? geometry=null;var kind=parts.SegType(index);
                if(kind==G.SegPlType.sgLine){using var line=parts.GetLineSegAt(index);geometry=SketchGeometryReader.Line(line);}
                else if(kind==G.SegPlType.sgArc){using var arc=parts.GetArcSegAt(index);geometry=SketchGeometryReader.Arc(arc);}
                else complete=false;
                var segment=new{index,native_segment_type=kind.ToString(),geometry};budget.Add(segment);segments.Add(segment);
            }
            return new{closed=true,geometry_details_complete=complete,segments};
        }
        try
        {
            foreach(var area in areas)
            {
                cancel();Need(area!=null,"PROFILE_GEOMETRY_UNAVAILABLE","A selected native area is unavailable.");using var outer=area.External;var inside=area.Internal;
                Need(inside!=null,"PROFILE_GEOMETRY_UNAVAILABLE","Actual selected hole inventory is unavailable.");
                try{var item=new{outer=Loop(outer),holes=inside.Select(loop=>{cancel();return Loop(loop);}).ToArray()};budget.Add(item);values.Add(item);}
                finally{foreach(var hole in inside.Distinct(ReferenceEqualityComparer.Instance))if(hole is IDisposable wrapper)wrapper.Dispose();}
            }
        }
        finally{foreach(var area in areas.Distinct(ReferenceEqualityComparer.Instance))if(area is IDisposable wrapper)wrapper.Dispose();}
        return Wire.Element(values);
    }
    static JsonElement Terminal(ExtrudeFeature feature)
    {
        using var first=feature.EntityTo1_id;using var second=feature.EntityTo2_id;
        return Wire.Element(new{first=first==null||first.IsNull?null:Key(first.TargetId),second=second==null||second.IsNull?null:Key(second.TargetId)});
    }
    static JsonElement Stable(JsonElement value,Action cancel)
    {
        var node=JsonNode.Parse(value.GetRawText())!;var pending=new Stack<JsonNode>();pending.Push(node);
        while(pending.Count>0){cancel();var item=pending.Pop();if(item is JsonObject fields){fields.Remove("inspection_duration_ms");fields.Remove("budget_notice");foreach(var field in fields)if(field.Value!=null)pending.Push(field.Value);}else if(item is JsonArray values)foreach(var child in values)if(child!=null)pending.Push(child);}
        return Wire.Element(node);
    }
    internal static Snapshot Capture(Mc3dSolid body,ExtrudeFeature feature,ModifyArguments args,DocumentState state,Db.Transaction tx,Action cancel,bool includeStandalone=false)
    {
        var drawing=ModelFeatureReorderState345.Scope(state,tx);var owner=ModelProfileDiscovery348.OwnerDocument(body,state);
        var representation=ModelHistoryEditGuard.CurrentRepresentation(body,state,tx,cancel);var solid=tx.GetObject(representation,Db.OpenMode.ForRead) as Db.Solid3d;
        Need(solid!=null&&solid.GetType()==typeof(Db.Solid3d)&&solid.OwnerId==ModelFeatureReorderState345.Model(state.Document.Database,tx)&&!solid.IsNull,"INVALID_GEOMETRY","Use the unique current nonempty native part representation.");
        var layer=(Db.LayerTableRecord)tx.GetObject(solid.LayerId,Db.OpenMode.ForRead);Need(!layer.IsLocked&&!layer.IsOff&&!layer.IsFrozen&&!layer.IsDependent,"LAYER_UNAVAILABLE","Body layer is unavailable.");
        var contents=ModelInventoryBudget.History(body,cancel).ToArray();var parameters=ModelParameterSnapshot.Capture(new(args.BridgeId,args.DocumentId,args.ExpectedRevision,args.OperationId,args.Units,representation.Handle.ToString(),"","",0),state,cancel);ModelParameterSnapshot.Healthy(parameters);
        Need(contents.Contains(feature.ID)&&feature.HostSolidID==body.ID&&feature.DbEntity.Document!=null&&feature.DbEntity.Document.ID==owner.ID&&
            feature.Status.ToString()=="Ok"&&!feature.IsSuppressed&&!feature.IsBeyondEndOfPart,"PROFILE_OWNERSHIP_MISMATCH","Extrusion is not healthy actual body composition in its verified native owner document.");
        var profile=ModelProfileDiscovery348.Resolve(Key(feature.ProfileID),owner,cancel);var sketch=profile.ParentSketchID.GetObjectOfType<PlanarSketch>();
        Need(sketch!=null&&contents.Contains(profile.ID)&&contents.Contains(sketch.ID)&&profile.HostSolidID==body.ID&&sketch.HostSolidID==body.ID&&
            profile.Dependent3dFeatureID==feature.ID&&sketch.DbEntity.Document!=null&&sketch.DbEntity.Document.ID==owner.ID&&!sketch.IsInEditMode,"PROFILE_OWNERSHIP_MISMATCH","The exact consumed profile/parent are absent from this part or have incoherent actual ownership.");
        var sketches=new List<object>();var profiles=new List<object>();var unobserved=new HashSet<string>(StringComparer.OrdinalIgnoreCase);var sources=new Dictionary<string,JsonElement>(StringComparer.OrdinalIgnoreCase);var budget=new ModelInventoryBudget(cancel);
        var nodes=parameters.GetProperty("nodes").EnumerateArray().ToArray();var links=new List<ModelSequenceLink>();var forming=new List<string>();
        JsonElement? selectedFrame=null;
        foreach(var id in contents)
        {
            cancel();var item=id.GetObjectOfType<HistoryItem>();
            // A retained peer can be intentionally suppressed while the
            // selected extrusion above must remain active and healthy.
            if(item!=null)Need(item.HostSolidID==body.ID&&!item.IsBeyondEndOfPart&&
                ((item.Status.ToString()=="Ok"&&!item.IsSuppressed)||ProjectedHistorySources.HealthySuppressed(item)),
                "MODEL_STATE_UNAVAILABLE","Retained history is foreign, inactive or unhealthy.");
            links.Add(new(Key(id),item==null||item.OrderObjBefore.IsNull?null:Key(item.OrderObjBefore)));
            if(ModelFeatureReorderState345.Forming(id))forming.Add(Key(id));
            var parent=id.GetObjectOfType<PlanarSketch>();if(parent!=null)
            {
                Need(parent.DbEntity.Document!=null&&parent.DbEntity.Document.ID==owner.ID&&!parent.IsInEditMode,"PROFILE_OWNERSHIP_MISMATCH","A retained native sketch lacks actual owner scope or remains in edit mode.");
                var geometry=Wire.Element(SketchGeometryReader.Read(parent,cancel,budget.Add));if(parent.ID==sketch.ID)selectedFrame=geometry.GetProperty("frame");
                if(!geometry.GetProperty("geometry_details_complete").GetBoolean())unobserved.Add(Key(parent.ID));
                var record=new{id=Key(parent.ID),support_id=ModelProfileDiscovery348.Id(parent.PlanarEntityID),geometry,constraints=Constraints(body,parent,cancel)};budget.Add(record);sketches.Add(record);
            }
            var partProfile=id.GetObjectOfType<SketchProfile>();if(partProfile!=null)
            {
                Need(partProfile.DbEntity.Document!=null&&partProfile.DbEntity.Document.ID==owner.ID&&!partProfile.IsEmpty,"PROFILE_OWNERSHIP_MISMATCH","A retained profile has unavailable owner or selection.");
                var contour=Contours(partProfile,cancel);if(contour.EnumerateArray().Any(area=>!area.GetProperty("outer").GetProperty("geometry_details_complete").GetBoolean()||area.GetProperty("holes").EnumerateArray().Any(hole=>!hole.GetProperty("geometry_details_complete").GetBoolean())))unobserved.Add(Key(partProfile.ID));
                var record=new{id=Key(partProfile.ID),parent_id=Key(partProfile.ParentSketchID),forming_id=ModelProfileDiscovery348.Id(partProfile.Dependent3dFeatureID),geometry_type=partProfile.ProfileGeomType.ToString(),contours=contour};budget.Add(record);profiles.Add(record);
            }
            var node=nodes.Single(value=>value.GetProperty("mc_id").GetString()!.Equals(Key(id),StringComparison.OrdinalIgnoreCase));if(node.GetProperty("parameters").ValueKind!=JsonValueKind.Object)unobserved.Add(Key(id));
        }
        Need(selectedFrame.HasValue,"SKETCH_FRAME_UNAVAILABLE","The selected profile has no observed parent sketch frame.");var plane=selectedFrame.Value;
        using(var nativePlane=profile.Plane)
        {
            var origin=plane.GetProperty("origin_wcs");var normal=plane.GetProperty("plane_normal_wcs");Need(nativePlane!=null&&Math.Abs(nativePlane.SignedDistanceTo(new G.Point3d(origin[0].GetDouble(),origin[1].GetDouble(),origin[2].GetDouble())))<1e-7&&
                Math.Abs(nativePlane.Normal.X*normal[0].GetDouble()+nativePlane.Normal.Y*normal[1].GetDouble()+nativePlane.Normal.Z*normal[2].GetDouble()-1)<1e-8,"PROFILE_PLANE_MISMATCH","Actual profile and parent sketch oriented planes disagree.");
        }
        void Source(string handle)
        {
            cancel();if(sources.ContainsKey(handle))return;var raw=CadStructureWriter.Find(state.Document.Database,handle);var entity=tx.GetObject(raw,Db.OpenMode.ForRead) as Db.Entity;
            Need(entity!=null&&entity.OwnerId==ModelFeatureReorderState345.Model(state.Document.Database,tx),"PROFILE_SOURCE_UNAVAILABLE","A retained drawing source is absent from ordinary Model Space.");
            var detail=Wire.Element(EntityAdapter.Detail(entity,EntityAdapter.Summary(entity,entity.OwnerId,"","",false),CancellationToken.None));
            var value=Wire.Element(new{handle,native_id=Key(McObjectId.FromHandle(raw.Handle.Value,drawing.ID)),geometry=detail.GetProperty("geometry"),properties=EntityAdapter.StoredProperties(entity),metadata=Metadata(entity,tx,cancel,false)});budget.Add(value);sources.Add(handle,value);
        }
        var projected=ProjectedHistorySources.Read(body,contents,state,tx,cancel);ProjectedHistorySources.Healthy(contents,projected);foreach(var source in projected)Source(source.handle);
        void PathSource(McObjectId native)
        {
            cancel();if(native.IsNull||native.Handle==0||!state.Document.Database.TryGetObjectId(new Db.Handle(native.Handle),out var raw)||raw.IsNull||raw.IsErased||raw.IsEffectivelyErased)return;
            if(tx.GetObject(raw,Db.OpenMode.ForRead) is Db.Curve curve&&curve.OwnerId==ModelFeatureReorderState345.Model(state.Document.Database,tx)&&McObjectId.FromHandle(raw.Handle.Value,drawing.ID)==native)Source(raw.Handle.ToString());
        }
        foreach(var id in contents){cancel();var sweep=id.GetObjectOfType<ExtrudeAlongPathFeature>();if(sweep!=null)foreach(var path in sweep.GetPath()??[])PathSource(path.ID);var loft=id.GetObjectOfType<LoftFeature>();if(loft==null)continue;if(loft.LoftType==LoftType.WithCenterLine&&loft.Path!=null)PathSource(loft.Path.ID);for(int i=0;i<loft.GuideRailsCount;i++)foreach(var path in loft.GetGuideRailsParams(i)??[])PathSource(path.ID);}
        var views=ModelViewFollowers.Resolve(body,contents,cancel,includeStandalone);
        var direct=feature.GetDependent();var downstream=feature.GetAllDependent(false);Need(direct!=null&&downstream!=null,"DEPENDENCY_INVENTORY_UNAVAILABLE","Actual direct/transitive extrusion consumers are unavailable.");ModelInventoryBudget.Ids(direct,cancel);ModelInventoryBudget.Ids(downstream,cancel);
        Need(direct.Concat(downstream).All(id=>id==body.ID||contents.Contains(id)||ModelViewFollowers.OwnsDependent(views,id)),"DEPENDENCY_OWNERSHIP_MISMATCH","The selected extrusion has an unscoped consumer outside this part.");
        var order=ModelFeatureReorderPlanning345.FeatureOrder(links.ToArray(),forming.ToArray(),Key(views.LastModelItem),cancel);
        var variableIds=Variables(feature).Where(id=>!id.IsNull).Select(Key).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var drivers=Wire.Element(parameters.GetProperty("variables").EnumerateArray().Where(value=>variableIds.Contains(value.GetProperty("mc_id").GetString()!)).ToArray());
        Need(drivers.GetArrayLength()==variableIds.Count&&drivers.EnumerateArray().All(value=>value.GetProperty("host_object_id").GetString()==Key(feature.ID)),"MODEL_DRIVER_CHANGED","An observed extrusion driver lacks its exact native owning feature.");
        var notices=new List<string>();notices.AddRange(ModelSketchConstraintObservation353.Warnings(Wire.Element(sketches).EnumerateArray().Select(sketch=>sketch.GetProperty("constraints"))));ModelFeatureOracle.Read(solid);try{using var brep=new Teigha.BoundaryRepresentation.Brep(solid);Need(!brep.IsNull&&brep.CheckEntity(),"INVALID_GEOMETRY","Native B-rep validation reports an invalid rebuilt part.");}
        catch(Teigha.Runtime.Exception){notices.Add(ModelFeatureReorderState345.ValidityWarning);}catch(NotSupportedException){notices.Add(ModelFeatureReorderState345.ValidityWarning);}
        var metadata=Metadata(solid,tx,cancel,true);notices.AddRange(NativeAssocManagerObservation356.Warnings(metadata));foreach(var source in sources.Values)notices.AddRange(NativeAssocManagerObservation356.Warnings(source.GetProperty("metadata")));var display=Wire.Element(new{layer=solid.LayerId.Handle.ToString(),linetype=solid.LinetypeId.Handle.ToString(),properties=EntityAdapter.StoredProperties(solid)});
        var sketchValues=Wire.Element(sketches);var profileValues=Wire.Element(profiles);var sourceValues=Wire.Element(sources.OrderBy(pair=>pair.Key,StringComparer.OrdinalIgnoreCase).Select(pair=>pair.Value).ToArray());var terminal=Terminal(feature);
        var stored=Wire.Element(new{drawing_document=Key(drawing.ID),owner_document=Key(owner.ID),body=Key(body.ID),feature=Key(feature.ID),representation=representation.Handle.ToString(),parameters,sketchValues,profileValues,sourceValues,metadata,display,terminal,plane,drivers,order,direct=Sorted(direct,cancel),downstream=Sorted(downstream,cancel)});budget.Add(stored);
        return new(body,feature,profile,sketch,representation,Key(drawing.ID),Key(owner.ID),contents,parameters,sketchValues,profileValues,sourceValues,metadata,display,terminal,plane,drivers,
            ModelFeatureReorderState345.Inventory(state,tx,cancel),views,order,Sorted(direct,cancel),Sorted(downstream,cancel),unobserved.OrderBy(id=>id,StringComparer.OrdinalIgnoreCase).ToArray(),notices.ToArray(),Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(Stable(stored,cancel),Wire.Json))));
    }
    internal static ExtrudeParamExtType SecondaryMode(ModelExtrusionExtentsSpec351 edit)=>edit.Extent2=="through_all"?ExtrudeParamExtType.All:ExtrudeParamExtType.Dist;
    internal static Plan Prepare(Snapshot before,ModelExtrusionExtentsSpec351 edit,Action cancel)
    {
        Need(edit.Extent2!="through_all"||before.Feature.PartOperation==PartFeatureOperation.Cut,"INVALID_GEOMETRY","Through-all requires an existing subtractive extrusion with material to cut.");
        Need(before.Feature.Extent==ExtrudeParamExtType.Dist&&before.Terminal.GetProperty("first").ValueKind==JsonValueKind.Null&&
            (edit.Direction!="different"||(before.Feature.Extent2 is ExtrudeParamExtType.Dist or ExtrudeParamExtType.All)&&before.Terminal.GetProperty("second").ValueKind==JsonValueKind.Null),
            "TERMINATION_SCOPE_MISMATCH","This retained two-sided branch retains first Dist and all existing face links. The second side may use Dist or All without a face target; ToFace conversion remains separate.");
        var inventory=before.Parameters.GetProperty("variables").EnumerateArray().ToDictionary(value=>value.GetProperty("mc_id").GetString()!,StringComparer.OrdinalIgnoreCase);
        var requested=new Dictionary<string,double>(StringComparer.OrdinalIgnoreCase);var changed=new HashSet<string>(StringComparer.OrdinalIgnoreCase);var removed=new Dictionary<string,HashSet<string>>(StringComparer.OrdinalIgnoreCase);
        var formulaCandidates=new List<string>();
        void Driver(McObjectId id,double property,double desired,bool angle,bool required)
        {
            cancel();if(id.IsNull){Need(!required,"MODEL_STATE_UNAVAILABLE","A first extrusion driver is absent from its native owner network.");return;}
            Need(inventory.TryGetValue(Key(id),out var value)&&value.GetProperty("value_available").GetBoolean()&&value.GetProperty("host_object_id").GetString()==Key(before.Feature.ID),"MODEL_STATE_UNAVAILABLE","An exact current owning driver/value is unavailable.");
            double target=angle?desired*180/Math.PI:desired,current=angle?property*180/Math.PI:property;
            Need(RequestedEqual(current,value.GetProperty("value").GetDouble()),"MODEL_STATE_UNAVAILABLE","Native property and owning MCS driver units/value disagree.");
            if(requested.TryGetValue(Key(id),out var priorRequest))Need(RequestedEqual(priorRequest,target),"VARIABLE_SCOPE_MISMATCH","Two requested independent native sides alias one owning driver with conflicting requested values.");else requested.Add(Key(id),target);
            if(RequestedEqual(current,target))return;
            string expression=value.GetProperty("expression").GetString()!;
            bool literal=double.TryParse(expression,NumberStyles.Float,CultureInfo.InvariantCulture,out var scalar)&&double.IsFinite(scalar);
            if(literal)changed.Add(Key(id));else formulaCandidates.Add(Key(id));
        }
        Driver(before.Feature.DistanceParamVarID,before.Feature.Distance,edit.NativeDistance,false,true);Driver(before.Feature.TaperAngleParamVarID,before.Feature.Angle,edit.TaperAngle,true,true);
        if(edit.Direction=="different"){if(edit.Extent2=="distance")Driver(before.Feature.Distance2ParamVarID,before.Feature.Distance2,edit.NativeDistance2!.Value,false,false);Driver(before.Feature.TaperAngle2ParamVarID,before.Feature.Angle2,edit.TaperAngle2!.Value,true,false);}
        HashSet<string> Closure()
        {
            var values=new HashSet<string>(changed,StringComparer.OrdinalIgnoreCase);var pending=new Queue<string>(changed);
            while(pending.Count>0)
            {
                cancel();string id=pending.Dequeue();Need(inventory.TryGetValue(id,out var value)&&value.GetProperty("downstream_variables").ValueKind==JsonValueKind.Array&&value.GetProperty("downstream_objects").ValueKind==JsonValueKind.Array,"DEPENDENCY_INVENTORY_UNAVAILABLE","An affected native variable has unavailable consumer inventory.");
                foreach(var child in value.GetProperty("downstream_variables").EnumerateArray()){cancel();string key=child.GetString()!;Need(inventory.ContainsKey(key),"DEPENDENCY_INVENTORY_UNAVAILABLE","A native descendant is absent from actual owner inventory.");if(values.Add(key))pending.Enqueue(key);}
                foreach(var consumer in value.GetProperty("downstream_objects").EnumerateArray())Need(consumer.GetString()==Key(before.Body.ID)||before.Contents.Any(native=>Same(native,consumer.GetString()!)),"DEPENDENCY_OWNERSHIP_MISMATCH","An affected variable also drives an unscoped object outside this retained part.");
            }
            return values;
        }
        Dictionary<string,double> Values(HashSet<string> affected)
        {
            var values=new Dictionary<string,double>(StringComparer.OrdinalIgnoreCase);foreach(string id in changed)values.Add(id,requested[id]);
            var pending=affected.Except(changed,StringComparer.OrdinalIgnoreCase).ToHashSet(StringComparer.OrdinalIgnoreCase);
            while(pending.Count>0)
            {
                cancel();bool progressed=false;
                foreach(string id in pending.ToArray())
                {
                    cancel();var formula=ModelArithmetic.ParseStored(inventory[id].GetProperty("expression").GetString()!,cancel);var references=new Dictionary<string,string>(StringComparer.Ordinal);
                    foreach(string name in formula.References){var matches=inventory.Values.Where(value=>value.GetProperty("name").GetString()==name).ToArray();Need(matches.Length==1,"VARIABLES_UNAVAILABLE","A retained descendant expression has an ambiguous native reference.");references.Add(name,matches[0].GetProperty("mc_id").GetString()!);}
                    if(references.Values.Any(parent=>affected.Contains(parent)&&!values.ContainsKey(parent)))continue;
                    double result=formula.Evaluate(name=>{string parent=references[name];if(values.TryGetValue(parent,out var known))return known;var original=inventory[parent];Need(original.GetProperty("value_available").GetBoolean(),"VARIABLES_UNAVAILABLE","A retained formula input has no finite native value.");return original.GetProperty("value").GetDouble();});values.Add(id,result);pending.Remove(id);progressed=true;
                }
                Need(progressed,"MODEL_DEPENDENCY_CHANGED","The affected native variable graph has a cycle or no executable dependency order.");
            }
            return values;
        }
        var affected=Closure();var expected=Values(affected);
        foreach(string id in formulaCandidates)
        {
            cancel();if(expected.TryGetValue(id,out var anticipated)&&RequestedEqual(anticipated,requested[id]))continue;
            Need(edit.ReplaceDriverFormulas,"PARAMETER_HAS_EXPRESSION","The requested value is not supplied by the retained formula cascade; preserve its computed value or explicitly replace_driver_formulas=true.");changed.Add(id);
            var formula=ModelArithmetic.ParseStored(inventory[id].GetProperty("expression").GetString()!,cancel);
            foreach(string name in formula.References)
            {var parents=inventory.Values.Where(value=>value.GetProperty("name").GetString()==name).ToArray();Need(parents.Length==1,"VARIABLES_UNAVAILABLE","A replaced formula reference has no unambiguous actual native variable.");string parent=parents[0].GetProperty("mc_id").GetString()!;if(!removed.TryGetValue(parent,out var edges))removed.Add(parent,edges=new(StringComparer.OrdinalIgnoreCase));edges.Add(id);}
            affected=Closure();expected=Values(affected);
        }
        foreach(var pair in requested)if(expected.TryGetValue(pair.Key,out var predicted))Need(RequestedEqual(pair.Value,predicted),"PARAMETER_HAS_EXPRESSION","The requested retained driver value conflicts with its resulting native formula cascade.");
        foreach(string id in affected)
        {
            cancel();string? host=inventory[id].GetProperty("host_object_id").GetString();var native=before.Contents.SingleOrDefault(value=>host!=null&&Same(value,host));
            if(!native.IsNull&&ModelPlacementHistory.IsPlacement(native))Need(ModelArithmetic.ParseStored(inventory[id].GetProperty("expression").GetString()!,cancel).References.Length==0,
                "MODEL_DRIVER_PERSISTENCE_UNAVAILABLE","This affected placement formula has the documented216 native expression-loss barrier; literal placement edits remain available.");
        }
        foreach(var parent in removed.Keys)Need(inventory[parent].GetProperty("downstream_variables").ValueKind==JsonValueKind.Array,"DEPENDENCY_INVENTORY_UNAVAILABLE","A replaced formula's source-variable dependency inventory is unavailable.");
        return new(requested,changed,affected,removed,expected);
    }
    static JsonElement NormalizeVariable(JsonElement source,string id,Plan plan,Action cancel)
    {
        var value=JsonNode.Parse(source.GetRawText())!.AsObject();
        if(plan.Affected.Contains(id)){value.Remove("value");value.Remove("value_available");}
        if(plan.Changed.Contains(id)){value.Remove("expression");value.Remove("type");}
        if(plan.RemovedEdges.TryGetValue(id,out var removed)&&value["downstream_variables"] is JsonArray children)
            value["downstream_variables"]=new JsonArray(children.Select(child=>child!.GetValue<string>()).Where(child=>!removed.Contains(child)).OrderBy(child=>child,StringComparer.OrdinalIgnoreCase).Select(child=>(JsonNode?)JsonValue.Create(child)).ToArray());
        foreach(string key in new[]{"downstream_variables","downstream_objects"})if(value[key] is JsonArray edges)
            value[key]=new JsonArray(edges.Select(edge=>{cancel();return edge!.GetValue<string>();}).OrderBy(edge=>edge,StringComparer.OrdinalIgnoreCase).Select(edge=>(JsonNode?)JsonValue.Create(edge)).ToArray());
        return Wire.Element(value);
    }
    static string? VariableField(string field)=>field switch
    {
        "taper_angle_variable_id"=>"taper_angle","taper_angle2_variable_id"=>"taper_angle2","taper_variable_id"=>"taper_angle",
        "angle_variable_id"=>"angle","angle2_variable_id"=>"angle2","radius_variable_id"=>"radius","distance_variable_id"=>"distance","distance2_variable_id"=>"distance2",
        "count_variable_id"=>"count","count_x_variable_id"=>"count_x","count_y_variable_id"=>"count_y","step_x_variable_id"=>"step_x","step_y_variable_id"=>"step_y",
        "pitch_variable_id"=>"pitch","length_variable_id"=>"length",_=>null
    };
    static JsonElement NormalizeNode(JsonElement source,string selected,Plan plan,HashSet<string> geometricDependents,HashSet<string> allocatedDrivers,Action cancel)
    {
        var value=JsonNode.Parse(source.GetRawText())!.AsObject();string id=value["mc_id"]!.GetValue<string>();
        // Both observations retain real owning-variable edges. The stable
        // form replaces subentity IDs with parents; it does not erase newly
        // allocated secondary drivers. Admit the same exact proved IDs in
        // both arrays, only for this selected feature.
        foreach(string field in new[]{"dependencies","stable_dependencies"})
            if(value[field] is JsonArray dependencies)value[field]=new JsonArray(dependencies.Select(edge=>edge!.GetValue<string>()).Where(edge=>!id.Equals(selected,StringComparison.OrdinalIgnoreCase)||!allocatedDrivers.Contains(edge)).OrderBy(edge=>edge,StringComparer.OrdinalIgnoreCase).Select(edge=>(JsonNode?)JsonValue.Create(edge)).ToArray());
        if(value["parameters"] is JsonObject parameters)
        {
            if(parameters["edge_ids"] is JsonArray edges){parameters["edge_count"]=edges.Count;parameters.Remove("edge_ids");}
            if(id.Equals(selected,StringComparison.OrdinalIgnoreCase))
            {
                foreach(string field in new[]{"direction","extent","extent2","distance","distance2","taper_angle","taper_angle2","distance2_variable_id","taper_angle2_variable_id"})parameters.Remove(field);
                if(parameters["terminal"] is JsonObject terminal)foreach(string field in new[]{"direction","extent","distance","taper_angle"})terminal.Remove(field);
            }
            else
            {
                foreach(var field in parameters.ToArray())
                {
                    cancel();string? scalar=VariableField(field.Key);if(scalar==null||field.Value is not JsonValue variable||!variable.TryGetValue<string>(out var key)||!plan.Affected.Contains(key))continue;
                    parameters.Remove(scalar);if(scalar=="angle"&&parameters["angle_degrees"]!=null)parameters.Remove("angle_degrees");
                    if(scalar=="angle")parameters.Remove("full_turn");
                    if(parameters["terminal"] is JsonObject terminal)terminal.Remove(scalar);
                }
                if(geometricDependents.Contains(id))
                {
                    string? kind=parameters["kind"]?.GetValue<string>();
                    if(kind=="construction_point")parameters.Remove("position");
                    else if(kind=="construction_axis"){parameters.Remove("start");parameters.Remove("end");}
                    else if(kind=="sketch")parameters.Remove("support_offset");
                }
            }
        }
        return Wire.Element(value);
    }
    // The retained native frame may move with its referenced face. Compare
    // geometry in that observed frame; never regenerate world coordinates.
    static JsonElement LocalGeometry(JsonElement geometry,JsonElement frame,Action cancel)
    {
        static double[] V(JsonElement item)=>item.EnumerateArray().Select(value=>value.GetDouble()).ToArray();
        var origin=V(frame.GetProperty("origin_wcs"));var x=V(frame.GetProperty("x_step_wcs"));var y=V(frame.GetProperty("y_step_wcs"));var n=V(frame.GetProperty("plane_normal_wcs"));
        static double[] Cross(double[] a,double[] b)=>[a[1]*b[2]-a[2]*b[1],a[2]*b[0]-a[0]*b[2],a[0]*b[1]-a[1]*b[0]];
        static double Dot(double[] a,double[] b)=>a[0]*b[0]+a[1]*b[1]+a[2]*b[2];
        var yn=Cross(y,n);var nx=Cross(n,x);var xy=Cross(x,y);double determinant=Dot(x,yn);Need(double.IsFinite(determinant)&&Math.Abs(determinant)>1e-12,"SKETCH_FRAME_UNAVAILABLE","The actual retained frame has no finite inverse.");
        double[] Convert(JsonArray array,bool point)
        {var p=array.Select(value=>value!.GetValue<double>()).ToArray();for(int i=0;i<3;i++)if(point)p[i]-=origin[i];return [Dot(p,yn)/determinant,Dot(p,nx)/determinant,Dot(p,xy)/determinant];}
        var value=JsonNode.Parse(geometry.GetRawText())!;var pending=new Stack<JsonNode>();pending.Push(value);
        while(pending.Count>0)
        {
            cancel();var node=pending.Pop();if(node is JsonArray array){foreach(var child in array)if(child!=null)pending.Push(child);continue;}if(node is not JsonObject item)continue;
            item.Remove("frame");foreach(var property in item.ToArray())
            {
                if(property.Value is JsonArray vector&&vector.Count==3&&property.Key is "start" or "end" or "center" or "normal" or "reference_direction")
                    item[property.Key]=JsonSerializer.SerializeToNode(Convert(vector,property.Key is "start" or "end" or "center"),Wire.Json);
                else if(property.Value!=null)pending.Push(property.Value);
            }
        }
        return Wire.Element(value);
    }
    static JsonElement SketchDesign(JsonElement value,Plan plan,bool moving,Action cancel)
    {
        var node=JsonNode.Parse(value.GetRawText())!.AsObject();if(moving){var geometry=value.GetProperty("geometry");node["geometry"]=JsonNode.Parse(LocalGeometry(geometry,geometry.GetProperty("frame"),cancel).GetRawText());}
        if(node["constraints"]?["constraints"] is JsonArray constraints)foreach(var constraint in constraints)
        {cancel();string? variable=constraint?["variable_id"]?.GetValue<string>();if(variable!=null&&plan.Affected.Contains(variable))constraint!.AsObject().Remove("parameter");}
        return Wire.Element(node);
    }
    static void VerifyVariables(Snapshot before,Snapshot after,Plan plan,ModelExtrusionExtentsSpec351 edit,string[] created,Action cancel)
    {
        var prior=before.Parameters.GetProperty("variables").EnumerateArray().ToDictionary(value=>value.GetProperty("mc_id").GetString()!,StringComparer.OrdinalIgnoreCase);
        var current=after.Parameters.GetProperty("variables").EnumerateArray().ToDictionary(value=>value.GetProperty("mc_id").GetString()!,StringComparer.OrdinalIgnoreCase);
        Need(prior.Keys.All(current.ContainsKey)&&current.Count==prior.Count+created.Length,"MODEL_DRIVER_CHANGED","A retained variable disappeared or an unrelated new variable appeared.");
        foreach(var pair in prior)
        {
            cancel();var value=current[pair.Key];Need(Equal(NormalizeVariable(pair.Value,pair.Key,plan,cancel),NormalizeVariable(value,pair.Key,plan,cancel),cancel),"MODEL_DRIVER_CHANGED","A retained variable changed its identity, expression, owner or nonrequested native dependency.");
            if(plan.Changed.Contains(pair.Key))
            {
                Need(ModelVariableMetadata.SameTypeOrTargetReference(pair.Value.GetProperty("type").GetString()!,value.GetProperty("type").GetString()!,true,false),"MODEL_DRIVER_CHANGED","Changing a native driver changed an unrelated type flag.");
                var expression=ModelArithmetic.ParseStored(value.GetProperty("expression").GetString()!,cancel);Need(expression.References.Length==0&&RequestedEqual(expression.Evaluate(_=>throw new InvalidOperationException()),plan.Requested[pair.Key]),"MODEL_UPDATE_FAILED","The explicitly changed driver is not its requested numeric literal.");
            }
            if(plan.Affected.Contains(pair.Key))
            {
                Need(value.GetProperty("value_available").GetBoolean(),"MODEL_UPDATE_FAILED","An affected native variable has no finite rebuilt value.");
                double expected=plan.Expected[pair.Key];
                Need(ModelParameterSnapshot.Near(value.GetProperty("value").GetDouble(),expected),"MODEL_UPDATE_FAILED","A retained native formula descendant did not rebuild to its actual referenced values.");
            }
        }
        foreach(var request in plan.Requested)
        {cancel();Need(current[request.Key].GetProperty("value_available").GetBoolean()&&RequestedEqual(current[request.Key].GetProperty("value").GetDouble(),request.Value),"MODEL_UPDATE_FAILED","An exact requested owning driver did not retain its requested native scalar.");}
        var secondary=new[]{after.Feature.Distance2ParamVarID,after.Feature.TaperAngle2ParamVarID}.Where(id=>!id.IsNull).Select(Key).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach(string id in created)
        {
            cancel();Need(secondary.Contains(id),"MODEL_DRIVER_CHANGED","An allocated native variable is not an observed secondary feature driver.");
            Need(current.TryGetValue(id,out var value)&&value.GetProperty("host_object_id").GetString()==Key(after.Feature.ID)&&value.GetProperty("value_available").GetBoolean(),"MODEL_DRIVER_CHANGED","An allocated native variable is not this exact secondary feature driver.");
            Need(value.GetProperty("downstream_variables").ValueKind==JsonValueKind.Array&&value.GetProperty("downstream_variables").GetArrayLength()==0&&value.GetProperty("downstream_objects").ValueKind==JsonValueKind.Array&&
                value.GetProperty("downstream_objects").EnumerateArray().All(item=>item.GetString()==Key(after.Feature.ID)),"DEPENDENCY_OWNERSHIP_MISMATCH","A newly allocated secondary driver has unexpected/unknown external consumers.");
            var expression=ModelArithmetic.ParseStored(value.GetProperty("expression").GetString()!,cancel);double actual=value.GetProperty("value").GetDouble();
            Need(expression.References.Length==0&&RequestedEqual(expression.Evaluate(_=>throw new InvalidOperationException()),actual),"MODEL_DRIVER_CHANGED","A new secondary native driver is not its actual numeric MCS literal.");
            if(edit.Direction=="different")
            {
                if(edit.Extent2=="distance"&&Same(after.Feature.Distance2ParamVarID,id))Need(RequestedEqual(actual,edit.NativeDistance2!.Value),"MODEL_UPDATE_FAILED","An actual allocated secondary distance driver differs from its requested native length.");
                if(Same(after.Feature.TaperAngle2ParamVarID,id))Need(RequestedEqual(actual,edit.TaperAngle2!.Value*180/Math.PI),"MODEL_UPDATE_FAILED","An actual allocated secondary taper driver differs from its requested owning degree scalar.");
            }
        }
    }
    internal static string[] CreatedVariables(Snapshot before,Snapshot after)=>after.Parameters.GetProperty("variables").EnumerateArray().Select(value=>value.GetProperty("mc_id").GetString()!).Except(before.Parameters.GetProperty("variables").EnumerateArray().Select(value=>value.GetProperty("mc_id").GetString()!),StringComparer.OrdinalIgnoreCase).OrderBy(id=>id,StringComparer.OrdinalIgnoreCase).ToArray();
    static void VerifyParameterValues(JsonElement node,Snapshot after,Plan plan,ModelExtrusionExtentsSpec351 edit,Action cancel)
    {
        var parameters=node.GetProperty("parameters");if(parameters.ValueKind!=JsonValueKind.Object)return;string owner=node.GetProperty("mc_id").GetString()!;
        bool suppressedPeer=!owner.Equals(Key(after.Feature.ID),StringComparison.OrdinalIgnoreCase)&&
            node.GetProperty("suppressed").ValueKind==JsonValueKind.True&&node.GetProperty("status").GetString() is "Ok" or "Suppressed";
        foreach(var field in parameters.EnumerateObject())
        {
            cancel();string? scalar=VariableField(field.Name);if(scalar==null||field.Value.ValueKind!=JsonValueKind.String||!plan.Expected.TryGetValue(field.Value.GetString()!,out double expected))continue;
            // Secondary SDK properties are inactive in Symmetric; their actual MCS cascade is verified independently.
            if(owner.Equals(Key(after.Feature.ID),StringComparison.OrdinalIgnoreCase)&&((edit.Direction=="symmetric"&&scalar is ("distance2" or "taper_angle2"))||(edit.Extent2=="through_all"&&scalar=="distance2")))continue;
            var variable=after.Parameters.GetProperty("variables").EnumerateArray().Single(value=>value.GetProperty("mc_id").GetString()==field.Value.GetString());
            Need(variable.GetProperty("host_object_id").GetString()==owner,"MODEL_DRIVER_CHANGED","An affected parameter field does not identify its actual owning variable.");
            // VerifyVariables still checks its exact stored formula/driver
            // value; a suppressed peer has no required active shape rebuild.
            if(suppressedPeer)continue;
            if(scalar=="angle"&&parameters.TryGetProperty("angle_degrees",out var degrees))Need(degrees.ValueKind==JsonValueKind.Number&&ModelParameterSnapshot.Near(degrees.GetDouble(),expected),"MODEL_UPDATE_FAILED","A native angular feature does not match its owning degree variable.");
            else if(parameters.TryGetProperty(scalar,out var value))
            {
                if(scalar is "angle" or "angle2" or "taper_angle" or "taper_angle2")expected*=Math.PI/180;
                Need(value.ValueKind==JsonValueKind.Number&&ModelParameterSnapshot.Near(value.GetDouble(),expected),"MODEL_UPDATE_FAILED","A native feature did not rebuild to its exact affected owning driver value.");
            }
        }
    }
    // Closed GetContours loops have traversal ordinals, not stable native
    // segment IDs. Only the selected profile can also use a complete physical
    // segment bijection; area/hole inventory and all other headers stay exact.
    static JsonElement Without(JsonElement value,string field)
    {var copy=JsonNode.Parse(value.GetRawText())!.AsObject();copy.Remove(field);return Wire.Element(copy);}
    static bool PhysicalSegmentsEqual(JsonElement[] left,JsonElement[] right,Action cancel)
    {
        if(left.Length==0||left.Length!=right.Length)return false;
        var used=new bool[right.Length];
        foreach(var prior in left)
        {
            cancel();bool found=false;
            for(int i=0;i<right.Length;i++)
            {
                cancel();if(used[i]||!Equal(Without(prior,"geometry"),Without(right[i],"geometry"),cancel)||
                    !SketchGeometryComparison.Same(prior.GetProperty("geometry"),right[i].GetProperty("geometry")))continue;
                used[i]=true;found=true;break;
            }
            if(!found)return false;
        }
        return true;
    }
    static bool ProfileHistoryEqual(JsonElement left,JsonElement right,Action cancel)
    {
        if(Equal(left,right,cancel))return true;
        var a=left.GetProperty("parameters");var b=right.GetProperty("parameters");
        if(a.GetProperty("kind").GetString()!="profile"||b.GetProperty("kind").GetString()!="profile"||
            !a.TryGetProperty("contour_geometry",out var x)||!b.TryGetProperty("contour_geometry",out var y)||
            x.GetProperty("kind").GetString()!="profile_contour"||y.GetProperty("kind").GetString()!="profile_contour"||
            x.GetProperty("geometry_details_complete").ValueKind!=JsonValueKind.True||y.GetProperty("geometry_details_complete").ValueKind!=JsonValueKind.True||
            x.GetProperty("identity_scope").GetString()!="boundary_order_only_not_native_subentity_identity"||
            y.GetProperty("identity_scope").GetString()!="boundary_order_only_not_native_subentity_identity"||
            !Equal(Without(x,"elements"),Without(y,"elements"),cancel))return false;
        JsonElement[]? Segments(JsonElement value)
        {
            var elements=value.GetProperty("elements");if(elements.ValueKind!=JsonValueKind.Array)return null;
            var result=new List<JsonElement>();int index=0;
            foreach(var item in elements.EnumerateArray())
            {
                cancel();if(item.GetProperty("element_id").GetString()!=(index++).ToString(CultureInfo.InvariantCulture)||
                    item.GetProperty("geometry").GetProperty("kind").GetString() is not ("line" or "circular_arc"))return null;
                result.Add(Without(item,"element_id"));
            }
            return result.ToArray();
        }
        var prior=Segments(x);var current=Segments(y);
        if(prior==null||current==null||!PhysicalSegmentsEqual(prior,current,cancel))return false;
        var l=JsonNode.Parse(left.GetRawText())!.AsObject();var r=JsonNode.Parse(right.GetRawText())!.AsObject();
        foreach(var node in new[]{l,r})
        {
            var parameters=node["parameters"]!.AsObject();parameters.Remove("contour_geometry");
            // Revision tokens can change with native boundary enumeration; only
            // after the complete same-frame physical boundary has been proved.
            if(parameters["profile_editing"] is JsonObject editing)editing.Remove("profile_token");
        }
        return Equal(Wire.Element(l),Wire.Element(r),cancel);
    }
    static bool ClosedLoopEqual(JsonElement left,JsonElement right,Action cancel,bool physical)
    {
        if(Equal(left,right,cancel))return true;
        if(left.ValueKind!=JsonValueKind.Object||right.ValueKind!=JsonValueKind.Object||
            !left.TryGetProperty("closed",out var lc)||lc.ValueKind!=JsonValueKind.True||!right.TryGetProperty("closed",out var rc)||rc.ValueKind!=JsonValueKind.True||
            !left.TryGetProperty("geometry_details_complete",out var ld)||ld.ValueKind!=JsonValueKind.True||!right.TryGetProperty("geometry_details_complete",out var rd)||rd.ValueKind!=JsonValueKind.True||
            !left.TryGetProperty("segments",out var ls)||ls.ValueKind!=JsonValueKind.Array||!right.TryGetProperty("segments",out var rs)||rs.ValueKind!=JsonValueKind.Array||
            ls.GetArrayLength()==0||ls.GetArrayLength()!=rs.GetArrayLength()||!Equal(Without(left,"segments"),Without(right,"segments"),cancel))return false;
        int count=ls.GetArrayLength();var prior=new JsonElement[count];var current=new JsonElement[count];
        bool Segment(JsonElement item,int ordinal,out JsonElement geometry)
        {
            cancel();geometry=default;
            if(item.ValueKind!=JsonValueKind.Object||!item.TryGetProperty("index",out var index)||index.ValueKind!=JsonValueKind.Number||!index.TryGetInt32(out int value)||value!=ordinal||
                !item.TryGetProperty("native_segment_type",out var type)||type.ValueKind!=JsonValueKind.String||!item.TryGetProperty("geometry",out var curve)||curve.ValueKind!=JsonValueKind.Object||
                !curve.TryGetProperty("kind",out var kind)||kind.ValueKind!=JsonValueKind.String||
                !((type.GetString()=="sgLine"&&kind.GetString()=="line")||(type.GetString()=="sgArc"&&kind.GetString()=="circular_arc")))return false;
            geometry=Without(item,"index");return true;
        }
        for(int i=0;i<count;i++)if(!Segment(ls[i],i,out prior[i])||!Segment(rs[i],i,out current[i]))return false;
        if(physical)return PhysicalSegmentsEqual(prior,current,cancel);
        for(int start=0;start<count;start++)
        {
            cancel();bool same=true;
            for(int i=0;i<count;i++)if(!Equal(prior[i],current[(int)(((long)i+start)%count)],cancel)){same=false;break;}
            if(same)return true;
        }
        return false;
    }
    static bool ProfileDesignEqual(JsonElement left,JsonElement right,Action cancel,bool physical)
    {
        if(Equal(left,right,cancel))return true;
        if(!Equal(Without(left,"contours"),Without(right,"contours"),cancel))return false;
        var a=left.GetProperty("contours");var b=right.GetProperty("contours");
        if(a.ValueKind!=JsonValueKind.Array||b.ValueKind!=JsonValueKind.Array||a.GetArrayLength()!=b.GetArrayLength())return false;
        for(int i=0;i<a.GetArrayLength();i++)
        {
            cancel();var x=a[i];var y=b[i];
            var headerLeft=Without(Without(x,"outer"),"holes");var headerRight=Without(Without(y,"outer"),"holes");
            if(!Equal(headerLeft,headerRight,cancel)||!ClosedLoopEqual(x.GetProperty("outer"),y.GetProperty("outer"),cancel,physical))return false;
            var holesLeft=x.GetProperty("holes");var holesRight=y.GetProperty("holes");
            if(holesLeft.ValueKind!=JsonValueKind.Array||holesRight.ValueKind!=JsonValueKind.Array||holesLeft.GetArrayLength()!=holesRight.GetArrayLength())return false;
            for(int hole=0;hole<holesLeft.GetArrayLength();hole++)if(!ClosedLoopEqual(holesLeft[hole],holesRight[hole],cancel,physical))return false;
        }
        return true;
    }
    static string? FirstDifference(JsonElement left,JsonElement right,Action cancel,string path="$")
    {
        cancel();if(Equal(left,right,cancel))return null;
        if(left.ValueKind==JsonValueKind.Object&&right.ValueKind==JsonValueKind.Object)
            foreach(string name in left.EnumerateObject().Select(field=>field.Name).Union(right.EnumerateObject().Select(field=>field.Name),StringComparer.Ordinal).OrderBy(name=>name,StringComparer.Ordinal))
            {
                string next=path+"."+name;if(next.Length>256)return next[..256];
                if(!left.TryGetProperty(name,out var a)||!right.TryGetProperty(name,out var b))return next;
                if(FirstDifference(a,b,cancel,next) is {} changed)return changed;
            }
        if(left.ValueKind==JsonValueKind.Array&&right.ValueKind==JsonValueKind.Array)
        {
            if(left.GetArrayLength()!=right.GetArrayLength())return path+".length";
            for(int i=0;i<left.GetArrayLength();i++)if(FirstDifference(left[i],right[i],cancel,path+"["+i+"]") is {} changed)return changed;
        }
        return path;
    }
    internal static void Verify(Snapshot before,Snapshot after,ModelExtrusionExtentsSpec351 edit,Plan plan,Action cancel)
    {
        Need(before.DrawingDocument==after.DrawingDocument&&before.OwnerDocument==after.OwnerDocument&&before.Parameters.GetProperty("owner").GetString()==after.OwnerDocument&&before.Body.ID==after.Body.ID,
            "DOCUMENT_CONTEXT_MISMATCH","Actual drawing lookup, body owner or body identity changed.");
        Need(before.Feature.ID==after.Feature.ID&&before.Profile.ID==after.Profile.ID&&before.Sketch.ID==after.Sketch.ID&&after.Feature.ProfileID==before.Profile.ID&&after.Profile.Dependent3dFeatureID==after.Feature.ID,"PROFILE_OWNERSHIP_MISMATCH","A retained native feature/profile/parent or consumer changed.");
        Need(before.Contents.ToHashSet().SetEquals(after.Contents)&&before.FeatureOrder.SequenceEqual(after.FeatureOrder,StringComparer.OrdinalIgnoreCase)&&after.Body.LastPartItemID==before.Views.LastHistoryItem,"MODEL_ORDER_MISMATCH","Extent revision changed retained native history identity/order or End-of-Part.");
        Need(before.DirectDownstream.SequenceEqual(after.DirectDownstream,StringComparer.OrdinalIgnoreCase)&&before.Downstream.SequenceEqual(after.Downstream,StringComparer.OrdinalIgnoreCase),"MODEL_DEPENDENCY_CHANGED","A retained native direct/transitive extrusion consumer changed.");
        Need(before.Inventory.Except(after.Inventory).All(id=>id==before.Representation)&&after.Inventory.Except(before.Inventory).All(id=>id==after.Representation)&&(before.Representation==after.Representation||!after.Inventory.Contains(before.Representation)),"OBJECT_IDENTITY_MISMATCH","Extent revision changed unrelated drawing membership or retained duplicate body representations.");
        // Allocate-only admission is based on exact actual MCS identities/hosts/values, never manager counts or names.
        var created=CreatedVariables(before,after);VerifyVariables(before,after,plan,edit,created,cancel);
        bool metadataRetained=ModelExtrusionOwnedMetadata357.RetainedWithSecondaryAllocation(before.Metadata,after.Metadata,created,cancel,out string metadataReason);
        if(!metadataRetained||before.Display.GetRawText()!=after.Display.GetRawText()||!Equal(before.Sources,after.Sources,cancel)||!Equal(before.Terminal,after.Terminal,cancel))
            throw new DomainException("MODEL_STATE_UNAVAILABLE","Extent revision changed stored body/source data, appearance or retained terminal references.",false,
                ModelExtrusionGuardDiagnostics357.Describe(before,after,metadataRetained,metadataReason,cancel));
        // Native wrappers are live: durable old IDs come from captured nodes.
        var oldNode=before.Parameters.GetProperty("nodes").EnumerateArray().Single(node=>node.GetProperty("mc_id").GetString()==Key(before.Feature.ID)).GetProperty("parameters");
        var newNode=after.Parameters.GetProperty("nodes").EnumerateArray().Single(node=>node.GetProperty("mc_id").GetString()==Key(after.Feature.ID)).GetProperty("parameters");
        foreach(string field in new[]{"distance_variable_id","taper_angle_variable_id","distance2_variable_id","taper_angle2_variable_id"})
            if(oldNode.GetProperty(field).ValueKind!=JsonValueKind.Null)Need(Equal(oldNode.GetProperty(field),newNode.GetProperty(field),cancel),"MODEL_DRIVER_CHANGED","A retained native driver ID was replaced or removed.");
        var f=after.Feature;Need(f.Direction==(edit.Direction=="different"?FeatureExtentDirection.Different:FeatureExtentDirection.Symmetric)&&f.Extent==ExtrudeParamExtType.Dist&&RequestedEqual(f.Distance,edit.NativeDistance)&&RequestedEqual(f.Angle*180/Math.PI,edit.TaperAngle*180/Math.PI),"MODEL_UPDATE_FAILED","The existing native extrusion did not retain its requested direction/first length/taper.");
        if(edit.Direction=="different")Need(f.Extent2==SecondaryMode(edit)&&(edit.Extent2!="distance"||RequestedEqual(f.Distance2,edit.NativeDistance2!.Value))&&RequestedEqual(f.Angle2*180/Math.PI,edit.TaperAngle2!.Value*180/Math.PI),"MODEL_UPDATE_FAILED","The exact negative-side native termination, active length and taper were not retained.");
        else foreach(string id in new[]{oldNode.GetProperty("distance2_variable_id").GetString(),oldNode.GetProperty("taper_angle2_variable_id").GetString()}.OfType<string>())
        {var prior=before.Parameters.GetProperty("variables").EnumerateArray().Single(value=>value.GetProperty("mc_id").GetString()==id);var current=after.Parameters.GetProperty("variables").EnumerateArray().Single(value=>value.GetProperty("mc_id").GetString()==id);Need(Equal(NormalizeVariable(prior,id,plan,cancel),NormalizeVariable(current,id,plan,cancel),cancel),"MODEL_DRIVER_CHANGED","Native Symmetric activation changed inactive secondary driver design outside its proved native formula cascade.");}
        var allocatedDrivers=created.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var selectedDependencies=after.Parameters.GetProperty("nodes").EnumerateArray().Single(node=>node.GetProperty("mc_id").GetString()==Key(after.Feature.ID)).GetProperty("dependencies").EnumerateArray().Select(edge=>edge.GetString()!).ToHashSet(StringComparer.OrdinalIgnoreCase);
        Need(allocatedDrivers.All(selectedDependencies.Contains),"MODEL_DEPENDENCY_CHANGED","An actual allocated owning secondary driver is absent from the selected feature dependencies.");
        var geometric=new HashSet<string>(before.Downstream,StringComparer.OrdinalIgnoreCase);var currentNodes=after.Parameters.GetProperty("nodes").EnumerateArray().ToDictionary(node=>node.GetProperty("mc_id").GetString()!,StringComparer.OrdinalIgnoreCase);
        foreach(var node in before.Parameters.GetProperty("nodes").EnumerateArray())
        {
            cancel();string id=node.GetProperty("mc_id").GetString()!;Need(currentNodes.TryGetValue(id,out var current),"MODEL_DEPENDENCY_CHANGED","A retained history item disappeared.");
            var prior=NormalizeNode(node,Key(before.Feature.ID),plan,geometric,allocatedDrivers,cancel);var actual=NormalizeNode(current,Key(before.Feature.ID),plan,geometric,allocatedDrivers,cancel);
            if(!(id.Equals(Key(before.Profile.ID),StringComparison.OrdinalIgnoreCase)?ProfileHistoryEqual(prior,actual,cancel):Equal(prior,actual,cancel)))
            {
                string? difference=FirstDifference(prior,actual,cancel);
                throw new DomainException("MODEL_DEPENDENCY_CHANGED","A retained history item changed outside the requested extent, proved formula descendant or native derived datum position.",false,
                    new{retained_node_id=id,first_observed_node_difference=difference==null?null:difference[..Math.Min(256,difference.Length)],selected_feature_id=Key(before.Feature.ID),actual_new_driver_ids=allocatedDrivers.OrderBy(value=>value,StringComparer.OrdinalIgnoreCase).ToArray()});
            }
            VerifyParameterValues(current,after,plan,edit,cancel);
        }
        var currentSketches=after.Sketches.EnumerateArray().ToDictionary(value=>value.GetProperty("id").GetString()!,StringComparer.OrdinalIgnoreCase);
        foreach(var value in before.Sketches.EnumerateArray())
        {
            cancel();string id=value.GetProperty("id").GetString()!;Need(currentSketches.TryGetValue(id,out var current),"MODEL_DEPENDENCY_CHANGED","A retained native sketch identity disappeared.");
            var priorDesign=SketchDesign(value,plan,geometric.Contains(id),cancel);var actualDesign=SketchDesign(current,plan,geometric.Contains(id),cancel);
            if(!ModelSketchConstraintObservation353.Complete(value.GetProperty("constraints"))||!ModelSketchConstraintObservation353.Complete(current.GetProperty("constraints")))
            {priorDesign=ModelSketchConstraintObservation353.WithoutUnavailableDetails(priorDesign);actualDesign=ModelSketchConstraintObservation353.WithoutUnavailableDetails(actualDesign);}
            Need(Equal(priorDesign,actualDesign,cancel),"MODEL_DEPENDENCY_CHANGED","A retained sketch changed its observed local construction, source elements, available constraints or support identity.");
        }
        var currentProfiles=after.Profiles.EnumerateArray().ToDictionary(value=>value.GetProperty("id").GetString()!,StringComparer.OrdinalIgnoreCase);
        var beforeFrames=before.Sketches.EnumerateArray().ToDictionary(value=>value.GetProperty("id").GetString()!,value=>value.GetProperty("geometry").GetProperty("frame"),StringComparer.OrdinalIgnoreCase);
        var afterFrames=after.Sketches.EnumerateArray().ToDictionary(value=>value.GetProperty("id").GetString()!,value=>value.GetProperty("geometry").GetProperty("frame"),StringComparer.OrdinalIgnoreCase);
        foreach(var value in before.Profiles.EnumerateArray())
        {
            cancel();string id=value.GetProperty("id").GetString()!,parent=value.GetProperty("parent_id").GetString()!;Need(currentProfiles.TryGetValue(id,out var current),"MODEL_DEPENDENCY_CHANGED","A retained native profile disappeared.");
            var left=JsonNode.Parse(value.GetRawText())!.AsObject();var right=JsonNode.Parse(current.GetRawText())!.AsObject();
            if(geometric.Contains(parent)){left["contours"]=JsonNode.Parse(LocalGeometry(value.GetProperty("contours"),beforeFrames[parent],cancel).GetRawText());right["contours"]=JsonNode.Parse(LocalGeometry(current.GetProperty("contours"),afterFrames[parent],cancel).GetRawText());}
            var prior=Wire.Element(left);var actual=Wire.Element(right);
            if(!ProfileDesignEqual(prior,actual,cancel,id.Equals(Key(before.Profile.ID),StringComparison.OrdinalIgnoreCase)))
                throw new DomainException("MODEL_DEPENDENCY_CHANGED","A retained profile changed selected areas/holes, parent, local contour or native forming backlink.",false,
                    ModelRepresentationDiagnostics377.Describe(new("set_model_extrusion_extent",Key(after.Body.ID),before.Representation.Handle.ToString(),after.Representation.Handle.ToString(),
                        before.DrawingDocument,after.DrawingDocument,before.OwnerDocument,after.OwnerDocument,null,null,
                        before.Metadata.GetProperty("owned").GetArrayLength(),after.Metadata.GetProperty("owned").GetArrayLength()),
                        [new("retained_profile_"+id,false,"Exact profile header/area/hole inventory; selected complete contour segments require a physical bijection; other profiles allow only cyclic ordinal rotation",prior,actual)],cancel));
        }
        ModelViewFollowers.Verify(after.Body,before.Views,cancel,refreshStandalone:true);
    }
}
