using Multicad;
using Multicad.DatabaseServices;
using NanoCAD.Contracts;
using Db=Teigha.DatabaseServices;

namespace NanoCADBridge;

// ACAD variables are persisted AssocVariable actions, independent of a default
// XY constraint group. Never initialize native metadata from a read.
internal static class DrawingPersistedVariables
{
    internal static DrawingVariableState[] Read(McDocument sheet,Action cancel)
    {
        var db=HostMgd.ApplicationServices.Application.DocumentManager.MdiActiveDocument.Database;
        var networkId=Db.AssocNetwork.GetInstanceFromObject(db.CurrentSpaceId,false,false,"");
        if(networkId.IsNull)throw new DomainException("VARIABLES_UNAVAILABLE","No persisted ordinary drawing association network; no empty inventory claim.");
        using var tx=db.TransactionManager.StartTransaction();
        var network=tx.GetObject(networkId,Db.OpenMode.ForRead) as Db.AssocNetwork;
        if(network==null)throw new DomainException("VARIABLES_UNAVAILABLE","Ordinary association network unavailable.");
        var actions=network.GetActions;
        if(actions==null)throw new DomainException("VARIABLES_UNAVAILABLE","Persisted association actions are unavailable.");
        var inventoryBudget=new ModelInventoryBudget(cancel);
        var variables=new List<DrawingVariableState>();
        foreach(Db.ObjectId raw in actions)
        {
            cancel();if(raw.IsNull||raw.IsErased)throw new DomainException("VARIABLES_UNAVAILABLE","Association action identity unavailable.");
            inventoryBudget.Add(raw.Handle.ToString());
            if(tx.GetObject(raw,Db.OpenMode.ForRead) is not Db.AssocVariable v)continue;
            var id=McObjectId.FromHandle(raw.Handle.Value,McDocument.ActiveDocument.ID);
            if(id.IsNull||id.Handle!=raw.Handle.Value||v.OwnerId!=networkId||v.OwningNetwork!=networkId)
                throw new DomainException("OBJECT_IDENTITY_MISMATCH","Persisted variable ownership or native identity mismatch.");
            if(v.Name==null||v.Expression==null||v.Name.Length>8192||v.Expression.Length>8192)
                throw new DomainException("VARIABLES_UNAVAILABLE","Variable metadata unavailable or exceeds8192 characters.");
            using var stored=v.Value;double? value=DrawingVariablesAccess.StoredScalar(stored?.AsArray());
            inventoryBudget.Add(new{id=id.ToString(),v.Name,v.Expression,value});
            variables.Add(new(id,v.Name,"User",v.Expression,value,false,McObjectId.Null,null,null,null,"persisted_assoc_variable_and_constraint_graph"));
        }
        var linked=DrawingVariableConsumers.Read(sheet,variables.ToArray(),cancel,true);
        var references=linked.Where(v=>v.Downstream!=null).SelectMany(v=>v.Downstream!).ToHashSet();
        return linked.Select(v=>v with
        {
            Type="User"+(v.Constraints?.Length>0?", Dim2DConstraint":"")+(references.Contains(v.Id)?", Reference":""),
            // Stored angular units have not been established for this fallback.
            // A numeric expression or a linear variable is not evidence of them.
            Value=v.Angular?null:v.Value
        }).ToArray();
    }
}
