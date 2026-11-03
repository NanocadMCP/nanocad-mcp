using Multicad.Mc3D;

namespace NanoCADBridge;

// Owned managed complex-ID wrappers. Report their actual target IDs without
// claiming a whole complex path, face ownership, or an editable surface route.
internal static class ModelRevolutionTerminationReader377
{
    internal static object Read(RevolveFeature feature)
    {
        using var first=feature.FaceToID1;using var second=feature.FaceToID2;
        return new{first_entity_id=first==null||first.IsNull?null:first.TargetId.ToString(),
            second_entity_id=second==null||second.IsNull?null:second.TargetId.ToString(),
            scope="actual_native_complex_ID_targets; full_path_and_target_owner_unverified; active_mode_is_reported_separately"};
    }
}
