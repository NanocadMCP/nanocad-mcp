using System.Diagnostics.CodeAnalysis;
using System.Security.Cryptography;
using System.Text.Json;
using Multicad;
using Multicad.Mc3D;
using G=Multicad.Geometry;
using NanoCAD.Contracts;
using Db=Teigha.DatabaseServices;

namespace NanoCADBridge;

// Independent of ModelHistoryReader/ModelParameterSnapshot: optional history
// metadata can call this without recursive reader/token capture.
internal static class ModelProfileLifecycleState340
{
    internal sealed record Snapshot(SketchProfile Profile,PlanarSketch Sketch,JsonElement Geometry,
        ModelProfileSpec? Contour,SketchPlaneSpec Plane,JsonElement ProfileGeometry,JsonElement Stored,string Token,ProjectedHistorySources.Source[] Sources,
        McObjectId[] ProjectionIds);
    static void Need([DoesNotReturnIf(false)] bool ok,string code,string message){if(!ok)throw new DomainException(code,message);}
    static Point3 P(JsonElement a)=>new(a[0].GetDouble(),a[1].GetDouble(),a[2].GetDouble());
    static double Dot(Point3 a,Point3 b)=>a.X*b.X+a.Y*b.Y+a.Z*b.Z;
    internal static SketchConstraintCurve[] Boundary(JsonElement geometry,SketchPlaneSpec plane,Action cancel)
    {
        return NativeSketchGeometry385.Curves(geometry,plane,cancel);
    }
    // GetContours returns the SELECTED native profile areas, not the entire
    // parent sketch. Fresh SDK IL constructs owned LocationLoops/Polyline3d
    // geometry copies; none of these are resident database objects.
    static JsonElement ProfileGeometry(SketchProfile profile,JsonElement sketchGeometry,Action cancel,bool allowCircularHoles)
    {
        cancel();var areas=profile.GetContours(false);
        Need(areas!=null,"PROFILE_GEOMETRY_UNAVAILABLE","The native profile does not expose closed areas; no parent-sketch contour is substituted.");
        try
        {
            Need(areas.Count==1,"OUT_OF_SCOPE","This adapter requires one native profile area; the dedicated circular-hole route observes its inner loops.");
            var area=areas[0];Need(area!=null,"PROFILE_GEOMETRY_UNAVAILABLE","Native profile area is unavailable.");
            var holes=area.Internal;
            try
            {
                Need(holes!=null,"PROFILE_GEOMETRY_UNAVAILABLE","The native selected hole inventory is unavailable.");
                if(allowCircularHoles&&holes.Count>0)
                {
                    using var actualOuter=area.External;
                    var plane=ModelProfileAreas377.Plane(sketchGeometry.GetProperty("frame"));
                    var curves=Boundary(sketchGeometry,plane,cancel);
                    // Preserve the accepted377 exact Circle member IDs/token
                    // shape. An unrelated open reference cannot disqualify an
                    // otherwise valid SELECTED native area with inner loops.
                    return curves.Length>0&&curves.All(curve=>curve.Kind=="circle")
                        ?ModelProfileAreas377.CircularArea(actualOuter,holes,sketchGeometry,cancel)
                        :SelectedArea(actualOuter,holes,sketchGeometry,cancel);
                }
                Need(holes.Count==0,"OUT_OF_SCOPE","Native profile hole loops need their dedicated adapter.");
            }
            finally {if(holes!=null)foreach(var hole in holes)hole?.Dispose();}
            using var outer=area.External;
            Need(outer!=null&&outer.IsClosed(),"INVALID_GEOMETRY","The selected native profile boundary is unavailable or open.");
            var loop=ModelProfileCurvedGeometry445.Loop(outer,sketchGeometry.GetProperty("frame"),cancel);
            return Wire.Element(new{kind="profile_contour",coordinate_system="WCS",geometry_details_complete=true,
                frame=sketchGeometry.GetProperty("frame"),elements=loop.GetProperty("elements"),identity_scope="boundary_order_only_not_native_subentity_identity"});
        }
        finally {foreach(var area in areas.Distinct(ReferenceEqualityComparer.Instance))if(area is IDisposable disposable)disposable.Dispose();}
    }
    static JsonElement SelectedArea(G.Polyline3d outer,IEnumerable<G.Polyline3d> holes,JsonElement sketchGeometry,Action cancel)
    {
        var frame=sketchGeometry.GetProperty("frame");ModelProfileAreas377.Plane(frame);
        var outside=ModelProfileElementsReader358.Loop(outer,frame,cancel);
        var inside=holes.Select(hole=>{cancel();return ModelProfileElementsReader358.Loop(hole,frame,cancel);}).ToArray();
        var result=Wire.Element(new{kind="profile_regions",coordinate_system="WCS",geometry_details_complete=true,frame,
            areas=new[]{new{outer=outside,holes=inside}},identity_scope="selected_native_area_and_boundary_order; no_parent_sketch_boundary_substitution_or_native_subentity_alias"});
        new ModelInventoryBudget(cancel).Add(result);return result;
    }
    internal static JsonElement SelectedGeometry(SketchProfile profile,PlanarSketch sketch,Action cancel)
    {
        Need(profile.ParentSketchID==sketch.ID&&!sketch.IsInEditMode,"PROFILE_OWNERSHIP_MISMATCH","Read the actual selected profile outside its exact parent sketch edit mode.");
        var budget=new ModelInventoryBudget(cancel);var geometry=Wire.Element(SketchGeometryReader.Read(sketch,cancel,budget.Add));
        return ProfileGeometry(profile,geometry,cancel,allowCircularHoles:true);
    }
    internal static void RequireRegionPoint(JsonElement geometry,SketchPlaneSpec plane,Point3 point,Action cancel)
    {
        var local=DrawingWorkplane.Local(plane,point);
        Need(double.IsFinite(local.X)&&double.IsFinite(local.Y),"PROFILE_REGION_AMBIGUOUS","Choose a finite point in the actual native profile plane.");
        bool Inside(JsonElement loop)
        {
            var curves=Boundary(loop,plane,cancel);
            Need(curves.Length>0,"PROFILE_GEOMETRY_UNAVAILABLE","An actual selected native boundary is empty.");
            if(curves.Length==1&&curves[0].Kind=="circle")
            {
                double distance=DrawingProfileGeometry.Distance(local,curves[0].Center!);
                Need(double.IsFinite(distance)&&Math.Abs(distance-curves[0].Radius)>1e-7,"PROFILE_REGION_AMBIGUOUS","Region point lies on a native circular boundary.");
                return distance<curves[0].Radius;
            }
            Need(curves.All(curve=>curve.Kind is "line" or "arc"),"PROFILE_GEOMETRY_UNAVAILABLE","This actual selected loop needs its supported native boundary adapter.");
            return ModelSketchSelection362.Inside(curves,local,cancel);
        }
        int selected=0;
        if(geometry.TryGetProperty("areas",out var areas))foreach(var area in areas.EnumerateArray())
        {
            cancel();bool inside=Inside(area.GetProperty("outer"));
            foreach(var hole in area.GetProperty("holes").EnumerateArray())if(Inside(hole))inside=false;
            if(inside)selected++;
        }
        else if(Inside(geometry))selected++;
        Need(selected==1,"PROFILE_REGION_AMBIGUOUS","The requested point must be inside exactly one actual selected native area, outside its holes and off all boundaries.");
    }
    internal static void RequireRegionPoint(SketchProfile profile,SketchPlaneSpec plane,Point3 point,Action cancel)
        =>ModelProfileCurvedGeometry445.RequireRegionPoint(profile,plane,point,cancel);
    internal static (ModelProfileSpec Profile,SketchPlaneSpec Plane) Contour(JsonElement geometry,Action cancel)
    {
        Need(geometry.GetProperty("geometry_details_complete").GetBoolean(),"PROFILE_GEOMETRY_UNAVAILABLE","Profile requires a complete observed native contour.");
        var f=geometry.GetProperty("frame");var plane=new SketchPlaneSpec(P(f.GetProperty("origin_wcs")),P(f.GetProperty("x_step_wcs")),P(f.GetProperty("y_step_wcs")));
        Need(ModelParameterSnapshot.Near(Dot(plane.XAxis,plane.XAxis),1)&&ModelParameterSnapshot.Near(Dot(plane.YAxis,plane.YAxis),1)&&Math.Abs(Dot(plane.XAxis,plane.YAxis))<1e-9,
            "OUT_OF_SCOPE","The native sketch must expose perpendicular unit coordinates; no coordinate-space guess is made.");
        var elements=geometry.GetProperty("elements").EnumerateArray().ToArray();
        Need(elements.Length>0,"INVALID_GEOMETRY","The native profile sketch is empty.");
        var curves=new List<SketchConstraintCurve>();var budget=new ModelInventoryBudget(cancel);
        foreach(var element in elements)
        {
            cancel();var g=element.GetProperty("geometry");string id=element.GetProperty("element_id").GetString()!;
            string? kind=g.GetProperty("kind").GetString();SketchConstraintCurve curve;
            if(kind=="line")curve=new(id,"line",DrawingWorkplane.Local(plane,P(g.GetProperty("start"))),DrawingWorkplane.Local(plane,P(g.GetProperty("end"))));
            else
            {
                Need(kind=="circular_arc","OUT_OF_SCOPE","This reusable-profile adapter supports one closed Circle or Line/Arc contour.");
                double start=g.GetProperty("start_angle").GetDouble(),end=g.GetProperty("end_angle").GetDouble();
                if(elements.Length==1&&ModelParameterSnapshot.Near(Math.Abs(end-start),2*Math.PI))
                {
                    var normal=DrawingWorkplane.Normal(plane);var observed=P(g.GetProperty("normal"));
                    Need(Math.Abs(Math.Abs(Dot(normal,observed))-1)<1e-8,"OUT_OF_SCOPE","Circle plane differs from the native sketch plane.");
                    var circle=new ModelProfileSpec("circle",DrawingWorkplane.Local(plane,P(g.GetProperty("center"))),g.GetProperty("radius").GetDouble(),null);
                    ModelExtrusionGeometry.Measure(new(plane,circle,1,"positive","Profile validation","0"),cancel);
                    return(circle,plane);
                }
                curve=ModelPolycurve.FromSketchArc(id,P(g.GetProperty("center")),g.GetProperty("radius").GetDouble(),P(g.GetProperty("normal")),P(g.GetProperty("reference_direction")),start,end,plane);
            }
            budget.Add(curve);curves.Add(curve);
        }
        return(curves.Any(c=>c.Kind=="arc")?ModelPolycurve.FromCurves(curves.ToArray(),cancel):DrawingProfileGeometry.Profile(curves.ToArray(),cancel),plane);
    }
    internal static Snapshot Capture(SketchProfile profile,Mc3dSolid? body,DocumentState state,Db.Transaction tx,Action cancel,
        bool allowDocumentProjectionDiscovery=true,Multicad.DatabaseServices.McDocument? documentScope=null,bool allowCircularHoles=false)
    {
        cancel();ModelProfileDiscovery348.DrawingDocument(state);
        var document=documentScope??(body==null?profile.DbEntity.Document:ModelProfileDiscovery348.OwnerDocument(body,state));
        Need(document!=null&&!document.ID.IsNull,"DOCUMENT_CONTEXT_MISMATCH","The profile's verified native owner document is unavailable.");
        var current=ModelProfileDiscovery348.Resolve(profile.ID.ToString(),document,cancel);
        Need(current.ID==profile.ID,"PROFILE_OWNERSHIP_MISMATCH","Native profile identity changed.");
        var sketch=profile.ParentSketchID.GetObjectOfType<PlanarSketch>();
        Need(sketch!=null&&sketch.ID==profile.ParentSketchID&&sketch.DbEntity.Document!=null&&sketch.DbEntity.Document.ID==document.ID&&!sketch.IsInEditMode,
            "PROFILE_OWNERSHIP_MISMATCH","Profile's exact native parent sketch is unavailable in this document.");
        Need(profile.HostSolidID.IsNull||sketch.HostSolidID.IsNull||profile.HostSolidID==sketch.HostSolidID,
            "PROFILE_OWNERSHIP_MISMATCH","The profile and parent sketch have different native part affinities.");
        McObjectId[]? bodyIds=null;
        if(body!=null)
        {
            var ids=bodyIds=ModelInventoryBudget.History(body,cancel).ToArray();
            // Null HostSolid is a supported native document construction state.
            // Nonnull affinity must be real selected-body composition; no target
            // body is written into an otherwise unattached sketch/profile token.
            Need((profile.HostSolidID.IsNull||(profile.HostSolidID==body.ID&&ids.Contains(profile.ID)))&&
                (sketch.HostSolidID.IsNull||(sketch.HostSolidID==body.ID&&ids.Contains(sketch.ID))),
                "PROFILE_OWNERSHIP_MISMATCH","The profile or parent sketch has native affinity to another body, or its host composition is unavailable.");
            if(!profile.Dependent3dFeatureID.IsNull)
                Need(profile.HostSolidID==body.ID&&sketch.HostSolidID==body.ID&&ids.Contains(profile.ID)&&ids.Contains(sketch.ID),
                    "PROFILE_OWNERSHIP_MISMATCH","Consumed profile and parent sketch must belong to the selected body's current history.");
            if(!allowDocumentProjectionDiscovery)
                Need(profile.HostSolidID==body.ID&&sketch.HostSolidID==body.ID&&ids.Contains(profile.ID)&&ids.Contains(sketch.ID),
                    "PROFILE_DOCUMENT_CONSTRUCTION","Read exact profile_ids to inspect document construction outside the body's native composition.");
        }
        Need((profile.Status.ToString()=="Ok"||ProjectedHistorySources.HealthySuppressed(profile))&&
            (sketch.Status.ToString()=="Ok"||ProjectedHistorySources.HealthySuppressed(sketch)),"MODEL_STATE_UNAVAILABLE","Profile or parent sketch requires native update.");
        var sketchBytes=new ModelInventoryBudget(cancel);
        var geometry=Wire.Element(SketchGeometryReader.Read(sketch,cancel,item=>sketchBytes.Add(item)));
        var profileGeometry=ProfileGeometry(profile,geometry,cancel,allowCircularHoles);
        var contour=profileGeometry.TryGetProperty("areas",out _)||ModelProfileCurvedGeometry445.HasCurves(profileGeometry)?(Profile:(ModelProfileSpec?)null,Plane:ModelProfileAreas377.Plane(geometry.GetProperty("frame"))):Contour(profileGeometry,cancel);
        object nativePlane;
        using(var plane=profile.Plane)
        {
            var n=DrawingWorkplane.Normal(contour.Plane);
            Need(plane!=null&&new[]{plane.Origin.X,plane.Origin.Y,plane.Origin.Z,plane.Normal.X,plane.Normal.Y,plane.Normal.Z}.All(double.IsFinite)&&
                Math.Abs(plane.SignedDistanceTo(new G.Point3d(contour.Plane.Origin.X,contour.Plane.Origin.Y,contour.Plane.Origin.Z)))<1e-7&&
                Math.Abs(n.X*plane.Normal.X+n.Y*plane.Normal.Y+n.Z*plane.Normal.Z-1)<1e-8,
                "PROFILE_PLANE_MISMATCH","Native profile and parent-sketch oriented planes disagree; extrusion direction cannot be guessed.");
            nativePlane=new{origin=new[]{plane.Origin.X,plane.Origin.Y,plane.Origin.Z},axis_u=new[]{plane.DirU.X,plane.DirU.Y,plane.DirU.Z},
                axis_v=new[]{plane.DirV.X,plane.DirV.Y,plane.DirV.Z},normal=new[]{plane.Normal.X,plane.Normal.Y,plane.Normal.Z}};
        }
        // Ordinary public body-history reads stay body-local. Explicit document
        // profile reads and unused construction writes opt in to document search.
        var candidates=body!=null&&profile.HostSolidID==body.ID&&sketch.HostSolidID==body.ID?bodyIds:null;
        var projected=ModelProfileDiscovery348.Projections(sketch,contour.Plane,state,tx,cancel,candidates);
        var projections=projected.Ids;var sources=projected.Sources;
        var depends=profile.GetDependsOn(false,false);
        Need(depends!=null,"HISTORY_UNAVAILABLE","Native profile dependency inventory is unavailable.");ModelInventoryBudget.Ids(depends,cancel);
        var sketchDepends=sketch.GetDependsOn(false,false);
        Need(sketchDepends!=null,"HISTORY_UNAVAILABLE","Native parent sketch dependency inventory is unavailable.");ModelInventoryBudget.Ids(sketchDepends,cancel);
        var stored=Wire.Element(new{mc_document_id=document.ID.ToString(),profile_id=profile.ID.ToString(),parent_sketch_id=sketch.ID.ToString(),
            profile_host_solid_id=ModelProfileDiscovery348.Id(profile.HostSolidID),sketch_host_solid_id=ModelProfileDiscovery348.Id(sketch.HostSolidID),
            profile_name=profile.Name,sketch_name=sketch.Name,profile_previous=profile.OrderObjBefore.ToString(),sketch_previous=sketch.OrderObjBefore.ToString(),
            support_id=sketch.PlanarEntityID.ToString(),dependencies=depends.Select(id=>id.ToString()).OrderBy(id=>id,StringComparer.Ordinal).ToArray(),
            sketch_dependencies=sketchDepends.Select(id=>id.ToString()).OrderBy(id=>id,StringComparer.Ordinal).ToArray(),projection_bindings=projected.Bindings,
            dependent_feature_id=profile.Dependent3dFeatureID.IsNull?null:profile.Dependent3dFeatureID.ToString(),
            geometry,profile_geometry=profileGeometry,native_plane=nativePlane,sources,projection_ids=projections.Select(id=>id.ToString()).OrderBy(id=>id,StringComparer.Ordinal).ToArray()});
        new ModelInventoryBudget(cancel).Add(stored);
        return new(profile,sketch,geometry,contour.Profile,contour.Plane,profileGeometry,stored,Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(stored,Wire.Json))),sources,projections);
    }
    internal static ModelProfileRead340 Read(SketchProfile profile,Mc3dSolid? body,DocumentState state,Db.Transaction tx,Action cancel,
        Multicad.DatabaseServices.McDocument? documentScope=null)
    {
        string? parent=null;
        try {
            var parentId=profile.ParentSketchID;parent=parentId.IsNull?null:parentId.ToString();
            var snapshot=ModelProfileRebindProfiles414.Capture(profile,body,state,tx,cancel,documentScope);static double[] V(Point3 p)=>[p.X,p.Y,p.Z];
            // Reusable describes a candidate route, not absence of consumers.
            // SDK Sample3dMix uses this one native profile for two features.
            // Mutation preflight observes/validates all actual forward uses.
            bool reusable=true;var actualConsumers=ModelProfileConsumers410.Capture(profile,profile.DbEntity.Document!,cancel);
            if(body!=null)ModelProfileConsumers410.RequireBody(actualConsumers,body);
            return new("profile",profile.ParentSketchID.ToString(),new(reusable,snapshot.Token),
                new(V(snapshot.Plane.Origin),V(snapshot.Plane.XAxis),V(snapshot.Plane.YAxis),V(DrawingWorkplane.Normal(snapshot.Plane))),
                snapshot.Sources.Select(s=>s.id).ToArray(),snapshot.Stored.GetProperty("projection_ids").EnumerateArray().Select(v=>v.GetString()!).ToArray(),snapshot.ProfileGeometry,profile.Dependent3dFeatureID.IsNull?null:profile.Dependent3dFeatureID.ToString(),
                profile.ID.ToString(),ModelProfileDiscovery348.Ownership(snapshot),ModelProfileDiscovery348.Id(snapshot.Sketch.PlanarEntityID),
                actualConsumers.Consumers.Select(consumer=>new ModelProfileConsumer411(consumer.FeatureId,consumer.Kind,consumer.BodyId,consumer.Profiles)).ToArray());
        }
        catch(OperationCanceledException){throw;}
        catch(Exception error)
        {
            // Additive read metadata must preserve the ordinary history result.
            string reason=(error is DomainException domain?domain.Error.code+": ":"NATIVE_PROFILE_READ_UNAVAILABLE: ")+error.Message;
            if(reason.Length>512)reason=reason[..512];
            return new("profile",parent,new(false,unavailable_reason:reason),profile_id:profile.ID.ToString());
        }
    }
}
