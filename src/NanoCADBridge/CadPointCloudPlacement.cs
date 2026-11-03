using NanoCAD.Contracts;
using Teigha.DatabaseServices;
using Teigha.Geometry;
using Cloud = HostMgd.PointClouds;

namespace NanoCADBridge;

// Rigid placement of the drawing insertion. The shared cloud definition/storage
// and vertex data are never opened for write, copied, reloaded or rewritten.
internal static class CadPointCloudPlacement
{
    internal static bool Placement(EditSpec edit) => edit is MoveSpec { Workplane: null } or RotateSpec { Workplane: null } or PointCloudPlacementSpec;

    internal static void CheckSource(Entity entity, Transaction tr, Action cancel)
    {
        cancel();
        using var insertion = Cloud.PointCloudBridgeMdg.asPointCloudInsertionByEntity(entity.UnmanagedObject, Cloud.OpenModeMdg.kForRead);
        if (insertion == null || insertion.ObjectId != entity.ObjectId)
            throw new DomainException("OBJECT_IDENTITY_MISMATCH", "Point cloud insertion wrapper identity mismatch.");
        var definition = insertion.PointCloudDefinitionID;
        if (definition.IsNull || !definition.IsValid || definition.IsErased || definition.IsEffectivelyErased || definition.Database != entity.Database)
            throw new DomainException("OBJECT_IDENTITY_MISMATCH", "Point cloud definition is unavailable in this drawing.");
        _ = tr.GetObject(definition, OpenMode.ForRead);
    }

    internal static Matrix3d Matrix(EditSpec edit) => edit is PointCloudPlacementSpec cloud
        ? CadSolidEditor.Matrix(cloud.Transform) : CadModifier.Matrix(edit);

    internal static void Apply(Entity entity, EditSpec edit, Transaction tr, Action cancel)
    {
        cancel();
        if (!entity.IsWriteEnabled)
            throw new DomainException("POINT_CLOUD_PLACEMENT_UNAVAILABLE", "Resident cloud insertion is not open for writing.");
        using var insertion = Cloud.PointCloudBridgeMdg.asPointCloudInsertionByEntity(entity.UnmanagedObject, Cloud.OpenModeMdg.kForRead);
        if (insertion == null || insertion.ObjectId != entity.ObjectId)
            throw new DomainException("OBJECT_IDENTITY_MISMATCH", "Point cloud insertion wrapper identity mismatch.");
        var definition = insertion.PointCloudDefinitionID;
        var owner = entity.OwnerId; var layer = entity.LayerId; var handle = entity.Handle;
        cancel();
        entity.TransformBy(Matrix(edit));
        if (insertion.ObjectId != entity.ObjectId || insertion.PointCloudDefinitionID != definition ||
            entity.OwnerId != owner || entity.LayerId != layer || entity.Handle != handle || !entity.IsWriteEnabled)
            throw new DomainException("POINT_CLOUD_PLACEMENT_MISMATCH", "Native placement changed insertion identity, ownership, layer or its cloud definition.");
        cancel();
    }
}
