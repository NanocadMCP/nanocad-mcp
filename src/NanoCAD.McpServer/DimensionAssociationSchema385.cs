using System.Text.Json.Nodes;
using NanoCAD.Contracts;

namespace NanoCAD.McpServer;

internal static class DimensionAssociationSchema385
{
    const string CurrentLineEndpointScope=" An aligned dimension can retain native start/end associations to a whole ordinary Line in WCS XY/Z0 when set_curve_geometry changes the source endpoints. Reuse the actual source/dimension identities and inspect the retained association and measurement after the connected edit. This is a source-to-annotation dependency, not a dimension-to-source driver or a projected/nested/trans-spatial association.";
    static JsonObject Text()=>new(){["type"]="string"};
    static JsonObject Bool()=>new(){["type"]="boolean"};
    static JsonObject Integer()=>new(){["type"]="integer"};
    static JsonObject Handle()=>new(){["type"]="string",["pattern"]="^[0-9A-Fa-f]{1,16}$"};
    static JsonObject Token()=>new(){["type"]="string",["pattern"]="^[0-9A-Fa-f]{64}$"};
    static JsonObject Array(JsonNode item)=>new(){["type"]="array",["items"]=item};
    static JsonObject Nullable(JsonNode item)=>new(){["anyOf"]=new JsonArray(item,new JsonObject{["type"]="null"})};
    static JsonObject Choice(IEnumerable<string> values)=>new(){["type"]="string",["enum"]=new JsonArray(values.Select(value=>(JsonNode?)JsonValue.Create(value)).ToArray())};
    static JsonObject Object(params(string Name,JsonNode Value)[] fields)
    {
        var properties=new JsonObject();var required=new JsonArray();foreach(var field in fields){properties[field.Name]=field.Value;required.Add(field.Name);}
        return new(){["type"]="object",["additionalProperties"]=false,["properties"]=properties,["required"]=required};
    }
    static JsonObject Point()=>new(){["type"]="array",["minItems"]=3,["maxItems"]=3,["items"]=new JsonObject{["type"]="number"}};
    internal static JsonObject Bindings(string? kind=null)
    {
        var roles=kind switch{"aligned" or "rotated"=>new[]{"xline1","xline2"},"radial"=>new[]{"center","chord"},"diametric"=>new[]{"chord","far_chord"},_=>DimensionAssociationValidation385.Roles};
        var result=Array(Object(("role",Choice(roles)),("source_handle",Handle()),("snap",Choice(DimensionAssociationValidation385.Snaps))));result["minItems"]=2;result["maxItems"]=2;
        result["description"]="The two actual defining roles, each once. Sources are committed ordinary local Line/Circle/Arc/open Polyline in the same drawing space, WCS XY/Z0. start/end select whole-curve ends; midpoint is a whole Line midpoint; center is Circle/Arc; quadrants select a whole Circle. Radial uses center+curve point of one Circle/Arc; diametric uses opposite Circle quadrants. Coordinates must already match the selected native snaps. No viewport/projected DrawingView alias, nested/Xref path, inferred Polyline segment marker or coordinate-only association.";
        if(kind is null or "aligned")result["description"]=result["description"]!.GetValue<string>()+CurrentLineEndpointScope;
        return result;
    }
    internal static JsonArray Operations()
    {
        var result=new JsonArray();foreach(string action in DimensionAssociationValidation385.Actions)
        {
            var handles=Array(Handle());handles["minItems"]=1;handles["maxItems"]=1;
            var fields=new List<(string,JsonNode)>{("operation",new JsonObject{["const"]=action}),("handles",handles),("association_token",Token())};
            if(action=="associate_dimension")fields.Add(("bindings",Bindings()));var operation=Object(fields.ToArray());
            operation["description"]=action=="associate_dimension"?
                "Bind one existing aligned/rotated/radial/diametric native dimension to its actual local source snaps using DimAssoc/OsnapPointRef. Read geometry.association and supply its fresh non-null association_token. Defining points must already match sources; no manual coordinate substitute is created. Existing active or retained inactive association storage is not overwritten. For a new dimension, the same optional associations array is available on its basic create_entities branch.":
                action=="update_dimension_association"?
                "Update an existing supported active native dimension association from its current retained source ObjectIds/snaps. Read a fresh association_token after source changes. Native measured endpoints and generated dimension placement may follow source geometry; sources, style, literal text and unrelated owned data remain. This does not drive source geometry.":
                "Detach the existing supported active native dimension association with RemoveAssociativity. Actual source geometry, current measured definition, style/text and unrelated owned data remain. Native inactive association storage may remain observed; no manual purge or source erasure is performed.";
            operation["description"]=operation["description"]!.GetValue<string>()+" Use a dedicated batch with distinct dimension targets. Field-bearing/annotative/trans-spatial/special-snap/projected or unclassified owned paths have an explicit unavailable reason. Undo/rollback is uncertain; pending/unknown never means success or rollback."+(action=="associate_dimension"?CurrentLineEndpointScope:"");result.Add(operation);
        }
        return result;
    }
    internal static JsonObject Read()
    {
        var point=Object(("role",Text()),("source_handle",Handle()),("osnap_type",Choice(new[]{"ModeStartpoint","ModeEnd","ModeMid","ModeCenter","ModeQuad"})),("point",Point()),("evaluated_point",Point()),("near_point_parameter",new JsonObject{["type"]="number"}));
        var noAssociation=Object(("association_handle",new JsonObject{["type"]="null"}));
        var native=Object(("association_handle",Handle()),("owner_handle",Nullable(Handle())),("dimension_handle",Handle()),("flags",Integer()),("trans_spatial",Bool()),("rotated_type",Text()),("points",Array(point)));
        var read=Object(("association_token",Nullable(Token())),("association_handle",Nullable(Handle())),("active",Nullable(Bool())),
            ("points",Nullable(new JsonObject{["oneOf"]=new JsonArray(noAssociation,native)})),("source_handles",Nullable(Array(Handle()))),("editing_unavailable_reason",Nullable(Text())),("scope",Text()));
        read["description"]="Stored native DimAssoc flags/whole source paths and Osnap picks, plus current native evaluated points; read performs no dimension update/regeneration. A fresh token admits the supported local association scope. Resident native AssocManager anchors and their readable owned records are observed by actual identity/ownership; the SDK exposes no complete private manager inventory. Inactive native storage is distinct from an active binding; null token/reason does not hide the independently readable stored dimension geometry.";return read;
    }
    internal static JsonObject Change(JsonNode reference)=>Object(("operation_index",Integer()),("dimension_ref",reference),("association_token",Token()),("association_handle",Nullable(Handle())),("source_handles",Array(Handle())),("warnings",Array(Text())));
}
