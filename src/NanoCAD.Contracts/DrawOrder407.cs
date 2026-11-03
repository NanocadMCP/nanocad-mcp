using System.Text.Json;

namespace NanoCAD.Contracts;

public sealed record SetDrawOrder407(string[] Targets,string Position,string? RelativeTo=null):EditSpec("set_draw_order",Targets);
public static class DrawOrderValidation407
{
    public static EditSpec Parse(JsonElement item,string path,string[] handles)
    {
        string String(string key)
        {if(!item.TryGetProperty(key,out var value)||value.ValueKind!=JsonValueKind.String)throw new DomainException("INVALID_ARGUMENT",path+"."+key+": Expected a string.");return value.GetString()!;}
        var seen=new HashSet<string>(StringComparer.Ordinal);
        foreach(var property in item.EnumerateObject())
            if(!seen.Add(property.Name)||property.Name is not ("operation" or "handles" or "position" or "relative_to"))
                throw new DomainException("INVALID_ARGUMENT",path+"."+property.Name+": Unknown or duplicate field.");
        string position=String("position");if(position is not ("top" or "bottom" or "above" or "below"))throw new DomainException("INVALID_ARGUMENT",path+".position: Use top, bottom, above or below.");
        string? anchor=null;
        if(position is "above" or "below")
        {
            anchor=Validation.NormalizeHandle(String("relative_to"));
            if(handles.Contains(anchor,StringComparer.Ordinal))throw new DomainException("INVALID_ARGUMENT",path+".relative_to: Relative entity must differ from every moved entity.");
        }
        else if(item.TryGetProperty("relative_to",out _))throw new DomainException("INVALID_ARGUMENT",path+".relative_to: Only above/below uses a relative entity.");
        return new SetDrawOrder407(handles,position,anchor);
    }
    public static void Batch(IReadOnlyCollection<EditSpec> edits)
    {
        if(edits.Any(edit=>edit is SetDrawOrder407)&&edits.Any(edit=>edit is not SetDrawOrder407))
            throw new DomainException("INVALID_OPERATION_ORDER","Drawing-order changes use a batch of set_draw_order operations; apply geometry/content changes first, then order the resulting actual handles.");
    }
}
