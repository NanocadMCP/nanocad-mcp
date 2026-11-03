using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using Multicad;
using Multicad.DatabaseServices;
using Multicad.DatabaseServices.Constraints;
using Multicad.Mc3D;
using NanoCAD.Contracts;
using Db=Teigha.DatabaseServices;

namespace NanoCADBridge;

// Existing relation/control-variable proof, not independent geometry creation
// or a guessed alias from a GS marker. No read here enters edit mode or solves.
internal static class ModelSketchConstraintParameterState379
{
    internal const string Scope="Existing internal radius/diameter control of one complete nonprojection Circle; numeric literal only, fixed center/plane, exact current solver member/variable binding and retained ordinary extrusion/revolve/sweep profile consumers. Other geometry, constraints, formulas and drawing sources remain retained. No new relation, projected source edit or formula replacement.";
    internal sealed record Snapshot(ModelFeatureReorderState345.Snapshot Part,PlanarSketch Sketch,McObjectId Constraint,
        McObjectId Member,McObjectId Variable,string Kind,double Value,double Radius,JsonElement Circle,JsonElement VariableState,
        JsonElement Relations,JsonElement Protected,string[] Profiles,string[] Consumers,ModelSketchConstraintMetadata379.Binding[] StorageBindings,string Token);
    internal static void Need([DoesNotReturnIf(false)] bool ok,string code,string message)
    {if(!ok)throw new DomainException(code,message);}
    internal static string Key(McObjectId id)=>id.ToString();
    internal static Constraints2D Manager(PlanarSketch sketch)
    {
        var owner=sketch.DbEntity.Document;Need(owner!=null&&!owner.ID.IsNull&&!sketch.IsInEditMode,"CONSTRAINT_SCOPE_MISMATCH","Read the existing sketch outside edit mode in its actual native owner.");
        using var plane=sketch.Plane;var manager=owner.Get2dConstraintsManager(Constraint2dNetType.Auto,sketch.ID,plane);
        if(manager==null)throw new DomainException("CONSTRAINTS_UNAVAILABLE","The existing native sketch manager is unavailable.");
        if(manager.DocumentId!=owner.ID||manager.PlanarSketchId!=sketch.ID||manager.ScopeId.IsNull)
        {manager.Dispose();throw new DomainException("CONSTRAINT_SCOPE_MISMATCH","The actual manager does not identify the selected owner/sketch scope; no alternate network is used.");}
        return manager;
    }
    static bool Same(string? a,string? b)=>string.Equals(a,b,StringComparison.OrdinalIgnoreCase);
    static bool Literal(string? text,double value)=>double.TryParse(text,NumberStyles.Float,CultureInfo.InvariantCulture,out double number)&&ModelParameterSnapshot.Near(number,value);
    static JsonElement Shape(JsonElement geometry,string member)
    {
        var matches=geometry.GetProperty("elements").EnumerateArray().Where(item=>Same(item.GetProperty("element_id").GetString(),member)).ToArray();
        Need(matches.Length==1,"SKETCH_BINDING_UNAVAILABLE","The relation must reference one exact current native sketch geometry member.");
        var g=matches[0].GetProperty("geometry");
        Need(g.GetProperty("kind").GetString()=="circular_arc"&&ModelParameterSnapshot.Near(Math.Abs(g.GetProperty("end_angle").GetDouble()-g.GetProperty("start_angle").GetDouble()),2*Math.PI)&&
            double.IsFinite(g.GetProperty("radius").GetDouble())&&g.GetProperty("radius").GetDouble()>0,
            "OUT_OF_SCOPE","This existing-control adapter changes one complete Circle, not an arc, line, projected source or arbitrary solver motion.");
        var normal=geometry.GetProperty("frame").GetProperty("plane_normal_wcs");var native=g.GetProperty("normal");
        Need(ModelParameterSnapshot.Near(Math.Abs(normal[0].GetDouble()*native[0].GetDouble()+normal[1].GetDouble()*native[1].GetDouble()+normal[2].GetDouble()*native[2].GetDouble()),1),
            "PROFILE_PLANE_MISMATCH","The actual Circle is not in the retained native sketch plane.");
        // A complete Circle has no orientation-dependent endpoints. The
        // sketch/profile oriented frame controls native feature direction.
        return Wire.Element(new{kind="circle",center=g.GetProperty("center"),normal,radius=g.GetProperty("radius")});
    }
    static void Geometry(JsonObject root,string field,string member)
    {
        if(root[field] is not JsonObject geometry||geometry["elements"] is not JsonArray elements)
            throw new DomainException("SKETCH_GEOMETRY_UNAVAILABLE","A protected native sketch geometry snapshot is unavailable.");
        var found=elements.Where(item=>Same(item!["element_id"]!.GetValue<string>(),member)).ToArray();
        Need(found.Length==1,"SKETCH_BINDING_UNAVAILABLE","An exact selected geometry member disappeared from a retained sketch snapshot.");
        var g=found[0]!["geometry"]!.AsObject();
        // Full Circle reference vector/angle are a boundary representation,
        // not the design's center, plane or radius. Physical geometry is
        // separately checked before/after; no other element is normalized.
        found[0]!["geometry"]=new JsonObject{["kind"]="circle",["center"]=JsonNode.Parse(g["center"]!.ToJsonString()),["normal"]=JsonNode.Parse(geometry["frame"]!["plane_normal_wcs"]!.ToJsonString()),["radius"]=null};
    }
    static JsonElement Protected(ModelFeatureReorderState345.Snapshot part,string sketch,string member,string variable,Dictionary<string,JsonElement> profiles,Action cancel)
    {
        var root=JsonNode.Parse(part.Preserved.GetRawText())!.AsObject();
        foreach(var row in root["variables"]!.AsArray())
        {cancel();if(Same(row!["mc_id"]!.GetValue<string>(),variable)){row["value"]=null;row["expression"]=null;}}
        foreach(var row in root["sketches"]!.AsArray())
        {
            cancel();if(!Same(row!["id"]!.GetValue<string>(),sketch))continue;
            Geometry(row.AsObject(),"geometry",member);Geometry(row["retained"]!.AsObject(),"geometry",member);
        }
        foreach(var row in root["profiles"]!.AsArray())
        {
            cancel();if(!Same(row!["parent_sketch_id"]!.GetValue<string>(),sketch))continue;
            Geometry(row.AsObject(),"geometry",member);
            if(profiles.TryGetValue(row["profile_id"]!.GetValue<string>(),out var shape))row["profile_geometry"]=JsonNode.Parse(shape.GetRawText());
        }
        return Wire.Element(root);
    }
    internal static JsonElement RelationDesign(JsonElement relations,string selected)
    {
        var root=JsonNode.Parse(relations.GetRawText())!.AsObject();
        foreach(var row in root["constraints"]!.AsArray())
            if(Same(row!["id"]!.GetValue<string>(),selected)){row["parameter"]=null;row["variable_expression"]=null;}
        return Wire.Element(root);
    }
    internal static Snapshot Capture(string handle,string sketchId,string constraintId,ModifyArguments args,DocumentState state,Db.Transaction tx,Action cancel)
    {
        var drawing=ModelFeatureReorderState345.Scope(state,tx);var raw=CadStructureWriter.Find(state.Document.Database,handle);
        var entity=tx.GetObject(raw,Db.OpenMode.ForRead) as Db.Solid3d;
        Need(entity!=null&&entity.GetType()==typeof(Db.Solid3d)&&entity.OwnerId==ModelFeatureReorderState345.Model(state.Document.Database,tx)&&!entity.IsNull,
            "UNSUPPORTED_ENTITY_TYPE","Select the unique current nonempty native Model Space part representation.");
        var layer=(Db.LayerTableRecord)tx.GetObject(entity.LayerId,Db.OpenMode.ForRead);
        Need(!layer.IsLocked&&!layer.IsOff&&!layer.IsFrozen&&!layer.IsDependent,"LAYER_UNAVAILABLE","The part layer is unavailable for editing.");
        var bodyId=McObjectId.FromHandle(raw.Handle.Value,drawing.ID);var body=bodyId.GetObjectOfType<Mc3dSolid>();
        Need(body!=null&&body.ID==bodyId&&ModelHistoryEditGuard.CurrentRepresentation(body,state,tx,cancel)==raw,"OBJECT_IDENTITY_MISMATCH","The selected representation does not resolve to one current native part.");
        var part=ModelFeatureReorderState345.Capture(body,raw,args,state,tx,cancel,captureFullDesign:true);
        var matches=part.Contents.Where(id=>Same(Key(id),sketchId)).ToArray();Need(matches.Length==1,"OUT_OF_SCOPE","Select the actual current body-owned sketch ID.");
        var sketch=matches[0].GetObjectOfType<PlanarSketch>();var owner=ModelProfileDiscovery348.OwnerDocument(body,state);
        Need(sketch!=null&&sketch.ID==matches[0]&&sketch.HostSolidID==body.ID&&sketch.DbEntity.Document?.ID==owner.ID&&!sketch.IsInEditMode,
            "CONSTRAINT_SCOPE_MISMATCH","The current sketch must belong to this part's actual owner outside edit mode.");
        using var manager=Manager(sketch);var ids=manager.GetAllConstraints();Need(ids!=null,"CONSTRAINTS_UNAVAILABLE","The current relation inventory is unavailable; no empty-network claim is made.");ModelInventoryBudget.Ids(ids,cancel);
        var selected=ids.Where(id=>Same(Key(id),constraintId)).ToArray();Need(selected.Length==1,"CONSTRAINT_SCOPE_MISMATCH","The selected dimensional relation is not a unique member of this current sketch network.");
        using var info=manager.GetConstraintInfo(selected[0]);Need(info!=null&&info.IsDimensional&&info.Type is ConstraintType.DimRadial or ConstraintType.DimDiametrical,
            "OUT_OF_SCOPE","Select an existing internal DimRadial or DimDiametrical relation; no driving mode is inferred from AddConstraint's fApply boolean.");
        var first=info.FirstObject;var second=info.SecondObject;var third=info.ThirdObject;var member=first?.ObjectId??McObjectId.Null;
        Need(!member.IsNull&&(second==null||second.ObjectId.IsNull)&&(third==null||third.ObjectId.IsNull),
            "OUT_OF_SCOPE","This Circle control must reference one exact existing native member.");
        var members=sketch.GetContainingObjects(true,false,false);var projections=sketch.GetContainingObjects(false,true,false);
        Need(members!=null&&projections!=null&&members.Contains(member)&&!projections.Contains(member),"SKETCH_BINDING_UNAVAILABLE","The actual relation member must be native nonprojection geometry; source-controlled projected curves use their drawing dimension route.");
        ModelInventoryBudget.Ids(members,cancel);ModelInventoryBudget.Ids(projections,cancel);
        // Abstract solver/geometry GUIDs do not need resident DWG storage.
        // When an exact current drawing handle roundtrip actually exists,
        // however, a top-level drawing curve is external retained source data,
        // not an independently owned internal Circle to change silently.
        if(member.Handle!=0&&state.Document.Database.TryGetObjectId(new Db.Handle(member.Handle),out var resident)&&
            !resident.IsNull&&!resident.IsErased&&!resident.IsEffectivelyErased&&McObjectId.FromHandle(resident.Handle.Value,drawing.ID)==member)
            Need(tx.GetObject(resident,Db.OpenMode.ForRead) is not Db.Entity residentEntity||residentEntity.OwnerId!=ModelFeatureReorderState345.Model(state.Document.Database,tx),
                "OUT_OF_SCOPE","The exact relation member is a resident drawing source; use its source drawing dimension route instead of the internal Circle adapter.");
        var geometry=Wire.Element(SketchGeometryReader.Read(sketch,cancel));var circle=Shape(geometry,Key(member));
        foreach(var peer in geometry.GetProperty("elements").EnumerateArray())
        {
            cancel();if(Same(peer.GetProperty("element_id").GetString(),Key(member)))continue;var g=peer.GetProperty("geometry");
            if(g.GetProperty("kind").GetString()!="circular_arc")continue;
            var carrier=Wire.Element(new{kind="circle",center=g.GetProperty("center"),normal=circle.GetProperty("normal"),radius=g.GetProperty("radius")});
            Need(!ModelFeatureReorderState345.Equivalent(circle,carrier,cancel),"PROFILE_BINDING_AMBIGUOUS","Another native Circle/Arc has the same physical carrier; profile coverage alone cannot prove which retained member it consumes.");
        }
        var relations=part.Constraints[Key(sketch.ID)];Need(ModelSketchConstraintObservation353.Complete(relations),"CONSTRAINTS_UNAVAILABLE","Read the complete actual existing relation inventory before changing an internal control value.");
        var bindings=ModelSketchGeometryBindingsReader365.Read(sketch,owner,relations,cancel);
        Need(bindings.geometry_members_complete&&bindings.solver_mapping_complete&&bindings.solver_members!=null&&
            bindings.solver_members.Any(row=>Same(row.native_object_id,Key(member))&&row.constraint_ids.Contains(Key(selected[0]),StringComparer.OrdinalIgnoreCase)&&Same(row.current_geometry_id,Key(member))),
            "SKETCH_BINDING_UNAVAILABLE","The actual solver member/reverse relation map does not bind this current geometry ID; no coincident-geometry alias is guessed.");
        var variable=manager.GetConstraintParamId(selected[0]);double value=manager.GetConstraintParamValue(selected[0]);
        Need(!variable.IsNull&&variable==info.VarId&&double.IsFinite(value)&&value>0&&ModelParameterSnapshot.Near(info.Param,value),
            "CONSTRAINT_PARAMETER_UNAVAILABLE","The existing relation has no consistent finite actual control/VarId/GetConstraintParamId binding.");
        Need(relations.GetProperty("constraints").EnumerateArray().All(peer=>Same(peer.GetProperty("id").GetString(),Key(selected[0]))||!Same(peer.GetProperty("variable_id").GetString(),Key(variable))),
            "PARAMETER_HAS_SHARED_RELATIONS","Another current sketch relation uses this same native variable; this single Circle control edit does not change a shared driver implicitly.");
        var variables=part.Preserved.GetProperty("variables").EnumerateArray().Where(row=>Same(row.GetProperty("mc_id").GetString(),Key(variable))).ToArray();
        Need(variables.Length==1,"CONSTRAINT_PARAMETER_UNAVAILABLE","The exact relation parameter is absent from its current MCS variable owner inventory.");var row=variables[0];
        Need(row.GetProperty("value_available").GetBoolean()&&ModelParameterSnapshot.Near(row.GetProperty("value").GetDouble(),value),
            "CONSTRAINT_PARAMETER_UNAVAILABLE","The current control and its actual native variable value differ; no unit conversion is guessed.");
        Need(Literal(row.GetProperty("expression").GetString(),value)&&(string.IsNullOrWhiteSpace(info.VarExpression)||Literal(info.VarExpression,value)),
            "PARAMETER_HAS_EXPRESSION","This native control-value route retains other formulas and edits an existing numeric literal only; it does not replace a formula.");
        var downstream=row.GetProperty("downstream_variables");var objects=row.GetProperty("downstream_objects");
        Need(downstream.ValueKind==JsonValueKind.Array&&downstream.GetArrayLength()==0&&objects.ValueKind==JsonValueKind.Array,
            "DEPENDENCY_INVENTORY_UNAVAILABLE","The selected control must have a complete no-dependent-variable inventory; linked formulas require their own propagation scope.");
        var allowed=part.Contents.Select(Key).Append(Key(body.ID)).Append(Key(selected[0])).ToHashSet(StringComparer.OrdinalIgnoreCase);
        if(!info.DimId.IsNull)allowed.Add(Key(info.DimId));
        Need(objects.EnumerateArray().All(id=>allowed.Contains(id.GetString()!)),"DEPENDENCY_OWNERSHIP_MISMATCH","The existing internal parameter has an object consumer outside this retained part/actual dimensional relation.");
        string? host=row.GetProperty("host_object_id").GetString();Need(host==null||allowed.Contains(host),"CONSTRAINT_SCOPE_MISMATCH","The selected relation parameter has a foreign owning object.");
        string kind=info.Type==ConstraintType.DimRadial?"radius":"diameter";double radius=circle.GetProperty("radius").GetDouble();
        Need(ModelParameterSnapshot.Near(value,kind=="radius"?radius:2*radius),"CONSTRAINT_PARAMETER_UNAVAILABLE","Actual full Circle radius and current dimensional control do not agree in documented millimeters.");
        var profileIds=new List<string>();var featureIds=new List<string>();var profileShapes=new Dictionary<string,JsonElement>(StringComparer.OrdinalIgnoreCase);
        var plane=ModelProfileAreas377.Plane(geometry.GetProperty("frame"));
        Point3 P(JsonElement p)=>new(p[0].GetDouble(),p[1].GetDouble(),p[2].GetDouble());
        var circles=geometry.GetProperty("elements").EnumerateArray().Where(item=>item.GetProperty("geometry").GetProperty("kind").GetString()=="circular_arc"&&
            ModelParameterSnapshot.Near(Math.Abs(item.GetProperty("geometry").GetProperty("end_angle").GetDouble()-item.GetProperty("geometry").GetProperty("start_angle").GetDouble()),2*Math.PI))
            .Select(item=>(Id:item.GetProperty("element_id").GetString()!,Shape:Shape(geometry,item.GetProperty("element_id").GetString()!))).ToArray();
        foreach(var id in ModelProfileDiscovery348.Ids<SketchProfile>(owner,cancel))
        {
            cancel();var profile=id.GetObjectOfType<SketchProfile>();Need(profile!=null&&profile.ID==id,"PROFILE_DISCOVERY_UNAVAILABLE","A current native profile inventory entry is unavailable.");
            if(profile.ParentSketchID!=sketch.ID)continue;
            Need(part.Contents.Contains(id)&&profile.HostSolidID==body.ID&&profile.DbEntity.Document?.ID==owner.ID,
                "DEPENDENCY_OWNERSHIP_MISMATCH","A peer profile of the selected internal sketch lies outside this part's retained composition.");
            var observed=ModelProfileLifecycleState340.Capture(profile,body,state,tx,cancel,documentScope:owner,allowCircularHoles:true);
            if(observed.ProfileGeometry.TryGetProperty("areas",out var areas))
            {
                bool uses=areas.EnumerateArray().Any(area=>Same(area.GetProperty("outer_element_id").GetString(),Key(member))||area.GetProperty("hole_element_ids").EnumerateArray().Any(id=>Same(id.GetString(),Key(member))));
                Need(!uses,"OUT_OF_SCOPE","An internal Circle used by a compound outer/hole profile needs its dedicated control-motion adapter; unaffected compound peers remain retained.");continue;
            }
            var boundary=ModelProfileLifecycleState340.Boundary(observed.ProfileGeometry,observed.Plane,cancel);
            var matchesCircle=circles.Where(candidate=>ModelProfileElementGeometry358.SameBoundary(boundary,
                [new SketchConstraintCurve(candidate.Id,"circle",Center:DrawingWorkplane.Local(plane,P(candidate.Shape.GetProperty("center"))),Radius:candidate.Shape.GetProperty("radius").GetDouble())],cancel)).ToArray();
            Need(matchesCircle.Length<=1,"PROFILE_BINDING_AMBIGUOUS","The actual native profile Circle boundary matches multiple current geometry members; no shape alias is selected.");
            if(matchesCircle.Length==1)
            {
                var shape=JsonNode.Parse(matchesCircle[0].Shape.GetRawText())!.AsObject();shape["member_id"]=matchesCircle[0].Id;
                if(Same(matchesCircle[0].Id,Key(member)))shape["radius"]=null;
                profileShapes.Add(Key(id),Wire.Element(shape));
            }
            if(matchesCircle.Length==0||!Same(matchesCircle[0].Id,Key(member)))continue;
            var consumer=profile.Dependent3dFeatureID;HistoryItem? forming=consumer.GetObjectOfType<ExtrudeFeature>()??
                (HistoryItem?)consumer.GetObjectOfType<RevolveFeature>()??consumer.GetObjectOfType<ExtrudeAlongPathFeature>();
            Need(!consumer.IsNull&&forming!=null&&forming.ID==consumer&&part.Contents.Contains(consumer)&&forming.HostSolidID==body.ID&&forming.DbEntity.Document?.ID==owner.ID&&
                (consumer.GetObjectOfType<ExtrudeFeature>()?.ProfileID??consumer.GetObjectOfType<RevolveFeature>()?.ProfileID??consumer.GetObjectOfType<ExtrudeAlongPathFeature>()?.ProfileID)==profile.ID,
                "PROFILE_OWNERSHIP_MISMATCH","The selected Circle profile must retain one actual typed extrusion/revolve/sweep consumer in this part.");
            profileIds.Add(Key(id));featureIds.Add(Key(consumer));
        }
        Need(profileIds.Count>0&&featureIds.Distinct(StringComparer.OrdinalIgnoreCase).Count()==featureIds.Count,"PROFILE_OWNERSHIP_MISMATCH","The existing internal Circle requires a selected native profile and unique typed forming consumer; no unconsumed or whole-sketch substitute is used.");
        var affected=profileIds.ToHashSet(StringComparer.OrdinalIgnoreCase);var forwards=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        void Consumer(HistoryItem? item,McObjectId profile)
        {
            if(profile.IsNull||!affected.Contains(Key(profile)))return;
            Need(item!=null&&part.Contents.Contains(item.ID)&&item.HostSolidID==body.ID&&item.DbEntity.Document?.ID==owner.ID&&featureIds.Contains(Key(item.ID),StringComparer.OrdinalIgnoreCase),
                "PROFILE_OWNERSHIP_MISMATCH","A selected Circle profile has another actual native forming consumer outside its retained typed owner.");forwards.Add(Key(item.ID));
        }
        foreach(var id in ModelProfileDiscovery348.Ids<ExtrudeFeature>(owner,cancel)){cancel();var item=id.GetObjectOfType<ExtrudeFeature>();Need(item!=null,"PROFILE_DISCOVERY_UNAVAILABLE","A typed extrusion inventory entry is unavailable.");Consumer(item,item.ProfileID);}
        foreach(var id in ModelProfileDiscovery348.Ids<RevolveFeature>(owner,cancel)){cancel();var item=id.GetObjectOfType<RevolveFeature>();Need(item!=null,"PROFILE_DISCOVERY_UNAVAILABLE","A typed revolution inventory entry is unavailable.");Consumer(item,item.ProfileID);}
        foreach(var id in ModelProfileDiscovery348.Ids<ExtrudeAlongPathFeature>(owner,cancel)){cancel();var item=id.GetObjectOfType<ExtrudeAlongPathFeature>();Need(item!=null,"PROFILE_DISCOVERY_UNAVAILABLE","A typed sweep inventory entry is unavailable.");Consumer(item,item.ProfileID);}
        foreach(var id in ModelProfileDiscovery348.Ids<LoftFeature>(owner,cancel))
        {cancel();var item=id.GetObjectOfType<LoftFeature>();Need(item!=null,"PROFILE_DISCOVERY_UNAVAILABLE","A typed loft inventory entry is unavailable.");Consumer(item,item.ProfileID);for(int i=0;i<item.SectionsCount;i++){cancel();var section=item.GetSection(i);Need(section!=null,"PROFILE_DISCOVERY_UNAVAILABLE","A native loft section inventory is unavailable.");Consumer(item,section.ID);}}
        Need(forwards.SetEquals(featureIds),"PROFILE_OWNERSHIP_MISMATCH","Actual forward forming references and selected profile backlinks disagree.");
        var known=part.Contents.Append(body.ID).ToHashSet();
        foreach(bool transitive in new[]{false,true})
        {
            var dependents=transitive?sketch.GetAllDependent(false):sketch.GetDependent();Need(dependents!=null,"DEPENDENCY_INVENTORY_UNAVAILABLE","The actual sketch downstream inventory is unavailable; no empty-consumer claim is made.");ModelInventoryBudget.Ids(dependents,cancel);
            Need(dependents.All(id=>known.Contains(id)||ModelViewFollowers.OwnsDependent(part.Views,id)),"DEPENDENCY_OWNERSHIP_MISMATCH","The selected internal sketch has an unscoped downstream owner.");
        }
        var protectedState=Protected(part,Key(sketch.ID),Key(member),Key(variable),profileShapes,cancel);
        var storage=ModelSketchConstraintMetadata379.Bind(profileIds.Concat(featureIds).Select(identity=>part.Contents.Single(id=>Same(Key(id),identity)))
            .Concat([body.ID,sketch.ID,selected[0],variable]),part.Representation,owner,drawing,state,tx,cancel);
        var token=Wire.Element(new{part=part.Preserved,part.Properties,part.BodyMetadata,order=part.Features,previous=part.Previous,relations,member=Key(member),constraint=Key(selected[0]),variable=Key(variable),value,circle,storage,
            profiles=profileIds.OrderBy(id=>id,StringComparer.OrdinalIgnoreCase).ToArray(),consumers=featureIds.OrderBy(id=>id,StringComparer.OrdinalIgnoreCase).ToArray()});new ModelInventoryBudget(cancel).Add(token);
        return new(part,sketch,selected[0],member,variable,kind,value,radius,circle,row,relations,protectedState,
            profileIds.OrderBy(id=>id,StringComparer.OrdinalIgnoreCase).ToArray(),featureIds.OrderBy(id=>id,StringComparer.OrdinalIgnoreCase).ToArray(),storage,Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(token,Wire.Json))));
    }
}
