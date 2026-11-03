using System.Text.Json;
using System.Text.Json.Serialization;
using static NanoCAD.Contracts.StructureWriteValidation;

namespace NanoCAD.Contracts;

public sealed record TableStyleRowChanges337(string RowType,double? TextHeight=null,string? Alignment=null,
    string? TextStyleHandle=null,int? ColorAci=null,RgbColor? ColorRgb=null,TableCellBackgroundChanges? Background=null,TableGridFormatChanges[]? Grids=null);
public sealed record TableStyleProperties337(string? Description=null,double? HorizontalMargin=null,double? VerticalMargin=null,
    TableStyleRowChanges337[]? Rows=null);
public sealed record TableStyleEdit337(string Kind,string SourceHandle,string SourceToken,string? Name,string? ExpectedName,
    TableStyleProperties337 Properties):StructureEdit(Kind);
public sealed record TableStyleEditing337(bool editable,
    [property:JsonIgnore(Condition=JsonIgnoreCondition.WhenWritingNull)] string? table_style_token=null,
    [property:JsonIgnore(Condition=JsonIgnoreCondition.WhenWritingNull)] string? unavailable_reason=null);
public sealed record TableStyleColor337(string method,int? aci,int[]? rgb);
public sealed record TableStyleRowRead337(string row_type,double text_height,string alignment,string? text_style_handle,TableStyleColor337 content_color,bool background_none,TableStyleColor337 background_color,JsonElement grids);
public sealed record TableStyleRead337(string description,double horizontal_margin,double vertical_margin,
    TableStyleRowRead337[] rows,TableStyleEditing337 editing,
    [property:JsonIgnore(Condition=JsonIgnoreCondition.WhenWritingNull)] string[]? cell_style_names=null,
    [property:JsonIgnore(Condition=JsonIgnoreCondition.WhenWritingNull)] NativeStyleLifecycleRead352? lifecycle=null);
public sealed record TableStyleChange337(int operation_index,EntityRef style_ref,EntityRef source_ref,string table_style_token,int affected_table_count,
    [property:JsonIgnore(Condition=JsonIgnoreCondition.WhenWritingNull)] string[]? changed_named_cell_styles=null,
    [property:JsonIgnore(Condition=JsonIgnoreCondition.WhenWritingNull)] string[]? warnings=null);

public static class TableStyleValidation337
{
    public static readonly string[] Operations={"create_table_style","set_table_style_properties"};
    public static readonly string[] RowTypes={"TitleRow","HeaderRow","DataRow"};
    public static readonly string[] Alignments={"TopLeft","TopCenter","TopRight","MiddleLeft","MiddleCenter","MiddleRight","BottomLeft","BottomCenter","BottomRight"};
    public static bool Handles(string operation)=>Operations.Contains(operation,StringComparer.Ordinal);
    public static bool IsSettingsOnly(TableStyleEdit337 edit)=>edit.Properties.HorizontalMargin==null&&edit.Properties.VerticalMargin==null&&
        (edit.Properties.Rows==null||edit.Properties.Rows.All(row=>row.TextHeight==null));
    public static void Batch(IReadOnlyCollection<StructureEdit> edits)
    {
        if(edits.Any(edit=>edit is TableStyleEdit337)&&edits.Count!=1)
            throw Invalid("operations","Create or edit one reusable TableStyle per request; referenced tables are inspected before any style setter.","INVALID_OPERATION_ORDER");
    }
    private static string Token(JsonElement input,string name,string path)
    {
        string token=Text(input,name,path,64);
        if(token.Length!=64||token.Any(c=>!Uri.IsHexDigit(c)))throw Invalid(path+"."+name,"Read the fresh64-hex table_style_token from get_structure table_styles properties.table_style.editing.");
        return token.ToUpperInvariant();
    }
    private static double? Length(JsonElement input,string name,string path,bool zero)
    {
        if(!input.TryGetProperty(name,out var field))return null;
        if(field.ValueKind!=JsonValueKind.Number||!field.TryGetDouble(out double number)||!double.IsFinite(number)||number>1e6||number<0||!zero&&number==0)
            throw Invalid(path+"."+name,zero?"Use a finite nonnegative native drawing length <=1e6.":"Use a positive finite native drawing length <=1e6.");
        return number;
    }
    private static TableStyleProperties337 Properties(JsonElement value,string path)
    {
        Shape(value,path,"description","horizontal_margin","vertical_margin","rows");
        if(!value.EnumerateObject().Any())throw Invalid(path,"Supply at least one reusable style default.");
        string? description=value.TryGetProperty("description",out var text)?AppearanceValidation.Description(text,path+".description"):null;
        TableStyleRowChanges337[]? rows=null;
        if(value.TryGetProperty("rows",out var array))
        {
            if(array.ValueKind!=JsonValueKind.Array||array.GetArrayLength()==0)throw Invalid(path+".rows","Supply distinct selected TitleRow/HeaderRow/DataRow defaults.");
            var result=new List<TableStyleRowChanges337>();var roles=new HashSet<string>(StringComparer.Ordinal);
            foreach(var row in array.EnumerateArray())
            {
                string rp=path+".rows["+result.Count+"]";
                Shape(row,rp,"row_type","text_height","alignment","text_style_handle","color_aci","color_rgb","background","grids");
                string role=Text(row,"row_type",rp,16);
                if(!RowTypes.Contains(role,StringComparer.Ordinal)||!roles.Add(role))throw Invalid(rp+".row_type","Use a distinct TitleRow/HeaderRow/DataRow native role.");
                if(row.EnumerateObject().Count()==1)throw Invalid(rp,"Supply a row default to change.");
                string? alignment=row.TryGetProperty("alignment",out _)?Text(row,"alignment",rp,16):null;
                if(alignment!=null&&!Alignments.Contains(alignment,StringComparer.Ordinal))throw Invalid(rp+".alignment","Use a supported native cell alignment.");
                string? style=row.TryGetProperty("text_style_handle",out _)?Validation.NormalizeHandle(Text(row,"text_style_handle",rp,16)):null;
                int? aci=null;
                if(row.TryGetProperty("color_aci",out var color))
                {
                    if(color.ValueKind!=JsonValueKind.Number||!color.TryGetInt32(out int number)||number is <1 or >255)throw Invalid(rp+".color_aci","Use explicit ACI1..255.");
                    aci=number;
                }
                var appearance=AppearanceValidation.Parse(row,rp);
                result.Add(new(role,Length(row,"text_height",rp,false),alignment,style,aci,appearance.ColorRgb,
                    row.TryGetProperty("background",out var background)?TableStyleRoleDefaults365.Background(background,rp+".background"):null,
                    row.TryGetProperty("grids",out var grids)?TableStyleRoleDefaults365.Grids(grids,rp+".grids"):null));
            }
            rows=result.ToArray();
        }
        return new(description,Length(value,"horizontal_margin",path,true),Length(value,"vertical_margin",path,true),rows);
    }
    public static TableStyleEdit337 Parse(JsonElement input,string path)
    {
        string operation=Text(input,"operation",path,32);bool creating=operation=="create_table_style";
        if(!Handles(operation))throw Invalid(path+".operation","Unknown reusable TableStyle operation.");
        Shape(input,path,creating?new[]{"operation","name","base_style_handle","base_style_token","properties"}:
            new[]{"operation","handle","expected_name","table_style_token","properties"});
        string source=Validation.NormalizeHandle(Text(input,creating?"base_style_handle":"handle",path,16));
        string name=Text(input,creating?"name":"expected_name",path,255);
        if(!BlockWriteValidation.OrdinaryName(name))throw Invalid(path+(creating?".name":".expected_name"),"Use an ordinary unqualified native style name.");
        var properties=input.TryGetProperty("properties",out var fields)?Properties(fields,path+".properties"):
            creating?new TableStyleProperties337():throw Invalid(path+".properties","Supply requested reusable defaults.");
        return new(operation,source,Token(input,creating?"base_style_token":"table_style_token",path),creating?name:null,creating?null:name,properties);
    }
}
