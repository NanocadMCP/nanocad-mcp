using System.Text.Json;
using static NanoCAD.Contracts.StructureWriteValidation;

namespace NanoCAD.Contracts;

public sealed record SetSectionViewCut351(string SectionId,string SectionToken,
    DrawingViewLifecycleTarget345[] Targets,Point3[] SectionLine):StructureEdit("set_section_view_cut");
public sealed record SectionCutRead351(EntityRef section_ref,string section_id,string section_token,string scope);
public sealed record SectionCutChange351(int operation_index,EntityRef section_ref,string section_id,string section_token,
    EntityRef[] view_refs,int processing_count,string processing_inventory_sha256,string processing_scope);
public sealed record SectionCutOutput351(string operation_id,string status,bool replayed,StructureChange[] changes,
    string undo_history,DrawingViewLifecycleChange345[] drawing_view_lifecycle_changes,SectionCutChange351[] section_cut_changes);

public static class SectionCutValidation351
{
    static string Token(JsonElement value,string key,string path)
    {string token=Text(value,key,path,64);if(token.Length!=64||!token.All(Uri.IsHexDigit))throw Invalid(path+"."+key,"Read the current complete native token.");return token.ToUpperInvariant();}
    public static StructureEdit Parse(JsonElement value,string path)
    {
        Shape(value,path,"operation","section_id","section_token","targets","section_line");
        if(Text(value,"operation",path,32)!="set_section_view_cut")throw Invalid(path,"Expected set_section_view_cut.");
        if(!Guid.TryParseExact(Text(value,"section_id",path,38),"B",out var id)||id==Guid.Empty)throw Invalid(path+".section_id","Read the exact section_id, not a DWG handle.");
        var targets=Required(value,"targets",path);if(targets.ValueKind!=JsonValueKind.Array||targets.GetArrayLength()==0)throw Invalid(path+".targets","Select all associated section views and their descendants with current tokens.");
        var selected=new List<DrawingViewLifecycleTarget345>();var seen=new HashSet<string>(StringComparer.Ordinal);
        foreach(var target in targets.EnumerateArray())
        {
            string at=path+".targets["+selected.Count+"]";Shape(target,at,"layout_handle","handle","expected_name","view_token");
            string handle=Validation.NormalizeHandle(Text(target,"handle",at,16));if(!seen.Add(handle))throw Invalid(at,"Select each native view once.");
            selected.Add(new(new(Validation.NormalizeHandle(Text(target,"layout_handle",at,16)),handle,Text(target,"expected_name",at,4096)),Token(target,"view_token",at)));
        }
        var line=Required(value,"section_line",path);if(line.ValueKind!=JsonValueKind.Array||line.GetArrayLength()!=2)throw Invalid(path+".section_line","Supply two WCS points for the existing native Plane section.");
        var points=new List<Point3>();foreach(var point in line.EnumerateArray())
        {
            if(point.ValueKind!=JsonValueKind.Array||point.GetArrayLength()!=3)throw Invalid(path+".section_line","Use WCS [x,y,z].");
            var xyz=new double[3];for(int i=0;i<3;i++)if(point[i].ValueKind!=JsonValueKind.Number||!point[i].TryGetDouble(out xyz[i])||!double.IsFinite(xyz[i])||Math.Abs(xyz[i])>1e6)throw Invalid(path+".section_line","Use finite drawing millimeters within1e6.");
            points.Add(new(xyz[0],xyz[1],xyz[2]));
        }
        return new SetSectionViewCut351(id.ToString("B").ToUpperInvariant(),Token(value,"section_token",path),selected.OrderBy(item=>item.Target.Handle,StringComparer.Ordinal).ToArray(),points.ToArray());
    }
    public static void Batch(IReadOnlyList<StructureEdit> edits)
    {if(edits.Any(edit=>edit is SetSectionViewCut351)&&edits.Count!=1)throw Invalid("$.operations","A retained section cut must be the sole operation.","INVALID_OPERATION_ORDER");}
}
