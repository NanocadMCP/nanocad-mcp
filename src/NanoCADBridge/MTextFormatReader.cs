using NanoCAD.Contracts;
using Teigha.DatabaseServices;

namespace NanoCADBridge;

internal static class MTextFormatReader
{
    private static double Number(double value)=>double.IsFinite(value)?value:
        throw new DomainException("OBJECT_READ_FAILED","MText stored formatting contains a non-finite value.");
    internal static MTextFormatting Read(MText text,EntityRef entity,CancellationToken cancellation)
    {
        cancellation.ThrowIfCancellationRequested();var style=text.TextStyleId;
        if(style.IsNull||!style.IsValid)throw new DomainException("OBJECT_READ_FAILED","MText text style identity is unavailable.");
        // SDK getter constructs an owned Color wrapper; do not keep it alive
        // beyond this DTO capture or infer the current drawing-window color.
        using var color=text.BackgroundFillColor;
        var storedColor=new{
            method=color.IsByLayer?"by_layer":color.IsByBlock?"by_block":color.IsByAci?"aci":color.IsByColor?"true_color":color.ColorMethod.ToString(),
            aci=color.IsByAci?(int?)color.ColorIndex:null,
            rgb=color.IsByColor?new[]{(int)color.Red,color.Green,color.Blue}:null};
        var result=new MTextFormatting(new(entity.bridge_instance_id,entity.document_id,Validation.NormalizeHandle(style.Handle.ToString())),
            text.LineSpacingStyle.ToString(),Number(text.LineSpacingFactor),text.FlowDirection.ToString(),text.ColumnType.ToString(),
            new(text.BackgroundFill,text.UseBackgroundColor,Number(text.BackgroundScaleFactor),storedColor),
            CadMTextFormatEditor.TryRead(text,cancellation.ThrowIfCancellationRequested));
        cancellation.ThrowIfCancellationRequested();return result;
    }
}
