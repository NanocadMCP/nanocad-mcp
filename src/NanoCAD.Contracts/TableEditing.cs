using System.Text.Json;
using System.Text.Json.Serialization;

namespace NanoCAD.Contracts;

public sealed record TableCellRangeSpec([property:JsonPropertyName("top_row")]int TopRow,[property:JsonPropertyName("left_column")]int LeftColumn,
    [property:JsonPropertyName("bottom_row")]int BottomRow,[property:JsonPropertyName("right_column")]int RightColumn);
public sealed record TableCellBackgroundChanges(bool None,int? ColorAci=null,RgbColor? ColorRgb=null);
public sealed record TableGridFormatChanges(string Line,bool? Visible,int? ColorAci,RgbColor? ColorRgb,int? Lineweight,string? LinetypeHandle);
public sealed record TableCellFormatChanges(string? Alignment,double? TextHeight,string? TextStyleHandle,int? ColorAci,
    RgbColor? ColorRgb=null,TableCellBackgroundChanges? Background=null,TableGridFormatChanges[]? Grids=null);
public sealed record TableCellFormatEdit(int Row,int Column,TableCellFormatChanges Properties);
public sealed record TableCellTextEdit(int Row,int Column,string Text);
public sealed record TableEditingSpec(string Kind,string[] Targets,string TableToken,int? Index=null,int? Count=null,
    double? Size=null,string[][]? Values=null,TableCellRangeSpec? Range=null,TableCellFormatEdit[]? Cells=null,TableCellTextEdit[]? TextCells=null,TableFormulaReplacement354[]? FormulaReplacements=null):EditSpec(Kind,Targets);

public static class TableEditingValidation
{
    public static readonly string[] Operations={"insert_table_rows","delete_table_rows","insert_table_columns","delete_table_columns",
        "merge_table_cells","unmerge_table_cells","set_table_cell_format","set_table_cell_text"};
    public static readonly string[] Alignments={"TopLeft","TopCenter","TopRight","MiddleLeft","MiddleCenter","MiddleRight","BottomLeft","BottomCenter","BottomRight"};
    public static readonly string[] GridLines={"HorizontalTop","HorizontalInside","HorizontalBottom","VerticalLeft","VerticalInside","VerticalRight"};
    public static bool Handles(EditSpec edit)=>edit is TableEditingSpec;
    public static bool Handles(string operation)=>Operations.Contains(operation,StringComparer.Ordinal);
    private static DomainException Invalid(string path,string message)=>new("INVALID_ARGUMENT",path+": "+message,false,new{path});
    private static JsonElement Required(JsonElement value,string name,string path)=>value.TryGetProperty(name,out var property)?property:throw Invalid(path+"."+name,"Required field.");
    private static void Shape(JsonElement value,string path,params string[] fields)
    {
        if(value.ValueKind!=JsonValueKind.Object)throw Invalid(path,"Expected object.");
        var names=new HashSet<string>(StringComparer.Ordinal);
        foreach(var property in value.EnumerateObject())if(!names.Add(property.Name)||!fields.Contains(property.Name,StringComparer.Ordinal))throw Invalid(path+"."+property.Name,"Unknown or duplicate field.");
    }
    private static int Index(JsonElement value,string path,bool positive=false)=>value.ValueKind==JsonValueKind.Number&&value.TryGetInt32(out var n)&&n>=(positive?1:0)?n:throw Invalid(path,"Expected a nonnegative integer index or positive count.");
    private static string Text(JsonElement value,string path,int max=255)
    {
        if(value.ValueKind!=JsonValueKind.String)throw Invalid(path,"Expected bounded text.");
        string text;
        try{text=value.GetString()!;}
        catch(InvalidOperationException){throw Invalid(path,"Expected valid Unicode scalar text.");}
        if(text.Length>max)throw Invalid(path,"Expected bounded text.");
        return text;
    }
    private static double Size(JsonElement value,string path)=>value.ValueKind==JsonValueKind.Number&&value.TryGetDouble(out var n)&&double.IsFinite(n)&&n is >=0.001 and <=1e6?n:throw Invalid(path,"Expected0.001..1000000 drawing millimeters.");
    private static int? Aci(JsonElement value,string path)
    {
        int? aci=value.TryGetProperty("color_aci",out var color)?Index(color,path+".color_aci"):null;
        if(aci>256)throw Invalid(path+".color_aci","Expected ACI0..256;0=ByBlock,256=ByLayer.");
        return aci;
    }
    private static TableCellBackgroundChanges Background(JsonElement value,string path)
    {
        Shape(value,path,"mode","color_aci","color_rgb");
        string mode=Text(Required(value,"mode",path),path+".mode");
        if(mode=="none"){Shape(value,path,"mode");return new(true);}
        Shape(value,path,"mode","color_aci","color_rgb");
        if(mode!="color")throw Invalid(path+".mode","Expected none or color.");
        var aci=Aci(value,path);var rgb=AppearanceValidation.Parse(value,path).ColorRgb;
        if(aci==null&&rgb==null)throw Invalid(path,"Color background requires color_aci or color_rgb.");
        return new(false,aci,rgb);
    }
    private static TableGridFormatChanges[] Grids(JsonElement value,string path)
    {
        if(value.ValueKind!=JsonValueKind.Array||value.GetArrayLength()==0)throw Invalid(path,"Select at least one explicit native grid line.");
        var result=new List<TableGridFormatChanges>();var seen=new HashSet<string>(StringComparer.Ordinal);
        foreach(var grid in value.EnumerateArray())
        {
            Shape(grid,path,"line","visible","color_aci","color_rgb","lineweight_hundredth_mm","linetype_handle");
            string line=Text(Required(grid,"line",path),path+".line");
            if(!GridLines.Contains(line,StringComparer.Ordinal)||!seen.Add(line))throw Invalid(path+".line","Use a distinct published single grid line, without combined masks.");
            if(grid.EnumerateObject().Count()==1)throw Invalid(path,"At least one grid appearance field required.");
            bool? visible=null;
            if(grid.TryGetProperty("visible",out var visibility))visible=visibility.ValueKind is JsonValueKind.True or JsonValueKind.False?visibility.GetBoolean():throw Invalid(path+".visible","Expected boolean.");
            int? weight=null;
            if(grid.TryGetProperty("lineweight_hundredth_mm",out var lineweight))
            {
                if(lineweight.ValueKind!=JsonValueKind.Number||!lineweight.TryGetInt32(out int parsed)||!ModifyValidation.Lineweights.Contains(parsed))throw Invalid(path+".lineweight_hundredth_mm","Use a published native lineweight value.");
                weight=parsed;
            }
            var aci=Aci(grid,path);var rgb=AppearanceValidation.Parse(grid,path).ColorRgb;
            string? linetype=grid.TryGetProperty("linetype_handle",out var id)?Validation.NormalizeHandle(Text(id,path+".linetype_handle",16)):null;
            result.Add(new(line,visible,aci,rgb,weight,linetype));
        }
        return result.ToArray();
    }
    public static string Literal(JsonElement value,string path)
    {
        var text=Text(value,path,4096);
        if(text.Any(char.IsControl)||text.IndexOfAny(new[]{'\\','{','}'})>=0||text.Contains("%<")||text.Contains("%%"))throw Invalid(path,"Only literal plain text, without fields/formatting codes.");
        return text;
    }
    public static TableEditingSpec Parse(JsonElement input,string path,string[] handles)
    {
        if(handles.Length!=1)throw Invalid(path+".handles","Select one existing native Table.");
        string operation=Text(Required(input,"operation",path),path+".operation");
        if(!Handles(operation))throw Invalid(path+".operation","Unknown table editing operation.");
        bool insert=operation.StartsWith("insert_",StringComparison.Ordinal),delete=operation.StartsWith("delete_",StringComparison.Ordinal);
        bool rows=operation.EndsWith("_rows",StringComparison.Ordinal);
        string sizeName=rows?"height":"width";
        Shape(input,path,insert?new[]{"operation","handles","table_token","index","count",sizeName,"values","formula_replacements"}:
            delete?new[]{"operation","handles","table_token","index","count","formula_replacements"}:
            operation is "set_table_cell_format" or "set_table_cell_text"?new[]{"operation","handles","table_token","cells"}:new[]{"operation","handles","table_token","range"});
        string token=Text(Required(input,"table_token",path),path+".table_token",64);
        if(token.Length!=64||token.Any(c=>!"0123456789ABCDEF".Contains(c)))throw Invalid(path+".table_token","Use the exact uppercase table_token from current inspect_objects geometry.editing.");
        int? index=null,count=null;double? size=null;string[][]? values=null;TableCellRangeSpec? range=null;TableCellFormatEdit[]? cells=null;TableCellTextEdit[]? textCells=null;
        if(insert||delete)
        {
            index=Index(Required(input,"index",path),path+".index");count=Index(Required(input,"count",path),path+".count",true);
            if(insert)
            {
                size=Size(Required(input,sizeName,path),path+"."+sizeName);
                var matrix=Required(input,"values",path);if(matrix.ValueKind!=JsonValueKind.Array||matrix.GetArrayLength()==0)throw Invalid(path+".values","Supply the literal contents of every inserted cell, in row-major order.");
                var result=new List<string[]>();
                foreach(var row in matrix.EnumerateArray())
                {
                    if(row.ValueKind!=JsonValueKind.Array||row.GetArrayLength()==0)throw Invalid(path+".values","Expected nonempty rows of literal strings.");
                    result.Add(row.EnumerateArray().Select(v=>Literal(v,path+".values")).ToArray());
                }
                if(result.Select(r=>r.Length).Distinct().Count()!=1)throw Invalid(path+".values","All rows must have the same number of columns.");
                values=result.ToArray();
                if(rows&&values.Length!=count||!rows&&values[0].Length!=count)throw Invalid(path+".values","Inserted values must match count along the edited axis.");
            }
        }
        else if(operation=="set_table_cell_text")
        {
            var edits=Required(input,"cells",path);if(edits.ValueKind!=JsonValueKind.Array||edits.GetArrayLength()==0)throw Invalid(path+".cells","Select at least one cell.");
            var result=new List<TableCellTextEdit>();var seen=new HashSet<(int,int)>();
            foreach(var cell in edits.EnumerateArray())
            {
                Shape(cell,path+".cells","row","column","text");int row=Index(Required(cell,"row",path),path+".cells.row"),column=Index(Required(cell,"column",path),path+".cells.column");
                if(!seen.Add((row,column)))throw Invalid(path+".cells","Duplicate cell.");
                result.Add(new(row,column,Literal(Required(cell,"text",path),path+".cells.text")));
            }
            textCells=result.ToArray();
        }
        else if(operation=="set_table_cell_format")
        {
            var edits=Required(input,"cells",path);if(edits.ValueKind!=JsonValueKind.Array||edits.GetArrayLength()==0)throw Invalid(path+".cells","Select at least one cell.");
            var result=new List<TableCellFormatEdit>();var seen=new HashSet<(int,int)>();
            foreach(var cell in edits.EnumerateArray())
            {
                Shape(cell,path+".cells","row","column","properties");int row=Index(Required(cell,"row",path),path+".cells.row"),column=Index(Required(cell,"column",path),path+".cells.column");
                if(!seen.Add((row,column)))throw Invalid(path+".cells","Duplicate cell.");
                var properties=Required(cell,"properties",path);Shape(properties,path+".cells.properties","alignment","text_height","text_style_handle","color_aci","color_rgb","background","grids");
                if(!properties.EnumerateObject().Any())throw Invalid(path+".cells.properties","At least one cell format field required.");
                string? alignment=properties.TryGetProperty("alignment",out var a)?Text(a,path+".cells.alignment"):null;
                if(alignment!=null&&!Alignments.Contains(alignment,StringComparer.Ordinal))throw Invalid(path+".cells.alignment","Use one of the nine published native alignments.");
                double? height=properties.TryGetProperty("text_height",out var h)?Size(h,path+".cells.text_height"):null;
                string? style=properties.TryGetProperty("text_style_handle",out var s)?Validation.NormalizeHandle(Text(s,path+".cells.text_style_handle",16)):null;
                int? color=Aci(properties,path+".cells.properties");var rgb=AppearanceValidation.Parse(properties,path+".cells.properties").ColorRgb;
                var background=properties.TryGetProperty("background",out var bg)?Background(bg,path+".cells.properties.background"):null;
                var grids=properties.TryGetProperty("grids",out var g)?Grids(g,path+".cells.properties.grids"):null;
                result.Add(new(row,column,new(alignment,height,style,color,rgb,background,grids)));
            }
            cells=result.ToArray();
        }
        else
        {
            var r=Required(input,"range",path);Shape(r,path+".range","top_row","left_column","bottom_row","right_column");
            range=new(Index(Required(r,"top_row",path),path+".range.top_row"),Index(Required(r,"left_column",path),path+".range.left_column"),
                Index(Required(r,"bottom_row",path),path+".range.bottom_row"),Index(Required(r,"right_column",path),path+".range.right_column"));
            if(range.BottomRow<range.TopRow||range.RightColumn<range.LeftColumn||range.BottomRow==range.TopRow&&range.RightColumn==range.LeftColumn)throw Invalid(path+".range","Expected an inclusive rectangular range of at least two cells.");
        }
        var replacements=input.TryGetProperty("formula_replacements",out var replacementValues)?TableTopologyValidation354.Parse(replacementValues,path+".formula_replacements"):null;
        return new(operation,handles,token,index,count,size,values,range,cells,textCells,replacements);
    }
}
