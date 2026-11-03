using System.Text;
using System.Text.Json;

namespace NanoCAD.Contracts;

public sealed record TableGridLineLayout341(string Line,string? LineStyle=null,double? DoubleSpacing=null);
public sealed record TableGridCellLayout341(int Row,int Column,TableGridLineLayout341[] Grids);
public sealed record TableGridLayoutEdit341(string Kind,string[] Targets,string TableToken,TableGridCellLayout341[] Cells):EditSpec(Kind,Targets);

public static class TableGridLayoutValidation341
{
    public const string Operation="set_table_grid_layout";
    public static readonly string[] Lines={"HorizontalTop","HorizontalInside","HorizontalBottom","VerticalLeft","VerticalInside","VerticalRight"};
    public static bool Handles(string operation)=>operation==Operation;
    private static DomainException Invalid(string path,string message)=>new("INVALID_ARGUMENT",path+": "+message,false,new{path});
    private static JsonElement Required(JsonElement value,string name,string path)=>value.TryGetProperty(name,out var found)?found:throw Invalid(path+"."+name,"Required field.");
    private static void Shape(JsonElement value,string path,params string[] names)
    {
        if(value.ValueKind!=JsonValueKind.Object)throw Invalid(path,"Expected object.");
        var seen=new HashSet<string>(StringComparer.Ordinal);
        foreach(var property in value.EnumerateObject())if(!seen.Add(property.Name)||!names.Contains(property.Name,StringComparer.Ordinal))throw Invalid(path+"."+property.Name,"Unknown or duplicate field.");
    }
    private static string Text(JsonElement value,string path)
    {
        if(value.ValueKind!=JsonValueKind.String)throw Invalid(path,"Expected string.");
        try{string result=value.GetString()!;new UTF8Encoding(false,true).GetByteCount(result);return result;}
        catch(Exception error)when(error is InvalidOperationException or EncoderFallbackException){throw Invalid(path,"Invalid Unicode scalar text.");}
    }
    private static int Index(JsonElement value,string path)=>value.ValueKind==JsonValueKind.Number&&value.TryGetInt32(out int result)&&result>=0?result:throw Invalid(path,"Expected nonnegative native integer index.");
    public static TableGridLayoutEdit341 Parse(JsonElement input,string path,string[] handles)
    {
        if(handles.Length!=1)throw Invalid(path+".handles","Select one existing native Table.");
        Shape(input,path,"operation","handles","table_token","cells");
        if(Text(Required(input,"operation",path),path+".operation")!=Operation)throw Invalid(path+".operation","Unknown grid-layout operation.");
        string token=Text(Required(input,"table_token",path),path+".table_token");
        if(token.Length!=64||token.Any(c=>!"0123456789ABCDEF".Contains(c)))throw Invalid(path+".table_token","Use the exact fresh uppercase geometry.editing.table_token.");
        var cells=Required(input,"cells",path);
        if(cells.ValueKind!=JsonValueKind.Array||cells.GetArrayLength()==0)throw Invalid(path+".cells","Select at least one native anchor cell.");
        var result=new List<TableGridCellLayout341>();var selected=new HashSet<(int,int)>();int ordinal=0;
        foreach(var cell in cells.EnumerateArray())
        {
            string at=path+".cells["+ordinal+++"]";Shape(cell,at,"row","column","grids");
            int row=Index(Required(cell,"row",at),at+".row"),column=Index(Required(cell,"column",at),at+".column");
            if(!selected.Add((row,column)))throw Invalid(at,"Duplicate native cell selection.");
            var grids=Required(cell,"grids",at);
            if(grids.ValueKind!=JsonValueKind.Array||grids.GetArrayLength()==0)throw Invalid(at+".grids","Select at least one native grid slot.");
            var lines=new HashSet<string>(StringComparer.Ordinal);var changes=new List<TableGridLineLayout341>();int lineIndex=0;
            foreach(var grid in grids.EnumerateArray())
            {
                string p=at+".grids["+lineIndex+++"]";Shape(grid,p,"line","line_style","double_spacing");
                string line=Text(Required(grid,"line",p),p+".line");
                if(!Lines.Contains(line,StringComparer.Ordinal)||!lines.Add(line))throw Invalid(p+".line","Select distinct single native grid slots.");
                string? style=grid.TryGetProperty("line_style",out var nativeStyle)?Text(nativeStyle,p+".line_style"):null;
                if(style!=null&&style is not ("Single" or "Double"))throw Invalid(p+".line_style","Expected native Single or Double.");
                double? spacing=null;
                if(grid.TryGetProperty("double_spacing",out var length))
                {
                    if(length.ValueKind!=JsonValueKind.Number||!length.TryGetDouble(out double value)||!double.IsFinite(value)||value<0||value>1e9)
                        throw Invalid(p+".double_spacing","Expected finite nonnegative drawing length up to1e9.");
                    spacing=value;
                }
                if(style==null&&spacing==null)throw Invalid(p,"Request line_style or double_spacing.");
                changes.Add(new(line,style,spacing));
            }
            result.Add(new(row,column,changes.ToArray()));
        }
        return new(Operation,handles,token,result.ToArray());
    }
    public static void Batch(IReadOnlyList<EditSpec> operations)
    {
        if(operations.Any(edit=>edit is TableGridLayoutEdit341)&&operations.Count!=1)
            throw new DomainException("INVALID_OPERATION_ORDER","Native grid layout must be the sole operation; inspect again for its complete fresh table token.");
    }
    // Native cell boundaries use inclusive indices. Merged ranges can share
    // only part of a border; inside slots are stored settings, not outer edges.
    public static bool SharedBorder(TableCellRangeSpec a,string aLine,TableCellRangeSpec b,string bLine)=>aLine switch
    {
        "HorizontalTop"=>bLine=="HorizontalBottom"&&(long)b.BottomRow+1==a.TopRow&&a.LeftColumn<=b.RightColumn&&b.LeftColumn<=a.RightColumn,
        "HorizontalBottom"=>bLine=="HorizontalTop"&&(long)a.BottomRow+1==b.TopRow&&a.LeftColumn<=b.RightColumn&&b.LeftColumn<=a.RightColumn,
        "VerticalLeft"=>bLine=="VerticalRight"&&(long)b.RightColumn+1==a.LeftColumn&&a.TopRow<=b.BottomRow&&b.TopRow<=a.BottomRow,
        "VerticalRight"=>bLine=="VerticalLeft"&&(long)a.RightColumn+1==b.LeftColumn&&a.TopRow<=b.BottomRow&&b.TopRow<=a.BottomRow,
        _=>false
    };
    public static bool Conflicting(TableGridLineLayout341 a,TableGridLineLayout341 b)=>
        a.LineStyle!=null&&b.LineStyle!=null&&a.LineStyle!=b.LineStyle||a.DoubleSpacing!=null&&b.DoubleSpacing!=null&&a.DoubleSpacing!=b.DoubleSpacing;
}
