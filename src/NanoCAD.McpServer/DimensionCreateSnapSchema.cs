using System.Text.Json.Nodes;
using NanoCAD.Contracts;

namespace NanoCAD.McpServer;

internal static class DimensionCreateSnapSchema
{
    internal static void Apply(JsonArray entities)
    {
        foreach (var entity in entities.OfType<JsonObject>())
        {
            var properties = entity["properties"]!.AsObject();
            if (properties["type"]?["const"]?.GetValue<string>() != "Dimension" ||
                properties["kind"]?["const"]?.GetValue<string>() is not { } kind ||
                !DimensionCreateSnaps.Supports(kind)) continue;
            foreach (var role in DimensionCreateSnaps.Roles(kind))
                NativeCurveSnapSchema.WithPointAlternative(entity, DimensionCreateSnaps.Field(role));
            entity["description"] = (entity["description"]?.GetValue<string>() ?? "") +
                " Each coordinate can instead use its matching _snap field. Supply exactly one literal or snap per point. " +
                "Snaps resolve existing geometry once in the requested active space, require WCS XY/Z0, and do not add persistent associations. " +
                "All ordinary dimension geometry and explicit associations are checked after resolution.";
        }
    }
}
