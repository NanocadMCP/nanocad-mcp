using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using NanoCAD.Contracts;
using NanoCAD.Ipc;
using Teigha.DatabaseServices;
using Teigha.Geometry;

namespace NanoCADBridge;

internal static class CadNativeContourFinishing342
{
    private sealed record PlaneValue(Point3d Point,Vector3d Normal);
    private sealed record State(JsonElement Shape,JsonElement Display,JsonElement Metadata,PlaneValue? Plane);
    private sealed record Plan(Curve Source,State Before,Curve[] Boundaries,State[] BoundaryStates,double[] Cuts,Point3d? Pick,Point3d[] RetainedPoints,string[] Warnings);
    private sealed record Generated(Entity[] Owned,Curve[] Kept,JsonElement[] Shapes);
    private sealed class Notices
    {
        private readonly List<string> items=[];private readonly HashSet<string> seen=new(StringComparer.Ordinal);private long bytes;private bool omitted;
        internal void Add(string value){if(seen.Contains(value))return;int n=Encoding.UTF8.GetByteCount(value);if(bytes+n>16384){omitted=true;return;}bytes+=n;seen.Add(value);items.Add(value);}
        internal string[] Read()=>omitted?items.Append("NATIVE_FINISHING_NOTICE_LIMIT: Further diagnostic details exceeded16KiB; no curve data or returned piece was truncated.").ToArray():items.ToArray();
    }
    private sealed class Budget
    {
        private long bytes=2;private readonly Action cancel;internal Budget(Action check){cancel=check;}internal long Mark=>bytes;
        internal void Add(object value){cancel();bytes+=JsonSerializer.SerializeToUtf8Bytes(value,Wire.Json).LongLength+1;Need(bytes<=8L*1024*1024,"Native finishing inventory exceeds the actual8MiB serialized snapshot budget.","RESPONSE_LIMIT_EXCEEDED");}
        internal JsonElement Finish(long mark,object value){bytes=mark;Add(value);return Wire.Element(value);}
    }
    private static void Need([DoesNotReturnIf(false)] bool valid,string message,string code="OUT_OF_SCOPE")
    {if(!valid)throw new DomainException(code,message);}
    private static string? Id(ObjectId id)=>id.IsNull?null:Validation.NormalizeHandle(id.Handle.ToString());
    private static bool Same(JsonElement a,JsonElement b)=>a.GetRawText()==b.GetRawText();
    private static double Finite(double value){Need(double.IsFinite(value),"Native curve reports a nonfinite value.","INVALID_GEOMETRY");return value;}
    private static Point3d Point(Point3 p)=>new(p.X,p.Y,p.Z);
    private static bool Near(Point3d a,Point3d b)=>a.DistanceTo(b)<=Tolerance.Global.EqualPoint;
    private static bool InPlane(PlaneValue plane,Point3d point)=>Math.Abs((point-plane.Point).DotProduct(plane.Normal))<=Tolerance.Global.EqualPoint;
    private static bool Supported(Type type)=>type==typeof(Line)||type==typeof(Arc)||type==typeof(Circle)||type==typeof(Polyline)||type==typeof(Ellipse)||type==typeof(Spline);
    private static PlaneValue? Plane(Curve curve,Action cancel)
    {
        cancel();if(curve.GetType()==typeof(Line))return null;using var plane=curve.GetPlane();Need(plane!=null,"Native curved source has no available plane.","CURVE_PLANE_UNAVAILABLE");
        var normal=plane.Normal;Need(double.IsFinite(normal.Length)&&normal.Length>0,"Native curve plane normal is invalid.","INVALID_GEOMETRY");return new(plane.PointOnPlane,normal.GetNormal());
    }
    private static void SamePlane(Curve curve,PlaneValue expected,Action cancel)
    {
        cancel();Need(InPlane(expected,curve.StartPoint)&&InPlane(expected,curve.EndPoint),"Native curve endpoints lie outside the selected source plane.","CURVE_PLANE_MISMATCH");
        var plane=Plane(curve,cancel);if(plane!=null)Need(plane.Normal.CrossProduct(expected.Normal).Length<=Tolerance.Global.EqualVector&&InPlane(expected,plane.Point),"Native curve plane differs from the selected source plane.","CURVE_PLANE_MISMATCH");
    }
    private static PlaneValue? SelectedPlane(Curve source,SketchPlaneSpec? frame,Action cancel)
    {
        if(frame==null)return Plane(source,cancel);
        var normal=DrawingWorkplane.Normal(frame);var selected=new PlaneValue(Point(frame.Origin),new Vector3d(normal.X,normal.Y,normal.Z).GetNormal());
        SamePlane(source,selected,cancel);return selected;
    }
    private static JsonElement Display(Entity source)=>Wire.Element(new{layer_handle=Id(source.LayerId),properties=EntityAdapter.StoredProperties(source)});
    private static State Snapshot(Curve source,Database db,Transaction tr,Action cancel,Notices? notices=null)
    {
        cancel();Need(source.Database==db&&!source.ObjectId.IsNull&&source.ObjectId.IsValid&&!source.ObjectId.IsErased&&!source.ObjectId.IsEffectivelyErased&&Supported(source.GetType()),"Requires exact live resident ordinary native curve.","UNSUPPORTED_ENTITY_TYPE");
        var budget=new Budget(cancel);var shape=CadNativeCurveInspection342.Shape(source,cancel);budget.Add(shape);var display=Display(source);budget.Add(display);
        var metadata=Metadata(source,tr,cancel,budget,notices);budget.Finish(2,new{shape,display,metadata});return new(shape,display,metadata,Plane(source,cancel));
    }
    private static JsonElement Metadata(Curve source,Transaction tr,Action cancel,Budget budget,Notices? notices)
    {
        long mark=budget.Mark,rootMark=budget.Mark;using var xdata=source.XData;using var reactors=source.GetPersistentReactorIds();var links=new List<string?>();
        foreach(ObjectId id in reactors){cancel();budget.Add(Id(id)!);links.Add(Id(id));}
        var root=new{handle=Id(source.ObjectId),owner_handle=Id(source.OwnerId),field_flag=source.HasFields,annotative=source.Annotative.ToString(),extension_handle=Id(source.ExtensionDictionary),
            xdata=xdata==null?null:NativeOwnedMetadata.Buffer(xdata,cancel,budget.Add),reactors=links.OrderBy(v=>v,StringComparer.Ordinal).ToArray()};budget.Finish(rootMark,root);
        var pending=new Stack<(ObjectId Id,ObjectId Owner)>();var visited=new HashSet<ObjectId>();var records=new List<object>();var identities=new Budget(cancel);
        void Push(ObjectId id,ObjectId owner){identities.Add(new{handle=Id(id),owner_handle=Id(owner)});pending.Push((id,owner));}
        if(!source.ExtensionDictionary.IsNull)Push(source.ExtensionDictionary,source.ObjectId);
        while(pending.Count>0)
        {
            cancel();var next=pending.Pop();Need(!next.Id.IsNull&&next.Id.IsValid&&!next.Id.IsErased&&!next.Id.IsEffectivelyErased&&visited.Add(next.Id),"Source owned data has invalid/repeated identity or a cycle.","INVALID_SOURCE_OWNERSHIP");
            var item=tr.GetObject(next.Id,OpenMode.ForRead);Need(item.Database==source.Database&&item.OwnerId==next.Owner,"Source owned data has wrong document/owner.","INVALID_SOURCE_OWNERSHIP");long nodeMark=budget.Mark;
            using var data=item.XData;using var refs=item.GetPersistentReactorIds();var references=new List<string?>();foreach(ObjectId id in refs){cancel();budget.Add(Id(id)!);references.Add(Id(id));}
            var common=new{handle=Id(item.ObjectId),owner_handle=Id(item.OwnerId),runtime_type=item.GetType().FullName,field_flag=item.HasFields,extension_handle=Id(item.ExtensionDictionary),
                xdata=data==null?null:NativeOwnedMetadata.Buffer(data,cancel,budget.Add),reactors=references.OrderBy(v=>v,StringComparer.Ordinal).ToArray()};object record;
            if(item.GetType()==typeof(DBDictionary)&&item is DBDictionary dictionary)
            {
                var entries=new List<object>();var children=new List<(ObjectId Id,string Name)>();foreach(DBDictionaryEntry entry in dictionary)
                {
                    cancel();Need(!entry.Value.IsNull&&entry.Value.IsValid&&!entry.Value.IsErased&&!entry.Value.IsEffectivelyErased,"Source dictionary reference is unavailable.","INVALID_SOURCE_OWNERSHIP");
                    var child=tr.GetObject(entry.Value,OpenMode.ForRead);bool owns=child.Database==source.Database&&child.OwnerId==item.ObjectId;
                    var descriptor=new{name=entry.Key,handle=Id(entry.Value),owner_handle=Id(child.OwnerId),runtime_type=child.GetType().FullName,owned=owns};budget.Add(descriptor);entries.Add(descriptor);if(owns)children.Add((entry.Value,entry.Key));
                }
                foreach(var child in children.OrderByDescending(v=>v.Name,StringComparer.Ordinal))Push(child.Id,item.ObjectId);record=new{common,dictionary_hard_owned=dictionary.TreatElementsAsHard,entries};
            }
            else if(item.GetType()==typeof(Xrecord)&&item is Xrecord xrecord)
            {using var value=xrecord.Data;record=new{common,xlate_references=xrecord.XlateReferences,merge_style=xrecord.MergeStyle.ToString(),data=value==null?null:NativeOwnedMetadata.Buffer(value,cancel,budget.Add)};}
            else{record=new{common,deep_state="unclassified_read_only"};notices?.Add("NATIVE_FINISHING_OWNED_DATA_UNVERIFIED: Read-only owned object "+Id(item.ObjectId)+" ("+item.GetType().FullName+") has checked observable identity/metadata; internal content is not deeply verified.");}
            if(!item.ExtensionDictionary.IsNull)Push(item.ExtensionDictionary,item.ObjectId);records.Add(budget.Finish(nodeMark,record));
        }
        return budget.Finish(mark,new{root,owned=records});
    }
    private static void MutationOwnership(Curve source,NativeContourFinishingSpec342 edit,Database db,Transaction tr,Action cancel)
    {
        using var refs=source.GetPersistentReactorIds();Need(!source.HasFields&&refs.Count==0&&source.Annotative!=AnnotativeStates.True,"Source mutation/replacement requires its independent curve route; fields/reactors/annotation contexts need their owning adapter.","UNSUPPORTED_CURVE_DEPENDENCIES");
        if(edit.EraseSource)
        {using var data=source.XData;Need(source.ExtensionDictionary.IsNull&&(data==null||data.AsArray().Length==0),"Source replacement would discard owned/application metadata; retain the source with native split-copy or use its owning adapter.","UNSUPPORTED_CURVE_DEPENDENCIES");}
        else if(!source.ExtensionDictionary.IsNull)
            _=NativeOwnedMetadata.Capture(source,tr,cancel); // exact supported owned state, no unclassified mutation claim
        var loaded=AppDomain.CurrentDomain.GetAssemblies().Select(a=>a.GetName().Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        if(loaded.Contains("mapimgd")&&loaded.Contains("MapiBaseTypes"))CheckNativeOwner(source,db,tr,cancel);
    }
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void CheckNativeOwner(Curve source,Database db,Transaction tr,Action cancel)
    {
        var document=Multicad.DatabaseServices.McDocument.ActiveDocument;
        Need(HostMgd.ApplicationServices.Application.DocumentManager.MdiActiveDocument?.Database==db&&document!=null&&
            (string.IsNullOrEmpty(db.Filename)||string.Equals(Path.GetFullPath(db.Filename),Path.GetFullPath(document.Path),StringComparison.OrdinalIgnoreCase)),"Cannot establish this source's exact native document.","DOCUMENT_CONTEXT_MISMATCH");
        var sourceId=Multicad.McObjectId.FromHandle(source.Handle.Value,document.ID);if(sourceId.IsNull)return;
        Need(sourceId.GetObjectOfType<Multicad.Mc3D.HistoryItem>()==null,"Curve belongs to native feature history; use its model/sketch route.","UNSUPPORTED_CURVE_DEPENDENCIES");
        var model=((BlockTable)tr.GetObject(db.BlockTableId,OpenMode.ForRead))[BlockTableRecord.ModelSpace];var budget=new Budget(cancel);
        foreach(ObjectId id in (BlockTableRecord)tr.GetObject(model,OpenMode.ForRead))
        {
            cancel();if(id.IsNull||!id.IsValid||id.IsErased||id.IsEffectivelyErased)continue;budget.Add(Id(id)!);if(tr.GetObject(id,OpenMode.ForRead) is not Solid3d)continue;
            var body=Multicad.McObjectId.FromHandle(id.Handle.Value,document.ID).GetObjectOfType<Multicad.Mc3D.Mc3dSolid>();if(body==null)continue;
            var history=ModelInventoryBudget.History(body,cancel);foreach(var item in history)
            {
                cancel();budget.Add(item.ToString());Need(item!=sourceId&&!(item.GetObjectOfType<Multicad.Mc3D.ProjectionSketchEnt>() is {} p&&p.SrcObjectID==sourceId)&&
                    !(item.GetObjectOfType<Multicad.Mc3D.RevolveFeature>() is {} r&&r.Axis.ID==sourceId),"Source is consumed by native model history; use its owning route.","UNSUPPORTED_CURVE_DEPENDENCIES");
            }
            foreach(var reference in NativePathSources.ReferencedIds(history,cancel)){budget.Add(reference);Need(reference!=sourceId.ToString(),"Source supports a native model sketch/path; use its owning route.","UNSUPPORTED_CURVE_DEPENDENCIES");}
        }
    }
    internal static void CheckBoundary(Entity entity,Action cancel)
    {Need(entity is Curve&&Supported(entity.GetType()),"Native boundary requires exact Line/Arc/Circle/Polyline/Ellipse/Spline.","UNSUPPORTED_ENTITY_TYPE");_=CadNativeCurveInspection342.Shape((Curve)entity,cancel);}
    internal static void CheckSource(Entity entity,NativeContourFinishingSpec342 edit,Database db,Transaction tr,Action cancel)
    {
        Need(entity is Curve&&Supported(entity.GetType()),"Native finishing source requires exact Line/Arc/Circle/Polyline/Ellipse/Spline.","UNSUPPORTED_ENTITY_TYPE");
        var source=(Curve)entity;var before=Snapshot(source,db,tr,cancel);var selected=SelectedPlane(source,edit.Workplane,cancel);
        Need(NativeCurveRead342.Token(before.Shape,cancel)==edit.ExpectedCurveToken,"The complete source native shape differs from expected_curve_token; read current source/context.","GEOMETRY_CHANGED");
        if(edit.EraseSource||edit.Kind=="extend_curve")MutationOwnership(source,edit,db,tr,cancel);
        if(edit.Kind=="split_curve")ValidateCuts(source,edit.CutParameters,cancel);
        if(edit.Kind=="extend_curve")
        {
            Need(!source.Closed,"Closed curves have no extendable endpoint.","CLOSED_EXTENSION_SOURCE");var target=Point(edit.TargetPointWcs!);Need(selected==null||InPlane(selected,target),"Extension target lies outside the declared/native source plane.","CURVE_PLANE_MISMATCH");
            if(source.GetType()==typeof(Line))Need(Near(source.GetClosestPointTo(target,true),target),"A Line extension target must lie on the original infinite support, not change its direction.","INVALID_EXTENSION_TARGET");
            cancel();Need(!Near(source.GetClosestPointTo(target,false),target),"Extension target lies on the finite original curve; shortening/reparameterization is not extension.","INVALID_EXTENSION_TARGET");
        }
    }
    private static void ValidateCuts(Curve source,double[] cuts,Action cancel)
    {
        double start=Finite(source.StartParam),end=Finite(source.EndParam);Need(end>start,"Native parameter interval is collapsed.","INVALID_GEOMETRY");
        foreach(double cut in cuts){cancel();Need(double.IsFinite(cut)&&cut>=start&&cut<end&&(source.Closed||cut>start),"Cut is outside the current source interval; open cuts must be interior, closed cuts may use the start seam.","INVALID_SPLIT_PARAMETER");_=source.GetPointAtParameter(cut);}
        for(int i=1;i<cuts.Length;i++){cancel();Need(cuts[i]>cuts[i-1],"Native cut parameters must increase strictly.","INVALID_SPLIT_PARAMETER");}
    }
    private static double[] Cuts(Curve source,Curve[] boundaries,PlaneValue? plane,Point3d pick,Action cancel,Budget budget)
    {
        Need(plane==null||InPlane(plane,pick),"Trim pick lies outside the declared/native source plane.","CURVE_PLANE_MISMATCH");cancel();Need(Near(source.GetClosestPointTo(pick,false),pick),"Trim pick must lie on the finite native source.","PICK_POINT_OFF_CURVE");
        var points=new List<Point3d>();foreach(var boundary in boundaries)
        {
            cancel();if(plane!=null)SamePlane(boundary,plane,cancel);using var hits=new Point3dCollection();source.IntersectWith(boundary,Intersect.OnBothOperands,hits,IntPtr.Zero,IntPtr.Zero);
            for(int i=0;i<hits.Count;i++){cancel();var point=hits[i];budget.Add(new[]{point.X,point.Y,point.Z});Need(!Near(point,pick),"Pick must be away from actual boundary intersections.","AMBIGUOUS_PICK");points.Add(point);}
        }
        var parameters=new SortedSet<double>();double start=source.StartParam,end=source.EndParam;
        foreach(var point in points)
        {cancel();double p=Finite(source.GetParameterAtPoint(point));if(source.Closed&&p==end)p=start;if(p>=start&&p<end&&(source.Closed||p>start))parameters.Add(p);}
        var cuts=parameters.ToArray();Need(cuts.Length>0&&(!source.Closed||cuts.Length>=2),"No finite native boundary cuts define a removable interval (closed curves need at least two).","NO_INTERSECTION");ValidateCuts(source,cuts,cancel);return cuts;
    }
    private static Point3d[] RetainedPoints(Curve source,Action cancel)
    {
        // Actual native knot/segment breakpoints and their midpoints are
        // observations of the original locus, not reconstructed geometry.
        var parameters=new SortedSet<double>{source.StartParam,source.EndParam};var budget=new Budget(cancel);
        if(source is Polyline polyline)for(int i=0;i<polyline.NumberOfVertices;i++){cancel();double value=source.GetParameterAtPoint(polyline.GetPoint3dAt(i));budget.Add(value);parameters.Add(value);}
        if(source is Spline spline){var data=spline.NurbsData;using var controls=data.GetControlPoints();var knots=data.GetKnots();for(int i=0;i<knots.Count;i++){cancel();double value=knots[i];if(value>=source.StartParam&&value<=source.EndParam){budget.Add(value);parameters.Add(value);}}}
        var values=parameters.ToArray();var points=new List<Point3d>();for(int i=0;i<values.Length;i++)
        {cancel();var p=source.GetPointAtParameter(values[i]);budget.Add(new[]{p.X,p.Y,p.Z});points.Add(p);if(i+1<values.Length){p=source.GetPointAtParameter(values[i]/2+values[i+1]/2);budget.Add(new[]{p.X,p.Y,p.Z});points.Add(p);}}
        return points.ToArray();
    }
    private static Plan Prepare(NativeContourFinishingSpec342 edit,Entity entity,Func<string,Entity> readBoundary,Database db,Transaction tr,Action cancel)
    {
        CheckSource(entity,edit,db,tr,cancel);var source=(Curve)entity;var notices=new Notices();var before=Snapshot(source,db,tr,cancel,notices) with{Plane=SelectedPlane(source,edit.Workplane,cancel)};var budget=new Budget(cancel);budget.Add(new{before.Shape,before.Display,before.Metadata});
        var boundaries=new List<Curve>();var states=new List<State>();foreach(string handle in edit.BoundaryHandles)
        {cancel();var boundary=readBoundary(handle);CheckBoundary(boundary,cancel);var state=Snapshot((Curve)boundary,db,tr,cancel,notices);budget.Add(new{state.Shape,state.Display,state.Metadata});boundaries.Add((Curve)boundary);states.Add(state);}
        Point3d? pick=edit.PickPointWcs==null?null:Point(edit.PickPointWcs);var cuts=edit.Kind=="trim_curve"?Cuts(source,boundaries.ToArray(),before.Plane,pick!.Value,cancel,budget):edit.CutParameters;
        var retained=edit.Kind=="extend_curve"?RetainedPoints(source,cancel):[];
        if(source.HasFields)notices.Add("NATIVE_FINISHING_SOURCE_FIELDS_UNVERIFIED: Split-copy retains the source field flag/refs/observable shape; referenced field internals are not universally inspected.");
        if(source is Spline{HasFitData:true})notices.Add("NATIVE_FINISHING_SPLINE_FIT_OBSERVATION: Full current NURBS shape/available native fit observations are checked; no promise of unavailable original fit-construction metadata.");
        if(edit.Kind=="extend_curve")notices.Add("NATIVE_EXTENSION_LOCUS_OBSERVATION: Original native endpoints and native knot/segment breakpoint/midpoint loci are checked after extension; this is not a symbolic proof of every original point.");
        return new(source,before,boundaries.ToArray(),states.ToArray(),cuts,pick,retained,notices.Read());
    }
    internal static void Preview(ModifyArguments args,CadModifier.Resolved resolved,Database db,Transaction tr,Action cancel)
    {var edit=(NativeContourFinishingSpec342)args.Operations.Single();_=Prepare(edit,(Entity)tr.GetObject(resolved.Sources[edit.Handles[0]],OpenMode.ForRead),h=>(Entity)tr.GetObject(resolved.Sources[h],OpenMode.ForRead),db,tr,cancel);}
    private static void Independent(Entity entity,Action cancel)
    {cancel();using var refs=entity.GetPersistentReactorIds();using var data=entity.XData;Need(!entity.HasFields&&entity.ExtensionDictionary.IsNull&&refs.Count==0&&(data==null||data.AsArray().Length==0),"A generated root inherited source dependency/application data; nothing was stripped or silently dropped.","UNSUPPORTED_SPLIT_RESULT");}
    private static void PrimitiveThickness(Curve source,Curve result,Action cancel)
    {
        double? original=source switch{Line line=>line.Thickness,Arc arc=>arc.Thickness,Circle circle=>circle.Thickness,_=>null};
        if(original==null)return;
        cancel();double actual=result switch{Line line=>line.Thickness,Arc arc=>arc.Thickness,Circle circle=>circle.Thickness,Polyline polyline=>polyline.Thickness,_=>0};
        Need(Math.Abs(actual-original.Value)<=Tolerance.Global.EqualPoint,"Native finishing changed the selected primitive's physical thickness.","NATIVE_FINISHING_MISMATCH");
        // A zero-thickness Line has no geometric normal. For extruded display,
        // retain the actual extrusion direction as well as its thickness.
        if(original!=0)
        {
            Vector3d normal=source switch{Line line=>line.Normal,Arc arc=>arc.Normal,Circle circle=>circle.Normal,_=>Vector3d.ZAxis};
            Vector3d? changed=result switch{Line line=>line.Normal,Arc arc=>arc.Normal,Circle circle=>circle.Normal,Polyline polyline=>polyline.Normal,_=>null};
            Need(changed.HasValue&&(changed.Value-normal).Length<=Tolerance.Global.EqualVector,"Native finishing lost the selected primitive's extrusion direction.","NATIVE_FINISHING_MISMATCH");
        }
    }
    private static void RetainedPrimitive(Curve source,JsonElement before,Action cancel)
    {
        if(source.GetType()!=typeof(Line)&&source.GetType()!=typeof(Arc))return;
        cancel();var stored=before.GetProperty("geometry");
        double thickness=stored.GetProperty("thickness").GetDouble();
        double actual=source is Line line?line.Thickness:((Arc)source).Thickness;
        Need(Math.Abs(actual-thickness)<=Tolerance.Global.EqualPoint,"Native extension changed retained physical thickness.","NATIVE_FINISHING_MISMATCH");
        Vector3d Vector(string name){var value=stored.GetProperty(name);return new(value[0].GetDouble(),value[1].GetDouble(),value[2].GetDouble());}
        if(source is Arc arc)
        {
            var center=stored.GetProperty("center");
            Need(Near(arc.Center,new(center[0].GetDouble(),center[1].GetDouble(),center[2].GetDouble()))&&
                Math.Abs(arc.Radius-stored.GetProperty("radius").GetDouble())<=Tolerance.Global.EqualPoint&&
                (arc.Normal-Vector("normal")).Length<=Tolerance.Global.EqualVector,
                "Native extension changed the retained circular support or oriented normal.","NATIVE_FINISHING_MISMATCH");
        }
        else if(thickness!=0)Need((((Line)source).Normal-Vector("normal")).Length<=Tolerance.Global.EqualVector,"Native extension changed the retained Line extrusion direction.","NATIVE_FINISHING_MISMATCH");
    }
    private static Generated Split(Plan plan,Action cancel)
    {
        var cutPoints=new List<Point3d>();var cutBudget=new Budget(cancel);foreach(double value in plan.Cuts){cancel();var point=plan.Source.GetPointAtParameter(value);cutBudget.Add(new[]{point.X,point.Y,point.Z});cutPoints.Add(point);}
        cancel();var parameters=new DoubleCollection(plan.Cuts);var returned=plan.Source.GetSplitCurves(parameters)??throw new DomainException("NO_SPLIT_RESULT","Native split returned no collection.");
        var owned=new List<DBObject>();var identities=new Budget(cancel);var pointers=new HashSet<IntPtr>();var forbidden=new HashSet<IntPtr>(plan.Boundaries.Select(c=>c.UnmanagedObject)){plan.Source.UnmanagedObject};bool releaseAttempted=false;
        void Release(){releaseAttempted=true;returned.Dispose();}
        try
        {
            // GetSplitCurves has already produced the complete native set.
            // Acquire every safely identified wrapper before cancellation or
            // budget/shape work can throw. A bad index does not stop draining
            // other indices, and its unsafe alias is never owned/disposed.
            Exception? acquisitionError=null;int count=returned.Count;
            for(int i=0;i<count;i++)
            {
                try
                {
                    var item=returned[i];Need(item!=null&&item.ObjectId.IsNull&&item.OwnerId.IsNull&&item.UnmanagedObject!=IntPtr.Zero&&!forbidden.Contains(item.UnmanagedObject),"Native split returned a resident/source/boundary alias; unsafe wrapper was not adopted/disposed.","INVALID_SPLIT_OWNERSHIP");
                    Need(pointers.Add(item.UnmanagedObject),"Native split returned repeated native identity.","INVALID_SPLIT_OWNERSHIP");owned.Add(item);
                }
                catch(Exception error){acquisitionError??=error;}
            }
            if(acquisitionError!=null)throw acquisitionError;
            foreach(var item in owned){cancel();identities.Add(new{pointer=item.UnmanagedObject.ToString("X"),runtime_type=item.GetType().FullName});}
            Need(owned.Count>0,"Native split produced no pieces.","NO_SPLIT_RESULT");var kept=new List<Curve>();var shapes=new List<JsonElement>();var endpoints=new List<Point3d>();var budget=new Budget(cancel);int removed=0;
            foreach(var item in owned)
            {
                cancel();Need(item is Curve&&Supported(item.GetType()),"Native split returned an unsupported root; no piece was silently omitted.","UNSUPPORTED_SPLIT_RESULT");var curve=(Curve)item;Independent(curve,cancel);if(plan.Before.Plane!=null)SamePlane(curve,plan.Before.Plane,cancel);
                curve.SetPropertiesFrom(plan.Source);Independent(curve,cancel);Need(Same(plan.Before.Display,Display(curve)),"Native split root failed to preserve source display/layer.","NATIVE_FINISHING_MISMATCH");
                PrimitiveThickness(plan.Source,curve,cancel);
                var shape=CadNativeCurveInspection342.Shape(curve,cancel);budget.Add(shape);
                endpoints.Add(curve.StartPoint);endpoints.Add(curve.EndPoint);
                foreach(var point in new[]{curve.StartPoint,curve.EndPoint,curve.GetPointAtParameter(curve.StartParam/2+curve.EndParam/2)})
                {cancel();Need(Near(plan.Source.GetClosestPointTo(point,false),point),"Native split returned an observed point outside the original source locus.","NATIVE_FINISHING_MISMATCH");}
                if(plan.Pick.HasValue&&Near(curve.GetClosestPointTo(plan.Pick.Value,false),plan.Pick.Value)){removed++;continue;}
                kept.Add(curve);shapes.Add(shape);
            }
            bool Endpoint(Point3d point){foreach(var endpoint in endpoints){cancel();if(Near(point,endpoint))return true;}return false;}
            foreach(var point in cutPoints){cancel();Need(Endpoint(point),"A requested native cut did not become an actual result endpoint.","NATIVE_FINISHING_MISMATCH");}
            if(!plan.Source.Closed)Need(Endpoint(plan.Source.StartPoint)&&Endpoint(plan.Source.EndPoint),"Native split lost an original open source endpoint.","NATIVE_FINISHING_MISMATCH");
            Need(!plan.Pick.HasValue||removed==1,"Native trim pick belongs to zero or multiple actual pieces; no ambiguous piece was removed.","AMBIGUOUS_PICK");
            var result=new Generated(owned.Cast<Entity>().ToArray(),kept.ToArray(),shapes.ToArray());Release();return result;
        }
        catch(Exception primary)
        {var failures=new List<Exception>();if(!releaseAttempted)try{Release();}catch(Exception e){failures.Add(e);}foreach(var item in owned)try{item.Dispose();}catch(Exception e){failures.Add(e);}if(failures.Count>0)throw new AggregateException("Native split cleanup failed; recovery is unknown.",new[]{primary}.Concat(failures));throw;}
    }
    private static void VerifyReadOnly(Plan plan,Database db,Transaction tr,Action cancel,bool includeSource)
    {
        if(includeSource){var actual=Snapshot(plan.Source,db,tr,cancel);Need(Same(plan.Before.Shape,actual.Shape)&&Same(plan.Before.Display,actual.Display)&&Same(plan.Before.Metadata,actual.Metadata),"Native finishing changed its read-only source.","NATIVE_FINISHING_MISMATCH");}
        for(int i=0;i<plan.Boundaries.Length;i++){cancel();var current=Snapshot(plan.Boundaries[i],db,tr,cancel);var before=plan.BoundaryStates[i];Need(Same(before.Shape,current.Shape)&&Same(before.Display,current.Display)&&Same(before.Metadata,current.Metadata),"Native finishing changed a read-only cutting boundary.","NATIVE_FINISHING_MISMATCH");}
    }
    private static void OutputBudget(ModifyArguments args,NativeContourFinishingSpec342 edit,EntityRef[] generated,string[] warnings,Action cancel)
    {
        EntityRef source=new(args.BridgeId,args.DocumentId,edit.Handles[0]);var value=new ModifiedEntities(args.OperationId,"committed",false,edit.Kind=="extend_curve"?[source]:[],edit.EraseSource?[source]:[],generated,[],"not_guaranteed",contour_results:[new(0,edit.Kind,source,generated,edit.EraseSource)]);
        cancel();Need(JsonSerializer.SerializeToUtf8Bytes(new{data=value,warnings},Wire.Json).Length<=Wire.PayloadBudget-16384,"Complete generated refs/provenance/warnings exceed the actual output-byte budget; no root was appended or source changed.","RESPONSE_LIMIT_EXCEEDED");
    }
    internal static ModifiedEntities Apply(ModifyArguments args,CadModifier.Resolved resolved,Transaction tr,List<Entity> owned,WriteJob job,List<string>? warnings=null)
    {
        var edit=(NativeContourFinishingSpec342)args.Operations.Single();var source=(Curve)tr.GetObject(resolved.Sources[edit.Handles[0]],OpenMode.ForRead);var db=source.Database;
        var plan=Prepare(edit,source,h=>(Entity)tr.GetObject(resolved.Sources[h],OpenMode.ForRead),db,tr,job.CheckCancellation);var sourceRef=new EntityRef(args.BridgeId,args.DocumentId,edit.Handles[0]);
        if(edit.Kind=="extend_curve")
        {
            OutputBudget(args,edit,[],plan.Warnings,job.CheckCancellation);var opposite=edit.End=="start"?source.EndPoint:source.StartPoint;
            source.UpgradeOpen();job.CheckCancellation();source.Extend(edit.End=="start",Point(edit.TargetPointWcs!));job.CheckCancellation();
            RetainedPrimitive(source,plan.Before.Shape,job.CheckCancellation);
            Need(Near(edit.End=="start"?source.StartPoint:source.EndPoint,Point(edit.TargetPointWcs!))&&Near(edit.End=="start"?source.EndPoint:source.StartPoint,opposite),"Native extension did not reach requested endpoint or changed the opposite endpoint.","NATIVE_FINISHING_MISMATCH");
            if(plan.Before.Plane!=null)SamePlane(source,plan.Before.Plane,job.CheckCancellation);
            foreach(var point in plan.RetainedPoints){job.CheckCancellation();Need(Near(source.GetClosestPointTo(point,false),point),"Native extension changed an observed original native locus point.","NATIVE_FINISHING_MISMATCH");}
            var current=Snapshot(source,db,tr,job.CheckCancellation);Need(Same(plan.Before.Display,current.Display)&&Same(plan.Before.Metadata,current.Metadata),"Native extension changed retained root/display/observable metadata.","NATIVE_FINISHING_MISMATCH");VerifyReadOnly(plan,db,tr,job.CheckCancellation,false);warnings?.AddRange(plan.Warnings);
            var oldLength=plan.Before.Shape.GetProperty("length");var newLength=current.Shape.GetProperty("length");
            if(oldLength.ValueKind==JsonValueKind.Null||newLength.ValueKind==JsonValueKind.Null)
                warnings?.Add("NATIVE_EXTENSION_LENGTH_UNVERIFIED: Optional native length is unavailable before/after extension; exact target/opposite endpoint, native plane, observed retained locus and metadata remain verified. Review the final engineering image.");
            else if(newLength.GetDouble()<=oldLength.GetDouble())
                warnings?.Add("NATIVE_EXTENSION_LENGTH_OBSERVATION: Native reported length did not increase (before="+oldLength.GetRawText()+", after="+newLength.GetRawText()+"); exact requested endpoint and retained-locus checks passed. Inspect the resulting contour against the intended extension.");
            return new(args.OperationId,"committed",false,[sourceRef],[],[],[],"not_guaranteed",contour_results:[new(0,edit.Kind,sourceRef,[],false)]);
        }
        var generated=Split(plan,job.CheckCancellation);owned.AddRange(generated.Owned); // adopt ALL roots before any fallible output/append work
        var projected=new List<EntityRef>();var ledger=new Budget(job.CheckCancellation);foreach(var curve in generated.Kept){job.CheckCancellation();var reference=sourceRef with{handle="FFFFFFFFFFFFFFFF"};ledger.Add(reference);projected.Add(reference);}
        OutputBudget(args,edit,projected.ToArray(),plan.Warnings,job.CheckCancellation);VerifyReadOnly(plan,db,tr,job.CheckCancellation,true);var refs=new List<EntityRef>();var space=(BlockTableRecord)tr.GetObject(resolved.Model,OpenMode.ForWrite);
        for(int i=0;i<generated.Kept.Length;i++)
        {
            job.CheckCancellation();var curve=generated.Kept[i];space.AppendEntity(curve);tr.AddNewlyCreatedDBObject(curve,true);Independent(curve,job.CheckCancellation);
            Need(curve.Database==db&&curve.OwnerId==resolved.Model&&!curve.ObjectId.IsNull&&Same(generated.Shapes[i],CadNativeCurveInspection342.Shape(curve,job.CheckCancellation))&&Same(plan.Before.Display,Display(curve)),"Appended native split root lost exact factory shape/display/ownership.","NATIVE_FINISHING_MISMATCH");
            refs.Add(new(args.BridgeId,args.DocumentId,Id(curve.ObjectId)!));
        }
        VerifyReadOnly(plan,db,tr,job.CheckCancellation,true);
        if(edit.EraseSource){job.CheckCancellation();source.UpgradeOpen();source.Erase(true);Need(source.IsErased,"Native source replacement did not erase the selected original.","NATIVE_FINISHING_MISMATCH");VerifyReadOnly(plan,db,tr,job.CheckCancellation,false);}
        warnings?.AddRange(plan.Warnings);return new(args.OperationId,"committed",false,[],edit.EraseSource?[sourceRef]:[],refs.ToArray(),[],"not_guaranteed",contour_results:[new(0,edit.Kind,sourceRef,refs.ToArray(),edit.EraseSource)]);
    }
}
