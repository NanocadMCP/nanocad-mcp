using System.Text.Json;
using NanoCAD.Contracts;
using Teigha.DatabaseServices;

namespace NanoCADBridge;

internal static class CadBlockPropertyEditor
{
    private static string? Handle(ObjectId id)=>id.IsNull?null:id.Handle.ToString();
    private static JsonElement Capture(BlockReference reference,AttributeReference[] attributes,Action cancel)=>Wire.Element(new{
        state=CadBlockMover.Snapshot(new CadBlockMover.Preview(reference,attributes),cancel),layer_name=reference.Layer,
        identity=new{root=Handle(reference.ObjectId),owner=Handle(reference.OwnerId),attributes=attributes.Select(a=>new{id=Handle(a.ObjectId),owner=Handle(a.OwnerId)}).ToArray()},
        alignments=attributes.Select(a=>new[]{a.AlignmentPoint.X,a.AlignmentPoint.Y,a.AlignmentPoint.Z}).ToArray()});
    private static void Edit(BlockReference reference,AttributeReference[] attributes,PropertiesSpec operation,CadModifier.PropertyIds ids,Transaction tr,Action cancel)
    {
        cancel();var before=Capture(reference,attributes,cancel);
        var symbols=new BlockPropertySymbols(ids.Layer.IsNull?null:((LayerTableRecord)tr.GetObject(ids.Layer,OpenMode.ForRead)).Name,Handle(ids.Layer),
            ids.Linetype.IsNull?null:((LinetypeTableRecord)tr.GetObject(ids.Linetype,OpenMode.ForRead)).Name,Handle(ids.Linetype));
        var expected=BlockRootProperties.Expected(before,operation.Properties,symbols);
        expected=CadAppearanceProperties.ExpectedBlock(expected,operation.Properties.ColorRgb,operation.Properties.Transparency);
        CadModifier.ApplyProperties(reference,operation,ids);
        expected=CadAppearanceProperties.NormalizeRequestedBlockColorIndex(expected,operation.Properties.ColorRgb,reference);
        if(!BlockMoveGeometry.SameProperties(expected,Capture(reference,attributes,cancel)))
            throw new DomainException("BLOCK_PROPERTY_MISMATCH","Native root property edit changed unrequested state or did not retain the requested values.");
    }
    public static void Preview(CadBlockMover.Preview preview,PropertiesSpec operation,CadModifier.PropertyIds ids,Transaction tr,Action cancel)=>
        Edit(preview.Reference,preview.Attributes,operation,ids,tr,cancel);
    public static void Apply(BlockReference reference,CadBlockMover.Resolved resolved,PropertiesSpec operation,CadModifier.PropertyIds ids,Transaction tr,Action cancel)
    {
        var attributes=resolved.Attributes.Select(id=>(AttributeReference)tr.GetObject(id,OpenMode.ForRead)).ToArray();
        Edit(reference,attributes,operation,ids,tr,cancel);
        if(!reference.AttributeCollection.Cast<ObjectId>().SequenceEqual(resolved.Attributes))
            throw new DomainException("BLOCK_PROPERTY_MISMATCH","Root property edit changed the owned attribute collection.");
    }
}
