using System.Text.Json;
using System.Text.Json.Serialization;

namespace NanoCAD.Contracts;

public sealed record ProjectedDrawingView(string Direction, Point2 Origin);
public sealed record CreateDrawingViewsEdit(string SourceHandle, string NamePrefix, Point2 FrontOrigin, Point2? TopOrigin, double Scale, ProjectedDrawingView[] ProjectedViews, SketchPlaneSpec? ProjectionPlane = null, string[]? SourceHandles = null, string? SourceScope = null, [property: JsonIgnore(Condition=JsonIgnoreCondition.WhenWritingNull)] string? LayoutHandle = null)
    : StructureEdit("create_drawing_views");

public static class DrawingViewCreateValidation
{
    public static CreateDrawingViewsEdit Parse(JsonElement item, string path)
    {
        StructureWriteValidation.Shape(item,path,"operation","source_handle","source_handles","source_scope","name_prefix","front_origin","top_origin","scale","projected_views","projection_plane","layout_handle");
        bool single=item.TryGetProperty("source_handle",out _), multiple=item.TryGetProperty("source_handles",out var sourcesValue), whole=item.TryGetProperty("source_scope",out _);
        if((single?1:0)+(multiple?1:0)+(whole?1:0)!=1)
            throw StructureWriteValidation.Invalid(path,"Choose exactly one of source_handle, source_handles or source_scope.");
        var source=single?Validation.NormalizeHandle(StructureWriteValidation.Text(item,"source_handle",path,16)):"";
        string[]? sources=null;string? scope=null;
        if(multiple)
        {
            if(sourcesValue.ValueKind!=JsonValueKind.Array||sourcesValue.GetArrayLength()==0)
                throw StructureWriteValidation.Invalid(path+".source_handles","Provide a nonempty set of body handles.");
            if(sourcesValue.EnumerateArray().Any(v=>v.ValueKind!=JsonValueKind.String))
                throw StructureWriteValidation.Invalid(path+".source_handles","Each body handle must be a string.");
            sources=sourcesValue.EnumerateArray().Select(v=>Validation.NormalizeHandle(v.GetString()!)).OrderBy(v=>v,StringComparer.Ordinal).ToArray();
            if(sources.Distinct(StringComparer.Ordinal).Count()!=sources.Length)
                throw StructureWriteValidation.Invalid(path+".source_handles","Body handles must be distinct.");
        }
        if(whole)
        {
            scope=StructureWriteValidation.Text(item,"source_scope",path,32);
            if(scope!="all_model_solids")throw StructureWriteValidation.Invalid(path+".source_scope","Use all_model_solids for all top-level solid bodies in Model Space.");
        }
        var name=StructureWriteValidation.Text(item,"name_prefix",path,64);
        if(name!=name.Trim() || name.Contains("%<",StringComparison.Ordinal))throw StructureWriteValidation.Invalid(path,"Use a trimmed literal name prefix.");
        double Number(JsonElement value,double min,double max)
        {
            if(value.ValueKind!=JsonValueKind.Number || !value.TryGetDouble(out var n) || !double.IsFinite(n) || n<min || n>max)
                throw StructureWriteValidation.Invalid(path,"Numeric value exceeds the supported range.");
            return n==0?0:n;
        }
        Point2 Point(JsonElement value,string key)
        {
            if(value.ValueKind!=JsonValueKind.Array || value.GetArrayLength()!=2)throw StructureWriteValidation.Invalid(path+"."+key,"Expected [x,y] in WCS XY millimeters.");
            return new(Number(value[0],-1e6,1e6),Number(value[1],-1e6,1e6));
        }
        var front=Point(StructureWriteValidation.Required(item,"front_origin",path),"front_origin");
        Point2? top=item.TryGetProperty("top_origin",out var topValue)?Point(topValue,"top_origin"):null;
        if(top!=null && (front.X!=top.X || top.Y-front.Y<1e-3))throw StructureWriteValidation.Invalid(path,"Top view must be above the front view with the same X and at least 0.001 mm separation.");
        SketchPlaneSpec? plane=null;
        if(item.TryGetProperty("projection_plane",out var planeValue))
        {
            var pp=path+".projection_plane";
            StructureWriteValidation.Shape(planeValue,pp,"origin","x_axis","y_axis");
            Point3 Triple(string field)
            {
                var a=StructureWriteValidation.Required(planeValue,field,pp);
                if(a.ValueKind!=JsonValueKind.Array || a.GetArrayLength()!=3)throw StructureWriteValidation.Invalid(pp+"."+field,"Expected a WCS XYZ triple.");
                return new(Number(a[0],-1e6,1e6),Number(a[1],-1e6,1e6),Number(a[2],-1e6,1e6));
            }
            plane=new(Triple("origin"),Triple("x_axis"),Triple("y_axis"));
            double Dot(Point3 a,Point3 b)=>a.X*b.X+a.Y*b.Y+a.Z*b.Z;
            if(Math.Abs(Dot(plane.XAxis,plane.XAxis)-1)>1e-10 || Math.Abs(Dot(plane.YAxis,plane.YAxis)-1)>1e-10 || Math.Abs(Dot(plane.XAxis,plane.YAxis))>1e-10)
                throw StructureWriteValidation.Invalid(pp,"Projection axes must be perpendicular unit vectors; no implicit normalization.","INVALID_GEOMETRY");
        }
        var projected=new List<ProjectedDrawingView>();
        if(item.TryGetProperty("projected_views",out var array))
        {
            if(array.ValueKind!=JsonValueKind.Array || array.GetArrayLength()>7)throw StructureWriteValidation.Invalid(path+".projected_views","Expected at most seven unique projected directions.");
            var seen=new HashSet<string>(StringComparer.Ordinal);
            foreach(var view in array.EnumerateArray())
            {
                var vp=path+".projected_views["+projected.Count+"]";
                StructureWriteValidation.Shape(view,vp,"direction","origin");
                var direction=StructureWriteValidation.Text(view,"direction",vp,32);
                var origin=Point(StructureWriteValidation.Required(view,"origin",vp),"projected_views.origin");
                var dx=origin.X-front.X;var dy=origin.Y-front.Y;
                bool valid=direction switch
                {
                    "bottom"=>dx==0 && dy<=-0.001,
                    "left"=>dx<=-0.001 && dy==0,
                    "right"=>dx>=0.001 && dy==0,
                    "iso_top_left"=>dx<=-0.001 && dy>=0.001,
                    "iso_top_right"=>dx>=0.001 && dy>=0.001,
                    "iso_bottom_left"=>dx<=-0.001 && dy<=-0.001,
                    "iso_bottom_right"=>dx>=0.001 && dy<=-0.001,
                    _=>false
                };
                if(!valid || !seen.Add(direction))throw StructureWriteValidation.Invalid(vp,"Use a unique supported direction with an origin aligned to Front or in its matching isometric quadrant (0.001 mm minimum separation).");
                projected.Add(new(direction,origin));
            }
        }
        // Canonical order also makes equivalent sets replay the same payload.
        return new(source,name,front,top,Number(StructureWriteValidation.Required(item,"scale",path),0.001,1000),projected.OrderBy(v=>v.Direction,StringComparer.Ordinal).ToArray(),plane,sources,scope,item.TryGetProperty("layout_handle",out _)?Validation.NormalizeHandle(StructureWriteValidation.Text(item,"layout_handle",path,16)):null);
    }
    public static void ValidateTargetSpace(IReadOnlyList<StructureEdit> edits,string targetSpace)
    {
        foreach(var view in edits.OfType<CreateDrawingViewsEdit>())
            if((view.LayoutHandle!=null)!=(targetSpace=="paper"))
                throw StructureWriteValidation.Invalid("$.target_space","Native Paper view creation requires target_space paper and an explicit active layout_handle. Omit layout_handle for the existing Model route.");
    }
    public static void ValidateBatch(IReadOnlyList<StructureEdit> edits)
    {
        if(edits.Any(x=>x is CreateDrawingViewsEdit) && (edits.Count!=1 || edits[0] is not CreateDrawingViewsEdit))
            throw StructureWriteValidation.Invalid("$.operations","Drawing-view creation must be the sole operation in its request.","INVALID_OPERATION_ORDER");
    }
}
