using Multicad;
using Multicad.Mc3D;

namespace NanoCADBridge;

internal static class ModelEdgeFeatureHistory
{
    internal static object? Parameters(McObjectId id)
    {
        var fillet=id.GetObjectOfType<FilletFeature>();
        if(fillet!=null)return new{kind="fillet",radius=fillet.Radius,radius_variable_id=fillet.RadiusParamVarID.IsNull?null:fillet.RadiusParamVarID.ToString(),
            edge_ids=fillet.GetEdges()?.Select(e=>e.ToString()).ToArray()};
        var chamfer=id.GetObjectOfType<ChamferFeature>();
        if(chamfer!=null)return new{kind="chamfer",distance=chamfer.Distance,distance_variable_id=chamfer.DistanceParamVarID.IsNull?null:chamfer.DistanceParamVarID.ToString(),
            edge_ids=chamfer.GetEdges()?.Select(e=>e.ToString()).ToArray()};
        return null;
    }
}
