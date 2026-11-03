using System.Text.Json;
using static NanoCAD.Contracts.StructureWriteValidation;

namespace NanoCAD.Contracts;

public abstract record ViewportLifecycleEdit(string LayoutHandle,string Handle,string Kind):StructureEdit(Kind);
public sealed record CopyViewportEdit(string LayoutHandle,string Handle,double[] Center,bool? IsOn=null)
    :ViewportLifecycleEdit(LayoutHandle,Handle,"copy_viewport");
public sealed record DeleteViewportEdit(string LayoutHandle,string Handle)
    :ViewportLifecycleEdit(LayoutHandle,Handle,"delete_viewport");
public sealed record ViewportLifecycleChange(int operation_index,string operation,EntityRef layout,EntityRef source,
    EntityRef? created,bool erased);

public static class ViewportLifecycleValidation
{
    public static bool Handles(string operation)=>operation is "copy_viewport" or "delete_viewport";
    public static ViewportLifecycleEdit Parse(JsonElement input,string path="$")
    {
        string operation=Text(input,"operation",path,32);
        if(!Handles(operation))throw Invalid(path,"Expected copy_viewport or delete_viewport.");
        Shape(input,path,operation=="copy_viewport"
            ?new[]{"operation","layout_handle","handle","center","is_on"}
            :new[]{"operation","layout_handle","handle"});
        string layout=Validation.NormalizeHandle(Text(input,"layout_handle",path,16));
        string handle=Validation.NormalizeHandle(Text(input,"handle",path,16));
        ViewportLifecycleEdit result;
        if(operation=="copy_viewport")
        {
            var point=Required(input,"center",path);
            if(point.ValueKind!=JsonValueKind.Array||point.GetArrayLength()!=2)
                throw Invalid(path+".center","Expected two paper-space coordinates in millimeters.");
            var center=point.EnumerateArray().Select(value=>value.ValueKind==JsonValueKind.Number&&
                value.TryGetDouble(out var number)&&double.IsFinite(number)&&Math.Abs(number)<=1e9
                ?number:throw Invalid(path+".center","Expected finite paper coordinates within ±1e9 mm.")).ToArray();
            bool? on=null;
            if(input.TryGetProperty("is_on",out var flag))
            {
                if(flag.ValueKind is not (JsonValueKind.True or JsonValueKind.False))throw Invalid(path+".is_on","Expected boolean.");
                on=flag.GetBoolean();
            }
            result=new CopyViewportEdit(layout,handle,center,on);
        }
        else result=new DeleteViewportEdit(layout,handle);
        Validate(result);return result;
    }
    public static void Validate(ViewportLifecycleEdit operation)
    {
        foreach(string handle in new[]{operation.LayoutHandle,operation.Handle})
            if(Validation.NormalizeHandle(handle)!=handle)throw Invalid("handle","Expected normalized native handle.");
        if(operation.LayoutHandle==operation.Handle)throw Invalid("handle","Layout and viewport identities must differ.");
        if(operation is CopyViewportEdit copy&&(copy.Center is null||copy.Center.Length!=2||
            copy.Center.Any(value=>!double.IsFinite(value)||Math.Abs(value)>1e9)))
            throw Invalid("center","Expected two finite paper coordinates within ±1e9 mm.");
        if(operation is not (CopyViewportEdit or DeleteViewportEdit))throw Invalid("operation","Unknown viewport lifecycle operation.");
    }
    public static void Batch(IEnumerable<StructureEdit> operations)
    {
        var batch=operations.ToArray();var edits=batch.OfType<ViewportLifecycleEdit>().ToArray();
        if(edits.Length==0)return;
        if(edits.Length!=batch.Length||edits.Select(edit=>edit.Operation).Distinct(StringComparer.Ordinal).Count()!=1)
            throw Invalid("operations","Copy or delete ordinary paper viewports in a separate operation group.","INVALID_OPERATION_ORDER");
        if(edits.Select(edit=>edit.LayoutHandle).Distinct(StringComparer.Ordinal).Count()!=1)
            throw Invalid("operations","Viewport lifecycle uses one explicit active Paper layout.","INVALID_OPERATION_ORDER");
        // Copy can reuse a retained source for several frames. Deletion cannot
        // address an already deleted source. No inventory/operation count cap.
        if(edits[0] is DeleteViewportEdit&&edits.Select(edit=>edit.Handle).Distinct(StringComparer.Ordinal).Count()!=edits.Length)
            throw Invalid("operations","Each viewport may be deleted only once.","INVALID_OPERATION_ORDER");
        foreach(var edit in edits)Validate(edit);
    }
    public static void RequireOrdinary(ViewportProperties value)
    {
        if(value.number==1||value.is_on&&value.number<=1||!value.is_on&&value.number<=1&&value.number!=-1||
            value.center[2]!=0||value.width<=0||value.height<=0||value.view_height<=0||value.custom_scale<=0||
            value.view_direction.All(number=>number==0)||value.is_perspective||value.non_rect_clip_on||
            value.clip_boundary.state!="null"||value.front_clip_on||value.back_clip_on||
            value.frozen_layers.Any(layer=>layer.state!="resolved"))
            throw new DomainException("UNSUPPORTED_VIEWPORT","Expected a valid ordinary rectangular paper viewport without perspective/clipping. The overview viewport1 is protected.");
    }
    public static void RequireCopy(ViewportProperties expected,ViewportProperties actual,bool resident)
    {
        if(resident)RequireOrdinary(actual);
        // Number is native resident display bookkeeping, not copied identity.
        // Detached and newly cloned resident On is finalized after disposal.
        ViewportWriteValidation.RequireSame(expected with{number=actual.number,is_on=resident?expected.is_on:actual.is_on},actual);
    }
}
