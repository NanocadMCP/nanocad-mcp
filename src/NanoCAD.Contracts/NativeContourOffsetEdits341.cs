using System.Text.Json;

namespace NanoCAD.Contracts;

// Native sign is intentionally explicit: it is not a guessed left/inward
// interpretation of every kernel curve family and orientation.
public sealed record NativeOffsetCurveSpec341(string[] Targets,double Distance,string Side,
    [property:System.Text.Json.Serialization.JsonIgnore(Condition=System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)] SketchPlaneSpec? Workplane=null):EditSpec("offset_curve",Targets)
{
    public double SignedDistance=>Side=="native_positive"?Distance:-Distance;
}
public static class NativeContourOffsetValidation341
{
    public static bool HandlesSide(string side)=>side is "native_positive" or "native_negative";
    private static DomainException Invalid(string path,string message)=>new("INVALID_ARGUMENT",path+": "+message,false,new{path});
    private static string Text(JsonElement value,string path)
    {if(value.ValueKind!=JsonValueKind.String)throw Invalid(path,"Expected string.");try{return value.GetString()!;}catch(InvalidOperationException){throw Invalid(path,"Invalid Unicode scalar text.");}}
    public static NativeOffsetCurveSpec341 Parse(JsonElement input,string path,string[] handles)
    {
        if(handles.Length!=1)throw Invalid(path+".handles","Select one existing planar native curve.");
        if(input.ValueKind!=JsonValueKind.Object)throw Invalid(path,"Expected object.");
        var names=new HashSet<string>(StringComparer.Ordinal);
        foreach(var property in input.EnumerateObject())if(!names.Add(property.Name)||property.Name is not ("operation" or "handles" or "distance" or "side" or "workplane"))throw Invalid(path+"."+property.Name,"Unknown or duplicate field.");
        if(!input.TryGetProperty("operation",out var operation)||Text(operation,path+".operation")!="offset_curve")throw Invalid(path+".operation","Expected offset_curve.");
        if(!input.TryGetProperty("side",out var direction)||direction.ValueKind!=JsonValueKind.String)throw Invalid(path+".side","Expected native_positive or native_negative.");
        string side=Text(direction,path+".side");
        if(!HandlesSide(side))throw Invalid(path+".side","Expected explicit native_positive or native_negative; no left/inward sign mapping is inferred.");
        if(!input.TryGetProperty("distance",out var distance)||distance.ValueKind!=JsonValueKind.Number||!distance.TryGetDouble(out double magnitude)||!double.IsFinite(magnitude)||magnitude<=0||magnitude>1e6)
            throw Invalid(path+".distance","Expected finite strictly positive native distance up to1000000 drawing millimeters.");
        var frame=input.TryGetProperty("workplane",out var workplane)?DrawingWorkplane.Parse(workplane):null;
        return new(handles,magnitude,side,frame);
    }
    public static void Batch(IReadOnlyList<EditSpec> operations)
    {
        if(operations.Any(edit=>edit is NativeOffsetCurveSpec341)&&operations.Count!=1)
            throw new DomainException("INVALID_OPERATION_ORDER","Native offset must be the sole operation: its read-only source and complete generated roots are verified together.");
    }
}
