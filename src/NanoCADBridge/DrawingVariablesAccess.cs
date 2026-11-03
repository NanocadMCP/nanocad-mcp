using Multicad;
using Multicad.DatabaseServices;
using Multicad.DatabaseServices.Constraints;
using NanoCAD.Contracts;
using Db=Teigha.DatabaseServices;

namespace NanoCADBridge;

internal sealed record DrawingVariableState(McObjectId Id,string Name,string Type,string Expression,double? Value,bool Angular,
    McObjectId Host,McObjectId[]? Downstream,McObjectId[]? Objects,McObjectId[]? Constraints,string MetadataSource="native_variable_manager");

internal static class DrawingVariablesAccess
{
    internal static bool ServiceExists(McDocument sheet)
    {
        using var plane=DrawingWorkplaneNative.Plane(DrawingWorkplane.XY);
        using var network=sheet.Get2dConstraintsManager(Constraint2dNetType.ACAD,sheet.ID,plane);
        return network!=null&&!network.ScopeId.IsNull;
    }
    internal static bool TryConstraintValue(McDocument sheet,McObjectId id,string name,string expression,bool angular,out double value)
    {
        value=double.NaN;
        if(ServiceExists(sheet))
        {
            using var manager=Manager(sheet);var metadata=manager.GetVariableInfo(id);
            return metadata!=null&&metadata.Name==name&&metadata.Expression==expression&&manager.GetVariableValue(id,out value)&&double.IsFinite(value);
        }
        // A stored linear value is readable without initializing the manager.
        // Angular conversion remains unavailable here until its native units
        // are established; never infer an angle from the expression text.
        if(angular)return false;
        var db=HostMgd.ApplicationServices.Application.DocumentManager.MdiActiveDocument.Database;
        if(id.IsNull||id.Handle==0||!db.TryGetObjectId(new Db.Handle(id.Handle),out var raw)||raw.IsNull||raw.IsErased||McObjectId.FromHandle(raw.Handle.Value,McDocument.ActiveDocument.ID)!=id)return false;
        using var tx=db.TransactionManager.StartTransaction();
        if(tx.GetObject(raw,Db.OpenMode.ForRead) is not Db.AssocVariable variable||variable.Name!=name||variable.Expression!=expression)return false;
        using var stored=variable.Value;var items=stored?.AsArray();
#if READ_ACCEPTANCE_AUDIT
        ReadAcceptanceAudit.ObserveVariableScalar(new{variable_id=id.ToString(),count=items?.Length,types=items?.Select(x=>new{code=x.TypeCode,type=x.Value?.GetType().FullName}).ToArray()});
#endif
        value=StoredScalar(items)??double.NaN;return double.IsFinite(value);
    }
    internal static double? StoredScalar(Db.TypedValue[]? items)
    {
        if(items?.Length!=1)return null;
        double result=items[0].Value switch
        {
            double d=>d,float f=>f,int i=>i,short i=>i,
            long i when i>=-9007199254740992L&&i<=9007199254740992L=>i,
            _=>double.NaN
        };
        return double.IsFinite(result)?result:null;
    }
    internal static Multicad.DatabaseServices.DocumentVariables.DocumentVariablesManager Manager(McDocument sheet)
    {
        // The public variables manager may initialize its default XY service
        // group. Creation belongs to a requested constraint write, never a read
        // or preflight transaction (even if that transaction would abort).
        if(!ServiceExists(sheet))
            throw new DomainException("VARIABLES_UNAVAILABLE","The ordinary variable service is not initialized. Constraint-only inspection remains available with include_variables=false; no service group was created by this read.",false,new{reason="default_variable_service_not_initialized"});
        return sheet.GetDocumentVariablesManager(Constraint2dNetType.ACAD)??throw new DomainException("VARIABLES_UNAVAILABLE","ACAD variables manager unavailable.");
    }
    internal static DrawingVariableState[] Read(McDocument sheet,Action cancel)
    {
        string stage="manager";McObjectId observed=McObjectId.Null;
        try
        {
        if(!ServiceExists(sheet))return DrawingPersistedVariables.Read(sheet,cancel);
        using var manager=Manager(sheet);
        stage="inventory";
        var ids=manager?.GetAllVariables();
        if(ids==null)throw new DomainException("VARIABLES_UNAVAILABLE","ACAD variable inventory unavailable; no empty inventory claim.");
        ModelInventoryBudget.Ids(ids,cancel);var inventoryBudget=new ModelInventoryBudget(cancel);
        string Text(string text)=>text!=null&&text.Length<=8192?text:throw new DomainException("VARIABLES_UNAVAILABLE","Variable text unavailable or exceeds8192 characters.");
        var result=new List<DrawingVariableState>();
        foreach(var id in ids)
        {
            cancel();observed=id;stage="metadata";var info=manager!.GetVariableInfo(id);if(info==null)throw new DomainException("VARIABLES_UNAVAILABLE","Native variable metadata unavailable.");
            stage="value";
            double? value=manager.GetVariableValue(id,out var actual)?actual:null;
            if(value.HasValue&&!double.IsFinite(value.Value))throw new DomainException("VARIABLE_VALUE_UNAVAILABLE","Native variable value is nonfinite.");
            stage="units";var angular=manager.IsAngularValue(id);stage="owner";var host=manager.GetVariableHostObjectId(id);
            stage="snapshot";
            inventoryBudget.Add(new{id=id.ToString(),info.Name,type=info.Type.ToString(),info.Expression,value,angular,host=host.ToString()});
            result.Add(new(id,Text(info.Name),info.Type.ToString(),Text(info.Expression),value,angular,host,null,null,null));
        }
        stage="persisted_consumers";return DrawingVariableConsumers.Read(sheet,result.ToArray(),cancel);
        }
        catch(InvalidOperationException error)
        {
            throw new DomainException("VARIABLES_UNAVAILABLE","A native variable property is unavailable; no empty inventory or missing-consumer claim is made.",false,
                new{stage,variable_id=observed.IsNull?null:observed.ToString(),native_message=error.Message});
        }
    }
}
