using System.Text.Json;

namespace NanoCAD.Contracts;

public sealed record TableNumericCellEdit(int Row,int Column,string DataType,string UnitType,JsonElement Value,string DataFormat);
public sealed record TableValueEditingSpec(string Kind,string[] Targets,string TableToken,TableNumericCellEdit[]? Cells=null,
    string? TableStyleHandle=null):EditSpec(Kind,Targets);

public static class TableValueEditingValidation336
{
    public static readonly string[] Operations={"set_table_cell_value","set_table_style"};
    public static readonly string[] DataTypes={"String","Long","Double"};
    public static readonly string[] UnitTypes={"Unitless","Distance","Area","Volume","Angle","Currency","Percentage"};
    public static bool Handles(string operation)=>Operations.Contains(operation,StringComparer.Ordinal);
    private static DomainException Invalid(string path,string message)=>new("INVALID_ARGUMENT",path+": "+message,false,new{path});
    private static JsonElement Required(JsonElement value,string name,string path)=>value.TryGetProperty(name,out var found)?found:throw Invalid(path+"."+name,"Required field.");
    private static void Shape(JsonElement value,string path,params string[] allowed)
    {
        if(value.ValueKind!=JsonValueKind.Object)throw Invalid(path,"Expected object.");
        var seen=new HashSet<string>(StringComparer.Ordinal);
        foreach(var property in value.EnumerateObject())if(!seen.Add(property.Name)||!allowed.Contains(property.Name,StringComparer.Ordinal))throw Invalid(path+"."+property.Name,"Unknown or duplicate field.");
    }
    private static string Text(JsonElement value,string path)
    {
        if(value.ValueKind!=JsonValueKind.String)throw Invalid(path,"Expected string.");
        string text;try{text=value.GetString()!;}catch(InvalidOperationException){throw Invalid(path,"Invalid Unicode scalar text.");}
        if(text.Any(char.IsControl))throw Invalid(path,"Control characters are not native scalar format text.");
        return text;
    }
    private static int Index(JsonElement value,string path)=>value.ValueKind==JsonValueKind.Number&&value.TryGetInt32(out int index)&&index>=0?index:throw Invalid(path,"Expected nonnegative native integer index.");
    public static TableValueEditingSpec Parse(JsonElement input,string path,string[] handles)
    {
        if(handles.Length!=1)throw Invalid(path+".handles","Select one existing native Table.");
        string operation=Text(Required(input,"operation",path),path+".operation");
        if(!Handles(operation))throw Invalid(path+".operation","Unknown native table value operation.");
        Shape(input,path,operation=="set_table_style"?new[]{"operation","handles","table_token","table_style_handle"}:new[]{"operation","handles","table_token","cells"});
        string token=Text(Required(input,"table_token",path),path+".table_token");
        if(token.Length!=64||token.Any(c=>!"0123456789ABCDEF".Contains(c)))throw Invalid(path+".table_token","Use the exact current uppercase table_token from inspect_objects geometry.editing.");
        if(operation=="set_table_style")return new(operation,handles,token,TableStyleHandle:Validation.NormalizeHandle(Text(Required(input,"table_style_handle",path),path+".table_style_handle")));
        var selected=Required(input,"cells",path);
        if(selected.ValueKind!=JsonValueKind.Array||selected.GetArrayLength()==0)throw Invalid(path+".cells","Select at least one numeric cell.");
        var cells=new List<TableNumericCellEdit>();var positions=new HashSet<(int,int)>();int ordinal=0;
        foreach(var item in selected.EnumerateArray())
        {
            string at=path+".cells["+ordinal+++"]";Shape(item,at,"row","column","data_type","unit_type","value","data_format");
            int row=Index(Required(item,"row",at),at+".row"),column=Index(Required(item,"column",at),at+".column");
            if(!positions.Add((row,column)))throw Invalid(at,"Duplicate selected cell.");
            string type=Text(Required(item,"data_type",at),at+".data_type"),unit=Text(Required(item,"unit_type",at),at+".unit_type");
            if(!DataTypes.Contains(type,StringComparer.Ordinal))throw Invalid(at+".data_type","Expected native String, Long (Int32) or Double.");
            if(!UnitTypes.Contains(unit,StringComparer.Ordinal))throw Invalid(at+".unit_type","Select one published scalar native unit type; no combined flags.");
            var value=Required(item,"value",at);
            if(type=="String")
            {
                TableEditingValidation.Literal(value,at+".value");
                if(unit!="Unitless")throw Invalid(at+".unit_type","Plain native strings require Unitless.");
            }
            else if(value.ValueKind!=JsonValueKind.Number||type=="Long"&&!value.TryGetInt32(out _)||type=="Double"&&(!value.TryGetDouble(out double number)||!double.IsFinite(number)))
                throw Invalid(at+".value","Supply an Int32 for Long or a finite native Double; formatted strings are not numeric values.");
            string format=Text(Required(item,"data_format",at),at+".data_format");
            cells.Add(new(row,column,type,unit,value.Clone(),format));
        }
        return new(operation,handles,token,cells.ToArray());
    }
    public static void Batch(IReadOnlyList<EditSpec> operations)
    {
        if(operations.Any(edit=>edit is TableValueEditingSpec)&&operations.Count!=1)
            throw new DomainException("INVALID_OPERATION_ORDER","Native table scalar values/style assignment must be the sole operation; inspect the new table_token after it.");
    }
}
