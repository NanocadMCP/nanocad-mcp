using NanoCAD.Contracts;
using Teigha.DatabaseServices;
using Teigha.Geometry;

namespace NanoCADBridge;

internal static class CadBlockCopier
{
    internal static void CheckSource(BlockReference reference,CadBlockMover.Resolved resolved,Transaction tr,Action cancel)
    {
        foreach(var item in new DBObject[]{reference}.Concat(resolved.Attributes.Select(id=>tr.GetObject(id,OpenMode.ForRead))))
        {
            cancel();using var xdata=item.XData;
            if(!item.ExtensionDictionary.IsNull || xdata!=null)
                throw new DomainException("UNSUPPORTED_BLOCK_CONTENT","Block copy excludes root/attribute extension dictionaries and XData; no opaque metadata is discarded.");
            if(item is AttributeReference attribute)NativeBlockMultiline442.CheckCopy(attribute,cancel);
        }
    }
    private static MoveSpec Translation(CopySpec copy)=>new(copy.Handles,copy.Displacement);
    internal static void Preview(CadBlockMover.Preview source,CadBlockMover.Resolved resolved,CopySpec operation,Transaction tr,Action cancel)
        =>PreviewMatrix(source,resolved,CadModifier.Matrix(Translation(operation)),tr,cancel);
    internal static void PreviewMatrix(CadBlockMover.Preview source,CadBlockMover.Resolved resolved,Matrix3d matrix,Transaction tr,Action cancel,bool keepAttributeTextReadable=false)
    {
        using var copy=CadBlockMover.DuplicatePreview(source,cancel);
        CadBlockMover.TransformMatrix(copy.Reference,copy.Attributes,resolved,matrix,tr,cancel,true,keepAttributeTextReadable);
    }
    internal static BlockReference Apply(BlockReference source,CadBlockMover.Resolved resolved,CopySpec operation,ObjectId modelId,Transaction tr,List<Entity> owned,Action cancel)
        =>ApplyMatrix(source,resolved,CadModifier.Matrix(Translation(operation)),modelId,tr,owned,cancel);
    internal static BlockReference ApplyMatrix(BlockReference source,CadBlockMover.Resolved resolved,Matrix3d matrix,ObjectId modelId,Transaction tr,List<Entity> owned,Action cancel,bool keepAttributeTextReadable=false)
    {
        var scope = CadBlockSpace.RequireTopLevel(source, source.Database, tr);
        if (source.OwnerId != modelId || resolved.Definition.Model != modelId || CadBlockSpace.RequireWriteSpace(source.Database, tr, scope.Kind, cancel) != modelId)
            throw new DomainException("OUT_OF_SCOPE", "Block copy must preserve its requested active Model or paper-layout space.");
        // Own each new entity before any append. The writer disposes these after
        // commit/abort; no full BlockReference.Clone or duplicate definition.
        var copy=CadBlockMover.Prepare(source,resolved,tr,cancel);
        owned.Add(copy.Reference);owned.AddRange(copy.Attributes);
        CadBlockMover.TransformMatrix(copy.Reference,copy.Attributes,resolved,matrix,tr,cancel,true,keepAttributeTextReadable);
        var expected=CadBlockMover.Snapshot(copy,cancel);
        var model=(BlockTableRecord)tr.GetObject(modelId,OpenMode.ForWrite);
        model.AppendEntity(copy.Reference);tr.AddNewlyCreatedDBObject(copy.Reference,true);
        foreach(var attribute in copy.Attributes)
        {
            cancel();copy.Reference.AttributeCollection.AppendAttribute(attribute);tr.AddNewlyCreatedDBObject(attribute,true);
            if(attribute.OwnerId!=copy.Reference.ObjectId || attribute.ObjectId.IsNull || resolved.Attributes.Contains(attribute.ObjectId))
                throw new DomainException("BLOCK_COPY_MISMATCH","New attribute ownership/identity was not retained.");
        }
        if(copy.Reference.OwnerId!=modelId || copy.Reference.BlockTableRecord!=source.BlockTableRecord ||
            !copy.Reference.AttributeCollection.Cast<ObjectId>().SequenceEqual(copy.Attributes.Select(a=>a.ObjectId)) ||
            !BlockMoveGeometry.SameProperties(expected,CadBlockMover.Snapshot(copy,cancel)))
            throw new DomainException("BLOCK_COPY_MISMATCH","Appended block copy differs from its detached geometry/properties or ownership.");
        resolved.Graph?.Verify(tr,cancel);
        return copy.Reference;
    }
}
