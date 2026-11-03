using System.Globalization;
using System.Text.Json;

namespace NanoCAD.Contracts;

public sealed record SolidFinishingCopySpec(string Kind,string SourceHandle,string TopologyToken,string[] Subentities,string? BaseFace,double Size,double OtherSize,string LayerName,string ShellSide="inward"):EntitySpec("Solid3d",LayerName);
public sealed record SolidFinishingEditSpec(string Kind,string[] Targets,string TopologyToken,string[] Subentities,string? BaseFace,double Size,double OtherSize,string ShellSide="inward"):EditSpec(Kind,Targets)
{
    public SolidFinishingCopySpec AsCopySpec(string layer) => new(Kind switch
    {
        "fillet_solid"=>"fillet_copy", "chamfer_solid"=>"chamfer_copy", "shell_solid"=>"shell_copy",
        _=>throw new DomainException("INVALID_ARGUMENT","Unknown solid finishing operation.")
    },Targets.Single(),TopologyToken,Subentities,BaseFace,Size,OtherSize,layer,ShellSide);
}

public static partial class CreateValidation
{
    private static EntitySpec ParseSolidFinishingCopy(JsonElement item,string path,string layer,string kind)
    {
        string member=kind=="shell_copy"?"remove_faces":"edges";
        var fields=new List<string>{"type","kind","layer","source_handle","topology_token",member};
        fields.AddRange(kind switch{"fillet_copy"=>new[]{"radius"},"chamfer_copy"=>new[]{"base_face","base_distance","other_distance"},_=>new[]{"thickness"}});
        if(kind=="shell_copy")fields.Add("side");
        Shape(item,path,fields.ToArray());
        string source=Validation.NormalizeHandle(Text(item,"source_handle",path,16));
        string token=Text(item,"topology_token",path,64);
        if(token.Length!=64||token.Any(c=>!(c>='0'&&c<='9'||c>='A'&&c<='F')))throw Invalid(path+".topology_token","Expected the exact uppercase topology snapshot token.");
        string Id(JsonElement value)
        {
            if(value.ValueKind!=JsonValueKind.String||!long.TryParse(value.GetString(),NumberStyles.None,CultureInfo.InvariantCulture,out long id)||id<=0||value.GetString()!=id.ToString(CultureInfo.InvariantCulture))
                throw Invalid(path+"."+member,"Native IDs must be canonical positive Int64 decimal strings from the current snapshot.");
            return value.GetString()!;
        }
        var selected=Required(item,member,path);
        if(selected.ValueKind!=JsonValueKind.Array||(kind!="shell_copy"&&selected.GetArrayLength()<1))throw Invalid(path+"."+member,"Select at least one subentity from the current topology snapshot.");
        string[] ids=selected.EnumerateArray().Select(Id).ToArray();
        if(ids.Distinct(StringComparer.Ordinal).Count()!=ids.Length)throw Invalid(path+"."+member,"Duplicate subentities.");
        string size=kind=="fillet_copy"?"radius":kind=="shell_copy"?"thickness":"base_distance";
        return new SolidFinishingCopySpec(kind,source,token,ids,kind=="chamfer_copy"?Id(Required(item,"base_face",path)):null,
            Number(Required(item,size,path),path+"."+size,1e-3,1e6),kind=="chamfer_copy"?Number(Required(item,"other_distance",path),path+".other_distance",1e-3,1e6):0,layer,kind=="shell_copy"?SolidShellSide353.Parse(item,path):"inward");
    }
}

public static partial class ModifyValidation
{
    private static SolidFinishingEditSpec ParseSolidFinishingEdit(JsonElement item,string path,string[] handles,string operation)
    {
        if(handles.Length!=1)throw Invalid(path+".handles","Finish exactly one direct solid per operation.");
        string collection=operation=="shell_solid"?"remove_faces":"edges";
        string size=operation=="fillet_solid"?"radius":operation=="shell_solid"?"thickness":"base_distance";
        if(operation=="chamfer_solid")Shape(item,path,"operation","handles","topology_token",collection,"base_face","base_distance","other_distance");
        else if(operation=="shell_solid")Shape(item,path,"operation","handles","topology_token",collection,size,"side");
        else Shape(item,path,"operation","handles","topology_token",collection,size);
        string token=Text(item,"topology_token",path,64);
        if(token.Length!=64||token.Any(c=>!(c>='0'&&c<='9'||c>='A'&&c<='F')))
            throw Invalid(path+".topology_token","Expected the exact uppercase topology snapshot token.");
        string Id(JsonElement value,string field)
        {
            if(value.ValueKind!=JsonValueKind.String||!long.TryParse(value.GetString(),NumberStyles.None,CultureInfo.InvariantCulture,out long id)||id<=0||value.GetString()!=id.ToString(CultureInfo.InvariantCulture))
                throw Invalid(path+"."+field,"Native ID must be a canonical positive Int64 decimal string from the current snapshot.");
            return value.GetString()!;
        }
        var selected=Required(item,collection,path);
        if(selected.ValueKind!=JsonValueKind.Array||(operation!="shell_solid"&&selected.GetArrayLength()<1))
            throw Invalid(path+"."+collection,"Select at least one subentity from the current topology snapshot.");
        string[] ids=selected.EnumerateArray().Select(value=>Id(value,collection)).ToArray();
        if(ids.Distinct(StringComparer.Ordinal).Count()!=ids.Length)throw Invalid(path+"."+collection,"Duplicate subentities.");
        string? face=operation=="chamfer_solid"?Id(Required(item,"base_face",path),"base_face"):null;
        double primary=Number(Required(item,size,path),path+"."+size,1e-3,1e6);
        double secondary=operation=="chamfer_solid"?Number(Required(item,"other_distance",path),path+".other_distance",1e-3,1e6):0;
        return new(operation,handles,token,ids,face,primary,secondary,operation=="shell_solid"?SolidShellSide353.Parse(item,path):"inward");
    }
}
