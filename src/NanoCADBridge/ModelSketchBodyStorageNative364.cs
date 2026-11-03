using Multicad;
using Multicad.DatabaseServices;
using Multicad.Mc3D;
using NanoCAD.Contracts;
using Db=Teigha.DatabaseServices;

namespace NanoCADBridge;

internal static class ModelSketchBodyStorageNative364
{
    // Called only after the exact new typed feature/profile/reference/body composition
    // and owner document have passed existing verification. No storage setter.
    internal static ModelSketchBodyStorage364.Proof Bind(ExtrudeAlongPathFeature feature,Mc3dSolid body,McDocument owner,McDocument drawing,
        Db.ObjectId representation,McObjectId[] beforeConstruction,McObjectId[] beforeContents,DocumentState state,Db.Transaction tx,Action cancel)
        =>Bind(feature,feature.ID.GetObjectOfType<ExtrudeAlongPathFeature>()?.ID==feature.ID,"sweep",body,owner,drawing,representation,beforeConstruction,beforeContents,state,tx,cancel);
    internal static ModelSketchBodyStorage364.Proof Bind(RevolveFeature feature,Mc3dSolid body,McDocument owner,McDocument drawing,
        Db.ObjectId representation,McObjectId[] beforeConstruction,McObjectId[] beforeContents,DocumentState state,Db.Transaction tx,Action cancel)
        =>Bind(feature,feature.ID.GetObjectOfType<RevolveFeature>()?.ID==feature.ID,"revolution",body,owner,drawing,representation,beforeConstruction,beforeContents,state,tx,cancel);
    internal static ModelSketchBodyStorage364.Proof Bind(LoftFeature feature,Mc3dSolid body,McDocument owner,McDocument drawing,
        Db.ObjectId representation,McObjectId[] beforeConstruction,McObjectId[] beforeContents,DocumentState state,Db.Transaction tx,Action cancel)
        =>Bind(feature,feature.ID.GetObjectOfType<LoftFeature>()?.ID==feature.ID,"loft",body,owner,drawing,representation,beforeConstruction,beforeContents,state,tx,cancel);
    static ModelSketchBodyStorage364.Proof Bind(HistoryItem feature,bool exactType,string kind,Mc3dSolid body,McDocument owner,McDocument drawing,
        Db.ObjectId representation,McObjectId[] beforeConstruction,McObjectId[] beforeContents,DocumentState state,Db.Transaction tx,Action cancel)
    {
        string? handle=null;var path=new List<string>();var budget=new ModelInventoryBudget(cancel);
        ModelSketchBodyStorage364.Proof Result(bool proved,string reason)=>new(feature.ID.ToString(),owner.ID.ToString(),handle,path.ToArray(),proved,reason);
        cancel();if(feature.ID.IsNull||beforeConstruction.Contains(feature.ID)||beforeContents.Contains(feature.ID)||feature.HostSolidID!=body.ID||
            feature.DbEntity.Document==null||feature.DbEntity.Document.ID!=owner.ID||!exactType)
            return Result(false,"returned_feature_is_not_exact_new_typed_selected_body_consumer");
        long rawHandle=feature.ID.Handle;
        if(rawHandle==0||!state.Document.Database.TryGetObjectId(new Db.Handle(rawHandle),out var raw)||raw.IsNull||raw.IsErased||raw.IsEffectivelyErased)
            return Result(false,"returned_feature_has_no_current_resident_DWG_storage_handle");
        handle=Validation.NormalizeHandle(raw.Handle.ToString());
        if(McObjectId.FromHandle(raw.Handle.Value,owner.ID)!=feature.ID&&McObjectId.FromHandle(raw.Handle.Value,drawing.ID)!=feature.ID)
            return Result(false,"resident_handle_does_not_roundtrip_to_exact_returned_feature_ID");
        var seen=new HashSet<Db.ObjectId>();var child=tx.GetObject(raw,Db.OpenMode.ForRead);
        while(child.ObjectId!=representation)
        {
            cancel();if(child.Database!=state.Document.Database||!seen.Add(child.ObjectId)||child.OwnerId.IsNull||!child.OwnerId.IsValid||child.OwnerId.IsErased||child.OwnerId.IsEffectivelyErased)
                return Result(false,"resident_native_storage_has_no_exact_local_body_owner_chain");
            var parent=tx.GetObject(child.OwnerId,Db.OpenMode.ForRead);string parentHandle=Validation.NormalizeHandle(parent.Handle.ToString());budget.Add(new{parentHandle});path.Add(parentHandle);
            bool reverse=parent.ExtensionDictionary==child.ObjectId;
            if(parent.GetType()==typeof(Db.DBDictionary)&&parent is Db.DBDictionary dictionary)
            {int matches=0;foreach(Db.DBDictionaryEntry entry in dictionary){cancel();if(entry.Value==child.ObjectId)matches++;}reverse=matches==1;}
            if(!reverse)return Result(false,"resident_native_storage_owner_has_no_exact_reverse_membership");
            child=parent;
        }
        if(path.Count<2||path[0]==Validation.NormalizeHandle(representation.Handle.ToString()))return Result(false,"new_native_storage_is_not_body_owned_extension_storage");
        return Result(true,"exact_new_typed_"+kind+"_resident_storage_roundtrip_and_selected_body_owner_path");
    }
}
