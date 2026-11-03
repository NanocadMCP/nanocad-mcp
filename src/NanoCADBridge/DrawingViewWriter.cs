using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text.Json;
using Multicad;
using Multicad.DatabaseServices;
using Multicad.Geometry;
using Multicad.Mc3D;
using NanoCAD.Contracts;
using NanoCAD.Ipc;
using Db=Teigha.DatabaseServices;

namespace NanoCADBridge;

// Separate MultiCAD tracker: never mix this operation with the Teigha batch transaction.
internal static class DrawingViewWriter
{
    public static bool Handles(StructureWriteArguments args)=>args.Operations.Length==1 && args.Operations[0] is CreateDrawingViewsEdit;
    private static void Need(bool condition,string code,string message)
    { if(!condition)throw new DomainException(code,message); }
    private static (string Name,DrawingView.ViewDirectionType Direction) Projection(string direction)=>direction switch
    {
        "bottom"=>("Bottom",DrawingView.ViewDirectionType.kViewBottom),
        "left"=>("Left",DrawingView.ViewDirectionType.kViewLeft),
        "right"=>("Right",DrawingView.ViewDirectionType.kViewRight),
        "iso_top_left"=>("IsoTopLeft",DrawingView.ViewDirectionType.kViewIsoTopLeft),
        "iso_top_right"=>("IsoTopRight",DrawingView.ViewDirectionType.kViewIsoTopRight),
        "iso_bottom_left"=>("IsoBottomLeft",DrawingView.ViewDirectionType.kViewIsoBottomLeft),
        "iso_bottom_right"=>("IsoBottomRight",DrawingView.ViewDirectionType.kViewIsoBottomRight),
        _=>throw new DomainException("INVALID_ARGUMENT","Unsupported projected direction.")
    };
    public static void Preflight(StructureWriteArguments args,DocumentState state,Db.Transaction tx,Action cancel)
    {
        Need(Handles(args),"INVALID_OPERATION_ORDER","Drawing views require a separate request.");
        if(args.Operations[0] is CreateDrawingViewsEdit{LayoutHandle:not null})
        {PaperDrawingViewScope356.Prepare(args,state,tx,cancel);return;}
        var loaded=AppDomain.CurrentDomain.GetAssemblies().Select(a=>a.GetName().Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        Need(loaded.Contains("mapimgd") && loaded.Contains("MapiBaseTypes"),"CAPABILITY_UNAVAILABLE","MultiCAD runtime is not already loaded.");
        var db=state.Document.Database;
        var table=(Db.BlockTable)tx.GetObject(db.BlockTableId,Db.OpenMode.ForRead);
        var model=table[Db.BlockTableRecord.ModelSpace];
        Need(db.CurrentSpaceId==model,"OUT_OF_SCOPE","Activate Model Space.");
        var layer=(Db.LayerTableRecord)tx.GetObject(db.Clayer,Db.OpenMode.ForRead);
        Need(layer.Name=="0" && !layer.IsLocked && !layer.IsFrozen && !layer.IsOff && !layer.IsDependent,"LAYER_UNAVAILABLE","Current layer 0 must be visible, thawed and unlocked.");
        var layerTable=(Db.LayerTable)tx.GetObject(db.LayerTableId,Db.OpenMode.ForRead);
        Need(layerTable.Has("VIEWS"),"LAYER_UNAVAILABLE","Pre-create the native VIEWS layer with create_layer in a separate committed request before creating views.");
        var viewLayer=(Db.LayerTableRecord)tx.GetObject(layerTable["VIEWS"],Db.OpenMode.ForRead);
        Need(!viewLayer.IsLocked && !viewLayer.IsFrozen && !viewLayer.IsOff && !viewLayer.IsDependent,"LAYER_UNAVAILABLE","The native VIEWS layer must be visible, thawed, unlocked and local.");
        var spec=(CreateDrawingViewsEdit)args.Operations[0];
        ResolveSources(spec,state,tx,cancel);
        var existing=ExistingViews(state,cancel);
        var names=new List<string>{spec.NamePrefix+" Front"};
        if(spec.TopOrigin!=null)names.Add(spec.NamePrefix+" Top");
        names.AddRange(spec.ProjectedViews.Select(p=>spec.NamePrefix+" "+Projection(p.Direction).Name));
        Need(!existing.Any(v=>names.Contains(v.Name,StringComparer.OrdinalIgnoreCase)),"NAME_COLLISION","A generated view name already exists; choose a different prefix.");
    }
    [MethodImpl(MethodImplOptions.NoInlining)]
    internal static List<McObjectId> ResolveSources(CreateDrawingViewsEdit spec,DocumentState state,Db.Transaction tx,Action cancel)
    {
        var active=McDocument.ActiveDocument;var db=state.Document.Database;
        Need(state.Document.IsNamedDrawing && active!=null && string.Equals(Path.GetFullPath(active.Path),Path.GetFullPath(state.Document.Name),StringComparison.OrdinalIgnoreCase),"DOCUMENT_CONTEXT_MISMATCH","The named MultiCAD document must match the active drawing.");
        var table=(Db.BlockTable)tx.GetObject(db.BlockTableId,Db.OpenMode.ForRead);
        var model=(Db.BlockTableRecord)tx.GetObject(table[Db.BlockTableRecord.ModelSpace],Db.OpenMode.ForRead);
        var result=new List<McObjectId>();
        void Add(Db.ObjectId id)
        {
            cancel();Need(!id.IsErased&&!id.IsEffectivelyErased,"OBJECT_ERASED","Source body was erased.");
            var entity=tx.GetObject(id,Db.OpenMode.ForRead) as Db.Solid3d;
            Need(entity!=null&&entity.GetType()==typeof(Db.Solid3d)&&entity.OwnerId==model.ObjectId,"UNSUPPORTED_ENTITY_TYPE","Each source must be a top-level direct or native solid body in Model Space.");
            var native=McObjectId.FromHandle(id.Handle.Value,active!.ID);
            Need(!native.IsNull,"UNSUPPORTED_ENTITY_TYPE","Source cannot be addressed by the native projection engine.");
            result.Add(native);
        }
        if(spec.SourceScope=="all_model_solids")
        {
            foreach(Db.ObjectId id in model)
            {
                cancel();if(id.IsErased||id.IsEffectivelyErased)continue;
                if(id.ObjectClass.DxfName.Equals("3DSOLID",StringComparison.OrdinalIgnoreCase))Add(id);
            }
        }
        else foreach(var handle in spec.SourceHandles??new[]{spec.SourceHandle})
        {
            Need(db.TryGetObjectId(new Db.Handle(unchecked((long)Convert.ToUInt64(handle,16))),out var id)&&!id.IsNull&&id.IsValid,"OBJECT_NOT_FOUND","Source body not found.");Add(id);
        }
        Need(result.Count>0,"OBJECT_NOT_FOUND","The requested scope contains no top-level solid bodies.");
        return result;
    }
    private static List<DrawingView> ExistingViews(DocumentState state,Action cancel)
    {
        using var tx=state.Document.Database.TransactionManager.StartTransaction();
        var table=(Db.BlockTable)tx.GetObject(state.Document.Database.BlockTableId,Db.OpenMode.ForRead);
        var result=new List<DrawingView>();
        foreach(Db.ObjectId blockId in table)
        {
            var block=(Db.BlockTableRecord)tx.GetObject(blockId,Db.OpenMode.ForRead);if(!block.IsLayout)continue;
            foreach(Db.ObjectId id in block)
            {
                cancel();if(id.IsErased||id.IsEffectivelyErased||id.ObjectClass.DxfName.Equals("3DSOLID",StringComparison.OrdinalIgnoreCase))continue;
                var native=McObjectId.FromHandle(id.Handle.Value,McDocument.ActiveDocument.ID);
                var view=native.IsNull?null:native.GetObjectOfType<DrawingView>();if(view!=null)result.Add(view);
            }
        }
        return result;
    }
    private static double Finite(double value)
    { Need(double.IsFinite(value),"MODEL_STATE_UNAVAILABLE","Nonfinite geometry cannot be verified.");return value; }
    private static object Plane(Plane3d p)=>new
    {
        origin=new[]{Finite(p.Origin.X),Finite(p.Origin.Y),Finite(p.Origin.Z)},
        axis_u=new[]{Finite(p.DirU.X),Finite(p.DirU.Y),Finite(p.DirU.Z)},
        axis_v=new[]{Finite(p.DirV.X),Finite(p.DirV.Y),Finite(p.DirV.Z)},
        normal=new[]{Finite(p.Normal.X),Finite(p.Normal.Y),Finite(p.Normal.Z)}
    };
    private static Plane3d ProjectionPlane(CreateDrawingViewsEdit spec)=>spec.ProjectionPlane is {} p
        ?new(new Point3d(p.Origin.X,p.Origin.Y,p.Origin.Z),new Vector3d(p.XAxis.X,p.XAxis.Y,p.XAxis.Z),new Vector3d(p.YAxis.X,p.YAxis.Y,p.YAxis.Z))
        :new(Point3d.Origin,Vector3d.XAxis);
    private static JsonElement Capture(DocumentState state,Action cancel)
        => Observe(state.Document,cancel);
    internal static JsonElement Observe(HostMgd.ApplicationServices.Document doc,Action cancel,bool allowPaperViews=false)
    {
        var nativeSections=new List<object>();
        var paperViews=new List<object>();var measured=allowPaperViews?new ModelInventoryBudget(cancel):null;
        var identityBudget=allowPaperViews?new ModelInventoryBudget(cancel):null;
        Db.ObjectId[] Inventory(IEnumerable<Db.ObjectId> source,bool effective=false)
        {
            if(!allowPaperViews)return source.Where(id=>!id.IsErased&&(!effective||!id.IsEffectivelyErased)).ToArray();
            var current=new List<Db.ObjectId>();
            foreach(var id in source)
            {cancel();if(id.IsNull||!id.IsValid||id.IsErased||effective&&id.IsEffectivelyErased)continue;identityBudget!.Add(id.Handle.ToString());current.Add(id);}
            return current.ToArray();
        }
        var mc=McDocument.ActiveDocument;
        Need(mc!=null && string.Equals(Path.GetFullPath(mc.Path),Path.GetFullPath(doc.Name),StringComparison.OrdinalIgnoreCase),"DOCUMENT_CONTEXT_MISMATCH","MultiCAD context changed.");
        using var tx=doc.Database.TransactionManager.StartTransaction();
        var table=(Db.BlockTable)tx.GetObject(doc.Database.BlockTableId,Db.OpenMode.ForRead);
        var blockIds=Inventory(table.Cast<Db.ObjectId>());
        
        var model=(Db.BlockTableRecord)tx.GetObject(table[Db.BlockTableRecord.ModelSpace],Db.OpenMode.ForRead);
        var ids=Inventory(model.Cast<Db.ObjectId>(),true);
        
        var entities=new List<object>();var bodies=new List<object>();var views=new List<object>();
        foreach(var id in ids)
        {
            cancel();var entity=tx.GetObject(id,Db.OpenMode.ForRead) as Db.Entity;
            Need(entity!=null && entity.OwnerId==model.ObjectId,"MODEL_STATE_UNAVAILABLE","Invalid model entity ownership.");
            var mcId=McObjectId.FromHandle(id.Handle.Value,mc!.ID);
            var view=mcId.IsNull?null:mcId.GetObjectOfType<DrawingView>();
            var section=mcId.IsNull?null:mcId.GetObjectOfType<Section>();
            if(section!=null)
            {
                Need(section.ID==mcId,"MODEL_STATE_UNAVAILABLE","Section identity unavailable.");
                nativeSections.Add(new{handle=id.Handle.ToString(),data=SectionViewState.Read(section,cancel)});
                measured?.Add(nativeSections[^1]);
            }
            entities.Add(new{handle=id.Handle.ToString(),type=entity!.GetType().FullName,layer=entity.LayerId.Handle.ToString(),color=entity.ColorIndex,visible=entity.Visible});
            measured?.Add(entities[^1]);
            if(view!=null)
            {
                Need(view.ID==mcId,"ANALYSIS_LIMIT_EXCEEDED","View identity is invalid.");
                var sources=view.GetTop3dEntitiesInView();var children=view.ChildViews;
                Need(sources!=null,"MODEL_STATE_UNAVAILABLE","View associations unavailable.");
                views.Add(new{handle=id.Handle.ToString(),mc_id=mcId.ToString(),name=view.Name,direction=view.DirectionType.ToString(),root=view.RootViewID.ToString(),parent=view.ParentViewID.ToString(),children=children?.Select(x=>x.ToString()).OrderBy(x=>x,StringComparer.Ordinal).ToArray()??Array.Empty<string>(),sources=sources!.Select(x=>x.ToString()).OrderBy(x=>x,StringComparer.Ordinal).ToArray(),status=view.Status.ToString(),suppressed=view.IsSuppressed,auto_update=view.EnableAutoUpdate,hidden_lines=view.ShowHiddenLines,hatches=view.ShowHatches,tangent_edges=view.ShowTangentEdges,view_plane=Plane(view.ViewPlane),projection_plane=Plane(view.ProjPlane),scale=Finite(view.DbEntity.DScale),design_scale=Finite(view.DbEntity.Scale),origin=new[]{Finite(view.ViewPlane.Origin.X),Finite(view.ViewPlane.Origin.Y),Finite(view.ViewPlane.Origin.Z)}});
                measured?.Add(views[^1]);
            }
            if(entity is Db.Solid3d solid)
            {
                var mass=solid.MassProperties;var box=solid.GeometricExtents;
                var body=mcId.IsNull?null:mcId.GetObjectOfType<Mc3dSolid>();var contents=body?.GetPartContents(false);
                Need(body==null || body.ID==mcId && contents!=null,"MODEL_STATE_UNAVAILABLE","Body part inventory unavailable.");
                bodies.Add(new{handle=id.Handle.ToString(),projection_id=mcId.IsNull?null:mcId.ToString(),mc_id=body==null?null:mcId.ToString(),contents=contents?.Select(x=>x.ToString()).OrderBy(x=>x,StringComparer.Ordinal).ToArray()??Array.Empty<string>(),geometry=new{volume=Finite(mass.Volume),centroid=new[]{Finite(mass.Centroid.X),Finite(mass.Centroid.Y),Finite(mass.Centroid.Z)},min=new[]{Finite(box.MinPoint.X),Finite(box.MinPoint.Y),Finite(box.MinPoint.Z)},max=new[]{Finite(box.MaxPoint.X),Finite(box.MaxPoint.Y),Finite(box.MaxPoint.Z)}}});
                measured?.Add(bodies[^1]);
            }
        }
        // Existing callers retain their Model-only admission. The lifecycle
        // observer additionally snapshots retained Paper views and sections.
        foreach(var blockId in blockIds)
        {
            var block=(Db.BlockTableRecord)tx.GetObject(blockId,Db.OpenMode.ForRead);
            if(!block.IsLayout || blockId==model.ObjectId)continue;
            var paperIds=Inventory(block.Cast<Db.ObjectId>(),true);
            
            foreach(var id in paperIds)
            {
                cancel();var mid=McObjectId.FromHandle(id.Handle.Value,mc!.ID);
                var paperView=mid.IsNull?null:mid.GetObjectOfType<DrawingView>();
                if(!allowPaperViews)Need(paperView==null,"OUT_OF_SCOPE","Existing paper-space drawing views are unsupported.");
                else if(paperView!=null)
                {
                    Need(paperView.ID==mid,"MODEL_STATE_UNAVAILABLE","Paper view native identity is inconsistent.");
                    var representation=(Db.Entity)tx.GetObject(id,Db.OpenMode.ForRead);
                    Need(representation.OwnerId==blockId,"MODEL_STATE_UNAVAILABLE","Paper view has inconsistent layout ownership.");
                    JsonElement? sectionData=null;
                    if(!paperView.SectionID.IsNull)
                    {
                        var section=paperView.SectionID.GetObjectOfType<Section>();
                        Need(section!=null&&section.ID==paperView.SectionID,"MODEL_STATE_UNAVAILABLE","Retained Paper view section identity is unavailable.");
                        sectionData=SectionViewState.Read(section!,cancel);
                    }
                    var retained=new{handle=id.Handle.ToString(),owner=blockId.Handle.ToString(),layout=block.LayoutId.Handle.ToString(),
                        properties=EntityAdapter.StoredProperties(representation),metadata=CadDrawingViewEditState.Metadata(paperView),section=sectionData};
                    measured!.Add(retained);paperViews.Add(retained);
                }
            }
        }
        var layerTable=(Db.LayerTable)tx.GetObject(doc.Database.LayerTableId,Db.OpenMode.ForRead);
        var layerIds=Inventory(layerTable.Cast<Db.ObjectId>());
        Need(layerTable.Has("VIEWS"),"MODEL_STATE_UNAVAILABLE","VIEWS layer is required.");
        var layers=layerIds.Select(id=>
        {
            cancel();var layer=(Db.LayerTableRecord)tx.GetObject(id,Db.OpenMode.ForRead);
            var record=new{handle=id.Handle.ToString(),name=layer.Name,color=layer.Color.ColorIndex,color_method=layer.Color.ColorMethod.ToString(),linetype=layer.LinetypeObjectId.Handle.ToString(),lineweight=(int)layer.LineWeight,off=layer.IsOff,frozen=layer.IsFrozen,locked=layer.IsLocked,plottable=layer.IsPlottable};
            measured?.Add(record);return record;
        }).OrderBy(x=>x.handle,StringComparer.Ordinal).ToArray();
        if(!allowPaperViews)return Wire.Element(new{current_layer=doc.Database.Clayer.Handle.ToString(),view_layer=layerTable["VIEWS"].Handle.ToString(),layers,entities,blocks=blockIds.Select(x=>x.Handle.ToString()).OrderBy(x=>x,StringComparer.Ordinal).ToArray(),bodies,views,sections=nativeSections});
        var extended=Wire.Element(new{current_layer=doc.Database.Clayer.Handle.ToString(),view_layer=layerTable["VIEWS"].Handle.ToString(),layers,entities,
            blocks=blockIds.Select(x=>x.Handle.ToString()).OrderBy(x=>x,StringComparer.Ordinal).ToArray(),bodies,views,sections=nativeSections,paper_views=paperViews});
        CadDrawingViewLifecycleState345.Bytes(extended,cancel);return extended;
    }
    private static bool Same(JsonElement a,JsonElement b)
    {
        if(a.ValueKind!=b.ValueKind)return false;
        return a.ValueKind switch
        {
            JsonValueKind.Object=>a.EnumerateObject().Count()==b.EnumerateObject().Count() && a.EnumerateObject().All(p=>b.TryGetProperty(p.Name,out var v)&&Same(p.Value,v)),
            JsonValueKind.Array=>a.GetArrayLength()==b.GetArrayLength() && a.EnumerateArray().Zip(b.EnumerateArray()).All(x=>Same(x.First,x.Second)),
            JsonValueKind.Number=>Math.Abs(a.GetDouble()-b.GetDouble())<=1e-8*Math.Max(1,Math.Max(Math.Abs(a.GetDouble()),Math.Abs(b.GetDouble()))),
            _=>a.ToString()==b.ToString()
        };
    }
    public static Result Execute(StructureWriteArguments args,DocumentState state,WriteJob job,ReadContext context,JsonLog log)
    {
        if(args.Operations[0] is CreateDrawingViewsEdit{LayoutHandle:not null})return PaperDrawingViewWriter356.Execute(args,state,job,context,log);
        McUndoPoint? tracker=null;bool started=false,commitAttempted=false;
        var createdViews=new List<DrawingView>();
        using var changes=new NativeOperationChanges(state.Document.Database);
        void Trace(string stage)=>log.Write(job.Operation,job.OriginalRequestId,"view_write_checkpoint",transaction:stage,operationId:job.OperationId);
        void Close(){var current=tracker;tracker=null;if(current==null)return;try{current.Stop();}finally{current.Dispose();}}
        try
        {
            var spec=(CreateDrawingViewsEdit)args.Operations[0];List<McObjectId> sources;
            using(var tx=state.Document.Database.TransactionManager.StartTransaction())sources=ResolveSources(spec,state,tx,job.CheckCancellation);
            var expectedSources=sources.ToHashSet();job.CheckCancellation();
#if DRAWING_VIEW_WRITE_PROBE
            DrawingViewWriteProbe.RecordProductSnapshot(args,state,"before",Capture(state,job.CheckCancellation));
#endif
            tracker=new McUndoPoint(false);tracker.Start();started=true;Trace("tracking_started");
            var front=new DrawingView();front.DirectionType=DrawingView.ViewDirectionType.kViewFront;front.SetIsSection(false);
            front.Set3dEntsIds(sources,true);front.ProjPlane=ProjectionPlane(spec);
            Plane3d Place(Point2 p)=>new(new Point3d(p.X,p.Y,0),Vector3d.XAxis,Vector3d.YAxis);
            front.ViewPlane=Place(spec.FrontOrigin);front.DbEntity.AddToCurrentDocument();front.Name=spec.NamePrefix+" Front";
            front.DbEntity.DScale=spec.Scale;front.DbEntity.Scale=1;job.CheckCancellation();
            createdViews.Add(front);
            if(spec.TopOrigin is {} topOrigin)
            {
                var top=front.CreateChildView(DrawingView.ViewDirectionType.kViewTop,Place(topOrigin));
                top.DbEntity.AddToCurrentDocument();top.Name=spec.NamePrefix+" Top";top.DbEntity.DScale=spec.Scale;top.DbEntity.Scale=1;
                createdViews.Add(top);
            }
            foreach(var projected in spec.ProjectedViews)
            {
                job.CheckCancellation();var info=Projection(projected.Direction);
                var view=front.CreateChildView(info.Direction,Place(projected.Origin));
                view.DbEntity.AddToCurrentDocument();view.Name=spec.NamePrefix+" "+info.Name;view.DbEntity.DScale=spec.Scale;view.DbEntity.Scale=1;
                createdViews.Add(view);
            }
            Trace("update_before");McObjectManager.UpdateAll();Trace("update_returned");job.CheckCancellation();
#if DRAWING_VIEW_WRITE_PROBE
            DrawingViewWriteProbe.RecordProductSnapshot(args,state,"changed",Capture(state,job.CheckCancellation));
            DrawingViewWriteProbe.CheckProductFailure(args,state);
#endif
            void VerifyCreated()
            {
                foreach(var view in createdViews)
                {
                    job.CheckCancellation();var actual=view.GetTop3dEntitiesInView();
                    Need(view.Status.ToString()=="Ok"&&!view.IsSuppressed&&view.DbEntity.DScale==spec.Scale&&view.DbEntity.Scale==1,
                        "MODEL_UPDATE_FAILED","Created view status or scale differs from the request.");
                    Need(actual!=null&&expectedSources.SetEquals(actual),"MODEL_UPDATE_FAILED","A created view lost or added source bodies.");
                    var geometry=view.GetViewGeometry(true,0);
                    try{Need(geometry!=null&&geometry.Count>0,"MODEL_UPDATE_FAILED","A created projection has no geometry.");}
                    finally{if(geometry!=null)foreach(var item in geometry.Distinct(ReferenceEqualityComparer.Instance))if(item is IDisposable d)d.Dispose();}
                }
            }
            VerifyCreated();
            job.CheckCancellation();job.BeginCommit();commitAttempted=true;Trace("tracking_close_before");Close();
            VerifyCreated();
            var identities=new Dictionary<McObjectId,string>();
            using(var tx=state.Document.Database.TransactionManager.StartTransaction())
            {
                var table=(Db.BlockTable)tx.GetObject(state.Document.Database.BlockTableId,Db.OpenMode.ForRead);
                var model=table[Db.BlockTableRecord.ModelSpace];var wanted=createdViews.Select(v=>v.ID).ToHashSet();
                foreach(var id in changes.Appended)
                {
                    var native=McObjectId.FromHandle(id.Handle.Value,McDocument.ActiveDocument.ID);
                    if(!wanted.Contains(native))continue;
                    Need(tx.GetObject(id,Db.OpenMode.ForRead) is Db.Entity e&&e.OwnerId==model,"OBJECT_IDENTITY_MISMATCH","Created view is outside Model Space.");
                    Need(identities.TryAdd(native,id.Handle.ToString()),"OBJECT_IDENTITY_MISMATCH","Created view has ambiguous native representations.");
                }
                Need(identities.Count==wanted.Count,"OBJECT_IDENTITY_MISMATCH","Cannot resolve each created view representation.");
            }
            var edits=createdViews.Select(v=>new StructureChange(0,"create_drawing_views",new(args.BridgeId,args.DocumentId,identities[v.ID]),"drawing_view",v.Name,true)).ToArray();
            var result=Result.Ok(job.OriginalRequestId,new ModifiedStructure(args.OperationId,"committed",false,edits,"not_guaranteed"),context,WriteRecovery.Warning);
            job.RecordCommit(result);Trace("commit_recorded");return result;
        }
        catch(System.Exception error)
        {
            if(job.CommittedResult is {} committed)return committed;
            bool rolledBack=false;System.Exception? cleanup=null;
            try
            {
                if(started && !commitAttempted && tracker!=null)
                {
                    Trace("rollback_before");tracker.Undo(true);Close();McObjectManager.UpdateAll();
                    // Absence of new objects alone does not prove full rollback of the native graph.
                    // Report unknown rather than rescan and serialize every body in the document.
                    rolledBack=false;Trace("rollback_unverified");
                }
                else Close();
            }
            catch(System.Exception e){cleanup=e;}
            bool unknown=commitAttempted || cleanup!=null || started&&!rolledBack;
            var code=error is DomainException domain?domain.Error.code:error is OperationCanceledException?"CANCELLED":"MODEL_UPDATE_FAILED";
            return job.Error(unknown?"WRITE_OUTCOME_UNKNOWN":code,unknown?"Drawing-view outcome could not be verified; inspect the affected body and view history. Replay only the identical request.":"Drawing-view creation failed; inspect the structured outcome.",unknown?"unknown":rolledBack?"rolled_back":"not_started",context,new{error_type=error.GetType().Name,cause=code,cleanup_error=cleanup?.GetType().Name});
        }
    }
}
