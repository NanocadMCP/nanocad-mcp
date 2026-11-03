using System.Text.Json;

namespace NanoCAD.Contracts;

public sealed record NativeWindowStretchEdit387(string[] Targets,string[] ExpectedStretchTokens,
    Point2 MinimumWcs,Point2 MaximumWcs,Point3 DisplacementWcs):EditSpec("stretch_curve",Targets);

public static class NativeWindowStretchValidation387
{
    private static DomainException Bad(string path,string message)=>new("INVALID_ARGUMENT",path+": "+message,false,new{path});
    private static JsonElement Required(JsonElement value,string name,string path)=>value.TryGetProperty(name,out var result)?result:throw Bad(path+"."+name,"Required field is missing.");
    private static string Text(JsonElement value,string path)
    {if(value.ValueKind!=JsonValueKind.String)throw Bad(path,"Expected string.");try{return value.GetString()!;}catch(InvalidOperationException){throw Bad(path,"Invalid Unicode scalar text.");}}
    private static void Fields(JsonElement value,string path,params string[] allowed)
    {
        if(value.ValueKind!=JsonValueKind.Object)throw Bad(path,"Expected object.");var seen=new HashSet<string>(StringComparer.Ordinal);
        foreach(var property in value.EnumerateObject())if(!seen.Add(property.Name)||!allowed.Contains(property.Name,StringComparer.Ordinal))throw Bad(path+"."+property.Name,"Unknown or duplicate field.");
    }
    private static double[] Coordinates(JsonElement value,string path,int count)
    {
        if(value.ValueKind!=JsonValueKind.Array||value.GetArrayLength()!=count)throw Bad(path,"Expected "+count+" WCS drawing-length components.");
        return value.EnumerateArray().Select((item,index)=>
        {if(item.ValueKind!=JsonValueKind.Number||!item.TryGetDouble(out double number)||!double.IsFinite(number)||Math.Abs(number)>1e6)throw Bad(path+"["+index+"]","Expected a finite component within +/-1000000 millimeters.");return number==0?0:number;}).ToArray();
    }
    public static NativeWindowStretchEdit387 Parse(JsonElement value,string path,string[] handles)
    {
        Fields(value,path,"operation","handles","mode","expected_stretch_tokens","selection_window","displacement_wcs");
        if(handles.Length==0||handles.Distinct(StringComparer.Ordinal).Count()!=handles.Length)throw Bad(path+".handles","Select distinct existing independent native Line/Polyline candidates.");
        if(Text(Required(value,"operation",path),path+".operation")!="stretch_curve"||Text(Required(value,"mode",path),path+".mode")!="native")throw Bad(path+".mode","Use stretch_curve with explicit mode=native.");
        var tokens=Required(value,"expected_stretch_tokens",path);
        if(tokens.ValueKind!=JsonValueKind.Array||tokens.GetArrayLength()!=handles.Length)throw Bad(path+".expected_stretch_tokens","Supply one current native_stretch.stretch_token for each handle in the same order.");
        var expected=tokens.EnumerateArray().Select((item,index)=>
        {string token=Text(item,path+".expected_stretch_tokens["+index+"]");if(token.Length!=64||token.Any(c=>!Uri.IsHexDigit(c)))throw Bad(path+".expected_stretch_tokens["+index+"]","Read the complete current native_stretch token of this handle.");return token.ToUpperInvariant();}).ToArray();
        var window=Required(value,"selection_window",path);Fields(window,path+".selection_window","min","max");
        var min=Coordinates(Required(window,"min",path+".selection_window"),path+".selection_window.min",2);var max=Coordinates(Required(window,"max",path+".selection_window"),path+".selection_window.max",2);
        if(min[0]>=max[0]||min[1]>=max[1])throw Bad(path+".selection_window","Require min.X < max.X and min.Y < max.Y.");
        var displacement=Coordinates(Required(value,"displacement_wcs",path),path+".displacement_wcs",3);
        if(displacement[2]!=0)throw Bad(path+".displacement_wcs[2]","Native window stretch retains WCS XY/Z=0; use zero Z displacement.");
        return new(handles,expected,new(min[0],min[1]),new(max[0],max[1]),new(displacement[0],displacement[1],displacement[2]));
    }
    public static int[] Selected(NativeWindowStretchEdit387 edit,IReadOnlyList<NativeStretchPoint345> points,Action? cancel=null)
    {
        NativeStretchRead345.Validate(points,cancel);var result=new List<int>();
        foreach(var point in points)
        {
            cancel?.Invoke();var p=point.point_wcs;
            // Inclusive WCS rectangle, using only the actual ordered native
            // point inventory. No entity bbox or guessed grip/vertex ordinal.
            if(p[0]>=edit.MinimumWcs.X&&p[0]<=edit.MaximumWcs.X&&p[1]>=edit.MinimumWcs.Y&&p[1]<=edit.MaximumWcs.Y)result.Add(point.index);
        }
        return result.ToArray();
    }
}
