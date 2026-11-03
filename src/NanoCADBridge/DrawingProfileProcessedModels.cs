using System.Text.Json;
using Multicad;
using Multicad.DatabaseServices;
using Multicad.Mc3D;
using NanoCAD.Contracts;
using Db=Teigha.DatabaseServices;
using MPoint=Multicad.Geometry.Point3d;

namespace NanoCADBridge;

internal static partial class DrawingProfileWriter
{
    internal sealed record TailFeature(McObjectId Feature,McObjectId Profile,McObjectId Sketch,string Operation,ModelExtrusionSpec Definition,string GeometryIdentity,ModelPlacement? Placement=null);
    private sealed record ProcessedBody(McObjectId Sketch,McObjectId Feature,McObjectId[] Projections,string[] Sources,double Height,string Fingerprint,
        McObjectId TouchedSketch);
    internal static void StraightExtrusion(ExtrudeFeature feature,Mc3dSolid body)
    {
        Need(feature.HostSolidID==body.ID&&!feature.IsSuppressed&&feature.Status.ToString()=="Ok"&&feature.Extent.ToString()=="Dist"&&
            feature.Distance>=.001&&feature.Distance<=1e6&&DrawingProfileGeometry.Near(feature.Angle,0)&&DrawingProfileGeometry.Near(feature.Distance2,0)&&DrawingProfileGeometry.Near(feature.Angle2,0)&&
            feature.Direction is FeatureExtentDirection.Positive or FeatureExtentDirection.Negative&&feature.PartOperation is PartFeatureOperation.Join or PartFeatureOperation.Cut,
            "OUT_OF_SCOPE","Processed profiles support bounded, healthy, untapered single-distance join/cut extrusions.");
    }
    internal static TailFeature ReadTail(ExtrudeFeature feature,McObjectId sketchId,Action cancel,JsonElement? suppliedGeometry=null)
    {
        var sketch=sketchId.GetObjectOfType<PlanarSketch>();Need(sketch!=null&&!sketch.IsInEditMode,"SKETCH_GEOMETRY_UNAVAILABLE","Tail feature sketch unavailable.");
        var snapshot=suppliedGeometry??Wire.Element(SketchGeometryReader.Read(sketch,cancel));Need(snapshot.GetProperty("geometry_details_complete").GetBoolean(),"OUT_OF_SCOPE","Tail sketch geometry is incomplete.");
        var frame=snapshot.GetProperty("frame");Point3 Point(JsonElement p)=>new(p[0].GetDouble(),p[1].GetDouble(),p[2].GetDouble());
        var origin=Point(frame.GetProperty("origin_wcs"));var x=Point(frame.GetProperty("x_step_wcs"));var y=Point(frame.GetProperty("y_step_wcs"));
        double Dot(Point3 a,Point3 b)=>a.X*b.X+a.Y*b.Y+a.Z*b.Z;
        Need(DrawingProfileGeometry.Near(Dot(x,x),1)&&DrawingProfileGeometry.Near(Dot(y,y),1)&&Math.Abs(Dot(x,y))<1e-9,"OUT_OF_SCOPE","Tail sketch needs perpendicular unit axes.");
        var normal=new Point3(x.Y*y.Z-x.Z*y.Y,x.Z*y.X-x.X*y.Z,x.X*y.Y-x.Y*y.X);
        Point2 Local(JsonElement p)
        {
            var v=Point(p);var delta=new Point3(v.X-origin.X,v.Y-origin.Y,v.Z-origin.Z);
            Need(Math.Abs(Dot(delta,normal))<1e-7,"OUT_OF_SCOPE","Tail geometry is outside its reported sketch plane.");return new(Dot(delta,x),Dot(delta,y));
        }
        var elements=snapshot.GetProperty("elements").EnumerateArray().ToArray();Need(elements.Length>=1,"OUT_OF_SCOPE","Tail profile needs a nonempty native curve inventory.");
        var curves=new List<SketchConstraintCurve>();
        foreach(var item in elements)
        {
            cancel();
            var g=item.GetProperty("geometry");string id=item.GetProperty("element_id").GetString()!;
            if(g.GetProperty("kind").GetString()=="line")curves.Add(new(id,"line",Local(g.GetProperty("start")),Local(g.GetProperty("end"))));
            else
            {
                Need(g.GetProperty("kind").GetString()=="circular_arc"&&g.GetProperty("angle_unit").GetString()=="radians","OUT_OF_SCOPE","Tail profiles support exact lines and circular arcs with explicit radians.");
                curves.Add(ModelPolycurve.FromSketchArc(id,Point(g.GetProperty("center")),g.GetProperty("radius").GetDouble(),Point(g.GetProperty("normal")),Point(g.GetProperty("reference_direction")),g.GetProperty("start_angle").GetDouble(),g.GetProperty("end_angle").GetDouble(),new(origin,x,y)));
            }
        }
        var profile=curves.Any(c=>c.Kind=="arc")?ModelPolycurve.FromCurves(curves.ToArray(),cancel):DrawingProfileGeometry.Profile(curves.ToArray(),cancel);
        Need(new[]{origin.X,origin.Y,origin.Z}.All(v=>double.IsFinite(v)&&Math.Abs(v)<=1e6)&&
            (profile.Kind=="circle"?profile.Radius>=.001&&profile.Radius<=1e6&&Math.Abs(profile.Center!.X)<=1e6&&Math.Abs(profile.Center.Y)<=1e6:profile.Vertices!.All(p=>double.IsFinite(p.X)&&double.IsFinite(p.Y)&&Math.Abs(p.X)<=1e6&&Math.Abs(p.Y)<=1e6)),
            "OUT_OF_SCOPE","Tail profiles must stay within the bounded model construction coordinates.");
        bool positive=feature.Direction==FeatureExtentDirection.Positive;
        using(var plane=sketch.Plane)if(plane.SignedDistanceTo(new MPoint(origin.X+normal.X,origin.Y+normal.Y,origin.Z+normal.Z))<0)positive=!positive;
        var definition=new ModelExtrusionSpec(new(origin,x,y),profile,feature.Distance,positive?"positive":"negative",feature.Name,"0");
        ModelExtrusionGeometry.Measure(definition,cancel);
        string identity=Wire.Element(elements.Select(e=>new{id=e.GetProperty("element_id").GetString(),type=e.GetProperty("geometry_type").GetString()}).ToArray()).GetRawText();
        return new(feature.ID,feature.ProfileID,sketchId,feature.PartOperation==PartFeatureOperation.Cut?"cut":"join",definition,identity);
    }
    private static ProcessedBody ReadProcessed(Mc3dSolid body,Dictionary<string,Source> selected,DocumentState state,Db.Transaction tx,Action cancel)
    {
        var contents=ModelInventoryBudget.History(body,cancel);
        var ids=contents.ToArray();var views=ModelViewFollowers.Resolve(body,ids,cancel);ModelViewFollowers.Verify(body,views,cancel);var external=ProjectedHistorySources.Read(body,ids,state,tx,cancel);ProjectedHistorySources.Healthy(ids,external);
        var allProjections=ids.Select(id=>id.GetObjectOfType<ProjectionSketchEnt>()).Where(v=>v!=null).ToArray();
        var selectedIds=selected.Values.Select(s=>s.Native).ToHashSet();var selectedProjections=allProjections.Where(p=>selectedIds.Contains(p!.SrcObjectID)).ToArray();
        Need(selectedProjections.Length>=1,"OUT_OF_SCOPE","Expected explicit source projections in one consuming sketch.");
        var touchedSketchId=selectedProjections[0]!.HostSketchID;
        Need(selectedProjections.All(p=>p!.HostSketchID==touchedSketchId),"OUT_OF_SCOPE","Select sources from one consuming sketch of this body per write.");
        Need(allProjections.Where(p=>p!.HostSketchID==touchedSketchId).All(p=>selectedIds.Contains(p!.SrcObjectID)),
            "INCOMPLETE_PROFILE_SELECTION","Include every projected source of the affected sketch.");
        var sketches=ids.Where(id=>id.GetObjectOfType<PlanarSketch>()!=null).ToHashSet();
        var profiles=ids.Select(id=>id.GetObjectOfType<SketchProfile>()).Where(p=>p!=null).ToDictionary(p=>p!.ID,p=>p!);
        var placements=ModelPlacementHistory.Ids(ids);
        var followers=ids.Where(id=>id.GetObjectOfType<ThicknessFeature>()!=null||id.GetObjectOfType<RectangularPatternFeature>()!=null||
            id.GetObjectOfType<FilletFeature>()!=null||id.GetObjectOfType<ChamferFeature>()!=null).ToArray();
        foreach(var id in followers)
        {
            cancel();
            var item=id.GetObjectOfType<HistoryItem>();
            Need(item!=null&&item.HostSolidID==body.ID&&!item.IsSuppressed&&item.Status.ToString()=="Ok","MODEL_STATE_UNAVAILABLE","Downstream feature must remain healthy and owned by this body.");
            if(id.GetObjectOfType<ThicknessFeature>() is {} shell)
                Need(shell.SurfaceId.IsNull&&shell.Direction==FeatureExtentDirection.Negative,"OUT_OF_SCOPE","Source-driven profile editing supports inward solid shells.");
            if(id.GetObjectOfType<RectangularPatternFeature>() is {} pattern)
                ModelInventoryBudget.Pattern(pattern,cancel);
        }
        var features=ids.Select(id=>id.GetObjectOfType<ExtrudeFeature>()).Where(f=>f!=null).ToDictionary(f=>f!.ID,f=>f!);
        Need(features.Count>=1&&profiles.Count==features.Count&&sketches.Count==features.Count,"OUT_OF_SCOPE","Require an additive base with distinct sketch/profile/extrusion bindings.");
        var profileSketch=new Dictionary<McObjectId,McObjectId>();
        foreach(var p in profiles.Values)
        {
            var deps=p.GetDependsOn(false,false);Need(deps!=null,"HISTORY_UNAVAILABLE","Profile dependencies unavailable.");var candidates=deps.Where(sketches.Contains).Distinct().ToArray();
            Need(candidates.Length==1&&p.HostSolidID==body.ID,"OUT_OF_SCOPE","Each profile must reference exactly one sketch of this body.");profileSketch[p.ID]=candidates[0];
        }
        Need(profileSketch.Values.Distinct().Count()==sketches.Count&&features.Values.Select(f=>f.ProfileID).Distinct().Count()==profiles.Count&&features.Values.All(f=>profiles.ContainsKey(f.ProfileID)),"OUT_OF_SCOPE","Profiles/sketches must bind one-to-one with features.");
        foreach(var f in features.Values)StraightExtrusion(f,body);
        // Rebuild may normalize an extrusion's predecessor to its own sketch.
        // Follow the actual predecessor links; never infer order from contents.
        var links=ids.Select(id=>{var node=id.GetObjectOfType<HistoryItem>();var previous=node?.OrderObjBefore??McObjectId.Null;return new ModelSequenceLink(Key(id),previous.IsNull?null:Key(previous));}).ToArray();
        var possible=new List<(ExtrudeFeature Root,string[] Order)>();
        foreach(var candidate in features.Values.Where(f=>f.PartOperation==PartFeatureOperation.Join))
            try{cancel();possible.Add((candidate,ModelFeatureSequence.FromHistory(links,features.Keys.Concat(placements).Concat(followers).Select(Key).ToArray(),Key(candidate.ID),Key(views.LastModelItem),cancel)));}
            catch(DomainException e)when(e.Error.code=="MODEL_ORDER_UNAVAILABLE"){}
        Need(possible.Count==1,"MODEL_ORDER_UNAVAILABLE","Cannot identify one additive base and its ordered following features.");
        var root=possible[0].Root;var order=possible[0].Order;var rootSketchId=profileSketch[root.ProfileID];
        var projections=allProjections.Where(p=>p!.HostSketchID==rootSketchId).ToArray();
        var tailProjections=allProjections.Except(projections).ToArray();
        Need(allProjections.Select(p=>p!.SrcObjectID).Distinct().Count()==allProjections.Length&&
            tailProjections.All(p=>sketches.Contains(p!.HostSketchID)),"OUT_OF_SCOPE","Retained source projections must be distinct and belong to this body's sketches.");
        var allowed=allProjections.Select(p=>p!.ID).Concat(sketches).Concat(profiles.Keys).Concat(features.Keys).Concat(placements).Concat(followers)
            .Concat(views.ViewIds).Concat(ids.Where(id=>external.Any(s=>s.id==Key(id)))).Append(body.ID).ToHashSet();
        Need(ids.Length==allowed.Count&&allowed.SetEquals(ids),"OUT_OF_SCOPE","Additional history items are outside processed extrusion scope.");
        var sketch=rootSketchId.GetObjectOfType<PlanarSketch>();Need(sketch!=null&&sketch.HostSolidID==body.ID&&!sketch.IsInEditMode,"OUT_OF_SCOPE","Base sketch unavailable.");
        var sourceHandles=projections.Select(p=>external.Single(s=>s.id==Key(p!.SrcObjectID)).handle).ToArray();
        var rootFrame=touchedSketchId==rootSketchId&&projections.Length>0&&selected.TryGetValue(sourceHandles[0],out var first)
            ?first.Workplane:DrawingWorkplaneNative.FromSketch(sketch);
        double height=root.Distance*DrawingWorkplaneNative.Orientation(sketch,rootFrame)*(root.Direction==FeatureExtentDirection.Positive?1:-1);
        var byText=features.Values.ToDictionary(f=>Key(f.ID),StringComparer.OrdinalIgnoreCase);
        // Reuse the public history adapter so retained feature settings and
        // dependencies are checked consistently with direct sketch editing.
        var followerNodes=new Dictionary<string,JsonElement>(StringComparer.OrdinalIgnoreCase);
        if(followers.Length>0)
        {
            var retainedNodes=ModelHistoryEditGuard.FeatureNodes(body,state,followers,cancel);
            foreach(var node in retainedNodes.EnumerateArray())
                if(followers.Any(id=>Key(id)==node.GetProperty("mc_id").GetString()))
                {
                    var value=System.Text.Json.Nodes.JsonNode.Parse(node.GetRawText())!;
                    var parameters=value["parameters"]!.AsObject();
                    if(parameters["edge_ids"] is {} edges){parameters["edge_count"]=edges.AsArray().Count;parameters.Remove("edge_ids");}
                    followerNodes.Add(node.GetProperty("mc_id").GetString()!,Wire.Element(value));
                }
            Need(followerNodes.Count==followers.Length,"HISTORY_UNAVAILABLE","Downstream feature settings unavailable.");
        }
        object TailNode(string id)
        {
            cancel();
            if(followerNodes.TryGetValue(id,out var follower))return new{id,kind="retained_feature",history=follower};
            if(!byText.TryGetValue(id,out var f))
                return new {id,kind="placement",placement=ModelPlacementHistory.Read(placements.Single(p=>Key(p)==id))};
            var sketchId=profileSketch[f.ProfileID];
            var tailSketch=sketchId.GetObjectOfType<PlanarSketch>();
            Need(tailSketch!=null&&tailSketch.HostSolidID==body.ID&&!tailSketch.IsInEditMode,"MODEL_STATE_UNAVAILABLE","Tail sketch is unavailable.");
            var geometry=Wire.Element(SketchGeometryReader.Read(tailSketch,cancel));
            Need(geometry.GetProperty("geometry_details_complete").GetBoolean(),"HISTORY_UNAVAILABLE","Tail sketch geometry is incomplete.");
            var elements=geometry.GetProperty("elements").EnumerateArray().ToArray();
            Need(elements.Length>=1,"OUT_OF_SCOPE","Tail sketch has no native curve inventory.");
            return new {id,kind="extrusion",profile=Key(f.ProfileID),sketch=Key(sketchId),
                operation=f.PartOperation.ToString(),distance=f.Distance,direction=f.Direction.ToString(),
                plane=geometry.GetProperty("frame"),
                element_ids=elements.Select(e=>new{id=e.GetProperty("element_id").GetString(),type=e.GetProperty("geometry_type").GetString()}).ToArray(),
                geometry=elements.Select(e=>e.GetProperty("geometry")).ToArray()};
        }
        var fingerprintBudget=new ModelInventoryBudget(cancel);
        var tail=order.Skip(1).Select(id=>{var item=TailNode(id);fingerprintBudget.Add(item);return item;}).ToArray();
        var fingerprint=Wire.Element(new{contents=ids.Select(Key).OrderBy(v=>v,StringComparer.Ordinal).ToArray(),order,root=Key(root.ID),sketch=Key(rootSketchId),height,
            projections=projections.Select(p=>new{id=Key(p!.ID),source=Key(p.SrcObjectID),sketch=Key(p.HostSketchID)}).OrderBy(p=>p.id,StringComparer.Ordinal).ToArray(),
            tail,views=views.Metadata}).GetRawText();
        using(var storedFingerprint=JsonDocument.Parse(fingerprint))new ModelInventoryBudget(cancel).Add(storedFingerprint.RootElement);
        return new(rootSketchId,root.ID,projections.Select(p=>p!.ID).ToArray(),sourceHandles,height,fingerprint,touchedSketchId);
    }
}
