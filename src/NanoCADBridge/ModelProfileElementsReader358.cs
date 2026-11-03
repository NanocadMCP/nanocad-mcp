using System.Security.Cryptography;
using System.Text.Json;
using Multicad;
using Multicad.Mc3D;
using NanoCAD.Contracts;
using G=Multicad.Geometry;
using Db=Teigha.DatabaseServices;

namespace NanoCADBridge;

internal static class ModelProfileElementsReader358
{
    internal sealed record Snapshot(SketchProfile Profile,PlanarSketch Sketch,SketchPlaneSpec Plane,JsonElement Geometry,
        McObjectId[] ElementIds,McObjectId[] Containing,ModelProfileDiscovery348.ProjectionState Projections,
        JsonElement SelectedGeometry,SketchConstraintCurve[][] Outer,SketchConstraintCurve[][][] Holes,
        JsonElement Constraints,JsonElement Stored,string Token);
    internal static void Need([System.Diagnostics.CodeAnalysis.DoesNotReturnIf(false)] bool value,string code,string message)
    {if(!value)throw new DomainException(code,message);}
    static Point3 P(JsonElement value)=>new(value[0].GetDouble(),value[1].GetDouble(),value[2].GetDouble());
    internal static JsonElement Loop(G.Polyline3d loop,JsonElement frame,Action cancel)
        =>ModelProfileCurvedGeometry445.Loop(loop,frame,cancel);
    internal static Snapshot Capture(Mc3dSolid body,SketchProfile profile,DocumentState state,Db.Transaction tx,Action cancel)
    {
        var owner=ModelProfileDiscovery348.OwnerDocument(body,state);var exact=ModelProfileDiscovery348.Resolve(profile.ID.ToString(),owner,cancel);
        var ids=ModelInventoryBudget.History(body,cancel).ToArray();var sketch=exact.ParentSketchID.GetObjectOfType<PlanarSketch>();
        Need(sketch!=null&&sketch.ID==exact.ParentSketchID&&sketch.DbEntity.Document!=null&&sketch.DbEntity.Document.ID==owner.ID&&!sketch.IsInEditMode,
            "PROFILE_OWNERSHIP_MISMATCH","The exact parent sketch must be resident in the selected body's actual native owner document.");
        Need((profile.HostSolidID.IsNull||profile.HostSolidID==body.ID&&ids.Contains(profile.ID))&&
            (sketch.HostSolidID.IsNull||sketch.HostSolidID==body.ID&&ids.Contains(sketch.ID)),"PROFILE_OWNERSHIP_MISMATCH","Existing native host affinity must belong to this actual part.");
        Need(profile.Status.ToString()=="Ok"&&sketch.Status.ToString()=="Ok"&&!profile.IsSuppressed&&!sketch.IsSuppressed&&!profile.IsBeyondEndOfPart&&!sketch.IsBeyondEndOfPart,
            "MODEL_STATE_UNAVAILABLE","Selected profile/sketch must be healthy and active.");
        if(!profile.Dependent3dFeatureID.IsNull)
            Need(profile.HostSolidID==body.ID&&sketch.HostSolidID==body.ID,"PROFILE_OWNERSHIP_MISMATCH","A consumed profile must retain actual native body composition.");
        var bytes=new ModelInventoryBudget(cancel);var geometry=Wire.Element(SketchGeometryReader.Read(sketch,cancel,bytes.Add));
        Need(geometry.GetProperty("geometry_details_complete").GetBoolean()&&geometry.GetProperty("elements").EnumerateArray().All(item=>
            item.GetProperty("status").GetString()=="read"&&item.GetProperty("geometry").GetProperty("kind").GetString() is "line" or "circular_arc" or "spline" or "elliptical_arc"),
            "PROFILE_GEOMETRY_UNAVAILABLE","The actual native profile sketch requires complete supported curve geometry.");
        var frame=geometry.GetProperty("frame");var plane=new SketchPlaneSpec(P(frame.GetProperty("origin_wcs")),P(frame.GetProperty("x_step_wcs")),P(frame.GetProperty("y_step_wcs")));
        Need(Math.Abs(DrawingWorkplane.Dot(plane.XAxis,plane.XAxis)-1)<=1e-10&&Math.Abs(DrawingWorkplane.Dot(plane.YAxis,plane.YAxis)-1)<=1e-10&&
            Math.Abs(DrawingWorkplane.Dot(plane.XAxis,plane.YAxis))<=1e-10,"SKETCH_FRAME_UNAVAILABLE","The native sketch frame must expose perpendicular unit WCS axes without normalization.");
        McObjectId[] elements;var raw=sketch.GetAllItemsGeometry(false);
        Need(raw!=null,"SKETCH_GEOMETRY_UNAVAILABLE","Native element identity inventory is unavailable.");
        try{elements=raw.Keys.OrderBy(id=>id.ToString(),StringComparer.OrdinalIgnoreCase).ToArray();}
        finally{foreach(var value in raw.Values.Distinct(ReferenceEqualityComparer.Instance))if(value is IDisposable disposable)disposable.Dispose();}
        Need(elements.Select(id=>id.ToString()).ToHashSet(StringComparer.OrdinalIgnoreCase).SetEquals(geometry.GetProperty("elements").EnumerateArray().Select(item=>item.GetProperty("element_id").GetString()!)),
            "PRECONDITION_FAILED","The native sketch element inventory changed during its read.");
        var containing=sketch.GetContainingObjects(true,true,false);
        Need(containing!=null,"SKETCH_GEOMETRY_UNAVAILABLE","Actual native sketch membership is unavailable.");ModelInventoryBudget.Ids(containing,cancel);
        var projections=ModelProfileDiscovery348.Projections(sketch,plane,state,tx,cancel,elements);
        Need(projections.Ids.All(id=>containing.Contains(id)&&sketch.ContainsObject(id)),"PROFILE_OWNERSHIP_MISMATCH","An observed projection lacks native sketch membership.");
        var areas=profile.GetContours(false);Need(areas!=null&&areas.Count>0,"PROFILE_GEOMETRY_UNAVAILABLE","Actual selected profile regions are unavailable.");
        var selected=new List<object>();var outers=new List<SketchConstraintCurve[]>();var holesByArea=new List<SketchConstraintCurve[][]>();
        try
        {
            foreach(var area in areas)
            {
                cancel();Need(area!=null,"PROFILE_GEOMETRY_UNAVAILABLE","A selected native area is unavailable.");using var outer=area.External;
                var outside=Loop(outer,frame,cancel);outers.Add(ModelProfileLifecycleState340.Boundary(outside,plane,cancel));
                var holes=area.Internal;Need(holes!=null,"PROFILE_GEOMETRY_UNAVAILABLE","Selected native hole inventory is unavailable.");var inner=new List<JsonElement>();var curves=new List<SketchConstraintCurve[]>();
                try{foreach(var hole in holes){cancel();Need(hole!=null,"PROFILE_GEOMETRY_UNAVAILABLE","A selected hole loop is unavailable.");var value=Loop(hole,frame,cancel);inner.Add(value);curves.Add(ModelProfileLifecycleState340.Boundary(value,plane,cancel));}}
                finally{foreach(var hole in holes.Distinct(ReferenceEqualityComparer.Instance))if(hole is IDisposable disposable)disposable.Dispose();}
                var item=new{outer=outside,holes=inner};bytes.Add(item);selected.Add(item);holesByArea.Add(curves.ToArray());
            }
        }
        finally{foreach(var area in areas.Distinct(ReferenceEqualityComparer.Instance))if(area is IDisposable disposable)disposable.Dispose();}
        using(var actualPlane=profile.Plane)
        {
            var normal=DrawingWorkplane.Normal(plane);Need(actualPlane!=null&&Math.Abs(actualPlane.SignedDistanceTo(new G.Point3d(plane.Origin.X,plane.Origin.Y,plane.Origin.Z)))<=1e-7&&
                Math.Abs(actualPlane.Normal.X*normal.X+actualPlane.Normal.Y*normal.Y+actualPlane.Normal.Z*normal.Z-1)<=1e-8,
                "PROFILE_PLANE_MISMATCH","Selected profile and parent sketch have different oriented physical planes.");
        }
        var constraints=ModelSketchConstraintObservation353.Read(body,sketch,cancel);var depends=profile.GetDependsOn(false,false);var sketchDepends=sketch.GetDependsOn(false,false);
        Need(depends!=null&&sketchDepends!=null,"HISTORY_UNAVAILABLE","Selected profile/sketch dependencies are unavailable.");
        ModelInventoryBudget.Ids(depends,cancel);ModelInventoryBudget.Ids(sketchDepends,cancel);
        var selectedGeometry=Wire.Element(selected);var stored=Wire.Element(new{owner_document_id=owner.ID.ToString(),profile_id=profile.ID.ToString(),sketch_id=sketch.ID.ToString(),
            profile_name=profile.Name,sketch_name=sketch.Name,profile_host_solid_id=ModelProfileDiscovery348.Id(profile.HostSolidID),sketch_host_solid_id=ModelProfileDiscovery348.Id(sketch.HostSolidID),
            profile_previous=ModelProfileDiscovery348.Id(profile.OrderObjBefore),sketch_previous=ModelProfileDiscovery348.Id(sketch.OrderObjBefore),
            support_id=ModelProfileDiscovery348.Id(sketch.PlanarEntityID),dependent_feature_id=ModelProfileDiscovery348.Id(profile.Dependent3dFeatureID),
            geometry,selected_profile_geometry=selectedGeometry,constraints,projection_bindings=projections.Bindings,sources=projections.Sources,
            containing_ids=containing.Select(id=>id.ToString()).OrderBy(id=>id,StringComparer.OrdinalIgnoreCase),
            dependencies=depends.Select(id=>id.ToString()).OrderBy(id=>id,StringComparer.OrdinalIgnoreCase),sketch_dependencies=sketchDepends.Select(id=>id.ToString()).OrderBy(id=>id,StringComparer.OrdinalIgnoreCase)});
        bytes.Add(stored);return new(profile,sketch,plane,geometry,elements,containing.ToArray(),projections,selectedGeometry,outers.ToArray(),holesByArea.ToArray(),constraints,stored,
            Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(stored,Wire.Json))));
    }
    internal static ModelProfileElementsRead358 Describe(Snapshot value,Arguments args)
    {
        static double[] V(Point3 p)=>[p.X,p.Y,p.Z];var byId=value.Projections.Sources.SelectMany(source=>source.projection_ids.Select(id=>(Id:id,Source:source)))
            .ToDictionary(item=>item.Id,item=>item.Source,StringComparer.OrdinalIgnoreCase);
        var elements=value.Geometry.GetProperty("elements").EnumerateArray().Select(item=>
        {string id=item.GetProperty("element_id").GetString()!;bool linked=byId.TryGetValue(id,out var source);return new ModelProfileElement358(id,item.GetProperty("geometry").GetProperty("kind").GetString()!,
            linked?source!.id:null,linked?new EntityRef(args.BridgeId!,args.DocumentId!,source!.handle):null,item.GetProperty("geometry"));}).ToArray();
        return new(value.Profile.ID.ToString(),value.Sketch.ID.ToString(),value.Profile.DbEntity.Document.ID.ToString(),ModelProfileDiscovery348.Id(value.Profile.Dependent3dFeatureID),value.Token,
            new(V(value.Plane.Origin),V(value.Plane.XAxis),V(value.Plane.YAxis),V(DrawingWorkplane.Normal(value.Plane))),elements,value.SelectedGeometry,value.Constraints,
            "actual_native_element_ids_and_selected_boundaries; removal_changes_sketch_membership_not_a_source_DWG_erase_or_reusable_object_claim");
    }
}
