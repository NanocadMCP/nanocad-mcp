using System.Text.Json;
using static NanoCAD.Contracts.StructureWriteValidation;

namespace NanoCAD.Contracts;

public sealed record BlockMemberGeometry388(string Handle,string Type,Point3? Start,Point3? End,Point3? Center,double? Radius,
    double? StartAngle=null,double? SweepAngle=null,EllipseSpec? Ellipse=null,SplineSpec? Spline=null,PolylineShape? Polyline=null);
public sealed record SetBlockDefinitionGeometry388(string DefinitionHandle,string ExpectedDefinitionToken,BlockMemberGeometry388[] Members)
    : StructureEdit("set_block_definition_geometry");

public static class BlockDefinitionGeometryValidation388
{
    public static SetBlockDefinitionGeometry388 Parse(JsonElement value,string path)
    {
        Shape(value,path,"operation","definition_handle","expected_definition_token","members");
        if(Text(value,"operation",path,48)!="set_block_definition_geometry")throw Invalid(path+".operation","Expected set_block_definition_geometry.");
        string handle=Validation.NormalizeHandle(Text(value,"definition_handle",path,16));
        string token=Text(value,"expected_definition_token",path,64);
        if(token.Length!=64||token.Any(c=>!"0123456789ABCDEF".Contains(c)))throw Invalid(path+".expected_definition_token","Read the exact current definition token from inspect_block.");
        var input=Required(value,"members",path);
        if(input.ValueKind!=JsonValueKind.Array||input.GetArrayLength()==0)throw Invalid(path+".members","Supply existing definition members to edit.");
        var edits=new List<BlockMemberGeometry388>();var used=new HashSet<string>(StringComparer.OrdinalIgnoreCase);int index=0;
        foreach(var item in input.EnumerateArray())
        {
            string at=path+".members["+index+++"]";Shape(item,at,"handle","geometry");
            string id=Validation.NormalizeHandle(Text(item,"handle",at,16));
            if(!used.Add(id))throw Invalid(at+".handle","Each existing member may appear once.");
            var geometry=Required(item,"geometry",at);string type=Text(geometry,"type",at+".geometry",16);
            Point3 Point(string name)
            {
                var v=Required(geometry,name,at+".geometry");
                if(v.ValueKind!=JsonValueKind.Array||v.GetArrayLength()!=3)throw Invalid(at+".geometry."+name,"Use three definition-local coordinates.");
                double Number(JsonElement number)=>number.ValueKind==JsonValueKind.Number&&number.TryGetDouble(out double n)&&double.IsFinite(n)&&Math.Abs(n)<=1e9?n:throw Invalid(at+".geometry."+name,"Coordinates must be finite and within +/-1e9.");
                var point=new Point3(Number(v[0]),Number(v[1]),Number(v[2]));
                if(Math.Abs(point.Z)>1e-9)throw Invalid(at+".geometry."+name,"This ordinary2D definition route requires local XY coordinates, third coordinate zero.");
                return point;
            }
            if(type=="Line")
            {
                Shape(geometry,at+".geometry","type","start","end");var start=Point("start");var end=Point("end");
                if(Math.Sqrt(Math.Pow(end.X-start.X,2)+Math.Pow(end.Y-start.Y,2)+Math.Pow(end.Z-start.Z,2))<=1e-9)throw Invalid(at+".geometry","Line endpoints must be distinct.");
                edits.Add(new(id,type,start,end,null,null));
            }
            else if(type=="Circle")
            {
                Shape(geometry,at+".geometry","type","center","radius");var radius=Required(geometry,"radius",at+".geometry");
                if(radius.ValueKind!=JsonValueKind.Number||!radius.TryGetDouble(out double number)||!double.IsFinite(number)||number<1e-9||number>1e9)throw Invalid(at+".geometry.radius","Radius must be finite and positive within native drawing bounds.");
                edits.Add(new(id,type,null,null,Point("center"),number));
            }
            else if(type=="Arc")
            {
                Shape(geometry,at+".geometry","type","center","radius","start_angle","sweep_angle");
                double Number(string key,double low,double high)
                {var v=Required(geometry,key,at+".geometry");if(v.ValueKind!=JsonValueKind.Number||!v.TryGetDouble(out double n)||!double.IsFinite(n)||n<low||n>high)throw Invalid(at+".geometry."+key,"Expected a finite value in the documented range.");return n;}
                edits.Add(new(id,type,null,null,Point("center"),Number("radius",1e-9,1e9),ContourGeometry.Angle(Number("start_angle",-2*Math.PI,2*Math.PI)),Number("sweep_angle",1e-9,2*Math.PI-1e-9)));
            }
            else if(type=="Ellipse"||type=="Spline")
            {
                // The existing creator grammar validates complete native axes
                // and NURBS data; only definition-local XYZ is adapted to XY.
                var values=geometry.EnumerateObject().ToDictionary(p=>p.Name,p=>p.Value.Clone(),StringComparer.Ordinal);
                if(values.ContainsKey("layer")||values.ContainsKey("workplane"))throw Invalid(at+".geometry","Display properties and the local definition plane are retained.");
                JsonElement Pair(Point3 point)=>JsonSerializer.SerializeToElement(new[]{point.X,point.Y});
                if(type=="Ellipse")
                {values["center"]=Pair(Point("center"));values["major_axis"]=Pair(Point("major_axis"));}
                else
                {
                    Required(geometry,"closed",at+".geometry");var array=Required(geometry,"control_points",at+".geometry");
                    if(array.ValueKind!=JsonValueKind.Array)throw Invalid(at+".geometry.control_points","Supply complete definition-local control points.");
                    var points=new List<double[]>();foreach(var valuePoint in array.EnumerateArray())
                    {
                        if(valuePoint.ValueKind!=JsonValueKind.Array||valuePoint.GetArrayLength()!=3||valuePoint[0].ValueKind!=JsonValueKind.Number||valuePoint[1].ValueKind!=JsonValueKind.Number||valuePoint[2].ValueKind!=JsonValueKind.Number||!valuePoint[0].TryGetDouble(out double x)||!valuePoint[1].TryGetDouble(out double y)||!valuePoint[2].TryGetDouble(out double z)||!double.IsFinite(x)||!double.IsFinite(y)||!double.IsFinite(z)||Math.Abs(z)>1e-9)
                            throw Invalid(at+".geometry.control_points","Use finite definition-local [x,y,0] points.");
                        points.Add(new[]{x,y});
                    }
                    values["control_points"]=JsonSerializer.SerializeToElement(points);
                }
                var entity=CreateValidation.ParseEntities(JsonSerializer.SerializeToElement(new[]{values}),at+".geometry",allowCurves:true).Single();
                edits.Add(new(id,type,null,null,null,null,Ellipse:entity as EllipseSpec,Spline:entity as SplineSpec));
            }
            else if(type=="Polyline")
            {
                Shape(geometry,at+".geometry","type","closed","vertices");var closed=Required(geometry,"closed",at+".geometry");
                if(closed.ValueKind is not JsonValueKind.True and not JsonValueKind.False)throw Invalid(at+".geometry.closed","Expected Boolean.");
                var vertices=Required(geometry,"vertices",at+".geometry");if(vertices.ValueKind!=JsonValueKind.Array)throw Invalid(at+".geometry.vertices","Expected explicit vertices with curvature and widths.");
                var result=new List<PolylineShapeVertex>();foreach(var vertex in vertices.EnumerateArray())
                {
                    string vp=at+".geometry.vertices["+result.Count+"]";Shape(vertex,vp,"position","bulge","start_width","end_width");
                    var pos=Required(vertex,"position",vp);if(pos.ValueKind!=JsonValueKind.Array||pos.GetArrayLength()!=3||pos[0].ValueKind!=JsonValueKind.Number||pos[1].ValueKind!=JsonValueKind.Number||pos[2].ValueKind!=JsonValueKind.Number||!pos[0].TryGetDouble(out double x)||!pos[1].TryGetDouble(out double y)||!pos[2].TryGetDouble(out double z)||!double.IsFinite(x)||!double.IsFinite(y)||!double.IsFinite(z)||Math.Abs(z)>1e-9)throw Invalid(vp+".position","Use definition-local [x,y,0].");
                    double Number(string key,double low,double high){var v=Required(vertex,key,vp);if(v.ValueKind!=JsonValueKind.Number||!v.TryGetDouble(out double n)||!double.IsFinite(n)||n<low||n>high)throw Invalid(vp+"."+key,"Expected finite explicit curvature/width.");return n;}
                    result.Add(new(new(x,y),Number("bulge",-1e6,1e6),Number("start_width",0,1e6),Number("end_width",0,1e6)));
                }
                var shape=new PolylineShape(closed.GetBoolean(),result.ToArray());PolylineTopologyGeometry.Validate(shape);edits.Add(new(id,type,null,null,null,null,Polyline:shape));
            }
            else throw Invalid(at+".geometry.type","Supported direct definition members: Line, Circle, Arc, Polyline, Ellipse, Spline.");
        }
        return new(handle,token,edits.ToArray());
    }
    public static void Batch(IReadOnlyList<StructureEdit> edits)
    {
        if(edits.Any(e=>e is SetBlockDefinitionGeometry388)&&edits.Count!=1)throw Invalid("operations","Edit one shared definition in a separate structural request.","INVALID_OPERATION_ORDER");
    }
}
