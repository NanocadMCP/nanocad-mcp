using System.Security.Cryptography;
using System.Text.Json;
using NanoCAD.Contracts;
using NanoCAD.Ipc;
using Teigha.DatabaseServices;
using Teigha.Geometry;

namespace NanoCADBridge;

// The same native owning factory/network as431. No Commit stub, entity-copy
// repetition, cached association labels or detached path geometry is used.
internal static class CadAssociativeArray441
{
    const string Expressions="unavailable: installed managed expression/evaluator getter arguments are passed by value; requested numeric setters replace only their own fields with literals";
    const string Warning="NATIVE_ASSOCIATIVE_ARRAY: Native polar/path action network acquires source geometry. Use actual returned root/source IDs. For path arrays inspect path_binding_observation: a null path_handle means the native source identity is unavailable, including after DWG reopen; readable item geometry does not prove that identity. Sparse numeric changes replace requested fields with literals; unrequested native parameters remain. Undo/rollback is not guaranteed; inspect unknown without replay.";
    sealed record Plan(EditSpec Edit,ObjectId Space,ObjectId[] Sources,Curve? Path,JsonElement? PathGeometry,Entity? Root,object? Before);
    static void Need([System.Diagnostics.CodeAnalysis.DoesNotReturnIf(false)]bool ok,string message,string code="OUT_OF_SCOPE")
    {if(!ok)throw new DomainException(code,message);}
    static bool Live(ObjectId id)=>!id.IsNull&&id.IsValid&&!id.IsErased&&!id.IsEffectivelyErased;
    static string Id(ObjectId id)=>Validation.NormalizeHandle(id.Handle.ToString());
    static double[] Point(Point3d value)=>[value.X,value.Y,value.Z];
    static double[] Vector(Vector3d value)=>[value.X,value.Y,value.Z];
    static bool Near(double a,double b)=>Math.Abs(a-b)<=1e-8*Math.Max(1,Math.Max(Math.Abs(a),Math.Abs(b)));
    static double Angle(double value)=>((value%360)+360)%360;
    static bool SameAngle(double a,double b)=>Near(Angle(a),Angle(b))||Near(Math.Abs(Angle(a)-Angle(b)),360);
    static bool Creates(EditSpec edit)=>edit is CreateAssociativePolarArray441 or CreateAssociativePathArray441;
    static string Token(object observed,Entity root)
    {
        var parameters=JsonSerializer.SerializeToNode(observed,Wire.Json)!.AsObject();
        // These are current optional observations, not editable parameters or
        // binding identities; evaluation/status changes cannot stale this token.
        parameters.Remove("evaluation_observation");parameters.Remove("item_geometry_observation");
        return Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(new{parameters,root=EntityAdapter.StoredProperties(root)},Wire.Json)));
    }
    static void CheckGeometry441(Entity entity,Action cancel)
    {
        if(entity.GetType()!=typeof(Spline)){CadModifier.CheckGeometry(entity,cancel);return;}
        // Array acquisition retains the native curve, rather than replacing its
        // controls through the narrower legacy editor. Fit/periodic definitions
        // therefore use the established complete native observation route.
        var shape=CadNativeCurveInspection342.ReadSpline((Spline)entity,cancel,NativeCurveRead342.SnapshotByteBudget);
        NativeCurveRead342.RequireFinite(shape,cancel);
        Need(shape.degree>0&&shape.control_points.Length>0&&shape.knots.Length>1&&shape.end_parameter>shape.start_parameter&&
            shape.knots.Zip(shape.knots.Skip(1),(a,b)=>a<=b).All(value=>value)&&
            (shape.rational?shape.weights.Length==shape.control_points.Length:shape.weights.Length==0||shape.weights.Length==shape.control_points.Length),
            "Native array Spline needs its complete finite NURBS definition and increasing parameter domain.","INVALID_GEOMETRY");
        void Point(double[] point)
        {
            cancel();Need(point.Length==3&&Math.Abs(point[2])<=1e-9,
                "The complete native array Spline must lie in WCS XY/Z=0; no projection is performed.","PLANAR_GEOMETRY_REQUIRED");
        }
        foreach(var point in shape.control_points)Point(point);
        Point(shape.start_point);Point(shape.end_point);
        if(shape.native_inspection.fit_data is {} fit)
        {
            foreach(var point in fit.points)Point(point);
            if(fit.start_tangent is {} start)Point(start);
            if(fit.end_tangent is {} end)Point(end);
        }
        // Complete actual XY controls establish the physical plane, including
        // straight curves and native shapes with an unavailable plane report.
        // Neither a +Z normal nor a representation-specific knot count is inferred.
    }
    static JsonElement PathGeometry441(Curve path,Action cancel)=>path.GetType()==typeof(Spline)?
        CadRetainedSolidPath392.StableShape(path,cancel):Wire.Element(CadContourBudget344.Geometry(path,cancel));
    static (string? Handle,AssociativePathBindingObservation441 Observation) ReadPathBinding441(AssocArrayPathParameters path,Entity root,Transaction tr,Action cancel)
    {
        bool? simple=null,external=null,sameSpace=null,sameDatabase=null,live=null;string? leafHandle=null,handle=null,reason=null;
        try
        {
            cancel();using var edge=path.Path;using var identity=edge.Entity;
            var id=identity.LeafId;live=Live(id);leafHandle=id.IsNull||!id.IsValid?null:Id(id);
            simple=identity.IsSimpleObjectId;external=identity.IsExternal;
            var bound=simple==true&&external==false&&live==true?tr.GetObject(id,OpenMode.ForRead) as Entity:null;
            if(bound!=null){sameSpace=bound.OwnerId==root.OwnerId;sameDatabase=bound.Database==root.Database;}
            if(simple==true&&external==false&&live==true&&bound is Curve&&sameSpace==true&&sameDatabase==true)handle=Id(id);
            else reason=simple!=true?"native_identity_not_simple":external==true?"native_identity_external":live!=true?"native_leaf_not_live":
                bound is not Curve?"native_target_not_curve":sameDatabase!=true?"native_target_database_mismatch":"native_target_space_mismatch";
        }
        catch(Teigha.Runtime.Exception error){cancel();reason="native_identity_read_failed: "+error.ErrorStatus;}
        catch(InvalidOperationException){cancel();reason="native_identity_read_failed: invalid_native_state";}
        cancel();return(handle,new(handle==null?"unavailable":"live_identity",reason,simple,external,leafHandle,live,sameSpace,sameDatabase,
            null,"not_observed; native item geometry and a readable source-curve identity are separate observations"));
    }
    static string[] Sources(AssocArray array,Entity root,Transaction tr,Action cancel)
    {
        using var ids=array.SourceEntities;var sources=new List<string>();var budget=new CadContourBudget344(cancel);
        foreach(ObjectId id in ids)
        {cancel();Need(Live(id)&&tr.GetObject(id,OpenMode.ForRead) is Entity source&&source.Database==root.Database,"A native array source identity is unavailable.","NATIVE_ARRAY_UNAVAILABLE");string handle=Id(id);budget.Add(handle);sources.Add(handle);}
        Need(sources.Count>0&&sources.Distinct(StringComparer.Ordinal).Count()==sources.Count,"Native array source identities are empty or duplicated.","NATIVE_ARRAY_UNAVAILABLE");return sources.ToArray();
    }
    internal static object? Read(Entity entity,AssocArray array,AssocArrayParameters raw,Transaction tr,Action cancel)
    {
        if(raw is not (AssocArrayPolarParameters or AssocArrayPathParameters))return null;
        Need(entity is BlockReference,"Native associative array lacks its actual insertion placement.","NATIVE_ARRAY_UNAVAILABLE");var root=(BlockReference)entity;
        var common=(AssocArrayCommonParameters)raw;using var basePoint=common.BasePoint;
        var basis=Point(basePoint.Point);var normal=Vector(common.BaseNormal);var position=Point(root.Position);
        var scale=new[]{root.ScaleFactors.X,root.ScaleFactors.Y,root.ScaleFactors.Z};var sources=Sources(array,entity,tr,cancel);
        Need(common.RowCount>0&&common.LevelCount>0&&basis.Concat(normal).Concat(position).Concat(scale).Concat(new[]{common.RowSpacing,common.LevelSpacing,common.RowElevation,root.Rotation}).All(double.IsFinite),
            "Native array count/frame/common parameters are invalid.","NATIVE_ARRAY_UNAVAILABLE");
        object result;
        if(raw is AssocArrayPolarParameters polar)
        {
            Need(polar.Direction is AssocArrayPolarParameters.ArcDirection.Clockwise or AssocArrayPolarParameters.ArcDirection.CounterClockwise,"The native polar direction enum is unavailable.","NATIVE_ARRAY_UNAVAILABLE");
            Need(polar.ItemCount>0&&new[]{polar.Radius,polar.StartAngle,polar.AngleBetweenItems,polar.FillAngle}.All(double.IsFinite),"Native polar array parameters are invalid.","NATIVE_ARRAY_UNAVAILABLE");
            double step=polar.Direction==AssocArrayPolarParameters.ArcDirection.Clockwise?-polar.AngleBetweenItems:polar.AngleBetweenItems;
            var value=new AssociativePolarArrayObservation441("","polar",Id(entity.ObjectId),sources,polar.ItemCount,common.RowCount,common.LevelCount,polar.Radius,
                polar.StartAngle,step,polar.FillAngle,polar.RotateItems,common.RowSpacing,common.LevelSpacing,common.RowElevation,basis,normal,position,scale,root.Rotation,Expressions);
            result=value with{array_token=Token(value,entity)};
        }
        else
        {
            var path=(AssocArrayPathParameters)raw;
            Need(path.Method is AssocArrayPathParameters.MethodType.Measure or AssocArrayPathParameters.MethodType.Divide,"The native path method enum is unavailable.","NATIVE_ARRAY_UNAVAILABLE");
            var binding=ReadPathBinding441(path,entity,tr,cancel);
            Need(path.ItemCount>0&&new[]{path.ItemSpacing,path.StartOffset,path.EndOffset}.All(double.IsFinite),"Native path-array parameters are invalid.","NATIVE_ARRAY_UNAVAILABLE");
            var value=new AssociativePathArrayObservation441("","path",Id(entity.ObjectId),sources,binding.Handle,path.ItemCount,common.RowCount,common.LevelCount,path.ItemSpacing,
                path.Method.ToString(),path.AlignItems,path.StartOffset,path.EndOffset,common.RowSpacing,common.LevelSpacing,common.RowElevation,basis,normal,position,scale,root.Rotation,
                "unavailable: installed managed PathDirection exposes a setter only; no current direction flag is inferred",Expressions,binding.Observation);
            result=value with{array_token=Token(value,entity),evaluation_observation=CadArrayEvaluationObservation446.Read(entity,tr,cancel)};
        }
        CadContourBudget344.Check(result,cancel);return result;
    }
    static Plan Resolve(ModifyArguments args,DocumentState state,Transaction tr,Action cancel)
    {
        AssociativeArrayValidation431.Batch(args.Operations);WriteSpace.Check(args);
        Need(args.Operations.Count()==1&&AssociativeArrayValidation441.Handles(args.Operations.Single()),"Use one supported native polar/path array operation.");
        Need(args.TargetSpace=="model","Native2D arrays use ordinary Model space.");var db=state.Document.Database;var space=CadWriteSpace.Resolve(db,tr,args.TargetSpace);
        Entity Entity(string handle)
        {
            cancel();var id=CadStructureWriter.Find(db,handle);var entity=tr.GetObject(id,OpenMode.ForRead) as Entity;
            Need(entity!=null&&entity.OwnerId==space&&entity.Database==db,"Native array/source/path must belong to the current ordinary Model space.");
            var layer=(LayerTableRecord)tr.GetObject(entity.LayerId,OpenMode.ForRead);Need(!layer.IsLocked&&!layer.IsDependent,"Native array/source/path needs an unlocked local layer.","LAYER_UNAVAILABLE");
            Need(!entity.HasFields&&entity.Annotative!=AnnotativeStates.True,"Field-bearing or annotative array content requires its owning adapter.","UNSUPPORTED_ARRAY_SOURCE");return entity;
        }
        var edit=args.Operations.Single();
        if(Creates(edit))
        {
            var sources=new List<ObjectId>();
            foreach(string handle in edit.Handles)
            {
                var source=Entity(handle);Need(source.GetType()==typeof(Line)||source.GetType()==typeof(Circle)||source.GetType()==typeof(Arc)||source.GetType()==typeof(Polyline)||source.GetType()==typeof(Ellipse)||source.GetType()==typeof(Spline),
                    "Acquire independent ordinary2D Line/Circle/Arc/Polyline/Ellipse/Spline sources.","UNSUPPORTED_ARRAY_SOURCE");
                CheckGeometry441(source,cancel);CadIndependentCurve.Require(source,db,tr,cancel);sources.Add(source.ObjectId);
            }
            Curve? path=null;JsonElement? geometry=null;
            if(edit is CreateAssociativePathArray441 create)
            {
                var candidate=Entity(create.PathHandle);Need(candidate is Curve&&!sources.Contains(candidate.ObjectId),"Use a separate actual native curve as the retained array path.","UNSUPPORTED_ARRAY_PATH");path=(Curve)candidate;
                CheckGeometry441(path,cancel);CadIndependentCurve.Require(path,db,tr,cancel);geometry=PathGeometry441(path,cancel);
            }
            return new(edit,space,sources.ToArray(),path,geometry,null,null);
        }
        var root=Entity(edit.Handles.Single());var before=CadAssociativeArray431.Read(root,tr,cancel);
        bool matches=edit is SetAssociativePolarArray441&&before is AssociativePolarArrayObservation441||edit is SetAssociativePathArray441&&before is AssociativePathArrayObservation441;
        Need(matches,"Select the actual matching polar/path associative array root, not a rectangular array, MInsert or copies.","UNSUPPORTED_ARRAY_TYPE");
        string expected=edit is SetAssociativePolarArray441 polar?polar.ArrayToken:((SetAssociativePathArray441)edit).ArrayToken;
        string token=before is AssociativePolarArrayObservation441 p?p.array_token:((AssociativePathArrayObservation441)before!).array_token;
        Need(token==expected,"Native array state changed; refresh its actual array_token.","ARRAY_STATE_CHANGED");
        Curve? targetPath=null;JsonElement? targetGeometry=null;
        if(edit is SetAssociativePathArray441{PathHandle:{} target})
        {
            var candidate=Entity(target);Need(candidate is Curve,"Choose an actual live same-space native curve as the explicit target path.","UNSUPPORTED_ARRAY_PATH");targetPath=(Curve)candidate;
            // This target is a read-only dependency, never acquired or edited.
            // Existing reactors need not disappear to select its real identity.
            CheckGeometry441(targetPath,cancel);targetGeometry=PathGeometry441(targetPath,cancel);
        }
        return new(edit,space,[],targetPath,targetGeometry,root,before);
    }
    internal static void Preflight(ModifyArguments args,DocumentState state,Transaction tr,Action cancel)=>_=Resolve(args,state,tr,cancel);
    static Entity Create(Plan plan,Database db,Transaction tr,Action cancel,Action begin,Action<string> at)
    {
        var anchor=plan.Edit is CreateAssociativePolarArray441 p?p.BasePoint:((CreateAssociativePathArray441)plan.Edit).BasePoint;
        at("native_source_collection");using var sources=new ObjectIdCollection(plan.Sources);
        at("native_source_anchor");using var basePoint=new VertexRef(new Point3d(anchor.X,anchor.Y,0));
        at(plan.Edit is CreateAssociativePolarArray441?"native_polar_parameters_constructor":"native_path_parameters_constructor");
        using AssocArrayCommonParameters parameters=plan.Edit is CreateAssociativePolarArray441 polar?
            new AssocArrayPolarParameters(Math.Abs(polar.AngleStepDegrees),1,1,polar.Count,1,1,0):
            new AssocArrayPathParameters(((CreateAssociativePathArray441)plan.Edit).ItemSpacing,1,1,((CreateAssociativePathArray441)plan.Edit).Count,1,1,0);
        at("native_parameters_base_normal");parameters.BaseNormal=Vector3d.ZAxis;
        at("native_parameters_origin");using var origin=new VertexRef(Point3d.Origin);
        at("native_parameters_base_point");parameters.BasePoint=origin;
        if(parameters is AssocArrayPolarParameters radial)
        {
            var create=(CreateAssociativePolarArray441)plan.Edit;
            at("native_polar_radius");radial.Radius=create.Radius;
            at("native_polar_start_angle");radial.StartAngle=Angle(create.StartAngleDegrees);
            at("native_polar_direction");radial.Direction=create.AngleStepDegrees<0?AssocArrayPolarParameters.ArcDirection.Clockwise:AssocArrayPolarParameters.ArcDirection.CounterClockwise;
            at("native_polar_rotate_items");radial.RotateItems=create.RotateItems;
        }
        else
        {
            var create=(CreateAssociativePathArray441)plan.Edit;var path=(AssocArrayPathParameters)parameters;
            // The resident-entity overload retains both the real identity and
            // its curve data. The compound-ID-only overload supplies no curve.
            at("native_path_edge_reference_constructor");using var reference=new EdgeRef(plan.Path!);
            at("native_path_edge_reference_verification");using var identity=reference.Entity;using var curve=reference.Curve;
            Need(identity.IsSimpleObjectId&&!identity.IsExternal&&Live(identity.LeafId)&&identity.LeafId==plan.Path!.ObjectId&&plan.Path.Database==db&&plan.Path.OwnerId==plan.Space&&curve!=null,
                "The native path edge reference must retain the exact resident curve identity and geometry.","NATIVE_ARRAY_PATH_UNAVAILABLE");
            at("native_path_binding");path.Path=reference;
            at("native_path_method");path.Method=AssocArrayPathParameters.MethodType.Measure;
            at("native_path_item_spacing");path.ItemSpacing=create.ItemSpacing;
            at("native_path_item_count");path.ItemCount=create.Count;
            at("native_path_align_items");path.AlignItems=create.AlignItems;
            at("native_path_direction");path.PathDirection=true;
        }
        at("native_sources_write_open");foreach(var id in plan.Sources){cancel();tr.GetObject(id,OpenMode.ForWrite);}
        at("native_array_factory");begin();var array=AssocArray.CreateArray(sources,basePoint,parameters);
        Need(array!=null&&Live(array.EntityId),"Native factory did not return a live polar/path array.","NATIVE_ARRAY_CREATION_FAILED");
        at("native_array_root_acquisition");
        var root=tr.GetObject(array.EntityId,OpenMode.ForWrite) as BlockReference;Need(root!=null&&root.OwnerId==plan.Space&&root.Database==db,"Native array root has an unexpected owner/type.","NATIVE_ARRAY_CREATION_FAILED");
        // Source geometry is localized about the explicit source anchor. Polar
        // item frames are radius/start-angle offsets about this actual root;
        // path item frames follow the retained WCS path with an identity root.
        at("native_array_root_position");root.Position=plan.Edit is CreateAssociativePolarArray441 c?new Point3d(c.Center.X,c.Center.Y,0):Point3d.Origin;
        if(plan.Edit is CreateAssociativePathArray441)
        {
            // Geometry parameters prepared before the factory may be cached.
            // Bind the same observed resident path on the actual owned native
            // parameters before the ordinary evaluation and strict readback.
            at("native_owned_path_parameters_acquisition");using var owned=array.GetParameters();
            Need(owned is AssocArrayPathParameters&&owned.Owner!=null&&owned.Owner.EntityId==array.EntityId,
                "The native path parameters must belong to this newly created array action.","NATIVE_ARRAY_PATH_DEPENDENCY_UNAVAILABLE");
            at("native_owned_path_reference_constructor");using var reference=new EdgeRef(plan.Path!);
            at("native_owned_path_reference_verification");using var identity=reference.Entity;using var curve=reference.Curve;
            Need(identity.IsSimpleObjectId&&!identity.IsExternal&&Live(identity.LeafId)&&identity.LeafId==plan.Path!.ObjectId&&plan.Path.Database==db&&plan.Path.OwnerId==plan.Space&&curve!=null,
                "The owned path edge reference must retain the exact resident curve identity and geometry.","NATIVE_ARRAY_PATH_UNAVAILABLE");
            at("native_owned_path_binding");((AssocArrayPathParameters)owned).Path=reference;
        }
        return root;
    }
    static void Set(Entity root,Plan plan,Action begin,Action<string> at)
    {
        var edit=plan.Edit;
        at("native_array_root_write_open");root.UpgradeOpen();
        at("native_array_owner_acquisition");var array=AssocArray.GetAssociativeArray(root.ObjectId);
        at("native_owned_parameters_acquisition");using var parameters=array.GetParameters();
        if(edit is SetAssociativePolarArray441 polar&&parameters is AssocArrayPolarParameters radial)
        {
            if(polar.Count is {} count){at("native_owned_polar_item_count");begin();radial.ItemCount=count;}
            if(polar.AngleStepDegrees is {} angle)
            {at("native_owned_polar_angle_step");begin();radial.AngleBetweenItems=Math.Abs(angle);at("native_owned_polar_direction");radial.Direction=angle<0?AssocArrayPolarParameters.ArcDirection.Clockwise:AssocArrayPolarParameters.ArcDirection.CounterClockwise;}
            if(polar.RotateItems is {} rotate){at("native_owned_polar_rotate_items");begin();radial.RotateItems=rotate;}
        }
        else if(edit is SetAssociativePathArray441 path&&parameters is AssocArrayPathParameters guided)
        {
            at("native_owned_path_method_verification");
            Need(guided.Method==AssocArrayPathParameters.MethodType.Measure,"Sparse count/spacing edits require the actual native Measure path method; Divide has different parameter semantics.","UNSUPPORTED_ARRAY_METHOD");
            if(path.PathHandle!=null)
            {
                at("native_explicit_path_reference_constructor");using var reference=new EdgeRef(plan.Path!);
                at("native_explicit_path_reference_verification");using var identity=reference.Entity;using var curve=reference.Curve;
                Need(identity.IsSimpleObjectId&&!identity.IsExternal&&Live(identity.LeafId)&&identity.LeafId==plan.Path!.ObjectId&&plan.Path.OwnerId==plan.Space&&plan.Path.Database==root.Database&&curve!=null,
                    "Explicit path binding must retain the exact selected resident curve identity and geometry.","NATIVE_ARRAY_PATH_UNAVAILABLE");
                at("native_explicit_path_binding");begin();guided.Path=reference;
            }
            if(path.ItemSpacing is {} spacing){at("native_owned_path_item_spacing");begin();guided.ItemSpacing=spacing;}
            if(path.Count is {} count){at("native_owned_path_item_count");begin();guided.ItemCount=count;}
            if(path.AlignItems is {} align){at("native_owned_path_align_items");begin();guided.AlignItems=align;}
        }
        else throw new DomainException("ARRAY_STATE_CHANGED","Native array kind changed before its owned parameter setter.");
    }
    static bool CanRequestEvaluation441(AssocStatus status)=>status is AssocStatus.IsUpToDateAssocStatus or
        AssocStatus.ChangedDirectlyAssocStatus or AssocStatus.ChangedTransitivelyAssocStatus or AssocStatus.ChangedNoDifferenceAssocStatus;
    static string MarkExistingController441(Entity root,Transaction tr,Action cancel,Action begin,Action<string> at)
    {
        const string pending="NATIVE_ARRAY_CONTROLLER_PENDING: ";
        string Pending(string reason)=>pending+reason+"; ordinary native evaluation continues without guessing a controller. Inspect actual item geometry.";
        AssocAction? controller=null;ObjectId dependency=ObjectId.Null;AssocStatus prior=default;
        try
        {
            at("native_array_controller_lookup");
            using var ids=AssocAction.GetActionsDependentOnObject(root,false,true);
            var candidates=new HashSet<ObjectId>();
            foreach(ObjectId id in ids)
            {
                cancel();
                if(!Live(id)||id.Database!=root.Database)return Pending("selected-root write-controller identity is not live in the current database");
                candidates.Add(id);
            }
            if(candidates.Count!=1)return Pending("selected root has "+candidates.Count+" distinct native write controllers; one exact controller is required for dirty marking");
            var selected=candidates.Single();
            at("native_array_controller_read");controller=tr.GetObject(selected,OpenMode.ForRead) as AssocAction;
            if(controller==null||controller.Database!=root.Database||controller.ObjectId!=selected||controller.IsActionBodyAProxy||controller.IsActionEvaluationInProgress)
                return Pending("selected-root controller is unavailable, foreign, a proxy or already evaluating");
            prior=controller.Status;
            if(!CanRequestEvaluation441(prior))return Pending("selected-root controller status "+prior+" is preserved; no failed, erased, suppressed or unknown status is reset");
            at("native_array_controller_reverse_dependencies");using var dependencies=controller.GetDependencies(false,true);
            foreach(ObjectId id in dependencies)
            {
                cancel();
                if(!Live(id)||id.Database!=root.Database)return Pending("native reverse write-dependency inventory is not completely local and live");
                if(tr.GetObject(id,OpenMode.ForRead) is not AssocDependency value||value.Database!=root.Database||
                    value.OwningAction!=selected||!value.IsWriteDependency)
                    return Pending("native reverse write-dependency ownership or write role is inconsistent");
                if(value.DependentOnObject!=root.ObjectId)continue;
                using var identity=value.DependentOnCompoundObject;
                if(!value.IsAttachedToObject||!CanRequestEvaluation441(value.Status)||!identity.IsSimpleObjectId||identity.IsExternal||
                    !Live(identity.LeafId)||identity.LeafId!=root.ObjectId||identity.LeafId.Database!=root.Database)
                    return Pending("selected-root reverse write dependency is inactive, compound, external or does not retain the exact live root");
                dependency=id;
            }
            if(dependency.IsNull)return Pending("no actual local simple write dependency reverses to the exact selected root");
            at("native_array_controller_write_open");if(!controller.IsWriteEnabled)controller.UpgradeOpen();
            // Requested parameter setters have already run. Read the current
            // native status here, rather than restoring an earlier status.
            prior=controller.Status;
            if(!Live(controller.ObjectId)||controller.Database!=root.Database||!CanRequestEvaluation441(prior)||
                controller.IsActionBodyAProxy||controller.IsActionEvaluationInProgress)
                return Pending("controller identity or admissible status changed before dirty marking");
        }
        catch(Teigha.Runtime.Exception error){cancel();return Pending("public controller/dependency observation failed: "+error.ErrorStatus);}
        catch(InvalidOperationException){cancel();return Pending("public controller/dependency observation has invalid native state");}
        // The explicit method checks the native ErrorStatus; the Status property
        // setter does not. Once this mutation is attempted, let ordinary write
        // outcome handling preserve any error or cancellation as unknown.
        at("native_array_controller_dirty_mark");begin();controller!.SetStatus(AssocStatus.ChangedDirectlyAssocStatus,true,false);
        return "NATIVE_ARRAY_CONTROLLER_MARKED: Exact selected-root write controller "+Id(controller.ObjectId)+" verified by native dependency "+Id(dependency)+
            "; checked evaluation request sent from status "+prior+" with owning-network notification. Actual regenerated item geometry still needs inspection.";
    }
    static void Verify(Plan plan,object observed,Transaction tr,Action cancel)
    {
        if(plan.Edit is CreateAssociativePolarArray441 create&&observed is AssociativePolarArrayObservation441 polar)
        {
            Need(polar.source_handles.Length==plan.Sources.Length&&polar.count==create.Count&&Near(polar.radius,create.Radius)&&SameAngle(polar.start_angle_degrees,create.StartAngleDegrees)&&Near(polar.angle_step_degrees,create.AngleStepDegrees)&&polar.rotate_items==create.RotateItems&&polar.rows==1&&polar.levels==1&&polar.position_wcs.SequenceEqual(new[]{create.Center.X,create.Center.Y,0d}),"Native polar count/placement/rotation/angle/radius differs from explicit inputs.","NATIVE_ARRAY_MISMATCH");return;
        }
        if(plan.Edit is CreateAssociativePathArray441 along&&observed is AssociativePathArrayObservation441 path)
        {
            Need(path.source_handles.Length==plan.Sources.Length&&path.path_handle==along.PathHandle&&path.path_binding_observation.state=="live_identity"&&path.count==along.Count&&Near(path.item_spacing,along.ItemSpacing)&&path.method=="Measure"&&path.align_items==along.AlignItems&&path.rows==1&&path.levels==1,"Native current path binding/count/spacing/alignment differs from explicit inputs.","NATIVE_ARRAY_MISMATCH");
            Need(Live(plan.Path!.ObjectId)&&plan.Path.OwnerId==plan.Space&&PathGeometry441(plan.Path,cancel).GetRawText()==plan.PathGeometry!.Value.GetRawText(),"The retained native path identity/owner/geometry changed during acquisition.","NATIVE_ARRAY_MISMATCH");return;
        }
        var before=JsonSerializer.SerializeToNode(plan.Before,Wire.Json)!.AsObject();var after=JsonSerializer.SerializeToNode(observed,Wire.Json)!.AsObject();
        void Requested(string field){before.Remove(field);after.Remove(field);}Requested("array_token");
        Requested("evaluation_observation");Requested("item_geometry_observation");
        if(plan.Edit is SetAssociativePolarArray441 edit&&observed is AssociativePolarArrayObservation441 actual)
        {
            if(edit.Count is {} count){Need(actual.count==count,"Native count setter did not retain its requested value.","NATIVE_ARRAY_MISMATCH");Requested("count");Requested("fill_angle_degrees");}
            if(edit.AngleStepDegrees is {} step){Need(Near(actual.angle_step_degrees,step),"Native polar step differs from requested value.","NATIVE_ARRAY_MISMATCH");Requested("angle_step_degrees");Requested("fill_angle_degrees");}
            if(edit.RotateItems is {} rotate){Need(actual.rotate_items==rotate,"Native rotation flag differs.","NATIVE_ARRAY_MISMATCH");Requested("rotate_items");}
        }
        else if(plan.Edit is SetAssociativePathArray441 editPath&&observed is AssociativePathArrayObservation441 actualPath)
        {
            if(editPath.PathHandle is {} target)
            {
                Need(actualPath.path_handle==target&&actualPath.path_binding_observation.state=="live_identity",
                    "The native path setter did not retain the exact explicitly selected live target.","NATIVE_ARRAY_MISMATCH");
                Need(Live(plan.Path!.ObjectId)&&Id(plan.Path.ObjectId)==target&&plan.Path.OwnerId==plan.Space&&plan.Path.Database==plan.Root!.Database&&
                    PathGeometry441(plan.Path,cancel).GetRawText()==plan.PathGeometry!.Value.GetRawText(),
                    "Explicit native path assignment changed the target curve identity, owner or complete observed geometry.","NATIVE_ARRAY_MISMATCH");
                Requested("path_handle");Requested("path_binding_observation");
            }
            if(editPath.Count is {} count){Need(actualPath.count==count,"Native path count differs.","NATIVE_ARRAY_MISMATCH");Requested("count");}
            if(editPath.ItemSpacing is {} spacing){Need(Near(actualPath.item_spacing,spacing),"Native path spacing differs.","NATIVE_ARRAY_MISMATCH");Requested("item_spacing");}
            if(editPath.AlignItems is {} align){Need(actualPath.align_items==align,"Native path alignment differs.","NATIVE_ARRAY_MISMATCH");Requested("align_items");}
        }
        Need(before.ToJsonString()==after.ToJsonString(),"Native array changed unrequested parameters, source/path identity or insertion placement.","NATIVE_ARRAY_MISMATCH");
    }
    internal static Result Execute(ModifyArguments args,DocumentState state,WriteJob job,ReadContext context,JsonLog log)
    {
        Transaction? tr=null;Exception? failure=null;bool started=false;string stage="preparation";
        try
        {
            stage="native_transaction_start";var db=state.Document.Database;tr=db.TransactionManager.StartTransaction();
            stage="native_plan_resolution";var plan=Resolve(args,state,tr,job.CheckCancellation);
            void At(string value){stage=value;job.CheckCancellation();}
            void Begin(){job.CheckCancellation();started=true;}
            var root=Creates(plan.Edit)?Create(plan,db,tr,job.CheckCancellation,Begin,At):plan.Root!;if(!Creates(plan.Edit))Set(root,plan,Begin,At);
            string? controllerWarning=Creates(plan.Edit)?null:MarkExistingController441(root,tr,job.CheckCancellation,Begin,At);
            bool pathLifecycle446=plan.Edit is SetAssociativePathArray441;
            string[] ancestorWarnings446=pathLifecycle446?CadArrayAncestorRequest446.Apply(root,tr,job.CheckCancellation,Begin,At):[];
            ObjectId originalRoot446=root.ObjectId;
            if(pathLifecycle446)
            {
                // Set() has disposed the owned parameter wrapper. Finish and
                // dispose the native controller/root transaction as well before
                // asking the public manager to evaluate pending notifications.
                stage="native_array_parameter_transaction_close446";job.CheckCancellation();
                tr.Commit();tr.Dispose();tr=null;
            }
            stage="native_array_evaluation";job.CheckCancellation();bool evaluated=AssocManager.EvaluateTopLevelNetwork(db,null,0);
            if(pathLifecycle446)
            {
                stage="native_array_fresh_read_transaction446";tr=db.TransactionManager.StartTransaction();
                Need(Live(originalRoot446)&&tr.GetObject(originalRoot446,OpenMode.ForRead) is BlockReference,
                    "The evaluated native array root is unavailable.","NATIVE_ARRAY_MISMATCH");
                root=(BlockReference)tr.GetObject(originalRoot446,OpenMode.ForRead);
                Need(root.Database==db&&root.OwnerId==plan.Space,"Native evaluation changed the selected array owner.","NATIVE_ARRAY_MISMATCH");
                Curve? currentPath446=null;
                if(plan.Edit is SetAssociativePathArray441{PathHandle:{} target446})
                    currentPath446=tr.GetObject(CadStructureWriter.Find(db,target446),OpenMode.ForRead) as Curve;
                plan=plan with{Root=root,Path=currentPath446};
            }
            tr=tr??throw new DomainException("NATIVE_ARRAY_UNAVAILABLE","Native array readback requires the reopened transaction.");
            stage="native_array_readback";var actual=CadAssociativeArray431.Read(root,tr,job.CheckCancellation);Need(actual!=null,"Native array disappeared after evaluation.","NATIVE_ARRAY_MISMATCH");
            stage="native_array_verification";Verify(plan,actual,tr,job.CheckCancellation);
            string? itemWarning446=null;
            if(plan.Edit is SetAssociativePathArray441&&actual is AssociativePathArrayObservation441 actualPath446)
            {
                stage="native_array_item_observation446";
                try
                {
                    var items446=CadArrayItemGeometry446.Read(AssocArray.GetAssociativeArray(root.ObjectId),(BlockReference)root,tr,job.CheckCancellation);
                    actual=actualPath446 with{item_geometry_observation=items446};
                    var expected446=(System.Numerics.BigInteger)actualPath446.count*actualPath446.rows*actualPath446.levels;
                    if(expected446!=items446.native_item_count)
                        itemWarning446="NATIVE_ARRAY_ITEM_REGENERATION_UNCONFIRMED: Actual native item storage contains "+
                            items446.native_item_count+" locators while stored count/rows/levels request "+expected446+
                            ". This is a current observation, not a rollback or successful rendered-regeneration claim. Inspect the actual image.";
                }
                catch(Teigha.Runtime.Exception error)
                {job.CheckCancellation();itemWarning446="NATIVE_ARRAY_ITEM_OBSERVATION_UNAVAILABLE: Current native item storage could not be observed: "+error.ErrorStatus+"; no zero/empty result is inferred. Inspect the actual image.";}
                catch(InvalidOperationException)
                {job.CheckCancellation();itemWarning446="NATIVE_ARRAY_ITEM_OBSERVATION_UNAVAILABLE: Current native item storage has an unavailable wrapper state; no zero/empty result is inferred. Inspect the actual image.";}
            }
            string[] nativeSources=actual is AssociativePolarArrayObservation441 p?p.source_handles:((AssociativePathArrayObservation441)actual).source_handles;
            EntityRef Ref(string handle)=>new(args.BridgeId,args.DocumentId,handle);var arrayRef=Ref(Id(root.ObjectId));var selected=plan.Edit.Handles.Select(Ref).ToArray();
            var erased=plan.Sources.Where(id=>id.IsErased||id.IsEffectivelyErased).Select(id=>Ref(Id(id))).ToArray();
            var data=new AssociativeArrayWriteResult431(args.OperationId,"committed",false,Creates(plan.Edit)?selected.Where(value=>!erased.Any(gone=>gone.handle==value.handle)).ToArray():[arrayRef],erased,Creates(plan.Edit)?[arrayRef]:[],[],"not_guaranteed",
                [new(plan.Edit.Operation,arrayRef,Creates(plan.Edit)?selected:[],nativeSources.Select(Ref).ToArray(),true,actual)]);
            var warnings=new List<string>{WriteRecovery.Warning,Warning};if(controllerWarning!=null)warnings.Add(controllerWarning);
            warnings.AddRange(ancestorWarnings446);
            if(itemWarning446!=null)warnings.Add(itemWarning446);
            warnings.Add(evaluated?"NATIVE_ARRAY_EVALUATED: Public native network evaluation reports that some evaluation occurred; this does not certify the requested regenerated item geometry.":
                "NATIVE_ARRAY_EVALUATION_PENDING: Public native network evaluation reports that no evaluation occurred; this is not a native error status. Actual parameters are read, but requested item regeneration remains unproved.");
            var result=Result.Ok(job.OriginalRequestId,data,context,warnings.ToArray());
            NativeCurveRead342.CheckSerializedBudget(result,Wire.PayloadBudget-4096,job.CheckCancellation);stage="native_commit";job.BeginCommit();tr.Commit();job.RecordCommit(result);
        }
        catch(Exception error){failure=error;}
        Exception? cleanup=null;try{tr?.Dispose();}catch(Exception error){cleanup=error;}
        if(job.CommittedResult is {} committed){if(cleanup!=null)committed=committed with{warnings=committed.warnings.Append("POST_COMMIT_CLEANUP_FAILED: Native array commit recorded; inspect current ownership/rendering.").ToArray()};job.RecordCommit(committed);return committed;}
        string code=failure is DomainException domain?domain.Error.code:failure is OperationCanceledException?"CANCELLED":"NATIVE_ARRAY_FAILED";bool unknown=started||cleanup!=null;
        return job.Error(unknown?"WRITE_OUTCOME_UNKNOWN":code,unknown?"Native polar/path array mutation started or cleanup failed. Inspect exact root/source/path ownership; do not replay with a new operation_id.":failure?.Message??"Native array did not start.",unknown?"unknown":"not_started",context,
            new{cause=code,stage,exception_type=failure?.GetType().FullName,native_error_status=failure is Teigha.Runtime.Exception native?native.ErrorStatus.ToString():null,
                native_details=failure is DomainException facts&&facts.Error.code=="NATIVE_ARRAY_PATH_UNAVAILABLE"?facts.Error.details:null,cleanup_error=cleanup?.GetType().Name}) with{warnings=[WriteRecovery.Warning,Warning]};
    }
}
