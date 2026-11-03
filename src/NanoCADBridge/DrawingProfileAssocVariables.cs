using Multicad;
using NanoCAD.Contracts;
using Db=Teigha.DatabaseServices;

namespace NanoCADBridge;

internal static partial class DrawingProfileWriter
{
    // MultiCAD ACAD ValidateName rejected both existing and new variable names
    // before any write in151. Bind the actual public AssocVariable objects and
    // use their native name/expression validation, preserving the same graph and
    // geometry verification. No MCS manager or arbitrary command fallback.
    private sealed record AssocBinding(Dictionary<McObjectId,Db.ObjectId> Variables,Db.ObjectId Network);
    private static AssocBinding BindAssoc(DrawingVariableState[] variables,DocumentState state,Db.Transaction tx)
    {
        Need(variables.Length>0,"CAPABILITY_UNAVAILABLE","Create a drawing dimension before creating User drivers in this ACAD network.");
        var result=new Dictionary<McObjectId,Db.ObjectId>();Db.ObjectId owner=Db.ObjectId.Null;
        foreach(var variable in variables)
        {
            long handle=variable.Id.Handle;
            bool found=state.Document.Database.TryGetObjectId(new Db.Handle(handle),out var raw);
            Need(handle!=0&&found&&!raw.IsNull&&!raw.IsErased,"OBJECT_IDENTITY_MISMATCH","Drawing variable has no current native database identity.");
            Need(McObjectId.FromHandle(raw.Handle.Value,Multicad.DatabaseServices.McDocument.ActiveDocument.ID)==variable.Id,"OBJECT_IDENTITY_MISMATCH","Native variable handle resolves to a different identity.");
            var value=tx.GetObject(raw,Db.OpenMode.ForRead) as Db.AssocVariable;
            Need(value!=null,"CAPABILITY_UNAVAILABLE","ACAD variable is not a public native AssocVariable: "+tx.GetObject(raw,Db.OpenMode.ForRead).GetType().FullName);
            Need(value.Name==variable.Name&&value.Expression==variable.Expression,"VARIABLES_UNAVAILABLE","ACAD and database variable metadata disagree.");
            if(owner.IsNull)owner=value.OwnerId;
            Need(value.OwnerId==owner,"OUT_OF_SCOPE","Drawing variables belong to different association networks.");result.Add(variable.Id,raw);
        }
        Need(tx.GetObject(owner,Db.OpenMode.ForRead) is Db.AssocNetwork,"OUT_OF_SCOPE","Native variable owner is not an association network.");
        var network=(Db.AssocNetwork)tx.GetObject(owner,Db.OpenMode.ForRead);var actions=network.GetActions.Cast<Db.ObjectId>().ToHashSet();
        Need(result.Values.All(actions.Contains),"OBJECT_IDENTITY_MISMATCH","Variable absent from its owning network actions.");return new(result,owner);
    }
    private static void ValidateAssoc(DrawingProfileEdit edit,DrawingVariableState[] variables,McObjectId target,DocumentState state)
    {
        using var tx=state.Document.Database.TransactionManager.StartTransaction();var binding=BindAssoc(variables,state,tx);
        var id=target.IsNull?variables[0].Id:target;var variable=(Db.AssocVariable)tx.GetObject(binding.Variables[id],Db.OpenMode.ForRead);
        string name=edit.Action=="set_drawing_expression"?variable.Name:edit.Name!;
        string expression=edit.Action=="set_drawing_expression"?edit.Expression!:edit.Action=="create_drawing_variable"?edit.Value!.Value.ToString("R",System.Globalization.CultureInfo.InvariantCulture):variable.Expression;
        string error="";
        try{variable.ValidateNameAndExpression(name,expression,ref error);}
        catch(Exception ex){throw new DomainException("INVALID_ARGUMENT","Native association validation rejected: "+ex.Message);}
        // Installed wrapper calls Interop.Check(nativeStatus) before returning.
        // Its ref errorMessage conversion constructs String from the OdString
        // object's address, yielding garbage even after successful validation.
        // A normal return is success; the out text is not a second status code.
    }
    private static McObjectId WriteAssocVariables(DrawingProfileEdit edit,VariablePlan plan,DocumentState state)
    {
        using var tx=state.Document.Database.TransactionManager.StartTransaction();var binding=BindAssoc(plan.Before,state,tx);Db.ObjectId created=Db.ObjectId.Null;
        if(edit.Action=="create_drawing_variable")
        {
            using var value=new Db.AssocVariable();created=state.Document.Database.AddDBObject(value);tx.AddNewlyCreatedDBObject(value,true);
            var network=(Db.AssocNetwork)tx.GetObject(binding.Network,Db.OpenMode.ForWrite);network.AddAction(created,true);value.SetName(edit.Name!,true);
            string evaluator=((Db.AssocVariable)tx.GetObject(binding.Variables[plan.Before[0].Id],Db.OpenMode.ForRead)).EvaluatorId;
            string error="";value.SetExpression(edit.Value!.Value.ToString("R",System.Globalization.CultureInfo.InvariantCulture),evaluator,true,true,ref error,false);
        }
        else
        {
            var value=(Db.AssocVariable)tx.GetObject(binding.Variables[plan.Target],Db.OpenMode.ForWrite);
            if(edit.Action=="name_drawing_parameter")value.SetName(edit.Name!,true);
            else{string error="";value.SetExpression(edit.Expression!,value.EvaluatorId,true,true,ref error,false);}
        }
        tx.Commit();
        return created.IsNull?McObjectId.Null:McObjectId.FromHandle(created.Handle.Value,Multicad.DatabaseServices.McDocument.ActiveDocument.ID);
    }
}
