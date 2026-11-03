using Multicad.Mc3D;
using NanoCAD.Contracts;

namespace NanoCADBridge;

// Additive route guidance from one already-resolved extrusion. No document
// inventory, profile factory, constraint manager or solver belongs in this read.
internal static class ModelProfileRebindReader349
{
    internal static ModelProfileRebindRead349 Describe(ExtrudeFeature feature)=>new(
        !feature.ProfileID.IsNull&&feature.Status.ToString()=="Ok"&&!feature.IsSuppressed&&!feature.IsBeyondEndOfPart,
        ModelProfileDiscovery348.Id(feature.ProfileID),["feature_id","expected_current_profile_id","expected_last_item_id","expected_sketch_id","expected_profile_token"],
        "candidate_route_not_preflight; choose profile_id with replacement parent/token OR replacement_regions WCS seeds with CURRENT profile parent/token to create one separate selected profile on its acquired sketch; separated/nested simple Circle/Line/Arc loops, holes and multiple areas, no open/branched/intersecting/touching loops or adjacent selected cells; replacement parent already in actual selected-part composition or actual same-part consumed profile; unused outside document construction excluded; same-part shared forward consumers and observed constraints retained; same oriented frame/support; acyclic native downstream preservation; previous profile protected, reuse unpromised");
}
