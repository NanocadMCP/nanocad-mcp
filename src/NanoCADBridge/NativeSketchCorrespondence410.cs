using System.Text.Json;
using System.Text.Json.Nodes;
using Multicad;
using Multicad.DatabaseServices.Constraints;
using NanoCAD.Contracts;

namespace NanoCADBridge;

// A marker is used only across this one editor session. It is never published
// as a permanent identity, and the geometric-search fallback is never called.
internal static class NativeSketchCorrespondence410
{
    internal sealed record ConstraintWitness(McObjectId Relation,McObjectId Variable,string Type,JsonElement OwnerVariable);
    internal sealed record Item(McObjectId Original,long Marker,string Kind,bool Construction,ConstraintWitness? Binding=null);
    internal sealed record OriginRole(McObjectId Point,McObjectId Relation,JsonElement Intent);
    internal sealed record PointRoleWitness(string Relation,string Field,string ObjectId,string? Selector,int Segment,string? Role);
    internal sealed record Markers(NativeSketchMembers385.Snapshot Before,Item[] Items,JsonElement Relations,OriginRole? Origin,JsonElement OriginAdmission,NativeSketchDimensionPresentation414.Witness[] PresentationWitnesses,PointRoleWitness[]? PointRoles=null);
    internal sealed record ReferenceSelector(string OriginalRelation,string CurrentRelation,string Field,string? OriginalSelector,string? CurrentSelector,string? CurrentSubtype,string Role);
    internal sealed record Mapping(Dictionary<McObjectId,McObjectId> Ids,JsonElement Receipt,OriginRole? ServiceOrigin=null,NativeSketchDimensionPresentation414.Transfer[]? Presentations=null,ReferenceSelector[]? ReferenceSelectors=null)
    {
        internal McObjectId Map(McObjectId id)=>Ids.TryGetValue(id,out var current)?current:id;
        internal string Map(string id)
        {foreach(var pair in Ids)if(Same(Key(pair.Key),id))return Key(pair.Value);return id;}
    }
    static string Key(McObjectId id)=>id.ToString();
    static bool Same(string? a,string? b)=>string.Equals(a,b,StringComparison.OrdinalIgnoreCase);
    static void Need([System.Diagnostics.CodeAnalysis.DoesNotReturnIf(false)] bool valid,string message,object details)
    {if(!valid)throw new DomainException("SKETCH_CORRESPONDENCE_UNAVAILABLE",message,false,details);}

    internal static Markers Capture(NativeSketchMembers385.Snapshot before,JsonElement relations,Action cancel)
    {
        var sketch=before.Sketch;Need(!sketch.IsInEditMode,"Capture current native markers before entering the sketch editor.",new{sketch_id=Key(sketch.ID)});
        var items=new List<Item>();var markers=new HashSet<long>();var budget=new ModelInventoryBudget(cancel);
        ConstraintWitness Binding(McObjectId id)
        {
            var members=before.Members.Where(member=>member.Id==id&&member.ObjectId==id&&member.Owned&&!member.ReferenceOnly&&member.Segment==null&&member.NativeType=="kCircArc").ToArray();
            var candidates=relations.GetProperty("constraints").EnumerateArray().Where(row=>RadialBinding(row,Key(id))).ToArray();
            Need(members.Length==1&&candidates.Length==1,"An unmarked owned curve lacks one exact retained radial relation/variable binding; no coordinate or name match is used.",new{stage="before_editor_entry",sketch_id=Key(sketch.ID),object_id=Key(id),direct_circular_member_count=members.Length,radial_binding_count=candidates.Length});
            var row=candidates[0];string relationId=row.GetProperty("id").GetString()!,variableId=row.GetProperty("variable").GetString()!;
            var relation=before.Helpers.Where(actual=>Same(Key(actual),relationId)).ToArray();var variable=before.Helpers.Where(actual=>Same(Key(actual),variableId)).ToArray();
            Need(relation.Length==1&&variable.Length==1&&relations.GetProperty("constraints").EnumerateArray().Count(actual=>Same(actual.GetProperty("variable").GetString(),variableId))==1,"The retained radial binding has no unique actual native relation/variable identities.",new{stage="before_editor_entry",object_id=Key(id),constraint_id=relationId,variable_id=variableId});
            return new(relation[0],variable[0],row.GetProperty("type").GetString()!,OwnerVariable(before,variable[0],cancel));
        }
        void Read(McObjectId id,bool construction)
        {
            cancel();using var geometry=sketch.GetItemGeometry(id,false);
            Need(geometry!=null&&NativeSketchMembership445.Member(sketch,id,geometry,cancel),"The original item lacks its exact current sketch geometry/membership.",new{stage="before_editor_entry",sketch_id=Key(sketch.ID),object_id=Key(id)});
            long marker=geometry!.GSMarker;
            // Optional construction anchors can retain their same exact ID.
            // An unmarked owned circular curve can instead use an exact
            // retained radial constraint and its actual control variable.
            var reverse=marker==-1?McObjectId.Null:sketch.GetSketchEntityByGsMarker(marker);
            if(construction&&(marker==-1||reverse!=id||markers.Contains(marker)))return;
            if(!construction&&marker==-1)
            {
                var binding=Binding(id);var fallbackItem=new Item(id,marker,geometry.GeometryType.ToString(),false,binding);
                budget.Add(new{original_id=Key(id),marker,fallbackItem.Kind,identity_basis="exact_retained_radial_constraint_and_variable",constraint_id=Key(binding.Relation),variable_id=Key(binding.Variable)});items.Add(fallbackItem);return;
            }
            Need(marker!=-1&&reverse==id&&markers.Add(marker),"A native item has no unique reversible GS marker; no geometry-to-ID alias is inferred.",new{
                stage="before_editor_entry",sketch_id=Key(sketch.ID),object_id=Key(id),marker,reverse_id=reverse.IsNull?null:Key(reverse),geometry_kind=geometry.GeometryType.ToString()});
            var item=new Item(id,marker,geometry.GeometryType.ToString(),construction);budget.Add(new{original_id=Key(id),marker,item.Kind,item.Construction});items.Add(item);
        }
        foreach(var id in before.Members.Where(m=>m.Owned).Select(m=>m.ObjectId).Distinct().OrderBy(Key,StringComparer.OrdinalIgnoreCase))Read(id,false);
        foreach(var point in before.Construction.OrderBy(p=>Key(p.Id),StringComparer.OrdinalIgnoreCase))Read(point.Id,true);
        var origin=ObserveOrigin(before,relations,cancel,requireCleanMetadata:true);
        return new(before,items.ToArray(),relations,origin.Role,origin.Admission,NativeSketchDimensionPresentation414.Capture(before,relations,cancel));
    }

    internal static Mapping Resolve(Markers markers,NativeSketchMembers385.Snapshot current,JsonElement currentRelations,Action cancel,string phase,bool preserveGeometry,Constraints2D? observedManager=null)
        =>ResolveCore(markers,current,currentRelations,cancel,phase,preserveGeometry,observedManager,false,true);

    // Existing editor editing keeps its exact prior admission. This additional
    // solver route proves ephemeral selectors by their actual native endpoint
    // role only after the complete GS-marker member bijection. Before Apply a
    // requested expression can legitimately leave a pending open contour.
    internal static Mapping ResolveForSolve(Markers markers,NativeSketchMembers385.Snapshot current,JsonElement currentRelations,Action cancel,string phase,bool preserveGeometry,bool solved,Constraints2D? observedManager=null)
        =>ResolveCore(markers,current,currentRelations,cancel,phase,preserveGeometry,observedManager,true,solved);
    static Mapping ResolveCore(Markers markers,NativeSketchMembers385.Snapshot current,JsonElement currentRelations,Action cancel,string phase,bool preserveGeometry,Constraints2D? observedManager,bool solverTransition,bool solved)
    {
        var before=markers.Before;var ids=new Dictionary<McObjectId,McObjectId>();var actual=new HashSet<McObjectId>();var rows=new List<object>();var budget=new ModelInventoryBudget(cancel);
        using var acquiredManager=observedManager==null?NativeSketchMembers385.Manager(current.Sketch):null;var manager=observedManager??acquiredManager!;
        Need(before.Sketch.ID==current.Sketch.ID&&Same(before.Stored.GetProperty("owner_document_id").GetString(),current.Stored.GetProperty("owner_document_id").GetString())&&
            ModelFeatureReorderState345.Equivalent(Wire.Element(before.Plane),Wire.Element(current.Plane),cancel),
            "Native editor correspondence changed the selected sketch, permanent owner or frame.",new{phase,sketch_id=Key(current.Sketch.ID)});
        Need(Same(Key(manager.DocumentId),currentRelations.GetProperty("document_id").GetString())&&Same(Key(manager.PlanarSketchId),currentRelations.GetProperty("sketch_id").GetString())&&Same(Key(manager.ScopeId),currentRelations.GetProperty("scope_id").GetString())&&manager.ConstraintsNetworkType.ToString()==currentRelations.GetProperty("network").GetString(),
            "Native correspondence does not use the actual observed sketch manager scope.",new{phase,sketch_id=Key(current.Sketch.ID)});
        foreach(var item in markers.Items)
        {
            if(item.Binding is { } binding)
            {
                // A sketch's stored item can be an abstract GUID and its editor
                // operand a resident object (3DCore.h). Do not require their
                // relation IDs to be equal. The exact owner variable alone is
                // only an index: prove its unique actual dimensional relation,
                // operand, reverse membership and unchanged design below.
                cancel();var exact=currentRelations.GetProperty("constraints").EnumerateArray().Where(row=>Same(row.GetProperty("id").GetString(),Key(binding.Relation))).ToArray();
                var candidates=exact.Length==1?exact:currentRelations.GetProperty("constraints").EnumerateArray().Where(row=>Same(row.GetProperty("variable").GetString(),Key(binding.Variable))).ToArray();
                Need(candidates.Length==1,"The retained radial control has no unique actual relation in the current native network.",new{phase,original_id=Key(item.Original),constraint_id=Key(binding.Relation),variable_id=Key(binding.Variable),constraint_count=candidates.Length,exact_id_count=exact.Length,
                    actual_radial_relations=currentRelations.GetProperty("constraints").EnumerateArray().Where(row=>row.GetProperty("type").GetString() is "DimRadial" or "DimDiametrical").Take(3).Select(row=>new{id=row.GetProperty("id").GetString(),type=row.GetProperty("type").GetString(),first=row.GetProperty("first"),variable=row.GetProperty("variable"),value=row.GetProperty("value"),expression=row.GetProperty("expression")}).ToArray()});
                var bindingRelation=candidates[0];string? reference=bindingRelation.GetProperty("first").ValueKind==JsonValueKind.Object?bindingRelation.GetProperty("first").GetProperty("object_id").GetString():null;
                Need(reference!=null&&RadialBinding(bindingRelation,reference)&&bindingRelation.GetProperty("type").GetString()==binding.Type&&Same(bindingRelation.GetProperty("variable").GetString(),Key(binding.Variable))&&currentRelations.GetProperty("constraints").EnumerateArray().Count(constraintRow=>Same(constraintRow.GetProperty("variable").GetString(),Key(binding.Variable)))==1,
                    "The retained native radial constraint changed its type, whole-curve role or exact control variable.",new{phase,original_id=Key(item.Original),constraint_id=Key(binding.Relation),variable_id=Key(binding.Variable),actual_first_id=reference,actual_type=bindingRelation.GetProperty("type").GetString(),actual_variable_id=bindingRelation.GetProperty("variable").GetString()});
                var relationIds=current.Helpers.Where(id=>Same(Key(id),bindingRelation.GetProperty("id").GetString())).ToArray();
                var members=current.Members.Where(member=>Same(Key(member.Id),reference)&&member.Id==member.ObjectId&&member.Owned&&!member.ReferenceOnly&&member.Segment==null&&member.NativeType==item.Kind).ToArray();
                Need(members.Length==1&&relationIds.Length==1&&current.Helpers.Contains(binding.Variable),"The actual retained radial operand is not one exact current owned circular member with its observed native helpers.",new{phase,original_id=Key(item.Original),constraint_id=Key(binding.Relation),current_id=reference,current_member_count=members.Length});
                var relationId=relationIds[0];using var relationInfo=manager.GetConstraintInfo(relationId);
                Need(relationInfo!=null&&relationInfo.VarId==binding.Variable&&relationInfo.Type.ToString()==binding.Type,"The live native radial receipt does not bind the exact retained control.",new{phase,original_constraint_id=Key(binding.Relation),current_constraint_id=Key(relationId),variable_id=Key(binding.Variable)});
                var actualBinding=NativeSketchDimensionBinding411.Prove(manager,relationId,relationInfo!,current,cancel);
                var boundId=members[0].Id;using var boundGeometry=current.Sketch.GetItemGeometry(boundId,false);
                Need(boundGeometry!=null&&boundGeometry.GeometryType.ToString()==item.Kind&&current.Sketch.ContainsObject(boundId)&&actual.Add(boundId),"The retained radial operand lacks its actual selected-sketch geometry/membership or duplicates another mapped input.",new{phase,original_id=Key(item.Original),current_id=Key(boundId),constraint_id=Key(binding.Relation)});
                ids.Add(item.Original,boundId);ids.Add(binding.Relation,relationId);
                var ownerVariable=OwnerVariable(current,binding.Variable,cancel);var mapping=new Mapping(ids,Wire.Element(new{}));
                Need(binding.OwnerVariable.GetProperty("value_available").GetBoolean()&&ownerVariable.GetProperty("value_available").GetBoolean()&&ModelFeatureReorderState345.Equivalent(Identities(binding.OwnerVariable,mapping,cancel),ownerVariable,cancel),"The exact retained radial variable changed its permanent owner, name, expression, evaluated value or witnessed native host during the native transition.",new{phase,constraint_id=Key(binding.Relation),variable_id=Key(binding.Variable)});
                var control=NativeSketchDimensionBinding411.Spec(relationInfo!,actualBinding.Kind,current,ownerVariable.GetProperty("value").GetDouble());if(!solverTransition||solved)NativeSketchMembers385.ValidateSolved([control],current);
                var bindingReceipt=new{original_id=Key(item.Original),current_id=Key(boundId),marker=(long?)null,geometry_kind=item.Kind,role="owned_input",identity_basis="exact_owner_VarId_and_metadata; unique_actual_radial_relation_and_whole_curve_operand; same_manager_reverse_binding; WCS_geometry_and_full_retained_intent",original_constraint_id=Key(binding.Relation),current_constraint_id=Key(relationId),variable_id=Key(binding.Variable),same_manager_reverse_binding=true};budget.Add(bindingReceipt);rows.Add(bindingReceipt);continue;
            }
            cancel();var resolved=current.Sketch.GetSketchEntityByGsMarker(item.Marker);using var geometry=resolved.IsNull?null:current.Sketch.GetItemGeometry(resolved,false);
            bool member=item.Construction?current.Construction.Any(p=>p.Id==resolved):current.Members.Any(m=>m.ObjectId==resolved&&m.Owned&&!m.ReferenceOnly);
            // Optional service construction is not required to retain a draw
            // marker. Its exact retained native Fix relation can witness it.
            if(item.Construction&&(resolved.IsNull||!member||geometry==null||geometry.GSMarker!=item.Marker||geometry.GeometryType.ToString()!=item.Kind))continue;
            Need(!resolved.IsNull&&member&&NativeSketchMembership445.Member(current.Sketch,resolved,geometry,cancel)&&geometry!=null&&geometry.GSMarker==item.Marker&&geometry.GeometryType.ToString()==item.Kind&&actual.Add(resolved),
                "The original native GS marker does not resolve to one exact current item of the same role/type.",new{
                    phase,sketch_id=Key(current.Sketch.ID),original_id=Key(item.Original),marker=item.Marker,current_id=resolved.IsNull?null:Key(resolved),current_geometry_kind=geometry?.GeometryType.ToString(),current_marker=geometry?.GSMarker,member});
            ids.Add(item.Original,resolved);var row=new{original_id=Key(item.Original),current_id=Key(resolved),marker=item.Marker,geometry_kind=item.Kind,role=item.Construction?"construction_anchor":"owned_input"};budget.Add(row);rows.Add(row);
        }
        foreach(var prior in before.Members)
        {
            cancel();var parent=ids.TryGetValue(prior.ObjectId,out var value)?value:prior.ObjectId;
            var matches=current.Members.Where(m=>m.ObjectId==parent&&m.Segment==prior.Segment&&m.Owned==prior.Owned&&m.ReferenceOnly==prior.ReferenceOnly&&m.NativeType==prior.NativeType&&m.Curve.Kind==prior.Curve.Kind).ToArray();
            Need(matches.Length==1,"A native correspondence lost a member/segment/type or changed its ownership/reference role.",new{phase,original_member_id=Key(prior.Id),current_object_id=Key(parent),segment=prior.Segment});
            var mapped=matches[0];
            if(prior.ReferenceOnly)Need(mapped.Id==prior.Id&&mapped.ObjectId==prior.ObjectId,"A projected reference changed its exact native identity.",new{phase,projection_id=Key(prior.ObjectId)});
            if(prior.Segment.HasValue)Need(mapped.Id==ConstraintInfo.MakePolylineSegmentId(parent,prior.Segment.Value),"A segment has no exact SDK correspondence to its native parent.",new{phase,original_member_id=Key(prior.Id),current_member_id=Key(mapped.Id),segment=prior.Segment});
            if(ids.TryGetValue(prior.Id,out var existing))Need(existing==mapped.Id,"Native member correspondence is inconsistent.",new{phase,original_member_id=Key(prior.Id)});else ids.Add(prior.Id,mapped.Id);
            if(preserveGeometry||prior.ReferenceOnly)Need(DrawingProfileGeometry.Same(prior.Curve,mapped.Curve),"Editor entry changed physical geometry before the requested setter, or changed an immutable projection.",new{phase,original_member_id=Key(prior.Id),current_member_id=Key(mapped.Id)});
            if(prior.ReferenceOnly&&prior.Segment==null){var row=new{original_id=Key(prior.Id),current_id=Key(mapped.Id),marker=(long?)null,geometry_kind=prior.NativeType,role="immutable_projection",identity_basis="same_exact_native_ID"};budget.Add(row);rows.Add(row);}
        }
        Need(before.Members.Length==current.Members.Length&&before.Members.Select(m=>ids[m.Id]).ToHashSet().SetEquals(current.Members.Select(m=>m.Id)),
            "Native correspondence is not a complete bijection of current editable/reference members.",new{phase,before_count=before.Members.Length,current_count=current.Members.Length});
        var currentOrigin=ObserveOrigin(current,currentRelations,cancel);
        foreach(var point in before.Construction)
        {
            cancel();var id=ids.TryGetValue(point.Id,out var value)?value:point.Id;
            var matches=current.Construction.Where(p=>p.Id==id).ToArray();
            if(matches.Length==0)
            {
                var prior=markers.Relations.GetProperty("constraints").EnumerateArray().Where(row=>FixedPoint(row,Key(point.Id))).ToArray();
                var currentRows=currentRelations.GetProperty("constraints").EnumerateArray().ToArray();
                var bindings=new List<(JsonElement Relation,NativeSketchMembers385.Construction Point)>();
                foreach(var relation in prior)
                {
                    cancel();string relationId=relation.GetProperty("id").GetString()!;
                    foreach(var actualRelation in currentRows.Where(row=>Same(row.GetProperty("id").GetString(),relationId)))
                    {
                        if(actualRelation.GetProperty("first").ValueKind!=JsonValueKind.Object)continue;
                        string? referenceId=actualRelation.GetProperty("first").GetProperty("object_id").GetString();if(referenceId==null||!FixedPoint(actualRelation,referenceId)||!ModelFeatureReorderState345.Equivalent(FixedIntent(relation,before),FixedIntent(actualRelation,current),cancel))continue;
                        foreach(var actualPoint in current.Construction.Where(p=>Same(Key(p.Id),referenceId)))
                        {
                            using var geometry=current.Sketch.GetItemGeometry(actualPoint.Id,false);
                            if(geometry!=null&&NativeSketchMembers385.IsPoint(geometry)&&current.Sketch.ContainsObject(actualPoint.Id))bindings.Add((actualRelation,actualPoint));
                        }
                    }
                }
                var targets=bindings.Select(binding=>binding.Point.Id).Distinct().ToArray();
                if(targets.Length==1&&!ids.Values.Contains(targets[0]))
                {
                    id=targets[0];matches=current.Construction.Where(p=>p.Id==id).ToArray();ids.Add(point.Id,id);
                    var row=new{original_id=Key(point.Id),current_id=Key(id),marker=(long?)null,geometry_kind="point",role="constraint_bound_construction_anchor",identity_basis="same_exact_native_GeomFix_relation_ID_and_actual_current_first_reference",relation_ids=bindings.Select(binding=>binding.Relation.GetProperty("id").GetString()).Distinct().ToArray()};budget.Add(row);rows.Add(row);
                }
                else if(markers.Origin is { } priorOrigin&&priorOrigin.Point==point.Id)
                {
                    var origin=currentOrigin.Role;
                    if(origin!=null&&!ids.Values.Contains(origin.Point)&&ModelFeatureReorderState345.Equivalent(priorOrigin.Intent,origin.Intent,cancel))
                    {
                        id=origin.Point;matches=current.Construction.Where(p=>p.Id==id).ToArray();ids.Add(point.Id,id);ids.Add(priorOrigin.Relation,origin.Relation);
                        var row=new{original_id=Key(point.Id),current_id=Key(id),marker=(long?)null,geometry_kind="point",role="optional_native_origin_recreated",identity_basis="unique_actual_selected_sketch_point_and_whole_Fix_at_native_origin; same_owner_frame_and_fixed_intent; not_same_object_GUID",original_fix_relation_id=Key(priorOrigin.Relation),current_fix_relation_id=Key(origin.Relation)};budget.Add(row);rows.Add(row);
                    }
                }
            }
            Need(matches.Length==1&&ModelFeatureReorderState345.Equivalent(matches[0].Geometry,point.Geometry,cancel),"A native construction anchor lost its exact witnessed identity or moved.",new{
                phase,original_id=Key(point.Id),current_id=Key(id),current_edit_mode=current.Sketch.IsInEditMode,current_point_count=matches.Length,
                original_wcs_geometry=point.Geometry,current_wcs_geometry=matches.Length==1?(JsonElement?)matches[0].Geometry:null,geometry_source="selected_sketch_GetItemGeometry(id,false); global_coordinates",
                current_construction=current.Construction.Take(3).Select(p=>new{id=Key(p.Id),geometry=p.Geometry}).ToArray(),
                prior_fixed_reference_ids=markers.Relations.GetProperty("constraints").EnumerateArray().Where(row=>FixedPoint(row,Key(point.Id))).Take(3).Select(row=>row.GetProperty("id").GetString()).ToArray(),
                current_fixed_references=currentRelations.GetProperty("constraints").EnumerateArray().Where(row=>row.GetProperty("type").GetString()=="GeomFix").Take(3).Select(row=>new{id=row.GetProperty("id").GetString(),first=row.GetProperty("first")}).ToArray(),
                original_origin_admission=markers.OriginAdmission,current_origin_admission=currentOrigin.Admission,
                original_fixed_intents=markers.Relations.GetProperty("constraints").EnumerateArray().Where(row=>FixedPoint(row,Key(point.Id))).Take(3).Select(row=>FixedIntent(row,before)).ToArray(),
                current_fixed_intents=currentRelations.GetProperty("constraints").EnumerateArray().Where(row=>row.GetProperty("type").GetString()=="GeomFix").Take(3).Select(row=>FixedIntent(row,current)).ToArray()});
            if(markers.Origin is { } originalOrigin&&originalOrigin.Point==point.Id)
            {
                var origin=currentOrigin.Role;
                if(origin!=null&&origin.Point==id&&ModelFeatureReorderState345.Equivalent(originalOrigin.Intent,origin.Intent,cancel))
                {
                    if(ids.TryGetValue(originalOrigin.Relation,out var fixedId))Need(fixedId==origin.Relation,"The optional origin Fix correspondence is inconsistent.",new{phase,original_fix_id=Key(originalOrigin.Relation),current_fix_id=Key(origin.Relation)});else ids.Add(originalOrigin.Relation,origin.Relation);
                    if(originalOrigin.Relation!=origin.Relation){var row=new{original_id=Key(originalOrigin.Relation),current_id=Key(origin.Relation),marker=(long?)null,geometry_kind="GeomFix",role="optional_native_origin_fix_recreated",identity_basis="same_unique_actual_optional_origin_role_and_fixed_intent; not_same_object_GUID"};budget.Add(row);rows.Add(row);}
                }
            }
            if(!ids.ContainsKey(point.Id)){var row=new{original_id=Key(point.Id),current_id=Key(id),marker=(long?)null,geometry_kind="point",role="construction_anchor",identity_basis="same_exact_native_ID"};budget.Add(row);rows.Add(row);}
            ids.TryAdd(point.Id,id);
        }
        if(solverTransition&&!solved)
        {
            ValidatePendingSolverStructure(markers,current,currentRelations,manager,ids,cancel,phase,currentOrigin.Role);
            var pending=Wire.Element(new{phase,sketch_id=Key(current.Sketch.ID),identity_basis="actual_native_GS_member_bijection_and_typed_structural_multiset; endpoint_and_relation_correspondence_pending_until_after_Apply",items=rows,
                endpoint_intent="pre_setter_witness_retained; no_pending_point_is_classified_as_an_endpoint"});budget.Add(pending);
            return new(ids,pending,currentOrigin.Role);
        }
        var selectors=solverTransition?new List<ReferenceSelector>():null;
        var presentations=TransferRelations(markers,current,currentRelations,manager,ids,rows,cancel,phase,budget,currentOrigin.Role,solverTransition,solved,selectors);
        var receipt=selectors==null?Wire.Element(new{phase,sketch_id=Key(current.Sketch.ID),identity_basis="actual_unique_GS_marker_or_full_native_radial_binding; complete_retained_relation_intent; never_coordinate_or_name_search; no_permanent_marker_guarantee",items=rows}):
            Wire.Element(new{phase,sketch_id=Key(current.Sketch.ID),identity_basis="actual_unique_GS_marker_or_full_native_radial_binding; complete_retained_relation_intent; never_coordinate_or_name_search; no_permanent_marker_guarantee",items=rows,endpoint_selector_transfers=selectors.ToArray()});budget.Add(receipt);
        return new(ids,receipt,currentOrigin.Role,presentations,selectors?.ToArray());
    }

    static NativeSketchDimensionPresentation414.Transfer[] TransferRelations(Markers markers,NativeSketchMembers385.Snapshot current,JsonElement currentRelations,Constraints2D manager,
        Dictionary<McObjectId,McObjectId> ids,List<object> receipt,Action cancel,string phase,ModelInventoryBudget budget,OriginRole? currentOrigin,bool solverTransition=false,bool solved=true,List<ReferenceSelector>? selectors=null)
    {
        var currentRows=currentRelations.GetProperty("constraints").EnumerateArray().ToArray();var matched=new HashSet<McObjectId>();var presentations=new List<NativeSketchDimensionPresentation414.Transfer>();
        var reverse=manager.GetAllObjectsToConstraints();
        foreach(var previous in markers.Relations.GetProperty("constraints").EnumerateArray())
        {
            cancel();var original=markers.Before.Helpers.Single(id=>Same(Key(id),previous.GetProperty("id").GetString()));
            var mapping=new Mapping(ids,Wire.Element(new{}));var mappedInputs=Inputs(markers.Before,mapping,cancel);var expected=Identities(previous,mapping,cancel);
            JsonElement Intent(JsonElement row,NativeSketchMembers385.Snapshot inputs,bool source=false)
            {var design=JsonNode.Parse((solverTransition?SolverIntent(row,inputs,cancel,source?markers:null,source?previous:(JsonElement?)null):NativeSketchRelations410.Design(row,inputs)).GetRawText())!.AsObject();design["dimension"]=row.GetProperty("dimension").ValueKind==JsonValueKind.Null?null:"$actual_dimension_binding";return Wire.Element(design);}
            var intent=Intent(expected,mappedInputs,source:true);var witness=markers.PresentationWitnesses.SingleOrDefault(value=>value.Relation==original);
            bool Presentation(JsonElement row)=>witness!=null&&(solverTransition?NativeSketchDimensionPresentation414.CandidateForSolve(witness,previous,row,current,phase):NativeSketchDimensionPresentation414.Candidate(witness,previous,row,current,phase))&&
                ModelFeatureReorderState345.Equivalent(intent,NativeSketchDimensionPresentation414.WithoutDimension(Intent(row,current)),cancel);
            bool Matches(JsonElement row)=>ModelFeatureReorderState345.Equivalent(intent,Intent(row,current),cancel)||Presentation(row);
            var exact=currentRows.Where(row=>Same(row.GetProperty("id").GetString(),mapping.Map(Key(original)))).ToArray();
            var candidates=exact.Length==1?exact:currentRows.Where(Matches).ToArray();
            if(!(candidates.Length==1&&Matches(candidates[0])))
            {
                // A filtered empty candidate set cannot explain the actual
                // editor transition. Report complete already-observed native
                // designs and their differing fields, prioritizing the exact
                // relation/variable/type. No alternate manager or native read.
                // Keep the existing8KiB outer error budget useful by charging
                // this observed-design array by its actual serialized bytes.
                const int designBytes=4096;int used=2;var observations=new List<JsonElement>();
                var ordered=currentRows.OrderByDescending(row=>Same(row.GetProperty("id").GetString(),mapping.Map(Key(original))))
                    .ThenByDescending(row=>previous.GetProperty("variable").ValueKind==JsonValueKind.String&&Same(row.GetProperty("variable").GetString(),previous.GetProperty("variable").GetString()))
                    .ThenByDescending(row=>row.GetProperty("type").GetString()==previous.GetProperty("type").GetString());
                foreach(var row in ordered)
                {
                    cancel();var design=Intent(row,current);
                    var fields=intent.EnumerateObject().Select(field=>field.Name).Concat(design.EnumerateObject().Select(field=>field.Name)).Distinct(StringComparer.Ordinal);
                    var differences=fields.Where(field=>!intent.TryGetProperty(field,out var oldValue)||!design.TryGetProperty(field,out var actualValue)||!ModelFeatureReorderState345.Equivalent(oldValue,actualValue,cancel)).ToArray();
                    var observation=Wire.Element(new{constraint_id=row.GetProperty("id"),native_dimension_id=row.GetProperty("dimension"),
                        native_first=row.GetProperty("first"),native_second=row.GetProperty("second"),native_third=row.GetProperty("third"),design,differing_fields=differences});
                    int bytes=JsonSerializer.SerializeToUtf8Bytes(observation,Wire.Json).Length+1;
                    if(used+bytes>designBytes)continue;used+=bytes;observations.Add(observation);
                }
                throw new DomainException("SKETCH_CORRESPONDENCE_UNAVAILABLE","A prior relation lost its exact type, native operands, endpoint/fixed intent, dimension role or control during editor transfer.",false,new{
                    phase,original_constraint_id=Key(original),candidate_count=candidates.Length,previous_design=intent,
                    original_first=previous.GetProperty("first"),
                    actual_manager=new{document_id=currentRelations.GetProperty("document_id"),sketch_id=currentRelations.GetProperty("sketch_id"),scope_id=currentRelations.GetProperty("scope_id"),network=currentRelations.GetProperty("network"),
                        permanent_owner_document_id=current.Stored.GetProperty("owner_document_id"),member_context_document_id=current.Stored.GetProperty("member_context_document_id")},
                    actual_relation_count=currentRows.Length,actual_relation_designs=observations.ToArray(),observed_design_count=observations.Count,omitted_design_count=currentRows.Length-observations.Count,
                    actual_relations_sha256=Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(currentRelations,Wire.Json))),
                    design_scope="complete_already_observed_designs; actual_serialized4096byte_array_budget; no_further_native_read_or_admission_change"});
            }
            var currentRow=candidates[0];var actual=current.Helpers.Single(id=>Same(Key(id),currentRow.GetProperty("id").GetString()));
            Need(matched.Add(actual),"Two prior relations map to one actual current relation.",new{phase,original_constraint_id=Key(original),current_constraint_id=Key(actual)});
            using var info=manager.GetConstraintInfo(actual);Need(info!=null,"The transferred native relation has no live manager receipt.",new{phase,current_constraint_id=Key(actual)});bool nativeOriginReceiptUsed=false;
            foreach(var reference in new[]{info!.FirstObject,info.SecondObject,info.ThirdObject}.Where(reference=>reference!=null&&!reference.ObjectId.IsNull))
            {
                bool reverseBinding=reverse!=null&&reverse.Any(pair=>(pair.Key==reference!.ObjectId||current.Members.Any(member=>member.Id==pair.Key&&member.ObjectId==reference.ObjectId&&member.Segment==reference.PolylineSegmentIndex))&&pair.Value!=null&&pair.Value.Contains(actual));
                // The host can omit its fixed origin from auxiliary lists.
                // Its exact same-manager Fix receipt, selected point geometry
                // and ContainsObject already witness this one native operand.
                bool fixedOriginBinding=currentOrigin is {} origin&&origin.Relation==actual&&origin.Point==reference!.ObjectId&&FixedPoint(currentRow,Key(origin.Point));
                Need(reverseBinding||fixedOriginBinding,"The transferred relation lacks its exact current native operand/segment/relation binding.",new{phase,current_constraint_id=Key(actual),object_id=Key(reference!.ObjectId),same_manager_reverse_binding=reverseBinding,observed_native_origin_binding=fixedOriginBinding});
                nativeOriginReceiptUsed|=!reverseBinding&&fixedOriginBinding;
            }
            void Map(McObjectId source,McObjectId target)
            {
                if(ids.TryGetValue(source,out var mapped))Need(mapped==target,"A native relation/dimension correspondence is inconsistent.",new{phase,original_id=Key(source),current_id=Key(target)});
                else{Need(!ids.Values.Contains(target),"Native relation/dimension correspondence is not one-to-one.",new{phase,original_id=Key(source),current_id=Key(target)});ids.Add(source,target);}
            }
            Map(original,actual);
            if(solverTransition)
            {
                Need(info.VarId.IsNull== (previous.GetProperty("variable").ValueKind==JsonValueKind.Null)&&
                    (info.VarId.IsNull||Same(Key(info.VarId),previous.GetProperty("variable").GetString())),
                    "A solver transition changed the exact permanent dimensional variable identity.",new{phase,original_constraint_id=Key(original),current_constraint_id=Key(actual)});
                foreach(string field in new[]{"first","second","third"})
                {
                    cancel();var oldRef=previous.GetProperty(field);var newRef=currentRow.GetProperty(field);
                    if(oldRef.ValueKind!=JsonValueKind.Object||oldRef.GetProperty("subentity_id").ValueKind==JsonValueKind.Null&&oldRef.GetProperty("point").ValueKind!=JsonValueKind.Array)continue;
                    string? role=SourcePointRole(markers,previous,field,cancel);if(role==null)continue;
                    selectors!.Add(new(Key(original),Key(actual),field,oldRef.GetProperty("subentity_id").GetString(),newRef.GetProperty("subentity_id").GetString(),newRef.GetProperty("subentity_type").GetString(),role!));
                }
            }
            if(Presentation(currentRow))presentations.Add(solverTransition?NativeSketchDimensionPresentation414.EnterForSolve(witness!,actual,info,manager,current,currentRelations,cancel,phase):NativeSketchDimensionPresentation414.Enter(witness!,actual,info,manager,current,currentRelations,cancel));
            else if(witness!=null&&!current.Sketch.IsInEditMode)NativeSketchDimensionPresentation414.Persistent(witness,actual,info,current,cancel);
            if(previous.GetProperty("dimension").ValueKind==JsonValueKind.String)
            {
                var dimension=markers.Before.Helpers.Single(id=>Same(Key(id),previous.GetProperty("dimension").GetString()));
                Need(!info.DimId.IsNull&&current.Helpers.Contains(info.DimId),"A retained dimensional relation lost its actual native dimension binding.",new{phase,current_constraint_id=Key(actual)});Map(dimension,info.DimId);
            }
            var item=new{original_id=Key(original),current_id=Key(actual),marker=(long?)null,geometry_kind=currentRow.GetProperty("type").GetString(),role="retained_relation",identity_basis="unique_complete_native_relation_intent_and_exact_current_operand_binding; reverse_inventory_or_observed_fixed_origin_receipt; owner_variable_unchanged",fixed_origin_receipt_used=nativeOriginReceiptUsed};budget.Add(item);receipt.Add(item);
        }
        var final=new Mapping(ids,Wire.Element(new{}),Presentations:presentations.ToArray(),ReferenceSelectors:selectors?.ToArray());var finalInputs=Inputs(markers.Before,final,cancel);var baseline=NativeSketchRelations410.Capture(Relations(markers.Relations,final,cancel),finalInputs,cancel);
        if(!solverTransition)NativeSketchRelations410.Retained(baseline,currentRelations,current,cancel,allowNetworkTransfer:true);
        else
        {
            // Transfer above already proves each complete typed relation and
            // endpoint tuple bijectively. A solve adds no requested relations.
            NativeSketchRelations410.Capture(currentRelations,current,cancel);
            Need(matched.Count==currentRows.Length,"A native solver transition introduced or lost an unaccounted relation.",new{phase,prior_count=matched.Count,current_count=currentRows.Length});
            if(solved)NativeSketchMembers385.ValidateSmoothRelations(currentRelations,current,cancel);
        }
        return presentations.ToArray();
    }

    static string? SourcePointRole(Markers markers,JsonElement relation,string field,Action cancel)
    {
        cancel();var reference=relation.GetProperty(field);if(reference.ValueKind!=JsonValueKind.Object)return null;
        var rows=(markers.PointRoles??[]).Where(role=>Same(role.Relation,relation.GetProperty("id").GetString())&&role.Field==field).ToArray();
        Need(rows.Length==1&&Same(rows[0].ObjectId,reference.GetProperty("object_id").GetString())&&Same(rows[0].Selector,reference.GetProperty("subentity_id").GetString())&&rows[0].Segment==reference.GetProperty("segment").GetInt32(),
            "The pending native binding lacks its exact original pre-setter endpoint-intent witness.",new{constraint_id=relation.GetProperty("id"),field});
        return rows[0].Role;
    }
    static void ValidatePendingSolverStructure(Markers markers,NativeSketchMembers385.Snapshot current,JsonElement relations,Constraints2D manager,Dictionary<McObjectId,McObjectId> ids,Action cancel,string phase,OriginRole? origin)
    {
        // A pending generic Point pick can refer to the requested position
        // while the native curve is not yet solved (or conversely). Do not
        // decode it, assign relation IDs, or choose between similar rows.
        var mapping=new Mapping(ids,Wire.Element(new{}));var mappedInputs=Inputs(markers.Before,mapping,cancel);
        JsonElement Signature(JsonElement row,NativeSketchMembers385.Snapshot inputs,bool source)
        {
            JsonNode? Reference(JsonElement value)
            {if(value.ValueKind==JsonValueKind.Null)return null;return new JsonObject{["object_id"]=source?mapping.Map(value.GetProperty("object_id").GetString()!):value.GetProperty("object_id").GetString(),["segment"]=value.GetProperty("segment").GetInt32()};}
            var signature=new JsonObject{["type"]=row.GetProperty("type").GetString(),["dimensional"]=row.GetProperty("dimensional").GetBoolean(),["geometrical"]=row.GetProperty("geometrical").GetBoolean(),
                ["first"]=Reference(row.GetProperty("first")),["second"]=Reference(row.GetProperty("second")),["third"]=Reference(row.GetProperty("third")),["variable"]=row.GetProperty("variable").GetString()};
            NativeSketchRelations410.CanonicalSegments(signature,inputs);return Wire.Element(signature);
        }
        var before=markers.Relations.GetProperty("constraints").EnumerateArray().Select(row=>Signature(row,mappedInputs,true).GetRawText()).OrderBy(value=>value,StringComparer.Ordinal).ToArray();
        var after=relations.GetProperty("constraints").EnumerateArray().Select(row=>Signature(row,current,false).GetRawText()).OrderBy(value=>value,StringComparer.Ordinal).ToArray();
        Need(before.SequenceEqual(after),"The pending editor changed native constraint/member/control structure before its solve.",new{phase,prior_count=before.Length,current_count=after.Length});
        var reverse=manager.GetAllObjectsToConstraints();Need(reverse!=null,"The pending editor solver reverse inventory is unavailable.",new{phase});
        foreach(var relation in manager.GetAllConstraints()??throw new DomainException("CONSTRAINTS_UNAVAILABLE","The pending editor relation inventory disappeared."))
        {
            cancel();using var info=manager.GetConstraintInfo(relation);Need(info!=null,"A pending editor relation has no native receipt.",new{phase,constraint_id=Key(relation)});
            foreach(var reference in new[]{info.FirstObject,info.SecondObject,info.ThirdObject}.Where(value=>value!=null&&!value.ObjectId.IsNull))
            {
                bool bound=reverse.Any(pair=>(pair.Key==reference!.ObjectId||current.Members.Any(member=>member.Id==pair.Key&&member.ObjectId==reference.ObjectId&&member.Segment==reference.PolylineSegmentIndex))&&pair.Value!=null&&pair.Value.Contains(relation));
                bool nativeOrigin=origin!=null&&origin.Relation==relation&&origin.Point==reference!.ObjectId;
                Need(bound||nativeOrigin,"A pending native relation lacks its actual selected input/relation reverse binding.",new{phase,constraint_id=Key(relation),object_id=Key(reference!.ObjectId)});
            }
        }
    }
    internal static string? SolverPointRole(JsonElement relation,string field,NativeSketchMembers385.Snapshot inputs,Action cancel)
    {
        cancel();var reference=relation.GetProperty(field);
        if(reference.ValueKind!=JsonValueKind.Object||reference.GetProperty("subentity_id").ValueKind==JsonValueKind.Null&&reference.GetProperty("point").ValueKind!=JsonValueKind.Array)return null;
        string id=reference.GetProperty("object_id").GetString()!;int segment=reference.GetProperty("segment").GetInt32();
        if(reference.GetProperty("subentity_id").ValueKind==JsonValueKind.Null)
        {
            // A whole native construction point Fix and a typed single-curve
            // radial control do not select a curve endpoint. Their actual
            // identity/fixed WCS intent or radial binding is proved separately.
            if(relation.GetProperty("type").GetString()=="GeomFix"&&inputs.Construction.Any(point=>Same(Key(point.Id),id)))return null;
            if(field=="first"&&(relation.GetProperty("type").GetString() is "DimRadial" or "DimDiametrical")&&
                relation.GetProperty("second").ValueKind==JsonValueKind.Null&&relation.GetProperty("third").ValueKind==JsonValueKind.Null&&
                inputs.Members.Count(member=>Same(Key(member.Id),id)&&member.Id==member.ObjectId&&member.Segment==null&&(member.Curve.Kind is "circle" or "arc"))==1)return null;
        }
        var members=inputs.Members.Where(member=>Same(Key(member.Id),id)||Same(Key(member.ObjectId),id)&&(member.Segment==null||member.Segment==segment)).ToArray();
        Need(members.Length==1,"A solver reference lacks its exact member/segment before endpoint-role proof.",new{constraint_id=relation.GetProperty("id"),field,object_id=id});
        var role=NativeSketchMembers385.PointRole(inputs,members[0],reference);
        Need(role!=null,"An arbitrary native point is not a proved endpoint or center selector.",new{constraint_id=relation.GetProperty("id"),field,object_id=id});
        return role;
    }
    static JsonElement SolverIntent(JsonElement relation,NativeSketchMembers385.Snapshot inputs,Action cancel,Markers? source=null,JsonElement? original=null)
    {
        var result=JsonNode.Parse(NativeSketchRelations410.Design(relation,inputs).GetRawText())!.AsObject();
        foreach(string field in new[]{"first","second","third"})
        {
            var role=source==null?SolverPointRole(relation,field,inputs,cancel):SourcePointRole(source,original!.Value,field,cancel);if(role==null)continue;
            result[field]!["subentity_id"]="$native_endpoint_role:"+role;result[field]!["subentity_type"]=role;
        }
        // The SDK's dimensional descriptor can retain its previous cached
        // Param/VarExpression after the permanent owner variable was updated.
        // This solver-only comparison proves relation identity, not a second
        // expression/value oracle: exact owner variables are independently
        // retained and final callers verify requested values/formulas plus
        // every nondriver. A dimension without a real VarId keeps both checks.
        if(relation.GetProperty("dimensional").GetBoolean()&&relation.GetProperty("variable").ValueKind==JsonValueKind.String)
        {result["expression"]=null;result["value"]=null;}
        return Wire.Element(result);
    }

    static bool FixedPoint(JsonElement row,string pointId)=>row.GetProperty("type").GetString()=="GeomFix"&&row.GetProperty("first").ValueKind==JsonValueKind.Object&&
        Same(row.GetProperty("first").GetProperty("object_id").GetString(),pointId)&&row.GetProperty("second").ValueKind==JsonValueKind.Null&&row.GetProperty("third").ValueKind==JsonValueKind.Null&&
        row.GetProperty("variable").ValueKind==JsonValueKind.Null&&row.GetProperty("dimension").ValueKind==JsonValueKind.Null;
    static bool RadialBinding(JsonElement row,string objectId)=>(row.GetProperty("type").GetString() is "DimRadial" or "DimDiametrical")&&row.GetProperty("dimensional").GetBoolean()&&
        row.GetProperty("first").ValueKind==JsonValueKind.Object&&Same(row.GetProperty("first").GetProperty("object_id").GetString(),objectId)&&row.GetProperty("first").GetProperty("subentity_id").ValueKind==JsonValueKind.Null&&
        row.GetProperty("second").ValueKind==JsonValueKind.Null&&row.GetProperty("third").ValueKind==JsonValueKind.Null&&row.GetProperty("variable").ValueKind==JsonValueKind.String;
    internal static JsonElement OwnerVariable(NativeSketchMembers385.Snapshot inputs,McObjectId variable,Action cancel)
    {
        cancel();var owner=inputs.Sketch.DbEntity.Document;using var manager=owner.GetDocumentVariablesManager(Constraint2dNetType.MCS);
        Need(manager!=null&&manager.DocumentId==owner.ID&&manager.ConstraintsNetworkType==Constraint2dNetType.MCS,"The permanent sketch-owner MCS variable scope is unavailable for its retained radial binding.",new{sketch_id=Key(inputs.Sketch.ID),variable_id=Key(variable)});
        var inventory=manager.GetAllVariables();Need(inventory!=null&&inventory.Count(id=>id==variable)==1,"The retained radial variable is not unique in its actual permanent-owner MCS inventory.",new{sketch_id=Key(inputs.Sketch.ID),variable_id=Key(variable)});ModelInventoryBudget.Ids(inventory,cancel);
        var info=manager.GetVariableInfo(variable);Need(info!=null,"The exact retained radial variable metadata is unavailable.",new{sketch_id=Key(inputs.Sketch.ID),variable_id=Key(variable)});
        var host=manager.GetVariableHostObjectId(variable);bool available=manager.GetVariableValue(variable,out double value);
        Need(!available||double.IsFinite(value),"The retained radial variable has a nonfinite observed value.",new{sketch_id=Key(inputs.Sketch.ID),variable_id=Key(variable)});
        var result=Wire.Element(new{document_id=Key(owner.ID),variable_id=Key(variable),type=info.Type.ToString(),name=info.Name,expression=info.Expression,host_id=host.IsNull?null:Key(host),value_available=available,value=available?(double?)value:null});new ModelInventoryBudget(cancel).Add(result);return result;
    }
    static JsonElement FixedIntent(JsonElement relation,NativeSketchMembers385.Snapshot? inputs=null)
    {
        var row=JsonNode.Parse(NativeSketchRelations410.Design(relation,inputs).GetRawText())!.AsObject();row["first"]!["object_id"]=null;
        return Wire.Element(row);
    }

    // SDK3DCore.h documents an optional fixed construction point at the native
    // sketch origin, created on editor entry. Observe one selected-sketch point
    // and whole-point Fix at that native origin. The construction-only list is
    // auxiliary: this host omits that exact member while GetItemGeometry and
    // ContainsObject expose it through its real Fix reference. This is semantic
    // fixed-origin anchor replacement, never a
    // correspondence for arbitrary geometric objects at equal coordinates.
    static (OriginRole? Role,JsonElement Admission) ObserveOrigin(NativeSketchMembers385.Snapshot inputs,JsonElement relations,Action cancel,bool requireCleanMetadata=false)
    {
        int? onlyCount=null,pointCount=null,fixCount=null,objectCount=null,helperCount=null;double[]? nativeOrigin=null;string? candidateId=null,geometryKind=null;bool? memberCollision=null,contains=null,constructionListed=null;object? metadataFlags=null;
        JsonElement Admission(string reason)=>Wire.Element(new{reason,require_clean_metadata=requireCleanMetadata,construction_only_count=onlyCount,construction_only_member=constructionListed,observed_construction_count=inputs.Construction.Length,native_origin_wcs=nativeOrigin,origin_point_count=pointCount,candidate_id=candidateId,geometry_kind=geometryKind,member_collision=memberCollision,contains_object=contains,fixed_relation_count=fixCount,object_state_count=objectCount,helper_identity_count=helperCount,metadata=metadataFlags,
            membership_basis="actual_same_manager_whole_point_Fix_reference; selected_sketch_GetItemGeometry_and_ContainsObject; auxiliary_construction_list_not_authoritative"});
        var ids=inputs.Sketch.GetContainingObjects(false,false,true);if(ids!=null){ModelInventoryBudget.Ids(ids,cancel);onlyCount=ids.Count();}
        var origin=inputs.Sketch.TransformPointSketchToWorld(Multicad.Geometry.Point3d.Origin);
        if(!double.IsFinite(origin.X)||!double.IsFinite(origin.Y)||!double.IsFinite(origin.Z))return(null,Admission("native_origin_nonfinite"));nativeOrigin=[origin.X,origin.Y,origin.Z];
        var points=inputs.Construction.Where(point=>
        {
            cancel();var p=point.Geometry.GetProperty("point");return NativeSketchRelations410.SamePoint(new(p[0].GetDouble(),p[1].GetDouble(),p[2].GetDouble()),origin);
        }).ToArray();
        pointCount=points.Length;if(points.Length!=1)return(null,Admission("construction_origin_not_unique"));var anchor=points[0];candidateId=Key(anchor.Id);constructionListed=ids==null?null:ids.Contains(anchor.Id);
        memberCollision=inputs.Members.Any(member=>member.Id==anchor.Id||member.ObjectId==anchor.Id);if(memberCollision.Value)return(null,Admission("origin_is_ordinary_or_projected_member"));
        using var geometry=inputs.Sketch.GetItemGeometry(anchor.Id,false);
        contains=inputs.Sketch.ContainsObject(anchor.Id);geometryKind=geometry?.GeometryType.ToString();
        if(!contains.Value||geometry==null||!NativeSketchMembers385.IsPoint(geometry)||!NativeSketchRelations410.SamePoint(geometry.Point,origin))return(null,Admission("current_native_origin_point_not_verified"));
        var fixedRows=relations.GetProperty("constraints").EnumerateArray().Where(row=>FixedPoint(row,Key(anchor.Id))).ToArray();fixCount=fixedRows.Length;if(fixedRows.Length!=1)return(null,Admission("origin_whole_point_fix_not_unique"));
        // Meaningful resident user data cannot disappear under a service-role
        // receipt. The final rich snapshot also retains all observed metadata.
        var objects=inputs.Stored.GetProperty("objects").EnumerateArray().Where(row=>Same(row.GetProperty("id").GetString(),Key(anchor.Id))).ToArray();
        objectCount=objects.Length;if(objects.Length!=1)return(null,Admission("origin_object_metadata_not_unique"));
        foreach(var row in objects)
        {
            var metadata=row.GetProperty("native_metadata");
            bool absent=row.TryGetProperty("resident_native_object_absent",out var residence)&&residence.GetBoolean();string? metadataScope=metadata.TryGetProperty("scope",out var observedScope)?observedScope.GetString():null;
            metadataFlags=new{resident_native_object_absent=absent,scope=metadataScope};
            if(!requireCleanMetadata)continue;
            if(absent)continue;
            if(metadata.TryGetProperty("scope",out var scope)&&scope.GetString()=="exact_resident_DWG_owned_metadata")
            {
                var stored=metadata.GetProperty("metadata");var root=stored.GetProperty("root");
                bool fieldsAvailable=root.TryGetProperty("HasFields",out var hasFields);bool? fields=fieldsAvailable?hasFields.GetBoolean():null;int ownedCount=stored.GetProperty("owned").GetArrayLength(),reactors=root.GetProperty("reactors").GetArrayLength();bool xdata=root.GetProperty("xdata").ValueKind!=JsonValueKind.Null,extension=root.GetProperty("extension_handle").ValueKind!=JsonValueKind.Null;
                metadataFlags=new{resident_native_object_absent=false,scope=metadataScope,fields_available=fieldsAvailable,has_fields=fields,owned_count=ownedCount,reactor_count=reactors,has_xdata=xdata,has_extension=extension};
                if(!fieldsAvailable||fields==true||ownedCount!=0||reactors!=0||xdata||extension)return(null,Admission("origin_has_observed_user_data_or_unknown_resident_fields"));
            }
            else return(null,Admission("origin_resident_user_metadata_unavailable"));
        }
        var relationId=fixedRows[0].GetProperty("id").GetString()!;
        // The input helper IDs came from this same actual complete manager
        // inventory. Never reacquire a different manager or invent a role GUID.
        var matches=inputs.Helpers.Where(id=>Same(Key(id),relationId)).ToArray();helperCount=matches.Length;if(matches.Length!=1)return(null,Admission("origin_fix_native_helper_identity_not_unique"));
        return(new(anchor.Id,matches[0],FixedIntent(fixedRows[0],inputs)),Admission("observed_optional_origin_role"));
    }

    internal static SketchConstraintSpec Spec(SketchConstraintSpec spec,Mapping mapping)=>spec with{First=mapping.Map(spec.First),Second=spec.Second==null?null:mapping.Map(spec.Second),Third=spec.Third==null?null:mapping.Map(spec.Third)};
    internal static Mapping Transition(Mapping from,Mapping to)
    {
        var ids=from.Ids.ToDictionary(pair=>pair.Value,pair=>to.Map(pair.Key));
        if(from.ServiceOrigin is {} previous&&to.ServiceOrigin is {} current)
        {
            Need(ModelFeatureReorderState345.Equivalent(previous.Intent,current.Intent,()=>{}),"The native optional editor-origin fixed intent changed during network transfer.",new{stage="editor_origin_transfer",previous=previous.Intent,current=current.Intent});
            void Map(McObjectId source,McObjectId target)
            {if(ids.TryGetValue(source,out var mapped))Need(mapped==target,"The native optional origin correspondence is inconsistent.",new{original_id=Key(source),current_id=Key(target)});else{Need(!ids.Values.Contains(target),"The native optional origin correspondence is not one-to-one.",new{original_id=Key(source),current_id=Key(target)});ids.Add(source,target);}}
            Map(previous.Point,current.Point);Map(previous.Relation,current.Relation);
        }
        var presentations=new List<NativeSketchDimensionPresentation414.Transfer>();
        foreach(var entry in from.Presentations??[])
        {
            Need(entry.SourceDimension.IsNull&&!entry.TargetDimension.IsNull&&(to.Presentations??[]).All(value=>value.SourceRelation!=entry.SourceRelation)&&
                ids.TryGetValue(entry.TargetRelation,out var target)&&target==to.Map(entry.SourceRelation),
                "The editor-only radial presentation lacks its strictly restored persistent relation correspondence.",new{stage="editor_dimension_presentation_exit",original_constraint_id=Key(entry.SourceRelation),editor_constraint_id=Key(entry.TargetRelation),editor_dimension_id=Key(entry.TargetDimension)});
            presentations.Add(new(entry.TargetRelation,to.Map(entry.SourceRelation),entry.TargetDimension,McObjectId.Null,entry.Variable));
        }
        return new(ids,Wire.Element(new{from=from.Receipt,to=to.Receipt}),to.ServiceOrigin,presentations.ToArray());
    }
    internal static JsonElement Relations(JsonElement source,Mapping mapping,Action cancel)
        =>NativeSketchDimensionPresentation414.Apply(Identities(source,mapping,cancel),mapping.Presentations??[],cancel);
    internal static NativeSketchMembers385.Snapshot Inputs(NativeSketchMembers385.Snapshot before,Mapping mapping,Action cancel)
    {
        var members=before.Members.Select(m=>m with{Id=mapping.Map(m.Id),ObjectId=mapping.Map(m.ObjectId),Curve=m.Curve with{Id=Key(mapping.Map(m.Id))}}).ToArray();
        return before with{Members=members,Stored=Identities(before.Stored,mapping,cancel),Construction=before.Construction.Select(p=>p with{Id=mapping.Map(p.Id)}).ToArray(),Helpers=before.Helpers.Select(mapping.Map).ToArray()};
    }

    // Rewrite only exact native identity fields/sets through the witnessed map.
    // Numeric geometry, formulas, names, resident metadata and opaque user data
    // remain untouched. Ordered geometric/topological arrays remain ordered.
    internal static JsonElement Identities(JsonElement source,Mapping mapping,Action cancel)
    {
        var root=JsonNode.Parse(source.GetRawText())!;
        var fields=new HashSet<string>(StringComparer.Ordinal){"id","mc_id","native_id","source_id","object_id","member_id","element_id","constraint_id","variable","dimension","host_id","host_object_id","source_object_id"};
        var sets=new HashSet<string>(StringComparer.Ordinal){"containing_ids","dependencies","stable_dependencies","sketch_dependencies","native_source_ids","member_ids","projection_ids"};
        var rows=new HashSet<string>(StringComparer.Ordinal){"members","objects","construction","nodes","constraint_helpers","constraints"};
        void Walk(JsonNode node,string? key=null)
        {
            cancel();if(node is JsonObject obj)
            {
                string? originalRelation=obj["id"] is JsonValue relationId&&relationId.TryGetValue<string>(out var relationText)?relationText:null;
                foreach(var field in obj.ToArray())
                {
                    if(field.Value==null||field.Key is "native_metadata" or "metadata" or "data" or "expression")continue;
                    if(fields.Contains(field.Key)&&field.Value is JsonValue value&&value.TryGetValue<string>(out var text)&&text!=null)obj[field.Key]=mapping.Map(text);
                    else Walk(field.Value,field.Key);
                }
                if(originalRelation!=null)foreach(var selector in mapping.ReferenceSelectors??[])
                {
                    if(!Same(originalRelation,selector.OriginalRelation)&&!Same(originalRelation,selector.CurrentRelation))continue;
                    if(obj[selector.Field] is not JsonObject reference)continue;
                    string? actual=reference["subentity_id"]?.GetValue<string>();
                    Need(Same(actual,selector.OriginalSelector)||Same(actual,selector.CurrentSelector),"A local expected selector does not match its witnessed original/current native reference.",new{constraint_id=originalRelation,selector.Field});
                    reference["subentity_id"]=selector.CurrentSelector;if(reference.ContainsKey("subentity_type"))reference["subentity_type"]=selector.CurrentSubtype;
                }
                // SketchGeometryReader emits an ID-sorted inventory. Native
                // editor renewal changes that order, while profile/segment
                // arrays remain genuinely ordered and must never be sorted.
                if(obj["kind"]?.GetValue<string>()=="sketch"&&obj["identity_scope"]?.GetValue<string>()=="bridge_document_revision"&&obj["elements"] is JsonArray inventory)
                {
                    var entries=inventory.Select(value=>value?.AsObject()).ToArray();
                    Need(entries.All(value=>value?["element_id"] is JsonValue id&&id.TryGetValue<string>(out var text)&&!string.IsNullOrEmpty(text)),"A native sketch inventory lacks exact element identities.",new{component="native_sketch_inventory",field="element_id"});
                    var identities=entries.Select(value=>value!["element_id"]!.GetValue<string>()).ToArray();
                    Need(identities.Distinct(StringComparer.OrdinalIgnoreCase).Count()==identities.Length,"A native sketch inventory repeats an element identity.",new{component="native_sketch_inventory",field="element_id"});
                    var ordered=entries.OrderBy(value=>value!["element_id"]!.GetValue<string>(),StringComparer.Ordinal).Select(value=>JsonNode.Parse(value!.ToJsonString())).ToArray();
                    inventory.Clear();foreach(var value in ordered)inventory.Add(value);
                }
            }
            else if(node is JsonArray array)
            {
                if(key!=null&&sets.Contains(key))
                {
                    var values=array.Select(value=>mapping.Map(value!.GetValue<string>())).OrderBy(value=>value,StringComparer.OrdinalIgnoreCase).ToArray();array.Clear();foreach(var value in values)array.Add(value);return;
                }
                foreach(var value in array)if(value!=null)Walk(value);
                if(key!=null&&rows.Contains(key))
                {
                    var ordered=array.Select(value=>value!).OrderBy(value=>value["mc_id"]?.GetValue<string>()??value["member_id"]?.GetValue<string>()??value["id"]?.GetValue<string>(),StringComparer.OrdinalIgnoreCase).Select(value=>JsonNode.Parse(value.ToJsonString())).ToArray();array.Clear();foreach(var value in ordered)array.Add(value);
                }
            }
        }
        Walk(root);return Wire.Element(root);
    }
}
