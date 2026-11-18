using Multicad;
using Multicad.DatabaseServices.Constraints;
using Multicad.Mc3D;

namespace NanoCADBridge;

internal static class ProjectedFeatureSource
{
    internal sealed record Variable(string mc_id, string? name, string? expression);
    internal sealed record Parameter(string property, double value, string unit, Variable? variable);
    internal sealed record Feature(string mc_id, string? name, string runtime_type, string status, bool suppressed, Parameter[] parameters);
    internal sealed record Driver(string feature_mc_id, string property, double value, string unit, string evidence);

    internal static Driver? HoleDiameter(McObjectId creator, string? topologyId, string? kind, double diameter, double projected)
    {
        // IMc3dHole::getFaceId names the cylindrical surface. A silhouette or
        // matching circle diameter by itself cannot identify this feature property.
        var hole = creator.GetObjectOfType<HoleFeature>();
        if (hole == null || hole.ID != creator || hole.HoleType != HoleFeatureType.Simple || hole.SeatType != HoleSeatType.Flat ||
            hole.Status.ToString() != "Ok" || hole.IsSuppressed || kind != "face" ||
            hole.FaceId.IsNull || hole.FaceId.ToString() != topologyId || !double.IsFinite(hole.Diameter) ||
            Math.Abs(diameter - hole.Diameter) > 1e-7 || Math.Abs(projected - hole.Diameter) > 1e-7) return null;
        return new(creator.ToString(), "diameter", hole.Diameter, "drawing_units", "creating_hole_cylindrical_face_and_projected_diameter");
    }

    // GetFeatureObjectIdForFEV identifies a topology creator. The feature's
    // property getters and their variable getters identify separate links.
    internal static Feature? Read(McObjectId id, McObjectId body)
    {
        var feature = id.GetObjectOfType<HistoryItem>();
        if (feature == null || feature.ID != id || feature.HostSolidID != body) return null;
        var document = feature.DbEntity.Document;
        List<Parameter> parameters = [];
        void Add(string role, double value, string unit, McObjectId variable)
        {
            if (!double.IsFinite(value)) return;
            Variable? binding = null;
            if (document != null && !variable.IsNull)
            {
                try
                {
                    using var manager = document.GetDocumentVariablesManager(Constraint2dNetType.MCS);
                    if (manager?.DocumentId == document.ID && manager.ConstraintsNetworkType == Constraint2dNetType.MCS &&
                        manager.GetVariableHostObjectId(variable) == id && manager.GetVariableInfo(variable) is { } info)
                        binding = new(variable.ToString(), info.Name, info.Expression);
                }
                catch (Exception error) when (error is NotImplementedException or NotSupportedException or Teigha.Runtime.Exception) { }
            }
            parameters.Add(new(role, value, unit, binding));
        }
        const string length = "drawing_units";
        if (id.GetObjectOfType<HoleFeature>() is { } hole)
        {
            Add("diameter", hole.Diameter, length, McObjectId.Null);
            if (!hole.IsFullDepth) Add("depth", hole.Depth, length, McObjectId.Null);
        }
        else if (id.GetObjectOfType<ExtrudeFeature>() is { } extrusion)
        {
            if (extrusion.Extent == ExtrudeParamExtType.Dist)
                Add("distance", extrusion.Distance, length, extrusion.DistanceParamVarID);
            if (extrusion.Direction == FeatureExtentDirection.Different && extrusion.Extent2 == ExtrudeParamExtType.Dist)
                Add("distance2", extrusion.Distance2, length, extrusion.Distance2ParamVarID);
        }
        else if (id.GetObjectOfType<FilletFeature>() is { } fillet)
            Add("radius", fillet.Radius, length, fillet.RadiusParamVarID);
        else if (id.GetObjectOfType<ChamferFeature>() is { } chamfer)
            Add("distance", chamfer.Distance, length, chamfer.DistanceParamVarID);
        else if (id.GetObjectOfType<RevolveFeature>() is { IsFull: false } revolution)
            Add("angle", revolution.Angle, "radians", revolution.RotationAngleParamVarID);
        else if (id.GetObjectOfType<ThreadFeature>() is { } thread)
        {
            Add("pitch", thread.Pitch, length, thread.PitchVarID);
            if (!thread.IsFullLength) Add("length", thread.Length, length, thread.LengthVarID);
        }
        return new(id.ToString(), feature.Name, id.QueryObjectType()?.FullName ?? feature.GetType().FullName!,
            feature.Status.ToString(), feature.IsSuppressed, parameters.ToArray());
    }
}
