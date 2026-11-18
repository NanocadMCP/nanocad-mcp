using Teigha.DatabaseServices;
using Teigha.Geometry;

namespace NanoCADBridge;

internal static class CadProjectedLineSubentity
{
    // A MultiCAD drawing-view graphics marker and an OdDb subentity index
    // belong to different APIs. Resolve and reverse-check the real host path.
    internal static object Observe(DocumentState state, Entity view, int marker, Point3d a, Point3d b)
    {
        string stage = "active_view";
        object Unavailable(string reason) => new { status = "unavailable", stage, reason };
        try
        {
            if (view.OwnerId != state.Document.Database.CurrentSpaceId) return Unavailable("view_is_not_in_active_space");
            using var camera = state.Document.Editor.GetCurrentView();
            if (camera.PerspectiveEnabled) return Unavailable("perspective_view");
            // nanoCAD 26 SDK PointCloudMgd.cs:187-189.
            var transform = Matrix3d.WorldToPlane(camera.ViewDirection) *
                Matrix3d.Displacement(Point3d.Origin - camera.Target) *
                Matrix3d.Rotation(camera.ViewTwist, camera.ViewDirection, camera.Target);
            stage = "subentity_paths_at_graphics_marker";
            // The selected entity is top-level. The managed hostdbmgd wrapper
            // appends every supplied ObjectId to the native insert stack.
            // This view is the receiver, with no containing insert.
            var paths = view.GetSubentityPathsAtGraphicsMarker(SubentityType.Edge, new IntPtr(marker),
                a + (b - a) * .5, transform, []);
            if (paths == null || paths.Length != 1) return Unavailable("edge_path_count_" + (paths?.Length ?? 0));
            var path = paths[0];
            if (path.IsNull || path.SubentId.Type != SubentityType.Edge ||
                path.GetObjectIds() is not [var owner] || owner != view.ObjectId)
                return Unavailable("edge_path_does_not_identify_current_view");
            stage = "graphics_markers_at_subentity_path";
            var reverse = view.GetGraphicsMarkersAtSubentityPathIntPtr(path);
            if (reverse == null || reverse.Count != 1 || reverse[0].ToInt64() != marker)
                return Unavailable("reverse_mapping_does_not_identify_selected_marker");
            stage = "subentity_line";
            var subentity = view.GetSubentity(path);
            if (subentity == null) return Unavailable("missing_subentity_geometry");
            if (!subentity.ObjectId.IsNull || !subentity.AutoDelete)
            {
                GC.SuppressFinalize(subentity);
                return Unavailable("subentity_is_not_an_owned_detached_curve");
            }
            using (subentity)
            {
                if (subentity is not Line line) return Unavailable("subentity_is_not_a_line");
                bool Near(Point3d p, Point3d q) => p.DistanceTo(q) <= 1e-7;
                if (!(Near(a, line.StartPoint) && Near(b, line.EndPoint) || Near(b, line.StartPoint) && Near(a, line.EndPoint)))
                    return Unavailable("native_subentity_endpoints_differ_from_projection");
                stage = "osnap_point_reference";
                bool Resolves(ObjectSnapModes mode, Point3d endpoint)
                {
                    using var reference = new OsnapPointRef();
                    reference.IdPath = path;
                    reference.OsnapType = mode;
                    reference.Point = endpoint;
                    reference.NearPointParam = line.GetParameterAtPoint(endpoint);
                    return Near(reference.EvalPoint(), endpoint);
                }
                if (!Resolves(ObjectSnapModes.ModeStartpoint, line.StartPoint) || !Resolves(ObjectSnapModes.ModeEnd, line.EndPoint))
                    return Unavailable("endpoint_reference_does_not_evaluate_on_selected_view");
                return new
                {
                    status = "observed",
                    view_handle = view.Handle.ToString(),
                    graphics_marker = marker,
                    subentity_type = "edge",
                    subentity_index = path.SubentId.IndexPtr.ToInt64().ToString(System.Globalization.CultureInfo.InvariantCulture),
                    start_point = new[] { line.StartPoint.X, line.StartPoint.Y, line.StartPoint.Z },
                    end_point = new[] { line.EndPoint.X, line.EndPoint.Y, line.EndPoint.Z }
                };
            }
        }
        catch (Exception ex) when (ex is Teigha.Runtime.Exception or InvalidOperationException or ArgumentException or NotSupportedException or NotImplementedException)
        {
            return Unavailable(ex is Teigha.Runtime.Exception native ? native.ErrorStatus.ToString() : ex.GetType().Name);
        }
    }
}
