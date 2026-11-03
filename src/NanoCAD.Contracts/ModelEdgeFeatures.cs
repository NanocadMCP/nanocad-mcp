using System.Globalization;
using System.Text.Json;

namespace NanoCAD.Contracts;

public sealed record ModelEdgeFeatureSpec(string Kind,string[] Targets,string ExpectedLastItemId,string TopologyToken,string[] EdgeIds,double Size)
    : EditSpec(Kind,Targets);
public sealed record ModelEdgeFeatureChange(EntityRef source,EntityRef body_ref,string body_mc_id,string feature_id,string variable_id,
    string kind,string[] edge_ids,double size,double volume_before,double volume_after,bool source_representation_replaced);

public static partial class ModifyValidation
{
    private static ModelEdgeFeatureSpec ParseModelEdgeFeature(JsonElement item,string path,string[] handles,string kind)
    {
        string sizeField=kind=="add_model_fillet"?"radius":"distance";
        Shape(item,path,"operation","handles","expected_last_item_id","topology_token","edges",sizeField);
        if(handles.Length!=1)throw Invalid(path+".handles","Append one native edge feature to one model body.");
        string last=Text(item,"expected_last_item_id",path,38);
        if(!Guid.TryParseExact(last,"B",out var id)||id==Guid.Empty)throw Invalid(path+".expected_last_item_id","Use the current last_item_id from inspect_model_history.");
        string token=Text(item,"topology_token",path,64);
        if(token.Length!=64||token.Any(c=>!Uri.IsHexDigit(c)))throw Invalid(path+".topology_token","Use the exact current snapshot_token from inspect_solid_topology.");
        var input=Required(item,"edges",path);
        if(input.ValueKind!=JsonValueKind.Array||input.GetArrayLength()<1)throw Invalid(path+".edges","Select a nonempty set of current native B-rep edges.");
        var markers=new List<string>();var unique=new HashSet<long>();
        foreach(var marker in input.EnumerateArray())
        {
            if(marker.ValueKind!=JsonValueKind.String||!long.TryParse(marker.GetString(),NumberStyles.None,CultureInfo.InvariantCulture,out long value)||value<=0||!unique.Add(value))
                throw Invalid(path+".edges","Edge IDs must be unique positive decimal strings from inspect_solid_topology.");
            markers.Add(value.ToString(CultureInfo.InvariantCulture));
        }
        double size=Number(Required(item,sizeField,path),path+"."+sizeField,.001,1e6);
        return new(kind,handles,id.ToString("B").ToUpperInvariant(),token,markers.ToArray(),size);
    }
}
