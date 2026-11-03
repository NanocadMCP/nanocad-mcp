using System.Text.Json;
using System.Text.Json.Nodes;
using Multicad;
using NanoCAD.Contracts;

namespace NanoCADBridge;

// Per-operation expected state, translated only through a completed live native
// editor proof. It never substitutes the fresh post-write witness for the
// original geometry, driver values, formulas, user metadata or foreign links.
internal static class NativeSketchSolveExpected445
{
    static void Need([System.Diagnostics.CodeAnalysis.DoesNotReturnIf(false)] bool ok,string message)
    {if(!ok)throw new DomainException("SKETCH_CORRESPONDENCE_UNAVAILABLE",message);}
    static string Key(McObjectId id)=>id.ToString();
    static bool Same(string? a,string? b)=>string.Equals(a,b,StringComparison.OrdinalIgnoreCase);
    static NativeSketchEditorSolve445.Transition[] Scoped(IEnumerable<NativeSketchEditorSolve445.Transition> transitions,McObjectId body)
        =>transitions.Where(value=>value.BeforeInputs.Sketch.HostSolidID==body).ToArray();
    internal static NativeSketchCorrespondence410.Mapping Mapping(IEnumerable<NativeSketchEditorSolve445.Transition> transitions,McObjectId bodyId,Action cancel)
    {
        var ids=new Dictionary<McObjectId,McObjectId>();var selectors=new List<NativeSketchCorrespondence410.ReferenceSelector>();
        var presentations=new List<NativeSketchDimensionPresentation414.Transfer>();var receipts=new List<JsonElement>();
        foreach(var value in Scoped(transitions,bodyId))
        {
            cancel();Need(value.SketchId==value.BeforeInputs.Sketch.ID&&value.Mapping.Map(value.SketchId)==value.SketchId&&value.Mapping.Map(bodyId)==bodyId,
                "A solver transition cannot substitute its permanent body or selected sketch identity.");
            foreach(var variable in value.BeforeRelations.GetProperty("constraints").EnumerateArray().Where(row=>row.GetProperty("variable").ValueKind==JsonValueKind.String))
                Need(Same(value.Mapping.Map(variable.GetProperty("variable").GetString()!),variable.GetProperty("variable").GetString()),"A solver transition cannot replace a permanent control variable.");
            foreach(var pair in value.Mapping.Ids)
            {
                cancel();if(ids.TryGetValue(pair.Key,out var actual))Need(actual==pair.Value,"Two live solver transitions disagree on an expected native identity.");
                else{Need(!ids.Any(old=>old.Key!=pair.Key&&old.Value==pair.Value),"Expected solver identities do not form a one-to-one native mapping.");ids.Add(pair.Key,pair.Value);}
            }
            selectors.AddRange(value.Mapping.ReferenceSelectors??[]);presentations.AddRange(value.Mapping.Presentations??[]);receipts.Add(value.Receipt);
        }
        return new(ids,Wire.Element(new{scope="operation_local_native_editor_solver_correspondence",transitions=receipts}),Presentations:presentations.ToArray(),ReferenceSelectors:selectors.ToArray());
    }
    static JsonElement ReferenceIdentity(JsonElement reference)
    {
        if(reference.ValueKind==JsonValueKind.Null)return JsonSerializer.SerializeToElement<object?>(null,Wire.Json);
        return Wire.Element(new{object_id=reference.GetProperty("object_id"),subentity_id=reference.GetProperty("subentity_id"),segment=reference.GetProperty("segment"),finite_pick_present=reference.GetProperty("point").ValueKind==JsonValueKind.Array});
    }
    static JsonElement RelationIdentity(JsonElement row)
    {
        JsonElement Property(string a,string b)=>row.TryGetProperty(a,out var value)?value:row.GetProperty(b);
        return Wire.Element(new{type=row.GetProperty("type"),first=ReferenceIdentity(row.GetProperty("first")),second=ReferenceIdentity(row.GetProperty("second")),third=ReferenceIdentity(row.GetProperty("third")),
            variable=Property("variable","variable_id"),dimension=Property("dimension","dimension_id"),name=Property("name","variable_name")});
    }
    static NativeSketchMembers385.Snapshot OriginalInputs(JsonElement geometry,NativeSketchMembers385.Snapshot fresh,Action cancel)
    {
        Point3 Point(JsonElement value)=>new(value[0].GetDouble(),value[1].GetDouble(),value[2].GetDouble());
        var frame=geometry.GetProperty("frame");var plane=new SketchPlaneSpec(Point(frame.GetProperty("origin_wcs")),Point(frame.GetProperty("x_step_wcs")),Point(frame.GetProperty("y_step_wcs")));
        var curves=NativeSketchGeometry385.Curves(geometry,plane,cancel);var elements=geometry.GetProperty("elements").EnumerateArray().ToDictionary(row=>row.GetProperty("element_id").GetString()!,StringComparer.OrdinalIgnoreCase);
        var members=new List<NativeSketchMembers385.Member>();
        foreach(var member in fresh.Members)
        {
            cancel();Need(elements.TryGetValue(Key(member.ObjectId),out var element),"An original endpoint role lacks its exact saved native element.");
            Need(element.GetProperty("geometry_type").GetString()==member.NativeType,"The original native element type changed before its endpoint-role witness.");
            var native=element.GetProperty("geometry");string curveId=Key(member.ObjectId);
            if(member.Segment is int segment)
            {
                Need(native.GetProperty("kind").GetString()=="polyline","An original native segment lacks its actual saved polyline parent.");
                var parts=native.GetProperty("segments").EnumerateArray().Where(row=>row.GetProperty("index").GetInt32()==segment).ToArray();
                Need(parts.Length==1,"An original native segment lacks its exact saved segment index.");native=parts[0].GetProperty("geometry");curveId+="/boundary/"+segment;
            }
            var selected=curves.Where(curve=>Same(curve.Id,curveId)).ToArray();Need(selected.Length==1,"An original endpoint role lacks its complete exact native curve.");
            // Comparison only: the ID/parent/segment was already proved equal
            // across original and fresh inventories. Geometry is the original
            // saved native observation, never the current post-write curve.
            members.Add(member with{Geometry=native,Curve=selected[0] with{Id=Key(member.Id)}});
        }
        return fresh with{Plane=plane,Members=members.ToArray()};
    }
    internal static NativeSketchCorrespondence410.PointRoleWitness[] PointRoles(NativeSketchConstraintState385.Snapshot original,NativeSketchMembers385.Snapshot fresh,JsonElement freshRelations,Action cancel)
    {
        cancel();var sketchId=fresh.Sketch.ID;var roles=new List<NativeSketchCorrespondence410.PointRoleWitness>();
        Need(original.Part.Body.ID==fresh.Sketch.HostSolidID&&original.Part.Body.DbEntity.Document?.ID==fresh.Sketch.DbEntity.Document?.ID,
            "The original expected part does not own the live pre-editor witness.");
        var rows=original.Part.Preserved.GetProperty("sketches").EnumerateArray().Where(row=>Same(row.GetProperty("id").GetString(),Key(sketchId))).ToArray();
        Need(rows.Length==1,"The original expected design lacks its exact selected solver sketch.");
        var geometry=rows[0].GetProperty("geometry");Need(geometry.GetProperty("geometry_details_complete").GetBoolean(),"The original expected sketch geometry inventory is incomplete.");
        var originalIds=geometry.GetProperty("elements").EnumerateArray().Select(row=>row.GetProperty("element_id").GetString()!).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var currentIds=fresh.Members.Select(member=>Key(member.ObjectId)).ToHashSet(StringComparer.OrdinalIgnoreCase);
        Need(originalIds.SetEquals(currentIds),"Native inputs changed identity before the live editor witness; no prior ID alias is inferred.");
        Need(original.Part.Constraints.TryGetValue(Key(sketchId),out var observed),"The original expected sketch has no retained native relation inventory.");
        var priorRows=observed.GetProperty("constraints").EnumerateArray().ToDictionary(row=>row.GetProperty("id").GetString()!,StringComparer.OrdinalIgnoreCase);
        var freshRows=freshRelations.GetProperty("constraints").EnumerateArray().ToDictionary(row=>row.GetProperty("id").GetString()!,StringComparer.OrdinalIgnoreCase);
        Need(priorRows.Keys.ToHashSet(StringComparer.OrdinalIgnoreCase).SetEquals(freshRows.Keys),"Native relation identities changed before the live editor witness.");
        var originalInputs=original.Inputs.Sketch.ID==sketchId?original.Inputs:OriginalInputs(geometry,fresh,cancel);
        foreach(var pair in priorRows)
        {
            cancel();var current=freshRows[pair.Key];Need(ModelParameterSnapshot.Equivalent(RelationIdentity(pair.Value),RelationIdentity(current)),
                "A relation changed its exact native type, operands, selector IDs, segment, control variable, annotation or name before the live editor witness.");
            var prior=JsonNode.Parse(pair.Value.GetRawText())!.AsObject();
            foreach(string field in new[]{"first","second","third"})
            {
                // Legacy retained observations omit the decoded subtype. The
                // exact unchanged native SubentGUID above permits the current
                // native subtype decode for that same selector. Original and
                // fresh picks are interpreted in their own observed geometry.
                if(prior[field] is JsonObject reference&&!reference.ContainsKey("subentity_type"))
                    reference["subentity_type"]=current.GetProperty(field).GetProperty("subentity_type").GetString();
            }
            var previous=Wire.Element(prior);
            foreach(string field in new[]{"first","second","third"})
            {
                var reference=previous.GetProperty(field);if(reference.ValueKind!=JsonValueKind.Object)continue;
                // Derive semantic intent ONLY from the coherent pre-setter
                // geometry/pick. The post-setter native reference is pending;
                // its raw ID binding above is verified, not its WCS endpoint.
                var role=NativeSketchCorrespondence410.SolverPointRole(previous,field,originalInputs,cancel);
                roles.Add(new(pair.Key,field,reference.GetProperty("object_id").GetString()!,reference.GetProperty("subentity_id").GetString(),reference.GetProperty("segment").GetInt32(),role));
            }
        }
        // This identity bridge deliberately excludes numeric descriptor values,
        // expressions and moving WCS picks: the existing graph/literal guards
        // independently authorize the requested change. None is copied into the
        // expected state; every original value/formula/anchor remains retained.
        if(original.Inputs.Sketch.ID==sketchId)
        {
            var a=original.Inputs.Members.Select(member=>(member.Id,member.ObjectId,member.Segment,member.NativeType,member.Owned,member.ReferenceOnly)).ToHashSet();
            var b=fresh.Members.Select(member=>(member.Id,member.ObjectId,member.Segment,member.NativeType,member.Owned,member.ReferenceOnly)).ToHashSet();
            Need(a.SetEquals(b),"The original input identity/type/segment/ownership roles changed before the live editor witness.");
            var oldRelations=original.Relations.GetProperty("constraints").EnumerateArray().Select(row=>row.GetProperty("id").GetString()!).ToHashSet(StringComparer.OrdinalIgnoreCase);
            Need(oldRelations.SetEquals(freshRelations.GetProperty("constraints").EnumerateArray().Select(row=>row.GetProperty("id").GetString()!)),"The original relation identities changed before the live editor witness.");
        }
        return roles.OrderBy(role=>role.Relation,StringComparer.OrdinalIgnoreCase).ThenBy(role=>role.Field,StringComparer.Ordinal).ToArray();
    }
    static void Bridge(NativeSketchConstraintState385.Snapshot original,NativeSketchEditorSolve445.Transition transition,Action cancel)
    {
        var expected=PointRoles(original,transition.BeforeInputs,transition.BeforeRelations,cancel);
        Need(expected.SequenceEqual(transition.PointRoles),"The completed native solver transition does not retain this original pre-setter endpoint intent.");
    }
    // PolylineSegmentIndex is meaningful only for a polyline (SDK docs).
    // Canonicalize this auxiliary sentinel only in comparison copies of a
    // completed transition's exact native whole-curve member observations.
    internal static NativeSketchConstraintState385.Snapshot CanonicalComparisons(NativeSketchConstraintState385.Snapshot observed,IEnumerable<NativeSketchEditorSolve445.Transition> transitions,Action cancel)
    {
        var scoped=Scoped(transitions,observed.Part.Body.ID);if(scoped.Length==0)return observed;
        var constraints=new Dictionary<string,JsonElement>(observed.Part.Constraints,StringComparer.OrdinalIgnoreCase);
        foreach(var transition in scoped)
        {
            cancel();string key=Key(transition.SketchId);
            Need(constraints.TryGetValue(key,out var observation),"A completed native transition lacks its exact retained constraint observation.");
            var inputs=NativeSketchCorrespondence410.Inputs(transition.BeforeInputs,transition.Mapping,cancel);
            Need(Same(observation.GetProperty("sketch_id").GetString(),key)&&Same(observation.GetProperty("document_id").GetString(),inputs.Stored.GetProperty("owner_document_id").GetString()),
                "Constraint sentinel comparison does not identify the completed transition's actual sketch and owner.");
            var sketches=observed.Part.Preserved.GetProperty("sketches").EnumerateArray().Where(row=>Same(row.GetProperty("id").GetString(),key)).ToArray();
            Need(sketches.Length==1&&sketches[0].GetProperty("geometry").GetProperty("geometry_details_complete").GetBoolean(),"The comparison lacks the exact complete observed native sketch inventory.");
            var elements=sketches[0].GetProperty("geometry").GetProperty("elements").EnumerateArray().ToDictionary(row=>row.GetProperty("element_id").GetString()!,StringComparer.OrdinalIgnoreCase);
            Need(elements.Keys.ToHashSet(StringComparer.OrdinalIgnoreCase).SetEquals(inputs.Members.Select(member=>Key(member.ObjectId))),"Constraint sentinel comparison lost the completed native member identity set.");
            foreach(var member in inputs.Members)
            {cancel();Need(elements[Key(member.ObjectId)].GetProperty("geometry_type").GetString()==member.NativeType,"Constraint sentinel comparison changed an actual native member type; a polyline is never inferred to be a whole primitive.");}
            var copy=JsonNode.Parse(observation.GetRawText())!.AsObject();
            if(copy["constraints"] is JsonArray rows)
            {
                foreach(var row in rows)
                {cancel();if(row is JsonObject relation)NativeSketchRelations410.CanonicalSegments(relation,inputs);}
                constraints[key]=Wire.Element(copy);
            }
            // Unavailable observations stay unavailable; no empty network or
            // metadata/role/numeric/pick/real-polyline-index claim is invented.
        }
        return observed with{Part=observed.Part with{Constraints=constraints}};
    }
    internal static JsonElement Body(ModelParameterBody body,IEnumerable<NativeSketchEditorSolve445.Transition> transitions,Action cancel)
    {
        var scoped=Scoped(transitions,body.Body.ID);if(scoped.Length==0)return body.Before;
        // Only native identity fields/sets are rewritten. Identities explicitly
        // excludes metadata, data, expressions and arbitrary string values.
        return NativeSketchCorrespondence410.Identities(body.Before,Mapping(scoped,body.Body.ID,cancel),cancel);
    }
    internal static NativeSketchConstraintState385.Snapshot Snapshot(NativeSketchConstraintState385.Snapshot before,IEnumerable<NativeSketchEditorSolve445.Transition> transitions,Action cancel)
    {
        var scoped=Scoped(transitions,before.Part.Body.ID);if(scoped.Length==0)return before;
        foreach(var value in scoped)Bridge(before,value,cancel);
        var map=Mapping(scoped,before.Part.Body.ID,cancel);
        var part=before.Part with
        {
            Contents=before.Part.Contents.Select(map.Map).ToArray(),Features=before.Part.Features.Select(map.Map).ToArray(),
            Previous=before.Part.Previous.ToDictionary(pair=>map.Map(pair.Key),pair=>pair.Value==null?null:map.Map(pair.Value),StringComparer.OrdinalIgnoreCase),
            Preserved=NativeSketchCorrespondence410.Identities(before.Part.Preserved,map,cancel),
            Constraints=before.Part.Constraints.ToDictionary(pair=>map.Map(pair.Key),pair=>NativeSketchCorrespondence410.Identities(pair.Value,map,cancel),StringComparer.OrdinalIgnoreCase)
        };
        var sketch=before.Sketch with
        {
            Geometry=NativeSketchCorrespondence410.Identities(before.Sketch.Geometry,map,cancel),Elements=before.Sketch.Elements.Select(map.Map).ToArray(),Containing=before.Sketch.Containing.Select(map.Map).ToArray(),
            Constraints=NativeSketchCorrespondence410.Identities(before.Sketch.Constraints,map,cancel),Profiles=NativeSketchCorrespondence410.Identities(before.Sketch.Profiles,map,cancel),Stored=NativeSketchCorrespondence410.Identities(before.Sketch.Stored,map,cancel)
        };
        return before with
        {
            Part=part,Sketch=sketch,Inputs=NativeSketchCorrespondence410.Inputs(before.Inputs,map,cancel),Relations=NativeSketchCorrespondence410.Relations(before.Relations,map,cancel),
            Protected=NativeSketchCorrespondence410.Identities(before.Protected,map,cancel),Storage=before.Storage.Select(binding=>binding with{native_id=map.Map(binding.native_id)}).ToArray(),
            GraphRoots=before.GraphRoots.Select(map.Map).ToArray(),Affected=before.Affected.Select(map.Map).ToHashSet(StringComparer.OrdinalIgnoreCase)
        };
    }
    internal static ModelSketchParameter411.Control Control(ModelSketchParameter411.Control before,IEnumerable<NativeSketchEditorSolve445.Transition> transitions,Action cancel)
    {
        var scoped=Scoped(transitions,before.State.Part.Body.ID);if(scoped.Length==0)return before;
        var map=Mapping(scoped,before.State.Part.Body.ID,cancel);Need(map.Map(before.Variable)==before.Variable,"The exact parameter graph driver variable changed identity.");
        return before with{State=Snapshot(before.State,scoped,cancel),Constraint=map.Map(before.Constraint),Spec=NativeSketchCorrespondence410.Spec(before.Spec,map)};
    }
    internal static NativeSketchConstraintState385.Control Literal(NativeSketchConstraintState385.Control before,IEnumerable<NativeSketchEditorSolve445.Transition> transitions,Action cancel)
    {
        var scoped=Scoped(transitions,before.State.Part.Body.ID);if(scoped.Length==0)return before;
        var map=Mapping(scoped,before.State.Part.Body.ID,cancel);Need(map.Map(before.Variable)==before.Variable&&map.Map(before.ParameterVariable)==before.ParameterVariable,"The exact literal driver variable changed identity.");
        return before with{State=Snapshot(before.State,scoped,cancel),Constraint=map.Map(before.Constraint),Member=map.Map(before.Member),VariableState=NativeSketchCorrespondence410.Identities(before.VariableState,map,cancel)};
    }
}
