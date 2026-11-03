using NanoCAD.Contracts;
using Teigha.DatabaseServices;
using Teigha.Geometry;

namespace NanoCADBridge;

internal static class CadTextEditor407
{
    private sealed record Settings(Point3d Position,Point3d Alignment,Vector3d Normal,ObjectId Style,
        TextHorizontalMode Horizontal,TextVerticalMode Vertical,double Height,double Width,double Rotation,double Oblique,double Thickness,bool MirrorX,bool MirrorY);
    internal static void CheckSource(Entity entity)
    {
        if(entity.GetType()!=typeof(DBText)||entity is not DBText text||text.HasFields||text.Annotative==AnnotativeStates.True||
            text.TextString.Any(char.IsControl)||text.TextString.Contains("%<")||text.TextString.Contains("%%"))
            throw new DomainException("UNSUPPORTED_TEXT_TYPE","Text replacement requires ordinary native single-line TEXT without fields, annotative contexts or CAD percent codes. Native justification and its alignment anchors are retained.");
        if(!Enum.IsDefined(typeof(TextHorizontalMode),text.HorizontalMode)||!Enum.IsDefined(typeof(TextVerticalMode),text.VerticalMode))
            throw new DomainException("UNSUPPORTED_TEXT_TYPE","Unknown native TEXT alignment requires its owning adapter.");
    }
    private static Settings Capture(DBText t)=>new(t.Position,t.AlignmentPoint,t.Normal,t.TextStyleId,t.HorizontalMode,t.VerticalMode,
        t.Height,t.WidthFactor,t.Rotation,t.Oblique,t.Thickness,t.IsMirroredInX,t.IsMirroredInY);
    internal static void Apply(Entity entity,ReplaceTextSpec edit,Database database,Action check)
    {
        check();CheckSource(entity);var text=(DBText)entity;
        if(text.TextString!=edit.ExpectedText)throw new DomainException("TEXT_CONTENT_CHANGED","Stored TEXT differs from expected_text after preceding edits.");
        var before=Capture(text);text.TextString=edit.Text;
        // The SDK's database-aware adjustment updates the derived Position for
        // center/right/top/bottom text, and fitted glyph sizing for Align/Fit.
        // Reverting that derived Position would move the visible anchor.
        if(before.Horizontal!=TextHorizontalMode.TextLeft||before.Vertical!=TextVerticalMode.TextBase)
        {check();text.AdjustAlignment(database);}
        check();var after=Capture(text);
        bool Near(double a,double b)=>double.IsFinite(a)&&double.IsFinite(b)&&Math.Abs(a-b)<=1e-10+Math.Max(Math.Abs(a),Math.Abs(b))*1e-12;
        bool Point(Point3d a,Point3d b)=>Near(a.X,b.X)&&Near(a.Y,b.Y)&&Near(a.Z,b.Z);
        bool Vector(Vector3d a,Vector3d b)=>Near(a.X,b.X)&&Near(a.Y,b.Y)&&Near(a.Z,b.Z);
        bool fitted=before.Horizontal is TextHorizontalMode.TextAlign or TextHorizontalMode.TextFit;
        bool defaultAnchor=before.Horizontal==TextHorizontalMode.TextLeft&&before.Vertical==TextVerticalMode.TextBase;
        if(text.TextString!=edit.Text||before.Style!=after.Style||before.Horizontal!=after.Horizontal||before.Vertical!=after.Vertical||
            !Vector(before.Normal,after.Normal)||!Near(before.Oblique,after.Oblique)||!Near(before.Thickness,after.Thickness)||
            before.MirrorX!=after.MirrorX||before.MirrorY!=after.MirrorY||
            (defaultAnchor||fitted)&&!Point(before.Position,after.Position)||!defaultAnchor&&!Point(before.Alignment,after.Alignment)||
            !fitted&&(!Near(before.Height,after.Height)||!Near(before.Width,after.Width)||!Near(before.Rotation,after.Rotation))||
            !double.IsFinite(after.Height)||after.Height<=0||!double.IsFinite(after.Width)||after.Width<=0)
            throw new DomainException("NATIVE_TEXT_MISMATCH","Native TEXT replacement changed requested content, meaningful alignment anchors or preserved font/format settings.");
    }
}
