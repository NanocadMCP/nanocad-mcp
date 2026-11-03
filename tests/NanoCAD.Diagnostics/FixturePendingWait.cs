using System.Text;
using System.Text.Json;

// Developer fixture transport only. No new product operation, native call,
// generated operation identity, context refresh, reconnect or host control.
internal static class FixturePendingWait
{
    internal delegate Task<JsonElement> Call(string operation,Dictionary<string,object?> input,CancellationToken cancel);
    internal static void RequireFreshPrefix(string path)
    {
        string directory=Path.GetDirectoryName(path)!,name=Path.GetFileName(path);
        if(Directory.EnumerateFiles(directory).Any(file=>
            string.Equals(Path.GetFileName(file),name,StringComparison.OrdinalIgnoreCase)||
            Path.GetFileName(file).StartsWith(name+".",StringComparison.OrdinalIgnoreCase)))
            throw new ArgumentException("Result prefix already has evidence; use a fresh prefix and retain the exact original request and receipts. No mutation was sent.");
    }
    internal static void SaveNew(string path,string text)
    {
        using var stream=new FileStream(path,FileMode.CreateNew,FileAccess.Write,FileShare.Read);
        using var writer=new StreamWriter(stream,new UTF8Encoding(false));writer.Write(text);writer.Flush();stream.Flush(true);
    }
    internal static void SaveNew(string path,JsonElement value)=>SaveNew(path,value.GetRawText());
    internal static void SaveNew(string path,object value)=>SaveNew(path,JsonSerializer.Serialize(value,new JsonSerializerOptions{WriteIndented=true}));
    private static bool Success(JsonElement result)=>result.ValueKind==JsonValueKind.Object&&result.TryGetProperty("success",out var success)&&success.ValueKind==JsonValueKind.True;
    private static JsonElement Lifetime(JsonElement response,string bridge)
    {
        if(!Success(response)||!response.TryGetProperty("data",out var data)||!data.TryGetProperty("instances",out var instances))
            throw new InvalidOperationException("Selected bridge lifetime cannot be established; do not submit/retrieve in another lifetime.");
        var matches=instances.EnumerateArray().Where(instance=>instance.TryGetProperty("reachable",out var reachable)&&reachable.ValueKind==JsonValueKind.True&&
            instance.GetProperty("descriptor").GetProperty("bridge_instance_id").GetString()==bridge).ToArray();
        if(matches.Length!=1)throw new InvalidOperationException("Original bridge lifetime ended or is unavailable; no reconnect or resubmission.");
        var descriptor=matches[0].GetProperty("descriptor");
        return JsonSerializer.SerializeToElement(new{bridge_instance_id=bridge,
            process_id=descriptor.GetProperty("process_id").GetInt32(),process_start_utc_ticks=descriptor.GetProperty("process_start_utc_ticks").GetInt64(),
            protocol_version=descriptor.GetProperty("protocol_version").GetString(),pipe_name=descriptor.GetProperty("pipe_name").GetString()});
    }
    internal static async Task<JsonElement> CaptureLifetime(Call call,string bridge,string output,CancellationToken cancel)
    {
        var response=await call("get_status",new(){{"bridge_instance_id",bridge}},cancel);
        SaveNew(output+".lifetime-preflight.json",response);return Lifetime(response,bridge);
    }
    private static bool Pending(JsonElement result)
    {
        if(result.TryGetProperty("error",out var error)&&error.ValueKind==JsonValueKind.Object)
        {
            string? code=error.GetProperty("code").GetString();
            if(code is "WRITE_OUTCOME_UNKNOWN" or "DOCUMENT_OUTCOME_UNKNOWN" or "BRIDGE_UNAVAILABLE")return false;
            if(code is "WRITE_OUTCOME_PENDING" or "DOCUMENT_OUTCOME_PENDING" or "OPERATION_PENDING")return true;
        }
        if(result.TryGetProperty("data",out var data)&&data.ValueKind==JsonValueKind.Object&&data.TryGetProperty("status",out var status)&&status.GetString()=="pending")return true;
        return Success(result)&&result.TryGetProperty("warnings",out var warnings)&&warnings.ValueKind==JsonValueKind.Array&&
            warnings.EnumerateArray().Any(value=>value.GetString()?.StartsWith("COMMAND_COMPLETION_PENDING",StringComparison.Ordinal)==true);
    }
    private static void RequirePendingIdentity(JsonElement result,string operationId)
    {
        string? observed=null;
        if(result.TryGetProperty("data",out var data)&&data.ValueKind==JsonValueKind.Object&&data.TryGetProperty("operation_id",out var id))
            observed=id.GetString();
        if(observed==null&&result.TryGetProperty("error",out var error)&&error.ValueKind==JsonValueKind.Object&&
            error.TryGetProperty("details",out var details)&&details.ValueKind==JsonValueKind.Object&&details.TryGetProperty("operation_id",out id))
            observed=id.GetString();
        if(!Guid.TryParseExact(observed,"D",out var actualId)||!Guid.TryParseExact(operationId,"D",out var savedId)||actualId!=savedId)
            throw new InvalidOperationException("Pending receipt does not identify the exact saved operation; no repeat or dependent write.");
    }
    internal static async Task<JsonElement> Await(Call call,string operation,Dictionary<string,object?> input,string bridge,
        JsonElement originalLifetime,string output)
    {
        string exactInput=JsonSerializer.Serialize(input);
        string operationId=JsonSerializer.Deserialize<JsonElement>(exactInput).GetProperty("operation_id").GetString()!;
        JsonElement? last=null;int attempt=0;bool submitted=false;
        SaveNew(output+".submission.json",new{operation,arguments=JsonSerializer.Deserialize<JsonElement>(exactInput),
            lifetime=originalLifetime,policy="Only exact saved operation ID/payload/revision in the original client and bridge lifetime; no live-operation total timer"});
        try
        {
            while(true)
            {
                if(JsonSerializer.Serialize(input)!=exactInput)throw new InvalidOperationException("Saved fixture payload changed; dependent writes stopped.");
                submitted=true;
                var result=await call(operation,input,CancellationToken.None);last=result.Clone();attempt++;
                SaveNew(output+$".attempt-{attempt:000000}.json",result);
                if(!Pending(result))
                {
                    SaveNew(output,result);
                    Console.WriteLine(JsonSerializer.Serialize(new{attempts=attempt,success=Success(result),result_path=output,
                        outcome="terminal_response_or_unknown_requires_caller_classification"}));
                    return result;
                }
                RequirePendingIdentity(result,operationId);
                Console.WriteLine($"Pending: attempt{attempt}; retain exact ID/payload/client/lifetime and await completion.");
                // A known commit with COMMAND_COMPLETION_PENDING is retrieved
                // by its same reserved ID; no new operation is ever submitted.
                await Task.Delay(1500,CancellationToken.None);
                var status=await call("get_status",new(){{"bridge_instance_id",bridge}},CancellationToken.None);
                SaveNew(output+$".status-{attempt:000000}.json",status);
                var actual=Lifetime(status,bridge);
                if(actual.GetRawText()!=originalLifetime.GetRawText())throw new InvalidOperationException("Original native process/bridge lifetime changed; no resubmission.");
            }
        }
        catch(Exception error)
        {
            // Do not manufacture a native terminal result. The caller sees a
            // missing final response and retains original request+all attempts.
            SaveNew(output+".reconciliation-stop.json",new{operation,submitted,attempts=attempt,lifetime=originalLifetime,
                last_native_response=last,error_type=error.GetType().Name,message=error.Message,
                policy="Unknown is not success or rollback. Reconcile the saved operation only in its original lifetime; never restart while pending."});
            throw;
        }
    }
}
