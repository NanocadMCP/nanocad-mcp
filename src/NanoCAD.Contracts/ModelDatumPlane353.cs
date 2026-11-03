using System.Text.Json;

namespace NanoCAD.Contracts;

public sealed record ModelSketchSupportRecoveryContext440(string SketchId,string ExpectedSupportId);
public sealed record ModelDatumPlaneCreate353(string[] Targets,string ExpectedLastItemId,string Name,SketchPlaneSpec Plane,
    ModelSketchSupportRecoveryContext440? RecoveryContext=null)
    :EditSpec("create_model_datum_plane",Targets);
public sealed record ModelDatumPlaneRead353(string plane_id,string name,string mc_document_id,string datum_token,
    string definition_type,JsonElement plane_wcs,double? native_parameter,string? parameter_variable_id,JsonElement? driver,
    JsonElement definition_bindings,string? host_solid_id,bool in_selected_body_contents,string? native_order_before_id,
    string status,bool suppressed,bool beyond_end_of_part,string[] dependency_ids,string[] direct_consumer_ids,
    string[] downstream_consumer_ids,JsonElement? representation,string geometry_scope,ModelFaceDatumPlaneRelationRead356? source_relationship=null);
public sealed record ModelDatumPlaneIdentity353(string plane_id,string name,string definition_type,string? host_solid_id);
public sealed record ModelDatumPlaneInventory353(string mc_document_id,string scope,ModelDatumPlaneIdentity353[] items,
    bool complete,string? next_cursor);
public sealed record ModelDatumPlaneDiscovery353(ModelDatumPlaneInventory353? Inventory,ModelDatumPlaneRead353[] Planes);
public sealed record ModelDatumPlaneChange353(int operation_index,EntityRef body_ref,string body_mc_id,
    string last_item_id,ModelDatumPlaneRead353 datum,EntityRef[] native_representations,double? requested_native_angle_radians=null,string[]? created_variable_ids=null,
    [property:System.Text.Json.Serialization.JsonIgnore(Condition=System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)] JsonElement? recovery_preparation=null);
public sealed record ModelDatumPlaneOutput353(string operation_id,string status,bool replayed,EntityRef[] modified,
    EntityRef[] erased,EntityRef[] created,CopyMapping[] copies,string undo_history,ModelDatumPlaneChange353[] model_datum_planes);

public static class ModelDatumPlaneValidation353
{
    static DomainException Invalid(string path,string message)=>new("INVALID_ARGUMENT",message,false,new{path});
    static string Text(JsonElement value,string key,string path)
    {if(!value.TryGetProperty(key,out var item)||item.ValueKind!=JsonValueKind.String)throw Invalid(path+"."+key,"Expected string.");return item.GetString()!;}
    public static string Id(JsonElement value,string key,string path)
    {if(!Guid.TryParseExact(Text(value,key,path),"B",out var id)||id==Guid.Empty)throw Invalid(path+"."+key,"Read the actual current native ID.");return id.ToString("B").ToUpperInvariant();}
    public static string Token(JsonElement value,string key,string path)
    {string token=Text(value,key,path);if(token.Length!=64||token.Any(c=>!Uri.IsHexDigit(c)))throw Invalid(path+"."+key,"Read the current datum_plane_ids datum_token.");return token.ToUpperInvariant();}
    static void Shape(JsonElement value,string path,string[] fields)
    {if(value.ValueKind!=JsonValueKind.Object)throw Invalid(path,"Expected object.");var seen=new HashSet<string>(StringComparer.Ordinal);foreach(var item in value.EnumerateObject())if(!fields.Contains(item.Name)||!seen.Add(item.Name))throw Invalid(path+"."+item.Name,"Unknown/duplicate field.");}
    public static EditSpec Parse(JsonElement value,string path,string[] handles)
    {
        if(ModelFaceDatumPlaneValidation356.Selects(value))return ModelFaceDatumPlaneValidation356.Parse(value,path,handles);
        Shape(value,path,["operation","handles","expected_last_item_id","name","definition","recovery_context"]);if(handles.Length!=1)throw Invalid(path,"Choose one current native part to establish actual owner scope.");
        string name=Text(value,"name",path);if(name.Length>255||!BlockWriteValidation.OrdinaryName(name))throw Invalid(path+".name","Choose an ordinary native datum name.");
        if(!value.TryGetProperty("definition",out var definition))throw Invalid(path,"definition is required.");
        Shape(definition,path+".definition",["kind","workplane"]);if(Text(definition,"kind",path+".definition")!="fixed")throw Invalid(path,"This branch creates a fixed native plane; it does not invent face/edge constraints.");
        if(!definition.TryGetProperty("workplane",out var plane))throw Invalid(path,"workplane is required.");
        ModelSketchSupportRecoveryContext440? recovery=null;
        if(value.TryGetProperty("recovery_context",out var context))
        {
            Shape(context,path+".recovery_context",["sketch_id","expected_support_id"]);
            recovery=new(Id(context,"sketch_id",path+".recovery_context"),Id(context,"expected_support_id",path+".recovery_context"));
        }
        return new ModelDatumPlaneCreate353(handles,Id(value,"expected_last_item_id",path),name,DrawingWorkplane.Parse(plane),recovery);
    }
    public static void Batch(IReadOnlyList<EditSpec> edits)
    {if(edits.Any(edit=>edit is ModelDatumPlaneCreate353 or ModelFaceDatumPlaneCreate356)&&edits.Count!=1)throw Invalid("$.operations","Create one reusable native plane per request and use its actual ID/token for dependent operations.");}
}
