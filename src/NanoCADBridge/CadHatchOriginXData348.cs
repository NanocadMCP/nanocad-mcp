using System.Globalization;
using System.Text.Json;
using NanoCAD.Contracts;

namespace NanoCADBridge;

// Compare completed managed snapshots only. The observed native Hatch Origin
// field is one complete ACAD app group; this is not a general XData exemption.
internal static class CadHatchOriginXData348
{
    internal static bool MetadataPreserved(JsonElement before,JsonElement current,double[] beforeOrigin,
        double[] currentOrigin,bool nativeOriginSetterExecuted,Action cancel)
    {
        cancel();
        if(before.GetRawText()==current.GetRawText())return true;
        if(!nativeOriginSetterExecuted||before.ValueKind!=JsonValueKind.Object||current.ValueKind!=JsonValueKind.Object)
            return false;
        foreach(var property in before.EnumerateObject())
        {
            cancel();
            if(!current.TryGetProperty(property.Name,out var actual))return false;
            if(property.Name!="xdata"&&property.Value.GetRawText()!=actual.GetRawText())return false;
        }
        foreach(var property in current.EnumerateObject())
        {cancel();if(!before.TryGetProperty(property.Name,out _))return false;}
        return before.TryGetProperty("xdata",out var expectedData)&&current.TryGetProperty("xdata",out var currentData)&&
            XDataPreserved(expectedData,currentData,beforeOrigin,currentOrigin,cancel);
    }
    private static JsonElement OriginRecord(double[] origin)=>Wire.Element(new{code=1010,value=new{
        native_type="Point3d",coordinates=new[]{Scalar(origin[0]),Scalar(origin[1]),Scalar(0d)}}});
    private static object Scalar(double value)=>new{native_type="double",
        bits=BitConverter.DoubleToInt64Bits(value).ToString("X16",CultureInfo.InvariantCulture)};
    private static bool IsApp(JsonElement value)=>value.ValueKind==JsonValueKind.Object&&
        value.TryGetProperty("code",out var code)&&code.GetRawText()=="1001";
    private static bool IsAcad(JsonElement value)=>IsApp(value)&&value.TryGetProperty("value",out var app)&&
        app.ValueKind==JsonValueKind.String&&app.GetString()=="ACAD";
    private static int Length(JsonElement value)=>value.ValueKind==JsonValueKind.Array?value.GetArrayLength():0;
    private static bool FindOrigin(JsonElement data,JsonElement originRecord,Action cancel,out int start)
    {
        start=-1;
        if(data.ValueKind==JsonValueKind.Null)return true;
        if(data.ValueKind!=JsonValueKind.Array)return false;
        var appRecord=Wire.Element(new{code=1001,value="ACAD"});
        for(int index=0;index<data.GetArrayLength();index++)
        {
            cancel();
            if(!IsAcad(data[index]))continue;
            // Do not reinterpret extended/multiple/unknown ACAD groups.
            if(start>=0||index+1>=data.GetArrayLength()||data[index].GetRawText()!=appRecord.GetRawText()||
                data[index+1].GetRawText()!=originRecord.GetRawText())return false;
            if(index+2<data.GetArrayLength()&&!IsApp(data[index+2]))return false;
            start=index;index++;
        }
        return true;
    }
    private static bool XDataPreserved(JsonElement before,JsonElement current,double[] beforeOrigin,double[] currentOrigin,Action cancel)
    {
        if(beforeOrigin.Length!=2||currentOrigin.Length!=2||!double.IsFinite(beforeOrigin[0])||!double.IsFinite(beforeOrigin[1])||
            !double.IsFinite(currentOrigin[0])||!double.IsFinite(currentOrigin[1]))return false;
        if(!FindOrigin(before,OriginRecord(beforeOrigin),cancel,out int oldStart)||
            !FindOrigin(current,OriginRecord(currentOrigin),cancel,out int newStart)||newStart<0)return false;
        int oldIndex=0,newIndex=0,oldLength=Length(before),newLength=Length(current);
        while(true)
        {
            cancel();
            if(oldIndex==oldStart)oldIndex+=2;
            if(newIndex==newStart)newIndex+=2;
            if(oldIndex==oldLength||newIndex==newLength)return oldIndex==oldLength&&newIndex==newLength;
            if(before[oldIndex].GetRawText()!=current[newIndex].GetRawText())return false;
            oldIndex++;newIndex++;
        }
    }
}
