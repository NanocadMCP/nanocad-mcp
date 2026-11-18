using System.Text.Json;
using Multicad;
using NanoCAD.Contracts;
using Teigha.DatabaseServices;

namespace NanoCADBridge;

internal static class CadViewMarkerDimensions
{
    internal static object Read(List<ObjectId> dimensions, MarkerDimensionPage page, Entity view, McObjectId viewId,
        int marker, DocumentState state, Transaction tx, string instance, Action cancel, int room)
    {
        if (page.StartIndex > dimensions.Count) throw new DomainException("INVALID_ARGUMENT", "The dimension page starts beyond the selected layout inventory.");
        var items = new List<object>();
        var unclassified = new List<object>();
        var table = (BlockTable)tx.GetObject(view.Database.BlockTableId, OpenMode.ForRead);
        var model = table[BlockTableRecord.ModelSpace];
        var viewRef = new EntityRef(instance, state.Id, Validation.NormalizeHandle(view.Handle.ToString()));
        string nativeView = viewId.ToString();
        int next = page.StartIndex, bytes = 1024;
        int end = next + Math.Min(page.PageSize, dimensions.Count - next);
        for (; next < end; next++)
        {
            cancel();
            var dim = (Dimension)tx.GetObject(dimensions[next], OpenMode.ForRead);
            var summary = EntityAdapter.Summary(dim, model, instance, state.Id, dim.OwnerId != model);
            var result = CadProjectedDimensionAssociationReader.ReadLoaded(dim, summary, cancel, includeDesignSource: false);
            var stored = result == null ? default : Wire.Element(result);
            object row;
            List<object> target;
            if (result == null || !stored.GetProperty("links_complete").GetBoolean())
            {
                row = new
                {
                    dimension_ref = summary.@ref,
                    reason = result == null ? "no_verified_local_view_association" :
                    stored.GetProperty("unavailable_reason").GetString()
                };
                target = unclassified;
            }
            else
            {
                bool sameView = stored.GetProperty("links").EnumerateArray().Any(link =>
                    string.Equals(link.GetProperty("view_mc_id").GetString(), nativeView, StringComparison.OrdinalIgnoreCase) &&
                    link.GetProperty("view_ref").GetProperty("handle").GetString() == viewRef.handle);
                if (!sameView) continue;
                var slots = stored.GetProperty("anchors").EnumerateArray().Where(anchor =>
                    !anchor.GetProperty("is_null").GetBoolean() &&
                    string.Equals(anchor.GetProperty("source_mc_id").GetString(), nativeView, StringComparison.OrdinalIgnoreCase) &&
                    anchor.GetProperty("gs_marker").GetInt32() == marker).Select(anchor => anchor.GetProperty("slot").GetInt32()).ToArray();
                if (slots.Length == 0) continue;
                row = new { dimension_ref = summary.@ref, dimension_type = dim.GetType().Name, anchor_slots = slots };
                target = items;
            }
            int size = JsonSerializer.SerializeToUtf8Bytes(row, Wire.Json).Length + 1;
            if ((long)bytes + size > room)
            {
                if (next == page.StartIndex) throw new DomainException("RESPONSE_LIMIT_EXCEEDED", "A marker dimension row exceeds the available serialized response budget.");
                break;
            }
            bytes += size;
            target.Add(row);
        }
        return new
        {
            view_ref = viewRef,
            marker,
            scope = "local_stored_view_anchor_slots",
            start_index = page.StartIndex,
            total_dimensions = dimensions.Count,
            examined_count = next - page.StartIndex,
            next_index = next < dimensions.Count ? (int?)next : null,
            page_complete = next == dimensions.Count,
            items,
            unclassified
        };
    }
}
