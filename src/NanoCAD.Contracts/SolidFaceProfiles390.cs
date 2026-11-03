using System.Globalization;
using System.Text.Json;

namespace NanoCAD.Contracts;

public sealed record SolidFaceProfile390(string Handle,string TopologyToken,string NativeId);
public sealed record FaceProfileSolidSpec390(string Kind,SolidFaceProfile390 ProfileFace,Point3 Vector,Point3 Origin,
    double Angle,double StartAngle,string? PathHandle,Point3? BasePoint,bool Align,bool Bank,double Twist,double Scale,
    string LayerName,[property:System.Text.Json.Serialization.JsonIgnore(Condition=System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)] string? PathToken=null):EntitySpec("Solid3d",LayerName);

public static partial class CreateValidation
{
    private static EntitySpec ParseFaceProfileSolid390(JsonElement item,string path,string layer,string kind)
    {
        var fields=new List<string>{"type","kind","layer","profile_face"};
        fields.AddRange(kind switch
        {
            "extrusion"=>new[]{"direction","taper_angle"},
            "revolve"=>new[]{"axis_origin","axis_direction","angle","start_angle"},
            "sweep"=>new[]{"path_handle","path_token","base_point","align","bank","twist_angle","scale_factor"},
            _=>throw Invalid(path+".kind","A face profile supports extrusion, revolve or sweep.")
        });
        Shape(item,path,fields.ToArray());
        var face=Required(item,"profile_face",path);string facePath=path+".profile_face";
        Shape(face,facePath,"handle","topology_token","native_id");
        string handle=CurveHandle(face,"handle",facePath),token=Text(face,"topology_token",facePath,64);
        if(token.Length!=64||token.Any(c=>!(c>='0'&&c<='9'||c>='A'&&c<='F')))
            throw Invalid(facePath+".topology_token","Expected the exact uppercase topology snapshot token.");
        string native=Text(face,"native_id",facePath,19);
        if(!long.TryParse(native,NumberStyles.None,CultureInfo.InvariantCulture,out long nativeId)||nativeId<=0||
            native!=nativeId.ToString(CultureInfo.InvariantCulture))
            throw Invalid(facePath+".native_id","Expected a canonical positive Int64 face ID from the current source topology.");
        var selector=new SolidFaceProfile390(handle,token,native);
        Point3 zero=new(0,0,0);
        if(kind=="sweep")
        {
            string trajectory=CurveHandle(item,"path_handle",path);
            if(trajectory==handle)throw Invalid(path+".path_handle","The path must be a distinct native curve.");
            string? pathToken=null;
            if(item.TryGetProperty("path_token",out _))
            {
                pathToken=Text(item,"path_token",path,64);
                if(pathToken.Length!=64||pathToken.Any(c=>!Uri.IsHexDigit(c)||char.IsLower(c)))
                    throw Invalid(path+".path_token","Use the exact current uppercase geometry.native_curve.curve_token.");
            }
            bool Flag(string name)
            {
                var value=Required(item,name,path);
                if(value.ValueKind is not (JsonValueKind.True or JsonValueKind.False))throw Invalid(path+"."+name,"Expected boolean.");
                return value.GetBoolean();
            }
            return new FaceProfileSolidSpec390(kind,selector,zero,zero,0,0,trajectory,TripleValue(item,"base_point",path),
                Flag("align"),Flag("bank"),Number(Required(item,"twist_angle",path),path+".twist_angle"),
                Number(Required(item,"scale_factor",path),path+".scale_factor",1e-9,1e9),layer,pathToken);
        }
        string vectorField=kind=="extrusion"?"direction":"axis_direction";
        var vector=TripleValue(item,vectorField,path);
        if(Math.Sqrt(vector.X*vector.X+vector.Y*vector.Y+vector.Z*vector.Z)<1e-9)
            throw Invalid(path+"."+vectorField,"A nonzero extrusion displacement or revolution axis is required.");
        string angleField=kind=="extrusion"?"taper_angle":"angle";
        double angle=Number(Required(item,angleField,path),path+"."+angleField);
        if(kind=="extrusion"?Math.Abs(angle)>=Math.PI/2:angle<=0||angle>2*Math.PI)
            throw Invalid(path+"."+angleField,"Extrusion taper must be between -pi/2 and pi/2; revolution angle must be positive and no greater than 2pi.");
        return new FaceProfileSolidSpec390(kind,selector,vector,kind=="extrusion"?zero:TripleValue(item,"axis_origin",path),
            angle,kind=="extrusion"?0:Number(Required(item,"start_angle",path),path+".start_angle"),null,null,false,false,0,1,layer);
    }
}
