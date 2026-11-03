using System.Text.Json;
using static NanoCAD.Contracts.StructureWriteValidation;

namespace NanoCAD.Contracts;

public sealed record SetBlockDefinitionMembers392(string DefinitionHandle,string ExpectedDefinitionToken,
    EntitySpec[] AddEntities,string[] RemoveHandles):StructureEdit("set_block_definition_members");

public static class BlockDefinitionMembersValidation392
{
    public static SetBlockDefinitionMembers392 Parse(JsonElement value,string path)
    {
        Shape(value,path,"operation","definition_handle","expected_definition_token","add_entities","remove_handles");
        if(Text(value,"operation",path,48)!="set_block_definition_members")throw Invalid(path+".operation","Expected set_block_definition_members.");
        string handle=Validation.NormalizeHandle(Text(value,"definition_handle",path,16));
        string token=Text(value,"expected_definition_token",path,64);
        if(token.Length!=64||token.Any(c=>!"0123456789ABCDEF".Contains(c)))throw Invalid(path+".expected_definition_token","Read the exact current token from inspect_block.definition_membership.");
        EntitySpec[] added=[];var removed=new List<string>();var used=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if(value.TryGetProperty("add_entities",out var entities))
        {
            if(entities.ValueKind!=JsonValueKind.Array)throw Invalid(path+".add_entities","Expected an entity array.");
            if(entities.GetArrayLength()!=0)
            {
                // Reuse the real definition member parser, including MText,
                // independent Hatch and existing static nested references.
                var template=JsonSerializer.SerializeToElement(new{operation="define_block",name="MCP_MEMBER_TEMPLATE",base_point=new[]{0d,0d},entities},Wire.Json);
                added=BlockWriteValidation.ParseDefine(template,path+".add_entities").Entities;
            }
        }
        if(value.TryGetProperty("remove_handles",out var handles))
        {
            if(handles.ValueKind!=JsonValueKind.Array)throw Invalid(path+".remove_handles","Expected direct existing member handles.");
            foreach(var input in handles.EnumerateArray())
            {
                if(input.ValueKind!=JsonValueKind.String||input.GetString() is not {Length:>0} text)throw Invalid(path+".remove_handles","Each member handle must be a string.");
                var id=Validation.NormalizeHandle(text);if(!used.Add(id))throw Invalid(path+".remove_handles","Each retired member may appear once.");removed.Add(id);
            }
        }
        if(added.Length==0&&removed.Count==0)throw Invalid(path,"Supply at least one added entity or retired direct member.");
        return new(handle,token,added,removed.ToArray());
    }
    public static void Batch(IReadOnlyList<StructureEdit> edits)
    {
        if(edits.Any(e=>e is SetBlockDefinitionMembers392)&&edits.Count!=1)throw Invalid("operations","Edit one shared definition's composition in a separate request.","INVALID_OPERATION_ORDER");
    }
}
