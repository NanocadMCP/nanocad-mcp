using System.Text.Json;
using Multicad;
using Multicad.DatabaseServices;
using Multicad.Mc3D;
using NanoCAD.Contracts;
using Db=Teigha.DatabaseServices;
using G=Teigha.Geometry;
using MPoint=Multicad.Geometry.Point3d;

namespace NanoCADBridge;

internal static partial class DrawingProfileWriter
{
    private sealed record RevolvedBody(McObjectId Sketch,McObjectId Feature,McObjectId[] Projections,string[] ProfileSources,string AxisSource,TailFeature[] Tail,string Fingerprint,double Angle);
    private static Db.Solid3d RevolutionTool(IEnumerable<Source> profileSources,Source axis,Action cancel,double angle=2*Math.PI)
    {
        var curves=profileSources.Select(s=>s.Curve).ToArray();var profile=curves.Any(c=>c.Kind=="arc")?ModelPolycurve.FromCurves(curves,cancel):DrawingProfileGeometry.Profile(curves,cancel);
        using Db.Curve contour=profile.Kind=="circle"?new Db.Circle(new G.Point3d(profile.Center!.X,profile.Center.Y,0),G.Vector3d.ZAxis,profile.Radius):new Db.Polyline(profile.Vertices!.Length);
        if(contour is Db.Polyline polygon){for(int i=0;i<profile.Vertices!.Length;i++){cancel();var p=profile.Vertices[i];polygon.AddVertexAt(i,new G.Point2d(p.X,p.Y),profile.Kind=="polycurve"?profile.Bulges![i]:0,0,0);}polygon.Closed=true;}
        var start=new G.Point3d(axis.Curve.Start!.X,axis.Curve.Start.Y,0);var end=new G.Point3d(axis.Curve.End!.X,axis.Curve.End.Y,0);var result=new Db.Solid3d();
        try
        {
            result.RecordHistory=false;using var options=new Db.RevolveOptions();cancel();result.CreateRevolvedSolid(contour,start,(end-start).GetNormal(),angle,0,options);
            ModelFeatureOracle.Read(result);result.TransformBy(DrawingWorkplaneNative.Matrix(axis.Workplane));return result;
        }
        catch{result.Dispose();throw;}
    }
    private static ModelFeatureOracle.Metrics ExpectedRevolved(RevolvedBody model,Dictionary<string,Source> selected,Action cancel)
    {
        using var detached=RevolutionTool(model.ProfileSources.Select(h=>selected[h]),selected[model.AxisSource],cancel,model.Angle);
        foreach(var feature in model.Tail)ModelPlacementHistory.Apply(detached,feature,cancel);return ModelFeatureOracle.Read(detached);
    }
    private static RevolvedBody ReadRevolved(Mc3dSolid body,Dictionary<string,Source> selected,DocumentState state,Db.Transaction tx,Action cancel)
    {
        var contents=ModelInventoryBudget.History(body,cancel);var ids=contents.ToArray();
        var views=ModelViewFollowers.Resolve(body,ids,cancel);ModelViewFollowers.Verify(body,views,cancel);
        var followers=ids.Where(id=>id.GetObjectOfType<FilletFeature>()!=null||id.GetObjectOfType<ChamferFeature>()!=null||id.GetObjectOfType<ThicknessFeature>()!=null||id.GetObjectOfType<RectangularPatternFeature>()!=null).ToArray();
        var retained=ModelHistoryEditGuard.FeatureNodes(body,state,followers,cancel);
        var external=ProjectedHistorySources.Read(body,ids,state,tx,cancel);ProjectedHistorySources.Healthy(ids,external);
        var roots=ids.Select(id=>id.GetObjectOfType<RevolveFeature>()).Where(f=>f!=null).ToArray();Need(roots.Length==1,"OUT_OF_SCOPE","One base revolution is supported.");var root=roots[0]!;
        Need(root.HostSolidID==body.ID&&root.PartOperation==PartFeatureOperation.Join&&double.IsFinite(root.Angle)&&DrawingProfileGeometry.Near(root.Angle2,0)&&
            root.Direction==FeatureExtentDirection.Positive&&(root.Extent.ToString()=="All"||root.Extent==RevolveParamExtType.Angle&&root.Angle>=Math.PI/1800&&root.Angle<=2*Math.PI),"OUT_OF_SCOPE","Use one positive additive All/Angle revolution without a second extent.");
        var native=selected.Values.ToDictionary(s=>s.Native);Need(native.TryGetValue(root.Axis.ID,out var axis)&&axis!.Curve.Kind=="line","INCOMPLETE_PROFILE_SELECTION","Include the exact separate native axis Line as well as all projected profile sources.");
        var sketches=ids.Where(id=>id.GetObjectOfType<PlanarSketch>()!=null).ToHashSet();var profiles=ids.Select(id=>id.GetObjectOfType<SketchProfile>()).Where(p=>p!=null).ToDictionary(p=>p!.ID,p=>p!);
        var placements=ModelPlacementHistory.Ids(ids);
        var tails=ids.Select(id=>id.GetObjectOfType<ExtrudeFeature>()).Where(f=>f!=null).ToDictionary(f=>f!.ID,f=>f!);Need(profiles.Count==tails.Count+1&&sketches.Count==profiles.Count,"OUT_OF_SCOPE","Each following fixed-profile join/cut extrusion must have its own profile and sketch.");
        var profileSketch=new Dictionary<McObjectId,McObjectId>();
        foreach(var p in profiles.Values){var deps=p.GetDependsOn(false,false);Need(deps!=null,"HISTORY_UNAVAILABLE","Profile dependencies unavailable.");var matches=deps.Where(sketches.Contains).Distinct().ToArray();Need(p.HostSolidID==body.ID&&matches.Length==1,"OUT_OF_SCOPE","Each profile must use one sketch of this body.");profileSketch.Add(p.ID,matches[0]);}
        Need(profiles.ContainsKey(root.ProfileID)&&profileSketch.Values.Distinct().Count()==sketches.Count&&tails.Values.Select(f=>f.ProfileID).Append(root.ProfileID).ToHashSet().SetEquals(profiles.Keys),"OUT_OF_SCOPE","Feature/profile/sketch association is not one-to-one.");
        var sketchId=profileSketch[root.ProfileID];var sketch=sketchId.GetObjectOfType<PlanarSketch>();Need(sketch!=null&&sketch.HostSolidID==body.ID&&!sketch.IsInEditMode,"OUT_OF_SCOPE","Base revolution sketch unavailable.");
        DrawingWorkplaneNative.Match(sketch,axis!.Workplane);
        var allProjections=ids.Select(id=>id.GetObjectOfType<ProjectionSketchEnt>()).Where(p=>p!=null).ToArray();
        var projections=allProjections.Where(p=>p!.HostSketchID==sketchId).ToArray();
        Need(projections.Length>=1,"OUT_OF_SCOPE","Require explicit base-profile source projections.");
        var sources=new List<Source>();foreach(var p in projections){Need(native.TryGetValue(p!.SrcObjectID,out var source)&&source!.Native!=axis!.Native,"INCOMPLETE_PROFILE_SELECTION","Include every profile source; axis must be a separate Line.");sources.Add(source!);}
        Need(sources.Select(s=>s.Native).Distinct().Count()==projections.Length,"OUT_OF_SCOPE","Duplicate projections are unsupported.");
        var tailProjections=allProjections.Except(projections).ToArray();
        Need(tailProjections.All(p=>sketches.Contains(p!.HostSketchID)&&!native.ContainsKey(p.SrcObjectID))&&allProjections.Select(p=>p!.SrcObjectID).Distinct().Count()==allProjections.Length,"OUT_OF_SCOPE","Tail projections must have distinct retained sources outside the selected drawing network.");
        var allowed=sources.Select(s=>s.Native).Concat(allProjections.Select(p=>p!.ID)).Concat(tailProjections.Select(p=>p!.SrcObjectID)).Concat(sketches).Concat(profiles.Keys).Concat(tails.Keys).Concat(placements).Concat(followers).Concat(views.ViewIds).Concat(ids.Where(id=>external.Any(s=>s.id==Key(id)))).Append(root.ID).Append(body.ID).ToHashSet();if(contents.Contains(axis!.Native))allowed.Add(axis.Native);
        Need(allowed.SetEquals(ids),"OUT_OF_SCOPE","Additional history types are outside full-revolution scope.");foreach(var f in tails.Values)StraightExtrusion(f,body);
        var links=ids.Select(id=>{var n=id.GetObjectOfType<HistoryItem>();var p=n?.OrderObjBefore??McObjectId.Null;return new ModelSequenceLink(Key(id),p.IsNull?null:Key(p));}).ToList();if(!contents.Contains(axis.Native))links.Add(new(Key(axis.Native),null));
        var order=ModelFeatureSequence.FromHistory(links.ToArray(),tails.Keys.Concat(placements).Concat(followers).Append(root.ID).Select(Key).ToArray(),Key(root.ID),Key(views.LastModelItem),cancel);var byText=tails.Values.ToDictionary(f=>Key(f.ID),StringComparer.OrdinalIgnoreCase);
        var tail=order.Skip(1).Where(id=>!followers.Any(f=>Key(f)==id)).Select(id=>{cancel();if(!byText.TryGetValue(id,out var f))return ModelPlacementHistory.Step(placements.Single(p=>Key(p)==id));return ReadTail(f,profileSketch[f.ProfileID],cancel);}).ToArray();
        var fingerprint=Wire.Element(new{contents=ids.Select(Key).OrderBy(v=>v,StringComparer.Ordinal).ToArray(),order,retained,views=views.Metadata,root=Key(root.ID),sketch=Key(sketchId),profile=Key(root.ProfileID),axis=Key(axis.Native),root.IsFull,root.Angle,root.Angle2,extent=root.Extent.ToString(),extent2=root.Extent2.ToString(),direction=root.Direction.ToString(),
            projections=allProjections.Select(p=>new{id=Key(p!.ID),source=Key(p.SrcObjectID),sketch=Key(p.HostSketchID)}).OrderBy(p=>p.id,StringComparer.Ordinal).ToArray(),tail=tail.Select(t=>new{id=Key(t.Feature),profile=Key(t.Profile),sketch=Key(t.Sketch),t.Operation,t.Definition,t.GeometryIdentity,t.Placement}).ToArray()}).GetRawText();
        using(var storedFingerprint=JsonDocument.Parse(fingerprint))new ModelInventoryBudget(cancel).Add(storedFingerprint.RootElement);
        return new(sketchId,root.ID,projections.Select(p=>p!.ID).ToArray(),sources.Select(s=>s.Curve.Id).ToArray(),axis.Curve.Id,tail,fingerprint,root.Extent.ToString()=="All"?2*Math.PI:root.Angle);
    }

    private static (RevolvedBody Model,Dictionary<string,Source> Selected) ResolveRevolutionEdit(Mc3dSolid body,DocumentState state,Db.Transaction tx,Action cancel,IReadOnlyDictionary<McObjectId,double>? distances,IReadOnlyDictionary<McObjectId,ModelPlacement>? placementChanges)
    {
        var contents=ModelInventoryBudget.History(body,cancel);
        var roots=contents.Select(id=>id.GetObjectOfType<RevolveFeature>()).Where(f=>f!=null).ToArray();Need(roots.Length==1,"OUT_OF_SCOPE","Select one base revolution.");var root=roots[0]!;
        var profile=root.ProfileID.GetObjectOfType<SketchProfile>();var dependencies=profile?.GetDependsOn(false,false);Need(dependencies!=null,"HISTORY_UNAVAILABLE","Base profile unavailable.");
        var sketches=dependencies.Where(id=>id.GetObjectOfType<PlanarSketch>()!=null).ToArray();Need(sketches.Length==1,"OUT_OF_SCOPE","Unique base sketch required.");
        var wanted=contents.Select(id=>id.GetObjectOfType<ProjectionSketchEnt>()).Where(p=>p!=null&&p.HostSketchID==sketches[0]).Select(p=>p!.SrcObjectID).Append(root.Axis.ID).ToHashSet();
        var selected=new Dictionary<string,Source>(StringComparer.OrdinalIgnoreCase);
        var entities=((Db.BlockTableRecord)tx.GetObject(Model(state,tx),Db.OpenMode.ForRead)).Cast<Db.ObjectId>().ToArray();
        
        var circular=entities.Where(id=>!id.IsErased&&wanted.Contains(McObjectId.FromHandle(id.Handle.Value,McDocument.ActiveDocument.ID))).Select(id=>tx.GetObject(id,Db.OpenMode.ForRead)).OfType<Db.Entity>().FirstOrDefault(e=>e is Db.Arc or Db.Circle);
        var frame=DrawingWorkplaneNative.FromSketch(sketches[0].GetObjectOfType<PlanarSketch>()!,circular);
        foreach(Db.ObjectId id in (Db.BlockTableRecord)tx.GetObject(Model(state,tx),Db.OpenMode.ForRead))
        {
            cancel();
            if(id.IsErased||!wanted.Contains(McObjectId.FromHandle(id.Handle.Value,McDocument.ActiveDocument.ID)))continue;
            var source=SourceOf(id,state,tx,false,frame);selected.Add(source.Curve.Id,source);
        }
        Need(selected.Count==wanted.Count,"OBJECT_IDENTITY_MISMATCH","Every retained base source and axis must resolve uniquely.");
        var model=ReadRevolved(body,selected,state,tx,cancel);
        if(distances!=null)
        {
            Need(distances.Keys.All(id=>model.Tail.Any(f=>f.Feature==id&&f.Placement==null)),"OUT_OF_SCOPE","Every changed distance must match retained revolution processing.");
            model=model with{Tail=model.Tail.Select(f=>distances.TryGetValue(f.Feature,out var value)?f with{Definition=f.Definition with{Distance=value}}:f).ToArray()};
        }
        if(placementChanges!=null)
        {
            Need(placementChanges.Keys.All(id=>model.Tail.Any(f=>f.Feature==id&&f.Placement!=null)),"OUT_OF_SCOPE","Every changed placement must match retained revolution history.");
            model=model with{Tail=model.Tail.Select(f=>placementChanges.TryGetValue(f.Feature,out var placement)?f with{Placement=placement}:f).ToArray()};
        }
        return(model,selected);
    }
    internal static void ValidateRevolutionChain(Mc3dSolid body,DocumentState state,Db.Transaction tx,Action cancel,IReadOnlyDictionary<McObjectId,double>? distances=null)
    {
        // Admission depends on retained native sources, axis and feature order.
        // A detached solid is diagnostic and need not duplicate the CAD kernel.
        ResolveRevolutionEdit(body,state,tx,cancel,distances,null);
    }
    internal static ModelFeatureOracle.Metrics PreviewRevolutionAngle(Mc3dSolid body,DocumentState state,Db.Transaction tx,double angle,Action cancel,IReadOnlyDictionary<McObjectId,double>? distances=null,IReadOnlyDictionary<McObjectId,ModelPlacement>? placementChanges=null)
    {
        var(model,selected)=ResolveRevolutionEdit(body,state,tx,cancel,distances,placementChanges);
        return ExpectedRevolved(model with{Angle=angle},selected,cancel);
    }
}
