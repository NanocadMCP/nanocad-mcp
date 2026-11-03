using System.Text.Json.Nodes;
using Multicad;
using Multicad.DatabaseServices;
using Multicad.Mc3D;
using NanoCAD.Contracts;

namespace NanoCADBridge;

// Only proven parameter identities/units, never inferred from IsAngularValue alone.
internal sealed record ModelFeatureVariable(McObjectId ID,McObjectId Variable,bool IsAngle,int PlacementComponent=-1,string? FinishingKind=null,string Role="extrusion_distance")
{
    internal bool IsPlacement=>PlacementComponent>=0;
    internal bool IsFinishing=>FinishingKind!=null;
    internal bool IsCount=>Role is "rect_count_x" or "rect_count_y" or "circular_count";
    internal string Unit=>IsAngle?"degrees":IsCount?"count":"Millimeters";
    internal double Value=>IsPlacement?Component(ModelPlacementHistory.Read(ID).Vector,PlacementComponent):Role switch
    {
        "revolution_angle"=>ID.GetObjectOfType<RevolveFeature>()!.Angle*180/Math.PI,
        "extrusion_distance2"=>ID.GetObjectOfType<ExtrudeFeature>()!.Distance2,
        "fillet_radius"=>ID.GetObjectOfType<FilletFeature>()!.Radius,
        "chamfer_distance"=>ID.GetObjectOfType<ChamferFeature>()!.Distance,
        "chamfer_distance2"=>ID.GetObjectOfType<ChamferFeature>()!.Distance2,
        "thread_pitch"=>ID.GetObjectOfType<ThreadFeature>()!.Pitch,
        "thread_length"=>ID.GetObjectOfType<ThreadFeature>()!.Length,
        "rect_count_x"=>ID.GetObjectOfType<RectangularPatternFeature>()!.SizeX,
        "rect_count_y"=>ID.GetObjectOfType<RectangularPatternFeature>()!.SizeY,
        "rect_step_x"=>ID.GetObjectOfType<RectangularPatternFeature>()!.DistancesX,
        "rect_step_y"=>ID.GetObjectOfType<RectangularPatternFeature>()!.DistancesY,
        "circular_count"=>ID.GetObjectOfType<CircularPatternFeature>()!.Size,
        "circular_angle"=>ID.GetObjectOfType<CircularPatternFeature>()!.Angle*180/Math.PI,
        "datum_offset"=>ID.GetObjectOfType<WorkPlane>()!.OffsetParamVal,
        "extrusion_taper" or "extrusion_taper2" or "sweep_taper"=>ModelTaperParameter412.ValueDegrees(ID,Variable,Role),
        "shell_thickness"=>ModelShellParameter412.Value(ID,Role),
        "hole_offset1" or "hole_offset2"=>ModelHoleParameter412.Value(ID,Role),
        _=>ID.GetObjectOfType<ExtrudeFeature>()!.Distance
    };
    internal void ValidateRequested(double value)
    {
        if(!double.IsFinite(value)||IsCount&&(value<1||value!=Math.Truncate(value)))throw new DomainException("INVALID_GEOMETRY","Native count parameters require a positive integer and all parameters must be finite.");
        if(ModelTaperParameter412.Handles(Role))ModelTaperParameter412.ValidateRequested(value);
        if(Role=="shell_thickness"){ModelShellParameter412.ValidateRequested(ID.GetObjectOfType<ThicknessFeature>()!,Role,value);return;}
        if(Role is "hole_offset1" or "hole_offset2")ModelHoleParameter412.ValidateRequested(ID.GetObjectOfType<HoleFeature>()!,Role,value);
        var owner=ID.GetObjectOfType<HistoryItem>();
        if(owner==null||!owner.IsValidVarValForObject(Variable,value))throw new DomainException("INVALID_GEOMETRY","The native owning object rejects the requested control value.");
    }
    internal void RemoveObservedValue(JsonObject parameters)
    {
        if(ModelTaperParameter412.Handles(Role)){ModelTaperParameter412.RemoveObservedValue(parameters,Role);return;}
        if(Role=="shell_thickness"){ModelShellParameter412.RemoveObservedValue(parameters,Role);return;}
        if(Role is "hole_offset1" or "hole_offset2"){ModelHoleParameter412.RemoveObservedValue(parameters,Role);return;}
        if(IsPlacement){if(parameters[IsAngle?"angles_degrees":"displacement"] is JsonArray vector)vector[PlacementComponent]=null;return;}
        string field=Role switch{"revolution_angle"=>"angle","extrusion_distance2"=>"distance2","fillet_radius"=>"radius","chamfer_distance2"=>"distance2","thread_pitch"=>"pitch","thread_length"=>"length","rect_count_x"=>"count_x","rect_count_y"=>"count_y","rect_step_x"=>"step_x","rect_step_y"=>"step_y","circular_count"=>"count","circular_angle"=>"angle_degrees","datum_offset"=>"support_offset",_=>"distance"};
        parameters.Remove(field);if(parameters["terminal"] is JsonObject terminal)terminal.Remove(field);if(Role=="revolution_angle")parameters.Remove("full_turn");
    }
    internal static double Component(Point3 vector,int axis)=>axis switch{0=>vector.X,1=>vector.Y,2=>vector.Z,_=>throw new ArgumentOutOfRangeException(nameof(axis))};
    internal static ModelFeatureVariable Resolve(Mc3dSolid body,McObjectId featureId,McObjectId variable,bool angular,bool allowPlacement=false,bool allowFinishing=false,Action? cancel=null)
    {
        var contents=ModelInventoryBudget.History(body,cancel);
        if(variable.IsNull||!contents.Contains(featureId))throw new DomainException("OUT_OF_SCOPE","Parameter owner must belong to the selected body history.");
        var item=featureId.GetObjectOfType<HistoryItem>();
        if(item==null||item.HostSolidID!=body.ID||!(item.Status.ToString()=="Ok"||ProjectedHistorySources.HealthySuppressed(item)))throw new DomainException("MODEL_STATE_UNAVAILABLE","The native control owner is unavailable or unhealthy.");
        // Typed public IDs select these angular roles even when the generic
        // IsAngularValue flag is absent. Current owner-MCS/getter coherence is
        // established by the adapter; post-rebuild uses the same exact getter.
        var taper=ModelTaperParameter412.Resolve(featureId,variable,body.DbEntity.Document,cancel);
        if(taper!=null)return new(featureId,variable,true,Role:taper.Role);
        var shell=featureId.GetObjectOfType<ThicknessFeature>();
        if(shell!=null&&ModelShellParameter412.Resolve(shell,variable,angular,cancel) is {} thickness)return thickness;
        var hole=featureId.GetObjectOfType<HoleFeature>();
        if(hole!=null&&ModelHoleParameter412.Resolve(hole,variable,angular,cancel) is {} position)return position;
        var distance=featureId.GetObjectOfType<ExtrudeFeature>();
        if(!angular&&distance!=null&&distance.HostSolidID==body.ID&&distance.DistanceParamVarID==variable&&distance.Extent.ToString()=="Dist")
            return new(featureId,variable,false);
        if(!angular&&distance!=null&&distance.Distance2ParamVarID==variable&&distance.Extent2.ToString()=="Dist")return new(featureId,variable,false,Role:"extrusion_distance2");
        var angle=featureId.GetObjectOfType<RevolveFeature>();
        if(angular&&angle!=null&&angle.HostSolidID==body.ID&&angle.RotationAngleParamVarID==variable&&angle.Extent==RevolveParamExtType.Angle)
            return new(featureId,variable,true,Role:"revolution_angle");
        if(allowFinishing&&!angular)
        {
            var fillet=featureId.GetObjectOfType<FilletFeature>();
            if(fillet!=null&&fillet.HostSolidID==body.ID&&
               fillet.RadiusParamVarID==variable&&double.IsFinite(fillet.Radius)&&fillet.Radius>0)
                return new(featureId,variable,false,FinishingKind:"fillet",Role:"fillet_radius");
            var chamfer=featureId.GetObjectOfType<ChamferFeature>();
            if(chamfer!=null&&chamfer.HostSolidID==body.ID&&
               chamfer.DistanceParamVarID==variable&&double.IsFinite(chamfer.Distance)&&chamfer.Distance>0)
                return new(featureId,variable,false,FinishingKind:"chamfer",Role:"chamfer_distance");
            if(chamfer!=null&&chamfer.Distance2ParamVarID==variable)return new(featureId,variable,false,FinishingKind:"chamfer",Role:"chamfer_distance2");
        }
        if(!angular)
        {
            var thread=featureId.GetObjectOfType<ThreadFeature>();
            if(thread!=null){if(thread.PitchVarID==variable)return new(featureId,variable,false,Role:"thread_pitch");if(thread.LengthVarID==variable&&!thread.IsFullLength)return new(featureId,variable,false,Role:"thread_length");}
            var pattern=featureId.GetObjectOfType<RectangularPatternFeature>();
            if(pattern!=null){if(pattern.SizeXParamVarID==variable)return new(featureId,variable,false,Role:"rect_count_x");if(pattern.SizeYParamVarID==variable)return new(featureId,variable,false,Role:"rect_count_y");if(pattern.DistancesXParamVarID==variable)return new(featureId,variable,false,Role:"rect_step_x");if(pattern.DistancesYParamVarID==variable)return new(featureId,variable,false,Role:"rect_step_y");}
            var plane=featureId.GetObjectOfType<WorkPlane>();if(plane!=null&&plane.DefinitionType==WPLDefinitionType.PlaneNOffset&&plane.OffsetParamVarID==variable)return new(featureId,variable,false,Role:"datum_offset");
        }
        var circular=featureId.GetObjectOfType<CircularPatternFeature>();
        if(circular!=null){if(!angular&&circular.CountParamVarID==variable)return new(featureId,variable,false,Role:"circular_count");if(angular&&circular.AngleParamVarID==variable)return new(featureId,variable,true,Role:"circular_angle");}
        if(allowPlacement&&ModelPlacementHistory.IsPlacement(featureId))
        {
            var feature=featureId.GetObjectOfType<HistoryItem>();var placement=ModelPlacementHistory.Read(featureId);
            var ids=ModelPlacementHistory.Variables(featureId);int component=Array.IndexOf(ids,variable);
            if(feature!.HostSolidID==body.ID&&component>=0&&!variable.IsNull&&ids.Distinct().Count()==3&&(!angular||placement.Kind=="rotate"))
                return new(featureId,variable,placement.Kind=="rotate",component);
        }
        throw new DomainException("CAPABILITY_UNAVAILABLE","The exact native control ID, active mode or units lack a supported typed binding; use its owning specialized editor. No unit is inferred from a name or generic angular flag.");
    }
}
