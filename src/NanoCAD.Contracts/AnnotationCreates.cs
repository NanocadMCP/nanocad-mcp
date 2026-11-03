using System.Text.Json;

namespace NanoCAD.Contracts;

public sealed record MLeaderSpec(Point2[] Vertices, Point2 TextPosition, string Text, double TextHeight, string TextStyle, string MLeaderStyle, string LayerName) : EntitySpec("MLeader", LayerName);
public sealed record HatchHole(Point2[]? Vertices=null,Point2? Center=null,double Radius=0);
public sealed record HatchSpec(Point2[] Boundary, string LayerName, string PatternName="SOLID", double PatternAngle=0, double PatternScale=1,HatchHole[]? Holes=null) : EntitySpec("Hatch", LayerName);
public sealed record AssociativeHatchSpec(string BoundaryHandle,string LayerName,string[][]? BoundaryGroups=null,
    string PatternName="SOLID",double PatternAngle=0,double PatternScale=1,Point2? OriginOcs=null,string HatchStyle="normal") : EntitySpec("Hatch",LayerName)
{
    public string[][] Groups()=>BoundaryGroups??[[BoundaryHandle]];
    public string[] SourceHandles()=>Groups().SelectMany(group=>group).ToArray();
}
public sealed record TableSpec(Point2 Position, string[][] Cells, double RowHeight, double ColumnWidth, string TableStyle, string LayerName) : EntitySpec("Table", LayerName);

public static partial class CreateValidation
{
    private static Point2[] Points(JsonElement item, string name, string path, int minimum)
    {
        var values = Required(item, name, path);
        if (values.ValueKind != JsonValueKind.Array || values.GetArrayLength() < minimum) throw Invalid(path, $"Expected at least {minimum} points.");
        return values.EnumerateArray().Select(v => v.ValueKind == JsonValueKind.Array && v.GetArrayLength() == 2 ? new Point2(Number(v[0], path), Number(v[1], path)) : throw Invalid(path, "Expected XY point.")).ToArray();
    }
    private static EntitySpec ParseAnnotation(JsonElement item, string type, string path, string layer)
    {
        if (type == "MLeader")
        {
            Shape(item, path, "type", "layer", "vertices", "text_position", "text", "text_height", "text_style", "mleader_style");
            var vertices = Points(item, "vertices", path, 2);
            for (int i = 1; i < vertices.Length; i++) if (Coincident(vertices[i-1], vertices[i])) throw Invalid(path, "Zero-length leader segment.", "INVALID_GEOMETRY");
            return new MLeaderSpec(vertices, Point(item, "text_position", path), Text(item, "text", path, 4096), Number(Required(item, "text_height", path), path, 1e-6),
                Text(item, "text_style", path, 255, "Standard"), Text(item, "mleader_style", path, 255), layer);
        }
        if (type == "Table")
        {
            Shape(item, path, "type", "layer", "position", "cells", "row_height", "column_width", "table_style");
            var rows = Required(item, "cells", path);
            if (rows.ValueKind != JsonValueKind.Array || rows.GetArrayLength() < 1) throw Invalid(path, "Expected a nonempty table row array.");
            int columns = -1; var cells = new List<string[]>();
            foreach (var row in rows.EnumerateArray())
            {
                if (row.ValueKind != JsonValueKind.Array || row.GetArrayLength() < 1 || columns != -1 && row.GetArrayLength() != columns) throw Invalid(path, "Table rows must have the same nonzero column count.");
                columns = row.GetArrayLength(); var values = new List<string>();
                foreach (var cell in row.EnumerateArray())
                {
                    if (cell.ValueKind != JsonValueKind.String) throw Invalid(path, "Expected literal table text.");
                    var text = cell.GetString()!;
                    if (text.Length > 4096 || text.Any(char.IsControl) || text.Contains("%<")) throw Invalid(path, "Unsupported table text or field expression.");
                    values.Add(text);
                }
                cells.Add(values.ToArray());
            }
            return new TableSpec(Point(item,"position",path), cells.ToArray(), Number(Required(item,"row_height",path),path,1e-6),
                Number(Required(item,"column_width",path),path,1e-6), Text(item,"table_style",path,255),layer);
        }
        if(item.TryGetProperty("boundary_handle",out var reference)||item.TryGetProperty("boundary_groups",out _))
        {
            Shape(item,path,"type","layer","boundary_handle","boundary_groups","pattern_name","pattern_angle","pattern_scale","origin_ocs","hatch_style");
            bool grouped=item.TryGetProperty("boundary_groups",out var groupValues);
            if(grouped&&item.TryGetProperty("boundary_handle",out _))throw Invalid(path,"Use either boundary_handle or boundary_groups, not both.");
            var groups=new List<string[]>();var seen=new HashSet<string>(StringComparer.Ordinal);
            if(grouped)
            {
                if(groupValues.ValueKind!=JsonValueKind.Array||groupValues.GetArrayLength()==0)throw Invalid(path+".boundary_groups","Expected a nonempty outer group followed by optional hole groups.");
                foreach(var group in groupValues.EnumerateArray())
                {
                    if(group.ValueKind!=JsonValueKind.Array||group.GetArrayLength()==0)throw Invalid(path+".boundary_groups","Each loop requires a nonempty group of existing curve handles.");
                    var handles=new List<string>();
                    foreach(var handle in group.EnumerateArray())
                    {
                        if(handle.ValueKind!=JsonValueKind.String)throw Invalid(path+".boundary_groups","Expected native hex handles.");
                        string id=Validation.NormalizeHandle(handle.GetString()!);if(!seen.Add(id))throw Invalid(path,"A source can occur in only one requested loop.");handles.Add(id);
                    }
                    groups.Add(handles.ToArray());
                }
            }
            else
            {
                if(reference.ValueKind!=JsonValueKind.String)throw Invalid(path,"Expected a source handle.");
                groups.Add([Validation.NormalizeHandle(reference.GetString()!)]);
            }
            string name=Text(item,"pattern_name",path,255,"SOLID");
            if(name.IndexOfAny(['\\','/',':'])>=0)throw Invalid(path+".pattern_name","Use an installed native predefined pattern name, not a file path.");
            double nativeAngle=item.TryGetProperty("pattern_angle",out var angleValue)?Number(angleValue,path+".pattern_angle",0,Math.PI*2):0;
            if(nativeAngle>=Math.PI*2)throw Invalid(path+".pattern_angle","Angle must be below 2pi.");
            double nativeScale=item.TryGetProperty("pattern_scale",out var scaleValue)?Number(scaleValue,path+".pattern_scale",double.Epsilon,Maximum):1;
            Point2? origin=item.TryGetProperty("origin_ocs",out _)?Point(item,"origin_ocs",path):null;
            string island=Text(item,"hatch_style",path,16,"normal");if(island is not ("normal" or "outer" or "ignore"))throw Invalid(path+".hatch_style","Use normal, outer or ignore.");
            return new AssociativeHatchSpec(groups[0][0],layer,grouped?groups.ToArray():null,name,nativeAngle,nativeScale,origin,island);
        }
        Shape(item, path, "type", "layer", "boundary", "holes", "pattern_name", "pattern_angle", "pattern_scale");
        var polygon = Points(item,"boundary",path,3);
        ValidateSimplePolygon(polygon,path);
        var pattern=Text(item,"pattern_name",path,16,"SOLID");
        if(pattern is not ("SOLID" or "ANSI31"))throw Invalid(path,"Only SOLID and bounded predefined ANSI31 are supported.");
        double angle=item.TryGetProperty("pattern_angle",out var a)?Number(a,path+".pattern_angle",0,Math.PI*2):0;
        double scale=item.TryGetProperty("pattern_scale",out var s)?Number(s,path+".pattern_scale",0.01,100):1;
        if(angle>=Math.PI*2||pattern=="SOLID"&&(angle!=0||scale!=1))throw Invalid(path,"Angle must be below2pi; SOLID requires angle0 and scale1.");
        if(pattern=="ANSI31"&&(polygon.Max(p=>p.X)-polygon.Min(p=>p.X)>100000||polygon.Max(p=>p.Y)-polygon.Min(p=>p.Y)>100000))
            throw Invalid(path,"Patterned hatch envelope exceeds100000 mm.","INVALID_GEOMETRY");
        HatchHole[]? holes=null;
        if(item.TryGetProperty("holes",out var holeValues))
        {
            if(holeValues.ValueKind!=JsonValueKind.Array||holeValues.GetArrayLength()<1)throw Invalid(path,"Expected at least one hole.");
            holes=holeValues.EnumerateArray().Select(h=>
            {
                string kind=Text(h,"kind",path,16);
                if(kind=="polygon"){Shape(h,path,"kind","vertices");return new HatchHole(Points(h,"vertices",path,3));}
                if(kind=="circle"){Shape(h,path,"kind","center","radius");return new HatchHole(Center:Point(h,"center",path),Radius:Number(Required(h,"radius",path),path,1e-6,1e9));}
                throw Invalid(path,"Hole kind must be polygon or circle.");
            }).ToArray();
            HatchRegionValidation.Validate(polygon,holes);
        }
        return new HatchSpec(polygon,layer,pattern,angle,scale,holes);
    }
    private static void ValidateSimplePolygon(Point2[] polygon,string path,Action? cancel=null)
    {
        double Cross(Point2 a,Point2 b,Point2 c) => (b.X-a.X)*(c.Y-a.Y)-(b.Y-a.Y)*(c.X-a.X);
        bool On(Point2 a,Point2 b,Point2 p) => p.X >= Math.Min(a.X,b.X) && p.X <= Math.Max(a.X,b.X) && p.Y >= Math.Min(a.Y,b.Y) && p.Y <= Math.Max(a.Y,b.Y);
        double area = 0;
        for (int i=0;i<polygon.Length;i++)
        {
            cancel?.Invoke();
            var a=polygon[i]; var b=polygon[(i+1)%polygon.Length];
            if (Coincident(a,b)) throw Invalid(path,"Repeated polygon vertex.","INVALID_GEOMETRY");
            area += a.X*b.Y-a.Y*b.X;
            for(int j=i+1;j<polygon.Length;j++)
            {
                cancel?.Invoke();
                if(j==i+1 || i==0 && j==polygon.Length-1) continue;
                var c=polygon[j]; var d=polygon[(j+1)%polygon.Length];
                double x=Cross(a,b,c),y=Cross(a,b,d),z=Cross(c,d,a),w=Cross(c,d,b);
                if(x*y<0 && z*w<0 || x==0 && On(a,b,c) || y==0 && On(a,b,d) || z==0 && On(c,d,a) || w==0 && On(c,d,b))
                    throw Invalid(path,"Self-intersecting polygon boundary.","INVALID_GEOMETRY");
            }
        }
        if(Math.Abs(area)<1e-12) throw Invalid(path,"Zero-area polygon boundary.","INVALID_GEOMETRY");
    }
}

