using System.Text.Json;

namespace NanoCAD.Contracts;

public sealed record ModelFeatureSuppressionSpec(string[] Targets,string ExpectedLastItemId,
    string FeatureId,bool ExpectedSuppressed,bool Suppressed)
    : EditSpec("set_model_feature_suppressed",Targets);

public sealed record ModelFeatureSuppressionChange(EntityRef source,EntityRef body_ref,
    string body_mc_id,string feature_id,string feature_type,bool suppressed,
    double volume_before,double volume_after,bool source_representation_replaced);

public static partial class ModifyValidation
{
    private static ModelFeatureSuppressionSpec ParseModelFeatureSuppression(JsonElement item,string path,string[] handles)
    {
        Shape(item,path,"operation","handles","expected_last_item_id","feature_id","expected_suppressed","suppressed");
        if(handles.Length!=1)throw Invalid(path+".handles","Select exactly one native model body.");
        string Id(string field)
        {
            string text=Text(item,field,path,38);
            if(!Guid.TryParseExact(text,"B",out var id)||id==Guid.Empty)
                throw Invalid(path+"."+field,"Use an exact current history ID.");
            return id.ToString("B").ToUpperInvariant();
        }
        bool Flag(string field)
        {
            var value=Required(item,field,path);
            if(value.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
                throw Invalid(path+"."+field,"Expected boolean.");
            return value.GetBoolean();
        }
        string last=Id("expected_last_item_id"),feature=Id("feature_id");
        bool expected=Flag("expected_suppressed"),desired=Flag("suppressed");
        if(expected==desired)throw Invalid(path+".suppressed","Feature state must change.");
        return new(handles,last,feature,expected,desired);
    }
}
