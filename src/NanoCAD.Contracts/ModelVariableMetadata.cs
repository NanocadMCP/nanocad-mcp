namespace NanoCAD.Contracts;

public static class ModelVariableMetadata
{
    // nanoCAD marks a distance variable Reference when its expression starts
    // depending on another variable. That change is expected only on the target;
    // ownership, units, name and every other type flag must remain unchanged.
    public static bool IsUser(string type)=>type is "User" or "User, Reference";
    public static bool SameTypeOrTargetReference(string before,string after,bool target,bool references,bool provedSketchDriver=false)
    {
        if(before==after)return true;
        if(!target)return false;
        var old=before.Split(',',StringSplitOptions.TrimEntries).ToHashSet(StringComparer.Ordinal);
        var current=after.Split(',',StringSplitOptions.TrimEntries).ToHashSet(StringComparer.Ordinal);
        if(!(old.Contains("Model3D")&&current.Contains("Model3D")||old.Contains("User")&&current.Contains("User")||provedSketchDriver&&old.Contains("Dim2DConstraint")&&current.Contains("Dim2DConstraint"))||current.Contains("Reference")!=references)return false;
        old.Remove("Reference");current.Remove("Reference");
        return old.SetEquals(current);
    }
}
