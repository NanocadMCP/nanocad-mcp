using Multicad;
using System.Text.Json;
using Multicad.DatabaseServices;
using Multicad.Mc3D;
using NanoCAD.Contracts;
using Db=Teigha.DatabaseServices;
using MPoint=Multicad.Geometry.Point3d;
using MPlane=Multicad.Geometry.Plane3d;

namespace NanoCADBridge;

internal static partial class DrawingProfileWriter
{
    private sealed record Consumer(McObjectId Native,Db.ObjectId Id,McObjectId Sketch,McObjectId Feature,McObjectId[] Projections,string[] Sources,double Height,ProcessedBody? Processed=null,RevolvedBody? Revolved=null,RetainedSweepBody? RetainedSweeps=null);
    private static Consumer BindBody(Mc3dSolid body,Db.ObjectId id,Dictionary<string,Source> selected,DocumentState state,Db.Transaction tx,Action cancel)
    {
        var contents=ModelInventoryBudget.History(body,cancel);
        var projections=contents.Select(v=>v.GetObjectOfType<ProjectionSketchEnt>()).Where(v=>v!=null).ToArray();
        var sketches=contents.Select(v=>v.GetObjectOfType<PlanarSketch>()).Where(v=>v!=null).ToArray();
        var profiles=contents.Select(v=>v.GetObjectOfType<SketchProfile>()).Where(v=>v!=null).ToArray();
        var extrusions=contents.Select(v=>v.GetObjectOfType<ExtrudeFeature>()).Where(v=>v!=null).ToArray();
        if(contents.Any(v=>v.GetObjectOfType<ExtrudeAlongPathFeature>()!=null))
        {
            var retained=CaptureRetainedSweeps366(body,id,selected,state,tx,cancel);
            return new(body.ID,id,retained.Sketch,McObjectId.Null,retained.Projections,retained.Sources,0,RetainedSweeps:retained);
        }
        if(contents.Any(v=>v.GetObjectOfType<RevolveFeature>()!=null))
        {
            var model=ReadRevolved(body,selected,state,tx,cancel);var revolvedSolid=(Db.Solid3d)tx.GetObject(id,Db.OpenMode.ForRead);Layer(revolvedSolid,tx);
            ModelFeatureOracle.Read(revolvedSolid);
            return new(body.ID,id,model.Sketch,model.Feature,model.Projections,model.ProfileSources.Append(model.AxisSource).ToArray(),0,Revolved:model);
        }
        if(extrusions.Length>1||contents.Any(v=>v.GetObjectOfType<DrawingView>()!=null)||contents.Any(ModelPlacementHistory.IsPlacement)||contents.Any(v=>
            v.GetObjectOfType<ThicknessFeature>()!=null||v.GetObjectOfType<RectangularPatternFeature>()!=null||
            v.GetObjectOfType<FilletFeature>()!=null||v.GetObjectOfType<ChamferFeature>()!=null))
        {
            var processed=ReadProcessed(body,selected,state,tx,cancel);var processedSolid=(Db.Solid3d)tx.GetObject(id,Db.OpenMode.ForRead);Layer(processedSolid,tx);
            ModelFeatureOracle.Read(processedSolid);
            return new(body.ID,id,processed.Sketch,processed.Feature,processed.Projections,processed.Sources,processed.Height,processed);
        }
        Need(sketches.Length==1&&profiles.Length==1&&extrusions.Length==1&&projections.Length>0,"OUT_OF_SCOPE","Consumer must be one sketch/profile/distance extrusion with explicit source projections.");
        var native=selected.Values.ToDictionary(v=>v.Native);var sources=new List<Source>();var sketch=sketches[0]!;var feature=extrusions[0]!;
        Need(sketch.HostSolidID==body.ID&&!sketch.IsInEditMode&&feature.HostSolidID==body.ID,"OBJECT_IDENTITY_MISMATCH","Consumer history owner mismatch.");
        foreach(var p in projections)
        {Need(p!.HostSketchID==sketch.ID&&native.TryGetValue(p.SrcObjectID,out _),"INCOMPLETE_PROFILE_SELECTION","Include all source curves of each consuming profile.");sources.Add(native[p.SrcObjectID]);}
        var sourceIds=sources.Select(s=>s.Native).ToHashSet();Need(sourceIds.Count==projections.Length,"OUT_OF_SCOPE","Duplicate projections are outside this simple profile scope.");
        var expectedIds=sourceIds.Concat(projections.Select(p=>p!.ID)).Concat(new[]{body.ID,sketch.ID,profiles[0]!.ID,feature.ID}).ToHashSet();
        Need(contents.Count==expectedIds.Count&&expectedIds.SetEquals(contents),"OUT_OF_SCOPE","Additional history features or sketch contents are outside the supported consumer scope.");
        Need(contents.Where(v=>!sourceIds.Contains(v)).All(v=>v.GetObjectOfType<HistoryItem>() is {} item&&item.Status.ToString()=="Ok"&&!item.IsSuppressed),"MODEL_STATE_UNAVAILABLE","Consumer history is not healthy.");
        Need(feature.Extent.ToString()=="Dist"&&feature.Distance>=.001&&feature.Distance<=1e6&&DrawingProfileGeometry.Near(feature.Angle,0)&&DrawingProfileGeometry.Near(feature.Distance2,0)&&feature.PartOperation.ToString()=="Join"&&
            feature.Direction is FeatureExtentDirection.Positive or FeatureExtentDirection.Negative,"OUT_OF_SCOPE","Consumer must be one untapered positive/negative distance extrusion.");
        double height=feature.Distance*DrawingWorkplaneNative.Orientation(sketch,sources[0].Workplane)*(feature.Direction==FeatureExtentDirection.Positive?1:-1);
        var solid=(Db.Solid3d)tx.GetObject(id,Db.OpenMode.ForRead);Layer(solid,tx);
        ModelFeatureOracle.Read(solid);
        return new(body.ID,id,sketch.ID,feature.ID,projections.Select(p=>p!.ID).ToArray(),sources.Select(s=>s.Curve.Id).ToArray(),height);
    }
    private static List<Consumer> Consumers(Dictionary<string,Source> selected,DocumentState state,Db.Transaction tx,Action cancel)
    {
        var native=selected.Values.Select(s=>s.Native).ToHashSet();var result=new List<Consumer>();
        foreach(Db.ObjectId id in (Db.BlockTableRecord)tx.GetObject(Model(state,tx),Db.OpenMode.ForRead))
        {
            cancel();if(tx.GetObject(id,Db.OpenMode.ForRead) is not Db.Solid3d)continue;
            var body=McObjectId.FromHandle(id.Handle.Value,McDocument.ActiveDocument.ID).GetObjectOfType<Mc3dSolid>();if(body==null)continue;
            var contents=ModelInventoryBudget.History(body,cancel);
            if(!contents.Any(native.Contains)&&!contents.Any(v=>v.GetObjectOfType<ProjectionSketchEnt>() is {} projection&&native.Contains(projection.SrcObjectID))&&
                !contents.Any(v=>v.GetObjectOfType<RevolveFeature>() is {} r&&native.Contains(r.Axis.ID)))continue;
            var splineIds=selected.Values.Where(s=>s.Curve.Kind=="spline").Select(s=>s.Native).ToHashSet();
            Need(!contents.Any(v=>splineIds.Contains(v)||v.GetObjectOfType<ProjectionSketchEnt>() is {} p&&splineIds.Contains(p.SrcObjectID)),
                "UNSUPPORTED_SPLINE_CONSUMER","This drawing Spline component drives a native model whose complete curved-profile preservation is not established by the ordinary drawing consumer adapter. Use the owning native sketch route; the constraint write has not started.");
            result.Add(BindBody(body,id,selected,state,tx,cancel));
        }
        return result;
    }
    private static Consumer CreateProfile(DrawingProfileEdit edit,Binding binding,DocumentState state,Action cancel)
    {
        var body=new Mc3dSolid();body.DbEntity.Layer=binding.Selected.Values.First().Layer;body.DbEntity.AddToCurrentDocument();
        var sketch=body.AddPlanarSketch();Need(sketch!=null,"MODEL_UPDATE_FAILED","Sketch creation failed.");sketch.Name=edit.Name+" Sketch";sketch.DbEntity.Visibility=0;McObjectManager.Add2Document(sketch.DbEntity);using(var plane=DrawingWorkplaneNative.Plane(binding.Selected.Values.First().Workplane))sketch.SetPlane(plane);
        foreach(var source in binding.Selected.Values.Where(s=>s.Curve.Id!=edit.AxisHandle))
        {cancel();var p=sketch.ProjectObject(source.Native);Need(p!=null&&p.SrcObjectID==source.Native&&p.HostSketchID==sketch.ID,"MODEL_UPDATE_FAILED","Projection did not retain its exact source/sketch identity.");}
        McObjectManager.UpdateAll();var profile=sketch.CreateProfile();Need(profile!=null,"MODEL_UPDATE_FAILED","Profile creation failed.");profile.DbEntity.Visibility=0;McObjectManager.Add2Document(profile.DbEntity);
        Need(profile.AutoProcessExternalContours(),"MODEL_UPDATE_FAILED","Projected sources did not yield a closed profile.");
        if(edit.Action=="revolve_drawing_profile")
        {var axis=new McGeomParam{ID=binding.Selected[edit.AxisHandle!].Native};double angle=(edit.AngleDegrees??360)*Math.PI/180;var feature=body.AddRevolveFeature(profile.ID,axis,angle);Need(feature!=null,"MODEL_UPDATE_FAILED","Native revolution creation failed.");if(edit.AngleDegrees.HasValue){feature.Extent=RevolveParamExtType.Angle;feature.Direction=FeatureExtentDirection.Positive;feature.Angle=angle;}feature.Name=edit.Name;}
        else
        {bool positive=edit.Direction=="positive";if(DrawingWorkplaneNative.Orientation(sketch,binding.Selected.Values.First().Workplane)<0)positive=!positive;var feature=body.AddExtrudeFeature(profile.ID,edit.Distance!.Value,0,positive?FeatureExtentDirection.Positive:FeatureExtentDirection.Negative);Need(feature!=null,"MODEL_UPDATE_FAILED","Extrusion creation failed.");feature.Name=edit.Name;}
        McObjectManager.UpdateAll();using var tx=state.Document.Database.TransactionManager.StartTransaction();
        var ids=((Db.BlockTableRecord)tx.GetObject(Model(state,tx),Db.OpenMode.ForRead)).Cast<Db.ObjectId>().Where(id=>tx.GetObject(id,Db.OpenMode.ForRead) is Db.Solid3d&&McObjectId.FromHandle(id.Handle.Value,McDocument.ActiveDocument.ID)==body.ID).ToArray();
        Need(ids.Length==1,"OBJECT_IDENTITY_MISMATCH","Created model has no unique native representation.");return BindBody(body,ids[0],binding.Selected,state,tx,cancel);
    }
    private static List<Consumer> VerifyModels(IEnumerable<Consumer> consumers,Dictionary<string,Source> selected,DocumentState state,Action cancel)
    {
        using var tx=state.Document.Database.TransactionManager.StartTransaction();var result=new List<Consumer>();
        var ids=((Db.BlockTableRecord)tx.GetObject(Model(state,tx),Db.OpenMode.ForRead)).Cast<Db.ObjectId>().ToArray();
        foreach(var before in consumers)
        {
            cancel();
            var matches=ids.Where(id=>tx.GetObject(id,Db.OpenMode.ForRead) is Db.Solid3d&&McObjectId.FromHandle(id.Handle.Value,McDocument.ActiveDocument.ID)==before.Native).ToArray();
            Need(matches.Length==1&&(matches[0]==before.Id||!ids.Contains(before.Id)),"OBJECT_IDENTITY_MISMATCH","Cannot identify a unique current model representation.");
            var body=before.Native.GetObjectOfType<Mc3dSolid>();Need(body!=null,"OBJECT_IDENTITY_MISMATCH","Consuming body no longer resolves.");var after=BindBody(body,matches[0],selected,state,tx,cancel);
            Need(after.Native==before.Native&&after.Sketch==before.Sketch&&after.Feature==before.Feature&&after.Projections.ToHashSet().SetEquals(before.Projections)&&after.Sources.ToHashSet().SetEquals(before.Sources)&&DrawingProfileGeometry.Near(after.Height,before.Height),"OBJECT_IDENTITY_MISMATCH","Consumer association, height or history changed unexpectedly.");
            bool same=before.Processed==null&&after.Processed==null;
            if(before.Processed!=null&&after.Processed!=null)
            {
                Need(before.Processed.TouchedSketch==after.Processed.TouchedSketch,"OBJECT_IDENTITY_MISMATCH","Selected consuming sketch changed.");
                JsonElement Protected(ProcessedBody value)
                {
                    var graph=System.Text.Json.Nodes.JsonNode.Parse(value.Fingerprint)!;
                    if(value.TouchedSketch!=value.Sketch)
                        foreach(var item in graph["tail"]!.AsArray())
                            if(item!["kind"]!.GetValue<string>()=="extrusion"&&
                               item["sketch"]!.GetValue<string>().Equals(value.TouchedSketch.ToString(),StringComparison.OrdinalIgnoreCase))
                                item.AsObject().Remove("geometry");
                    return Wire.Element(graph);
                }
                same=ModelParameterSnapshot.Equivalent(Protected(before.Processed),Protected(after.Processed));
            }
            Need(same,"MODEL_STATE_UNAVAILABLE","Ordered feature identities, tail profiles, parameters or planes changed unexpectedly.");
            same=before.Revolved==null&&after.Revolved==null;if(before.Revolved!=null&&after.Revolved!=null){using var a=JsonDocument.Parse(before.Revolved.Fingerprint);using var b=JsonDocument.Parse(after.Revolved.Fingerprint);same=ModelParameterSnapshot.Equivalent(a.RootElement,b.RootElement);}
            Need(same,"MODEL_STATE_UNAVAILABLE","Revolution axis association or ordered history changed unexpectedly.");
            Need((before.RetainedSweeps==null)==(after.RetainedSweeps==null),"OBJECT_IDENTITY_MISMATCH","The native shared-sketch sweep binding route changed during the source edit.");
            if(before.RetainedSweeps!=null&&after.RetainedSweeps!=null)VerifyRetainedSweeps366(before.RetainedSweeps,after.RetainedSweeps,before.Id,after.Id,cancel);
            result.Add(after);
        }
        return result;
    }
}
