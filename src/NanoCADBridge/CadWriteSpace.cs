using NanoCAD.Contracts;
using Teigha.DatabaseServices;

namespace NanoCADBridge;

internal static class CadWriteSpace
{
    internal static ObjectId Resolve(Database db,Transaction tr,string target)
    {
        var blocks=(BlockTable)tr.GetObject(db.BlockTableId,OpenMode.ForRead);
        var model=blocks[BlockTableRecord.ModelSpace];var active=db.CurrentSpaceId;
        if(target=="model"&&active==model)return model;
        if(target=="paper"&&active!=model&&tr.GetObject(active,OpenMode.ForRead) is BlockTableRecord record&&record.IsLayout)
        {
            var layout=(Layout)tr.GetObject(record.LayoutId,OpenMode.ForRead);
            if(!layout.ModelType)return active;
        }
        throw new DomainException("OUT_OF_SCOPE","Activate the requested target_space before writing; paper requires an active sheet outside its model viewport.");
    }
}
