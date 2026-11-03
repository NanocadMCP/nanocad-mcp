using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using System.Text.Json.Nodes;
using Multicad;
using Multicad.Mc3D;
using NanoCAD.Contracts;

namespace NanoCADBridge;

// A source change edits the existing native array's input list. Its copied
// source items may change; original part history and the four drivers persist.
internal static class ModelPatternEditState430
{
    static void Need([DoesNotReturnIf(false)] bool ok,string code,string message)
    {if(!ok)throw new DomainException(code,message);}

    internal static void ReplaceSources(ArrayFeature pattern,McObjectId[] sources,Action cancel)
    {
        cancel();Need(pattern.Clear(false),"MODEL_UPDATE_FAILED","The native array rejected clearing its source list.");
        foreach(var source in sources)
        {cancel();Need(pattern.AddObject(source,false),"MODEL_UPDATE_FAILED","The native array rejected an original source feature.");}
    }
    internal static JsonElement History(ModifyArguments args,DocumentState state,McObjectId pattern,bool retarget,
        string? handle=null,Action? cancel=null,HashSet<McObjectId>? referenceIds=null)
    {
        var snapshot=ModelHistoryEditGuard.Capture(args,state,pattern,handle,cancel);
        var root=JsonNode.Parse(snapshot.GetRawText())!.AsObject();
        var nodes=root["nodes"]!.AsArray();
        if(referenceIds!=null)
            foreach(var node in nodes.ToArray())
                if(referenceIds.Any(id=>id.ToString().Equals(node!["mc_id"]!.GetValue<string>(),StringComparison.OrdinalIgnoreCase)))nodes.Remove(node);
        if(!retarget)return Wire.Element(root);
        var target=nodes.Single(node=>node!["mc_id"]!.GetValue<string>().Equals(pattern.ToString(),StringComparison.OrdinalIgnoreCase))!;
        // Only this selected node's source dependencies may change. The full
        // new input group, predecessor and every other dependency are checked
        // separately by VerifyDependencies and ModelPatternSources416.Verify.
        target["dependencies"]=null;target["stable_dependencies"]=null;
        return Wire.Element(root);
    }
    internal static JsonElement WithReferenceScope(JsonElement snapshot,HashSet<McObjectId> referenceIds)
    {
        var root=JsonNode.Parse(snapshot.GetRawText())!.AsObject();var nodes=root["nodes"]!.AsArray();
        foreach(var node in nodes.ToArray())if(referenceIds.Any(id=>id.ToString().Equals(node!["mc_id"]!.GetValue<string>(),StringComparison.OrdinalIgnoreCase)))nodes.Remove(node);
        return Wire.Element(root);
    }
    internal static void VerifyHistory(JsonElement before,ModifyArguments args,DocumentState state,McObjectId pattern,
        bool retarget,string handle,Action? cancel=null,HashSet<McObjectId>? referenceIds=null,bool normalizeAxisOrder=false,string? originalAnchor=null)
    {
        var current=History(args,state,pattern,retarget,handle,cancel,referenceIds);
        if(normalizeAxisOrder)
        {
            var root=JsonNode.Parse(current.GetRawText())!.AsObject();
            var target=root["nodes"]!.AsArray().Single(node=>node!["mc_id"]!.GetValue<string>().Equals(pattern.ToString(),StringComparison.OrdinalIgnoreCase))!;
            target["native_order_before_id"]=originalAnchor;current=Wire.Element(root);
        }
        Need(ModelParameterSnapshot.Equivalent(before,current),"MODEL_STATE_UNAVAILABLE",
            "Editing the array changed another history item, retained parameters or external source bindings.");
    }

    internal static void VerifyDependencies(ArrayFeature pattern,McObjectId[] before,
        McObjectId[] originalSources,McObjectId[] sources,McObjectId predecessor,bool retarget,Action? cancel,
        McObjectId[]? oldDirections=null,McObjectId[]? newDirections=null)
    {
        var expected=before.ToHashSet();
        if(retarget)
        {
            expected.ExceptWith(originalSources);expected.UnionWith(sources);
            if(before.Contains(predecessor))expected.Add(predecessor);
        }
        if(oldDirections!=null&&newDirections!=null)
        {
            // Only explicitly replaced binding edges change; retain their
            // original source/predecessor roles if one ID served both roles.
            expected.ExceptWith(oldDirections);expected.UnionWith(newDirections);
            expected.UnionWith(sources);if(before.Contains(predecessor))expected.Add(predecessor);
        }
        Need(ModelPatternSources416.Dependencies(pattern,cancel).ToHashSet().SetEquals(expected),
            "MODEL_STATE_UNAVAILABLE","Array source, direction or predecessor dependencies differ from the requested retained design.");
    }
    internal static HashSet<(int X,int Y)>? Suppression(ArrayFeature pattern,Action? cancel)
    {
        cancel?.Invoke();var indices=pattern.GetSuppressedElementsIndices();
        if(indices==null)return null;
        var result=new HashSet<(int,int)>();var budget=new ModelInventoryBudget(cancel);
        foreach(var index in indices)
        {cancel?.Invoke();budget.Add(index.Index1+":"+index.Index2);Need(result.Add((index.Index1,index.Index2)),"MODEL_STATE_UNAVAILABLE","Native array suppression indices are duplicated.");}
        return result;
    }
    internal static bool VerifySuppression(ArrayFeature pattern,HashSet<(int X,int Y)>? before,
        int originalX,int originalY,int countX,int countY,Action? cancel)
    {
        // Count shrink/growth does not authorize changing suppression of the
        // retained positions. SDK storage of out-of-range indices is ignored.
        bool Retained((int X,int Y) index)=>index.X>=0&&index.Y>=0&&index.X<Math.Min(originalX,countX)&&index.Y<Math.Min(originalY,countY);
        var after=Suppression(pattern,cancel);
        if(before==null||after==null)return false;
        Need(before.Where(Retained).ToHashSet().SetEquals(after.Where(Retained)),"MODEL_UPDATE_FAILED",
            "Editing the array changed suppression of a retained pattern position.");
        return true;
    }
}
