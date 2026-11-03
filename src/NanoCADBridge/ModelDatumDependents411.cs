using System.Text.Json;
using System.Text.Json.Nodes;
using Multicad;
using Multicad.DatabaseServices;
using Multicad.Mc3D;
using NanoCAD.Contracts;
using Db=Teigha.DatabaseServices;

namespace NanoCADBridge;

// WorkPlane support is a native dependency, not a coordinate replacement. The
// changed graph comes from the actual part and its native downstream inventory.
internal static class ModelDatumDependents411
{
    internal sealed record Snapshot(ModelFeatureReorderState345.Snapshot Part,McObjectId[] Roots,
        HashSet<string> Affected,Dictionary<string,NativeSketchMembers385.Snapshot> Inputs,
        ModelSketchConstraintMetadata379.Binding[] Storage,HashSet<string> MovingSources);
    internal sealed record SupportChange(string Sketch,string? Previous,string Current,McObjectId Datum);
    static void Need([System.Diagnostics.CodeAnalysis.DoesNotReturnIf(false)]bool ok,string code,string message)
        =>ModelDatumPlaneState353.Need(ok,code,message);
    static string Key(McObjectId id)=>id.ToString();
    static bool Equal(JsonElement a,JsonElement b,Action cancel)=>ModelFeatureReorderState345.Equivalent(a,b,cancel);

    internal static void RequireConsumerScope(WorkPlane plane,Mc3dSolid body,McDocument owner,Action cancel)
    {
        var contents=ModelInventoryBudget.History(body,cancel).ToHashSet();var views=ModelViewFollowers.Resolve(body,contents.ToArray(),cancel,includeStandalone:true);
        var consumers=plane.GetAllDependent(false);Need(consumers!=null,"DEPENDENCY_INVENTORY_UNAVAILABLE","Actual WorkPlane consumers are unavailable; no unshared-plane claim is made.");
        ModelInventoryBudget.Ids(consumers,cancel);NativePathSources.Inspection? observed=null;
        foreach(var id in consumers)
        {
            cancel();if(id==body.ID||ModelViewFollowers.OwnsDependent(views,id))continue;
            var item=id.GetObjectOfType<HistoryItem>();
            if(item!=null)
            {
                Need(item.ID==id&&item.DbEntity.Document?.ID==owner.ID&&(item.HostSolidID.IsNull||item.HostSolidID==body.ID&&contents.Contains(id)),
                    "DEPENDENCY_OWNERSHIP_MISMATCH","The selected WorkPlane also serves another native part/document; its shared foreign consumers are not changed.");
                Need((item.Status.ToString()=="Ok"||ProjectedHistorySources.HealthySuppressed(item))&&!item.IsBeyondEndOfPart,
                    "MODEL_STATE_UNAVAILABLE","An actual WorkPlane consumer is unhealthy or beyond End Of Part.");continue;
            }
            observed??=NativePathSources.Inspect(contents.ToArray(),cancel);
            bool known=observed.Details.TryGetValue(Key(id),out var detail);
            Need(contents.Contains(id)&&known,"DEPENDENCY_OWNERSHIP_MISMATCH","An actual WorkPlane dependent lacks a verified selected-part native binding.");
            var row=Wire.Element(detail!);Need(row.TryGetProperty("kind",out var kind)&&kind.GetString() is "native_sketch_input_source" or "native_sketch_relation_source" or "native_sketch_construction_point",
                "DEPENDENCY_OWNERSHIP_MISMATCH","An actual WorkPlane dependent is not a witnessed selected-part sketch helper.");
        }
    }

    internal static Snapshot Capture(Mc3dSolid body,Db.ObjectId representation,ModifyArguments args,DocumentState state,Db.Transaction tx,
        IEnumerable<McObjectId> roots,Action cancel)
    {
        var part=ModelFeatureReorderState345.Capture(body,representation,args,state,tx,cancel,captureFullDesign:true);
        var preserved=JsonNode.Parse(part.Preserved.GetRawText())!.AsObject();
        preserved["nodes"]=JsonNode.Parse(ModelDependencyGraph410.Nodes(part.Preserved.GetProperty("nodes"),part.Contents,cancel).GetRawText());
        part=part with{Preserved=Wire.Element(preserved)};
        var selected=roots.Distinct().ToArray();Need(selected.All(part.Contents.Contains),"DEPENDENCY_OWNERSHIP_MISMATCH","Each edited datum consumer must belong to this selected part.");
        var affected=ModelDependencyGraph410.Closure(part.Preserved,selected.Select(Key),cancel);
        affected.UnionWith(ModelDependencyGraph410.OwnedClosure(body,selected,cancel,part.Views));
        var inputs=new Dictionary<string,NativeSketchMembers385.Snapshot>(StringComparer.OrdinalIgnoreCase);
        foreach(var id in part.Contents)
        {
            cancel();if(!affected.Contains(Key(id)))continue;var sketch=id.GetObjectOfType<PlanarSketch>();if(sketch==null)continue;
            var geometry=part.Preserved.GetProperty("sketches").EnumerateArray().Single(row=>row.GetProperty("id").GetString()==Key(id)).GetProperty("geometry");
            if(geometry.GetProperty("elements").GetArrayLength()==0)continue;
            var current=NativeSketchMembers385.Capture(sketch,state,tx,cancel);
            Need(current.Members.All(m=>m.Owned||m.ReferenceOnly),"DEPENDENCY_OWNERSHIP_MISMATCH","A moved sketch contains geometry shared with another sketch; source projections remain the supported immutable-reference route.");
            inputs.Add(Key(id),current);
        }
        var owner=ModelProfileDiscovery348.OwnerDocument(body,state);var drawing=ModelProfileDiscovery348.DrawingDocument(state);
        var storage=ModelSketchConstraintMetadata379.Bind(part.Contents.Concat(inputs.Values.SelectMany(s=>s.Members.Select(m=>m.ObjectId).Concat(s.Helpers).Concat(s.Construction.Select(p=>p.Id)))),representation,owner,drawing,state,tx,cancel);
        var movingSources=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach(var input in inputs.Values)
        {
            var projected=input.Sketch.GetContainingObjects(false,true,false);Need(projected!=null,"SKETCH_BINDING_UNAVAILABLE","Actual projection membership is unavailable.");ModelInventoryBudget.Ids(projected,cancel);
            foreach(var id in projected)
            {
                cancel();var projection=id.GetObjectOfType<ProjectionSketchEnt>();if(projection==null)continue;
                Need(projection.ID==id&&projection.HostSketchID==input.Sketch.ID,"SKETCH_BINDING_UNAVAILABLE","A current projection resolves another native sketch/item identity.");
                var source=projection.SrcObjectID;
                if(source.IsEdge&&source.ParentId==body.ID&&affected.Contains(Key(source.HostFeatureOrParentId)))movingSources.Add(Key(source));
            }
        }
        return new(part,selected,affected,inputs,storage,movingSources);
    }

    // The geometry getter explicitly supplies WCS. Only those known geometric
    // vectors are expressed in the observed orthonormal native sketch frame.
    internal static JsonElement Local(JsonElement geometry,SketchPlaneSpec frame,Action cancel)
    {
        var root=JsonNode.Parse(geometry.GetRawText())!;var n=DrawingWorkplane.Normal(frame);
        void Visit(JsonNode? node)
        {
            cancel();if(node is JsonArray array){foreach(var row in array)Visit(row);return;}if(node is not JsonObject obj)return;
            foreach(var field in obj.ToArray())
            {
                if(field.Value is JsonArray vector&&vector.Count==3&&field.Key is "start" or "end" or "center" or "point" or "normal" or "reference_direction")
                {
                    var v=vector.Select(x=>x!.GetValue<double>()).ToArray();Need(v.All(double.IsFinite),"INVALID_GEOMETRY","A moved native input has nonfinite geometry.");
                    bool point=field.Key is "start" or "end" or "center" or "point";
                    var p=new Point3(v[0]-(point?frame.Origin.X:0),v[1]-(point?frame.Origin.Y:0),v[2]-(point?frame.Origin.Z:0));
                    obj[field.Key]=JsonSerializer.SerializeToNode(new[]{DrawingWorkplane.Dot(p,frame.XAxis),DrawingWorkplane.Dot(p,frame.YAxis),DrawingWorkplane.Dot(p,n)},Wire.Json);
                }
                else Visit(field.Value);
            }
        }
        Visit(root);return Wire.Element(root);
    }

    static JsonElement InputDesign(NativeSketchMembers385.Snapshot value,Action cancel)
    {
        var root=JsonNode.Parse(value.Stored.GetRawText())!.AsObject();root["plane"]=null;
        foreach(var member in root["members"]!.AsArray())
        {
            cancel();string id=member!["member_id"]!.GetValue<string>();var actual=value.Members.Single(m=>Key(m.Id)==id);
            if(actual.Owned)member["geometry"]=JsonNode.Parse(Local(actual.Geometry,value.Plane,cancel).GetRawText());
            else if(actual.ReferenceOnly)
            {
                // Its exact projection/source binding is retained separately.
                // Projection geometry is evaluated in the newly selected plane.
                var geometry=JsonNode.Parse(actual.Geometry.GetRawText())!;NativeSketchGeometry385.Motion(geometry,cancel);
                if(geometry is JsonObject g&&g.ContainsKey("normal"))g["normal"]=null;member["geometry"]=geometry;
            }
        }
        foreach(var point in root["construction"]!.AsArray())point!["geometry"]=JsonNode.Parse(Local(Wire.Element(point["geometry"]!),value.Plane,cancel).GetRawText());
        return Wire.Element(root);
    }

    static void SourceBindings(JsonObject root,Snapshot value,SupportChange? change,Action cancel)
    {
        if(change==null)return;
        void Bindings(JsonNode? node)
        {
            if(node is not JsonArray rows)return;
            foreach(var row in rows)
            {cancel();if(row?["host_sketch_id"]?.GetValue<string>()==change.Sketch)row!["planar_object_id"]="$SELECTED_SUPPORT";}
        }
        foreach(var sketch in root["sketches"]!.AsArray())if(sketch!["id"]!.GetValue<string>()==change.Sketch)
        {
            sketch["support"]="$SELECTED_SUPPORT";
            if(sketch["retained"] is JsonObject stored){stored["support_id"]="$SELECTED_SUPPORT";Bindings(stored["projection_bindings"]);ReplaceDependency(stored,"dependencies",change,value.Part.Previous[change.Sketch]);}
        }
        foreach(var profile in root["profiles"]!.AsArray())if(profile!["parent_sketch_id"]!.GetValue<string>()==change.Sketch)
        {profile["support_id"]="$SELECTED_SUPPORT";Bindings(profile["projection_bindings"]);ReplaceDependency(profile!.AsObject(),"sketch_dependencies",change,value.Part.Previous[change.Sketch]);}
        var raw=value.Part.Preserved.GetProperty("nodes").EnumerateArray().ToDictionary(node=>node.GetProperty("mc_id").GetString()!,StringComparer.OrdinalIgnoreCase);
        void Dependencies(JsonObject node)
        {
            // The exact raw edge is separately observable. Reconstruct only
            // these selected nodes from that inventory: an old face's stable
            // edge can collapse to its parent body, which is not a removable
            // alias for every unrelated body reference.
            string id=node["mc_id"]!.GetValue<string>();node["dependencies"]=JsonNode.Parse(raw[id].GetProperty("dependencies").GetRawText());
            ReplaceDependency(node,"dependencies",change);
            node["stable_dependencies"]=JsonNode.Parse(node["dependencies"]!.ToJsonString());
        }
        foreach(var node in root["nodes"]!.AsArray())
        {
            cancel();if(node?["mc_id"]?.GetValue<string>()==change.Sketch)
            {Dependencies(node!.AsObject());if(node["parameters"] is JsonObject p){p["support_id"]="$SELECTED_SUPPORT";p["support_definition"]="$SELECTED_SUPPORT_DEFINITION";p["support_parameter_id"]="$SELECTED_SUPPORT_DRIVER";}}
            if(node?["parameters"] is JsonObject projection&&projection["kind"]?.GetValue<string>()=="projection"&&projection["host_sketch_id"]?.GetValue<string>()==change.Sketch)
            {projection["planar_object_id"]="$SELECTED_SUPPORT";Dependencies(node!.AsObject());}
            if(node?["parameters"] is JsonObject profile&&profile["kind"]?.GetValue<string>()=="profile"&&profile["parent_sketch_id"]?.GetValue<string>()==change.Sketch)profile["support_id"]="$SELECTED_SUPPORT";
        }
        if(root["native_source_ids"] is JsonArray sources)
            root["native_source_ids"]=JsonSerializer.SerializeToNode(sources.Select(s=>s!.GetValue<string>()).Where(s=>s!=change.Previous&&s!=change.Current).ToArray(),Wire.Json);
    }
    static void ReplaceDependency(JsonObject node,string property,SupportChange change,string? predecessor=null)
    {
        if(node[property] is not JsonArray values)return;
        node[property]=JsonSerializer.SerializeToNode(values.Select(x=>x!.GetValue<string>()).Where(x=>x!=change.Previous&&x!=change.Current&&x!=predecessor).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(x=>x,StringComparer.OrdinalIgnoreCase).ToArray(),Wire.Json);
    }

    static JsonElement Design(Snapshot value,IReadOnlySet<string> affected,SupportChange? change,bool datumAdded,Action cancel)
    {
        var root=JsonNode.Parse(ModelDependencyGraph410.ProtectPartDerived(value.Part.Preserved,affected,new HashSet<string>(),cancel).GetRawText())!.AsObject();
        void RestoreSources(JsonNode? target,JsonElement actual)
        {
            if(target is not JsonArray rows)return;
            var sources=actual.EnumerateArray().ToDictionary(row=>row.GetProperty("id").GetString()!,StringComparer.OrdinalIgnoreCase);
            foreach(var row in rows)
            {
                cancel();string id=row!["id"]!.GetValue<string>();var source=sources[id];
                if(source.GetProperty("source_kind").GetString()=="native_part_edge"&&!value.MovingSources.Contains(source.GetProperty("source_object_id").GetString()!))
                    row["source_geometry"]=JsonNode.Parse(source.GetProperty("source_geometry").GetRawText());
            }
        }
        var actualSketches=value.Part.Preserved.GetProperty("sketches").EnumerateArray().ToDictionary(row=>row.GetProperty("id").GetString()!,StringComparer.OrdinalIgnoreCase);
        foreach(var sketch in root["sketches"]!.AsArray())if(sketch!["retained"] is JsonObject retained)
            RestoreSources(retained["projection_bindings"],actualSketches[sketch["id"]!.GetValue<string>()].GetProperty("retained").GetProperty("projection_bindings"));
        var actualProfiles=value.Part.Preserved.GetProperty("profiles").EnumerateArray().ToDictionary(row=>row.GetProperty("profile_id").GetString()!,StringComparer.OrdinalIgnoreCase);
        foreach(var profile in root["profiles"]!.AsArray())
        {
            RestoreSources(profile!["projection_bindings"],actualProfiles[profile["profile_id"]!.GetValue<string>()].GetProperty("projection_bindings"));
            if(affected.Contains(profile!["parent_sketch_id"]!.GetValue<string>())&&profile["constraint_observation"] is {} observation)
                profile["constraint_observation"]=JsonNode.Parse(ModelDependencyGraph410.ConstraintPoints(Wire.Element(observation),cancel).GetRawText());
        }
        SourceBindings(root,value,change,cancel);
        if(change!=null&&datumAdded)
            foreach(string key in new[]{"nodes","construction"})if(root[key] is JsonArray rows)
                for(int i=rows.Count-1;i>=0;i--)if(rows[i]?[key=="nodes"?"mc_id":"id"]?.GetValue<string>()==change.Current)rows.RemoveAt(i);
        return Wire.Element(root);
    }

    internal static JsonElement SupportAcquisition(JsonElement before,JsonElement after,string body,string newSketch,Action cancel)
    {
        var old=JsonNode.Parse(before.GetRawText())!.AsObject();var current=JsonNode.Parse(after.GetRawText())!.AsObject();
        string? prior=old["host_solid_id"]?.GetValue<string>(),now=current["host_solid_id"]?.GetValue<string>();
        if(prior==null&&now==body)
        {
            old["host_solid_id"]=body;
            string? previous=current["native_order_before_id"]?.GetValue<string>();
            old["native_order_before_id"]=previous;
            if(old["dependencies"] is JsonArray a&&current["dependencies"] is JsonArray b)
            {
                var oldIds=a.Select(v=>v!.GetValue<string>()).ToHashSet(StringComparer.OrdinalIgnoreCase);var newIds=b.Select(v=>v!.GetValue<string>()).ToHashSet(StringComparer.OrdinalIgnoreCase);
                Need(oldIds.IsSubsetOf(newIds)&&newIds.Except(oldIds,StringComparer.OrdinalIgnoreCase).All(id=>id==previous),"DATUM_DEPENDENCY_CHANGED","Native support acquisition changed a non-order source dependency.");
                old["dependencies"]=JsonNode.Parse(b.ToJsonString());
            }
        }
        else Need(prior==now,"DATUM_HOST_MISMATCH","The selected reusable WorkPlane acquired another part's host.");
        cancel();return Wire.Element(old);
    }

    static string? SelectedProfile(Snapshot value,string feature,string sketch,Action cancel)
    {
        if(!value.Part.Features.Contains(feature,StringComparer.OrdinalIgnoreCase))return null;
        var node=value.Part.Preserved.GetProperty("nodes").EnumerateArray().Single(row=>row.GetProperty("mc_id").GetString()==feature);
        if(!node.TryGetProperty("parameters",out var parameters)||parameters.ValueKind!=JsonValueKind.Object||
            !parameters.TryGetProperty("kind",out var kind)||kind.GetString() is not ("extrusion" or "revolution" or "sweep" or "loft")||
            !parameters.TryGetProperty("profile_id",out var selected)||selected.ValueKind!=JsonValueKind.String)return null;
        string profile=selected.GetString()!;string body=Key(value.Part.Body.ID);
        foreach(var row in value.Part.Preserved.GetProperty("profiles").EnumerateArray())
        {
            cancel();if(row.GetProperty("profile_id").GetString()!=profile)continue;
            return row.GetProperty("parent_sketch_id").GetString()==sketch&&
                row.GetProperty("profile_host_solid_id").GetString()==body&&row.GetProperty("sketch_host_solid_id").GetString()==body&&
                row.GetProperty("mc_document_id").GetString()==value.Part.Preserved.GetProperty("variables_owner").GetString()&&
                node.GetProperty("dependencies").EnumerateArray().Any(id=>id.GetString()==profile)?profile:null;
        }
        return null;
    }

    static bool RetainedPredecessor(Snapshot before,Snapshot after,KeyValuePair<string,string?> link,
        SupportChange? support,bool added,Action cancel)
    {
        cancel();if(!after.Part.Previous.TryGetValue(link.Key,out var actual))return false;
        if(link.Value==actual)return true;
        if(!added||support==null)return false;
        if(link.Key==support.Sketch&&actual==support.Current&&after.Part.Previous.TryGetValue(support.Current,out var previous)&&previous==link.Value)return true;
        // Native first acquisition can keep the construction sketch beside the
        // forming chain: its actual profile consumer then points directly to
        // that sketch's unchanged preceding forming feature. This is not a
        // feature reorder. Prove the same typed profile/parent in both frozen
        // snapshots; every unrelated link and all stored design stay exact.
        if(link.Value!=support.Sketch||!before.Part.Previous.TryGetValue(support.Sketch,out var predecessor)||predecessor==null||actual!=predecessor||
            !after.Part.Previous.TryGetValue(support.Sketch,out var sketchPrevious)||sketchPrevious!=predecessor||
            !before.Part.Features.Contains(predecessor,StringComparer.OrdinalIgnoreCase))return false;
        string? profile=SelectedProfile(before,link.Key,support.Sketch,cancel);
        return profile!=null&&profile==SelectedProfile(after,link.Key,support.Sketch,cancel);
    }

    internal static string[] Verify(Snapshot before,Snapshot after,Action cancel,SupportChange? support=null)
    {
        Need(before.Part.Body.ID==after.Part.Body.ID&&before.Part.NativeDocument==after.Part.NativeDocument&&before.Part.Features.SequenceEqual(after.Part.Features)&&
            before.Part.Preserved.GetProperty("last").GetRawText()==after.Part.Preserved.GetProperty("last").GetRawText(),"MODEL_ORDER_MISMATCH","Native datum revision changed the selected part, feature order or terminal.");
        bool added=support!=null&&!before.Part.Contents.Contains(support.Datum)&&after.Part.Contents.Contains(support.Datum);
        var allowed=added?new[]{support!.Datum}:Array.Empty<McObjectId>();
        Need(before.Part.Contents.ToHashSet().SetEquals(after.Part.Contents.Except(allowed)),"MODEL_ORDER_MISMATCH","Native support edit changed unrelated history identities.");
        var actualAffected=new HashSet<string>(after.Affected,StringComparer.OrdinalIgnoreCase);if(added)actualAffected.Remove(support!.Current);
        Need(before.Affected.SetEquals(actualAffected),"MODEL_DEPENDENCY_CHANGED","The actual native downstream graph changed outside the selected support relationship.");
        Need(before.Part.Previous.All(pair=>RetainedPredecessor(before,after,pair,support,added,cancel)),
            "MODEL_ORDER_MISMATCH","Native support edit changed a retained predecessor outside its exact acquired datum or selected-sketch profile-consumer transition.");
        Need(before.Inputs.Keys.ToHashSet(StringComparer.OrdinalIgnoreCase).SetEquals(after.Inputs.Keys),"SKETCH_BINDING_UNAVAILABLE","A retained affected native sketch/input inventory disappeared.");
        JsonElement Protected(Snapshot snapshot)=>Wire.Element(new{part=Design(snapshot,before.Affected,support,added,cancel),inputs=snapshot.Inputs.OrderBy(pair=>pair.Key,StringComparer.OrdinalIgnoreCase).Select(pair=>new{id=pair.Key,state=InputDesign(pair.Value,cancel)}).ToArray()});
        var oldDesign=Protected(before);var newDesign=Protected(after);
        var storage=ModelSketchConstraintMetadata379.Compare(oldDesign,newDesign,before.Part.BodyMetadata,after.Part.BodyMetadata,before.Storage,after.Storage,cancel);
        Need(storage.retained&&Equal(oldDesign,storage.design,cancel)&&Equal(before.Part.BodyMetadata,storage.body,cancel),"MODEL_DEPENDENCY_CHANGED","Native support edit changed retained design, source geometry, variable expressions, foreign links or readable metadata outside its scoped derived geometry.");
        Need(before.Part.Properties.GetRawText()==after.Part.Properties.GetRawText(),"MODEL_STATE_UNAVAILABLE","Native support edit changed retained part appearance.");
        Need(before.Part.Constraints.Keys.ToHashSet(StringComparer.OrdinalIgnoreCase).SetEquals(after.Part.Constraints.Keys),"CONSTRAINTS_UNAVAILABLE","A retained sketch constraint observation disappeared.");
        foreach(var pair in before.Part.Constraints)
        {
            var current=after.Part.Constraints[pair.Key];var prior=pair.Value;
            if(before.Affected.Contains(pair.Key))
            {prior=ModelDependencyGraph410.ConstraintPoints(prior,cancel);current=ModelDependencyGraph410.ConstraintPoints(current,cancel);}
            Need(ModelSketchConstraintObservation353.Equivalent(prior,current,cancel),"CONSTRAINT_BASELINE_CHANGED","A retained native relation, selector, driver or constraint scope changed during its support revision.");
        }
        ModelViewFollowers.Verify(after.Part.Body,before.Part.Views,cancel,refreshStandalone:true);
        return storage.changed?[ModelSketchConstraintMetadata379.Warning]:[];
    }

    internal static void DetachedSupport(ModelDatumPlaneState353.Snapshot before,ModelDatumPlaneState353.Snapshot after,
        string sketch,IReadOnlySet<string> affected,Action cancel)
    {
        var priorDesign=JsonNode.Parse(before.Design.GetRawText())!.AsObject();var currentDesign=JsonNode.Parse(after.Design.GetRawText())!.AsObject();
        if(priorDesign["driver"] is JsonObject oldDriver&&currentDesign["driver"] is JsonObject newDriver&&oldDriver["downstream_objects"] is JsonArray oldObjects&&newDriver["downstream_objects"] is JsonArray newObjects)
        {
            var oldIds=oldObjects.Select(id=>id!.GetValue<string>()).ToHashSet(StringComparer.OrdinalIgnoreCase);var currentIds=newObjects.Select(id=>id!.GetValue<string>()).ToHashSet(StringComparer.OrdinalIgnoreCase);
            Need(currentIds.IsSubsetOf(oldIds)&&oldIds.Except(currentIds,StringComparer.OrdinalIgnoreCase).All(affected.Contains),"SUPPORT_DEPENDENCY_CHANGED","Rebinding a sketch changed an unrelated previous-datum driver consumer.");
            oldDriver.Remove("downstream_objects");newDriver.Remove("downstream_objects");
        }
        Need(before.Read.plane_id==after.Read.plane_id&&before.Read.mc_document_id==after.Read.mc_document_id&&before.Read.host_solid_id==after.Read.host_solid_id&&
            before.Read.native_order_before_id==after.Read.native_order_before_id&&Equal(Wire.Element(priorDesign),Wire.Element(currentDesign),cancel),"SUPPORT_DEFINITION_CHANGED","Rebinding the sketch changed its previous WorkPlane's identity, host, physical definition, driver or metadata.");
        Need(before.Read.direct_consumer_ids.Where(id=>id!=sketch).ToHashSet(StringComparer.OrdinalIgnoreCase).SetEquals(after.Read.direct_consumer_ids),
            "SUPPORT_DEPENDENCY_CHANGED","Rebinding a sketch changed another direct consumer of its previous WorkPlane.");
        var old=before.Read.downstream_consumer_ids.ToHashSet(StringComparer.OrdinalIgnoreCase);var current=after.Read.downstream_consumer_ids.ToHashSet(StringComparer.OrdinalIgnoreCase);
        Need(current.IsSubsetOf(old)&&old.Except(current,StringComparer.OrdinalIgnoreCase).All(affected.Contains),"SUPPORT_DEPENDENCY_CHANGED","Rebinding a sketch changed unscoped downstream consumers of its previous WorkPlane.");
        if(before.Read.representation is {} prior)Need(after.Read.representation is {} observed&&Equal(prior,observed,cancel),"DATUM_METADATA_CHANGED","Rebinding a sketch changed its previous WorkPlane's readable representation metadata.");
    }
}
