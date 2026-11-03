using System.Text.Json;
using static NanoCAD.Contracts.StructureWriteValidation;

namespace NanoCAD.Contracts;

public sealed record CreateBlockFromObjects407(string Name,string[] Handles,Point2 BasePoint)
    :StructureEdit("create_block_from_objects");

public static class BlockFromObjectsValidation407
{
    public static CreateBlockFromObjects407 Parse(JsonElement value,string path)
    {
        Shape(value,path,"operation","name","handles","base_point");
        if(Text(value,"operation",path,48)!="create_block_from_objects")throw Invalid(path+".operation","Expected create_block_from_objects.");
        string name=Text(value,"name",path,255);if(!BlockWriteValidation.OrdinaryName(name))throw Invalid(path+".name","Use a new ordinary local block name.");
        var input=Required(value,"handles",path);if(input.ValueKind!=JsonValueKind.Array||input.GetArrayLength()==0)throw Invalid(path+".handles","Select existing top-level objects to clone into the definition.");
        var handles=new List<string>();var used=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach(var item in input.EnumerateArray())
        {if(item.ValueKind!=JsonValueKind.String)throw Invalid(path+".handles","Use actual current entity handles.");var id=Validation.NormalizeHandle(item.GetString()!);if(!used.Add(id))throw Invalid(path+".handles","Select each original once.");handles.Add(id);}
        var point=Required(value,"base_point",path);
        double Number(JsonElement v){if(v.ValueKind!=JsonValueKind.Number||!v.TryGetDouble(out double n)||!double.IsFinite(n)||Math.Abs(n)>1e9)throw Invalid(path+".base_point","Use a finite XY base point in drawing millimeters.");return n;}
        if(point.ValueKind!=JsonValueKind.Array||point.GetArrayLength()!=2)throw Invalid(path+".base_point","Use an XY pair.");
        return new(name,handles.ToArray(),new(Number(point[0]),Number(point[1])));
    }
    public static void Batch(IReadOnlyList<StructureEdit> operations)
    {
        if(operations.Any(op=>op is CreateBlockFromObjects407)&&(operations.Count!=1||operations[0] is not CreateBlockFromObjects407))
            throw Invalid("operations","Create a block from existing objects in a separate structural request.","INVALID_OPERATION_ORDER");
    }
}
