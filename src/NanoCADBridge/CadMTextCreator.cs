using NanoCAD.Contracts;
using Teigha.DatabaseServices;
using Teigha.Geometry;

namespace NanoCADBridge;

internal static class CadMTextCreator
{
    internal static MText Create(MTextSpec spec,ObjectId style)
    {
        var text=new MText();
        try
        {
            text.TextStyleId=style;text.TextHeight=spec.Height;text.Width=spec.Width;
            text.Normal=Vector3d.ZAxis;text.Direction=Vector3d.XAxis;text.Attachment=AttachmentPoint.TopLeft;
            text.ColumnType=ColumnType.NoColumns;text.BackgroundFill=false;
            text.Contents=PlainMTextEncoding.Encode(spec.Paragraphs);text.Location=new Point3d(spec.Position.X,spec.Position.Y,0);
            Verify(text,spec,style);return text;
        }
        catch{text.Dispose();throw;}
    }
    internal static void Verify(MText text,MTextSpec spec,ObjectId style)
    {
        bool Near(double a,double b)=>double.IsFinite(a)&&Math.Abs(a-b)<=1e-7;
        if(text.GetType()!=typeof(MText)||text.HasFields||text.ColumnType!=ColumnType.NoColumns||text.BackgroundFill||
            text.TextStyleId!=style||text.Attachment!=AttachmentPoint.TopLeft||!text.Normal.IsEqualTo(Vector3d.ZAxis)||!text.Direction.IsEqualTo(Vector3d.XAxis)||
            !Near(text.Location.X,spec.Position.X)||!Near(text.Location.Y,spec.Position.Y)||!Near(text.Location.Z,0)||!Near(text.TextHeight,spec.Height)||!Near(text.Width,spec.Width)||
            MTextContent.PlainText(text.Contents)!=string.Join("\n",spec.Paragraphs))
            throw new DomainException("NATIVE_MTEXT_MISMATCH","Native MText does not retain the requested literal paragraphs, placement or style settings.");
    }
}
