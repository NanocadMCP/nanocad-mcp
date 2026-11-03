using System.Text.Json;
using System.Text.Json.Serialization;
using static NanoCAD.Contracts.StructureWriteValidation;

namespace NanoCAD.Contracts;

public sealed record SetViewportClipping358(string LayoutHandle,string Handle,string ClippingToken,string Action,
    string? BoundaryHandle=null,string? BoundaryCurveToken=null,bool? Enabled=null):StructureEdit("set_viewport_clipping");
public sealed record ViewportClippingRead358(ViewportProperties viewport,bool candidate,string? clipping_token,
    EntityRef? boundary_ref,string? boundary_curve_token,EntityRef? reverse_viewport_ref,string scope,
    [property:JsonIgnore(Condition=JsonIgnoreCondition.WhenWritingNull)] string? unavailable_reason=null);
public sealed record ViewportClippingChange358(int operation_index,EntityRef viewport_ref,EntityRef layout_ref,
    string action,EntityRef? boundary_ref,bool enabled,string preservation_scope);

public static class ViewportClippingValidation358
{
    public const string Scope="Retained native Paper viewport clipping link; viewing basis, engineering scale and Model-to-Paper placement remain. Native clipping may reframe the viewport to the boundary extents; verified view-height/center compensation preserves the same projected detail on Paper. Layer, frozen layers and selected boundary geometry remain. Target viewport/boundary readable root/owned data and their unrelated links remain. Other sheet members retain observed membership/owner/type/basic appearance and viewport cameras; unrelated private data and opaque native internals are unobserved. This does not create/copy/erase a viewport or boundary, transfer a sheet, or attach annotation followers.";
    public static string Token(JsonElement input,string field,string path)
    {
        string value=Text(input,field,path,64);
        if(value.Length!=64||value.Any(ch=>!"0123456789ABCDEF".Contains(ch)))throw Invalid(path+"."+field,"Use the current uppercase SHA256 observation token.");
        return value;
    }
    public static SetViewportClipping358 Parse(JsonElement input,string path)
    {
        string action=Text(input,"action",path,32);
        if(action is not ("set_boundary" or "enable" or "disable" or "clear"))throw Invalid(path+".action","Use set_boundary, enable, disable or clear.");
        Shape(input,path,action=="set_boundary"?["operation","layout_handle","handle","clipping_token","action","boundary_handle","boundary_curve_token","enabled"]:
            ["operation","layout_handle","handle","clipping_token","action"]);
        string layout=Validation.NormalizeHandle(Text(input,"layout_handle",path,16)),handle=Validation.NormalizeHandle(Text(input,"handle",path,16));
        string token=Token(input,"clipping_token",path);
        string? boundary=null,curve=null;bool? enabled=null;
        if(action=="set_boundary")
        {
            boundary=Validation.NormalizeHandle(Text(input,"boundary_handle",path,16));curve=Token(input,"boundary_curve_token",path);
            var flag=Required(input,"enabled",path);if(flag.ValueKind is not (JsonValueKind.True or JsonValueKind.False))throw Invalid(path+".enabled","Expected boolean.");
            enabled=flag.GetBoolean();if(boundary==handle||boundary==layout)throw Invalid(path,"Viewport, layout and boundary identities must be distinct.");
        }
        if(layout==handle)throw Invalid(path,"Viewport and layout identities must be distinct.");
        return new(layout,handle,token,action,boundary,curve,enabled);
    }
    public static void Batch(IReadOnlyCollection<StructureEdit> edits)
    {
        // A link edit may change a reverse boundary edge. Keep that graph out of
        // a mixed request; callers may edit any number of distinct viewports.
        if(!edits.Any(e=>e is SetViewportClipping358))return;
        if(edits.Any(e=>e is not SetViewportClipping358))throw Invalid("operations","Keep viewport clipping edits in their own related group.","INVALID_OPERATION_ORDER");
        var views=new HashSet<string>(StringComparer.OrdinalIgnoreCase);var targets=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach(var edit in edits.Cast<SetViewportClipping358>())
        {
            if(!views.Add(edit.Handle))throw Invalid("operations","Read a fresh clipping token before another edit of this viewport.","INVALID_OPERATION_ORDER");
            if(edit.BoundaryHandle is{} boundary&&!targets.Add(boundary))throw Invalid("operations","A clipping boundary cannot be assigned to multiple viewports in this request.","INVALID_OPERATION_ORDER");
        }
    }
}
