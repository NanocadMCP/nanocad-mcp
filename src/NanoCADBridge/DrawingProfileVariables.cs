using System.Globalization;
using Multicad;
using Multicad.DatabaseServices.Constraints;
using NanoCAD.Contracts;

namespace NanoCADBridge;

internal static partial class DrawingProfileWriter
{
    private sealed record VariablePlan(DrawingVariableState[] Before,McObjectId Target,Dictionary<string,string> Names,
        Dictionary<string,string> Expressions,Dictionary<string,double> Values,HashSet<string> Affected);
    private static string Key(McObjectId id)=>id.ToString();
    private static ModelExpressionVariable[] Arithmetic(DrawingVariableState[] variables,Network network)
    {
        return variables.Select(v=>
        {
            Need(v.Value.HasValue&&v.Downstream!=null,"VARIABLES_UNAVAILABLE","Variable values and direct dependency inventories must be known.");
            return new ModelExpressionVariable(Key(v.Id),v.Name,v.Expression,v.Value,v.Downstream!.Select(Key).ToArray(),network.Relations.Any(r=>r.Variable==v.Id));
        }).ToArray();
    }
    private static VariablePlan PrepareVariables(DrawingProfileEdit edit,Binding binding,DocumentState state,Action cancel)
    {
        Need(binding.Network.Available,"CONSTRAINTS_UNAVAILABLE","Read an existing ACAD network before variable writes.");
        var before=DrawingVariablesAccess.Read(binding.Sheet,cancel);var arithmetic=Arithmetic(before,binding.Network);DrawingVariablePlanning.VerifyConsumers(arithmetic,cancel);
        Need(before.All(v=>v.Objects!=null&&v.Constraints!=null),"VARIABLES_UNAVAILABLE","Direct2D/3D consumer inventories must be observable before a variable write.");
        var names=before.ToDictionary(v=>Key(v.Id),v=>v.Name,StringComparer.OrdinalIgnoreCase);
        var expressions=before.ToDictionary(v=>Key(v.Id),v=>v.Expression,StringComparer.OrdinalIgnoreCase);
        var values=before.ToDictionary(v=>Key(v.Id),v=>v.Value!.Value,StringComparer.OrdinalIgnoreCase);
        var affected=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if(edit.Action=="create_drawing_variable")
        {
            Need(!before.Any(v=>v.Name.Equals(edit.Name,StringComparison.OrdinalIgnoreCase)),"NAME_CONFLICT","Variable name already exists.");
            ValidateAssoc(edit,before,McObjectId.Null,state);
            return new(before,McObjectId.Null,names,expressions,values,affected);
        }
        var matches=before.Where(v=>Key(v.Id).Equals(edit.VariableId,StringComparison.OrdinalIgnoreCase)).ToArray();
        Need(matches.Length==1,"OBJECT_NOT_FOUND","Native variable is absent; read fresh IDs after reopen.");var target=matches[0];
        var unchanged=ModelExpressionPlanning.Prepare(arithmetic,Key(target.Id),target.Expression,true,cancel,storedTargetExpression:true);
        foreach(var id in unchanged.AffectedIds)affected.Add(id);
        if(edit.Action=="name_drawing_parameter")
        {
            Need(target.Name==edit.ExpectedName,"PRECONDITION_FAILED","Variable name changed.");
            Need(!before.Any(v=>v.Name.Equals(edit.Name,StringComparison.OrdinalIgnoreCase)),"NAME_CONFLICT","Variable name already exists.");
            names[Key(target.Id)]=edit.Name!;
            foreach(var v in before){cancel();expressions[Key(v.Id)]=ModelArithmetic.ParseStored(v.Expression,cancel).RenameReference(target.Name,edit.Name!);}
        }
        else
        {
            Need(target.Expression==edit.ExpectedExpression&&DrawingProfileGeometry.Near(target.Value!.Value,edit.ExpectedValue!.Value),"EXPECTED_DIMENSION_MISMATCH","Variable value or expression changed.");
            var plan=ModelExpressionPlanning.Prepare(arithmetic,Key(target.Id),edit.Expression!,true,cancel);
            expressions[Key(target.Id)]=edit.Expression!;foreach(var pair in plan.ExpectedValues)values[pair.Key]=pair.Value;
            foreach(var id in plan.ReferencedIds)affected.Add(id); // references must also have supported units/ownership
        }
        foreach(var id in affected)
        {
            var v=before.Single(v=>Key(v.Id).Equals(id,StringComparison.OrdinalIgnoreCase));
            Need(v.Objects!=null&&v.Objects.Length==0,"OUT_OF_SCOPE","Only ACAD User/dimension variables without direct3D consumers are supported. Bodies must consume projected sources.");
            var dimensions=binding.Network.Relations.Where(r=>r.Variable==v.Id).ToArray();
            Need(v.Constraints!.ToHashSet().SetEquals(dimensions.Select(r=>r.Id)),"OUT_OF_SCOPE","Variable has dimensional consumers outside the observed WCSXY network.");
            if(ModelVariableMetadata.IsUser(v.Type))Need(v.Host.IsNull&&dimensions.Length==0&&!v.Angular,"OUT_OF_SCOPE","User variable must be a document-owned nonangular scalar.");
            else
            {
                Need(dimensions.Length==1&&dimensions[0].Spec?.Kind is "radius" or "diameter" or "length" or "distance" or "distance_x" or "distance_y" or "angle","INCOMPLETE_CONSTRAINT_SELECTION","Include the complete components of all affected/referenced supported dimensions.");
                bool angular=dimensions[0].Spec!.Kind=="angle";
                Need(v.Angular==angular,"CONSTRAINT_VALUE_UNAVAILABLE","Angular variable metadata differs from its bound dimensional type.");
                if(angular)Need(values[id]>=DrawingAngularGeometry.MinimumDegrees&&values[id]<=DrawingAngularGeometry.MaximumDegrees,"OUT_OF_SCOPE","Angular formulas must remain0.1..179.9 degrees.");
                Need(DrawingVariablePlanning.IsDimensionType(v.Type),"OUT_OF_SCOPE","Unsupported native drawing variable type.");
            }
        }
        // Only the target and its native downstream variables may change.
        affected=unchanged.AffectedIds.ToHashSet(StringComparer.OrdinalIgnoreCase);
        ValidateAssoc(edit,before,target.Id,state);
        return new(before,target.Id,names,expressions,values,affected);
    }
    private static McObjectId WriteVariables(DrawingProfileEdit edit,Binding binding,VariablePlan plan,DocumentState state)
    {
        var created=WriteAssocVariables(edit,plan,state);
        if(edit.Action=="create_drawing_variable")Need(!created.IsNull,"VARIABLE_UPDATE_FAILED","Native User variable identity unavailable after creation.");
        if(edit.Action=="set_drawing_expression")
        {using var manager=Manager(binding.Sheet,binding.Selected.Values.First().Workplane);Need(manager!=null&&manager.ApplyAllConstraints(),"CONSTRAINT_SOLVE_FAILED","Native drawing expression solve rejected.");}
        return created;
    }
    private static DrawingVariableChange[] VerifyVariables(DrawingProfileEdit edit,Binding binding,VariablePlan plan,McObjectId created,Action cancel)
    {
        var after=DrawingVariablesAccess.Read(binding.Sheet,cancel);var current=after.ToDictionary(v=>Key(v.Id),StringComparer.OrdinalIgnoreCase);
        Need(current.Keys.ToHashSet(StringComparer.OrdinalIgnoreCase).SetEquals(plan.Before.Select(v=>Key(v.Id)).Concat(created.IsNull?Array.Empty<string>():new[]{Key(created)})),"VARIABLE_UPDATE_FAILED","Variable inventory changed unexpectedly.");
        foreach(var old in plan.Before)
        {
            string id=Key(old.Id);var now=current[id];bool target=old.Id==plan.Target;
            var a=old.Type.Split(',',StringSplitOptions.TrimEntries).Where(t=>t!="Reference").ToHashSet(StringComparer.Ordinal);
            var b=now.Type.Split(',',StringSplitOptions.TrimEntries).Where(t=>t!="Reference").ToHashSet(StringComparer.Ordinal);
            bool references=ModelArithmetic.ParseStored(plan.Expressions[id],cancel).References.Length>0;
            Need(old.Type==now.Type||target&&edit.Action=="set_drawing_expression"&&a.SetEquals(b)&&now.Type.Split(',',StringSplitOptions.TrimEntries).Contains("Reference")==references,"VARIABLE_UPDATE_FAILED","Native variable type changed unexpectedly.");
            Need(now.Host==old.Host&&now.Angular==old.Angular&&now.Objects!=null&&old.Objects!=null&&now.Objects.ToHashSet().SetEquals(old.Objects),"VARIABLE_UPDATE_FAILED","Variable ownership, units or direct3D consumers changed.");
            Need(now.Constraints!=null&&old.Constraints!=null&&now.Constraints.ToHashSet().SetEquals(old.Constraints),"VARIABLE_UPDATE_FAILED","Direct dimensional consumer bindings changed.");
            Need(now.Name==plan.Names[id]&&ModelArithmetic.ParseStored(now.Expression,cancel).Canonical==ModelArithmetic.ParseStored(plan.Expressions[id],cancel).Canonical&&now.Value.HasValue&&DrawingProfileGeometry.Near(now.Value.Value,plan.Values[id]),"VARIABLE_UPDATE_FAILED","Variable name, expression or native value differs from plan.");
        }
        if(!created.IsNull)
        {
            var v=current[Key(created)];Need(v.Name==edit.Name&&ModelVariableMetadata.IsUser(v.Type)&&v.Host.IsNull&&!v.Angular&&v.Value.HasValue&&DrawingProfileGeometry.Near(v.Value.Value,edit.Value!.Value)&&ModelArithmetic.ParseStored(v.Expression,cancel).References.Length==0&&v.Objects!=null&&v.Objects.Length==0&&v.Constraints!=null&&v.Constraints.Length==0,"VARIABLE_UPDATE_FAILED","Created variable differs from requested document scalar.");
        }
        DrawingVariablePlanning.VerifyConsumers(Arithmetic(after,binding.Network),cancel);
        return after.Where(v=>v.Id==created||plan.Affected.Contains(Key(v.Id))).Select(v=>new DrawingVariableChange(Key(v.Id),v.Name,v.Expression,v.Value!.Value,v.Angular?"degrees":"native_variable_units")).ToArray();
    }
}
