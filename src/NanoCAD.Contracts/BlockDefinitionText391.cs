using System.Text.Json;
using static NanoCAD.Contracts.StructureWriteValidation;

namespace NanoCAD.Contracts;

public sealed record BlockMemberText391(string Handle,string Type,string ExpectedText,string Text);
public sealed record SetBlockDefinitionText391(string DefinitionHandle,string ExpectedDefinitionToken,BlockMemberText391[] Members)
    : StructureEdit("set_block_definition_text");

public static class BlockDefinitionTextValidation391
{
    public static SetBlockDefinitionText391 Parse(JsonElement value,string path)
    {
        Shape(value,path,"operation","definition_handle","expected_definition_token","members");
        if(Text(value,"operation",path,48)!="set_block_definition_text")throw Invalid(path+".operation","Expected set_block_definition_text.");
        string handle=Validation.NormalizeHandle(Text(value,"definition_handle",path,16));
        string token=Text(value,"expected_definition_token",path,64);
        if(token.Length!=64||token.Any(c=>!"0123456789ABCDEF".Contains(c)))throw Invalid(path+".expected_definition_token","Read the exact current token from inspect_block.definition_text_editing.");
        var input=Required(value,"members",path);
        if(input.ValueKind!=JsonValueKind.Array||input.GetArrayLength()==0)throw Invalid(path+".members","Supply existing direct Text/MText members to edit.");
        var edits=new List<BlockMemberText391>();var used=new HashSet<string>(StringComparer.OrdinalIgnoreCase);int index=0;
        foreach(var item in input.EnumerateArray())
        {
            string at=path+".members["+index+++"]";Shape(item,at,"handle","type","expected_text","text");
            string id=Validation.NormalizeHandle(Text(item,"handle",at,16));
            if(!used.Add(id))throw Invalid(at+".handle","Each existing member may appear once.");
            string kind=Text(item,"type",at,16);
            if(kind is not ("Text" or "MText"))throw Invalid(at+".type","Supported direct definition labels: Text, MText.");
            string expected=Text(item,"expected_text",at,kind=="Text"?4096:8192,true);
            string replacement=Text(item,"text",at,kind=="Text"?4096:8192,true);
            if(kind=="Text"?!IsPlainAttribute(expected)||!IsPlainAttribute(replacement):!MTextEditContent.IsCanonical(expected)||!MTextEditContent.IsCanonical(replacement))
                throw Invalid(at,"Text requires literal single-line content; MText requires exact canonical literal Contents. Fields, percent codes, controls and inline formatting need their owning route.");
            edits.Add(new(id,kind,expected,replacement));
        }
        return new(handle,token,edits.ToArray());
    }
    public static void Batch(IReadOnlyList<StructureEdit> edits)
    {
        if(edits.Any(e=>e is SetBlockDefinitionText391)&&edits.Count!=1)throw Invalid("operations","Edit one shared definition's literal labels in a separate structural request.","INVALID_OPERATION_ORDER");
    }
}
