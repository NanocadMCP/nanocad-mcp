using System.Globalization;
using Multicad;
using Multicad.DatabaseServices.Constraints;
using Multicad.Mc3D;
using NanoCAD.Contracts;

namespace NanoCADBridge;

// Numeric dedicated editors must preserve a retained formula when its value is
// unchanged. Changing a formula to a literal is explicit request data, never a
// side effect of editing another property of the same feature.
internal static class ModelNumericDriverEditor
{
    // These dedicated editors verify actual distance/taper with absolute
    // tolerances. A relative snapshot comparison would skip a real requested
    // change on a large feature, then fail that post-write verification.
    private static bool Unchanged(double current,double requested)=>
        double.IsFinite(requested)&&Math.Abs(current-requested)<=1e-8;
    private static bool Resolve(HistoryItem feature,McObjectId id,double value,bool replaceFormula,bool write)
    {
        using var manager=feature.DbEntity.Document.GetDocumentVariablesManager(Constraint2dNetType.MCS);
        var info=manager?.GetVariableInfo(id);
        if(manager==null||id.IsNull||manager.GetVariableHostObjectId(id)!=feature.ID||info==null||
            !manager.GetVariableValue(id,out var current)||!double.IsFinite(current))
            throw new DomainException("MODEL_STATE_UNAVAILABLE","The exact owning feature variable/value is unavailable.");
        bool unchanged=Unchanged(current,value);
        bool literal=double.TryParse(info.Expression,NumberStyles.Float,CultureInfo.InvariantCulture,out var scalar)&&double.IsFinite(scalar);
        if(!unchanged&&!literal&&!replaceFormula)
            throw new DomainException("PARAMETER_HAS_EXPRESSION","This numeric edit would replace a retained driver expression. Preserve its value, use the supported expression editor, or explicitly request replace_driver_formulas=true.");
        if(write&&!unchanged&&!manager.SetVariableExpression(id,value.ToString("R",CultureInfo.InvariantCulture)))
            throw new DomainException("MODEL_UPDATE_FAILED","Native numeric variable update was rejected.");
        return unchanged;
    }
    internal static void Validate(HistoryItem feature,McObjectId id,double value,bool replaceFormula)=>Resolve(feature,id,value,replaceFormula,false);
    internal static void Set(HistoryItem feature,McObjectId id,double value,bool replaceFormula)=>Resolve(feature,id,value,replaceFormula,true);
    // A dedicated single-part numeric edit cannot authorize a variable cascade
    // into an unrelated part. Traverse the actual owner network before writing;
    // unchanged controls do not introduce a new cascade or lose their formula.
    internal static void ValidateChangeScope(HistoryItem feature,McObjectId id,double requested,
        Mc3dSolid body,McObjectId[] contents,ModelViewFollowers.Snapshot views,Action? cancel=null)
    {
        using var manager=feature.DbEntity.Document.GetDocumentVariablesManager(Constraint2dNetType.MCS);
        if(manager==null||manager.DocumentId!=feature.DbEntity.Document.ID||manager.ConstraintsNetworkType!=Constraint2dNetType.MCS||
            manager.GetVariableHostObjectId(id)!=feature.ID||!manager.GetVariableValue(id,out var current)||!double.IsFinite(current))
            throw new DomainException("VARIABLES_UNAVAILABLE","The exact numeric driver's owning network/value is unavailable.");
        if(Unchanged(current,requested))return;
        var inventory=manager.GetAllVariables()??throw new DomainException("DEPENDENCY_INVENTORY_UNAVAILABLE","The owning native variable inventory is unavailable.");
        ModelInventoryBudget.Ids(inventory,cancel);
        var actual=inventory.ToHashSet();var permitted=contents.ToHashSet();permitted.Add(body.ID);
        if(!actual.Contains(id))throw new DomainException("DEPENDENCY_INVENTORY_UNAVAILABLE","The requested driver is absent from its owner network.");
        var roots=new HashSet<McObjectId>{feature.ID};
        var seen=new HashSet<McObjectId>{id};var pending=new Queue<McObjectId>();pending.Enqueue(id);var budget=new ModelInventoryBudget(cancel);
        while(pending.Count>0)
        {
            cancel?.Invoke();var variable=pending.Dequeue();
            var host=manager.GetVariableHostObjectId(variable);
            var info=manager.GetVariableInfo(variable);
            if(info==null||host.IsNull&&(!ModelVariableMetadata.IsUser(info.Type.ToString())||manager.IsAngularValue(variable)))
                throw new DomainException("DEPENDENCY_OWNERSHIP_MISMATCH","An affected native driver has unknown control ownership; no write was started.");
            if(!host.IsNull&&!permitted.Contains(host)&&!ModelViewFollowers.OwnsDependent(views,host))
                throw new DomainException("DEPENDENCY_OWNERSHIP_MISMATCH","A downstream driver belongs to an unscoped native object. Use set_model_parameter with propagate=true and explicit scope_handles for all affected bodies.");
            if(host!=body.ID&&permitted.Contains(host)&&host.GetObjectOfType<HistoryItem>()!=null)roots.Add(host);
            var children=manager.GetDependentOnVariables(variable);
            var consumers=manager.GetDependentOn3dObjects(variable);
            if(children==null||consumers==null)
                throw new DomainException("DEPENDENCY_INVENTORY_UNAVAILABLE","An affected native driver has unknown consumers; no write was started.");
            foreach(var child in children)
            {
                cancel?.Invoke();budget.Add(child.ToString());
                if(!actual.Contains(child))throw new DomainException("DEPENDENCY_INVENTORY_UNAVAILABLE","An affected descendant is absent from the owning variable inventory.");
                if(seen.Add(child))pending.Enqueue(child);
            }
            foreach(var consumer in consumers)
            {
                cancel?.Invoke();budget.Add(consumer.ToString());
                if(!permitted.Contains(consumer)&&!ModelViewFollowers.OwnsDependent(views,consumer))
                    throw new DomainException("DEPENDENCY_OWNERSHIP_MISMATCH","This driver also changes an unscoped native object. Use set_model_parameter with propagate=true and explicit scope_handles for all affected bodies.");
                if(consumer!=body.ID&&permitted.Contains(consumer)&&consumer.GetObjectOfType<HistoryItem>()!=null)roots.Add(consumer);
            }
        }
        // DVM reports direct object consumers. Their native geometry may in
        // turn feed projections/features, so retain the existing transitive
        // native-object ownership check as well as the variable BFS above.
        ModelDependencyGraph410.OwnedClosure(body,roots,cancel??(()=>{}),views);
    }
    internal static string? RetainedExpression(HistoryItem feature,McObjectId id,double? requestedValue)
    {
        using var manager=feature.DbEntity.Document.GetDocumentVariablesManager(Constraint2dNetType.MCS);
        var info=manager?.GetVariableInfo(id);
        if(manager==null||id.IsNull||manager.GetVariableHostObjectId(id)!=feature.ID||info==null||
            !manager.GetVariableValue(id,out var current)||!double.IsFinite(current))
            throw new DomainException("MODEL_STATE_UNAVAILABLE","The exact owning feature variable/value is unavailable.");
        return requestedValue==null||Unchanged(current,requestedValue.Value)?info.Expression:null;
    }
    internal static void VerifyRetained(HistoryItem feature,IReadOnlyDictionary<McObjectId,string> expressions)
    {
        using var manager=feature.DbEntity.Document.GetDocumentVariablesManager(Constraint2dNetType.MCS);
        foreach(var pair in expressions)
            if(manager==null||manager.GetVariableHostObjectId(pair.Key)!=feature.ID||
                manager.GetVariableInfo(pair.Key)?.Expression!=pair.Value)
                throw new DomainException("MODEL_UPDATE_FAILED","Editing another feature property changed a protected driver expression.");
    }
}
