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

internal static class NativeSketchConstraintState385
{
    internal const string Scope="Native owned Line/Circle/Arc/Spline/Ellipse and actual SDK polyline-segment relations with exact same-sketch source projections as immutable references; persistent dimensional numeric controls in millimeters or radians. Explicit solve/rebuild may move connected owned geometry. Native construction anchors, source geometry, relation intent/formulas, profile/feature IDs and consumer links remain retained; initial value alone is not proof of reactive driving and no globally constrained-state claim is made.";
    internal sealed record Snapshot(ModelFeatureReorderState345.Snapshot Part,ModelSketchLifecycleReader362.Snapshot Sketch,NativeSketchMembers385.Snapshot Inputs,
        JsonElement Relations,JsonElement Protected,ModelSketchConstraintMetadata379.Binding[] Storage,string Token,McObjectId[] GraphRoots,HashSet<string> Affected,
        NativeBodyTransparency445.Witness? BodyTransparency=null);
    internal sealed record Control(Snapshot State,McObjectId Constraint,McObjectId Variable,string Kind,double Value,string Expression,McObjectId Member,JsonElement VariableState,
        McObjectId ParameterVariable,double? ParameterValue,double? ParameterRawValue,double RelationValue,string? RelationExpression);
    static void Need([System.Diagnostics.CodeAnalysis.DoesNotReturnIf(false)] bool ok,string code,string message)=>NativeSketchMembers385.Need(ok,code,message);
    internal static string Key(McObjectId id)=>id.ToString();
    internal static bool Same(string? a,string? b)=>string.Equals(a,b,StringComparison.OrdinalIgnoreCase);
    internal static JsonElement Relations(Constraints2D manager,Action cancel,Action<McObjectId,ConstraintInfo>? observed=null,IEnumerable<McObjectId>? inventory=null)
    {
        var ids=inventory??manager.GetAllConstraints();Need(ids!=null,"CONSTRAINTS_UNAVAILABLE","The actual constraint inventory is unavailable, not observed empty.");ModelInventoryBudget.Ids(ids,cancel);var rows=new List<object>();var budget=new ModelInventoryBudget(cancel);
        object? Ref(IConstraintObjectInfo? r)
        {if(r==null||r.ObjectId.IsNull)return null;var p=r.PointRefOnObject;return new{object_id=Key(r.ObjectId),subentity_id=r.SubentId.IsNull?null:r.SubentId.ToString(),subentity_type=r.SubentId.IsNull?null:ConstraintInfo.GetSubentSubType(r.SubentId).ToString(),segment=r.PolylineSegmentIndex,point=double.IsFinite(p.X)&&double.IsFinite(p.Y)&&double.IsFinite(p.Z)?new[]{p.X,p.Y,p.Z}:null,point_coordinate_basis="native_reference_frame_unspecified"};}
        foreach(var id in ids.OrderBy(Key,StringComparer.OrdinalIgnoreCase))
        {cancel();using var info=manager.GetConstraintInfo(id);Need(info!=null,"CONSTRAINTS_UNAVAILABLE","An actual existing relation is unavailable.");observed?.Invoke(id,info);var row=new{id=Key(id),type=info.Type.ToString(),geometrical=info.IsGeometrical,dimensional=info.IsDimensional,first=Ref(info.FirstObject),second=Ref(info.SecondObject),third=Ref(info.ThirdObject),variable=info.VarId.IsNull?null:Key(info.VarId),dimension=info.DimId.IsNull?null:Key(info.DimId),name=info.VarName,expression=info.VarExpression,value=double.IsFinite(info.Param)?(double?)info.Param:null};budget.Add(row);rows.Add(row);}
        return Wire.Element(new{document_id=Key(manager.DocumentId),sketch_id=Key(manager.PlanarSketchId),scope_id=Key(manager.ScopeId),network=manager.ConstraintsNetworkType.ToString(),constraints=rows});
    }
    internal static Snapshot Capture(string handle,string sketchId,ModifyArguments args,DocumentState state,Db.Transaction tx,Action cancel)
    {
        Need(args.Units=="Millimeters"&&(int)state.Document.Database.Insunits==4,"OUT_OF_SCOPE","Use Millimeters Model Space.");var drawing=ModelFeatureReorderState345.Scope(state,tx);var raw=CadStructureWriter.Find(state.Document.Database,handle);
        var entity=tx.GetObject(raw,Db.OpenMode.ForRead) as Db.Solid3d;Need(entity!=null&&entity.GetType()==typeof(Db.Solid3d)&&entity.OwnerId==ModelFeatureReorderState345.Model(state.Document.Database,tx)&&!entity.IsNull,"UNSUPPORTED_ENTITY_TYPE","Select the actual native part representation.");
        var layer=(Db.LayerTableRecord)tx.GetObject(entity.LayerId,Db.OpenMode.ForRead);Need(!layer.IsLocked&&!layer.IsOff&&!layer.IsFrozen&&!layer.IsDependent,"LAYER_UNAVAILABLE","The part layer is unavailable.");
        var id=McObjectId.FromHandle(raw.Handle.Value,drawing.ID);var body=id.GetObjectOfType<Mc3dSolid>();Need(body!=null&&body.ID==id&&ModelHistoryEditGuard.CurrentRepresentation(body,state,tx,cancel)==raw,"OBJECT_IDENTITY_MISMATCH","The current body identity is unavailable.");
        var part=ModelFeatureReorderState345.Capture(body,raw,args,state,tx,cancel,captureFullDesign:true);var owner=ModelProfileDiscovery348.OwnerDocument(body,state);var sketch=ModelSketchLifecycleReader362.Capture(body,ModelSketchLifecycleReader362.Resolve(sketchId,owner,cancel),state,tx,cancel);
        var preserved=JsonNode.Parse(part.Preserved.GetRawText())!.AsObject();
        preserved["nodes"]=JsonNode.Parse(ModelDependencyGraph410.Nodes(part.Preserved.GetProperty("nodes"),part.Contents,cancel).GetRawText());
        part=part with{Preserved=Wire.Element(preserved)};
        using var manager=NativeSketchMembers385.Manager(sketch.Sketch);
        var inventory=manager.GetAllConstraints()??throw new DomainException("CONSTRAINTS_UNAVAILABLE","The actual constraint inventory is unavailable, not observed empty.");
        var inputs=NativeSketchMembers385.Capture(sketch.Sketch,state,tx,cancel,manager,inventory);
        Need(inputs.Members.Any(m=>m.Owned)&&inputs.Members.All(m=>m.Owned||m.ReferenceOnly),"OUT_OF_SCOPE","Native solver motion requires owned internal inputs; same-sketch projections can participate only as immutable references.");
        // A line control can drive another sketch in the same captured part.
        // This is only a read-only body-property witness; ownership admission
        // for the actual editor mutation remains in HasOwnedSpline.
        bool observedSpline=inputs.Members.Any(member=>member.Owned&&member.NativeType=="kSpline")||
            part.Preserved.GetProperty("sketches").EnumerateArray().Any(row=>row.TryGetProperty("geometry",out var geometry)&&geometry.ValueKind==JsonValueKind.Object&&
                geometry.TryGetProperty("elements",out var elements)&&elements.ValueKind==JsonValueKind.Array&&
                elements.EnumerateArray().Any(element=>element.TryGetProperty("geometry_type",out var type)&&type.GetString()=="kSpline"));
        var bodyTransparency=observedSpline
            ?NativeBodyTransparency445.Capture(body,state,tx,cancel):null;
        // Cover all actual observed geometry by the containing native inputs;
        // this is physical inventory proof, not an anonymous GUID alias.
        Need(ModelProfileElementGeometry358.SameBoundary(ModelSketchLifecycleReader362.Curves(sketch,cancel),inputs.Members.Select(m=>m.Curve).ToArray(),cancel),"SKETCH_BINDING_UNAVAILABLE","The current native input geometry does not cover the complete observed sketch boundary.");
        var relations=NativeSketchRelations410.Capture(manager,inputs,cancel,inventory).Relations;
        var peers=sketch.Profiles.EnumerateArray().Select(row=>ModelProfileDiscovery348.Resolve(row.GetProperty("profile_id").GetString()!,owner,cancel).ID).ToArray();
        // A document-construction sketch can feed an owned profile without
        // itself belonging to the part. Roots use only actual body membership.
        var graphRoots=peers.Append(sketch.Sketch.ID).Where(part.Contents.Contains).Distinct().ToArray();
        var affected=ModelDependencyGraph410.OwnedClosure(body,graphRoots,cancel,part.Views);
        var protectedDesign=Protected(part,sketch,inputs,[],null,cancel,affected:affected);
        var storage=ModelSketchConstraintMetadata379.Bind(part.Contents.Concat(inputs.Members.Select(m=>m.ObjectId)).Concat(inputs.Helpers).Concat(peers).Append(body.ID).Append(sketch.Sketch.ID),raw,owner,drawing,state,tx,cancel);
        var token=Wire.Element(new{part.Preserved,part.Properties,part.BodyMetadata,part.Features,part.Previous,sketch=sketch.Stored,inputs=inputs.Stored,relations,storage,graph_roots=graphRoots.Select(Key),affected=affected.OrderBy(id=>id,StringComparer.OrdinalIgnoreCase)});new ModelInventoryBudget(cancel).Add(token);
        return new(part,sketch,inputs,relations,protectedDesign,storage,Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(token,Wire.Json))),graphRoots,affected,bodyTransparency);
    }
    static void Shape(JsonNode? value,Action cancel)
    {if(value!=null)NativeSketchGeometry385.Motion(value,cancel);}
    internal static JsonElement Protected(ModelFeatureReorderState345.Snapshot part,ModelSketchLifecycleReader362.Snapshot sketch,NativeSketchMembers385.Snapshot inputs,string[] newVariables,string? selectedVariable,Action cancel,string[]? newObjects=null,IReadOnlySet<string>? affected=null,IReadOnlySet<string>? stationaryRoots=null)
    {
        string selected=Key(sketch.Sketch.ID);var profiles=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var stationary=stationaryRoots??new HashSet<string>(StringComparer.OrdinalIgnoreCase){selected};
        var protectedPart=affected==null?part.Preserved:ModelDependencyGraph410.ProtectPartDerived(part.Preserved,affected,stationary,cancel);
        var root=JsonNode.Parse(protectedPart.GetRawText())!.AsObject();
        var added=(newObjects??[]).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach(string name in new[]{"nodes","construction"})if(root[name] is JsonArray rows)
            for(int i=rows.Count-1;i>=0;i--){cancel();string? id=rows[i]?[name=="nodes"?"mc_id":"id"]?.GetValue<string>();if(id!=null&&added.Contains(id))rows.RemoveAt(i);}
        void NewReferences(JsonNode? value)
        {
            if(value is JsonObject obj)foreach(string key in new[]{"containing_ids","dependencies","stable_dependencies","sketch_dependencies"})if(obj[key] is JsonArray ids)for(int i=ids.Count-1;i>=0;i--){cancel();if(ids[i] is JsonValue id&&id.TryGetValue<string>(out var text)&&added.Contains(text))ids.RemoveAt(i);}
        }
        foreach(var profile in root["profiles"]!.AsArray())
        {
            cancel();if(!Same(profile!["parent_sketch_id"]!.GetValue<string>(),selected))continue;profiles.Add(profile["profile_id"]!.GetValue<string>());
            Shape(profile["geometry"],cancel);Shape(profile["profile_geometry"],cancel);profile.AsObject().Remove("constraint_observation");
        }
        foreach(var row in root["sketches"]!.AsArray())
        {cancel();if(!Same(row!["id"]!.GetValue<string>(),selected))continue;Shape(row["geometry"],cancel);if(row["retained"] is JsonObject retained)Shape(retained["geometry"],cancel);}
        foreach(var node in root["nodes"]!.AsArray())
        {cancel();if(node!["parameters"] is JsonObject p&&profiles.Contains(node["mc_id"]!.GetValue<string>()))p.Remove("profile_editing");}
        var variables=root["variables"]!.AsArray();for(int i=variables.Count-1;i>=0;i--)
        {string id=variables[i]!["mc_id"]!.GetValue<string>();if(newVariables.Contains(id,StringComparer.OrdinalIgnoreCase)){variables.RemoveAt(i);continue;}if(Same(id,selectedVariable)){variables[i]!["value"]=null;variables[i]!["expression"]=null;}}
        var own=JsonNode.Parse(sketch.Stored.GetRawText())!.AsObject();own.Remove("constraint_observation");Shape(own["geometry"],cancel);
        // Document-construction peers can be outside body composition. Retain
        // their real headers, properties, metadata, ownership and selected-loop
        // topology while allowing the same intended solver motion.
        foreach(var row in own["profiles"]!.AsArray())
        {cancel();var profile=row!["state"]!.AsObject();Shape(profile["geometry"],cancel);Shape(profile["selected_profile_geometry"],cancel);profile.Remove("constraints");}
        var members=JsonNode.Parse(inputs.Stored.GetRawText())!.AsObject();
        foreach(var member in members["members"]!.AsArray())
        {cancel();string id=member!["member_id"]!.GetValue<string>();if(inputs.Members.Any(m=>Key(m.Id)==id&&m.Owned))Shape(member,cancel);}
        foreach(string name in new[]{"objects","construction","constraint_helpers"})if(members[name] is JsonArray stored)
            for(int index=stored.Count-1;index>=0;index--){cancel();if(stored[index]?["id"] is JsonValue id&&id.TryGetValue<string>(out var text)&&added.Contains(text))stored.RemoveAt(index);}
        foreach(var node in root["nodes"]!.AsArray())NewReferences(node);
        foreach(var profile in root["profiles"]!.AsArray())NewReferences(profile);
        foreach(var row in root["sketches"]!.AsArray()){NewReferences(row);NewReferences(row?["retained"]);}
        NewReferences(own);foreach(var profile in own["profiles"]!.AsArray())NewReferences(profile?["state"]);
        return Wire.Element(new{part=root,selected_sketch=own,native_inputs=members});
    }
    internal static JsonElement Intent(JsonElement relations,string? selected=null,string[]? additions=null,NativeSketchMembers385.Snapshot? inputs=null)
    {
        var root=JsonNode.Parse(relations.GetRawText())!.AsObject();var rows=root["constraints"]!.AsArray();
        for(int i=rows.Count-1;i>=0;i--)
        {
            var row=rows[i]!.AsObject();string id=row["id"]!.GetValue<string>();if(additions?.Contains(id,StringComparer.OrdinalIgnoreCase)==true){rows.RemoveAt(i);continue;}
            if(inputs!=null)NativeSketchRelations410.CanonicalSegments(row,inputs);
            if(Same(id,selected)){row["expression"]=null;row["value"]=null;}
            // Endpoint/center reference coordinates can follow intended solver
            // motion. Object/subentity/segment binding remains exact. Fix is
            // checked separately against the original actual anchor position.
            foreach(string name in new[]{"first","second","third"})if(row[name] is JsonObject reference)reference["point"]=null;
        }
        return Wire.Element(root);
    }
    internal static Control ControlOf(Snapshot state,string constraintId,Action cancel)
    {
        using var manager=NativeSketchMembers385.Manager(state.Sketch.Sketch);var ids=manager.GetAllConstraints();Need(ids!=null,"CONSTRAINTS_UNAVAILABLE","The existing relation inventory is unavailable.");var matches=ids.Where(id=>Same(Key(id),constraintId)).ToArray();Need(matches.Length==1,"CONSTRAINT_SCOPE_MISMATCH","Read one exact current dimensional relation ID.");
        var id=matches[0];using var info=manager.GetConstraintInfo(id);Need(info!=null&&info.IsDimensional,"OUT_OF_SCOPE","The selected relation is not dimensional.");
        var binding=NativeSketchDimensionBinding411.Prove(manager,id,info,state.Inputs,cancel);string kind=binding.Kind;var references=binding.References;
        var parameterVariable=manager.GetConstraintParamId(id);double parameterValue=manager.GetConstraintParamValue(id);var variable=info.VarId;double value=info.Param;
        if(variable.IsNull||!parameterVariable.IsNull&&parameterVariable!=variable||!double.IsFinite(value)||value<=0)
            throw new DomainException("CONSTRAINT_PARAMETER_UNAVAILABLE","The actual relation variable/value is unavailable or a nonnull parameter getter binds another variable.",false,new{constraint_id=Key(id),relation_variable_id=variable.IsNull?null:Key(variable),parameter_variable_id=parameterVariable.IsNull?null:Key(parameterVariable),relation_value=double.IsFinite(value)?(double?)value:null,parameter_value=double.IsFinite(parameterValue)?(double?)parameterValue:null});
        Need(state.Relations.GetProperty("constraints").EnumerateArray().All(row=>Same(row.GetProperty("id").GetString(),constraintId)||!Same(row.GetProperty("variable").GetString(),Key(variable))),"PARAMETER_HAS_SHARED_RELATIONS","Another current relation shares this control variable.");
        var rows=state.Part.Preserved.GetProperty("variables").EnumerateArray().Where(row=>Same(row.GetProperty("mc_id").GetString(),Key(variable))).ToArray();Need(rows.Length==1,"CONSTRAINT_PARAMETER_UNAVAILABLE","The actual parameter variable is unavailable in the current owner inventory.");var row=rows[0];string? expression=row.GetProperty("expression").GetString();
        bool Literal(string? text,double expected)=>double.TryParse(text,NumberStyles.Float,CultureInfo.InvariantCulture,out double number)&&ModelParameterSnapshot.Near(number,expected);
        Need(row.GetProperty("value_available").GetBoolean()&&row.GetProperty("value").ValueKind==JsonValueKind.Number&&double.IsFinite(row.GetProperty("value").GetDouble())&&row.GetProperty("value").GetDouble()>0,"CONSTRAINT_PARAMETER_UNAVAILABLE","The exact owning variable has no positive finite current value.");
        double variableValue=row.GetProperty("value").GetDouble();
        Need(Literal(expression,variableValue)&&(string.IsNullOrWhiteSpace(info.VarExpression)||double.TryParse(info.VarExpression,NumberStyles.Float,CultureInfo.InvariantCulture,out double descriptorLiteral)&&double.IsFinite(descriptorLiteral)),"PARAMETER_HAS_EXPRESSION","This route changes an existing numeric literal control; actual variable and relation formulas are not replaced.");
        Need(kind!="angle"||ModelParameterSnapshot.Near(variableValue,value),"CONSTRAINT_PARAMETER_UNAVAILABLE","An angular variable/relation mismatch has no verified unit conversion and cannot use numeric reconciliation.");
        // ConstraintInfo.VarId is the documented parametric variable identity.
        // A missing convenience GetConstraintParamId is not an invented ID:
        // prove the exact real variable independently in the owner MCS network.
        // Available contradictory parameter values stay visible as observations
        // of the partial state and must be reconciled before a write can commit.
        var owner=state.Part.Body.DbEntity.Document;using(var variables=owner.GetDocumentVariablesManager(Constraint2dNetType.MCS))
        {
            Need(variables!=null&&variables.DocumentId==owner.ID&&variables.ConstraintsNetworkType==Constraint2dNetType.MCS,"CONSTRAINT_SCOPE_MISMATCH","The exact owner MCS variable manager is unavailable.");
            var inventory=variables.GetAllVariables();Need(inventory!=null&&inventory.Count(actual=>actual==variable)==1,"CONSTRAINT_PARAMETER_UNAVAILABLE","The actual relation variable is not unique in its owning MCS inventory.");ModelInventoryBudget.Ids(inventory,cancel);
            var actual=variables.GetVariableInfo(variable);var host=variables.GetVariableHostObjectId(variable);
            Need(actual!=null&&actual.Name==row.GetProperty("name").GetString()&&actual.Type.ToString()==row.GetProperty("type").GetString()&&actual.Expression==expression&&variables.GetVariableValue(variable,out double scalar)&&ModelParameterSnapshot.Near(scalar,variableValue)&&Same(host.IsNull?null:Key(host),row.GetProperty("host_object_id").GetString()),"CONSTRAINT_PARAMETER_UNAVAILABLE","The exact owning variable metadata/value/host differs from its current literal snapshot.");
        }
        Need(row.GetProperty("downstream_variables").GetArrayLength()==0,"DEPENDENCY_INVENTORY_UNAVAILABLE","A linked variable formula requires its own propagation scope.");
        var allowed=state.Part.Contents.Append(state.Part.Body.ID).Append(state.Sketch.Sketch.ID).Concat(state.Inputs.Members.Select(m=>m.ObjectId)).Append(id).Append(info.DimId).Append(manager.ScopeId).Select(Key).ToHashSet(StringComparer.OrdinalIgnoreCase);
        allowed.UnionWith(state.Sketch.Profiles.EnumerateArray().Select(profile=>profile.GetProperty("profile_id").GetString()!));
        Need(row.GetProperty("downstream_objects").EnumerateArray().All(v=>allowed.Contains(v.GetString()!))&& (row.GetProperty("host_object_id").ValueKind==JsonValueKind.Null||allowed.Contains(row.GetProperty("host_object_id").GetString()!)),"DEPENDENCY_OWNERSHIP_MISMATCH","The selected control has an object/owner outside the current retained part/sketch.");
        double? rawValue=double.IsFinite(parameterValue)?parameterValue:null;
        return new(state,id,variable,kind,variableValue,expression!,references[0]!.ObjectId,row,parameterVariable,parameterVariable==variable?rawValue:null,rawValue,value,info.VarExpression);
    }
    internal static void FixedAnchors(Snapshot before,Snapshot after,Action cancel)
    {
        NativeSketchRelations410.ImmutableReferences(before.Inputs,after.Inputs,cancel);
        foreach(var row in before.Relations.GetProperty("constraints").EnumerateArray())
        {
            cancel();if(row.GetProperty("type").GetString()!="GeomFix")continue;var reference=row.GetProperty("first");if(reference.ValueKind==JsonValueKind.Null)continue;
            string id=reference.GetProperty("object_id").GetString()!;var old=before.Inputs.Members.Where(m=>Same(Key(m.ObjectId),id)||Same(Key(m.Id),id)).ToArray();var now=after.Inputs.Members.Where(m=>Same(Key(m.ObjectId),id)||Same(Key(m.Id),id)).ToArray();
            if(old.Length==0)
            {
                var construction=before.Inputs.Construction.Where(c=>Same(Key(c.Id),id)).ToArray();
                Need(construction.Length==1&&after.Inputs.Construction.Any(c=>c.Id==construction[0].Id&&ModelFeatureReorderState345.Equivalent(c.Geometry,construction[0].Geometry,cancel)),"CONSTRAINT_SCOPE_MISMATCH","A fixed native construction point disappeared or moved.");
                continue;
            }
            Need(old.Length==now.Length,"CONSTRAINT_SCOPE_MISMATCH","A fixed native member disappeared.");
            if(old.Length==1&&NativeSketchMembers385.PointRole(before.Inputs,old[0],reference) is string role)
            {
                var anchor=NativeSketchMembers385.Point(before.Inputs,old[0],role);
                Need(now.Length==1&&now[0].Id==old[0].Id&&NativeSketchRelations410.SamePoint(anchor,NativeSketchMembers385.Point(after.Inputs,now[0],role)),"CONSTRAINT_SOLVE_FAILED","A retained fixed native endpoint/center moved in actual WCS geometry.");
                continue;
            }
            // A point-specific fixed relation retains its actual original point.
            var point=reference.GetProperty("point");
            if(point.ValueKind==JsonValueKind.Null)
            {
                Need(old.All(prior=>now.Any(current=>current.Id==prior.Id&&SketchGeometryComparison.Same(prior.Geometry,current.Geometry))),"CONSTRAINT_SOLVE_FAILED","Whole-object native fixation did not preserve the complete fixed geometry.");
                continue;
            }
            Need(point.ValueKind==JsonValueKind.Array,"CONSTRAINTS_UNAVAILABLE","An existing fixed anchor point is unavailable.");var p=new Point3(point[0].GetDouble(),point[1].GetDouble(),point[2].GetDouble());var local=DrawingWorkplane.Local(before.Inputs.Plane,p);
            var candidates=old.SelectMany(m=>new[]{"start","end","center"}.Select(name=>{try{return (m,name,p:NativeSketchMembers385.PointLocal(before.Inputs,m,name));}catch(DomainException){return (m,name,p:(Point2?)null);}})).Where(v=>v.p!=null&&DrawingProfileGeometry.Same(v.p,local)).ToArray();
            Need(candidates.Length>0&&candidates.All(v=>after.Inputs.Members.Any(m=>m.Id==v.m.Id&&DrawingProfileGeometry.Same(NativeSketchMembers385.PointLocal(after.Inputs,m,v.name),local))),"CONSTRAINT_SOLVE_FAILED","A retained fixed native point moved.");
        }
    }
}
