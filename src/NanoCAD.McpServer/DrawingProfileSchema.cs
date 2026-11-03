using System.Text.Json.Nodes;
using NanoCAD.Contracts;

namespace NanoCAD.McpServer;

internal static class DrawingProfileSchema
{
    private static JsonObject Object(JsonObject fields)=>new(){["type"]="object",["additionalProperties"]=false,["properties"]=fields,["required"]=new JsonArray(fields.Select(v=>(JsonNode?)JsonValue.Create(v.Key)).ToArray())};
    private static JsonObject Handle()=>new(){["type"]="string",["pattern"]="^[0-9A-Fa-f]{1,16}$"};
    private static JsonObject Kinds(params string[] values)=>new(){["enum"]=new JsonArray(values.Select(v=>(JsonNode?)JsonValue.Create(v)).ToArray())};
    private static JsonObject Point()=>Kinds("start","end","center");
    private static JsonObject Size()=>new(){["type"]="number",["minimum"]=.001,["maximum"]=1e6};
    internal static IEnumerable<JsonObject> Operations()
    {
        foreach(var op in CoreOperations())
        {
            op["properties"]!["workplane"]=WorkplaneSchema.Input();
            op["description"]=op["description"]!.GetValue<string>().Replace("WCSXY/Z0","selected workplane (WCSXY/Z0 by default)").Replace("WCSXY","selected workplane");
            if(op["properties"]!["operation"]!["const"]!.GetValue<string>() is not "extrude_drawing_profile" and not "revolve_drawing_profile")
                op["description"]=op["description"]!.GetValue<string>()+" Arc constraint edits require canonical entity OCS axes for the plane normal; rotated in-plane Arc solver frames are refused before writing after a geometry failure. Without a native default XY service, persisted linear variables and formulas remain available; adding angular constraints in another plane is refused before mutation because its stored angular units are not verified. No synthetic service group is created.";
            if(op["properties"]!["operation"]!["const"]!.GetValue<string>()=="set_drawing_dimension")
                op["description"]=op["description"]!.GetValue<string>()+" An existing ordinary ACAD radius/diameter on one Circle can also drive its one retained native part with distinct selected Circle profiles and typed sweep consumers sharing one sketch. Select that source Circle and its exact current constraint/expression/value. Create source constraints before consumption; this route does not add or edit internal PlanarSketch constraints. Native projections and consumed profile selections update through the same part rebuild, without Clear/reselection/body recreation. Peer Circle centers/radii, actual profile/sweep/ordered Line-path IDs, native taper drivers/formulas, feature order/dependencies and readable owned metadata remain protected; returned retained_sweep_parts exposes their actual current bindings and radii. The initial shared-sketch scope uses projected Circle profiles, existing extrusion/sweep construction, resident drawing Line paths and healthy associative views; other downstream feature types need their source-edit adapter. Unknown private constraint/association internals remain explicitly unobserved. Refresh actual history/source dimensions after the edit and inspect the useful resulting shape.";
            if(op["properties"]!["operation"]!["const"]!.GetValue<string>()=="add_drawing_constraints")
                op["description"]=op["description"]!.GetValue<string>()+" fix anchors one complete Line/Circle/Arc/Polyline, including its size; omit point selectors. fix_point anchors only the selected endpoint/center. symmetric retains two exact Lines or two exact Circles mirrored about the separate Line handle in third; it creates a persistent geometric relation, not mirrored copies. Include all three source handles. Attach these relations only when the current shape already satisfies the intended relation; adding incompatible or redundant anchors is not a way to repair geometry. Existing anchors are checked on later dimensional edits. Optional first_segment_index/second_segment_index/third_segment_index address actual straight or arc segments of retained native lightweight Polylines. Point names follow the original Polyline traversal, including clockwise arcs; dimensions and formulas use the same real ACAD controls. Selected parents retain their handle, normal, elevation, topology and widths. No Polyline-to-3D consuming-profile adapter is implied.";
            yield return op;
        }
    }
    private static IEnumerable<JsonObject> CoreOperations()
    {
        foreach(var action in new[]{"add_drawing_constraints","set_drawing_dimension","extrude_drawing_profile","revolve_drawing_profile","create_drawing_variable","name_drawing_parameter","set_drawing_expression","remove_drawing_constraints","remove_drawing_variable"})
        {
            var fields=new JsonObject{["operation"]=new JsonObject{["const"]=action},["handles"]=new JsonObject{["type"]="array",["minItems"]=1,["items"]=Handle()}};
            if(action is "remove_drawing_constraints" or "remove_drawing_variable")
            {
                JsonObject Id()=>new(){["type"]="string",["pattern"]="^\\{[0-9A-Fa-f]{8}(-[0-9A-Fa-f]{4}){3}-[0-9A-Fa-f]{12}\\}$"};
                JsonObject Text(int max)=>new(){["type"]="string",["minLength"]=1,["maxLength"]=max};
                if(action=="remove_drawing_constraints")fields["constraint_ids"]=new JsonObject{["type"]="array",["minItems"]=1,["uniqueItems"]=true,["items"]=Id()};
                else{fields["variable_id"]=Id();fields["expected_name"]=Text(128);fields["expected_expression"]=Text(8192);fields["expected_value"]=new JsonObject{["type"]="number",["minimum"]=-1e12,["maximum"]=1e12};}
                var removal=Object(fields);removal["description"]="Reconfigure ordinary ACAD drawing constraints without recreating source geometry. First inspect_constraints(include_variables=true); use fresh IDs and the complete connected Line/Circle/Arc components and consuming profiles/axes in handles. remove_drawing_constraints removes only the explicitly listed relations. A dimensional variable must have no other consumers: detach dependent formulas first. Geometry, retained constraints, variables and dependent models must remain unchanged. Native orphan scalar retention is possible; returned removed_variable_ids lists only verified actual deletions. remove_drawing_variable removes an exact document-owned User scalar only when no variables, dimensions or3D objects depend on it; match current name/expression/value. Delete unused leaf variables before their former drivers. After removal, add needed relations only when current geometry satisfies them, then use existing dimension/formula edits. This changes remaining solver freedom; it does not claim fully constrained geometry. Sole operation, WCSXY/Z0 and millimeters for source geometry. No internal MCS network deletion. ROLLBACK_NOT_GUARANTEED; partial failure is unknown, never inferred rolled back.";
                yield return removal;continue;
            }
            if(action=="revolve_drawing_profile")
            {
                fields["axis_handle"]=Handle();fields["name"]=new JsonObject{["type"]="string",["minLength"]=1,["maxLength"]=128};
                var revolution=Object(fields);fields["angle_degrees"]=new JsonObject{["type"]="number",["minimum"]=.1,["maximum"]=360};revolution["description"]="Create a native Mc3dSolid revolution from one Circle, at least three Lines, or at least two Lines/Arcs forming a simple closed WCSXY/Z0 profile, millimeters. axis_handle is a separate existing exact WCSXY Line, not part of handles; axis and profile layers must be available/unlocked. The profile must stay on one side of the infinite axis; line/arc contours may touch the axis, circles may not; arc interiors are checked, not just endpoints. Original curves/axis are retained; result source_refs includes the axis. Profile driving dimensions/User formulas can rebuild this body and ordered fixed-profile cut/join extrusions, including editable_profile tails with distinct retained sources. Include profile AND axis in later dimensional-edit handles; keep datum geometry fixed if it should not move. Native body validity, requested feature parameters, source links and history are checked after creation and rebuild. Read fresh history/variables/topology after creation. Optional angle_degrees is0.1..360 DEGREES, positive right-hand rotation around the directed axis. Omitted means the legacy complete All extent. Explicit angle_degrees creates an editable Angle extent, including360; set_model_revolution changes a numeric unshared angle later. Profile dimensions/formulas still rebuild partial bodies and ordered processing/placement. No internal MCS sketch constraints or face attachment claim. Sole operation. ROLLBACK_NOT_GUARANTEED; unknown may retain partial native history; inspect the affected model before dependent edits.";
                yield return revolution;continue;
            }
            if(NanoCAD.Contracts.DrawingVariablePlanning.IsAction(action))
            {
                JsonObject Text(int max)=>new(){["type"]="string",["minLength"]=1,["maxLength"]=max};
                JsonObject Scalar()=>new(){["type"]="number",["minimum"]=-1e12,["maximum"]=1e12};
                if(action=="create_drawing_variable"){fields["name"]=Text(64);fields["value"]=Scalar();}
                else
                {
                    fields["variable_id"]=new JsonObject{["type"]="string",["pattern"]="^\\{[0-9A-Fa-f]{8}(-[0-9A-Fa-f]{4}){3}-[0-9A-Fa-f]{12}\\}$"};
                    if(action=="name_drawing_parameter"){fields["expected_name"]=Text(128);fields["name"]=Text(64);}
                    else{fields["expected_expression"]=Text(8192);fields["expected_value"]=Scalar();fields["expression"]=Text(1024);}
                }
                var variable=Object(fields);
                variable["description"]="Ordinary ACAD drawing variables, separate from MCS model variables. Sole operation in millimeters. First inspect_constraints(include_variables=true) for current variable IDs, exact names/expressions/values, downstream dependencies and source_refs. create_drawing_variable creates a named nonangular document scalar. name_drawing_parameter renames a User driver or supported drawing dimension and preserves dependent expressions/values/geometry. set_drawing_expression sets arithmetic (+ - * / parentheses, exact existing names) and propagates the complete connected variable graph, solves dimensions and rebuilds affected projected models. A base may be an extrusion or a complete revolution around a separate native Line axis; include that axis in edit handles. A model may include subsequent explicitly ordered fixed-profile native join/cut extrusions (circle, polygon or polycurve) from add_model_extrusion; tail profiles/planes/parameters remain fixed during the drawing edit. Numeric literals can edit a User driver; linear dimensions must remain0.001–1000000 mm; angular dimensions and their formulas use0.1–179.9 degrees. User scalars may drive them explicitly, for example AngleDegrees and180-AngleDegrees. Renaming preserves angular formulas. Include complete connected Line/Circle/Arc source components of all affected and referenced dimensions and all consuming profiles (complete selected handles,WCSXY/Z0). Whole-network observation uses actual serialized-byte budgets and cancellation; no source, variable or relation count truncation. Extrusion histories may additionally retain healthy inward-shell, rectangular-pattern and fillet/chamfer features; their settings and dependencies must survive source-driven edits. No functions/internal MCS constraints or arbitrary other history types. Create an ACAD driving dimension before creating User variables. ROLLBACK_NOT_GUARANTEED; unknown requires inspection of affected sources and consumers. Underconstrained geometry may move: retain anchors and verify geometry plus an image after a meaningful group.";
                yield return variable;continue;
            }
            if(action=="add_drawing_constraints")
            {
                var specs=new JsonArray(
                    Object(new(){["kind"]=Kinds("horizontal","vertical"),["first"]=Handle()}),
                    Object(new(){["kind"]=Kinds("fix"),["first"]=Handle()}),
                    Object(new(){["kind"]=Kinds("symmetric"),["first"]=Handle(),["second"]=Handle(),["third"]=Handle()}),
                    Object(new(){["kind"]=Kinds("parallel","perpendicular","equal","concentric","collinear","tangent"),["first"]=Handle(),["second"]=Handle()}),
                    Object(new(){["kind"]=Kinds("coincident"),["first"]=Handle(),["second"]=Handle(),["first_point"]=Point(),["second_point"]=Point()}),
                    Object(new(){["kind"]=Kinds("smooth"),["first"]=Handle(),["second"]=Handle(),["first_point"]=Kinds("start","end"),["second_point"]=Kinds("start","end")}),
                    Object(new(){["kind"]=Kinds("fix_point"),["first"]=Handle(),["first_point"]=Point()}),
                    Object(new(){["kind"]=Kinds("radius","diameter","length"),["first"]=Handle(),["value"]=Size()}),
                    Object(new(){["kind"]=Kinds("angle"),["first"]=Handle(),["second"]=Handle(),["value"]=new JsonObject{["type"]="number",["minimum"]=DrawingAngularGeometry.MinimumDegrees,["maximum"]=DrawingAngularGeometry.MaximumDegrees}}),
                    Object(new(){["kind"]=Kinds("distance","distance_x","distance_y"),["first"]=Handle(),["second"]=Handle(),["first_point"]=Point(),["second_point"]=Point(),["value"]=Size()}));
                foreach(var spec in specs.OfType<JsonObject>())
                {
                    var properties=spec["properties"]!.AsObject();
                    if(properties["kind"]!["enum"]!.AsArray().Any(value=>value!.GetValue<string>()=="fix"))continue;
                    foreach(string role in new[]{"first","second","third"})if(properties.ContainsKey(role))
                        properties[role+"_segment_index"]=new JsonObject{["type"]="integer",["minimum"]=0,["description"]="Optional exact zero-based segment of this retained native Polyline; omit for an ordinary Line/Circle/Arc. Read current vertex order; no explosion or tessellation."};
                }
                fields["constraints"]=new JsonObject{["type"]="array",["minItems"]=1,["items"]=new JsonObject{["oneOf"]=specs}};
            }
            else if(action=="set_drawing_dimension")
            {
                fields["constraint_id"]=new JsonObject{["type"]="string",["pattern"]="^\\{[0-9A-Fa-f]{8}(-[0-9A-Fa-f]{4}){3}-[0-9A-Fa-f]{12}\\}$"};
                fields["expected_expression"]=new JsonObject{["type"]="string",["minLength"]=1,["maxLength"]=8192};fields["expected_value"]=Size();fields["value"]=Size();
            }
            else
            {fields["distance"]=Size();fields["direction"]=Kinds("positive","negative");fields["name"]=new JsonObject{["type"]="string",["minLength"]=1,["maxLength"]=128};}
            var result=Object(fields);
            result["description"]="Sole operation on selected exact top-level native Lines/Circles/Arcs in WCSXY/Z0 and millimeters. add_drawing_constraints attaches the selected native relations to geometry that ALREADY satisfies them, preserving shape. first/second are source handles; start/end mean Line or Arc endpoints, center means Circle or Arc center. Arcs have +Z normal, zero thickness and counterclockwise sweep; radial/equal/concentric constraints use circle support, tangent must lie on the arc sweep. Horizontal/vertical/parallel/perpendicular/collinear/length/angle remain Line relations. angle uses two distinct Lines and value in degrees (0.1..179.9): the sector between each Line's start-to-end direction, including obtuse angles, not its acute supplement. Both infinite lines must intersect inside supported coordinates; the annotation point is derived inside that sector. set_drawing_dimension also edits these angles numerically in degrees. ACAD angle variables and their expressions use degrees, unlike radians in other3D APIs; read returned units explicitly. distance_x/distance_y constrain absolute WCS X/Y point separation in millimeters, not Euclidean distance or a signed coordinate. They allow two different endpoints of the same Line. Zero separation is outside dimensional scope; use a geometric relation instead. Include every source connected to your selection by constraints; inspector references_outside_selection identifies an incomplete component. set_drawing_dimension edits a current angle, length, aligned/axis point distance, radius or diameter by exact ID/current value/expression; keep fixed anchors or inspect remaining solver freedom. Underconstrained geometry may move. Native constraint values, all touched relations, fixed anchors, unselected network sources and consuming model geometry are verified. Formula consumers of the edited variable are outside this numeric scope. extrude_drawing_profile projects one Circle, at least three Lines, or at least two Lines/Arcs forming one simple closed contour, preserving source entities; source order/orientation is resolved by connectivity, no gaps/branches/holes/self-intersections. Exact circular-segment moments verify arc profiles; arcs are not tessellated. Arc source contours support extrusion and full revolution around a separate Line axis. Their radius/diameter and endpoint/center relations can drive the same variable/formula and dependent-history workflow. Consuming projected models (extrusion or complete revolution) can rebuild; include separate revolution axis Lines in handles. Extrusion histories may retain healthy inward shells, rectangular patterns and edge treatments. Each with explicitly ordered subsequent join/cut extrusions using fixed circle/line/arc profiles; all their source curves must be selected. Native inventories use actual serialized-byte budgets and cancellation, without a relation/source-count truncation. Read inspect_constraints/history before edits, then inspect changed geometry and an image. ROLLBACK_NOT_GUARANTEED; partial failure is unknown; inspect affected sources and models before dependent edits. Does not imply internal MCS sketch constraints, arbitrary profiles or fully constrained status.";
            if(action is "add_drawing_constraints" or "set_drawing_dimension")
                result["description"]=result["description"]!.GetValue<string>().Replace("Lines/Circles/Arcs in","Lines/Circles/Arcs/Polylines/Splines in")+" Native open Spline endpoints also support coincident, fix_point and point-distance controls; whole-object fix retains a complete native Spline definition. smooth requires explicit start/end on both operands and at least one open Spline. Supply already coincident endpoints with collinear native endpoint tangents that continue through the join without a cusp, then retain the actual GeomSmooth relationship; it is not a one-time tangent approximation and no G2 continuity is promised. Spline controls, knots, weights, domain and stored fit definitions are verified. Include all connected sources. This ordinary-drawing route does not edit a Spline component consumed by a 3D model; use that model's native sketch route.";
            yield return result;
        }
    }
}

