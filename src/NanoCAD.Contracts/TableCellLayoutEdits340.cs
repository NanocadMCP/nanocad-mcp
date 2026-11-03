using System.Text;
using System.Text.Json;

namespace NanoCAD.Contracts;

public sealed record TableCellMargins340(double? Top=null,double? Left=null,double? Bottom=null,double? Right=null);
public sealed record TableCellLayoutProperties340(TableCellMargins340? Margins=null,double? Rotation=null,double? Scale=null,
    bool? AutoScale=null,string? CellStyle=null,string? DataFormat=null);
public sealed record TableCellLayoutChange340(int Row,int Column,TableCellLayoutProperties340 Properties);
public sealed record TableCellLayoutEdit340(string Kind,string[] Targets,string TableToken,TableCellLayoutChange340[] Cells):EditSpec(Kind,Targets);

public static class TableCellLayoutValidation340
{
    public const string Operation="set_table_cell_layout";
    public static bool Handles(string operation)=>operation==Operation;
    private static DomainException Invalid(string path,string message)=>new("INVALID_ARGUMENT",path+": "+message,false,new{path});
    private static JsonElement Required(JsonElement value,string name,string path)=>value.TryGetProperty(name,out var found)?found:throw Invalid(path+"."+name,"Required field.");
    private static void Shape(JsonElement value,string path,params string[] names)
    {
        if(value.ValueKind!=JsonValueKind.Object)throw Invalid(path,"Expected object.");
        var seen=new HashSet<string>(StringComparer.Ordinal);
        foreach(var property in value.EnumerateObject())if(!seen.Add(property.Name)||!names.Contains(property.Name,StringComparer.Ordinal))throw Invalid(path+"."+property.Name,"Unknown or duplicate field.");
    }
    private static string Text(JsonElement value,string path,bool nonempty=false)
    {
        if(value.ValueKind!=JsonValueKind.String)throw Invalid(path,"Expected native string.");
        string result;try{result=value.GetString()!;new UTF8Encoding(false,true).GetByteCount(result);}catch(Exception error)when(error is InvalidOperationException or EncoderFallbackException){throw Invalid(path,"Invalid Unicode scalar text.");}
        if(result.Any(char.IsControl)||nonempty&&string.IsNullOrWhiteSpace(result))throw Invalid(path,"Expected native scalar text without control characters.");
        return result;
    }
    private static int Index(JsonElement value,string path)=>value.ValueKind==JsonValueKind.Number&&value.TryGetInt32(out int parsed)&&parsed>=0?parsed:throw Invalid(path,"Expected nonnegative native integer index.");
    private static double Number(JsonElement value,string path,bool nonnegative=false,bool positive=false)
    {
        if(value.ValueKind!=JsonValueKind.Number||!value.TryGetDouble(out double result)||!double.IsFinite(result)||Math.Abs(result)>1e9||nonnegative&&result<0||positive&&result<=0)
            throw Invalid(path,positive?"Expected finite positive native value up to1e9.":nonnegative?"Expected finite nonnegative drawing length up to1e9.":"Expected finite native radians within +/-1e9.");
        return result;
    }
    public static TableCellLayoutEdit340 Parse(JsonElement input,string path,string[] handles)
    {
        if(handles.Length!=1)throw Invalid(path+".handles","Select one existing native Table.");
        Shape(input,path,"operation","handles","table_token","cells");
        if(Text(Required(input,"operation",path),path+".operation")!=Operation)throw Invalid(path+".operation","Unknown cell-layout operation.");
        string token=Text(Required(input,"table_token",path),path+".table_token");
        if(token.Length!=64||token.Any(c=>!"0123456789ABCDEF".Contains(c)))throw Invalid(path+".table_token","Use the exact fresh uppercase geometry.editing.table_token.");
        var items=Required(input,"cells",path);
        if(items.ValueKind!=JsonValueKind.Array||items.GetArrayLength()==0)throw Invalid(path+".cells","Select at least one native anchor cell.");
        var result=new List<TableCellLayoutChange340>();var selected=new HashSet<(int,int)>();int ordinal=0;
        foreach(var item in items.EnumerateArray())
        {
            string at=path+".cells["+ordinal+++"]";Shape(item,at,"row","column","properties");
            int row=Index(Required(item,"row",at),at+".row"),column=Index(Required(item,"column",at),at+".column");
            if(!selected.Add((row,column)))throw Invalid(at,"Duplicate native cell selection.");
            var props=Required(item,"properties",at);string p=at+".properties";
            Shape(props,p,"margins","rotation","scale","auto_scale","cell_style","data_format");
            if(!props.EnumerateObject().Any())throw Invalid(p,"Request at least one cell-layout property.");
            TableCellMargins340? margins=null;
            if(props.TryGetProperty("margins",out var m))
            {
                Shape(m,p+".margins","top","left","bottom","right");
                if(!m.EnumerateObject().Any())throw Invalid(p+".margins","Request at least one margin.");
                double? Margin(string name)=>m.TryGetProperty(name,out var v)?Number(v,p+".margins."+name,nonnegative:true):null;
                margins=new(Margin("top"),Margin("left"),Margin("bottom"),Margin("right"));
            }
            double? rotation=props.TryGetProperty("rotation",out var angle)?Number(angle,p+".rotation"):null;
            double? scale=props.TryGetProperty("scale",out var size)?Number(size,p+".scale",positive:true):null;
            bool? auto=null;
            if(props.TryGetProperty("auto_scale",out var flag))auto=flag.ValueKind is JsonValueKind.True or JsonValueKind.False?flag.GetBoolean():throw Invalid(p+".auto_scale","Expected boolean.");
            if(scale!=null&&auto!=false)throw Invalid(p+".scale","Manual content scale requires explicit auto_scale:false; automatic scale is derived by native layout.");
            string? style=props.TryGetProperty("cell_style",out var name)?Text(name,p+".cell_style",nonempty:true):null;
            string? format=props.TryGetProperty("data_format",out var f)?Text(f,p+".data_format"):null;
            result.Add(new(row,column,new(margins,rotation,scale,auto,style,format)));
        }
        return new(Operation,handles,token,result.ToArray());
    }
    public static void Batch(IReadOnlyList<EditSpec> operations)
    {
        if(operations.Any(edit=>edit is TableCellLayoutEdit340)&&operations.Count!=1)
            throw new DomainException("INVALID_OPERATION_ORDER","Native cell layout must be the sole operation; inspect again for its complete fresh table token.");
    }
}
