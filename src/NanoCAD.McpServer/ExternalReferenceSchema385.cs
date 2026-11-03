using System.Text.Json.Nodes;
using NanoCAD.Contracts;

namespace NanoCAD.McpServer;

internal static class ExternalReferenceSchema385
{
    static JsonObject Text(int? maximum=null)=>maximum.HasValue?new(){["type"]="string",["maxLength"]=maximum.Value}:new(){["type"]="string"};
    static JsonObject Number(bool positive=false)=>positive?new(){["type"]="number",["exclusiveMinimum"]=0}:new(){["type"]="number"};
    static JsonObject Point(int count=3)=>new(){["type"]="array",["items"]=Number(),["minItems"]=count,["maxItems"]=count};
    static JsonObject Object(params (string Key,JsonNode Value)[] properties)=>new(){["type"]="object",["additionalProperties"]=false,["properties"]=new JsonObject(properties.Select(p=>KeyValuePair.Create<string,JsonNode?>(p.Key,p.Value))),["required"]=new JsonArray(properties.Select(p=>(JsonNode?)JsonValue.Create(p.Key)).ToArray())};
    static JsonObject Nullable(JsonNode value)=>new(){["anyOf"]=new JsonArray(value,new JsonObject{["type"]="null"})};
    static JsonObject Enum(params string[] values)=>new(){["type"]="string",["enum"]=new JsonArray(values.Select(v=>(JsonNode?)JsonValue.Create(v)).ToArray())};
    static JsonObject Token()=>new(){["type"]="string",["pattern"]="^[0-9A-Fa-f]{64}$",["description"]="Exact fresh readback token for the native entity or definition named by handle. Definition lifecycle may affect all disclosed occurrence refs."};
    static JsonObject Handle()=>new(){["type"]="string",["pattern"]="^[0-9A-Fa-f]{1,16}$"};
    static JsonObject Input(string operation,params (string Key,JsonNode Value)[] properties)
    {
        var shape=Object(new[]{("operation",(JsonNode)new JsonObject{["const"]=operation})}.Concat(properties).ToArray());return shape;
    }
    static void Optional(JsonObject shape,params string[] names)
    {var required=shape["required"]!.AsArray();foreach(string name in names){var node=required.FirstOrDefault(n=>n?.GetValue<string>()==name);if(node!=null)required.Remove(node);}}
    static JsonObject Name()=>new(){["type"]="string",["minLength"]=1,["maxLength"]=255,["description"]="Unique ordinary native definition name; existing definitions are never replaced."};
    static JsonObject Path()=>new(){["type"]="string",["minLength"]=4,["maxLength"]=240,["description"]="Existing absolute local drive file path, not UNC/device/relative/ADS. Source files are opened read-only and never modified or removed. Native format loader must be installed."};
    public static IEnumerable<JsonNode> Operations()
    {
        var xref=Input("attach_xref",("source_path",Path()),("name",Name()),("position",Point(2)),("layer",Text(255)),("rotation",Number()),("scale",Number(true)),("overlay",new JsonObject{["type"]="boolean"}));
        Optional(xref,"rotation","scale","overlay");xref["description"]="Native DWG xref definition plus one ordinary top-level insertion. Host placement is WCS XY/Z0 in millimeters; radians rotation, explicit positive native ScaleFactors (default1). Retain the source drawing's native units; choose scale for the intended physical reference size. overlay defaults false. No bind/explode/source copying or source rewrite.";yield return xref;
        var raster=Input("insert_raster_image",("source_path",Path()),("name",Name()),("position",Point(2)),("layer",Text(255)),("rotation",Number()),("width",Number(true)),("height",Number(true)));
        Optional(raster,"rotation");raster["description"]="Native RasterImageDef/linked RasterImage. position is lower-left image origin; width/height are explicit physical millimeters, independent of file DPI. Both dimensions are intentional; choose their ratio to preserve or change the source aspect ratio. Native loader validates the existing image.";yield return raster;
        var underlay=Input("attach_underlay",("format",Enum("dwf","dgn","pdf")),("source_path",Path()),("item_name",Text(255)),("name",Name()),("position",Point(2)),("layer",Text(255)),("rotation",Number()),("scale",Number(true)));
        underlay["properties"]!["item_name"]!["minLength"]=1;underlay["properties"]!["item_name"]!["description"]="Exact native sheet/page/model item name from the source; no guessed default page/model. Password-protected items are outside this route.";
        Optional(underlay,"rotation","scale");underlay["description"]="Native DWF/DGN/PDF input underlay; SourceFileName/ItemName/Load then a linked native reference. Input is separate from excluded printing/PDF export. Loading support depends on the installed native format host; unavailable items fail before insertion.";yield return underlay;
        foreach(string kind in new[]{"xref","raster_image","dwf","dgn","pdf"})
        {
            var properties=kind=="raster_image"?Object(("position",Point(2)),("rotation",Number()),("width",Number(true)),("height",Number(true))):Object(("position",Point(2)),("rotation",Number()),("scale",Number(true)));
            Optional(properties,kind=="raster_image"?["position","rotation","width","height"]:["position","rotation","scale"]);properties["minProperties"]=1;
            var edit=Input("set_reference_placement",("reference_kind",new JsonObject{["const"]=kind}),("handle",Handle()),("reference_token",Token()),("properties",properties));
            edit["description"]="Edit one current top-level native reference with its entity token. Retain source/definition links, stored display/clipping and user data; WCS XY/Z0. Sparse properties preserve unspecified placement. No source file rewrite.";yield return edit;
        }
        var imageDisplay=Object(("brightness",new JsonObject{["type"]="integer",["minimum"]=0,["maximum"]=100}),
            ("contrast",new JsonObject{["type"]="integer",["minimum"]=0,["maximum"]=100}),
            ("fade",new JsonObject{["type"]="integer",["minimum"]=0,["maximum"]=100}),
            ("show_image",new JsonObject{["type"]="boolean"}),("image_transparency",new JsonObject{["type"]="boolean"}),
            ("show_unaligned",new JsonObject{["type"]="boolean"}));
        Optional(imageDisplay,"brightness","contrast","fade","show_image","image_transparency","show_unaligned");imageDisplay["minProperties"]=1;
        var adjustImage=Input("set_raster_image_display",("reference_kind",new JsonObject{["const"]="raster_image"}),("handle",Handle()),("reference_token",Token()),("properties",imageDisplay));
        adjustImage["description"]="Adjust one existing native raster occurrence for readable engineering tracing: native brightness/contrast/fade integers0..100, image visibility/transparency and display when unaligned with the current view. Sparse fields retain other settings. Read fresh occurrence reference_token from inspect_objects. Retain source/definition, physical placement, clipping, other occurrences and user data; no source rewrite, image processing or implicit save. This changes native display, not pixels on disk.";yield return adjustImage;
        var clip=Input("set_raster_image_clip",("reference_kind",new JsonObject{["const"]="raster_image"}),("handle",Handle()),("reference_token",Token()),("mode",Enum("polygon","clear")),("boundary",new JsonObject{["type"]="array",["items"]=Point(),["minItems"]=3}));
        Optional(clip,"boundary");clip["allOf"]=new JsonArray(new JsonObject{["if"]=new JsonObject{["properties"]=new JsonObject{["mode"]=new JsonObject{["const"]="polygon"}}},["then"]=new JsonObject{["required"]=new JsonArray("boundary")},["else"]=new JsonObject{["not"]=new JsonObject{["required"]=new JsonArray("boundary")}}});
        clip["description"]="Crop one current native raster occurrence with mode=polygon and ordered WCS XYZ vertices in drawing millimeters on its actual image plane, or use mode=clear to display the whole image. Read fresh entity reference_token and placement.clip_editing.boundary_world. The native pixel-to-model transform maps the boundary; source pixels/file, physical frame, definition and other occurrences stay retained. One simple polygon, no holes. Native private inversion state is retained where present; review the actual visible crop. No source rewrite or implicit save.";yield return clip;
        foreach(string operation in new[]{"reload_xref","detach_xref"})
        {
            var edit=Input(operation,("reference_kind",new JsonObject{["const"]="xref"}),("handle",Handle()),("reference_token",Token()));
            edit["description"]=operation=="detach_xref"?"Definition handle/token from get_structure blocks or an insertion's geometry.definition. Native detach removes this direct xref definition and ALL disclosed top-level occurrences in Model/Paper. It does not delete the source file. Nested/custom occurrence ownership is outside this lifecycle.":"Reload the direct xref DEFINITION from its existing source file; all disclosed occurrences retain placement/display/user data. Does not reattach, bind or modify the source.";yield return edit;
        }
        var reload=Input("reload_reference",("reference_kind",Enum("raster_image","dwf","dgn","pdf")),("handle",Handle()),("reference_token",Token()));reload["description"]="Reload the native image/underlay DEFINITION with its definition token, affecting every disclosed linked occurrence. Source path/item and placements remain. No source write or implicit save.";yield return reload;
        var delete=Input("delete_reference",("reference_kind",Enum("xref","raster_image","dwf","dgn","pdf")),("handle",Handle()),("reference_token",Token()));delete["description"]="Erase exactly one disclosed top-level reference ENTITY using its entity token. Retain the definition, source files and other occurrences; no purge. Use detach_xref to remove an entire xref definition.";yield return delete;
    }
    static JsonObject Definition(JsonObject reference)=>Object(("definition_ref",reference.DeepClone()),("native_type",Text()),("reference_kind",Enum("xref","raster_image","dwf","dgn","pdf")),("name",Text()),("source_path",Text()),("item_name",Nullable(Text())),("overlay",new JsonObject{["type"]="boolean"}),("reference_refs",new JsonObject{["type"]="array",["items"]=reference.DeepClone()}),("loaded",Nullable(new JsonObject{["type"]="boolean"})),("image_pixel_size",Nullable(Point(2))),("status",Nullable(Text())),("reference_token",Nullable(Token())),("editing_unavailable_reason",Nullable(Text())));
    public static JsonObject DefinitionRead(JsonObject reference)=>Definition(reference);
    public static JsonObject Read(JsonObject reference)
    {
        var block=Object(("position",Point()),("rotation",Number()),("scale_factors",Point()),("normal",Point()));
        var image=Object(("position",Point()),("x_axis",Point()),("y_axis",Point()),("show_image",new JsonObject{["type"]="boolean"}),("is_clipped",new JsonObject{["type"]="boolean"}));
        var imageProps=image["properties"]!.AsObject();
        foreach(string name in new[]{"brightness","contrast","fade","display_options_raw"})imageProps[name]=new JsonObject{["type"]="integer"};
        foreach(string name in new[]{"image_transparency","show_unaligned"})imageProps[name]=new JsonObject{["type"]="boolean"};
        imageProps["clip_boundary_type"]=Text();imageProps["clip_boundary"]=new JsonObject{["type"]="array",["items"]=Point(2),["description"]="Unmodified native image clip coordinates; this read does not reinterpret them as drawing millimeters."};
        imageProps["clip_editing"]=Object(("enabled",new JsonObject{["type"]="boolean"}),("boundary_world",Nullable(new JsonObject{["type"]="array",["items"]=Point()})),("unavailable_reason",Nullable(Text())));
        image["required"]=new JsonArray(imageProps.Select(p=>(JsonNode?)JsonValue.Create(p.Key)).ToArray());
        var underlay=(JsonObject)block.DeepClone();var props=underlay["properties"]!.AsObject();
        props["is_on"]=new JsonObject{["type"]="boolean"};props["is_clipped"]=new JsonObject{["type"]="boolean"};props["clip_boundary"]=new JsonObject{["type"]="array",["items"]=Point(2)};props["fade"]=new JsonObject{["type"]="integer"};props["contrast"]=new JsonObject{["type"]="integer"};props["monochrome"]=new JsonObject{["type"]="boolean"};props["adjust_color_for_background"]=new JsonObject{["type"]="boolean"};
        underlay["required"]=new JsonArray(props.Select(p=>(JsonNode?)JsonValue.Create(p.Key)).ToArray());
        return Object(("reference_kind",Enum("xref","raster_image","dwf","dgn","pdf")),("native_type",Text()),("owner_ref",reference.DeepClone()),("definition_ref",reference.DeepClone()),("placement",new JsonObject{["oneOf"]=new JsonArray(block,image,underlay)}),("definition",Definition(reference)),("reference_token",Nullable(Token())),("editing_unavailable_reason",Nullable(Text())));
    }
}
