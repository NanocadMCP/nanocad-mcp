using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using System.Text.Json;
using NanoCAD.Contracts;
using NanoCAD.Ipc;
using Teigha.DatabaseServices;
using Teigha.Geometry;

namespace NanoCADBridge;

internal static class CadIndependentContourCorners343
{
    private sealed record SourceState(Curve Curve,JsonElement Shape,JsonElement Display,JsonElement Metadata,JsonElement Frame,Point3d Opposite);
    private sealed record PairState(SourceState[] Sources,Point3d Corner,Vector3d Normal,string[] Warnings,double[] SourceCornerGaps);
    private sealed record Attachment(Point3d Point,double Parameter,double Setback);
    private sealed record TrimPieces(Entity[] Owned,Curve Kept);
    private sealed class Budget
    {
        private long bytes=2;private readonly Action cancel;internal Budget(Action check){cancel=check;}internal long Mark=>bytes;
        internal void Add(object value){cancel();bytes+=JsonSerializer.SerializeToUtf8Bytes(value,Wire.Json).LongLength+1;Need(bytes<=8L*1024*1024,"Two-curve corner inventory exceeds actual8MiB serialized bytes.","RESPONSE_LIMIT_EXCEEDED");}
        internal JsonElement Finish(long mark,object value){bytes=mark;Add(value);return Wire.Element(value);}
    }
    private static void Need([DoesNotReturnIf(false)] bool valid,string message,string code="OUT_OF_SCOPE")
    {if(!valid)throw new DomainException(code,message);}
    private static string? Id(ObjectId value)=>value.IsNull?null:Validation.NormalizeHandle(value.Handle.ToString());
    private static double Finite(double value){Need(double.IsFinite(value),"Native corner geometry contains a nonfinite value.","INVALID_GEOMETRY");return value;}
    private static double[] Point(Point3d p)=>new[]{Finite(p.X),Finite(p.Y),Finite(p.Z)};
    private static double[] Vector(Vector3d v)=>new[]{Finite(v.X),Finite(v.Y),Finite(v.Z)};
    private static bool Near(Point3d a,Point3d b)=>a.DistanceTo(b)<=Tolerance.Global.EqualPoint;
    private static bool Exact(JsonElement a,JsonElement b)=>a.GetRawText()==b.GetRawText();
    private static Point3d Endpoint(Curve curve,string end)=>end=="start"?curve.StartPoint:curve.EndPoint;
    private static Vector3d StoredNormal(Curve curve)=>curve is Arc arc?arc.Normal:((Line)curve).Normal;
    private static JsonElement Display(Entity source)=>Wire.Element(new{layer_handle=Id(source.LayerId),properties=EntityAdapter.StoredProperties(source)});
    private static JsonElement Frame(Curve curve)=>curve is Arc arc?Wire.Element(new{runtime_type=curve.GetType().FullName,normal=Vector(arc.Normal),thickness=arc.Thickness,center=Point(arc.Center),radius=arc.Radius}):
        Wire.Element(new{runtime_type=curve.GetType().FullName,normal=Vector(((Line)curve).Normal),thickness=((Line)curve).Thickness});
    private static JsonElement Metadata(Curve curve,Transaction tr,bool mutation,Action cancel,List<string>? notices)
    {
        var budget=new Budget(cancel);using var xdata=curve.XData;using var reactors=curve.GetPersistentReactorIds();var links=new List<string?>();
        foreach(ObjectId id in reactors){cancel();budget.Add(Id(id)!);links.Add(Id(id));}
        var data=xdata==null?null:NativeOwnedMetadata.Buffer(xdata,cancel,budget.Add);JsonElement? owned=null;string? unavailable=null;
        try{owned=NativeOwnedMetadata.Capture(curve,tr,cancel);}
        catch(DomainException e)when(!mutation&&e.Error.code=="UNSUPPORTED_OWNED_METADATA")
        {unavailable=e.Error.code;notices?.Add("CORNER_RETAINED_OWNED_DATA_UNVERIFIED: Source "+Id(curve.ObjectId)+" is read-only; root identity/XData/reactors/extension link remain checked, but unclassified owned internals require their adapter for deep inspection.");}
        return budget.Finish(2,new{handle=Id(curve.ObjectId),owner_handle=Id(curve.OwnerId),field_flag=curve.HasFields,annotative=curve.Annotative.ToString(),extension_handle=Id(curve.ExtensionDictionary),xdata=data,
            reactors=links.OrderBy(v=>v,StringComparer.Ordinal).ToArray(),owned,owned_unavailable_reason=unavailable});
    }
    internal static void CheckSource(Entity entity,NativeCornerCurveSpec343 edit,Database db,Transaction tr,Action cancel)
    {
        cancel();Need(entity.GetType()==typeof(Line)||entity.GetType()==typeof(Arc),"Two-curve corners currently use exact ordinary Line/Arc sources.","UNSUPPORTED_ENTITY_TYPE");
        var curve=(Curve)entity;Need(curve.Database==db&&!curve.ObjectId.IsNull&&curve.ObjectId.IsValid&&!curve.IsErased&&!curve.ObjectId.IsEffectivelyErased,"Requires the actual live resident source.","INVALID_SOURCE_OWNERSHIP");
        Need(!curve.Closed&&(curve is Arc arc?arc.Thickness==0&&arc.Radius>0:((Line)curve).Thickness==0),"Corner sources must be finite open zero-thickness native curves.");
        var shape=CadNativeCurveInspection342.Shape(curve,cancel);int index=Array.IndexOf(edit.Handles,Id(curve.ObjectId));Need(index>=0,"Source differs from the explicit ordered pair.","INVALID_SOURCE_OWNERSHIP");
        Need(NativeCurveRead342.Token(shape,cancel)==edit.ExpectedCurveTokens[index],"Source native shape differs from its expected_curve_token; read current geometry/context.","GEOMETRY_CHANGED");
        Need(curve.EndParam>curve.StartParam&&!Near(curve.StartPoint,curve.EndPoint),"Corner source has a collapsed finite interval.","INVALID_GEOMETRY");
        _=Metadata(curve,tr,edit.TrimSources,cancel,null);
        if(edit.TrimSources)
        {
            using var links=curve.GetPersistentReactorIds();Need(!curve.HasFields&&links.Count==0&&curve.Annotative!=AnnotativeStates.True,"In-place corner trim needs independent sources; fields/reactors/annotation contexts require their owning route.","UNSUPPORTED_CURVE_DEPENDENCIES");
            var loaded=AppDomain.CurrentDomain.GetAssemblies().Select(a=>a.GetName().Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
            if(loaded.Contains("mapimgd")&&loaded.Contains("MapiBaseTypes"))CheckModels(curve,db,tr,cancel);
        }
    }
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void CheckModels(Curve source,Database db,Transaction tr,Action cancel)
    {
        var document=Multicad.DatabaseServices.McDocument.ActiveDocument;
        Need(HostMgd.ApplicationServices.Application.DocumentManager.MdiActiveDocument?.Database==db&&document!=null&&
            (string.IsNullOrEmpty(db.Filename)||string.Equals(Path.GetFullPath(db.Filename),Path.GetFullPath(document.Path),StringComparison.OrdinalIgnoreCase)),"Cannot establish the matching native source document.","DOCUMENT_CONTEXT_MISMATCH");
        var sourceId=Multicad.McObjectId.FromHandle(source.Handle.Value,document.ID);if(sourceId.IsNull)return;
        Need(sourceId.GetObjectOfType<Multicad.Mc3D.HistoryItem>()==null,"Curve belongs to native feature history; use its owning route.","UNSUPPORTED_CURVE_DEPENDENCIES");
        var model=((BlockTable)tr.GetObject(db.BlockTableId,OpenMode.ForRead))[BlockTableRecord.ModelSpace];var budget=new Budget(cancel);
        foreach(ObjectId id in (BlockTableRecord)tr.GetObject(model,OpenMode.ForRead))
        {
            cancel();if(id.IsNull||!id.IsValid||id.IsErased||id.IsEffectivelyErased)continue;budget.Add(Id(id)!);if(tr.GetObject(id,OpenMode.ForRead) is not Solid3d)continue;
            var body=Multicad.McObjectId.FromHandle(id.Handle.Value,document.ID).GetObjectOfType<Multicad.Mc3D.Mc3dSolid>();if(body==null)continue;
            var history=ModelInventoryBudget.History(body,cancel);foreach(var item in history)
            {
                cancel();budget.Add(item.ToString());Need(item!=sourceId&&!(item.GetObjectOfType<Multicad.Mc3D.ProjectionSketchEnt>() is {} p&&p.SrcObjectID==sourceId)&&
                    !(item.GetObjectOfType<Multicad.Mc3D.RevolveFeature>() is {} r&&r.Axis.ID==sourceId),"Curve is consumed by native model history; use its owning route.","UNSUPPORTED_CURVE_DEPENDENCIES");
            }
            foreach(var reference in NativePathSources.ReferencedIds(history,cancel)){budget.Add(reference);Need(reference!=sourceId.ToString(),"Curve supports a native model sketch/path; use its owning route.","UNSUPPORTED_CURVE_DEPENDENCIES");}
        }
    }
    private static SourceState Capture(Curve curve,NativeCornerCurveSpec343 edit,int index,Database db,Transaction tr,Action cancel,List<string>? notices)
    {
        CheckSource(curve,edit,db,tr,cancel);var budget=new Budget(cancel);var shape=CadNativeCurveInspection342.Shape(curve,cancel);budget.Add(shape);var display=Display(curve);budget.Add(display);
        var metadata=Metadata(curve,tr,edit.TrimSources,cancel,notices);budget.Add(metadata);var frame=Frame(curve);budget.Finish(2,new{shape,display,metadata,frame});
        return new(curve,shape,display,metadata,frame,Endpoint(curve,edit.Ends[index]=="start"?"end":"start"));
    }
    private static PairState Prepare(NativeCornerCurveSpec343 edit,Func<string,Entity> read,Database db,Transaction tr,Action cancel)
    {
        var notices=new List<string>();var states=new SourceState[2];var budget=new Budget(cancel);
        for(int i=0;i<2;i++){states[i]=Capture((Curve)read(edit.Handles[i]),edit,i,db,tr,cancel,notices);budget.Add(new{states[i].Shape,states[i].Display,states[i].Metadata,states[i].Frame});}
        var first=states[0].Curve;var second=states[1].Curve;Point3d corner;
        if(edit.CornerMode=="supporting_line_intersection")
        {
            Need(first.GetType()==typeof(Line)&&second.GetType()==typeof(Line),"Supporting-line corners require two exact ordinary Lines; separated Arc corners need their own native solution scope.","UNSUPPORTED_ENTITY_TYPE");
            corner=CadSupportingLineCorner386.Intersection((Line)first,(Line)second,edit.Ends,cancel);
            if(edit.Kind=="chamfer_curves")for(int i=0;i<2;i++)
                _=CadSupportingLineCorner386.Setback((Line)states[i].Curve,edit.Ends[i],corner,edit.Distances[i]);
        }
        else
        {
            corner=Endpoint(first,edit.Ends[0]);Need(Near(corner,Endpoint(second,edit.Ends[1])),"Selected source ends must meet at one actual corner; choose supporting_line_intersection explicitly for two separated Lines.","NO_COMMON_CORNER");
        }
        Vector3d normal;
        if(first is Arc||second is Arc)
        {
            var arc=first as Arc??(Arc)second;cancel();using var plane=arc.GetPlane();Need(plane!=null&&plane.Normal.Length>0,"Arc native plane is unavailable.","CURVE_PLANE_UNAVAILABLE");normal=plane.Normal.GetNormal();
            foreach(var curve in new[]{first,second})
            {
                Need(Math.Abs((curve.StartPoint-plane.PointOnPlane).DotProduct(normal))<=Tolerance.Global.EqualPoint&&Math.Abs((curve.EndPoint-plane.PointOnPlane).DotProduct(normal))<=Tolerance.Global.EqualPoint,"Sources lie in different native planes.","CURVE_PLANE_MISMATCH");
                if(curve is Arc other)Need(other.Normal.GetNormal().CrossProduct(normal).Length<=Tolerance.Global.EqualVector&&Math.Abs((other.Center-plane.PointOnPlane).DotProduct(normal))<=Tolerance.Global.EqualPoint,"Arc source planes differ.","CURVE_PLANE_MISMATCH");
            }
        }
        else
        {
            var a=(first.EndPoint-first.StartPoint).GetNormal();var b=(second.EndPoint-second.StartPoint).GetNormal();var cross=a.CrossProduct(b);
            Need(cross.Length>Tolerance.Global.EqualVector,"Collinear Lines do not define an unambiguous corner plane.","AMBIGUOUS_CORNER");normal=cross.GetNormal();
        }
        return new(states,corner,normal,notices.Distinct(StringComparer.Ordinal).ToArray(),
            states.Select((value,i)=>Finite(Endpoint(value.Curve,edit.Ends[i]).DistanceTo(corner))).ToArray());
    }
    internal static void Preview(ModifyArguments args,CadModifier.Resolved resolved,Transaction tr,Action cancel)
    {var edit=(NativeCornerCurveSpec343)args.Operations.Single();var source=(Entity)tr.GetObject(resolved.Sources[edit.Handles[0]],OpenMode.ForRead);_=Prepare(edit,h=>(Entity)tr.GetObject(resolved.Sources[h],OpenMode.ForRead),source.Database,tr,cancel);}
    private static bool Detached(DBObject item,HashSet<IntPtr> forbidden)=>item.UnmanagedObject!=IntPtr.Zero&&!forbidden.Contains(item.UnmanagedObject)&&item.ObjectId.IsNull&&item.OwnerId.IsNull;
    private static void Independent(Entity value,Action cancel)
    {cancel();using var links=value.GetPersistentReactorIds();using var data=value.XData;Need(!value.HasFields&&value.ExtensionDictionary.IsNull&&links.Count==0&&(data==null||data.AsArray().Length==0),"Generated geometry has an unclassified dependency/application graph; no data was stripped.","UNSUPPORTED_CORNER_RESULT");}
    private static Curve3d Geometry(Curve curve,HashSet<IntPtr> forbidden,Action cancel)
    {
        cancel();var geometry=curve.GetGeCurve();Need(geometry!=null&&geometry.UnmanagedObject!=IntPtr.Zero&&!forbidden.Contains(geometry.UnmanagedObject),"Native geometry getter returned an unavailable/source/owned alias; unsafe geometry was not adopted/disposed.","INVALID_CORNER_OWNERSHIP");return geometry;
    }
    private static Attachment Attach(Curve source,Point3d point,string end,Action cancel)
    {
        cancel();Need(Near(source.GetClosestPointTo(point,false),point),"Connector attachment lies outside its finite selected source; no extension was requested.","NATIVE_CORNER_MISMATCH");
        double parameter=Finite(source.GetParameterAtPoint(point));Need(parameter>source.StartParam&&parameter<source.EndParam&&!Near(point,source.StartPoint)&&!Near(point,source.EndPoint),"Attachment must leave a noncollapsed retained source and remove part of its selected end.","INVALID_CORNER_SETBACK");
        double start=Finite(source.GetDistanceAtParameter(source.StartParam)),finish=Finite(source.GetDistanceAtParameter(source.EndParam)),at=Finite(source.GetDistanceAtParameter(parameter));
        double setback=end=="start"?at-start:finish-at;Need(setback>0&&setback<finish-start,"Native attachment setback is not interior to the selected source.","INVALID_CORNER_SETBACK");return new(point,parameter,setback);
    }
    private static Curve Connector(PairState pair,NativeCornerCurveSpec343 edit,List<Entity> owned,Action cancel,Action<string> stage,out Attachment[] attachments)
    {
        var sources=pair.Sources.Select(s=>s.Curve).ToArray();var forbidden=new HashSet<IntPtr>(sources.Select(c=>c.UnmanagedObject).Concat(owned.Select(c=>c.UnmanagedObject)));Curve value;attachments=new Attachment[2];
        Curve Adopt(Curve candidate)
        {
            // Register safe factory ownership before any temporary geometry
            // disposal can throw. A refused native alias is never disposed.
            Need(candidate!=null&&Detached(candidate,forbidden),"Connector factory returned a resident/source/owned alias; unsafe wrapper was not adopted/disposed.","INVALID_CORNER_OWNERSHIP");
            owned.Add(candidate);return candidate;
        }
        if(edit.Kind=="fillet_curves")
        {
            if (sources[0].GetType()==typeof(Line) && sources[1].GetType()==typeof(Line))
            {
                // The tan/tan solver expects tangency parameters, not the common
                // corner. Straight finite rays have one exact interior solution;
                // construct its native circular arc without a branch search.
                stage("fillet_plan_straight_tangencies");
                var plan=IndependentFilletGeometry349.BetweenLines(Point(pair.Corner),
                    Point(pair.Sources[0].Opposite),Point(pair.Sources[1].Opposite),
                    edit.Radius!.Value,Tolerance.Global.EqualPoint,Tolerance.Global.EqualVector);
                static Point3d P(double[] v)=>new(v[0],v[1],v[2]);
                static Vector3d V(double[] v)=>new(v[0],v[1],v[2]);
                cancel();stage("fillet_allocate_exact_native_arc");
                using var fillet=new CircularArc3d(P(plan.Center),V(plan.Normal),V(plan.ReferenceDirection),
                    edit.Radius!.Value,0,plan.Sweep);
                forbidden.Add(fillet.UnmanagedObject);
                stage("fillet_create_database_curve");
                value=Adopt(Curve.CreateFromGeCurve(fillet,V(plan.Normal),Tolerance.Global));
            }
            else
            {
                stage("fillet_get_first_gecurve");using var first=Geometry(sources[0],forbidden,cancel);forbidden.Add(first.UnmanagedObject);
                stage("fillet_get_second_gecurve");using var second=Geometry(sources[1],forbidden,cancel);
                // GeCurve and database parameters have independent conventions.
                // Use finite interior points to identify the retained sides;
                // two identical common-corner guesses are not tangency seeds.
                double Seed(Curve source,Curve3d geometry,string end)
                {
                    double a=Finite(source.GetDistanceAtParameter(source.StartParam));
                    double b=Finite(source.GetDistanceAtParameter(source.EndParam));
                    double length=b-a;Need(length>Tolerance.Global.EqualPoint,"A source has no finite interior.","INVALID_GEOMETRY");
                    double step=Math.Min(edit.Radius!.Value,length/2);
                    double distance=end=="start"?a+step:b-step;
                    var hint=source.GetPointAtParameter(source.GetParameterAtDistance(distance));
                    double seed=Finite(geometry.GetParameterOf(hint));
                    Need(Near(geometry.EvaluatePoint(seed),hint),"Native tangency seed does not identify the selected finite source.","NATIVE_CORNER_MISMATCH");
                    return seed;
                }
                stage("fillet_get_interior_seed_parameters");
                double seed1=Seed(sources[0],first,edit.Ends[0]),seed2=Seed(sources[1],second,edit.Ends[1]);
                forbidden.Add(second.UnmanagedObject);cancel();stage("fillet_allocate_ge_arc");using var fillet=new CircularArc3d();forbidden.Add(fillet.UnmanagedObject);
                stage("fillet_set_ge_arc");fillet.Set(first,second,edit.Radius!.Value,seed1,seed2);cancel();
                stage("fillet_create_database_curve");value=Adopt(Curve.CreateFromGeCurve(fillet,pair.Normal,Tolerance.Global));
            }
        }
        else
        {
            for(int i=0;i<2;i++)
            {
                var source=sources[i];double sourceSetback=edit.CornerMode=="supporting_line_intersection"?
                    CadSupportingLineCorner386.Setback((Line)source,edit.Ends[i],pair.Corner,edit.Distances[i]):edit.Distances[i];
                double distance=IndependentContourCornersValidation343.TargetDistance(edit.Ends[i],Finite(source.GetDistanceAtParameter(source.StartParam)),Finite(source.GetDistanceAtParameter(source.EndParam)),sourceSetback);
                cancel();double parameter=Finite(source.GetParameterAtDistance(distance));attachments[i]=Attach(source,source.GetPointAtParameter(parameter),edit.Ends[i],cancel);
                Need(Math.Abs(attachments[i].Setback-sourceSetback)<=Tolerance.Global.EqualPoint,"Native finite-source setback differs from the requested corner dimension.","NATIVE_CORNER_MISMATCH");
                if(edit.CornerMode=="supporting_line_intersection")Need(Math.Abs(attachments[i].Point.DistanceTo(pair.Corner)-edit.Distances[i])<=Tolerance.Global.EqualPoint,
                    "Native chamfer attachment differs from its requested supporting-intersection dimension.","NATIVE_CORNER_MISMATCH");
            }
            Need(!Near(attachments[0].Point,attachments[1].Point),"Chamfer attachments collapse to one point.","INVALID_GEOMETRY");value=Adopt(new Line(attachments[0].Point,attachments[1].Point));
        }
        stage("verify_connector_native_geometry");Need(value.GetType()==(edit.Kind=="fillet_curves"?typeof(Arc):typeof(Line)),"Native corner factory returned another geometry type.","UNSUPPORTED_CORNER_RESULT");Independent(value,cancel);
        if(edit.Kind=="fillet_curves")
        {
            var arc=(Arc)value;Need(Math.Abs(arc.Radius-edit.Radius!.Value)<=Tolerance.Global.EqualPoint&&!Near(arc.StartPoint,arc.EndPoint),"Native fillet did not retain the requested radius/finite endpoints.","NATIVE_CORNER_MISMATCH");
            bool On(Curve source,Point3d p)=>Near(source.GetClosestPointTo(p,false),p);
            bool forward=On(sources[0],arc.StartPoint)&&On(sources[1],arc.EndPoint),reverse=On(sources[0],arc.EndPoint)&&On(sources[1],arc.StartPoint);
            Need(forward!=reverse,"Native fillet attachments do not identify exactly one ordered finite source pair.","AMBIGUOUS_CORNER");
            var points=forward?new[]{arc.StartPoint,arc.EndPoint}:new[]{arc.EndPoint,arc.StartPoint};attachments=new Attachment[2];
            for(int i=0;i<2;i++)
            {
                attachments[i]=Attach(sources[i],points[i],edit.Ends[i],cancel);var a=sources[i].GetFirstDerivative(attachments[i].Parameter);var b=arc.GetFirstDerivative(arc.GetParameterAtPoint(points[i]));
                Need(a.Length>0&&b.Length>0&&a.GetNormal().CrossProduct(b.GetNormal()).Length<=Tolerance.Global.EqualVector,"Native fillet is not tangent to the corresponding selected source.","NATIVE_CORNER_MISMATCH");
            }
        }
        stage("copy_connector_display");value.SetPropertiesFrom(sources[0]);Independent(value,cancel);Need(Exact(pair.Sources[0].Display,Display(value)),"Connector did not retain first-source display/layer.","NATIVE_CORNER_MISMATCH");
        Need(Math.Abs((value.StartPoint-pair.Corner).DotProduct(pair.Normal))<=Tolerance.Global.EqualPoint&&Math.Abs((value.EndPoint-pair.Corner).DotProduct(pair.Normal))<=Tolerance.Global.EqualPoint,"Native connector left the selected corner plane.","NATIVE_CORNER_MISMATCH");return value;
    }
    private static TrimPieces RetainedPiece(Curve source,Attachment attachment,Point3d opposite,HashSet<IntPtr> forbidden,Action cancel)
    {
        cancel();var collection=source.GetSplitCurves(new DoubleCollection(new[]{attachment.Parameter}))??throw new DomainException("NO_SPLIT_RESULT","Native source trim returned no split collection.");
        var owned=new List<DBObject>();var pointers=new HashSet<IntPtr>();bool releaseAttempted=false;void Release(){releaseAttempted=true;collection.Dispose();}
        try
        {
            Exception? acquisitionError=null;int count=collection.Count;
            for(int i=0;i<count;i++)try{var item=collection[i];Need(item!=null&&Detached(item,forbidden),"Source trim returned a resident/source/owned alias; unsafe wrapper was not adopted/disposed.","INVALID_CORNER_OWNERSHIP");Need(pointers.Add(item.UnmanagedObject),"Source trim returned repeated native identity.","INVALID_CORNER_OWNERSHIP");owned.Add(item);}catch(Exception e){acquisitionError??=e;}
            if(acquisitionError!=null)throw acquisitionError;
            var budget=new Budget(cancel);var matching=new List<Curve>();
            foreach(var item in owned)
            {
                cancel();budget.Add(new{pointer=item.UnmanagedObject.ToString("X"),runtime_type=item.GetType().FullName});Need(item is Curve&&item.GetType()==source.GetType(),"Source trim returned another native type; no temporary piece was silently dropped.","UNSUPPORTED_CORNER_RESULT");var curve=(Curve)item;Independent(curve,cancel);budget.Add(CadNativeCurveInspection342.Shape(curve,cancel));
                if((Near(curve.StartPoint,opposite)&&Near(curve.EndPoint,attachment.Point))||(Near(curve.EndPoint,opposite)&&Near(curve.StartPoint,attachment.Point)))matching.Add(curve);
            }
            Need(matching.Count==1,"Native retained piece is not uniquely identified by the opposite endpoint and actual attachment.","AMBIGUOUS_CORNER");var result=new TrimPieces(owned.Cast<Entity>().ToArray(),matching[0]);Release();return result;
        }
        catch(Exception primary)
        {var failures=new List<Exception>();if(!releaseAttempted)try{Release();}catch(Exception e){failures.Add(e);}foreach(var item in owned)try{item.Dispose();}catch(Exception e){failures.Add(e);}if(failures.Count>0)throw new AggregateException("Native corner split cleanup failed; recovery is unknown.",new[]{primary}.Concat(failures));throw;}
    }
    private static void VerifySource(SourceState before,Transaction tr,bool mutation,Action cancel)
    {Need(Exact(before.Display,Display(before.Curve))&&Exact(before.Metadata,Metadata(before.Curve,tr,mutation,cancel,null))&&BlockMoveGeometry.SameProperties(before.Frame,Frame(before.Curve)),"Corner changed retained source identity/frame/display/observable metadata.","NATIVE_CORNER_MISMATCH");}
    private static object FailureDetails(Exception error,string stage)
    {
        // Already-thrown managed exception only; no CAD getter or extra attempt.
        const int limit=8192;
        try
        {
            string stack=error.StackTrace??string.Empty;var frames=new List<string>();int omitted=0,bytes=1536;
            foreach(var line in stack.Split('\n'))
            {
                if(line.Length==0)continue;
                int size=JsonSerializer.SerializeToUtf8Bytes(line,Wire.Json).Length+1;
                if((long)bytes+size<=limit){frames.Add(line);bytes+=size;}else omitted++;
            }
            var details=new{error_type=error.GetType().Name,component="independent_contour_corner",last_attempted_stage=stage,
                stage_scope="Last attempted stage; native/managed cleanup may execute during exception unwinding.",
                exception_stack_sha256=Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(stack))),
                exception_stack_available=stack.Length>0,stack_frames=frames.ToArray(),omitted_stack_frame_count=omitted};
            if(JsonSerializer.SerializeToUtf8Bytes(details,Wire.Json).Length<=limit)return details;
        }
        catch(System.Exception){ }
        return new{error_type=error.GetType().Name,component="independent_contour_corner",last_attempted_stage=stage,
            diagnostic_unavailable_reason="Managed exception details were unavailable or exceeded their byte budget."};
    }
    internal static ModifiedEntities Apply(ModifyArguments args,CadModifier.Resolved resolved,Transaction tr,List<Entity> owned,WriteJob job,List<string>? warnings=null)
    {
        string stage="apply_entry";
        try{return ApplyCore(args,resolved,tr,owned,job,warnings,value=>stage=value);}
        catch(OperationCanceledException){throw;}
        catch(DomainException){throw;}
        catch(Teigha.Runtime.Exception){throw;}
        catch(System.Exception error)
        {throw new DomainException("API_EXCEPTION","Unexpected write failure; see bridge diagnostics.",false,FailureDetails(error,stage));}
    }
    private static ModifiedEntities ApplyCore(ModifyArguments args,CadModifier.Resolved resolved,Transaction tr,List<Entity> owned,WriteJob job,List<string>? warnings,Action<string> stage)
    {
        var edit=(NativeCornerCurveSpec343)args.Operations.Single();var db=((Entity)tr.GetObject(resolved.Sources[edit.Handles[0]],OpenMode.ForRead)).Database;
        stage("capture_pair");var pair=Prepare(edit,h=>(Entity)tr.GetObject(resolved.Sources[h],OpenMode.ForRead),db,tr,job.CheckCancellation);
        stage("create_connector");var connector=Connector(pair,edit,owned,job.CheckCancellation,stage,out var attachments);var budget=new Budget(job.CheckCancellation);
        foreach(var state in pair.Sources)budget.Add(new{state.Shape,state.Display,state.Metadata,state.Frame});
        stage("capture_connector_shape");var connectorShape=CadNativeCurveInspection342.Shape(connector,job.CheckCancellation);budget.Add(connectorShape);var pieces=new TrimPieces?[2];
        if(edit.TrimSources)for(int i=0;i<2;i++)
        {stage("split_source_"+i);var forbidden=new HashSet<IntPtr>(pair.Sources.Select(v=>v.Curve.UnmanagedObject).Concat(owned.Select(v=>v.UnmanagedObject)));pieces[i]=RetainedPiece(pair.Sources[i].Curve,attachments[i],pair.Sources[i].Opposite,forbidden,job.CheckCancellation);owned.AddRange(pieces[i]!.Owned);foreach(var value in pieces[i]!.Owned)budget.Add(CadNativeCurveInspection342.Shape((Curve)value,job.CheckCancellation));}
        var sourceRefs=edit.Handles.Select(h=>new EntityRef(args.BridgeId,args.DocumentId,h)).ToArray();
        ContourCornerResult343 Mapping(EntityRef root)=>new(0,edit.Kind,sourceRefs,edit.Ends,root,edit.TrimSources,attachments.Select(v=>Point(v.Point)).ToArray(),attachments.Select(v=>v.Parameter).ToArray(),attachments.Select(v=>v.Setback).ToArray(),edit.Radius,edit.CornerMode,Point(pair.Corner),
            pair.SourceCornerGaps);
        ModifiedEntities Result(EntityRef root)=>new(args.OperationId,"committed",false,edit.TrimSources?sourceRefs:[],[],[root],[],"not_guaranteed",contour_results:sourceRefs.Select(s=>new ContourMapping(0,edit.Kind,s,[root],false)).ToArray(),contour_corner_results:[Mapping(root)]);
        stage("project_complete_result");var projected=Result(sourceRefs[0] with{handle="FFFFFFFFFFFFFFFF"});job.CheckCancellation();Need(JsonSerializer.SerializeToUtf8Bytes(new{data=projected,warnings=pair.Warnings},Wire.Json).Length<=Wire.PayloadBudget-16384,"Complete corner pair/result provenance exceeds actual transport bytes; no root was appended or source changed.","RESPONSE_LIMIT_EXCEEDED");
        stage("verify_sources_before_write");foreach(var source in pair.Sources){VerifySource(source,tr,edit.TrimSources,job.CheckCancellation);Need(Exact(source.Shape,CadNativeCurveInspection342.Shape(source.Curve,job.CheckCancellation)),"Native corner planning changed a read-only source.","NATIVE_CORNER_MISMATCH");}
        if(edit.TrimSources)for(int i=0;i<2;i++)
        {
            stage("get_retained_gecurve_"+i);var before=pair.Sources[i];var retained=pieces[i]!.Kept;using var geometry=Geometry(retained,new HashSet<IntPtr>(pair.Sources.Select(v=>v.Curve.UnmanagedObject).Concat(owned.Select(v=>v.UnmanagedObject))),job.CheckCancellation);
            stage("set_source_geometry_"+i);before.Curve.UpgradeOpen();job.CheckCancellation();before.Curve.SetFromGeCurve(geometry,StoredNormal(before.Curve),Tolerance.Global);job.CheckCancellation();
            stage("verify_trimmed_source_"+i);Need(Near(Endpoint(before.Curve,edit.Ends[i]),attachments[i].Point)&&Near(Endpoint(before.Curve,edit.Ends[i]=="start"?"end":"start"),before.Opposite),"In-place native trim lost requested attachment/opposite endpoint.","NATIVE_CORNER_MISMATCH");
            var midpoint=retained.GetPointAtParameter(retained.StartParam/2+retained.EndParam/2);Need(Near(before.Curve.GetClosestPointTo(midpoint,false),midpoint),"In-place native trim did not retain the selected native piece.","NATIVE_CORNER_MISMATCH");VerifySource(before,tr,true,job.CheckCancellation);
        }
        stage("append_connector");var space=(BlockTableRecord)tr.GetObject(resolved.Model,OpenMode.ForWrite);job.CheckCancellation();space.AppendEntity(connector);tr.AddNewlyCreatedDBObject(connector,true);
        stage("verify_appended_connector");Need(connector.Database==db&&connector.OwnerId==resolved.Model&&!connector.ObjectId.IsNull,"Corner connector was appended to another owner/document.","INVALID_CORNER_OWNERSHIP");Independent(connector,job.CheckCancellation);
        Need(Exact(connectorShape,CadNativeCurveInspection342.Shape(connector,job.CheckCancellation))&&Exact(pair.Sources[0].Display,Display(connector)),"Appended connector differs from native factory geometry/display.","NATIVE_CORNER_MISMATCH");
        stage("verify_final_sources");foreach(var source in pair.Sources){VerifySource(source,tr,edit.TrimSources,job.CheckCancellation);if(!edit.TrimSources)Need(Exact(source.Shape,CadNativeCurveInspection342.Shape(source.Curve,job.CheckCancellation)),"Source-retaining corner changed its source geometry.","NATIVE_CORNER_MISMATCH");}
        warnings?.AddRange(pair.Warnings);warnings?.Add("INDEPENDENT_CORNER: Sources/connector are ordinary geometry without a new automatic association. Native recovery/Undo is not guaranteed; inspect the final corner shape and retained source portions.");
        stage("create_returned_receipt");return Result(new(args.BridgeId,args.DocumentId,Id(connector.ObjectId)!));
    }
}
