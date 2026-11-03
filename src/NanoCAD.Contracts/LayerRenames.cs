using System.Text.Json;
using static NanoCAD.Contracts.StructureWriteValidation;

namespace NanoCAD.Contracts;

public sealed record RenameLayerEdit(string Handle,string ExpectedName,string Name):StructureEdit("rename_layer");
public static class LayerRenameValidation
{
    public static RenameLayerEdit Parse(JsonElement item,string path)
    {
        Shape(item,path,"operation","handle","expected_name","name");
        string expected=Text(item,"expected_name",path,255),name=Text(item,"name",path,255);
        if(!IsOrdinaryLayerName(expected)||!IsOrdinaryLayerName(name)||string.Equals(expected,name,StringComparison.OrdinalIgnoreCase))
            throw Invalid(path,"Expected distinct ordinary source and destination names; no0/Defpoints/special/dependent or case-only rename.");
        return new(Validation.NormalizeHandle(Text(item,"handle",path,16)),expected,name);
    }
    public static void Batch(IReadOnlyList<StructureEdit> edits)
    {
        var renames=edits.OfType<RenameLayerEdit>().ToArray();if(renames.Length==0)return;
        if(renames.Length!=edits.Count||renames.Select(e=>e.Handle).Distinct(StringComparer.Ordinal).Count()!=renames.Length||
            renames.Select(e=>e.Name).Distinct(StringComparer.OrdinalIgnoreCase).Count()!=renames.Length)
            throw Invalid("operations","Rename requires a separate batch of distinct source handles and distinct destination names.","INVALID_OPERATION_ORDER");
    }
    public static void Source(string actual,bool dependent,RenameLayerEdit edit,bool destinationExists)
    {
        if(dependent||!IsOrdinaryLayerName(actual))throw new DomainException("LAYER_UNAVAILABLE","Only ordinary independent native layers can be renamed.");
        if(!string.Equals(actual,edit.ExpectedName,StringComparison.Ordinal))throw new DomainException("LAYER_NAME_CHANGED","Stored name differs from expected_name.");
        if(destinationExists)throw new DomainException("LAYER_EXISTS","Destination name already exists; rename never overwrites or swaps layers.");
    }
}
