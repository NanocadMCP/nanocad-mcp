using System.Text.Json;

namespace NanoCAD.Contracts;

// An additive native observation after a verified defining-geometry edit is
// not proof of data loss. Existing groups and all other metadata stay exact;
// unrequested native handle/ObjectId/layer references remain outside this path.
public static class DimensionAddedXData358
{
    sealed record App(string Name,JsonElement[] Items);
    static bool Exact(JsonElement a,JsonElement b)=>a.GetRawText()==b.GetRawText();
    static bool Apps(JsonElement data,Action cancel,out App[] apps)
    {
        var result=new List<App>();var names=new HashSet<string>(StringComparer.Ordinal);string? name=null;var items=new List<JsonElement>();
        apps=[];if(data.ValueKind==JsonValueKind.Null)return true;if(data.ValueKind!=JsonValueKind.Array)return false;
        foreach(var item in data.EnumerateArray())
        {
            cancel();if(item.ValueKind!=JsonValueKind.Object||!item.TryGetProperty("code",out var code)||code.ValueKind!=JsonValueKind.Number||!code.TryGetInt32(out int type)||!item.TryGetProperty("value",out var value))return false;
            if(type==1001)
            {
                if(value.ValueKind!=JsonValueKind.String||string.IsNullOrEmpty(value.GetString())||value.GetString()!.Any(char.IsControl)||!names.Add(value.GetString()!))return false;
                if(name!=null)result.Add(new(name,items.ToArray()));name=value.GetString();items=[];
            }
            else if(name==null)return false;
            items.Add(item.Clone());
        }
        if(name!=null)result.Add(new(name,items.ToArray()));apps=result.ToArray();return true;
    }
    static bool ObjectReference(JsonElement value,Action cancel)
    {
        cancel();if(value.ValueKind==JsonValueKind.Object)
        {
            if(value.TryGetProperty("native_type",out var type)&&type.GetString()=="ObjectId")return true;
            foreach(var property in value.EnumerateObject()){cancel();if(ObjectReference(property.Value,cancel))return true;}
        }
        else if(value.ValueKind==JsonValueKind.Array)foreach(var item in value.EnumerateArray()){cancel();if(ObjectReference(item,cancel))return true;}
        return false;
    }
    static bool Nonreferential(App app,Action cancel)
    {
        int depth=0;
        foreach(var item in app.Items.Skip(1))
        {
            cancel();int code=item.GetProperty("code").GetInt32();var value=item.GetProperty("value");
            if(code is not(1000 or 1002 or 1004 or 1010 or 1011 or 1012 or 1013 or 1040 or 1041 or 1042 or 1070 or 1071)||ObjectReference(value,cancel))return false;
            if(code==1002)
            {if(value.ValueKind!=JsonValueKind.String)return false;if(value.GetString()=="{")depth++;else if(value.GetString()=="}"){if(--depth<0)return false;}else return false;}
        }
        return depth==0;
    }
    public static bool MetadataPreserved(JsonElement before,JsonElement after,Action cancel,out JsonElement added)
    {
        cancel();added=Wire.Element(Array.Empty<object>());if(before.ValueKind!=JsonValueKind.Object||after.ValueKind!=JsonValueKind.Object)return false;
        foreach(var property in before.EnumerateObject())
        {cancel();if(!after.TryGetProperty(property.Name,out var current)||property.Name!="xdata"&&!Exact(property.Value,current))return false;}
        foreach(var property in after.EnumerateObject()){cancel();if(!before.TryGetProperty(property.Name,out _))return false;}
        if(!before.TryGetProperty("xdata",out var oldData)||!after.TryGetProperty("xdata",out var newData)||!Apps(oldData,cancel,out var old)||!Apps(newData,cancel,out var currentApps))return false;
        var oldNames=old.Select(app=>app.Name).ToHashSet(StringComparer.Ordinal);var retained=currentApps.Where(app=>oldNames.Contains(app.Name)).ToArray();
        if(retained.Length!=old.Length)return false;
        for(int index=0;index<old.Length;index++)
        {cancel();if(old[index].Name!=retained[index].Name||old[index].Items.Length!=retained[index].Items.Length)return false;
            for(int item=0;item<old[index].Items.Length;item++){cancel();if(!Exact(old[index].Items[item],retained[index].Items[item]))return false;}}
        var additions=currentApps.Where(app=>!oldNames.Contains(app.Name)).ToArray();if(additions.Length==0)return false;
        foreach(var app in additions){cancel();if(!Nonreferential(app,cancel))return false;}
        added=Wire.Element(additions.Select(app=>new{application=app.Name,items=app.Items}).ToArray());return true;
    }
}

// One cumulative observation per result, within the ordinary modifier's existing
// 16KiB prewrite envelope reserve. Measure the response-escaped string, not only
// its unescaped sample. Large observations are compacted without refusing edits.
public static class DimensionAddedXDataWarning358
{
    public const int SerializedWarningByteBudget=4096;
    const string Prefix="DIMENSION_NATIVE_XDATA_ADDED: Verified requested geometry/effective style and all previous readable groups, identities, owned data and links remain exact. Native recompute added nonreferential groups; private-state completeness is unobserved. ";
    public static void Append(List<string> warnings,string handle,JsonElement added,Action cancel)
    {
        cancel();int index=warnings.FindIndex(value=>value.StartsWith(Prefix,StringComparison.Ordinal));
        long count=0,total=0;string chain="";JsonElement? sample=null;
        if(index>=0)
        {
            using var prior=JsonDocument.Parse(warnings[index][Prefix.Length..]);var value=prior.RootElement;
            count=value.GetProperty("observation_count").GetInt64();total=value.GetProperty("actual_typed_bytes").GetInt64();
            chain=value.GetProperty("ordered_observation_sha256").GetString()!;
            if(value.GetProperty("typed_sample").ValueKind!=JsonValueKind.Null)sample=value.GetProperty("typed_sample").Clone();
        }
        var bytes=JsonSerializer.SerializeToUtf8Bytes(added,Wire.Json);cancel();
        string digest=Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(bytes));
        var header=System.Text.Encoding.UTF8.GetBytes(chain+":"+handle+":"+bytes.Length+":");
        var material=new byte[checked(header.Length+bytes.Length)];header.CopyTo(material,0);bytes.CopyTo(material,header.Length);
        chain=Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(material));count++;total+=bytes.LongLength;cancel();
        if(count==1)sample=bytes.Length<=1024?Wire.Element(new{handle,complete=true,actual_bytes=bytes.Length,sha256=digest,added_typed_data=added.Clone()}):
            Wire.Element(new{handle,complete=false,actual_bytes=bytes.Length,sha256=digest,unavailable_reason="Typed sample exceeds its actual serialized-byte budget; complete managed observation was hashed."});
        string Render(JsonElement? value)=>Prefix+JsonSerializer.Serialize(new{observation_count=count,actual_typed_bytes=total,ordered_observation_sha256=chain,
            complete_observation_inventory=false,typed_sample=value,scope="ordered captured additions; bounded sample; no rollback or private-state claim"},Wire.Json);
        string warning=Render(sample);cancel();
        if(JsonSerializer.SerializeToUtf8Bytes(warning,Wire.Json).Length>SerializedWarningByteBudget)warning=Render(null);
        if(index<0)warnings.Add(warning);else warnings[index]=warning;cancel();
    }
}
