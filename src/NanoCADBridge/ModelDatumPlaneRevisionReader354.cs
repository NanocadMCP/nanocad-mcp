using Multicad;
using Multicad.Mc3D;
using NanoCAD.Contracts;
using Db=Teigha.DatabaseServices;

namespace NanoCADBridge;

internal static class ModelDatumPlaneRevisionReader354
{
    internal static Mc3dSolid Body(string handle,DocumentState state,Db.Transaction tx,Action cancel,bool forWrite)
    {
        var drawing=ModelFeatureReorderState345.Scope(state,tx);var representation=CadStructureWriter.Find(state.Document.Database,handle);var raw=tx.GetObject(representation,Db.OpenMode.ForRead);
        ModelDatumPlaneRevisionState354.Need(raw.GetType()==typeof(Db.Solid3d)&&raw.OwnerId==ModelFeatureReorderState345.Model(state.Document.Database,tx),"OUT_OF_SCOPE","Select the exact current top-level native part.");
        if(forWrite)
        {
            var layer=(Db.LayerTableRecord)tx.GetObject(((Db.Entity)raw).LayerId,Db.OpenMode.ForRead);
            ModelDatumPlaneRevisionState354.Need(!layer.IsLocked&&!layer.IsOff&&!layer.IsFrozen&&!layer.IsDependent,"LAYER_UNAVAILABLE","The retained part layer is unavailable for this native revision.");
        }
        var id=McObjectId.FromHandle(representation.Handle.Value,drawing.ID);var body=id.IsNull?null:id.GetObjectOfType<Mc3dSolid>();
        ModelDatumPlaneRevisionState354.Need(body!=null&&body.ID==id&&ModelHistoryEditGuard.CurrentRepresentation(body,state,tx,cancel)==representation,"OBJECT_IDENTITY_MISMATCH","The selected representation does not match one actual current native part.");return body;
    }
    internal static ModelDatumPlaneRevisionRead354 Describe(ModelDatumPlaneRevisionState354.Snapshot value,string bridge,string document)
        =>new(new EntityRef(bridge,document,Validation.NormalizeHandle(value.Part.Representation.Handle.ToString())),value.Part.Body.ID.ToString(),value.Part.Body.LastPartItemID.ToString(),value.Token,
            value.Datum.Read,value.Mirrors,value.Downstream,value.Part.Parameters.GetProperty("native"),"native_fixed_oriented_plane_and_same_part_sketch_profile_feature_or_mirror_consumers; owned_inputs_in_actual_local_frame; source_links_and_source_geometry_retained; generated_edge_markers_and_private_solver_internals_not_durable_identity",value.Sketches,value.DirectConsumers);
    internal static ModelDatumPlaneRevisionRead354[] Read(string bodyHandle,string[] planeIds,Arguments args,DocumentState state,Db.Transaction tx,Action cancel)
    {
        var body=Body(bodyHandle,state,tx,cancel,false);var owner=ModelProfileDiscovery348.OwnerDocument(body,state);
        var readArgs=new ModifyArguments(args.BridgeId!,args.DocumentId!,args.ExpectedRevision!,"readonly_datum_revision_metadata","Millimeters",[]);
        var result=new List<ModelDatumPlaneRevisionRead354>();
        foreach(string id in planeIds){cancel();var plane=ModelDatumPlaneState353.Resolve(id,owner,cancel);result.Add(Describe(ModelDatumPlaneRevisionState354.Capture(body,plane,readArgs,state,tx,cancel),args.BridgeId!,args.DocumentId!));}
        return result.ToArray();
    }
}
