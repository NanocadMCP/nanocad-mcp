using System.Text.Json.Nodes;

namespace NanoCAD.McpServer;

internal static class SolidFaceProfileCreateSchema390
{
    internal static IEnumerable<JsonObject> Creates()
    {
        foreach(string kind in new[]{"extrusion","revolve","sweep"})
        {
            JsonObject Const(string value)=>new(){["type"]="string",["const"]=value};
            var face=BasicModelingSchema.Obj(new()
            {
                ["handle"]=BasicModelingSchema.Handle(),
                ["topology_token"]=new JsonObject{["type"]="string",["pattern"]="^[0-9A-F]{64}$"},
                ["native_id"]=new JsonObject{["type"]="string",["pattern"]="^[1-9][0-9]{0,18}$",
                    ["description"]="Canonical positive Int64 native face ID from this source's current inspect_solid_topology snapshot."}
            });
            var properties=new JsonObject{["type"]=Const("Solid3d"),["kind"]=Const(kind),
                ["layer"]=new JsonObject{["type"]="string"},["profile_face"]=face};
            if(kind=="extrusion")
            {properties["direction"]=BasicModelingSchema.Point();properties["taper_angle"]=BasicModelingSchema.Num();}
            else if(kind=="revolve")
            {properties["axis_origin"]=BasicModelingSchema.Point();properties["axis_direction"]=BasicModelingSchema.Point();
                properties["angle"]=BasicModelingSchema.Num();properties["start_angle"]=BasicModelingSchema.Num();}
            else
            {properties["path_handle"]=BasicModelingSchema.Handle();properties["base_point"]=BasicModelingSchema.Point();
                properties["align"]=BasicModelingSchema.Flag();properties["bank"]=BasicModelingSchema.Flag();
                properties["twist_angle"]=BasicModelingSchema.Num();properties["scale_factor"]=new JsonObject{["type"]="number",["minimum"]=1e-9,["maximum"]=1e9};}
            var schema=BasicModelingSchema.Obj(properties);
            if(kind=="sweep")schema["properties"]!["path_token"]=new JsonObject{["type"]="string",["pattern"]="^[0-9A-F]{64}$",
                ["description"]="Current geometry.native_curve.curve_token from inspect_objects. Required for an exact native Helix; optional for established other paths. An explicit token always checks the actual complete current path shape."};
            schema["description"]="Create a new independent direct Solid3d from one selected current face of a committed exact native Solid3d in the same Model Space. Use profile_face instead of profile_handle: handle, exact topology_token and one faces[].native_id from inspect_solid_topology, never a MultiCAD face ID. Preserve the original source body and optional original path; only checked independent detached copies reach the native face-profile overload. The entire source topology must retain the actual face mapping in its clone; unavailable mapping rejects before append. Source native history flags must be disabled, as for existing solid-copy creation; documented or locked-layer read-only sources remain. Face geometry, placement and kernel eligibility determine whether a nonempty solid can be formed; no flattened boundary, tessellation or sampled reconstruction is used. "+(kind switch
            {
                "extrusion"=>"direction is the full WCS displacement vector (its length is extrusion distance), with taper_angle radians in (-pi/2,pi/2). This creates a separate matching boss/stock, rather than extending the source body.",
                "revolve"=>"axis_origin and nonzero axis_direction define the WCS axis; angle in (0,2pi], start_angle radians. Plan a geometrically admissible face/axis placement.",
                _=>"path_handle is a distinct retained exact native Line/Arc/Circle/Polyline/Ellipse/Spline/Helix. Supply fresh path_token for a native Helix; inspect the actual returned runtime type because a created helix may be stored as Spline. The native Helix path retains its full reported NURBS and native parameters in checked copies. base_point is WCS, align controls native profile alignment, bank native banking, twist_angle radians and scale_factor positive final scale. Plan the actual face/path frame before sweeping."
            })+" No persistent association, feature or mate is created. Root geometry/identity/display/data is read back before append; opaque source-owned private records are not deeply verified and are disclosed when present. Inspect the returned body and the intended part view after a meaningful group. Native kernel applicability depends on the actual face/axis/path frame. Report any exact refusal or unknown outcome; supported ordinary curve/Region and loft routes remain available.";
            yield return schema;
        }
    }
}
