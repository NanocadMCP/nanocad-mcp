using NanoCAD.Contracts;
using Teigha.DatabaseServices;

namespace NanoCADBridge;

// Shared by structural reads and style preflight/preview. New getters still
// require native read gates before candidate 0.8 is accepted or registered.
internal static class TextStyleMetadataReader
{
    public static TextStyleProperties Read(TextStyleTableRecord source, Action check)
    {
        check();
        if (source.GetType() != typeof(TextStyleTableRecord))
            throw new DomainException("OBJECT_READ_FAILED", "Expected an exact native text style record.");
        var font = source.Font; // FontDescriptor is a value type in the installed SDK.
        var descriptor = new TextFontDescriptor(font.TypeFace, font.Bold, font.Italic, font.CharacterSet, font.PitchAndFamily);
        check();
        TextFontMetadata metadata;
        using (var buffer = source.XData)
        {
            try
            {
                metadata = TextFontMetadata.From(buffer is null ? null : StyleFontMetadata.Read(buffer.GetEnumerator(), value =>
                {
                    if (value is not TypedValue item) throw new DomainException("OBJECT_READ_FAILED", "Unexpected native XData element.");
                    return new FontMetadataValue(item.TypeCode, item.Value);
                }, check));
            }
            catch (DomainException error) when (error.Error.code == "UNSUPPORTED_STYLE_TYPE")
            { metadata = TextFontMetadata.Unsupported(); }
        }
        check();
        var facts = new TextStyleFacts(source.Name, source.IsDependent, source.FileName, source.BigFontFileName,
            source.TextSize, source.XScale, source.IsVertical, source.IsShapeFile, descriptor, source.ObliquingAngle,
            source.FlagBits, (int)source.Annotative, !source.ExtensionDictionary.IsNull, source.HasFields, metadata);
        check();
        return TextStyleMetadata.Describe(facts);
    }
}
