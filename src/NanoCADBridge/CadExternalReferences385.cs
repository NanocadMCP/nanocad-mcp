using System.Diagnostics.CodeAnalysis;
using System.Security.Cryptography;
using System.Text.Json;
using NanoCAD.Contracts;
using NanoCAD.Ipc;
using Teigha.DatabaseServices;
using Teigha.Geometry;

namespace NanoCADBridge;

// Native database references. File access here is read-only; no source DWG,
// raster or underlay is saved, copied, rewritten, deleted or detached on disk.
internal static class CadExternalReferences385
{
    internal sealed record FileStamp(string Path,long Length,string Sha256);
    internal sealed record Resolved(ExternalReferenceEdit385 Edit,ObjectId Space,ObjectId Layer,ObjectId Target,ObjectId Definition,
        string Kind,string Name,FileStamp? Source,JsonElement Before,Dictionary<ObjectId,JsonElement> Peers)
    { internal JsonElement DefinitionBefore {get;init;} }
    internal sealed record Prepared(DBObject? Definition,Entity? Entity)
    {
        // The caller releases these wrappers after its transaction has closed.
        // Raster/Xref creation happens in Apply, not in a detached load preview.
        internal List<DBObject>? CreatedObjects {get;init;}
    }
    static void Need([DoesNotReturnIf(false)] bool value,string message,string code="OUT_OF_SCOPE")
    {if(!value)throw new DomainException(code,message);}
    static bool Live(ObjectId id)=>!id.IsNull&&id.IsValid&&!id.IsErased&&!id.IsEffectivelyErased;
    static string Handle(ObjectId id)=>Validation.NormalizeHandle(id.Handle.ToString());
    static EntityRef Ref(ObjectId id,EntityRef context)=>new(context.bridge_instance_id,context.document_id,Handle(id));
    static JsonElement Element(object value,Action cancel)
    {
        cancel();var result=Wire.Element(value);
        Need(JsonSerializer.SerializeToUtf8Bytes(result,Wire.Json).Length<=Wire.PayloadBudget-16384,"Reference snapshot exceeds the actual response-byte budget; no reference count was truncated.","RESPONSE_LIMIT_EXCEEDED");
        return result;
    }
    static string Token(JsonElement value)=>Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(value,Wire.Json)));
    static bool Exact(JsonElement a,JsonElement b)=>a.GetRawText()==b.GetRawText();
    static double N(double value){Need(double.IsFinite(value),"Reference geometry contains a non-finite number.","OBJECT_READ_FAILED");return value==0?0:value;}
    static double[] P(Point3d p)=>[N(p.X),N(p.Y),N(p.Z)];
    static double[] V(Vector3d v)=>[N(v.X),N(v.Y),N(v.Z)];
    static object User(DBObject item,Transaction tr,Action cancel)
    {
        using var data=item.XData;using var reactors=item.GetPersistentReactorIds();
        return new{has_fields=item.HasFields,xdata=data==null?null:NativeOwnedMetadata.Buffer(data,cancel,_=>cancel()),reactors=reactors.Cast<ObjectId>().Select(Handle).OrderBy(h=>h,StringComparer.Ordinal).ToArray(),owned=NativeOwnedMetadata.Capture(item,tr,cancel)};
    }
    static void RequireRoot(Entity entity,Database db,Transaction tr)
    {
        Need(!entity.HasFields,"Field-bearing reference roots need their field-owning operation.");
        using var reactors=entity.GetPersistentReactorIds();
        foreach(ObjectId id in reactors)
            Need(entity is RasterImage image&&id==image.ReactorId&&Live(id)&&tr.GetObject(id,OpenMode.ForRead).Database?.UnmanagedObject==db.UnmanagedObject,"Reference root has an unclassified persistent dependency; use its owning operation.");
        Need(entity.Database!=null&&entity.Database.UnmanagedObject==db.UnmanagedObject,"Reference root belongs to another database.");
    }
    internal static string Kind(Entity entity)=>entity.GetType()==typeof(BlockReference)?"xref":entity.GetType()==typeof(RasterImage)?"raster_image":entity.GetType()==typeof(DwfReference)?"dwf":entity.GetType()==typeof(DgnReference)?"dgn":entity.GetType()==typeof(PdfReference)?"pdf":
        throw new DomainException("UNSUPPORTED_ENTITY_TYPE","Expected a native Xref insertion, RasterImage or DWF/DGN/PDF underlay reference.");
    static ObjectId Definition(Entity entity)=>entity switch{BlockReference b=>b.BlockTableRecord,RasterImage image=>image.ImageDefId,UnderlayReference underlay=>underlay.DefinitionId,_=>ObjectId.Null};
    static string DictionaryKey(string kind)=>UnderlayDefinition.GetDictionaryKey(kind=="dwf"?typeof(DwfDefinition):kind=="dgn"?typeof(DgnDefinition):typeof(PdfDefinition));
    static ObjectId ExistingDictionary(Database db,Transaction tr,string kind)
    {
        // Installed GetImageDictionary calls createImageDictionary. Never call
        // that write-capable convenience method from a read/preflight route.
        var nod=(DBDictionary)tr.GetObject(db.NamedObjectsDictionaryId,OpenMode.ForRead);
        if(kind=="raster_image")
        {
            ObjectId observed=ObjectId.Null;
            foreach(DBDictionaryEntry entry in nod)
            {
                if(!Live(entry.Value)||tr.GetObject(entry.Value,OpenMode.ForRead) is not DBDictionary dictionary)continue;
                if(dictionary.GetType()!=typeof(DBDictionary)||dictionary.ObjectId!=entry.Value||dictionary.Database?.UnmanagedObject!=db.UnmanagedObject||dictionary.OwnerId!=db.NamedObjectsDictionaryId)continue;
                foreach(DBDictionaryEntry child in dictionary)
                {
                    if(!Live(child.Value))continue;var image=tr.GetObject(child.Value,OpenMode.ForRead);
                    if(image.GetType()!=typeof(RasterImageDef)||image.ObjectId!=child.Value||image.Database?.UnmanagedObject!=db.UnmanagedObject||image.OwnerId!=dictionary.ObjectId)continue;
                    Need(dictionary.Cast<DBDictionaryEntry>().Count(e=>e.Value==image.ObjectId)==1,"Raster definition has ambiguous current dictionary membership.");
                    Need(observed.IsNull||observed==entry.Value,"Raster definitions have ambiguous current dictionary ownership.");observed=entry.Value;
                }
            }
            return observed; // An empty dictionary is identified only by the native create route after write starts.
        }
        string key=DictionaryKey(kind);
        return nod.Contains(key)?nod.GetAt(key):ObjectId.Null;
    }
    static DBObject RequireDefinition(ObjectId id,string kind,Database db,Transaction tr)
    {
        Need(Live(id),"The reference definition is unavailable.");var item=tr.GetObject(id,OpenMode.ForRead);
        Need(item.Database!=null&&item.Database.UnmanagedObject==db.UnmanagedObject,"Reference definition belongs to another database.");
        if(kind=="xref")
        {
            Need(item.GetType()==typeof(BlockTableRecord)&&item is BlockTableRecord,"Expected a native xref block definition.");var block=(BlockTableRecord)item;
            Need(block.OwnerId==db.BlockTableId&&!block.IsLayout&&!block.IsAnonymous&&!block.IsDynamicBlock&&!block.Name.Contains('|')&&(block.IsFromExternalReference||block.IsFromOverlayReference),"Only a direct host-owned xref definition is eligible; nested/dependent definitions need their owning host.");
        }
        else
        {
            Type expected=kind=="raster_image"?typeof(RasterImageDef):kind=="dwf"?typeof(DwfDefinition):kind=="dgn"?typeof(DgnDefinition):typeof(PdfDefinition);
            ObjectId dictionary=ExistingDictionary(db,tr,kind);
            Need(item.GetType()==expected&&Live(dictionary)&&item.OwnerId==dictionary,"Reference definition has an unexpected native type or dictionary owner.");
            var records=(DBDictionary)tr.GetObject(dictionary,OpenMode.ForRead);
            Need(records.OwnerId==db.NamedObjectsDictionaryId&&records.Cast<DBDictionaryEntry>().Count(entry=>entry.Value==id)==1,"Reference definition is not indexed exactly once by its native dictionary.");
        }
        return item;
    }
    static ObjectId[] References(ObjectId definition,string kind,Database db,Transaction tr,Action cancel,bool strict=true)
    {
        var result=new List<ObjectId>();long serializedBytes=1024;var blocks=(BlockTable)tr.GetObject(db.BlockTableId,OpenMode.ForRead);
        foreach(ObjectId blockId in blocks)
        {
            cancel();if(!Live(blockId))continue;var block=(BlockTableRecord)tr.GetObject(blockId,OpenMode.ForRead);
            foreach(ObjectId id in block)
            {
                cancel();if(!Live(id)||tr.GetObject(id,OpenMode.ForRead) is not Entity entity)continue;
                bool supported=kind=="xref"?entity is BlockReference:kind=="raster_image"?entity is RasterImage:entity is UnderlayReference;
                if(!supported||Definition(entity)!=definition)continue;
                if(strict){Need(Kind(entity)==kind&&block.IsLayout&&block.OwnerId==db.BlockTableId,"Reference is also used by a nested/custom occurrence; this lifecycle needs its owning operation.");CadBlockSpace.RequireSpace(db,tr,entity.OwnerId);}result.Add(id);
                serializedBytes+=JsonSerializer.SerializeToUtf8Bytes(Handle(id),Wire.Json).Length+1;
                Need(serializedBytes<=Wire.PayloadBudget-16384,"Reference inventory exceeds the actual response-byte budget.","RESPONSE_LIMIT_EXCEEDED");
            }
        }
        return result.OrderBy(Handle,StringComparer.Ordinal).ToArray();
    }
    static object Pose(Entity entity)
    {
        if(entity is BlockReference block)return new{position=P(block.Position),rotation=N(block.Rotation),scale_factors=new[]{N(block.ScaleFactors.X),N(block.ScaleFactors.Y),N(block.ScaleFactors.Z)},normal=V(block.Normal)};
        if(entity is RasterImage image)return CadRasterImageDisplay390.Read(image);
        var underlay=(UnderlayReference)entity;
        return new{position=P(underlay.Position),rotation=N(underlay.Rotation),scale_factors=new[]{N(underlay.ScaleFactors.X),N(underlay.ScaleFactors.Y),N(underlay.ScaleFactors.Z)},normal=V(underlay.Normal),is_on=underlay.IsOn,is_clipped=underlay.IsClipped,clip_boundary=underlay.GetClipBoundary().Select(p=>new[]{N(p.X),N(p.Y)}).ToArray(),fade=underlay.Fade,contrast=underlay.Contrast,monochrome=underlay.Monochrome,adjust_color_for_background=underlay.AdjustColorForBackground};
    }
    static JsonElement Root(Entity entity,Transaction tr,Action cancel,bool pose=true)=>Element(new{handle=Handle(entity.ObjectId),owner=Handle(entity.OwnerId),definition=Handle(Definition(entity)),native_type=entity.GetType().FullName,placement=pose?Pose(entity):null,display=EntityAdapter.StoredProperties(entity),user_data=User(entity,tr,cancel)},cancel);
    static JsonElement DefinitionState(DBObject item,string kind,Database db,Transaction tr,Action cancel)
    {
        string path=item switch{BlockTableRecord pathBlock=>pathBlock.PathName,RasterImageDef image=>image.SourceFileName,UnderlayDefinition u=>u.SourceFileName,_=>""};
        string name=item is BlockTableRecord block?block.Name:((DBDictionary)tr.GetObject(item.OwnerId,OpenMode.ForRead)).Cast<DBDictionaryEntry>().Single(entry=>entry.Value==item.ObjectId).Key;
        string? itemName=item is UnderlayDefinition underlay?underlay.ItemName:null;
        return Element(new{handle=Handle(item.ObjectId),owner=Handle(item.OwnerId),native_type=item.GetType().FullName,reference_kind=kind,name,source_path=path,item_name=itemName,overlay=item is BlockTableRecord b&&b.IsFromOverlayReference,reference_handles=References(item.ObjectId,kind,db,tr,cancel,false).Select(Handle).ToArray()},cancel);
    }
    internal static object ReadDefinition(DBObject item,string kind,EntityRef context,Database db,Transaction tr,Action cancel)
    {
        var state=DefinitionState(item,kind,db,tr,cancel);string? reason=null;string? token=null;
        try{RequireDefinition(item.ObjectId,kind,db,tr);References(item.ObjectId,kind,db,tr,cancel);token=Token(state);}catch(DomainException e){reason=e.Error.message;}
        return new{definition_ref=Ref(item.ObjectId,context),native_type=item.GetType().Name,reference_kind=kind,name=state.GetProperty("name").GetString(),source_path=state.GetProperty("source_path").GetString(),item_name=state.GetProperty("item_name"),overlay=state.GetProperty("overlay"),reference_refs=state.GetProperty("reference_handles").EnumerateArray().Select(h=>new EntityRef(context.bridge_instance_id,context.document_id,h.GetString()!)).ToArray(),loaded=item is RasterImageDef image?(bool?)image.IsLoaded:item is UnderlayDefinition u?u.Loaded:null,image_pixel_size=item is RasterImageDef pixels?new[]{N(pixels.Size.X),N(pixels.Size.Y)}:null,status=item is BlockTableRecord block?block.XrefStatus.ToString():null,reference_token=token,editing_unavailable_reason=reason};
    }
    internal static object Read(Entity entity,EntityRef context,Transaction tr,Action cancel)
    {
        string kind=Kind(entity);var db=entity.Database;Need(db!=null,"Resident reference database unavailable.","OBJECT_READ_FAILED");ObjectId def=Definition(entity);
        Need(Live(def),"Reference definition is unavailable.","OBJECT_READ_FAILED");var item=tr.GetObject(def,OpenMode.ForRead);
        string? token=null;string? reason=null;
        try{RequireDefinition(def,kind,db,tr);RequireRoot(entity,db,tr);CadBlockSpace.RequireSpace(db,tr,entity.OwnerId);token=Token(Root(entity,tr,cancel));}catch(DomainException e){reason=e.Error.message;}
        var result=new{reference_kind=kind,native_type=entity.GetType().Name,owner_ref=Ref(entity.OwnerId,context),definition_ref=Ref(def,context),placement=Pose(entity),definition=ReadDefinition(item,kind,context,db,tr,cancel),reference_token=token,editing_unavailable_reason=reason};
        Element(result,cancel);return result;
    }
    static string SourcePath(DBObject definition,string kind,Database db)
    {
        string path=definition switch{BlockTableRecord b=>b.PathName,RasterImageDef image=>image.SourceFileName,UnderlayDefinition u=>u.SourceFileName,_=>""};
        if(!Path.IsPathRooted(path))
        {Need(!string.IsNullOrEmpty(db.Filename)&&Path.IsPathRooted(db.Filename),"A relative reference source requires a named host drawing.");path=Path.GetFullPath(Path.Combine(Path.GetDirectoryName(db.Filename)!,path));}
        return ExternalReferenceValidation385.NormalizePath(path,kind);
    }
    static FileStamp? File(string path,bool required,Action cancel)
    {
        cancel();if(!System.IO.File.Exists(path)){Need(!required,"Reference source file does not exist.","SOURCE_FILE_UNAVAILABLE");return null;}
        try
        {
            using var stream=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.ReadWrite|FileShare.Delete);using var hash=SHA256.Create();
            byte[] buffer=new byte[65536];int count;long length=0;
            while((count=stream.Read(buffer,0,buffer.Length))>0){cancel();hash.TransformBlock(buffer,0,count,buffer,0);length+=count;}
            hash.TransformFinalBlock(Array.Empty<byte>(),0,0);return new(path,length,Convert.ToHexString(hash.Hash!));
        }
        catch(IOException e){throw new DomainException("SOURCE_FILE_UNAVAILABLE","Reference source cannot be read: "+e.Message);}
        catch(UnauthorizedAccessException e){throw new DomainException("SOURCE_FILE_UNAVAILABLE","Reference source cannot be read: "+e.Message);}
    }
    static void CheckSource(FileStamp? source,Action cancel)
    {if(source!=null){var after=File(source.Path,true,cancel);Need(after==source,"Reference source file changed during the operation; no source file was written by this operation.","SOURCE_FILE_CHANGED");}}
    static void Plane(Entity entity)
    {
        if(entity is RasterImage image)
        {
            var p=image.Orientation;Need(Math.Abs(p.Origin.Z)<=1e-9&&Math.Abs(p.Xaxis.Z)<=1e-9&&Math.Abs(p.Yaxis.Z)<=1e-9&&p.Xaxis.Length>0&&p.Yaxis.Length>0&&Math.Abs(p.Xaxis.GetNormal().DotProduct(p.Yaxis.GetNormal()))<=1e-9&&p.Xaxis.CrossProduct(p.Yaxis).Z>0,"Raster placement edits require a positive orthogonal WCS XY frame; the stored clipped image remains attached.");return;
        }
        Point3d position;Vector3d normal;Scale3d scale;
        if(entity is BlockReference b){position=b.Position;normal=b.Normal;scale=b.ScaleFactors;}
        else{var u=(UnderlayReference)entity;position=u.Position;normal=u.Normal;scale=u.ScaleFactors;}
        Need(Math.Abs(position.Z)<=1e-9&&Math.Abs(normal.X)<=1e-9&&Math.Abs(normal.Y)<=1e-9&&Math.Abs(normal.Z-1)<=1e-9&&scale.X>0&&scale.X==scale.Y&&scale.X==scale.Z,"Reference placement edits require WCS XY/Z0/+Z and positive uniform scale.");
    }
    internal static Resolved Resolve(ExternalReferenceEdit385 edit,StructureWriteArguments args,Database db,Transaction tr,ObjectId space,Func<ObjectId,LayerWriteState> layers,Action cancel)
    {
        string? path=edit switch{AttachXref385 e=>e.SourcePath,InsertRasterImage385 e=>e.SourcePath,AttachUnderlay385 e=>e.SourcePath,_=>null};
        string kind=edit switch{AttachXref385=>"xref",InsertRasterImage385=>"raster_image",AttachUnderlay385 e=>e.Format,SetReferencePlacement385 e=>e.ReferenceKind,SetRasterImageDisplay390 or SetRasterImageClip391=>"raster_image",ReferenceLifecycle385 e=>e.ReferenceKind,_=>throw new DomainException("INVALID_ARGUMENT","Unknown reference operation.")};
        string name=edit switch{AttachXref385 e=>e.Name,InsertRasterImage385 e=>e.Name,AttachUnderlay385 e=>e.Name,_=>""};
        string? layerName=edit switch{AttachXref385 e=>e.Layer,InsertRasterImage385 e=>e.Layer,AttachUnderlay385 e=>e.Layer,_=>null};
        var layer=ObjectId.Null;var target=ObjectId.Null;var definition=ObjectId.Null;JsonElement before=Wire.Element(new{});var peers=new Dictionary<ObjectId,JsonElement>();
        if(path!=null)
        {
            Need(string.IsNullOrEmpty(db.Filename)||!string.Equals(Path.GetFullPath(db.Filename),path,StringComparison.OrdinalIgnoreCase),"The host drawing cannot attach itself.");
            if(kind=="xref")Need(!((BlockTable)tr.GetObject(db.BlockTableId,OpenMode.ForRead)).Has(name),"Xref definition name already exists.","BLOCK_EXISTS");
            else{ObjectId dictionary=ExistingDictionary(db,tr,kind);if(Live(dictionary))Need(!((DBDictionary)tr.GetObject(dictionary,OpenMode.ForRead)).Contains(name),"Reference definition name already exists.","DEFINITION_EXISTS");}
            var table=(LayerTable)tr.GetObject(db.LayerTableId,OpenMode.ForRead);Need(table.Has(layerName!),"Reference layer must already exist.","LAYER_UNAVAILABLE");layer=table[layerName!];layers(layer).RequireAttributeWrite();
            CadBlockSpace.RequireSpace(db,tr,space);return new(edit,space,layer,target,definition,kind,name,File(path,true,cancel),before,peers);
        }
        string handle=edit switch{SetReferencePlacement385 p=>p.Handle,SetRasterImageDisplay390 p=>p.Handle,SetRasterImageClip391 p=>p.Handle,ReferenceLifecycle385 p=>p.Handle,_=>throw new DomainException("INVALID_ARGUMENT","Unknown reference edit.")};
        string expected=edit switch{SetReferencePlacement385 q=>q.ReferenceToken,SetRasterImageDisplay390 q=>q.ReferenceToken,SetRasterImageClip391 q=>q.ReferenceToken,ReferenceLifecycle385 q=>q.ReferenceToken,_=>throw new DomainException("INVALID_ARGUMENT","Unknown reference edit.")};target=CadStructureWriter.Find(db,handle);
        bool definitionOperation=edit is ReferenceLifecycle385{Action:"reload_xref" or "detach_xref" or "reload_reference"};
        if(definitionOperation)
        {
            definition=target;var item=RequireDefinition(definition,kind,db,tr);before=DefinitionState(item,kind,db,tr,cancel);name=before.GetProperty("name").GetString()!;
            foreach(ObjectId id in References(definition,kind,db,tr,cancel))
            {var entity=(Entity)tr.GetObject(id,OpenMode.ForRead);RequireRoot(entity,db,tr);layers(entity.LayerId).RequireAttributeWrite();peers.Add(id,Root(entity,tr,cancel));}
            path=SourcePath(item,kind,db);
        }
        else
        {
            Need(tr.GetObject(target,OpenMode.ForRead) is Entity,"Expected a resident native reference entity.");var entity=(Entity)tr.GetObject(target,OpenMode.ForRead);
            Need(Kind(entity)==kind,"reference_kind differs from the actual native entity.");definition=Definition(entity);var item=RequireDefinition(definition,kind,db,tr);
            Need(entity.OwnerId==space,"Reference entity is outside the requested active target_space.");CadBlockSpace.RequireSpace(db,tr,entity.OwnerId);layers(entity.LayerId).RequireAttributeWrite();
            RequireRoot(entity,db,tr);if(edit is SetReferencePlacement385)Plane(entity);
            before=Root(entity,tr,cancel);name=kind=="xref"?((BlockTableRecord)item).Name:((DBDictionary)tr.GetObject(item.OwnerId,OpenMode.ForRead)).Cast<DBDictionaryEntry>().Single(entry=>entry.Value==definition).Key;
            foreach(ObjectId id in References(definition,kind,db,tr,cancel)){if(id!=target)peers.Add(id,Root((Entity)tr.GetObject(id,OpenMode.ForRead),tr,cancel));}
            path=SourcePath(item,kind,db);
        }
        Need(Token(before)==expected,"Reference changed since readback; refresh its current token.","REFERENCE_CONFLICT");
        if(edit is SetRasterImageClip391 clip)
        {
            var source=(RasterImage)tr.GetObject(target,OpenMode.ForRead);
            _=CadRasterImageClip391.Resolve(source,clip,cancel);
            using var preview=(RasterImage)source.Clone();
            Need(preview.GetType()==typeof(RasterImage)&&preview.ObjectId.IsNull&&preview.UnmanagedObject!=source.UnmanagedObject,"Native clip preview did not yield an independent detached raster.","CLIP_PREVIEW_UNAVAILABLE");
            CadRasterImageClip391.Apply(preview,clip,cancel);
        }
        Element(peers.Select(pair=>new{handle=Handle(pair.Key),snapshot=pair.Value}).ToArray(),cancel);
        return new(edit,space,layer,target,definition,kind,name,File(path,edit is ReferenceLifecycle385{Action:"reload_xref" or "reload_reference"},cancel),before,peers)
        {DefinitionBefore=DefinitionState(RequireDefinition(definition,kind,db,tr),kind,db,tr,cancel)};
    }
    static void Place(Entity entity,Point2 position,double rotation,double scale,double width=0,double height=0)
    {
        var origin=new Point3d(position.X,position.Y,0);
        if(entity is BlockReference b){b.Position=origin;b.Rotation=rotation;b.ScaleFactors=new Scale3d(scale);}
        else if(entity is RasterImage image){image.Orientation=new CoordinateSystem3d(origin,new Vector3d(width*Math.Cos(rotation),width*Math.Sin(rotation),0),new Vector3d(-height*Math.Sin(rotation),height*Math.Cos(rotation),0));}
        else{var u=(UnderlayReference)entity;u.Position=origin;u.Normal=Vector3d.ZAxis;u.Rotation=rotation;u.ScaleFactors=new Scale3d(scale);}
        bool Near(double actual,double expected)=>ModelParameterSnapshot.Near(actual,expected);
        bool Same(Point3d actual)=>Near(actual.X,origin.X)&&Near(actual.Y,origin.Y)&&Near(actual.Z,0);
        if(entity is RasterImage raster)
        {
            var frame=raster.Orientation;
            Need(Same(frame.Origin)&&Near(frame.Xaxis.X,width*Math.Cos(rotation))&&Near(frame.Xaxis.Y,width*Math.Sin(rotation))&&Near(frame.Yaxis.X,-height*Math.Sin(rotation))&&Near(frame.Yaxis.Y,height*Math.Cos(rotation))&&Near(frame.Xaxis.Z,0)&&Near(frame.Yaxis.Z,0),"Native raster orientation did not retain the requested physical frame.","API_EXCEPTION");
        }
        else
        {
            Point3d actual;double angle;Scale3d factors;
            if(entity is BlockReference block){actual=block.Position;angle=block.Rotation;factors=block.ScaleFactors;}else{var u=(UnderlayReference)entity;actual=u.Position;angle=u.Rotation;factors=u.ScaleFactors;}
            Need(Same(actual)&&Near(Math.Cos(angle),Math.Cos(rotation))&&Near(Math.Sin(angle),Math.Sin(rotation))&&Near(factors.X,scale)&&Near(factors.Y,scale)&&Near(factors.Z,scale),"Native reference placement did not retain the requested position, rotation and scale.","API_EXCEPTION");
        }
    }
    internal static Prepared Preview(Resolved resolved,List<DBObject> owned,Action cancel,Action<string>? trace=null)
    {
        cancel();DBObject? definition=null;Entity? entity=null;
        if(resolved.Edit is AttachXref385 or InsertRasterImage385)
        {
            // Source readability/current owner/name/layer and complete output
            // bytes have already been resolved. Native raster loading and frame
            // setters use an actual registered definition and resident image.
            trace?.Invoke("creation_deferred_until_database_write");
            CheckSource(resolved.Source,cancel);return new(null,null){CreatedObjects=owned};
        }
        else if(resolved.Edit is AttachUnderlay385 underlay)
        {
            trace?.Invoke("underlay_definition_allocate_before");var data=underlay.Format=="dwf"?(UnderlayDefinition)new DwfDefinition():underlay.Format=="dgn"?new DgnDefinition():new PdfDefinition();definition=data;owned.Add(data);trace?.Invoke("underlay_definition_allocate_returned");
            data.SourceFileName=underlay.SourcePath;data.ItemName=underlay.ItemName;trace?.Invoke("underlay_definition_load_before");data.Load("");trace?.Invoke("underlay_definition_load_returned");Need(data.Loaded,"Native underlay item is unavailable; check the declared item_name and installed format loader.","SOURCE_FILE_UNAVAILABLE");
            trace?.Invoke("underlay_entity_allocate_before");entity=underlay.Format=="dwf"?new DwfReference():underlay.Format=="dgn"?new DgnReference():new PdfReference();owned.Add(entity);trace?.Invoke("underlay_entity_allocate_returned");
            trace?.Invoke("underlay_frame_before");Place(entity,underlay.Position,underlay.Rotation,underlay.Scale);((UnderlayReference)entity).IsOn=true;trace?.Invoke("underlay_frame_returned");
        }
        CheckSource(resolved.Source,cancel);return new(definition,entity);
    }
    static ObjectId Append(Entity entity,Resolved resolved,Transaction tr,WriteJob job,Action<string>? trace=null)
    {
        entity.LayerId=resolved.Layer;var space=(BlockTableRecord)tr.GetObject(resolved.Space,OpenMode.ForWrite);job.CheckCancellation();trace?.Invoke("entity_append_before");ObjectId id=space.AppendEntity(entity);trace?.Invoke("entity_append_returned");
        trace?.Invoke("entity_transaction_register_before");tr.AddNewlyCreatedDBObject(entity,true);trace?.Invoke("entity_transaction_register_returned");return id;
    }
    static ObjectId AddDefinition(DBObject definition,Resolved resolved,Database db,Transaction tr,WriteJob job,Action<string>? trace=null)
    {
        ObjectId dictionary=ExistingDictionary(db,tr,resolved.Kind);
        if(resolved.Kind=="raster_image")
        {job.CheckCancellation();trace?.Invoke("raster_dictionary_create_before");ObjectId actual=RasterImageDef.CreateImageDictionary(db);trace?.Invoke("raster_dictionary_create_returned");Need(dictionary.IsNull||dictionary==actual,"Native image dictionary differs from its current observed definitions.","API_EXCEPTION");dictionary=actual;}
        if(dictionary.IsNull)
        {
            job.CheckCancellation();var nod=(DBDictionary)tr.GetObject(db.NamedObjectsDictionaryId,OpenMode.ForWrite);var records=new DBDictionary();try{dictionary=nod.SetAt(DictionaryKey(resolved.Kind),records);tr.AddNewlyCreatedDBObject(records,true);}finally{records.Dispose();}
        }
        var owner=(DBDictionary)tr.GetObject(dictionary,OpenMode.ForWrite);Need(owner.OwnerId==db.NamedObjectsDictionaryId&&!owner.Contains(resolved.Name),"Native reference dictionary is not host-owned or already contains the requested name.","DEFINITION_EXISTS");job.CheckCancellation();
        trace?.Invoke("definition_dictionary_set_before");ObjectId id=owner.SetAt(resolved.Name,definition);trace?.Invoke("definition_dictionary_set_returned");
        trace?.Invoke("definition_transaction_register_before");tr.AddNewlyCreatedDBObject(definition,true);trace?.Invoke("definition_transaction_register_returned");
        Need(definition.ObjectId==id&&definition.Database?.UnmanagedObject==db.UnmanagedObject&&definition.OwnerId==dictionary,"New reference definition has unexpected ownership.","API_EXCEPTION");return id;
    }
    internal static StructureChange Apply(StructureWriteArguments args,int index,Resolved resolved,Prepared prepared,Database db,Transaction tr,WriteJob job,Action<string>? trace=null)
    {
        void Stage(string value)=>trace?.Invoke("reference_"+index+"_"+resolved.Kind+"_"+value);
        job.CheckCancellation();Stage("source_verify_before");CheckSource(resolved.Source,job.CheckCancellation);Stage("source_verify_returned");ObjectId target=resolved.Target;ObjectId definition=resolved.Definition;bool created=false;StructureChild[]? children=null;
        if(resolved.Edit is AttachXref385 attach)
        {
            var createdObjects=prepared.CreatedObjects;Need(createdObjects!=null,"Reference creation wrapper ownership was not prepared.","API_EXCEPTION");Stage("xref_attach_before");definition=attach.Overlay?db.OverlayXref(attach.SourcePath,attach.Name):db.AttachXref(attach.SourcePath,attach.Name);Stage("xref_attach_returned");var data=(BlockTableRecord)RequireDefinition(definition,"xref",db,tr);
            Need(data.Name==attach.Name&&data.IsFromOverlayReference==attach.Overlay&&data.XrefStatus==XrefStatus.Resolved,"Native xref did not resolve with the requested name/mode.","API_EXCEPTION");
            Stage("xref_entity_allocate_before");var entity=new BlockReference(new Point3d(attach.Position.X,attach.Position.Y,0),definition);createdObjects.Add(entity);Stage("xref_entity_allocate_returned");
            Stage("xref_frame_before");Place(entity,attach.Position,attach.Rotation,attach.Scale);Stage("xref_frame_returned");target=Append(entity,resolved,tr,job,Stage);created=true;
        }
        else if(resolved.Edit is InsertRasterImage385 rasterEdit)
        {
            var createdObjects=prepared.CreatedObjects;Need(createdObjects!=null,"Raster creation wrapper ownership was not prepared.","API_EXCEPTION");
            Stage("raster_definition_allocate_before");var data=new RasterImageDef();createdObjects.Add(data);Stage("raster_definition_allocate_returned");
            definition=AddDefinition(data,resolved,db,tr,job,Stage);
            Stage("raster_source_path_before");data.SourceFileName=rasterEdit.SourcePath;Stage("raster_source_path_returned");
            Stage("raster_definition_load_before");data.Load();Stage("raster_definition_load_returned");
            Need(data.IsLoaded,"Native raster loader did not load the image.","SOURCE_FILE_UNAVAILABLE");
            var pixels=data.Size;Need(double.IsFinite(pixels.X)&&double.IsFinite(pixels.Y)&&pixels.X>0&&pixels.Y>0,"Native raster loader did not decode a nonempty image.","SOURCE_FILE_UNAVAILABLE");
            Stage("raster_entity_allocate_before");var image=new RasterImage();createdObjects.Add(image);Stage("raster_entity_allocate_returned");
            Stage("raster_definition_bind_before");image.ImageDefId=definition;Stage("raster_definition_bind_returned");target=Append(image,resolved,tr,job,Stage);
            Stage("raster_frame_before");Place(image,rasterEdit.Position,rasterEdit.Rotation,1,rasterEdit.Width,rasterEdit.Height);image.ShowImage=true;Stage("raster_frame_returned");
            Stage("raster_reactors_enable_before");RasterImage.EnableReactors(true);Stage("raster_reactors_enable_returned");
            Stage("raster_associate_before");image.AssociateRasterDef(data);Stage("raster_associate_returned");
            Need(image.ImageDefId==definition&&image.ObjectId==target&&image.OwnerId==resolved.Space&&image.Database?.UnmanagedObject==db.UnmanagedObject,"Native raster link or occurrence ownership differs from the registered definition and requested space.","API_EXCEPTION");created=true;
        }
        else if(prepared.Definition!=null&&prepared.Entity!=null)
        {
            definition=AddDefinition(prepared.Definition,resolved,db,tr,job,Stage);
            ((UnderlayReference)prepared.Entity).DefinitionId=definition;target=Append(prepared.Entity,resolved,tr,job,Stage);
            created=true;
        }
        else if(resolved.Edit is SetReferencePlacement385 placement)
        {
            var entity=(Entity)tr.GetObject(target,OpenMode.ForWrite);Need(Exact(Root(entity,tr,job.CheckCancellation),resolved.Before),"Reference changed after preflight.","REFERENCE_CONFLICT");var retained=Root(entity,tr,job.CheckCancellation,false);
            if(entity is RasterImage raster)
            {var frame=raster.Orientation;double rotation=Math.Atan2(frame.Xaxis.Y,frame.Xaxis.X);Place(entity,placement.Properties.Position??new(frame.Origin.X,frame.Origin.Y),placement.Properties.Rotation??rotation,1,placement.Properties.Width??frame.Xaxis.Length,placement.Properties.Height??frame.Yaxis.Length);}
            else if(entity is BlockReference b)Place(entity,placement.Properties.Position??new(b.Position.X,b.Position.Y),placement.Properties.Rotation??b.Rotation,placement.Properties.Scale??b.ScaleFactors.X);
            else{var u=(UnderlayReference)entity;Place(entity,placement.Properties.Position??new(u.Position.X,u.Position.Y),placement.Properties.Rotation??u.Rotation,placement.Properties.Scale??u.ScaleFactors.X);}
            Need(Exact(retained,Root(entity,tr,job.CheckCancellation,false)),"Reference placement changed unrelated identity, display or user data.","API_EXCEPTION");
            Plane(entity);
        }
        else if(resolved.Edit is SetRasterImageClip391 clip)
        {
            var image=(RasterImage)tr.GetObject(target,OpenMode.ForWrite);
            Need(Exact(Root(image,tr,job.CheckCancellation),resolved.Before),"Image changed after preflight.","REFERENCE_CONFLICT");
            var retained=Root(image,tr,job.CheckCancellation,false);
            CadRasterImageClip391.Apply(image,clip,job.CheckCancellation);
            Need(Exact(retained,Root(image,tr,job.CheckCancellation,false)),"Raster clipping changed unrelated native identity, owners, appearance or user data.","API_EXCEPTION");
            image.RecordGraphicsModified(true);
        }
        else if(resolved.Edit is SetRasterImageDisplay390 display)
        {
            var image=(RasterImage)tr.GetObject(target,OpenMode.ForWrite);
            Need(Exact(Root(image,tr,job.CheckCancellation),resolved.Before),"Image changed after preflight.","REFERENCE_CONFLICT");
            var retained=Root(image,tr,job.CheckCancellation,false);
            CadRasterImageDisplay390.Apply(image,display.Properties);
            Need(Exact(retained,Root(image,tr,job.CheckCancellation,false)),"Image adjustment changed unrelated identity, display properties or user data.","API_EXCEPTION");
            image.RecordGraphicsModified(true);
        }
        else
        {
            var lifecycle=(ReferenceLifecycle385)resolved.Edit;
            if(lifecycle.Action=="delete_reference"){var entity=(Entity)tr.GetObject(target,OpenMode.ForWrite);Need(Exact(Root(entity,tr,job.CheckCancellation),resolved.Before),"Reference changed after preflight.","REFERENCE_CONFLICT");entity.Erase();Need(target.IsErased,"Native reference erase did not retire the selected entity.","API_EXCEPTION");Need(Live(definition),"Deleting a reference unexpectedly retired its shared definition.","API_EXCEPTION");}
            else
            {
                var data=RequireDefinition(definition,resolved.Kind,db,tr);Need(Exact(DefinitionState(data,resolved.Kind,db,tr,job.CheckCancellation),resolved.Before),"Reference definition changed after preflight.","REFERENCE_CONFLICT");
                if(lifecycle.Action=="detach_xref"){db.DetachXref(definition);Need(!Live(definition)&&resolved.Peers.Keys.All(id=>!Live(id)),"Native detach did not remove the selected definition and its disclosed references.","API_EXCEPTION");}
                else if(lifecycle.Action=="reload_xref"){using var ids=new ObjectIdCollection();ids.Add(definition);db.ReloadXrefs(ids);Need(((BlockTableRecord)RequireDefinition(definition,resolved.Kind,db,tr)).XrefStatus==XrefStatus.Resolved,"Native xref reload did not resolve the source.","API_EXCEPTION");}
                else if(data is RasterImageDef raster){raster.UpgradeOpen();raster.Unload(true);raster.Load();Need(raster.IsLoaded,"Native raster reload did not load the source.","API_EXCEPTION");}
                else{var underlay=(UnderlayDefinition)data;underlay.UpgradeOpen();underlay.Unload();underlay.Load("");Need(underlay.Loaded,"Native underlay reload did not load the declared item.","API_EXCEPTION");}
            }
        }
        bool detached=resolved.Edit is ReferenceLifecycle385{Action:"detach_xref"};
        if(!created&&!detached)
        {
            var current=DefinitionState(RequireDefinition(definition,resolved.Kind,db,tr),resolved.Kind,db,tr,job.CheckCancellation);
            foreach(var field in resolved.DefinitionBefore.EnumerateObject())
                if(field.Name!="reference_handles")Need(Exact(field.Value,current.GetProperty(field.Name)),"Reference operation changed definition identity, path/item or overlay mode.","API_EXCEPTION");
            string? retired=resolved.Edit is ReferenceLifecycle385{Action:"delete_reference"}?Handle(target):null;
            var expected=resolved.DefinitionBefore.GetProperty("reference_handles").EnumerateArray().Select(h=>h.GetString()).Where(h=>h!=retired).ToArray();
            Need(expected.SequenceEqual(current.GetProperty("reference_handles").EnumerateArray().Select(h=>h.GetString())),"Reference operation changed unrequested occurrence bindings.","API_EXCEPTION");
        }
        if(!detached)foreach(var peer in resolved.Peers){job.CheckCancellation();Need(Live(peer.Key)&&Exact(peer.Value,Root((Entity)tr.GetObject(peer.Key,OpenMode.ForRead),tr,job.CheckCancellation)),"Reference operation changed an unselected occurrence's placement/display/user data.","API_EXCEPTION");}
        Stage("source_post_verify_before");CheckSource(resolved.Source,job.CheckCancellation);Stage("source_post_verify_returned");
        var context=new EntityRef(args.BridgeId,args.DocumentId,Handle(target));
        if(created)children=[new(new(args.BridgeId,args.DocumentId,Handle(definition)),resolved.Kind+"_definition",-1,null)];
        else if(resolved.Edit is ReferenceLifecycle385{Action:"detach_xref" or "reload_xref" or "reload_reference"})children=resolved.Peers.Keys.Select(id=>new StructureChild(new(args.BridgeId,args.DocumentId,Handle(id)),resolved.Kind,0,null)).ToArray();
        return new(index,resolved.Edit.Operation,context,resolved.Kind,resolved.Name,created,children);
    }
    internal static void Batch(IEnumerable<Resolved> operations)
    {
        var values=operations.ToArray();
        foreach(var item in values)
        {
            if(item.Target.IsNull)continue;
            // Selected occurrence display/placement edits keep definition and
            // membership unchanged. Each selected root has its own exact
            // preflight/apply proof; only unselected peers must match the old
            // snapshot after another selected occurrence has intentionally moved.
            if(item.Edit is SetReferencePlacement385 or SetRasterImageDisplay390 or SetRasterImageClip391)
                foreach(var selected in values)
                    if(selected.Target!=item.Target&&selected.Definition==item.Definition&&selected.Edit is SetReferencePlacement385 or SetRasterImageDisplay390 or SetRasterImageClip391)
                        item.Peers.Remove(selected.Target);
            bool all=item.Edit is ReferenceLifecycle385{Action:"reload_xref" or "detach_xref" or "reload_reference"};
            foreach(var other in values)
                if(!ReferenceEquals(item,other)&&!other.Target.IsNull)
                    Need(!(all&&(other.Definition==item.Definition||other.Target==item.Target)||other.Edit is ReferenceLifecycle385{Action:"reload_xref" or "detach_xref" or "reload_reference"}&&other.Definition==item.Definition),"Do not combine a definition lifecycle with another mutation of that definition or its occurrences; use committed readback between groups.","INVALID_OPERATION_ORDER");
        }
    }
    internal static StructureChange Project(StructureWriteArguments args,int index,Resolved resolved)
    {
        bool created=resolved.Edit is AttachXref385 or InsertRasterImage385 or AttachUnderlay385;
        StructureChild[]? children=created?[new(new(args.BridgeId,args.DocumentId,"FFFFFFFFFFFFFFFF"),resolved.Kind+"_definition",-1,null)]:resolved.Edit is ReferenceLifecycle385{Action:"detach_xref" or "reload_xref" or "reload_reference"}?resolved.Peers.Keys.Select(id=>new StructureChild(new(args.BridgeId,args.DocumentId,Handle(id)),resolved.Kind,0,null)).ToArray():null;
        return new(index,resolved.Edit.Operation,new(args.BridgeId,args.DocumentId,"FFFFFFFFFFFFFFFF"),resolved.Kind,resolved.Name,created,children);
    }
}
