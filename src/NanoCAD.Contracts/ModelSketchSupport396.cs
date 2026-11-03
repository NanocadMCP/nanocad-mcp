using System.Text.Json;

namespace NanoCAD.Contracts;

public sealed record ModelSketchSupportSpec396(string Kind,string Id,string Token,
    bool RecoverLostSupport=false,string? ExpectedSupportId=null,string FrameReferencePolicy="preserve");
public sealed record ModelSketchNativeSupport396(string kind,string support_id,string source_token,string source_body_id,
    string mc_document_id,ModelProfilePlane340 native_frame,
    [property:System.Text.Json.Serialization.JsonIgnore(Condition=System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)] string? support_host_solid_id=null,
    [property:System.Text.Json.Serialization.JsonIgnore(Condition=System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)] string? sketch_host_solid_id=null);

public static class ModelSketchSupportValidation396
{
    public static ModelSketchSupportSpec396 Parse(JsonElement value,string path,bool allowRecovery=false)
    {
        DomainException Bad(string message)=>new("INVALID_ARGUMENT",message,false,new{path});
        if(value.ValueKind!=JsonValueKind.Object)throw Bad("Supply a native face or reusable WorkPlane in the selected part's owning document.");
        string Text(string field)
        {if(!value.TryGetProperty(field,out var item)||item.ValueKind!=JsonValueKind.String)throw Bad("Missing string "+field+".");return item.GetString()!;}
        string kind=Text("kind");string idField,tokenField;
        if(kind=="face"){idField="face_id";tokenField="face_token";}
        else if(kind=="workplane"){idField="plane_id";tokenField="expected_datum_token";}
        else throw Bad("Use face or workplane support.");
        var seen=new HashSet<string>(StringComparer.Ordinal);
        foreach(var field in value.EnumerateObject())
            if(!seen.Add(field.Name)||field.Name!="kind"&&field.Name!=idField&&field.Name!=tokenField&&
                !(allowRecovery&&kind=="workplane"&&(field.Name is "recover_lost_support" or "expected_support_id" or "frame_reference_policy")))
                throw Bad("Unknown or duplicate native support field.");
        if(!Guid.TryParseExact(Text(idField),"B",out var id)||id==Guid.Empty)throw Bad("Read the exact current nonempty native support ID.");
        string token=Text(tokenField);if(token.Length!=64||!token.All(Uri.IsHexDigit))throw Bad("Read the current native face or datum token.");
        bool recover=false;string? previous=null;
        if(value.TryGetProperty("recover_lost_support",out var recovery))
        {
            if(recovery.ValueKind is not JsonValueKind.True and not JsonValueKind.False)throw Bad("recover_lost_support must be boolean.");
            recover=recovery.GetBoolean();
        }
        if(recover)
        {
            if(!Guid.TryParseExact(Text("expected_support_id"),"B",out var old)||old==Guid.Empty)
                throw Bad("Read the exact retained failed support ID before recovery.");
            previous=old.ToString("B").ToUpperInvariant();
        }
        else if(value.TryGetProperty("expected_support_id",out _))throw Bad("expected_support_id requires explicit recover_lost_support=true.");
        string framePolicy="preserve";
        if(value.TryGetProperty("frame_reference_policy",out var policy))
        {
            if(!recover)throw Bad("frame_reference_policy requires explicit recover_lost_support=true.");
            if(policy.ValueKind!=JsonValueKind.String||policy.GetString() is not ("preserve" or "use_target_datum"))
                throw Bad("frame_reference_policy must be preserve or use_target_datum.");
            framePolicy=policy.GetString()!;
        }
        return new(kind,id.ToString("B").ToUpperInvariant(),token.ToUpperInvariant(),recover,previous,framePolicy);
    }
}
