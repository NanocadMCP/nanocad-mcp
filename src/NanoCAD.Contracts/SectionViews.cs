using System.Text.Json;

namespace NanoCAD.Contracts;

public sealed record CreateSectionViewEdit(string[] SourceHandles,string Name,Point3[] SectionLine,Point2 Origin,double Scale,Point3 ViewingDirection,Point3 VerticalDirection):StructureEdit("create_section_view");
public static class SectionViewValidation
{
    public static CreateSectionViewEdit Parse(JsonElement item,string path)
    {
        StructureWriteValidation.Shape(item,path,"operation","source_handles","name","section_line","origin","scale","viewing_direction","vertical_direction");
        var name=StructureWriteValidation.Text(item,"name",path,64);
        if(name!=name.Trim() || name.Contains("%<",StringComparison.Ordinal))throw StructureWriteValidation.Invalid(path,"Use a trimmed literal view name.");
        var input=StructureWriteValidation.Required(item,"source_handles",path);
        if(input.ValueKind!=JsonValueKind.Array || input.GetArrayLength()<1)throw StructureWriteValidation.Invalid(path,"Specify the top-level source bodies.");
        var handles=new HashSet<string>(StringComparer.Ordinal);
        foreach(var value in input.EnumerateArray())
            if(value.ValueKind!=JsonValueKind.String || !handles.Add(Validation.NormalizeHandle(value.GetString()!)))throw StructureWriteValidation.Invalid(path,"Source handles must be valid and unique.");
        double Number(JsonElement v,double min=-1e6,double max=1e6)
        {if(v.ValueKind!=JsonValueKind.Number || !v.TryGetDouble(out var n) || !double.IsFinite(n) || n<min || n>max)throw StructureWriteValidation.Invalid(path,"Number outside supported range.");return n==0?0:n;}
        var line=StructureWriteValidation.Required(item,"section_line",path);
        if(line.ValueKind!=JsonValueKind.Array || line.GetArrayLength()!=2)throw StructureWriteValidation.Invalid(path,"Expected two WCS section-line points.");
        var points=line.EnumerateArray().Select(v=>
        {
            if(v.ValueKind!=JsonValueKind.Array || v.GetArrayLength()!=3)throw StructureWriteValidation.Invalid(path,"Expected [x,y,z].");
            return new Point3(Number(v[0]),Number(v[1]),Number(v[2]));
        }).ToArray();
        bool hasViewing=item.TryGetProperty("viewing_direction",out var viewingValue),hasVertical=item.TryGetProperty("vertical_direction",out var verticalValue);
        if(hasViewing!=hasVertical)throw StructureWriteValidation.Invalid(path,"Specify both viewing_direction and vertical_direction, or omit both for +X/+Z.");
        Point3 Axis(JsonElement value)
        {
            if(value.ValueKind!=JsonValueKind.Array || value.GetArrayLength()!=3)throw StructureWriteValidation.Invalid(path,"Expected a unit WCS vector [x,y,z].");
            return new(Number(value[0]),Number(value[1]),Number(value[2]));
        }
        var viewing=hasViewing?Axis(viewingValue):new Point3(1,0,0);var vertical=hasVertical?Axis(verticalValue):new Point3(0,0,1);
        double Dot(Point3 a,Point3 b)=>a.X*b.X+a.Y*b.Y+a.Z*b.Z;
        if(Math.Abs(Dot(viewing,viewing)-1)>1e-10 || Math.Abs(Dot(vertical,vertical)-1)>1e-10 || Math.Abs(Dot(viewing,vertical))>1e-10)
            throw StructureWriteValidation.Invalid(path,"Viewing and vertical directions must be perpendicular unit vectors.","INVALID_GEOMETRY");
        var along=new Point3(vertical.Y*viewing.Z-vertical.Z*viewing.Y,vertical.Z*viewing.X-vertical.X*viewing.Z,vertical.X*viewing.Y-vertical.Y*viewing.X);
        var delta=new Point3(points[1].X-points[0].X,points[1].Y-points[0].Y,points[1].Z-points[0].Z);double length=Dot(delta,along);
        if(length<0.001 || Math.Abs(Dot(delta,vertical))>1e-8 || Math.Abs(Dot(delta,viewing))>1e-8)
            throw StructureWriteValidation.Invalid(path,"Section line must follow vertical_direction cross viewing_direction for at least0.001 mm.","INVALID_GEOMETRY");
        var origin=StructureWriteValidation.Required(item,"origin",path);
        if(origin.ValueKind!=JsonValueKind.Array || origin.GetArrayLength()!=2)throw StructureWriteValidation.Invalid(path,"Expected WCS XY origin [x,y].");
        return new(handles.OrderBy(h=>h,StringComparer.Ordinal).ToArray(),name,points,new(Number(origin[0]),Number(origin[1])),Number(StructureWriteValidation.Required(item,"scale",path),0.001,1000),viewing,vertical);
    }
    public static void ValidateBatch(IReadOnlyList<StructureEdit> edits)
    {
        if(edits.Any(e=>e is CreateSectionViewEdit) && edits.Count!=1)throw StructureWriteValidation.Invalid("$.operations","Section creation must be the sole operation.","INVALID_OPERATION_ORDER");
    }
}
