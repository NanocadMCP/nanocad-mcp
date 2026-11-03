using System.Text.Json;
using static NanoCAD.Contracts.StructureWriteValidation;

namespace NanoCAD.Contracts;

// One native record/default selection per request. Existing layer/layout
// rename adapters retain their published operations and ownership policy.
public sealed record StructureLifecycleEdit(string Kind,string Handle,string ExpectedName,string? Name=null,
    string? ExpectedCurrentHandle=null):StructureEdit(Kind);

public static class StructureLifecycleValidation
{
    public static readonly string[] Operations={"set_current_text_style","set_current_dimension_style",
        "rename_text_style","rename_dimension_style","delete_layer","delete_text_style","delete_dimension_style","delete_layout"};
    public static bool Handles(string operation)=>Operations.Contains(operation,StringComparer.Ordinal);
    public static bool IsCurrent(StructureLifecycleEdit edit)=>edit.Operation.StartsWith("set_current_",StringComparison.Ordinal);
    public static bool IsRename(StructureLifecycleEdit edit)=>edit.Operation.StartsWith("rename_",StringComparison.Ordinal);
    public static bool IsDelete(StructureLifecycleEdit edit)=>edit.Operation.StartsWith("delete_",StringComparison.Ordinal);
    public static string Kind(StructureLifecycleEdit edit)=>edit.Operation.EndsWith("dimension_style",StringComparison.Ordinal)?"dimension_style":
        edit.Operation.EndsWith("text_style",StringComparison.Ordinal)?"text_style":edit.Operation=="delete_layer"?"layer":"layout";
    public static StructureLifecycleEdit Parse(JsonElement input,string path)
    {
        string operation=Text(input,"operation",path,32);
        if(!Handles(operation))throw Invalid(path+".operation","Unknown ordinary native structure lifecycle operation.");
        bool current=operation.StartsWith("set_current_",StringComparison.Ordinal),rename=operation.StartsWith("rename_",StringComparison.Ordinal);
        Shape(input,path,current?new[]{"operation","handle","expected_name","expected_current_handle"}:
            rename?new[]{"operation","handle","expected_name","name"}:new[]{"operation","handle","expected_name"});
        string handle=Validation.NormalizeHandle(Text(input,"handle",path,16)),expected=Text(input,"expected_name",path,255);
        if(!BlockWriteValidation.OrdinaryName(expected))throw Invalid(path+".expected_name","Use an ordinary unqualified native record name.");
        string? name=null,currentHandle=null;
        if(rename)
        {
            name=Text(input,"name",path,255);
            if(!BlockWriteValidation.OrdinaryName(name)||string.Equals(name,expected,StringComparison.OrdinalIgnoreCase))
                throw Invalid(path+".name","Use a distinct ordinary unused name; no case-only/no-op rename.");
        }
        if(current)currentHandle=Validation.NormalizeHandle(Text(input,"expected_current_handle",path,16));
        return new(operation,handle,expected,name,currentHandle);
    }
    public static void Batch(IReadOnlyCollection<StructureEdit> edits)
    {
        if(edits.Any(edit=>edit is StructureLifecycleEdit)&&edits.Count!=1)
            throw Invalid("operations","Select, rename or safely delete one existing native structure record in a separate request.","INVALID_OPERATION_ORDER");
    }
}
