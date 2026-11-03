using System.Text.Json;

namespace NanoCAD.Contracts;

public sealed record ModelUserVariableEdit(string[] Targets,string Name,double Value)
    :EditSpec("create_model_variable",Targets);
public sealed record ModelUserVariableChange(EntityRef source_ref,string variable_id,string name,string expression,double value,string value_unit);

public static partial class ModifyValidation
{
    private static ModelUserVariableEdit ParseUserVariable(JsonElement item,string path,string[] handles)
    {
        Shape(item,path,"operation","handles","name","value");
        if(handles.Length!=1)throw Invalid(path+".handles","Select one native model as the document variable network context.");
        var name=Text(item,"name",path,64);var identifier=ModelArithmetic.Parse(name);
        if(!identifier.IsExactIdentifier(name))throw Invalid(path+".name","Use one arithmetic identifier.");
        if(!item.TryGetProperty("value",out var number)||number.ValueKind!=JsonValueKind.Number||!number.TryGetDouble(out var value)||!double.IsFinite(value)||Math.Abs(value)>1e12)
            throw Invalid(path+".value","Use a finite scalar within +/-1e12 native variable units.");
        return new(handles,name,value);
    }
}
