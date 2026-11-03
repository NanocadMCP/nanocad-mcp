using System.Text.Json.Nodes;
namespace NanoCAD.McpServer;
internal static class PlaneCurveTransformSchema394
{
    internal const string Scope = "Optional workplane on move/copy/rotate/scale/mirror places exact independent native Line/Circle/Arc/Ellipse/Spline/Polyline in an explicit orthonormal WCS plane. Displacement, center and mirror axis_start/axis_end are local XY pairs in that frame; positive rotation follows its x_axis toward y_axis about their normal, radians, and scale is positive uniform. Native world matrices preserve that physical plane without projection or reconstruction. Mirror retains its actual reflected normal/bulges/parameter direction; read WCS endpoints/native geometry after it. Same-target ordered transforms require the same explicit frame, with optional appearance edits; use separate requests for geometry replacement, legacy transforms or another frame. Read actual current geometry/owner/revision, choose the intended plane, and use existing copies mappings for new independent native roots. Only independent zero-thickness six-family curves use this new scope; constraints/model sources, Fields, persistent links, custom curves, blocks, annotations and solids need their existing owning routes. Native copy/mirror-copy retains the source and uses actual DeepClone ID mapping for ordinary owned data; observed native shape, root/display/XData and dictionaries/Xrecords are checked. Without workplane, existing WCS XY transformation and each legacy object-family scope remain unchanged.";
    internal static void Enrich(JsonArray operations)
    {
        foreach(var operation in operations.OfType<JsonObject>())
        {
            if(operation["properties"]?["operation"]?["const"]?.GetValue<string>() is not ("move" or "copy" or "rotate" or "scale" or "mirror"))continue;
            operation["properties"]!["workplane"]=WorkplaneSchema.Input();
            var legacy=operation["description"]?.GetValue<string>();
            operation["description"]=Scope+(legacy==null?"":" Without workplane: "+legacy);
            if(operation["properties"]?["operation"]?["const"]?.GetValue<string>() is "move" or "copy" or "rotate")
                operation["description"]="Ray/Xline use their separate native construction-curve adapter for this operation, including the explicit frame; the finite-curve scope below does not exclude them. "+ConstructionCurveSchema443.Scope+" "+operation["description"]!.GetValue<string>();
        }
    }
}
