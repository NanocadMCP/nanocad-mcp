using System.Text.Json;
using NanoCAD.Contracts;

namespace NanoCADBridge;

// Completed managed snapshots only. Native display setters store their typed
// per-entity dimvar overrides under ACAD/DSTYLE. This is not an ACAD exemption.
// Full effective-style readback must pass independently before this guard runs.
internal static class CadDimensionDStyleXData349
{
    private sealed record App(string Name,JsonElement Header,JsonElement[] Items);
    private sealed record Section(JsonElement[] Outside,Dictionary<int,JsonElement> Values,bool Present);
    private static bool Exact(JsonElement first,JsonElement second)=>first.GetRawText()==second.GetRawText();
    private static bool Code(JsonElement item,int code)=>item.ValueKind==JsonValueKind.Object&&
        item.TryGetProperty("code",out var actual)&&actual.ValueKind==JsonValueKind.Number&&actual.TryGetInt32(out int value)&&value==code;
    private static bool Text(JsonElement item,int code,string text)=>Code(item,code)&&item.TryGetProperty("value",out var value)&&
        value.ValueKind==JsonValueKind.String&&value.GetString()==text;

    internal static bool MetadataPreserved(JsonElement before,JsonElement current,JsonElement requested,Action cancel)
    {
        cancel();if(Exact(before,current))return true;
        if(before.ValueKind!=JsonValueKind.Object||current.ValueKind!=JsonValueKind.Object)return false;
        foreach(var property in before.EnumerateObject())
        {
            cancel();if(!current.TryGetProperty(property.Name,out var actual))return false;
            if(property.Name!="xdata"&&!Exact(property.Value,actual))return false;
        }
        foreach(var property in current.EnumerateObject()){cancel();if(!before.TryGetProperty(property.Name,out _))return false;}
        if(!before.TryGetProperty("xdata",out var oldData)||!current.TryGetProperty("xdata",out var newData))return false;
        var expected=Requested(requested,cancel);
        return expected.Count>0&&XDataPreserved(oldData,newData,expected,cancel);
    }

    // IDs/types are the documented DIMSTYLE DXF dimvar IDs and XData value
    // restypes. Actual values use the same native CLR coercions as Display().
    private static Dictionary<int,JsonElement> Requested(JsonElement properties,Action cancel)
    {
        var values=new Dictionary<int,JsonElement>();
        foreach(var property in properties.EnumerateObject())
        {
            cancel();int id=property.Name switch{
                "linear_factor"=>144,"linear_unit_format"=>277,"angular_unit_format"=>275,"rounding"=>45,
                "linear_zero_suppression"=>78,"angular_zero_suppression"=>79,"alternate_enabled"=>170,
                "alternate_factor"=>143,"alternate_unit_format"=>273,"alternate_precision"=>171,"alternate_rounding"=>148,
                "alternate_zero_suppression"=>285,"extension_offset"=>42,"extension_beyond"=>44,"text_gap"=>147,
                "suppress_extension1"=>75,"suppress_extension2"=>76,"suppress_dimension1"=>281,"suppress_dimension2"=>282,
                "primary_prefix"=>3,"alternate_prefix"=>4,"primary_suffix" or "alternate_suffix"=>-1,
                _=>throw new DomainException("INVALID_ARGUMENT","Unknown native dimension display override.")};
            if(id<0)continue;
            if(id is 3 or 4)
            {
                string suffix=id==3?"primary_suffix":"alternate_suffix";
                values.Add(id,Wire.Element(new{code=1000,value=property.Value.GetString()+"<>"+properties.GetProperty(suffix).GetString()}));
                continue;
            }
            object native=DimensionEditingValidation341.NativeDisplayValue(property.Name,property.Value);
            if(native is double real)values.Add(id,Wire.Element(new{code=1040,value=NativeOwnedMetadata.Value(real)}));
            else values.Add(id,Wire.Element(new{code=1070,value=native is bool flag?(flag?1:0):(int)native}));
        }
        return values;
    }
    private static bool Apps(JsonElement data,Action cancel,out App[] result)
    {
        result=Array.Empty<App>();if(data.ValueKind==JsonValueKind.Null)return true;
        if(data.ValueKind!=JsonValueKind.Array)return false;
        var apps=new List<App>();var names=new HashSet<string>(StringComparer.Ordinal);int index=0;
        while(index<data.GetArrayLength())
        {
            cancel();var header=data[index++];
            if(!Code(header,1001)||!header.TryGetProperty("value",out var value)||value.ValueKind!=JsonValueKind.String)return false;
            string name=value.GetString()!;if(!names.Add(name))return false;
            var items=new List<JsonElement>();
            while(index<data.GetArrayLength()&&!Code(data[index],1001)){cancel();items.Add(data[index++]);}
            apps.Add(new(name,header,items.ToArray()));
        }
        result=apps.ToArray();return true;
    }
    private static bool DStyle(App? app,Action cancel,out Section result)
    {
        result=new(Array.Empty<JsonElement>(),new(),false);if(app==null)return true;
        var outside=new List<JsonElement>();var values=new Dictionary<int,JsonElement>();bool present=false;int depth=0;
        for(int index=0;index<app.Items.Length;index++)
        {
            cancel();var item=app.Items[index];
            if(depth==0&&Text(item,1000,"DSTYLE"))
            {
                if(present||index+1>=app.Items.Length||!Text(app.Items[++index],1002,"{"))return false;
                present=true;bool closed=false;
                while(++index<app.Items.Length)
                {
                    cancel();var key=app.Items[index];
                    if(Text(key,1002,"}")){closed=true;break;}
                    if(!Code(key,1070)||!key.TryGetProperty("value",out var idValue)||idValue.ValueKind!=JsonValueKind.Number||!idValue.TryGetInt32(out int id)||
                        id<=0||id>short.MaxValue||index+1>=app.Items.Length)return false;
                    var value=app.Items[++index];
                    // Other retained dimvars can include object references.
                    // Their complete typed pair is preserved without decoding.
                    if(!TypedValue(value)||!values.TryAdd(id,value))return false;
                }
                if(!closed)return false;
                continue;
            }
            outside.Add(item);
            if(Code(item,1002))
            {
                if(Text(item,1002,"{"))depth++;
                else if(!Text(item,1002,"}")||--depth<0)return false;
            }
        }
        if(depth!=0)return false;
        result=new(outside.ToArray(),values,present);return true;
    }
    private static bool SameItems(JsonElement[] first,JsonElement[] second,Action cancel)
    {
        if(first.Length!=second.Length)return false;
        for(int index=0;index<first.Length;index++){cancel();if(!Exact(first[index],second[index]))return false;}
        return true;
    }
    private static bool TypedValue(JsonElement item)
    {
        if(item.ValueKind!=JsonValueKind.Object||!item.TryGetProperty("code",out var type)||type.ValueKind!=JsonValueKind.Number||
            !type.TryGetInt32(out int code)||!item.TryGetProperty("value",out var value))return false;
        if(code==1000)return value.ValueKind==JsonValueKind.String;
        if(code==1070)return value.ValueKind==JsonValueKind.Number&&value.TryGetInt32(out int number)&&number>=short.MinValue&&number<=ushort.MaxValue;
        if(code==1040)return value.ValueKind==JsonValueKind.Object&&value.TryGetProperty("native_type",out var native)&&
            native.ValueKind==JsonValueKind.String&&native.GetString()=="double"&&value.TryGetProperty("bits",out var bits)&&
            bits.ValueKind==JsonValueKind.String&&bits.GetString() is {} text&&text.Length==16&&
            ulong.TryParse(text,System.Globalization.NumberStyles.HexNumber,System.Globalization.CultureInfo.InvariantCulture,out _);
        if(code==1005)return value.ValueKind==JsonValueKind.String||value.ValueKind==JsonValueKind.Object&&
            value.TryGetProperty("native_type",out var reference)&&reference.ValueKind==JsonValueKind.String&&reference.GetString()=="ObjectId"&&
            value.TryGetProperty("handle",out var handle)&&handle.ValueKind is JsonValueKind.String or JsonValueKind.Null;
        return false;
    }
    private static bool XDataPreserved(JsonElement before,JsonElement current,Dictionary<int,JsonElement> requested,Action cancel)
    {
        if(!Apps(before,cancel,out var oldApps)||!Apps(current,cancel,out var newApps))return false;
        var oldOther=oldApps.Where(app=>app.Name!="ACAD").ToArray();var newOther=newApps.Where(app=>app.Name!="ACAD").ToArray();
        if(oldOther.Length!=newOther.Length)return false;
        for(int index=0;index<oldOther.Length;index++)
        {
            cancel();if(!Exact(oldOther[index].Header,newOther[index].Header)||!SameItems(oldOther[index].Items,newOther[index].Items,cancel))return false;
        }
        var oldAcad=oldApps.SingleOrDefault(app=>app.Name=="ACAD");var newAcad=newApps.SingleOrDefault(app=>app.Name=="ACAD");
        if(oldAcad!=null&&newAcad!=null&&!Exact(oldAcad.Header,newAcad.Header))return false;
        if(newAcad!=null&&!Exact(newAcad.Header,Wire.Element(new{code=1001,value="ACAD"})))return false;
        if(!DStyle(oldAcad,cancel,out var oldSection)||!DStyle(newAcad,cancel,out var newSection)||
            !SameItems(oldSection.Outside,newSection.Outside,cancel))return false;
        foreach(var pair in oldSection.Values)
        {
            cancel();if(requested.TryGetValue(pair.Key,out var desired))
            {if(!Exact(pair.Value.GetProperty("code"),desired.GetProperty("code")))return false;continue;}
            if(!newSection.Values.TryGetValue(pair.Key,out var actual)||!Exact(pair.Value,actual))return false;
        }
        foreach(var pair in newSection.Values)
        {
            cancel();
            if(requested.TryGetValue(pair.Key,out var expected)){if(!Exact(pair.Value,expected))return false;}
            else if(!oldSection.Values.TryGetValue(pair.Key,out var original)||!Exact(pair.Value,original))return false;
        }
        // A native setter may omit/remove an explicit requested entry when the
        // verified effective value comes from its retained base style. This
        // does not certify an equal-valued literal override/inheritance mask.
        return true;
    }
}
