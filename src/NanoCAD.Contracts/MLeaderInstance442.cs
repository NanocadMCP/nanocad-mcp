using System.Text.Json;
using System.Text.Json.Nodes;

namespace NanoCAD.Contracts;

public sealed record MLeaderBlockAttribute442(string AttributeDefinitionHandle,string Value,string? ExpectedValue=null);
public sealed record MLeaderBlockSpec442(Point2[] Vertices,Point2 BlockPosition,string BlockHandle,double BlockScale,double BlockRotation,
    string BlockConnection,string MLeaderStyle,MLeaderBlockAttribute442[] Attributes,string LayerName):EntitySpec("MLeader",LayerName);
public sealed record SetMLeaderInstance442(string[] Targets,string InstanceToken,JsonElement Properties,MLeaderBlockAttribute442[] Attributes)
    :EditSpec("set_mleader_instance",Targets);

public static class MLeaderInstanceValidation442
{
    public static readonly string[] Properties=["content_position_wcs","text_height","text_style_handle","arrow_size","arrow_symbol_handle",
        "landing_gap","dogleg_length","enable_landing","enable_dogleg","enable_frame_text","text_alignment","text_angle_type",
        "text_attachment_left","text_attachment_right","block_scale","block_rotation","block_connection_type"];
    public static readonly string[] BlockConnections=["ConnectExtents","ConnectBase"];
    static DomainException Bad(string path,string message)=>new("INVALID_ARGUMENT",path+": "+message,false,new{path});
    static JsonElement R(JsonElement value,string name,string path)=>value.TryGetProperty(name,out var item)?item:throw Bad(path+"."+name,"Required field.");
    static void Shape(JsonElement value,string path,params string[] allowed)
    {
        if(value.ValueKind!=JsonValueKind.Object)throw Bad(path,"Expected object.");var seen=new HashSet<string>(StringComparer.Ordinal);
        foreach(var p in value.EnumerateObject())if(!seen.Add(p.Name)||!allowed.Contains(p.Name,StringComparer.Ordinal))throw Bad(path+"."+p.Name,"Unknown or duplicate field.");
    }
    static string Text(JsonElement value,string path)=>value.ValueKind==JsonValueKind.String?value.GetString()!:throw Bad(path,"Expected string.");
    static string Handle(JsonElement value,string path)=>Validation.NormalizeHandle(Text(value,path));
    static double Number(JsonElement value,string path,bool positive=false)
    {if(value.ValueKind!=JsonValueKind.Number||!value.TryGetDouble(out double n)||!double.IsFinite(n)||Math.Abs(n)>1e9||positive&&n<=0)throw Bad(path,"Expected finite native value within +/-1e9 and declared sign.");return n;}
    static Point2 Point(JsonElement value,string path,bool triple=false)
    {
        if(value.ValueKind!=JsonValueKind.Array||value.GetArrayLength()!=(triple?3:2))throw Bad(path,triple?"Use WCS XY/Z0 point [x,y,0].":"Use XY point [x,y].");
        var p=new Point2(Number(value[0],path),Number(value[1],path));if(triple&&Number(value[2],path)!=0)throw Bad(path,"Use WCS XY/Z0.");return p;
    }
    static string Choice(JsonElement value,string path,string[] choices)
    {string s=Text(value,path);if(!choices.Contains(s,StringComparer.Ordinal))throw Bad(path,"Use a published native enum value.");return s;}
    public static MLeaderBlockAttribute442[] Attributes(JsonElement value,string path,bool editing)
    {
        if(value.ValueKind!=JsonValueKind.Array)throw Bad(path,"Expected native attribute-definition handles and literal values.");
        var rows=new List<MLeaderBlockAttribute442>();var seen=new HashSet<string>(StringComparer.Ordinal);
        foreach(var item in value.EnumerateArray())
        {
            string at=path+"["+rows.Count+"]";Shape(item,at,editing?["attribute_definition_handle","expected_value","value"]:["attribute_definition_handle","value"]);
            string id=Handle(R(item,"attribute_definition_handle",at),at);if(!seen.Add(id))throw Bad(at,"Duplicate attribute definition.");
            string Literal(string name){string text=Text(R(item,name,at),at+"."+name);if(!MLeaderTextLiteral.Supported(text,0,4096))throw Bad(at+"."+name,"Use single-line literal attribute text without Fields or formatting.");return text;}
            rows.Add(new(id,Literal("value"),editing?Literal("expected_value"):null));
        }
        return rows.ToArray();
    }
    public static MLeaderBlockSpec442 ParseCreate(JsonElement value,string path,string layer)
    {
        Shape(value,path,"type","content_type","layer","vertices","block_position","block_handle","block_scale","block_rotation","block_connection_type","mleader_style","attributes");
        if(Text(R(value,"content_type",path),path)!="block")throw Bad(path,"Use content_type=block.");
        var raw=R(value,"vertices",path);if(raw.ValueKind!=JsonValueKind.Array||raw.GetArrayLength()<2)throw Bad(path+".vertices","At least two arrow-first line vertices are required.");
        var points=raw.EnumerateArray().Select(p=>Point(p,path+".vertices")).ToArray();for(int i=1;i<points.Length;i++)if(points[i]==points[i-1])throw Bad(path,"Adjacent vertices cannot coincide.");
        string style=Text(R(value,"mleader_style",path),path);if(string.IsNullOrWhiteSpace(style)||style.Length>255||style.Any(char.IsControl)||style.Contains('|'))throw Bad(path,"Use an existing local MLeaderStyle name.");
        var spec=new MLeaderBlockSpec442(points,Point(R(value,"block_position",path),path+".block_position"),Handle(R(value,"block_handle",path),path+".block_handle"),
            value.TryGetProperty("block_scale",out var scale)?Number(scale,path+".block_scale",true):1,
            value.TryGetProperty("block_rotation",out var rotation)?Number(rotation,path+".block_rotation"):0,
            value.TryGetProperty("block_connection_type",out var connection)?Choice(connection,path,BlockConnections):"ConnectExtents",style,
            Attributes(R(value,"attributes",path),path+".attributes",false),layer);SerializedCapacity357.Check(spec);return spec;
    }
    public static SetMLeaderInstance442 Parse(JsonElement value,string path,string[] handles)
    {
        Shape(value,path,"operation","handles","instance_token","properties","block_attributes");if(handles.Length!=1)throw Bad(path,"Select one actual native MLeader.");
        if(Text(R(value,"operation",path),path)!="set_mleader_instance")throw Bad(path+".operation","Expected set_mleader_instance.");
        string token=Text(R(value,"instance_token",path),path);if(token.Length!=64||token.Any(c=>!Uri.IsHexDigit(c)))throw Bad(path,"Read geometry.instance_editing.instance_token.");
        var properties=value.TryGetProperty("properties",out var p)?p:Wire.Element(new{});Shape(properties,path+".properties",Properties);
        foreach(var field in properties.EnumerateObject())
        {
            string at=path+".properties."+field.Name;
            switch(field.Name)
            {
                case "content_position_wcs":Point(field.Value,at,true);break;
                case "text_style_handle":case "arrow_symbol_handle":Handle(field.Value,at);break;
                case "enable_landing":case "enable_dogleg":case "enable_frame_text":if(field.Value.ValueKind is not(JsonValueKind.True or JsonValueKind.False))throw Bad(at,"Use boolean.");break;
                case "text_alignment":Choice(field.Value,at,MLeaderStyleValidation337.TextAlignments);break;
                case "text_angle_type":Choice(field.Value,at,MLeaderStyleValidation337.TextAngles);break;
                case "text_attachment_left":case "text_attachment_right":Choice(field.Value,at,MLeaderStyleValidation337.HorizontalAttachments);break;
                case "block_connection_type":Choice(field.Value,at,BlockConnections);break;
                case "text_height":case "block_scale":Number(field.Value,at,true);break;
                case "block_rotation":Number(field.Value,at);break;
                default:if(Number(field.Value,at)<0)throw Bad(at,"Use a nonnegative native length.");break;
            }
        }
        var attributes=value.TryGetProperty("block_attributes",out var a)?Attributes(a,path+".block_attributes",true):[];
        if(!properties.EnumerateObject().Any()&&attributes.Length==0)throw Bad(path,"Request a property or explicit attribute value change.");
        var canonical=JsonNode.Parse(properties.GetRawText())!.AsObject();
        foreach(string name in new[]{"text_style_handle","arrow_symbol_handle"})
            if(properties.TryGetProperty(name,out var selected))canonical[name]=Handle(selected,path+".properties."+name);
        var edit=new SetMLeaderInstance442(handles,token.ToUpperInvariant(),Wire.Element(canonical),attributes);SerializedCapacity357.Check(edit);return edit;
    }
    public static void Batch(IReadOnlyCollection<EditSpec> operations)
    {if(operations.Any(op=>op is SetMLeaderInstance442)&&operations.Count!=1)throw new DomainException("INVALID_OPERATION_ORDER","Combine this MLeader's properties and attribute edits into one sole token-bound operation, then read its new token.");}
}
