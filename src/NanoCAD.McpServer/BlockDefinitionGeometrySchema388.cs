using System.Text.Json;
using System.Text.Json.Nodes;

namespace NanoCAD.McpServer;

internal static class BlockDefinitionGeometrySchema388
{
    static JsonObject Text()=>new(){["type"]="string"};
    static JsonObject Handle()=>new(){["type"]="string",["pattern"]="^[0-9A-Fa-f]{1,16}$",["minLength"]=1,["maxLength"]=16};
    static JsonObject Point()=>new(){["type"]="array",["minItems"]=3,["maxItems"]=3,
        ["prefixItems"]=new JsonArray(new JsonObject{["type"]="number",["minimum"]=-1e9,["maximum"]=1e9},
            new JsonObject{["type"]="number",["minimum"]=-1e9,["maximum"]=1e9},new JsonObject{["type"]="number",["minimum"]=-1e-9,["maximum"]=1e-9}),
        ["items"]=false,["description"]="Definition-local XY coordinates [x,y,0], before every insertion transform; third coordinate is zero within native 1e-9 tolerance."};
    static JsonObject Object(params(string Key,JsonObject Value)[] fields)
    {var properties=new JsonObject();var required=new JsonArray();foreach(var f in fields){properties[f.Key]=f.Value;required.Add(f.Key);}return new(){["type"]="object",["additionalProperties"]=false,["properties"]=properties,["required"]=required};}
    static JsonObject Number(double low,double high)=>new(){["type"]="number",["minimum"]=low,["maximum"]=high};
    static JsonObject Geometry(bool reading=false)
    {
        var vertices=new JsonObject{["type"]="array",["minItems"]=2,["items"]=Object(("position",Point()),("bulge",Number(-1e6,1e6)),("start_width",Number(0,1e6)),("end_width",Number(0,1e6)))};
        var variants=new JsonArray(
            Object(("type",new JsonObject{["const"]="Line"}),("start",Point()),("end",Point())),
            Object(("type",new JsonObject{["const"]="Circle"}),("center",Point()),("radius",Number(1e-9,1e9))),
            Object(("type",new JsonObject{["const"]="Arc"}),("center",Point()),("radius",Number(1e-9,1e9)),("start_angle",Number(-2*Math.PI,2*Math.PI)),("sweep_angle",Number(1e-9,2*Math.PI-1e-9))),
            Object(("type",new JsonObject{["const"]="Polyline"}),("closed",new JsonObject{["type"]="boolean"}),("vertices",vertices)),
            Object(("type",new JsonObject{["const"]="Ellipse"}),("center",Point()),("major_axis",Point()),("radius_ratio",Number(.01,1)),("start_parameter",Number(0,2*Math.PI)),("sweep_parameter",Number(1e-6,2*Math.PI))),
            Object(("type",new JsonObject{["const"]="Spline"}),("degree",new JsonObject{["type"]="integer",["minimum"]=1,["maximum"]=5}),
                ("closed",new JsonObject{["type"]="boolean"}),("control_points",new JsonObject{["type"]="array",["minItems"]=2,["items"]=Point()}),
                ("knots",new JsonObject{["type"]="array",["minItems"]=4,["items"]=Number(0,1),["description"]="Complete clamped knot vector, control count+degree+1; endpoints repeated degree+1 times at 0 and 1. Internal multiplicity <= degree. Distinct knots separated by at least 1e-6."}),
                ("weights",new JsonObject{["type"]="array",["items"]=Number(.01,100),["description"]="Empty for nonrational, otherwise one weight per control. Closed clamped curves repeat their first control at the end."})));
        if(reading)
        {
            // Imported source NURBS may exceed the replacement creator grammar.
            void Relax(JsonNode? node)
            {
                if(node is JsonObject obj){obj.Remove("minimum");obj.Remove("maximum");foreach(var value in obj.Select(p=>p.Value).ToArray())Relax(value);}
                else if(node is JsonArray array)foreach(var child in array)Relax(child);
            }
            Relax(variants);
        }
        return new(){["oneOf"]=variants};
    }
    internal static JsonElement Input()
    {
        var result=Object(("operation",new JsonObject{["type"]="string",["const"]="set_block_definition_geometry"}),
            ("definition_handle",Handle()),("expected_definition_token",new JsonObject{["type"]="string",["pattern"]="^[0-9A-F]{64}$"}),
            ("members",new JsonObject{["type"]="array",["minItems"]=1,["items"]=Object(("handle",Handle()),("geometry",Geometry()))}));
        result["description"]="Edit existing direct native Line/Circle/Arc/Polyline/Ellipse/Spline members of one ordinary local static shared block definition. Read inspect_block by definition handle or an insertion: definition_editing supplies the current definition_token and member IDs. Coordinates are local XY [x,y,0] parent_definition coordinates, before every insertion transform, in a Millimeters drawing and a Unitless/Millimeters definition; preserve native +Z normal and zero thickness. Selected and related definitions are ordinary observed XY2D graphs: Line/Circle/Arc/Ellipse/Polyline/Spline, literal Text/MText/AttributeDefinition and local static nested BlockReference/MInsert. Native model/3D/custom/Field-bearing members in those actual definitions require their owning route; a ModelSpace-only scan does not establish nested independence. Changes affect all actual existing Model/Paper/nested insertions; their placement and literal attribute definitions/values remain. Complete geometry replacement preserves the exact native entity type and handle. Polyline vertices/bulges/widths and closed state are explicit; Spline replacement retains the established editable clamped source scope and uses complete clamped NURBS grammar, not a fit-data edit. Wider fit/periodic sources remain observable and retain their data as unselected members. Existing inspection can report an imported NURBS definition outside that replacement grammar. Native members, definition and attributes are never recreated, reassigned or synchronized. Use member handles from that exact definition, each once, and one operation in a separate request. Source curves with Fields, native constraints/model references or persistent associations need their owning edit route; dynamic/xref/foreign/annotative affected insertions and Field-bearing attributes are protected. Selected member and affected insertion layers must be writable for native graphics notification. Result changes.children lists edited members and actual insertions marked for redraw; refresh inspect_block. Cached bounding boxes/private evaluated graphics are not a geometry oracle, and unselected owned private payloads are not exhaustively observed. Inspect one useful shared-symbol view. Undo/rollback is not guaranteed.";
        return JsonSerializer.SerializeToElement(result);
    }
    internal static JsonObject Read(JsonObject reference)
    {
        JsonObject Ref()=>JsonNode.Parse(reference.ToJsonString())!.AsObject();
        JsonObject Nullable(JsonObject value)=>new(){["anyOf"]=new JsonArray(value,new JsonObject{["type"]="null"})};
        var geometry=Geometry(reading:true);
        var member=Object(("ref",Ref()),("geometry",geometry),("eligible",new JsonObject{["type"]="boolean"}),("unavailable_reason",Nullable(Text())));
        return Object(("definition_ref",Ref()),("definition_name",Text()),("coordinate_system",new JsonObject{["const"]="parent_definition"}),
            ("definition_token",Nullable(new JsonObject{["type"]="string",["pattern"]="^[0-9A-F]{64}$"})),("can_edit",new JsonObject{["type"]="boolean"}),
            ("unavailable_reason",Nullable(Text())),("members",new JsonObject{["type"]="array",["items"]=member}),
            ("referencing_insertions",new JsonObject{["type"]="array",["items"]=Ref()}));
    }
}
