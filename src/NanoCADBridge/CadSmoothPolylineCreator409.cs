using System.Runtime.CompilerServices;
using System.Diagnostics.CodeAnalysis;
using System.Security.Cryptography;
using System.Text.Json;
using NanoCAD.Contracts;
using Teigha.DatabaseServices;
using Teigha.Geometry;

namespace NanoCADBridge;

internal static class CadSmoothPolylineCreator409
{
    private sealed record Origin(string Method,string? NativeRejection,int[] CornerVertices);
    private static readonly ConditionalWeakTable<Polyline,Origin> Origins=new();
    // Keep an unproved auto-delete wrapper strongly referenced. Disposing it
    // or allowing its finalizer to run could destroy a resident/source alias.
    private static readonly List<Curve> QuarantinedConversionWrappers=new();
    private static void Need([DoesNotReturnIf(false)] bool condition,string message)
    {if(!condition)throw new DomainException("SMOOTH_POLYLINE_FIT_MISMATCH",message);}

    private static SmoothPolylineGeometry409.Plan ReadLocal(Polyline polyline,SmoothPolylineSpec409 spec,int[] corners,Action cancel,bool inWorld)
    {
        cancel();Need(polyline.GetType()==typeof(Polyline)&&polyline.Closed==spec.Closed&&polyline.Thickness==0&&polyline.ConstantWidth==0,
            "Smooth construction requires an actual zero-width native arc/line Polyline with the requested closure.");
        var frame=inWorld?spec.Workplane??DrawingWorkplane.XY:DrawingWorkplane.XY;
        var normal=DrawingWorkplane.Normal(frame);
        var inverse=DrawingWorkplaneNative.Matrix(frame).Inverse();
        Need((polyline.Normal-new Vector3d(normal.X,normal.Y,normal.Z)).Length<=1e-9,"Smooth Polyline changed its requested plane normal.");
        double resolution=SmoothPolylineGeometry409.Resolution(spec.SourcePoints);
        resolution=Math.Max(resolution,64*2.2204460492503131e-16*Math.Max(1,Math.Max(Math.Abs(frame.Origin.X),Math.Max(Math.Abs(frame.Origin.Y),Math.Abs(frame.Origin.Z)))));
        var vertices=new List<Point2>();var bulges=new List<double>();var capacity=new ModelInventoryBudget(cancel);
        Need(polyline.NumberOfVertices>=2,"Native conversion returned insufficient Polyline vertices.");
        for(int i=0;i<polyline.NumberOfVertices;i++)
        {
            cancel();Need(polyline.GetStartWidthAt(i)==0&&polyline.GetEndWidthAt(i)==0,"Smooth conversion introduced unrequested segment widths.");
            var p=polyline.GetPoint3dAt(i);var delta=new Point3(p.X-frame.Origin.X,p.Y-frame.Origin.Y,p.Z-frame.Origin.Z);
            Need(double.IsFinite(p.X)&&double.IsFinite(p.Y)&&double.IsFinite(p.Z)&&Math.Abs(DrawingWorkplane.Dot(delta,normal))<=resolution,
                "Actual Polyline vertex left the requested workplane; no projection occurs.");
            // Invert the actual declared creation transform. Dot products
            // alone accumulate frame-validation roundoff for long curves.
            var localPoint=p.TransformBy(inverse);var local=new Point2(localPoint.X,localPoint.Y);
            double bulge=polyline.GetBulgeAt(i);capacity.Add(new{point=local,bulge});vertices.Add(local);bulges.Add(bulge);
        }
        Need(corners.Length==spec.CornerIndices.Length,"Native fit lost an explicitly marked source corner.");
        for(int i=0;i<corners.Length;i++)
        {
            cancel();Need(corners[i]>=0&&corners[i]<vertices.Count,"Native corner identity lies outside its Polyline vertices.");
            var actual=vertices[corners[i]];var source=spec.SourcePoints[spec.CornerIndices[i]];
            Need(Math.Sqrt(Math.Pow(actual.X-source.X,2)+Math.Pow(actual.Y-source.Y,2))<=resolution,"Native fit moved an explicitly marked source corner.");
        }
        int count=spec.Closed?vertices.Count:vertices.Count-1;var segments=new SmoothPolylineGeometry409.Segment[count];
        for(int i=0;i<count;i++)
        {cancel();segments[i]=new(vertices[i],vertices[(i+1)%vertices.Count],bulges[i],-1,-1,corners.Contains(i));}
        return new(segments,spec.Closed,corners);
    }
    private static bool RecoverableConversion(Teigha.Runtime.Exception error)=>error.ErrorStatus.ToString() is
        "NotApplicable" or "NotImplementedYet" or "NotImplemented" or "InvalidInput" or "DegenerateGeometry" or "GeneralModelingFailure";

    internal static Polyline CreateLocal(SmoothPolylineSpec409 spec,Action? check=null)
    {
        var cancel=check??(()=>{});cancel();string? rejected=null;
        double resolution=SmoothPolylineGeometry409.Resolution(spec.SourcePoints);
        Need(spec.FitTolerance>=resolution,"Requested sample tolerance is below the numerical resolution of its coordinates.");
        for(int i=1;i<spec.SourcePoints.Length;i++)
        {
            cancel();var a=spec.SourcePoints[i-1];var b=spec.SourcePoints[i];
            Need(Math.Sqrt(Math.Pow(a.X-b.X,2)+Math.Pow(a.Y-b.Y,2))>resolution,"Consecutive source spacing is below coordinate resolution.");
        }
        // This native route is eligible only for an open smooth piece. A
        // fitted Spline is a temporary conversion source and is always freed.
        // No Spline is returned/appended as a substitute for the Polyline.
        if(!spec.Closed&&spec.CornerIndices.Length==0&&spec.SourcePoints.Length>=3)
        {
            Curve? owned=null;
            try
            {
                var localPoints=new Point3d[spec.SourcePoints.Length];
                for(int i=0;i<localPoints.Length;i++){cancel();var p=spec.SourcePoints[i];localPoints[i]=new(p.X,p.Y,0);}
                using var points=new Point3dCollection(localPoints);
                using var spline=spec.StartTangent is {} start&&spec.EndTangent is {} end
                    ?new Spline(points,new Vector3d(start.X,start.Y,0),new Vector3d(end.X,end.Y,0),3,0)
                    :new Spline(points,3,0);
                cancel();var converted=spline.ToPolyline(true,true);
                try
                {
                    if(converted==null||converted.UnmanagedObject==IntPtr.Zero||!converted.ObjectId.IsNull||!converted.OwnerId.IsNull||converted.Database!=null||converted.UnmanagedObject==spline.UnmanagedObject)
                        throw new DomainException("UNSAFE_NATIVE_SMOOTH_CONVERSION","Native arc conversion ownership is unproved.");
                    owned=converted;
                }
                catch(Exception error)
                {
                    if(converted!=null){GC.SuppressFinalize(converted);QuarantinedConversionWrappers.Add(converted);}
                    throw new DomainException("UNSAFE_NATIVE_SMOOTH_CONVERSION","Native arc conversion ownership is unproved; its wrapper was quarantined without disposal.",false,
                        new{observation_error=error is DomainException domain?domain.Error.code:error.GetType().Name});
                }
                Need(converted!=null,"Native conversion did not return a curve.");cancel();
                Need(converted.GetType()==typeof(Polyline)&&!converted.HasFields&&converted.ExtensionDictionary.IsNull,
                    "Native arc conversion did not return an ordinary lightweight Polyline.");
                var polyline=(Polyline)converted;var plan=ReadLocal(polyline,spec,Array.Empty<int>(),cancel,false);
                _=SmoothPolylineGeometry409.Observe(plan,spec.SourcePoints,spec.FitTolerance,false,cancel);
                SmoothPolylineGeometry409.RequireEndpointDirections(plan,spec.StartTangent,spec.EndTangent);
                Origins.Add(polyline,new("native_fit_spline_arc_conversion",null,Array.Empty<int>()));owned=null;return polyline;
            }
            catch(DomainException error) when(error.Error.code is "SMOOTH_POLYLINE_FIT_MISMATCH" or "RESPONSE_LIMIT_EXCEEDED")
            {rejected=error.Message;}
            catch(Teigha.Runtime.Exception error) when(RecoverableConversion(error))
            {rejected="Native arc conversion returned "+error.ErrorStatus+" for this detached request.";}
            finally{owned?.Dispose();}
        }
        else rejected=spec.Closed?"Native open fit conversion does not establish a tangent-continuous closed seam.":
            spec.CornerIndices.Length>0?"Explicit source corners need separately fitted smooth spans.":"Two source endpoints define an exact line or a directed biarc.";
        cancel();var budget=new ModelInventoryBudget(cancel);
        var fitted=SmoothPolylineGeometry409.Fit(spec.SourcePoints,spec.FitTolerance,spec.Closed,spec.CornerIndices,spec.StartTangent,spec.EndTangent,cancel,s=>budget.Add(s));
        var result=new Polyline(fitted.Segments.Length+(spec.Closed?0:1));
        try
        {
            for(int i=0;i<fitted.Segments.Length;i++)
            {cancel();var s=fitted.Segments[i];result.AddVertexAt(i,new Point2d(s.Start.X,s.Start.Y),s.Bulge,0,0);}
            if(!spec.Closed){var last=fitted.Segments[^1].End;result.AddVertexAt(fitted.Segments.Length,new Point2d(last.X,last.Y),0,0,0);}
            result.Closed=spec.Closed;
            var actual=ReadLocal(result,spec,fitted.CornerVertexIndices,cancel,false);
            _=SmoothPolylineGeometry409.Observe(actual,spec.SourcePoints,spec.FitTolerance,false,cancel);
            SmoothPolylineGeometry409.RequireEndpointDirections(actual,spec.StartTangent,spec.EndTangent);
            Origins.Add(result,new("deterministic_tangent_biarcs",rejected,fitted.CornerVertexIndices));return result;
        }
        catch{result.Dispose();throw;}
    }

    // Call after the ordinary creation-frame transform, before the first
    // resident append. Include this report in the common complete-result byte
    // admission. After append, call again for actual native shape verification.
    internal static SmoothPolylineFit409 VerifyCreated(Polyline polyline,SmoothPolylineSpec409 spec,int entityIndex,Action cancel)
    {
        cancel();Need(Origins.TryGetValue(polyline,out var origin),"Smooth creation provenance is unavailable for this result.");
        var plan=ReadLocal(polyline,spec,origin!.CornerVertices,cancel,true);
        var observed=SmoothPolylineGeometry409.Observe(plan,spec.SourcePoints,spec.FitTolerance,spec.IncludeSampleDetails,cancel);
        SmoothPolylineGeometry409.RequireEndpointDirections(plan,spec.StartTangent,spec.EndTangent);
        string hash=Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(new
            {source_points=spec.SourcePoints,workplane=spec.Workplane??DrawingWorkplane.XY,closed=spec.Closed,corner_indices=spec.CornerIndices},Wire.Json)));
        var report=new SmoothPolylineFit409(entityIndex,origin.Method,"ordered_request_points",hash,spec.SourcePoints.Length,spec.FitTolerance,
            "ordered_correspondence_witness",observed.Resolution,observed.MaximumDistance,observed.RmsDistance,observed.LargestChord,observed.MaximumJoinAngle,observed.LineCount,observed.ArcCount,
            spec.Closed,spec.CornerIndices,origin.CornerVertices,false,"source_points local to declared workplane; returned entity geometry is WCS",origin.NativeRejection,observed.Samples);
        SerializedCapacity357.Check(report,Wire.PayloadBudget-16384,cancel);return report;
    }
}
