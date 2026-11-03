using System.Text.Json;

namespace NanoCAD.Contracts;

public sealed record ModelParameterNameEdit(string[] Targets,string VariableId,string ExpectedName,string Name,string[]? ScopeHandles=null)
    :EditSpec("name_model_parameter",Targets);
public sealed record ModelParameterNameChange(EntityRef body_ref,string variable_id,string previous_name,string name,string[] rewritten_expression_ids);

public static partial class ModifyValidation
{
    private static ModelParameterNameEdit ParseParameterName(JsonElement item,string path,string[] handles)
    {
        Shape(item,path,"operation","handles","variable_id","expected_name","name","scope_handles");
        if(handles.Length!=1)throw Invalid(path+".handles","Name one parameter of one native model.");
        var variable=Text(item,"variable_id",path,38);
        if(!Guid.TryParseExact(variable,"B",out var id)||id==Guid.Empty)throw Invalid(path+".variable_id","Use a current variable ID from get_model_variables.");
        var expected=Text(item,"expected_name",path,128);var name=Text(item,"name",path,128);
        var identifier=ModelArithmetic.Parse(name);
        if(!identifier.IsExactIdentifier(name))throw Invalid(path+".name","Use one identifier: letters/underscore followed by letters/digits/underscore.");
        if(string.Equals(expected,name,StringComparison.OrdinalIgnoreCase))throw Invalid(path+".name","Choose a different name; case-only renaming is not supported.");
        return new(handles,id.ToString("B").ToUpperInvariant(),expected,name,ModelParameterScope.Parse(item,handles[0]));
    }
}
