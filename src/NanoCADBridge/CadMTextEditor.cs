using NanoCAD.Contracts;
using Teigha.DatabaseServices;
using Teigha.Geometry;

namespace NanoCADBridge;

internal static class CadMTextEditor
{
    private sealed record Settings(Point3d Location,Vector3d Normal,Vector3d Direction,ObjectId Style,double Width,double Height,
        AttachmentPoint Attachment,LineSpacingStyle Spacing,double SpacingFactor,FlowDirection Flow,
        bool Background,bool DrawingBackground,double BackgroundScale,string BackgroundColor,byte BackgroundAlpha);

    internal static void CheckSource(Entity entity)
    {
        if(entity.GetType()!=typeof(MText) || entity is not MText text || text.HasFields || text.ColumnType!=ColumnType.NoColumns ||
            text.Annotative==AnnotativeStates.True || !Enum.IsDefined(typeof(AttachmentPoint),text.Attachment)||
            !double.IsFinite(text.Width)||text.Width<0 || !double.IsFinite(text.LineSpacingFactor)||text.LineSpacingFactor<=0)
            throw new DomainException("UNSUPPORTED_MTEXT_CONTENT","Replacement requires ordinary native MTEXT without fields, columns or annotative contexts; any retained attachment, background and one uniform formatting run are supported.");
        _=UniformMTextContent407.Capture(text.Contents);
    }
    private static Settings Capture(MText text)
    {
        using var color=text.BackgroundFillColor;
        return new(text.Location,text.Normal,text.Direction,text.TextStyleId,text.Width,text.TextHeight,
            text.Attachment,text.LineSpacingStyle,text.LineSpacingFactor,text.FlowDirection,text.BackgroundFill,text.UseBackgroundColor,
            text.BackgroundScaleFactor,$"{color.ColorMethod}:{color.ColorIndex}:{color.Red}:{color.Green}:{color.Blue}",text.BackgroundTransparency.Alpha);
    }
    internal static void CheckFont(MText text,ReplaceMTextSpec edit,Transaction tr,Action<string>? warning=null)
    {
        var envelope=UniformMTextContent407.Capture(text.Contents);string content=string.Join("\n",edit.Paragraphs);
        if(envelope.Typeface is {} face)
        {
            if(face.EndsWith(".shx",StringComparison.OrdinalIgnoreCase))
            {warning?.Invoke("FONT_CHECK_UNAVAILABLE: Retained inline SHX font coverage is unverified; inspect the requested native glyphs.");return;}
            try{GdiFontCoverage.Require(face,envelope.Bold,envelope.Italic,content);}
            catch(DomainException error) when(error.Error.code=="FONT_CHECK_UNAVAILABLE")
            {warning?.Invoke("FONT_CHECK_UNAVAILABLE: Retained inline font coverage is unverified; inspect the requested native glyphs.");}
        }
        else FontCoverage.Require(tr,text.TextStyleId,content);
    }

    internal static void Apply(Entity entity,ReplaceMTextSpec edit,Action check,Action<string>? warning=null)
    {
        check();CheckSource(entity);var text=(MText)entity;
        var before=Capture(text);string encoded=UniformMTextContent407.Replace(text.Contents,edit.ExpectedParagraphs,edit.Paragraphs);
        if(warning!=null&&text.Database?.TransactionManager.TopTransaction is {} tr)CheckFont(text,edit,tr,warning);
        text.Contents=encoded;check();CheckSource(text);var after=Capture(text);
        bool Near(double a,double b)=>double.IsFinite(a)&&double.IsFinite(b)&&Math.Abs(a-b)<=1e-7;
        bool Point(Point3d a,Point3d b)=>Near(a.X,b.X)&&Near(a.Y,b.Y)&&Near(a.Z,b.Z);
        bool Vector(Vector3d a,Vector3d b)=>Near(a.X,b.X)&&Near(a.Y,b.Y)&&Near(a.Z,b.Z);
        if(text.Contents!=encoded || !Point(before.Location,after.Location)||!Vector(before.Normal,after.Normal)||!Vector(before.Direction,after.Direction)||
            before.Style!=after.Style||before.Attachment!=after.Attachment||before.Spacing!=after.Spacing||before.Flow!=after.Flow||
            !Near(before.Width,after.Width)||!Near(before.Height,after.Height)||!Near(before.SpacingFactor,after.SpacingFactor)||
            before.Background!=after.Background||before.DrawingBackground!=after.DrawingBackground||!Near(before.BackgroundScale,after.BackgroundScale)||
            before.BackgroundColor!=after.BackgroundColor||before.BackgroundAlpha!=after.BackgroundAlpha)
            throw new DomainException("NATIVE_MTEXT_MISMATCH","Native replacement changed requested paragraphs or preserved MText placement/style settings.");
    }
}
