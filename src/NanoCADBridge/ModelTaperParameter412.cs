using System.Text.Json.Nodes;
using Multicad;
using Multicad.DatabaseServices;
using Multicad.DatabaseServices.Constraints;
using Multicad.Mc3D;
using NanoCAD.Contracts;

namespace NanoCADBridge;

// Angle/Angle2 are documented radians. Existing owning-variable taper editors
// use degrees in MCS; that convention is checked against the actual getter on
// every binding and after the generic expression rebuild. A generic angular
// flag alone never selects a taper or establishes its units. Nonzero formula
// propagation still needs the postponed grouped native assessment.
internal static class ModelTaperParameter412
{
    internal sealed record Binding(string Role,double Degrees);
    static void Need([System.Diagnostics.CodeAnalysis.DoesNotReturnIf(false)] bool ok,string code,string message)
    {if(!ok)throw new DomainException(code,message);}
    static bool Extent(ExtrudeParamExtType value)=>value is ExtrudeParamExtType.Dist or ExtrudeParamExtType.All or ExtrudeParamExtType.ToFace;
    static bool Direction(FeatureExtentDirection value)=>value is FeatureExtentDirection.Positive or FeatureExtentDirection.Negative or FeatureExtentDirection.Symmetric or FeatureExtentDirection.Different;
    internal static bool Handles(string role)=>role is "extrusion_taper" or "extrusion_taper2" or "sweep_taper";

    // Read-only typed identity/mode observation. No history enumeration, solver,
    // metadata write, driver allocation or feature-mode activation occurs here.
    // The caller keeps body scope/health and compares an available MCS value
    // with Degrees before publishing a coherent degree-unit observation.
    internal static Binding? Observe(McObjectId featureId,McObjectId variable,McDocument owner,Action? cancel=null)
    {
        cancel?.Invoke();if(featureId.IsNull||variable.IsNull||owner.ID.IsNull)return null;
        var extrusion=featureId.GetObjectOfType<ExtrudeFeature>();
        if(extrusion!=null&&extrusion.ID==featureId&&extrusion.DbEntity.Document?.ID==owner.ID)
        {
            if(!Direction(extrusion.Direction)||!Extent(extrusion.Extent))return null;
            // These fields are distinct owning controls, not interchangeable
            // names. A cross-unit alias cannot establish a unique taper role.
            if(variable==extrusion.DistanceParamVarID||variable==extrusion.Distance2ParamVarID)return null;
            bool first=extrusion.TaperAngleParamVarID==variable;
            bool second=extrusion.Direction==FeatureExtentDirection.Different&&Extent(extrusion.Extent2)&&extrusion.TaperAngle2ParamVarID==variable;
            if(first==second)return null;
            double radians=first?extrusion.Angle:extrusion.Angle2;
            return double.IsFinite(radians)?new(first?"extrusion_taper":"extrusion_taper2",radians*180/Math.PI):null;
        }
        var sweep=featureId.GetObjectOfType<ExtrudeAlongPathFeature>();
        if(sweep==null||sweep.ID!=featureId||sweep.DbEntity.Document?.ID!=owner.ID||sweep.TaperAngleParamVarID!=variable||
            sweep.ProfileOrientation is not (SweepProfileOrientation.NormalToPath or SweepProfileOrientation.ParallelToOriginalProfile))return null;
        var path=sweep.GetPath();if(path==null||path.Count==0)return null;
        foreach(var item in path){cancel?.Invoke();if(item==null||item.ID.IsNull)return null;}
        double taper=sweep.Angle;return double.IsFinite(taper)?new("sweep_taper",taper*180/Math.PI):null;
    }

    // Mutation preflight adds exact owner-MCS host/type/value proof. The caller's
    // shared graph still owns body membership, health, formula/dependency scope,
    // requested native-domain validation and strict post-rebuild getter proof.
    internal static Binding? Resolve(McObjectId featureId,McObjectId variable,McDocument owner,Action? cancel=null)
    {
        var binding=Observe(featureId,variable,owner,cancel);if(binding==null)return null;
        using var manager=owner.GetDocumentVariablesManager(Constraint2dNetType.MCS);
        Need(manager!=null&&manager.DocumentId==owner.ID&&manager.ConstraintsNetworkType==Constraint2dNetType.MCS,
            "VARIABLE_SCOPE_MISMATCH","The taper's exact owning MCS document/network is unavailable.");
        var info=manager.GetVariableInfo(variable);var flags=info?.Type.ToString().Split(',',StringSplitOptions.TrimEntries).ToHashSet(StringComparer.Ordinal);
        Need(info!=null&&flags!=null&&flags.Contains("Model3D")&&flags.All(flag=>flag is "Model3D" or "Reference")&&
            manager.GetVariableHostObjectId(variable)==featureId,
            "VARIABLE_SCOPE_MISMATCH","The exact taper variable has a different native host or unsupported metadata type.");
        cancel?.Invoke();Need(manager.GetVariableValue(variable,out double value)&&double.IsFinite(value)&&ModelParameterSnapshot.Near(value,binding.Degrees),
            "MODEL_STATE_UNAVAILABLE","The actual owning MCS taper scalar does not agree with its exact native radians getter converted to degrees.");
        return binding;
    }
    internal static double ValueDegrees(McObjectId featureId,McObjectId variable,string role)
    {
        var feature=featureId.GetObjectOfType<HistoryItem>();Need(feature!=null&&feature.ID==featureId&&feature.DbEntity.Document!=null,
            "MODEL_STATE_UNAVAILABLE","The exact retained taper owner is unavailable.");
        var binding=Observe(featureId,variable,feature.DbEntity.Document);
        Need(binding!=null&&binding.Role==role,"MODEL_STATE_UNAVAILABLE","The exact taper variable binding or active native mode changed; refresh its owner.");
        return binding.Degrees;
    }
    internal static void ValidateRequested(double degrees)
        =>Need(double.IsFinite(degrees)&&Math.Abs(degrees)<90,"INVALID_GEOMETRY","A taper must be finite degrees strictly between -90 and90; zero and either sign are allowed when the actual native owner accepts them.");
    internal static void RemoveObservedValue(JsonObject parameters,string role)
    {
        Need(Handles(role),"OUT_OF_SCOPE","Unknown typed taper role.");string field=role=="extrusion_taper2"?"taper_angle2":"taper_angle";
        parameters.Remove(field);if(parameters["terminal"] is JsonObject terminal)terminal.Remove(field);
        // All other taper/length controls, extent/direction/face termination,
        // sweep path/orientation, profile and native owner links remain exact.
    }
}
