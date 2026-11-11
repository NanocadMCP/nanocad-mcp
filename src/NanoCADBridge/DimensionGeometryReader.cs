using NanoCAD.Contracts;
using Teigha.DatabaseServices;
using Teigha.Geometry;

namespace NanoCADBridge;

internal static class DimensionGeometryReader
{
    public static bool Supported(Type type) => type == typeof(AlignedDimension) || type == typeof(RotatedDimension) ||
        type == typeof(RadialDimension) || type == typeof(DiametricDimension) || type == typeof(LineAngularDimension2) ||
        type == typeof(Point3AngularDimension) || type == typeof(OrdinateDimension) || CadExtendedDimensions356.Supported(type);
    private static double Number(double value) => double.IsFinite(value) ? value : throw new DomainException("OBJECT_READ_FAILED", "Non-finite dimension geometry.");
    private static double[] Point(Point3d value) => new[] { Number(value.X), Number(value.Y), Number(value.Z) };
    public static object Read(Dimension dimension, EntitySummary summary, CancellationToken cancellation)
    {
        cancellation.ThrowIfCancellationRequested();
        var openedRuntime = dimension.GetType().FullName; using var nativeBinding = CadNativeDimensionBinding357.Bind(dimension); dimension = (Dimension)nativeBinding.Value;
        if (!Supported(dimension.GetType())) throw new DomainException("UNSUPPORTED_ENTITY_TYPE", "Unsupported native dimension subtype.", false, CadNativeDimensionBinding357.NativeDetails(dimension));
        // Stored geometry only: no dimension block regeneration, formatting,
        // style override mutation or associative dependency traversal.
        object definition = Definition(dimension);
        cancellation.ThrowIfCancellationRequested();
        var normal = dimension.Normal;
        var text = dimension.DimensionText;
        if (text.Length > 16384) throw new DomainException("ENTITY_TOO_LARGE", "Dimension text exceeds 16384 characters.");
        var style = dimension.DimensionStyle;
        if (style.IsNull || !style.IsValid || style.IsErased) throw new DomainException("OBJECT_READ_FAILED", "Dimension style reference is invalid.");
        var (measurement, unavailable) = DimensionMeasurementReader.Read(dimension, cancellation);
        var (format, formatReason) = CadDimensionFormatEditor.TryRead(dimension);
        var (display, displayReason) = CadDimensionEditing341.TryReadDisplay(dimension, cancellation.ThrowIfCancellationRequested);
        var association = CadDimensionAssociation385.TryRead(dimension, cancellation.ThrowIfCancellationRequested);
        var viewAssociation = CadProjectedDimensionAssociationReader.TryRead(dimension, summary, association, cancellation.ThrowIfCancellationRequested);
        var result = new
        {
            dimension_kind = dimension.GetType().Name,
            definition,
            measurement,
            measurement_unavailable_reason = unavailable,
            measurement_unit = dimension is LineAngularDimension2 or Point3AngularDimension ? "radians" : "drawing_units",
            normal = new[] { Number(normal.X), Number(normal.Y), Number(normal.Z) },
            elevation = Number(dimension.Elevation),
            dimension_style = new EntityRef(summary.@ref.bridge_instance_id, summary.@ref.document_id, Validation.NormalizeHandle(style.Handle.ToString())),
            dimension_text = text,
            text_position = Point(dimension.TextPosition),
            text_rotation = Number(dimension.TextRotation),
            format,
            format_unavailable_reason = formatReason,
            display,
            display_unavailable_reason = displayReason,
            association,
            view_association = viewAssociation,
            native_details = CadExtendedDimensions356.Details(dimension),
            native_binding = new
            {
                opened_runtime_type = openedRuntime,
                definition_runtime_type = dimension.GetType().FullName,
                borrowed_wrapper = nativeBinding.IsBorrowed,
                scope = nativeBinding.IsBorrowed ? "exact_native_RX_descriptor_binding; borrowed_wrapper_does_not_own_database_entity" : "ordinary_exact_managed_dimension_adapter; no_pointer_binding"
            }
        };
        cancellation.ThrowIfCancellationRequested();
        return result;
    }
    internal static object Definition(Dimension dimension)
    {
        using var nativeBinding = CadNativeDimensionBinding357.Bind(dimension); dimension = (Dimension)nativeBinding.Value;
        return dimension switch
        {
            AlignedDimension d => new { xline1_point = Point(d.XLine1Point), xline2_point = Point(d.XLine2Point), dimline_point = Point(d.DimLinePoint) },
            RotatedDimension d => new { xline1_point = Point(d.XLine1Point), xline2_point = Point(d.XLine2Point), dimline_point = Point(d.DimLinePoint), rotation = Number(d.Rotation) },
            RadialDimension d => new { center = Point(d.Center), chord_point = Point(d.ChordPoint), leader_length = Number(d.LeaderLength) },
            DiametricDimension d => new { chord_point = Point(d.ChordPoint), far_chord_point = Point(d.FarChordPoint), leader_length = Number(d.LeaderLength) },
            LineAngularDimension2 d => new { xline1_start = Point(d.XLine1Start), xline1_end = Point(d.XLine1End), xline2_start = Point(d.XLine2Start), xline2_end = Point(d.XLine2End), arc_point = Point(d.ArcPoint) },
            Point3AngularDimension d => new { center_point = Point(d.CenterPoint), xline1_point = Point(d.XLine1Point), xline2_point = Point(d.XLine2Point), arc_point = Point(d.ArcPoint) },
            OrdinateDimension d => new { origin = Point(d.Origin), defining_point = Point(d.DefiningPoint), leader_end_point = Point(d.LeaderEndPoint), using_x_axis = d.UsingXAxis },
            _ when CadExtendedDimensions356.Supported(dimension.GetType()) => CadExtendedDimensions356.Definition(dimension),
            _ => throw new DomainException("UNSUPPORTED_ENTITY_TYPE", "Unsupported native dimension subtype.", false, CadNativeDimensionBinding357.NativeDetails(dimension))
        };
    }
}
