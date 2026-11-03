using System.Text.Json;

namespace NanoCAD.Contracts;

public static partial class ModifyValidation
{
    private static DrawingProfileEdit ParseDrawingVariable(JsonElement item,string path,string[] handles,string action)
    {
        string Name(string field){var name=Text(item,field,path,64);var parsed=ModelArithmetic.Parse(name);
            if(!parsed.IsExactIdentifier(name))throw Invalid(path,"Use an exact arithmetic identifier.");return name;}
        double Scalar(string field)=>Number(Required(item,field,path),path+"."+field,-1e12,1e12);
        if(action=="create_drawing_variable")
        {Shape(item,path,"operation","handles","name","value");return new(action,handles,Name:Name("name"),Value:Scalar("value"));}
        var variable=Text(item,"variable_id",path,38);
        if(!Guid.TryParseExact(variable,"B",out var id)||id==Guid.Empty)throw Invalid(path,"Use a current native variable ID from inspect_constraints(include_variables=true).");
        if(action=="name_drawing_parameter")
        {
            Shape(item,path,"operation","handles","variable_id","expected_name","name");var expected=Text(item,"expected_name",path,128);var name=Name("name");
            if(string.Equals(name,expected,StringComparison.OrdinalIgnoreCase))throw Invalid(path,"Use a different name; case-only rename is unsupported.");
            return new(action,handles,Name:name,VariableId:id.ToString("B").ToUpperInvariant(),ExpectedName:expected);
        }
        Shape(item,path,"operation","handles","variable_id","expected_expression","expected_value","expression");
        var expression=Text(item,"expression",path,1024);ModelArithmetic.Parse(expression);
        return new(action,handles,VariableId:id.ToString("B").ToUpperInvariant(),ExpectedExpression:Text(item,"expected_expression",path,8192),ExpectedValue:Scalar("expected_value"),Expression:expression);
    }
}

// All arithmetic is a preflight prediction, never a substitute for the native
// value and geometry checks after solve. The SDK consumer graph must agree.
public static class DrawingVariablePlanning
{
    public static bool IsAction(string action)=>action is "create_drawing_variable" or "name_drawing_parameter" or "set_drawing_expression";
    public static bool IsDimensionType(string type)
    {var flags=type.Split(',',StringSplitOptions.TrimEntries);return flags.Contains("Dim2DConstraint")&&flags.All(t=>t is "User" or "Dim2DConstraint" or "Reference");}
    public static Dictionary<string,string[]> Consumers(ModelExpressionVariable[] variables,Action? cancel=null)
    {
        long graphBytes=2;
        var names=new Dictionary<string,string>(StringComparer.Ordinal);
        var folded=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var result=new Dictionary<string,HashSet<string>>(StringComparer.OrdinalIgnoreCase);
        foreach(var v in variables)
        {
            cancel?.Invoke();graphBytes+=JsonSerializer.SerializeToUtf8Bytes(v,Wire.Json).LongLength+1;
            if(graphBytes>8L*1024*1024)throw new DomainException("RESPONSE_LIMIT_EXCEEDED","Drawing variable graph exceeds the actual8MiB serialized snapshot budget.");
            if(!names.TryAdd(v.Name,v.Id)||!folded.Add(v.Name)||!result.TryAdd(v.Id,new(StringComparer.OrdinalIgnoreCase)))
                throw new DomainException("VARIABLE_NAME_AMBIGUOUS","Variable names/IDs must be unique, including case-insensitive names.");
        }
        foreach(var v in variables)
        {
            cancel?.Invoke();
            foreach(var name in ModelArithmetic.ParseStored(v.Expression,cancel).References)
            {
                cancel?.Invoke();
                if(!names.TryGetValue(name,out var parent))throw new DomainException("VARIABLE_NAME_AMBIGUOUS","Copy exact variable names from inspect_constraints(include_variables=true).");
                result[parent].Add(v.Id);
            }
        }
        // Reject pre-existing cycles as well as new ones; the observed network
        // must be understandable before changing one of its variables.
        // Whole native inventories can contain long chains. Use an explicit DAG
        // schedule so valid depth does not exhaust the CAD process call stack.
        var incoming=new Dictionary<string,int>(StringComparer.OrdinalIgnoreCase);
        foreach(var id in result.Keys){cancel?.Invoke();incoming.Add(id,0);}
        foreach(var children in result.Values)foreach(var child in children){cancel?.Invoke();incoming[child]++;}
        var ready=new Queue<string>();foreach(var pair in incoming){cancel?.Invoke();if(pair.Value==0)ready.Enqueue(pair.Key);}
        int visited=0;
        while(ready.Count>0)
        {
            cancel?.Invoke();var id=ready.Dequeue();visited++;
            foreach(var child in result[id]){cancel?.Invoke();if(--incoming[child]==0)ready.Enqueue(child);}
        }
        if(visited!=result.Count)throw new DomainException("CYCLIC_MODEL_EXPRESSION","Drawing variable expressions contain a cycle.");
        return result.ToDictionary(v=>{cancel?.Invoke();return v.Key;},v=>v.Value.OrderBy(x=>x,StringComparer.OrdinalIgnoreCase).ToArray(),StringComparer.OrdinalIgnoreCase);
    }
    public static void VerifyConsumers(ModelExpressionVariable[] variables,Action? cancel=null)
    {
        var expected=Consumers(variables,cancel);
        foreach(var v in variables)
        {
            cancel?.Invoke();
            if(!expected[v.Id].ToHashSet(StringComparer.OrdinalIgnoreCase).SetEquals(v.DownstreamVariables))
                throw new DomainException("VARIABLE_DEPENDENCIES_MISMATCH","Native drawing dependencies differ from the supported arithmetic graph.");
        }
    }
}
