using System.Runtime.InteropServices;
using System.Text;
using NanoCAD.Contracts;
using Teigha.DatabaseServices;
namespace NanoCADBridge;
internal static class FontCoverage
{
    public static void Require(Transaction tr,ObjectId style,string text)
    {
        if(style.IsNull)return;
        var record=(TextStyleTableRecord)tr.GetObject(style,OpenMode.ForRead);var font=record.Font;
        if(!record.IsShapeFile&&string.IsNullOrEmpty(record.BigFontFileName))GdiFontCoverage.Require(font.TypeFace,font.Bold,font.Italic,text);
    }
    public static ObjectId DimensionStyle(Dimension dimension)
    {
        using var data=dimension.GetDimstyleData();
        return data.Dimtxsty;
    }
    public static TextStyleTableRecord Resolve(SetTextStyleFontEdit edit,Database db,Transaction tr,Action cancel,Action<string>? warning=null)
    {
        var obj=tr.GetObject(CadStructureWriter.Find(db,edit.Handle),OpenMode.ForRead);
        if(obj is not TextStyleTableRecord style||style.IsDependent||style.IsVertical||style.IsShapeFile||style.OwnerId!=db.TextStyleTableId||!string.IsNullOrEmpty(style.BigFontFileName))throw new DomainException("UNSUPPORTED_STYLE_TYPE","Font change requires an ordinary horizontal local text style without a big font.");
        if(TextStyleMetadata.DescribeTypeface(style.Font.TypeFace)!=edit.ExpectedTypeface)throw new DomainException("STYLE_CHANGED","Typeface differs from expected_typeface.");
        var text=new StringBuilder(edit.RequiredText);var budget=new ModelInventoryBudget(cancel);budget.Add(edit.RequiredText);
        var notices=new HashSet<string>(StringComparer.Ordinal);
        foreach(ObjectId block in (BlockTable)tr.GetObject(db.BlockTableId,OpenMode.ForRead))
        foreach(ObjectId id in (BlockTableRecord)tr.GetObject(block,OpenMode.ForRead))
        {
            cancel();
            Visit(tr,tr.GetObject(id,OpenMode.ForRead), (styleId,value)=> {if(styleId==style.ObjectId){budget.Add(value);text.Append(value);}},
                (styleId,reason)=>{if(styleId==style.ObjectId&&notices.Add(reason))warning?.Invoke(reason);},cancel);
        }
        try{GdiFontCoverage.Require(edit.Typeface,style.Font.Bold,style.Font.Italic,text.ToString());}
        catch(DomainException error) when(error.Error.code=="FONT_CHECK_UNAVAILABLE")
        {warning?.Invoke("FONT_CHECK_UNAVAILABLE: Requested font availability or required glyph coverage could not be verified; review actual rendered text.");}
        return style;
    }
    public static void Visit(Transaction tr,DBObject entity,Action<ObjectId,string> visit,Action<ObjectId,string>? unverified=null,Action? cancel=null)
    {
        cancel?.Invoke();
        void MTextValue(MText text)
        {
            string raw=text.Contents;new ModelInventoryBudget(cancel).Add(raw);
            cancel?.Invoke();string? plain=MTextContent.PlainText(raw);cancel?.Invoke();
            if(plain==null)
                unverified?.Invoke(text.TextStyleId,"MTEXT_VISIBLE_TEXT_UNVERIFIED: Stored Contents cannot be fully decoded by the conservative text adapter; raw control codes were not checked as glyphs. Review actual rendered text.");
            else if(InlineFont(raw,cancel))
                unverified?.Invoke(text.TextStyleId,"MTEXT_INLINE_FONT_COVERAGE_UNVERIFIED: Base font availability is checked separately; retained inline font runs are not verified as base-style glyphs. Review actual rendered text.");
            else visit(text.TextStyleId,plain);
        }
        if(entity is DBText attribute && NativeAttributeMText377.IsMultiline(attribute))
        {
            var owned = NativeAttributeMText377.Acquire(attribute);
            try { if(owned != null) MTextValue(owned); }
            finally { NativeAttributeMText377.Release(owned); }
        }
        else if(entity is DBText t)visit(t.TextStyleId,t.TextString);
        else if(entity is MText m)MTextValue(m);
        else if(entity is FeatureControlFrame frame)unverified?.Invoke(frame.TextStyleId,"TOLERANCE_NATIVE_GLYPHS_UNVERIFIED: Native GD&T control codes/glyph runs are not interpreted as literal base-font text; review actual native frame glyphs.");
        else if(entity is Dimension d)visit(DimensionStyle(d),d.DimensionText);
        else if(entity is MLeader leader && leader.ContentType==ContentType.MTextContent) {
            var owned=leader.MText;
            // The installed getter returns an owned detached wrapper. An
            // unexpected resident alias must never enter a disposal scope.
            if(owned!=null&&!owned.ObjectId.IsNull)throw new DomainException("API_EXCEPTION","MLeader.MText returned a resident wrapper; native ownership is unresolved.");
            using var text=owned;if(text!=null)MTextValue(text);
        }
        else if(entity is Table table) {
            for(int r=0;r<table.Rows.Count;r++)for(int c=0;c<table.Columns.Count;c++)
            for(int i=0;i<table.Cells[r,c].Contents.Count;i++)
            {cancel?.Invoke();if(table.GetContentTypes(r,c,i)==CellContentTypes.Value)visit(table.GetTextStyleId(r,c,i),table.GetTextString(r,c,i));}
        }
        else if(entity is BlockReference block)foreach(ObjectId id in block.AttributeCollection)Visit(tr,tr.GetObject(id,OpenMode.ForRead),visit,unverified,cancel);
    }
    private static bool InlineFont(string contents,Action? cancel)
    {
        for(int i=0;i<contents.Length-1;i++)
        {
            cancel?.Invoke();if(contents[i]!='\\')continue;char code=contents[++i];if(code=='\\'||code is not ('f' or 'F'))continue;
            for(int j=i+1;j<contents.Length;j++){cancel?.Invoke();if(contents[j]==';')return j>i+1;if(contents[j] is '{' or '}' or '\\')break;}
        }
        return false;
    }
    public static void RequireEntity(Transaction tr,DBObject entity,Action<string>? warning=null,Action? cancel=null)
    {
        var notices=new HashSet<string>(StringComparer.Ordinal);
        void Notice(string reason){if(notices.Add(reason))warning?.Invoke(reason);}
        void Check(ObjectId id,string value)
        {
            if(id.IsNull){Notice("FONT_CHECK_UNAVAILABLE: Native base text style is unresolved; review actual rendered text.");return;}
            var record=(TextStyleTableRecord)tr.GetObject(id,OpenMode.ForRead);
            if(record.IsShapeFile||!string.IsNullOrEmpty(record.BigFontFileName)||string.IsNullOrWhiteSpace(record.Font.TypeFace))
            {Notice("FONT_CHECK_UNAVAILABLE: SHX/big-font availability and glyph coverage need their native font adapter; review actual rendered text.");return;}
            try{Require(tr,id,value);}
            catch(DomainException error) when(error.Error.code=="FONT_CHECK_UNAVAILABLE")
            {Notice("FONT_CHECK_UNAVAILABLE: Base font availability or glyph coverage could not be verified; review actual rendered text.");}
        }
        Visit(tr,entity,Check,(id,reason)=>
        {
            // Family substitution is still a proven error. Unknown run text
            // must not be replaced by raw formatting codes or an empty-text
            // assertion of complete glyph coverage.
            Check(id,string.Empty);
            Notice(reason);
        },cancel);
    }
    public static void RefreshDimensions(Database db,Transaction tr,ObjectId style,Action cancel)
    {
        var dimensions=new List<ObjectId>();
        foreach(ObjectId block in (BlockTable)tr.GetObject(db.BlockTableId,OpenMode.ForRead))
        foreach(ObjectId id in (BlockTableRecord)tr.GetObject(block,OpenMode.ForRead)) {
            cancel();
            if(tr.GetObject(id,OpenMode.ForRead) is Dimension d && DimensionStyle(d)==style)dimensions.Add(id);
        }
        foreach(var id in dimensions){cancel();((Dimension)tr.GetObject(id,OpenMode.ForWrite)).RecomputeDimensionBlock(true);}
    }
    public static void Apply(TextStyleTableRecord style,SetTextStyleFontEdit edit)
    {
        var f=style.Font;style.Font=new Teigha.GraphicsInterface.FontDescriptor(edit.Typeface,f.Bold,f.Italic,f.CharacterSet,f.PitchAndFamily);
        if(style.Font.TypeFace!=edit.Typeface)throw new DomainException("FONT_UNAVAILABLE","Native font setter did not retain the requested family.");
    }
}
