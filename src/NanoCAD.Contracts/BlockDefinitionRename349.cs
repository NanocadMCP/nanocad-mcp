using System.Text.Json;
using static NanoCAD.Contracts.StructureWriteValidation;

namespace NanoCAD.Contracts;

public sealed record RenameBlockDefinition349(string Handle,string ExpectedName,string Name) : StructureEdit("rename_block_definition");

public static class BlockDefinitionRenameValidation349
{
    public static RenameBlockDefinition349 Parse(JsonElement value,string path="$" )
    {
        Shape(value,path,"operation","handle","expected_name","name");
        if(Text(value,"operation",path,32)!="rename_block_definition")throw Invalid(path+".operation","Expected rename_block_definition.");
        string oldName=Text(value,"expected_name",path,255),name=Text(value,"name",path,255);
        if(!BlockWriteValidation.OrdinaryName(oldName)||!BlockWriteValidation.OrdinaryName(name))
            throw Invalid(path,"Expected ordinary local non-anonymous block names.");
        if(string.Equals(oldName,name,StringComparison.OrdinalIgnoreCase))
            throw Invalid(path+".name","Choose a different unused name; no no-op or case-only rename.");
        return new(Validation.NormalizeHandle(Text(value,"handle",path,16)),oldName,name);
    }
    public static void Batch(IReadOnlyList<StructureEdit> edits)
    {
        if(edits.Any(edit=>edit is RenameBlockDefinition349)&&edits.Count!=1)
            throw Invalid("operations","Rename one shared block definition in a separate request.","INVALID_OPERATION_ORDER");
    }
}
