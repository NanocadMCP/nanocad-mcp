using System.Text.Json;
using System.Globalization;
using Multicad.DatabaseServices.Constraints;
using Multicad;
using Multicad.DatabaseServices;
using Multicad.Mc3D;
using NanoCAD.Contracts;
using NanoCAD.Ipc;
using Teigha.DatabaseServices;
using EntityGeomType=Multicad.Geometry.EntityGeomType;

namespace NanoCADBridge;

internal static class ModelManufacturingWriter
{
    internal static bool Handles(ModifyArguments a)=>a.Operations.Any(e=>e is ModelManufacturingEdit);
    static void Need([System.Diagnostics.CodeAnalysis.DoesNotReturnIf(false)] bool ok,string code,string message){if(!ok)throw new DomainException(code,message);}
    static bool Same(McObjectId id,string value)=>id.ToString().Equals(value,StringComparison.OrdinalIgnoreCase);
    static double N(JsonElement d,string k)=>d.GetProperty(k).GetDouble();
    static bool B(JsonElement d,string k)=>d.GetProperty(k).GetBoolean();
    static string S(JsonElement d,string k)=>d.GetProperty(k).GetString()!;
    static double R(JsonElement d,string k)=>N(d,k)*Math.PI/180;
    // GetGCSElement may initialize a native datum: call only inside a started write.
    static McObjectId ReferencePlane(string name)=>McDocumentsManager.GetActiveSheet().Get3dHistory()
        .GetGCSElement(name switch{"XY"=>GCSElementType.WPL_XY,"YZ"=>GCSElementType.WPL_YZ,_=>GCSElementType.WPL_ZX}).ID;
    sealed record ProfileBinding410(SketchProfile Profile,McDocument Owner,ModelProfileConsumers410.Snapshot Before);
    sealed record Binding(Mc3dSolid Body,ObjectId Source,string Layer,McObjectId[] Contents,ModelViewFollowers.Snapshot Views,
        McObjectId Feature,McObjectId Face,McObjectId Edge1,McObjectId Edge2,McObjectId Plane,McObjectId[] Sources,HashSet<ObjectId> Inventory,HashSet<string> ExternalSources,Dictionary<McObjectId,string> RetainedDriverExpressions,ModelDatumPlaneMirror353.Binding? Datum=null,ProfileBinding410[]? ProfileConsumers=null,Dictionary<McObjectId,bool>? Suppression=null,ModelHoleParameter412.NumericEdit? HoleOffsets=null);
    static ObjectId Space(Database db,Transaction tx)=>((BlockTable)tx.GetObject(db.BlockTableId,OpenMode.ForRead))[BlockTableRecord.ModelSpace];
    static HashSet<ObjectId> Inventory(Database db,Transaction tx)=>((BlockTableRecord)tx.GetObject(Space(db,tx),OpenMode.ForRead)).Cast<ObjectId>().ToHashSet();
    static Binding Resolve(ModelManufacturingEdit edit,ModifyArguments args,DocumentState state,Transaction tx,Action? cancel=null)
    {
        Need(args.Operations.Length==1,"INVALID_ARGUMENT","One native history operation per request.");
        var db=state.Document.Database;var active=McDocument.ActiveDocument;
        Need(db.CurrentSpaceId==Space(db,tx),"OUT_OF_SCOPE","Activate Model Space.");
        Need(state.Document.IsNamedDrawing&&active!=null&&!string.IsNullOrWhiteSpace(active.Path)&&string.Equals(Path.GetFullPath(active.Path),Path.GetFullPath(state.Document.Name),StringComparison.OrdinalIgnoreCase),"DOCUMENT_CONTEXT_MISMATCH","Use the exact active named drawing.");
        var source=CadStructureWriter.Find(db,edit.Handles.Single());
        Need(tx.GetObject(source,OpenMode.ForRead) is Solid3d,"UNSUPPORTED_ENTITY_TYPE","Select a native model body.");
        var entity=(Solid3d)tx.GetObject(source,OpenMode.ForRead);
        Need(entity.OwnerId==db.CurrentSpaceId&&!entity.IsNull,"OUT_OF_SCOPE","Use a nonempty top-level body.");
        var layer=(LayerTableRecord)tx.GetObject(entity.LayerId,OpenMode.ForRead);
        Need(!layer.IsLocked&&!layer.IsOff&&!layer.IsFrozen&&!layer.IsDependent,"LAYER_UNAVAILABLE","Body layer is unavailable.");
        var id=McObjectId.FromHandle(source.Handle.Value,active!.ID);var body=id.GetObjectOfType<Mc3dSolid>();
        Need(body!=null&&body.ID==id&&Same(body.LastPartItemID,edit.ExpectedLastItemId),"PRECONDITION_FAILED","Body or history anchor changed.");
        var contents=ModelInventoryBudget.History(body!,cancel).ToArray();Need(contents.Length>0,"HISTORY_UNAVAILABLE","Native history is unavailable.");
        var views=ModelViewFollowers.Resolve(body,contents,cancel,includeStandalone:true);
        var projected=ProjectedHistorySources.Read(body,contents,state,tx,cancel);
        ProjectedHistorySources.Healthy(contents!,projected,edit.Kind=="set_model_hole"?edit.FeatureId:null);
        var external=projected.Select(x=>x.id).ToHashSet(StringComparer.OrdinalIgnoreCase);
        external.UnionWith(NativePathSources.Read(contents!,cancel));
        var feature=McObjectId.Null;var face=McObjectId.Null;var edge1=McObjectId.Null;var edge2=McObjectId.Null;var plane=McObjectId.Null;McObjectId[] sources=[];
        var d=edit.Definition;bool add=edit.Kind.StartsWith("add_");
        if(add)ModelViewFollowers.PrepareInsertion(views,true);
        else {
            feature=contents!.SingleOrDefault(x=>Same(x,edit.FeatureId!));var h=feature.IsNull?null:feature.GetObjectOfType<HistoryItem>();
            Need(h!=null&&h.HostSolidID==id&&(h.Status.ToString()=="Ok"||ProjectedHistorySources.HealthySuppressed(h)||(edit.Kind=="set_model_hole"&&!h.IsSuppressed&&h.Status.ToString()=="NeedUpdate")),"PRECONDITION_FAILED","Select a healthy feature owned by this body; intentional suppression will remain stored.");
            bool valid=edit.Kind switch{"set_model_sketch_frame" or "set_model_sketch_support"=>feature.GetObjectOfType<PlanarSketch>()!=null,"set_model_extrusion_extent"=>feature.GetObjectOfType<ExtrudeFeature>()!=null,"set_model_loft"=>feature.GetObjectOfType<LoftFeature>()!=null,"set_model_sweep"=>feature.GetObjectOfType<ExtrudeAlongPathFeature>()!=null,"set_model_hole"=>feature.GetObjectOfType<HoleFeature>()!=null,"set_model_thread"=>feature.GetObjectOfType<ThreadFeature>()!=null,"set_model_circular_pattern"=>feature.GetObjectOfType<CircularPatternFeature>()!=null,_=>feature.GetObjectOfType<MirrorFeature>()!=null};
            Need(valid,"INVALID_ARGUMENT","Feature type differs from requested editor.");
            if(edit.Kind=="set_model_circular_pattern") {
                var p=feature.GetObjectOfType<CircularPatternFeature>()!;
                using var manager=body.DbEntity.Document.GetDocumentVariablesManager(Constraint2dNetType.MCS);
                var info=manager?.GetVariableInfo(p.AngleParamVarID);
                Need(manager!=null&&!p.AngleParamVarID.IsNull&&manager.GetVariableHostObjectId(p.AngleParamVarID)==feature&&info!=null&&double.TryParse(info.Expression,NumberStyles.Float,CultureInfo.InvariantCulture,out _),"MODEL_STATE_UNAVAILABLE","Read the owning angular expression before replacing a formula-driven pattern angle.");
            }
        }
        if(edit.Kind=="set_model_sketch_frame"){
            var snapshot=ModelEdgeReader.Capture(source,state,args.BridgeId,CancellationToken.None,cancel);
            Need(snapshot.edge_token==S(d,"edge_token"),"STALE_TOPOLOGY","Read current model edges before attaching the sketch frame.");
            McObjectId Find(string key){var found=snapshot.edges.SingleOrDefault(x=>Same(McObjectId.FromGsMarker(id,long.Parse(x.marker,CultureInfo.InvariantCulture)),S(d,key)));Need(found!=null,"SUBENTITY_NOT_FOUND","Frame edge is not current.");return McObjectId.FromGsMarker(id,long.Parse(found!.marker,CultureInfo.InvariantCulture));}
            edge1=Find("origin_edge_id");edge2=Find("axis_edge_id");
            ModelSketchFrame.Preflight(feature.GetObjectOfType<PlanarSketch>()!,edge1,edge2,d);
        }
        if(edit.Kind=="set_model_extrusion_extent"){
            var extrusion=feature.GetObjectOfType<ExtrudeFeature>()!;
            ModelExtrusionExtentEditor.Validate(extrusion,d);
            Need(S(d,"extent")!="through_all"||extrusion.PartOperation==PartFeatureOperation.Cut,"INVALID_GEOMETRY","Through-all requires a subtractive extrusion with material to cut.");
            if(S(d,"extent")=="to_face"){
                var targetSource=CadStructureWriter.Find(db,S(d,"target_body_handle"));
                var snapshot=Wire.Element(ModelFaceReader.Capture(targetSource,state,args.BridgeId,CancellationToken.None,cancel));
                Need(S(snapshot,"face_token")==S(d,"face_token"),"STALE_TOPOLOGY","Read current target model faces.");
                var chosen=snapshot.GetProperty("faces").EnumerateArray().SingleOrDefault(x=>S(x,"model_face_id").Equals(S(d,"face_id"),StringComparison.OrdinalIgnoreCase));
                Need(chosen.ValueKind==JsonValueKind.Object,"SUBENTITY_NOT_FOUND","Requested terminal face is not current.");
                var targetBody=McObjectId.FromHandle(targetSource.Handle.Value,active!.ID);
                face=McObjectId.FromGsMarker(targetBody,long.Parse(S(chosen,"marker"),CultureInfo.InvariantCulture));
            }
        }
        if(add&&d.TryGetProperty("face_id",out _)) {
            var snapshot=Wire.Element(ModelFaceReader.Capture(source,state,args.BridgeId,CancellationToken.None,cancel));
            Need(S(snapshot,"face_token")==S(d,"face_token"),"STALE_TOPOLOGY","Read current native model faces.");
            var matches=snapshot.GetProperty("faces").EnumerateArray().Where(f=>S(f,"model_face_id").Equals(S(d,"face_id"),StringComparison.OrdinalIgnoreCase)).ToArray();
            Need(matches.Length==1,"SUBENTITY_NOT_FOUND","Selected face is not current.");var chosen=matches[0];
            face=McObjectId.FromGsMarker(id,long.Parse(S(chosen,"marker"),System.Globalization.CultureInfo.InvariantCulture));
            Need(edit.Kind.EndsWith("_hole")?chosen.GetProperty("plane").ValueKind==JsonValueKind.Object:chosen.TryGetProperty("cylinder",out var c)&&c.ValueKind==JsonValueKind.Object,"UNSUPPORTED_FACE_TYPE","Hole requires a planar support; thread requires a cylindrical face.");
        }
        if(add&&d.TryGetProperty("edge_token",out _)) {
            var edges=ModelEdgeReader.Capture(source,state,args.BridgeId,CancellationToken.None,cancel);
            Need(edges.edge_token==S(d,"edge_token"),"STALE_TOPOLOGY","Read current native model edges.");
            McObjectId Edge(string key){var found=edges.edges.SingleOrDefault(e=>e.model_edge_id.Equals(S(d,key),StringComparison.OrdinalIgnoreCase));Need(found!=null,"SUBENTITY_NOT_FOUND","Selected edge is not current.");return McObjectId.FromGsMarker(id,long.Parse(found!.marker,System.Globalization.CultureInfo.InvariantCulture));}
            if(edit.Kind.EndsWith("_hole")) {
                edge1=Edge("first_edge_id");edge2=Edge("second_edge_id");var onFace=Service.GetLinkedFEVsToObject(face,EntityGeomType.kLine);
                Need(onFace!=null&&onFace.Contains(edge1)&&onFace.Contains(edge2),"INVALID_ARGUMENT","Both straight datum edges must belong to the support face.");
                var a=Service.GetFEVGeometry(edge1).LineSeg;var b=Service.GetFEVGeometry(edge2).LineSeg;
                Need(a!=null&&b!=null&&!a.IsParallelTo(b,Service.Tol3d),"INVALID_GEOMETRY","Hole datum edges must not be parallel.");
            } else if(edit.Kind.EndsWith("_thread")) {
                edge1=Edge("front_edge_id");var onFace=Service.GetLinkedFEVsToObject(face,EntityGeomType.kCircArc);
                Need(onFace!=null&&onFace.Contains(edge1),"INVALID_ARGUMENT","Choose a circular boundary edge of the thread face as its start.");
            } else {edge1=Edge("axis_edge_id");var e=edges.edges.Single(x=>Same(edge1,x.model_edge_id));Need(e.circle==null&&e.length>0&&Service.GetFEVGeometry(edge1).GeometryType==EntityGeomType.kLine,"INVALID_ARGUMENT","Pattern axis must be a straight native model edge.");}
        }
        if(add&&d.TryGetProperty("source_feature_ids",out var array)) {
            sources=array.EnumerateArray().Select(v=>contents!.SingleOrDefault(x=>Same(x,v.GetString()!))).ToArray();
            foreach(var f in sources){var h=f.IsNull?null:f.GetObjectOfType<FeatureEntity>();Need(h!=null&&h.HostSolidID==id&&!h.IsSuppressed&&h.Status.ToString()=="Ok","INVALID_ARGUMENT","Pattern sources must be healthy features of this body.");}
        }
        ModelDatumPlaneMirror353.Binding? datum=null;
        if(edit.Kind=="add_model_mirror"&&d.TryGetProperty("datum_plane_id",out _))
        {
            datum=ModelDatumPlaneMirror353.Resolve(S(d,"datum_plane_id"),S(d,"expected_datum_token"),body,args,state,tx,cancel??(()=>{}),views);
            plane=datum.Datum.Plane.ID;
        }
        if(edit.Kind.EndsWith("_mirror")&&d.TryGetProperty("plane_id",out _)) {
            plane=contents!.FirstOrDefault(x=>Same(x,S(d,"plane_id")));
            Need(!plane.IsNull&&plane.GetObjectOfType<WorkPlane>()!=null,"INVALID_ARGUMENT","Use a native work-plane ID from model history, or reference_plane for a document datum.");
        }
        if(edit.Kind.EndsWith("_loft")) {
            sources=d.GetProperty("profile_ids").EnumerateArray().Select(p=>contents!.SingleOrDefault(x=>Same(x,p.GetString()!))).ToArray();
            foreach(var profile in sources)Need(!profile.IsNull&&profile.GetObjectOfType<SketchProfile>() is {} sp&&sp.HostSolidID==id,"INVALID_ARGUMENT","Loft profiles must belong to this body.");
            if(!add)Need(feature.GetObjectOfType<LoftFeature>()!.LoftType==LoftType.Regular,"INVALID_ARGUMENT","This section editor preserves regular lofts; guided loft must retain its guide bindings.");
        }
        if(edit.Kind.EndsWith("_sweep")) {
            if(!add)ModelSweepFactory.ValidateTaper(feature.GetObjectOfType<ExtrudeAlongPathFeature>()!,N(d,"taper_angle"),ModelExtrusionExtentEditor.ReplacesFormulas(d));
            var profile=contents!.SingleOrDefault(x=>Same(x,S(d,"profile_id")));
            Need(!profile.IsNull&&profile.GetObjectOfType<SketchProfile>() is {} sketchProfile&&sketchProfile.HostSolidID==id,"INVALID_ARGUMENT","Sweep profile must belong to the selected body's native history.");
            var paths=ModelSweepFactory.ResolvePaths(d.GetProperty("path_handles").EnumerateArray().Select(x=>x.GetString()!).ToArray(),state,tx);
            sources=new[]{profile}.Concat(paths).ToArray();
        }
        var profileConsumers=new List<ProfileBinding410>();
        if(edit.Kind.EndsWith("_loft")||edit.Kind.EndsWith("_sweep"))
        {
            var selected=sources.Where(x=>x.GetObjectOfType<SketchProfile>()!=null).ToList();
            if(!add)
            {
                var oldSweep=feature.GetObjectOfType<ExtrudeAlongPathFeature>();if(oldSweep!=null)selected.Add(oldSweep.ProfileID);
                var oldLoft=feature.GetObjectOfType<LoftFeature>();
                if(oldLoft!=null){selected.Add(oldLoft.ProfileID);for(int index=0;index<oldLoft.SectionsCount;index++){cancel?.Invoke();var section=oldLoft.GetSection(index);Need(section!=null,"PROFILE_DISCOVERY_UNAVAILABLE","A retained loft section is unavailable.");selected.Add(section!.ID);}}
            }
            foreach(var profileId in selected.Distinct())
            {
                cancel?.Invoke();var profile=profileId.GetObjectOfType<SketchProfile>();Need(profile!=null,"PROFILE_DISCOVERY_UNAVAILABLE","An actual old/new selected profile is unavailable.");
                var owner=profile!.DbEntity.Document;Need(owner!=null&&owner.ID==body!.DbEntity.Document.ID,"PROFILE_OWNERSHIP_MISMATCH","Selected profiles must belong to this actual native owner document.");
                var consumers=ModelProfileConsumers410.Capture(profile,owner,cancel??(()=>{}));ModelProfileConsumers410.RequireBody(consumers,body);
                profileConsumers.Add(new(profile,owner,consumers));
            }
        }
        var suppression=contents.Where(x=>x.GetObjectOfType<HistoryItem>()!=null).ToDictionary(x=>x,x=>x.GetObjectOfType<HistoryItem>()!.IsSuppressed);
        var retained=new Dictionary<McObjectId,string>();ModelHoleParameter412.NumericEdit? holeOffsets=null;
        if(!add) {
            var owner=feature.GetObjectOfType<HistoryItem>()!;
            void Keep(McObjectId variable,double? requested){var expression=ModelNumericDriverEditor.RetainedExpression(owner,variable,requested);if(expression!=null)retained.Add(variable,expression);}
            if(edit.Kind=="set_model_extrusion_extent") {
                var extrusion=feature.GetObjectOfType<ExtrudeFeature>()!;
                Keep(extrusion.DistanceParamVarID,ModelExtrusionExtentEditor.Mode(d)==ExtrudeParamExtType.Dist?N(d,"distance"):null);
                Keep(extrusion.TaperAngleParamVarID,N(d,"taper_angle")*180/Math.PI);
            }
            else if(edit.Kind=="set_model_sweep")Keep(feature.GetObjectOfType<ExtrudeAlongPathFeature>()!.TaperAngleParamVarID,N(d,"taper_angle")*180/Math.PI);
            else if(edit.Kind=="set_model_hole"&&!d.TryGetProperty("center",out _))
            {
                holeOffsets=ModelHoleParameter412.PrepareNumeric(feature.GetObjectOfType<HoleFeature>()!,d,cancel);
                foreach(var driver in holeOffsets.ExpectedExpressions.Keys)
                    ModelNumericDriverEditor.ValidateChangeScope(owner,driver,driver==holeOffsets.FirstVariable?holeOffsets.First:holeOffsets.Second,body,contents,views,cancel);
            }
        }
        return new(body,source,entity.Layer,contents!,views,feature,face,edge1,edge2,plane,sources,Inventory(db,tx),external,retained,datum,profileConsumers.ToArray(),suppression,holeOffsets);
    }
    internal static void Preflight(ModifyArguments a,DocumentState s,Transaction t,Action? cancel=null)
    {
        if(((ModelManufacturingEdit)a.Operations.Single()).Kind.EndsWith("_circular_pattern",StringComparison.Ordinal))
            ModelPatternWriter.Preflight(ModelPatternRequest439.Translate(a),s,t,cancel);
        else Resolve((ModelManufacturingEdit)a.Operations.Single(),a,s,t,cancel);
    }
    static void VerifyRetained410(Binding binding,McObjectId feature,bool add,Action cancel)
    {
        foreach(var pair in binding.Suppression??new Dictionary<McObjectId,bool>())
        {cancel();var item=pair.Key.GetObjectOfType<HistoryItem>();Need(item!=null&&item.IsSuppressed==pair.Value,"MODEL_STATE_UNAVAILABLE","An existing intentional suppression flag changed during the feature edit.");}
        foreach(var profile in binding.ProfileConsumers??[])
            ModelProfileConsumers410.VerifyRetained(profile.Before,profile.Profile,profile.Owner,cancel,changingConsumer:add?default:feature,addedConsumer:add?feature:default);
    }
    static void Hole(HoleFeature h,JsonElement d,bool updatePlacement,bool deferBlindDepth,ModelHoleParameter412.NumericEdit? offsets=null) {
        if(updatePlacement&&offsets!=null)ModelHoleParameter412.ApplyNumeric(h,offsets);
        else if(updatePlacement) {
            var original=h.GetPlacement();Need(original!=null,"MODEL_STATE_UNAVAILABLE","Hole placement is unavailable.");
            var fixedPosition=d.TryGetProperty("center",out _);
            var placement=fixedPosition?IppDefinition.Create(IppDefinitionType.Fixed):(IppDefinition)original!.Clone();
            if(fixedPosition)placement.TargetPlaneId=original!.TargetPlaneId;
            if(d.TryGetProperty("center",out var center)){
                Need(original!.DefType==IppDefinitionType.Fixed,"INVALID_ARGUMENT","Fixed center edits require an existing fixed placement; edge constraints are not silently discarded.");
                Need(placement.SetFixedPosition(new Multicad.Geometry.Point3d(center[0].GetDouble(),center[1].GetDouble(),center[2].GetDouble())).IsSuccess(),"MODEL_UPDATE_FAILED","Native fixed placement rejected its center.");
            }else{Need(placement.DefType==IppDefinitionType.TwoEdges,"INVALID_ARGUMENT","Edge-offset edits require TwoEdges placement.");placement.Param1=N(d,"offset1");placement.Param2=N(d,"offset2");placement.Sector=d.GetProperty("sector").GetInt32();}Need(h.SetPlacement(placement),"MODEL_UPDATE_FAILED","Hole placement was rejected.");
        }
        h.Diameter=N(d,"diameter");h.IsFullDepth=B(d,"through");if(!h.IsFullDepth&&!deferBlindDepth)h.Depth=N(d,"depth");
        var seat = RequestedSeat(d);
        h.SeatType = seat;
        Need(h.SeatType == seat, "MODEL_UPDATE_FAILED", "Native hole rejected the requested seat type.");
        if (seat != HoleSeatType.Flat) h.SeatDiameter = N(d, "seat_diameter");
        if (seat == HoleSeatType.Counterbore) h.SeatDepth = N(d, "seat_depth");
        if (seat == HoleSeatType.Countersink) h.SeatAngle = R(d, "seat_angle_degrees");
        h.FlatBottom=B(d,"flat_bottom");if(!h.FlatBottom)h.BottomAngle=R(d,"bottom_angle_degrees");
    }
    private static HoleSeatType RequestedSeat(JsonElement definition) => S(definition, "seat") switch
    {
        "counterbore" => HoleSeatType.Counterbore,
        "countersink" => HoleSeatType.Countersink,
        _ => HoleSeatType.Flat
    };
    static void Thread(ThreadFeature t,JsonElement d){t.IsRight=B(d,"right_handed");t.IsInternal=B(d,"internal");t.Pitch=N(d,"pitch");t.IsFullLength=B(d,"full_length");if(!t.IsFullLength)t.Length=N(d,"length");}
    static void VerifyValues(McObjectId id,ModelManufacturingEdit edit) {
        var d=edit.Definition;bool Near(double x,double y)=>Math.Abs(x-y)<=1e-8*Math.Max(1,Math.Max(Math.Abs(x),Math.Abs(y)));
        bool ok;
        if(edit.Kind=="set_model_sketch_frame")ok=id.GetObjectOfType<PlanarSketch>()!=null;
        else if(edit.Kind=="set_model_sketch_support")ok=ModelSketchSupport.Verify(id.GetObjectOfType<PlanarSketch>()!,d);
        else if(edit.Kind=="set_model_extrusion_extent")ok=ModelExtrusionExtentEditor.Verify(id.GetObjectOfType<ExtrudeFeature>()!,d);
        else if(edit.Kind.EndsWith("_loft")){var f=id.GetObjectOfType<LoftFeature>()!;ok=f.LoftType==LoftType.Regular&&f.PartOperation==(S(d,"part_operation")=="cut"?PartFeatureOperation.Cut:PartFeatureOperation.Join);}
        else if(edit.Kind.EndsWith("_sweep")){var f=id.GetObjectOfType<ExtrudeAlongPathFeature>()!;ok=Same(f.ProfileID,S(d,"profile_id"))&&Near(f.Angle,N(d,"taper_angle"))&&f.ProfileOrientation==(S(d,"orientation")=="normal_to_path"?SweepProfileOrientation.NormalToPath:SweepProfileOrientation.ParallelToOriginalProfile)&&f.PartOperation==(S(d,"part_operation")=="cut"?PartFeatureOperation.Cut:PartFeatureOperation.Join);}
        else if (edit.Kind.EndsWith("_hole", StringComparison.Ordinal))
        {
            var hole = id.GetObjectOfType<HoleFeature>()!;
            var seat = RequestedSeat(d);
            var placement = hole.GetPlacement();
            ok = hole.SeatType == seat && Near(hole.Diameter, N(d, "diameter")) &&
                hole.IsFullDepth == B(d, "through") && (hole.IsFullDepth || Near(hole.Depth, N(d, "depth"))) &&
                hole.FlatBottom == B(d, "flat_bottom") && (hole.FlatBottom || Near(hole.BottomAngle, R(d, "bottom_angle_degrees"))) &&
                placement != null && (d.TryGetProperty("center", out _) ? placement.DefType == IppDefinitionType.Fixed :
                    Near(placement.Param1, N(d, "offset1")) && Near(placement.Param2, N(d, "offset2")) &&
                    placement.Sector == d.GetProperty("sector").GetInt32());
            if (ok && seat != HoleSeatType.Flat)
                ok = Near(hole.SeatDiameter, N(d, "seat_diameter")) && (seat == HoleSeatType.Counterbore
                    ? Near(hole.SeatDepth, N(d, "seat_depth")) : Near(hole.SeatAngle, R(d, "seat_angle_degrees")));
        }
        else if(edit.Kind.EndsWith("_thread")){var t=id.GetObjectOfType<ThreadFeature>()!;ok=Near(t.Pitch,N(d,"pitch"))&&t.IsRight==B(d,"right_handed")&&t.IsInternal==B(d,"internal")&&t.IsFullLength==B(d,"full_length")&&(t.IsFullLength||Near(t.Length,N(d,"length")));}
        else if(edit.Kind.EndsWith("_circular_pattern")){var p=id.GetObjectOfType<CircularPatternFeature>()!;ok=p.Size==d.GetProperty("count").GetInt32()&&Near(p.Angle,R(d,"angle_degrees"));}
        else ok=id.GetObjectOfType<MirrorFeature>()!=null;
        Need(ok,"MODEL_UPDATE_FAILED","Native feature did not retain requested parameters.");
    }
    internal static Result Execute(ModifyArguments args,DocumentState state,WriteJob job,ReadContext context,JsonLog log)
    {
        if(((ModelManufacturingEdit)args.Operations.Single()).Kind.EndsWith("_circular_pattern",StringComparison.Ordinal))
            return ModelPatternWriter.Execute(ModelPatternRequest439.Translate(args),state,job,context,log);
        Binding? b=null;McUndoPoint? tracker=null;bool started=false;McObjectId feature=McObjectId.Null;string stage="preflight";object? placementDiagnostic=null;var createdDatums=new HashSet<McObjectId>();
        void Close(){var t=tracker;tracker=null;if(t!=null)try{t.Stop();}finally{t.Dispose();}}
        try {
            var e=(ModelManufacturingEdit)args.Operations.Single();var d=e.Definition;bool add=e.Kind.StartsWith("add_");
            using(var tx=state.Document.Database.TransactionManager.StartTransaction())b=Resolve(e,args,state,tx,job.CheckCancellation);
            if(b.Datum!=null)ModelDatumPlaneMirror353.PreWrite(b.Datum,args,state,job.CheckCancellation);
            var setHoleExtent = e.Kind.EndsWith("_hole", StringComparison.Ordinal) &&
                (B(d, "through") || (!add && b.Feature.GetObjectOfType<HoleFeature>()!.IsFullDepth))
                ? NativeModelHistory421.LoadHoleExtent() : null;
            job.CheckCancellation();ModelViewFollowers.CheckStandalone(b.Body,b.Views,job.CheckCancellation);tracker=new McUndoPoint(false);tracker.Start();started=true;stage="native_write";
            if(add)ModelViewFollowers.PrepareInsertion(b.Views,false);
            feature=b.Feature;
            if(e.Kind=="set_model_sketch_frame")ModelSketchFrame.Apply(feature.GetObjectOfType<PlanarSketch>()!,b.Edge1,b.Edge2,d,createdDatums);
            else if(e.Kind=="set_model_sketch_support")ModelSketchSupport.Apply(feature.GetObjectOfType<PlanarSketch>()!,d,createdDatums);
            else if(e.Kind=="set_model_extrusion_extent")ModelExtrusionExtentEditor.Apply(feature.GetObjectOfType<ExtrudeFeature>()!,d,b.Face);
            else if(e.Kind.EndsWith("_loft")) {
                var f=add?b.Body.AddLoftFeature(b.Sources[0],b.Sources.Skip(1).ToArray()):feature.GetObjectOfType<LoftFeature>();
                Need(f!=null,"MODEL_UPDATE_FAILED","Native loft feature creation failed.");feature=f!.ID;
                if(!add){
                    while(f.SectionsCount>b.Sources.Length)Need(f.RemoveSection(f.SectionsCount-1),"MODEL_UPDATE_FAILED","Loft rejected section removal.");
                    f.ProfileID=b.Sources[0];
                    for(int i=1;i<b.Sources.Length;i++)Need(i<f.SectionsCount?f.SetSection(i,b.Sources[i]):f.AddSection(b.Sources[i]),"MODEL_UPDATE_FAILED","Loft rejected a section.");
                }
                f.LoftType=LoftType.Regular;f.PartOperation=S(d,"part_operation")=="cut"?PartFeatureOperation.Cut:PartFeatureOperation.Join;
            } else if(e.Kind.EndsWith("_sweep")) {
                var path=b.Sources.Skip(1).Select(id=>new McGeomParam{ID=id}).ToList();
                var f=add?b.Body.AddExtrudeAlongPathFeature(b.Sources[0],path):feature.GetObjectOfType<ExtrudeAlongPathFeature>();
                Need(f!=null,"MODEL_UPDATE_FAILED","Native sweep feature creation failed.");feature=f!.ID;
                f.ProfileID=b.Sources[0];if(!add)Need(f.SetPath(path),"MODEL_UPDATE_FAILED","Native sweep rejected changed path.");
                f.ProfileOrientation=S(d,"orientation")=="normal_to_path"?SweepProfileOrientation.NormalToPath:SweepProfileOrientation.ParallelToOriginalProfile;
                ModelSweepFactory.SetTaper(f,N(d,"taper_angle"),add||ModelExtrusionExtentEditor.ReplacesFormulas(d));f.PartOperation=S(d,"part_operation")=="cut"?PartFeatureOperation.Cut:PartFeatureOperation.Join;
            } else if(e.Kind.EndsWith("_hole")) {
                HoleFeature h;
                if(add){var fixedPosition=d.TryGetProperty("center",out var center);var placement=IppDefinition.Create(fixedPosition?IppDefinitionType.Fixed:IppDefinitionType.TwoEdges);placement.TargetPlaneId=b.Face;
                    if(fixedPosition)Need(placement.SetFixedPosition(new Multicad.Geometry.Point3d(center[0].GetDouble(),center[1].GetDouble(),center[2].GetDouble())).IsSuccess(),"MODEL_UPDATE_FAILED","Native fixed placement rejected its center.");
                    else{placement.SetAssocParams(new McGeomParam{ID=b.Edge1},new McGeomParam{ID=b.Edge2});placement.Param1=N(d,"offset1");placement.Param2=N(d,"offset2");placement.Sector=d.GetProperty("sector").GetInt32();}
                    h=b.Body.AddHoleFeature(HoleFeatureType.Simple,placement);Need(h!=null,"MODEL_UPDATE_FAILED","Native hole creation failed.");feature=h!.ID;}
                else h=feature.GetObjectOfType<HoleFeature>()!;
                Hole(h,d,!add,setHoleExtent!=null,b.HoleOffsets);
            } else if(e.Kind.EndsWith("_thread")) {
                var t=add?b.Body.AddThreadFeature(b.Face,B(d,"right_handed")):feature.GetObjectOfType<ThreadFeature>();Need(t!=null,"MODEL_UPDATE_FAILED","Native thread creation failed.");feature=t!.ID;if(add){t.FrontEdgeId=b.Edge1;t.DbEntity.Update();}Thread(t,d);
            } else if(e.Kind.EndsWith("_circular_pattern")) {
                var p=add?b.Body.AddCircularPatternFeature(b.Sources,new McGeomParam{ID=b.Edge1},d.GetProperty("count").GetInt32(),R(d,"angle_degrees")):feature.GetObjectOfType<CircularPatternFeature>();Need(p!=null,"MODEL_UPDATE_FAILED","Native circular pattern creation failed.");feature=p!.ID;p.Size=d.GetProperty("count").GetInt32();
                if(add)p.Angle=R(d,"angle_degrees");
                else {
                    // The getter is radians; the initialized MCS angle expression is degrees.
                    using var manager=b.Body.DbEntity.Document.GetDocumentVariablesManager(Constraint2dNetType.MCS);
                    Need(manager!=null&&manager.SetVariableExpression(p.AngleParamVarID,N(d,"angle_degrees").ToString("R",CultureInfo.InvariantCulture)),"MODEL_UPDATE_FAILED","Native pattern angle expression was rejected.");
                }
            } else {
                if(d.TryGetProperty("reference_plane",out var reference))b=b with{Plane=ReferencePlane(reference.GetString()!)};
                var m=add?b.Body.AddMirrorFeature(b.Sources,b.Plane):feature.GetObjectOfType<MirrorFeature>();Need(m!=null,"MODEL_UPDATE_FAILED","Native mirror creation failed.");feature=m!.ID;Need(m.SetPlane(b.Plane),"MODEL_UPDATE_FAILED","Native mirror plane was rejected.");
            }
            var item=feature.GetObjectOfType<HistoryItem>()!;
            if(add){if(item.OrderObjBefore!=b.Views.LastModelItem)Need(item.SetOrderObjBefore(b.Views.LastModelItem,false),"MODEL_UPDATE_FAILED","Feature predecessor was rejected.");ModelViewFollowers.CompleteInsertion(b.Views,feature);}
            if (setHoleExtent != null)
            {
                // Select the SDK extent mode explicitly and recalculate the through depth.
                stage = "set_hole_extent";
                bool through = B(d, "through");
                var extentStatus = setHoleExtent(b.Body.DbEntity.Document.ID.ToString(), b.Body.ID.ToString(),
                    feature.ToString(), through ? 1 : 0);
                Need(extentStatus == 0, "MODEL_UPDATE_FAILED", $"Native hole extent was not confirmed (HRESULT 0x{unchecked((uint)extentStatus):X8}).");
                if (!through) feature.GetObjectOfType<HoleFeature>()!.Depth = N(d, "depth");
            }
            item.DbEntity.Update();stage="rebuild";McObjectManager.UpdateAll();if(!add){Need(b.Body.RebuildPart(),"MODEL_UPDATE_FAILED","Native model rebuild failed.");McObjectManager.UpdateAll();}job.CheckCancellation();stage="verify";
            var contents=ModelInventoryBudget.History(b.Body,job.CheckCancellation);Need(b.Contents.Where(x=>!b.ExternalSources.Contains(x.ToString())).All(contents.Contains)&&contents.Contains(feature),"MODEL_STATE_UNAVAILABLE","Existing history was lost.");
            var currentPaths=NativePathSources.Read(contents!,job.CheckCancellation);
            currentPaths.UnionWith(createdDatums.Select(x=>x.ToString()));
            using(var verification=state.Document.Database.TransactionManager.StartTransaction())
                ProjectedHistorySources.Healthy(contents!.ToArray(),ProjectedHistorySources.Read(b.Body,contents.ToArray(),state,verification,job.CheckCancellation));
            VerifyValues(feature,e);
            if(b.HoleOffsets!=null)ModelHoleParameter412.VerifyNumeric(feature.GetObjectOfType<HoleFeature>()!,b.HoleOffsets);
            ModelNumericDriverEditor.VerifyRetained(item,b.RetainedDriverExpressions);
            VerifyRetained410(b,feature,add,job.CheckCancellation);
            if(e.Kind.EndsWith("_loft")){var lf=feature.GetObjectOfType<LoftFeature>()!;Need(Enumerable.Range(0,lf.SectionsCount).Select(i=>lf.GetSection(i).ID).SequenceEqual(b.Sources),"MODEL_UPDATE_FAILED","Loft lost ordered section bindings.");}
            if(e.Kind.EndsWith("_sweep"))Need(feature.GetObjectOfType<ExtrudeAlongPathFeature>()!.GetPath().Select(p=>p.ID).SequenceEqual(b.Sources.Skip(1)),"MODEL_UPDATE_FAILED","Sweep lost ordered path bindings.");
            Need(item.HostSolidID==b.Body.ID&&b.Body.LastPartItemID==(add&&b.Views.ViewIds.Length==0?feature:b.Views.LastHistoryItem),"MODEL_STATE_UNAVAILABLE","Feature ownership or final history link changed.");
            if(!add)Need(contents!.Where(x=>!currentPaths.Contains(x.ToString())&&!b.ExternalSources.Contains(x.ToString())).ToHashSet().SetEquals(b.Contents.Where(x=>!b.ExternalSources.Contains(x.ToString()))),"MODEL_STATE_UNAVAILABLE","Editing parameters changed existing history identities.");
            if(add&&b.Sources.Length>0) {
                var dependencies=item.GetDependsOn(false,false)??[];
                Need(b.Sources.All(dependencies.Contains),"MODEL_STATE_UNAVAILABLE","Pattern lost a requested source dependency.");
            }
            if(e.Kind.EndsWith("_mirror"))Need(item.GetDependsOn(false,false)?.Contains(b.Plane)==true,"MODEL_STATE_UNAVAILABLE","Mirror did not retain the requested plane dependency.");
            if(b.Views.HasFollowers){if(add)ModelViewFollowers.VerifyAfterInsertion(b.Body,b.Views,feature,job.CheckCancellation,refreshStandalone:true);else ModelViewFollowers.Verify(b.Body,b.Views,job.CheckCancellation,refreshStandalone:true);}
            EntityRef bodyRef;
            using(var tx=state.Document.Database.TransactionManager.StartTransaction()) {
                var inventory=Inventory(state.Document.Database,tx);var matches=inventory.Where(x=>tx.GetObject(x,OpenMode.ForRead) is Solid3d&&McObjectId.FromHandle(x.Handle.Value,McDocument.ActiveDocument.ID)==b.Body.ID).ToArray();
                Need(matches.Length==1,"OBJECT_IDENTITY_MISMATCH","Native representation is not unique.");
                bool OwnedDatum(ObjectId x)=>createdDatums.Contains(McObjectId.FromHandle(x.Handle.Value,McDocument.ActiveDocument.ID))||!b.Plane.IsNull&&b.Plane.GetObjectOfType<WorkPlane>()!=null&&McObjectId.FromHandle(x.Handle.Value,McDocument.ActiveDocument.ID)==b.Plane;
                Need(inventory.Except(b.Inventory).All(x=>x==matches[0]||OwnedDatum(x))&&b.Inventory.Except(inventory).All(x=>x==b.Source),"MODEL_STATE_UNAVAILABLE","Unrelated entities changed membership.");
                var solid=(Solid3d)tx.GetObject(matches[0],OpenMode.ForRead);Need(!solid.IsNull&&solid.Layer==b.Layer,"MODEL_UPDATE_FAILED","Resulting body is empty or changed layer.");
                bodyRef=new(args.BridgeId,args.DocumentId,Validation.NormalizeHandle(matches[0].Handle.ToString()));
            }
            var parameters=ModelManufacturingHistory.Parameters(feature);
            Need(parameters!=null,"MODEL_STATE_UNAVAILABLE","Result feature parameters are unavailable.");
            var parameterSnapshot=Wire.Element(parameters!);ModelDatumPlaneRead353? datumRead=null;
            if(b.Datum!=null)
            {
                using var datumVerification=state.Document.Database.TransactionManager.StartTransaction();
                datumRead=ModelDatumPlaneMirror353.Verify(b.Datum,b.Body,feature,args,state,datumVerification,job.CheckCancellation);
                Need(JsonSerializer.SerializeToUtf8Bytes(new{parameterSnapshot,datumRead},Wire.Json).Length<Wire.PayloadBudget,"ENTITY_TOO_LARGE","Actual native mirror/datum metadata exceeds the common transport budget; use focused reads.");
            }
            job.BeginCommit();Close();
            VerifyRetained410(b,feature,add,job.CheckCancellation);
            if(b.HoleOffsets!=null)ModelHoleParameter412.VerifyNumeric(feature.GetObjectOfType<HoleFeature>()!,b.HoleOffsets);
            if(b.Datum!=null)
            {
                stage="verify_after_native_close";using var datumVerification=state.Document.Database.TransactionManager.StartTransaction();
                datumRead=ModelDatumPlaneMirror353.Verify(b.Datum,b.Body,feature,args,state,datumVerification,job.CheckCancellation);
                var representation=ModelHistoryEditGuard.CurrentRepresentation(b.Body,state,datumVerification,job.CheckCancellation);
                bodyRef=new(args.BridgeId,args.DocumentId,Validation.NormalizeHandle(representation.Handle.ToString()));
                var freshParameters=ModelManufacturingHistory.Parameters(feature);Need(freshParameters!=null,"MODEL_STATE_UNAVAILABLE","Native mirror parameters are unavailable after native closure.");
                parameterSnapshot=Wire.Element(freshParameters!);
            }
            var old=new EntityRef(args.BridgeId,args.DocumentId,e.Handles[0]);bool replaced=old.handle!=bodyRef.handle;
            var change=new ModelManufacturingChange(e.Kind,bodyRef,b.Body.ID.ToString(),feature.ToString(),parameterSnapshot,datumRead);
            var data=new ModifiedEntities(args.OperationId,"committed",false,[bodyRef],replaced?[old]:[],replaced?[bodyRef]:[],[],"not_guaranteed",manufacturing_changes:[change]);
            var warnings=new List<string>{WriteRecovery.Warning,"NATIVE_FEATURE_CHANGED: Review the resulting feature group and its dependent geometry; retained parameters alone do not establish engineering correctness."};
            if(b.Datum!=null)warnings.AddRange(b.Datum.Part.Warnings);
            if(e.Kind.EndsWith("_thread"))warnings.Add("THREAD_REPRESENTATION_UNVERIFIED: Native ThreadFeature is stored. This does not certify a physical helical B-rep; do not claim physical helical geometry from feature metadata alone.");
            var result=Result.Ok(job.OriginalRequestId,data,context,warnings.Distinct(StringComparer.Ordinal).ToArray());
            if(b.Datum!=null)Need(JsonSerializer.SerializeToUtf8Bytes(result,Wire.Json).Length<=Wire.PayloadBudget,"ENTITY_TOO_LARGE","Actual native mirror/datum receipt exceeds the common transport budget.");
            result=ModelViewFollowers.WithWarnings(result,b.Views);job.RecordCommit(result);return result;
        } catch(Exception ex) {
            if(job.CommittedResult is {} committed)return committed;
            Exception? cleanup=null;try{Close();}catch(Exception err){cleanup=err;}
            bool unknown=started||cleanup!=null;string cause=ex is DomainException d?d.Error.code:ex is OperationCanceledException?"CANCELLED":"MODEL_MANUFACTURING_FAILED";
            log.Write(job.Operation,job.OriginalRequestId,"manufacturing_failure",transaction:stage,operationId:job.OperationId);
            return job.Error(unknown?"WRITE_OUTCOME_UNKNOWN":cause,unknown?"Native feature outcome is unverified. Inspect body/history before dependent work.":ex.Message,unknown?"unknown":"not_started",context,new{cause,stage,message=ex.Message,body_mc_id=b?.Body.ID.ToString(),feature_id=feature.ToString(),placement=placementDiagnostic,cleanup=cleanup?.Message}) with{warnings=[WriteRecovery.Warning]};
        }
    }
}

