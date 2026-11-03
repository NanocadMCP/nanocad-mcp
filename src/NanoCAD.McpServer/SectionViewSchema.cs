using System.Text.Json;
using System.Text.Json.Nodes;

namespace NanoCAD.McpServer;
internal static class SectionViewSchema
{
    public static JsonElement Create()
    {
        JsonObject Number()=>new(){["type"]="number",["minimum"]=-1e6,["maximum"]=1e6};
        JsonObject Point(int n)=>new(){["type"]="array",["minItems"]=n,["maxItems"]=n,["items"]=Number()};
        return JsonSerializer.SerializeToElement(new JsonObject
        {
            ["type"]="object",["additionalProperties"]=false,
            ["required"]=new JsonArray("operation","source_handles","name","section_line","origin","scale"),
            ["dependentRequired"]=new JsonObject{["viewing_direction"]=new JsonArray("vertical_direction"),["vertical_direction"]=new JsonArray("viewing_direction")},
            ["properties"]=new JsonObject
            {
                ["operation"]=new JsonObject{["type"]="string",["const"]="create_section_view"},
                ["source_handles"]=new JsonObject{["type"]="array",["minItems"]=1,["uniqueItems"]=true,["items"]=new JsonObject{["type"]="string",["pattern"]="^[0-9A-Fa-f]{1,16}$"}},
                ["name"]=new JsonObject{["type"]="string",["minLength"]=1,["maxLength"]=64},
                ["section_line"]=new JsonObject{["type"]="array",["minItems"]=2,["maxItems"]=2,["items"]=Point(3)},
                ["viewing_direction"]=Point(3),["vertical_direction"]=Point(3),
                ["origin"]=Point(2),["scale"]=new JsonObject{["type"]="number",["minimum"]=0.001,["maximum"]=1000}
            },
            ["description"]="Add one planar Section and associated root view to a named Millimeters Model Space drawing. Existing healthy sections/views are retained; use an unused view name. Sole operation; current layer0 and pre-created VIEWS must be available. source_handles declares the intended nonempty current top-level direct Solid3d/native Model body composition. This root Section path derives view participants from the Section and is applicable when the complete native Model solid composition is intended. A selected subset with other visible Model solids on on/thawed layers is not retained and returns SECTION_SOURCE_SELECTION_UNSUPPORTED before object changes. Hidden/off/frozen state alone does not establish a narrower saved source scope; only exact complete native view participation can prove it. The newly resident Section has unique native generation settings recording the requested sources in Live/2D/3D modes, with current type2D and live sectioning disabled; those stored settings alone do not prove projection selection. Destinations/appearance and all existing Section settings are retained. Actual complete view sources must exactly match the requested composition, separately from configuration-derived Section processing and sliced intersections; an unselected projection participant is a failure. Unselected block insertions/Paper views are retained rather than used as sources. Optional viewing_direction/vertical_direction must be supplied together as perpendicular unit WCS vectors; omit both for +X/+Z. The two section_line points follow vertical cross viewing for at least0.001 mm; the resulting plane must cross a selected body's bounds. origin places the view in WCS XY. Scale is native DScale denominator (2 draws at one-half size); design scale1. No pure slice, live-section change, existing-section edit, source mutation or save. Multi-body follower changes require their owning route. Recovery is verified when possible; Undo grouping is not guaranteed."
        });
    }
}

