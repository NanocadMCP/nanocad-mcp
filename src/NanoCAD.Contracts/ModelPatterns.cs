using System.Text.Json;
using System.Text.Json.Serialization;

namespace NanoCAD.Contracts;

public sealed record ModelPatternSpec(string Kind,string[] Targets,string ExpectedLastItemId,
    string? FeatureId,string? SourceFeatureId,string? DirectionXHandle,string? DirectionYHandle,
    int CountX,int CountY,double StepX,double StepY,
    int? ExpectedCountX=null,int? ExpectedCountY=null,double? ExpectedStepX=null,double? ExpectedStepY=null,
    string[]? SourceFeatureIds=null,string[]? ExpectedSourceFeatureIds=null,bool ReplaceDriverFormulas=false,
    JsonElement? DirectionX=null,JsonElement? DirectionY=null,string? ExpectedDirectionXId=null,string? ExpectedDirectionYId=null)
    : EditSpec(Kind,Targets);

public sealed record ModelPatternChange(string operation,EntityRef source,EntityRef body_ref,
    string body_mc_id,string feature_id,string? source_feature_id,
    EntityRef? direction_x_ref,EntityRef? direction_y_ref,
    int count_x,int count_y,double step_x,double step_y,
    double volume_before,double volume_after,bool source_representation_replaced,
    [property:JsonIgnore(Condition=JsonIgnoreCondition.WhenWritingNull)] string[]? source_feature_ids=null,
    [property:JsonIgnore(Condition=JsonIgnoreCondition.WhenWritingNull)] string[]? native_source_item_ids=null,
    [property:JsonIgnore(Condition=JsonIgnoreCondition.WhenWritingNull)] string? source_binding_scope=null,
    [property:JsonIgnore(Condition=JsonIgnoreCondition.WhenWritingNull)] string[]? previous_source_feature_ids=null,
    [property:JsonIgnore(Condition=JsonIgnoreCondition.WhenWritingNull)] bool? sources_retargeted=null,
    [property:JsonIgnore(Condition=JsonIgnoreCondition.WhenWritingNull)] ModelPatternDirectionBinding439? direction_x=null,
    [property:JsonIgnore(Condition=JsonIgnoreCondition.WhenWritingNull)] ModelPatternDirectionBinding439? direction_y=null);

public sealed record ModelPatternElementSpec(string[] Targets,string ExpectedLastItemId,
    string FeatureId,string ExpectedSourceFeatureId,int ExpectedCountX,int ExpectedCountY,
    double ExpectedStepX,double ExpectedStepY,int IndexX,int IndexY,
    bool ExpectedSuppressed,bool Suppressed)
    : EditSpec("set_model_pattern_element",Targets);

public sealed record ModelPatternElementChange(EntityRef source,EntityRef body_ref,
    string body_mc_id,string feature_id,string source_feature_id,int index_x,int index_y,
    bool suppressed,double volume_before,double volume_after,bool source_representation_replaced);

public static partial class ModifyValidation
{
    private static ModelPatternSpec ParseModelPattern(JsonElement item,string path,string[] handles,string kind)
    {
        bool add=kind=="add_model_rectangular_pattern";
        Shape(item,path,"operation","handles","expected_last_item_id","feature_id","source_feature_id","source_feature_ids","expected_source_feature_ids",
            "direction_x_handle","direction_y_handle","count_x","count_y","step_x","step_y",
            "expected_count_x","expected_count_y","expected_step_x","expected_step_y","replace_driver_formulas",
            "direction_x","direction_y","expected_direction_x_id","expected_direction_y_id");
        if(handles.Length!=1)throw Invalid(path+".handles","Select exactly one native model body.");
        string Id(string field)
        {
            string text=Text(item,field,path,38);
            if(!Guid.TryParseExact(text,"B",out var id)||id==Guid.Empty)
                throw Invalid(path+"."+field,"Use an exact current native history ID.");
            return id.ToString("B").ToUpperInvariant();
        }
        string[]? Ids(string field)
        {
            if(!item.TryGetProperty(field,out var array))return null;
            if(array.ValueKind!=JsonValueKind.Array||array.GetArrayLength()==0)throw Invalid(path+"."+field,"Use a nonempty array of distinct current native feature IDs.");
            var ids=new HashSet<string>(StringComparer.OrdinalIgnoreCase);var ordered=new List<string>();
            foreach(var value in array.EnumerateArray())
            {
                if(value.ValueKind!=JsonValueKind.String||!Guid.TryParseExact(value.GetString(),"B",out var id)||id==Guid.Empty||!ids.Add(id.ToString("B")))
                    throw Invalid(path+"."+field,"Use distinct nonempty current native feature IDs.");
                ordered.Add(id.ToString("B").ToUpperInvariant());
            }
            return ordered.ToArray();
        }
        int Count(string field)
        {
            var value=Required(item,field,path);
            if(value.ValueKind!=JsonValueKind.Number||!value.TryGetInt32(out int count)||count<1)
                throw Invalid(path+"."+field,"Use a positive native Int32 count.");
            return count;
        }
        double Step(string field)=>Number(Required(item,field,path),path+"."+field,double.MinValue,double.MaxValue);
        string last=Id("expected_last_item_id");
        int x=Count("count_x"),y=Count("count_y");
        double sx=Step("step_x"),sy=Step("step_y");
        if(add)
        {
            if(item.TryGetProperty("feature_id",out _)||item.TryGetProperty("expected_count_x",out _)||
               item.TryGetProperty("expected_count_y",out _)||item.TryGetProperty("expected_step_x",out _)||
               item.TryGetProperty("expected_step_y",out _)||item.TryGetProperty("expected_source_feature_ids",out _)||
               item.TryGetProperty("replace_driver_formulas",out _)||item.TryGetProperty("expected_direction_x_id",out _)||
               item.TryGetProperty("expected_direction_y_id",out _))throw Invalid(path,"Add request contains update-only fields.");
            var sources=Ids("source_feature_ids");bool single=item.TryGetProperty("source_feature_id",out _);
            if(single==(sources!=null))throw Invalid(path,"Choose exactly one of source_feature_id or source_feature_ids.");
            string? source=single?Id("source_feature_id"):null;
            (string? Handle,JsonElement? Selector) Direction(string axis)
            {
                string field="direction_"+axis,legacy=field+"_handle";
                bool old=item.TryGetProperty(legacy,out _),fresh=item.TryGetProperty(field,out var selector);
                if(old==fresh)throw Invalid(path+"."+field,"Choose exactly one direction selector or legacy Line handle for each direction.");
                return old?(Validation.NormalizeHandle(Text(item,legacy,path,16)),null):(null,PatternDirection439(selector,path+"."+field));
            }
            var dx=Direction("x");var dy=Direction("y");
            if(dx.Handle!=null&&(dx.Handle==dy.Handle||dx.Handle==handles[0])||dy.Handle==handles[0])throw Invalid(path,"Direction Lines must be distinct from each other and the body.");
            return new(kind,handles,last,null,source,dx.Handle,dy.Handle,x,y,sx,sy,SourceFeatureIds:sources,DirectionX:dx.Selector,DirectionY:dy.Selector);
        }
        if(item.TryGetProperty("source_feature_id",out _)||item.TryGetProperty("direction_x_handle",out _)||
           item.TryGetProperty("direction_y_handle",out _))throw Invalid(path,"Update request contains add-only fields.");
        (JsonElement? Selector,string? Expected) ChangeDirection(string axis)
        {
            string field="direction_"+axis,expected="expected_"+field+"_id";
            bool change=item.TryGetProperty(field,out var selector),hasExpected=item.TryGetProperty(expected,out _);
            if(change!=hasExpected)throw Invalid(path+"."+field,"A direction replacement requires its observed expected native binding ID, and vice versa.");
            return change?(PatternDirection439(selector,path+"."+field),Id(expected)):(null,null);
        }
        var changeX=ChangeDirection("x");var changeY=ChangeDirection("y");
        string feature=Id("feature_id");
        int oldX=Count("expected_count_x"),oldY=Count("expected_count_y");
        double oldSx=Step("expected_step_x"),oldSy=Step("expected_step_y");
        var expectedSources=Ids("expected_source_feature_ids");
        if(expectedSources?.Contains(feature,StringComparer.OrdinalIgnoreCase)==true)throw Invalid(path,"The pattern cannot be its own source.");
        var sourcesToSet=Ids("source_feature_ids");
        if(sourcesToSet!=null&&expectedSources==null)throw Invalid(path+".expected_source_feature_ids","Replacing sources requires the exact original source-feature group as a precondition.");
        if(sourcesToSet?.Contains(feature,StringComparer.OrdinalIgnoreCase)==true)throw Invalid(path,"The pattern cannot be its own source.");
        bool replace=false;
        if(item.TryGetProperty("replace_driver_formulas",out var flag))
        {
            if(flag.ValueKind is not (JsonValueKind.True or JsonValueKind.False))throw Invalid(path+".replace_driver_formulas","Use a boolean.");
            replace=flag.GetBoolean();
        }
        return new(kind,handles,last,feature,null,null,null,x,y,sx,sy,oldX,oldY,oldSx,oldSy,SourceFeatureIds:sourcesToSet,ExpectedSourceFeatureIds:expectedSources,ReplaceDriverFormulas:replace,
            DirectionX:changeX.Selector,DirectionY:changeY.Selector,ExpectedDirectionXId:changeX.Expected,ExpectedDirectionYId:changeY.Expected);
    }

    private static ModelPatternElementSpec ParseModelPatternElement(JsonElement item,string path,string[] handles)
    {
        Shape(item,path,"operation","handles","expected_last_item_id","feature_id","expected_source_feature_id",
            "expected_count_x","expected_count_y","expected_step_x","expected_step_y",
            "index_x","index_y","expected_suppressed","suppressed");
        if(handles.Length!=1)throw Invalid(path+".handles","Select exactly one native model body.");
        string Id(string field)
        {
            string value=Text(item,field,path,38);
            if(!Guid.TryParseExact(value,"B",out var id)||id==Guid.Empty)
                throw Invalid(path+"."+field,"Use an exact current native history ID.");
            return id.ToString("B").ToUpperInvariant();
        }
        int Count(string field)
        {
            var value=Required(item,field,path);
            if(value.ValueKind!=JsonValueKind.Number||!value.TryGetInt32(out int count)||count<1)
                throw Invalid(path+"."+field,"Use a positive native Int32 count.");
            return count;
        }
        int Index(string field)
        {
            var value=Required(item,field,path);
            if(value.ValueKind!=JsonValueKind.Number||!value.TryGetInt32(out int index)||index<0)
                throw Invalid(path+"."+field,"Use a nonnegative native Int32 index.");
            return index;
        }
        bool Flag(string field)
        {
            var value=Required(item,field,path);
            if(value.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
                throw Invalid(path+"."+field,"Expected boolean.");
            return value.GetBoolean();
        }
        var last=Id("expected_last_item_id");var feature=Id("feature_id");
        var source=Id("expected_source_feature_id");
        // The full history may end in later machining or drawing views. The
        // bridge resolves pattern ownership and validates that complete history.
        if(source==feature)throw Invalid(path,"Use the pattern and its distinct source feature.");
        int countX=Count("expected_count_x"),countY=Count("expected_count_y");
        double stepX=Number(Required(item,"expected_step_x",path),path+".expected_step_x",double.MinValue,double.MaxValue);
        double stepY=Number(Required(item,"expected_step_y",path),path+".expected_step_y",double.MinValue,double.MaxValue);
        int x=Index("index_x"),y=Index("index_y");
        if(x>=countX||y>=countY||x==0&&y==0)throw Invalid(path,"Select one non-origin array position inside the current pattern.");
        bool expected=Flag("expected_suppressed"),desired=Flag("suppressed");
        if(expected==desired)throw Invalid(path,"The requested element state must change.");
        return new(handles,last,feature,source,countX,countY,stepX,stepY,x,y,expected,desired);
    }
}
