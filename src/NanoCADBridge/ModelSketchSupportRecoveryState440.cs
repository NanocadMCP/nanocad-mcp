using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using Multicad;
using Multicad.DatabaseServices;
using Multicad.DatabaseServices.Constraints;
using Multicad.DatabaseServices.StandardObjects;
using Multicad.Mc3D;
using NanoCAD.Contracts;
using G=Multicad.Geometry;
using Db=Teigha.DatabaseServices;

namespace NanoCADBridge;

// A separate observation protocol for a lost face support. Ordinary healthy
// edit/reorder snapshots retain their guards. No getter here substitutes a
// target WCS frame for unavailable original geometry or runs a solver.
internal static class ModelSketchSupportRecoveryState440
{
    internal sealed record Scope(McObjectId Sketch,McObjectId OldSupport,McObjectId NewSupport,McObjectId DatumDriver,
        HashSet<string> Recovering,HashSet<string> Affected,McObjectId[] Profiles,bool UseTargetDatum=false);
    internal sealed record Snapshot(Mc3dSolid Body,Db.ObjectId Representation,McDocument Owner,McObjectId[] Contents,
        Dictionary<string,string?> Previous,string[] Features,JsonElement Design,JsonElement BodyMetadata,
        ModelSketchConstraintMetadata379.Binding[] Storage,ModelViewFollowers.Snapshot Views,
        SketchPlaneSpec? Frame,JsonElement Notices,string Token,ModelSketchFrameRetirement440.Before? Retirement=null);
    static void Need([System.Diagnostics.CodeAnalysis.DoesNotReturnIf(false)]bool ok,string code,string message)
        =>ModelDatumPlaneState353.Need(ok,code,message);
    static string Key(McObjectId id)=>id.ToString();
    static string? Id(McObjectId id)=>id.IsNull?null:Key(id);
    static string[] Sorted(IEnumerable<McObjectId> ids)=>ids.Select(Key).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(id=>id,StringComparer.OrdinalIgnoreCase).ToArray();
    static bool Equal(JsonElement a,JsonElement b,Action cancel)=>ModelFeatureReorderState345.Equivalent(a,b,cancel);
    // The general model comparison allows numeric geometric tolerance. Native
    // reference markers/data/flags are identity data, so compare their fully
    // deterministic record serialization exactly (including 64-bit integers).
    static bool SameFrameReferences(Snapshot before,Snapshot after)=>
        before.Design.GetProperty("selected_sketch").GetProperty("frame_references").GetRawText()==
        after.Design.GetProperty("selected_sketch").GetProperty("frame_references").GetRawText();

    internal static JsonElement Notices(HistoryItem item,Action cancel)
    {
        cancel();var info=item.StatusInfo;if(info?.Records==null)return Wire.Element(Array.Empty<object>());
        var rows=new List<object>();
        foreach(var record in info.Records)
        {
            cancel();Need(record!=null,"RECOVERY_DIAGNOSIS_UNAVAILABLE","A current native status record is unavailable.");
            var actions=record.HealingActions;
            var row=new{severity=record.Severity.ToString(),severity_code=(int)record.Severity,description=record.Description,
                healing_actions=actions?.Select(action=>new{target_id=Id(action.TargetObjectId),actions=action.ApplicableActions.ToString(),mask=(int)action.ApplicableActions}).ToArray()};
            new ModelInventoryBudget(cancel).Add(row);rows.Add(row);
        }
        return Wire.Element(rows);
    }
    static void Failure(HistoryItem item,string description,Action cancel)
    {
        Need(item.Status.ToString()=="Error"&&!item.IsSuppressed&&!item.IsBeyondEndOfPart,"RECOVERY_SCOPE_UNSUPPORTED","Recovery admits only the exact active Error nodes caused by the lost sketch plane.");
        var errors=Notices(item,cancel).EnumerateArray().Where(row=>row.GetProperty("severity_code").GetInt32()>=2).ToArray();
        Need(errors.Length>0&&errors.All(row=>string.Equals(row.GetProperty("description").GetString(),description,StringComparison.Ordinal)),
            "RECOVERY_SCOPE_UNSUPPORTED","A selected history error is not the established lost-plane error or its direct profile/extrusion propagation. No unrelated error is waived.");
    }
    internal static Scope Resolve(Mc3dSolid body,PlanarSketch sketch,McObjectId target,string expectedSupport,Action cancel)
    {
        var ids=ModelInventoryBudget.History(body,cancel).ToArray();var owner=body.DbEntity.Document;
        Need(sketch.ID!=McObjectId.Null&&sketch.HostSolidID==body.ID&&ids.Contains(sketch.ID)&&sketch.DbEntity.Document?.ID==owner.ID&&!sketch.IsInEditMode,
            "SKETCH_OWNERSHIP_MISMATCH","Recover the same actual active selected-part sketch outside its editor.");
        var old=sketch.PlanarEntityID;
        Need(!old.IsNull&&old.IsFace&&old.ParentId==body.ID&&Key(old).Equals(expectedSupport,StringComparison.OrdinalIgnoreCase)&&old!=target,
            "PRECONDITION_FAILED","Recovery requires the exact retained failed face support ID of this sketch; reread the selected history.");
        Failure(sketch,"Cannot to get a sketch's plane geometry",cancel);
        // Native status establishes the plane failure. A final-face inventory
        // omission is neither used here nor treated as historical invalidity.
        var profiles=ids.Where(id=>id.GetObjectOfType<SketchProfile>() is {} p&&p.ParentSketchID==sketch.ID).ToArray();
        Need(profiles.Length>0,"RECOVERY_SCOPE_UNSUPPORTED","The lost sketch must retain an actual selected profile and dependent extrusion.");
        var recovering=new HashSet<string>(StringComparer.OrdinalIgnoreCase){Key(sketch.ID)};
        foreach(var id in profiles)
        {
            var profile=id.GetObjectOfType<SketchProfile>()!;
            Need(profile.ID==id&&profile.HostSolidID==body.ID&&profile.DbEntity.Document?.ID==owner.ID,"PROFILE_OWNERSHIP_MISMATCH","The failed sketch's retained profile belongs to another native owner.");
            if(profile.Status.ToString()!="Ok"){Failure(profile,"There is an error in the host sketch",cancel);recovering.Add(Key(id));}
        }
        var extrusions=ids.Where(id=>id.GetObjectOfType<ExtrudeFeature>() is {} f&&profiles.Contains(f.ProfileID)).ToArray();
        Need(extrusions.Length>0,"RECOVERY_SCOPE_UNSUPPORTED","The retained profile has no same-part extrusion to recover.");
        foreach(var id in extrusions)
        {
            var feature=id.GetObjectOfType<ExtrudeFeature>()!;
            Need(feature.ID==id&&feature.HostSolidID==body.ID&&feature.DbEntity.Document?.ID==owner.ID,"PROFILE_OWNERSHIP_MISMATCH","The failed profile's extrusion belongs to another native owner.");
            if(feature.Status.ToString()!="Ok"){Failure(feature,"There is an error in the base sketch profile",cancel);recovering.Add(Key(id));}
        }
        var affected=new HashSet<string>(StringComparer.OrdinalIgnoreCase){Key(sketch.ID)};
        var downstream=sketch.GetAllDependent(false);Need(downstream!=null,"DEPENDENCY_INVENTORY_UNAVAILABLE","The failed sketch's actual downstream inventory is unavailable.");ModelInventoryBudget.Ids(downstream,cancel);
        var views=ModelViewFollowers.Resolve(body,ids,cancel,includeStandalone:true);
        foreach(var id in downstream)
        {
            cancel();if(id==body.ID||ModelViewFollowers.OwnsDependent(views,id))continue;
            Need(ids.Contains(id),"DEPENDENCY_OWNERSHIP_MISMATCH","The failed sketch has an actual consumer outside the selected part and declared drawings.");
            var item=id.GetObjectOfType<HistoryItem>();
            if(item!=null)Need(item.ID==id&&item.HostSolidID==body.ID&&item.DbEntity.Document?.ID==owner.ID&&
                (profiles.Contains(id)||extrusions.Contains(id)||id.GetObjectOfType<DrawingView>()!=null),
                "RECOVERY_SCOPE_UNSUPPORTED","This bounded recovery retains direct sketch profiles/extrusions and drawing followers; another downstream feature requires its own recovery adapter.");
            affected.Add(Key(id));
        }
        Need(!affected.Contains(Key(target)),"CYCLIC_DEPENDENCY","The chosen WorkPlane is a downstream consumer of the failed sketch.");
        var driver=target.IsNull?McObjectId.Null:target.GetObjectOfType<WorkPlane>()?.ParamVarID??McObjectId.Null;
        return new(sketch.ID,old,target,driver,recovering,affected,profiles);
    }
    static JsonElement Geometry(G.EntityGeometry geometry,Action cancel)
    {
        cancel();if(geometry.GeometryType==G.EntityGeomType.kLine){using var line=geometry.LineSeg;return Wire.Element(SketchGeometryReader.Line(line));}
        if(geometry.GeometryType==G.EntityGeomType.kCircArc){using var arc=geometry.CircArc;return Wire.Element(SketchGeometryReader.Arc(arc));}
        if(NativeSketchMembers385.IsPoint(geometry)){var p=geometry.Point;Need(new[]{p.X,p.Y,p.Z}.All(double.IsFinite),"INVALID_GEOMETRY","A local construction point is nonfinite.");return Wire.Element(new{kind="point",point=new[]{p.X,p.Y,p.Z}});}
        if(geometry.GeometryType==G.EntityGeomType.kPolyline)
        {
            using var polyline=geometry.Polyline;using var segments=polyline.Segments;var values=new List<JsonElement>();
            for(uint index=0;index<segments.Count;index++)
            {
                cancel();if(segments.SegType(index)==G.SegPlType.sgLine){using var line=segments.GetLineSegAt(index);values.Add(Wire.Element(SketchGeometryReader.Line(line)));}
                else if(segments.SegType(index)==G.SegPlType.sgArc){using var arc=segments.GetArcSegAt(index);values.Add(Wire.Element(SketchGeometryReader.Arc(arc)));}
                else throw new DomainException("RECOVERY_SCOPE_UNSUPPORTED","A retained local polyline segment needs its actual typed observation adapter.");
            }
            return Wire.Element(new{kind="polyline",closed=polyline.IsClosed(),segments=values});
        }
        throw new DomainException("RECOVERY_SCOPE_UNSUPPORTED","Lost-support recovery reads complete native local Line/Arc/Circle/Polyline and point data; unsupported geometry is never reconstructed.");
    }
    static JsonElement LocalProfile(SketchProfile profile,Action cancel)
    {
        // SDK getContours(true) explicitly requests profile coordinates.
        var areas=profile.GetContours(true);Need(areas!=null&&areas.Count>0,"RECOVERY_LOCAL_PROFILE_UNAVAILABLE","The failed profile does not expose its selected native LOCAL regions; no parent contour or new profile is substituted.");
        var result=new List<object>();var frame=Wire.Element(new{coordinate_system="native_profile_local"});
        try
        {
            foreach(var area in areas)
            {
                cancel();Need(area!=null,"RECOVERY_LOCAL_PROFILE_UNAVAILABLE","A retained selected local region is unavailable.");using var outer=area.External;
                var outside=ModelProfileElementsReader358.Loop(outer,frame,cancel);var holes=area.Internal;
                Need(holes!=null,"RECOVERY_LOCAL_PROFILE_UNAVAILABLE","The local selected hole inventory is unavailable.");
                var inside=new List<JsonElement>();try{foreach(var hole in holes){cancel();Need(hole!=null,"RECOVERY_LOCAL_PROFILE_UNAVAILABLE","A local selected hole is unavailable.");inside.Add(ModelProfileElementsReader358.Loop(hole,frame,cancel));}}
                finally{foreach(var hole in holes.Distinct(ReferenceEqualityComparer.Instance))if(hole is IDisposable value)value.Dispose();}
                result.Add(new{outer=outside,holes=inside});
            }
        }
        finally{foreach(var area in areas.Distinct(ReferenceEqualityComparer.Instance))if(area is IDisposable value)value.Dispose();}
        return Wire.Element(new{profile_id=Key(profile.ID),parent_sketch_id=Key(profile.ParentSketchID),dependent_feature_id=Id(profile.Dependent3dFeatureID),local_selected_regions=result});
    }
    static JsonElement SelectedSketch(PlanarSketch sketch,McDocument owner,DocumentState state,Db.Transaction tx,Action cancel,out McObjectId[] observedIds)
    {
        // A nonnull exact sketch ID selects its existing dependency scope.
        // IMcNativeGate.h documents the optional plane for non-sketch scope;
        // null never substitutes a target/new plane into old relation data.
        using var manager=owner.Get2dConstraintsManager(Constraint2dNetType.Auto,sketch.ID,null!);
        Need(manager!=null,"RECOVERY_CONSTRAINTS_UNAVAILABLE","The failed sketch's existing native manager is unavailable.");NativeSketchMembers385.RequireManagerScope(manager,sketch,owner);
        var constraints=manager.GetAllConstraints();Need(constraints!=null,"RECOVERY_CONSTRAINTS_UNAVAILABLE","The existing exact sketch scope has no complete relation inventory.");ModelInventoryBudget.Ids(constraints,cancel);
        var helpers=new HashSet<McObjectId>(constraints);var refs=new HashSet<McObjectId>();var controls=new List<object>();
        foreach(var id in constraints.OrderBy(Key,StringComparer.OrdinalIgnoreCase))
        {
            cancel();using var info=manager.GetConstraintInfo(id);Need(info!=null,"RECOVERY_CONSTRAINTS_UNAVAILABLE","A retained existing native relation is unreadable.");
            foreach(var reference in new[]{info.FirstObject,info.SecondObject,info.ThirdObject})if(reference!=null&&!reference.ObjectId.IsNull)refs.Add(reference.ObjectId);
            if(!info.VarId.IsNull)helpers.Add(info.VarId);if(!info.DimId.IsNull)helpers.Add(info.DimId);
            controls.Add(new{id=Key(id),relation=NativeSketchMembers385.DescribeRelation(info),info.IsGeometrical,info.IsDimensional,
                variable_id=Id(info.VarId),variable_name=info.VarName,variable_expression=info.VarExpression,dimension_id=Id(info.DimId),
                parameter_id=Id(manager.GetConstraintParamId(id))});
        }
        var raw=sketch.GetAllItemsGeometry(true);Need(raw!=null&&raw.Count>0,"RECOVERY_LOCAL_GEOMETRY_UNAVAILABLE","The failed sketch has no complete native LOCAL element inventory; no WCS reconstruction is attempted.");
        var geometry=new Dictionary<string,JsonElement>(StringComparer.OrdinalIgnoreCase);
        try{foreach(var pair in raw.OrderBy(pair=>Key(pair.Key),StringComparer.OrdinalIgnoreCase)){Need(!pair.Key.IsNull&&pair.Value!=null,"RECOVERY_LOCAL_GEOMETRY_UNAVAILABLE","A retained local native element is unavailable.");geometry.Add(Key(pair.Key),Geometry(pair.Value,cancel));}}
        finally{foreach(var value in raw.Values.Distinct(ReferenceEqualityComparer.Instance))if(value is IDisposable item)item.Dispose();}
        var containing=sketch.GetContainingObjects(true,true,true);Need(containing!=null,"RECOVERY_LOCAL_GEOMETRY_UNAVAILABLE","Complete current sketch membership including construction is unavailable.");ModelInventoryBudget.Ids(containing,cancel);
        var ids=containing.Concat(refs).Concat(helpers).Concat(raw.Keys).Distinct().ToArray();observedIds=ids;
        var objects=new List<object>();
        foreach(var id in ids.OrderBy(Key,StringComparer.OrdinalIgnoreCase))
        {
            cancel();Need(id.GetObjectOfType<ProjectionSketchEnt>()==null,"RECOVERY_SCOPE_UNSUPPORTED","Lost-support recovery of projected/external sketch geometry needs a separately verified reference adapter.");
            if(id.GetObjectOfType<SketchProfile>()!=null)continue;
            var actual=id.GetObject();Need(actual==null||actual.ID==id,"OBJECT_IDENTITY_MISMATCH","A retained sketch input/helper resolves another native identity.");
            McDbObject? native=actual is McEntity entity?entity.DbObject:actual is DbGeometry curve?curve.DbEntity:null;
            Need(native==null||native.Document?.ID==owner.ID,"DEPENDENCY_OWNERSHIP_MISMATCH","A retained sketch input/helper belongs to another native document.");
            if(!geometry.ContainsKey(Key(id))&&!helpers.Contains(id)&&id.GetObjectOfType<HistoryItem>()==null)
            {
                using var local=sketch.GetItemGeometry(id,true);
                Need(local!=null,"RECOVERY_LOCAL_GEOMETRY_UNAVAILABLE","A retained construction/reference has no actual local geometry.");geometry.Add(Key(id),Geometry(local,cancel));
            }
            objects.Add(new{id=Key(id),native_type=actual?.GetType().FullName,native_metadata=native==null?Wire.Element(new{scope="no_proved_resident_native_helper_metadata"}):ModelSketchLifecycleReader362.Metadata(id,native,owner,state,tx,cancel)});
        }
        foreach(var peer in ModelProfileDiscovery348.Ids<PlanarSketch>(owner,cancel))
        {
            if(peer==sketch.ID)continue;var other=peer.GetObjectOfType<PlanarSketch>();Need(other!=null&&other.ID==peer,"SKETCH_BINDING_UNAVAILABLE","A document sketch inventory item is unavailable.");
            var shared=other.GetContainingObjects(true,true,true);Need(shared!=null,"SKETCH_BINDING_UNAVAILABLE","Shared-sketch membership cannot be verified.");
            Need(!shared.Any(id=>geometry.ContainsKey(Key(id))),"RECOVERY_SCOPE_UNSUPPORTED","A retained failed-sketch element is shared with another sketch.");
        }
        var frameReferences=NativeSketchFrameObservation440.Read(sketch);
        return Wire.Element(new{sketch_id=Key(sketch.ID),owner_document_id=Key(owner.ID),coordinate_system="native_sketch_local",frame_references=frameReferences,manager_document_id=Key(manager.DocumentId),scope_id=Key(manager.ScopeId),network=manager.ConstraintsNetworkType.ToString(),
            containing_ids=Sorted(containing),local_geometry=geometry.OrderBy(pair=>pair.Key,StringComparer.OrdinalIgnoreCase).Select(pair=>new{id=pair.Key,geometry=pair.Value}).ToArray(),controls,objects});
    }
    static JsonElement Variables(McDocument owner,Action cancel)
    {
        using var manager=owner.GetDocumentVariablesManager(Constraint2dNetType.MCS);
        Need(manager!=null&&manager.DocumentId==owner.ID&&manager.ConstraintsNetworkType==Constraint2dNetType.MCS,"VARIABLES_UNAVAILABLE","The actual owner MCS variable network is unavailable.");
        var ids=manager.GetAllVariables();Need(ids!=null,"VARIABLES_UNAVAILABLE","The complete owner variable inventory is unavailable.");ModelInventoryBudget.Ids(ids,cancel);var rows=new List<object>();
        foreach(var id in ids.OrderBy(Key,StringComparer.OrdinalIgnoreCase))
        {
            cancel();var info=manager.GetVariableInfo(id);Need(info!=null,"VARIABLES_UNAVAILABLE","A retained owner variable is unreadable.");bool available=manager.GetVariableValue(id,out double value);
            Need(!available||double.IsFinite(value),"INVALID_GEOMETRY","A retained owner variable is nonfinite.");
            var variables=manager.GetDependentOnVariables(id);var objects=manager.GetDependentOn3dObjects(id);
            Need(variables!=null&&objects!=null,"VARIABLES_UNAVAILABLE","A retained variable's actual consumer inventory is unavailable.");
            rows.Add(new{id=Key(id),name=info.Name,type=info.Type.ToString(),expression=info.Expression,host_object_id=Id(manager.GetVariableHostObjectId(id)),value=available?(double?)value:null,available,
                downstream_variables=Sorted(variables),downstream_objects=Sorted(objects)});
        }
        return Wire.Element(new{owner_document_id=Key(owner.ID),network="MCS",variables=rows});
    }
    static object? Parameters(McObjectId id,Action cancel)
    {
        var extrusion=id.GetObjectOfType<ExtrudeFeature>();
        if(extrusion!=null)return new{kind="extrusion",profile_id=Id(extrusion.ProfileID),part_operation=extrusion.PartOperation.ToString(),extent=extrusion.Extent.ToString(),
            terminal=ModelExtrusionExtentEditor.Read(extrusion),direction=extrusion.Direction.ToString(),distance=extrusion.Distance,taper_angle=extrusion.Angle,
            extent2=extrusion.Extent2.ToString(),distance2=extrusion.Distance2,taper_angle2=extrusion.Angle2,
            distance_variable_id=Id(extrusion.DistanceParamVarID),taper_angle_variable_id=Id(extrusion.TaperAngleParamVarID),distance2_variable_id=Id(extrusion.Distance2ParamVarID),taper_angle2_variable_id=Id(extrusion.TaperAngle2ParamVarID)};
        var revolution=id.GetObjectOfType<RevolveFeature>();
        if(revolution!=null)return new{kind="revolution",profile_id=Id(revolution.ProfileID),axis_object_id=Id(revolution.Axis.ID),part_operation=revolution.PartOperation.ToString(),full_turn=revolution.IsFull,
            extent=revolution.Extent.ToString(),extent2=revolution.Extent2.ToString(),terminal=ModelRevolutionTerminationReader377.Read(revolution),direction=revolution.Direction.ToString(),angle=revolution.Angle,angle2=revolution.Angle2,
            angle_variable_id=Id(revolution.RotationAngleParamVarID),angle2_variable_id=Id(revolution.RotationAngle2ParamVarID)};
        var projection=id.GetObjectOfType<ProjectionSketchEnt>();
        if(projection!=null)return new{kind="projection",source_object_id=Id(projection.SrcObjectID),host_sketch_id=Id(projection.HostSketchID),planar_object_id=Id(projection.PlanarObjectID)};
        var profile=id.GetObjectOfType<SketchProfile>();if(profile!=null)return new{kind="profile",profile_id=Key(profile.ID),parent_sketch_id=Id(profile.ParentSketchID),dependent_feature_id=Id(profile.Dependent3dFeatureID)};
        var pattern=id.GetObjectOfType<RectangularPatternFeature>();
        if(pattern!=null)
        {
            var excluded=pattern.GetSuppressedElementsIndices();Need(excluded!=null,"MODEL_PARAMETERS_UNAVAILABLE","A retained pattern exclusion inventory is unavailable.");
            var x=NativePatternObservation439.TryRead(pattern,0);var y=NativePatternObservation439.TryRead(pattern,1);Need(x!=null&&y!=null,"MODEL_PARAMETERS_UNAVAILABLE","A retained pattern direction binding is unavailable.");
            return new{kind="rectangular_pattern",native_source_item_ids=Enumerable.Range(0,pattern.SourceItemsCount).Select(index=>{cancel();return Key(pattern.GetSourceItemFeature(index));}).ToArray(),
                count_x=pattern.SizeX,count_y=pattern.SizeY,step_x=pattern.DistancesX,step_y=pattern.DistancesY,count_x_variable_id=Id(pattern.SizeXParamVarID),count_y_variable_id=Id(pattern.SizeYParamVarID),
                step_x_variable_id=Id(pattern.DistancesXParamVarID),step_y_variable_id=Id(pattern.DistancesYParamVarID),direction_x=x,direction_y=y,has_sub_feature_items=pattern.HasSubFeatureItems,
                suppressed_elements=excluded.Select(index=>new[]{index.Index1,index.Index2}).ToArray()};
        }
        return ModelManufacturingHistory.Parameters(id,cancel)??ModelEdgeFeatureHistory.Parameters(id)??ModelPlacementHistory.Parameters(id)??SheetMetalHistory.Read(id,id.GetObjectOfType<HistoryItem>()!.HostSolidID);
    }
    internal static Snapshot Capture(Mc3dSolid body,Scope scope,ModifyArguments args,DocumentState state,Db.Transaction tx,Action cancel,bool before)
    {
        var owner=ModelProfileDiscovery348.OwnerDocument(body,state);var representation=ModelHistoryEditGuard.CurrentRepresentation(body,state,tx,cancel);var contents=ModelInventoryBudget.History(body,cancel).ToArray();
        var sketch=ModelSketchLifecycleReader362.Resolve(Key(scope.Sketch),owner,cancel);var views=ModelViewFollowers.Resolve(body,contents,cancel,includeStandalone:true);
        var previous=new Dictionary<string,string?>(StringComparer.OrdinalIgnoreCase);var links=new List<ModelSequenceLink>();var features=new List<string>();var construction=new List<object>();
        foreach(var id in contents.OrderBy(Key,StringComparer.OrdinalIgnoreCase))
        {
            cancel();var item=id.GetObjectOfType<HistoryItem>();string? predecessor=null;
            if(item!=null)
            {
                Need(item.ID==id&&item.HostSolidID==body.ID&&item.DbEntity.Document?.ID==owner.ID&&!item.IsBeyondEndOfPart&&
                    (item.Status.ToString()=="Ok"||ProjectedHistorySources.HealthySuppressed(item)||before&&scope.Recovering.Contains(Key(id))&&item.Status.ToString()=="Error"&&!item.IsSuppressed),
                    "MODEL_STATE_UNAVAILABLE","An unrelated/unclassified retained history item is unhealthy, inactive or foreign. Only the frozen lost-plane propagation is admitted before recovery.");
                predecessor=Id(item.OrderObjBefore);construction.Add(new{id=Key(id),owner_document_id=Key(owner.ID),layer=item.DbEntity.Layer,visibility=item.DbEntity.Visibility,
                    native_metadata=ModelSketchLifecycleReader362.Metadata(id,item.DbEntity,owner,state,tx,cancel)});
            }
            previous.Add(Key(id),predecessor);links.Add(new(Key(id),predecessor));if(ModelFeatureReorderState345.Forming(id))features.Add(Key(id));
        }
        var order=ModelFeatureReorderPlanning345.FeatureOrder(links.ToArray(),features.ToArray(),Key(views.LastModelItem),cancel);
        var selected=SelectedSketch(sketch,owner,state,tx,cancel,out var memberIds);
        // Ordinary history/source readers deliberately ask for WCS membership.
        // Do not call them for the selected failed sketch: the independently
        // observed local inventory above is its complete explicit substitute.
        // Every other path/support still uses the existing strict inspector.
        var sources=NativePathSources.Inspect(contents.Where(id=>id!=scope.Sketch).ToArray(),cancel);
        Need(sources.Failures.Count==0,"NATIVE_PATH_STATE_UNAVAILABLE","A retained source/path failure lies outside this exact failed sketch support.");
        var externalObserved=ProjectedHistorySources.Read(body,contents,state,tx,cancel);
        var externalIds=externalObserved.Select(source=>source.id).ToHashSet(StringComparer.OrdinalIgnoreCase);var localIds=memberIds.ToHashSet();
        var history=new List<object>();
        foreach(var id in contents.OrderBy(Key,StringComparer.OrdinalIgnoreCase))
        {
            cancel();var item=id.GetObjectOfType<HistoryItem>();
            if(item==null)
            {
                Need(localIds.Contains(id)||sources.Sources.Contains(Key(id))||externalIds.Contains(Key(id)),"HISTORY_UNAVAILABLE","A retained non-history object lacks an exact local sketch, bound source or external-source observation.");
                history.Add(new{mc_id=Key(id),runtime_type=id.QueryObjectType()?.FullName,name=(string?)null,status=(string?)null,suppressed=(bool?)null,host_solid_id=(string?)null,
                    native_order_before_id=(string?)null,dependencies=Array.Empty<string>(),parameters=new{kind=localIds.Contains(id)?"selected_local_sketch_member_or_control":externalIds.Contains(Key(id))?"retained_external_source":"retained_native_source"},detail_unavailable_reason=(string?)null});
                continue;
            }
            var dependencies=item.GetDependsOn(false,false);Need(dependencies!=null,"HISTORY_UNAVAILABLE","A retained history dependency inventory is unavailable.");ModelInventoryBudget.Ids(dependencies,cancel);
            var parameters=Parameters(id,cancel);Need(!ModelFeatureReorderState345.Forming(id)||parameters!=null,"MODEL_PARAMETERS_UNAVAILABLE","A retained forming feature needs its complete typed preservation adapter.");
            history.Add(new{mc_id=Key(id),runtime_type=id.QueryObjectType()?.FullName,name=item.Name,status=item.Status.ToString(),suppressed=(bool?)item.IsSuppressed,host_solid_id=Id(item.HostSolidID),
                native_order_before_id=Id(item.OrderObjBefore),dependencies=Sorted(dependencies),parameters,detail_unavailable_reason=(string?)null});
        }
        var nodes=ModelDependencyGraph410.Nodes(Wire.Element(history),contents,cancel);
        var profiles=new List<object>();var peers=new List<JsonElement>();
        foreach(var id in contents.OrderBy(Key,StringComparer.OrdinalIgnoreCase))
        {
            cancel();var profile=id.GetObjectOfType<SketchProfile>();
            if(profile!=null)profiles.Add(profile.ParentSketchID==scope.Sketch?LocalProfile(profile,cancel):ModelProfileRebindProfiles414.Capture(profile,body,state,tx,cancel).Stored);
            var peer=id.GetObjectOfType<PlanarSketch>();if(peer!=null&&id!=scope.Sketch)peers.Add(ModelSketchLifecycleReader362.Capture(body,peer,state,tx,cancel).Stored);
        }
        var external=new List<object>();
        foreach(var source in externalObserved)
        {
            var raw=CadStructureWriter.Find(state.Document.Database,source.handle);var entity=(Db.Entity)tx.GetObject(raw,Db.OpenMode.ForRead);
            var detail=Wire.Element(EntityAdapter.Detail(entity,EntityAdapter.Summary(entity,entity.OwnerId,"","",false),CancellationToken.None));
            external.Add(new{id=source.id,source,geometry=detail.GetProperty("geometry"),properties=EntityAdapter.StoredProperties(entity),metadata=ModelProfileElementsState358.Metadata(entity,tx,cancel,false,observeNativeProxy:true)});
        }
        var variables=Variables(owner,cancel);var solid=(Db.Solid3d)tx.GetObject(representation,Db.OpenMode.ForRead);
        Need(!solid.IsNull,"INVALID_GEOMETRY","The selected part has no nonempty current native representation.");
        using(var brep=new Teigha.BoundaryRepresentation.Brep(solid))Need(!brep.IsNull&&brep.CheckEntity(),"INVALID_GEOMETRY","The selected part's native representation is invalid.");
        var bodyMetadata=ModelProfileElementsState358.Metadata(solid,tx,cancel,true,observeNativeProxy:true);
        var storage=ModelSketchConstraintMetadata379.Bind(contents.Concat(memberIds),representation,owner,ModelProfileDiscovery348.DrawingDocument(state),state,tx,cancel);
        SketchPlaneSpec? frame=null;try{frame=ModelSketchSupportState396.Frame(sketch);}catch(Exception error)when(error is not (OperationCanceledException or OutOfMemoryException)){}
        var notices=Wire.Element(scope.Recovering.OrderBy(id=>id,StringComparer.OrdinalIgnoreCase).Select(id=>new{id,status=contents.Single(native=>Key(native)==id).GetObjectOfType<HistoryItem>()!.Status.ToString(),records=Notices(contents.Single(native=>Key(native)==id).GetObjectOfType<HistoryItem>()!,cancel)}).ToArray());
        var design=Wire.Element(new{body_id=Key(body.ID),owner_document_id=Key(owner.ID),last_item_id=Id(body.LastPartItemID),nodes,selected_sketch=selected,profiles,peer_sketches=peers,construction,
            variables,external_sources=external,native_source_ids=sources.Sources.OrderBy(id=>id,StringComparer.OrdinalIgnoreCase).ToArray(),
            properties=EntityAdapter.StoredProperties(solid)});new ModelInventoryBudget(cancel).Add(new{design,bodyMetadata});
        var retirement=scope.UseTargetDatum&&before?ModelSketchFrameRetirement440.Capture(body,sketch,selected,nodes,contents,storage,cancel):null;
        string token=Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(new{design,bodyMetadata,notices,frame,retirement=retirement?.stored},Wire.Json)));
        return new(body,representation,owner,contents,previous,order,design,bodyMetadata,storage,views,frame,notices,token,retirement);
    }
    static JsonElement Protected(Snapshot value,Scope scope,bool acquired,Action cancel,string[]? retiredFrameIds=null)
    {
        var root=JsonNode.Parse(value.Design.GetRawText())!.AsObject();
        root["nodes"]=JsonNode.Parse(ModelDependencyGraph410.ProtectedNodes(Wire.Element(new{nodes=value.Design.GetProperty("nodes")}),scope.Affected,cancel:cancel).GetRawText());
        foreach(var node in root["nodes"]!.AsArray())
        {
            cancel();string id=node!["mc_id"]!.GetValue<string>();node.AsObject().Remove("native_order_before_id");
            // Assigned ordering has its own exact before/after proof below.
            // Typed profile/axis/source/driver IDs remain in parameters and in
            // the separately observed selected local graph.
            foreach(string field in new[]{"dependencies","stable_dependencies"})if(node[field] is JsonArray dependencies)
                node[field]=JsonSerializer.SerializeToNode(dependencies.Select(entry=>entry!.GetValue<string>()).Where(dependency=>dependency!=value.Previous[id]).OrderBy(dependency=>dependency,StringComparer.OrdinalIgnoreCase).ToArray(),Wire.Json);
            if(scope.Recovering.Contains(id)){node["status"]="$RECOVERED_LOST_PLANE_GRAPH";node["detail_unavailable_reason"]=null;}
            if(node["parameters"] is JsonObject parameters)parameters.Remove("native_order_before_id");
            if(id==Key(scope.Sketch))
            {
                // Use the uncollapsed explicit dependency inventory here: a
                // historical face may collapse to its body in the stable view.
                var raw=value.Design.GetProperty("nodes").EnumerateArray().Single(item=>item.GetProperty("mc_id").GetString()==id);
                node["dependencies"]=JsonSerializer.SerializeToNode(raw.GetProperty("dependencies").EnumerateArray().Select(item=>item.GetString()!).Where(dependency=>dependency!=Key(scope.OldSupport)&&dependency!=Key(scope.NewSupport)&&dependency!=value.Previous[id]&&!(retiredFrameIds??[]).Contains(dependency,StringComparer.OrdinalIgnoreCase)).OrderBy(dependency=>dependency,StringComparer.OrdinalIgnoreCase).ToArray(),Wire.Json);
                node["stable_dependencies"]=JsonNode.Parse(node["dependencies"]!.ToJsonString());
                var p=node["parameters"]!.AsObject();foreach(string field in new[]{"support_id","support_definition","support_offset","support_parameter_id"})if(p.ContainsKey(field))p[field]=null;
            }
        }
        root["native_source_ids"]=JsonSerializer.SerializeToNode(value.Design.GetProperty("native_source_ids").EnumerateArray().Select(id=>id.GetString()!).Where(id=>id!=Key(scope.OldSupport)&&id!=Key(scope.NewSupport)).ToArray(),Wire.Json);
        if(!scope.DatumDriver.IsNull)foreach(var variable in root["variables"]!["variables"]!.AsArray())
            if(variable?["id"]?.GetValue<string>()==Key(scope.DatumDriver)&&variable["host_object_id"]?.GetValue<string>()==Key(scope.NewSupport)&&variable["downstream_objects"] is JsonArray consumers)
                variable["downstream_objects"]=JsonSerializer.SerializeToNode(consumers.Select(entry=>entry!.GetValue<string>()).Where(id=>!scope.Affected.Contains(id)).OrderBy(id=>id,StringComparer.OrdinalIgnoreCase).ToArray(),Wire.Json);
        if(acquired)foreach(string field in new[]{"nodes","construction"})
        {
            var rows=root[field]!.AsArray();for(int index=rows.Count-1;index>=0;index--)if(rows[index]?[field=="nodes"?"mc_id":"id"]?.GetValue<string>()==Key(scope.NewSupport))rows.RemoveAt(index);
        }
        return Wire.Element(root);
    }
    static object FailureDiagnostics(Snapshot before,Snapshot after,Scope scope,JsonElement prior,
        ModelSketchConstraintMetadata379.Observation storage,Action cancel)
    {
        // Completed managed snapshots only. Identity-keyed diagnostic views do
        // not replace ordered/exact guard comparisons and never waive storage.
        JsonElement Keyed(JsonElement rows,string field)
        {
            var result=new JsonObject();foreach(var row in rows.EnumerateArray()){cancel();result[row.GetProperty(field).GetString()!]=JsonNode.Parse(row.GetRawText());}return Wire.Element(result);
        }
        JsonElement Body(JsonElement metadata)
        {
            var root=JsonNode.Parse(metadata.GetRawText())!.AsObject();var records=new JsonObject();
            foreach(var row in metadata.GetProperty("owned").EnumerateArray())
            {cancel();records[row.GetProperty("common").GetProperty("handle").GetString()!]=JsonNode.Parse(row.GetRawText());}
            root["owned"]=records;return Wire.Element(root);
        }
        var oldNodes=Keyed(prior.GetProperty("nodes"),"mc_id");var newNodes=Keyed(storage.design.GetProperty("nodes"),"mc_id");
        var oldConstruction=Keyed(prior.GetProperty("construction"),"id");var newConstruction=Keyed(storage.design.GetProperty("construction"),"id");
        var identity=new ModelRepresentationDiagnostics377.Identity("set_model_sketch_support.recover_lost_support",Key(before.Body.ID),
            Validation.NormalizeHandle(before.Representation.Handle.ToString()),Validation.NormalizeHandle(after.Representation.Handle.ToString()),
            Key(before.Owner.ID),Key(after.Owner.ID),Key(before.Owner.ID),Key(after.Owner.ID),null,null,
            before.BodyMetadata.GetProperty("owned").GetArrayLength(),after.BodyMetadata.GetProperty("owned").GetArrayLength());
        var diagnostic=ModelRepresentationDiagnostics377.Describe(identity,
            [new("nodes",Equal(prior.GetProperty("nodes"),storage.design.GetProperty("nodes"),cancel),"actual_existing_protected_node_guard; identities_keyed_for_diagnostic_only",oldNodes,newNodes),
             new("construction",Equal(prior.GetProperty("construction"),storage.design.GetProperty("construction"),cancel),"actual_existing_construction_guard; identities_keyed_for_diagnostic_only",oldConstruction,newConstruction),
             new("body_metadata",Equal(before.BodyMetadata,storage.body,cancel),"actual_existing_storage_guard; owned_handles_keyed_for_diagnostic_only",Body(before.BodyMetadata),Body(storage.body))],cancel);
        var changed=oldConstruction.EnumerateObject().Where(row=>!newConstruction.TryGetProperty(row.Name,out var current)||!Equal(row.Value,current,cancel)).Select(row=>row.Name)
            .Concat(newConstruction.EnumerateObject().Where(row=>!oldConstruction.TryGetProperty(row.Name,out _)).Select(row=>row.Name));
        var ids=new[]{Key(scope.Sketch),Key(before.Body.ID)}.Concat(changed).Concat(scope.Affected).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        object? Proof(ModelSketchConstraintMetadata379.Binding? value)=>value==null?null:new{value.raw_handle,value.proved,value.reason,owner_document_id=value.owner_document_id,owner_path_count=value.owner_path.Length};
        var proofs=ids.Select(id=>new{native_id=id,before=Proof(before.Storage.SingleOrDefault(value=>value.native_id.Equals(id,StringComparison.OrdinalIgnoreCase))),
            after=Proof(after.Storage.SingleOrDefault(value=>value.native_id.Equals(id,StringComparison.OrdinalIgnoreCase)))}).ToArray();
        var reported=new List<object>();foreach(var proof in proofs){cancel();if(JsonSerializer.SerializeToUtf8Bytes(reported.Append(proof),Wire.Json).Length>1536)break;reported.Add(proof);}
        JsonElement Selected(Snapshot part)=>part.Design.GetProperty("nodes").EnumerateArray().Single(row=>row.GetProperty("mc_id").GetString()==Key(scope.Sketch));
        string[] Dependencies(Snapshot part)=>Selected(part).GetProperty("dependencies").EnumerateArray().Select(id=>id.GetString()!).ToArray();
        var old=Dependencies(before);var current=Dependencies(after);
        return new{snapshot_diagnostics=diagnostic,selected_sketch_frame_references=new{
                before=before.Design.GetProperty("selected_sketch").GetProperty("frame_references"),after=after.Design.GetProperty("selected_sketch").GetProperty("frame_references"),
                policy="exact_observed_native_bindings_and_flags_retained; no_reference_substitution_waived; no_old_WCS_inference"},
            storage_binding_proof=new{entries=reported,total_count=proofs.Length,omitted_count=proofs.Length-reported.Count,
                complete_proof_sha256=Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(proofs,Wire.Json))),
                scope="existing_exact_native_storage_bindings; no_new_identity_inference_or_private_payload_classification"},
            selected_sketch_explicit_dependency_change=new{removed=old.Except(current,StringComparer.OrdinalIgnoreCase).OrderBy(id=>id,StringComparer.OrdinalIgnoreCase).ToArray(),
                added=current.Except(old,StringComparer.OrdinalIgnoreCase).OrderBy(id=>id,StringComparer.OrdinalIgnoreCase).ToArray(),
                previous_support_id=Key(scope.OldSupport),chosen_support_id=Key(scope.NewSupport),scope="actual_raw_native_ID_edges; non_support_edges_not_waived"}};
    }
    internal static string[] Verify(Snapshot before,Snapshot after,Scope scope,Action cancel)
    {
        Need(before.Body.ID==after.Body.ID&&before.Owner.ID==after.Owner.ID&&before.Features.SequenceEqual(after.Features),"MODEL_ORDER_MISMATCH","Support recovery changed native part ownership or forming-feature order.");
        bool acquired=!before.Contents.Contains(scope.NewSupport)&&after.Contents.Contains(scope.NewSupport);
        Need(before.Contents.ToHashSet().SetEquals(after.Contents.Where(id=>!acquired||id!=scope.NewSupport)),"MODEL_ORDER_MISMATCH","Support recovery changed native identities outside its acquired target WorkPlane.");
        foreach(var link in before.Previous)
        {
            cancel();Need(after.Previous.TryGetValue(link.Key,out var current),"MODEL_ORDER_MISMATCH","A retained history item disappeared during support recovery.");if(current==link.Value)continue;
            bool sketch=acquired&&link.Key==Key(scope.Sketch)&&current==Key(scope.NewSupport)&&after.Previous[Key(scope.NewSupport)]==link.Value;
            bool profileConsumer=false;
            if(acquired&&link.Value==Key(scope.Sketch)&&current==before.Previous[Key(scope.Sketch)]&&after.Previous[Key(scope.Sketch)]==before.Previous[Key(scope.Sketch)])
            {
                var id=after.Contents.Single(native=>Key(native)==link.Key);var feature=id.GetObjectOfType<ExtrudeFeature>();profileConsumer=feature!=null&&scope.Profiles.Contains(feature.ProfileID);
            }
            Need(sketch||profileConsumer,"MODEL_ORDER_MISMATCH","Support recovery changed an unrelated retained predecessor.");
        }
        if(!scope.DatumDriver.IsNull)
        {
            JsonElement Driver(Snapshot part)=>part.Design.GetProperty("variables").GetProperty("variables").EnumerateArray().Single(row=>row.GetProperty("id").GetString()==Key(scope.DatumDriver)&&row.GetProperty("host_object_id").GetString()==Key(scope.NewSupport));
            Need(ModelProfileConsumers410.RetainedVariable(Driver(before),Driver(after),scope.Affected,cancel),"MODEL_DRIVER_CHANGED","The chosen native datum driver changed ID, value, formula or an existing/foreign consumer outside its observed recovered-sketch graph.");
        }
        var retired=scope.UseTargetDatum?ModelSketchFrameRetirement440.FrameIds(before.Design.GetProperty("selected_sketch")):[];
        var prior=Protected(before,scope,acquired,cancel,retired);var currentDesign=Protected(after,scope,acquired,cancel,retired);
        var beforeBody=before.BodyMetadata;var afterBody=after.BodyMetadata;
        bool frameReferencesRetained=SameFrameReferences(before,after);bool framePolicyVerified=frameReferencesRetained;
        if(scope.UseTargetDatum)
        {
            var replacement=ModelSketchFrameRetirement440.Normalize(before,after,scope,prior,currentDesign,cancel);
            prior=replacement.before_design;currentDesign=replacement.after_design;beforeBody=replacement.before_body;afterBody=replacement.after_body;
            framePolicyVerified=true; // Separate typed replacement proof above; default still requires exact preservation.
        }
        var storage=ModelSketchConstraintMetadata379.Compare(prior,currentDesign,beforeBody,afterBody,before.Storage,after.Storage,cancel);
        if(!framePolicyVerified||!storage.retained||!Equal(prior,storage.design,cancel)||!Equal(beforeBody,storage.body,cancel))
            throw new DomainException("MODEL_DEPENDENCY_CHANGED","Support recovery changed retained local geometry, selected profile regions, native controls/drivers, source references, user data or unrelated model state.",false,
                new{storage_reason=storage.reason,frame_references_retained=frameReferencesRetained,frame_reference_policy=scope.UseTargetDatum?"use_target_datum":"preserve",frame_reference_policy_verified=framePolicyVerified,
                    retired_source_proof=before.Retirement?.stored,changed_design_fields=prior.EnumerateObject().Where(field=>!storage.design.TryGetProperty(field.Name,out var actual)||!Equal(field.Value,actual,cancel)).Select(field=>field.Name).ToArray(),
                    body_metadata_changed=!Equal(beforeBody,storage.body,cancel),comparison_details=FailureDiagnostics(before,after,scope,prior,storage,cancel)});
        ModelViewFollowers.Verify(after.Body,before.Views,cancel,refreshStandalone:true);return storage.changed?[ModelSketchConstraintMetadata379.Warning]:[];
    }
    internal static string[] VerifyCreatedDatum(Snapshot before,Snapshot after,Action cancel)
    {
        // Creating unconsumed document construction must leave the failed
        // native part exactly as observed. This is not an early repair claim.
        Need(before.Body.ID==after.Body.ID&&before.Owner.ID==after.Owner.ID&&before.Representation==after.Representation&&
            before.Contents.ToHashSet().SetEquals(after.Contents)&&before.Features.SequenceEqual(after.Features)&&
            before.Previous.Count==after.Previous.Count&&before.Previous.All(link=>after.Previous.TryGetValue(link.Key,out var actual)&&actual==link.Value),
            "MODEL_ORDER_MISMATCH","Creating recovery construction changed the selected failed part's identity, history composition or assigned order.");
        Need(Equal(before.Notices,after.Notices,cancel),"RECOVERY_DIAGNOSIS_CHANGED","Creating the unconsumed datum changed the established failed sketch/profile/extrusion status records.");
        Need(SameFrameReferences(before,after),"SKETCH_FRAME_REFERENCES_CHANGED","Creating the unconsumed recovery datum changed exact native frame-reference IDs, markers, scalar bits, user data or flags.");
        var storage=ModelSketchConstraintMetadata379.Compare(before.Design,after.Design,before.BodyMetadata,after.BodyMetadata,before.Storage,after.Storage,cancel);
        Need(storage.retained&&Equal(before.Design,storage.design,cancel)&&Equal(before.BodyMetadata,storage.body,cancel),
            "MODEL_DEPENDENCY_CHANGED","Creating the recovery datum changed the existing failed local geometry, selected profiles, native controls/drivers, source references, user data or unrelated model state.");
        if(before.Frame!=null)Need(after.Frame!=null&&ModelSketchSupportRecoveryFrame440.Same(before.Frame,after.Frame),"SKETCH_FRAME_CHANGED","Creating unconsumed recovery construction changed the observed original sketch frame.");
        ModelViewFollowers.CheckStandalone(after.Body,before.Views,cancel);return storage.changed?[ModelSketchConstraintMetadata379.Warning]:[];
    }
}
