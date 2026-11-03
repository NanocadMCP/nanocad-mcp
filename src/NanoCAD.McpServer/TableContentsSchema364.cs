using System.Text.Json.Nodes;
using NanoCAD.Contracts;

namespace NanoCAD.McpServer;

internal static class TableContentsSchema364
{
    static JsonObject Text()=>new(){["type"]="string"};
    static JsonObject Number()=>new(){["type"]="number"};
    static JsonObject Bool()=>new(){["type"]="boolean"};
    static JsonObject Index()=>new(){["type"]="integer",["minimum"]=0,["maximum"]=int.MaxValue};
    static JsonObject Enum(params string[] values)=>new(){["type"]="string",["enum"]=new JsonArray(values.Select(value=>(JsonNode?)JsonValue.Create(value)).ToArray())};
    static JsonObject Array(JsonObject item)=>new(){["type"]="array",["items"]=item};
    static JsonObject Nullable(JsonObject item)=>new(){["anyOf"]=new JsonArray(item,new JsonObject{["type"]="null"})};
    static JsonObject Object(params (string Name,JsonObject Schema)[] fields)
    {
        var properties=new JsonObject();foreach(var field in fields)properties[field.Name]=field.Schema;
        return new(){["type"]="object",["additionalProperties"]=false,["properties"]=properties,["required"]=new JsonArray(fields.Select(field=>(JsonNode?)JsonValue.Create(field.Name)).ToArray())};
    }
    static JsonObject Action(string action,params (string Name,JsonObject Schema)[] fields)
    {
        var common=new[]{("action",new JsonObject{["const"]=action}),("row",Index()),("column",Index()),("content_index",Index())};
        return Object(common.Concat(fields).ToArray());
    }
    internal static JsonObject Operation()
    {
        JsonObject ValueAction(string action)
        {
            var value=Action(action,("data_type",Enum(TableValueEditingValidation336.DataTypes)),("unit_type",Enum(TableValueEditingValidation336.UnitTypes)),
                ("value",new JsonObject{["anyOf"]=new JsonArray(Text(),Number())}),("data_format",Text()));
            value["allOf"]=new JsonArray(
                new JsonObject{["if"]=new JsonObject{["properties"]=new JsonObject{["data_type"]=new JsonObject{["const"]="String"}}},["then"]=new JsonObject{["properties"]=new JsonObject{["value"]=new JsonObject{["type"]="string",["maxLength"]=4096},["unit_type"]=new JsonObject{["const"]="Unitless"}}}},
                new JsonObject{["if"]=new JsonObject{["properties"]=new JsonObject{["data_type"]=new JsonObject{["const"]="Long"}}},["then"]=new JsonObject{["properties"]=new JsonObject{["value"]=new JsonObject{["type"]="integer",["minimum"]=int.MinValue,["maximum"]=int.MaxValue}}}},
                new JsonObject{["if"]=new JsonObject{["properties"]=new JsonObject{["data_type"]=new JsonObject{["const"]="Double"}}},["then"]=new JsonObject{["properties"]=new JsonObject{["value"]=Number()}}});return value;
        }
        var scale=Number();scale["exclusiveMinimum"]=0;
        var height=Number();height["exclusiveMinimum"]=0;
        var font=new JsonObject{["type"]="string",["pattern"]="^[0-9A-Fa-f]{1,16}$"};
        var aci=new JsonObject{["type"]="integer",["minimum"]=0,["maximum"]=256};
        var rgb=new JsonObject{["type"]="array",["minItems"]=3,["maxItems"]=3,["items"]=new JsonObject{["type"]="integer",["minimum"]=0,["maximum"]=255}};
        var format=Action("set_format",("data_format",Text()),("rotation",Number()),("scale",scale),("text_height",height),("text_style_handle",font),("color_aci",aci),("color_rgb",rgb));
        format["required"]=new JsonArray("action","row","column","content_index");
        format["anyOf"]=new JsonArray(new[]{"data_format","rotation","scale","text_height","text_style_handle","color_aci","color_rgb"}.Select(name=>(JsonNode?)new JsonObject{["required"]=new JsonArray(name)}).ToArray());
        format["not"]=new JsonObject{["required"]=new JsonArray("color_aci","color_rgb")};
        var cellProperties=Object(("alignment",Enum(TableEditingValidation.Alignments)),("background",TableStyleSchema337.RoleBackground()),("grids",TableStyleSchema337.RoleGrids()));
        cellProperties.Remove("required");cellProperties["minProperties"]=1;
        var appearance=Object(("action",new JsonObject{["const"]="set_cell_format"}),("row",Index()),("column",Index()),("properties",cellProperties));
        var layout=Object(("action",new JsonObject{["const"]="set_layout"}),("row",Index()),("column",Index()),("content_layout",Enum(TableContentsValidation364.Layouts)));
        var literal=new JsonObject{["type"]="string",["maxLength"]=4096};
        var blockAttribute=Object(("attribute_definition_handle",(JsonObject)font.DeepClone()),("value",(JsonObject)literal.DeepClone()));
        var blockAttributes=Array(blockAttribute);blockAttributes["description"]="Optional complete initial values keyed by actual nonconstant ATTDEF handles. Omit to explicitly use their current ordinary single-line definition defaults; [] is complete only for a definition without nonconstant attributes. Constants remain untouched.";
        var insertBlock=Action("insert_block",("block_handle",(JsonObject)font.DeepClone()),("auto_fit",Bool()),("attributes",blockAttributes));
        insertBlock["required"]=new JsonArray("action","row","column","content_index","block_handle","auto_fit");
        insertBlock["description"]="Insert a native block slot at current index0..count. Select an existing ordinary named static local BlockTableRecord from get_structure section=blocks; no definition is created or edited. auto_fit passes the native assignment autoFit boolean; current scale and automatic scaling are read back rather than calculated. Source geometry, ATTDEF/member metadata and all other slots remain retained.";
        var setAttribute=Action("set_block_attribute",("attribute_definition_handle",(JsonObject)font.DeepClone()),("value",(JsonObject)literal.DeepClone()));
        setAttribute["description"]="Edit one literal nonconstant single-line attribute value of the actual current block slot. Read block_attributes and block_editing first; use its actual attribute_definition_handle. No shared ATTDEF, source definition or other occurrence is rewritten.";
        var actions=Array(new JsonObject{["oneOf"]=new JsonArray(ValueAction("set_value"),ValueAction("insert_value"),insertBlock,setAttribute,format,layout,appearance,Action("move",("destination_index",Index())),Action("delete"))});actions["minItems"]=1;
        var handles=Array(new JsonObject{["type"]="string",["pattern"]="^[0-9A-Fa-f]{1,16}$"});handles["minItems"]=1;handles["maxItems"]=1;
        var token=new JsonObject{["type"]="string",["pattern"]="^[0-9A-F]{64}$",["description"]="Exact fresh inspect_objects geometry.contents_editing.contents_token. Includes all actual indexed slots/order, native storage/type/unit/format/overrides, Field/block bindings and readable owned metadata, frame/topology and resident identity. Legacy geometry.editing.table_token is a different token."};
        var result=Object(("operation",new JsonObject{["const"]=TableContentsValidation364.Operation}),("handles",handles),("contents_token",token),("actions",actions));
        result["description"]="Edit actual ordered native contents of one resident Table in Model or active ordinary Paper; sole operation. Read geometry.contents_editing first. Actions execute in listed order with zero-based current indices. insert_value and insert_block accept index0 through current count; move destination is the final index in the same cell and must be less than pre-move count. Scalar actions select ordinary String/Unitless, Int32 Long or finite Double slots. delete also selects a fresh field_delete.eligible native Field/formula slot by its sole current local binding. Native DeleteContent removes cell content; the application never erases, purges or reparents its Field. Actual GetChildren definitions and current owners are observed. Only proved exclusively owned selected storage may retire; otherwise selected storage, child definitions and supported typed persistent references must survive exactly. An unobserved/foreign native storage class, shared slot binding, referenced-not-owned child reports its scoped boundary. delete also removes an eligible ordinary native block slot by current binding, without erasing its existing definition. insert_block assigns an existing local static definition and initial ordinary single-line attribute values; set_block_attribute changes a current occurrence value. Read block_editing. Native generated block-reference display is admitted only through exact exclusive Table layout ownership/current source bindings and absence of foreign Field/XData/extension/reactor metadata. Nested source definitions, dynamic/annotative/Field-bearing source members and multiline attributes retain their scoped adapter boundary. Every other Field/block binding/definition/attribute remains retained when indices shift. set_format also selects a scalar slot font/positive drawing-millimeter text height/ACI or RGB content color. Actual local horizontal text-style handles come from get_structure; the existing font/glyph engine checks readable visible text and discloses unavailable run coverage. set_cell_format has no content_index and selects cell alignment/background/six grid visibility/color/native lineweight; scalar and dependent slots share cell appearance while their storage and bindings remain. Opposite shared physical borders cannot receive conflicting final properties. set_layout selects the cell's native Flow, StackedHorizontal or StackedVertical arrangement and has no content_index; retained dependent slots may share that cell. Strings are plain literals; no formatting/Field-code parsing. Typed values pass in native storage units, without implicit conversion. Data format is the native scalar format string; empty may resolve to readable native default with a warning. Rotation uses native radians; positive Scale edits require an existing nonautomatic scalar slot. Select merged anchors; native content/format protection is enforced. No table reconstruction, Field assignment/reuse, arbitrary Field code construction/evaluation, source block redefinition, external links or link refresh. Block replacement uses ordered delete and insert_block at the intended current index; block movement and scalar set_format are not block attribute operations. Before deleting a Field, allow for unproved auxiliary cleanup and nonguaranteed Undo: a started failure may leave partial changes. A deleted binding does not prove that unused Field storage was physically erased; actual surviving MText-owned storage remains observed. Persistent Table-root dependencies and actual indexless TableContent ownership that cannot prove multi-slot Field membership remain scoped barriers. Native layout can reflow physical sizes; read the new contents token and current frame, then review useful text/order/readability in the combined view. Actual selected child ownership is observed for deletion; private child/evaluator payload remains unobserved. Recovery/Undo is not guaranteed; unknown is not success or rollback.";
        return result;
    }
    internal static JsonObject Read()
    {
        var handle=Nullable(Text());var anyValue=new JsonObject{["anyOf"]=new JsonArray(Text(),Number(),Bool(),new JsonObject{["type"]="object"},new JsonObject{["type"]="null"})};
        var color=Object(("method",Text()),("aci",Nullable(Index())),("rgb",Nullable(Array(Index()))));
        var definition=Object(("handle",Text()),("owner_handle",Text()),("runtime_type",Text()),("field_code",Text()),("object_reference_code",Text()),("evaluator_id",Text()),("format",Text()),
            ("evaluation_options",Text()),("filing_options",Text()),("is_text_field",Bool()),("has_fields",Bool()),("private_state_complete",new JsonObject{["const"]=false}),("scope",Text()));
        var attribute=Object(("definition_handle",Text()),("owner_handle",Text()),("tag",Text()),("constant",Bool()),("value",Text()));
        var fieldDelete=Nullable(Object(("eligible",Bool()),("reason",Nullable(Text())),("scope",Text())));
        var blockEditing=Nullable(Object(("eligible",Bool()),("reason",Nullable(Text())),("scope",Text()),("editable_attribute_definition_handles",Array(Text()))));
        var slot=Object(("content_index",Index()),("native_content_type",Enum("Value","Field","Block")),("stored_text",Text()),("data_type",Text()),("unit_type",Text()),("native_value",anyValue),("double_bits",Nullable(Text())),
            ("data_format",Text()),("rotation",Number()),("scale",Number()),("auto_scale",Bool()),("overrides",Index()),("text_height",Number()),("text_style_handle",Text()),("content_color",color),("has_formula",Bool()),("formula",Text()),
            ("field_handle",(JsonObject)handle.DeepClone()),("block_handle",(JsonObject)handle.DeepClone()),("field_definition",Nullable(definition)),("block_attributes",Nullable(Array(attribute))),("evaluation_unavailable_reason",Nullable(Text())),("native_value_source",Nullable(Text())),("stored_text_unavailable_reason",Nullable(Text())),("can_edit_scalar",Bool()),("field_delete",fieldDelete),("block_editing",blockEditing));
        var range=Object(("top_row",Index()),("left_column",Index()),("bottom_row",Index()),("right_column",Index()));
        var grid=Object(("line",Text()),("visibility",Text()),("lineweight",new JsonObject{["type"]="integer"}),("linetype_handle",Nullable(Text())),("line_style",Text()),("double_spacing",Number()),("overrides",Index()),("color",(JsonObject)color.DeepClone()));
        var cellFormat=Object(("cell_style",Text()),("data_format",Text()),("alignment",Text()),("text_height",Number()),("text_style_handle",Text()),("content_color",(JsonObject)color.DeepClone()),
            ("background_none",Bool()),("background_color",(JsonObject)color.DeepClone()),("margins",Array(Number())),("grids",Array(grid)));
        var cell=Object(("row",Index()),("column",Index()),("content_count",Index()),("content_layout",Text()),("content_editable",Bool()),("format_editable",Bool()),("merged_range",Nullable(range)),("format",Nullable(cellFormat)),("contents",Array(slot)));
        var style=Text();style["description"]="Current exact native TableStyle handle, from the complete indexed snapshot; resolve reusable defaults with get_structure table_styles.";
        var roles=Array(Text());roles["description"]="Native Table.RowType for each current row, in row order; native named cell-style assignment is independent and no localized name alias is inferred.";
        return Object(("contents_token",Text()),("table_style_handle",style),("row_roles",roles),("scope",Text()),("cells",Array(cell)),("actions",Array(Text())),("indices",Text()),("preservation",Text()));
    }
}
