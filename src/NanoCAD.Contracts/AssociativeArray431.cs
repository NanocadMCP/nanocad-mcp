using System.Text.Json;

namespace NanoCAD.Contracts;

public sealed record CreateAssociativeRectangularArray431(string[] Targets,Point2 BasePoint,int Columns,int Rows,double ColumnSpacing,double RowSpacing)
    :EditSpec("create_associative_rectangular_array",Targets);
public sealed record SetAssociativeRectangularArray431(string[] Targets,string ArrayToken,int? Columns,int? Rows,double? ColumnSpacing,double? RowSpacing,double? AxesAngleDegrees=null,Point3? PositionWcs=null)
    :EditSpec("set_associative_rectangular_array",Targets);
public sealed record AssociativeRectangularArrayObservation431(string array_token,string kind,string array_handle,string[] source_handles,
    int columns,int rows,int levels,double column_spacing,double row_spacing,double level_spacing,double row_elevation,double axes_angle,
    double[] x_axis_direction,double[]? y_axis_direction,double[] base_normal,double[] position_wcs,double[] scale_factors,double rotation,
    string driver_expression_observation,string axes_angle_unit,string? y_axis_direction_unavailable_reason);
public sealed record AssociativeArrayChange431(string operation,EntityRef array,EntityRef[] selected_sources,EntityRef[] native_sources,
    bool associative,object parameters);
public sealed record AssociativeArrayWriteResult431(string operation_id,string status,bool replayed,EntityRef[] modified,EntityRef[] erased,EntityRef[] created,
    CopyMapping[] copies,string undo_history,AssociativeArrayChange431[] associative_array_changes);

public static class AssociativeArrayValidation431
{
    private static DomainException Bad(string path,string message)=>new("INVALID_ARGUMENT",path+": "+message,false,new{path});
    public static EditSpec Parse(JsonElement input,string path,string[] handles,string operation)
    {
        bool create=operation=="create_associative_rectangular_array";
        string[] allowed=create?["operation","handles","base_point","columns","rows","column_spacing","row_spacing"]:
            ["operation","handles","array_token","columns","rows","column_spacing","row_spacing","axes_angle_degrees","position_wcs"];
        var fields=new HashSet<string>(StringComparer.Ordinal);
        foreach(var field in input.EnumerateObject())if(!fields.Add(field.Name)||!allowed.Contains(field.Name))throw Bad(path+"."+field.Name,"Unknown or duplicate field.");
        if(!create&&handles.Length!=1)throw Bad(path+".handles","Choose one native associative array root.");
        int? Count(string name)
        {
            if(!input.TryGetProperty(name,out var value)){if(create)throw Bad(path+"."+name,"Required positive count including the seed.");return null;}
            if(value.ValueKind!=JsonValueKind.Number||!value.TryGetInt32(out int result)||result<1)throw Bad(path+"."+name,"Use a positive native Int32 count including the seed; no artificial item ceiling.");
            return result;
        }
        double? Spacing(string name)
        {
            if(!input.TryGetProperty(name,out var value)){if(create)throw Bad(path+"."+name,"Required finite positive drawing-length spacing.");return null;}
            if(value.ValueKind!=JsonValueKind.Number||!value.TryGetDouble(out double result)||!double.IsFinite(result)||result<=0||result>1e9)throw Bad(path+"."+name,"Use finite positive drawing-length spacing up to1e9mm.");
            return result;
        }
        var columns=Count("columns");var rows=Count("rows");var columnSpacing=Spacing("column_spacing");var rowSpacing=Spacing("row_spacing");
        if(create)
        {
            if(!input.TryGetProperty("base_point",out var point)||point.ValueKind!=JsonValueKind.Array||point.GetArrayLength()!=2||
                point.EnumerateArray().Any(value=>value.ValueKind!=JsonValueKind.Number||!value.TryGetDouble(out double number)||!double.IsFinite(number)||Math.Abs(number)>1e6))
                throw Bad(path+".base_point","Use an explicit WCSXY group base point in drawing millimeters.");
            return new CreateAssociativeRectangularArray431(handles,new(point[0].GetDouble(),point[1].GetDouble()),columns!.Value,rows!.Value,columnSpacing!.Value,rowSpacing!.Value);
        }
        double? axesAngle=null;Point3? position=null;
        if(input.TryGetProperty("axes_angle_degrees",out var angle))
        {
            if(angle.ValueKind!=JsonValueKind.Number||!angle.TryGetDouble(out double degrees)||!double.IsFinite(degrees)||degrees<=0||degrees>=180)
                throw Bad(path+".axes_angle_degrees","Use an angle strictly between0and180degrees so the two array axes are nonparallel.");
            axesAngle=degrees;
        }
        if(input.TryGetProperty("position_wcs",out var placement))
        {
            if(placement.ValueKind!=JsonValueKind.Array||placement.GetArrayLength()!=3||placement.EnumerateArray().Any(value=>value.ValueKind!=JsonValueKind.Number||!value.TryGetDouble(out double number)||!double.IsFinite(number)||Math.Abs(number)>1e6))
                throw Bad(path+".position_wcs","Use the explicit native array insertion position in WCS millimeters.");
            position=new(placement[0].GetDouble(),placement[1].GetDouble(),placement[2].GetDouble());
        }
        if(columns==null&&rows==null&&columnSpacing==null&&rowSpacing==null&&axesAngle==null&&position==null)throw Bad(path,"Supply at least one requested native count, spacing, axes angle or insertion-position change.");
        if(!input.TryGetProperty("array_token",out var token)||token.ValueKind!=JsonValueKind.String||token.GetString() is not {} text||text.Length!=64||text.Any(c=>!Uri.IsHexDigit(c)))
            throw Bad(path+".array_token","Read the current array_token from this actual native array root.");
        return new SetAssociativeRectangularArray431(handles,text.ToUpperInvariant(),columns,rows,columnSpacing,rowSpacing,axesAngle,position);
    }
    public static void Batch(IReadOnlyList<EditSpec> edits)
    {
        if(edits.Any(edit=>edit is CreateAssociativeRectangularArray431 or SetAssociativeRectangularArray431||AssociativeArrayValidation441.Handles(edit))&&edits.Count!=1)
            throw new DomainException("INVALID_OPERATION_ORDER","Use one native associative-array operation in its request; refresh its actual root/parameters before later editing.");
    }
}
