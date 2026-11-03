using System.Text.Json;
using static NanoCAD.Contracts.StructureWriteValidation;

namespace NanoCAD.Contracts;

public sealed record DrawingViewLifecycleTarget345(DrawingViewTarget Target,string ViewToken);
public sealed record SetDrawingViewSources345(DrawingViewLifecycleTarget345[] Targets,string[] SourceHandles):StructureEdit("set_drawing_view_sources");
public sealed record RenameDrawingView345(DrawingViewLifecycleTarget345 Target,string Name):StructureEdit("rename_drawing_view");
public sealed record AddDrawingViewChild345(DrawingViewLifecycleTarget345 Parent,string Name,string Direction,Point2 Origin,double ModelScale):StructureEdit("add_drawing_view_child");
public sealed record DrawingViewLifecycleChange345(int operation_index,EntityRef view_ref,string view_token,
    EntityRef[] source_refs,string[] top_source_ids,string? parent_view_id,string? root_view_id,string[] child_view_ids);

public static class DrawingViewLifecycleValidation345
{
    public static bool Handles(string operation)=>operation is "set_drawing_view_sources" or "rename_drawing_view" or "add_drawing_view_child";
    private static DrawingViewLifecycleTarget345 Target(JsonElement item,string path)
    {
        string token=Text(item,"view_token",path,64);
        if(token.Length!=64||token.Any(c=>!Uri.IsHexDigit(c)))throw Invalid(path+".view_token","Use the current get_drawing_views view_token.");
        return new(new(Validation.NormalizeHandle(Text(item,"layout_handle",path,16)),Validation.NormalizeHandle(Text(item,"handle",path,16)),
            Text(item,"expected_name",path,4096)),token.ToUpperInvariant());
    }
    private static double Number(JsonElement value,string path,bool positive=false)
    {
        if(value.ValueKind!=JsonValueKind.Number||!value.TryGetDouble(out double number)||!double.IsFinite(number)||positive&&number<=0)
            throw Invalid(path,positive?"Expected a finite positive native scale denominator.":"Expected a finite WCS coordinate in millimeters.");
        return number==0?0:number;
    }
    public static StructureEdit Parse(JsonElement item,string path)
    {
        string operation=Text(item,"operation",path,32);
        if(operation=="set_drawing_view_sources")
        {
            Shape(item,path,"operation","targets","source_handles");var input=Required(item,"targets",path);
            if(input.ValueKind!=JsonValueKind.Array||input.GetArrayLength()==0)throw Invalid(path+".targets","Select an explicit nonempty group of current native views.");
            var targets=new List<DrawingViewLifecycleTarget345>();
            foreach(var target in input.EnumerateArray())
            {
                string targetPath=path+".targets["+targets.Count+"]";Shape(target,targetPath,"layout_handle","handle","expected_name","view_token");
                targets.Add(Target(target,targetPath));
            }
            if(targets.Select(target=>target.Target.Handle).Distinct(StringComparer.Ordinal).Count()!=targets.Count)
                throw Invalid(path+".targets","Each view is selected once.");
            var sources=Required(item,"source_handles",path);
            if(sources.ValueKind!=JsonValueKind.Array||sources.GetArrayLength()==0)throw Invalid(path+".source_handles","Supply nonempty current top-level Model solid handles.");
            var handles=new List<string>();
            foreach(var source in sources.EnumerateArray())
            {
                if(source.ValueKind!=JsonValueKind.String)throw Invalid(path+".source_handles","Expected native handles.");
                string handle;try{handle=source.GetString()!;}catch(InvalidOperationException){throw Invalid(path+".source_handles","Invalid Unicode native handle.");}
                handles.Add(Validation.NormalizeHandle(handle));
            }
            if(handles.Distinct(StringComparer.Ordinal).Count()!=handles.Count)throw Invalid(path+".source_handles","Each source is selected once.");
            return new SetDrawingViewSources345(targets.OrderBy(target=>target.Target.Handle,StringComparer.Ordinal).ToArray(),handles.OrderBy(handle=>handle,StringComparer.Ordinal).ToArray());
        }
        if(operation=="rename_drawing_view")
        {
            Shape(item,path,"operation","layout_handle","handle","expected_name","view_token","name");
            return new RenameDrawingView345(Target(item,path),Text(item,"name",path,4096));
        }
        if(operation=="add_drawing_view_child")
        {
            Shape(item,path,"operation","layout_handle","handle","expected_name","view_token","name","direction","origin","model_scale");
            string direction=Text(item,"direction",path,24);
            if(direction is not ("top" or "bottom" or "left" or "right" or "iso_top_left" or "iso_top_right" or "iso_bottom_left" or "iso_bottom_right"))
                throw Invalid(path+".direction","Choose a supported native child direction.");
            var point=Required(item,"origin",path);
            if(point.ValueKind!=JsonValueKind.Array||point.GetArrayLength()!=2)throw Invalid(path+".origin","Expected WCS [x,y] in the parent's owning space.");
            return new AddDrawingViewChild345(Target(item,path),Text(item,"name",path,4096),direction,
                new(Number(point[0],path+".origin[0]"),Number(point[1],path+".origin[1]")),Number(Required(item,"model_scale",path),path+".model_scale",true));
        }
        throw Invalid(path+".operation","Unknown native view lifecycle operation.");
    }
    public static void Batch(IReadOnlyList<StructureEdit> edits)
    {
        if(edits.Any(edit=>edit is SetDrawingViewSources345 or RenameDrawingView345 or AddDrawingViewChild345)&&edits.Count!=1)
            throw Invalid("$.operations","Each native view lifecycle request is one sole operation; source targets form its explicit group.","INVALID_OPERATION_ORDER");
    }
}
