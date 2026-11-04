using System.Globalization;
using System.Text.Json;

namespace NanoCAD.Contracts;

public sealed record SolidFaceSelection409(string Handle,string NativeId);
public sealed record SolidMeasurement409(string Mode,SolidFaceSelection409[]? Faces,double[]? Point);

public static class SolidMeasurementValidation409
{
    public static bool Handles(string? mode) => mode is "solid_distance" or "face_distance" or "wall_thickness" || IsAxisMeasurement(mode);

    public static bool IsAxisMeasurement(string? mode) => mode is "cylinder_diameter" or "axis_axis_distance" or "axis_plane_distance";

    public static GeometryAnalysisArguments Parse(JsonElement input)
    {
        if(input.ValueKind!=JsonValueKind.Object)throw new DomainException("INVALID_ARGUMENT","Solid measurement arguments must be an object.");
        var seen=new HashSet<string>(StringComparer.Ordinal);var read=new Dictionary<string,JsonElement>();
        foreach(var property in input.EnumerateObject())
        {
            if(!seen.Add(property.Name)||property.Name is not ("bridge_instance_id" or "document_id" or "expected_revision" or "mode" or "handles" or "faces" or "point"))
                throw new DomainException("INVALID_ARGUMENT","Unknown or duplicate solid measurement argument: "+property.Name);
            if(property.Name is not ("mode" or "faces" or "point"))read.Add(property.Name,property.Value);
        }
        if(!input.TryGetProperty("mode",out var modeValue)||modeValue.ValueKind!=JsonValueKind.String||!Handles(modeValue.GetString()))
            throw new DomainException("INVALID_ARGUMENT","Unknown solid measurement mode.");
        string mode=modeValue.GetString()!;
        var args=Validation.Parse("inspect_objects",Wire.Element(read));
        if(args.BridgeId==null||args.DocumentId==null||args.ExpectedRevision==null||args.Handles==null)
            throw new DomainException("INVALID_ARGUMENT","Solid measurement requires explicit bridge, document, expected_revision and handles.");
        SolidFaceSelection409[]? faces=null;double[]? point=null;
        if(mode=="solid_distance")
        {
            if(args.Handles.Length!=2||input.TryGetProperty("faces",out _)||input.TryGetProperty("point",out _))
                throw new DomainException("INVALID_ARGUMENT","solid_distance requires exactly two distinct body handles and no faces or point.");
        }
        else
        {
            int faceCount = mode == "cylinder_diameter" ? 1 : 2;
            if(args.Handles.Length < 1 || args.Handles.Length > faceCount || !input.TryGetProperty("faces",out var selections)||selections.ValueKind!=JsonValueKind.Array||selections.GetArrayLength()!=faceCount)
                throw new DomainException("INVALID_ARGUMENT",$"{mode} requires exactly {faceCount} face selection(s) and their source body handles.");
            faces=selections.EnumerateArray().Select(ParseFace).ToArray();
            if(faces.Distinct().Count()!=faceCount||!args.Handles.ToHashSet(StringComparer.Ordinal).SetEquals(faces.Select(f=>f.Handle)))
                throw new DomainException("INVALID_ARGUMENT","Select distinct faces; handles must match exactly the selected source bodies.");
            if(mode=="wall_thickness")
            {
                if(args.Handles.Length!=1||!input.TryGetProperty("point",out var location)||location.ValueKind!=JsonValueKind.Array||location.GetArrayLength()!=3)
                    throw new DomainException("INVALID_ARGUMENT","wall_thickness requires two faces of one body and a WCS XYZ point in the first face interior.");
                point=location.EnumerateArray().Select(v=>v.ValueKind==JsonValueKind.Number&&v.TryGetDouble(out var n)&&double.IsFinite(n)&&Math.Abs(n)<=1e12?n:
                    throw new DomainException("INVALID_ARGUMENT","Point coordinates must be finite drawing-unit values within ±1e12.")).ToArray();
            }
            else if(input.TryGetProperty("point",out _))throw new DomainException("INVALID_ARGUMENT",$"{mode} does not accept point.");
        }
        return new(args,mode,point){SolidMeasurement=new(mode,faces,point)};
    }

    private static SolidFaceSelection409 ParseFace(JsonElement input)
    {
        if(input.ValueKind!=JsonValueKind.Object)throw new DomainException("INVALID_ARGUMENT","Each face selection must be an object.");
        string? handle=null,id=null;var seen=new HashSet<string>(StringComparer.Ordinal);
        foreach(var property in input.EnumerateObject())
        {
            if(!seen.Add(property.Name)||property.Value.ValueKind!=JsonValueKind.String||property.Name is not ("handle" or "native_id"))
                throw new DomainException("INVALID_ARGUMENT","A face selection accepts unique string handle and native_id only.");
            if(property.Name=="handle")handle=Validation.NormalizeHandle(property.Value.GetString()!);else id=property.Value.GetString();
        }
        if(handle==null||id==null||!long.TryParse(id,NumberStyles.None,CultureInfo.InvariantCulture,out long bits)||bits<=0||id!=bits.ToString(CultureInfo.InvariantCulture))
            throw new DomainException("INVALID_ARGUMENT","Use a positive native_id copied from the current body topology and its source handle.");
        return new(handle,id);
    }
}
