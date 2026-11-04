using System.Text.Json.Nodes;
namespace NanoCAD.McpServer;
internal static class ModelManufacturingSchema
{
    static JsonObject Id()=>new(){["type"]="string",["pattern"]=@"^\{[0-9A-Fa-f]{8}-[0-9A-Fa-f]{4}-[0-9A-Fa-f]{4}-[0-9A-Fa-f]{4}-[0-9A-Fa-f]{12}\}$"};
    static JsonObject Number(double min=0,double max=1e6)=>new(){["type"]="number",["minimum"]=min,["maximum"]=max};
    static JsonObject Flag()=>new(){["type"]="boolean"};
    static JsonObject Token()=>new(){["type"]="string",["pattern"]="^[0-9A-Fa-f]{64}$"};
    static JsonObject Object(JsonObject p)=>new(){["type"]="object",["additionalProperties"]=false,["properties"]=p,["required"]=new JsonArray(p.Select(x=>(JsonNode?)JsonValue.Create(x.Key)).ToArray())};
    private static JsonObject RequireHoleSeatFields(string seat, params string[] fields) => new()
    {
        ["if"] = new JsonObject { ["properties"] = new JsonObject { ["seat"] = new JsonObject { ["const"] = seat } } },
        ["then"] = new JsonObject { ["required"] = new JsonArray(fields.Select(field => (JsonNode?)JsonValue.Create(field)).ToArray()) }
    };
    internal static IEnumerable<JsonObject> Operations()
    {
        var support=Object(new(){["operation"]=new JsonObject{["type"]="string",["const"]="set_model_sketch_support"},["handles"]=BasicModelingSchema.List(BasicModelingSchema.Handle(),1),["expected_last_item_id"]=Id(),["feature_id"]=Id(),["definition"]=Object(new(){["reference_plane"]=new JsonObject{["type"]="string",["enum"]=new JsonArray("XY","YZ","ZX")},["offset"]=Number(-1e6)})});
        support["properties"]!["handles"]!["maxItems"]=1;
        support["properties"]!["definition"]!["properties"]!["update_support_id"]=Id();
        support["description"]="Native sketch support binding. feature_id is the existing PlanarSketch ID from current body history. Creates an offset WorkPlane associated with a document XY/YZ/ZX plane and binds the sketch to it; dependent features rebuild natively. Rebinding uses the datum coordinate frame, so the sketch may move in-plane as well as along its normal. Optional update_support_id explicitly selects the current offset support from history and edits its offset in place, preserving its identity and consumers; reference_plane is then retained, not replaced. This replaces its numeric offset and can affect other consumers of that same plane; inspect dependencies first. Without update_support_id, existing planes are not modified or erased. This changes support placement, not internal sketch constraints. Read resulting support_id and review dependent shape. ROLLBACK_NOT_GUARANTEED.";
        yield return support;
        var retained=(JsonObject)support.DeepClone();
        retained["properties"]!["definition"]=Object(new(){["kind"]=new JsonObject{["const"]="workplane"},["plane_id"]=Id(),["expected_datum_token"]=Token()});
        retained["properties"]!["definition"]!["properties"]!["recover_lost_support"]=new JsonObject{["const"]=false};
        retained["description"]="Bind an existing native PlanarSketch (feature_id) to a retained WorkPlane in the selected part's actual owner document. Read current datum_plane_ids token and last_item_id; definition.kind=workplane retains that exact plane rather than creating an offset copy. The datum may remain document construction or have selected-part affinity; foreign owners/part consumers and cyclic references are not transferred. The actual sketch local frame, projected references, profiles and dependent features are evaluated and rebuilt natively. Intentional support placement changes can move/rotate the sketch and dependent shape. Refresh body_ref, sketch/native_support, datum and history after the connected change, then review the intended geometry. Native Undo/rollback not guaranteed.";
        yield return retained;
        var recovery=(JsonObject)retained.DeepClone();
        recovery["properties"]!["definition"]!["properties"]!["recover_lost_support"]=new JsonObject{["const"]=true};
        recovery["properties"]!["definition"]!["properties"]!["expected_support_id"]=Id();
        recovery["properties"]!["definition"]!["properties"]!["frame_reference_policy"]=new JsonObject{
            ["type"]="string",["enum"]=new JsonArray("preserve","use_target_datum"),["default"]="preserve",
            ["description"]="preserve retains the observed native point/axis/second-axis frame references. use_target_datum explicitly retires only those sketch-frame references and uses the chosen WorkPlane's actual native frame; it does not remove internal sketch controls or authorize dropping other dependencies."};
        recovery["properties"]!["definition"]!["required"]=new JsonArray("kind","plane_id","expected_datum_token","recover_lost_support","expected_support_id");
        recovery["description"]="Explicit native recovery of an existing sketch whose retained planar support cannot be evaluated. Select the actual sketch, its observed failed expected_support_id, and an exact healthy upstream WorkPlane/token. Optional frame_reference_policy defaults to preserve, retaining observed native frame references. use_target_datum explicitly replaces that placement condition with the chosen datum's actual native frame and retires only the observed old point/axis/second-axis frame references. Neither policy removes internal sketch controls or waives other dependencies. recover_lost_support=true does not ignore unrelated model errors or authorize coordinate reconstruction. Recovery preserves readable native local sketch elements, controls, profile selections and dependent feature identities through native reassignment/rebuild. An unreadable old world frame is unknown; unchanged former WCS placement is not claimed. Read recovery_preparation.frame_references when present, inspect returned observations and the rebuilt part through MCP before accepting the result. Available only where native local geometry and relations can be read; no automatic face guessing, general healing or guaranteed Undo.";
        yield return recovery;
        var frame=Object(new(){["operation"]=new JsonObject{["type"]="string",["const"]="set_model_sketch_frame"},["handles"]=BasicModelingSchema.List(BasicModelingSchema.Handle(),1),["expected_last_item_id"]=Id(),["feature_id"]=Id(),["definition"]=Object(new(){["edge_token"]=Token(),["origin_edge_id"]=Id(),["axis_edge_id"]=Id(),["origin_point"]=new JsonObject{["type"]="string",["enum"]=new JsonArray("start","end","midpoint")},["axis_is_x"]=Flag(),["reverse_axis"]=Flag()})});
        frame["properties"]!["handles"]!["maxItems"]=1;
        frame["description"]="Native associative sketch frame. feature_id selects a PlanarSketch. Choose current straight native model edges from this same part: origin_point on origin_edge_id defines an associative WorkPoint, axis_edge_id defines a WorkAxis. Native preflight rejects downstream/cyclic references or an axis normal to the sketch plane. Sketch consumes the point and axis; its support plane remains unchanged. Can move/rotate the sketch and dependent features: choose engineering datums before applying, then inspect the group. Not a geometric constraint between sketch elements. ROLLBACK_NOT_GUARANTEED.";
        yield return frame;
        var terminal=Object(new(){["operation"]=new JsonObject{["type"]="string",["const"]="set_model_extrusion_extent"},["handles"]=BasicModelingSchema.List(BasicModelingSchema.Handle(),1),["expected_last_item_id"]=Id(),["feature_id"]=Id(),["definition"]=Object(new(){["extent"]=new JsonObject{["type"]="string",["enum"]=new JsonArray("distance","through_all","to_face")},["direction"]=new JsonObject{["type"]="string",["enum"]=new JsonArray("positive","negative")},["distance"]=Number(.001),["taper_angle"]=Number(-Math.PI/2+1e-6,Math.PI/2-1e-6),["target_body_handle"]=BasicModelingSchema.Handle(),["face_token"]=Token(),["face_id"]=Id()})});
        terminal["properties"]!["handles"]!["maxItems"]=1;
        terminal["properties"]!["definition"]!["required"]=new JsonArray("extent","direction","distance","taper_angle");
        terminal["properties"]!["definition"]!["properties"]!["replace_driver_formulas"]=new JsonObject{["type"]="boolean",["default"]=false};
        terminal["description"]="Edit an existing native extrusion's termination and taper, retaining its profile/history. One body, sole operation. direction is relative to its sketch normal. distance applies to distance mode; otherwise supply the retained positive distance. taper_angle radians. through_all requires a cut feature. to_face requires target_body_handle and current native model face_token/face_id from inspect_solid_topology(include_native_model_faces=true) for that target; the native face reference is retained. Choose a stable design datum and avoid downstream/cyclic references. This editor selects a one-sided positive/negative mode; it does not preserve a prior two-sided extrusion. Unchanged distance/taper values retain their expressions, including an inactive distance driver. A changed value with a formula requires explicit replace_driver_formulas=true to replace it with the supplied literal; read variables first. ROLLBACK_NOT_GUARANTEED.";
        yield return terminal;
        foreach(string kind in new[]{"loft","sweep","hole","thread","circular_pattern","mirror"})
        foreach(bool add in new[]{true,false})
        {
            var d=new JsonObject();
            if(kind=="loft") {d["profile_ids"]=BasicModelingSchema.List(Id(),2);d["part_operation"]=new JsonObject{["type"]="string",["enum"]=new JsonArray("join","cut")};}
            else if(kind=="sweep") {
                d["profile_id"]=Id();d["path_handles"]=BasicModelingSchema.List(BasicModelingSchema.Handle(),1);
                d["orientation"]=new JsonObject{["type"]="string",["enum"]=new JsonArray("normal_to_path","parallel_to_profile")};d["taper_angle"]=Number(-Math.PI/2+1e-6,Math.PI/2-1e-6);
                d["part_operation"]=new JsonObject{["type"]="string",["enum"]=new JsonArray("join","cut")};
            } else if(kind=="hole") {
                d["diameter"]=Number(.001);d["through"]=Flag();d["depth"]=Number();
                d["seat"]=new JsonObject{["type"]="string",["enum"]=new JsonArray("none","counterbore","countersink")};
                d["seat_diameter"]=Number();d["seat_depth"]=Number();d["seat_angle_degrees"]=Number(.001,179.999);
                d["flat_bottom"]=Flag();d["bottom_angle_degrees"]=Number(.001,179.999);
                d["offset1"]=Number(-1e6);d["offset2"]=Number(-1e6);d["sector"]=new JsonObject{["type"]="integer",["minimum"]=0,["maximum"]=3};
                if(add){d["face_token"]=Token();d["face_id"]=Id();d["edge_token"]=Token();d["first_edge_id"]=Id();d["second_edge_id"]=Id();}
            } else if(kind=="thread") {
                d["pitch"]=Number(.001);d["right_handed"]=Flag();d["internal"]=Flag();d["full_length"]=Flag();d["length"]=Number();
                if(add){d["face_token"]=Token();d["face_id"]=Id();d["edge_token"]=Token();d["front_edge_id"]=Id();}
            } else if(kind=="circular_pattern") {
                d["count"]=new JsonObject{["type"]="integer",["minimum"]=1,["maximum"]=int.MaxValue};d["angle_degrees"]=new JsonObject{["type"]="number"};
                d["axis"]=ModelPatternDirectionSchema439.Selector();
                if(add){d["edge_token"]=Token();d["axis_edge_id"]=Id();}
                else
                {
                    d["expected_count"]=new JsonObject{["type"]="integer",["minimum"]=1,["maximum"]=int.MaxValue};d["expected_angle_degrees"]=new JsonObject{["type"]="number"};
                    d["expected_axis_id"]=Id();
                    d["source_feature_ids"]=new JsonObject{["type"]="array",["minItems"]=1,["uniqueItems"]=true,["items"]=Id()};
                    d["expected_source_feature_ids"]=new JsonObject{["type"]="array",["minItems"]=1,["uniqueItems"]=true,["items"]=Id()};
                    d["replace_driver_formulas"]=Flag();
                }
            } else {d["plane_id"]=Id();d["reference_plane"]=new JsonObject{["type"]="string",["enum"]=new JsonArray("XY","YZ","ZX")};}
            if(add&&(kind=="circular_pattern"||kind=="mirror"))d["source_feature_ids"]=new JsonObject{["type"]="array",["minItems"]=1,["uniqueItems"]=true,["items"]=Id()};
            var definition=Object(d);
            if (kind == "hole")
            {
                definition["required"] = new JsonArray(d.Where(pair => !pair.Key.StartsWith("seat_", StringComparison.Ordinal))
                    .Select(pair => (JsonNode?)JsonValue.Create(pair.Key)).ToArray());
                definition["allOf"] = new JsonArray(
                    RequireHoleSeatFields("counterbore", "seat_diameter", "seat_depth"),
                    RequireHoleSeatFields("countersink", "seat_diameter", "seat_angle_degrees"));
            }
            if(kind=="circular_pattern")
            {
                definition["required"]=add?new JsonArray("count","angle_degrees","source_feature_ids"):new JsonArray("count","angle_degrees");
                if(add)definition["oneOf"]=new JsonArray(
                    new JsonObject{["required"]=new JsonArray("axis"),["not"]=new JsonObject{["anyOf"]=new JsonArray(new JsonObject{["required"]=new JsonArray("axis_edge_id")},new JsonObject{["required"]=new JsonArray("edge_token")})}},
                    new JsonObject{["required"]=new JsonArray("axis_edge_id","edge_token"),["not"]=new JsonObject{["required"]=new JsonArray("axis")}});
                else definition["allOf"]=new JsonArray(
                    new JsonObject{["if"]=new JsonObject{["required"]=new JsonArray("axis")},["then"]=new JsonObject{["required"]=new JsonArray("expected_axis_id")}},
                    new JsonObject{["if"]=new JsonObject{["required"]=new JsonArray("expected_axis_id")},["then"]=new JsonObject{["required"]=new JsonArray("axis")}},
                    new JsonObject{["if"]=new JsonObject{["required"]=new JsonArray("source_feature_ids")},["then"]=new JsonObject{["required"]=new JsonArray("expected_source_feature_ids")}});
            }
            if(kind=="mirror") {
                definition["required"]=new JsonArray(add?new JsonNode?[]{JsonValue.Create("source_feature_ids")}:Array.Empty<JsonNode?>());
                definition["oneOf"]=new JsonArray(new JsonObject{["required"]=new JsonArray("plane_id")},new JsonObject{["required"]=new JsonArray("reference_plane")});
            }
            var p=new JsonObject{["operation"]=new JsonObject{["type"]="string",["const"]=(add?"add_model_":"set_model_")+kind},["handles"]=new JsonObject{["type"]="array",["minItems"]=1,["maxItems"]=1,["items"]=new JsonObject{["type"]="string",["pattern"]="^[0-9A-Fa-f]{1,16}$"}},["expected_last_item_id"]=Id(),["definition"]=definition};
            if(!add)p["feature_id"]=Id();
            var schema=Object(p);
            if(kind=="sweep")definition["properties"]!["replace_driver_formulas"]=new JsonObject{["type"]="boolean",["default"]=false};
            if(kind=="hole")definition["properties"]!["replace_driver_formulas"]=new JsonObject{["type"]="boolean",["default"]=false,["description"]="For an existing supported native hole placement, explicitly replace a changed offset driver formula with the supplied literal. Unchanged offset formulas remain retained. Omit for ordinary edits; this does not create a missing named diameter/depth binding."};
            schema["description"]="Native parametric "+kind+". Read current body/history and native model topology before choosing real engineering datums. Sole operation; parameters in millimeters; angles in degrees except explicitly named taper_angle in radians. Use returned body_ref after rebuild. ROLLBACK_NOT_GUARANTEED. "+(kind switch{
                "loft"=>"Create/update regular native loft join/cut using ordered profile_ids owned by this body. Retains profile references. Updates replace regular loft sections; this editor does not discard guide bindings from guided lofts. For a new loft body including guides use model_loft creation.",
                "sweep"=>"General native swept join/cut or update. Select profile_id from this body history and ordered connected path_handles from the current drawing. Taper angle is radians; orientation is normal_to_path or parallel_to_profile. Maintains native profile/path references. An unchanged taper retains its driver expression. Changing a formula-driven taper requires explicit replace_driver_formulas=true to replace it with the supplied literal; read variables first. A changed body representation may have a new handle; use returned body_ref.",
                "hole"=>"Edit an existing native TwoEdges hole using its actual retained support/datum-edge bindings, signed offsets and sector. This edit does not create a new placement or silently convert a fixed-center hole. TwoEdges creation is unavailable on this host; choose fixed placement only when it expresses the task, otherwise retain the needed positional dependencies through a supported sketch cut. Blind depth must be positive. Counterbore requires diameter and depth; countersink requires diameter and angle. Seat diameter exceeds bore, and counterbore depth is below blind depth.",
                "thread"=>"Stores a native ThreadFeature on a cylindrical face with a circular start boundary edge. Read topology tokens and IDs; select internal/external from the actual surface and task. This feature alone does not establish physical helical B-rep geometry. Do not promise a modeled helix or silently substitute this representation when real helical geometry is required. Partial length must be positive.",
                "circular_pattern"=>"Retained native circular feature pattern of one part, using a healthy original source_feature_ids group. Choose definition.axis: global_axis X/Y/Z, an existing same-owner work_axis, explicit fixed_axis WCS origin/nonzero direction, native line, or current same-part linear/circular edge. The legacy edge_token/axis_edge_id creation pair remains supported. Native global/fixed WorkAxis construction is resolved/created only inside the write. Count is a positive native Int32 and includes the original. angle_degrees is a finite signed angular step, not total sweep; for a full ring use 360/N. Small/zero/full-turn steps are not silently clamped: native update decides validity and a stored pattern alone does not certify distinct material instances. Updates preserve axes/sources unless explicitly changed: axis requires expected_axis_id from current history.parameters.axis.native_id; source_feature_ids requires expected_source_feature_ids containing the original same-part feature IDs, never generated source_item_ids. Optional expected_count/expected_angle_degrees strengthen value preconditions. Retained native step mode, reversal, original identities/links and suppression of retained positions are checked. Range/fill interpretation is not edited here. Unchanged count/angle values preserve their exact owning expressions; a changed formula requires replace_driver_formulas=true or use set_model_parameter with current expected_expression. Read resulting native axis, driver IDs and mode from history/manufacturing_changes parameters, then inspect the intended repeated geometry.",
                _=>"Creation mirrors selected healthy features of the same body across a native WorkPlane. Choose reference_plane XY/YZ/ZX, or plane_id from existing body history; supply exactly one. Native reference datums are resolved during the write, never created by inspection. Update changes its plane while retaining sources."});
            if(kind!="hole"||!add)yield return schema;
            if(kind=="hole"){
                var fixedSchema=(JsonObject)schema.DeepClone();var definition2=(JsonObject)fixedSchema["properties"]!["definition"]!;var properties=(JsonObject)definition2["properties"]!;
                foreach(var key in new[]{"offset1","offset2","sector","edge_token","first_edge_id","second_edge_id"})properties.Remove(key);
                properties["center"] = BasicModelingSchema.Point();
                definition2["required"] = new JsonArray(definition2["required"]!.AsArray()
                    .Where(field => properties.ContainsKey(field!.GetValue<string>())).Select(field => field!.DeepClone()).Append(JsonValue.Create("center")).ToArray());
                fixedSchema["description"]="Native parametric hole with an explicit fixed WCS center on a selected planar support face. Keeps HoleFeature dimensions and face support but adds NO edge-offset constraints; use only when explicit placement matches the task, never present it as TwoEdges placement. Updates require an existing fixed-placement hole. Sole operation. Get expected_last_item_id from inspect_model_history. For creation get face_token and the planar native model face ID from inspect_solid_topology with include_native_model_faces=true. Omit seat_* for seat=none. Read body_ref after rebuild; ROLLBACK_NOT_GUARANTEED. Assess the hole geometry and required dimensions.";
                yield return fixedSchema;
            }
        }
    }
}
