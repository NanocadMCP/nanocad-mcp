using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Nodes;
using Multicad;
using Multicad.DatabaseServices.Constraints;
using Multicad.Mc3D;
using NanoCAD.Contracts;

namespace NanoCADBridge;

// 3DCore.h:9893-9912 binds the default shell thickness to its actual variable.
// The read-only public-SDK shim exposes this identity omitted by mapimgd.dll.
// This adapter covers the existing body-shell route: inward, default thickness.
internal static class ModelShellParameter412
{
    internal const string NamedFormulaUnavailableReason="Native Shell thickness formulas that reference named variables did not persist after meaningful value changes. Literal Shell thickness editing remains available.";
    internal sealed record Binding(string Role,McObjectId Variable,double Value,string Unit="Millimeters");

    private static void Need([DoesNotReturnIf(false)] bool condition,string code,string message)
    {if(!condition)throw new DomainException(code,message);}

    internal static Binding[] Observe(ThicknessFeature feature,Action? cancel=null)
    {
        cancel?.Invoke();
        if(!feature.SurfaceId.IsNull||feature.Direction!=FeatureExtentDirection.Negative)return [];
        var document=feature.DbEntity.Document;
        Need(document!=null&&!feature.ID.IsNull,"MODEL_STATE_UNAVAILABLE","The exact native shell owner document is unavailable.");
        // The managed one-channel getter passes SDK kPositiveExtDir even for
        // an inward feature. Overall Direction must not select channel 1.
        var native=NativeFeatureParameters412.ReadShell(document.ID,feature.ID);
        if(native.Variable.IsNull)return [];
        double managed=feature.GetFaceThickness(McObjectId.Null);
        Need(double.IsFinite(native.Value)&&native.Value>0&&ModelParameterSnapshot.Near(native.Value,managed),
            "MODEL_STATE_UNAVAILABLE","The native default shell thickness and managed getter disagree.");
        using var manager=document.GetDocumentVariablesManager(Constraint2dNetType.MCS);
        Need(manager!=null&&manager.DocumentId==document.ID&&manager.ConstraintsNetworkType==Constraint2dNetType.MCS,
            "VARIABLES_UNAVAILABLE","The native shell owner MCS document/network is unavailable.");
        var variables=manager.GetAllVariables();
        Need(variables!=null,"VARIABLES_UNAVAILABLE","The native shell owner MCS variable inventory is unavailable.");
        ModelInventoryBudget.Ids(variables,cancel);
        var info=manager.GetVariableInfo(native.Variable);
        var flags=info?.Type.ToString().Split(',',StringSplitOptions.TrimEntries).ToHashSet(StringComparer.Ordinal);
        Need(variables.Contains(native.Variable)&&manager.GetVariableHostObjectId(native.Variable)==feature.ID&&
            info!=null&&flags!=null&&flags.Contains("Model3D")&&flags.All(flag=>flag is "Model3D" or "Reference")&&!manager.IsAngularValue(native.Variable)&&
            manager.GetVariableValue(native.Variable,out var value)&&double.IsFinite(value)&&ModelParameterSnapshot.Near(value,native.Value),
            "MODEL_STATE_UNAVAILABLE","The exact native shell thickness variable, owner, length role or current value is unavailable or inconsistent.");
        return [new("shell_thickness",native.Variable,native.Value)];
    }

    internal static ModelFeatureVariable? Resolve(ThicknessFeature feature,McObjectId variable,bool angular,Action? cancel=null)
    {
        if(angular||variable.IsNull)return null;
        var binding=Observe(feature,cancel).SingleOrDefault(item=>item.Variable==variable);
        return binding==null?null:new(feature.ID,variable,false,Role:binding.Role);
    }

    internal static double Value(McObjectId feature,string role)
    {
        Need(role=="shell_thickness","CAPABILITY_UNAVAILABLE","Unsupported native shell parameter role.");
        var shell=feature.GetObjectOfType<ThicknessFeature>();
        Need(shell!=null&&shell.ID==feature,"MODEL_STATE_UNAVAILABLE","The exact native shell parameter owner is unavailable.");
        var binding=Observe(shell).SingleOrDefault(item=>item.Role==role);
        Need(binding!=null,"CAPABILITY_UNAVAILABLE","The current shell mode has no observed default-thickness variable.");
        return binding.Value;
    }

    internal static void ValidateRequested(ThicknessFeature feature,string role,double value)
    {
        Need(double.IsFinite(value)&&value>0,"INVALID_GEOMETRY","A default shell thickness must be finite and positive.");
        var binding=Observe(feature).SingleOrDefault(item=>item.Role==role);
        Need(binding!=null,"CAPABILITY_UNAVAILABLE","The current shell thickness role has no exact native variable binding.");
        // The generic HistoryItem validator rejects even this healthy shell's
        // unchanged positive default thickness (native-controls412-tail/025).
        // No public shell contract defines a different signed input here.
        // Keep exact native role/owner/mm proof; actual geometry feasibility is
        // decided by the native rebuild and the retained getter/graph checks.
    }

    internal static void ValidateExpression(string expression,Action? cancel=null)=>
        Need(ModelArithmetic.ParseStored(expression,cancel).References.Length==0,"CAPABILITY_UNAVAILABLE",NamedFormulaUnavailableReason);

    internal static void RemoveObservedValue(JsonObject parameters,string role)
    {
        Need(role=="shell_thickness","CAPABILITY_UNAVAILABLE","Unsupported native shell parameter role.");
        parameters.Remove("thickness");
    }
}
