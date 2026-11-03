using NanoCAD.Contracts;
using Teigha.DatabaseServices;
using Teigha.Geometry;
using Brep=Teigha.BoundaryRepresentation.Brep;
using Face=Teigha.BoundaryRepresentation.Face;
using BoundaryLoop=Teigha.BoundaryRepresentation.BoundaryLoop;
using static NanoCADBridge.CadHatchBoundaryState349;
using Geometry=NanoCADBridge.CadHatchAssociationGeometry362;

namespace NanoCADBridge;

// Native finite curve intersections and exact B-rep containment prove topology.
// Only detached curve copies/Regions are consumed. Resident sources stay intact.
internal static class CadHatchAssociationTopology362
{
    // Installed hostdbmgd PE: Polyline.GetSegmentType returns the native
    // SegmentType enum, Line=0 and Arc=1 (segment-type362.json). Explicit
    // native values avoid the compiler's unavailable enum field names; no
    // other segment kind is admitted or inferred from a bulge.
    const Teigha.DatabaseServices.SegmentType NativeLineSegment=(Teigha.DatabaseServices.SegmentType)0,
        NativeArcSegment=(Teigha.DatabaseServices.SegmentType)1;
    sealed class Boundary : IDisposable
    {
        internal readonly Curve[] Sources;
        internal readonly List<Curve3d> Segments=[];
        internal readonly Geometry.Boundary Geometry;
        internal readonly Point3d Witness;
        internal Boundary(Curve source,Geometry.Boundary geometry,Point3d witness):this([source],geometry,witness){}
        internal Boundary(Curve[] sources,Geometry.Boundary geometry,Point3d witness){Sources=sources;Geometry=geometry;Witness=witness;}
        public void Dispose(){foreach(var curve in Segments.Distinct<Curve3d>(ReferenceEqualityComparer.Instance))curve.Dispose();}
    }
    static double Number(double value)
    {Need(double.IsFinite(value)&&Math.Abs(value)<=1e9,"INVALID_GEOMETRY","Boundary geometry must be finite within +/-1e9.");return value;}
    static bool Plane(double actual,double expected)=>double.IsFinite(actual)&&Math.Abs(actual-expected)<=1e-8;
    static Point2d Ocs(Point3d value,Matrix3d transform)
    {var point=value.TransformBy(transform);return new(Number(point.X),Number(point.Y));}
    static Vector2d Axis(Vector3d value,Matrix3d transform)
    {var vector=value.TransformBy(transform);Number(vector.X);Number(vector.Y);Need(vector.Length>0,"INVALID_GEOMETRY","Native ellipse axis is degenerate.");return new Vector2d(vector.X,vector.Y).GetNormal();}
    static void Range(Curve3d curve)
    {
        using var interval=curve.GetInterval();Need(interval.IsBounded&&double.IsFinite(interval.LowerBound)&&double.IsFinite(interval.UpperBound)&&interval.UpperBound>interval.LowerBound,
            "INVALID_GEOMETRY","Boundary needs finite increasing native curve intervals.");
        double length=curve.GetLength(interval.LowerBound,interval.UpperBound,Tolerance.Global.EqualPoint);
        Need(double.IsFinite(length)&&length>Tolerance.Global.EqualPoint,"INVALID_GEOMETRY","Native boundary segment has no finite physical length.");
    }
    static Boundary Capture(Entity entity,Hatch hatch,Action cancel,ModelInventoryBudget budget,IReadOnlyDictionary<Entity,Entity>? observationSources=null)
    {
        Need(entity is Curve&&!entity.HasFields,"UNSUPPORTED_HATCH_BOUNDARY","Native curve fields require their owning adapter.");
        var source=(Curve)entity;Entity identity=source;
        if(observationSources!=null&&observationSources.TryGetValue(entity,out var original))
        {
            // The proxy represents a planned exact line/arc observation. It
            // never receives a resident ObjectId/handle; provenance is the
            // caller's actual borrowed source, checked in this Hatch context.
            Need(entity.GetType()==typeof(Polyline)&&entity.ObjectId.IsNull&&entity.OwnerId.IsNull&&entity.Database==null&&
                original.GetType()==typeof(Polyline2d)&&!original.ObjectId.IsNull&&original.ObjectId.IsValid&&
                !original.ObjectId.IsErased&&!original.ObjectId.IsEffectivelyErased&&original.Database==hatch.Database&&original.OwnerId==hatch.OwnerId&&
                original.UnmanagedObject!=entity.UnmanagedObject,
                "INVALID_SOURCE_PROVENANCE","Only a detached exact Polyline observation of an actual same-space native Polyline2d may supply provenance.");
            identity=original;
        }
        string handle=Validation.NormalizeHandle(identity.Handle.ToString());var transform=Matrix3d.WorldToPlane(hatch.Normal);
        Boundary? result=null;
        try
        {
            if(source.GetType()==typeof(Polyline)&&source is Polyline polyline)
            {
                Need(polyline.Closed&&polyline.NumberOfVertices>=2&&polyline.Thickness==0&&polyline.ConstantWidth==0&&polyline.Normal==hatch.Normal&&Plane(polyline.Elevation,hatch.Elevation),
                    "UNSUPPORTED_HATCH_BOUNDARY","Use a closed zero-width native Polyline in the Hatch's exact retained OCS plane.");
                var vertices=new List<Geometry.Vertex>();var edges=new List<Geometry.Edge>();
                result=new(source,new(handle,"Polyline",[],[]),source.StartPoint);
                for(int index=0;index<polyline.NumberOfVertices;index++)
                {
                    cancel();Need(polyline.GetStartWidthAt(index)==0&&polyline.GetEndWidthAt(index)==0,"UNSUPPORTED_HATCH_BOUNDARY","Wide Polyline source segments require their adapter.");
                    var point=polyline.GetPoint2dAt(index);var vertex=new Geometry.Vertex(new(Number(point.X),Number(point.Y)),CadHatchEditingReader.Number(polyline.GetBulgeAt(index)));
                    budget.Add(vertex);vertices.Add(vertex);var type=polyline.GetSegmentType(index);
                    Need(type is NativeLineSegment or NativeArcSegment,"INVALID_GEOMETRY","Closed Polyline contains a coincident/empty/point segment.");
                    Curve3d segment=type==NativeLineSegment?polyline.GetLineSegmentAt(index):polyline.GetArcSegmentAt(index);
                    result.Segments.Add(segment);Range(segment);
                    using Curve2d planar=type==NativeLineSegment?polyline.GetLineSegment2dAt(index):polyline.GetArcSegment2dAt(index);
                    var edge=Geometry.Capture(planar);budget.Add(edge);edges.Add(edge);
                }
                var completed=new Boundary(source,new(handle,"Polyline",vertices.ToArray(),edges.ToArray()),source.StartPoint);
                completed.Segments.AddRange(result.Segments);result.Segments.Clear();result.Dispose();result=completed;
            }
            else if(source.GetType()==typeof(Circle)&&source is Circle circle)
            {
                var center=circle.Center.TransformBy(transform);
                Need(circle.Thickness==0&&circle.Normal==hatch.Normal&&Plane(center.Z,hatch.Elevation)&&circle.Radius>0,
                    "UNSUPPORTED_HATCH_BOUNDARY","Use a positive-radius zero-thickness Circle in the retained native Hatch plane.");
                var edge=new Geometry.Edge("circle",Ocs(circle.StartPoint,transform),Ocs(circle.EndPoint,transform),new(Number(center.X),Number(center.Y)),Number(circle.Radius),0,default,default,2*Math.PI,false);
                result=new(source,new(handle,"Circle",[],[edge]),source.StartPoint);result.Segments.Add(circle.GetGeCurve());Range(result.Segments[0]);
            }
            else if(source.GetType()==typeof(Ellipse)&&source is Ellipse ellipse)
            {
                _=CadEllipseReader.Read(ellipse,cancel);var center=ellipse.Center.TransformBy(transform);
                Need(ellipse.Closed&&ellipse.Normal==hatch.Normal&&Plane(center.Z,hatch.Elevation)&&
                    Math.Abs(ellipse.EndParam-ellipse.StartParam-2*Math.PI)<=Tolerance.Global.EqualVector,
                    "UNSUPPORTED_HATCH_BOUNDARY","Use a complete native Ellipse in the retained Hatch plane; elliptical arcs are not closed sources.");
                var edge=new Geometry.Edge("ellipse",Ocs(ellipse.StartPoint,transform),Ocs(ellipse.EndPoint,transform),new(Number(center.X),Number(center.Y)),
                    Number(ellipse.MajorRadius),Number(ellipse.MinorRadius),Axis(ellipse.MajorAxis,transform),Axis(ellipse.MinorAxis,transform),2*Math.PI,false);
                result=new(source,new(handle,"Ellipse",[],[edge]),source.StartPoint);result.Segments.Add(ellipse.GetGeCurve());Range(result.Segments[0]);
            }
            else if(source.GetType()==typeof(Spline)&&source is Spline spline)
            {
                var definition=CadHatchSpline443.Capture(spline,hatch,cancel);
                result=new(source,new(handle,"Spline",[],[],Spline443:definition),source.StartPoint);
                result.Segments.Add(spline.GetGeCurve());Range(result.Segments[0]);
            }
            else throw new DomainException("UNSUPPORTED_HATCH_BOUNDARY","Rebind supports closed zero-width native Polyline (Line/Arc bulges), Circle, full Ellipse or one closed native Spline in WCS XY/Z0; other curves need their adapter.");
            budget.Add(result.Geometry);return result;
        }
        catch{result?.Dispose();throw;}
    }
    static bool Same(Point3d first,Point3d second)=>first.DistanceTo(second)<=Tolerance.Global.EqualPoint;
    static Geometry.Edge[] Orient(Geometry.Edge[] raw,Action cancel)
    {
        foreach(bool reverseFirst in new[]{false,true})
        {
            var edges=new Geometry.Edge[raw.Length];edges[0]=reverseFirst?Geometry.Reverse(raw[0]):raw[0];bool connected=true;
            for(int index=1;index<raw.Length;index++)
            {
                cancel();var edge=raw[index];var end=edges[index-1].End;
                if(end.GetDistanceTo(edge.Start)<=Tolerance.Global.EqualPoint)edges[index]=edge;
                else if(end.GetDistanceTo(edge.End)<=Tolerance.Global.EqualPoint)edges[index]=Geometry.Reverse(edge);
                else{connected=false;break;}
            }
            if(connected&&edges[^1].End.GetDistanceTo(edges[0].Start)<=Tolerance.Global.EqualPoint)return edges;
        }
        throw new DomainException("INVALID_HATCH_TOPOLOGY","Supply each Line/Arc group in connected contour order, with a closed final endpoint; native source direction may differ.");
    }
    static Boundary CaptureGroup(Entity[] sources,Hatch hatch,Action cancel,ModelInventoryBudget budget,IReadOnlyDictionary<Entity,Entity>? observationSources=null)
    {
        Need(sources.Length>0,"INVALID_HATCH_TOPOLOGY","Each native outer/hole source group must be nonempty.");
        if(sources.Length==1)return Capture(sources[0],hatch,cancel,budget,observationSources);
        var transform=Matrix3d.WorldToPlane(hatch.Normal);var curves=new List<Curve>();var edges=new List<Geometry.Edge>();var handles=new List<string>();
        Boundary? result=null;
        try
        {
            foreach(var entity in sources)
            {
                cancel();Need(entity is Curve&&!entity.HasFields,"UNSUPPORTED_HATCH_BOUNDARY","Boundary groups require ordinary native curves without fields.");
                var curve=(Curve)entity;curves.Add(curve);handles.Add(Validation.NormalizeHandle(curve.Handle.ToString()));
            }
            result=new(curves.ToArray(),new(handles[0],"Line_Arc_chain",[],[],handles.ToArray()),curves[0].StartPoint);
            foreach(var source in curves)
            {
                cancel();Geometry.Edge edge;var start=source.StartPoint.TransformBy(transform);var end=source.EndPoint.TransformBy(transform);
                Need(source.GetType()==typeof(Line)||source.GetType()==typeof(Arc),"UNSUPPORTED_HATCH_BOUNDARY",
                    "A multi-source boundary group supports native Line/Arc chains. Closed Polyline/Circle/full Ellipse/Spline use one source per group.");
                Need(Plane(start.Z,hatch.Elevation)&&Plane(end.Z,hatch.Elevation)&&start.DistanceTo(end)>Tolerance.Global.EqualPoint,
                    "INVALID_HATCH_TOPOLOGY","Each finite Line/Arc group member must lie in the retained Hatch plane and have distinct endpoints.");
                var ge=source.GetGeCurve();result.Segments.Add(ge);Range(ge);
                if(source.GetType()==typeof(Line)&&source is Line line)
                {
                    Need(line.Thickness==0,"UNSUPPORTED_HATCH_BOUNDARY","Grouped Line boundaries must have zero thickness.");
                    edge=new("line",new(Number(start.X),Number(start.Y)),new(Number(end.X),Number(end.Y)),default,0,0,default,default,0,false);
                }
                else if(source.GetType()==typeof(Arc)&&source is Arc arc)
                {
                    var center=arc.Center.TransformBy(transform);
                    Need(arc.Thickness==0&&arc.Normal.Length>0&&arc.Normal.GetNormal().CrossProduct(hatch.Normal.GetNormal()).Length<=Tolerance.Global.EqualVector&&
                        Plane(center.Z,hatch.Elevation)&&arc.Radius>Tolerance.Global.EqualPoint&&ge.GetType()==typeof(CircularArc3d),
                        "UNSUPPORTED_HATCH_BOUNDARY","Grouped Arc boundaries need zero thickness in the retained native plane; either normal direction is allowed.");
                    using var interval=ge.GetInterval();double sweep=Number(interval.UpperBound-interval.LowerBound);
                    Need(sweep>0&&sweep<2*Math.PI,"INVALID_HATCH_TOPOLOGY","Each grouped Arc must have a finite partial-circle native span.");
                    // The native derivative determines orientation in retained
                    // OCS; no transposed NRX angle/direction semantics.
                    var tangent=arc.GetFirstDerivative(arc.StartParam).TransformBy(transform);
                    double turn=CadHatchEditingReader.Number((start.X-center.X)*tangent.Y-(start.Y-center.Y)*tangent.X);
                    Need(turn!=0,"INVALID_GEOMETRY","Native Arc tangent does not define a nondegenerate planar direction.");
                    edge=new("circle",new(Number(start.X),Number(start.Y)),new(Number(end.X),Number(end.Y)),new(Number(center.X),Number(center.Y)),Number(arc.Radius),0,default,default,sweep,turn<0);
                }
                else throw new DomainException("UNSUPPORTED_HATCH_BOUNDARY","A multi-source boundary group supports native Line/Arc chains. Closed Polyline/Circle/full Ellipse/Spline use one source per group.");
                budget.Add(edge);edges.Add(edge);
            }
            var ordered=Orient(edges.ToArray(),cancel);var completed=new Boundary(curves.ToArray(),new(handles[0],"Line_Arc_chain",[],ordered,handles.ToArray()),curves[0].StartPoint);
            completed.Segments.AddRange(result.Segments);result.Segments.Clear();result.Dispose();result=completed;budget.Add(result.Geometry);return result;
        }
        catch{result?.Dispose();throw;}
    }
    static Point3d Endpoint(Curve3d curve,bool end)
    {using var interval=curve.GetInterval();return curve.EvaluatePoint(end?interval.UpperBound:interval.LowerBound);}
    static bool SharedEndpoint(Boundary boundary,int first,int second,Point3d point)
    {
        int count=boundary.Segments.Count;
        if(second!=first+1&&!(first==0&&second==count-1))return false;
        bool OnEnd(Curve3d curve)=>Same(point,Endpoint(curve,false))||Same(point,Endpoint(curve,true));
        return OnEnd(boundary.Segments[first])&&OnEnd(boundary.Segments[second]);
    }
    static void Relate(Boundary first,int a,Boundary second,int b,Vector3d normal,bool self,Action cancel)
    {
        cancel();var curveA=first.Segments[a];var curveB=second.Segments[b];using var rangeA=curveA.GetInterval();using var rangeB=curveB.GetInterval();
        using var relation=new CurveCurveIntersector3d(curveA,curveB,rangeA,rangeB,normal);cancel();
        int intersections=relation.NumberOfIntersectionPoints,overlaps=relation.OverlapCount();
        Need(intersections>=0&&overlaps>=0,"NATIVE_HATCH_TOPOLOGY_MISMATCH","Native curve relation inventory is invalid.");
        Need(overlaps==0,"INVALID_HATCH_TOPOLOGY","Hatch sources must not contain overlapping native segments.");
        for(int index=0;index<intersections;index++)
        {cancel();var point=relation.GetIntersectionPoint(index);Need(self&&SharedEndpoint(first,a,b,point),"INVALID_HATCH_TOPOLOGY","Native Hatch boundaries cross or touch outside adjacent closed-loop endpoints.");}
    }
    static string Contains(Boundary boundary,Point3d point,Action cancel)
    {
        cancel();var clones=new List<DBObject>();
        try
        {
        using var curves=new DBObjectCollection();
        foreach(var source in boundary.Sources)
        {
            cancel();var copied=source is Spline spline?CadHatchSpline443.Detached(spline,cancel):source.Clone() as DBObject;
            Need(copied!=null&&!ReferenceEquals(copied,source)&&copied.UnmanagedObject!=source.UnmanagedObject&&copied.ObjectId.IsNull&&copied.OwnerId.IsNull,
                "NATIVE_HATCH_TOPOLOGY_MISMATCH","Topology requires its own detached native curve copies; unsafe aliases were not disposed.");
            clones.Add(copied!);Need(copied is Curve&&copied.GetType()==source.GetType(),"NATIVE_HATCH_TOPOLOGY_MISMATCH","Temporary native curve clone type differs from its source.");curves.Add(copied!);
        }
        using var regions=Region.CreateFromCurves(curves);
        try
        {
            cancel();Need(regions.Count==1&&regions[0] is Region&&regions[0].ObjectId.IsNull,"INVALID_HATCH_TOPOLOGY","Each closed boundary must produce exactly one detached native Region.");
            using var brep=new Brep((Region)regions[0]);Need(!brep.IsNull&&brep.CheckEntity(),"INVALID_HATCH_TOPOLOGY","Temporary boundary Region failed native B-rep validity.");
            int faces=0;string? classification=null;
            foreach(Face face in brep.Faces)using(face)
            {
                cancel();Need(++faces==1&&!face.IsNull,"INVALID_HATCH_TOPOLOGY","Closed source must have exactly one native Region face.");int loops=0;
                foreach(BoundaryLoop loop in face.Loops)using(loop){cancel();Need(++loops==1&&!loop.IsNull,"INVALID_HATCH_TOPOLOGY","Source must define one simple native boundary without hidden loops.");}
                Need(loops==1,"INVALID_HATCH_TOPOLOGY","Temporary native face has no boundary loop.");
                using var container=face.GetPointContainment(point,out var native);cancel();classification=NanoCAD.Contracts.PointContainment.Classify(native.ToString(),container==null||container.IsNull);
            }
            Need(faces==1&&classification!=null,"INVALID_HATCH_TOPOLOGY","Temporary native boundary has no valid face.");return classification!;
        }
        finally{foreach(DBObject region in regions)region.Dispose();}
        }
        finally{foreach(var clone in clones)clone.Dispose();}
    }
    internal static Geometry.Boundary[] Validate(Entity[][] groups,Hatch hatch,Action cancel,IReadOnlyDictionary<Entity,Entity>? observationSources=null)
    {
        var boundaries=new List<Boundary>();var budget=new ModelInventoryBudget(cancel);
        try
        {
            foreach(var group in groups){cancel();boundaries.Add(CaptureGroup(group,hatch,cancel,budget,observationSources));}
            for(int i=0;i<boundaries.Count;i++)
            {
                var boundary=boundaries[i];
                for(int a=0;a<boundary.Segments.Count;a++)for(int b=a+1;b<boundary.Segments.Count;b++)Relate(boundary,a,boundary,b,hatch.Normal,true,cancel);
                // Also certify the complete loop when there are no holes. This
                // witness lies on the source itself and must be native boundary.
                Need(Contains(boundary,boundary.Witness,cancel)=="boundary","NATIVE_HATCH_TOPOLOGY_MISMATCH","Native source witness is not on its temporary Region boundary.");
                for(int j=i+1;j<boundaries.Count;j++)
                    for(int a=0;a<boundary.Segments.Count;a++)for(int b=0;b<boundaries[j].Segments.Count;b++)Relate(boundary,a,boundaries[j],b,hatch.Normal,false,cancel);
            }
            for(int i=1;i<boundaries.Count;i++)
            {
                Need(Contains(boundaries[0],boundaries[i].Witness,cancel)=="inside","INVALID_HATCH_TOPOLOGY","Every hole must lie strictly inside the explicit native outer boundary.");
                for(int j=1;j<i;j++)Need(Contains(boundaries[j],boundaries[i].Witness,cancel)=="outside"&&Contains(boundaries[i],boundaries[j].Witness,cancel)=="outside",
                    "INVALID_HATCH_TOPOLOGY","Native holes must remain separate without nesting.");
            }
            return boundaries.Select(boundary=>boundary.Geometry).ToArray();
        }
        finally{foreach(var boundary in boundaries)boundary.Dispose();}
    }
}
