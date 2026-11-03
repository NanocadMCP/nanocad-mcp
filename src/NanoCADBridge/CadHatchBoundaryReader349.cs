using NanoCAD.Contracts;
using Teigha.DatabaseServices;

namespace NanoCADBridge;

internal static class CadHatchBoundaryReader349
{
    internal static HatchBoundaryRead349 Describe(Hatch hatch)=>new(!hatch.Associative&&hatch.HatchObjectType==HatchObjectType.HatchObject&&
        !hatch.HasFields&&hatch.Annotative!=AnnotativeStates.True,
        "candidate_only_existing_independent_native_hatch_polygon_or_circle_outer_and_holes",
        ["hatch_token","outer_loop","holes"],"existing_hatch_OCS_XY_millimeters_normal_and_elevation_retained");
}
