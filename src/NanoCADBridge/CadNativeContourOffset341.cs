using System.Text.Json;
using System.Text;
using NanoCAD.Contracts;
using Teigha.DatabaseServices;
using Teigha.Geometry;

namespace NanoCADBridge;

// Native factory creates new curves, never edits/rebuilds its resident source.
internal static class CadNativeContourOffset341
{
    internal sealed record Generated(Entity[] Curves,JsonElement[] Shapes,JsonElement Display,JsonElement Source,string[] Warnings);
    private sealed record PlaneValue(double[] point,double[] normal);
    private sealed class Notices
    {
        private readonly List<string> items=new();private readonly HashSet<string> seen=new(StringComparer.Ordinal);private long bytes;private bool omitted;
        internal void Add(string value){if(seen.Contains(value))return;int size=Encoding.UTF8.GetByteCount(value);if(bytes+size>16384){omitted=true;return;}seen.Add(value);bytes+=size;items.Add(value);}
        internal string[] Read()=>omitted?items.Append("OFFSET_VERIFICATION_NOTICE_LIMIT: Further diagnostic details exceeded16KiB; source owned internals/public spline detail may remain unverified, and no curve/model data was truncated.").ToArray():items.ToArray();
    }
    private sealed class Budget
    {
        private long bytes=2;private readonly Action cancel;
        internal Budget(Action check){cancel=check;}
        internal long Mark=>bytes;
        internal void Add(object value){cancel();bytes+=JsonSerializer.SerializeToUtf8Bytes(value,Wire.Json).LongLength+1;Need(bytes<=8*1024*1024,"Native offset snapshot exceeds the actual8MiB serialized-byte budget.","RESPONSE_LIMIT_EXCEEDED");}
        internal JsonElement Finish(long mark,object value){bytes=mark;Add(value);return Wire.Element(value);}
    }
    private static void Need(bool condition,string message,string code="OUT_OF_SCOPE")
    {if(!condition)throw new DomainException(code,message);}
    private static double Number(double value){Need(double.IsFinite(value),"Native curve contains a nonfinite stored value.","INVALID_GEOMETRY");return value;}
    private static double[] Point(Point3d value)=>new[]{Number(value.X),Number(value.Y),Number(value.Z)};
    private static double[] Vector(Vector3d value)=>new[]{Number(value.X),Number(value.Y),Number(value.Z)};
    private static string? Id(ObjectId id)=>id.IsNull?null:Validation.NormalizeHandle(id.Handle.ToString());
    private static bool Exact(JsonElement a,JsonElement b)=>a.GetRawText()==b.GetRawText();
    private static Point3d P(double[] p)=>new(p[0],p[1],p[2]);
    private static Vector3d V(double[] p)=>new(p[0],p[1],p[2]);
    private static PlaneValue Plane(Curve curve,Action cancel,SketchPlaneSpec? frame=null)
    {
        if(frame!=null)
        {
            var requestedNormal=DrawingWorkplane.Normal(frame);
            var selected=new PlaneValue(new[]{frame.Origin.X,frame.Origin.Y,frame.Origin.Z},Vector(new Vector3d(requestedNormal.X,requestedNormal.Y,requestedNormal.Z).GetNormal()));
            Need(InPlane(selected,Point(curve.StartPoint))&&InPlane(selected,Point(curve.EndPoint)),"Native source endpoints are outside the declared offset plane.","CURVE_PLANE_MISMATCH");
            if(curve.GetType()!=typeof(Line))
            {
                var actual=Plane(curve,cancel);
                Need(V(actual.normal).CrossProduct(V(selected.normal)).Length<=Tolerance.Global.EqualVector&&InPlane(selected,actual.point),"Native source plane differs from the declared offset plane.","CURVE_PLANE_MISMATCH");
            }
            return selected;
        }
        if(curve.GetType()==typeof(Line))throw new DomainException("INVALID_ARGUMENT","A Line has no unique offset plane; supply the intended workplane for its native signed offset.");
        cancel();using var plane=curve.GetPlane();
        if(plane==null)throw new DomainException("CURVE_PLANE_UNAVAILABLE","Native planar curve has no available plane.");
        var normal=plane.Normal;Number(normal.Length);Need(normal.Length>0,"Native curve plane has zero normal.","INVALID_GEOMETRY");
        return new(Point(plane.PointOnPlane),Vector(normal.GetNormal()));
    }
    private static bool InPlane(PlaneValue plane,double[] point)=>Math.Abs((P(point)-P(plane.point)).DotProduct(V(plane.normal)))<=Tolerance.Global.EqualPoint;
    private static JsonElement Shape(Curve curve,Action cancel,Budget budget,PlaneValue? expectedPlane=null)
    {
        long mark=budget.Mark;cancel();var type=curve.GetType();object geometry;
        double start=Number(curve.StartParam),end=Number(curve.EndParam);
        double length=Number(curve.GetDistanceAtParameter(end)-curve.GetDistanceAtParameter(start));cancel();
        Need(end>start&&length>0,"Native curve has a collapsed or unavailable finite parameter interval/length.","INVALID_GEOMETRY");
        if(type==typeof(Polyline))
        {
            var p=(Polyline)curve;Need(p.NumberOfVertices>=2&&p.Thickness==0&&p.ConstantWidth==0,"Native offset uses planar zero-thickness/zero-width Polylines with real segments.");
            var vertices=new List<object>();
            for(int i=0;i<p.NumberOfVertices;i++)
            {
                cancel();Need(p.GetStartWidthAt(i)==0&&p.GetEndWidthAt(i)==0,"Native polyline offset does not claim variable-width edge semantics.");
                var point=Point(p.GetPoint3dAt(i));Need(expectedPlane==null||InPlane(expectedPlane,point),"Native offset result left the source plane.","NATIVE_OFFSET_MISMATCH");
                var vertex=new{point_ocs=new[]{Number(p.GetPoint2dAt(i).X),Number(p.GetPoint2dAt(i).Y)},point_wcs=point,bulge=Number(p.GetBulgeAt(i)),start_width=p.GetStartWidthAt(i),end_width=p.GetEndWidthAt(i)};
                budget.Add(vertex);vertices.Add(vertex);
            }
            geometry=new{normal=Vector(p.Normal),elevation=Number(p.Elevation),thickness=p.Thickness,constant_width=p.ConstantWidth,vertices};
        }
        else if(type==typeof(Ellipse))
        {
            var e=(Ellipse)curve;geometry=new{center=Point(e.Center),major_axis=Vector(e.MajorAxis),minor_axis=Vector(e.MinorAxis),normal=Vector(e.Normal),
                major_radius=Number(e.MajorRadius),minor_radius=Number(e.MinorRadius),radius_ratio=Number(e.RadiusRatio)};
        }
        else if(type==typeof(Spline))
        {
            var s=(Spline)curve;Need(s.IsPlanar,"Native offset supports planar Spline sources/results.");var data=s.NurbsData;
            using var points=data.GetControlPoints();var knots=data.GetKnots();var weights=data.GetWeights();var controls=new List<double[]>();var knotValues=new List<double>();var weightValues=new List<double>();
            for(int i=0;i<points.Count;i++){cancel();var point=Point(points[i]);Need(expectedPlane==null||InPlane(expectedPlane,point),"Native offset NURBS control polygon left the source plane.","NATIVE_OFFSET_MISMATCH");budget.Add(point);controls.Add(point);}
            for(int i=0;i<knots.Count;i++){cancel();double knot=Number(knots[i]);Need(i==0||knots[i-1]<=knot,"Native spline knot sequence is unordered.","INVALID_GEOMETRY");budget.Add(knot);knotValues.Add(knot);}
            for(int i=0;i<weights.Count;i++){cancel();double weight=Number(weights[i]);budget.Add(weight);weightValues.Add(weight);}
            // The resident kernel already evaluates its parameter interval and
            // length. Retain its complete reported definition; do not impose
            // an unproved clamped-knot formula on periodic representations.
            Need(data.Degree>0&&controls.Count>0&&knotValues.Count>0,"Native spline NURBS definition is unavailable.","INVALID_GEOMETRY");
            geometry=new{degree=data.Degree,rational=data.Rational,periodic=data.Periodic,closed=data.Closed,has_fit_data=s.HasFitData,controls,knots=knotValues,weights=weightValues,
                control_tolerance=Number(data.ControlPointTolerance),knot_tolerance=Number(data.KnotTolerance)};
        }
        else if(type==typeof(Line)){var l=(Line)curve;Need(l.Thickness==0,"Native offset returned a thick Line.","NATIVE_OFFSET_MISMATCH");geometry=new{start=Point(l.StartPoint),end=Point(l.EndPoint),normal=Vector(l.Normal),thickness=l.Thickness};}
        else if(type==typeof(Arc)){var a=(Arc)curve;Need(a.Radius>0&&a.Thickness==0,"Native offset returned an invalid/thick Arc.","NATIVE_OFFSET_MISMATCH");geometry=new{center=Point(a.Center),radius=Number(a.Radius),normal=Vector(a.Normal),start_angle=Number(a.StartAngle),end_angle=Number(a.EndAngle),thickness=a.Thickness};}
        else if(type==typeof(Circle)){var c=(Circle)curve;Need(c.Radius>0&&c.Thickness==0,"Native offset returned an invalid/thick Circle.","NATIVE_OFFSET_MISMATCH");geometry=new{center=Point(c.Center),radius=Number(c.Radius),normal=Vector(c.Normal),thickness=c.Thickness};}
        else throw new DomainException("UNSUPPORTED_OFFSET_RESULT","Native offset returned an unclassified curve type; no returned piece was silently discarded.",false,new{runtime_type=type.FullName});
        var first=Point(curve.StartPoint);var last=Point(curve.EndPoint);
        Need(expectedPlane==null||InPlane(expectedPlane,first)&&InPlane(expectedPlane,last),"Native offset endpoints left the source plane.","NATIVE_OFFSET_MISMATCH");
        PlaneValue? plane=type==typeof(Line)?null:Plane(curve,cancel);
        if(expectedPlane!=null&&plane!=null)Need(V(plane.normal).CrossProduct(V(expectedPlane.normal)).Length<=Tolerance.Global.EqualVector&&InPlane(expectedPlane,plane.point),"Native offset curve plane differs from its source plane.","NATIVE_OFFSET_MISMATCH");
        return budget.Finish(mark,new{runtime_type=type.FullName,closed=curve.Closed,start_parameter=start,end_parameter=end,length,start_point=first,end_point=last,plane,geometry});
    }
    private static JsonElement Display(Entity entity)=>Wire.Element(new{layer_handle=Id(entity.LayerId),properties=EntityAdapter.StoredProperties(entity)});
    // Follow only ownership. Unclassified read-only nodes retain their observed
    // common state; their internals are a disclosed observation boundary.
    private static JsonElement Metadata(Curve source,Transaction tr,Action cancel,Budget budget,Notices? warnings)
    {
        long mark=budget.Mark;long rootMark=budget.Mark;using var rootXdata=source.XData;using var rootReactors=source.GetPersistentReactorIds();
        var reactors=new List<string?>();foreach(ObjectId id in rootReactors){cancel();budget.Add(Id(id)!);reactors.Add(Id(id));}
        var root=new{handle=Id(source.ObjectId),owner_handle=Id(source.OwnerId),field_flag=source.HasFields,extension_handle=Id(source.ExtensionDictionary),
            xdata=rootXdata==null?null:NativeOwnedMetadata.Buffer(rootXdata,cancel,budget.Add),reactors=reactors.OrderBy(v=>v,StringComparer.Ordinal).ToArray()};budget.Finish(rootMark,root);
        var pending=new Stack<(ObjectId Id,ObjectId Owner)>();var visited=new HashSet<ObjectId>();var records=new List<object>();var identities=new Budget(cancel);
        void Push(ObjectId id,ObjectId owner){identities.Add(new{handle=Id(id),owner_handle=Id(owner)});pending.Push((id,owner));}
        if(!source.ExtensionDictionary.IsNull)Push(source.ExtensionDictionary,source.ObjectId);
        while(pending.Count>0)
        {
            cancel();var next=pending.Pop();Need(!next.Id.IsNull&&next.Id.IsValid&&!next.Id.IsErased&&!next.Id.IsEffectivelyErased,"Source owned metadata identity is invalid.","INVALID_SOURCE_OWNERSHIP");
            Need(visited.Add(next.Id),"Source owned metadata has a cycle or repeated ownership path.","INVALID_SOURCE_OWNERSHIP");
            var item=tr.GetObject(next.Id,OpenMode.ForRead);Need(item.Database==source.Database&&item.OwnerId==next.Owner,"Source owned metadata has inconsistent owner/document.","INVALID_SOURCE_OWNERSHIP");
            long nodeMark=budget.Mark;using var xdata=item.XData;using var links=item.GetPersistentReactorIds();var references=new List<string?>();
            foreach(ObjectId id in links){cancel();budget.Add(Id(id)!);references.Add(Id(id));}
            var common=new{handle=Id(item.ObjectId),owner_handle=Id(item.OwnerId),runtime_type=item.GetType().FullName,field_flag=item.HasFields,
                extension_handle=Id(item.ExtensionDictionary),xdata=xdata==null?null:NativeOwnedMetadata.Buffer(xdata,cancel,budget.Add),reactors=references.OrderBy(v=>v,StringComparer.Ordinal).ToArray()};object record;
            if(item.GetType()==typeof(DBDictionary)&&item is DBDictionary dict)
            {
                var entries=new List<object>();var owned=new List<(ObjectId Id,string Name)>();
                foreach(DBDictionaryEntry entry in dict)
                {
                    cancel();Need(!entry.Value.IsNull&&entry.Value.IsValid&&!entry.Value.IsErased&&!entry.Value.IsEffectivelyErased,"Source dictionary child identity is invalid.","INVALID_SOURCE_OWNERSHIP");
                    var child=tr.GetObject(entry.Value,OpenMode.ForRead);bool owns=child.Database==source.Database&&child.OwnerId==item.ObjectId;
                    var descriptor=new{name=entry.Key,handle=Id(entry.Value),owner_handle=Id(child.OwnerId),runtime_type=child.GetType().FullName,owned=owns};budget.Add(descriptor);entries.Add(descriptor);
                    if(owns)owned.Add((entry.Value,entry.Key));
                }
                foreach(var child in owned.OrderByDescending(v=>v.Name,StringComparer.Ordinal))Push(child.Id,item.ObjectId);
                record=new{common,dictionary_hard_owned=dict.TreatElementsAsHard,entries};
            }
            else if(item.GetType()==typeof(Xrecord)&&item is Xrecord xrecord)
            {using var data=xrecord.Data;record=new{common,xlate_references=xrecord.XlateReferences,merge_style=xrecord.MergeStyle.ToString(),data=data==null?null:NativeOwnedMetadata.Buffer(data,cancel,budget.Add)};}
            else
            {
                record=new{common,deep_state="unclassified_read_only"};
                warnings?.Add("OFFSET_SOURCE_OWNED_DATA_UNVERIFIED: Read-only owned path owner="+Id(item.OwnerId)+"/object="+Id(item.ObjectId)+" ("+item.GetType().FullName+") retains observed identities/flags/metadata; its internal content is not deeply verified.");
            }
            if(!item.ExtensionDictionary.IsNull)Push(item.ExtensionDictionary,item.ObjectId);
            var stored=budget.Finish(nodeMark,record);records.Add(stored);
        }
        return budget.Finish(mark,new{root,owned=records});
    }
    private static JsonElement Source(Curve source,Database db,Transaction tr,Action cancel,Notices? warnings=null,SketchPlaneSpec? workplane=null)
    {
        cancel();Need(!source.ObjectId.IsNull&&source.Database==db,"Native offset requires the exact resident source.");
        var type=source.GetType();Need(type==typeof(Line)||type==typeof(Arc)||type==typeof(Circle)||type==typeof(Polyline)||type==typeof(Ellipse)||type==typeof(Spline),"Explicit native-sign offset supports exact planar Line/Arc/Circle/Polyline/Ellipse/Spline sources.","UNSUPPORTED_ENTITY_TYPE");
        var budget=new Budget(cancel);var plane=Plane(source,cancel,workplane);var shape=Shape(source,cancel,budget,plane);var display=Display(source);budget.Add(display);var metadata=Metadata(source,tr,cancel,budget,warnings);
        if(source.HasFields)warnings?.Add("OFFSET_SOURCE_FIELDS_UNVERIFIED: The source is read-only and its field flag/references/observable geometry remain checked; referenced field internals are not cloned or universally deeply verified.");
        if(source is Spline{HasFitData:true})warnings?.Add("OFFSET_SOURCE_SPLINE_FIT_UNVERIFIED: Exact current NURBS shape and HasFitData are retained; original fit constraints/tolerances are not fully inspected by this read-only factory adapter.");
        return budget.Finish(2,new{shape,display,metadata});
    }
    internal static void CheckSource(Entity entity,NativeOffsetCurveSpec341 edit,Database db,Transaction tr,Action cancel)
    {
        Need(entity is Curve,"Native offset requires a curve.","UNSUPPORTED_ENTITY_TYPE");Source((Curve)entity,db,tr,cancel,workplane:edit.Workplane);
        Need(NativeContourOffsetValidation341.HandlesSide(edit.Side)&&double.IsFinite(edit.SignedDistance)&&edit.SignedDistance!=0,"Invalid native offset sign/distance.","INVALID_ARGUMENT");
    }
    private static bool Detached(DBObject item,IntPtr sourcePointer)=>item.ObjectId.IsNull&&item.OwnerId.IsNull&&item.UnmanagedObject!=IntPtr.Zero&&item.UnmanagedObject!=sourcePointer;
    private static void Independent(Entity item,Action cancel)
    {
        cancel();using var reactors=item.GetPersistentReactorIds();using var xdata=item.XData;
        Need(!item.HasFields&&item.ExtensionDictionary.IsNull&&reactors.Count==0&&(xdata==null||xdata.AsArray().Length==0),"Native offset result inherited fields/reactors/XData/owned data; its graph was not stripped or attached.","UNSUPPORTED_OFFSET_RESULT");
    }
    internal static Generated Create(Entity entity,NativeOffsetCurveSpec341 edit,Database db,Transaction tr,Action cancel)
    {
        CheckSource(entity,edit,db,tr,cancel);var source=(Curve)entity;var warnings=new Notices();var before=Source(source,db,tr,cancel,warnings,edit.Workplane);
        var plane=Plane(source,cancel,edit.Workplane);var display=Display(source);IntPtr sourcePointer=source.UnmanagedObject;
        cancel();var returned=(edit.Workplane==null?source.GetOffsetCurves(edit.SignedDistance):source.GetOffsetCurvesGivenPlaneNormal(V(plane.normal),edit.SignedDistance))??throw new DomainException("NO_OFFSET_RESULT","Native offset returned no collection.");
        var owned=new List<DBObject>();var pointers=new HashSet<IntPtr>();var identities=new Budget(cancel);bool collectionReleaseAttempted=false;
        void ReleaseCollection(){collectionReleaseAttempted=true;returned.Dispose();}
        try
        {
            // Drain every factory index before cancellation/byte checks or
            // shape admission can fail. Retain the first acquisition failure,
            // but still adopt the remaining safe unique detached wrappers.
            // Never own/dispose resident, source or repeated native aliases.
            Exception? acquisitionError=null;int count=returned.Count;
            for(int index=0;index<count;index++)
            {
                try
                {
                    var item=returned[index]??throw new DomainException("UNSUPPORTED_OFFSET_RESULT","Native offset contains an unavailable result wrapper.");
                    Need(Detached(item,sourcePointer),"Native offset returned a source/resident/owned alias; it was not adopted or disposed.","INVALID_OFFSET_OWNERSHIP");
                    Need(pointers.Add(item.UnmanagedObject),"Native offset returned duplicate native identities.","INVALID_OFFSET_OWNERSHIP");owned.Add(item);
                }
                catch(Exception error){acquisitionError??=error;}
            }
            if(acquisitionError!=null)throw acquisitionError;
            foreach(var item in owned)
                identities.Add(new{native_pointer=item.UnmanagedObject.ToString("X"),runtime_type=item.GetType().FullName});
            Need(owned.Count>0,"Native offset produced no curve for this source/distance.","NO_OFFSET_RESULT");
            var curves=new List<Entity>();var shapes=new List<JsonElement>();var budget=new Budget(cancel);
            foreach(var item in owned)
            {
                cancel();if(item is not Curve curve)throw new DomainException("UNSUPPORTED_OFFSET_RESULT","Native offset returned a non-curve object; no returned piece was discarded.",false,new{runtime_type=item.GetType().FullName});
                Independent(curve,cancel);curve.SetPropertiesFrom(source);cancel();Independent(curve,cancel);
                Need(Exact(display,Display(curve)),"Generated native curve did not retain requested source display properties/layer.","NATIVE_OFFSET_MISMATCH");
                shapes.Add(Shape(curve,cancel,budget,plane));curves.Add(curve);
                if(curve.GetType()==typeof(Spline))
                    warnings.Add("OFFSET_RESULT_SPLINE_DETAIL_SCOPE: Complete reported native Spline NURBS data can be inspected within the actual response budget; optional fit/plane observations can be unavailable. Creation, transforms, geometry replacement and paired analysis retain their operation-specific scope; inspection does not certify broader editing.");
            }
            Need(Exact(before,Source(source,db,tr,cancel,workplane:edit.Workplane)),"Native offset changed its read-only source geometry/identity/observed metadata.","NATIVE_OFFSET_MISMATCH");
            var generated=new Generated(curves.ToArray(),shapes.ToArray(),display,before,warnings.Read());
            // A release fault must remain inside this ownership catch: the
            // caller cannot adopt returned wrappers until Create succeeds.
            ReleaseCollection();return generated;
        }
        catch(Exception original)
        {
            // Fresh PE DeleteUnmanagedObject removes cached wrapper references
            // and releases the native OdRxObjectPtr array; it does not call
            // Dispose on wrappers. Unmaterialized returns are released by the
            // collection. Dispose every adopted wrapper even if another fails;
            // do not grow an unbounded cleanup inventory or touch bad aliases.
            var failures=new List<Exception>();
            if(!collectionReleaseAttempted)try{ReleaseCollection();}catch(Exception error){failures.Add(error);}
            foreach(var item in owned)try{item.Dispose();}catch(Exception error){failures.Add(error);}
            if(failures.Count>0)throw new AggregateException("Native offset cleanup failed; recovery is unknown.",new[]{original}.Concat(failures));throw;
        }
    }
    internal static void CheckProjectedResultBudget(Generated generated,int operationIndex,EntityRef source,Action cancel)
    {
        var refs=new List<EntityRef>();long bytes=0;
        foreach(var curve in generated.Curves)
        {
            cancel();var reference=source with{handle="FFFFFFFFFFFFFFFF"};bytes+=2L*JsonSerializer.SerializeToUtf8Bytes(reference,Wire.Json).Length+2;
            Need(bytes<=Wire.PayloadBudget-16384,"Complete native offset mappings exceed the actual transport budget; no result was appended or omitted.","RESPONSE_LIMIT_EXCEEDED");refs.Add(reference);
        }
        var projected=new{status="committed",operation_id="FFFFFFFF-FFFF-FFFF-FFFF-FFFFFFFFFFFF",modified=Array.Empty<EntityRef>(),created=refs,erased=Array.Empty<EntityRef>(),
            contour_results=new[]{new ContourMapping(operationIndex,"offset_curve",source,refs.ToArray(),false)},undo_history="not_guaranteed",warnings=generated.Warnings};
        cancel();Need(JsonSerializer.SerializeToUtf8Bytes(projected,Wire.Json).Length<=Wire.PayloadBudget-16384,
            "Complete native offset mappings/warnings exceed the actual transport budget; no result was appended or omitted.","RESPONSE_LIMIT_EXCEEDED");
    }
    internal static void VerifyAppended(Entity entity,JsonElement shape,JsonElement display,ObjectId owner,Action cancel)
    {
        Need(entity is Curve&&!entity.ObjectId.IsNull&&entity.OwnerId==owner,"Native offset root was not appended to the intended owning space.","INVALID_OFFSET_OWNERSHIP");
        Independent(entity,cancel);Need(Exact(shape,Shape((Curve)entity,cancel,new Budget(cancel)))&&Exact(display,Display(entity)),"Appended native curve differs from its factory geometry/display.","NATIVE_OFFSET_MISMATCH");
    }
    internal static void VerifySource(Entity entity,JsonElement before,Database db,Transaction tr,Action cancel,SketchPlaneSpec? workplane=null)
    {Need(entity is Curve&&Exact(before,Source((Curve)entity,db,tr,cancel,workplane:workplane)),"Native offset changed its retained source state.","NATIVE_OFFSET_MISMATCH");}
}
