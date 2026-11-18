using System.Text.Json.Nodes;
using NanoCAD.Contracts;

namespace NanoCAD.McpServer;

internal static class SolidSectionSchema
{
    internal static JsonObject Input(bool intervals)
    {
        var result = JsonNode.Parse("""
            {"type":"object","additionalProperties":false,"required":["bridge_instance_id","document_id","expected_revision","mode","handles","workplane"],
             "properties":{"bridge_instance_id":{"type":"string","format":"uuid"},"document_id":{"type":"string","format":"uuid"},
               "expected_revision":{"type":"string","pattern":"^[0-9]{1,20}$"},"handles":{"type":"array","minItems":1,"maxItems":1,"items":{"type":"string","pattern":"^[0-9A-Fa-f]{1,16}$"}}}}
            """)!.AsObject();
        var props = result["properties"]!;
        props["mode"] = new JsonObject { ["const"] = intervals ? SolidSectionAnalysis.Intervals : SolidSectionAnalysis.Section };
        props["workplane"] = WorkplaneSchema.Input();
        var pair = JsonNode.Parse("""{"type":"array","minItems":2,"maxItems":2,"items":{"type":"array","minItems":2,"maxItems":2,"items":{"type":"number"}}}""")!;
        pair["description"] = intervals ? "Ordered finite segment endpoints in local coordinates of the section workplane, in drawing units."
            : "Optional local XY minimum and maximum corners. The returned section is the actual planar area clipped to this rectangle; its boundary can include window edges.";
        props[intervals ? "segment" : "window"] = pair;
        if (intervals) result["required"]!.AsArray().Add("segment");
        return result;
    }

    internal static JsonObject Output(bool intervals)
    {
        var result = JsonNode.Parse("""
            {"type":"object","additionalProperties":false,"required":["mode","body","units","solver","workplane","status","area","perimeter","faces","window","measurement"],
              "properties":{"body":{"$ref":"#/$defs/ref"},"units":{"type":"object"},"solver":{"type":"string"},"workplane":{"type":"object"},
              "status":{"type":"string"},"area":{"type":["number","null"]},"perimeter":{"type":["number","null"]},"faces":{"type":["array","null"],"items":{"type":"object"}},
              "window":{"type":["array","null"]},"measurement":{"type":["object","null"]}}}
            """)!.AsObject();
        result["properties"]!["mode"] = new JsonObject { ["const"] = intervals ? SolidSectionAnalysis.Intervals : SolidSectionAnalysis.Section };
        return result;
    }
}
