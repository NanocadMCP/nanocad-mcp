using System.Security.Cryptography;
using System.Text.Json;
using NanoCAD.Contracts;
using NanoCAD.Ipc;
using Teigha.DatabaseServices;
using Teigha.Geometry;

namespace NanoCADBridge;

// Public AssocArray factory and owned parameter setters, followed by public
// network evaluation. AssocArrayParameters.Commit is an installed SDK stub and
// is deliberately not part of this native route.
internal static class CadAssociativeArray431
{
    internal const string Warning="NATIVE_ASSOCIATIVE_ARRAY: This is a retained native rectangular action network. Creation acquires the selected source group; sources may move into native ownership and must subsequently be addressed through the returned actual source IDs. Numeric edits replace the requested fields' previous expressions with literals; unrequested fields and item overrides remain native-owned. Driver-expression text is unavailable through the installed managed getters. Undo/rollback is not guaranteed; inspect unknown rather than repeating it.";
    sealed record Selection(EditSpec Edit,ObjectId Space,ObjectId[] Sources,Entity? Root,AssociativeRectangularArrayObservation431? Before);
    static void Need(bool value,string message,string code="OUT_OF_SCOPE"){if(!value)throw new DomainException(code,message);}
    static bool Live(ObjectId id)=>!id.IsNull&&id.IsValid&&!id.IsErased&&!id.IsEffectivelyErased;
    static string Id(ObjectId id)=>Validation.NormalizeHandle(id.Handle.ToString());
    static double[] Vector(Vector3d value)=>[value.X,value.Y,value.Z];
    internal static bool Handles(ModifyArguments args)=>args.Operations.Any(edit=>edit is CreateAssociativeRectangularArray431 or SetAssociativeRectangularArray431||AssociativeArrayValidation441.Handles(edit));
    internal static object? Read(Entity entity,Transaction tr,Action cancel)
    {
        cancel();if(!Live(entity.ObjectId)||!AssocArray.IsAssociativeArray(entity.ObjectId))return null;
        var array=AssocArray.GetAssociativeArray(entity.ObjectId);
        Need(array!=null&&array.EntityId==entity.ObjectId,"Native array action cannot be bound to this root.","NATIVE_ARRAY_UNAVAILABLE");
        using var parameters=array!.GetParameters();
        if(parameters is not AssocArrayRectangularParameters rectangle)return CadAssociativeArray441.Read(entity,array,parameters,tr,cancel);
        Need(entity is BlockReference,"Native array insertion placement is unavailable.","NATIVE_ARRAY_UNAVAILABLE");var insertion=(BlockReference)entity;
        using var sources=array.SourceEntities;var handles=new List<string>();var budget=new CadContourBudget344(cancel);
        foreach(ObjectId source in sources)
        {
            cancel();Need(Live(source)&&tr.GetObject(source,OpenMode.ForRead) is Entity value&&value.Database==entity.Database,
                "Native array contains an unavailable source entity.","NATIVE_ARRAY_UNAVAILABLE");
            var handle=Id(source);budget.Add(handle);handles.Add(handle);
        }
        Need(handles.Count>0&&handles.Distinct(StringComparer.Ordinal).Count()==handles.Count,"Native array has empty or duplicate source identities.","NATIVE_ARRAY_UNAVAILABLE");
        var observedY=Vector(rectangle.YAxisDirection);
        bool availableY=observedY.All(double.IsFinite)&&observedY.Any(value=>value!=0);
        var result=new AssociativeRectangularArrayObservation431("", "rectangular",Id(entity.ObjectId),handles.ToArray(),rectangle.ColumnCount,rectangle.RowCount,rectangle.LevelCount,
            rectangle.ColumnSpacing,rectangle.RowSpacing,rectangle.LevelSpacing,rectangle.RowElevation,rectangle.AxesAngle,
            Vector(rectangle.XAxisDirection),availableY?observedY:null,Vector(rectangle.BaseNormal),
            [insertion.Position.X,insertion.Position.Y,insertion.Position.Z],[insertion.ScaleFactors.X,insertion.ScaleFactors.Y,insertion.ScaleFactors.Z],insertion.Rotation,
            "unavailable: installed managed Get* expression/evaluator arguments are passed by value; do not infer expressions from numeric values",
            "degrees",availableY?null:"Installed managed YAxisDirection getter returned a zero/nonfinite optional-out vector; this is not an observed axis. Use actual XAxisDirection, BaseNormal and AxesAngle in degrees together with the rendered array.");
        Need(result.columns>0&&result.rows>0&&result.levels>0&&new[]{result.column_spacing,result.row_spacing,result.level_spacing,result.row_elevation,result.axes_angle}
            .Concat(result.x_axis_direction).Concat(result.y_axis_direction??[]).Concat(result.base_normal).Concat(result.position_wcs).Concat(result.scale_factors).Append(result.rotation).All(double.IsFinite),"Native array parameters are nonfinite or counts are invalid.","NATIVE_ARRAY_UNAVAILABLE");
        var tokenBytes=JsonSerializer.SerializeToUtf8Bytes(new{parameters=result,root=EntityAdapter.StoredProperties(entity)},Wire.Json);
        CadContourBudget344.Check(result,cancel);return result with{array_token=Convert.ToHexString(SHA256.HashData(tokenBytes))};
    }
    static Selection Resolve(ModifyArguments args,DocumentState state,Transaction tr,Action cancel)
    {
        AssociativeArrayValidation431.Batch(args.Operations);WriteSpace.Check(args);
        var db=state.Document.Database;var space=CadWriteSpace.Resolve(db,tr,args.TargetSpace);
        Need(args.TargetSpace=="model","The initial native associative-array route uses ordinary Model space.");
        Entity Entity(string handle)
        {
            cancel();var id=CadStructureWriter.Find(db,handle);var entity=tr.GetObject(id,OpenMode.ForRead) as Entity;
            Need(entity!=null&&entity.OwnerId==space&&entity.Database==db,"Array targets/sources must belong to the current selected ordinary space.");
            var layer=(LayerTableRecord)tr.GetObject(entity!.LayerId,OpenMode.ForRead);
            Need(!layer.IsLocked&&!layer.IsDependent,"Array source/root must have a local unlocked layer.","LAYER_UNAVAILABLE");
            Need(!entity.HasFields&&entity.Annotative!=AnnotativeStates.True,"Field-bearing/annotative content needs its owning adapter.","UNSUPPORTED_ARRAY_SOURCE");
            return entity;
        }
        var edit=args.Operations.Single();
        if(edit is CreateAssociativeRectangularArray431 create)
        {
            var sources=new List<ObjectId>();
            foreach(string handle in create.Handles)
            {
                var source=Entity(handle);
                Need(source.GetType()==typeof(Line)||source.GetType()==typeof(Circle)||source.GetType()==typeof(Arc)||source.GetType()==typeof(Polyline)||source.GetType()==typeof(Ellipse)||source.GetType()==typeof(Spline),
                    "Create the native rectangular array from independent ordinary2D Line/Circle/Arc/Polyline/Ellipse/Spline sources.","UNSUPPORTED_ARRAY_SOURCE");
                CadModifier.CheckGeometry(source,cancel);CadIndependentCurve.Require(source,db,tr,cancel);sources.Add(source.ObjectId);
            }
            return new(edit,space,sources.ToArray(),null,null);
        }
        var set=(SetAssociativeRectangularArray431)edit;var root=Entity(set.Handles.Single());var before=Read(root,tr,cancel) as AssociativeRectangularArrayObservation431;
        Need(before!=null,"Select one actual native rectangular AssocArray root, not MInsert or independent copies.","UNSUPPORTED_ARRAY_TYPE");
        Need(before!.array_token==set.ArrayToken,"Native array parameters/source ownership changed; refresh its array_token.","ARRAY_STATE_CHANGED");
        return new(edit,space,[],root,before);
    }
    internal static void Preflight(ModifyArguments args,DocumentState state,Transaction tr,Action? cancel=null)
    {if(args.Operations.Any(AssociativeArrayValidation441.Handles))CadAssociativeArray441.Preflight(args,state,tr,cancel??(()=>{}));else _=Resolve(args,state,tr,cancel??(()=>{}));}
    static Entity Create(Selection plan,CreateAssociativeRectangularArray431 edit,Database db,Transaction tr,Action cancel,Action begin)
    {
        using var sources=new ObjectIdCollection(plan.Sources);
        using var basePoint=new VertexRef(new Point3d(edit.BasePoint.X,edit.BasePoint.Y,0));
        // Installed YAxisDirection setter converts angleTo radians to degrees;
        // the rectangular constructor/AxesAngle setter themselves forward degrees.
        using var parameters=new AssocArrayRectangularParameters(edit.ColumnSpacing,edit.RowSpacing,1,edit.Columns,edit.Rows,1,0,90);
        parameters.BaseNormal=Vector3d.ZAxis;parameters.XAxisDirection=Vector3d.XAxis;parameters.YAxisDirection=Vector3d.YAxis;
        foreach(var id in plan.Sources){cancel();tr.GetObject(id,OpenMode.ForWrite);}
        cancel();begin();var array=AssocArray.CreateArray(sources,basePoint,parameters);
        Need(array!=null&&Live(array.EntityId),"Native factory did not return a live associative-array root.","NATIVE_ARRAY_CREATION_FAILED");
        var root=tr.GetObject(array!.EntityId,OpenMode.ForWrite) as Entity;
        Need(root!=null&&root.OwnerId==plan.Space&&root.Database==db,"Native array root has an unexpected owner.","NATIVE_ARRAY_CREATION_FAILED");
        Need(root is BlockReference,"Native array factory did not return an insertion root.","NATIVE_ARRAY_CREATION_FAILED");
        // CreateArray localizes source geometry about basePoint and returns its
        // insertion at the origin. Preserve the requested seed placement in WCS.
        ((BlockReference)root!).Position=new Point3d(edit.BasePoint.X,edit.BasePoint.Y,0);
        return root!;
    }
    static void Set(Entity root,SetAssociativeRectangularArray431 edit,Action begin)
    {
        root.UpgradeOpen();var array=AssocArray.GetAssociativeArray(root.ObjectId);using var parameters=array.GetParameters();
        Need(parameters is AssocArrayRectangularParameters,"The current native array is no longer rectangular.","ARRAY_STATE_CHANGED");
        var rectangle=(AssocArrayRectangularParameters)parameters;
        begin();
        if(edit.Columns is {} columns)rectangle.ColumnCount=columns;
        if(edit.Rows is {} rows)rectangle.RowCount=rows;
        if(edit.ColumnSpacing is {} columnSpacing)rectangle.ColumnSpacing=columnSpacing;
        if(edit.RowSpacing is {} rowSpacing)rectangle.RowSpacing=rowSpacing;
        if(edit.AxesAngleDegrees is {} angle)rectangle.AxesAngle=angle;
        if(edit.PositionWcs is {} point)((BlockReference)root).Position=new Point3d(point.X,point.Y,point.Z);
    }
    static void Verify(Selection plan,AssociativeRectangularArrayObservation431 actual)
    {
        if(plan.Edit is CreateAssociativeRectangularArray431 create)
        {
            Need(actual.source_handles.Length==plan.Sources.Length&&actual.columns==create.Columns&&actual.rows==create.Rows&&Math.Abs(actual.column_spacing-create.ColumnSpacing)<=1e-9&&Math.Abs(actual.row_spacing-create.RowSpacing)<=1e-9&&actual.levels==1&&Math.Abs(actual.axes_angle-90)<=1e-9&&actual.position_wcs.SequenceEqual(new[]{create.BasePoint.X,create.BasePoint.Y,0d}),
                "Native array differs from the requested count, spacing, orthogonal axes or seed placement.","NATIVE_ARRAY_MISMATCH");return;
        }
        var set=(SetAssociativeRectangularArray431)plan.Edit;var before=plan.Before!;
        Need(actual.array_handle==before.array_handle&&actual.source_handles.SequenceEqual(before.source_handles)&&actual.columns==(set.Columns??before.columns)&&actual.rows==(set.Rows??before.rows)&&
            Math.Abs(actual.column_spacing-(set.ColumnSpacing??before.column_spacing))<=1e-9&&Math.Abs(actual.row_spacing-(set.RowSpacing??before.row_spacing))<=1e-9&&
            actual.levels==before.levels&&actual.level_spacing==before.level_spacing&&actual.row_elevation==before.row_elevation&&Math.Abs(actual.axes_angle-(set.AxesAngleDegrees??before.axes_angle))<=1e-9&&
            actual.x_axis_direction.SequenceEqual(before.x_axis_direction)&&(set.AxesAngleDegrees!=null||actual.y_axis_direction==null||before.y_axis_direction==null||actual.y_axis_direction.SequenceEqual(before.y_axis_direction))&&actual.base_normal.SequenceEqual(before.base_normal)&&
            actual.position_wcs.SequenceEqual(set.PositionWcs is {} point?new[]{point.X,point.Y,point.Z}:before.position_wcs)&&actual.scale_factors.SequenceEqual(before.scale_factors)&&actual.rotation==before.rotation,
            "Native array edit changed unrequested parameters or source identities.","NATIVE_ARRAY_MISMATCH");
    }
    internal static Result Execute(ModifyArguments args,DocumentState state,WriteJob job,ReadContext context,JsonLog log)
    {
        if(args.Operations.Any(AssociativeArrayValidation441.Handles))return CadAssociativeArray441.Execute(args,state,job,context,log);
        Transaction? tr=null;Exception? failure=null;bool started=false;string stage="preparation";
        try
        {
            var db=state.Document.Database;tr=db.TransactionManager.StartTransaction();var plan=Resolve(args,state,tr,job.CheckCancellation);
            void Begin(){job.CheckCancellation();stage="native_array_mutation";started=true;}
            job.CheckCancellation();var root=plan.Edit is CreateAssociativeRectangularArray431 create?Create(plan,create,db,tr,job.CheckCancellation,Begin):plan.Root!;
            if(plan.Edit is SetAssociativeRectangularArray431 set)Set(root,set,Begin);
            stage="native_array_evaluation";job.CheckCancellation();
            bool evaluated=AssocManager.EvaluateTopLevelNetwork(db,null,0);
            var actual=Read(root,tr,job.CheckCancellation) as AssociativeRectangularArrayObservation431;Need(actual!=null,"Native associative array disappeared after evaluation.","NATIVE_ARRAY_MISMATCH");Verify(plan,actual!);
            EntityRef Ref(string handle)=>new(args.BridgeId,args.DocumentId,handle);
            var arrayRef=Ref(Id(root.ObjectId));var selected=args.Operations.Single().Handles.Select(Ref).ToArray();
            var erased=plan.Sources.Where(id=>id.IsErased||id.IsEffectivelyErased).Select(id=>Ref(Id(id))).ToArray();
            var modified=plan.Edit is CreateAssociativeRectangularArray431?selected.Where(value=>!erased.Any(gone=>gone.handle==value.handle)).ToArray():[arrayRef];
            var data=new AssociativeArrayWriteResult431(args.OperationId,"committed",false,modified,erased,plan.Edit is CreateAssociativeRectangularArray431?[arrayRef]:[],[],"not_guaranteed",
                [new(plan.Edit.Operation,arrayRef,plan.Edit is CreateAssociativeRectangularArray431?selected:[],actual!.source_handles.Select(Ref).ToArray(),true,actual)]);
            var outcome=Result.Ok(job.OriginalRequestId,data,context,[WriteRecovery.Warning,Warning,
                evaluated?"NATIVE_ARRAY_EVALUATED: Public action network evaluation returned true; inspect the rendered group at the useful checkpoint.":"NATIVE_ARRAY_EVALUATION_PENDING: Public evaluation returned false; parameter readback is available but rendered item regeneration requires inspection."]);
            NativeCurveRead342.CheckSerializedBudget(outcome,Wire.PayloadBudget-4096,job.CheckCancellation);
            stage="native_commit";job.BeginCommit();tr.Commit();job.RecordCommit(outcome);
        }
        catch(Exception error){failure=error;}
        Exception? cleanup=null;try{tr?.Dispose();}catch(Exception error){cleanup=error;}
        if(job.CommittedResult is {} committed)
        {
            if(cleanup!=null)committed=committed with{warnings=committed.warnings.Append("POST_COMMIT_CLEANUP_FAILED: Native array commit recorded; refresh current ownership and rendering.").ToArray()};
            job.RecordCommit(committed);return committed;
        }
        string code=failure is DomainException domain?domain.Error.code:failure is OperationCanceledException?"CANCELLED":"NATIVE_ARRAY_FAILED";
        bool unknown=started||cleanup!=null;
        return job.Error(unknown?"WRITE_OUTCOME_UNKNOWN":code,unknown?"Native array mutation/evaluation started or cleanup failed; inspect current root/source ownership and preserve the exact request. Do not submit another operation_id.":failure?.Message??"Native array did not start.",
            unknown?"unknown":"not_started",context,new{cause=code,stage,native_error_status=failure is Teigha.Runtime.Exception native?native.ErrorStatus.ToString():null,cleanup_error=cleanup?.GetType().Name})
            with{warnings=[WriteRecovery.Warning,Warning]};
    }
}
