using System.Text.Json;
using NanoCAD.Contracts;
using Teigha.DatabaseServices;
using Color=Teigha.Colors.Color;
using static NanoCADBridge.CadTableContentsReader364;
namespace NanoCADBridge;

// Native cell appearance while every indexed sibling/binding stays retained.
// Border handling follows the existing native cell appearance editor: only
// the same physical opposite border can expose an exact requested alias.
internal static class CadTableContentsAppearance365
{
    static bool Same(object? a,object? b)=>JsonSerializer.Serialize(a,Wire.Json)==JsonSerializer.Serialize(b,Wire.Json);
    internal static Color NativeColor(int? aci,RgbColor? rgb)=>rgb is {} value?Color.FromRgb(value.Red,value.Green,value.Blue):
        Color.FromColorIndex(aci==0?Teigha.Colors.ColorMethod.ByBlock:aci==256?Teigha.Colors.ColorMethod.ByLayer:Teigha.Colors.ColorMethod.ByAci,(short)aci!.Value);
    internal static ColorValue ExpectedColor(int? aci,RgbColor? rgb)=>rgb is {} value?new("true_color",null,value.Channels()):
        new(aci==0?"by_block":aci==256?"by_layer":"aci",aci is 0 or 256?null:aci,null);
    internal static ObjectId Font(string handle,Database db,Transaction tr,Action cancel)
    {
        cancel();var id=CadStructureWriter.Find(db,handle);var value=tr.GetObject(id,OpenMode.ForRead);
        Need(value.GetType()==typeof(TextStyleTableRecord)&&value is TextStyleTableRecord font&&font.OwnerId==db.TextStyleTableId&&
            !font.IsDependent&&!font.IsShapeFile&&!font.IsVertical&&!font.Name.Contains('|'),"Indexed font must be an existing ordinary horizontal local native text style.","STYLE_UNAVAILABLE");
        return id;
    }
    internal static string[] Coverage(string font,string raw,Database db,Transaction tr,Action cancel)
    {
        var id=Font(font,db,tr,cancel);var record=(TextStyleTableRecord)tr.GetObject(id,OpenMode.ForRead);var warnings=new List<string>();
        if(!string.IsNullOrEmpty(record.BigFontFileName)||string.IsNullOrWhiteSpace(record.Font.TypeFace))
            return new[]{"TABLE_FONT_COVERAGE_UNVERIFIED: Retained SHX/big-font or unresolved typeface needs its existing coverage adapter; review rendered text."};
        cancel();string? plain=MTextContent.PlainText(raw);cancel();
        // Use the established conservative decoder and FontCoverage engine;
        // raw controls/inline font names are never passed as visible glyphs.
        bool inline=false;
        for(int i=0;i<raw.Length-1;i++)
        {
            cancel();if(raw[i]!='\\')continue;char code=raw[++i];if(code=='\\'||code is not ('f' or 'F'))continue;
            for(int j=i+1;j<raw.Length;j++){cancel();if(raw[j]==';'){inline=j>i+1;break;}if(raw[j] is '{' or '}' or '\\')break;}
            if(inline)break;
        }
        try{FontCoverage.Require(tr,id,string.Empty);if(plain!=null&&!inline)FontCoverage.Require(tr,id,plain);}
        catch(DomainException error)when(error.Error.code=="FONT_CHECK_UNAVAILABLE")
        {warnings.Add("TABLE_FONT_COVERAGE_UNVERIFIED: Existing font availability/glyph engine could not verify this typeface; review rendered text.");}
        if(plain==null||inline)warnings.Add("TABLE_INLINE_FONT_COVERAGE_UNVERIFIED: Raw contents/runs are retained; conservative visible text or per-run font coverage is unavailable. Review actual rendered text.");
        return warnings.ToArray();
    }
    internal static TableCellFormatChanges Merge(TableCellFormatChanges? before,TableCellFormatChanges after)
    {
        if(before==null)return after;
        var grids=(before.Grids??Array.Empty<TableGridFormatChanges>()).ToDictionary(g=>g.Line,StringComparer.Ordinal);
        foreach(var change in after.Grids??Array.Empty<TableGridFormatChanges>())
        {
            if(!grids.TryGetValue(change.Line,out var old)){grids.Add(change.Line,change);continue;}
            bool color=change.ColorAci!=null||change.ColorRgb!=null;
            grids[change.Line]=old with{Visible=change.Visible??old.Visible,Lineweight=change.Lineweight??old.Lineweight,
                ColorAci=color?change.ColorAci:old.ColorAci,ColorRgb=color?change.ColorRgb:old.ColorRgb};
        }
        return before with{Alignment=after.Alignment??before.Alignment,Background=after.Background??before.Background,Grids=grids.Values.ToArray()};
    }
    internal static int ContentMask(TableCellFormatChanges? value)=>(value?.Alignment!=null?16:0)|(value?.Background!=null?512:0);
    internal static void Apply(Table table,int row,int column,TableCellFormatChanges p,Database db,Transaction tr,Action cancel)
    {
        if(p.Alignment!=null){cancel();table.SetAlignment(row,column,Enum.Parse<CellAlignment>(p.Alignment));}
        // Shared established setter, with no content-font/color properties here.
        CadTableAppearanceEditor.Apply(table,row,column,p,db,tr,cancel);
    }
    static TableCellRangeSpec Bounds(CellValue cell)=>cell.merged_range??new(cell.row,cell.column,cell.row,cell.column);
    static bool Shared(CellValue a,string line,CellValue b,string other)
    {
        var x=Bounds(a);var y=Bounds(b);
        return(line,other) switch{
            ("HorizontalTop","HorizontalBottom")=>y.BottomRow+1==x.TopRow&&y.LeftColumn<=x.RightColumn&&x.LeftColumn<=y.RightColumn,
            ("HorizontalBottom","HorizontalTop")=>x.BottomRow+1==y.TopRow&&y.LeftColumn<=x.RightColumn&&x.LeftColumn<=y.RightColumn,
            ("VerticalLeft","VerticalRight")=>y.RightColumn+1==x.LeftColumn&&y.TopRow<=x.BottomRow&&x.TopRow<=y.BottomRow,
            ("VerticalRight","VerticalLeft")=>x.RightColumn+1==y.LeftColumn&&y.TopRow<=x.BottomRow&&x.TopRow<=y.BottomRow,
            _=>false};
    }
    static int Mask(TableGridFormatChanges edit)=>(edit.Lineweight!=null?2:0)|(edit.ColorAci!=null||edit.ColorRgb!=null?8:0)|(edit.Visible!=null?16:0);
    static bool Matches(GridValue old,GridValue actual,TableGridFormatChanges? direct,TableGridFormatChanges[] shared)
    {
        var applicable=(direct==null?shared:shared.Prepend(direct).ToArray());int mask=applicable.Aggregate(0,(bits,edit)=>bits|Mask(edit));
        if((old.overrides&~mask)!=(actual.overrides&~mask))return false;
        bool ColorRequested(TableGridFormatChanges edit)=>edit.ColorAci!=null||edit.ColorRgb!=null;
        bool color=direct!=null&&ColorRequested(direct)?Same(actual.color,ExpectedColor(direct.ColorAci,direct.ColorRgb)):
            Same(actual.color,old.color)||shared.Any(edit=>ColorRequested(edit)&&Same(actual.color,ExpectedColor(edit.ColorAci,edit.ColorRgb)));
        bool weight=direct?.Lineweight is {} requestedWeight?actual.lineweight==requestedWeight:
            actual.lineweight==old.lineweight||shared.Any(edit=>edit.Lineweight==actual.lineweight);
        bool visible=direct?.Visible is {} requestedVisible?actual.visibility==(requestedVisible?"Visible":"Invisible"):
            actual.visibility==old.visibility||shared.Any(edit=>edit.Visible is {} flag&&actual.visibility==(flag?"Visible":"Invisible"));
        return color&&weight&&visible&&Same(old with{overrides=actual.overrides,color=actual.color,lineweight=actual.lineweight,visibility=actual.visibility},actual);
    }
    internal static void ValidateBorders(Snapshot before,Dictionary<int,TableCellFormatChanges> changes,Action cancel)
    {
        // Contradictory final requests on a single physical border cannot both
        // be retained. Reject before write instead of selecting a hidden winner.
        foreach(var first in changes)foreach(var second in changes)
        {
            cancel();if(first.Key>=second.Key)continue;
            foreach(var a in first.Value.Grids??Array.Empty<TableGridFormatChanges>())foreach(var b in second.Value.Grids??Array.Empty<TableGridFormatChanges>())
            {
                cancel();if(!Shared(before.Cells[first.Key],a.Line,before.Cells[second.Key],b.Line))continue;
                bool ca=a.ColorAci!=null||a.ColorRgb!=null,cb=b.ColorAci!=null||b.ColorRgb!=null;
                Need(!(a.Visible!=null&&b.Visible!=null&&a.Visible!=b.Visible||a.Lineweight!=null&&b.Lineweight!=null&&a.Lineweight!=b.Lineweight||
                    ca&&cb&&!Same(ExpectedColor(a.ColorAci,a.ColorRgb),ExpectedColor(b.ColorAci,b.ColorRgb))),"Final cell requests assign conflicting appearance to the same physical shared border.","INVALID_ARGUMENT");
            }
        }
    }
    internal static FormatValue VerifyFormat(Snapshot before,CellValue cell,FormatValue prior,FormatValue actual,Dictionary<int,TableCellFormatChanges> changes,Action cancel)
    {
        changes.TryGetValue(checked(cell.row*before.Columns+cell.column),out var p);var expected=prior;
        if(p!=null)
        {
            expected=expected with{alignment=p.Alignment??prior.alignment};
            if(p.Background is {} bg)expected=expected with{background_none=bg.None,background_color=bg.None?actual.background_color:ExpectedColor(bg.ColorAci,bg.ColorRgb)};
        }
        Need(Same(expected with{grids=actual.grids},actual),"Native cell appearance changed an unrequested default/margin or failed alignment/background readback.","NATIVE_TABLE_MISMATCH");
        foreach(var old in prior.grids)
        {
            cancel();var current=actual.grids.Single(g=>g.line==old.line);var direct=p?.Grids?.SingleOrDefault(g=>g.Line==old.line);
            var shared=new List<TableGridFormatChanges>();
            foreach(var selected in changes)
            {
                cancel();foreach(var edit in selected.Value.Grids??Array.Empty<TableGridFormatChanges>())
                    if(Shared(before.Cells[selected.Key],edit.Line,cell,old.line))shared.Add(edit);
            }
            Need(Matches(old,current,direct,shared.ToArray()),"Native grid appearance failed requested properties or changed an unrelated line/property.","NATIVE_TABLE_MISMATCH");
        }
        return expected with{grids=actual.grids};
    }
    internal static HashSet<int> CellOverrideIds(Snapshot before,CellValue cell,Dictionary<int,TableCellFormatChanges> changes,Action cancel)
    {
        var allowed=new HashSet<int>();changes.TryGetValue(checked(cell.row*before.Columns+cell.column),out var p);
        if(p?.Alignment!=null)allowed.Add(130);if(p?.Background!=null){allowed.Add(131);allowed.Add(132);}
        foreach(var selected in changes)foreach(var edit in selected.Value.Grids??Array.Empty<TableGridFormatChanges>())
        foreach(var line in new[]{"HorizontalTop","VerticalRight","HorizontalBottom","VerticalLeft"})
        {
            cancel();if(!(selected.Key==cell.row*before.Columns+cell.column&&edit.Line==line)&&!Shared(before.Cells[selected.Key],edit.Line,cell,line))continue;
            int side=line switch{"HorizontalTop"=>0,"VerticalRight"=>1,"HorizontalBottom"=>2,_=>3};
            // Pinned native TableStyleOverrides cell IDs136..147, preserving
            // every unselected side/property and all non-grid override IDs.
            if(edit.ColorAci!=null||edit.ColorRgb!=null)allowed.Add(136+side);
            if(edit.Lineweight!=null)allowed.Add(140+side);if(edit.Visible!=null)allowed.Add(144+side);
        }
        return allowed;
    }
}
