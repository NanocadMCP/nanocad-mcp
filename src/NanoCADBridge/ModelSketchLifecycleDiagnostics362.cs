using System.Security.Cryptography;
using System.Text.Json;
using System.Text;
using NanoCAD.Contracts;

namespace NanoCADBridge;

// Completed managed observations only. A large partial native creation must
// retain a bounded exact unknown receipt rather than overflow the IPC frame.
internal static class ModelSketchLifecycleDiagnostics362
{
    static string Hash(byte[] bytes)=>Convert.ToHexString(SHA256.HashData(bytes));
    static string Prefix(string value,int maximumBytes)
    {
        var result=new StringBuilder();foreach(var rune in value.EnumerateRunes())
        {string candidate=result.ToString()+rune.ToString();if(JsonSerializer.SerializeToUtf8Bytes(candidate,Wire.Json).Length>maximumBytes)break;result.Append(rune.ToString());}return result.ToString();
    }
    internal static object Capture(string cause,string stage,string message,string? body,string? sketch,string? profile,string? feature,
        string[] observed,object? verification,string? cleanup)
    {
        var messageBytes=JsonSerializer.SerializeToUtf8Bytes(message,Wire.Json);string reportedMessage=Prefix(message,1024);
        var projectionBytes=JsonSerializer.SerializeToUtf8Bytes(observed,Wire.Json);var sample=new List<string>();int used=2;
        foreach(string identity in observed){int size=JsonSerializer.SerializeToUtf8Bytes(identity,Wire.Json).Length+1;if(used+size>2048)break;used+=size;sample.Add(identity);}
        var inner=JsonSerializer.SerializeToElement(verification,Wire.Json);var innerBytes=JsonSerializer.SerializeToUtf8Bytes(inner,Wire.Json);object? detail=inner;
        if(innerBytes.Length>4096)
        {
            var fields=new List<object>();used=2;int count=0;
            if(inner.ValueKind==JsonValueKind.Object)foreach(var field in inner.EnumerateObject())
            {count++;var row=new{name=field.Name,value=field.Value};int size=JsonSerializer.SerializeToUtf8Bytes(row,Wire.Json).Length+1;if(used+size>3072)continue;used+=size;fields.Add(row);}
            detail=new{scope="bounded_captured_verification_fields",serialized_bytes=innerBytes.Length,sha256=Hash(innerBytes),fields,omitted_field_count=count-fields.Count,complete=false};
        }
        object Result(bool includeDetail)=>new{cause=Prefix(cause,512),stage=Prefix(stage,512),message=reportedMessage,message_serialized_bytes=messageBytes.Length,message_sha256=Hash(messageBytes),omitted_message_utf16_units=message.Length-reportedMessage.Length,body_mc_id=body,sketch_id=sketch,profile_id=profile,feature_id=feature,
            observed_projection_count=observed.Length,observed_projection_ids_sha256=Hash(projectionBytes),observed_projection_ids=sample.ToArray(),omitted_projection_count=observed.Length-sample.Count,
            verification_details=includeDetail?detail:null,verification_details_sha256=Hash(innerBytes),verification_details_serialized_bytes=innerBytes.Length,
            verification_details_unavailable_reason=includeDetail?(string?)null:"captured_details_exceed_complete_unknown_receipt_byte_budget",cleanup_error=cleanup==null?null:Prefix(cleanup,512)};
        var result=Result(true);return JsonSerializer.SerializeToUtf8Bytes(result,Wire.Json).Length<=8192?result:Result(false);
    }
}
