using System.Text.Json;
using NanoCAD.Contracts;
using Teigha.DatabaseServices;

namespace NanoCADBridge;

// Private verification records are complete and byte bounded. Construction
// reservations and the final containing record use separate accounting.
internal sealed class CadContourBudget344
{
    internal sealed record SourceState(System.Text.Json.JsonElement Geometry,System.Text.Json.JsonElement Metadata,System.Text.Json.JsonElement Display,bool HasExtension);
    private readonly Action _cancel;
    private readonly long _maximum;
    private long _bytes=2;
    internal CadContourBudget344(Action cancel,long maximum=NativeCurveRead342.SnapshotByteBudget)
    {_cancel=cancel;_maximum=maximum;}
    internal void Add(object value)
    {
        _cancel();_bytes+=JsonSerializer.SerializeToUtf8Bytes(value,Wire.Json).LongLength+1;_cancel();
        if(_bytes>_maximum)throw new DomainException("RESPONSE_LIMIT_EXCEEDED","Complete contour verification data exceeds its actual serialized-byte budget.");
    }
    internal static void Check(object value,Action cancel)
        =>NativeCurveRead342.CheckSerializedBudget(value,NativeCurveRead342.SnapshotByteBudget,cancel);
    internal static void Output(ModifiedEntities value,Action cancel)
    {
        cancel();long bytes=JsonSerializer.SerializeToUtf8Bytes(new{data=value,warnings=Array.Empty<string>()},Wire.Json).LongLength;cancel();
        if(bytes>Wire.PayloadBudget-16384)
            throw new DomainException("RESPONSE_LIMIT_EXCEEDED","Complete contour result references and provenance exceed the response-byte budget; no contour root was appended or source changed.");
    }
    internal static EntityRef[] Projected(ModifyArguments args,int count,Action cancel)
    {
        var result=new List<EntityRef>();var budget=new CadContourBudget344(cancel,Wire.PayloadBudget);
        for(int i=0;i<count;i++)
        {cancel();var value=new EntityRef(args.BridgeId,args.DocumentId,"FFFFFFFFFFFFFFFF");budget.Add(value);result.Add(value);}
        return result.ToArray();
    }
    internal static void CheckCopyCapacity(ModifyArguments args,Action cancel)
    {
        // Count the actual serialized reference/mapping shapes before native previews.
        // This is a transport bound, not a ceiling on CAD instances or model size.
        long bytes=JsonSerializer.SerializeToUtf8Bytes(new ModifiedEntities(args.OperationId,"committed",false,[],[],[],[],"single_command"),Wire.Json).LongLength+16384;
        var placeholder=new EntityRef(args.BridgeId,args.DocumentId,"FFFFFFFFFFFFFFFF");
        long referenceBytes=JsonSerializer.SerializeToUtf8Bytes(placeholder,Wire.Json).LongLength+1;
        for(int index=0;index<args.Operations.Length;index++)
        {
            cancel();var operation=args.Operations[index];bool repeat=CopyTransforms2d407.Handles(operation);
            if(!repeat&&operation is not (CopySpec or Mirror2dSpec{Copy:true}))continue;
            long count=repeat?CopyTransforms2d407.CopiesPerSource(operation):1;
            foreach(var handle in operation.Handles)
            {
                cancel();long mappingBytes=JsonSerializer.SerializeToUtf8Bytes(new CopyMapping(index,new(args.BridgeId,args.DocumentId,handle),placeholder),Wire.Json).LongLength+1;
                bytes+=count*(mappingBytes+referenceBytes*(repeat?2:1));
                if(bytes>Wire.PayloadBudget)throw new DomainException("RESPONSE_LIMIT_EXCEEDED","Complete copy references and provenance exceed actual response bytes; no copies were appended. Divide the requested repetition into meaningful groups.");
            }
            if(repeat)
            {
                var result=new RepetitionResult407(index,operation.Operation,false,operation is PolarRepeat2d407 polar?polar.Count:2,
                    operation.Handles.Select(h=>new EntityRef(args.BridgeId,args.DocumentId,h)).ToArray(),[]);
                bytes+=JsonSerializer.SerializeToUtf8Bytes(result,Wire.Json).LongLength+1;
            }
            if(bytes>Wire.PayloadBudget)throw new DomainException("RESPONSE_LIMIT_EXCEEDED","Complete grouped-copy response exceeds its actual serialized-byte budget.");
        }
    }
    internal static void BatchOutput(ModifyArguments args,IReadOnlyDictionary<int,int> contourCounts,MTextFormatChange[] formatChanges,Action cancel,ToleranceFrameChange356[]? toleranceChanges=null)
    {
        var modified=new List<string>();var erased=new List<string>();var copies=new List<CopyMapping>();
        var created=new List<(int Index,EntityRef Reference)>();var contours=new List<ContourMapping>();
        EntityRef Ref(string handle)=>new(args.BridgeId,args.DocumentId,handle);
        for(int i=0;i<args.Operations.Length;i++)
        {
            cancel();var operation=args.Operations[i];
            foreach(string handle in operation.Handles)
            {
                cancel();
                if(CadContourEditor.Handles(operation))
                {
                    var generated=operation is ExtendCurveSpec?Array.Empty<EntityRef>():Projected(args,contourCounts[i],cancel);
                    bool erase=operation is TrimCurveSpec;
                    foreach(var reference in generated){cancel();created.Add((i,reference));}
                    contours.Add(new(i,operation.Operation,Ref(handle),generated,erase));
                    if(erase)erased.Add(handle);else if(operation is ExtendCurveSpec&&!modified.Contains(handle))modified.Add(handle);
                }
                else if(operation is CopySpec or Mirror2dSpec{Copy:true} or CopyTransform2d407 or PolarRepeat2d407)
                {
                    int count=CopyTransforms2d407.Handles(operation)?CopyTransforms2d407.CopiesPerSource(operation):1;
                    for(int copy=0;copy<count;copy++){cancel();var reference=Ref("FFFFFFFFFFFFFFFF");copies.Add(new(i,Ref(handle),reference));created.Add((i,reference));}
                }
                else if(operation is EraseSpec)erased.Add(handle);
                else if(!modified.Contains(handle))modified.Add(handle);
            }
        }
        var repetitions=args.Operations.Select((op,index)=>(op,index)).Where(pair=>CopyTransforms2d407.Handles(pair.op))
            .Select(pair=>new RepetitionResult407(pair.index,pair.op.Operation,false,pair.op is PolarRepeat2d407 polar?polar.Count:2,
                pair.op.Handles.Select(Ref).ToArray(),copies.Where(copy=>copy.operation_index==pair.index).Select(copy=>copy.created).ToArray())).ToArray();
        Output(new(args.OperationId,"committed",false,modified.Where(h=>!erased.Contains(h)).Select(Ref).ToArray(),
            erased.Select(Ref).ToArray(),created.OrderBy(v=>v.Index).Select(v=>v.Reference).ToArray(),copies.ToArray(),"single_command",
            contours.ToArray(),mtext_format_changes:formatChanges.Length==0?null:formatChanges,tolerance_changes:toleranceChanges is {Length:>0}?toleranceChanges:null,repetition_results:repetitions.Length==0?null:repetitions),cancel);
    }
    internal static MTextFormatChange ProjectedFormat(ModifyArguments args,string handle,string[]? warnings)
    {
        // Only serialized width is predicted: the effective values themselves
        // remain native readback, not formatting or geometric acceptance gates.
        const double number=-1.2345678901234567E-308;
        var source=new EntityRef(args.BridgeId,args.DocumentId,handle);
        return new(source,new string('F',64),source with{handle="FFFFFFFFFFFFFFFF"},number,number,"BottomCenter","AtLeast",number,[number,number,number],warnings);
    }
    internal static Entity Detached(Entity source)
    {
        var value=source.Clone() as Entity;
        if(value==null||value.GetType()!=source.GetType()||!value.ObjectId.IsNull||!value.OwnerId.IsNull||
            value.UnmanagedObject==IntPtr.Zero||value.UnmanagedObject==source.UnmanagedObject)
            throw new DomainException("UNSAFE_NATIVE_CLONE","Contour preview clone did not prove an exact independent detached root; an unsafe alias was not adopted or disposed.");
        return value;
    }
    internal static void Replacement(Entity source,Transaction tr,Action cancel)
    {
        cancel();using var data=source.XData;
        if(data!=null&&data.AsArray().Length!=0)
            throw new DomainException("UNSUPPORTED_CURVE_DEPENDENCIES","Independent replacement cannot discard source application data; retain the source or use its owning adapter.");
        if(source.ExtensionDictionary.IsNull)return;
        var id=source.ExtensionDictionary;
        if(!id.IsValid||id.IsErased||id.IsEffectivelyErased||tr.GetObject(id,OpenMode.ForRead) is not DBDictionary dictionary||dictionary.OwnerId!=source.ObjectId)
            throw new DomainException("INVALID_SOURCE_OWNERSHIP","Cannot prove the replacement source's native extension ownership.");
        using var extensionData=dictionary.XData;using var extensionLinks=dictionary.GetPersistentReactorIds();
        cancel();
        if(dictionary.Count!=0||dictionary.HasFields||extensionLinks.Count!=0||extensionData!=null&&extensionData.AsArray().Length!=0)
            throw new DomainException("UNSUPPORTED_CURVE_DEPENDENCIES","Independent replacement cannot discard nonempty owned source data; retain the source or use its owning adapter.");
    }
    internal static SourceState Source(Entity source,Action cancel)
    {
        cancel();string? Id(ObjectId id)=>id.IsNull?null:Validation.NormalizeHandle(id.Handle.ToString());
        var geometry=Wire.Element(Geometry(source,cancel));var budget=new CadContourBudget344(cancel);
        using var data=source.XData;using var links=source.GetPersistentReactorIds();var reactors=new List<string?>();
        foreach(ObjectId link in links){cancel();var value=Id(link);budget.Add(value!);reactors.Add(value);}
        var metadata=Wire.Element(new{handle=Id(source.ObjectId),owner_handle=Id(source.OwnerId),runtime_type=source.GetType().FullName,
            source.HasFields,extension_handle=Id(source.ExtensionDictionary),xdata=data==null?null:NativeOwnedMetadata.Buffer(data,cancel,budget.Add),reactors});
        var display=Wire.Element(new{layer_handle=Id(source.LayerId),properties=EntityAdapter.StoredProperties(source)});
        Check(new{geometry,metadata,display},cancel);return new(geometry,metadata,display,!source.ExtensionDictionary.IsNull);
    }
    internal static void VerifySource(Entity source,SourceState before,Action cancel)
    {
        var after=Source(source,cancel);var pending=new Stack<(JsonElement Before,JsonElement After)>();pending.Push((before.Geometry,after.Geometry));
        bool equal=true;
        while(pending.Count!=0&&equal)
        {
            cancel();var pair=pending.Pop();var a=pair.Before;var b=pair.After;
            if(a.ValueKind!=b.ValueKind){equal=false;break;}
            if(a.ValueKind==JsonValueKind.Array)
            {
                if(a.GetArrayLength()!=b.GetArrayLength()){equal=false;break;}
                using var left=a.EnumerateArray();using var right=b.EnumerateArray();
                while(left.MoveNext()&&right.MoveNext()){cancel();pending.Push((left.Current,right.Current));}
            }
            else if(a.ValueKind==JsonValueKind.Object)
            {
                if(a.EnumerateObject().Count()!=b.EnumerateObject().Count()){equal=false;break;}
                foreach(var value in a.EnumerateObject()){cancel();if(!b.TryGetProperty(value.Name,out var other)){equal=false;break;}pending.Push((value.Value,other));}
            }
            else equal=BlockMoveGeometry.SameProperties(a,b);
        }
        cancel();
        if(!equal||before.Metadata.GetRawText()!=after.Metadata.GetRawText()||before.Display.GetRawText()!=after.Display.GetRawText())
            throw new DomainException("NATIVE_GEOMETRY_MISMATCH","Independent contour construction changed an unselected source frame, identity, display or observed root metadata.");
    }
    internal static void RetentionWarning(IEnumerable<SourceState> sources,List<string>? warnings)
    {
        if(sources.Any(source=>source.HasExtension))
            warnings?.Add("CONTOUR_OWNED_DATA_UNVERIFIED: Retained source extension identities are unchanged; their internal records are not deeply rechecked. Generated independent curves copy display properties without transferring application data or associations.");
    }
    internal static void VerifyProperties(Entity source,SourceState before,Action cancel)
    {
        var after=Source(source,cancel);cancel();
        if(before.Metadata.GetRawText()!=after.Metadata.GetRawText()||before.Display.GetRawText()!=after.Display.GetRawText())
            throw new DomainException("NATIVE_GEOMETRY_MISMATCH","Native contour extension changed retained root identity, display or observed source metadata.");
    }
    internal static object Geometry(Entity entity,Action cancel)
    {
        cancel();
        double[] Point(Teigha.Geometry.Point3d point)=>[point.X,point.Y,point.Z];
        double[] Vector(Teigha.Geometry.Vector3d vector)=>[vector.X,vector.Y,vector.Z];
        object geometry;
        if(entity.GetType()==typeof(Line)&&entity is Line line)
            geometry=new{start=Point(line.StartPoint),end=Point(line.EndPoint),normal=Vector(line.Normal),line.Thickness};
        else if(entity.GetType()==typeof(Arc)&&entity is Arc arc)
            geometry=new{center=Point(arc.Center),arc.Radius,arc.StartAngle,arc.EndAngle,normal=Vector(arc.Normal),arc.Thickness};
        else if(entity.GetType()==typeof(Circle)&&entity is Circle circle)
            geometry=new{center=Point(circle.Center),circle.Radius,normal=Vector(circle.Normal),circle.Thickness};
        else if(entity.GetType()==typeof(Polyline)&&entity is Polyline polyline)
        {
            int count=polyline.NumberOfVertices;
            if(count<2)throw new DomainException("INVALID_GEOMETRY","A contour Polyline requires at least two native vertices.");
            var vertices=new List<object>();var budget=new CadContourBudget344(cancel);
            for(int i=0;i<count;i++)
            {
                cancel();var vertex=new{point=Point(polyline.GetPoint3dAt(i)),bulge=polyline.GetBulgeAt(i),start_width=polyline.GetStartWidthAt(i),end_width=polyline.GetEndWidthAt(i)};
                budget.Add(vertex);vertices.Add(vertex);
            }
            geometry=new{polyline.Closed,polyline.Elevation,normal=Vector(polyline.Normal),polyline.Thickness,polyline.ConstantWidth,vertices};
        }
        else if(entity.GetType()==typeof(Ellipse)&&entity is Ellipse ellipse)geometry=CadEllipseReader.Read(ellipse,cancel);
        else if(entity.GetType()==typeof(Spline)&&entity is Spline spline)geometry=CadSplineReader.Read(spline,cancel);
        else throw new DomainException("UNSUPPORTED_ENTITY_TYPE","Contour preview byte accounting requires an existing supported curve adapter.");
        Check(geometry,cancel);return geometry;
    }
}
