using System.Globalization;
using System.Text.Json;
using NanoCAD.Contracts;
using NanoCAD.Ipc;
using Teigha.DatabaseServices;

namespace NanoCADBridge;

internal static class CadStructureWriter
{
    internal sealed record AttributeTarget(ObjectId Block, ObjectId Attribute);
    internal sealed record Resolved(ObjectId LayerTable, Dictionary<int, ObjectId> Layers,
        Dictionary<int, ObjectId> Linetypes, Dictionary<int, AttributeTarget> Attributes, Dictionary<int, string> Names,
        Dictionary<int, CadBlockWriter.Resolved> Blocks, Dictionary<int, CadStyleWriter.Resolved> Styles,
        Dictionary<int, CadDimensionStyleWriter.Resolved> DimensionStyles, Dictionary<int, CadLinetypeWriter.Resolved> NewLinetypes, Dictionary<int, CadViewportWriter.Resolved> Viewports, Dictionary<int, CadLayoutWriter.Resolved> Layouts, Dictionary<int, CadViewportCreator.Resolved> NewViewports, Dictionary<int, CadLayoutEditor.Resolved> LayoutEdits, Dictionary<int, CadStructureLifecycleWriter.Resolved> Lifecycle,
        Dictionary<int, CadViewportLifecycleWriter.Resolved> ViewportLifecycle,
        Dictionary<int, CadTableStyleWriter337.Resolved> TableStyles, Dictionary<int, CadMLeaderStyleWriter337.Resolved> LeaderStyles,
        Dictionary<int,CadNativeStyleLifecycle352.Resolved> NativeStyleLifecycle,
        Dictionary<int,CadViewportClipping358.Resolved> ViewportClipping)
    { public Dictionary<int,CadBlockArray384.Resolved> Arrays {get;} = new(); public Dictionary<int,CadExternalReferences385.Resolved> References {get;} = new(); }
    internal sealed record Prepared(Dictionary<int, LayerTableRecord> Layers, Dictionary<int, CadBlockWriter.Prepared> Blocks,
        Dictionary<int, TextStyleTableRecord> Styles, Dictionary<int, DimStyleTableRecord> DimensionStyles, Dictionary<int, LinetypeTableRecord> NewLinetypes, Dictionary<int, CadViewportWriter.Prepared> Viewports, Dictionary<int, CadLayoutWriter.Prepared> Layouts, Dictionary<int, CadViewportCreator.Prepared> NewViewports,
        Dictionary<int, CadViewportLifecycleWriter.Prepared> ViewportLifecycle,
        Dictionary<int, CadTableStyleWriter337.Prepared> TableStyles, Dictionary<int, MLeaderStyle> LeaderStyles)
    { public Dictionary<int,MInsertBlock> Arrays {get;} = new(); public Dictionary<int,CadExternalReferences385.Prepared> References {get;} = new(); }

    internal static ObjectId Find(Database db, string handle)
    {
        ulong bits = ulong.Parse(handle, NumberStyles.HexNumber, CultureInfo.InvariantCulture);
        if (!db.TryGetObjectId(new Handle(unchecked((long)bits)), out var id) || id.IsNull || !id.IsValid)
            throw new DomainException("OBJECT_NOT_FOUND", "Structural handle was not found in this document.", false, new { handle });
        if (id.IsErased || id.IsEffectivelyErased) throw new DomainException("OBJECT_ERASED", "Structural object was erased.", false, new { handle });
        return id;
    }
    public static Resolved Resolve(StructureWriteArguments args, Database db, Transaction tr, Action cancel, Action<string>? warning = null)
    {
        var blocks = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
        var model = blocks[BlockTableRecord.ModelSpace];
        var blockSpace = args.Operations.Any(op => op is ExternalReferenceEdit385 or DefineBlockEdit or InsertBlockEdit or InsertBlockArray384 or SetBlockArray384 or SetAttributeEdit)
            ? CadBlockSpace.RequireWriteSpace(db,tr,args.EffectiveTargetSpace,cancel) : model;
        var layers = (LayerTable)tr.GetObject(db.LayerTableId, OpenMode.ForRead);
        var linetypes = (LinetypeTable)tr.GetObject(db.LinetypeTableId, OpenMode.ForRead);
        var result = new Resolved(layers.ObjectId, new(), new(), new(), new(), new(), new(), new(), new(), new(), new(), new(), new(), new(), new(), new(), new(),new(),new());
        int newObjects = args.Operations.All(edit=>edit is ViewportLifecycleEdit) ? 0 : ViewportCreateValidation.ValidateBatch(args.Operations);
        var flags = new Dictionary<ObjectId, LayerWriteState>();
        var textValues = new Dictionary<ObjectId, string>();
        var viewportSourceBudget=new ModelInventoryBudget(cancel);
        var viewportSources=new HashSet<ObjectId>();
        LayerWriteState Layer(ObjectId id)
        {
            if (!flags.TryGetValue(id, out var value))
            {
                var record = (LayerTableRecord)tr.GetObject(id, OpenMode.ForRead);
                value = new(record.Name, id == db.Clayer, record.IsDependent, record.IsOff, record.IsFrozen, record.IsLocked);
                flags.Add(id, value);
            }
            return value;
        }
        for (int i = 0; i < args.Operations.Length; i++)
        {
            cancel();
            LayerChanges? properties = null;
            switch (args.Operations[i])
            {
                case SetViewportClipping358 clipping:
                    var clip=CadViewportClipping358.Resolve(args,clipping,db,tr,cancel);
                    viewportSourceBudget.Add(CadViewportClipping358.SourceObservation(clip));
                    result.ViewportClipping.Add(i,clip);result.Names.Add(i,clip.Before.Name+":"+clipping.Handle);break;
                case NativeStyleLifecycle352 nativeStyle:
                    var nativeLifecycle=CadNativeStyleLifecycle352.Resolve(nativeStyle,db,tr,cancel);
                    result.NativeStyleLifecycle.Add(i,nativeLifecycle);result.Names.Add(i,nativeStyle.Name??nativeStyle.ExpectedName);break;
                case TableStyleEdit337 tableStyle:
                    var resolvedTableStyle=CadTableStyleWriter337.Resolve(tableStyle,db,tr,cancel);
                    result.TableStyles.Add(i,resolvedTableStyle);result.Names.Add(i,tableStyle.Name??resolvedTableStyle.SourceName);break;
                case MLeaderStyleEdit337 leaderStyle:
                    var resolvedLeaderStyle=CadMLeaderStyleWriter337.Resolve(args,leaderStyle,db,tr,cancel);
                    result.LeaderStyles.Add(i,resolvedLeaderStyle);result.Names.Add(i,leaderStyle.Name??leaderStyle.ExpectedName!);break;
                case ViewportLifecycleEdit lifecycleViewport:
                    var lifecycleView = CadViewportLifecycleWriter.Resolve(args,lifecycleViewport,db,tr,cancel,
                        result.ViewportLifecycle.Values.FirstOrDefault(item=>item.Edit.Handle==lifecycleViewport.Handle)??result.ViewportLifecycle.Values.FirstOrDefault());
                    if(viewportSources.Add(lifecycleView.Source))viewportSourceBudget.Add(new{lifecycleView.Before,lifecycleView.Appearance,lifecycleView.NativeData});
                    result.ViewportLifecycle.Add(i,lifecycleView);result.Names.Add(i,lifecycleView.Name);break;
                case StructureLifecycleEdit lifecycle:
                    var recordLifecycle=CadStructureLifecycleWriter.Resolve(args,lifecycle,db,tr,cancel);
                    result.Lifecycle.Add(i,recordLifecycle);result.Names.Add(i,lifecycle.Name??lifecycle.ExpectedName);break;
                case RenameLayoutEdit:
                case SetLayoutPropertiesEdit:
                    var layoutEdit = CadLayoutEditor.Resolve(args, args.Operations[i], db, tr, cancel);
                    result.LayoutEdits.Add(i, layoutEdit); result.Names.Add(i, layoutEdit.Name); break;
                case SetCurrentLayerEdit current:
                    var currentTarget = CadCurrentLayerWriter.Resolve(current, db, tr);
                    result.Layers.Add(i, currentTarget); result.Names.Add(i, ((LayerTableRecord)tr.GetObject(currentTarget, OpenMode.ForRead)).Name); break;
                case RenameLayerEdit rename:
                    result.Layers.Add(i,CadLayerRenamer.Resolve(rename,db,tr));result.Names.Add(i,rename.Name);break;
                case CreateViewportEdit newViewport:
                    var resolvedNewViewport = CadViewportCreator.Resolve(args, newViewport, db, tr, cancel);
                    result.NewViewports.Add(i, resolvedNewViewport); result.Names.Add(i, resolvedNewViewport.Name + ":FFFFFFFFFFFFFFFF"); break;
                case CreateLayoutEdit layout:
                    var resolvedLayout = CadLayoutWriter.Resolve(args, layout, i, db, tr, cancel);
                    result.Layouts.Add(i, resolvedLayout); result.Names.Add(i, resolvedLayout.Name); break;
                case SetViewportViewEdit view:
                    var resolvedView = CadViewportWriter.Resolve(args, view, db, tr, cancel);
                    result.Viewports.Add(i, resolvedView); result.Names.Add(i, resolvedView.Name); break;
                case CreateLinetypeEdit newLinetype:
                    var resolvedLinetype = CadLinetypeWriter.Resolve(args, newLinetype, db, tr, cancel);
                    result.NewLinetypes.Add(i, resolvedLinetype); result.Names.Add(i, resolvedLinetype.Name);
                    break;
                case CreateDimensionStyleEdit dimensionStyle:
                    var resolvedDimension = CadDimensionStyleWriter.Resolve(args, dimensionStyle, db, tr, cancel);
                    result.DimensionStyles.Add(i, resolvedDimension); result.Names.Add(i, resolvedDimension.Name);
                    if (resolvedDimension.NotationWarning is {} dimensionNotice) warning?.Invoke(dimensionNotice);
                    break;
                case SetDimensionStylePropertiesEdit dimensionStyleEdit:
                    var resolvedDimensionEdit = CadDimensionStyleWriter.Resolve(args, dimensionStyleEdit, db, tr, cancel);
                    result.DimensionStyles.Add(i, resolvedDimensionEdit); result.Names.Add(i, resolvedDimensionEdit.Name);
                    if (resolvedDimensionEdit.NotationWarning is {} dimensionEditNotice) warning?.Invoke(dimensionEditNotice); break;
                case SetTextStyleFontEdit fontEdit:
                    var fontStyle=FontCoverage.Resolve(fontEdit,db,tr,cancel,warning);
                    result.Layers.Add(i,fontStyle.ObjectId);result.Names.Add(i,fontStyle.Name);break;
                case CreateTextStyleEdit style:
                    var resolvedStyle = CadStyleWriter.Resolve(style, db, tr, cancel);
                    result.Styles.Add(i, resolvedStyle); result.Names.Add(i, resolvedStyle.Name);
                    break;
                case SetTextStylePropertiesEdit styleEdit:
                    var resolvedStyleEdit = CadStyleWriter.Resolve(styleEdit, db, tr, cancel);
                    result.Styles.Add(i, resolvedStyleEdit); result.Names.Add(i, resolvedStyleEdit.Name); break;
                case CreateLayerEdit create:
                    if (layers.Has(create.Name)) throw new DomainException("LAYER_EXISTS", "Layer already exists; creation does not overwrite it.", false, new { operation_index = i, name = create.Name });
                    properties = create.Properties; result.Names.Add(i, create.Name);
                    break;
                case SetLayerEdit edit:
                    var layerId = Find(db, edit.Handle);
                    if (tr.GetObject(layerId, OpenMode.ForRead) is not LayerTableRecord layer || layer.GetType() != typeof(LayerTableRecord) || layer.OwnerId != layers.ObjectId)
                        throw new DomainException("OUT_OF_SCOPE", "Expected a native layer-table record from this database.");
                    flags[layerId] = Layer(layerId).Change(edit.Properties);
                    result.Layers.Add(i, layerId); result.Names.Add(i, layer.Name); properties = edit.Properties;
                    break;
                case ExternalReferenceEdit385 reference:
                    var referenced=CadExternalReferences385.Resolve(reference,args,db,tr,blockSpace,Layer,cancel);result.References.Add(i,referenced);result.Names.Add(i,referenced.Name);break;
                case SetAttributeEdit edit:
                    var attributeTarget=CadBlockAttributeEditor.Resolve(edit,db,tr,blockSpace,Layer,textValues,cancel);
                    result.Attributes.Add(i,new(attributeTarget.Block,attributeTarget.Attribute));result.Names.Add(i,attributeTarget.Tag);break;
                case InsertBlockArray384:
                case SetBlockArray384:
                    var array = CadBlockArray384.Resolve(args.Operations[i],db,tr,blockSpace,Layer,cancel);
                    result.Arrays.Add(i,array); result.Names.Add(i,array.Definition.Name); break;
                case DefineBlockEdit:
                case InsertBlockEdit:
                    var block = CadBlockWriter.Resolve(args.Operations[i], db, tr, blockSpace, Layer, cancel, warning);
                    result.Blocks.Add(i, block); result.Names.Add(i, block.Name);
                    if (args.Operations[i] is InsertBlockEdit) newObjects += block.AttributeIds.Length;
                    BlockWriteValidation.CheckObjectBudget(newObjects);
                    break;
                default: throw new DomainException("INVALID_ARGUMENT", "Unknown structural write operation.");
            }
            if (properties?.Linetype is { } pattern)
            {
                if (!linetypes.Has(pattern)) throw new DomainException("LINETYPE_UNAVAILABLE", "Linetype must already exist; no external file is loaded.");
                var record = (LinetypeTableRecord)tr.GetObject(linetypes[pattern], OpenMode.ForRead);
                if (record.IsDependent) throw new DomainException("LINETYPE_UNAVAILABLE", "Dependent linetypes cannot be assigned.");
                result.Linetypes.Add(i, record.ObjectId);
            }
        }
        CadExternalReferences385.Batch(result.References.Values);
        var resultCapacity=new SerializedCapacity357(Wire.PayloadBudget-8192,cancel);
        var projected = args.Operations.Select((operation, index) => {cancel();var change=result.References.TryGetValue(index,out var reference) ? CadExternalReferences385.Project(args,index,reference) : result.Arrays.TryGetValue(index,out var array) ? CadBlockArray384.Project(args,index,array) : result.Blocks.TryGetValue(index, out var block)
            ? CadBlockWriter.Project(args, index, block)
            : new StructureChange(index, operation.Operation, new(args.BridgeId, args.DocumentId, "FFFFFFFFFFFFFFFF"),
                operation is NativeStyleLifecycle352 nativeLifecycle ? NativeStyleLifecycleValidation352.RecordKind(nativeLifecycle) : operation is TableStyleEdit337 ? "table_style" : operation is MLeaderStyleEdit337 ? "mleader_style" : operation is StructureLifecycleEdit lifecycle ? StructureLifecycleValidation.Kind(lifecycle) : operation is CreateLayoutEdit or RenameLayoutEdit or SetLayoutPropertiesEdit ? "layout" : operation is SetViewportViewEdit or CreateViewportEdit or SetViewportClipping358 or ViewportLifecycleEdit ? "viewport" : operation is CreateLinetypeEdit ? "linetype" : operation is SetAttributeEdit ? "attribute" : operation is CreateTextStyleEdit or SetTextStyleFontEdit or SetTextStylePropertiesEdit ? "text_style" : operation is CreateDimensionStyleEdit or SetDimensionStylePropertiesEdit ? "dimension_style" : "layer",
                result.Names[index], operation is TableStyleEdit337{Kind:"create_table_style"} or MLeaderStyleEdit337{Kind:"create_mleader_style"} or CopyViewportEdit or CreateViewportEdit or CreateLayoutEdit or CreateLayerEdit or CreateTextStyleEdit or CreateDimensionStyleEdit or CreateLinetypeEdit);resultCapacity.Add(change);return change;}).ToArray();
        var projectedClipping=result.ViewportClipping.Count==0?null:result.ViewportClipping.Select(pair=>CadViewportClipping358.ProjectResult(pair.Key,pair.Value)).ToArray();
        if(projectedClipping!=null)warning?.Invoke(CadViewportClipping358.Warning);
        var projectedViewportLifecycle=result.ViewportLifecycle.Count==0?null:result.ViewportLifecycle.Select(pair=>CadViewportLifecycleWriter.ProjectResult(pair.Key,pair.Value)).ToArray();
        var projectedTableStyles=result.TableStyles.Count==0?null:result.TableStyles.Select(pair=>new TableStyleChange337(pair.Key,
            new(args.BridgeId,args.DocumentId,"FFFFFFFFFFFFFFFF"),new(args.BridgeId,args.DocumentId,pair.Value.Edit.SourceHandle),new string('F',64),pair.Value.Consumers.Length)).ToArray();
        if (JsonSerializer.SerializeToUtf8Bytes(new ModifiedStructure(args.OperationId, "committed", false, projected, "single_command",projectedViewportLifecycle,projectedTableStyles,viewport_clipping_changes:projectedClipping), Wire.Json).Length > Wire.PayloadBudget - 8192)
            throw new DomainException("RESPONSE_LIMIT_EXCEEDED", "Projected structural result exceeds the bounded payload; split the batch.");
        return result;
    }
    private static void ApplyProperties(LayerTableRecord record, LayerChanges changes, ObjectId linetype)
    {
        if (changes.ColorAci is { } aci)
        {
            using var color = Teigha.Colors.Color.FromColorIndex(Teigha.Colors.ColorMethod.ByAci, (short)aci);
            record.Color = color;
        }
        if (!linetype.IsNull) record.LinetypeObjectId = linetype;
        if (changes.LineweightHundredthMm is { } weight) record.LineWeight = (LineWeight)weight;
        if (changes.IsOff is { } off) record.IsOff = off;
        if (changes.IsFrozen is { } frozen) record.IsFrozen = frozen;
        if (changes.IsLocked is { } locked) record.IsLocked = locked;
        if (changes.IsPlottable is { } plot) record.IsPlottable = plot;
        using var actualColor = record.Color;
        if (changes.ColorAci is { } requestedAci && (!actualColor.IsByAci || actualColor.ColorIndex != requestedAci) ||
            !linetype.IsNull && record.LinetypeObjectId != linetype ||
            changes.LineweightHundredthMm is { } requestedWeight && (int)record.LineWeight != requestedWeight ||
            changes.IsOff is { } requestedOff && record.IsOff != requestedOff ||
            changes.IsFrozen is { } requestedFrozen && record.IsFrozen != requestedFrozen ||
            changes.IsLocked is { } requestedLocked && record.IsLocked != requestedLocked ||
            changes.IsPlottable is { } requestedPlot && record.IsPlottable != requestedPlot)
            throw new DomainException("API_EXCEPTION", "Native layer setters did not retain the requested properties.");
        CadAppearanceProperties.Apply(record, changes.ColorRgb, changes.Transparency, changes.Description);
    }
    // Validate applicable native setters on owned, detached copies before
    // opening originals ForWrite. Raster/Xref creation and raster loading use
    // the registered database lifecycle in Apply. New layers remain prepared.
    public static Prepared Preview(StructureWriteArguments args, Resolved resolved,
        Transaction tr, Database db, List<DBObject> owned, Action cancel,Action<string>? trace=null)
    {
        var created = new Dictionary<int, LayerTableRecord>();
        var blocks = new Dictionary<int, CadBlockWriter.Prepared>();
        var styles = new Dictionary<int, TextStyleTableRecord>();
        var dimensions = new Dictionary<int, DimStyleTableRecord>();
        var newLinetypes = new Dictionary<int, LinetypeTableRecord>();
        var viewports = new Dictionary<int, CadViewportWriter.Prepared>();
        var layouts = new Dictionary<int, CadLayoutWriter.Prepared>();
        var newViewports = new Dictionary<int, CadViewportCreator.Prepared>();
        var viewportLifecycle = new Dictionary<int, CadViewportLifecycleWriter.Prepared>();
        var tableStyles = new Dictionary<int, CadTableStyleWriter337.Prepared>();
        var leaderStyles = new Dictionary<int, MLeaderStyle>();
        var arrays = new Dictionary<int,MInsertBlock>();
        var references = new Dictionary<int,CadExternalReferences385.Prepared>();
        var copies = new Dictionary<ObjectId, DBObject>();
        var multilinePreviews = new Dictionary<ObjectId, string>();
        DBObject Copy(ObjectId id)
        {
            if (!copies.TryGetValue(id, out var copy))
            {
                copy = (DBObject)tr.GetObject(id, OpenMode.ForRead).Clone(); owned.Add(copy);
                if (!copy.ObjectId.IsNull) throw new DomainException("API_EXCEPTION", "Preview clone is not detached; no write was opened.");
                copies.Add(id, copy);
            }
            return copy;
        }
        for (int i = 0; i < args.Operations.Length; i++)
        {
            cancel(); var linetype = resolved.Linetypes.TryGetValue(i, out var id) ? id : ObjectId.Null;
            switch (args.Operations[i])
            {
                case SetViewportClipping358:
                    CadViewportClipping358.Verify(resolved.ViewportClipping[i],db,tr,cancel);break;
                case NativeStyleLifecycle352:
                    CadNativeStyleLifecycle352.Verify(resolved.NativeStyleLifecycle[i],db,tr,cancel);break;
                case TableStyleEdit337:
                    tableStyles.Add(i,CadTableStyleWriter337.Preview(resolved.TableStyles[i],db,tr,owned,cancel));break;
                case MLeaderStyleEdit337:
                    leaderStyles.Add(i,CadMLeaderStyleWriter337.Preview(resolved.LeaderStyles[i],tr,db,owned,cancel));break;
                case ViewportLifecycleEdit:
                    viewportLifecycle.Add(i,CadViewportLifecycleWriter.Preview(resolved.ViewportLifecycle[i],tr,db,owned,cancel));break;
                case StructureLifecycleEdit:
                    CadStructureLifecycleWriter.Preview(resolved.Lifecycle[i],db,tr,owned,cancel);break;
                case RenameLayoutEdit:
                case SetLayoutPropertiesEdit:
                    CadLayoutEditor.Preview(resolved.LayoutEdits[i], tr, owned, cancel); break;
                case SetCurrentLayerEdit:
                    // Database.Clayer has no detached preview. Resolve validates the resident identities and flags.
                    break;
                case CreateViewportEdit:
                    newViewports.Add(i, CadViewportCreator.Preview(resolved.NewViewports[i], tr, db, owned, cancel)); break;
                case CreateLayoutEdit:
                    layouts.Add(i, CadLayoutWriter.Preview(resolved.Layouts[i], tr, db, owned, cancel)); break;
                case SetViewportViewEdit:
                    viewports.Add(i, CadViewportWriter.Preview(resolved.Viewports[i], tr, db, owned, cancel)); break;
                case CreateLinetypeEdit:
                    newLinetypes.Add(i, CadLinetypeWriter.Preview(resolved.NewLinetypes[i], tr, db, owned, cancel)); break;
                case CreateDimensionStyleEdit:
                case SetDimensionStylePropertiesEdit:
                    dimensions.Add(i, CadDimensionStyleWriter.Preview(resolved.DimensionStyles[i], tr, db, owned, cancel)); break;
                case SetTextStyleFontEdit fontEdit:
                    FontCoverage.Apply((TextStyleTableRecord)Copy(resolved.Layers[i]),fontEdit);break;
                case CreateTextStyleEdit:
                case SetTextStylePropertiesEdit:
                    styles.Add(i, CadStyleWriter.Preview(resolved.Styles[i], tr, owned, cancel)); break;
                case CreateLayerEdit create:
                    var record = new LayerTableRecord(); owned.Add(record);
                    record.Name = create.Name; ApplyProperties(record, create.Properties, linetype); created.Add(i, record); break;
                case SetLayerEdit edit:
                    ApplyProperties((LayerTableRecord)Copy(resolved.Layers[i]), edit.Properties, linetype); break;
                case RenameLayerEdit rename:
                    CadLayerRenamer.Preview(rename,owned);break;
                case ExternalReferenceEdit385:
                    int referenceIndex=i;trace?.Invoke("reference_"+referenceIndex+"_preview_before");
                    references.Add(i,CadExternalReferences385.Preview(resolved.References[i],owned,cancel,value=>trace?.Invoke("reference_"+referenceIndex+"_preview_"+value)));
                    trace?.Invoke("reference_"+referenceIndex+"_preview_returned");break;
                case SetAttributeEdit edit when edit.ContentType=="mtext":
                    var attributeId=resolved.Attributes[i].Attribute;
                    multilinePreviews.TryGetValue(attributeId,out var preceding);
                    NativeAttributeMText377.Preview((AttributeReference)tr.GetObject(attributeId,OpenMode.ForRead),edit,preceding,cancel);
                    multilinePreviews[attributeId]=edit.Text;break;
                case SetAttributeEdit edit:
                    CadBlockAttributeEditor.Preview((AttributeReference)Copy(resolved.Attributes[i].Attribute),edit,db,tr,cancel);break;
                case InsertBlockArray384:
                case SetBlockArray384:
                    arrays.Add(i,CadBlockArray384.Preview(resolved.Arrays[i],tr,owned,cancel)); break;
                case DefineBlockEdit:
                case InsertBlockEdit:
                    blocks.Add(i, CadBlockWriter.Preview(args.Operations[i], resolved.Blocks[i], tr, owned, cancel)); break;
                default: throw new DomainException("INVALID_ARGUMENT", "Unknown structural preview operation.");
            }
        }
        var prepared = new Prepared(created, blocks, styles, dimensions, newLinetypes, viewports, layouts, newViewports, viewportLifecycle,tableStyles,leaderStyles);
        foreach(var array in arrays) prepared.Arrays.Add(array.Key,array.Value);
        foreach(var reference in references) prepared.References.Add(reference.Key,reference.Value);
        return prepared;
    }
    public static ModifiedStructure Apply(StructureWriteArguments args, Resolved resolved, Prepared prepared,
        Transaction tr, Database db, WriteJob job, Action<string>? trace = null,List<string>? nativeFormatWarnings = null)
    {
        if(resolved.ViewportClipping.Count>0)
        {
            var clipChanges=CadViewportClipping358.ApplyGroup(resolved.ViewportClipping,db,tr,job);
            var clipResults=resolved.ViewportClipping.Select(pair=>CadViewportClipping358.ProjectResult(pair.Key,pair.Value)).ToArray();
            return new(args.OperationId,"committed",false,clipChanges,"not_guaranteed",viewport_clipping_changes:clipResults);
        }
        var changes = new List<StructureChange>();
        var viewportLifecycleResults = new List<ViewportLifecycleChange>();
        var tableStyleChanges = new List<TableStyleChange337>();
        for (int i = 0; i < args.Operations.Length; i++)
        {
            job.CheckCancellation(); ObjectId target; string kind; bool isNew = false;
            var linetype = resolved.Linetypes.TryGetValue(i, out var id) ? id : ObjectId.Null;
            switch (args.Operations[i])
            {
                case ExternalReferenceEdit385:
                    changes.Add(CadExternalReferences385.Apply(args,i,resolved.References[i],prepared.References[i],db,tr,job,trace));continue;
                case NativeStyleLifecycle352:
                    changes.Add(CadNativeStyleLifecycle352.Apply(args,i,resolved.NativeStyleLifecycle[i],db,tr,job,nativeFormatWarnings));continue;
                case TableStyleEdit337:
                    var tableStyleChange=CadTableStyleWriter337.Apply(args,i,resolved.TableStyles[i],prepared.TableStyles[i],db,tr,job);
                    changes.Add(tableStyleChange.Change);tableStyleChanges.Add(tableStyleChange.Details);
                    if(tableStyleChange.Details.warnings is{} styleWarnings)nativeFormatWarnings?.AddRange(styleWarnings);continue;
                case MLeaderStyleEdit337:
                    changes.Add(CadMLeaderStyleWriter337.Apply(i,resolved.LeaderStyles[i],prepared.LeaderStyles[i],tr,db,job,nativeFormatWarnings));continue;
                case ViewportLifecycleEdit:
                    var viewportChange=CadViewportLifecycleWriter.Apply(i,resolved.ViewportLifecycle[i],prepared.ViewportLifecycle[i],tr,db,job,trace);
                    changes.Add(viewportChange.Change);viewportLifecycleResults.Add(viewportChange.Result);continue;
                case RenameLayoutEdit:
                case SetLayoutPropertiesEdit:
                    changes.Add(CadLayoutEditor.Apply(i, resolved.LayoutEdits[i], tr, db, job)); continue;
                case SetCurrentLayerEdit current:
                    target = resolved.Layers[i]; CadCurrentLayerWriter.Apply(current, target, db, tr); kind = "layer"; break;
                case StructureLifecycleEdit:
                    changes.Add(CadStructureLifecycleWriter.Apply(i,resolved.Lifecycle[i],db,tr,job));continue;
                case CreateViewportEdit:
                    changes.Add(CadViewportCreator.Apply(i, resolved.NewViewports[i], prepared.NewViewports[i], tr, db, job, trace)); continue;
                case CreateLayoutEdit:
                    changes.Add(CadLayoutWriter.Apply(i, resolved.Layouts[i], prepared.Layouts[i], tr, db, job)); continue;
                case SetViewportViewEdit:
                    changes.Add(CadViewportWriter.Apply(i, resolved.Viewports[i], prepared.Viewports[i], tr, db, job)); continue;
                case CreateLinetypeEdit:
                    changes.Add(CadLinetypeWriter.Apply(i, resolved.NewLinetypes[i], prepared.NewLinetypes[i], tr, db, job)); continue;
                case CreateDimensionStyleEdit:
                case SetDimensionStylePropertiesEdit:
                    changes.Add(CadDimensionStyleWriter.Apply(args, i, resolved.DimensionStyles[i], prepared.DimensionStyles[i], tr, db, job)); continue;
                case SetTextStyleFontEdit fontEdit:
                    target=resolved.Layers[i]; FontCoverage.Apply((TextStyleTableRecord)tr.GetObject(target,OpenMode.ForWrite),fontEdit); FontCoverage.RefreshDimensions(db,tr,target,job.CheckCancellation); kind="text_style";break;
                case CreateTextStyleEdit:
                case SetTextStylePropertiesEdit:
                    changes.Add(CadStyleWriter.Apply(args, i, resolved.Styles[i], prepared.Styles[i], tr, job)); continue;
                case CreateLayerEdit:
                    var table = (LayerTable)tr.GetObject(resolved.LayerTable, OpenMode.ForWrite);
                    var record = prepared.Layers[i]; target = table.Add(record); tr.AddNewlyCreatedDBObject(record, true);
                    kind = "layer"; isNew = true; break;
                case SetLayerEdit edit:
                    target = resolved.Layers[i]; ApplyProperties((LayerTableRecord)tr.GetObject(target, OpenMode.ForWrite), edit.Properties, linetype);
                    kind = "layer"; break;
                case RenameLayerEdit rename:
                    target=resolved.Layers[i];CadLayerRenamer.Apply(rename,target,db,tr);kind="layer";break;
                case SetAttributeEdit edit:
                    target = resolved.Attributes[i].Attribute;
                    var attributeSpace=CadBlockSpace.RequireWriteSpace(db,tr,args.EffectiveTargetSpace,job.CheckCancellation);
                    CadBlockAttributeEditor.Apply(edit,resolved.Attributes[i].Block,target,attributeSpace,db,tr,job.CheckCancellation);
                    kind = "attribute"; break;
                case InsertBlockArray384:
                case SetBlockArray384:
                    changes.Add(CadBlockArray384.Apply(args,i,resolved.Arrays[i],prepared.Arrays[i],db,tr,job)); continue;
                case DefineBlockEdit:
                case InsertBlockEdit:
                    changes.Add(CadBlockWriter.Apply(args, i, resolved.Blocks[i], prepared.Blocks[i], tr, job)); continue;
                default: throw new DomainException("INVALID_ARGUMENT", "Unknown structural write operation.");
            }
            changes.Add(new(i, args.Operations[i].Operation, new(args.BridgeId, args.DocumentId, Validation.NormalizeHandle(target.Handle.ToString())), kind, resolved.Names[i], isNew));
        }
        // Newly created viewport state becomes final only after transaction disposal.
        // The caller verifies both new and existing paper entities after Commit.
        return new(args.OperationId, "committed", false, changes.ToArray(), "single_command", viewportLifecycleResults.Count==0?null:viewportLifecycleResults.ToArray(),tableStyleChanges.Count==0?null:tableStyleChanges.ToArray());
    }
}

