using Multicad;
using Multicad.DatabaseServices;
using Multicad.Mc3D;
using NanoCAD.Contracts;

namespace NanoCADBridge;

// SDK3DCore.h says SectionsCount includes the base and GetSection accepts that
// native index unchanged. Never prepend ProfileID to that complete inventory.
internal static class ModelProfileLoftNative446
{
    static void Need([System.Diagnostics.CodeAnalysis.DoesNotReturnIf(false)] bool ok,string message)
    {if(!ok)throw new DomainException("PROFILE_OWNERSHIP_MISMATCH",message);}
    internal static McObjectId[] InventoryProfiles(LoftFeature feature,Action cancel)
    {
        // Preserve the existing owner-wide consumer reader's base-only
        // observation for an unrelated zero-section/incomplete feature. It is
        // not admission for forming; Profiles below requires a valid new loft.
        var result=new List<McObjectId>{feature.ProfileID};
        for(int index=0;index<feature.SectionsCount;index++)
        {cancel();var selected=feature.GetSection(index);Need(selected!=null&&!selected.ID.IsNull,"A native loft section is unavailable.");if(index==0)Need(selected.ID==feature.ProfileID,"The first native loft section differs from its base profile.");else result.Add(selected.ID);}
        return result.ToArray();
    }
    internal static McObjectId[] Profiles(LoftFeature feature,Action cancel)
    {
        Need(feature.SectionsCount>=2&&!feature.ProfileID.IsNull,"A native loft has no complete ordered section inventory.");
        var result=InventoryProfiles(feature,cancel);Need(result.Length==feature.SectionsCount,"The native loft section inventory is incomplete.");return result;
    }
    internal static LoftFeature Append(Mc3dSolid body,SketchProfile[] profiles,ModelProfileLoft446 edit,McObjectId[][] rails,Action cancel)
    {
        cancel();var feature=body.AddLoftFeature(profiles[0].ID,profiles.Skip(1).Select(p=>p.ID).ToArray());
        Need(feature!=null,"Native loft creation returned no feature for the exact retained sections.");
        feature.Name=edit.Name;feature.PartOperation=edit.PartOperation=="cut"?PartFeatureOperation.Cut:PartFeatureOperation.Join;
        feature.Closed=false;feature.FirstSectionCondition=LoftCondition.Free;feature.LastSectionCondition=LoftCondition.Free;
        if(edit.PathHandle!=null){feature.LoftType=LoftType.WithCenterLine;feature.Path=new McGeomParam{ID=rails[0][0]};}
        else if(edit.GuideHandles.Length>0)
        {feature.LoftType=LoftType.WithRails;foreach(var rail in rails){cancel();Need(feature.AddGuideRailsParams(rail.Select(id=>new McGeomParam{ID=id}).ToList()),"Native loft rejected a supplied guide rail.");}}
        else feature.LoftType=LoftType.Regular;
        feature.DbEntity.Update();return feature;
    }
    internal static void Verify(LoftFeature feature,SketchProfile[] profiles,ModelProfileLoft446 edit,McObjectId[][] rails,Action cancel)
    {
        cancel();var expected=edit.PathHandle!=null?LoftType.WithCenterLine:edit.GuideHandles.Length>0?LoftType.WithRails:LoftType.Regular;
        Need(Profiles(feature,cancel).SequenceEqual(profiles.Select(p=>p.ID))&&!feature.Closed&&feature.LoftType==expected&&
            feature.FirstSectionCondition==LoftCondition.Free&&feature.LastSectionCondition==LoftCondition.Free&&
            feature.PartOperation==(edit.PartOperation=="cut"?PartFeatureOperation.Cut:PartFeatureOperation.Join),"Native loft section order, end conditions, mode or material operation changed.");
        if(edit.PathHandle!=null)Need(feature.Path!=null&&feature.Path.ID==rails[0][0]&&feature.GuideRailsCount==0,"Native loft center path differs from the selected curve.");
        else
        {
            Need(feature.GuideRailsCount==rails.Length,"Native loft guide count differs from the selected rail set.");
            for(int index=0;index<rails.Length;index++){cancel();var actual=feature.GetGuideRailsParams(index);Need(actual!=null&&actual.Select(p=>p.ID).SequenceEqual(rails[index]),"Native loft guide order or native curve bindings changed.");}
        }
    }
}
