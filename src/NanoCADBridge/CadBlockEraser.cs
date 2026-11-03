using NanoCAD.Contracts;
using Teigha.DatabaseServices;

namespace NanoCADBridge;

internal static class CadBlockEraser
{
    public static void Apply(BlockReference reference,CadBlockMover.Resolved resolved,Transaction tr,Action cancel)
    {
        var id=reference.ObjectId;var owner=reference.OwnerId;var definition=reference.BlockTableRecord;
        if(!reference.AttributeCollection.Cast<ObjectId>().SequenceEqual(resolved.Attributes))
            throw new DomainException("BLOCK_ERASE_MISMATCH","Owned attribute collection changed before erase.");
        cancel();reference.Erase(true);
        if(!id.IsErased||reference.ObjectId!=id||reference.OwnerId!=owner||definition.IsErased||definition.IsEffectivelyErased)
            throw new DomainException("BLOCK_ERASE_MISMATCH","Erase changed ownership/definition or did not erase the selected insertion.");
        foreach(var attributeId in resolved.Attributes)
        {
            cancel();
            if(!attributeId.IsErased&&!attributeId.IsEffectivelyErased)
                throw new DomainException("BLOCK_ERASE_MISMATCH","Native erase retained a live owned attribute.");
            var attribute=(AttributeReference)tr.GetObject(attributeId,OpenMode.ForRead,true);
            if(attribute.OwnerId!=id)throw new DomainException("BLOCK_ERASE_MISMATCH","Erased attribute ownership changed.");
        }
        foreach(var member in resolved.Definition.PrimitiveIds.Concat(resolved.Definition.AttributeIds))
        {
            cancel();if(member.IsErased||member.IsEffectivelyErased)
                throw new DomainException("BLOCK_ERASE_MISMATCH","Erase affected the shared definition.");
        }
        resolved.Graph?.Verify(tr,cancel);
    }
}
