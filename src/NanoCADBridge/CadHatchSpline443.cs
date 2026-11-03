using NanoCAD.Contracts;
using Teigha.DatabaseServices;
using Teigha.Geometry;
using Span443=NanoCAD.Contracts.HatchBoundaryGeometry.ParameterSpan443;

namespace NanoCADBridge;

// Only exact native definitions and native geometric relations. These temporary
// curves are verification inputs; AppendLoop receives the resident source IDs.
internal static class CadHatchSpline443
{
    internal sealed record Definition(int Degree,bool Rational,bool Periodic,Point2[] Controls,double[] Knots,double[] Weights,
        double Start,double End,double KnotTolerance,double Precision);
    const double Epsilon=2.2204460492503131e-16;
    static void Need(bool ok,string message,string code="UNSUPPORTED_HATCH_BOUNDARY")
    {if(!ok)throw new DomainException(code,message);}
    static double Number(double value)
    {Need(double.IsFinite(value),"Native Spline definition contains a nonfinite value.","INVALID_GEOMETRY");return value;}

    internal static Definition Capture(Spline source,Hatch hatch,Action cancel)
    {
        cancel();Need(source.GetType()==typeof(Spline)&&source.Closed&&!source.HasFields,
            "Use an actually closed native Spline without Fields as one outer/hole source.");
        // This additive route has an explicit WCS XY/Z0 scope. Existing conic,
        // bulged-Polyline and Line/Arc routes retain their other native planes.
        Need(hatch.Normal.IsEqualTo(Vector3d.ZAxis,Tolerance.Global)&&Math.Abs(hatch.Elevation)<=Tolerance.Global.EqualPoint,
            "Spline Hatch sources currently require WCS XY/Z0; no projection is performed.","PLANAR_GEOMETRY_REQUIRED");
        var data=source.NurbsData;using var points=data.GetControlPoints();var knots=data.GetKnots();var weights=data.GetWeights();
        var controls=new List<Point2>();var knotValues=new List<double>();var weightValues=new List<double>();var budget=new ModelInventoryBudget(cancel);
        double precision=1e-7;
        for(int i=0;i<points.Count;i++)
        {
            cancel();var p=points[i];Number(p.X);Number(p.Y);Number(p.Z);
            precision=Math.Max(precision,32*Epsilon*Math.Max(Math.Abs(p.X),Math.Abs(p.Y)));
            Need(Math.Abs(p.Z)<=Tolerance.Global.EqualPoint,"Every native Spline control must lie in WCS XY/Z0; no projection is performed.","PLANAR_GEOMETRY_REQUIRED");
            var point=new Point2(p.X,p.Y);budget.Add(point);controls.Add(point);
        }
        for(int i=0;i<knots.Count;i++){cancel();double value=Number(knots[i]);budget.Add(value);knotValues.Add(value);}
        for(int i=0;i<weights.Count;i++){cancel();double value=Number(weights[i]);budget.Add(value);weightValues.Add(value);}
        double start=Number(source.StartParam),end=Number(source.EndParam);
        Need(data.Degree>0&&controls.Count>data.Degree&&knotValues.Count>1&&end>start&&
            knotValues.Zip(knotValues.Skip(1),(a,b)=>a<=b).All(ok=>ok)&&
            (data.Rational?weights.Count==points.Count:weights.Count==0||weights.Count==points.Count),
            "Closed Spline needs its complete finite native NURBS definition; no refit is performed.","INVALID_GEOMETRY");
        var result=new Definition(data.Degree,data.Rational,data.Periodic,controls.ToArray(),knotValues.ToArray(),weightValues.ToArray(),start,end,Number(data.KnotTolerance),precision);
        budget.Add(result);return result;
    }

    static NurbCurve2d Native(Definition definition,Action cancel)
    {
        using var knots=new KnotCollection();knots.Tolerance=definition.KnotTolerance;
        foreach(double value in definition.Knots){cancel();knots.Add(value);}
        using var controls=new Point2dCollection(definition.Controls.Select(p=>new Point2d(p.X,p.Y)).ToArray());
        var weights=new DoubleCollection(definition.Weights);
        // Public native NURBS constructor copies observed controls/knots/weights.
        // This is neither fit-point interpolation nor faceted reconstruction.
        var native=definition.Rational?new NurbCurve2d(definition.Degree,knots,controls,weights,definition.Periodic):
            new NurbCurve2d(definition.Degree,knots,controls,definition.Periodic);
        try
        {
            // Installed public Interval(lower,upper,tolerance) has no 2-arg
            // overload; parameter tolerance is the observed native knot value.
            // Curve2d.SetInterval returns void in this installed SDK.
            using var interval=new Interval(definition.Start,definition.End,definition.KnotTolerance);native.SetInterval(interval);cancel();
            Need(native.IsClosed(new Tolerance(Tolerance.Global.EqualVector,definition.Precision)),"Observed native Spline definition did not yield the same closed verification curve.","NATIVE_HATCH_ASSOCIATION_MISMATCH");
            return native;
        }
        catch{native.Dispose();throw;}
    }

    // RXObject.Clone of a closed Spline changed its resident source in441d.
    // Direct native construction from the complete observed NurbsData avoids
    // that path. Only an independently proved detached root is ever adopted.
    internal static Spline Detached(Spline source,Action cancel)
    {
        cancel();var before=CadSplineReader.ReadReported(source,cancel);var data=source.NurbsData;
        using var controls=data.GetControlPoints();var knots=data.GetKnots();var weights=data.GetWeights();
        var copy=new Spline(data.Degree,data.Rational,data.Closed,data.Periodic,controls,knots,weights,data.ControlPointTolerance,data.KnotTolerance);
        if(copy.GetType()!=typeof(Spline)||!copy.ObjectId.IsNull||!copy.OwnerId.IsNull||copy.Database!=null||!copy.AutoDelete||
            copy.UnmanagedObject==IntPtr.Zero||copy.UnmanagedObject==source.UnmanagedObject)
        {
            // A rejected borrowed alias is not ours to dispose or finalize.
            GC.SuppressFinalize(copy);
            throw new DomainException("UNSAFE_NATIVE_CLONE","Temporary Spline topology copy did not prove independent detached ownership; an unsafe alias was not adopted, disposed or finalized.");
        }
        try
        {
            cancel();Need(BlockMoveGeometry.SameProperties(Wire.Element(before),Wire.Element(CadSplineReader.ReadReported(source,cancel))),
                "Native Spline topology preparation changed the retained source definition.","NATIVE_HATCH_SOURCE_CHANGED");
            // Fit records are not needed on this temporary Region input. The
            // complete geometric NURBS data, actual closure and domain are.
            var actual=CadSplineReader.ReadReported(copy,cancel);
            Need(actual.degree==before.degree&&actual.rational==before.rational&&actual.periodic==before.periodic&&actual.closed==before.closed&&
                actual.native_inspection.entity_definition.closed==before.native_inspection.entity_definition.closed&&
                BlockMoveGeometry.SameProperties(Wire.Element(actual.control_points),Wire.Element(before.control_points))&&
                BlockMoveGeometry.SameProperties(Wire.Element(actual.knots),Wire.Element(before.knots))&&
                BlockMoveGeometry.SameProperties(Wire.Element(actual.weights),Wire.Element(before.weights))&&
                actual.start_parameter==before.start_parameter&&actual.end_parameter==before.end_parameter,
                "Temporary Spline Region input changed the observed native NURBS geometry.","NATIVE_HATCH_TOPOLOGY_MISMATCH");
            return copy;
        }
        catch{copy.Dispose();throw;}
    }

    static double Length(Curve2d curve,double start,double end,Tolerance tolerance,Action cancel)
    {
        cancel();double length=curve.GetLength(start,end,tolerance);cancel();
        Need(double.IsFinite(length)&&length>=0,"Native curve relation returned an invalid physical length.","NATIVE_HATCH_ASSOCIATION_MISMATCH");return length;
    }
    static bool Covered(Curve2d curve,Interval domain,List<Span443> spans,double precision,Tolerance tolerance,Action cancel)
    {
        double missing=0;
        foreach(var gap in HatchBoundaryGeometry.Uncovered443(domain.LowerBound,domain.UpperBound,spans,cancel))
        {missing+=Length(curve,gap.Start,gap.End,tolerance,cancel);if(missing>precision)return false;}
        return true;
    }
    internal static bool Same(Definition definition,Curve2d[] stored,Action cancel)
    {
        if(stored.Length==0)return false;using var expected=Native(definition,cancel);using var domain=expected.GetInterval();
        var tolerance=new Tolerance(Tolerance.Global.EqualVector,definition.Precision);double length=Length(expected,domain.LowerBound,domain.UpperBound,tolerance,cancel);
        Need(length>definition.Precision,"Closed Spline boundary has no positive physical extent.","INVALID_GEOMETRY");
        // Fast native equivalence may handle the ordinary unchanged basis. A
        // false result is not a representation-based refusal: native overlap
        // coverage below admits changed knots, direction, seam and split edges.
        if(stored.Length==1&&expected.IsEqualTo(stored[0],tolerance))
        {
            using var actual=stored[0].GetInterval();
            if(actual.IsBounded&&Math.Abs(Length(stored[0],actual.LowerBound,actual.UpperBound,tolerance,cancel)-length)<=definition.Precision)return true;
        }
        var sourceRanges=new List<Span443>();double total=0;var budget=new ModelInventoryBudget(cancel);
        foreach(var curve in stored)
        {
            cancel();using var actual=curve.GetInterval();
            if(!actual.IsBounded||!double.IsFinite(actual.LowerBound)||!double.IsFinite(actual.UpperBound)||actual.UpperBound<=actual.LowerBound)return false;
            var ranges=new List<Span443>();using var relation=new CurveCurveIntersector2d(expected,curve,domain,actual,tolerance);cancel();
            int count=relation.OverlapCount;Need(count>=0,"Native NURBS overlap inventory is invalid.","NATIVE_HATCH_ASSOCIATION_MISMATCH");
            for(int i=0;i<count;i++)
            {
                cancel();Interval[]? pair=null;
                try
                {
                    pair=relation.GetOverlapRanges(i);cancel();
                    Need(pair!=null&&pair.Length==2&&pair.All(value=>value!=null&&value.IsBounded),"Native NURBS overlap must have two finite parameter ranges.","NATIVE_HATCH_ASSOCIATION_MISMATCH");
                    var a=new Span443(pair![0].LowerBound,pair[0].UpperBound);var b=new Span443(pair[1].LowerBound,pair[1].UpperBound);
                    budget.Add(a);budget.Add(b);sourceRanges.Add(a);ranges.Add(b);
                }
                finally{if(pair!=null)foreach(var item in pair.Distinct<Interval>(ReferenceEqualityComparer.Instance))item?.Dispose();}
            }
            if(!Covered(curve,actual,ranges,definition.Precision,tolerance,cancel))return false;
            total+=Length(curve,actual.LowerBound,actual.UpperBound,tolerance,cancel);
        }
        // Coverage in both domains plus physical length excludes duplicate full
        // traversals; there is no sampled-point or synthetic-area acceptance.
        return Covered(expected,domain,sourceRanges,definition.Precision,tolerance,cancel)&&
            Math.Abs(total-length)<=definition.Precision+32*Epsilon*Math.Max(total,length);
    }

    internal static Curve2d[] StoredPolyline(HatchLoop loop,Action cancel)
    {
        // A native kernel may canonicalize a degenerate/conic Spline to exact
        // native line/arc bulges. Read those stored segments without chords.
        using var polyline=new Polyline();var values=loop.Polyline.Cast<BulgeVertex>().ToArray();int count=values.Length;
        if(count>1&&values[0].Vertex.GetDistanceTo(values[^1].Vertex)<=Tolerance.Global.EqualPoint&&values[^1].Bulge==0)count--;
        for(int i=0;i<count;i++){cancel();polyline.AddVertexAt(i,values[i].Vertex,values[i].Bulge,0,0);}polyline.Closed=true;
        var curves=new List<Curve2d>();
        try
        {
            for(int i=0;i<count;i++)
            {
                cancel();int type=(int)polyline.GetSegmentType(i);
                Need(type is 0 or 1,"Native stored loop contains an empty or unsupported segment.","NATIVE_HATCH_ASSOCIATION_MISMATCH");
                curves.Add(type==0?polyline.GetLineSegment2dAt(i):polyline.GetArcSegment2dAt(i));
            }
            return curves.ToArray();
        }
        catch{foreach(var curve in curves)curve.Dispose();throw;}
    }
}
