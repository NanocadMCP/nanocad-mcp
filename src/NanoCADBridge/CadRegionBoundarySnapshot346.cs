using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using NanoCAD.Contracts;
using Teigha.Geometry;

namespace NanoCADBridge;

// Edge.Curve supplies an owned native geometric copy. Keep its real type:
// converting every B-rep edge to NURBS crashes this installed host on circles.
internal static class CadRegionBoundarySnapshot346
{
    private const long MaxBytes=8L*1024*1024;
    private static void Need([DoesNotReturnIf(false)] bool value,string message)
    {if(!value)throw new DomainException("REGION_GEOMETRY_UNAVAILABLE",message);}
    private static double Number(double value)=>double.IsFinite(value)?(value==0?0:value):
        throw new DomainException("INVALID_GEOMETRY","Native Region boundary contains a nonfinite value.");
    private static double[] Point(Point3d value)=>new[]{Number(value.X),Number(value.Y),Number(value.Z)};
    private static double[] Vector(Vector3d value)=>new[]{Number(value.X),Number(value.Y),Number(value.Z)};
    private sealed class Budget
    {
        internal long Bytes=2;
        internal void Add(object value)
        {
            Bytes+=JsonSerializer.SerializeToUtf8Bytes(value,Wire.Json).LongLength+1;
            if(Bytes>MaxBytes)throw new DomainException("RESPONSE_LIMIT_EXCEEDED","Native Region boundary snapshot exceeds the actual8MiB serialized-byte budget.");
        }
        internal void Finish(long mark,object value){Bytes=mark;Add(value);}
    }
    private static object Interval(Curve3d curve)
    {
        using var interval=curve.GetInterval();
        Need(interval!=null,"Native Region boundary parameter interval is unavailable.");
        // A line's supporting curve can be unbounded; the B-rep vertices still
        // delimit its edge. Never replace it with a guessed finite interval.
        bool below=interval!.IsBoundedBelow,above=interval.IsBoundedAbove;
        return new{bounded_below=below,bounded_above=above,
            lower=below?(double?)Number(interval.LowerBound):null,
            upper=above?(double?)Number(interval.UpperBound):null};
    }
    private static object Primitive(Curve3d curve,Action cancel,Budget budget)
    {
        if(curve is LineSegment3d segment)return new{start=Point(segment.StartPoint),end=Point(segment.EndPoint)};
        if(curve is LinearEntity3d line)return new{point=Point(line.PointOnLine),direction=Vector(line.Direction)};
        if(curve is CircularArc3d circle)return new{center=Point(circle.Center),normal=Vector(circle.Normal),
            reference=Vector(circle.ReferenceVector),radius=Number(circle.Radius),start_angle=Number(circle.StartAngle),end_angle=Number(circle.EndAngle)};
        if(curve is EllipticalArc3d ellipse)return new{center=Point(ellipse.Center),normal=Vector(ellipse.Normal),
            major_axis=Vector(ellipse.MajorAxis),minor_axis=Vector(ellipse.MinorAxis),major_radius=Number(ellipse.MajorRadius),
            minor_radius=Number(ellipse.MinorRadius),start_angle=Number(ellipse.StartAngle),end_angle=Number(ellipse.EndAngle)};
        if(curve is NurbCurve3d nurb)
        {
            var data=nurb.DefinitionData;
            // DefinitionData allocates the backing native collections. Their
            // lifetime is separate from this owned Curve3d copy.
            using var points=data.ControlPoints;using var knots=data.Knots;var weights=data.Weights;
            Need(data.Degree>=1&&points.Count>0&&knots.Count>0,"Native Region boundary NURBS definition is unavailable.");
            var controls=new List<double[]>();var knotValues=new List<double>();var weightValues=new List<double>();
            for(int i=0;i<points.Count;i++){cancel();var value=Point(points[i]);budget.Add(value);controls.Add(value);}
            for(int i=0;i<knots.Count;i++){cancel();var value=Number(knots[i]);budget.Add(value);knotValues.Add(value);}
            for(int i=0;i<weights.Count;i++){cancel();var value=Number(weights[i]);budget.Add(value);weightValues.Add(value);}
            return new{degree=data.Degree,rational=data.Rational,periodic=data.Periodic,knot_tolerance=Number(knots.Tolerance),
                controls,knots=knotValues,weights=weightValues};
        }
        throw new DomainException("REGION_GEOMETRY_UNAVAILABLE","Native Region boundary curve type has no complete stored-geometry reader.",false,new{runtime_type=curve.GetType().FullName});
    }
    internal static JsonElement Capture(Curve3d root,Action cancel)
    {
        Need(root!=null&&root.UnmanagedObject!=IntPtr.Zero,"Native Region boundary curve is unavailable.");
        var budget=new Budget();var identities=new Budget();
        var owned=new List<Curve3d>();var pointers=new HashSet<IntPtr>{root.UnmanagedObject};
        var pending=new Stack<(Curve3d Curve,int Index)>();var nodes=new List<object?>{null};pending.Push((root,0));
        Exception? failure=null;JsonElement snapshot=default;
        Curve3d Adopt(Curve3d? value)
        {
            Need(value!=null&&value.UnmanagedObject!=IntPtr.Zero&&pointers.Add(value.UnmanagedObject),
                "Native Region boundary getter returned a null or repeated wrapper; an unsafe alias was not disposed.");
            owned.Add(value!);return value!;
        }
        int Register(Curve3d child)
        {
            cancel();int index=nodes.Count;identities.Add(new{index,pointer=child.UnmanagedObject.ToString("X")});
            nodes.Add(null);pending.Push((child,index));return index;
        }
        try
        {
            while(pending.Count>0)
            {
                cancel();var item=pending.Pop();long mark=budget.Bytes;var interval=Interval(item.Curve);budget.Add(interval);object geometry;
                if(item.Curve is ExternalCurve3d external)
                {
                    // NativeCurve returns a newly owned native copy (CreateRaw),
                    // not the external library pointer exposed by ExternalCurve.
                    var native=Adopt(external.NativeCurve);geometry=new{external_kind=external.ExternalCurveKind.ToString(),native_curve=Register(native)};
                }
                else if(item.Curve is CompositeCurve3d composite)
                {
                    var children=composite.GetCurves();Need(children!=null&&children.Length>0,"Native Region composite boundary has no curves.");
                    Exception? acquisitionFailure=null;
                    // The returned shared wrappers are all acquired before a
                    // cancellable/budgeted traversal can abandon the array.
                    foreach(var child in children!)try{Adopt(child);}catch(Exception e){acquisitionFailure??=e;}
                    if(acquisitionFailure!=null)throw acquisitionFailure;
                    var indices=new int[children!.Length];
                    for(int i=0;i<children.Length;i++)indices[i]=Register(children[i]);
                    geometry=new{children=indices};
                }
                else geometry=Primitive(item.Curve,cancel,budget);
                var node=new{index=item.Index,runtime_type=item.Curve.GetType().FullName,interval,geometry};
                budget.Finish(mark,node);nodes[item.Index]=node;
            }
            cancel();snapshot=Wire.Element(new{root=0,nodes});
            if(JsonSerializer.SerializeToUtf8Bytes(snapshot,Wire.Json).LongLength>MaxBytes)
                throw new DomainException("RESPONSE_LIMIT_EXCEEDED","Complete native Region boundary snapshot exceeds the actual8MiB serialized-byte budget.");
        }
        catch(Exception e){failure=e;}
        var cleanup=new List<Exception>();
        foreach(var curve in owned)try{curve.Dispose();}catch(Exception e){cleanup.Add(e);}
        if(failure!=null)
        {
            if(cleanup.Count>0)throw new AggregateException("Region boundary observation failed and some owned geometric wrappers could not be released.",new[]{failure}.Concat(cleanup));
            System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failure).Throw();
        }
        if(cleanup.Count>0)throw new AggregateException("Some owned Region boundary geometric wrappers could not be released.",cleanup);
        return snapshot;
    }
}
