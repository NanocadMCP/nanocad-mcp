using System.Diagnostics.CodeAnalysis;
using Multicad;
using Multicad.DatabaseServices;
using Multicad.Mc3D;
using NanoCAD.Contracts;

namespace NanoCADBridge;

// Requested original history features, assigned predecessor and native copied
// source items are three different observations. Never alias one to another.
internal static class ModelPatternSources416
{
    internal const string Scope="explicit_original_same_part_dependency_edges_and_native_source_item_count; native_copies_not_original_history_ids; no_per_item_copy_mapping";
    static void Need([DoesNotReturnIf(false)] bool ok,string code,string message)
    {if(!ok)throw new DomainException(code,message);}
    internal static McObjectId[] Resolve(string[] requested,Mc3dSolid body,McObjectId[] contents,McObjectId predecessor,McObjectId pattern,Action? cancel)
    {
        var owner=body.DbEntity.Document;
        Need(owner!=null&&!owner.ID.IsNull,"DOCUMENT_CONTEXT_MISMATCH","The selected part's actual native owner is unavailable.");
        // OrderObjBefore contains assigned edges only (SDK 3DCore:4246).
        // Source admission uses the native ordered composition, not a guessed
        // complete chain of explicit anchors.
        cancel?.Invoke();var ordered=body.GetPartContents(true);
        Need(ordered!=null&&ordered.Distinct().Count()==ordered.Count&&contents.All(ordered.Contains),"MODEL_ORDER_UNAVAILABLE","The actual native ordered part composition is unavailable or inconsistent.");
        ModelInventoryBudget.Ids(ordered,cancel);int insertion=pattern.IsNull?ordered.IndexOf(predecessor):ordered.IndexOf(pattern);
        Need(insertion>=0,"MODEL_ORDER_UNAVAILABLE","The native insertion point is absent from ordered part composition.");
        var sources=new List<McObjectId>();
        foreach(string identity in requested)
        {
            cancel?.Invoke();var id=contents.SingleOrDefault(value=>value.ToString().Equals(identity,StringComparison.OrdinalIgnoreCase));
            var feature=id.IsNull?null:id.GetObjectOfType<FeatureEntity>();
            Need(feature!=null&&feature.ID==id&&id!=pattern&&feature.HostSolidID==body.ID&&feature.DbEntity.Document?.ID==owner.ID&&
                feature.Status.ToString()=="Ok"&&!feature.IsSuppressed&&!feature.IsBeyondEndOfPart,
                "OUT_OF_SCOPE","Every pattern source must be an exact healthy active forming feature of this part and its actual owner.");
            int index=ordered.IndexOf(id);Need(index>=0&&(pattern.IsNull?index<=insertion:index<insertion),"MODEL_ORDER_UNAVAILABLE","Each original pattern source must precede its insertion point in actual native ordered history.");
            var depends=feature.GetAllDependsOn(false);
            Need(depends!=null,"DEPENDENCY_INVENTORY_UNAVAILABLE","A pattern source's native dependency closure is unavailable.");
            ModelInventoryBudget.Ids(depends,cancel);
            Need(!depends.Contains(id)&&(pattern.IsNull||!depends.Contains(pattern)),"DEPENDENCY_CYCLE","An original pattern source depends on itself or the selected pattern.");
            sources.Add(id);
        }
        Need(sources.Count>0&&sources.Distinct().Count()==sources.Count,"INVALID_ARGUMENT","Choose distinct original native pattern features.");
        return sources.ToArray();
    }
    internal static McObjectId[] Dependencies(ArrayFeature pattern,Action? cancel)
    {
        cancel?.Invoke();var values=pattern.GetDependsOn(false,false);
        Need(values!=null,"DEPENDENCY_INVENTORY_UNAVAILABLE","The native pattern dependency inventory is unavailable.");
        ModelInventoryBudget.Ids(values,cancel);return values.ToArray();
    }
    internal static McObjectId[] Verify(ArrayFeature pattern,Mc3dSolid body,McObjectId[] sources,McObjectId predecessor,Action? cancel,McObjectId[]? originalScope=null)
    {
        cancel?.Invoke();Need(pattern.HostSolidID==body.ID&&pattern.DbEntity.Document?.ID==body.DbEntity.Document.ID&&
            pattern.Status.ToString()=="Ok"&&!pattern.IsSuppressed&&!pattern.IsBeyondEndOfPart&&pattern.OrderObjBefore==predecessor&&
            pattern.HasSubFeatureItems&&pattern.SourceItemsCount==sources.Length,"MODEL_STATE_UNAVAILABLE","Pattern source count, actual owner, health or assigned predecessor changed.");
        var dependencies=Dependencies(pattern,cancel);
        Need(sources.All(dependencies.Contains),"MODEL_STATE_UNAVAILABLE","The native pattern lost an explicitly requested original source dependency.");
        if(originalScope!=null)
        {
            var actual=new HashSet<McObjectId>();
            foreach(var id in dependencies)
            {
                cancel?.Invoke();var feature=id.GetObjectOfType<FeatureEntity>();if(feature==null)continue;
                Need(originalScope.Contains(id)&&feature.ID==id&&feature.HostSolidID==body.ID&&feature.DbEntity.Document?.ID==body.DbEntity.Document.ID,
                    "DEPENDENCY_OWNERSHIP_MISMATCH","A group pattern has an unproved forming-feature dependency outside this part and actual owner.");
                actual.Add(id);
            }
            // Ordering can contribute one extra forming dependency. Together
            // with the exact native source count, this equality disambiguates
            // that predecessor from the explicit original source group.
            var expected=sources.ToHashSet();bool explained=actual.SetEquals(expected);
            if(predecessor.GetObjectOfType<FeatureEntity>()!=null)expected.Add(predecessor);
            Need(explained||actual.SetEquals(expected),"PATTERN_SOURCE_BINDING_MISMATCH","The original group and optional actual predecessor do not explain the complete native forming-source dependencies.");
        }
        var native=new List<McObjectId>();
        for(int index=0;index<pattern.SourceItemsCount;index++)
        {
            cancel?.Invoke();var value=pattern.GetSourceItemFeature(index);
            Need(!value.IsNull,"MODEL_STATE_UNAVAILABLE","A native pattern source-item feature is unavailable.");native.Add(value);
        }
        ModelInventoryBudget.Ids(native,cancel);return native.ToArray();
    }
    internal static (McObjectId Id,double Value)[] Controls(RectangularPatternFeature pattern,int countX,int countY,double stepX,double stepY)=>
        [(pattern.SizeXParamVarID,countX),(pattern.SizeYParamVarID,countY),(pattern.DistancesXParamVarID,stepX),(pattern.DistancesYParamVarID,stepY)];
    internal static string? LegacySource(RectangularPatternFeature pattern,McObjectId[] contents,Action? cancel)
    {
        // OrderObjBefore is assigned ordering, not a source-group selector.
        // Keep the old scalar only when one same-part forming dependency is
        // actually witnessed and it is that predecessor; otherwise unknown.
        var dependencies=pattern.GetDependsOn(false,false);if(dependencies==null)return null;
        var features=dependencies.Where(id=>{cancel?.Invoke();return contents.Contains(id)&&id.GetObjectOfType<FeatureEntity>() is {} feature&&feature.HostSolidID==pattern.HostSolidID;}).Distinct().ToArray();
        return pattern.SourceItemsCount==1&&features.Length==1&&features[0]==pattern.OrderObjBefore?features[0].ToString():null;
    }
}
