using System.Text.Json;
using System.Text.Json.Nodes;
using Multicad;
using Multicad.DatabaseServices.Constraints;
using NanoCAD.Contracts;
using G=Multicad.Geometry;

namespace NanoCADBridge;

// A relation network contains native baseline construction as well as requested
// relations. The SDK creates a fixed construction point on editor entry; an
// unknown inventory never means an empty network. Only exact current native
// member/construction IDs can witness a baseline reference.
internal static class NativeSketchRelations410
{
    internal sealed record Baseline(JsonElement Relations,McObjectId[] NativeIds,bool Complete=true,string? UnavailableReason=null,NativeSketchMembers385.Snapshot? Inputs=null,
        string ObservationStage="observed_before_write",string? PreAddUnavailableReason=null);
    static void Need(bool ok,string code,string message)=>NativeSketchMembers385.Need(ok,code,message);
    static string Key(McObjectId id)=>id.ToString();
    internal static bool SamePoint(G.Point3d a,G.Point3d b)
    {
        bool Finite(G.Point3d p)=>double.IsFinite(p.X)&&double.IsFinite(p.Y)&&double.IsFinite(p.Z);
        if(!Finite(a)||!Finite(b))return !Finite(a)&&!Finite(b)&&double.IsNaN(a.X)&&double.IsNaN(a.Y)&&double.IsNaN(a.Z)&&double.IsNaN(b.X)&&double.IsNaN(b.Y)&&double.IsNaN(b.Z);
        return ModelParameterSnapshot.Near(a.X,b.X)&&ModelParameterSnapshot.Near(a.Y,b.Y)&&ModelParameterSnapshot.Near(a.Z,b.Z);
    }
    internal static bool MemberReference(NativeSketchMembers385.Snapshot inputs,JsonElement reference)
    {
        if(reference.ValueKind==JsonValueKind.Null)return true;
        string id=reference.GetProperty("object_id").GetString()!;int segment=reference.GetProperty("segment").GetInt32();
        return inputs.Members.Any(m=>Key(m.Id).Equals(id,StringComparison.OrdinalIgnoreCase)||Key(m.ObjectId).Equals(id,StringComparison.OrdinalIgnoreCase)&&(m.Segment==null||m.Segment==segment));
    }
    internal static JsonElement AddRequest(Constraints2D manager,ConstraintInfo info,SketchConstraintSpec spec,
        NativeSketchMembers385.Snapshot inputs,McObjectId activeSheet,McObjectId drawingDocument,int requestIndex,Baseline baseline)
    {
        object? Reference(IConstraintObjectInfo? value)
        {
            if(value==null||value.ObjectId.IsNull)return null;var p=value.PointRefOnObject;bool finite=double.IsFinite(p.X)&&double.IsFinite(p.Y)&&double.IsFinite(p.Z);
            return new{object_id=Key(value.ObjectId),subentity_id=value.SubentId.IsNull?null:value.SubentId.ToString(),segment=value.PolylineSegmentIndex,
                point=finite?new[]{p.X,p.Y,p.Z}:null,point_scope=finite?"actual_finite_reference":"unspecified_or_nonfinite"};
        }
        object? Member(string? id)
        {
            if(id==null)return null;var member=inputs.Members.Single(m=>Key(m.Id).Equals(id,StringComparison.OrdinalIgnoreCase));
            var stored=inputs.Stored.GetProperty("objects").EnumerateArray().Single(row=>row.GetProperty("id").GetString()==Key(member.ObjectId));
            return new{member_id=Key(member.Id),object_id=Key(member.ObjectId),member.NativeType,member.Segment,member.Owned,member.ReferenceOnly,
                owner_document_id=stored.GetProperty("owner_document_id").GetString()};
        }
        // Fixed-size request fields, at most three actual references and three
        // member identities. No geometry/metadata or imagined native HRESULT.
        return Wire.Element(new{stage="requested_AddConstraint_false",request_index=requestIndex,requested_kind=spec.Kind,apply=false,
            sketch_id=Key(inputs.Sketch.ID),host_solid_id=inputs.Sketch.HostSolidID.IsNull?null:Key(inputs.Sketch.HostSolidID),
            sketch_owner_document_id=inputs.Stored.GetProperty("owner_document_id").GetString(),active_sheet_id=Key(activeSheet),drawing_document_id=Key(drawingDocument),
            input_context_document_id=inputs.Stored.GetProperty("member_context_document_id").GetString(),
            manager_document_id=Key(manager.DocumentId),manager_sketch_id=manager.PlanarSketchId.IsNull?null:Key(manager.PlanarSketchId),
            manager_scope_id=manager.ScopeId.IsNull?null:Key(manager.ScopeId),manager_network=manager.ConstraintsNetworkType.ToString(),sketch_edit_mode=inputs.Sketch.IsInEditMode,
            baseline_complete=baseline.Complete,baseline_stage=baseline.ObservationStage,pre_add_inventory_available=baseline.Complete&&baseline.PreAddUnavailableReason==null,
            pre_add_unavailable_reason=baseline.PreAddUnavailableReason??baseline.UnavailableReason,
            constraint_type=info.Type.ToString(),is_geometrical=info.IsGeometrical,is_dimensional=info.IsDimensional,
            first=Reference(info.FirstObject),second=Reference(info.SecondObject),third=Reference(info.ThirdObject),first_member=Member(spec.First),second_member=Member(spec.Second),third_member=Member(spec.Third),
            native_hresult=(int?)null,native_hresult_unavailable_reason="managed_AddConstraint_returns_only_McObjectId; the nonzero native HRESULT is not exposed"});
    }
    internal static Baseline Capture(Constraints2D manager,NativeSketchMembers385.Snapshot inputs,Action cancel,IEnumerable<McObjectId>? inventory=null)
    {
        var ids=new HashSet<McObjectId>();var relations=NativeSketchConstraintState385.Relations(manager,cancel,(id,info)=>
        {
            ids.Add(id);if(!info.DimId.IsNull)ids.Add(info.DimId);if(!info.VarId.IsNull)ids.Add(info.VarId);
            foreach(var item in new[]{(Name:"first",Value:info.FirstObject),(Name:"second",Value:info.SecondObject),(Name:"third",Value:info.ThirdObject)})
            {
                var reference=item.Value;if(reference==null||reference.ObjectId.IsNull)continue;
                bool member=inputs.Members.Any(m=>m.Id==reference.ObjectId||m.ObjectId==reference.ObjectId&&(m.Segment==null||m.Segment==reference.PolylineSegmentIndex));
                bool construction=info.Type==ConstraintType.GeomFix&&inputs.Construction.Any(c=>c.Id==reference.ObjectId&&c.Geometry.GetProperty("kind").GetString()=="point");
                if(!member&&!construction)throw BaselineReferenceFailure(manager,inputs,id,info.Type,item.Name,reference,cancel);
            }
        },inventory);var captured=Capture(relations,inputs,cancel);ids.UnionWith(captured.NativeIds);
        return captured with{NativeIds=ids.OrderBy(Key,StringComparer.OrdinalIgnoreCase).ToArray()};
    }
    static DomainException BaselineReferenceFailure(Constraints2D manager,NativeSketchMembers385.Snapshot inputs,McObjectId relation,
        ConstraintType type,string role,IConstraintObjectInfo reference,Action cancel)
    {
        object Geometry(G.EntityGeometry? geometry)
        {
            double[]? point=null;if(geometry!=null&&NativeSketchMembers385.IsPoint(geometry))
            {var p=geometry.Point;if(double.IsFinite(p.X)&&double.IsFinite(p.Y)&&double.IsFinite(p.Z))point=[p.X,p.Y,p.Z];}
            return new{returned=geometry!=null,native_geometry_type=geometry?.GeometryType.ToString(),point};
        }
        object Failure(Exception error)=>new{exception_type=error.GetType().Name,exception_hresult=error.HResult};
        object? selectedGeometry=null,nativeGeometry=null,selectedError=null,nativeError=null;string? nativeType=null,nativeDocument=null;bool? nativeIdentity=null,contains=null;
        cancel();try{using var geometry=inputs.Sketch.GetItemGeometry(reference.ObjectId,false);selectedGeometry=Geometry(geometry);contains=inputs.Sketch.ContainsObject(reference.ObjectId);}
        catch(OperationCanceledException){throw;}catch(Exception error){selectedError=Failure(error);}
        cancel();try
        {
            var value=reference.ObjectId.GetObjectOfType<Multicad.DatabaseServices.StandardObjects.DbGeometry>();
            if(value!=null){nativeType=value.GetType().FullName;nativeIdentity=value.ID==reference.ObjectId;nativeDocument=value.DbEntity.Document?.ID.ToString();using var geometry=value.Geometry;nativeGeometry=Geometry(geometry);}
        }
        catch(OperationCanceledException){throw;}catch(Exception error){nativeError=Failure(error);}
        var p=reference.PointRefOnObject;bool finite=double.IsFinite(p.X)&&double.IsFinite(p.Y)&&double.IsFinite(p.Z);
        return new DomainException("CONSTRAINT_SCOPE_MISMATCH","A baseline relation references neither an exact selected-sketch input nor its observed native fixed construction point.",false,new{
            stage="baseline_reference_validation",selected_sketch_id=Key(inputs.Sketch.ID),selected_sketch_document_id=inputs.Stored.GetProperty("owner_document_id").GetString(),
            manager_document_id=Key(manager.DocumentId),manager_sketch_id=manager.PlanarSketchId.IsNull?null:Key(manager.PlanarSketchId),manager_scope_id=manager.ScopeId.IsNull?null:Key(manager.ScopeId),manager_network=manager.ConstraintsNetworkType.ToString(),
            relation_id=Key(relation),constraint_type=type.ToString(),reference_role=role,object_id=Key(reference.ObjectId),subentity_id=reference.SubentId.IsNull?null:reference.SubentId.ToString(),segment=reference.PolylineSegmentIndex,
            reported_reference_point=finite?new[]{p.X,p.Y,p.Z}:null,contains_object=contains,observed_construction_count=inputs.Construction.Length,
            selected_sketch_GetItemGeometry_wcs=selectedGeometry,selected_lookup_error=selectedError,native_object_type=nativeType,native_object_identity_matches=nativeIdentity,native_object_document_id=nativeDocument,native_object_geometry=nativeGeometry,native_lookup_error=nativeError,
            verification="actual native reference ID and readonly geometry/type observations; no coordinate-to-origin identity inference"});
    }
    internal static Baseline Unavailable(NativeSketchMembers385.Snapshot inputs,string reason)=>new(
        Wire.Element(new{document_id=inputs.Sketch.DbEntity.Document.ID.ToString(),sketch_id=inputs.Sketch.ID.ToString(),constraints=(object?)null,observation_scope="native_baseline_unavailable_not_empty"}),inputs.Construction.Select(c=>c.Id).ToArray(),false,reason,inputs);
    internal static Baseline Capture(JsonElement relations,NativeSketchMembers385.Snapshot inputs,Action cancel)
    {
        var ids=inputs.Construction.Select(c=>c.Id).ToHashSet();
        foreach(var row in relations.GetProperty("constraints").EnumerateArray())
        {
            cancel();
            Need(new[]{"first","second","third"}.Any(name=>row.GetProperty(name).ValueKind==JsonValueKind.Object),"CONSTRAINT_SCOPE_MISMATCH","A baseline relation has no exact native geometry reference.");
            foreach(string name in new[]{"first","second","third"})
            {
                var reference=row.GetProperty(name);if(reference.ValueKind==JsonValueKind.Null||MemberReference(inputs,reference))continue;
                string id=reference.GetProperty("object_id").GetString()!;
                var construction=inputs.Construction.Where(c=>Key(c.Id).Equals(id,StringComparison.OrdinalIgnoreCase)).ToArray();
                if(!(row.GetProperty("type").GetString()=="GeomFix"&&construction.Length==1&&construction[0].Geometry.GetProperty("kind").GetString()=="point"))
                {
                    string Hash(IEnumerable<McObjectId> values)=>Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(values.Select(Key).OrderBy(value=>value,StringComparer.OrdinalIgnoreCase),Wire.Json)));
                    throw new DomainException("CONSTRAINT_SCOPE_MISMATCH","A baseline relation references neither an exact selected-sketch input nor its observed native fixed construction point.",false,new{
                        stage="baseline_reference_validation",selected_sketch_id=Key(inputs.Sketch.ID),relation_id=row.GetProperty("id").GetString(),constraint_type=row.GetProperty("type").GetString(),reference_role=name,actual_reference=reference,
                        observed_member_count=inputs.Members.Length,observed_member_ids_sha256=Hash(inputs.Members.Select(m=>m.Id)),observed_construction_count=inputs.Construction.Length,observed_construction_ids_sha256=Hash(inputs.Construction.Select(c=>c.Id)),
                        matching_construction=construction.Select(c=>new{id=Key(c.Id),geometry_kind=c.Geometry.GetProperty("kind").GetString()}).ToArray(),
                        verification="exact actual member/construction identity inventory; no coordinate-to-origin alias"});
                }
                ids.Add(construction[0].Id);
            }
        }
        return new(relations,ids.OrderBy(Key,StringComparer.OrdinalIgnoreCase).ToArray(),Inputs:inputs);
    }
    internal static JsonElement Design(JsonElement relation,NativeSketchMembers385.Snapshot? inputs=null)
    {
        var row=JsonNode.Parse(relation.GetRawText())!.AsObject();row.Remove("id");
        // Param/VarName/VarExpression describe a dimension (SDK property docs),
        // not geometrical constraint intent. Empty native defaults can differ
        // between the editor and persistent network.
        if(row["dimensional"]?.GetValue<bool>()==false)foreach(string field in new[]{"name","expression","value"})row[field]=null;
        if(row["type"]?.GetValue<string>()!="GeomFix")
        {foreach(string name in new[]{"first","second","third"})if(row[name] is JsonObject reference)reference["point"]=null;}
        else if(inputs!=null)foreach(string fieldName in new[]{"first","second","third"})if(row[fieldName] is JsonObject pointReference&&pointReference["object_id"] is JsonValue identity)
        {
            var points=inputs.Construction.Where(c=>Key(c.Id).Equals(identity.GetValue<string>(),StringComparison.OrdinalIgnoreCase)).ToArray();
            if(points.Length==1)pointReference["point"]=JsonNode.Parse(points[0].Geometry.GetProperty("point").GetRawText());
            else
            {
                string id=identity.GetValue<string>();int segment=pointReference["segment"]!.GetValue<int>();
                var members=inputs.Members.Where(m=>Key(m.Id).Equals(id,StringComparison.OrdinalIgnoreCase)||Key(m.ObjectId).Equals(id,StringComparison.OrdinalIgnoreCase)&&m.Segment==segment).ToArray();
                if(members.Length==1&&NativeSketchMembers385.PointRole(inputs,members[0],Wire.Element(pointReference)) is string role)
                {var point=NativeSketchMembers385.Point(inputs,members[0],role);pointReference["point"]=JsonSerializer.SerializeToNode(new[]{point.X,point.Y,point.Z},Wire.Json);}
            }
        }
        if(inputs!=null)CanonicalSegments(row,inputs);
        return Wire.Element(row);
    }
    internal static void CanonicalSegments(JsonObject relation,NativeSketchMembers385.Snapshot inputs)
    {
        foreach(string name in new[]{"first","second","third"})
            if(relation[name] is JsonObject reference&&reference["object_id"] is JsonValue identity&&NativeSketchMembers385.DirectCurveReference(inputs,identity.GetValue<string>()))
                reference["segment"]=null;
    }
    internal static JsonElement Without(JsonElement relations,ISet<string> ids)
    {
        var root=JsonNode.Parse(relations.GetRawText())!.AsObject();var rows=root["constraints"]!.AsArray();
        for(int index=rows.Count-1;index>=0;index--)if(ids.Contains(rows[index]!["id"]!.GetValue<string>()))rows.RemoveAt(index);
        return Wire.Element(root);
    }
    internal static string[] Retained(Baseline baseline,JsonElement current,NativeSketchMembers385.Snapshot inputs,Action cancel,bool allowNetworkTransfer=false)
    {
        Need(baseline.Complete,"CONSTRAINTS_UNAVAILABLE","An unavailable native baseline cannot be compared as an empty network.");
        Capture(current,inputs,cancel);var rows=current.GetProperty("constraints").EnumerateArray().ToArray();var matched=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach(var previous in baseline.Relations.GetProperty("constraints").EnumerateArray())
        {
            cancel();string oldId=previous.GetProperty("id").GetString()!;
            var exact=rows.Where(row=>row.GetProperty("id").GetString()!.Equals(oldId,StringComparison.OrdinalIgnoreCase)).ToArray();
            var oldDesign=Design(previous,baseline.Inputs);
            var candidates=exact.Length==1?exact:allowNetworkTransfer?rows.Where(row=>ModelFeatureReorderState345.Equivalent(oldDesign,Design(row,inputs),cancel)).ToArray():[];
            if(!(candidates.Length==1&&ModelFeatureReorderState345.Equivalent(oldDesign,Design(candidates[0],inputs),cancel)&&matched.Add(candidates[0].GetProperty("id").GetString()!)))
                throw new DomainException("CONSTRAINT_BASELINE_CHANGED","A prior native relation disappeared, changed its intent/control/reference, or has an ambiguous network-transfer binding.",false,new{
                    stage="baseline_network_transfer",allow_network_transfer=allowNetworkTransfer,previous,previous_design=oldDesign,
                    exact_id_count=exact.Length,semantic_candidate_count=candidates.Length,
                    candidate_relations=rows.Where(row=>row.GetProperty("type").GetString()==previous.GetProperty("type").GetString()).Select(row=>new{relation=row,design=Design(row,inputs)}).ToArray()});
            if(baseline.Inputs!=null)ReferenceRoles(previous,candidates[0],baseline.Inputs,inputs,cancel);
        }
        NativeSketchMembers385.ValidateSmoothRelations(current,inputs,cancel);
        return matched.ToArray();
    }
    static void ReferenceRoles(JsonElement before,JsonElement after,NativeSketchMembers385.Snapshot oldInputs,NativeSketchMembers385.Snapshot currentInputs,Action cancel)
    {
        foreach(string field in new[]{"first","second","third"})
        {
            cancel();var old=before.GetProperty(field);var current=after.GetProperty(field);if(old.ValueKind!=JsonValueKind.Object||old.GetProperty("point").ValueKind!=JsonValueKind.Array)continue;
            string id=old.GetProperty("object_id").GetString()!;int segment=old.GetProperty("segment").GetInt32();
            var members=oldInputs.Members.Where(m=>Key(m.Id).Equals(id,StringComparison.OrdinalIgnoreCase)||Key(m.ObjectId).Equals(id,StringComparison.OrdinalIgnoreCase)&&(m.Segment==null||m.Segment==segment)).ToArray();
            if(members.Length!=1)continue;string? role=NativeSketchMembers385.PointRole(oldInputs,members[0],old);
            // Existing arbitrary native selector points are retained by their
            // real object/subentity binding; no endpoint role is invented.
            if(role==null)continue;
            var actual=currentInputs.Members.Where(m=>m.Id==members[0].Id).ToArray();Need(actual.Length==1&&current.ValueKind==JsonValueKind.Object&&current.GetProperty("point").ValueKind==JsonValueKind.Array,"CONSTRAINT_POINT_REFERENCE_CHANGED","A prior endpoint/center reference lost its native member or finite point role.");
            Need(NativeSketchMembers385.PointRole(currentInputs,actual[0],current)==role,"CONSTRAINT_POINT_REFERENCE_CHANGED","A prior relation switched endpoint/center role during native solve.");
        }
    }
    internal static void ImmutableReferences(NativeSketchMembers385.Snapshot before,NativeSketchMembers385.Snapshot after,Action cancel)
    {
        foreach(var prior in before.Members.Where(m=>m.ReferenceOnly))
        {
            cancel();var current=after.Members.Where(m=>m.Id==prior.Id&&m.ReferenceOnly&&m.ObjectId==prior.ObjectId&&m.Segment==prior.Segment).ToArray();
            Need(current.Length==1&&DrawingProfileGeometry.Same(prior.Curve,current[0].Curve),"PROJECTION_REFERENCE_CHANGED","Native solve changed the physical geometry or identity of an immutable source projection.");
        }
        foreach(var prior in before.Construction)
        {
            cancel();var current=after.Construction.Where(c=>c.Id==prior.Id).ToArray();
            Need(current.Length==1&&ModelFeatureReorderState345.Equivalent(prior.Geometry,current[0].Geometry,cancel),"CONSTRAINT_ANCHOR_CHANGED","A retained native construction point disappeared or moved.");
        }
    }
    internal static void RequirePointReceipt(SketchConstraintSpec spec,ConstraintInfo actual,NativeSketchMembers385.Snapshot inputs)
    {
        if(spec.Kind is not ("fix_point" or "coincident" or "smooth" or "distance" or "distance_x" or "distance_y" or "length"))return;
        using var expected=NativeSketchMembers385.Info(spec,inputs);
        Need(NativeSketchMembers385.RequestedReferences(actual,expected,spec,inputs,comparePoints:true),"CONSTRAINT_POINT_REFERENCE_CHANGED","Returned native relation does not retain the requested endpoint/center selector and actual WCS geometry; unresolved picks require exact finite WCS coordinates.");
    }
    internal static McObjectId[] RequestedPersistent(Constraints2D manager,SketchConstraintSpec[] specs,NativeSketchMembers385.Snapshot inputs,ISet<string> baselineIds,Action cancel)
    {
        var inventory=manager.GetAllConstraints()??throw new DomainException("CONSTRAINTS_UNAVAILABLE","The persistent sketch relation inventory is unavailable.");var result=new List<McObjectId>();
        foreach(var spec in specs)
        {
            cancel();using var expected=NativeSketchMembers385.Info(spec,inputs);var candidates=new List<McObjectId>();
            foreach(var id in inventory)
            {
                cancel();if(baselineIds.Contains(Key(id)))continue;using var info=manager.GetConstraintInfo(id);
                if(info==null||!NativeSketchMembers385.RequestedReferences(info,expected,spec,inputs))continue;
                if(spec.Kind is "fix_point" or "coincident" or "smooth" or "distance" or "distance_x" or "distance_y" or "length")
                {if(!NativeSketchMembers385.RequestedReferences(info,expected,spec,inputs,comparePoints:true))continue;}
                if(spec.Value.HasValue&&(!info.IsDimensional||info.VarId.IsNull||manager.GetConstraintParamId(id)!=info.VarId||!ModelParameterSnapshot.Near(manager.GetConstraintParamValue(id),spec.Value.Value)))continue;
                candidates.Add(id);
            }
            Need(candidates.Count==1&&!result.Contains(candidates[0]),"CONSTRAINT_PERSISTENCE_UNAVAILABLE","A requested relation did not transfer uniquely to the persistent network with exact point/control binding.");result.Add(candidates[0]);
        }
        return result.ToArray();
    }
}
