using System.Text.Json;

namespace NanoCAD.Contracts;

public static class WriteSpace
{
    public static string Parse(JsonElement value)
    {
        if (!value.TryGetProperty("target_space", out var space)) return "model";
        if (space.ValueKind == JsonValueKind.String && space.GetString() is "model" or "paper") return space.GetString()!;
        throw new DomainException("INVALID_ARGUMENT", "target_space must be model or paper.");
    }
    public static void Check(CreateArguments args)
    {
        if (args.TargetSpace != "paper" && args.Entities.Any(item => item is ProjectedDiametricDimensionSpec or ProjectedRadialDimensionSpec or ProjectedArcLengthDimensionSpec or ProjectedAngularDimensionSpec))
            throw new DomainException("OUT_OF_SCOPE", "Projected circular dimensions require target_space paper.");
        if (args.TargetSpace != "paper") return;
        if (args.Entities.Any(e => e.Workplane != null || e is not (LineSpec or LineFromNativeSnapSpec or ResolvedNativeSnapLineSpec or CircleSpec or CircleFromNativeSnapSpec or ArcSpec or ArcFromNativeSnapSpec or PolylineSpec or SmoothPolylineSpec409 or EllipseSpec or SplineSpec or FitSplineSpec408 or PeriodicFitSplineSpec441 or CurveSplineSpec408 or WipeoutSpec431 or PointSpec408 or PointFromNativeSnapSpec or ConstructionCurve443 or TextSpec or MTextSpec or IDimensionCreation or ProjectedAlignedDimensionSpec or ProjectedDiametricDimensionSpec or ProjectedRadialDimensionSpec or ProjectedArcLengthDimensionSpec or ProjectedAngularDimensionSpec or ToleranceSpec356 or MLeaderSpec or MLeaderBlockSpec442 or TableSpec or HatchSpec or AssociativeHatchSpec)))
            throw new DomainException("OUT_OF_SCOPE", "Paper writes support sheet-plane 2D geometry and annotations, including native associative Hatch from source curves in the same active sheet; model features and solids require Model Space.");
        if (args.Entities.OfType<ConstructionCurve443>().Any(curve => curve.BasePoint.Z != 0 || curve.UnitDirection.Z != 0)) throw new DomainException("OUT_OF_SCOPE", "Paper Ray/Xline creation uses sheet-plane Z0 bases and directions.");
        if (args.Entities.OfType<PointSpec408>().Any(point => point.Position.Z != 0)) throw new DomainException("OUT_OF_SCOPE", "Paper Point creation uses sheet-plane Z0 positions.");
        if (args.Entities.OfType<ResolvedNativeSnapLineSpec>().Any(line => line.Start.Z != 0 || line.End.Z != 0)) throw new DomainException("OUT_OF_SCOPE", "Paper Line snaps use sheet-plane WCS Z0 endpoints.");
    }
    public static void Check(ModifyArguments args)
    {
        if (args.TargetSpace != "paper") return;
        if (args.Operations.OfType<PointPositionEdit408>().Any(point => point.Workplane != null || point.Position.Z != 0 || point.ExpectedPosition.Z != 0)) throw new DomainException("OUT_OF_SCOPE", "Paper Point coordinate edits use unframed sheet-plane WCS Z0 positions.");
        string[] allowed = { "set_dynamic_block_property", "set_text_format", "set_mleader_instance", "set_point_position", "set_legacy_polyline", "explode_native", "mirror", "move", "rotate", "scale", "copy", "erase", "set_properties", "set_polyline_vertices", "set_polyline_geometry", "set_polyline_corners", "reverse_polyline", "set_curve_geometry", "trim_curve", "extend_curve", "offset_curve", "replace_text", "replace_mtext", "replace_mleader_text", "set_mleader_vertices", "edit_mleader_topology", "set_wipeout_boundary", "replace_table_text", "resize_table", "replace_dimension_text", "set_dimension_line", "set_dimension_text_position", "set_dimension_format", "set_hatch_pattern", "set_hatch_properties", "boolean_region" };
        if (args.Operations.Any(o => !allowed.Contains(o.Operation) && !TableEditingValidation.Handles(o) && o is not SetDrawOrder407 and not CopyTransform2d407 and not PolarRepeat2d407 and not TableContentsEditing364 and not TableValueEditingSpec and not TableFormulaEditingSpec343 and not TableFormulaRangeSpec350 and not TableCellLayoutEdit340 and not TableGridLayoutEdit341 and not DimensionDisplayEdit341 and not DimensionDefinitionEdit341 and not NativeContourFinishingSpec342 and not NativeCornerCurveSpec343 and not NativeStretchEdit345 and not NativeWindowStretchEdit387 and not SetMTextFormatSpec and not SetHatchBoundarySpec349 and not SetHatchAssociation358 and not ToleranceEdit356)) throw new DomainException("OUT_OF_SCOPE", "This edit family requires Model Space; paper edits support ordinary placement, transformed copies/repetition, drawing order, properties and annotation changes.");
    }
}
