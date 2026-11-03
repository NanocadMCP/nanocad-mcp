using System.Text.Json.Nodes;

namespace NanoCAD.McpServer;

internal static class ExtendedCurveEditSchema
{
    internal static void Add(JsonObject geometry)
    {
        // Reuse the existing creation geometry contract; property/layer changes
        // are intentionally absent from direct geometry replacement.
        var create=JsonNode.Parse(CreateSchema.Input().GetRawText())!;
        var choices=create["properties"]!["entities"]!["items"]!["oneOf"]!.AsArray();
        foreach(string type in new[]{"Ellipse","Spline"})
        {
            // Geometry replacement still uses the complete control-point
            // contract. Top-level fitted/source-derived Spline creation has
            // the same native type but a different construction grammar.
            var shape=choices.Single(c=>c?["properties"]?["type"]?["const"]?.GetValue<string>()==type&&
                (type!="Spline"||c?["properties"]?["control_points"]!=null))!.DeepClone().AsObject();
            shape["properties"]!.AsObject().Remove("layer");
            var required=shape["required"]!.AsArray();
            if(type=="Spline"&&!required.Any(n=>n!.GetValue<string>()=="closed"))required.Add("closed");
            geometry["oneOf"]!.AsArray().Add(shape);
        }
    }
}
