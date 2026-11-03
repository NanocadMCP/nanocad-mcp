using System.Text.Json.Nodes;
namespace NanoCAD.McpServer;
internal static class BasicModelingSchema
{
 internal static JsonObject Num()=>new(){["type"]="number"};
 internal static JsonObject Flag()=>new(){["type"]="boolean"};
 internal static JsonObject Point()=>new(){["type"]="array",["minItems"]=3,["maxItems"]=3,["items"]=Num()};
 internal static JsonObject Handle()=>new(){["type"]="string",["pattern"]="^[0-9A-Fa-f]{1,16}$"};
 internal static JsonObject List(JsonObject item,int min=0)=>new(){["type"]="array",["minItems"]=min,["uniqueItems"]=true,["items"]=item};
 internal static JsonObject Obj(JsonObject p)=>new(){["type"]="object",["additionalProperties"]=false,["properties"]=p,["required"]=new JsonArray(p.Select(x=>(JsonNode?)JsonValue.Create(x.Key)).ToArray())};
 static JsonObject Const(string s)=>new(){["type"]="string",["const"]=s};
 internal static IEnumerable<JsonObject> Creates(){
  foreach(var kind in new[]{"extrusion","revolve"}){
   var p=new JsonObject{["type"]=Const("Solid3d"),["kind"]=Const(kind),["layer"]=new JsonObject{["type"]="string"},["profile_handle"]=Handle()};
   if(kind=="extrusion"){p["direction"]=Point();p["taper_angle"]=Num();}else{p["axis_origin"]=Point();p["axis_direction"]=Point();p["angle"]=Num();p["start_angle"]=Num();}
   var shape=Obj(p);shape["description"]="Create an independent direct solid from an existing closed planar curve or Region; the source is retained, with no associative history. "+(kind=="extrusion"?"direction is the full WCS displacement vector (its length is extrusion distance), taper_angle radians. Region profiles can contain holes; kernel determines admissible taper.":"Revolve about a WCS axis through axis_origin with nonzero axis_direction, angle in (0,2pi], start_angle radians. The axis and profile must form an admissible solid; no tessellation or coordinate-profile reconstruction.");yield return shape;
  }
  var region=Obj(new(){["type"]=Const("Region"),["layer"]=new JsonObject{["type"]="string"},["boundary_handles"]=List(Handle(),1),["subtract_boundaries"]=List(List(Handle(),1))});
  region["description"]="Create a planar Region from existing closed boundary curves, then subtract optional closed boundaries in order. Each handle chain must form one closed planar region. Sources are retained. Region can be used as the profile of a general sweep/loft; curved boundaries are not tessellated.";yield return region;

  var helix=Obj(new(){["type"]=Const("Helix"),["layer"]=new JsonObject{["type"]="string"},["axis_origin"]=Point(),["axis_direction"]=Point(),["radial_direction"]=Point(),["base_radius"]=Num(),["top_radius"]=Num(),["height"]=Num(),["turns"]=Num(),["right_handed"]=Flag()});
  helix["description"]="Helical path constructed with native Helix for general modeling. nanoCAD may store it as a Spline; inspect the returned entity rather than assuming editable Helix properties. Axis and radial directions must be nonzero and perpendicular. Start is axis_origin + normalized radial_direction * base_radius. Positive height and turns; top_radius may be zero. Pitch = height/turns. No body is created. inspect_objects reports the current curve token/endpoints for retained selected-face paths; use that readback rather than assuming the created runtime type.";yield return helix;
  var sweep=Obj(new(){["type"]=Const("Solid3d"),["kind"]=Const("sweep"),["layer"]=new JsonObject{["type"]="string"},["profile_handle"]=Handle(),["path_handle"]=Handle(),["base_point"]=Point(),["align"]=Flag(),["bank"]=Flag(),["twist_angle"]=Num(),["scale_factor"]=Num()});
  sweep["description"]="General direct sweep of an existing closed planar curve/Region along an existing native curve, including spline/helix. Preserves both source entities; creates a new independent body without associative history. base_point is WCS; align controls alignment of profile to path, bank native profile banking, twist_angle radians, positive scale_factor final scale. Plan the profile frame and orientation before sweeping; a helical path alone does not determine a thread form. Native kernel applicability is geometry dependent. Await pending; do not duplicate a write.";yield return sweep;
  var path=Handle();path["type"]=new JsonArray("string","null");
  var loft=Obj(new(){["type"]=Const("Solid3d"),["kind"]=Const("loft"),["layer"]=new JsonObject{["type"]="string"},["section_handles"]=List(Handle(),2),["guide_handles"]=List(Handle()),["path_handle"]=path,["interpolation"]=new JsonObject{["type"]="string",["enum"]=new JsonArray("smooth","ruled")}});
  loft["properties"]!["closed"]=new JsonObject{["type"]="boolean",["default"]=false,
   ["description"]="Optional native closure along the ordered loft sections, joining last section back to first. This is distinct from each profile already being a closed planar boundary. Omit or use false for the established capped loft. Use distinct sections in their intended cyclic order; do not repeat the first handle. Native kernel determines admissible section/guide/path placement."};
  loft["description"]="Direct solid loft through ordered existing closed planar profiles, preserving sources. Sections need not be circles or parallel to WCS XY. Choose guide curves intersecting every section, or one path_handle; use empty guide_handles and null path_handle for unguided loft. Native smooth/ruled interpolation and optional closed loop through the sections; no associative history. The explicit closed flag and interpolation are read back from native options before construction. Native closure/guide applicability depends on the actual geometry; inspect the returned body and the required closing span. Assess intended shape and required sections, not an assumed volume formula.";yield return loft;
 }
 internal static IEnumerable<JsonObject> FaceOperations(JsonObject transform){
  foreach(var kind in new[]{"offset_faces","presspull_faces","extrude_faces","extrude_faces_along_path","transform_faces","taper_faces","remove_faces"}){
   var handles=List(Handle(),1);handles["maxItems"]=1;
   var p=new JsonObject{["operation"]=Const(kind),["handles"]=handles,["topology_token"]=new JsonObject{["type"]="string",["pattern"]="^[0-9A-F]{64}$"},["faces"]=List(new JsonObject{["type"]="string",["pattern"]="^[1-9][0-9]*$"},1)};
   if(kind is "offset_faces" or "presspull_faces" or "extrude_faces")p["distance"]=Num();
   if(kind is "extrude_faces" or "taper_faces")p["angle"]=Num();
   if(kind=="taper_faces"){p["base_point"]=Point();p["draft_direction"]=Point();}
   if(kind=="transform_faces")p["transform"]=transform.DeepClone();
   if(kind=="extrude_faces_along_path")p["path_handle"]=Handle();
   var result=Obj(p);
   if(kind=="extrude_faces_along_path")result["properties"]!["path_token"]=new JsonObject{["type"]="string",["pattern"]="^[0-9A-F]{64}$",
    ["description"]="Current geometry.native_curve.curve_token from inspect_objects. Required for an exact native Helix; optional for established other paths. An explicit token checks the complete current path shape."};
   result["description"]="Edit selected faces of one independent direct body in place. Read native face IDs and topology_token from inspect_solid_topology; IDs are invalidated by topology changes. Sole operation. ROLLBACK_NOT_GUARANTEED. "+(kind switch{
    "offset_faces" or "presspull_faces"=>"Signed normal offset of existing faces, with adjacent geometry rebuilt by the kernel. Positive expands outward, negative inward. This is face push/pull, not automatic detection of a bounded screen region.",
    "extrude_faces"=>"Extrude existing faces by signed distance, taper angle in radians (-pi/2,pi/2).",
    "extrude_faces_along_path"=>"Grow the selected existing direct-body faces along one distinct committed exact native Line/Arc/Circle/Polyline/Ellipse/Spline/Helix path in the same Model Space, retaining the body handle and original path. Supply fresh path_token for an exact native Helix from inspect_objects; a host-stored Spline follows the actual Spline route. Complete native NURBS and Helix parameters remain checked in independent copies. Inspect its actual WCS endpoints/frame and plan the intended face/path placement; the native kernel determines applicability and growth orientation. The path is passed as an independent detached copy without automatic alignment, coordinate reconstruction, history or persistent association. Read-only source curves may remain on locked layers. Current topology/body ownership and complete retained path geometry/root data are checked before/after; opaque path-owned private payloads are not deeply verified. Native mutation runs once on the resident body, following the existing face-edit lifecycle; no detached success oracle or analytic volume target. Reacquire actual topology and review the shape in a meaningful engineering group. Runtime placement/kernel support remains unverified; a started failure remains unknown, never rolled_back.",
    "transform_faces"=>"UNVERIFIED ON CURRENT HOST: native TransformFaces returned NotImplementedYet in the grouped check; do not promise this route. Apply translation/rotation/mirror/uniform scale to selected faces, healing neighboring geometry through the native kernel.",
    "taper_faces"=>"Draft selected faces about WCS base_point along nonzero draft_direction; signed angle in radians (-pi/2,pi/2).",
    _=>"UNVERIFIED ON CURRENT HOST: native RemoveFaces returned NotImplementedYet in the grouped check; do not promise this route. Remove selected faces and let the native kernel heal the solid. Cannot guarantee arbitrary missing-face reconstruction."});yield return result;
  }
 }
 internal static JsonObject BooleanOperation(){
  var targets=List(Handle(),1);targets["maxItems"]=1;
  var result=Obj(new(){["operation"]=Const("boolean_solid"),["handles"]=targets,
   ["operator"]=new JsonObject{["type"]="string",["enum"]=new JsonArray("union","intersection","subtract")},
   ["tool_handles"]=List(Handle(),1)});
  result["description"]="Apply union, subtraction or intersection to one existing independent direct Solid3d in place, preserving its handle and display properties. tool_handles are ordered, distinct committed top-level native Solid3d shapes in the same Model Space; they remain unchanged and are never consumed, erased or moved. The target cannot also be a tool. Enabled native history flags on tool shapes are excluded, matching the existing copy-boolean route. Each native boolean is applied in order to the target using a detached copy of its tool. Target feature histories, fields and unclassified dependencies need their owning adapter; healthy target drawing views/sections are retained and refreshed. Sole operation. Detached preview rejects empty or invalid results before native target mutation. A contained union tool or nonintersecting subtraction may be a valid no-op; do not infer design correctness from success or volume alone. ROLLBACK_NOT_GUARANTEED: a started failure may leave a changed target; inspect its geometry/history before dependent work. Reacquire topology and review the intended result after a meaningful group. New in332; native acceptance pending.";
  return result;
 }
}
