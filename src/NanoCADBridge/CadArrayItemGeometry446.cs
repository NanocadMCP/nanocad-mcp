using System.Security.Cryptography;
using NanoCAD.Contracts;
using Teigha.DatabaseServices;

namespace NanoCADBridge;

// Optional current action-body item/anonymous-block observations. No desired
// count generates items, and different cardinalities do not veto a working write.
internal static class CadArrayItemGeometry446
{
    static void Need([System.Diagnostics.CodeAnalysis.DoesNotReturnIf(false)]bool value,string message)
    {if(!value)throw new DomainException("NATIVE_ARRAY_ITEM_GEOMETRY_MISMATCH",message);}
    static string Handle(ObjectId id)=>Validation.NormalizeHandle(id.Handle.ToString());
    internal static AssociativeArrayItemsObservation446 Read(AssocArray array,BlockReference root,Transaction tr,Action cancel)
    {
        cancel();var locators=array.getItems(false);
        var seen=new HashSet<(int Item,int Row,int Level)>();var erased=new HashSet<(int Item,int Row,int Level)>();
        using var digest=IncrementalHash.CreateHash(HashAlgorithmName.SHA256);long active=0;
        foreach(var locator in locators)
        {
            cancel();var key=(locator.ItemIndex,locator.RowIndex,locator.LevelIndex);
            Need(key.ItemIndex>=0&&key.RowIndex>=0&&key.LevelIndex>=0&&seen.Add(key),"Native array items have duplicate or invalid actual locators.");
            bool gone=array.IsErased(locator);
            digest.AppendData(BitConverter.GetBytes(key.ItemIndex));digest.AppendData(BitConverter.GetBytes(key.RowIndex));digest.AppendData(BitConverter.GetBytes(key.LevelIndex));
            digest.AppendData(new[]{gone?(byte)1:(byte)0});
            if(gone){erased.Add(key);continue;}
            active++;var transform=array.GetItemTransform(locator).ToArray();
            Need(transform.Length==16&&transform.All(double.IsFinite),"A native active item's actual transform is unavailable or nonfinite.");
            foreach(double value in transform)digest.AppendData(BitConverter.GetBytes(value));
        }
        long? references=null;string? reason=null;string? sourceOwner=null;
        using(var ids=array.SourceEntities)
        {
            var owners=new HashSet<ObjectId>();
            foreach(ObjectId id in ids){cancel();Need(tr.GetObject(id,OpenMode.ForRead) is Entity,"Array source storage is unavailable.");owners.Add(((Entity)tr.GetObject(id,OpenMode.ForRead)).OwnerId);}
            if(owners.Count!=1)reason="source_entities_have_multiple_native_owners";
            else
            {
                var owner=owners.Single();sourceOwner=Handle(owner);
                if(owner==root.BlockTableRecord)reason="root_definition_is_also_source_storage";
                else if(tr.GetObject(root.BlockTableRecord,OpenMode.ForRead) is not BlockTableRecord representation)
                    reason="root_definition_unavailable";
                else
                {
                    long count=0;bool other=false;
                    foreach(ObjectId id in representation)
                    {
                        cancel();if(id.IsErased||id.IsEffectivelyErased)continue;
                        if(tr.GetObject(id,OpenMode.ForRead) is BlockReference child&&child.GetType()==typeof(BlockReference)&&child.BlockTableRecord==owner)count++;
                        else other=true;
                    }
                    if(other)reason="representation_contains_other_native_entities";
                    else references=count;
                }
            }
        }
        var observation=new AssociativeArrayItemsObservation446(locators.LongLength,active,erased.Count,
            Convert.ToHexString(digest.GetHashAndReset()),references,sourceOwner,reason,
            "Actual public AssocArray.getItems(false), IsErased and GetItemTransform observations, plus current root-definition references to the actual source owner when available. Reference cardinality is diagnostic; no generic one-to-one mapping from those references to native item locators is assumed. Final rendered appearance needs visual review.");
        return observation;
    }
}
