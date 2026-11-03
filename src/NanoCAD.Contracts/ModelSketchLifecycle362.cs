using System.Text.Json;
using System.Text.Json.Serialization;

namespace NanoCAD.Contracts;

public sealed record ModelSketchCreate362(string[] Targets,string ExpectedLastItemId,string[] SourceHandles,
    [property:JsonIgnore(Condition=JsonIgnoreCondition.WhenWritingNull)] SketchPlaneSpec? Plane,string Name,NativeSketchInitialization385? NativeGeometry=null,
    [property:JsonIgnore(Condition=JsonIgnoreCondition.WhenWritingNull)] ModelSketchSupportSpec396? Support=null,
    [property:JsonIgnore(Condition=JsonIgnoreCondition.WhenWritingNull)] string[]? ReferenceIds=null)
    :EditSpec("create_model_sketch",Targets);
public sealed record ModelSketchProfileCreate362(string[] Targets,string ExpectedLastItemId,string SketchId,string ExpectedSketchToken,Point3 RegionPoint,string Name)
    :EditSpec("create_model_profile_on_sketch",Targets);
public sealed record ModelProfileSweep362(string[] Targets,string ExpectedLastItemId,string ProfileId,string ExpectedSketchId,string ExpectedProfileToken,
    string[] PathHandles,string Orientation,double TaperAngle,string PartOperation,string Name):EditSpec("add_model_sweep_from_profile",Targets);
public sealed record ModelProfileRevolution411(string[] Targets,string ExpectedLastItemId,string ProfileId,string ExpectedSketchId,string ExpectedProfileToken,
    string PartOperation,string AxisHandle,double AngleDegrees,string Name):EditSpec("add_model_revolution_from_profile",Targets);
public sealed record ModelSketchRead362(string sketch_id,string mc_document_id,string? host_solid_id,string? support_id,string sketch_token,
    ModelProfilePlane340 plane,JsonElement geometry,string[] profile_ids,string[] projection_ids,string[] source_object_ids,JsonElement constraint_observation,string scope,ModelSketchGeometryBindings365? geometry_bindings=null,NativeSketchBindings385? native_inputs=null,
    [property:JsonIgnore(Condition=JsonIgnoreCondition.WhenWritingNull)] ModelSketchNativeSupport396? native_support=null);
public sealed record ModelSketchIdentity362(string sketch_id,string? host_solid_id,string name);
public sealed record ModelSketchInventory362(string mc_document_id,string scope,ModelSketchIdentity362[] items,bool complete,string? next_cursor);
public sealed record ModelSketchDiscovery362(ModelSketchInventory362? Inventory,ModelSketchRead362[] Sketches);
public sealed record ModelSketchChange362(int operation_index,string operation,EntityRef source,EntityRef body_ref,string body_mc_id,string mc_document_id,
    string sketch_id,string sketch_token,string last_item_id,EntityRef[] source_refs,bool source_representation_replaced,
    [property:JsonIgnore(Condition=JsonIgnoreCondition.WhenWritingNull)] string? profile_id=null,
    [property:JsonIgnore(Condition=JsonIgnoreCondition.WhenWritingNull)] string? profile_token=null,
    [property:JsonIgnore(Condition=JsonIgnoreCondition.WhenWritingNull)] string? feature_id=null,
    [property:JsonIgnore(Condition=JsonIgnoreCondition.WhenWritingNull)] ModelProfileOwnership348? profile_ownership=null,
    [property:JsonIgnore(Condition=JsonIgnoreCondition.WhenWritingNull)] NativeSketchCreated385[]? native_objects=null,
    [property:JsonIgnore(Condition=JsonIgnoreCondition.WhenWritingNull)] SketchConstraintChange[]? constraint_changes=null,
    [property:JsonIgnore(Condition=JsonIgnoreCondition.WhenWritingNull)] ModelSketchNativeSupport396? native_support=null,
    [property:JsonIgnore(Condition=JsonIgnoreCondition.WhenWritingNull)] string? angle_variable_id=null,
    [property:JsonIgnore(Condition=JsonIgnoreCondition.WhenWritingNull)] double? angle_degrees=null,
    [property:JsonIgnore(Condition=JsonIgnoreCondition.WhenWritingNull)] EntityRef? axis_ref=null,
    [property:JsonIgnore(Condition=JsonIgnoreCondition.WhenWritingNull)] ModelProfileConsumed446[]? consumed_profiles=null);
public sealed record ModelSketchOutput362(string operation_id,string status,bool replayed,EntityRef[] modified,EntityRef[] erased,EntityRef[] created,
    CopyMapping[] copies,string undo_history,ModelSketchChange362[] model_sketch_lifecycle);

public static class ModelSketchLifecycleValidation362
{
    static DomainException Invalid(string path,string message)=>new("INVALID_ARGUMENT",message,false,new{path});
    static string Text(JsonElement item,string name,string path,int maximum=64)
    {
        if(!item.TryGetProperty(name,out var value)||value.ValueKind!=JsonValueKind.String)throw Invalid(path+"."+name,"Expected a string.");
        string text=value.GetString()!;
        if(string.IsNullOrWhiteSpace(text)||text.Length>maximum||text.Any(char.IsControl))throw Invalid(path+"."+name,"Expected a bounded nonempty string without control characters.");
        return text;
    }
    static string Id(JsonElement item,string name,string path)
    {string text=Text(item,name,path,38);if(!Guid.TryParseExact(text,"B",out var id)||id==Guid.Empty)throw Invalid(path+"."+name,"Read the current nonempty native braced GUID.");return id.ToString("B").ToUpperInvariant();}
    static string Token(JsonElement item,string name,string path)
    {string value=Text(item,name,path);if(value.Length!=64||!value.All(Uri.IsHexDigit))throw Invalid(path+"."+name,"Read the current exact native construction token.");return value.ToUpperInvariant();}
    public static string[] Ids(JsonElement value,string path)
    {
        if(value.ValueKind!=JsonValueKind.Array||value.GetArrayLength()==0)throw Invalid(path,"Select exact native sketch IDs.");var values=new HashSet<string>(StringComparer.Ordinal);
        foreach(var item in value.EnumerateArray())
        {if(item.ValueKind!=JsonValueKind.String||!Guid.TryParseExact(item.GetString(),"B",out var id)||id==Guid.Empty||!values.Add(id.ToString("B").ToUpperInvariant()))throw Invalid(path,"IDs must be distinct nonempty native braced GUIDs.");}
        return values.ToArray();
    }
    static string[] Handles(JsonElement item,string name,string path,string body)
    {
        if(!item.TryGetProperty(name,out var value)||value.ValueKind!=JsonValueKind.Array||value.GetArrayLength()==0)throw Invalid(path+"."+name,"Select committed native source curves.");
        var result=new List<string>();foreach(var entry in value.EnumerateArray())
        {if(entry.ValueKind!=JsonValueKind.String)throw Invalid(path+"."+name,"Expected a drawing handle.");string handle=Validation.NormalizeHandle(entry.GetString()!);if(handle==body||result.Contains(handle))throw Invalid(path+"."+name,"Select distinct source handles separate from the body.");result.Add(handle);}return result.ToArray();
    }
    public static EditSpec Parse(JsonElement item,string path,string[] handles)
    {
        if(item.ValueKind!=JsonValueKind.Object||handles.Length!=1)throw Invalid(path,"Select one existing native Model Space part.");string operation=Text(item,"operation",path);
        string[] specific=operation switch{
            "create_model_sketch"=>["source_handles","reference_ids","plane","support","native_geometry","initial_constraints"],
            "create_model_profile_on_sketch"=>["sketch_id","expected_sketch_token","region_point"],
            "add_model_sweep_from_profile"=>["profile_id","expected_sketch_id","expected_profile_token","path_handles","orientation","taper_angle","part_operation"],
            "add_model_revolution_from_profile"=>["profile_id","expected_sketch_id","expected_profile_token","axis_handle","angle_degrees","part_operation"],
            _=>throw Invalid(path+".operation","Unknown native sketch lifecycle operation.")};
        var allowed=specific.Concat(["operation","handles","expected_last_item_id","name"]).ToHashSet(StringComparer.Ordinal);var seen=new HashSet<string>(StringComparer.Ordinal);
        foreach(var field in item.EnumerateObject())if(!allowed.Contains(field.Name)||!seen.Add(field.Name))throw Invalid(path+"."+field.Name,"Unknown or duplicate field.");
        string last=Id(item,"expected_last_item_id",path),name=item.TryGetProperty("name",out _)?Text(item,"name",path):operation switch{"create_model_sketch"=>"Retained sketch","create_model_profile_on_sketch"=>"Selected profile","add_model_revolution_from_profile"=>"Retained profile revolution",_=>"Retained profile sweep"};
        if(operation=="create_model_sketch")
        {
            bool raw=item.TryGetProperty("plane",out var plane),supported=item.TryGetProperty("support",out var support);
            if(raw==supported)throw Invalid(path,"Choose exactly one explicit plane or actual native support.");
            SketchPlaneSpec? frame=raw?DrawingWorkplane.Parse(plane):null;var selected=supported?ModelSketchSupportValidation396.Parse(support,path+".support"):null;
            string[]? references=item.TryGetProperty("reference_ids",out var referenceArray)?Ids(referenceArray,path+".reference_ids"):null;
            var sources=item.TryGetProperty("source_handles",out _)?Handles(item,"source_handles",path,handles[0]):[];
            if(item.TryGetProperty("native_geometry",out var native)){var constraints=item.TryGetProperty("initial_constraints",out var c)?c:Wire.Element(Array.Empty<object>());return new ModelSketchCreate362(handles,last,sources,frame,name,NativeSketchValidation385.Parse(native,constraints,frame,path+".native_geometry",referenceIds:references),selected,references);}
            if(item.TryGetProperty("initial_constraints",out _))throw Invalid(path,"initial_constraints applies to new native_geometry.");
            if(sources.Length==0&&references==null)throw Invalid(path,"Supply retained drawing sources, current native part reference IDs or owned native_geometry.");
            return new ModelSketchCreate362(handles,last,sources,frame,name,Support:selected,ReferenceIds:references);
        }
        if(operation=="create_model_profile_on_sketch")
        {
            if(!item.TryGetProperty("region_point",out var point)||point.ValueKind!=JsonValueKind.Array||point.GetArrayLength()!=3)throw Invalid(path+".region_point","Choose one bounded region interior in WCS.");
            var values=point.EnumerateArray().Select(value=>value.ValueKind==JsonValueKind.Number&&value.TryGetDouble(out double number)&&double.IsFinite(number)&&Math.Abs(number)<=1e6?number:throw Invalid(path+".region_point","Use finite WCS coordinates within1e6 millimeters.")).ToArray();
            return new ModelSketchProfileCreate362(handles,last,Id(item,"sketch_id",path),Token(item,"expected_sketch_token",path),new(values[0],values[1],values[2]),name);
        }
        string part=Text(item,"part_operation",path);
        if(part is not ("join" or "cut"))throw Invalid(path+".part_operation","Use join or cut.");
        if(operation=="add_model_revolution_from_profile")
        {
            string axis=Validation.NormalizeHandle(Text(item,"axis_handle",path,16));
            if(axis==handles[0])throw Invalid(path+".axis_handle","Choose a committed coplanar native Line separate from the body.");
            if(!item.TryGetProperty("angle_degrees",out var a)||a.ValueKind!=JsonValueKind.Number||!a.TryGetDouble(out double degrees)||!double.IsFinite(degrees)||degrees<=0||degrees>360)
                throw Invalid(path+".angle_degrees","Use a finite positive revolution angle up to360 degrees, about the axis Line's stored direction.");
            return new ModelProfileRevolution411(handles,last,Id(item,"profile_id",path),Id(item,"expected_sketch_id",path),Token(item,"expected_profile_token",path),part,axis,degrees,name);
        }
        string orientation=Text(item,"orientation",path);
        if(orientation is not ("normal_to_path" or "parallel_to_profile"))throw Invalid(path+".orientation","Use normal_to_path or parallel_to_profile.");
        if(!item.TryGetProperty("taper_angle",out var angle)||angle.ValueKind!=JsonValueKind.Number||!angle.TryGetDouble(out double taper)||!double.IsFinite(taper)||Math.Abs(taper)>=Math.PI/2-1e-6)throw Invalid(path+".taper_angle","Use radians strictly within the supported sweep taper range.");
        return new ModelProfileSweep362(handles,last,Id(item,"profile_id",path),Id(item,"expected_sketch_id",path),Token(item,"expected_profile_token",path),Handles(item,"path_handles",path,handles[0]),orientation,taper,part,name);
    }
    public static void Batch(IReadOnlyList<EditSpec> edits)
    {if(edits.Any(edit=>edit is ModelSketchCreate362 or ModelSketchProfileCreate362 or ModelProfileSweep362 or ModelProfileRevolution411 or ModelProfileLoft446)&&edits.Count!=1)throw new DomainException("INVALID_OPERATION_ORDER","A native sketch lifecycle operation must be the sole operation in its request.");}
}
