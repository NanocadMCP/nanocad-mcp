using System.Text.Json;

namespace NanoCAD.Contracts;

public sealed record ModelSketchElementEdit(string ElementId,JsonElement ExpectedGeometry,JsonElement Geometry);
public sealed record ModelSketchEdit(string[] Targets,string ExpectedLastItemId,string SketchId,ModelSketchElementEdit[] Elements):EditSpec("set_model_sketch_geometry",Targets);
public sealed record ModelSketchChange(EntityRef body_ref,string sketch_id,string[] element_ids,double volume_before,double volume_after);

public static partial class ModifyValidation
{
    private static ModelSketchEdit ParseModelSketch(JsonElement item,string path,string[] handles)
    {
        Shape(item,path,"operation","handles","expected_last_item_id","sketch_id","elements");
        if(handles.Length!=1)throw Invalid(path,"Edit one sketch in one model per request.");
        string Id(JsonElement o,string key,string at)
        {var s=Text(o,key,at,38);if(!Guid.TryParseExact(s,"B",out var id)||id==Guid.Empty)throw Invalid(at+"."+key,"Use a fresh nonempty braced GUID from inspect_model_history.");return id.ToString("B").ToUpperInvariant();}
        string last=Id(item,"expected_last_item_id",path),sketch=Id(item,"sketch_id",path);
        var list=Required(item,"elements",path);if(list.ValueKind!=JsonValueKind.Array||list.GetArrayLength()<1)throw Invalid(path+".elements","Change at least one existing element, preserving its identity and geometry type.");
        var edits=new List<ModelSketchElementEdit>();
        foreach(var e in list.EnumerateArray())
        {
            Shape(e,path,"element_id","expected_geometry","geometry");string id=Id(e,"element_id",path);
            var before=SketchGeometry(Required(e,"expected_geometry",path),path+".expected_geometry");var after=SketchGeometry(Required(e,"geometry",path),path+".geometry");
            if(before.GetProperty("kind").GetString()!=after.GetProperty("kind").GetString())throw Invalid(path,"Keep each element's geometry type; creation/deletion is outside this edit.");
            edits.Add(new(id,before,after));
        }
        if(edits.Select(e=>e.ElementId).Distinct(StringComparer.OrdinalIgnoreCase).Count()!=edits.Count)throw Invalid(path,"Duplicate element IDs.");
        return new(handles,last,sketch,edits.ToArray());
    }
    private static JsonElement SketchGeometry(JsonElement g,string path)
    {
        Shape(g,path,"kind","start","end","center","radius","normal","reference_direction","start_angle","end_angle","angle_unit");string kind=Text(g,"kind",path,16);
        double[] Point(string key)
        {var p=Required(g,key,path);if(p.ValueKind!=JsonValueKind.Array||p.GetArrayLength()!=3)throw Invalid(path,"Use WCS XYZ triples.");return p.EnumerateArray().Select(v=>Number(v,path,-1e6,1e6)).ToArray();}
        if(kind=="line")
        {Shape(g,path,"kind","start","end");var a=Point("start");var b=Point("end");if(a.Zip(b).Sum(v=>Math.Pow(v.First-v.Second,2))<1e-12)throw Invalid(path,"A line must have distinct endpoints.","INVALID_GEOMETRY");}
        else if(kind=="circular_arc")
        {
            Shape(g,path,"kind","center","radius","normal","reference_direction","start_angle","end_angle","angle_unit");Point("center");Number(Required(g,"radius",path),path,.001,1e6);
            var n=Point("normal");var x=Point("reference_direction");
            if(Math.Abs(n.Sum(v=>v*v)-1)>1e-9||Math.Abs(x.Sum(v=>v*v)-1)>1e-9||Math.Abs(n.Zip(x).Sum(p=>p.First*p.Second))>1e-9)throw Invalid(path,"Arc normal and reference_direction must be perpendicular unit vectors.","INVALID_GEOMETRY");
            double a=Number(Required(g,"start_angle",path),path,-1e6,1e6),b=Number(Required(g,"end_angle",path),path,-1e6,1e6);
            if(Math.Abs(b-a)<1e-10||Math.Abs(b-a)>2*Math.PI+1e-9)throw Invalid(path,"Arc sweep must be nonzero and at most one full turn.","INVALID_GEOMETRY");
            if(Text(g,"angle_unit",path,16)!="radians")throw Invalid(path,"Arc angles use radians.");
        }
        else throw Invalid(path,"Only line and circular_arc geometry returned by inspect_model_history is editable.");
        return g.Clone();
    }
}
