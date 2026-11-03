using System.Text.Json;
using System.Text.Json.Serialization;

namespace NanoCAD.Contracts;

public sealed record MTextFormatChanges(string? TextStyleHandle=null,double? TextHeight=null,double? Width=null,
    string? Attachment=null,string? LineSpacingStyle=null,double? LineSpacingFactor=null,Point3? DirectionWcs=null);
public sealed record SetMTextFormatSpec(string[] Targets,string MTextToken,MTextFormatChanges Properties):EditSpec("set_mtext_format",Targets);
public sealed record MTextEditingMetadata(
    [property:JsonIgnore(Condition=JsonIgnoreCondition.WhenWritingNull)] string? mtext_token,
    bool editable,
    [property:JsonIgnore(Condition=JsonIgnoreCondition.WhenWritingNull)] string? unavailable_reason=null);
public sealed record MTextFormatChange(EntityRef source_ref,string mtext_token,EntityRef text_style_ref,double text_height,
    double width,string attachment,string line_spacing_style,double line_spacing_factor,double[] direction_wcs,
    [property:JsonIgnore(Condition=JsonIgnoreCondition.WhenWritingNull)] string[]? warnings=null);

public static class MTextFormatValidation
{
    public static void Batch(IReadOnlyList<EditSpec> edits)
    {
        foreach(var edit in edits.OfType<SetMTextFormatSpec>())
            if(edits.Any(other=>!ReferenceEquals(other,edit)&&other.Handles.Contains(edit.Handles[0],StringComparer.OrdinalIgnoreCase)))
                throw new DomainException("INVALID_OPERATION_ORDER","Combine all requested MText format fields in one token-bound edit per target; refresh its token before another edit.");
    }
}

public static partial class ModifyValidation
{
    private static SetMTextFormatSpec ParseMTextFormat(JsonElement item,string path,string[] handles)
    {
        Shape(item,path,"operation","handles","mtext_token","properties");
        if(handles.Length!=1)throw Invalid(path+".handles","Select one existing MText for its fresh token-bound format edit.");
        string token=Text(item,"mtext_token",path,64);
        if(token.Length!=64||token.Any(c=>!Uri.IsHexDigit(c)))throw Invalid(path+".mtext_token","Read the64-hex mtext_token from inspect_objects geometry.formatting.editing.");
        var p=Required(item,"properties",path);string pp=path+".properties";
        Shape(p,pp,"text_style_handle","text_height","width","attachment","line_spacing_style","line_spacing_factor","direction_wcs");
        if(!p.EnumerateObject().Any())throw Invalid(pp,"Supply at least one format/layout field.");
        string? style=p.TryGetProperty("text_style_handle",out _)?Validation.NormalizeHandle(Text(p,"text_style_handle",pp,16)):null;
        double? Positive(string field)
        {
            if(!p.TryGetProperty(field,out var value))return null;
            double number=Number(value,pp+"."+field,0,1e6);
            if(number<=0)throw Invalid(pp+"."+field,"Use a positive native value.");
            return number;
        }
        double? width=p.TryGetProperty("width",out var w)?Number(w,pp+".width",0,1e6):null;
        string? attachment=p.TryGetProperty("attachment",out _)?Text(p,"attachment",pp,20):null;
        if(attachment!=null&&attachment is not ("top_left" or "top_center" or "top_right" or "middle_left" or "middle_center" or "middle_right" or "bottom_left" or "bottom_center" or "bottom_right"))
            throw Invalid(pp+".attachment","Use an ordinary top/middle/bottom left/center/right anchor.");
        string? spacing=p.TryGetProperty("line_spacing_style",out _)?Text(p,"line_spacing_style",pp,16):null;
        if(spacing is not (null or "at_least" or "exactly"))throw Invalid(pp+".line_spacing_style","Use at_least or exactly.");
        Point3? direction=null;
        if(p.TryGetProperty("direction_wcs",out var d))
        {
            if(d.ValueKind!=JsonValueKind.Array||d.GetArrayLength()!=3)throw Invalid(pp+".direction_wcs","Supply a WCS XYZ unit direction.");
            direction=new(Number(d[0],pp+".direction_wcs",-1,1),Number(d[1],pp+".direction_wcs",-1,1),Number(d[2],pp+".direction_wcs",-1,1));
            if(Math.Abs(direction.X*direction.X+direction.Y*direction.Y+direction.Z*direction.Z-1)>1e-10)
                throw Invalid(pp+".direction_wcs","Supply an explicit unit vector; no implicit scaling or normalization.");
        }
        return new(handles,token.ToUpperInvariant(),new(style,Positive("text_height"),width,attachment,spacing,Positive("line_spacing_factor"),direction));
    }
}
