using NanoCAD.Contracts;
using Teigha.DatabaseServices;

namespace NanoCADBridge;

internal static class TextFormatReader
{
    internal static TextFormatting Read(DBText text,EntityRef entity,CancellationToken cancellation)
    {
        cancellation.ThrowIfCancellationRequested();
        var style=text.TextStyleId;double oblique=text.Oblique,thickness=text.Thickness;
        if(style.IsNull||!style.IsValid||!double.IsFinite(oblique)||!double.IsFinite(thickness))
            throw new DomainException("OBJECT_READ_FAILED","Text stored formatting is unavailable or non-finite.");
        var result=new TextFormatting(new(entity.bridge_instance_id,entity.document_id,Validation.NormalizeHandle(style.Handle.ToString())),
            oblique,thickness,text.IsMirroredInX,text.IsMirroredInY,CadTextFormat442.TryRead(text,cancellation.ThrowIfCancellationRequested));
        cancellation.ThrowIfCancellationRequested();return result;
    }
}
