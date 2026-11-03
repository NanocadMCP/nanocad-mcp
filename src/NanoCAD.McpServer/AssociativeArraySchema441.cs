using System.Text.Json.Nodes;

namespace NanoCAD.McpServer;

internal static class AssociativeArraySchema441
{
    static JsonObject Object(JsonObject fields,params string[] required)=>new(){["type"]="object",["additionalProperties"]=false,["properties"]=fields,["required"]=new JsonArray(required.Select(value=>(JsonNode?)JsonValue.Create(value)).ToArray())};
    static JsonObject Number(bool positive=false)=>positive?new(){["type"]="number",["exclusiveMinimum"]=0}:new(){["type"]="number"};
    static JsonObject Text()=>new(){["type"]="string"};
    static JsonObject NullableText()=>new(){["type"]=new JsonArray("string","null")};
    static JsonObject Flag()=>new(){["type"]="boolean"};
    static JsonObject NullableFlag()=>new(){["type"]=new JsonArray("boolean","null")};
    static JsonObject Count()=>new(){["type"]="integer",["minimum"]=1,["maximum"]=int.MaxValue};
    static JsonObject Handle()=>new(){["type"]="string",["pattern"]="^[0-9A-Fa-f]{1,16}$"};
    static JsonObject Vector(int count=3)=>new(){["type"]="array",["minItems"]=count,["maxItems"]=count,["items"]=Number()};
    static JsonObject Point()=>new(){["type"]="array",["minItems"]=2,["maxItems"]=2,["items"]=new JsonObject{["type"]="number",["minimum"]=-1e6,["maximum"]=1e6}};
    static JsonObject Operation(bool polar,bool create)
    {
        string operation=(create?"create_associative_":"set_associative_")+(polar?"polar_array":"path_array");
        var handles=new JsonObject{["type"]="array",["minItems"]=1,["uniqueItems"]=true,["items"]=Handle()};if(!create)handles["maxItems"]=1;
        var fields=new JsonObject{["operation"]=new JsonObject{["const"]=operation},["handles"]=handles,["count"]=Count()};
        var required=new List<string>{"operation","handles"};
        if(create){fields["base_point"]=Point();required.Add("base_point");required.Add("count");}
        else {fields["array_token"]=new JsonObject{["type"]="string",["pattern"]="^[0-9A-Fa-f]{64}$"};required.Add("array_token");}
        string[] changed;
        if(polar)
        {
            fields["angle_step_degrees"]=new JsonObject{["type"]="number",["not"]=new JsonObject{["const"]=0},["description"]="Signed native polar angular step in degrees, not total fill angle; negative is clockwise. count includes the seed. A full ring normally uses360/count; native validation decides non-distinct or invalid placements."};
            fields["rotate_items"]=Flag();changed=["count","angle_step_degrees","rotate_items"];
            if(create)
            {
                fields["center"]=Point();fields["radius"]=Number(true);fields["start_angle_degrees"]=Number();
                required.AddRange(["center","radius","start_angle_degrees","angle_step_degrees","rotate_items"]);
            }
        }
        else
        {
            fields["item_spacing"]=Number(true);fields["align_items"]=Flag();fields["path_handle"]=Handle();changed=["count","item_spacing","align_items","path_handle"];
            fields["path_handle"]!["description"]=create?"Actual live same-space native curve used as the requested path.":"Optional explicit live same-space target curve. Supplying it assigns native Path; omission never rebinds or repairs a missing identity. Read the desired actual curve and current array_token first. The target remains unchanged; this does not certify identity persistence after saving/reopening.";
            if(create)required.AddRange(["path_handle","item_spacing","align_items"]);
        }
        var result=Object(fields,required.ToArray());
        if(!create)result["anyOf"]=new JsonArray(changed.Select(field=>(JsonNode)new JsonObject{["required"]=new JsonArray(field)}).ToArray());
        result["description"]=create?(polar?
            "Create a retained native2D polar array from independent ordinary Model-space Line/Circle/Arc/Polyline/Ellipse/Spline source geometry. base_point is the explicit source-group anchor; item placement uses the WCSXY center, positive radius and start_angle_degrees, with count including the seed. rotate_items selects item rotation; angle_step_degrees is signed step, not fill. One row/level. Source geometry is acquired into native ownership; inspect actual returned source IDs and rendered item placement. Sole operation; native Undo/rollback is not guaranteed.":
            "Create a retained native2D path array from independent ordinary Model-space Line/Circle/Arc/Polyline/Ellipse/Spline sources and a separate live same-space curve path_handle. Creation verifies the requested current path identity and unchanged curve. base_point explicitly anchors the source group; Measure mode places count items with positive millimeter item_spacing along the path. align_items selects tangent alignment; direction follows the native path. One row/level. Sources are acquired. After DWG reopen, path_handle may be null with an explicit path_binding_observation while native item geometry/count/spacing remain readable; later following of the original curve is then unverified. Inspect actual parameters and rendered placement. Sole operation; native Undo/rollback is not guaranteed."):
            "Change sparse native "+(polar?"polar count/angle_step_degrees/rotate_items":"Measure-path count/item_spacing/align_items, with optional explicit path_handle")+" parameters using this root's current array_token. At least one requested field is required. Retain actual source IDs, native root placement and unrequested parameters; requested numeric drivers become literals. "+
            (polar?"Native polar fill angle may follow changed count/step. ":"Only an explicit path_handle assigns the selected current native Curve through owned Path; the target identity/owner/geometry remain. Omission retains the prior observed path state. Path edits complete and release their parameter/controller transaction before public top-level network evaluation and fresh reads. Optional item_geometry_observation reports actual native item storage and current source-reference representation separately from parameter count and rendered appearance; a differing count is disclosed without claiming regeneration or disabling the operation. If identity is unavailable after reopening, explicitly select the intended current path when rebinding is required; never infer that identity from item placement. This does not prove source following after another reopen. ")+
            "Driver-expression text is unavailable on read. Refresh actual parameters and inspect the useful rendered group; native Undo/rollback is not guaranteed.";
        return result;
    }
    internal static IEnumerable<JsonObject> Creates(){yield return Operation(true,true);yield return Operation(false,true);}
    internal static IEnumerable<JsonObject> Edits(){yield return Operation(true,false);yield return Operation(false,false);}
    internal static JsonObject Observation(bool polar)
    {
        var fields=new JsonObject{["array_token"]=new JsonObject{["type"]="string",["pattern"]="^[0-9A-Fa-f]{64}$"},["kind"]=new JsonObject{["const"]=polar?"polar":"path"},["array_handle"]=Text(),
            ["source_handles"]=new JsonObject{["type"]="array",["minItems"]=1,["items"]=Text()},["count"]=Count(),["rows"]=Count(),["levels"]=Count(),
            ["row_spacing"]=Number(),["level_spacing"]=Number(),["row_elevation"]=Number(),["native_base_point"]=Vector(),["base_normal"]=Vector(),["position_wcs"]=Vector(),["scale_factors"]=Vector(),["rotation"]=Number(),["driver_expression_observation"]=Text()};
        fields["native_base_point"]!["description"]="Actual native base-plane point in the array's own coordinate representation; position_wcs is the actual native root insertion in WCS. Do not silently treat this local point as a WCS group anchor.";
        if(polar)
        {fields["radius"]=Number();fields["start_angle_degrees"]=Number();fields["angle_step_degrees"]=Number();fields["fill_angle_degrees"]=Number();fields["rotate_items"]=Flag();}
        else
        {
            fields["path_handle"]=NullableText();fields["path_handle"]!["description"]="Current verified live same-space source-curve identity, or null when native identity is unavailable. Never infer it from item placement or earlier handles.";
            fields["item_spacing"]=Number();fields["method"]=new JsonObject{["enum"]=new JsonArray("Measure","Divide")};fields["align_items"]=Flag();fields["start_offset"]=Number();fields["end_offset"]=Number();fields["path_direction_observation"]=Text();
            var binding=new JsonObject{["state"]=new JsonObject{["enum"]=new JsonArray("live_identity","unavailable")},["unavailable_reason"]=NullableText(),
                ["simple"]=NullableFlag(),["external"]=NullableFlag(),["native_leaf_handle"]=NullableText(),["native_leaf_live"]=NullableFlag(),
                ["same_space"]=NullableFlag(),["same_database"]=NullableFlag(),["native_curve_geometry_available"]=NullableFlag(),["native_curve_geometry_observation"]=Text()};
            fields["path_binding_observation"]=Object(binding,binding.Select(field=>field.Key).ToArray());
            fields["path_binding_observation"]!["description"]="Actual native identity observation. Null flags are unobserved. A missing identity can coexist with readable native items, count/spacing, source IDs and frame; it does not prove the original curve still drives the array. Geometry availability is separate and may be unobserved.";
            var itemFields=new JsonObject{
                ["native_item_count"]=new JsonObject{["type"]="integer",["minimum"]=0},
                ["active_item_count"]=new JsonObject{["type"]="integer",["minimum"]=0},
                ["erased_item_count"]=new JsonObject{["type"]="integer",["minimum"]=0},
                ["locator_transform_sha256"]=Text(),
                ["materialized_source_reference_count"]=new JsonObject{["type"]=new JsonArray("integer","null"),["minimum"]=0},
                ["source_owner_handle"]=NullableText(),["representation_unavailable_reason"]=NullableText(),["scope"]=Text()};
            fields["item_geometry_observation"]=new JsonObject{["anyOf"]=new JsonArray(Object(itemFields,itemFields.Select(field=>field.Key).ToArray()),new JsonObject{["type"]="null"}),
                ["description"]="Optional actual native item/representation observations after an existing path-array edit; null means unobserved, not zero. Counts and transforms come from current native items, never generated from the requested count. Exact anonymous source-reference count is nullable when that representation cannot be proved. Review actual rendering separately."};
            var networkFields=new JsonObject{["network_handle"]=Text(),["status"]=NullableText(),["parent_network_handle"]=NullableText(),
                ["contains_observed_child_action"]=NullableFlag(),["is_database_top_network"]=NullableFlag(),["unavailable_reason"]=NullableText()};
            var evaluationFields=new JsonObject{["state"]=new JsonObject{["enum"]=new JsonArray("observed","partial","unavailable")},["complete"]=Flag(),
                ["unavailable_reason"]=NullableText(),["controller_handle"]=NullableText(),["controller_status"]=NullableText(),["action_body_handle"]=NullableText(),
                ["root_write_dependency_handle"]=NullableText(),["root_write_dependency_status"]=NullableText(),["owning_network_handle"]=NullableText(),
                ["database_top_network_handle"]=NullableText(),["reaches_database_top_network"]=NullableFlag(),
                ["owner_chain"]=new JsonObject{["type"]="array",["items"]=Object(networkFields,networkFields.Select(field=>field.Key).ToArray())},["scope"]=Text()};
            fields["evaluation_observation"]=new JsonObject{["anyOf"]=new JsonArray(Object(evaluationFields,evaluationFields.Select(field=>field.Key).ToArray()),new JsonObject{["type"]="null"}),
                ["description"]="Optional current read-only native controller, exact reverse-write dependency and actual owning-network status/membership chain. No network is created, marked dirty or evaluated. Unavailable/partial observations and nullable statuses do not disable readable array parameters. These transient observations and item_geometry_observation are excluded from array_token and unrequested-parameter comparisons; a current status is not a solver control, queue inventory or proof of rendered regeneration."};
        }
        return Object(fields,fields.Select(field=>field.Key).ToArray());
    }
}
