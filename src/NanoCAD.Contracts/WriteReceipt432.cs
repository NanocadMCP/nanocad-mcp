using System.Reflection;
using System.Text.Json;

namespace NanoCAD.Contracts;

public sealed record OperationResultLocator432(string tool,string bridge_instance_id,string operation_id,
    string result_mode,string references_mode,string warnings_mode);
public sealed record WriteReceipt432(string operation,string? operation_id,string status,string outcome,
    bool details_omitted,ReadContext? historical_context,string? original_error_code,int original_warning_count,
    int returned_warning_count,bool warnings_complete,OperationResultLocator432? lookup,string next_action,string detail);

// Presentation-only copies. Neither this helper nor its measurement delegate
// replaces the bridge-owned ledger Result, its fingerprint or its phase.
public static class WriteResponseBudget432
{
    public const string MassWarning="NATIVE_MASS_PROPERTIES_MAY_BE_STALE: After slicing or separating imported solids, nanoCAD may retain the original volume and centroid on both pieces. The split geometry is available; do not use those native mass values for engineering calculations without independent verification.";
    public const string PagedWarning="RESPONSE_DETAILS_PAGED: Preserve this recorded outcome and operation_id. Full returned references and original warning text are available through get_operation_result in this bridge lifetime. Do not infer rollback, atomicity or permission to repeat a write from omitted details.";

    static object? Field(object? value,string name)
    {
        if(value is JsonElement json)return json.ValueKind==JsonValueKind.Object&&json.TryGetProperty(name,out var item)?item.Clone():null;
        if(value is null)return null;
        if(value is System.Collections.IDictionary dictionary)return dictionary.Contains(name)?dictionary[name]:null;
        var property=value.GetType().GetProperty(name,BindingFlags.Instance|BindingFlags.Public);
        return property==null||property.GetIndexParameters().Length!=0?null:property.GetValue(value);
    }
    static string? Text(object? value)=>value is string text?text:value is JsonElement json&&json.ValueKind==JsonValueKind.String?json.GetString():null;
    static string? TextField(object? value,string name)=>Text(Field(value,name));
    static bool NonemptyArray(object? value,string name)
    {
        var field=Field(value,name);return field is JsonElement json?json.ValueKind==JsonValueKind.Array&&json.GetArrayLength()!=0:field is Array array&&array.Length!=0;
    }
    public static OperationResultLookup432? Lookup(Result result)
    {
        if(result.data is OperationResultLookup432 lookup)return lookup;
        if(result.data is not JsonElement data||data.ValueKind!=JsonValueKind.Object||!data.TryGetProperty("recorded_result_included",out _)||!data.TryGetProperty("commit_recorded",out _))return null;
        try{return JsonSerializer.Deserialize<OperationResultLookup432>(data.GetRawText(),Wire.Json);}
        catch(JsonException){return null;}
    }
    static Result AddMassWarning(Result value)
    {
        if(!value.success||!NonemptyArray(value.data,"solid_slice_results")&&!NonemptyArray(value.data,"solid_separation_results")||value.warnings.Contains(MassWarning,StringComparer.Ordinal))return value;
        return value with{warnings=value.warnings.Append(MassWarning).ToArray()};
    }
    public static Result WithPresentationWarnings(Result value)
    {
        var result=AddMassWarning(value);var lookup=Lookup(result);
        if(lookup?.recorded_result is not {} recorded)return result;
        var decorated=AddMassWarning(recorded);
        return ReferenceEquals(recorded,decorated)?result:result with{data=lookup with{recorded_result=decorated}};
    }
    static bool Fits(Result value,int budget,Func<Result,int> measure)=>measure(value)<=budget;
    static Dictionary<string,object?> Scalars(object? value,IEnumerable<string> fields)
    {
        var result=new Dictionary<string,object?>(StringComparer.Ordinal);
        foreach(string name in fields)
        {
            var item=Field(value,name);
            if(item is JsonElement json)
            {if(json.ValueKind is JsonValueKind.String or JsonValueKind.Number or JsonValueKind.True or JsonValueKind.False or JsonValueKind.Null)result[name]=json.Clone();}
            else if(item is string or bool or byte or short or int or long or uint or ulong or float or double or decimal)result[name]=item;
        }
        return result;
    }

    public static bool TryFit(Result original,string operation,int budget,Func<Result,int> measure,out Result fitted,string? bridgeInstanceId=null)
    {
        if(budget<1)throw new ArgumentOutOfRangeException(nameof(budget));
        ArgumentNullException.ThrowIfNull(measure);
        fitted=original;
        try{if(Fits(original,budget,measure))return true;}
        catch(JsonException) when(operation=="get_operation_result"&&Lookup(original)?.recorded_result!=null)
        {
            // The historical Result may fit its own normal depth while the
            // extra lookup envelope exceeds that depth. Preserve the lookup
            // state/pages and omit only its nested historical body below.
        }
        var visible=WithPresentationWarnings(original);var lookup=Lookup(visible);
        if(!Wire.WriteOperations.Contains(operation)&&operation!="get_operation_result"&&original.receipt is null)
        {
            fitted=Result.Fail(original.request_id,new("RESPONSE_LIMIT_EXCEEDED","Request a smaller page; no object data was silently truncated."),original.context);
            return Fits(fitted,budget,measure);
        }
        var prior=visible.receipt;
        var historical=lookup?.recorded_result??visible;
        string? operationId=lookup?.operation_id??prior?.operation_id??TextField(visible.data,"operation_id")??TextField(visible.error?.details,"operation_id");
        string actualOperation=lookup?.operation??prior?.operation??operation;
        string? dataStatus=TextField(historical.data,"status");
        string? recordedOutcome=TextField(historical.error?.details,"outcome")??TextField(historical.data,"outcome");
        bool pending=historical.error?.code is "WRITE_OUTCOME_PENDING" or "DOCUMENT_OUTCOME_PENDING" or "OPERATION_PENDING";
        string status=prior?.status??dataStatus??(pending?"pending":recordedOutcome??(lookup is {found:true,job_finished:false,commit_recorded:false}?"pending":lookup?.outcome??"unknown"));
        string outcome=lookup?.outcome??prior?.outcome??recordedOutcome??(status=="pending"?"unknown":status);
        bool commandCompletionPending=visible.warnings.Concat(historical.warnings).Any(warning=>warning.StartsWith("COMMAND_COMPLETION_PENDING",StringComparison.Ordinal));
        string nextAction=(status=="pending"||lookup is {found:true,job_finished:false}||commandCompletionPending||prior?.next_action=="wait_for_result")?"wait_for_result":
            ((status is "committed" or "completed")||(outcome is "committed" or "completed"))?"inspect_current_result":
            outcome=="not_started"?"correct_request_or_obstacle":outcome=="rolled_back"?"refresh_context":"reconcile_affected_state";
        var context=prior?.historical_context??historical.context;
        string? errorCode=prior?.original_error_code??historical.error?.code;
        string? bridge=lookup?.bridge_instance_id??prior?.lookup?.bridge_instance_id??bridgeInstanceId??context?.bridge_instance_id;
        var locator=operationId!=null&&bridge!=null?new OperationResultLocator432("get_operation_result",bridge,operationId,"result","references","warnings"):prior?.lookup;
        string[] warnings=lookup?.recorded_result is {} nested?visible.warnings.Concat(nested.warnings).ToArray():visible.warnings;
        int originalWarningCount=prior?.original_warning_count??original.warnings.Length+(Lookup(original)?.recorded_result?.warnings.Length??0);
        bool priorWarningsComplete=prior?.warnings_complete??true;
        object? compactData;
        if(lookup!=null)compactData=lookup with{recorded_result=null,recorded_result_included=false};
        else if(visible.success)
        {
            var data=Scalars(visible.data,new[]{"operation_id","status","outcome","replayed","undo_history","action","written_path","written_bytes"});
            if(operationId!=null)data["operation_id"]=operationId;
            if(!data.ContainsKey("status"))data["status"]=status;
            data["details_omitted"]=true;compactData=data;
        }
        else compactData=visible.data;
        DomainError? CompactError(bool retainMessage)
        {
            if(visible.error is not {} error)return null;
            var details=Scalars(error.details,new[]{"operation_id","outcome","replayed","undo_history","phase","job_finished","commit_recorded"});
            if(operationId!=null)details["operation_id"]=operationId;
            details["outcome"]=outcome;details["details_omitted"]=true;
            return error with{message=retainMessage?error.message:"Original diagnostic details exceed this response budget; retain the operation outcome and use its lookup locator.",details=details};
        }
        Result Candidate(string[] returned,bool complete,bool small)
        {
            object? data=compactData;
            if(small&&lookup==null&&visible.success)
            {
                var minimal=Scalars(compactData,new[]{"operation_id","status","outcome","replayed","undo_history"});
                // Scalars also accepts dictionaries below through their public
                // serialized form only at this already small receipt boundary.
                if(compactData is Dictionary<string,object?> stored)minimal=stored.Where(pair=>pair.Key is "operation_id" or "status" or "outcome" or "replayed" or "undo_history").ToDictionary(pair=>pair.Key,pair=>pair.Value,StringComparer.Ordinal);
                minimal["details_omitted"]=true;data=minimal;
            }
            return visible with{data=data,error=CompactError(!small),warnings=returned,receipt=new(actualOperation,operationId,status,outcome,true,context,errorCode,originalWarningCount,returned.Length,complete&&priorWarningsComplete,locator,nextAction,
                locator==null?"Response details were omitted; no bridge-lifetime lookup locator is available. Preserve this outcome; omitted details never prove that nothing changed.":"The original ledger result is retained. Retrieve returned-reference and warning pages through the locator; never repeat a committed/completed write to obtain omitted details.")};
        }
        fitted=Candidate(warnings,true,false);
        if(Fits(fitted,budget,measure))return true;
        fitted=Candidate(warnings,true,true);
        if(Fits(fitted,budget,measure))return true;
        // Byte-driven warning selection, not a warning/object-count ceiling.
        // The mass-properties caveat remains visible even when all other full
        // warning text must be read in lossless ledger warning pages.
        var protectedWarnings=warnings.Where(warning=>warning==MassWarning).Distinct(StringComparer.Ordinal).Append(PagedWarning).ToArray();
        var pageable=warnings.Where(warning=>warning!=MassWarning).ToArray();
        var codes=pageable.Select(warning=>
        {
            int colon=warning.IndexOf(':');if(colon<1)return null;
            var code=warning.Substring(0,colon);return code.All(ch=>ch is >= 'A' and <= 'Z' or >= '0' and <= '9' or '_')?
                code+": Full warning text is available in the operation-result warning pages.":null;
        }).OfType<string>().Distinct(StringComparer.Ordinal).ToArray();
        var codedWarnings=protectedWarnings.Concat(codes).ToArray();
        // Prefer retaining all warning codes to hiding a later short caveat
        // behind an earlier oversized message. Full text remains losslessly
        // pageable; this compact view does not claim warning completeness.
        var coded=Candidate(codedWarnings,false,true);
        if(Fits(coded,budget,measure))protectedWarnings=codedWarnings;
        int low=0,high=pageable.Length;
        fitted=Candidate(protectedWarnings,false,true);
        if(!Fits(fitted,budget,measure))return false;
        while(low<high)
        {
            int middle=low+(high-low+1)/2;
            var attempt=Candidate(protectedWarnings.Concat(pageable.Take(middle)).ToArray(),false,true);
            if(Fits(attempt,budget,measure)){low=middle;fitted=attempt;}else high=middle-1;
        }
        return true;
    }
}
