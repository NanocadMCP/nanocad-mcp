using System.Text.Json;
using static NanoCAD.Contracts.StructureWriteValidation;

namespace NanoCAD.Contracts;

public sealed record DrawingViewTarget(string LayoutHandle,string Handle,string ExpectedName);
public sealed record SetDrawingViewPropertiesEdit(DrawingViewTarget Target,Point2? Origin,double? ModelScale,
    bool? ShowHiddenLines,bool? ShowHatches,bool? ShowTangentEdges):StructureEdit("set_drawing_view_properties");
public sealed record DeleteDrawingViewsEdit(DrawingViewTarget[] Targets):StructureEdit("delete_drawing_views");

public static class DrawingViewEditValidation
{
    public static bool Handles(string operation)=>operation is "set_drawing_view_properties" or "delete_drawing_views";
    private static DrawingViewTarget Target(JsonElement input,string path)=>new(
        Validation.NormalizeHandle(Text(input,"layout_handle",path,16)),
        Validation.NormalizeHandle(Text(input,"handle",path,16)),Text(input,"expected_name",path,4096));
    private static double Number(JsonElement value,string path,bool positive=false)
    {
        if(value.ValueKind!=JsonValueKind.Number||!value.TryGetDouble(out var number)||!double.IsFinite(number)||positive&&number<=0)
            throw Invalid(path,positive?"Expected a finite positive native model-scale denominator.":"Expected a finite coordinate in millimeters.");
        return number==0?0:number;
    }
    private static bool? Boolean(JsonElement input,string key,string path)
    {
        if(!input.TryGetProperty(key,out var value))return null;
        if(value.ValueKind is not (JsonValueKind.True or JsonValueKind.False))throw Invalid(path+"."+key,"Expected a boolean.");
        return value.GetBoolean();
    }
    public static StructureEdit Parse(JsonElement input,string path)
    {
        string operation=Text(input,"operation",path,32);
        if(operation=="delete_drawing_views")
        {
            Shape(input,path,"operation","targets");var array=Required(input,"targets",path);
            if(array.ValueKind!=JsonValueKind.Array||array.GetArrayLength()==0)throw Invalid(path+".targets","Select a nonempty explicit native view leaf/subtree.");
            var targets=new List<DrawingViewTarget>();
            foreach(var item in array.EnumerateArray())
            {
                string itemPath=path+".targets["+targets.Count+"]";Shape(item,itemPath,"layout_handle","handle","expected_name");
                targets.Add(Target(item,itemPath));
            }
            if(targets.Select(item=>item.Handle).Distinct(StringComparer.Ordinal).Count()!=targets.Count)
                throw Invalid(path+".targets","Each drawing view handle must be selected once.");
            return new DeleteDrawingViewsEdit(targets.OrderBy(item=>item.Handle,StringComparer.Ordinal).ToArray());
        }
        if(operation!="set_drawing_view_properties")throw Invalid(path+".operation","Unknown existing native drawing-view operation.");
        Shape(input,path,"operation","layout_handle","handle","expected_name","origin","model_scale","show_hidden_lines","show_hatches","show_tangent_edges");
        Point2? origin=null;
        if(input.TryGetProperty("origin",out var point))
        {
            if(point.ValueKind!=JsonValueKind.Array||point.GetArrayLength()!=2)throw Invalid(path+".origin","Expected WCS [x,y] in millimeters; the native view plane axes and Z stay unchanged.");
            origin=new(Number(point[0],path+".origin[0]"),Number(point[1],path+".origin[1]"));
        }
        double? scale=input.TryGetProperty("model_scale",out var scaleValue)?Number(scaleValue,path+".model_scale",true):null;
        var hidden=Boolean(input,"show_hidden_lines",path);var hatches=Boolean(input,"show_hatches",path);var tangent=Boolean(input,"show_tangent_edges",path);
        if(origin==null&&scale==null&&hidden==null&&hatches==null&&tangent==null)throw Invalid(path,"Provide at least one placement, scale or display property.");
        return new SetDrawingViewPropertiesEdit(Target(input,path),origin,scale,hidden,hatches,tangent);
    }
    public static void Batch(IReadOnlyList<StructureEdit> edits)
    {
        if(!edits.Any(edit=>edit is SetDrawingViewPropertiesEdit or DeleteDrawingViewsEdit))return;
        if(edits.Count==1&&edits[0] is DeleteDrawingViewsEdit)return;
        if(edits.Any(edit=>edit is not SetDrawingViewPropertiesEdit))
            throw Invalid("$.operations","Drawing-view property edits form their own native group; deletion must be the sole operation.","INVALID_OPERATION_ORDER");
        if(edits.Cast<SetDrawingViewPropertiesEdit>().Select(edit=>edit.Target.Handle).Distinct(StringComparer.Ordinal).Count()!=edits.Count)
            throw Invalid("$.operations","Combine requested properties of each native view in one operation.","INVALID_OPERATION_ORDER");
    }
}
