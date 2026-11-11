using System.Text.Json;
using System.Text.Json.Nodes;
using NanoCAD.Contracts;
using Teigha.DatabaseServices;

namespace NanoCADBridge;

// Optional top-level observation adapters. EntityAdapter.Detail stays pure so
// internal canonical-definition snapshots never recursively request inventories.
internal static class CadDynamicBlockObservation
{
    sealed record Observation(bool Applies, DynamicBlockPropertiesInventory? Inventory, string? Reason);
    static bool MustPropagate(string code) => code is "CANCELLED" or "STALE_DOCUMENT" or
        "STALE_SELECTION" or "DOCUMENT_CLOSED" or "DOCUMENT_NOT_ACTIVE" or "HOST_BUSY" or "NO_DOCUMENT" or "UI_THREAD_MISMATCH";
    static string Reason(DomainException error)
    {
        string reason = error.Error.code + ": " + error.Error.message;
        if (error.Error.details == null) return reason;
        var details = Wire.Element(error.Error.details); if (details.ValueKind != JsonValueKind.Object) return reason;
        var fields = new List<string>();
        foreach (string field in new[] { "runtime_type", "type_code", "value_path" })
            if (details.TryGetProperty(field, out var value)) fields.Add(field + "=" + value.GetRawText());
        return fields.Count == 0 ? reason : reason + " [" + string.Join(", ", fields) + "]";
    }
    static Observation Observe(Entity entity, Transaction tr, string bridge, string document, Action cancel)
    {
        cancel();
        if (entity.GetType() != typeof(BlockReference)) return new(false, null, null);
        try
        {
            var reference = (BlockReference)entity;
            if (!reference.IsDynamicBlock) return new(false, null, null);
            var inventory = CadDynamicBlockProperties.Read(reference, tr, bridge, document, cancel);
            return new(true, inventory, inventory.complete ? null :
                "INCOMPLETE_DYNAMIC_BLOCK_PROPERTIES: The complete native property inventory is unavailable; the returned property rows retain their exact unavailable reasons.");
        }
        catch (DomainException error) when (!MustPropagate(error.Error.code))
        { return new(true, null, Reason(error)); }
        catch (Teigha.Runtime.Exception error) when (!error.ErrorStatus.ToString().Contains("Lock", StringComparison.OrdinalIgnoreCase))
        { return new(true, null, "NATIVE_DYNAMIC_BLOCK_PROPERTIES_UNAVAILABLE: " + error.ErrorStatus); }
        catch (NotSupportedException error)
        { return new(true, null, "NATIVE_DYNAMIC_BLOCK_PROPERTIES_UNAVAILABLE: " + error.Message); }
        catch (JsonException error)
        { return new(true, null, "DYNAMIC_BLOCK_PROPERTIES_SERIALIZATION_UNAVAILABLE: " + error.Message); }
    }
    static object Geometry(object geometry, Observation observation)
    {
        var merged = JsonNode.Parse(Wire.Element(geometry).GetRawText())!.AsObject();
        merged["dynamic_properties"] = observation.Inventory == null ? null : JsonNode.Parse(Wire.Element(observation.Inventory).GetRawText());
        merged["dynamic_properties_unavailable_reason"] = observation.Reason;
        return Wire.Element(merged);
    }
    static string[] Warnings(IEnumerable<string> before, Observation observation) => observation.Reason == null
        ? before.ToArray() : before.Append("DYNAMIC_BLOCK_PROPERTIES_UNAVAILABLE: " + observation.Reason).ToArray();
    static bool Fits(object merged, Action cancel)
    {
        try { SerializedCapacity357.Check(merged, Wire.PayloadBudget - 8192, cancel); return true; }
        catch (DomainException error) when (error.Error.code == "RESPONSE_LIMIT_EXCEEDED") { return false; }
    }
    static Observation TooLarge() => new(true, null,
        "DYNAMIC_BLOCK_PROPERTIES_RESPONSE_LIMIT_EXCEEDED: The complete optional inventory does not fit this object's response-byte budget; base geometry is retained and no property rows are clipped.");
    static EntityDetail With(EntityDetail detail, Observation observation) => detail with
    { geometry = Geometry(detail.geometry, observation), warnings = Warnings(detail.warnings, observation) };
    internal static EntityDetail Detail(EntityDetail detail, Entity entity, Transaction tr, string bridge, string document, Action cancel)
    {
        var observation = Observe(entity, tr, bridge, document, cancel);
        if (!observation.Applies) return detail;
        var merged = With(detail, observation);
        return Fits(merged, cancel) ? merged : With(detail, TooLarge());
    }
    internal static void Compact(Dictionary<string, object?> detail, Entity entity, Transaction tr, string bridge, string document, Action cancel)
    {
        if (!detail.TryGetValue("geometry", out var geometry) || geometry == null) return;
        var observation = Observe(entity, tr, bridge, document, cancel); if (!observation.Applies) return;
        var merged = new Dictionary<string, object?>(detail) { ["geometry"] = Geometry(geometry, observation) };
        if (observation.Reason != null)
        {
            var warnings = detail.TryGetValue("warnings", out var existing) && existing is IEnumerable<string> strings ? strings : [];
            merged["warnings"] = Warnings(warnings, observation);
        }
        if (!Fits(merged, cancel))
        {
            observation = TooLarge(); merged["geometry"] = Geometry(geometry, observation);
            var warnings = detail.TryGetValue("warnings", out var existing) && existing is IEnumerable<string> strings ? strings : [];
            merged["warnings"] = Warnings(warnings, observation);
        }
        foreach (var field in merged) detail[field.Key] = field.Value;
    }
    internal static BlockInspection Block(BlockInspection inspection, BlockReference? root, Transaction tr, string bridge, string document, Action cancel)
    {
        if (root == null) return inspection;
        var observation = Observe(root, tr, bridge, document, cancel);
        BlockInspection WithObservation(Observation observed) => inspection with
        {
            dynamic_properties = observed.Inventory,
            dynamic_properties_unavailable_reason = observed.Reason,
            complete = inspection.complete && observed.Inventory?.complete == true,
            warnings = Warnings(inspection.warnings, observed)
        };
        if (!observation.Applies) return inspection;
        var merged = WithObservation(observation);
        return Fits(merged, cancel) ? merged : WithObservation(TooLarge());
    }
}
