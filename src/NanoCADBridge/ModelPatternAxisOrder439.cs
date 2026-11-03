namespace NanoCADBridge;

// Pure proof over two ACTUAL native ordered compositions. OrderObjBefore is
// only an assigned edge, not the effective position (SDK 3DCore.h:4246).
internal static class ModelPatternAxisOrder439
{
    internal static bool ProvesRelease(string patternId,string? beforeAnchor,string? afterAnchor,string[] beforeBindings,string[] afterBindings,
        string[] orderedBefore,string[] orderedAfter)
    {
        var compare=StringComparer.OrdinalIgnoreCase;
        if(beforeAnchor==null||compare.Equals(beforeAnchor,afterAnchor)||beforeBindings.Length!=afterBindings.Length||beforeBindings.Length==0)return false;
        var changedOld=beforeBindings.Where((id,index)=>!compare.Equals(id,afterBindings[index])).ToHashSet(compare);
        var changedNew=afterBindings.Where((id,index)=>!compare.Equals(id,beforeBindings[index])).ToHashSet(compare);
        if(!changedOld.Contains(beforeAnchor)||afterAnchor!=null&&!changedNew.Contains(afterAnchor))return false;
        // An anchor is released only when no retained direction still uses it.
        if(afterBindings.Contains(beforeAnchor,compare))return false;
        var prior=orderedBefore.ToHashSet(compare);var current=orderedAfter.ToHashSet(compare);
        if(prior.Count!=orderedBefore.Length||current.Count!=orderedAfter.Length||!prior.Contains(beforeAnchor)||!prior.Contains(patternId)||!current.Contains(patternId))return false;
        int BeforeIndex(string id)=>Array.FindIndex(orderedBefore,value=>compare.Equals(value,id));
        int AfterIndex(string id)=>Array.FindIndex(orderedAfter,value=>compare.Equals(value,id));
        if(BeforeIndex(beforeAnchor)>=BeforeIndex(patternId)||afterAnchor!=null&&!current.Contains(afterAnchor))return false;
        if(changedNew.Any(id=>current.Contains(id)&&AfterIndex(id)>=AfterIndex(patternId)))return false;
        if(prior.Except(current,compare).Any(id=>!changedOld.Contains(id))||current.Except(prior,compare).Any(id=>!changedNew.Contains(id)))return false;
        // Compare every retained identity, including axes which still belong
        // to the part. Only the observed acquired/released memberships differ.
        return orderedBefore.Where(current.Contains).SequenceEqual(orderedAfter.Where(prior.Contains),compare);
    }
}
