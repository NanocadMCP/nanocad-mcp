using System.Text.Json;
namespace NanoCAD.Contracts;

public sealed record ModelFaceDatumPlaneCreate356(string[] Targets,string ExpectedLastItemId,string Name,
    string FaceToken,string FaceId,string EdgeToken,string EdgeId,double NativeAngleRadians):EditSpec("create_model_datum_plane",Targets);
public sealed record ModelFaceDatumPlaneRelationRead356(string kind,string source_body_id,string face_id,string edge_id,
    JsonElement face_geometry,JsonElement edge_geometry,string source_token,double unsigned_dihedral_radians,
    double? observed_native_parameter_radians,string angle_scope);
public static class ModelFaceDatumPlaneValidation356
{
    static DomainException Bad(string path,string message)=>new("INVALID_ARGUMENT",message,false,new{path});
    static string Text(JsonElement value,string key,string path)
    {if(!value.TryGetProperty(key,out var item)||item.ValueKind!=JsonValueKind.String)throw Bad(path+"."+key,"Expected a string.");return item.GetString()!;}
    static void Shape(JsonElement value,string path,string[] names)
    {if(value.ValueKind!=JsonValueKind.Object)throw Bad(path,"Expected an object.");var seen=new HashSet<string>(StringComparer.Ordinal);foreach(var p in value.EnumerateObject())if(!names.Contains(p.Name)||!seen.Add(p.Name))throw Bad(path+"."+p.Name,"Unknown or duplicate field.");}
    static string Token(JsonElement value,string key,string path)
    {string text=Text(value,key,path);if(text.Length!=64||text.Any(c=>!Uri.IsHexDigit(c)))throw Bad(path+"."+key,"Read the current native face/edge inventory token.");return text.ToUpperInvariant();}
    public static bool Selects(JsonElement value)=>value.TryGetProperty("definition",out var definition)&&definition.ValueKind==JsonValueKind.Object&&
        definition.TryGetProperty("kind",out var kind)&&kind.ValueKind==JsonValueKind.String&&kind.GetString()=="face_angle";
    public static EditSpec Parse(JsonElement value,string path,string[] handles)
    {
        Shape(value,path,["operation","handles","expected_last_item_id","name","definition"]);if(handles.Length!=1)throw Bad(path,"Choose one exact current native part.");
        string name=Text(value,"name",path);if(name.Length>255||!BlockWriteValidation.OrdinaryName(name))throw Bad(path+".name","Choose an ordinary native datum name.");
        var definition=value.GetProperty("definition");string at=path+".definition";
        Shape(definition,at,["kind","face_token","face_id","edge_token","edge_id","native_angle_radians"]);
        if(Text(definition,"kind",at)!="face_angle")throw Bad(at,"Use the native planar-face/lying-line/angular definition.");
        if(!definition.TryGetProperty("native_angle_radians",out var scalar)||scalar.ValueKind!=JsonValueKind.Number||!scalar.TryGetDouble(out double angle)||!double.IsFinite(angle)||angle< -Math.PI||angle>Math.PI)
            throw Bad(at+".native_angle_radians","Use a finite native angular parameter in radians between -pi and pi.");
        return new ModelFaceDatumPlaneCreate356(handles,ModelDatumPlaneValidation353.Id(value,"expected_last_item_id",path),name,
            Token(definition,"face_token",at),ModelDatumPlaneValidation353.Id(definition,"face_id",at),Token(definition,"edge_token",at),ModelDatumPlaneValidation353.Id(definition,"edge_id",at),angle);
    }
}
