using System.Text.Json;
using Multicad;
using Multicad.DatabaseServices;
using Multicad.Mc3D;
using NanoCAD.Contracts;

namespace NanoCADBridge;

internal static class ModelSketchFrame
{
    static void Need(bool ok,string message){if(!ok)throw new DomainException("INVALID_SKETCH_REFERENCE",message);}
    static (McGeomParam Origin,McGeomParam Axis) References(McObjectId first,McObjectId second,JsonElement d)
    {
        using var g1=Service.GetFEVGeometry(first);using var g2=Service.GetFEVGeometry(second);
        using var line1=g1?.LineSeg;using var line2=g2?.LineSeg;
        Need(line1!=null&&line2!=null,"Frame references require current straight native edges.");
        var point=d.GetProperty("origin_point").GetString() switch{"start"=>line1!.StartPoint,"end"=>line1!.EndPoint,_=>line1!.MidPoint};
        var origin=new McGeomParam();origin.Init(first,point);
        var axis=new McGeomParam();axis.Init(second,line2!.MidPoint);
        Need(!origin.IsNull&&origin.ID==first&&!axis.IsNull&&axis.ID==second,"Native frame references are unavailable.");
        return(origin,axis);
    }
    internal static void Preflight(PlanarSketch sketch,McObjectId first,McObjectId second,JsonElement d)
    {
        var refs=References(first,second,d);
        Need(sketch.SetAssocCoordSystem(refs.Origin,refs.Axis,new McGeomParam(),d.GetProperty("axis_is_x").GetBoolean(),d.GetProperty("reverse_axis").GetBoolean(),true),
            "Native sketch rejects these references: select independent upstream edges belonging to the same part and an axis projected into its plane.");
    }
    internal static void Apply(PlanarSketch sketch,McObjectId first,McObjectId second,JsonElement d,HashSet<McObjectId> created)
    {
        var refs=References(first,second,d);var predecessor=sketch.OrderObjBefore;
        var point=new WorkPoint();McObjectManager.Add2Document(point.DbObject);created.Add(point.ID);
        Need(point.SetDefinition(WPTDefinitionType.PointRef,new[]{refs.Origin}),"Associative work point was rejected.");
        Need(point.SetOrderObjBefore(predecessor,false),"Work point could not enter the part history.");
        var axis=new WorkAxis();McObjectManager.Add2Document(axis.DbObject);created.Add(axis.ID);
        Need(axis.SetDefinition(WADefinitionType.Axis,new[]{refs.Axis}),"Associative work axis was rejected.");
        Need(axis.SetOrderObjBefore(point.ID,false),"Work axis could not enter the part history.");
        Need(sketch.SetOrderObjBefore(axis.ID,false),"Sketch order could not retain its new references.");
        point.DbEntity.Update();axis.DbEntity.Update();McObjectManager.UpdateAll();
        var originRef=new McGeomParam();originRef.Init(point.ID,point.Position);
        using var line=axis.PositionAsLineSeg;var axisRef=new McGeomParam();axisRef.Init(axis.ID,line.MidPoint);
        Need(!originRef.IsNull&&!axisRef.IsNull&&sketch.SetAssocCoordSystem(originRef,axisRef,new McGeomParam(),d.GetProperty("axis_is_x").GetBoolean(),d.GetProperty("reverse_axis").GetBoolean(),false),"Sketch did not accept the associative point and axis.");
        sketch.DbEntity.Update();McObjectManager.UpdateAll();
        var dependencies=sketch.GetDependsOn(false,false);
        Need(dependencies!=null&&dependencies.Contains(point.ID)&&dependencies.Contains(axis.ID),"Sketch did not retain both frame dependencies.");
    }
}
