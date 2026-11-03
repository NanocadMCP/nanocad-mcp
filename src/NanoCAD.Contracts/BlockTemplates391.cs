using System.Text.Json;
using static NanoCAD.Contracts.StructureWriteValidation;

namespace NanoCAD.Contracts;

// This spec is admitted only inside define_block, never as a top-level create.
public sealed record BlockReferenceSpec391(string DefinitionHandle,Point2 Position,double Rotation,double Scale,
    SortedDictionary<string,string> AttributeValues,string LayerName):EntitySpec("BlockReference",LayerName)
{
    public InsertBlockEdit Insertion()=>new(DefinitionHandle,Position,LayerName,Rotation,Scale,AttributeValues);
}
public static class BlockTemplateValidation391
{
    internal static BlockReferenceSpec391 ParseReference(JsonElement item,string path,string layer)
    {
        Shape(item,path,"type","layer","definition_handle","position","rotation","scale","attribute_values");
        var fields=new Dictionary<string,object?>{{"operation","insert_block"},{"definition_handle",Required(item,"definition_handle",path)},
            {"position",Required(item,"position",path)},{"layer",layer}};
        foreach(var name in new[]{"rotation","scale","attribute_values"})if(item.TryGetProperty(name,out var value))fields.Add(name,value);
        var insertion=BlockWriteValidation.ParseInsert(JsonSerializer.SerializeToElement(fields,Wire.Json),path);
        return new(insertion.DefinitionHandle,insertion.Position,insertion.Rotation,insertion.Scale,insertion.AttributeValues,layer);
    }
}
