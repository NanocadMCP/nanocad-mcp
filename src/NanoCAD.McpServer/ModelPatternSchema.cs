using System.Text.Json.Nodes;

namespace NanoCAD.McpServer;

internal static class ModelPatternSchema
{
    private static JsonObject Count()=>new(){["type"]="integer",["minimum"]=1,["maximum"]=int.MaxValue};
    private static JsonObject Step()=>new(){["type"]="number"};
    private static JsonObject Id()=>new(){["type"]="string",["pattern"]="^\\{[0-9A-Fa-f]{8}-[0-9A-Fa-f]{4}-[0-9A-Fa-f]{4}-[0-9A-Fa-f]{4}-[0-9A-Fa-f]{12}\\}$"};
    private static JsonObject Ids()=>new(){["type"]="array",["minItems"]=1,["uniqueItems"]=true,["items"]=Id()};
    private static JsonObject Handle()=>new(){["type"]="string",["pattern"]="^[0-9A-Fa-f]{1,16}$"};
    internal static JsonObject Operation(bool update)
    {
        var fields=new JsonObject{
            ["operation"]=new JsonObject{["const"]=update?"set_model_rectangular_pattern":"add_model_rectangular_pattern"},
            ["handles"]=new JsonObject{["type"]="array",["minItems"]=1,["maxItems"]=1,["items"]=Handle()},
            ["expected_last_item_id"]=Id(),
            ["count_x"]=Count(),["count_y"]=Count(),["step_x"]=Step(),["step_y"]=Step()
        };
        if(update)
        {
            fields["feature_id"]=Id();fields["expected_count_x"]=Count();fields["expected_count_y"]=Count();
            fields["expected_step_x"]=Step();fields["expected_step_y"]=Step();
            fields["expected_source_feature_ids"]=Ids();fields["source_feature_ids"]=Ids();
            fields["replace_driver_formulas"]=new JsonObject{["type"]="boolean",["default"]=false};
            fields["expected_direction_x_id"]=Id();fields["expected_direction_y_id"]=Id();
        }
        else
        {
            fields["source_feature_id"]=Id();fields["source_feature_ids"]=Ids();fields["direction_x_handle"]=Handle();fields["direction_y_handle"]=Handle();
        }
        fields["direction_x"]=ModelPatternDirectionSchema439.Selector();fields["direction_y"]=ModelPatternDirectionSchema439.Selector();
        var required=fields.Where(p=>p.Key is not ("source_feature_id" or "source_feature_ids" or "expected_source_feature_ids" or "replace_driver_formulas" or
            "direction_x" or "direction_y" or "direction_x_handle" or "direction_y_handle" or "expected_direction_x_id" or "expected_direction_y_id")).Select(p=>(JsonNode?)JsonValue.Create(p.Key)).ToArray();
        var result=new JsonObject{["type"]="object",["additionalProperties"]=false,
            ["properties"]=fields,["required"]=new JsonArray(required)};
        if(!update)result["oneOf"]=new JsonArray(
            new JsonObject{["required"]=new JsonArray("source_feature_id"),["not"]=new JsonObject{["required"]=new JsonArray("source_feature_ids")}},
            new JsonObject{["required"]=new JsonArray("source_feature_ids"),["not"]=new JsonObject{["required"]=new JsonArray("source_feature_id")}});
        var conditions=new JsonArray();
        if(update)conditions.Add(new JsonObject{
            ["if"]=new JsonObject{["required"]=new JsonArray("source_feature_ids")},
            ["then"]=new JsonObject{["required"]=new JsonArray("expected_source_feature_ids")}});
        foreach(string axis in new[]{"x","y"})
        {
            string field="direction_"+axis,legacy=field+"_handle",expected="expected_"+field+"_id";
            if(update)
            {
                conditions.Add(new JsonObject{["if"]=new JsonObject{["required"]=new JsonArray(field)},["then"]=new JsonObject{["required"]=new JsonArray(expected)}});
                conditions.Add(new JsonObject{["if"]=new JsonObject{["required"]=new JsonArray(expected)},["then"]=new JsonObject{["required"]=new JsonArray(field)}});
            }
            else conditions.Add(new JsonObject{["oneOf"]=new JsonArray(new JsonObject{["required"]=new JsonArray(field),["not"]=new JsonObject{["required"]=new JsonArray(legacy)}},
                new JsonObject{["required"]=new JsonArray(legacy),["not"]=new JsonObject{["required"]=new JsonArray(field)}})});
        }
        result["allOf"]=conditions;
        result["description"]=update?
            "Edit counts, spacing, directions or the original feature group of one existing native rectangular pattern, including one followed by other features or associative views. Read feature_id, counts, steps, last_item_id and parameters.direction_x/direction_y from inspect_model_history. Supply exact expected counts/steps. A source-group pattern needs expected_source_feature_ids containing original same-part IDs; generated native_source_item_ids are separate copies. Optional source_feature_ids replaces that group and requires expected_source_feature_ids; each source must be a healthy preceding same-part forming feature without a cycle. To replace direction_x or direction_y, supply its native selector and expected_direction_x_id/expected_direction_y_id from the current binding.native_id. Other directions, step mode, reversal, driver IDs, retained-position suppression and meaningful history remain checked. Native range/fill patterns are not interpreted as step patterns by this editor. Unchanged numeric values preserve exact expressions; changed formula values require replace_driver_formulas=true, or use set_model_parameter with current expected_expression. Legacy omitted-group update retains its one-extrusion admission; explicit original groups cover other forming features. Each count is a positive native Int32; there is no Cartesian-product model-size cap. Sole operation; inspect the intended repeated geometry. ROLLBACK_NOT_GUARANTEED.":
            "Append one retained native rectangular feature pattern. Choose source_feature_id or source_feature_ids for a nonempty distinct group of healthy preceding native forming features in this exact part and owner; terminal extrusion-only admission is not required. Insertion follows the actual last model item before supported views. For each direction choose direction_x/direction_y native selector, or its legacy direction_x_handle/direction_y_handle Line. Selectors support global_axis X/Y/Z, existing same-owner work_axis, explicit fixed_axis WCS origin/nonzero direction, native line or current same-part linear edge/token. Line directions need no preliminary projection; nonorthogonal directions are passed to the native array. A fixed selector creates retained native WorkAxis construction without extra geometric constraints. Counts are positive native Int32 values per direction, with no Cartesian-product cap. Steps are finite signed drawing-unit values; native update determines geometric validity, not silent clamping. Results report original source_feature_ids separately from native_source_item_ids and actual direction bindings/modes. Retain original IDs as expected_source_feature_ids for later edits. Sole operation; inspect history and MCP images. ROLLBACK_NOT_GUARANTEED.";
        return result;
    }

    internal static JsonObject ElementOperation()
    {
        var fields=new JsonObject{
            ["operation"]=new JsonObject{["const"]="set_model_pattern_element"},
            ["handles"]=new JsonObject{["type"]="array",["minItems"]=1,["maxItems"]=1,["items"]=Handle()},
            ["expected_last_item_id"]=Id(),["feature_id"]=Id(),["expected_source_feature_id"]=Id(),
            ["expected_count_x"]=Count(),["expected_count_y"]=Count(),
            ["expected_step_x"]=Step(),["expected_step_y"]=Step(),
            ["index_x"]=new JsonObject{["type"]="integer",["minimum"]=0,["maximum"]=int.MaxValue},
            ["index_y"]=new JsonObject{["type"]="integer",["minimum"]=0,["maximum"]=int.MaxValue},
            ["expected_suppressed"]=new JsonObject{["type"]="boolean"},
            ["suppressed"]=new JsonObject{["type"]="boolean"}
        };
        var required=fields.Select(p=>(JsonNode?)JsonValue.Create(p.Key)).ToArray();
        return new JsonObject{["type"]="object",["additionalProperties"]=false,
            ["properties"]=fields,["required"]=new JsonArray(required),
            ["description"]="Suppress or restore one non-origin element of a native rectangular feature array, including one followed by other features or associative views. Read exact feature/source IDs, counts, steps and current suppressed state from inspect_model_history. Zero-based index_x/index_y must select an existing position other than [0,0]; the expected state must differ from the requested state. The native feature and other element states must persist. Sole operation; visually inspect the result. ROLLBACK_NOT_GUARANTEED; unknown requires inspection, never a new-ID retry."};
    }
}
