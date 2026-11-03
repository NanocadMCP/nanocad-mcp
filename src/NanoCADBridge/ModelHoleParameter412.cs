using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using Multicad;
using Multicad.DatabaseServices;
using Multicad.DatabaseServices.Constraints;
using Multicad.Mc3D;
using NanoCAD.Contracts;

namespace NanoCADBridge;

// 3DCore.h:2024-2088 defines the placement roles and their actual variable IDs.
// The managed IppDefinition omits getVar1/getVar2. The read-only SDK shim supplies
// those identities; neither a variable name nor a matching scalar binds a role.
internal static class ModelHoleParameter412
{
    internal sealed record Binding(string Role,McObjectId Variable,double Value,string Unit="Millimeters");
    internal sealed record NumericEdit(McObjectId OwnerDocument,McObjectId Feature,McObjectId FirstVariable,McObjectId SecondVariable,
        McObjectId TargetPlane,McObjectId FirstPart,McObjectId[] Parts,McGeomParam[] References,
        double OriginalFirst,double OriginalSecond,double First,double Second,int OriginalSector,int Sector,bool ReplaceFormulas,
        Dictionary<McObjectId,string> ExpectedExpressions,Dictionary<McObjectId,string> Names);

    private static void Need([DoesNotReturnIf(false)] bool condition,string code,string message)
    {if(!condition)throw new DomainException(code,message);}

    internal static Binding[] Observe(HoleFeature feature,Action? cancel=null)
    {
        cancel?.Invoke();
        var document=feature.DbEntity.Document;
        Need(document!=null&&!feature.ID.IsNull,"MODEL_STATE_UNAVAILABLE","The exact native hole owner document is unavailable.");
        using var placement=feature.GetPlacement();
        Need(placement!=null,"MODEL_STATE_UNAVAILABLE","The native hole placement definition is unavailable.");
        if(placement.DefType is not (IppDefinitionType.TwoEdges or IppDefinitionType.TwoVertices or IppDefinitionType.Radial))return [];
        var native=NativeFeatureParameters412.ReadHolePlacement(document.ID,feature.ID);
        Need(native.Definition==placement.DefType.ToString(),"MODEL_STATE_UNAVAILABLE","Native and managed reads disagree on the actual hole placement mode.");

        // Fixed/concentric placements have no numeric placement roles. Array,
        // dependent and part-CS placements need their own mode-specific adapter.
        // Radial Param2 is geometrically radians, but that alone does not prove
        // the owning MCS angular variable's unit; no degree/radian guess is made.
        bool first=native.Definition is "TwoEdges" or "TwoVertices" or "Radial";
        bool second=native.Definition is "TwoEdges" or "TwoVertices";
        var candidates=new List<Binding>();
        if(first&&!native.FirstVariable.IsNull)candidates.Add(new("hole_offset1",native.FirstVariable,native.FirstValue));
        if(second&&!native.SecondVariable.IsNull)candidates.Add(new("hole_offset2",native.SecondVariable,native.SecondValue));
        if(candidates.Count==0)return [];
        Need(candidates.Select(binding=>binding.Variable).Distinct().Count()==candidates.Count,"CAPABILITY_UNAVAILABLE","One hole placement variable controls multiple numeric roles; that binding requires a combined-role adapter.");
        using var manager=document.GetDocumentVariablesManager(Constraint2dNetType.MCS);
        Need(manager!=null&&manager.DocumentId==document.ID&&manager.ConstraintsNetworkType==Constraint2dNetType.MCS,
            "VARIABLE_SCOPE_MISMATCH","The native hole owner MCS variable manager has a different document or network.");
        var variables=manager.GetAllVariables();
        Need(variables!=null,"VARIABLES_UNAVAILABLE","The native hole owner MCS variable inventory is unavailable.");
        ModelInventoryBudget.Ids(variables,cancel);
        foreach(var binding in candidates)
        {
            cancel?.Invoke();
            double managed=binding.Role=="hole_offset1"?placement.Param1:placement.Param2;
            Need(double.IsFinite(binding.Value)&&ModelParameterSnapshot.Near(binding.Value,managed),"MODEL_STATE_UNAVAILABLE","The actual native hole placement scalar differs from the managed placement getter.");
            var info=manager.GetVariableInfo(binding.Variable);
            var flags=info?.Type.ToString().Split(',',StringSplitOptions.TrimEntries).ToHashSet(StringComparer.Ordinal);
            Need(variables.Contains(binding.Variable)&&manager.GetVariableHostObjectId(binding.Variable)==feature.ID&&
                info!=null&&flags!=null&&flags.Contains("Model3D")&&flags.All(flag=>flag is "Model3D" or "Reference")&&!manager.IsAngularValue(binding.Variable)&&
                manager.GetVariableValue(binding.Variable,out var value)&&double.IsFinite(value)&&ModelParameterSnapshot.Near(value,binding.Value),
                "MODEL_STATE_UNAVAILABLE","The exact native hole placement variable, owner, length role or current value is unavailable or inconsistent.");
        }
        return candidates.ToArray();
    }

    internal static ModelFeatureVariable? Resolve(HoleFeature feature,McObjectId variable,bool angular,Action? cancel=null)
    {
        if(angular||variable.IsNull)return null;
        var binding=Observe(feature,cancel).SingleOrDefault(item=>item.Variable==variable);
        return binding==null?null:new(feature.ID,variable,false,Role:binding.Role);
    }

    internal static double Value(McObjectId feature,string role)
    {
        var hole=feature.GetObjectOfType<HoleFeature>();
        Need(hole!=null&&hole.ID==feature,"MODEL_STATE_UNAVAILABLE","The exact native hole parameter owner is unavailable.");
        var binding=Observe(hole).SingleOrDefault(item=>item.Role==role);
        Need(binding!=null,"CAPABILITY_UNAVAILABLE","The current hole placement mode has no observed native variable for the requested role.");
        return binding.Value;
    }

    internal static void ValidateRequested(HoleFeature feature,string role,double value)
    {
        Need(double.IsFinite(value),"INVALID_GEOMETRY","A hole placement offset must be finite.");
        var binding=Observe(feature).SingleOrDefault(item=>item.Role==role);
        Need(binding!=null,"CAPABILITY_UNAVAILABLE","The current hole placement role has no exact native variable binding.");
        // Offsets can be signed. The owning native object decides the valid
        // domain; this adapter does not replace it with a positive-length rule.
        Need(feature.IsValidVarValForObject(binding.Variable,value),"INVALID_GEOMETRY","The native hole rejects the requested placement offset.");
    }

    internal static void RemoveObservedValue(JsonObject parameters,string role)
    {
        Need(role is "hole_offset1" or "hole_offset2","CAPABILITY_UNAVAILABLE","Unsupported native hole parameter role.");
        if(parameters["placement"] is JsonObject placement)placement.Remove(role=="hole_offset1"?"offset1":"offset2");
    }

    private static bool Unchanged(double before,double after)=>double.IsFinite(after)&&Math.Abs(before-after)<=1e-8;

    // Existing scalar hole edits must not erase a positional expression while
    // changing the diameter/seat. The SDK explicitly warns that Param setters
    // overwrite expressions (3DCore.h:2043-2051).
    internal static NumericEdit PrepareNumeric(HoleFeature feature,JsonElement definition,Action? cancel=null)
    {
        var bindings=Observe(feature,cancel);
        using var placement=feature.GetPlacement();
        Need(placement!=null&&placement.DefType==IppDefinitionType.TwoEdges,"INVALID_ARGUMENT","Edge-offset edits require the existing TwoEdges placement.");
        var document=feature.DbEntity.Document;
        var native=NativeFeatureParameters412.ReadHolePlacement(document.ID,feature.ID);
        Need(native.Definition=="TwoEdges","MODEL_STATE_UNAVAILABLE","The hole placement mode changed during numeric preflight.");
        Need(native.FirstVariable==(bindings.SingleOrDefault(binding=>binding.Role=="hole_offset1")?.Variable??McObjectId.Null)&&
            native.SecondVariable==(bindings.SingleOrDefault(binding=>binding.Role=="hole_offset2")?.Variable??McObjectId.Null),
            "MODEL_STATE_UNAVAILABLE","The exact hole placement variable identities changed during numeric preflight.");
        double first=definition.GetProperty("offset1").GetDouble(),second=definition.GetProperty("offset2").GetDouble();
        int sector=definition.GetProperty("sector").GetInt32();
        Need(double.IsFinite(first)&&double.IsFinite(second)&&sector is >=0 and <=3,"INVALID_GEOMETRY","Hole offsets must be finite and the sector must be0..3.");
        bool replace=definition.TryGetProperty("replace_driver_formulas",out var flag)&&flag.GetBoolean();
        var expressions=new Dictionary<McObjectId,string>();
        var names=new Dictionary<McObjectId,string>();
        using var variables=bindings.Length==0?null:document.GetDocumentVariablesManager(Constraint2dNetType.MCS);
        Need(bindings.Length==0||variables!=null&&variables.DocumentId==document.ID&&variables.ConstraintsNetworkType==Constraint2dNetType.MCS,
            "VARIABLE_SCOPE_MISMATCH","The exact hole owner MCS manager changed during numeric preflight.");
        foreach(var binding in bindings)
        {
            cancel?.Invoke();double requested=binding.Role=="hole_offset1"?first:second;
            Need(feature.IsValidVarValForObject(binding.Variable,requested),"INVALID_GEOMETRY","The native hole rejects the requested placement offset.");
            ModelNumericDriverEditor.Validate(feature,binding.Variable,requested,replace);
            // The unchanged expression is kept byte-for-byte. A changed control
            // has passed the explicit formula replacement check above.
            expressions.Add(binding.Variable,ModelNumericDriverEditor.RetainedExpression(feature,binding.Variable,requested)??requested.ToString("R",CultureInfo.InvariantCulture));
            var info=variables!.GetVariableInfo(binding.Variable);
            Need(info!=null,"VARIABLES_UNAVAILABLE","The actual native hole parameter name is unavailable.");
            names.Add(binding.Variable,info.Name);
        }
        var references=placement.GetAssocParams();
        Need(references!=null,"MODEL_STATE_UNAVAILABLE","The hole's actual associative placement references are unavailable.");
        var parts=placement.PartsIDs;
        Need(parts!=null,"MODEL_STATE_UNAVAILABLE","The hole's actual placement part inventory is unavailable.");
        return new(document.ID,feature.ID,native.FirstVariable,native.SecondVariable,placement.TargetPlaneId,placement.FirstPartID,
            parts.ToArray(),references.Select(reference=>(McGeomParam)reference.Clone()).ToArray(),
            placement.Param1,placement.Param2,first,second,placement.Sector,sector,replace,expressions,names);
    }

    private static void SameBinding(HoleFeature feature,NumericEdit edit)
    {
        Need(feature.ID==edit.Feature&&feature.DbEntity.Document?.ID==edit.OwnerDocument,"MODEL_STATE_UNAVAILABLE","The actual hole parameter owner changed.");
        using var placement=feature.GetPlacement();
        Need(placement!=null&&placement.DefType==IppDefinitionType.TwoEdges,"MODEL_STATE_UNAVAILABLE","The retained hole placement mode changed.");
        var native=NativeFeatureParameters412.ReadHolePlacement(edit.OwnerDocument,feature.ID);
        var references=placement.GetAssocParams();
        var parts=placement.PartsIDs;
        Need(native.Definition=="TwoEdges"&&native.FirstVariable==edit.FirstVariable&&native.SecondVariable==edit.SecondVariable&&
            placement.TargetPlaneId==edit.TargetPlane&&placement.FirstPartID==edit.FirstPart&&parts!=null&&parts.SequenceEqual(edit.Parts)&&
            references!=null&&references.Count==edit.References.Length&&references.Zip(edit.References).All(pair=>pair.First.Equals(pair.Second)),
            "MODEL_STATE_UNAVAILABLE","The exact hole placement variables, support or associative references changed.");
        if(edit.Names.Count>0)
        {
            using var variables=feature.DbEntity.Document.GetDocumentVariablesManager(Constraint2dNetType.MCS);
            Need(variables!=null&&variables.DocumentId==edit.OwnerDocument&&variables.ConstraintsNetworkType==Constraint2dNetType.MCS,
                "VARIABLE_SCOPE_MISMATCH","The exact hole parameter owner MCS manager is unavailable.");
            foreach(var pair in edit.Names)
                Need(variables.GetVariableHostObjectId(pair.Key)==feature.ID&&variables.GetVariableInfo(pair.Key)?.Name==pair.Value,
                    "MODEL_STATE_UNAVAILABLE","The exact native hole parameter name or owner changed.");
        }
    }

    internal static void ApplyNumeric(HoleFeature feature,NumericEdit edit)
    {
        SameBinding(feature,edit);
        // Recheck every requested formula/domain before the first mutation.
        foreach(var pair in new[]{(edit.FirstVariable,edit.First),(edit.SecondVariable,edit.Second)})
            if(!pair.Item1.IsNull)ModelNumericDriverEditor.Validate(feature,pair.Item1,pair.Item2,edit.ReplaceFormulas);
        bool firstScalar=edit.FirstVariable.IsNull&&!Unchanged(edit.OriginalFirst,edit.First);
        bool secondScalar=edit.SecondVariable.IsNull&&!Unchanged(edit.OriginalSecond,edit.Second);
        if(firstScalar||secondScalar||edit.Sector!=edit.OriginalSector)
        {
            using var original=feature.GetPlacement();
            Need(original!=null,"MODEL_STATE_UNAVAILABLE","The existing hole placement is unavailable before its scalar/sector edit.");
            using var placement=(IppDefinition)original.Clone();
            // No bound Param setter is called, even for an unchanged formula.
            if(firstScalar)placement.Param1=edit.First;
            if(secondScalar)placement.Param2=edit.Second;
            if(edit.Sector!=edit.OriginalSector)placement.Sector=edit.Sector;
            var retained=new Dictionary<McObjectId,string>();
            foreach(var id in new[]{edit.FirstVariable,edit.SecondVariable}.Where(id=>!id.IsNull))
                retained.Add(id,ModelNumericDriverEditor.RetainedExpression(feature,id,null)!);
            Need(feature.SetPlacement(placement),"MODEL_UPDATE_FAILED","Native hole rejected its scalar/sector placement edit.");
            SameBinding(feature,edit);
            ModelNumericDriverEditor.VerifyRetained(feature,retained);
        }
        if(!edit.FirstVariable.IsNull)ModelNumericDriverEditor.Set(feature,edit.FirstVariable,edit.First,edit.ReplaceFormulas);
        if(!edit.SecondVariable.IsNull)ModelNumericDriverEditor.Set(feature,edit.SecondVariable,edit.Second,edit.ReplaceFormulas);
    }

    internal static void VerifyNumeric(HoleFeature feature,NumericEdit edit)
    {
        SameBinding(feature,edit);
        var bindings=Observe(feature);
        using var placement=feature.GetPlacement();
        Need(placement!=null&&placement.Sector==edit.Sector&&Unchanged(placement.Param1,edit.First)&&Unchanged(placement.Param2,edit.Second),
            "MODEL_UPDATE_FAILED","The requested hole offsets/sector were not retained after rebuild.");
        Need(bindings.All(binding=>Unchanged(binding.Value,binding.Role=="hole_offset1"?edit.First:edit.Second)),
            "MODEL_UPDATE_FAILED","The exact native hole placement variables do not agree with the requested offsets.");
        ModelNumericDriverEditor.VerifyRetained(feature,edit.ExpectedExpressions);
    }
}
