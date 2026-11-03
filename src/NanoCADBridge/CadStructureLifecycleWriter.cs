using System.Text.Json;
using NanoCAD.Contracts;
using NanoCAD.Ipc;
using Teigha.DatabaseServices;

namespace NanoCADBridge;

// Native symbol-record lifecycle only. Layout deletion is deliberately routed
// separately, after releasing the selector transaction.
internal static class CadStructureLifecycleWriter
{
    internal sealed record Resolved(StructureLifecycleEdit Edit,ObjectId Target,ObjectId Table,EntityRef Reference,
        JsonElement Properties,JsonElement Inventory,ObjectId CurrentLayer,ObjectId CurrentTextStyle,ObjectId CurrentDimensionStyle,
        CadUsedDimensionStyleRename.State? UsedDimensionRename=null);
    private static void Need(bool value,string message,string code="STRUCTURE_UNAVAILABLE")
    {if(!value)throw new DomainException(code,message);}
    private static EntityRef Ref(EntityRef context,ObjectId id)=>new(context.bridge_instance_id,context.document_id,Validation.NormalizeHandle(id.Handle.ToString()));
    private static ObjectId Table(string kind,Database db)=>kind switch{"layer"=>db.LayerTableId,"text_style"=>db.TextStyleTableId,"dimension_style"=>db.DimStyleTableId,_=>ObjectId.Null};
    private static Type Type(string kind)=>kind switch{"layer"=>typeof(LayerTableRecord),"text_style"=>typeof(TextStyleTableRecord),"dimension_style"=>typeof(DimStyleTableRecord),_=>typeof(void)};
    private static ObjectId Current(string kind,Database db)=>kind switch{"layer"=>db.Clayer,"text_style"=>db.Textstyle,"dimension_style"=>db.Dimstyle,_=>ObjectId.Null};
    private static bool Has(SymbolTable table,string name)=>table switch{LayerTable layers=>layers.Has(name),TextStyleTable styles=>styles.Has(name),DimStyleTable styles=>styles.Has(name),_=>false};
    private static ObjectId Named(SymbolTable table,string name)=>table switch{LayerTable layers=>layers[name],TextStyleTable styles=>styles[name],DimStyleTable styles=>styles[name],_=>ObjectId.Null};
    private static void Dependencies(DBObject item)
    {
        using var reactors=item.GetPersistentReactorIds();
        Need(!item.HasFields&&reactors.Count==0,"Native rename/delete excludes fields and persistent dependencies; use the owning source route.");
    }
    private static JsonElement Properties(SymbolTableRecord item,EntityRef context,Transaction tr,Database db,Action cancel)
    {
        cancel();object metadata=item switch{
            TextStyleTableRecord text=>TextStyleMetadataReader.Read(text,cancel),
            // This field is derived from Name, which intentionally changes on
            // rename. It is not evidence of a native family-parent relation.
            DimStyleTableRecord dimension=>DimensionStyleMetadataReader.Read(dimension,tr,db,id=>Ref(context,id),cancel,cancel) with{family_name_candidate=false},
            LayerTableRecord layer=>CadLayerRenamer.Properties(layer),
            _=>throw new DomainException("UNSUPPORTED_ENTITY_TYPE","Expected an exact native symbol record.")};
        using var data=item.XData;
        var xdata=data?.AsArray().Select(value=>new{code=value.TypeCode,value=value.Value is ObjectId id?Validation.NormalizeHandle(id.Handle.ToString()):value.Value}).ToArray();
        return Wire.Element(new{metadata,xdata,extension=item.ExtensionDictionary.IsNull?null:item.ExtensionDictionary.Handle.ToString()});
    }
    private static bool SameProperties(JsonElement expected,JsonElement actual)=>
        ModelParameterSnapshot.Equivalent(expected.GetProperty("metadata"),actual.GetProperty("metadata"))&&
        // These typed stored values do not undergo a numerical edit. Relative
        // geometry tolerance must not hide a changed large1071 integer, type
        // code, application string, binary value or native extension identity.
        expected.GetProperty("xdata").GetRawText()==actual.GetProperty("xdata").GetRawText()&&
        expected.GetProperty("extension").GetRawText()==actual.GetProperty("extension").GetRawText();
    private static JsonElement Inventory(SymbolTable table,Transaction tr,ObjectId? excluded,Action cancel)
    {
        var records=new List<object>();
        foreach(ObjectId id in table)
        {
            cancel();if(id==excluded||id.IsErased||id.IsEffectivelyErased)continue;
            var record=(SymbolTableRecord)tr.GetObject(id,OpenMode.ForRead);
            records.Add(new{handle=Validation.NormalizeHandle(id.Handle.ToString()),record.Name,type=record.GetType().FullName,owner=record.OwnerId.Handle.ToString()});
        }
        return Wire.Element(records.OrderBy(value=>JsonSerializer.Serialize(value,Wire.Json),StringComparer.Ordinal));
    }
    private static void PurgeEligible(ObjectId target,Database db,Action cancel)
    {
        cancel();using var candidates=new ObjectIdCollection(new[]{target});db.Purge(candidates);cancel();
        Need(candidates.Count==1&&candidates[0]==target,"Native record is in use or not purge eligible; no entity or definition was deleted/reassigned.","STRUCTURE_IN_USE");
    }
    internal static Resolved Resolve(StructureWriteArguments args,StructureLifecycleEdit edit,Database db,Transaction tr,Action cancel)
    {
        Need(edit.Operation!="delete_layout","Native layout deletion uses its separate transaction-free adapter.","INVALID_ARGUMENT");
        string kind=StructureLifecycleValidation.Kind(edit);var tableId=Table(kind,db);var target=CadStructureWriter.Find(db,edit.Handle);
        CadUsedDimensionStyleRename.State? usedDimensionRename=null;
        var item=tr.GetObject(target,OpenMode.ForRead) as SymbolTableRecord;
        Need(item!=null&&item.GetType()==Type(kind)&&item.OwnerId==tableId,"Expected an exact native record from this document's matching symbol table.","OUT_OF_SCOPE");
        Need(!item!.IsDependent&&!item.Name.Contains('|')&&BlockWriteValidation.OrdinaryName(item.Name),"Dependent/special record names require their owning route.");
        Need(item.Name==edit.ExpectedName,"Native record name differs from expected_name; refresh get_structure.","STRUCTURE_NAME_MISMATCH");
        var table=(SymbolTable)tr.GetObject(tableId,OpenMode.ForRead);
        Need(Has(table,item.Name)&&Named(table,item.Name)==target,"Native symbol-table name index is inconsistent.");
        if(StructureLifecycleValidation.IsCurrent(edit))
        {
            if(item is TextStyleTableRecord text)Need(!text.IsShapeFile,"Shape-file records are not ordinary current text styles.");
            var expected=CadStructureWriter.Find(db,edit.ExpectedCurrentHandle!);
            Need(expected==Current(kind,db),"Current native style differs from expected_current_handle; refresh get_structure is_current.","CURRENT_STYLE_MISMATCH");
            var old=tr.GetObject(expected,OpenMode.ForRead);
            Need(old.GetType()==Type(kind)&&old.OwnerId==tableId,"Current style identity/owner is inconsistent.");
            if(item is DimStyleTableRecord selected)
            {
                var selectedRef=new EntityRef(args.BridgeId,args.DocumentId,edit.Handle);
                var defaults=DimensionStyleMetadataReader.Read(selected,tr,db,id=>Ref(selectedRef,id),cancel,cancel);
                Need(DimensionStyleWriteValidation.Dependencies(defaults).All(dependency=>dependency.state is "null" or "resolved"),
                    "Selected dimension style has an unavailable native default reference.");
                foreach(var dependency in DimensionStyleWriteValidation.Dependencies(defaults).Where(dependency=>dependency.state=="resolved"))
                {
                    cancel();var native=tr.GetObject(CadStructureWriter.Find(db,dependency.@ref!.handle),OpenMode.ForRead);
                    Need(native is SymbolTableRecord symbol&&!symbol.IsDependent&&
                        (native is not BlockTableRecord block||!block.IsFromExternalReference&&!block.IsFromOverlayReference),
                        "Selected dimension style defaults must retain local native references.");
                }
            }
        }
        else
        {
            if(edit.Operation=="rename_dimension_style")
                usedDimensionRename=CadUsedDimensionStyleRename.Capture((DimStyleTableRecord)item,
                    new EntityRef(args.BridgeId,args.DocumentId,edit.Handle),db,tr,cancel);
            else Dependencies(item);
            if(StructureLifecycleValidation.IsRename(edit))Need(!Has(table,edit.Name!),"Destination native style name already exists.","STYLE_EXISTS");
            else
            {
                Need(target!=Current(kind,db),"The current native record cannot be deleted; select a different current record first.","STRUCTURE_PROTECTED");
                Need(kind!="layer"||target!=db.LayerZero,"The native Database.LayerZero record is protected.","STRUCTURE_PROTECTED");
                PurgeEligible(target,db,cancel);
            }
        }
        var reference=new EntityRef(args.BridgeId,args.DocumentId,edit.Handle);
        return new(edit,target,tableId,reference,Properties(item,reference,tr,db,cancel),Inventory(table,tr,null,cancel),db.Clayer,db.Textstyle,db.Dimstyle,usedDimensionRename);
    }
    internal static void Preview(Resolved resolved,Database db,Transaction tr,List<DBObject> owned,Action cancel)
    {
        VerifySource(resolved,db,tr,cancel);
        if(!StructureLifecycleValidation.IsRename(resolved.Edit))return;
        // Used-DIM rename has complete resident retention preflight. Do not
        // clone its borrowed reactor graph for an unrelated name oracle.
        if(resolved.UsedDimensionRename!=null)return;
        cancel();var source=(SymbolTableRecord)tr.GetObject(resolved.Target,OpenMode.ForRead);var clone=source.Clone();
        Need(!ReferenceEquals(clone,source),"Native style preview returned its resident source; no setter was called.","API_EXCEPTION");
        if(clone is not SymbolTableRecord){(clone as IDisposable)?.Dispose();throw new DomainException("API_EXCEPTION","Native style preview did not produce a symbol record.");}
        var copy=(SymbolTableRecord)clone;owned.Add(copy);
        Need(copy.GetType()==source.GetType()&&copy.ObjectId.IsNull,"Native style preview must be exact and detached.","API_EXCEPTION");
        copy.Name=resolved.Edit.Name!;Need(copy.Name==resolved.Edit.Name,"Native name preview did not retain the requested name.","NATIVE_STRUCTURE_MISMATCH");
        // DimStyleMetadataReader has a separate detached mode; no preview
        // database defaults or resident record setters are invoked here.
    }
    private static void VerifySource(Resolved resolved,Database db,Transaction tr,Action cancel)
    {
        cancel();var source=(SymbolTableRecord)tr.GetObject(resolved.Target,OpenMode.ForRead);
        Need(source.GetType()==Type(StructureLifecycleValidation.Kind(resolved.Edit))&&source.OwnerId==resolved.Table&&source.Name==resolved.Edit.ExpectedName,
            "Native structure identity/name/owner changed before apply.","STRUCTURE_NAME_MISMATCH");
        Need(SameProperties(resolved.Properties,Properties(source,resolved.Reference,tr,db,cancel))&&
            ModelParameterSnapshot.Equivalent(resolved.Inventory,Inventory((SymbolTable)tr.GetObject(resolved.Table,OpenMode.ForRead),tr,null,cancel)),
            "Native structure metadata/name inventory changed before apply.","STRUCTURE_STATE_CHANGED");
        Need(db.Clayer==resolved.CurrentLayer&&db.Textstyle==resolved.CurrentTextStyle&&db.Dimstyle==resolved.CurrentDimensionStyle,
            "Current native record selection changed before apply.","CURRENT_STYLE_MISMATCH");
        if(resolved.UsedDimensionRename is {} used)
            CadUsedDimensionStyleRename.Verify(used,resolved.Reference,db,tr,cancel,"STRUCTURE_STATE_CHANGED");
    }
    private static JsonElement DimensionDefaults(DimensionStyleProperties value)=>Wire.Element(new{value.dimension_scale,value.text_height,value.arrow_size,
        value.variables,value.references,value.colors,value.lineweights});
    internal static StructureChange Apply(int index,Resolved resolved,Database db,Transaction tr,WriteJob job)
    {
        VerifySource(resolved,db,tr,job.CheckCancellation);var edit=resolved.Edit;string kind=StructureLifecycleValidation.Kind(edit);
        var item=(SymbolTableRecord)tr.GetObject(resolved.Target,OpenMode.ForRead);var table=(SymbolTable)tr.GetObject(resolved.Table,OpenMode.ForRead);
        if(StructureLifecycleValidation.IsCurrent(edit))
        {
            if(kind=="text_style")db.Textstyle=resolved.Target;
            else
            {
                var selected=(DimStyleTableRecord)item;
                var expected=DimensionStyleMetadataReader.Read(selected,tr,db,id=>Ref(resolved.Reference,id),job.CheckCancellation,job.CheckCancellation);
                db.Dimstyle=resolved.Target;db.SetDimstyleData(selected);
                using var defaults=db.GetDimstyleData();
                var actual=DimensionStyleMetadataReader.ReadDetached(defaults,resolved.Reference,tr,db,id=>Ref(resolved.Reference,id),job.CheckCancellation,job.CheckCancellation);
                Need(ModelParameterSnapshot.Equivalent(DimensionDefaults(expected),DimensionDefaults(actual)),"Native current DIM defaults differ from the selected style.","NATIVE_STRUCTURE_MISMATCH");
            }
            Need(Current(kind,db)==resolved.Target,"Native current style setter did not retain the selected identity.","NATIVE_STRUCTURE_MISMATCH");
        }
        else if(StructureLifecycleValidation.IsRename(edit))
        {
            if(resolved.UsedDimensionRename==null)Dependencies(item);
            Need(!Has(table,edit.Name!),"Destination native style name already exists.","STYLE_EXISTS");
            item.UpgradeOpen();item.Name=edit.Name!;
            Need(item.Name==edit.Name&&Has(table,edit.Name!)&&Named(table,edit.Name!)==resolved.Target&&!Has(table,edit.ExpectedName),
                "Native rename did not retain the exact name/index/root identity.","NATIVE_STRUCTURE_MISMATCH");
        }
        else
        {
            Dependencies(item);Need(resolved.Target!=Current(kind,db),"Current native record cannot be deleted.","STRUCTURE_PROTECTED");
            PurgeEligible(resolved.Target,db,job.CheckCancellation);item.UpgradeOpen();item.Erase(true);
            Need(resolved.Target.IsErased&&!Has(table,edit.ExpectedName),"Native record deletion did not erase the requested record/name index.","NATIVE_STRUCTURE_MISMATCH");
        }
        bool deleting=StructureLifecycleValidation.IsDelete(edit);
        if(!deleting)Need(item.ObjectId==resolved.Target&&item.OwnerId==resolved.Table&&
            SameProperties(resolved.Properties,Properties(item,resolved.Reference,tr,db,job.CheckCancellation)),
            "Native operation changed the retained record's metadata/identity/owner.","NATIVE_STRUCTURE_MISMATCH");
        if(resolved.UsedDimensionRename is {} retained)
            CadUsedDimensionStyleRename.Verify(retained,resolved.Reference,db,tr,job.CheckCancellation,"NATIVE_STRUCTURE_MISMATCH");
        var expectedInventory=resolved.Inventory;
        if(deleting||StructureLifecycleValidation.IsRename(edit))
        {
            var entries=resolved.Inventory.EnumerateArray().Where(entry=>!deleting||entry.GetProperty("handle").GetString()!=edit.Handle)
                .Select(entry=>Wire.Element(new{handle=entry.GetProperty("handle").GetString(),Name=entry.GetProperty("handle").GetString()==edit.Handle?edit.Name:entry.GetProperty("Name").GetString(),
                    type=entry.GetProperty("type").GetString(),owner=entry.GetProperty("owner").GetString()}));
            expectedInventory=Wire.Element(entries.OrderBy(entry=>JsonSerializer.Serialize(entry,Wire.Json),StringComparer.Ordinal));
        }
        Need(ModelParameterSnapshot.Equivalent(expectedInventory,Inventory(table,tr,null,job.CheckCancellation)),"Native operation changed unselected symbol-table identities/names.","NATIVE_STRUCTURE_MISMATCH");
        Need(db.Clayer==resolved.CurrentLayer&&db.Textstyle==(edit.Operation=="set_current_text_style"?resolved.Target:resolved.CurrentTextStyle)&&
            db.Dimstyle==(edit.Operation=="set_current_dimension_style"?resolved.Target:resolved.CurrentDimensionStyle),
            "Native operation changed an unrelated current record identity.","NATIVE_STRUCTURE_MISMATCH");
        return new(index,edit.Operation,resolved.Reference,kind,edit.Name??edit.ExpectedName,false);
    }
}
