using Multicad;
using Multicad.DatabaseServices.Constraints;
using NanoCAD.Contracts;

namespace NanoCADBridge;

internal static partial class DrawingProfileWriter
{
    private sealed record RemovalPlan(Relation[] Relations,DrawingVariableState[] Variables,McObjectId User);
    private static bool IsRemoval(DrawingProfileEdit edit)=>edit.Action is "remove_drawing_constraints" or "remove_drawing_variable";
    private static RemovalPlan PrepareRemoval(DrawingProfileEdit edit,Binding binding,Action cancel)
    {
        Need(binding.Network.Available,"CONSTRAINTS_UNAVAILABLE","Read the existing ACAD network before removing constraints or variables.");
        var variables=DrawingVariablesAccess.Read(binding.Sheet,cancel);
        Need(variables.All(v=>v.Value.HasValue&&v.Downstream!=null&&v.Objects!=null&&v.Constraints!=null),"VARIABLES_UNAVAILABLE","Complete stored values and direct consumers required before removal.");
        if(edit.Action=="remove_drawing_variable")
        {
            var found=variables.Where(v=>Key(v.Id).Equals(edit.VariableId,StringComparison.OrdinalIgnoreCase)).ToArray();Need(found.Length==1,"OBJECT_NOT_FOUND","Variable no longer exists; obtain a fresh ID.");var v=found[0];
            Need(v.Name==edit.ExpectedName&&v.Expression==edit.ExpectedExpression&&DrawingProfileGeometry.Near(v.Value!.Value,edit.ExpectedValue!.Value),"PRECONDITION_FAILED","Variable name, expression or value changed.");
            Need(ModelVariableMetadata.IsUser(v.Type)&&v.Host.IsNull&&!v.Angular,"OUT_OF_SCOPE","Only a document-owned User scalar can be removed directly; remove its dimensional constraint first.");
            Need(v.Downstream!.Length==0&&v.Objects!.Length==0&&v.Constraints!.Length==0,"VARIABLE_HAS_DEPENDENTS","Detach all dependent expressions and geometric consumers before deleting a User variable.");
            using var manager=DrawingVariablesAccess.Manager(binding.Sheet);
            Need(manager!=null&&manager.CanVariableBeDeleted(v.Id),"VARIABLE_DELETE_REJECTED","Native manager does not permit removing this variable.");
            return new(Array.Empty<Relation>(),variables,v.Id);
        }
        var selected=new List<Relation>();
        foreach(var id in edit.ConstraintIds!)
        {
            var found=binding.Network.Relations.Where(r=>Key(r.Id).Equals(id,StringComparison.OrdinalIgnoreCase)).ToArray();Need(found.Length==1&&found[0].Spec!=null,"OBJECT_NOT_FOUND","Constraint is absent or outside the complete selected component.");var r=found[0];
            if(!r.Variable.IsNull)
            {
                var v=variables.SingleOrDefault(v=>v.Id==r.Variable);Need(v!=null&&v.Downstream!.Length==0&&v.Objects!.Length==0&&v.Constraints!.Length==1&&v.Constraints[0]==r.Id,"PARAMETER_HAS_DEPENDENTS","A removed dimensional variable must belong only to this constraint and have no other consumers. Detach dependent expressions first.");
            }
            selected.Add(r);
        }
        return new(selected.ToArray(),variables,McObjectId.Null);
    }
    private static void WriteRemoval(Binding binding,RemovalPlan plan,Action cancel)
    {
        if(!plan.User.IsNull)
        {
            using var variables=DrawingVariablesAccess.Manager(binding.Sheet);
            Need(variables!=null&&variables.DeleteVariable(plan.User),"VARIABLE_DELETE_FAILED","Native variable deletion was not confirmed.");return;
        }
        using var manager=Manager(binding.Sheet,binding.Selected.Values.First().Workplane);Need(manager!=null,"CONSTRAINTS_UNAVAILABLE","ACAD manager became unavailable.");
        foreach(var relation in plan.Relations){cancel();Need(manager.DeleteConstraint(relation.Id),"CONSTRAINT_DELETE_FAILED","Native constraint deletion was not confirmed; earlier removals may remain.");}
    }
    private static string[] VerifyRemoval(Binding binding,RemovalPlan plan,Action cancel)
    {
        var after=DrawingVariablesAccess.Read(binding.Sheet,cancel);var before=plan.Variables.ToDictionary(v=>v.Id);var current=after.ToDictionary(v=>v.Id);
        var permitted=plan.Relations.Where(r=>!r.Variable.IsNull).Select(r=>r.Variable).Concat(plan.User.IsNull?Array.Empty<McObjectId>():new[]{plan.User}).ToHashSet();
        var removed=before.Keys.Where(v=>!current.ContainsKey(v)).ToHashSet();var constraints=plan.Relations.Select(r=>r.Id).ToHashSet();
        Need(current.Keys.All(before.ContainsKey)&&removed.IsSubsetOf(permitted)&&(plan.User.IsNull||removed.Contains(plan.User)),"VARIABLE_DELETE_FAILED","Unexpected variable allocation/removal, or requested User remains.");
        foreach(var now in after)
        {
            var old=before[now.Id];bool detached=permitted.Contains(now.Id);
            Need(now.Name==old.Name&&now.Expression==old.Expression&&now.Value.HasValue&&DrawingProfileGeometry.Near(now.Value.Value,old.Value!.Value)&&now.Host==old.Host,"VARIABLE_DELETE_FAILED","Retained variable name/expression/value/owner changed.");
            Need(now.Type==old.Type||detached&&ModelVariableMetadata.IsUser(now.Type),"VARIABLE_DELETE_FAILED","Retained variable type changed outside the removed dimension.");
            Need(now.Angular==old.Angular||detached&&!now.Angular,"VARIABLE_DELETE_FAILED","Unrelated variable units changed.");
            Need(now.Downstream!=null&&now.Downstream.ToHashSet().SetEquals(old.Downstream!.Where(v=>!removed.Contains(v)))&&now.Objects!=null&&now.Objects.ToHashSet().SetEquals(old.Objects!)&&now.Constraints!=null&&now.Constraints.ToHashSet().SetEquals(old.Constraints!.Where(v=>!constraints.Contains(v))),"VARIABLE_DELETE_FAILED","Unrelated consumer links changed or deleted links remain.");
        }
        return removed.Select(Key).OrderBy(v=>v,StringComparer.Ordinal).ToArray();
    }
}
