using System.Text.Json;
using System.Text.Json.Serialization;
using static NanoCAD.Contracts.StructureWriteValidation;

namespace NanoCAD.Contracts;

public sealed record NativeStyleLifecycle352(string Kind,string Handle,string ExpectedName,string StyleToken,
    string? Name=null,string? ExpectedCurrentHandle=null):StructureEdit(Kind);
public sealed record NativeStyleLifecycleRead352(bool candidate,string[] operations,string? style_token,
    string? current_style_handle,string scope,
    [property:JsonIgnore(Condition=JsonIgnoreCondition.WhenWritingNull)] string? unavailable_reason=null);

public static class NativeStyleLifecycleValidation352
{
    public static readonly string[] Operations=["rename_table_style","rename_mleader_style","set_current_table_style","set_current_mleader_style"];
    public static bool Handles(string operation)=>Operations.Contains(operation,StringComparer.Ordinal);
    public static bool Current(NativeStyleLifecycle352 edit)=>edit.Operation.StartsWith("set_current_",StringComparison.Ordinal);
    public static bool Table(NativeStyleLifecycle352 edit)=>edit.Operation.EndsWith("table_style",StringComparison.Ordinal);
    public static string RecordKind(NativeStyleLifecycle352 edit)=>Table(edit)?"table_style":"mleader_style";
    public static NativeStyleLifecycle352 Parse(JsonElement input,string path)
    {
        string operation=Text(input,"operation",path,32);if(!Handles(operation))throw Invalid(path,"Unknown native style lifecycle operation.");
        bool current=operation.StartsWith("set_current_",StringComparison.Ordinal);
        Shape(input,path,current?["operation","handle","expected_name","style_token","expected_current_handle"]:
            ["operation","handle","expected_name","style_token","name"]);
        string handle=Validation.NormalizeHandle(Text(input,"handle",path,16)),expected=Text(input,"expected_name",path,255);
        if(!BlockWriteValidation.OrdinaryName(expected))throw Invalid(path+".expected_name","Use an ordinary local unqualified style name.");
        string token=Text(input,"style_token",path,64);if(token.Length!=64||token.Any(ch=>!"0123456789ABCDEF".Contains(ch)))
            throw Invalid(path+".style_token","Use the fresh lifecycle.style_token for the observed native identity/settings from get_structure.");
        string? name=null,previous=null;
        if(current)previous=Validation.NormalizeHandle(Text(input,"expected_current_handle",path,16));
        else
        {
            name=Text(input,"name",path,255);if(!BlockWriteValidation.OrdinaryName(name)||string.Equals(name,expected,StringComparison.OrdinalIgnoreCase))
                throw Invalid(path+".name","Choose a distinct unused ordinary native style name.");
        }
        return new(operation,handle,expected,token,name,previous);
    }
    public static void Batch(IReadOnlyCollection<StructureEdit> edits)
    {
        if(edits.Any(edit=>edit is NativeStyleLifecycle352)&&edits.Count!=1)
            throw Invalid("operations","Rename or select one retained native style per request, with fresh name/token/current identity.","INVALID_OPERATION_ORDER");
    }
}
