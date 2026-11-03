using System.Text.Json;
using System.Text.Json.Nodes;
using NanoCAD.Contracts;
using Teigha.DatabaseServices;
using Index=NanoCADBridge.CadTableContentsReader364;
using Legacy=NanoCADBridge.CadTableEditingReader;

namespace NanoCADBridge;

// Same-style defaults propagation, not assignment of an unrelated TableStyle.
// Every comparison input is read by an existing native reader. This adapter
// does not manufacture slots, rewrite content or select any retained Field.
internal static class CadTableStyleConsumerPreservation375
{
    static bool Same(object? a,object? b)=>JsonSerializer.Serialize(a,Wire.Json)==JsonSerializer.Serialize(b,Wire.Json);
    static JsonElement Root(JsonElement metadata)
    {var root=JsonNode.Parse(metadata.GetRawText())!.AsObject();root.Remove("owned");return Wire.Element(root);}
    static JsonElement DiagnosticMetadata(JsonElement metadata,Action cancel)
    {
        var root=JsonNode.Parse(metadata.GetRawText())!.AsObject();
        // Diagnostic projection only. Admission is still performed by the
        // actual exclusive-block/member/Field owner comparers below.
        foreach(var node in root["owned"]!.AsArray())
        {cancel();var record=node!.AsObject();if(record.ContainsKey("table_cell_mtext_owner"))record.Remove("cached_contents");}
        return Wire.Element(root);
    }
    internal static bool HasContext(DomainException error)
    {
        if(error.Error.details==null)return false;var details=Wire.Element(error.Error.details);
        return details.ValueKind==JsonValueKind.Object&&details.TryGetProperty("table_handle",out _)&&details.TryGetProperty("phase",out _);
    }
    internal static DomainException Diagnostics(DomainException error,string handle,string phase,object before,object after,Action cancel)
    {
        var a=Wire.Element(before);var b=Wire.Element(after);
        var difference=CadTableStyleCloneDiagnostics367.Capture("set_table_style_properties",handle,a,b,cancel,
            phase,"retained_consumer_state_and_observed_defaults","actual_completed_native_consumer_state");
        // Existing leaf diagnostics are <=4096 bytes; do not expose entire
        // native inventories through a failure response or change admission.
        var details=new{table_handle=handle,phase,failed_predicate=error.Error.message,
            original_details_sha256=error.Error.details==null?null:Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(error.Error.details,Wire.Json))),
            original_details=error.Error.details!=null&&JsonSerializer.SerializeToUtf8Bytes(error.Error.details,Wire.Json).Length<=1024?error.Error.details:null,
            difference,scope="completed_native_reads_only; root_identity_and_unselected_Field_ownership_not_normalized"};
        if(JsonSerializer.SerializeToUtf8Bytes(details,Wire.Json).Length<=8192)return new(error.Error.code,error.Error.message,error.Error.retryable,details);
        return new(error.Error.code,error.Error.message,error.Error.retryable,new{table_handle=handle,phase,
            before_sha256=Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(a,Wire.Json))),
            after_sha256=Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(b,Wire.Json))),
            diagnostic_omitted=true,byte_budget=8192});
    }
    internal static void VerifyLegacyMetadata(CadTableStyleWriter337.Consumer before,Legacy.Snapshot after,JsonElement metadata,
        Index.Snapshot contents,Action cancel)
    {
        string handle=before.Before.Identity.Split(':')[0];
        var rootBefore=new{identity=before.Before.Identity,rows=before.Before.Rows,columns=before.Before.Columns,root=Root(before.Metadata)};
        var rootAfter=new{identity=after.Identity,rows=after.Rows,columns=after.Columns,root=Root(metadata)};
        if(!Same(rootBefore,rootAfter))throw Diagnostics(new DomainException("NATIVE_TABLE_STYLE_MISMATCH","Native style inheritance changed Table root identity, topology or root metadata."),
            handle,"single_root_after_native_layout",rootBefore,rootAfter,cancel);
        try
        {
            // Existing formula comparator expects the indexed reader's actual
            // root {extension,...} shape. All selected sets are EMPTY: Field
            // IDs/owners/code/settings/siblings and anonymous BTR identity stay
            // exact. Only proved MText generated cached_contents are excluded;
            // plain carrier turnover uses the existing exclusive-owner proof.
            CadTableFormulaField358.Verify(before.Contents.Metadata,contents.Metadata,before.Before,after,
                Array.Empty<(int Row,int Column)>(),Array.Empty<(int Row,int Column)>(),cancel);
        }
        catch(DomainException error)
        {throw Diagnostics(error,handle,"single_owned_graph_after_native_layout",DiagnosticMetadata(before.Contents.Metadata,cancel),DiagnosticMetadata(contents.Metadata,cancel),cancel);}
    }
    internal static string[] VerifySingle(CadTableStyleWriter337.Consumer consumer,Legacy.Snapshot after,Index.Snapshot contents,
        CadTableStyleWriter337.Stored beforeStyle,CadTableStyleWriter337.Stored afterStyle,TableStyleProperties337 edit,Action cancel)
    {
        var before=consumer.Before;string handle=before.Identity.Split(':')[0];var warnings=new List<string>();
        if(CadTableNativeLayout356.VerifyTopology(before,after,cancel))warnings.Add(CadTableNativeLayout356.Warning);
        for(int offset=0;offset<before.Values.Length;offset++)
        {
            cancel();var old=before.Values[offset];var current=after.Values[offset];
            if(old==null&&current==null)continue;
            if(old==null||current==null)throw Diagnostics(new DomainException("NATIVE_TABLE_STYLE_MISMATCH","Native style inheritance created or removed stored cell content."),
                handle,"single_native_content_count",new{offset,content=old},new{offset,content=current},cancel);
            var expected=CadTableFormulaEditor343.NormalizeEvaluation(old,current);
            // Formula cache normalization does not grant a change to an
            // explicit scale, or to disabled automatic scaling.
            if(!old.auto_scale||!current.auto_scale||(old.overrides&8)!=0)expected=expected with{scale=old.scale};
            if(old.auto_scale&&current.auto_scale&&(old.overrides&8)==0)expected=expected with{scale=current.scale};
            if(!Same(expected,current))throw Diagnostics(new DomainException("NATIVE_TABLE_STYLE_MISMATCH","Native style inheritance changed retained raw values/types/units/formulas/Field definitions or explicit content layout."),
                handle,"single_native_stored_content",new{row=before.Cells[offset].row,column=before.Cells[offset].column,content=expected},
                new{row=after.Cells[offset].row,column=after.Cells[offset].column,content=current},cancel);
        }
        warnings.AddRange(CadTableStyleIndexedConsumers367.VerifySingle(new(consumer.Id,consumer.Contents,consumer.RowRoles),contents,
            beforeStyle,afterStyle,edit,cancel));
        return warnings.Distinct(StringComparer.Ordinal).ToArray();
    }
}
