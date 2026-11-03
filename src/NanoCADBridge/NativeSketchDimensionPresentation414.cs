using System.Text.Json;
using System.Text.Json.Nodes;
using Multicad;
using Multicad.DatabaseServices;
using Multicad.DatabaseServices.Constraints;
using Multicad.Dimensions;
using NanoCAD.Contracts;

namespace NanoCADBridge;

// A null persistent DimId can acquire a real radial annotation on editor
// entry. This is a witnessed presentation transition for one existing driver,
// not permission to disregard dimension presence in a relation's design.
internal static class NativeSketchDimensionPresentation414
{
    internal sealed record Witness(McObjectId Relation,McObjectId Variable,JsonElement OwnerVariable);
    internal sealed record Transfer(McObjectId SourceRelation,McObjectId TargetRelation,McObjectId SourceDimension,McObjectId TargetDimension,McObjectId Variable);
    static string Key(McObjectId id)=>id.ToString();
    static bool Same(string? a,string? b)=>string.Equals(a,b,StringComparison.OrdinalIgnoreCase);
    static void Need([System.Diagnostics.CodeAnalysis.DoesNotReturnIf(false)] bool valid,string message,object details)
    {if(!valid)throw new DomainException("SKETCH_CORRESPONDENCE_UNAVAILABLE",message,false,details);}
    internal static Witness[] Capture(NativeSketchMembers385.Snapshot inputs,JsonElement relations,Action cancel)
    {
        var result=new List<Witness>();var budget=new ModelInventoryBudget(cancel);
        foreach(var row in relations.GetProperty("constraints").EnumerateArray())
        {
            cancel();if(row.GetProperty("dimension").ValueKind!=JsonValueKind.Null||!WholeRadial(row,inputs))continue;
            var relation=inputs.Helpers.Single(id=>Same(Key(id),row.GetProperty("id").GetString()));
            var variable=inputs.Helpers.Single(id=>Same(Key(id),row.GetProperty("variable").GetString()));
            Need(relations.GetProperty("constraints").EnumerateArray().Count(item=>Same(item.GetProperty("variable").GetString(),Key(variable)))==1,
                "A null-dimension radial control does not have one unique actual variable/relation binding.",new{stage="before_editor_entry",constraint_id=Key(relation),variable_id=Key(variable)});
            var ownerVariable=NativeSketchCorrespondence410.OwnerVariable(inputs,variable,cancel);
            RequireVariable(ownerVariable,inputs,relation,variable,"before_editor_entry");
            using var variables=inputs.Sketch.DbEntity.Document.GetDocumentVariablesManager(Constraint2dNetType.MCS);
            Need(variables!=null&&variables.DocumentId==inputs.Sketch.DbEntity.Document.ID&&variables.ConstraintsNetworkType==Constraint2dNetType.MCS&&variables.GetDimensionIdByVariableId(variable).IsNull,
                "The prior radial control has no proved null persistent dimension association in its permanent owner MCS manager.",new{stage="before_editor_entry",constraint_id=Key(relation),variable_id=Key(variable)});
            var witness=new Witness(relation,variable,ownerVariable);budget.Add(new{constraint_id=Key(relation),variable_id=Key(variable),owner_variable=ownerVariable});result.Add(witness);
        }
        return result.ToArray();
    }
    static bool WholeRadial(JsonElement row,NativeSketchMembers385.Snapshot inputs)
    {
        if(row.GetProperty("type").GetString() is not ("DimRadial" or "DimDiametrical")||!row.GetProperty("dimensional").GetBoolean()||row.GetProperty("geometrical").GetBoolean()||
            row.GetProperty("variable").ValueKind!=JsonValueKind.String||row.GetProperty("first").ValueKind!=JsonValueKind.Object||
            row.GetProperty("second").ValueKind!=JsonValueKind.Null||row.GetProperty("third").ValueKind!=JsonValueKind.Null)return false;
        var first=row.GetProperty("first");if(first.GetProperty("subentity_id").ValueKind!=JsonValueKind.Null)return false;
        string id=first.GetProperty("object_id").GetString()!;
        return inputs.Members.Count(member=>Same(Key(member.Id),id)&&member.Id==member.ObjectId&&member.Owned&&!member.ReferenceOnly&&member.Segment==null&&member.NativeType=="kCircArc"&&member.Curve.Kind is "circle" or "arc")==1;
    }
    static void RequireVariable(JsonElement value,NativeSketchMembers385.Snapshot inputs,McObjectId relation,McObjectId variable,string phase)
    {
        var flags=value.GetProperty("type").GetString()!.Split(',',StringSplitOptions.TrimEntries).ToHashSet(StringComparer.Ordinal);
        Need(value.GetProperty("value_available").GetBoolean()&&flags.Contains("Dim2DConstraint")&&flags.All(flag=>flag is "Dim2DConstraint" or "Reference")&&Same(value.GetProperty("host_id").GetString(),Key(inputs.Sketch.ID)),
            "The retained radial presentation has no exact sketch-owned finite dimensional variable witness.",new{phase,constraint_id=Key(relation),variable_id=Key(variable),actual_variable=value});
    }
    internal static bool Candidate(Witness witness,JsonElement previous,JsonElement current,NativeSketchMembers385.Snapshot inputs,string phase)
        =>phase=="editor_before_add"&&CandidateCore(witness,previous,current,inputs);
    internal static bool CandidateForSolve(Witness witness,JsonElement previous,JsonElement current,NativeSketchMembers385.Snapshot inputs,string phase)
        =>(phase is "editor_before_parameter_solve" or "editor_after_parameter_solve")&&CandidateCore(witness,previous,current,inputs);
    static bool CandidateCore(Witness witness,JsonElement previous,JsonElement current,NativeSketchMembers385.Snapshot inputs)
        =>inputs.Sketch.IsInEditMode&&previous.GetProperty("dimension").ValueKind==JsonValueKind.Null&&
            current.GetProperty("dimension").ValueKind==JsonValueKind.String&&WholeRadial(current,inputs)&&Same(current.GetProperty("variable").GetString(),Key(witness.Variable));
    internal static JsonElement WithoutDimension(JsonElement design)
    {var row=JsonNode.Parse(design.GetRawText())!.AsObject();row["dimension"]=null;return Wire.Element(row);}
    internal static Transfer Enter(Witness witness,McObjectId relation,ConstraintInfo info,Constraints2D manager,
        NativeSketchMembers385.Snapshot inputs,JsonElement relations,Action cancel)
        =>EnterCore(witness,relation,info,manager,inputs,relations,cancel,"editor_before_add");
    internal static Transfer EnterForSolve(Witness witness,McObjectId relation,ConstraintInfo info,Constraints2D manager,
        NativeSketchMembers385.Snapshot inputs,JsonElement relations,Action cancel,string phase)
    {
        Need(phase is "editor_before_parameter_solve" or "editor_after_parameter_solve","A solver presentation requires its actual live editor phase.",new{phase});
        return EnterCore(witness,relation,info,manager,inputs,relations,cancel,phase);
    }
    static Transfer EnterCore(Witness witness,McObjectId relation,ConstraintInfo info,Constraints2D manager,
        NativeSketchMembers385.Snapshot inputs,JsonElement relations,Action cancel,string phase)
    {
        Need(inputs.Sketch.IsInEditMode&&info.IsDimensional&&!info.IsGeometrical&&info.Type is ConstraintType.DimRadial or ConstraintType.DimDiametrical&&
            info.VarId==witness.Variable&&!info.DimId.IsNull&&inputs.Helpers.Contains(info.DimId)&&relations.GetProperty("constraints").EnumerateArray().Count(row=>Same(row.GetProperty("dimension").GetString(),Key(info.DimId)))==1,
            "The editor radial presentation has no unique actual dimensional relation/annotation/variable receipt.",new{phase,constraint_id=Key(relation),variable_id=Key(witness.Variable),dimension_id=info.DimId.IsNull?null:Key(info.DimId)});
        RequireOwner(witness,inputs,relation,cancel,phase);
        NativeSketchDimensionBinding411.Prove(manager,relation,info,inputs,cancel);
        var context=McDocumentsManager.GetActiveSheet();
        Need(context!=null&&context.ID==manager.DocumentId&&manager.ConstraintsNetworkType==Constraint2dNetType.MCS&&manager.PlanarSketchId==inputs.Sketch.ID,
            "The actual editor annotation manager does not identify this sketch's current MCS document.",new{phase,constraint_id=Key(relation),manager_document_id=Key(manager.DocumentId)});
        using var variables=context.GetDocumentVariablesManager(Constraint2dNetType.MCS);
        var forward=variables?.GetDimensionIdByVariableId(witness.Variable)??McObjectId.Null;
        var reverse=variables?.GetVariableIdByDimensionId(info.DimId)??McObjectId.Null;
        Need(variables!=null&&variables.DocumentId==manager.DocumentId&&variables.ConstraintsNetworkType==Constraint2dNetType.MCS&&forward==info.DimId&&reverse==witness.Variable,
            "The actual editor MCS variable/dimension association is not bidirectional for this exact retained control.",new{phase,constraint_id=Key(relation),variable_id=Key(witness.Variable),dimension_id=Key(info.DimId),forward_dimension_id=forward.IsNull?null:Key(forward),reverse_variable_id=reverse.IsNull?null:Key(reverse)});
        var dimension=info.DimId.GetObjectOfType<McDimension>();var document=dimension?.DbObject?.Document;
        bool type=info.Type==ConstraintType.DimRadial?dimension is McRadialDimension:dimension is McDiametralDimension;
        Need(dimension!=null&&dimension.ID==info.DimId&&document!=null&&document.ID==manager.DocumentId&&type,
            "The exact editor dimension ID does not resolve to the matching radial annotation type in the actual manager document.",new{phase,constraint_id=Key(relation),dimension_id=Key(info.DimId),actual_native_type=dimension?.GetType().FullName,actual_document_id=document==null?null:Key(document.ID),expected_document_id=Key(manager.DocumentId)});
        return new(witness.Relation,relation,McObjectId.Null,info.DimId,witness.Variable);
    }
    static void RequireOwner(Witness witness,NativeSketchMembers385.Snapshot inputs,McObjectId relation,Action cancel,string phase)
    {
        var observed=NativeSketchCorrespondence410.OwnerVariable(inputs,witness.Variable,cancel);RequireVariable(observed,inputs,relation,witness.Variable,phase);
        Need(ModelFeatureReorderState345.Equivalent(witness.OwnerVariable,observed,cancel),
            "The exact permanent-owner variable changed type, name, expression, value, host or owner during radial presentation transfer.",new{phase,constraint_id=Key(relation),variable_id=Key(witness.Variable),previous_variable=witness.OwnerVariable,actual_variable=observed});
    }
    internal static void Persistent(Witness witness,McObjectId relation,ConstraintInfo info,NativeSketchMembers385.Snapshot inputs,Action cancel)
    {
        Need(!inputs.Sketch.IsInEditMode&&info.DimId.IsNull&&info.VarId==witness.Variable,
            "The retained radial relation did not restore its original null persistent dimension binding.",new{phase="persistent_after_solve",constraint_id=Key(relation),variable_id=Key(witness.Variable),actual_dimension_id=info.DimId.IsNull?null:Key(info.DimId)});
        RequireOwner(witness,inputs,relation,cancel,"persistent_after_solve");
        using var variables=inputs.Sketch.DbEntity.Document.GetDocumentVariablesManager(Constraint2dNetType.MCS);
        Need(variables!=null&&variables.DocumentId==inputs.Sketch.DbEntity.Document.ID&&variables.ConstraintsNetworkType==Constraint2dNetType.MCS&&variables.GetDimensionIdByVariableId(witness.Variable).IsNull,
            "The original null owner-variable dimension association was not restored after native editor exit.",new{phase="persistent_after_solve",constraint_id=Key(relation),variable_id=Key(witness.Variable)});
    }
    // Called only with a proved per-relation transfer. It never changes an
    // arbitrary relation, drops a dimension globally, or edits native state.
    internal static JsonElement Apply(JsonElement mapped,IReadOnlyList<Transfer> transfers,Action cancel)
    {
        if(transfers.Count==0)return mapped;var root=JsonNode.Parse(mapped.GetRawText())!.AsObject();var rows=root["constraints"]!.AsArray();
        foreach(var transfer in transfers)
        {
            cancel();var matches=rows.Where(row=>Same(row!["id"]!.GetValue<string>(),Key(transfer.TargetRelation))).ToArray();
            Need(matches.Length==1&&Same(matches[0]!["variable"]?.GetValue<string>(),Key(transfer.Variable))&&Same(matches[0]!["dimension"]?.GetValue<string>(),transfer.SourceDimension.IsNull?null:Key(transfer.SourceDimension)),
                "A radial presentation receipt does not bind the exact source relation, variable and dimension field.",new{source_constraint_id=Key(transfer.SourceRelation),target_constraint_id=Key(transfer.TargetRelation),variable_id=Key(transfer.Variable)});
            matches[0]!["dimension"]=transfer.TargetDimension.IsNull?null:JsonValue.Create(Key(transfer.TargetDimension));
        }
        return Wire.Element(root);
    }
}
