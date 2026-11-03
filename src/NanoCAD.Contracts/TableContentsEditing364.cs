using System.Text.Json;

namespace NanoCAD.Contracts;

public sealed record TableBlockAttributeValue384(string AttributeDefinitionHandle,string Value);
public sealed record TableContentAction364(string Action,int Row,int Column,int ContentIndex,
    string? DataType=null,string? UnitType=null,JsonElement? Value=null,string? DataFormat=null,
    double? Rotation=null,double? Scale=null,int? DestinationIndex=null,string? ContentLayout=null,
    double? TextHeight=null,string? TextStyleHandle=null,int? ColorAci=null,RgbColor? ColorRgb=null,TableCellFormatChanges? CellFormat=null,
    string? BlockHandle=null,bool? AutoFit=null,TableBlockAttributeValue384[]? BlockAttributes=null,
    string? AttributeDefinitionHandle=null,string? AttributeValue=null);
public sealed record TableContentsEditing364(string Kind,string[] Targets,string ContentsToken,TableContentAction364[] Actions):EditSpec(Kind,Targets);

// Indices refer to the ordered native slots immediately before each action.
// The origin plan is also the preservation map for every retained slot.
public static class TableContentSequence364
{
    public static int[][] Plan(int[][] initial,int columns,IReadOnlyList<TableContentAction364> actions,Action cancel)
    {
        if(columns<=0||initial.Length%columns!=0)throw new DomainException("INVALID_ARGUMENT","Native contents plan requires a complete rectangular cell inventory.");
        var result=initial.Select(slots=>slots.ToList()).ToArray();
        for(int ordinal=0;ordinal<actions.Count;ordinal++)
        {
            cancel();var action=actions[ordinal];
            if(action.Column<0||action.Row<0||action.ContentIndex<0||action.Column>=columns||action.Row>=initial.Length/columns)
                throw new DomainException("CELL_OUT_OF_RANGE","Selected native Table cell is outside its current topology.",false,new{ordinal,action.Row,action.Column});
            var slots=result[checked(action.Row*columns+action.Column)];
            if(action.Action is "set_layout" or "set_cell_format")continue;
            bool insert=action.Action is "insert_value" or "insert_block";
            if(action.ContentIndex>(insert?slots.Count:slots.Count-1))
                throw new DomainException("CONTENT_OUT_OF_RANGE","Content index is outside the native slot sequence at this action.",false,new{ordinal,action.Row,action.Column,action.ContentIndex,content_count=slots.Count});
            if(insert)slots.Insert(action.ContentIndex,checked(-1-ordinal));
            else if(action.Action=="delete")slots.RemoveAt(action.ContentIndex);
            else if(action.Action=="move")
            {
                int destination=action.DestinationIndex!.Value;
                if(destination<0||destination>=slots.Count)throw new DomainException("CONTENT_OUT_OF_RANGE","Move destination must be an existing index in the same cell before removal.",false,new{ordinal,destination,content_count=slots.Count});
                int origin=slots[action.ContentIndex];slots.RemoveAt(action.ContentIndex);slots.Insert(destination,origin);
            }
        }
        return result.Select(slots=>slots.ToArray()).ToArray();
    }
}

public static class TableContentsValidation364
{
    public const string Operation="edit_table_contents";
    public static readonly string[] Layouts={"Flow","StackedHorizontal","StackedVertical"};
    static DomainException Invalid(string path,string message)=>new("INVALID_ARGUMENT",path+": "+message,false,new{path});
    static JsonElement Required(JsonElement value,string name,string path)=>value.TryGetProperty(name,out var item)?item:throw Invalid(path+"."+name,"Required field.");
    static void Shape(JsonElement value,string path,params string[] allowed)
    {
        if(value.ValueKind!=JsonValueKind.Object)throw Invalid(path,"Expected object.");
        var seen=new HashSet<string>(StringComparer.Ordinal);
        foreach(var property in value.EnumerateObject())if(!seen.Add(property.Name)||!allowed.Contains(property.Name,StringComparer.Ordinal))throw Invalid(path+"."+property.Name,"Unknown or duplicate field.");
    }
    static int Index(JsonElement value,string path)=>value.ValueKind==JsonValueKind.Number&&value.TryGetInt32(out int index)&&index>=0?index:throw Invalid(path,"Expected nonnegative Int32 native index.");
    static string Text(JsonElement value,string path)
    {
        if(value.ValueKind!=JsonValueKind.String)throw Invalid(path,"Expected string.");
        var text=value.GetString()!;if(text.Any(char.IsControl))throw Invalid(path,"Control characters are not native scalar format text.");return text;
    }
    static double? Number(JsonElement value,string name,string path,bool positive=false)
    {
        if(!value.TryGetProperty(name,out var item))return null;
        if(item.ValueKind!=JsonValueKind.Number||!item.TryGetDouble(out var number)||!double.IsFinite(number)||positive&&number<=0)
            throw Invalid(path+"."+name,positive?"Expected finite positive native scale.":"Expected finite native rotation in radians.");
        return number;
    }
    public static TableContentsEditing364 Parse(JsonElement input,string path,string[] handles)
    {
        if(handles.Length!=1)throw Invalid(path+".handles","Select one existing native Table.");
        Shape(input,path,"operation","handles","contents_token","actions");
        if(Text(Required(input,"operation",path),path+".operation")!=Operation)throw Invalid(path+".operation","Unknown Table contents operation.");
        string token=Text(Required(input,"contents_token",path),path+".contents_token");
        if(token.Length!=64||token.Any(c=>!"0123456789ABCDEF".Contains(c)))throw Invalid(path+".contents_token","Use current geometry.contents_editing.contents_token from inspect_objects.");
        var selected=Required(input,"actions",path);if(selected.ValueKind!=JsonValueKind.Array||selected.GetArrayLength()==0)throw Invalid(path+".actions","Select at least one indexed action.");
        var actions=new List<TableContentAction364>();int ordinal=0;
        foreach(var item in selected.EnumerateArray())
        {
            string at=path+".actions["+ordinal+++"]",action=Text(Required(item,"action",at),at+".action");
            string[] common=action is "set_layout" or "set_cell_format"?new[]{"action","row","column"}:new[]{"action","row","column","content_index"};
            var allowed=action switch
            {
                "set_value" or "insert_value"=>common.Concat(new[]{"data_type","unit_type","value","data_format"}).ToArray(),
                "insert_block"=>common.Concat(new[]{"block_handle","auto_fit","attributes"}).ToArray(),
                "set_block_attribute"=>common.Concat(new[]{"attribute_definition_handle","value"}).ToArray(),
                "set_format"=>common.Concat(new[]{"data_format","rotation","scale","text_height","text_style_handle","color_aci","color_rgb"}).ToArray(),
                "set_cell_format"=>common.Concat(new[]{"properties"}).ToArray(),
                "set_layout"=>common.Concat(new[]{"content_layout"}).ToArray(),
                "move"=>common.Concat(new[]{"destination_index"}).ToArray(),"delete"=>common,
                _=>throw Invalid(at+".action","Expected set_value, insert_value, insert_block, set_block_attribute, set_format, set_layout, set_cell_format, move or delete.")
            };
            Shape(item,at,allowed);int row=Index(Required(item,"row",at),at+".row"),column=Index(Required(item,"column",at),at+".column"),index=action is "set_layout" or "set_cell_format"?0:Index(Required(item,"content_index",at),at+".content_index");
            string? layout=null;
            if(action=="set_layout")
            {layout=Text(Required(item,"content_layout",at),at+".content_layout");if(!Layouts.Contains(layout,StringComparer.Ordinal))throw Invalid(at+".content_layout","Select native Flow, StackedHorizontal or StackedVertical.");}
            string? type=null,unit=null,format=null;JsonElement? value=null;
            if(action is "set_value" or "insert_value")
            {
                type=Text(Required(item,"data_type",at),at+".data_type");unit=Text(Required(item,"unit_type",at),at+".unit_type");
                if(!TableValueEditingValidation336.DataTypes.Contains(type,StringComparer.Ordinal)||!TableValueEditingValidation336.UnitTypes.Contains(unit,StringComparer.Ordinal))throw Invalid(at,"Select published scalar datatype and native unit.");
                var raw=Required(item,"value",at);
                if(type=="String")
                {TableEditingValidation.Literal(raw,at+".value");if(unit!="Unitless")throw Invalid(at+".unit_type","Plain String requires Unitless.");}
                else if(raw.ValueKind!=JsonValueKind.Number||type=="Long"&&!raw.TryGetInt32(out _)||type=="Double"&&(!raw.TryGetDouble(out var real)||!double.IsFinite(real)))throw Invalid(at+".value","Expected Int32 Long or finite Double; no display-text parsing or unit conversion.");
                value=raw.Clone();format=Text(Required(item,"data_format",at),at+".data_format");
            }
            else if(item.TryGetProperty("data_format",out var dataFormat))format=Text(dataFormat,at+".data_format");
            double? rotation=Number(item,"rotation",at),scale=Number(item,"scale",at,true);
            double? height=Number(item,"text_height",at,true);string? font=null;int? aci=null;RgbColor? rgb=null;
            if(item.TryGetProperty("text_style_handle",out var style))font=Validation.NormalizeHandle(Text(style,at+".text_style_handle"));
            if(item.TryGetProperty("color_aci",out var selectedColor))
            {if(selectedColor.ValueKind!=JsonValueKind.Number||!selectedColor.TryGetInt32(out int color)||color is <0 or >256)throw Invalid(at+".color_aci","Use native ACI0..256.");aci=color;}
            rgb=AppearanceValidation.Parse(item,at).ColorRgb;
            var properties=action=="set_cell_format"?TableContentFormat365.Parse(Required(item,"properties",at),at+".properties"):null;
            if(action=="set_format"&&format==null&&rotation==null&&scale==null&&height==null&&font==null&&aci==null&&rgb==null)throw Invalid(at,"Select at least one scalar content format property.");
            int? destination=action=="move"?Index(Required(item,"destination_index",at),at+".destination_index"):null;
            string? block=null,definition=null,attributeValue=null;bool? autoFit=null;TableBlockAttributeValue384[]? attributes=null;
            if(action=="insert_block")
            {
                block=Validation.NormalizeHandle(Text(Required(item,"block_handle",at),at+".block_handle"));
                var fit=Required(item,"auto_fit",at);if(fit.ValueKind is not (JsonValueKind.True or JsonValueKind.False))throw Invalid(at+".auto_fit","Expected native autoFit boolean.");autoFit=fit.GetBoolean();
                if(item.TryGetProperty("attributes",out var selectedAttributes))
                {
                if(selectedAttributes.ValueKind!=JsonValueKind.Array)throw Invalid(at+".attributes","Expected complete nonconstant attribute values array; [] for a definition without editable attributes.");
                var list=new List<TableBlockAttributeValue384>();var seen=new HashSet<string>(StringComparer.Ordinal);
                foreach(var attribute in selectedAttributes.EnumerateArray())
                {
                    string ap=at+".attributes["+list.Count+"]";Shape(attribute,ap,"attribute_definition_handle","value");
                    string ah=Validation.NormalizeHandle(Text(Required(attribute,"attribute_definition_handle",ap),ap+".attribute_definition_handle"));
                    if(!seen.Add(ah))throw Invalid(ap+".attribute_definition_handle","Duplicate native attribute definition.");
                    string literal=TableEditingValidation.Literal(Required(attribute,"value",ap),ap+".value");list.Add(new(ah,literal));
                }
                attributes=list.ToArray();
                }
            }
            else if(action=="set_block_attribute")
            {definition=Validation.NormalizeHandle(Text(Required(item,"attribute_definition_handle",at),at+".attribute_definition_handle"));attributeValue=TableEditingValidation.Literal(Required(item,"value",at),at+".value");}
            actions.Add(new(action,row,column,index,type,unit,value,format,rotation,scale,destination,layout,height,font,aci,rgb,properties,block,autoFit,attributes,definition,attributeValue));
        }
        return new(Operation,handles,token,actions.ToArray());
    }
    public static void Batch(IReadOnlyList<EditSpec> operations)
    {if(operations.Any(edit=>edit is TableContentsEditing364)&&operations.Count!=1)throw new DomainException("INVALID_OPERATION_ORDER","Indexed Table contents editing must be the sole operation; reacquire its contents_token after the edit.");}
}
