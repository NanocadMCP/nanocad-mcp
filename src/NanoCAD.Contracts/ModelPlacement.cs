using System.Text.Json;

namespace NanoCAD.Contracts;

public sealed record ModelPlacement(string Kind,Point3 Vector,Point3? Center=null);
public sealed record ModelPlacementEdit(string Mode,string[] Targets,string ExpectedLastItemId,ModelPlacement Placement,ModelPlacement? ExpectedPlacement=null)
    :EditSpec(Mode,Targets);
public sealed record ModelPlacementChange(EntityRef body_ref,string feature_id,string kind,string[] variable_ids,EntityRef? anchor_ref);

public static partial class ModifyValidation
{
    private static ModelPlacementEdit ParseModelPlacement(JsonElement item,string path,string[] handles,string operation)
    {
        bool edit=operation=="set_model_placement";
        Shape(item,path,edit?["operation","handles","expected_last_item_id","placement","expected_placement"]:["operation","handles","expected_last_item_id","placement"]);
        if(handles.Length!=1)throw Invalid(path,"Place one native model per request.");
        string last=Text(item,"expected_last_item_id",path,38);
        if(!Guid.TryParseExact(last,"B",out var id)||id==Guid.Empty)throw Invalid(path,"Read a nonempty current last_item_id from inspect_model_history.");
        ModelPlacement Read(JsonElement value,string at)
        {
            string kind=Text(value,"kind",at,16);
            Point3 P(string name,double limit)
            {
                var p=Required(value,name,at);if(p.ValueKind!=JsonValueKind.Array||p.GetArrayLength()!=3)throw Invalid(at+"."+name,"Expected an XYZ triple.");
                return new(Number(p[0],at,-limit,limit),Number(p[1],at,-limit,limit),Number(p[2],at,-limit,limit));
            }
            if(kind=="move"){Shape(value,at,"kind","displacement");return new(kind,P("displacement",1e6));}
            if(kind=="rotate"){Shape(value,at,"kind","center","angles_degrees");return new(kind,P("angles_degrees",360),P("center",1e6));}
            throw Invalid(at,"Use move or rotate; no scaling, reflection or copy.");
        }
        var placement=Read(Required(item,"placement",path),path+".placement");
        var expected=edit?Read(Required(item,"expected_placement",path),path+".expected_placement"):null;
        if(expected!=null&&(expected.Kind!=placement.Kind||expected.Center!=placement.Center))throw Invalid(path,"Edit the same final placement kind and retain its rotation center. Append another placement to rotate around a new center.");
        return new(operation,handles,id.ToString("B").ToUpperInvariant(),placement,expected);
    }
}
