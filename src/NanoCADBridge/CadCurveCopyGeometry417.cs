using System.Text.Json;
using System.Text.Json.Nodes;
using NanoCAD.Contracts;

namespace NanoCADBridge;

// Only detached kernel inputs use this comparison. Original-source snapshots,
// ownership, display, tokens and the detached-root/nonalias proof stay separate.
internal static class CadCurveCopyGeometry417
{
    internal sealed record Difference(string path,string source,string copy);
    internal sealed record Diff(Difference[] fields,bool truncated);
    internal sealed record Comparison(bool equal,bool optional_metadata_normalized,string basis,
        Diff defining_differences,Diff observed_differences);
    const int DifferenceLimit=8,ValueLimit=128,PathLimit=256;

    static bool Number(JsonElement value)=>value.ValueKind==JsonValueKind.Number&&value.TryGetDouble(out var number)&&double.IsFinite(number);
    static bool Boolean(JsonElement value)=>value.ValueKind is JsonValueKind.True or JsonValueKind.False;
    static bool Point(JsonElement value)=>value.ValueKind==JsonValueKind.Array&&value.GetArrayLength()==3&&value.EnumerateArray().All(Number);
    static bool Property(JsonElement value,string name,Func<JsonElement,bool> valid)
        =>value.TryGetProperty(name,out var property)&&valid(property);

    static bool SplineDefinition(JsonElement value,Action cancel,out JsonElement definition)
    {
        definition=default;cancel();
        if(value.ValueKind!=JsonValueKind.Object||!Property(value,"runtime_type",v=>v.ValueKind==JsonValueKind.String&&v.GetString()=="Teigha.DatabaseServices.Spline")||
            !Property(value,"closed",Boolean)||!Property(value,"start_parameter",Number)||!Property(value,"end_parameter",Number)||
            !Property(value,"start_point",Point)||!Property(value,"end_point",Point)||
            !value.TryGetProperty("geometry",out var geometry)||geometry.ValueKind!=JsonValueKind.Object)return false;
        if(!Property(geometry,"degree",v=>v.ValueKind==JsonValueKind.Number&&v.TryGetInt32(out _))||
            !Property(geometry,"rational",Boolean)||!Property(geometry,"periodic",Boolean)||!Property(geometry,"closed",Boolean))return false;
        foreach(string field in new[]{"control_point_tolerance","knot_tolerance","start_parameter","end_parameter"})
        {cancel();if(!Property(geometry,field,Number))return false;}
        foreach(string field in new[]{"start_point","end_point"})
        {cancel();if(!Property(geometry,field,Point))return false;}
        if(!geometry.TryGetProperty("control_points",out var controls)||controls.ValueKind!=JsonValueKind.Array||controls.GetArrayLength()==0)return false;
        foreach(var point in controls.EnumerateArray()){cancel();if(!Point(point))return false;}
        foreach(string field in new[]{"knots","weights"})
        {
            cancel();if(!geometry.TryGetProperty(field,out var numbers)||numbers.ValueKind!=JsonValueKind.Array||field=="knots"&&numbers.GetArrayLength()==0)return false;
            foreach(var number in numbers.EnumerateArray()){cancel();if(!Number(number))return false;}
        }
        // Fit construction/read observations do not define the detached NURBS
        // used by the solid kernel. Keep every other field, including unknown
        // future fields, both closed flags, tolerances and duplicate endpoints.
        var root=JsonNode.Parse(value.GetRawText())!.AsObject();var shape=root["geometry"]!.AsObject();
        shape.Remove("has_fit_data");shape.Remove("native_inspection");
        definition=Wire.Element(root);cancel();return true;
    }

    static string Summary(JsonElement? value)
    {
        if(value==null)return "<missing>";
        var item=value.Value;
        if(item.ValueKind==JsonValueKind.Array)return "array["+item.GetArrayLength()+"]";
        if(item.ValueKind==JsonValueKind.Object)return "object{"+item.EnumerateObject().Count()+"}";
        string text=item.GetRawText();return text.Length<=ValueLimit?text:text[..ValueLimit]+"…";
    }

    internal static Diff Differences(JsonElement source,JsonElement copy,Action cancel)
    {
        var differences=new List<Difference>();bool truncated=false;
        void Add(string path,JsonElement? before,JsonElement? after)
        {
            if(differences.Count==DifferenceLimit){truncated=true;return;}
            differences.Add(new(path.Length<=PathLimit?path:path[..PathLimit]+"…",Summary(before),Summary(after)));
        }
        void Visit(JsonElement before,JsonElement after,string path)
        {
            cancel();if(truncated)return;
            if(before.ValueKind!=after.ValueKind){Add(path,before,after);return;}
            if(before.ValueKind==JsonValueKind.Object)
            {
                foreach(var field in before.EnumerateObject())
                {
                    cancel();if(truncated)return;
                    string child=path+"."+field.Name;
                    if(after.TryGetProperty(field.Name,out var actual))Visit(field.Value,actual,child);else Add(child,field.Value,null);
                }
                foreach(var field in after.EnumerateObject())
                {cancel();if(truncated)return;if(!before.TryGetProperty(field.Name,out _))Add(path+"."+field.Name,null,field.Value);}
            }
            else if(before.ValueKind==JsonValueKind.Array)
            {
                int count=before.GetArrayLength(),actual=after.GetArrayLength();
                if(count!=actual)Add(path+".length",Wire.Element(count),Wire.Element(actual));
                for(int i=0;i<Math.Min(count,actual)&&!truncated;i++)Visit(before[i],after[i],path+"["+i+"]");
            }
            else if(before.GetRawText()!=after.GetRawText())Add(path,before,after);
        }
        Visit(source,copy,"$");return new(differences.ToArray(),truncated);
    }

    internal static Comparison Compare(JsonElement source,JsonElement copy,bool exactSpline,
        Func<JsonElement,JsonElement,bool> previousEquality,Action cancel)
    {
        cancel();var empty=new Diff([],false);
        if(previousEquality(source,copy))return new(true,false,"unchanged_previous_comparison",empty,empty);
        var observed=Differences(source,copy,cancel);
        if(!exactSpline||!SplineDefinition(source,cancel,out var original)||!SplineDefinition(copy,cancel,out var detached))
            return new(false,false,"previous_comparison_failed_no_complete_exact_spline_definition",empty,observed);
        // The fallback admits metadata differences only. Do not extend a
        // caller's pre-existing numeric tolerance to newly accepted NURBS data.
        bool equal=original.GetRawText()==detached.GetRawText();cancel();
        return new(equal,equal,"exact_complete_spline_nurbs_type_domain_endpoints_and_all_other_nonfit_fields",
            equal?empty:Differences(original,detached,cancel),observed);
    }

    internal static void Require(JsonElement source,JsonElement copy,bool exactSpline,
        Func<JsonElement,JsonElement,bool> previousEquality,string code,string message,string route,
        Action cancel,List<string>? warnings=null)
    {
        var comparison=Compare(source,copy,exactSpline,previousEquality,cancel);
        if(!comparison.equal)throw new DomainException(code,message,false,new{route,copy_comparison=comparison});
        if(comparison.optional_metadata_normalized&&warnings!=null)
            warnings.Add("NATIVE_SPLINE_COPY_METADATA_NORMALIZED: Detached kernel input retains the complete reported NURBS, native type, parameter span and endpoints. Optional fit/read observations differ; original source preservation is checked separately. "+
                JsonSerializer.Serialize(new{route,differences=comparison.observed_differences},Wire.Json));
    }
}
