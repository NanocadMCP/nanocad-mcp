using System.Text.Json;
using System.Text.Json.Nodes;

namespace NanoCAD.McpServer;

internal static class DrawingViewCreateSchema
{
    public static JsonElement Create()
    {
        JsonObject Point()=>new(){["type"]="array",["minItems"]=2,["maxItems"]=2,["items"]=new JsonObject{["type"]="number",["minimum"]=-1e6,["maximum"]=1e6}};
        JsonObject Triple()=>new(){["type"]="array",["minItems"]=3,["maxItems"]=3,["items"]=new JsonObject{["type"]="number",["minimum"]=-1e6,["maximum"]=1e6}};
        return JsonSerializer.SerializeToElement(new JsonObject
        {
            ["type"]="object",["additionalProperties"]=false,
            ["required"]=new JsonArray("operation","name_prefix","front_origin","scale"),
            ["oneOf"]=new JsonArray(
                new JsonObject{["required"]=new JsonArray("source_handle")},
                new JsonObject{["required"]=new JsonArray("source_handles")},
                new JsonObject{["required"]=new JsonArray("source_scope")}),
            ["properties"]=new JsonObject
            {
                ["operation"]=new JsonObject{["const"]="create_drawing_views",["type"]="string"},
                ["source_handle"]=new JsonObject{["type"]="string",["pattern"]="^[0-9A-Fa-f]{1,16}$"},
                ["source_handles"]=new JsonObject{["type"]="array",["minItems"]=1,["uniqueItems"]=true,["items"]=new JsonObject{["type"]="string",["pattern"]="^[0-9A-Fa-f]{1,16}$"},["description"]="Bodies projected together into each view. No body-count ceiling."},
                ["source_scope"]=new JsonObject{["type"]="string",["const"]="all_model_solids",["description"]="Project all top-level direct and native solid bodies in Model Space together. Resolved inside nanoCAD at the requested revision; no need to send every handle. This scope excludes bodies nested in block insertions."},
                ["layout_handle"]=new JsonObject{["type"]="string",["pattern"]="^[0-9A-Fa-f]{1,16}$",["description"]="Optional explicit active ordinary Paper layout from get_structure. Requires root target_space paper and CVPORT1. Omit for the existing Model creation route. Fresh native sheet objects only; no retained layout transfer."},
                ["name_prefix"]=new JsonObject{["type"]="string",["minLength"]=1,["maxLength"]=64},
                ["front_origin"]=Point(),["top_origin"]=Point(),
                ["projection_plane"]=new JsonObject{["type"]="object",["additionalProperties"]=false,
                    ["required"]=new JsonArray("origin","x_axis","y_axis"),
                    ["properties"]=new JsonObject{["origin"]=Triple(),["x_axis"]=Triple(),["y_axis"]=Triple()},
                    ["description"]="Optional root projection basis in WCS: perpendicular unit x_axis/y_axis, normal=x_axis cross y_axis. For a +Z plan use origin [0,0,0], x_axis [1,0,0], y_axis [0,1,0]. The root still has the native name/direction Front; child directions are relative to this basis, not fixed WCS axes. Omission retains the legacy native plane with normal +X. Inspect returned projection planes and the image when choosing views."},
                ["projected_views"]=new JsonObject{["type"]="array",["maxItems"]=7,["items"]=new JsonObject
                {
                    ["type"]="object",["additionalProperties"]=false,["required"]=new JsonArray("direction","origin"),
                    ["properties"]=new JsonObject
                    {
                        ["direction"]=new JsonObject{["type"]="string",["enum"]=new JsonArray("bottom","left","right","iso_top_left","iso_top_right","iso_bottom_left","iso_bottom_right")},
                        ["origin"]=Point()
                    }
                }},
                ["scale"]=new JsonObject{["type"]="number",["minimum"]=0.001,["maximum"]=1000}
            },
            ["description"]="Add an associated view group from one source_handle, a source_handles set, or source_scope all_model_solids in Model Space: root Front, optional Top (omit top_origin to skip), and optional projected_views. Source bodies are projected together; use a prefix whose generated names are unused. Each additional direction is unique. Bottom/Left/Right align with Front; isometric origins lie in the matching quadrant, at least 0.001 mm away per applicable axis. Sole operation. Named Millimeters drawing; current layer 0 and pre-created local VIEWS layer must be available. Create VIEWS separately with create_layer because its automatic creation cannot be rolled back reliably. WCS XY placements; optional Top shares Front X and is above it. Projection basis is independent of placement. Scale is native DScale denominator (2 draws at one-half size), applied to ALL views in the new group. Names append Front/Top/Bottom/Left/Right/IsoTopLeft/IsoTopRight/IsoBottomLeft/IsoBottomRight. No source transformation, section, retained layout transfer, annotation attachment, save or implicit fit. Optional layout_handle with target_space paper creates the same genuine linked projection group in that exact active native Paper sheet, while source bodies remain in Model. Placements then mean sheet XY millimeters. Paper creation returns drawing_view_creation_results with actual native document/source/tree identity and projection observation. Refresh get_drawing_views for lifecycle tokens and inspect rendered detail; empty/unavailable native geometry is disclosed. No document or source body-count ceiling."
        });
    }
}


