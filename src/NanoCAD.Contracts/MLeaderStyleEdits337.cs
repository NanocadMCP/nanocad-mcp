using System.Text.Json;
using System.Text.Json.Serialization;
using static NanoCAD.Contracts.StructureWriteValidation;

namespace NanoCAD.Contracts;

public sealed record MLeaderStyleChanges337(string? TextStyleHandle,double? TextHeight,double? ArrowSize,double? LandingGap,
    double? DoglegLength,bool? EnableLanding,bool? EnableDogleg,double? Scale,string? LeaderLinetypeHandle,int? LeaderLineweight,
    int? LeaderColorAci,RgbColor? LeaderColorRgb,int? TextColorAci,RgbColor? TextColorRgb,
    bool? EnableFrameText=null,string? TextAlignment=null,string? TextAttachmentType=null,
    string? TextAngleType=null,string? TextAttachmentLeft=null,string? TextAttachmentRight=null);
public sealed record MLeaderStyleEdit337(string Kind,string SourceHandle,string SourceToken,MLeaderStyleChanges337 Properties,
    string? Name=null,string? ExpectedName=null):StructureEdit(Kind);
public sealed record MLeaderStyleEditing337(string style_token);
public sealed record MLeaderStyleLeaderRead337(EntityRef style_ref,JsonElement effective_settings,bool complete_override_mask_available=false);
public sealed record MLeaderStyleRead337(string native_type,bool is_local,bool can_reference,bool is_current,
    [property:JsonIgnore(Condition=JsonIgnoreCondition.WhenWritingNull)] JsonElement? settings=null,
    [property:JsonIgnore(Condition=JsonIgnoreCondition.WhenWritingNull)] MLeaderStyleEditing337? editing=null,
    [property:JsonIgnore(Condition=JsonIgnoreCondition.WhenWritingNull)] string? editing_unavailable_reason=null,
    [property:JsonIgnore(Condition=JsonIgnoreCondition.WhenWritingNull)] NativeStyleLifecycleRead352? lifecycle=null,
    [property:JsonIgnore(Condition=JsonIgnoreCondition.WhenWritingNull)] bool? creation_candidate=null);

public static class MLeaderStyleValidation337
{
    public static readonly string[] TextAngles={"InsertAngle","HorizontalAngle","AlwaysRightReadingAngle"};
    public static readonly string[] TextAlignments={"LeftAlignment","CenterAlignment","RightAlignment"};
    public static readonly string[] HorizontalAttachments={"AttachmentTopOfTop","AttachmentMiddleOfTop","AttachmentMiddle","AttachmentMiddleOfBottom","AttachmentBottomOfBottom","AttachmentBottomLine","AttachmentBottomOfTopLine","AttachmentBottomOfTop","AttachmentAllLine"};
    public static readonly string[] Operations={"create_mleader_style","set_mleader_style_properties"};
    public static bool IsSettingsOnly(MLeaderStyleEdit337 edit)=>edit.Properties is
        {TextHeight:null,ArrowSize:null,LandingGap:null,DoglegLength:null};
    private static string Name(JsonElement input,string field,string path)
    {
        string name=Text(input,field,path,255);
        if(!BlockWriteValidation.OrdinaryName(name))throw Invalid(path+"."+field,"Use an ordinary local native style name.");
        return name;
    }
    private static MLeaderStyleChanges337 Changes(JsonElement input,string path,bool creating)
    {
        Shape(input,path,"text_style_handle","text_height","arrow_size","landing_gap","dogleg_length","enable_landing","enable_dogleg","scale",
            "leader_linetype_handle","leader_lineweight_hundredth_mm","leader_color_aci","leader_color_rgb","text_color_aci","text_color_rgb",
            "enable_frame_text","text_alignment","text_attachment_type","text_angle_type","text_attachment_left","text_attachment_right");
        if(!creating&&!input.EnumerateObject().Any())throw Invalid(path,"Select at least one stored style default.");
        double? Number(string name,bool positive)
        {
            if(!input.TryGetProperty(name,out var value))return null;
            if(value.ValueKind!=JsonValueKind.Number||!value.TryGetDouble(out double number)||!double.IsFinite(number)||
                (positive?number<=0:number<0))throw Invalid(path+"."+name,positive?"Expected finite positive native value.":"Expected finite nonnegative native value.");
            return number;
        }
        bool? Bool(string name)
        {
            if(!input.TryGetProperty(name,out var value))return null;
            if(value.ValueKind is not (JsonValueKind.True or JsonValueKind.False))throw Invalid(path+"."+name,"Expected boolean.");
            return value.GetBoolean();
        }
        int? Integer(string name)
        {
            if(!input.TryGetProperty(name,out var value))return null;
            if(value.ValueKind!=JsonValueKind.Number||!value.TryGetInt32(out int number))throw Invalid(path+"."+name,"Expected native integer.");
            return number;
        }
        string? Handle(string name)=>input.TryGetProperty(name,out _)?Validation.NormalizeHandle(Text(input,name,path,16)):null;
        (int?,RgbColor?) Color(string prefix)
        {
            int? aci=Integer(prefix+"_aci");
            if(aci is <0 or >256)throw Invalid(path+"."+prefix+"_aci","Native ACI is0 ByBlock,1..255 explicit or256 ByLayer.");
            var values=new Dictionary<string,JsonElement>();
            if(input.TryGetProperty(prefix+"_rgb",out var rgb))values.Add("color_rgb",rgb);
            if(input.TryGetProperty(prefix+"_aci",out var selected))values.Add("color_aci",selected);
            return(aci,AppearanceValidation.Parse(Wire.Element(values),path+"."+prefix).ColorRgb);
        }
        int? weight=Integer("leader_lineweight_hundredth_mm");
        if(weight.HasValue&&!ModifyValidation.Lineweights.Contains(weight.Value))throw Invalid(path+".leader_lineweight_hundredth_mm","Select a supported native lineweight including inheritance.");
        string? Choice(string name,string[] choices)
        {
            if(!input.TryGetProperty(name,out _))return null;string selected=Text(input,name,path,64);
            if(!choices.Contains(selected,StringComparer.Ordinal))throw Invalid(path+"."+name,"Select a published native enum value.");return selected;
        }
        if(input.TryGetProperty("text_attachment_type",out _)&&(input.TryGetProperty("text_attachment_left",out _)||input.TryGetProperty("text_attachment_right",out _)))
            throw Invalid(path,"Use either the common horizontal attachment or sparse left/right defaults, not both.");
        var leader=Color("leader_color");var text=Color("text_color");
        return new(Handle("text_style_handle"),Number("text_height",true),Number("arrow_size",false),Number("landing_gap",false),
            Number("dogleg_length",false),Bool("enable_landing"),Bool("enable_dogleg"),Number("scale",true),Handle("leader_linetype_handle"),
            weight,leader.Item1,leader.Item2,text.Item1,text.Item2,Bool("enable_frame_text"),Choice("text_alignment",TextAlignments),Choice("text_attachment_type",HorizontalAttachments),
            Choice("text_angle_type",TextAngles),Choice("text_attachment_left",HorizontalAttachments),Choice("text_attachment_right",HorizontalAttachments));
    }
    public static MLeaderStyleEdit337 Parse(JsonElement input,string path)
    {
        string operation=Text(input,"operation",path,32);bool creating=operation=="create_mleader_style";
        if(!Operations.Contains(operation,StringComparer.Ordinal))throw Invalid(path+".operation","Unknown native MLeaderStyle operation.");
        Shape(input,path,creating?new[]{"operation","name","base_style_handle","base_style_token","properties"}:
            new[]{"operation","handle","expected_name","style_token","properties"});
        string handle=Validation.NormalizeHandle(Text(input,creating?"base_style_handle":"handle",path,16));
        string field=creating?"base_style_token":"style_token",token=Text(input,field,path,64);
        if(token.Length!=64||token.Any(c=>!"0123456789ABCDEF".Contains(c)))throw Invalid(path+"."+field,"Use the exact current uppercase editing.style_token from get_structure mleader_styles.");
        return new(operation,handle,token,Changes(Required(input,"properties",path),path+".properties",creating),
            creating?Name(input,"name",path):null,creating?null:Name(input,"expected_name",path));
    }
    public static void Batch(IReadOnlyList<StructureEdit> edits)
    {
        if(edits.Any(edit=>edit is MLeaderStyleEdit337)&&edits.Count!=1)
            throw new DomainException("INVALID_OPERATION_ORDER","Native MLeaderStyle creation/default editing uses one token-bound style operation per request; refresh its inventory afterward.");
    }
}
