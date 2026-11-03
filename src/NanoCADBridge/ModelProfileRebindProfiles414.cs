using System.Security.Cryptography;
using System.Text.Json;
using Multicad;
using Multicad.DatabaseServices;
using Multicad.Mc3D;
using NanoCAD.Contracts;
using G=Multicad.Geometry;
using Db=Teigha.DatabaseServices;

namespace NanoCADBridge;

// The native profile is its selected areas, not a copied outer sketch loop.
// Keep existing single-area tokens byte-for-byte; add only the missing native
// multi-area observation shared by public reads and the extrusion rebind.
internal static class ModelProfileRebindProfiles414
{
    static void Need([System.Diagnostics.CodeAnalysis.DoesNotReturnIf(false)] bool ok,string code,string message)
    {if(!ok)throw new DomainException(code,message);}
    static string Key(McObjectId id)=>id.ToString();
    internal static ModelProfileLifecycleState340.Snapshot Capture(SketchProfile profile,Mc3dSolid? body,DocumentState state,
        Db.Transaction tx,Action cancel,McDocument? documentScope=null)
    {
        cancel();var areas=profile.GetContours(false);Need(areas!=null,"PROFILE_GEOMETRY_UNAVAILABLE","The native selected profile areas are unavailable.");
        int count=areas.Count;
        if(count==1)
        {
            foreach(var area in areas.Distinct(ReferenceEqualityComparer.Instance))if(area is IDisposable disposable)disposable.Dispose();
            // Explicit document scope admits retained unused construction while
            // keeping body supplied for nonnull affinity/composition checks.
            // An ordinary body-only reader keeps its existing local policy.
            return ModelProfileLifecycleState340.Capture(profile,body,state,tx,cancel,allowDocumentProjectionDiscovery:body==null||documentScope!=null,documentScope:documentScope,allowCircularHoles:true);
        }
        try
        {
            Need(count>0,"PROFILE_GEOMETRY_UNAVAILABLE","The native profile has no selected closed area.");
            var owner=documentScope??(body==null?profile.DbEntity.Document:ModelProfileDiscovery348.OwnerDocument(body,state));
            Need(owner!=null&&!owner.ID.IsNull,"DOCUMENT_CONTEXT_MISMATCH","The actual native profile owner is unavailable.");
            var exact=ModelProfileDiscovery348.Resolve(Key(profile.ID),owner,cancel);Need(exact.ID==profile.ID,"PROFILE_OWNERSHIP_MISMATCH","Native profile identity changed.");
            var sketch=profile.ParentSketchID.GetObjectOfType<PlanarSketch>();
            Need(sketch!=null&&sketch.ID==profile.ParentSketchID&&sketch.DbEntity.Document?.ID==owner.ID&&!sketch.IsInEditMode,
                "PROFILE_OWNERSHIP_MISMATCH","The selected profile needs its exact same-owner parent sketch outside edit mode.");
            Need(profile.HostSolidID.IsNull||sketch.HostSolidID.IsNull||profile.HostSolidID==sketch.HostSolidID,
                "PROFILE_OWNERSHIP_MISMATCH","The selected profile and sketch have conflicting native part affinities.");
            McObjectId[]? contents=null;
            if(body!=null)
            {
                contents=ModelInventoryBudget.History(body,cancel).ToArray();
                Need((profile.HostSolidID.IsNull||profile.HostSolidID==body.ID&&contents.Contains(profile.ID))&&
                    (sketch.HostSolidID.IsNull||sketch.HostSolidID==body.ID&&contents.Contains(sketch.ID)),
                    "PROFILE_OWNERSHIP_MISMATCH","A nonempty native profile/sketch affinity must match actual selected-part composition.");
            }
            Need((profile.Status.ToString()=="Ok"||ProjectedHistorySources.HealthySuppressed(profile))&&
                (sketch.Status.ToString()=="Ok"||ProjectedHistorySources.HealthySuppressed(sketch)),"MODEL_STATE_UNAVAILABLE","The selected profile or parent sketch needs native update.");
            var budget=new ModelInventoryBudget(cancel);var geometry=Wire.Element(SketchGeometryReader.Read(sketch,cancel,budget.Add));
            Need(geometry.GetProperty("geometry_details_complete").GetBoolean(),"SKETCH_GEOMETRY_UNAVAILABLE","The actual parent sketch geometry is incomplete.");
            var frame=geometry.GetProperty("frame");var plane=ModelProfileAreas377.Plane(frame);var selected=new List<object>();
            foreach(var area in areas)
            {
                cancel();Need(area!=null,"PROFILE_GEOMETRY_UNAVAILABLE","A selected native profile area is unavailable.");using var outer=area.External;
                var outside=ModelProfileElementsReader358.Loop(outer,frame,cancel);var holes=area.Internal;
                Need(holes!=null,"PROFILE_GEOMETRY_UNAVAILABLE","The selected native profile hole inventory is unavailable.");
                try{selected.Add(new{outer=outside,holes=holes.Select(hole=>{cancel();return ModelProfileElementsReader358.Loop(hole,frame,cancel);}).ToArray()});}
                finally{foreach(var hole in holes.Distinct(ReferenceEqualityComparer.Instance))if(hole is IDisposable disposable)disposable.Dispose();}
            }
            var profileGeometry=Wire.Element(new{kind="profile_regions",coordinate_system="WCS",geometry_details_complete=true,frame,areas=selected,
                identity_scope="selected_native_area_and_boundary_order; no_parent_sketch_boundary_substitution_or_native_subentity_alias"});
            object nativePlane;
            using(var actual=profile.Plane)
            {
                var normal=DrawingWorkplane.Normal(plane);
                Need(Math.Abs(actual.SignedDistanceTo(new G.Point3d(plane.Origin.X,plane.Origin.Y,plane.Origin.Z)))<1e-7&&
                    Math.Abs(normal.X*actual.Normal.X+normal.Y*actual.Normal.Y+normal.Z*actual.Normal.Z-1)<1e-8,
                    "PROFILE_PLANE_MISMATCH","The actual selected profile and parent sketch have different oriented physical planes.");
                nativePlane=new{origin=new[]{actual.Origin.X,actual.Origin.Y,actual.Origin.Z},axis_u=new[]{actual.DirU.X,actual.DirU.Y,actual.DirU.Z},
                    axis_v=new[]{actual.DirV.X,actual.DirV.Y,actual.DirV.Z},normal=new[]{actual.Normal.X,actual.Normal.Y,actual.Normal.Z}};
            }
            var candidates=body!=null&&profile.HostSolidID==body.ID&&sketch.HostSolidID==body.ID?contents:null;
            var projections=ModelProfileDiscovery348.Projections(sketch,plane,state,tx,cancel,candidates);
            var depends=profile.GetDependsOn(false,false);var sketchDepends=sketch.GetDependsOn(false,false);
            Need(depends!=null&&sketchDepends!=null,"HISTORY_UNAVAILABLE","Actual selected profile/sketch dependencies are unavailable.");
            ModelInventoryBudget.Ids(depends,cancel);ModelInventoryBudget.Ids(sketchDepends,cancel);
            var stored=Wire.Element(new{mc_document_id=Key(owner.ID),profile_id=Key(profile.ID),parent_sketch_id=Key(sketch.ID),
                profile_host_solid_id=ModelProfileDiscovery348.Id(profile.HostSolidID),sketch_host_solid_id=ModelProfileDiscovery348.Id(sketch.HostSolidID),
                profile_name=profile.Name,sketch_name=sketch.Name,profile_previous=Key(profile.OrderObjBefore),sketch_previous=Key(sketch.OrderObjBefore),
                support_id=Key(sketch.PlanarEntityID),dependencies=depends.Select(Key).OrderBy(id=>id,StringComparer.Ordinal).ToArray(),
                sketch_dependencies=sketchDepends.Select(Key).OrderBy(id=>id,StringComparer.Ordinal).ToArray(),projection_bindings=projections.Bindings,
                dependent_feature_id=ModelProfileDiscovery348.Id(profile.Dependent3dFeatureID),geometry,profile_geometry=profileGeometry,native_plane=nativePlane,
                sources=projections.Sources,projection_ids=projections.Ids.Select(Key).OrderBy(id=>id,StringComparer.Ordinal).ToArray()});
            budget.Add(stored);
            return new(profile,sketch,geometry,null,plane,profileGeometry,stored,Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(stored,Wire.Json))),projections.Sources,projections.Ids);
        }
        finally{foreach(var area in areas.Distinct(ReferenceEqualityComparer.Instance))if(area is IDisposable disposable)disposable.Dispose();}
    }
}
