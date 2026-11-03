using System.Globalization;
using NanoCAD.Contracts;
using Teigha.DatabaseServices;
using Teigha.Geometry;

namespace NanoCADBridge;

internal static class GeometryAnalyzer
{
    public static object Read(GeometryAnalysisArguments args, DocumentState state, Transaction transaction,
        ObjectId modelId, string instance, CancellationToken cancellationToken)
    {
        if(args.BoundaryTrace is {} trace)return CadBoundaryTrace408.Read(args.Read,trace,state,transaction,cancellationToken.ThrowIfCancellationRequested);
        if(args.SolidMeasurement!=null)return CadSolidMeasurement409.Read(args,state,transaction,modelId,instance,cancellationToken);
        if(SolidGeometryAnalysisValidation.Handles(args.Mode))
            return CadSolidAnalysis.Read(args,state,transaction,modelId,instance,cancellationToken);
        void CheckBudget()
        {
            cancellationToken.ThrowIfCancellationRequested();
        }
        var curves = new List<Curve>();var outputCapacity=new SerializedCapacity357(Wire.PayloadBudget-8192,CheckBudget);
        var splineDetails = new Dictionary<Curve,SplineGeometry>(ReferenceEqualityComparer.Instance);
        foreach (string handle in args.Read.Handles!)
        {
            CheckBudget();
            var bits = ulong.Parse(handle, NumberStyles.HexNumber, CultureInfo.InvariantCulture);
            if (!state.Document.Database.TryGetObjectId(new Handle(unchecked((long)bits)), out var id) || id.IsNull || !id.IsValid)
                throw new DomainException("OBJECT_NOT_FOUND", "Analysis handle was not found.", false, new { handle });
            if (id.IsErased || id.IsEffectivelyErased) throw new DomainException("OBJECT_ERASED", "Analysis object has been erased.", false, new { handle });
            var item = transaction.GetObject(id, OpenMode.ForRead);
            if (item is not Entity entity || entity.OwnerId != modelId)
                throw new DomainException("OUT_OF_SCOPE", "Analysis requires top-level Model Space curves.", false, new { handle });
            if (item.GetType() != typeof(Line) && item.GetType() != typeof(Circle) && item.GetType() != typeof(Arc) && item.GetType() != typeof(Polyline) && item.GetType()!=typeof(Ellipse) && item.GetType()!=typeof(Spline))
                throw new DomainException("UNSUPPORTED_ENTITY_TYPE", "Analysis supports native Line, Circle, Arc, Ellipse, bounded Spline and lightweight Polyline.", false, new { handle });
            if(args.Mode=="containment" && item is not (Circle or Polyline))
                throw new DomainException("UNSUPPORTED_ENTITY_TYPE","Containment supports Circle or simple closed straight Polyline.");
            if(args.Workplane!=null&&item is not (Line or Circle or Arc or Polyline))
                throw new DomainException("UNSUPPORTED_ENTITY_TYPE","Explicit-plane analysis supports native Line/Circle/Arc/Polyline only.");
            if(item is Ellipse ellipse && args.Mode!="metrics")CadEllipseReader.Read(ellipse,CheckBudget);
            if(item is Spline spline && args.Mode!="metrics")
            {
                var data=CadSplineReader.Read(spline,CheckBudget);SplineVerification.RequirePairScope(data,args.Mode);splineDetails.Add(spline,data);
            }
            if (item is Polyline polyline && polyline.NumberOfVertices<2)
                throw new DomainException("ANALYSIS_LIMIT_EXCEEDED", "Each polyline requires at least two vertices.", false, new { handle });
            if (item is Line line && line.Length == 0)
                throw new DomainException("DEGENERATE_GEOMETRY", "A zero-length line has no curve direction.", false, new { handle });
            curves.Add((Curve)item);
        }
        EntityRef Reference(Curve c) => new(instance, state.Id, Validation.NormalizeHandle(c.Handle.ToString()));
        var units = new { name = state.Document.Database.Insunits.ToString(), value = (int)state.Document.Database.Insunits };
        if(args.Mode=="containment")
        {
            var classification=CadPointContainment.Read(curves[0],args.Point!,CheckBudget,args.Workplane);CheckBudget();
            return new{mode=args.Mode,coordinate_system="WCS",units,solver="native_bounds_and_brep",boundary=Reference(curves[0]),point=args.Point,
                classification,method="native_bounds_then_brep_face",tolerance_policy="native_kernel_with_conservative_bounds_exclusion",result_scope=args.Workplane==null?"single_simple_contour_in_WCS_XY_Z0":"single_simple_contour_in_explicit_workplane"};
        }
        if(args.Mode=="relations")
        {
            var result=CadCurveRelations.Read(curves[0],curves[1],CheckBudget,splineDetails.GetValueOrDefault(curves[0]),splineDetails.GetValueOrDefault(curves[1]),args.Workplane);CheckBudget();
            return new{mode=args.Mode,coordinate_system="WCS",units,solver="native_kernel",first=Reference(curves[0]),second=Reference(curves[1]),
                points=result.Points,overlaps=result.Overlaps,overlap_evaluated=true,result_scope=args.Workplane==null?"finite_curves_in_WCS_XY_Z0":"finite_curves_in_explicit_workplane",
                warning="Native kernel tolerance is used. Overlap intervals have positive length; no containment or simple-boundary classification is performed."};
        }
        if(args.Mode=="distance")
        {
            bool linear=splineDetails.Values.Any(s=>s.closed);
            var result=linear?CadLinearSplineDistance.Read(curves[0],curves[1],splineDetails,CheckBudget):CadCurveDistance.Read(curves[0],curves[1],CheckBudget);CheckBudget();
            bool circlePrimitive=linear&&curves.Any(c=>c is Circle);
            return new{mode=args.Mode,coordinate_system="WCS",units,solver=linear?(curves.Any(c=>c is Arc)?"analytic_segment_arc":circlePrimitive?"analytic_segment_circle":"analytic_finite_segments"):"native_kernel",
                first=Reference(curves[0]),second=Reference(curves[1]),first_point=result.First,second_point=result.Second,distance=result.Distance,
                result_scope="minimum_euclidean_distance_between_finite_curves",overlap_evaluated=false,
                warning=linear?"All actual finite segments are checked against the other segments, exact circle or finite arc, without curve approximation. Closest points may be nonunique; zero distance does not classify overlap. No extension or projection out of WCS XY/Z0.":"The closest pair need not be unique. Zero distance does not classify overlap. Native kernel tolerance is used; curves are not extended or projected."};
        }
        if (args.Mode == "intersections")
        {
            using var points = new Point3dCollection();
            curves[0].IntersectWith(curves[1], Intersect.OnBothOperands, points, IntPtr.Zero, IntPtr.Zero);
            CheckBudget();
            return new { mode = args.Mode, coordinate_system = "WCS", units, solver = "native_kernel",
                first = Reference(curves[0]), second = Reference(curves[1]), points = points.Cast<Point3d>().Select(p=>{CheckBudget();var value=Point(p);outputCapacity.Add(value);return value;}).ToArray(),
                result_scope = "isolated_points_on_both_finite_curves", overlap_evaluated = false,
                warning = "An empty point list does not rule out coincident or overlapping curves. Native kernel tolerance is used; curves are not extended or projected." };
        }
        var metrics = new List<object>();
        bool analyticLinearSpline=false;
        bool partial=false;
        foreach (var curve in curves)
        {
            try
            {
                CheckBudget();
                var splineData = curve is Spline spline ? CadSplineReader.Read(spline,CheckBudget) : null;
                var linear=splineData==null?null:LinearSplineMetrics.TryCalculate(splineData);
                analyticLinearSpline|=linear!=null;
                double length = linear?.Length ?? (curve switch { Line l => l.Length, Circle c => c.Circumference, Arc a => a.Length, Polyline pl => pl.Length, Ellipse e=>CadEllipseReader.Length(e,CheckBudget), Spline s=>CadSplineReader.Length(s,splineData!,CheckBudget), _ => throw new InvalidOperationException() });
                bool closed = curve is Circle || curve is Polyline p2 && p2.Closed || curve is Ellipse ellipse && ellipse.Closed || splineData?.closed == true;
                var extents = linear==null?curve.GeometricExtents:new Extents3d(new Point3d(linear.Min),new Point3d(linear.Max));
                object? nearest = null;
                if (args.Point is { } p)
                {
                    var query = new Point3d(p[0], p[1], p[2]);
                    var closest = curve.GetClosestPointTo(query, false);
                    nearest = new { point = Point(closest), distance = Number(query.DistanceTo(closest)) };
                }
                var observation=new { @ref = Reference(curve), type = curve.GetType().Name, length = Number(length), closed,
                    area = linear?.Area ?? (closed && splineData is null ? (double?)Number(curve.Area) : null),
                    area_source = linear?.Area != null ? "analytic_degree1_simple_xy_polygon" : closed && splineData is null ? "native_kernel" : "unavailable",
                    bounds = new { min = Point(extents.MinPoint), max = Point(extents.MaxPoint) }, nearest,
                    metric_source=linear==null?"native_kernel":"analytic_degree1_nurbs_length_and_bounds" };outputCapacity.Add(observation);metrics.Add(observation);
            }
            catch(DomainException e) when(e.Error.code=="ANALYSIS_LIMIT_EXCEEDED"&&metrics.Count>0){partial=true;break;}
        }
        return new { mode = args.Mode, coordinate_system = "WCS", units, solver = analyticLinearSpline?"native_kernel_with_analytic_linear_spline_metrics":"native_kernel", items = metrics,
            complete = !partial, remaining_handles = partial?curves.Skip(metrics.Count).Select(c=>Reference(c).handle).ToArray():Array.Empty<string>(),
            area_scope = "native_area_for_other_closed_curves;_analytic_area_for_simple_closed_degree1_xy_spline;_other_spline_area_unavailable", distance_scope = "euclidean_distance_from_point_to_finite_curve" };
    }
    private static double Number(double value) => double.IsFinite(value) ? value : throw new DomainException("INVALID_GEOMETRY", "Native analysis returned a non-finite number.");
    private static double[] Point(Point3d value) => new[] { Number(value.X), Number(value.Y), Number(value.Z) };
}
