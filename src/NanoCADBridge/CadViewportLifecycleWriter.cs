using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using NanoCAD.Contracts;
using NanoCAD.Ipc;
using Teigha.DatabaseServices;
using Teigha.Geometry;
using Application=HostMgd.ApplicationServices.Application;

namespace NanoCADBridge;

// Existing ordinary Paper viewport lifecycle, without a change of active
// sheet/model camera or any ownership of the model geometry it displays.
internal static class CadViewportLifecycleWriter
{
    internal sealed record ExistingEntity(ObjectId Id,string AppearanceDigest,string? ViewDigest,bool Overview);
    internal sealed record Resolved(ViewportLifecycleEdit Edit,ObjectId Layout,ObjectId Block,ObjectId Source,ObjectId Overview,
        EntityRef Context,string Name,LayoutProperties LayoutBefore,ViewportProperties Before,JsonElement Appearance,
        ViewportDataValue[] NativeData,JsonElement NamedViews,ExistingEntity[] Entities);
    internal sealed record Prepared(ViewportProperties? Expected,Viewport? DetachedViewport=null);
    internal sealed record Applied(StructureChange Change,ViewportLifecycleChange Result);
    internal static ViewportLifecycleChange ProjectResult(int index,Resolved resolved)=>new(index,resolved.Edit.Operation,
        Ref(resolved.Context,resolved.Layout),resolved.Context,resolved.Edit is CopyViewportEdit
            ?resolved.Context with{handle="FFFFFFFFFFFFFFFF"}:null,resolved.Edit is DeleteViewportEdit);
    private static void Need(bool value,string message,string code="UNSUPPORTED_VIEWPORT")
    {if(!value)throw new DomainException(code,message);}
    private static EntityRef Ref(EntityRef context,ObjectId id)=>new(context.bridge_instance_id,context.document_id,Validation.NormalizeHandle(id.Handle.ToString()));
    private static bool Gone(ObjectId id)=>!id.IsValid||id.IsErased||id.IsEffectivelyErased;
    private static JsonElement Appearance(Entity entity)=>Wire.Element(new{
        runtime_type=entity.GetType().FullName,layer_handle=Validation.NormalizeHandle(entity.LayerId.Handle.ToString()),
        linetype_handle=Validation.NormalizeHandle(entity.LinetypeId.Handle.ToString()),properties=EntityAdapter.StoredProperties(entity)});
    private static void Same(JsonElement expected,JsonElement actual,string message)
    {Need(expected.GetRawText()==actual.GetRawText(),message,"VIEWPORT_VERIFICATION_FAILED");}
    private static string Digest(JsonElement value)=>Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(value,Wire.Json)));
    private static string ViewDigest(ViewportProperties value)=>Digest(Wire.Element(value with{number=0,
        frozen_layers=value.frozen_layers.OrderBy(layer=>layer.@ref?.handle,StringComparer.Ordinal).ToArray()}));
    private static ViewportProperties Read(Viewport source,Layout layout,Resolved context,Transaction tr,Database db,Action cancel)=>
        LayoutMetadataReader.ReadViewport(source,layout,tr,db,id=>Ref(context.Context,id),cancel,cancel);
    private static void Active(Database db,ObjectId layoutId,ObjectId blockId,ObjectId overview,Transaction tr,Action cancel)
    {
        cancel();var document=Application.DocumentManager.MdiActiveDocument;
        Need(document!=null&&document.Database==db&&!db.TileMode&&db.CurrentSpaceId==blockId,
            "Activate the explicit Paper layout outside its model viewport before viewport lifecycle edits.","LAYOUT_NOT_ACTIVE");
        object value=Application.GetSystemVariable("CVPORT");
        int number=value switch{int item=>item,short item=>item,_=>throw new DomainException("VIEW_UNAVAILABLE","Unexpected native CVPORT value.")};
        Need(number==1,"Viewport lifecycle requires Paper CVPORT1; the active model viewport is protected.","ACTIVE_VIEWPORT_PROTECTED");
        string name=LayoutManager.Current.CurrentLayout;var active=LayoutManager.Current.GetLayoutId(name);
        Need(active==layoutId&&tr.GetObject(active,OpenMode.ForRead) is Layout layout&&layout.GetType()==typeof(Layout)&&
            layout.OwnerId==db.LayoutDictionaryId&&layout.LayoutName==name&&!layout.ModelType&&layout.BlockTableRecordId==blockId,
            "Native active layout identity differs from the explicit Paper layout.","LAYOUT_NOT_ACTIVE");
        Need(document!.Editor.CurrentViewportObjectId==overview,
            "Native current viewport is not the explicit layout's reserved overview viewport1.","ACTIVE_VIEWPORT_PROTECTED");
        cancel();
    }
    private static ViewportDataValue[] Plain(Viewport viewport,Transaction tr,Action cancel)
    {
        cancel();CadViewportWriter.RequireNoExtensionContent(viewport,tr,cancel);
        using var reactors=viewport.GetPersistentReactorIds();
        Need(reactors.Count==0,"Viewport has persistent dependencies; its owning adapter must manage copying/deletion.","LINKED_VIEWPORT_PROTECTED");
        // Canonical ACAD/MVIEW contains native view settings only. The reader
        // rejects unknown application records without discarding their data.
        var nativeData=CadViewportWriter.ReadNativeData(viewport,cancel);cancel();return nativeData;
    }
    private static JsonElement NamedViews(Database db,Transaction tr,ObjectId layout,Action cancel)
    {
        var table=(ViewTable)tr.GetObject(db.ViewTableId,OpenMode.ForRead);var result=new List<object>();
        var budget=new ModelInventoryBudget(cancel);
        foreach(ObjectId id in table)
        {
            cancel();if(Gone(id))continue;
            var item=tr.GetObject(id,OpenMode.ForRead) as ViewTableRecord;
            Need(item!=null&&item.GetType()==typeof(ViewTableRecord)&&item.OwnerId==table.ObjectId,
                "A named view has an unsupported native owner/type; viewport linkage is uncertain.","LINKED_VIEWPORT_PROTECTED");
            bool associated=item!.ViewAssociatedToViewport;var owner=item.Layout;bool paper=item.IsPaperspaceView;
            bool available=!owner.IsNull&&!Gone(owner);bool otherPaper=false;
            if(available)
            {
                var target=tr.GetObject(owner,OpenMode.ForRead);
                available=target.GetType()==typeof(Layout)&&target.OwnerId==db.LayoutDictionaryId;
                otherPaper=available&&target is Layout ownerLayout&&!ownerLayout.ModelType&&owner!=layout;
            }
            // LinkedToSheetView is a known NotImplemented SDK getter. Use its
            // callable native named-view association/layout graph instead; an
            // unavailable association owner cannot establish independence.
            Need(!associated||otherPaper,
                "A named view is associated with a viewport on this sheet, or its association owner is unavailable. Preserve it through the owning sheet-view route.","LINKED_VIEWPORT_PROTECTED");
            Need(!paper||available,"A Paper named view has unavailable layout ownership; viewport linkage is uncertain.","LINKED_VIEWPORT_PROTECTED");
            var observed=new{handle=Validation.NormalizeHandle(id.Handle.ToString()),item.Name,associated,paper,
                layout_handle=owner.IsNull?null:Validation.NormalizeHandle(owner.Handle.ToString())};
            budget.Add(observed);result.Add(observed);
        }
        cancel();return Wire.Element(result);
    }
    internal static Resolved Resolve(StructureWriteArguments args,ViewportLifecycleEdit edit,Database db,Transaction tr,Action cancel,Resolved? baseline=null)
    {
        ViewportLifecycleValidation.Validate(edit);cancel();
        var layoutId=CadStructureWriter.Find(db,edit.LayoutHandle);var sourceId=CadStructureWriter.Find(db,edit.Handle);
        var layout=tr.GetObject(layoutId,OpenMode.ForRead) as Layout;
        Need(layout!=null&&layout.GetType()==typeof(Layout)&&layout.OwnerId==db.LayoutDictionaryId&&!layout.ModelType,
            "Expected an exact native Paper layout from this document.");
        var block=tr.GetObject(layout!.BlockTableRecordId,OpenMode.ForRead) as BlockTableRecord;
        Need(block!=null&&block.GetType()==typeof(BlockTableRecord)&&block.OwnerId==db.BlockTableId&&block.IsLayout&&block.LayoutId==layoutId&&
            !block.IsDependent&&!block.IsFromExternalReference&&!block.IsFromOverlayReference,"Paper-space ownership is inconsistent or dependent.");
        var source=tr.GetObject(sourceId,OpenMode.ForRead) as Viewport;
        Need(source!=null&&source.GetType()==typeof(Viewport)&&source.OwnerId==block!.ObjectId,
            "Viewport is not owned by the explicit Paper layout.","VIEWPORT_NOT_IN_LAYOUT");
        var layer=tr.GetObject(source!.LayerId,OpenMode.ForRead) as LayerTableRecord;
        Need(layer!=null&&layer.GetType()==typeof(LayerTableRecord)&&layer.OwnerId==db.LayerTableId&&!layer.IsDependent&&
            !layer.IsLocked&&!layer.IsOff&&!layer.IsFrozen,"Viewport layer must be local, visible, thawed and unlocked.","LAYER_NOT_WRITABLE");
        var context=new EntityRef(args.BridgeId,args.DocumentId,edit.Handle);
        Need(baseline==null||baseline.Layout==layoutId&&baseline.Block==block!.ObjectId&&
            baseline.Context.bridge_instance_id==args.BridgeId&&baseline.Context.document_id==args.DocumentId,
            "Viewport lifecycle baseline belongs to a different layout/document.","OUT_OF_SCOPE");
        var properties=baseline?.LayoutBefore??LayoutMetadataReader.ReadLayout(layout,tr,db,id=>Ref(context,id),cancel,cancel);
        Need(properties.plot.paper_units_native_value==1,"Viewport lifecycle requires a Paper layout using millimeters.");
        bool sameSource=baseline?.Source==sourceId;
        var before=sameSource?baseline!.Before:LayoutMetadataReader.ReadViewport(source,layout,tr,db,id=>Ref(context,id),cancel,cancel);
        ViewportLifecycleValidation.RequireOrdinary(before);
        var nativeData=sameSource?baseline!.NativeData:Plain(source,tr,cancel);
        if(edit is CopyViewportEdit copy)
            Need(Math.Abs(copy.Center[0])+before.width/2<=1e9&&Math.Abs(copy.Center[1])+before.height/2<=1e9,
                "Copied paper frame exceeds the supported coordinate bound.","INVALID_ARGUMENT");
        ExistingEntity[] entities;ObjectId overview;
        if(baseline!=null){entities=baseline.Entities;overview=baseline.Overview;}
        else
        {
            var records=new List<ExistingEntity>();var overviews=new List<ObjectId>();
            var budget=new ModelInventoryBudget(cancel);
            foreach(ObjectId id in block!)
            {
                cancel();if(Gone(id))continue;var entity=tr.GetObject(id,OpenMode.ForRead) as Entity;
                Need(entity!=null&&entity.OwnerId==block.ObjectId,"Paper-space entity ownership is inconsistent.");
                ViewportProperties? view=null;
                if(entity is Viewport viewport)
                {
                    view=LayoutMetadataReader.ReadViewport(viewport,layout,tr,db,value=>Ref(context,value),cancel,cancel);
                    if(view.number==1)overviews.Add(id);
                }
                // Keep compact exact hashes for unrelated stored state. Do not
                // retain every frozen-layer array once per requested copy.
                var observed=new ExistingEntity(id,Digest(Appearance(entity!)),view==null?null:ViewDigest(view),view?.number==1);
                budget.Add(new{handle=Validation.NormalizeHandle(id.Handle.ToString()),observed.AppearanceDigest,observed.ViewDigest,observed.Overview});
                records.Add(observed);
            }
            Need(overviews.Count==1,"Paper layout must contain exactly one native overview viewport1.","LAYOUT_NOT_INITIALIZED");
            entities=records.ToArray();overview=overviews[0];
        }
        Active(db,layoutId,block!.ObjectId,overview,tr,cancel);
        return new(edit,layoutId,block.ObjectId,sourceId,overview,context,layout.LayoutName+":"+before.number.ToString(CultureInfo.InvariantCulture),
            properties,before,sameSource?baseline!.Appearance:Appearance(source),nativeData,
            baseline?.NamedViews??NamedViews(db,tr,layoutId,cancel),entities);
    }
    internal static Prepared Preview(Resolved resolved,Transaction tr,Database db,List<DBObject> owned,Action cancel)
    {
        Active(db,resolved.Layout,resolved.Block,resolved.Overview,tr,cancel);
        if(resolved.Edit is DeleteViewportEdit)return new(null);
        var edit=(CopyViewportEdit)resolved.Edit;var source=(Viewport)tr.GetObject(resolved.Source,OpenMode.ForRead);
        cancel();var cloned=source.Clone();
        // Only a proved distinct exact detached wrapper belongs to our cleanup.
        // Never dispose a resident/source/previously owned alias on refusal.
        Need(cloned is Viewport candidate&&candidate.GetType()==typeof(Viewport)&&candidate.ObjectId.IsNull&&candidate.OwnerId.IsNull&&
            candidate.UnmanagedObject!=IntPtr.Zero&&candidate.UnmanagedObject!=source.UnmanagedObject&&
            !owned.Any(item=>item.UnmanagedObject==candidate.UnmanagedObject),
            "Viewport clone returned an unsafe type/owner/native alias; ownership was not adopted.","VIEWPORT_COPY_MISMATCH");
        var clone=(Viewport)cloned;var clonePointer=clone.UnmanagedObject;owned.Add(clone);
        // The installed native virtual copy route is exercised on a detached
        // object first. A caller cannot reach resident insertion unless its
        // entire observed camera, frozen set, display and MVIEW are preserved.
        cancel();clone.CopyFrom(source);cancel();
        Need(clone.GetType()==typeof(Viewport)&&clone.ObjectId.IsNull&&clone.OwnerId.IsNull&&clone.UnmanagedObject==clonePointer&&
            clonePointer!=source.UnmanagedObject&&owned.Count(item=>item.UnmanagedObject==clonePointer)==1,
            "Native viewport settings copy changed detached ownership.","VIEWPORT_COPY_MISMATCH");
        var layout=(Layout)tr.GetObject(resolved.Layout,OpenMode.ForRead);
        var initial=LayoutMetadataReader.ReadViewportDetached(clone,layout,tr,db,resolved.Context,id=>Ref(resolved.Context,id),cancel,cancel);
        ViewportLifecycleValidation.RequireCopy(resolved.Before,initial,false);Same(resolved.Appearance,Appearance(clone),"Detached viewport copy changed display settings.");
        ViewportMviewData.RequireSame(resolved.NativeData,CadViewportWriter.ReadNativeData(clone,cancel),cancel);
        SetCenter(clone,edit.Center,cancel);
        var expected=resolved.Before with{center=new[]{edit.Center[0],edit.Center[1],resolved.Before.center[2]},is_on=edit.IsOn??resolved.Before.is_on};
        ViewportLifecycleValidation.RequireCopy(expected,LayoutMetadataReader.ReadViewportDetached(clone,layout,tr,db,resolved.Context,id=>Ref(resolved.Context,id),cancel,cancel),false);
        ViewportMviewData.RequireSame(resolved.NativeData,CadViewportWriter.ReadNativeData(clone,cancel),cancel);cancel();return new(expected,clone);
    }
    private static void SetCenter(Viewport viewport,double[] center,Action cancel)
    {
        // A copy can be placed while preserving the source/copy locked state.
        // Do not unlock the retained source.
        bool locked=viewport.Locked;cancel();viewport.Locked=false;
        cancel();viewport.CenterPoint=new Point3d(center[0],center[1],viewport.CenterPoint.Z);
        cancel();viewport.Locked=locked;
    }
    internal static Applied Apply(int index,Resolved resolved,Prepared prepared,Transaction tr,Database db,WriteJob job,Action<string>? trace=null)
    {
        void Stage(string value)=>trace?.Invoke($"viewport_lifecycle_{index}_{value}");
        void CopyNeed(bool value,string message,string stage,object observation)
        {
            if(value)return;
            Stage("verification_failed_"+stage);
            throw new DomainException("VIEWPORT_COPY_MISMATCH",message,false,new{operation_index=index,stage,observation});
        }
        Stage("source_verify_begin");
        Active(db,resolved.Layout,resolved.Block,resolved.Overview,tr,job.CheckCancellation);
        var source=(Viewport)tr.GetObject(resolved.Source,OpenMode.ForRead);var layout=(Layout)tr.GetObject(resolved.Layout,OpenMode.ForRead);
        var current=Read(source,layout,resolved,tr,db,job.CheckCancellation);ViewportLifecycleValidation.RequireOrdinary(current);
        ViewportWriteValidation.RequireSame(resolved.Before with{number=current.number},current);
        Same(resolved.Appearance,Appearance(source),"Source viewport changed after selection.");Plain(source,tr,job.CheckCancellation);
        Same(resolved.NamedViews,NamedViews(db,tr,resolved.Layout,job.CheckCancellation),"Named-view associations changed after viewport selection.");
        var layoutRef=Ref(resolved.Context,resolved.Layout);
        if(resolved.Edit is DeleteViewportEdit)
        {
            job.CheckCancellation();var target=(Viewport)tr.GetObject(resolved.Source,OpenMode.ForWrite);
            target.Erase(true);job.CheckCancellation();
            Need(Gone(resolved.Source),"Native viewport erase did not erase the requested identity.","VIEWPORT_VERIFICATION_FAILED");
            return new(new(index,"delete_viewport",resolved.Context,"viewport",resolved.Name,false),
                new(index,"delete_viewport",layoutRef,resolved.Context,null,true));
        }
        // The original DeepClone route failed a mapped-graph postcondition
        // and entered the host's broken WasErased transaction-abort path (347).
        // Insert the already verified, owned, exact native clone instead.
        var copy=prepared.DetachedViewport;
        bool exactType=copy!=null&&copy.GetType()==typeof(Viewport);
        bool detached=exactType&&copy!.ObjectId.IsNull&&copy.OwnerId.IsNull;
        bool distinct=detached&&copy!.UnmanagedObject!=IntPtr.Zero&&copy.UnmanagedObject!=source.UnmanagedObject;
        CopyNeed(exactType&&detached&&distinct&&prepared.Expected!=null,"Prepared viewport copy is no longer a distinct detached object.","prepared_identity",
            new{exact_type=exactType,detached,distinct_native_pointer=distinct,has_expected_state=prepared.Expected!=null});
        var nativePointer=copy!.UnmanagedObject;
        Stage("open_block_begin");job.CheckCancellation();
        var block=(BlockTableRecord)tr.GetObject(resolved.Block,OpenMode.ForWrite);
        Stage("append_begin");job.CheckCancellation();var id=block.AppendEntity(copy);
        Stage("register_begin");tr.AddNewlyCreatedDBObject(copy,true);
        // Viewport AppendEntity resets native settings. Restore the complete
        // native object settings, not merely selected public camera fields.
        Stage("copy_settings_begin");job.CheckCancellation();copy.CopyFrom(source);job.CheckCancellation();
        bool live=!id.IsNull&&!Gone(id);bool sameIdentity=live&&copy.ObjectId==id;
        bool sameOwner=copy.OwnerId==resolved.Block;bool sameType=copy.GetType()==typeof(Viewport);
        bool samePointer=copy.UnmanagedObject==nativePointer&&nativePointer!=source.UnmanagedObject;
        CopyNeed(live&&sameIdentity&&sameOwner&&sameType&&samePointer&&id!=resolved.Source,
            "Native viewport settings copy changed its newly resident identity/owner.","resident_identity",
            new{live,same_identity=sameIdentity,same_owner=sameOwner,exact_type=sameType,same_native_pointer=samePointer,
                distinct_source=id!=resolved.Source});
        // Root identity alone does not prove extension-dictionary ownership.
        // In particular an empty dictionary must belong to this new viewport,
        // never be a borrowed source dictionary. Both retained/new roots must
        // still have exact native owned data and no persistent dependencies.
        Stage("owned_data_begin");Plain(copy,tr,job.CheckCancellation);Plain(source,tr,job.CheckCancellation);
        Stage("center_begin");SetCenter(copy,((CopyViewportEdit)resolved.Edit).Center,job.CheckCancellation);
        Stage("on_begin");job.CheckCancellation();copy.On=prepared.Expected!.is_on;
        // On/Number and native view bookkeeping are finalized only after
        // Commit and transaction disposal (the established creator097 path).
        // All camera/MVIEW/frozen/display/source/unrelated-Paper checks remain
        // in VerifyCommitted, with exact returned root provenance.
        Stage("verification_deferred_until_commit_disposal");
        var created=Ref(resolved.Context,id);
        return new(new(index,"copy_viewport",created,"viewport",resolved.Name+":"+created.handle,true),
            new(index,"copy_viewport",layoutRef,resolved.Context,created,false));
    }
    internal static void VerifyCommitted(IReadOnlyDictionary<int,Resolved> resolved,IReadOnlyDictionary<int,Prepared> prepared,
        IReadOnlyList<StructureChange> changes,Database db,Action cancel)
    {
        using var tr=db.TransactionManager.StartTransaction();
        foreach(var group in resolved.GroupBy(item=>item.Value.Layout))
        {
            var baseline=group.First().Value;Active(db,baseline.Layout,baseline.Block,baseline.Overview,tr,cancel);
            var layout=(Layout)tr.GetObject(baseline.Layout,OpenMode.ForRead);var block=(BlockTableRecord)tr.GetObject(baseline.Block,OpenMode.ForRead);
            Need(layout.BlockTableRecordId==baseline.Block&&block.LayoutId==baseline.Layout&&block.OwnerId==db.BlockTableId,
                "Committed Paper layout ownership changed.","VIEWPORT_VERIFICATION_FAILED");
            LayoutCreateValidation.RequireSame(baseline.LayoutBefore,LayoutMetadataReader.ReadLayout(layout,tr,db,id=>Ref(baseline.Context,id),cancel,cancel));
            Same(baseline.NamedViews,NamedViews(db,tr,baseline.Layout,cancel),"Viewport lifecycle changed named-view associations.");
            var deleted=group.Where(item=>item.Value.Edit is DeleteViewportEdit).Select(item=>item.Value.Source).ToHashSet();
            var copies=group.Where(item=>item.Value.Edit is CopyViewportEdit).ToDictionary(item=>item.Key,
                item=>CadStructureWriter.Find(db,changes.Single(change=>change.operation_index==item.Key).@ref.handle));
            Need(copies.Values.Distinct().Count()==copies.Count&&!baseline.Entities.Any(item=>copies.Values.Contains(item.Id)),
                "Committed copy provenance reused an old or another newly created identity.","VIEWPORT_VERIFICATION_FAILED");
            var expected=baseline.Entities.Where(item=>!deleted.Contains(item.Id)).Select(item=>item.Id).Concat(copies.Values).ToHashSet();
            var actual=new HashSet<ObjectId>();var numbers=new HashSet<int>();
            foreach(ObjectId id in block)
            {
                cancel();if(Gone(id))continue;actual.Add(id);
                if(tr.GetObject(id,OpenMode.ForRead) is Viewport viewport&&viewport.On)
                    Need(viewport.Number>0&&numbers.Add(viewport.Number),"Native active viewport numbers are invalid/repeated.","VIEWPORT_VERIFICATION_FAILED");
            }
            Need(expected.SetEquals(actual),"Viewport lifecycle changed unexpected Paper entity identities.","VIEWPORT_VERIFICATION_FAILED");
            foreach(var item in baseline.Entities)
            {
                cancel();if(deleted.Contains(item.Id)){Need(Gone(item.Id),"Requested viewport remains after committed deletion.","VIEWPORT_VERIFICATION_FAILED");continue;}
                var entity=tr.GetObject(item.Id,OpenMode.ForRead) as Entity;
                Need(entity!=null&&entity.OwnerId==baseline.Block,"Retained sheet entity ownership changed.","VIEWPORT_VERIFICATION_FAILED");
                Need(item.AppearanceDigest==Digest(Appearance(entity!)),"Viewport lifecycle changed retained sheet display settings/type/layer.","VIEWPORT_VERIFICATION_FAILED");
                if(item.ViewDigest!=null)
                {
                    var current=Read((Viewport)entity!,layout,baseline,tr,db,cancel);
                    Need(item.Overview==(current.number==1),"Reserved overview viewport identity changed.","VIEWPORT_VERIFICATION_FAILED");
                    Need(item.ViewDigest==ViewDigest(current),"Viewport lifecycle changed a retained native view/camera/frozen set.","VIEWPORT_VERIFICATION_FAILED");
                }
            }
            foreach(var item in group)
            {
                cancel();if(item.Value.Edit is DeleteViewportEdit)continue;
                var copy=(Viewport)tr.GetObject(copies[item.Key],OpenMode.ForRead);
                Need(copy.GetType()==typeof(Viewport)&&copy.OwnerId==baseline.Block,"Committed viewport copy type/owner differs from provenance.","VIEWPORT_VERIFICATION_FAILED");
                Plain(copy,tr,cancel);ViewportLifecycleValidation.RequireCopy(prepared[item.Key].Expected!,Read(copy,layout,baseline,tr,db,cancel),true);
                Same(item.Value.Appearance,Appearance(copy),"Committed viewport copy changed display settings.");
                ViewportMviewData.RequireSame(item.Value.NativeData,CadViewportWriter.ReadNativeData(copy,cancel),cancel);
                var source=(Viewport)tr.GetObject(item.Value.Source,OpenMode.ForRead);
                Plain(source,tr,cancel);
                ViewportMviewData.RequireSame(item.Value.NativeData,CadViewportWriter.ReadNativeData(source,cancel),cancel);
            }
        }
        cancel();
    }
}
