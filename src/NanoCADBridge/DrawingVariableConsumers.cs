using Multicad;
using Multicad.DatabaseServices;
using Multicad.DatabaseServices.Constraints;
using NanoCAD.Contracts;
using Db=Teigha.DatabaseServices;
using M=Multicad.Geometry;

namespace NanoCADBridge;

// Read the persisted association graph; no variable-manager consumer getters,
// default XY group creation, UCS edits or suppressed revision events.
internal static class DrawingVariableConsumers
{
    internal static DrawingVariableState[] Read(McDocument sheet,DrawingVariableState[] variables,Action cancel,bool deriveUnits=false)
    {
        using var tx=HostMgd.ApplicationServices.Application.DocumentManager.MdiActiveDocument.Database.TransactionManager.StartTransaction();
        var db=HostMgd.ApplicationServices.Application.DocumentManager.MdiActiveDocument.Database;
        var ids=new Dictionary<Db.ObjectId,DrawingVariableState>();
        void Need(bool ok,string message){if(!ok)throw new DomainException("VARIABLES_UNAVAILABLE",message);}
        foreach(var v in variables)
        {
            cancel();bool found=db.TryGetObjectId(new Db.Handle(v.Id.Handle),out var id);Need(v.Id.Handle!=0&&found,"ACAD variable database identity unavailable.");
            Need(!id.IsNull&&!id.IsErased&&McObjectId.FromHandle(id.Handle.Value,McDocument.ActiveDocument.ID)==v.Id,"ACAD variable database identity changed.");
            var value=tx.GetObject(id,Db.OpenMode.ForRead) as Db.AssocVariable;
            Need(value!=null&&value.Name==v.Name&&value.Expression==v.Expression,"Association and native variable metadata disagree.");ids.Add(id,v);
        }
        var groups=new Dictionary<Db.ObjectId,Dictionary<McObjectId,McObjectId[]>>();var angular=new Dictionary<McObjectId,bool>();
        var inventoryBudget=new ModelInventoryBudget(cancel);
        Dictionary<McObjectId,McObjectId[]> Group(Db.ObjectId id,Db.Assoc2dConstraintGroup group)
        {
            if(groups.TryGetValue(id,out var cached))return cached;
            cancel();inventoryBudget.Add(id.Handle.ToString());
            using var plane=group.WorkPlane;var p=plane.PointOnPlane;var n=plane.Normal;
            using var frame=new M.Plane3d(new M.Point3d(p.X,p.Y,p.Z),new M.Vector3d(n.X,n.Y,n.Z));
            using var manager=sheet.Get2dConstraintsManager(Constraint2dNetType.ACAD,sheet.ID,frame);
            Need(manager!=null&&manager.DocumentId==sheet.ID&&manager.PlanarSketchId.IsNull&&manager.ConstraintsNetworkType==Constraint2dNetType.ACAD&&manager.ScopeId==McObjectId.FromHandle(id.Handle.Value,McDocument.ActiveDocument.ID),"Persisted constraint group did not resolve to the same read-only native scope.");
            var constraints=manager!.GetAllConstraints();Need(constraints!=null,"Constraint consumer inventory is unavailable.");
            var found=new Dictionary<McObjectId,List<McObjectId>>();
            foreach(var constraint in constraints!)
            {
                cancel();using var info=manager.GetConstraintInfo(constraint);Need(info!=null,"Constraint consumer details unavailable.");
                inventoryBudget.Add(new{id=constraint.ToString(),variable=info!.VarId.ToString(),type=info.Type.ToString()});
                if(info!.VarId.IsNull)continue;
                bool isAngle=info.Type.ToString()=="DimAngular";
                if(deriveUnits)Need(info.IsDimensional&&info.Type.ToString() is "DimAligned" or "DimRadial" or "DimDiametrical" or "DimHorizontal" or "DimVertical" or "DimAngular","Stored dimensional variable has an unsupported unit binding.");
                Need(!angular.TryGetValue(info.VarId,out var priorAngle)||priorAngle==isAngle,"Variable has inconsistent dimensional units.");angular[info.VarId]=isAngle;
                if(!found.TryGetValue(info.VarId,out var entries))found.Add(info.VarId,entries=new());entries.Add(constraint);
            }
            cached=found.ToDictionary(p=>p.Key,p=>p.Value.ToArray());groups.Add(id,cached);return cached;
        }
        var result=new List<DrawingVariableState>();
        foreach(var entry in ids)
        {
            cancel();var v=entry.Value;var downstream=new HashSet<McObjectId>();var constraints=new HashSet<McObjectId>();bool knownVariables=true,knownObjects=true;
            var actions=Db.AssocAction.GetActionsDependentOnObject(tx.GetObject(entry.Key,Db.OpenMode.ForRead),true,false);
            Need(actions!=null,"Persisted direct consumer graph is unavailable.");
            foreach(Db.ObjectId action in actions!)
            {
                cancel();Need(!action.IsNull&&!action.IsErased,"Consumer action identity is unavailable.");
                inventoryBudget.Add(new{variable=v.Id.ToString(),action=action.Handle.ToString()});
                if(ids.TryGetValue(action,out var dependent)){downstream.Add(dependent.Id);continue;}
                var obj=tx.GetObject(action,Db.OpenMode.ForRead);
                if(obj is Db.Assoc2dConstraintGroup group)
                {
                    var mapping=Group(action,group);Need(mapping.TryGetValue(v.Id,out var matches)&&matches.Length>0,"Consumer group has no matching dimensional variable binding.");
                    constraints.UnionWith(matches!);
                    foreach(var match in matches!)inventoryBudget.Add(new{variable=v.Id.ToString(),constraint=match.ToString()});
                }
                else if(obj is Db.AssocVariable)knownVariables=false;
                else knownObjects=false; // Unknown action is not an empty 3D consumer inventory.
            }
            result.Add(v with{Angular=deriveUnits?angular.GetValueOrDefault(v.Id):v.Angular,Downstream=knownVariables?downstream.ToArray():null,Constraints=constraints.ToArray(),Objects=knownObjects?Array.Empty<McObjectId>():null});
        }
        return result.ToArray();
    }
}
