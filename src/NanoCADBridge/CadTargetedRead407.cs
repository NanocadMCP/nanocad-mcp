using NanoCAD.Contracts;
using Teigha.DatabaseServices;
using Teigha.Geometry;

namespace NanoCADBridge;

internal static class CadTargetedRead407
{
    internal static bool MatchBlock(Entity entity, QueryBlock407? filter, Transaction transaction, Action cancel)
    {
        if (filter == null) return true;
        if (entity is not BlockReference block) return false;
        cancel();
        // The named dynamic definition is the canonical definition; an anonymous
        // evaluated block is not silently treated as a separate named symbol.
        var definitionId = block.IsDynamicBlock ? block.DynamicBlockTableRecord : block.BlockTableRecord;
        if (definitionId.IsNull || !definitionId.IsValid || definitionId.IsErased || definitionId.IsEffectivelyErased)
            throw new DomainException("OBJECT_READ_FAILED", "Block definition is unavailable for the requested filter.");
        if (filter.Handle != null && !StringComparer.Ordinal.Equals(filter.Handle, Validation.NormalizeHandle(definitionId.Handle.ToString()))) return false;
        if (filter.Name == null) return true;
        cancel();
        var definition = (BlockTableRecord)transaction.GetObject(definitionId, OpenMode.ForRead);
        return string.Equals(definition.Name, filter.Name, filter.CaseSensitive ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase);
    }
    internal static bool MatchText(Entity entity, QueryText407? filter, Action cancel)
    {
        if (filter == null) return true;
        cancel(); string? text;
        if (entity.HasFields) return false;
        if (entity is DBText ordinary) text = ordinary.TextString;
        else if (entity is MText rich) text = MTextContent.PlainText(rich.Contents);
        else if (entity is Dimension dimension) text = dimension.DimensionText;
        else if (entity is MLeader leader && leader.ContentType == ContentType.MTextContent)
        {
            var content = leader.MText;
            try
            {
                // A resident alias belongs to the transaction. Do not evaluate,
                // search or dispose it as an independent content observation.
                if (content == null || !content.ObjectId.IsNull || content.HasFields) return false;
                text = MTextContent.PlainText(content.Contents);
            }
            finally { if (content != null && content.ObjectId.IsNull) content.Dispose(); }
        }
        else return false;
        // No literal text is inferred from a field, dimension measurement, Table
        // cell or unsupported MTEXT escape sequence. Such content is not a match.
        if (text == null) return false;
        var comparison = filter.CaseSensitive ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;
        return filter.Match == "equals" ? string.Equals(text, filter.Value, comparison) : text.Contains(filter.Value, comparison);
    }
    internal static bool MatchBounds(Entity entity, QueryBounds407? filter, Action cancel, out bool unavailable)
    {
        unavailable = false;
        if (filter == null) return true;
        cancel();
        // Ray/Xline do not have finite entity extents. Never interpret a native
        // sentinel extent as an actual finite bounding rectangle.
        if (entity is Ray or Xline) { unavailable = true; return false; }
        try
        {
            var bounds = entity.GeometricExtents;
            var min = bounds.MinPoint; var max = bounds.MaxPoint;
            if (!double.IsFinite(min.X) || !double.IsFinite(min.Y) || !double.IsFinite(max.X) || !double.IsFinite(max.Y) || min.X > max.X || min.Y > max.Y)
            { unavailable = true; return false; }
            double minX=min.X,minY=min.Y,maxX=max.X,maxY=max.Y;
            if(entity.GetType()==typeof(MInsertBlock)&&entity is MInsertBlock array)
            {
                if(!CadMInsertBounds407.TryExpand(array,min,max,cancel,out var candidate)) { unavailable=true;return false; }
                minX=candidate.MinX;minY=candidate.MinY;maxX=candidate.MaxX;maxY=candidate.MaxY;
            }
            return filter.Mode == "within"
                ? minX >= filter.Min[0] && minY >= filter.Min[1] && maxX <= filter.Max[0] && maxY <= filter.Max[1]
                : minX <= filter.Max[0] && minY <= filter.Max[1] && maxX >= filter.Min[0] && maxY >= filter.Min[1];
        }
        catch (Teigha.Runtime.Exception error) when (!error.ErrorStatus.ToString().Contains("Lock", StringComparison.OrdinalIgnoreCase))
        { cancel(); unavailable = true; return false; }
    }
    internal static object Semantics(Arguments args, int unavailableBounds) => new
    {
        filters_combined = "and", space = args.ReadSpace,
        spatial = args.QueryBounds == null ? null : new
        {
            coordinate_system = "WCS_XY", predicate = "candidate_envelope_" + args.QueryBounds.Mode,
            exact_geometry_intersection = false, unavailable_bounds_policy = "excluded",
            unavailable_bounds_count_on_page = unavailableBounds,
            ordinary_bounds_kind = "reported_native_extents",
            bounds_kind = "entity_specific_candidate_envelope",
            minsert_bounds_kind = CadMInsertBounds407.Kind,
            minsert_bounds_scope = CadMInsertBounds407.Scope,
            minsert_within_semantics = "sufficient_conservative_envelope_containment_may_exclude_other_geometrically_inside_arrays"
        },
        text = args.QueryText == null ? null : new
        {
            scope = "literal_TEXT_plain_MTEXT_MLeader_or_dimension_override",
            unsupported_plain_text_policy = "excluded", match = args.QueryText.Match, case_sensitive = args.QueryText.CaseSensitive,
            fields_evaluated = false, table_cells_searched = false, nested_block_attributes_searched = false
        },
        block_definition = args.QueryBlock == null ? null : new
        { scope = "canonical_named_definition_for_dynamic_insertions_else_direct_definition", nested_members_searched = false }
    };
    internal static Dictionary<string, object?> Compact(Entity entity, EntitySummary summary, string[] sections, CancellationToken cancel)
    {
        var result = new Dictionary<string, object?>
        {
            ["ref"] = summary.@ref, ["type"] = summary.type, ["runtime_type"] = summary.runtime_type,
            ["layer"] = summary.layer, ["space"] = summary.space, ["detail_support"] = summary.detail_support,
            ["coordinate_system"] = "WCS", ["owner_ref"] = new EntityRef(summary.@ref.bridge_instance_id, summary.@ref.document_id,
                Validation.NormalizeHandle(entity.OwnerId.Handle.ToString()))
        };
        bool editing = sections.Contains("editing", StringComparer.Ordinal);
        bool geometry = editing || sections.Contains("geometry", StringComparer.Ordinal);
        bool appearance = sections.Contains("appearance", StringComparer.Ordinal);
        // Identity-only and appearance-only reads avoid expensive geometry arrays,
        // model history, Table fields and native editor-token observations entirely.
        if (geometry)
        {
            if (!editing && PlainGeometry(entity, cancel) is { } plain) result.Add("geometry", plain);
            else
            {
                var detail = EntityAdapter.Detail(entity, summary, cancel, includeNativeCurve: editing, includeAppearance: false);
                result.Add("geometry", detail.geometry);
                if (detail.warnings.Length != 0) result.Add("warnings", detail.warnings);
            }
        }
        if (appearance) result.Add("properties", EntityAdapter.StoredProperties(entity));
        return result;
    }
    private static object? PlainGeometry(Entity entity, CancellationToken cancel)
    {
        static double Number(double value) => double.IsFinite(value) ? value : throw new DomainException("OBJECT_READ_FAILED", "Stored geometry contains a nonfinite reported value.");
        static double[] Point(Point3d value) => [Number(value.X), Number(value.Y), Number(value.Z)];
        static double[] Vector(Vector3d value) => [Number(value.X), Number(value.Y), Number(value.Z)];
        cancel.ThrowIfCancellationRequested();
        // Ordinary placement/content requires neither definition traversal nor
        // formatting token snapshots. Ask for editing when those are needed.
        if (entity.GetType() == typeof(MInsertBlock) && entity is MInsertBlock array)
        {
            object? candidateBounds=null;string? boundsReason=null;
            try
            {
                var extents=CadMInsertBounds407.Read(array,cancel.ThrowIfCancellationRequested);
                candidateBounds=new{min=Point(extents.MinPoint),max=Point(extents.MaxPoint)};
            }
            catch(Teigha.Runtime.Exception error)when(!error.ErrorStatus.ToString().Contains("Lock",StringComparison.OrdinalIgnoreCase))
            {boundsReason="NATIVE_ARRAY_BOUNDS_UNAVAILABLE: "+error.ErrorStatus;}
            catch(DomainException error){boundsReason=error.Error.code+": "+error.Message;}
            return new
            {
                definition_handle = Validation.NormalizeHandle(array.BlockTableRecord.Handle.ToString()),
                position = Point(array.Position), normal = Vector(array.Normal), rotation = Number(array.Rotation),
                scale_factors = new[] { Number(array.ScaleFactors.X), Number(array.ScaleFactors.Y), Number(array.ScaleFactors.Z) },
                rows = unchecked((ushort)array.Rows), columns = unchecked((ushort)array.Columns), row_spacing = Number(array.RowSpacing), column_spacing = Number(array.ColumnSpacing),
                instance_count = (long)unchecked((ushort)array.Rows) * unchecked((ushort)array.Columns),
                bounds=candidateBounds,bounds_kind=candidateBounds==null?null:CadMInsertBounds407.Kind,bounds_unavailable_reason=boundsReason
            };
        }
        if (entity.GetType() == typeof(DBText) && entity is DBText text)
            return new { text = text.TextString, position = Point(text.Position), alignment_point = Point(text.AlignmentPoint),
                height = Number(text.Height), width_factor = Number(text.WidthFactor), rotation = Number(text.Rotation), normal = Vector(text.Normal),
                horizontal_mode = text.HorizontalMode.ToString(), vertical_mode = text.VerticalMode.ToString(), text_style = text.TextStyleName };
        if (entity.GetType() == typeof(MText) && entity is MText rich)
            return new { contents = rich.Contents, plain_text = MTextContent.PlainText(rich.Contents), location = Point(rich.Location),
                normal = Vector(rich.Normal), direction = Vector(rich.Direction), text_height = Number(rich.TextHeight), width = Number(rich.Width),
                attachment = rich.Attachment.ToString(), text_style = rich.TextStyleName };
        return null;
    }
}
