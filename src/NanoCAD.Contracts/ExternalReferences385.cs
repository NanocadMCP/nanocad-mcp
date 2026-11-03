using System.Text.Json;
using static NanoCAD.Contracts.StructureWriteValidation;

namespace NanoCAD.Contracts;

public abstract record ExternalReferenceEdit385(string Kind) : StructureEdit(Kind);
public sealed record AttachXref385(string SourcePath,string Name,Point2 Position,string Layer,double Rotation,double Scale,bool Overlay)
    : ExternalReferenceEdit385("attach_xref");
public sealed record InsertRasterImage385(string SourcePath,string Name,Point2 Position,string Layer,double Rotation,double Width,double Height)
    : ExternalReferenceEdit385("insert_raster_image");
public sealed record AttachUnderlay385(string Format,string SourcePath,string ItemName,string Name,Point2 Position,string Layer,double Rotation,double Scale)
    : ExternalReferenceEdit385("attach_underlay");
public sealed record ReferencePlacement385(Point2? Position,double? Rotation,double? Scale,double? Width,double? Height);
public sealed record SetReferencePlacement385(string ReferenceKind,string Handle,string ReferenceToken,ReferencePlacement385 Properties)
    : ExternalReferenceEdit385("set_reference_placement");
public sealed record ReferenceLifecycle385(string Action,string ReferenceKind,string Handle,string ReferenceToken)
    : ExternalReferenceEdit385(Action);

public static class ExternalReferenceValidation385
{
    public static readonly string[] Operations=["attach_xref","insert_raster_image","attach_underlay","set_reference_placement","reload_xref","detach_xref","reload_reference","delete_reference","set_raster_image_display","set_raster_image_clip"];
    public static bool Handles(string operation)=>Operations.Contains(operation,StringComparer.Ordinal);
    public static double Number(JsonElement value,string path,bool positive=false)
    {
        if(value.ValueKind!=JsonValueKind.Number||!value.TryGetDouble(out double number)||!double.IsFinite(number)||positive&&number<=0)
            throw Invalid(path,positive?"Expected a finite positive number.":"Expected a finite number.","INVALID_GEOMETRY");
        return number==0?0:number;
    }
    static Point2 Point(JsonElement value,string path)
    {
        if(value.ValueKind!=JsonValueKind.Array||value.GetArrayLength()!=2)throw Invalid(path,"Expected a WCS XY pair at Z=0.");
        return new(Number(value[0],path+"[0]"),Number(value[1],path+"[1]"));
    }
    public static double Rotation(double value){double result=value%(2*Math.PI);return result<0?result+2*Math.PI:result==0?0:result;}
    public static string NormalizePath(string path,string kind)
    {
        path=path.Replace('/','\\');
        if(path.Length is <4 or >240||!(path[0] is >= 'A' and <= 'Z' or >= 'a' and <= 'z')||path[1]!=':'||path[2]!='\\'||path.Skip(2).Any(c=>c<32||":*?\"<>|".Contains(c)))
            throw new DomainException("INVALID_PATH","Use an absolute local drive path of at most240 characters; network/device/relative paths are outside this route.");
        foreach(string component in path[3..].Split('\\'))
        {
            string stem=component.Split('.')[0].ToUpperInvariant();
            if(component.Length==0||component is "." or ".."||component.EndsWith(' ')||component.EndsWith('.')||stem is "CON" or "PRN" or "AUX" or "NUL"||
                stem.Length==4&&(stem.StartsWith("COM",StringComparison.Ordinal)||stem.StartsWith("LPT",StringComparison.Ordinal))&&stem[3] is >= '1' and <= '9')
                throw new DomainException("INVALID_PATH","Path contains an ambiguous or reserved Win32 component.");
        }
        string extension=System.IO.Path.GetExtension(path).ToLowerInvariant();
        if(kind=="xref"&&extension!=".dwg"||kind=="dwf"&&extension is not (".dwf" or ".dwfx")||kind=="dgn"&&extension!=".dgn"||kind=="pdf"&&extension!=".pdf")
            throw new DomainException("UNSUPPORTED_FORMAT","Xrefs use DWG; DWF/DGN/PDF underlays use their declared format. Raster files must be decodable by the native image loader.");
        return System.IO.Path.GetFullPath(path);
    }
    static string Name(JsonElement value,string path)
    {
        string name=Text(value,"name",path,255);
        if(!BlockWriteValidation.OrdinaryName(name)||name.StartsWith('*'))throw Invalid(path+".name","Expected a unique ordinary definition name.");
        return name;
    }
    static string Token(JsonElement value,string path)
    {
        string token=Text(value,"reference_token",path,64);
        if(token.Length!=64||token.Any(c=>!Uri.IsHexDigit(c)))throw Invalid(path+".reference_token","Use the exact current reference_token from readback.");
        return token.ToUpperInvariant();
    }
    static string Kind(JsonElement value,string path)
    {
        string kind=Text(value,"reference_kind",path,16);
        if(kind is not ("xref" or "raster_image" or "dwf" or "dgn" or "pdf"))throw Invalid(path+".reference_kind","Use xref, raster_image, dwf, dgn or pdf matching the observed native reference.");
        return kind;
    }
    public static ExternalReferenceEdit385 Parse(JsonElement value,string path)
    {
        string operation=Text(value,"operation",path,32);
        if(operation=="set_raster_image_display")return RasterImageDisplayValidation390.Parse(value,path);
        if(operation=="set_raster_image_clip")return RasterImageClipValidation391.Parse(value,path);
        if(operation is "attach_xref" or "insert_raster_image" or "attach_underlay")
        {
            string[] extra=operation switch{"attach_xref"=>["scale","overlay"],"insert_raster_image"=>["width","height"],_=>["format","item_name","scale"]};
            Shape(value,path,new[]{"operation","source_path","name","position","layer","rotation"}.Concat(extra).ToArray());
            string kind=operation=="attach_xref"?"xref":operation=="insert_raster_image"?"raster_image":Text(value,"format",path,3);
            if(operation=="attach_underlay"&&kind is not ("dwf" or "dgn" or "pdf"))throw Invalid(path+".format","Use dwf, dgn or pdf matching the native file format.");
            string file=NormalizePath(Text(value,"source_path",path,240),kind),name=Name(value,path),layer=Text(value,"layer",path,255);
            Point2 position=Point(Required(value,"position",path),path+".position");
            double rotation=value.TryGetProperty("rotation",out var angle)?Rotation(Number(angle,path+".rotation")):0;
            if(operation=="insert_raster_image")return new InsertRasterImage385(file,name,position,layer,rotation,Number(Required(value,"width",path),path+".width",true),Number(Required(value,"height",path),path+".height",true));
            double scale=value.TryGetProperty("scale",out var factor)?Number(factor,path+".scale",true):1;
            if(operation=="attach_underlay")return new AttachUnderlay385(kind,file,Text(value,"item_name",path,255),name,position,layer,rotation,scale);
            bool overlay=false;
            if(value.TryGetProperty("overlay",out var flag)){if(flag.ValueKind is not (JsonValueKind.True or JsonValueKind.False))throw Invalid(path+".overlay","Expected boolean.");overlay=flag.GetBoolean();}
            return new AttachXref385(file,name,position,layer,rotation,scale,overlay);
        }
        Shape(value,path,operation=="set_reference_placement"?["operation","reference_kind","handle","reference_token","properties"]:["operation","reference_kind","handle","reference_token"]);
        string actualKind=Kind(value,path),handle=Validation.NormalizeHandle(Text(value,"handle",path,16)),token=Token(value,path);
        if(operation=="set_reference_placement")
        {
            var properties=Required(value,"properties",path);Shape(properties,path+".properties",actualKind=="raster_image"?["position","rotation","width","height"]:["position","rotation","scale"]);
            if(!properties.EnumerateObject().Any())throw Invalid(path+".properties","At least one placement property is required.");
            double? N(string name)=>properties.TryGetProperty(name,out var n)?Number(n,path+".properties."+name,name!="rotation"):null;
            double? r=N("rotation");
            return new SetReferencePlacement385(actualKind,handle,token,new(properties.TryGetProperty("position",out var p)?Point(p,path+".properties.position"):null,r.HasValue?Rotation(r.Value):null,N("scale"),N("width"),N("height")));
        }
        if(operation is "reload_xref" or "detach_xref"){if(actualKind!="xref")throw Invalid(path+".reference_kind","Xref lifecycle requires an actual xref definition.");}
        else if(operation=="reload_reference"){if(actualKind=="xref")throw Invalid(path+".operation","Use reload_xref for an xref definition.");}
        else if(operation!="delete_reference")throw Invalid(path+".operation","Unknown external-reference operation.");
        return new ReferenceLifecycle385(operation,actualKind,handle,token);
    }
    public static void Batch(IEnumerable<StructureEdit> edits)
    {
        if(!edits.Any(edit=>edit is ExternalReferenceEdit385))return;
        if(edits.Any(edit=>edit is not ExternalReferenceEdit385))throw Invalid("operations","Reference operations use a separate batch with already committed layers/source files; no implicit save or source modification.","INVALID_OPERATION_ORDER");
        var names=new HashSet<string>(StringComparer.OrdinalIgnoreCase);var targets=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach(var edit in edits)
        {
            string? name=edit switch{AttachXref385 e=>e.Name,InsertRasterImage385 e=>e.Name,AttachUnderlay385 e=>e.Name,_=>null};
            string? handle=edit switch{SetReferencePlacement385 e=>e.Handle,SetRasterImageDisplay390 e=>e.Handle,SetRasterImageClip391 e=>e.Handle,ReferenceLifecycle385 e=>e.Handle,_=>null};
            string kind=edit switch{AttachXref385=>"xref",InsertRasterImage385=>"raster_image",AttachUnderlay385 e=>e.Format,_=>""};
            if(name!=null&&!names.Add(kind+":"+name)||handle!=null&&!targets.Add(handle))throw Invalid("operations","Definition names within each native dictionary and existing targets must be distinct in one reference batch.","INVALID_OPERATION_ORDER");
        }
    }
}
