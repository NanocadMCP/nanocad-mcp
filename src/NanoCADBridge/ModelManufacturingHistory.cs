using Multicad;
using Multicad.Mc3D;
namespace NanoCADBridge;
internal static class ModelManufacturingHistory
{
    static object? Placement(IppDefinition? p)
    {
        if(p==null)return null;
        return new{placement_type=p.DefType.ToString(),offset1=p.Param1,offset2=p.Param2,sector=p.Sector,support_face_id=p.TargetPlaneId.ToString()};
    }
    internal static object? Parameters(McObjectId id,Action? cancel=null)
    {
        var budget=new ModelInventoryBudget(cancel);
        string? Ref(McObjectId reference){var value=reference.IsNull?null:reference.ToString();budget.Add(value);return value;}
        var point=id.GetObjectOfType<WorkPoint>();
        if(point!=null){var p=point.Position;return new{kind="construction_point",definition=point.DefinitionType.ToString(),position=new[]{p.X,p.Y,p.Z},reference_ids=point.GetDefinitionParams().Where(x=>!x.IsNull).Select(x=>x.ID.ToString()).ToArray()};}
        var axis=id.GetObjectOfType<WorkAxis>();
        if(axis!=null){using var line=axis.PositionAsLineSeg;return new{kind="construction_axis",definition=axis.DefinitionType.ToString(),start=new[]{line.StartPoint.X,line.StartPoint.Y,line.StartPoint.Z},end=new[]{line.EndPoint.X,line.EndPoint.Y,line.EndPoint.Z},reference_ids=axis.GetDefinitionParams().Where(x=>!x.IsNull).Select(x=>x.ID.ToString()).ToArray()};}
        var sketch=id.GetObjectOfType<PlanarSketch>();if(sketch!=null)return ModelSketchSupport.Read(sketch);
        var extrusion=id.GetObjectOfType<ExtrudeFeature>();if(extrusion!=null)return ModelExtrusionExtentEditor.Read(extrusion);
        var loft=id.GetObjectOfType<LoftFeature>();
        if(loft!=null){
            var sections=Enumerable.Range(0,loft.SectionsCount).Select(i=>{cancel?.Invoke();var section=loft.GetSection(i);if(section==null){budget.Add<object?>(null);return null;}return Ref(section.ID);}).ToArray();
            var guides=Enumerable.Range(0,loft.GuideRailsCount).Select(i=>{cancel?.Invoke();return loft.GetGuideRailsParams(i)?.Select(p=>{cancel?.Invoke();if(p==null){budget.Add<object?>(null);return null;}return Ref(p.ID);}).ToArray();}).ToArray();
            return new{kind="loft",profile_id=loft.ProfileID.ToString(),section_profile_ids=sections,
                sections_complete=sections.Length>0&&sections.All(s=>s!=null),loft_type=loft.LoftType.ToString(),closed=loft.Closed,
                path_id=loft.LoftType==LoftType.WithCenterLine?loft.Path?.ID.ToString():null,guide_ids=guides,
                guide_bindings_complete=guides.All(g=>g!=null&&g.Length>0&&g.All(s=>s!=null))};
        }
        var sweep=id.GetObjectOfType<ExtrudeAlongPathFeature>();
        if(sweep!=null){var paths=sweep.GetPath();return new{kind="sweep",profile_id=sweep.ProfileID.ToString(),orientation=sweep.ProfileOrientation.ToString(),taper_angle=sweep.Angle,part_operation=sweep.PartOperation.ToString(),taper_variable_id=sweep.TaperAngleParamVarID.ToString(),
            path_ids=paths?.Select(p=>{cancel?.Invoke();if(p==null){budget.Add<object?>(null);return null;}return Ref(p.ID);}).ToArray(),path_bindings_complete=paths!=null&&paths.Count>0&&paths.All(p=>p!=null&&!p.ID.IsNull)};}
        var h=id.GetObjectOfType<HoleFeature>();
        if(h!=null){var p=h.GetPlacement();return new{kind="hole",diameter=h.Diameter,through=h.IsFullDepth,depth=h.Depth,seat=h.SeatType.ToString(),seat_diameter=h.SeatDiameter,seat_depth=h.SeatDepth,seat_angle_degrees=h.SeatAngle*180/Math.PI,flat_bottom=h.FlatBottom,bottom_angle_degrees=h.BottomAngle*180/Math.PI,face_id=h.FaceId.ToString(),terminal_face_id=h.TerminalFaceId.ToString(),placement=Placement(p),thread_id=h.GetThread()?.ID.ToString()};}
        var t=id.GetObjectOfType<ThreadFeature>();
        if(t!=null)return new{kind="thread",pitch=t.Pitch,right_handed=t.IsRight,@internal=t.IsInternal,full_length=t.IsFullLength,length=t.Length,minimum_diameter=t.MinDiameter,maximum_diameter=t.MaxDiameter,designation=t.Designation,face_id=t.ThreadFaceId.ToString(),front_edge_id=t.FrontEdgeId.ToString(),pitch_variable_id=t.PitchVarID.ToString(),length_variable_id=t.LengthVarID.ToString(),representation="native_thread_feature_helical_brep_unverified"};
        var c=id.GetObjectOfType<CircularPatternFeature>();
        if(c!=null)
        {
            var axisBinding=NativePatternObservation439.TryRead(c,0);var excluded=c.GetSuppressedElementsIndices();
            return new{kind="circular_pattern",count=c.Size,angle_degrees=c.Angle*180/Math.PI,count_variable_id=c.CountParamVarID.ToString(),angle_variable_id=c.AngleParamVarID.ToString(),
                axis=axisBinding,direction_bindings_complete=axisBinding!=null,
                source_item_ids=Enumerable.Range(0,c.SourceItemsCount).Select(index=>{cancel?.Invoke();var value=c.GetSourceItemFeature(index).ToString();budget.Add(value);return value;}).ToArray(),has_sub_feature_items=c.HasSubFeatureItems,
                suppressed_elements=(excluded??[]).Select(index=>{cancel?.Invoke();return new[]{index.Index1,index.Index2};}).ToArray(),suppressed_elements_complete=excluded!=null};
        }
        var m=id.GetObjectOfType<MirrorFeature>();
        if(m!=null)return new{kind="mirror",source_item_ids=Enumerable.Range(0,m.SourceItemsCount).Select(index=>{cancel?.Invoke();var value=m.GetSourceItemFeature(index).ToString();budget.Add(value);return value;}).ToArray(),has_sub_feature_items=m.HasSubFeatureItems,dependency_ids=(m.GetDependsOn(false,false)??[]).Select(x=>{cancel?.Invoke();var value=x.ToString();budget.Add(value);return value;}).ToArray()};
        return null;
    }
}
