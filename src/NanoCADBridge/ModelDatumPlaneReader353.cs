using System.Text.Json;
using Multicad;
using Multicad.DatabaseServices;
using Multicad.Mc3D;
using NanoCAD.Contracts;
using Db=Teigha.DatabaseServices;

namespace NanoCADBridge;

internal static class ModelDatumPlaneReader353
{
    internal static ModelDatumPlaneDiscovery353 Read(DocumentState state,Db.Transaction tx,Action cancel,
        bool includeInventory,string? cursor,string[]? planeIds,string[] bodyHandles)
    {
        if(!includeInventory&&(planeIds==null||planeIds.Length==0))return new(null,[]);
        var drawing=ModelProfileDiscovery348.DrawingDocument(state);var documents=new Dictionary<McObjectId,McDocument>();var bodies=new List<Mc3dSolid>();
        foreach(string handle in bodyHandles)
        {
            cancel();var raw=CadStructureWriter.Find(state.Document.Database,handle);var entity=tx.GetObject(raw,Db.OpenMode.ForRead);
            ModelDatumPlaneState353.Need(entity.GetType()==typeof(Db.Solid3d)&&entity.OwnerId==ModelFeatureReorderState345.Model(state.Document.Database,tx),
                "OUT_OF_SCOPE","Native datum scope requires actual top-level part representations.");
            var id=McObjectId.FromHandle(raw.Handle.Value,drawing.ID);var body=id.IsNull?null:id.GetObjectOfType<Mc3dSolid>();
            ModelDatumPlaneState353.Need(body!=null&&body.ID==id&&ModelHistoryEditGuard.CurrentRepresentation(body,state,tx,cancel)==raw,
                "OBJECT_IDENTITY_MISMATCH","The selected current native part identity is unavailable.");
            var owner=ModelProfileDiscovery348.OwnerDocument(body,state);documents[owner.ID]=owner;bodies.Add(body);
        }
        ModelDatumPlaneState353.Need(documents.Count==1,"DATUM_DOCUMENT_SCOPE_AMBIGUOUS","Choose current bodies sharing the intended plane's actual native owner document. Ordinary multiple-body history remains available without optional datum discovery.");
        var document=documents.Values.Single();ModelDatumPlaneInventory353? inventory=null;
        if(includeInventory)
        {
            var items=new List<ModelDatumPlaneIdentity353>();int used=2;bool complete=true;
            foreach(var id in ModelProfileDiscovery348.Ids<WorkPlane>(document,cancel))
            {
                cancel();if(cursor!=null&&StringComparer.OrdinalIgnoreCase.Compare(id.ToString(),cursor)<=0)continue;
                var plane=ModelDatumPlaneState353.Resolve(id.ToString(),document,cancel);var item=new ModelDatumPlaneIdentity353(id.ToString(),plane.Name,plane.DefinitionType.ToString(),ModelDatumPlaneState353.Id(plane.HostSolidID));
                int size=JsonSerializer.SerializeToUtf8Bytes(item,Wire.Json).Length+1;if(used+size>128*1024&&items.Count>0){complete=false;break;}used+=size;items.Add(item);
            }
            inventory=new(document.ID.ToString(),"same_actual_native_owner_document; identities_are_not_body_composition",items.ToArray(),complete,complete?null:items[^1].plane_id);
        }
        var details=new List<ModelDatumPlaneRead353>();var bytes=new ModelInventoryBudget(cancel);
        foreach(string nativeId in planeIds??[])
        {
            cancel();var plane=ModelDatumPlaneState353.Resolve(nativeId,document,cancel);var body=bodies.FirstOrDefault(value=>value.ID==plane.HostSolidID)??bodies[0];
            var detail=ModelDatumPlaneState353.Capture(plane,body,state,tx,cancel).Read;bytes.Add(detail);details.Add(detail);
        }
        // CadReader measures the actual whole history+inventory+detail response.
        return new(inventory,details.ToArray());
    }
}
