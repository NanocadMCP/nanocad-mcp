using System.Security.Cryptography;
using System.Text.Json;
using NanoCAD.Contracts;
using Teigha.DatabaseServices;

namespace NanoCADBridge;

// STAGED ONLY. Rename retains IDs; no dependency removal, reassignment or
// dimension regeneration. Whole consumer sets are hashed one record at a time.
internal static class CadUsedDimensionStyleRename
{
    internal sealed record State(ObjectId Source, string Fingerprint, ObjectId[] Consumers);
    private static void Need(bool value,string message,string code="STRUCTURE_UNAVAILABLE",object? details=null)
    {if(!value)throw new DomainException(code,message,false,details);}
    private static bool Live(ObjectId id)=>!id.IsNull&&id.IsValid&&!id.IsErased&&!id.IsEffectivelyErased;
    private static string? Id(ObjectId id)
    {
        if(id.IsNull)return null;
        Need(id.IsValid,"A retained native dependency ID is invalid.");
        return Validation.NormalizeHandle(id.Handle.ToString());
    }
    private static EntityRef Ref(EntityRef context,ObjectId id)=>new(context.bridge_instance_id,context.document_id,Id(id)!);

    private sealed class Fingerprint : IDisposable
    {
        private readonly IncrementalHash _hash=IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        private readonly Action _cancel;
        internal Fingerprint(Action cancel)=>_cancel=cancel;
        private void Count(int value)=>_hash.AppendData(BitConverter.GetBytes(value));
        internal void Text(string? value)
        {
            _cancel();if(value==null){Count(-1);return;}Count(value.Length);
            // Exact UTF-16 units, including retained native strings that cannot
            // be decoded as UTF-8. Bounded scratch buffer; no total text cap.
            var buffer=new byte[8192];int offset=0;
            while(offset<value.Length)
            {
                _cancel();int count=Math.Min(buffer.Length/2,value.Length-offset);
                for(int i=0;i<count;i++){char c=value[offset+i];buffer[2*i]=(byte)c;buffer[2*i+1]=(byte)(c>>8);}
                _hash.AppendData(buffer,0,count*2);offset+=count;
            }
        }
        internal void Bytes(byte[] value)
        {
            _cancel();Count(value.Length);
            for(int offset=0;offset<value.Length;offset+=8192)
            {_cancel();_hash.AppendData(value,offset,Math.Min(8192,value.Length-offset));}
        }
        internal void Small(object? value)
        {
            _cancel();var bytes=JsonSerializer.SerializeToUtf8Bytes(value,Wire.Json);Bytes(bytes);
        }
        internal void NativeValue(object? value)
        {
            Text(value?.GetType().FullName);
            if(value is ObjectId id&&!id.IsNull)Need(id.IsValid,"A stored native metadata ID is invalid.");
            if(value is string text)Text(text);
            else if(value is byte[] data)Bytes(data);
            else Small(NativeOwnedMetadata.Value(value));
        }
        internal string Finish(){_cancel();return Convert.ToHexString(_hash.GetHashAndReset());}
        public void Dispose()=>_hash.Dispose();
    }
    private static void Buffer(Fingerprint hash,ResultBuffer? buffer,Action cancel)
    {
        hash.Small(buffer!=null);if(buffer==null)return;
        // Stream native typed values; do not materialize AsArray or a complete
        // multi-consumer JSON/XData inventory just to rename one symbol.
        var iterator=buffer.GetEnumerator();
        // The SDK enumerator exposes a TypedValue Current and does not
        // implement IDisposable; the caller owns and disposes the buffer.
        while(true)
        {
            cancel();if(!iterator.MoveNext())break;
            TypedValue value=iterator.Current;
            hash.Text("typed_value");hash.Small(value.TypeCode);hash.NativeValue(value.Value);
        }
        hash.Text("end_buffer");
    }
    private static ObjectId[] Reactors(DBObject item,Action cancel,bool raw=false)
    {
        var result=new List<ObjectId>();using var native=item.GetPersistentReactorIds();
        foreach(ObjectId id in native){cancel();result.Add(id);}
        // Order is not a design link. Preserve exact membership/multiplicity,
        // independent of native reactor enumeration order after symbol rename.
        return raw?result.ToArray():Ordered(result,cancel);
    }
    private static ObjectId[] Ordered(List<ObjectId> ids,Action cancel)
    {
        var keys=new List<(string? Key,ObjectId Id)>();
        foreach(var id in ids){cancel();keys.Add((Id(id),id));}
        try{keys.Sort((left,right)=>{cancel();return StringComparer.Ordinal.Compare(left.Key,right.Key);});}
        catch(InvalidOperationException error)when(error.InnerException is OperationCanceledException)
        {System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(error.InnerException).Throw();throw;}
        var result=new ObjectId[keys.Count];for(int i=0;i<keys.Count;i++){cancel();result[i]=keys[i].Id;}return result;
    }
    private static void Root(Fingerprint hash,DBObject item,Action cancel)
    {
        cancel();Need(!item.HasFields,"Field-bearing native metadata needs its owning adapter.");
        hash.Small(new{handle=Id(item.ObjectId),owner=Id(item.OwnerId),type=item.GetType().FullName,
            item.HasFields,extension=Id(item.ExtensionDictionary)});
        foreach(var id in Reactors(item,cancel)){cancel();hash.Text("reactor");hash.Text(Id(id));}
        hash.Text("end_reactors");using var data=item.XData;Buffer(hash,data,cancel);
    }
    private static void Owned(Fingerprint hash,DBObject source,Database db,Transaction tr,Action cancel)
    {
        var pending=new Stack<(ObjectId Id,ObjectId Owner)>();var visited=new HashSet<ObjectId>();
        if(!source.ExtensionDictionary.IsNull)pending.Push((source.ExtensionDictionary,source.ObjectId));
        while(pending.Count!=0)
        {
            cancel();var next=pending.Pop();Need(Live(next.Id)&&visited.Add(next.Id),"Owned metadata has an unavailable, repeated or cyclic ownership identity.");
            var item=tr.GetObject(next.Id,OpenMode.ForRead);
            Need(item.Database==db&&item.OwnerId==next.Owner,"Owned metadata belongs to a different document or native owner.");
            hash.Text("owned_object");Root(hash,item,cancel);
            if(item.GetType()==typeof(DBDictionary)&&item is DBDictionary dictionary)
            {
                hash.Small(dictionary.TreatElementsAsHard);
                foreach(DBDictionaryEntry entry in dictionary)
                {
                    cancel();Need(Live(entry.Value),"Owned dictionary has an unavailable reference.");
                    var child=tr.GetObject(entry.Value,OpenMode.ForRead);bool owns=child.Database==db&&child.OwnerId==item.ObjectId;
                    hash.Text(entry.Key);hash.Small(new{handle=Id(entry.Value),owner=Id(child.OwnerId),type=child.GetType().FullName,owned=owns});
                    if(owns)pending.Push((entry.Value,item.ObjectId));
                }
                hash.Text("end_dictionary");
            }
            else if(item.GetType()==typeof(Xrecord)&&item is Xrecord xrecord)
            {
                hash.Small(new{xrecord.XlateReferences,merge_style=(int)xrecord.MergeStyle});
                using var data=xrecord.Data;Buffer(hash,data,cancel);
            }
            else throw new DomainException("UNSUPPORTED_OWNED_METADATA","Native owned data needs its dedicated adapter.",false,new{runtime_type=item.GetType().FullName});
            if(!item.ExtensionDictionary.IsNull)pending.Push((item.ExtensionDictionary,item.ObjectId));
        }
        hash.Text("end_owned_objects");
    }
    private static BlockTableRecord Owner(Dimension dimension,Database db,Transaction tr,Action cancel)
    {
        cancel();Need(Live(dimension.OwnerId),"A referring dimension owner is unavailable.");
        if(tr.GetObject(dimension.OwnerId,OpenMode.ForRead) is not BlockTableRecord owner||owner.GetType()!=typeof(BlockTableRecord)||
            owner.Database!=db||owner.OwnerId!=db.BlockTableId||owner.IsDependent||owner.IsFromExternalReference||owner.IsFromOverlayReference)
            throw new DomainException("STRUCTURE_UNAVAILABLE","A referring dimension needs its native source owner.",false,new{dimension=Id(dimension.ObjectId),owner=Id(dimension.OwnerId)});
        return owner;
    }
    private static ObjectId[] Classify(DimStyleTableRecord style,Database db,Transaction tr,Action cancel)
    {
        var details=new List<object>();int bytes=1024;long unclassified=0,omitted=0;using var digest=new Fingerprint(cancel);
        var linkedConsumers=new List<ObjectId>();
        foreach(var id in Reactors(style,cancel,raw:true))
        {
            cancel();bool live=Live(id);DBObject? target=live?tr.GetObject(id,OpenMode.ForRead):null;string classification="unavailable";
            ObjectId? observedStyle=target is Dimension d&&target.Database==db&&CadNativeDimensionBinding357.Supported(d)?d.DimensionStyle:null;
            if(target!=null&&target.Database==db&&id==style.OwnerId&&id==db.DimStyleTableId&&target.GetType()==typeof(DimStyleTable))
                classification="native_symbol_table_owner";
            else if(target is Dimension dimension&&observedStyle==style.ObjectId)
            {
                Owner(dimension,db,tr,cancel);classification="same_document_dimension_consumer";linkedConsumers.Add(id);
            }
            else if(target!=null)classification="unclassified_native_dependency";
            var descriptor=new{handle=id.IsNull||!id.IsValid?null:Id(id),runtime_type=target?.GetType().FullName,
                owner=target==null?null:Id(target.OwnerId),dimension_style=observedStyle.HasValue?Id(observedStyle.Value):null,classification};digest.Small(descriptor);
            if(classification is "unavailable" or "unclassified_native_dependency")
            {
                unclassified++;int size=JsonSerializer.SerializeToUtf8Bytes(descriptor,Wire.Json).Length+1;
                if(size<=6144-bytes){bytes+=size;details.Add(descriptor);}else omitted++;
            }
        }
        if(unclassified!=0)throw new DomainException("STRUCTURE_UNAVAILABLE",
            "Dimension style rename has an unclassified native dependency; use its owning source route.",false,
            new{style_handle=Id(style.ObjectId),dependency_fingerprint=digest.Finish(),unclassified_count=unclassified,omitted_count=omitted,dependencies=details});
        return linkedConsumers.ToArray();
    }
    internal static State Capture(DimStyleTableRecord style,EntityRef context,Database db,Transaction tr,Action cancel)
    {
        cancel();Need(style.GetType()==typeof(DimStyleTableRecord)&&style.Database==db&&style.OwnerId==db.DimStyleTableId&&!style.IsDependent,
            "Expected one local exact native dimension style.");
        Need(!style.HasFields,"Field-bearing style rename needs its owning adapter.");var linkedConsumers=Classify(style,db,tr,cancel);
        using var hash=new Fingerprint(cancel);Root(hash,style,cancel);Owned(hash,style,db,tr,cancel);
        // Only the label derived from Name is excluded. Native DIM values and
        // references remain exact; no parent-family stub is invoked.
        hash.Small(DimensionStyleMetadataReader.Read(style,tr,db,id=>Ref(context,id),cancel,cancel) with{family_name_candidate=false});
        hash.Small(new{current_dimension=Id(db.Dimstyle),current_text=Id(db.Textstyle),current_layer=Id(db.Clayer)});
        cancel();var acquired=db.GetDimstyleData();
        if(acquired==null||acquired.GetType()!=typeof(DimStyleTableRecord)||!acquired.ObjectId.IsNull)
            throw new DomainException("UNSAFE_NATIVE_CLONE","Current DIM defaults getter returned no proved detached record.");
        using(acquired)
            hash.Small(DimensionStyleMetadataReader.ReadDetached(acquired,context,tr,db,id=>Ref(context,id),cancel,cancel) with{family_name_candidate=false});
        var consumers=new List<ObjectId>();var membership=new HashSet<ObjectId>();
        foreach(ObjectId blockId in (BlockTable)tr.GetObject(db.BlockTableId,OpenMode.ForRead))
        {
            cancel();if(!Live(blockId))continue;
            var block=(BlockTableRecord)tr.GetObject(blockId,OpenMode.ForRead);
            foreach(ObjectId id in block)
            {
                cancel();if(!Live(id))continue;
                if(tr.GetObject(id,OpenMode.ForRead) is not Dimension dimension||dimension.DimensionStyle!=style.ObjectId)continue;
                Need(CadNativeDimensionBinding357.Supported(dimension),"A referring native dimension kind needs its owning adapter.");
                Need(dimension.OwnerId==blockId&&membership.Add(id),"A referring dimension has inconsistent or repeated block ownership.");
                Owner(dimension,db,tr,cancel);consumers.Add(id);
            }
        }
        foreach(var id in linkedConsumers){cancel();Need(membership.Contains(id),"A native dimension reactor is not present in its actual owner inventory.");}
        var ordered=Ordered(consumers,cancel);
        foreach(var id in ordered)
        {
            cancel();var dimension=(Dimension)tr.GetObject(id,OpenMode.ForRead);hash.Text("dimension_consumer");
            Root(hash,dimension,cancel);Owned(hash,dimension,db,tr,cancel);
            hash.Small(new{layer=Id(dimension.LayerId),linetype=Id(dimension.LinetypeId),stored=EntityAdapter.StoredProperties(dimension),
                style=Id(dimension.DimensionStyle),annotative=(int)dimension.Annotative,definition=DimensionGeometryReader.Definition(dimension),
                normal=new[]{dimension.Normal.X,dimension.Normal.Y,dimension.Normal.Z},dimension.Elevation,
                text_position=new[]{dimension.TextPosition.X,dimension.TextPosition.Y,dimension.TextPosition.Z},
                dimension.TextRotation,dimension.UsingDefaultTextPosition,dimension.Dimtmove});
            hash.Text(dimension.DimensionText);
            hash.Small(CadDimensionEditing341.RetainedStyleSnapshot(dimension,db,tr,cancel));
        }
        hash.Text("end_dimension_consumers");return new(style.ObjectId,hash.Finish(),ordered);
    }
    internal static void Verify(State before,EntityRef context,Database db,Transaction tr,Action cancel,string code)
    {
        var source=tr.GetObject(before.Source,OpenMode.ForRead);
        if(source is not DimStyleTableRecord style)throw new DomainException(code,"Native dimension style identity changed.");
        var actual=Capture(style,context,db,tr,cancel);
        Need(actual.Source==before.Source&&actual.Fingerprint==before.Fingerprint&&before.Consumers.SequenceEqual(actual.Consumers),
            "Native style rename changed retained defaults, consumers or owned data.",code,
            new{before_fingerprint=before.Fingerprint,current_fingerprint=actual.Fingerprint,
                before_consumer_count=before.Consumers.Length,current_consumer_count=actual.Consumers.Length});
    }
}
