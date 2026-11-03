using System.Text.Json;

namespace NanoCAD.Contracts;

public sealed record ModelShellSpec(string[] Targets,string ExpectedLastItemId,string FaceToken,
    string[] OpenFaceIds,double Thickness) : EditSpec("add_model_shell",Targets);

public sealed record ModelShellChange(string operation,EntityRef source,EntityRef body_ref,string body_mc_id,
    string feature_id,string open_face_id,double thickness,double? previous_thickness,
    double volume_before,double volume_after,bool source_representation_replaced,string[] open_face_ids);

public sealed record ModelShellThicknessSpec(string[] Targets,string ExpectedLastItemId,
    string FeatureId,double ExpectedThickness,double Thickness,bool ReplaceDriverFormulas=false)
    : EditSpec("set_model_shell_thickness",Targets);

public static partial class ModifyValidation
{
    private static ModelShellSpec ParseModelShell(JsonElement item,string path,string[] handles)
    {
        Shape(item,path,"operation","handles","expected_last_item_id","face_token","open_face_id","open_face_ids","thickness");
        if(handles.Length!=1)throw Invalid(path+".handles","Select exactly one native model body.");
        string last=Text(item,"expected_last_item_id",path,38);
        if(!Guid.TryParseExact(last,"B",out var id)||id==Guid.Empty)
            throw Invalid(path+".expected_last_item_id","Use the current terminal history ID.");
        string token=Text(item,"face_token",path,64);
        if(token.Length!=64||token.Any(c=>!Uri.IsHexDigit(c)))
            throw Invalid(path+".face_token","Use the exact current native face token.");
        bool single=item.TryGetProperty("open_face_id",out _),many=item.TryGetProperty("open_face_ids",out var listed);
        if(single==many)throw Invalid(path,"Supply exactly one of open_face_id or open_face_ids.");
        string[] faces;
        if(single)faces=[Text(item,"open_face_id",path,128)];
        else
        {
            if(listed.ValueKind!=JsonValueKind.Array||listed.GetArrayLength()==0)
                throw Invalid(path+".open_face_ids","Select a nonempty set of actual native opening face IDs.");
            faces=listed.EnumerateArray().Select((face,index)=>
            {
                string? value=face.ValueKind==JsonValueKind.String?face.GetString():null;
                if(string.IsNullOrWhiteSpace(value)||value.Length>128)
                    throw Invalid(path+".open_face_ids["+index+"]","Use a current native model_face_id.");
                return value.Trim();
            }).ToArray();
        }
        if(faces.Distinct(StringComparer.OrdinalIgnoreCase).Count()!=faces.Length)
            throw Invalid(path+".open_face_ids","Select distinct current native opening faces.");
        double thickness=Number(Required(item,"thickness",path),path+".thickness",.001,1e6);
        return new(handles,id.ToString("B").ToUpperInvariant(),token.ToUpperInvariant(),faces,thickness);
    }
    private static ModelShellThicknessSpec ParseModelShellThickness(JsonElement item,string path,string[] handles)
    {
        Shape(item,path,"operation","handles","expected_last_item_id","feature_id","expected_thickness","thickness","replace_driver_formulas");
        if(handles.Length!=1)throw Invalid(path+".handles","Select exactly one native model body.");
        string Id(string field)
        {
            string text=Text(item,field,path,38);
            if(!Guid.TryParseExact(text,"B",out var id)||id==Guid.Empty)
                throw Invalid(path+"."+field,"Use the exact current native history ID.");
            return id.ToString("B").ToUpperInvariant();
        }
        string last=Id("expected_last_item_id"),feature=Id("feature_id");
        double before=Number(Required(item,"expected_thickness",path),path+".expected_thickness",.001,1e6);
        double after=Number(Required(item,"thickness",path),path+".thickness",.001,1e6);
        if(Math.Abs(before-after)<=Math.Max(1e-9,Math.Max(before,after)*1e-10))
            throw Invalid(path+".thickness","Requested native thickness must change.");
        bool replace=false;
        if(item.TryGetProperty("replace_driver_formulas",out var flag))
        {
            if(flag.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
                throw Invalid(path+".replace_driver_formulas","Use a boolean.");
            replace=flag.GetBoolean();
        }
        return new(handles,last,feature,before,after,replace);
    }
}
