using System.Text.Json;
using System.Text.Json.Nodes;
using Multicad;
using Multicad.Mc3D;
using NanoCAD.Contracts;

namespace NanoCADBridge;

// Separate stored design from geometry that the native dependency graph must
// recompute. The closure is read from actual dependencies, not a shape fixture
// or a limit on the number/order of finishing features.
internal static class ModelDependencyGraph410
{
    internal static HashSet<string> OwnedClosure(Mc3dSolid body,IEnumerable<McObjectId> roots,Action cancel,
        ModelViewFollowers.Snapshot? observers=null)
    {
        var contents=ModelInventoryBudget.History(body,cancel).ToHashSet();
        var result=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        NativePathSources.Inspection? sources=null;
        foreach(var root in roots.Distinct())
        {
            cancel();var item=root.GetObjectOfType<HistoryItem>();
            if(!contents.Contains(root)||item==null||item.ID!=root||item.HostSolidID!=body.ID)
                throw new DomainException("DEPENDENCY_OWNERSHIP_MISMATCH","An edited native graph root lacks exact ownership in this part.");
            var downstream=item.GetAllDependent(false);
            if(downstream==null)throw new DomainException("DEPENDENCY_INVENTORY_UNAVAILABLE","Native transitive downstream inventory is unavailable.");
            ModelInventoryBudget.Ids(downstream,cancel);result.Add(root.ToString());
            foreach(var id in downstream)
            {
                cancel();if(!contents.Contains(id))
                {
                    if(observers!=null&&ModelViewFollowers.OwnsDependent(observers,id))continue;
                    throw new DomainException("DEPENDENCY_OWNERSHIP_MISMATCH","A native downstream consumer is outside this part and its declared view followers.");
                }
                var consumer=id.GetObjectOfType<HistoryItem>();
                if(consumer==null)
                {
                    // SDK GetAllDependent returns objects, not only HistoryItem
                    // wrappers. Admit generated member/relation/variable IDs
                    // only through a witnessed current native sketch binding.
                    sources??=NativePathSources.Inspect(contents,cancel);
                    if(!sources.Details.TryGetValue(id.ToString(),out var detail))
                        throw new DomainException("DEPENDENCY_OWNERSHIP_MISMATCH","A native downstream helper lacks a witnessed current sketch binding.");
                    var observed=Wire.Element(detail);
                    if(!observed.TryGetProperty("kind",out var kind)||kind.GetString() is not ("native_sketch_input_source" or "native_sketch_relation_source" or "native_sketch_construction_point")||
                        !observed.TryGetProperty("sketch_id",out var selected))
                        throw new DomainException("DEPENDENCY_OWNERSHIP_MISMATCH","A native downstream source is not an actual internal sketch member, relation or construction point.");
                    var hostId=contents.SingleOrDefault(candidate=>candidate.ToString().Equals(selected.GetString(),StringComparison.OrdinalIgnoreCase));
                    var sketch=hostId.IsNull?null:hostId.GetObjectOfType<PlanarSketch>();
                    if(sketch==null||sketch.ID!=hostId||sketch.HostSolidID!=body.ID||sketch.DbEntity.Document?.ID!=body.DbEntity.Document.ID||
                        !(sketch.Status.ToString()=="Ok"||ProjectedHistorySources.HealthySuppressed(sketch)))
                        throw new DomainException("DEPENDENCY_OWNERSHIP_MISMATCH","A native downstream helper's actual healthy sketch belongs to another part/document.");
                    result.Add(id.ToString());continue;
                }
                if(consumer.ID!=id||consumer.HostSolidID!=body.ID||
                    !(consumer.Status.ToString()=="Ok"||ProjectedHistorySources.HealthySuppressed(consumer)))
                    throw new DomainException("DEPENDENCY_OWNERSHIP_MISMATCH","A native downstream consumer lacks exact healthy same-part ownership.");
                result.Add(id.ToString());
            }
        }
        return result;
    }
    internal static JsonElement Nodes(JsonElement nodes,McObjectId[] contents,Action? cancel=null)
    {
        var ids=contents.ToDictionary(id=>id.ToString(),StringComparer.OrdinalIgnoreCase);
        var result=JsonNode.Parse(nodes.GetRawText())!.AsArray();
        foreach(var node in result)
        {
            cancel?.Invoke();string id=node!["mc_id"]!.GetValue<string>();
            if(!ids.TryGetValue(id,out var native))throw new DomainException("HISTORY_UNAVAILABLE","A history node lacks its actual native identity.");
            var item=native.GetObjectOfType<HistoryItem>();
            if(item==null){node["stable_dependencies"]=JsonNode.Parse(node["dependencies"]!.ToJsonString());continue;}
            // The SDK replaces generated subentity IDs with their actual owner
            // IDs. Exact uncollapsed IDs remain in the ordinary observation.
            var dependencies=item.GetDependsOn(true,false);
            if(dependencies==null)throw new DomainException("HISTORY_UNAVAILABLE","Native source-object dependency inventory is unavailable.");
            ModelInventoryBudget.Ids(dependencies,cancel);
            node["stable_dependencies"]=JsonSerializer.SerializeToNode(dependencies.Select(x=>x.ToString()).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(x=>x,StringComparer.OrdinalIgnoreCase).ToArray(),Wire.Json);
        }
        return Wire.Element(result);
    }
    internal static HashSet<string> Closure(JsonElement snapshot,IEnumerable<string> roots,Action? cancel=null)
    {
        var nodes=snapshot.GetProperty("nodes").EnumerateArray().ToArray();
        var known=nodes.Select(n=>n.GetProperty("mc_id").GetString()!).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var affected=roots.ToHashSet(StringComparer.OrdinalIgnoreCase);
        if(!affected.IsSubsetOf(known))throw new DomainException("OUT_OF_SCOPE","An affected parameter consumer is outside the observed native history.");
        bool changed;
        do
        {
            changed=false;
            foreach(var node in nodes)
            {
                cancel?.Invoke();string id=node.GetProperty("mc_id").GetString()!;
                if(affected.Contains(id))continue;
                var dependencies=node.GetProperty("dependencies").EnumerateArray().Concat(node.GetProperty("stable_dependencies").EnumerateArray());
                if(dependencies.Any(d=>affected.Contains(d.GetString()!)))changed|=affected.Add(id);
            }
        }while(changed);
        return affected;
    }
    internal static JsonElement ProtectedNodes(JsonElement snapshot,IReadOnlySet<string> affected,
        IEnumerable<ModelFeatureVariable>? edited=null,IReadOnlySet<string>? replaceTargetParameters=null,Action? cancel=null,bool sketchMotion=false)
    {
        var controls=(edited??[]).GroupBy(x=>x.ID.ToString(),StringComparer.OrdinalIgnoreCase).ToDictionary(g=>g.Key,g=>g.ToArray(),StringComparer.OrdinalIgnoreCase);
        var nodes=JsonNode.Parse(snapshot.GetProperty("nodes").GetRawText())!.AsArray();
        foreach(var node in nodes)
        {
            cancel?.Invoke();string id=node!["mc_id"]!.GetValue<string>();
            // Health is verified separately. Suppressed/Ok are both documented
            // healthy stored states for an intentionally suppressed node.
            if(node["suppressed"]?.GetValue<bool>()==true)node["status"]="retained_healthy_suppression";
            node["dependencies"]=node["stable_dependencies"] is {} stable?JsonNode.Parse(stable.ToJsonString()):null;
            if(replaceTargetParameters?.Contains(id)==true){node["parameters"]=null;continue;}
            if(node["parameters"] is not JsonObject p)continue;
            if(affected.Contains(id))
            {
                string? kind=p["kind"]?.GetValue<string>();
                if(kind is "fillet" or "chamfer")
                {
                    if(p["edge_ids"] is not JsonArray&&p["edge_count"] is not JsonValue)throw new DomainException("HISTORY_UNAVAILABLE","Retained edge-feature bindings are unavailable.");
                    if(p["edge_ids"] is JsonArray edges)p["edge_count"]=edges.Count;
                    p.Remove("edge_ids");
                }
                if(kind=="construction_point")p.Remove("position");
                if(kind=="construction_axis"){p.Remove("start");p.Remove("end");}
                if(kind=="sketch")p.Remove("support_offset");
                if(sketchMotion&&kind=="profile")
                {
                    // Supplemental411 snapshots retain exact profile intent,
                    // metadata, member/consumer IDs and support/source links.
                    // These are observed contours/frames regenerated by the
                    // actual affected native sketch graph.
                    p.Remove("profile_editing");NativeSketchGeometry385.Motion(p["contour_geometry"],cancel??(()=>{}));
                    void Orientation(JsonNode? geometry)
                    {
                        cancel?.Invoke();if(geometry is JsonObject obj)
                        {if(obj.ContainsKey("normal"))obj["normal"]=null;foreach(var field in obj.ToArray())Orientation(field.Value);}
                        else if(geometry is JsonArray array)foreach(var row in array)Orientation(row);
                    }
                    Orientation(p["contour_geometry"]);
                    if(p["plane"] is JsonObject plane)foreach(string field in new[]{"origin","normal","axis_u","axis_v","origin_wcs","x_step_wcs","y_step_wcs","plane_normal_wcs","normal_wcs","x_axis_wcs","y_axis_wcs"})if(plane.ContainsKey(field))plane[field]=null;
                }
                // Diameter limits are evaluated from the rebuilt cylindrical
                // source. Pitch, handedness, length and references stay stored.
                if(kind=="thread"){p.Remove("minimum_diameter");p.Remove("maximum_diameter");}
            }
            if(controls.TryGetValue(id,out var selected))foreach(var control in selected)
            {
                control.RemoveObservedValue(p);
            }
        }
        return Wire.Element(nodes);
    }
    internal static void Verify(JsonElement before,JsonElement after,IReadOnlySet<string> affected,
        IEnumerable<ModelFeatureVariable>? edited=null,Action? cancel=null,bool sketchMotion=false)
    {
        foreach(string field in new[]{"owner","body","last_item","external_sources","native_source_ids"})
            if(!ModelParameterSnapshot.Equivalent(before.GetProperty(field),after.GetProperty(field)))
                throw new DomainException("MODEL_STATE_UNAVAILABLE","Parameter rebuild changed native body identity, terminal or retained source bindings.");
        if(!ModelParameterSnapshot.Equivalent(ProtectedNodes(before,affected,edited,cancel:cancel,sketchMotion:sketchMotion),ProtectedNodes(after,affected,edited,cancel:cancel,sketchMotion:sketchMotion)))
            throw new DomainException("MODEL_STATE_UNAVAILABLE","Parameter rebuild changed stored design or an unrelated history item outside the native dependency closure.");
    }
    // Native sketch edits use the richer part snapshot. Its stored IDs, source
    // bindings, metadata, formulas and topology are retained. Only geometric
    // observations of actual downstream nodes can change after native rebuild.
    internal static JsonElement ProtectPartDerived(JsonElement snapshot,IReadOnlySet<string> affected,
        IReadOnlySet<string> stationaryRoots,Action cancel)
    {
        var root=JsonNode.Parse(snapshot.GetRawText())!.AsObject();
        root["nodes"]=JsonNode.Parse(ProtectedNodes(snapshot,affected,cancel:cancel).GetRawText());
        void Frame(JsonNode? value)
        {
            if(value is not JsonObject frame)return;
            foreach(string field in new[]{"origin_wcs","x_step_wcs","y_step_wcs","plane_normal_wcs","x_axis_wcs","y_axis_wcs","normal_wcs","origin","axis_u","axis_v","normal"})
                if(frame.ContainsKey(field))frame[field]=null;
        }
        void Geometry(JsonNode? value,bool movingFrame)
        {
            NativeSketchGeometry385.Motion(value,cancel);
            void Normals(JsonNode? node)
            {
                cancel();if(node is JsonObject obj)
                {
                    if(obj.ContainsKey("normal"))obj["normal"]=null;
                    if(obj["frame"] is {} frame)Frame(frame);
                    foreach(var field in obj.ToArray())if(field.Key!="frame")Normals(field.Value);
                }
                else if(node is JsonArray array)foreach(var row in array)Normals(row);
            }
            if(movingFrame)Normals(value);
        }
        void Bindings(JsonNode? value)
        {
            if(value is not JsonArray rows)return;
            foreach(var row in rows)
            {
                cancel();if(row?["source_kind"]?.GetValue<string>()=="native_part_edge")Geometry(row["source_geometry"],true);
                // Drawing sources and all actual native source/projection IDs
                // stay exact. The capture re-resolves each FEV with IdMatchExact.
            }
        }
        void StoredSketch(JsonObject stored,bool movingFrame)
        {
            Geometry(stored["geometry"],movingFrame);Bindings(stored["projection_bindings"]);
        }
        foreach(var sketch in root["sketches"]!.AsArray())
        {
            cancel();string id=sketch!["id"]!.GetValue<string>();if(!affected.Contains(id)||stationaryRoots.Contains(id))continue;
            Geometry(sketch["geometry"],true);if(sketch["retained"] is JsonObject retained)StoredSketch(retained,true);
        }
        foreach(var profile in root["profiles"]!.AsArray())
        {
            cancel();string id=profile!["profile_id"]!.GetValue<string>();if(!affected.Contains(id))continue;
            string sketch=profile["parent_sketch_id"]!.GetValue<string>();bool movingFrame=!stationaryRoots.Contains(sketch);
            Geometry(profile["geometry"],movingFrame);Geometry(profile["profile_geometry"],movingFrame);
            if(movingFrame){Frame(profile["native_plane"]);Bindings(profile["projection_bindings"]);}
        }
        foreach(var node in root["nodes"]!.AsArray())
        {
            cancel();string id=node!["mc_id"]!.GetValue<string>();
            if(affected.Contains(id)&&node["parameters"] is JsonObject p&&p["kind"]?.GetValue<string>()=="profile")
            {
                p.Remove("profile_editing");string? parent=p["parent_sketch_id"]?.GetValue<string>();
                bool movingFrame=parent==null||!stationaryRoots.Contains(parent);
                Geometry(p["contour_geometry"],movingFrame);if(movingFrame)Frame(p["plane"]);
            }
        }
        return Wire.Element(root);
    }
    internal static JsonElement ConstraintPoints(JsonElement observation,Action cancel)
    {
        var root=JsonNode.Parse(observation.GetRawText())!.AsObject();
        if(root["constraints"] is JsonArray rows)foreach(var row in rows)
        {
            cancel();foreach(string field in new[]{"first","second","third"})if(row?[field] is JsonObject reference)reference["point"]=null;
        }
        return Wire.Element(root);
    }
}
