using System.Text.Json;
using Multicad;
using Multicad.DatabaseServices;
using Multicad.Mc3D;
using NanoCAD.Contracts;

namespace NanoCADBridge;

// A native datum remains the sketch's support. No coordinate regeneration is
// used to imitate the dependency when the datum offset changes.
internal static class ModelSketchSupport
{
    static void Need(bool ok,string message){if(!ok)throw new DomainException("MODEL_UPDATE_FAILED",message);}
    internal static void Apply(PlanarSketch sketch,JsonElement definition,HashSet<McObjectId> created)
    {
        if(definition.TryGetProperty("update_support_id",out var expected)){
            var current=sketch.PlanarEntityID;
            Need(!current.IsNull&&current.ToString().Equals(expected.GetString(),StringComparison.OrdinalIgnoreCase),"The sketch support changed; read current history.");
            var existing=current.GetObjectOfType<WorkPlane>();
            Need(existing!=null&&existing.DefinitionType==WPLDefinitionType.PlaneNOffset,"Only an existing offset support can be edited by this route.");
            // The native parameter is relative to the datum's retained definition
            // frame, not necessarily to its referenced parent's geometric plane.
            // SetDefinition can rebase that frame. Preserve it and change only the
            // documented scalar by the requested geometric displacement.
            var actual=Offset(existing);var parameter=existing!.ParamVal;
            Need(actual.HasValue&&double.IsFinite(parameter),"The current support offset is unavailable.");
            existing.ParamVal=parameter+definition.GetProperty("offset").GetDouble()-actual!.Value;
            existing.DbEntity.Update();return;
        }
        string name=definition.GetProperty("reference_plane").GetString()!;
        var datum=McDocumentsManager.GetActiveSheet().Get3dHistory().GetGCSElement(name switch{
            "XY"=>GCSElementType.WPL_XY,"YZ"=>GCSElementType.WPL_YZ,_=>GCSElementType.WPL_ZX}) as WorkPlane;
        Need(datum!=null,"The document reference plane is unavailable.");created.Add(datum!.ID);
        // Rebinding creates a distinct datum; a shared user plane is never edited.
        var plane=new WorkPlane();McObjectManager.Add2Document(plane.DbObject);created.Add(plane.ID);
        datum!.DbEntity.Update();McObjectManager.UpdateAll();
        using var position=datum.Position;
        var source=new McGeomParam();source.Init(datum.ID,position.PointOnPlane);
        Need(!source.IsNull&&source.ID==datum.ID,"Reference plane binding has no matching native target.");
        var offset=new McGeomParam{Param=definition.GetProperty("offset").GetDouble()};
        Need(plane.SetDefinition(WPLDefinitionType.PlaneNOffset,new[]{source,offset}),"Native offset plane definition was rejected.");
        plane.DbEntity.Update();McObjectManager.UpdateAll();
        sketch.PlanarEntityID=plane.ID;sketch.DbEntity.Update();
    }
    static double? Offset(WorkPlane? plane)
    {
        if(plane==null||plane.DefinitionType!=WPLDefinitionType.PlaneNOffset)return null;
        var refs=plane.GetDefinitionParams().ToArray();
        var parent=refs.Length==2&&!refs[0].IsNull?refs[0].ID.GetObjectOfType<WorkPlane>():null;
        if(parent==null)return null;
        using var baseline=parent.Position;using var actual=plane.Position;
        return baseline.SignedDistanceTo(actual.PointOnPlane);
    }
    internal static bool Verify(PlanarSketch sketch,JsonElement d)
    {
        var plane=sketch.PlanarEntityID.IsNull?null:sketch.PlanarEntityID.GetObjectOfType<WorkPlane>();
        return plane!=null&&plane.DefinitionType==WPLDefinitionType.PlaneNOffset&&
            Offset(plane) is double value&&Math.Abs(value-d.GetProperty("offset").GetDouble())<1e-7;
    }
    internal static object Read(PlanarSketch sketch)
    {
        var support=sketch.PlanarEntityID;
        var plane=support.IsNull?null:support.GetObjectOfType<WorkPlane>();
        return new{kind="sketch",support_id=support.IsNull?null:support.ToString(),
            support_definition=plane?.DefinitionType.ToString(),support_offset=Offset(plane),
            support_parameter_id=plane==null||plane.ParamVarID.IsNull?null:plane.ParamVarID.ToString()};
    }
}
