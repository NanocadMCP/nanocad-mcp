using Multicad;
using Multicad.Mc3D;
using NanoCAD.Contracts;
using Db=Teigha.DatabaseServices;

namespace NanoCADBridge;

internal static class ModelExtrusionExtentsReader351
{
    internal static ModelExtrusionExtentsRead351 Describe(ModelExtrusionExtentsState351.Snapshot value)
    {
        var feature=value.Feature;static double? Finite(double number)=>double.IsFinite(number)?number:null;
        bool hasSecond=!feature.Distance2ParamVarID.IsNull||!feature.TaperAngle2ParamVarID.IsNull;
        return new(feature.ID.ToString(),value.Profile.ID.ToString(),value.Sketch.ID.ToString(),value.OwnerDocument,value.Token,
            feature.Direction.ToString(),feature.Extent.ToString(),feature.Extent2.ToString(),feature.Distance,feature.Angle,Finite(feature.Distance2),Finite(feature.Angle2),
            ModelProfileDiscovery348.Id(feature.DistanceParamVarID),ModelProfileDiscovery348.Id(feature.TaperAngleParamVarID),
            ModelProfileDiscovery348.Id(feature.Distance2ParamVarID),ModelProfileDiscovery348.Id(feature.TaperAngle2ParamVarID),value.Drivers,value.Plane,
            value.Parameters.GetProperty("native").GetProperty("bounds"),
            feature.Direction==FeatureExtentDirection.Different?"first_positive_normal_and_second_negative_normal_native_lengths":
                feature.Direction==FeatureExtentDirection.Symmetric?"one_native_Distance_driver_for_Symmetric; total_or_half_physical_span_not_assumed; bounds_describe_current_complete_body_WCS_not_isolated_feature":
                "first_native_distance_in_current_one_sided_direction; bounds_describe_current_complete_body_WCS",
            (feature.Direction==FeatureExtentDirection.Different&&feature.Extent2==ExtrudeParamExtType.All?"negative_side_ThroughAll_has_no_finite_requested_length; Distance2_is_inactive_stored_readback; ":"")+(hasSecond?"actual_observed_secondary_driver_IDs; null_IDs_have_no_owned_MCS_expression_claim":"secondary_native_property_values_only; no_observed_secondary_MCS_variable_IDs"),value.Sketches.EnumerateArray().Select(sketch=>sketch.GetProperty("constraints")).ToArray(),NativeAssocManagerObservation356.Describe(value),"shared_expression_plan_binds_actual_active_first_and_second_Dist_lengths_and_typed_first_or_active_Different_second_tapers; taper_owner_MCS_degrees_must_agree_with_the_matching_native_radian_getter; all_actual_affected_consumers_must_be_scoped; inactive_or_missing_driver_IDs_are_not_allocated; retained_formula_alone_is_not_reactive_edit_acceptance",ModelExtrusionOwnedMetadata357.Scope,value.Terminal.GetProperty("first").GetString(),value.Terminal.GetProperty("second").GetString());
    }
    internal static ModelExtrusionExtentsRead351[] Read(string bodyHandle,string[] featureIds,Arguments args,DocumentState state,Db.Transaction tx,Action cancel)
    {
        var drawing=ModelFeatureReorderState345.Scope(state,tx);var representation=CadStructureWriter.Find(state.Document.Database,bodyHandle);var raw=tx.GetObject(representation,Db.OpenMode.ForRead);
        ModelExtrusionExtentsState351.Need(raw.GetType()==typeof(Db.Solid3d)&&raw.OwnerId==ModelFeatureReorderState345.Model(state.Document.Database,tx),"OUT_OF_SCOPE","Extent metadata requires one exact current top-level native part.");
        var id=McObjectId.FromHandle(representation.Handle.Value,drawing.ID);var body=id.GetObjectOfType<Mc3dSolid>();
        ModelExtrusionExtentsState351.Need(body!=null&&body.ID==id&&ModelHistoryEditGuard.CurrentRepresentation(body,state,tx,cancel)==representation,"OBJECT_IDENTITY_MISMATCH","The selected body has no matching unique current native identity.");
        var contents=ModelInventoryBudget.History(body,cancel);var result=new List<ModelExtrusionExtentsRead351>();
        var readArgs=new ModifyArguments(args.BridgeId!,args.DocumentId!,args.ExpectedRevision!,"readonly_extent_metadata","Millimeters",[]);
        foreach(string featureId in featureIds)
        {
            cancel();var native=contents.SingleOrDefault(value=>value.ToString().Equals(featureId,StringComparison.OrdinalIgnoreCase));var feature=native.IsNull?null:native.GetObjectOfType<ExtrudeFeature>();
            if(feature==null||feature.ID!=native||feature.HostSolidID!=body.ID)
                throw new DomainException("OUT_OF_SCOPE","A selected extrusion is not this actual body's native history.",false,
                    ModelExtrusionScopeDiagnostics412.Describe(featureId,body,drawing.ID,representation,contents,state,tx,cancel));
            result.Add(Describe(ModelExtrusionExtentsState351.Capture(body,feature,readArgs,state,tx,cancel,includeStandalone:true)));
        }
        // CadReader measures the complete public response, not a count ceiling.
        return result.ToArray();
    }
}
