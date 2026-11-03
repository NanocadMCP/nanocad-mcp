using System.Security.Cryptography;
using System.Text.Json;

namespace NanoCAD.Contracts;

public sealed record NativeStretchEdit345(string[] Targets,string ExpectedStretchToken,int[] Indices,Point3 DisplacementWcs)
    :EditSpec("stretch_curve",Targets);
public sealed record NativeStretchPoint345(int index,double[] point_wcs);
public sealed record NativeStretchObservation345(string stretch_token,string curve_token,string runtime_type,int point_count,
    bool complete,NativeStretchPoint345[] points,string scope);

public static class NativeStretchValidation345
{
    private static DomainException Bad(string path,string message)=>new("INVALID_ARGUMENT",path+": "+message,false,new{path});
    private static JsonElement Required(JsonElement value,string name,string path)=>value.TryGetProperty(name,out var result)?result:throw Bad(path+"."+name,"Required field is missing.");
    private static string Text(JsonElement value,string path)
    {if(value.ValueKind!=JsonValueKind.String)throw Bad(path,"Expected string.");try{return value.GetString()!;}catch(InvalidOperationException){throw Bad(path,"Invalid Unicode scalar text.");}}
    public static NativeStretchEdit345 Parse(JsonElement value,string path,string[] handles)
    {
        if(value.ValueKind!=JsonValueKind.Object)throw Bad(path,"Expected object.");
        var allowed=new HashSet<string>(["operation","handles","mode","expected_stretch_token","indices","displacement_wcs"],StringComparer.Ordinal);
        var fields=new HashSet<string>(StringComparer.Ordinal);
        foreach(var property in value.EnumerateObject())if(!fields.Add(property.Name)||!allowed.Contains(property.Name))throw Bad(path+"."+property.Name,"Unknown or duplicate field.");
        if(handles.Length!=1)throw Bad(path+".handles","Select one existing exact native Line or Polyline.");
        if(Text(Required(value,"operation",path),path+".operation")!="stretch_curve"||Text(Required(value,"mode",path),path+".mode")!="native")throw Bad(path+".mode","Use stretch_curve with explicit mode=native.");
        string token=Text(Required(value,"expected_stretch_token",path),path+".expected_stretch_token");
        if(token.Length!=64||token.Any(c=>!Uri.IsHexDigit(c)))throw Bad(path+".expected_stretch_token","Read the current geometry.native_stretch.stretch_token.");
        var indices=Required(value,"indices",path);if(indices.ValueKind!=JsonValueKind.Array||indices.GetArrayLength()==0)throw Bad(path+".indices","Select nonempty distinct native stretch-point indices from the current read.");
        var selected=new List<int>();var seen=new HashSet<int>();
        foreach(var item in indices.EnumerateArray())
        {if(item.ValueKind!=JsonValueKind.Number||!item.TryGetInt32(out int index)||index<0||!seen.Add(index))throw Bad(path+".indices","Indices must be distinct nonnegative native Int32 values.");selected.Add(index);}
        var displacement=Required(value,"displacement_wcs",path);
        if(displacement.ValueKind!=JsonValueKind.Array||displacement.GetArrayLength()!=3)throw Bad(path+".displacement_wcs","Expected a WCS XYZ drawing-length vector.");
        var coordinates=displacement.EnumerateArray().Select((item,index)=>
        {if(item.ValueKind!=JsonValueKind.Number||!item.TryGetDouble(out double number)||!double.IsFinite(number)||Math.Abs(number)>1e6)throw Bad(path+".displacement_wcs["+index+"]","Expected a finite drawing-length component within +/-1000000 millimeters.");return number==0?0:number;}).ToArray();
        if(coordinates[2]!=0)throw Bad(path+".displacement_wcs[2]","Initial native stretch keeps the WCS XY source plane; use zero Z displacement.");
        return new(handles,token.ToUpperInvariant(),selected.ToArray(),new(coordinates[0],coordinates[1],coordinates[2]));
    }
    public static void Batch(IReadOnlyList<EditSpec> operations)
    {if(operations.Any(e=>e is NativeStretchEdit345 or NativeWindowStretchEdit387)&&operations.Count!=1)throw new DomainException("INVALID_OPERATION_ORDER","Native stretch must be the sole operation; inspect its retained current object before subsequent editing.");}
}

public static class NativeStretchRead345
{
    public static string Token(JsonElement shape,IReadOnlyList<NativeStretchPoint345> points,Action? cancel=null)
    {
        Validate(points,cancel);cancel?.Invoke();var bytes=JsonSerializer.SerializeToUtf8Bytes(new{shape,points},Wire.Json);cancel?.Invoke();
        if(bytes.Length>NativeCurveRead342.SnapshotByteBudget)throw new DomainException("RESPONSE_LIMIT_EXCEEDED","Complete native stretch shape/points exceed the actual snapshot-byte budget; nothing was truncated.");
        var result=Convert.ToHexString(SHA256.HashData(bytes));cancel?.Invoke();return result;
    }
    public static void Validate(IReadOnlyList<NativeStretchPoint345> points,Action? cancel=null)
    {
        long bytes=2;
        for(int i=0;i<points.Count;i++)
        {
            cancel?.Invoke();var point=points[i];
            if(point.index!=i||point.point_wcs.Length!=3||point.point_wcs.Any(n=>!double.IsFinite(n)))throw new DomainException("NATIVE_STRETCH_READ_FAILED","Native stretch inventory must retain complete ordered finite XYZ points and actual indices.");
            bytes+=JsonSerializer.SerializeToUtf8Bytes(point,Wire.Json).Length+1;
            if(bytes>NativeCurveRead342.SnapshotByteBudget)throw new DomainException("RESPONSE_LIMIT_EXCEEDED","Complete native stretch points exceed the actual snapshot-byte budget.");
        }
    }
    public static NativeStretchPoint345[] Expected(IReadOnlyList<NativeStretchPoint345> points,NativeStretchEdit345 edit,Action? cancel=null)
    {
        Validate(points,cancel);var selected=new HashSet<int>();
        foreach(int index in edit.Indices)
        {cancel?.Invoke();if(index<0||index>=points.Count||!selected.Add(index))throw new DomainException("STRETCH_POINT_NOT_FOUND","Select distinct indices present in the current complete native stretch inventory.");}
        var result=new NativeStretchPoint345[points.Count];
        for(int i=0;i<points.Count;i++)
        {
            cancel?.Invoke();var point=points[i].point_wcs;bool move=selected.Contains(i);
            var p=new[]{point[0]+(move?edit.DisplacementWcs.X:0),point[1]+(move?edit.DisplacementWcs.Y:0),point[2]+(move?edit.DisplacementWcs.Z:0)};
            if(p.Any(n=>!double.IsFinite(n)||Math.Abs(n)>1e9))throw new DomainException("INVALID_GEOMETRY","Requested native stretch exceeds finite source-coordinate bounds.");
            result[i]=new(i,p);
        }
        Validate(result,cancel);return result;
    }
}
