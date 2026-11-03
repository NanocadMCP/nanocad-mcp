using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using NanoCAD.Contracts;

namespace NanoCADBridge;

// Completed native default observations only. No setter, normalization or
// preservation exception is introduced by this diagnostic.
internal static class CadTableStyleCloneDiagnostics367
{
    static string Hash(JsonElement value)=>Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(value,Wire.Json)));
    static string Segment(string value)=>value.Length<=80?value.Replace("~","~0").Replace("/","~1"):"@sha256="+Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
    static JsonElement? Small(JsonElement? value)=>value.HasValue&&JsonSerializer.SerializeToUtf8Bytes(value.Value,Wire.Json).Length<=256?value:null;
    internal static object Capture(string operation,string source,JsonElement before,JsonElement clone,Action cancel,
        string stage="detached_TableStyle_clone_before_requested_setters",string beforeSemantics="actual_source_defaults",string actualSemantics="actual_detached_clone_defaults")
    {
        var changes=new List<object>();long count=0;int used=1024;
        string? Identity(JsonElement root,string array,int index,string key)
        {
            if(!root.TryGetProperty(array,out var items)||items.ValueKind!=JsonValueKind.Array||index>=items.GetArrayLength()||
                !items[index].TryGetProperty(key,out var item)||item.ValueKind!=JsonValueKind.String)return null;
            string value=item.GetString()!;
            return Encoding.UTF8.GetByteCount(value)<=96?value:"@sha256="+Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
        }
        void Add(string path,JsonElement? a,JsonElement? b)
        {
            cancel();count++;
            string[] parts=path.Split('/');int index;
            string? roleBefore=null,roleClone=null,namedBefore=null,namedClone=null;
            if(parts.Length>2&&int.TryParse(parts[2],out index))
            {
                if(parts[1]=="rows"){roleBefore=Identity(before,"rows",index,"row_type");roleClone=Identity(clone,"rows",index,"row_type");}
                if(parts[1]=="named_cell_styles"){namedBefore=Identity(before,"named_cell_styles",index,"name");namedClone=Identity(clone,"named_cell_styles",index,"name");}
            }
            if(Encoding.UTF8.GetByteCount(path)>256)path="/@path-sha256="+Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(path)));
            var change=new{path,before_role=roleBefore,clone_role=roleClone,before_named_style=namedBefore,clone_named_style=namedClone,
                before_kind=a?.ValueKind.ToString(),clone_kind=b?.ValueKind.ToString(),before_value=Small(a),clone_value=Small(b),
                before_sha256=a.HasValue?Hash(a.Value):null,clone_sha256=b.HasValue?Hash(b.Value):null,
                before_value_omitted=a.HasValue&&!Small(a).HasValue,clone_value_omitted=b.HasValue&&!Small(b).HasValue};
            int size=JsonSerializer.SerializeToUtf8Bytes(change,Wire.Json).Length+1;
            if(used+size<=3584){changes.Add(change);used+=size;}
        }
        void Missing(string path,JsonElement value,bool added)
        {
            cancel();
            if(value.ValueKind==JsonValueKind.Object&&value.EnumerateObject().Any())
            {foreach(var item in value.EnumerateObject())Missing(path+"/"+Segment(item.Name),item.Value,added);return;}
            if(value.ValueKind==JsonValueKind.Array&&value.GetArrayLength()>0)
            {for(int index=0;index<value.GetArrayLength();index++)Missing(path+"/"+index,value[index],added);return;}
            Add(path,added?null:value,added?value:null);
        }
        void Walk(string path,JsonElement a,JsonElement b)
        {
            cancel();if(a.GetRawText()==b.GetRawText())return;
            if(a.ValueKind==JsonValueKind.Object&&b.ValueKind==JsonValueKind.Object)
            {
                foreach(var item in a.EnumerateObject())if(b.TryGetProperty(item.Name,out var current))Walk(path+"/"+Segment(item.Name),item.Value,current);else Missing(path+"/"+Segment(item.Name),item.Value,false);
                foreach(var item in b.EnumerateObject())if(!a.TryGetProperty(item.Name,out _))Missing(path+"/"+Segment(item.Name),item.Value,true);return;
            }
            if(a.ValueKind==JsonValueKind.Array&&b.ValueKind==JsonValueKind.Array)
            {
                for(int index=0;index<Math.Max(a.GetArrayLength(),b.GetArrayLength());index++)
                    if(index>=a.GetArrayLength())Missing(path+"/"+index,b[index],true);else if(index>=b.GetArrayLength())Missing(path+"/"+index,a[index],false);else Walk(path+"/"+index,a[index],b[index]);return;
            }
            Add(path,a,b);
        }
        Walk("",before,clone);
        object Result(bool details)=>new{stage,operation,source_handle=source,before_semantics=beforeSemantics,clone_semantics=actualSemantics,
            scope="actual_completed_native_default_observations; no_inert_field_or_metadata_waiver",
            before_sha256=Hash(before),clone_sha256=Hash(clone),changed_leaf_count=count,
            changes=details?changes.ToArray():Array.Empty<object>(),omitted_leaf_count=count-(details?changes.Count:0),
            diagnostic_compaction=details?(string?)null:"actual4096_byte_detail_budget; complete_counts_and_hashes_retained"};
        var result=Result(true);return JsonSerializer.SerializeToUtf8Bytes(result,Wire.Json).Length<=4096?result:Result(false);
    }
}
