using System.Security.Cryptography;
using System.Text.Json;
using Multicad;
using Multicad.Mc3D;
using Multicad.DatabaseServices.Constraints;
using NanoCAD.Contracts;
using G=Multicad.Geometry;
using Db=Teigha.DatabaseServices;

namespace NanoCADBridge;

// Targeted actual native areas, not a parent-sketch approximation. No edit
// mode, constraints solve, factory or document scan occurs in this reader.
internal static class ModelProfileRegionsReader350
{
    internal sealed record Snapshot(SketchProfile Profile,PlanarSketch Sketch,SketchPlaneSpec Plane,JsonElement SketchGeometry,
        ModelCircleLoop350[] Loops,ModelCircleRegion350[] Regions,ModelProfileDiscovery348.ProjectionState Projections,
        JsonElement Constraints,JsonElement Stored,string Token);
    static void Need([System.Diagnostics.CodeAnalysis.DoesNotReturnIf(false)] bool ok,string code,string message)
    {if(!ok)throw new DomainException(code,message);}
    static Point3 P(JsonElement point)=>new(point[0].GetDouble(),point[1].GetDouble(),point[2].GetDouble());
    static double Dot(Point3 a,Point3 b)=>a.X*b.X+a.Y*b.Y+a.Z*b.Z;
    static bool Near(double a,double b)=>Math.Abs(a-b)<=1e-7;
    static bool Near(Point2 a,Point2 b)=>Near(a.X,b.X)&&Near(a.Y,b.Y);
    internal static ModelCircleLoop350[] SourceKeys(ModelCircleLoop350[] loops)=>loops.Select(loop=>loop with{element_id=loop.source_object_id}).ToArray();
    internal static ModelCircleRegion350[] SourceKeys(ModelCircleRegion350[] regions,ModelCircleLoop350[] loops)
    {
        var keys=loops.ToDictionary(loop=>loop.element_id,loop=>loop.source_object_id,StringComparer.OrdinalIgnoreCase);
        return regions.Select(region=>new ModelCircleRegion350(keys[region.outer_element_id],region.hole_element_ids.Select(id=>keys[id]).OrderBy(id=>id,StringComparer.OrdinalIgnoreCase).ToArray()))
            .OrderBy(region=>region.outer_element_id,StringComparer.OrdinalIgnoreCase).ToArray();
    }
    static JsonElement ConstraintState(Mc3dSolid body,PlanarSketch sketch,Action cancel)=>ModelSketchConstraintObservation353.Read(body,sketch,cancel);
    internal static string CircleLoop(G.Polyline3d loop,ModelCircleLoop350[] candidates,SketchPlaneSpec plane,Action cancel)
    {
        Need(loop!=null&&loop.IsClosed(),"PROFILE_GEOMETRY_UNAVAILABLE","A native selected profile loop is absent or open.");
        using var segments=loop.Segments;Need(segments.Count>0,"PROFILE_GEOMETRY_UNAVAILABLE","A native selected loop is empty.");
        ModelCircleLoop350? matched=null;Point2? first=null,last=null;double total=0;int direction=0;var normal=DrawingWorkplane.Normal(plane);
        for(uint i=0;i<segments.Count;i++)
        {
            cancel();Need(segments.SegType(i)==G.SegPlType.sgArc,"OUT_OF_SCOPE","This region topology adapter reads complete whole Circle loops only.");
            using var arc=segments.GetArcSegAt(i);var center=DrawingWorkplane.Local(plane,new(arc.Center.X,arc.Center.Y,arc.Center.Z));
            double dot=Dot(normal,new(arc.Normal.X,arc.Normal.Y,arc.Normal.Z)),delta=arc.EndAng-arc.StartAng;
            Need(new[]{arc.Radius,dot,delta}.All(double.IsFinite)&&Near(Math.Abs(dot),1)&&Math.Abs(delta)>1e-10&&Math.Abs(delta)<=2*Math.PI+1e-7,
                "INVALID_GEOMETRY","Native circle-loop arc has unavailable plane/orientation/sweep.");
            var match=candidates.SingleOrDefault(candidate=>Near(candidate.center,center)&&Near(candidate.radius,arc.Radius));
            Need(match!=null&&(matched==null||matched.element_id==match.element_id),"PROFILE_GEOMETRY_MISMATCH","The selected native loop does not match one exact retained Circle.");
            var start=arc.StartPoint;var end=arc.EndPoint;
            var from=DrawingWorkplane.Local(plane,new(start.X,start.Y,start.Z));var to=DrawingWorkplane.Local(plane,new(end.X,end.Y,end.Z));
            Need(last==null||Near(last,from),"INVALID_GEOMETRY","Native selected Circle segments are disconnected or overlap out of sequence.");
            first??=from;last=to;
            int sign=Math.Sign(dot*delta);Need(direction==0||direction==sign,"INVALID_GEOMETRY","A native loop retraces or reverses its Circle boundary.");
            direction=sign;total+=Math.Abs(delta);matched=match;
        }
        Need(matched!=null&&first!=null&&last!=null&&Near(first,last)&&Near(total,2*Math.PI),"PROFILE_GEOMETRY_MISMATCH","The native selected boundary is not one complete retained Circle.");return matched.element_id;
    }
    static ModelCircleRegion350[] Regions(SketchProfile profile,ModelCircleLoop350[] loops,SketchPlaneSpec plane,Dictionary<string,string?> parents,Action cancel)
    {
        cancel();var areas=profile.GetContours(false);Need(areas!=null&&areas.Count>0,"PROFILE_GEOMETRY_UNAVAILABLE","Selected native closed areas are unavailable.");
        var values=new List<ModelCircleRegion350>();var bytes=new ModelInventoryBudget(cancel);
        try
        {
            foreach(var area in areas)
            {
                cancel();Need(area!=null,"PROFILE_GEOMETRY_UNAVAILABLE","A native selected area is unavailable.");using var outer=area.External;
                string outside=CircleLoop(outer,loops,plane,cancel);var holes=area.Internal;
                Need(holes!=null,"PROFILE_GEOMETRY_UNAVAILABLE","Native selected hole inventory is unavailable.");var inside=new List<string>();
                try{foreach(var hole in holes){cancel();Need(hole!=null,"PROFILE_GEOMETRY_UNAVAILABLE","A native selected hole is unavailable.");inside.Add(CircleLoop(hole,loops,plane,cancel));}}
                finally{foreach(var hole in holes.Distinct(ReferenceEqualityComparer.Instance))if(hole is IDisposable disposable)disposable.Dispose();}
                var item=new ModelCircleRegion350(outside,inside.OrderBy(id=>id,StringComparer.OrdinalIgnoreCase).ToArray());bytes.Add(item);values.Add(item);
            }
        }
        finally{foreach(var area in areas.Distinct(ReferenceEqualityComparer.Instance))if(area is IDisposable disposable)disposable.Dispose();}
        Need(values.Select(region=>region.outer_element_id).Distinct().Count()==values.Count,"INVALID_GEOMETRY","Native selected areas repeat one outer Circle.");
        foreach(var region in values)
        {
            cancel();var expected=parents.Where(pair=>pair.Value==region.outer_element_id).Select(pair=>pair.Key).OrderBy(id=>id,StringComparer.OrdinalIgnoreCase).ToArray();
            Need(expected.SequenceEqual(region.hole_element_ids,StringComparer.OrdinalIgnoreCase),"OUT_OF_SCOPE","A selected native region is not an atomic cell in the retained Circle arrangement.");
            Need(!values.Any(other=>parents[other.outer_element_id]==region.outer_element_id),"OUT_OF_SCOPE","Adjacent selected cells need a merged-boundary adapter.");
        }
        return values.OrderBy(region=>region.outer_element_id,StringComparer.OrdinalIgnoreCase).ToArray();
    }
    internal static Snapshot Capture(Mc3dSolid body,SketchProfile profile,DocumentState state,Db.Transaction tx,Action cancel)
    {
        var owner=ModelProfileDiscovery348.OwnerDocument(body,state);var exact=ModelProfileDiscovery348.Resolve(profile.ID.ToString(),owner,cancel);
        var ids=ModelInventoryBudget.History(body,cancel).ToArray();var sketch=exact.ParentSketchID.GetObjectOfType<PlanarSketch>();
        Need(sketch!=null&&sketch.ID==exact.ParentSketchID&&ids.Contains(profile.ID)&&ids.Contains(sketch.ID)&&
            profile.HostSolidID==body.ID&&sketch.HostSolidID==body.ID&&sketch.DbEntity.Document!=null&&sketch.DbEntity.Document.ID==owner.ID&&!sketch.IsInEditMode,
            "PROFILE_OWNERSHIP_MISMATCH","Region editing requires the exact consumed profile and existing parent sketch in actual body composition.");
        Need(profile.Status.ToString()=="Ok"&&sketch.Status.ToString()=="Ok"&&!profile.IsSuppressed&&!sketch.IsSuppressed,
            "MODEL_STATE_UNAVAILABLE","Native selected profile or sketch is unhealthy.");
        var budget=new ModelInventoryBudget(cancel);var geometry=Wire.Element(SketchGeometryReader.Read(sketch,cancel,budget.Add));
        var frame=geometry.GetProperty("frame");var plane=new SketchPlaneSpec(P(frame.GetProperty("origin_wcs")),P(frame.GetProperty("x_step_wcs")),P(frame.GetProperty("y_step_wcs")));
        Need(Near(Dot(plane.XAxis,plane.XAxis),1)&&Near(Dot(plane.YAxis,plane.YAxis),1)&&Near(Dot(plane.XAxis,plane.YAxis),0),"SKETCH_FRAME_UNAVAILABLE","Use the reported perpendicular unit native sketch frame.");
        using(var nativePlane=profile.Plane)
        {
            var normal=DrawingWorkplane.Normal(plane);Need(nativePlane!=null&&Math.Abs(nativePlane.SignedDistanceTo(new G.Point3d(plane.Origin.X,plane.Origin.Y,plane.Origin.Z)))<1e-7&&
                Near(Dot(normal,new(nativePlane.Normal.X,nativePlane.Normal.Y,nativePlane.Normal.Z)),1),"PROFILE_PLANE_MISMATCH","Native selected profile and parent oriented planes differ.");
        }
        var projections=ModelProfileDiscovery348.Projections(sketch,plane,state,tx,cancel,ids);var sourceByProjection=projections.Sources
            .SelectMany(source=>source.projection_ids.Select(id=>(Id:id,Source:source))).ToDictionary(pair=>pair.Id,pair=>pair.Source,StringComparer.OrdinalIgnoreCase);
        var loops=new List<ModelCircleLoop350>();
        foreach(var element in geometry.GetProperty("elements").EnumerateArray())
        {
            cancel();string id=element.GetProperty("element_id").GetString()!;var g=element.GetProperty("geometry");
            bool hasSource=sourceByProjection.TryGetValue(id,out var source);
            Need(element.GetProperty("status").GetString()=="read"&&g.GetProperty("kind").GetString()=="circular_arc"&&
                Near(Math.Abs(g.GetProperty("end_angle").GetDouble()-g.GetProperty("start_angle").GetDouble()),2*Math.PI)&&hasSource,
                "OUT_OF_SCOPE","Each retained sketch element must be one complete Circle with an exact native source projection.");
            var raw=(Db.Entity)tx.GetObject(CadStructureWriter.Find(state.Document.Database,source!.handle),Db.OpenMode.ForRead);
            Need(raw.GetType()==typeof(Db.Circle),"OUT_OF_SCOPE","The whole-circle element requires an exact ordinary Circle drawing source.");var circle=(Db.Circle)raw;
            var center=DrawingWorkplane.Local(plane,P(g.GetProperty("center")));double radius=g.GetProperty("radius").GetDouble();
            var nativeNormal=P(g.GetProperty("normal"));Need(Near(Math.Abs(Dot(DrawingWorkplane.Normal(plane),nativeNormal)),1)&&
                Near(center,DrawingWorkplane.Local(plane,new(circle.Center.X,circle.Center.Y,circle.Center.Z)))&&Near(radius,circle.Radius),
                "PROFILE_GEOMETRY_MISMATCH","Native sketch Circle differs from its exact retained drawing source.");
            var item=new ModelCircleLoop350(id,center,radius,source.id);budget.Add(item);loops.Add(item);
        }
        Need(loops.Count==projections.Ids.Length&&loops.Select(loop=>loop.source_object_id).Distinct(StringComparer.OrdinalIgnoreCase).Count()==loops.Count,
            "OUT_OF_SCOPE","Use one exact retained projection per Circle source; construction extras/shared sources need their topology adapter.");
        var parents=ModelCircleRegions350.Parents(loops.ToArray(),cancel);
        var regions=Regions(profile,loops.ToArray(),plane,parents,cancel);var constraints=ConstraintState(body,sketch,cancel);
        var depends=profile.GetDependsOn(false,false);var sketchDepends=sketch.GetDependsOn(false,false);
        Need(depends!=null&&sketchDepends!=null,"HISTORY_UNAVAILABLE","Native profile/sketch dependencies are unavailable.");ModelInventoryBudget.Ids(depends,cancel);ModelInventoryBudget.Ids(sketchDepends,cancel);
        var stored=Wire.Element(new{owner_document_id=owner.ID.ToString(),profile_id=profile.ID.ToString(),sketch_id=sketch.ID.ToString(),profile_name=profile.Name,sketch_name=sketch.Name,
            support_id=ModelProfileDiscovery348.Id(sketch.PlanarEntityID),profile_host_solid_id=profile.HostSolidID.ToString(),sketch_host_solid_id=sketch.HostSolidID.ToString(),
            dependent_feature_id=ModelProfileDiscovery348.Id(profile.Dependent3dFeatureID),geometry,loops,regions,constraints,projection_bindings=projections.Bindings,
            dependencies=depends.Select(id=>id.ToString()).OrderBy(id=>id,StringComparer.OrdinalIgnoreCase).ToArray(),
            sketch_dependencies=sketchDepends.Select(id=>id.ToString()).OrderBy(id=>id,StringComparer.OrdinalIgnoreCase).ToArray()});budget.Add(stored);
        return new(profile,sketch,plane,geometry,loops.ToArray(),regions,projections,constraints,stored,Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(stored,Wire.Json))));
    }
    internal static ModelProfileRegionsRead350 Describe(Snapshot value,ExtrudeFeature feature)
    {
        static double[] V(Point3 point)=>[point.X,point.Y,point.Z];return new(value.Profile.ID.ToString(),value.Sketch.ID.ToString(),value.Profile.DbEntity.Document.ID.ToString(),feature.ID.ToString(),value.Token,
            new(V(value.Plane.Origin),V(value.Plane.XAxis),V(value.Plane.YAxis),V(DrawingWorkplane.Normal(value.Plane))),value.Loops,value.Regions,
            "actual_native_selected_circle_areas_and_hole_loops_centers_in_reported_native_sketch_XY_not_boundary_indices",value.Constraints);
    }
}
