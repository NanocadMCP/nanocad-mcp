using System.Text.Json;
using Multicad;
using Multicad.Mc3D;
using NanoCAD.Contracts;

namespace NanoCADBridge;

internal static class ModelPatternRequest439
{
    internal static bool Circular(ModelPatternSpec edit)=>edit.Kind.EndsWith("_circular_pattern",StringComparison.Ordinal);
    internal static bool Add(ModelPatternSpec edit)=>edit.Kind.StartsWith("add_",StringComparison.Ordinal);
    internal static ModifyArguments Translate(ModifyArguments args)
    {
        var edit=(ModelManufacturingEdit)args.Operations.Single();var d=edit.Definition;bool add=edit.Kind.StartsWith("add_",StringComparison.Ordinal);
        string[]? Ids(string name)=>d.TryGetProperty(name,out var values)?values.EnumerateArray().Select(value=>value.GetString()!.ToUpperInvariant()).ToArray():null;
        JsonElement? axis=d.TryGetProperty("axis",out var selected)?selected.Clone():add?
            Wire.Element(new{kind="edge",edge_id=d.GetProperty("axis_edge_id").GetString(),edge_token=d.GetProperty("edge_token").GetString()}):null;
        var pattern=new ModelPatternSpec(edit.Kind,edit.Handles,edit.ExpectedLastItemId,edit.FeatureId,null,null,null,
            d.GetProperty("count").GetInt32(),1,d.GetProperty("angle_degrees").GetDouble(),1,
            d.TryGetProperty("expected_count",out var oldCount)?oldCount.GetInt32():null,null,
            d.TryGetProperty("expected_angle_degrees",out var oldAngle)?oldAngle.GetDouble():null,null,
            Ids("source_feature_ids"),Ids("expected_source_feature_ids"),d.TryGetProperty("replace_driver_formulas",out var flag)&&flag.GetBoolean(),
            axis,null,d.TryGetProperty("expected_axis_id",out var oldAxis)?oldAxis.GetString():null);
        return args with{Operations=[pattern]};
    }
    internal static ArrayFeature? Pattern(McObjectId id,bool circular)=>circular?id.GetObjectOfType<CircularPatternFeature>():id.GetObjectOfType<RectangularPatternFeature>();
    internal static (int X,int Y,double StepX,double StepY) Values(ArrayFeature pattern)=>pattern is CircularPatternFeature c?
        (c.Size,1,c.Angle*180/Math.PI,1):pattern is RectangularPatternFeature r?(r.SizeX,r.SizeY,r.DistancesX,r.DistancesY):throw new DomainException("INVALID_ARGUMENT","Select a rectangular or circular native pattern.");
    internal static (McObjectId Id,double Value)[] Controls(ArrayFeature pattern,ModelPatternSpec edit)=>pattern is CircularPatternFeature c?
        [(c.CountParamVarID,edit.CountX),(c.AngleParamVarID,edit.StepX)]:ModelPatternSources416.Controls((RectangularPatternFeature)pattern,edit.CountX,edit.CountY,edit.StepX,edit.StepY);
    internal static McObjectId[] NativeSources(ArrayFeature pattern,Action? cancel)
    {
        var sources=new List<McObjectId>();for(int index=0;index<pattern.SourceItemsCount;index++)
        {cancel?.Invoke();var id=pattern.GetSourceItemFeature(index);if(id.IsNull)throw new DomainException("PATTERN_SOURCE_BINDING_MISMATCH","A native pattern source-item ID is unavailable.");sources.Add(id);}
        ModelInventoryBudget.Ids(sources,cancel);return sources.ToArray();
    }
}
