using System.Text.Json;
using NanoCAD.Contracts;
using Teigha.DatabaseServices;
using Teigha.Geometry;

namespace NanoCADBridge;

internal static class DimensionMeasurementReader
{
    // Measurement writes the SDK's internal cache and includes DIMLFAC for
    // linear dimensions. Isolate both effects in an owned, nonresident clone.
    // Never open the source for write or regenerate its dimension block.
    internal static (double? Value, string? Unavailable) Read(Dimension source, CancellationToken cancellation)
    {
        cancellation.ThrowIfCancellationRequested();
        using var nativeBinding = CadNativeDimensionBinding357.Bind(source); source = (Dimension)nativeBinding.Value;
        if (!DimensionGeometryReader.Supported(source.GetType()) || source.HasFields)
            return (null, "unsupported_dimension_content");
        // An ordinary DimAssoc is precisely what makes this dimension follow
        // its source; its presence must not hide the current measured result.
        // Admit only the observed owning association and organizational Groups.
        // Unknown external reactors still need their owning read adapter.
        using var reactors = source.GetPersistentReactorIds();
        if (reactors.Count != 0)
        {
            var transaction = source.Database?.TransactionManager.TopTransaction;
            if (transaction == null) return (null, "persistent_dependencies");
            var remaining = reactors.Cast<ObjectId>().Where(id => !CadDrawingGroups431.OrganizationalReactor(source, id, transaction, cancellation.ThrowIfCancellationRequested)).ToArray();
            if (remaining.Length != 0)
            {
                try
                {
                    var association = CadDimensionAssociation385.Capture(source, source.Database!, transaction, cancellation.ThrowIfCancellationRequested);
                    if (association.Association.IsNull || remaining.Any(id => id != association.Association)) return (null, "persistent_dependencies");
                }
                catch (OperationCanceledException) { throw; }
                catch (DomainException) { return (null, "unsupported_dimension_association"); }
                catch (Teigha.Runtime.Exception) { return (null, "unsupported_dimension_association"); }
            }
        }
        if (source.Normal != Vector3d.ZAxis || source.Elevation != 0) return (null, "unsupported_dimension_plane");
        var definition = Wire.Element(DimensionGeometryReader.Definition(source));
        foreach (var field in definition.EnumerateObject())
        {
            cancellation.ThrowIfCancellationRequested();
            if (field.Value.ValueKind == JsonValueKind.Array)
            {
                var values = field.Value.EnumerateArray().Select(v => v.GetDouble()).ToArray();
                if (values.Length != 3 || values[2] != 0 || values.Any(v => !double.IsFinite(v) || Math.Abs(v) > 1e9))
                    return (null, "unsupported_dimension_geometry");
            }
            else if (field.Value.ValueKind == JsonValueKind.Number &&
                (!field.Value.TryGetDouble(out double n) || !double.IsFinite(n) || Math.Abs(n) > 1e9))
                return (null, "unsupported_dimension_geometry");
        }
        return MeasureDetached(source, definition, cancellation.ThrowIfCancellationRequested);
    }

    // A caller which has already checked the exact resident projection anchors
    // can measure a detached clone without applying the ordinary source adapter.
    internal static (double? Value, string? Unavailable) MeasureProjectedSheet(Dimension source, Action check)
    {
        check();
        // MultiCAD projected dimensions can retain their physical nominal in the
        // DWG measurement cache even on a clone with DIMLFAC reset to one.
        // The stored chord and extension points are in the owning sheet's units.
        double? sheetLength = source switch
        {
            DiametricDimension d => d.ChordPoint.DistanceTo(d.FarChordPoint),
            RadialDimension d => d.Center.DistanceTo(d.ChordPoint),
            AlignedDimension d => d.XLine1Point.DistanceTo(d.XLine2Point),
            RotatedDimension d when d.Normal == Vector3d.ZAxis => Math.Abs(
                (d.XLine2Point.X - d.XLine1Point.X) * Math.Cos(d.Rotation) +
                (d.XLine2Point.Y - d.XLine1Point.Y) * Math.Sin(d.Rotation)),
            _ => null
        };
        if (sheetLength is { } length)
            return double.IsFinite(length) ? (length, null) : (null, "nonfinite_sheet_geometry");
        // This fallback may read a cached physical nominal. It cannot supply the sheet length for an oblique OCS.
        if (source is RotatedDimension) return (null, "tilted_rotated_dimension_sheet_geometry_unavailable");
        return MeasureDetached(source, Wire.Element(DimensionGeometryReader.Definition(source)), check);
    }

    internal static (double? Value, string? Unavailable) MeasureDetached(Dimension source, JsonElement definition, Action check)
    {
        check();
        using var owned = CadNativeDimensionBinding357.Clone(source);
        using var cloneBinding = CadNativeDimensionBinding357.Bind(owned);
        if (cloneBinding.Value is not Dimension clone || clone.GetType() != source.GetType() || !clone.ObjectId.IsNull ||
            !clone.IsNewObject || !clone.IsWriteEnabled)
            throw new DomainException("OBJECT_READ_FAILED", "Expected an owned detached native dimension clone.");
        // Clear only the independent owned clone's copied reactor links before
        // its cache/Dimlfac setters run. Never update/remove a source association
        // or a Group, nor mutate the resident dimension while measuring it.
        ObjectId[] cloneReactors; using (var links = clone.GetPersistentReactorIds()) cloneReactors = links.Cast<ObjectId>().ToArray();
        foreach (var id in cloneReactors) { check(); clone.RemovePersistentReactor(id); }
        check();
        clone.Dimlfac = 1;
        if (clone.Dimlfac != 1) throw new DomainException("OBJECT_READ_FAILED", "Unable to normalize the temporary dimension measurement factor.");
        double value;
        try { value = clone.Measurement; }
        catch (Teigha.Runtime.Exception error) when (error.ErrorStatus.ToString() == "NotOpenForWrite")
        { return (null, "requires_write_access"); }
        check();
        if (!double.IsFinite(value) || !DimensionTextGeometry.SameDefinition(definition, Wire.Element(DimensionGeometryReader.Definition(clone))))
            throw new DomainException("OBJECT_READ_FAILED", "Native dimension measurement is nonfinite or changed its temporary definition.");
        return (value, null);
    }
}
