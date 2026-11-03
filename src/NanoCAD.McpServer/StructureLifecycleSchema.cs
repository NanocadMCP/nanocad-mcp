using System.Text.Json.Nodes;
using NanoCAD.Contracts;

namespace NanoCAD.McpServer;

internal static class StructureLifecycleSchema
{
    private static JsonObject Text()=>new(){["type"]="string",["minLength"]=1,["maxLength"]=255};
    private static JsonObject Handle()=>new(){["type"]="string",["pattern"]="^[0-9A-Fa-f]{1,16}$"};
    internal static IEnumerable<JsonObject> Operations()
    {
        foreach(var operation in StructureLifecycleValidation.Operations)
        {
            bool current=operation.StartsWith("set_current_",StringComparison.Ordinal),rename=operation.StartsWith("rename_",StringComparison.Ordinal);
            var fields=new JsonObject{["operation"]=new JsonObject{["type"]="string",["const"]=operation},["handle"]=Handle(),["expected_name"]=Text()};
            if(current)fields["expected_current_handle"]=Handle();
            if(rename)fields["name"]=Text();
            string description="One existing ordinary local native record per request, from Model or Paper. Read get_structure and supply its handle/exact expected_name. No implicit entity reassignment, sheet-content deletion or save. This edits native DWG records, not MultiCAD model styles. ";
            description+=current?
                "Read the matching style section's is_current flag and provide that handle as expected_current_handle. Select this style as the document default; existing entity style references and per-entity overrides are unchanged. "+
                (operation=="set_current_dimension_style"?"Native selected style dimension defaults are copied into document DIM variables; this intentionally replaces current document-level DIM overrides. Defaults must retain valid local references; no child-style family lifecycle or per-object override removal is supplied. Undo/rollback is not guaranteed for this document-default change.":"An ordinary text style is selected by native Database.Textstyle; shape-file records are excluded from current text selection."):
                rename?"Rename the native record while retaining its handle, stored style metadata, current-style identity and existing style references. Destination must be unused ignoring case; no case-only/no-op rename, fields or persistent dependencies. In-use records may be renamed by stable native identity; no child-family lifecycle or MultiCAD style cache update is claimed.":
                operation=="delete_layout"?"Delete an inactive empty native Paper layout through LayoutManager.DeleteLayout. Model/current layout and the last Paper layout are protected. Only its optional reserved native viewport1 may exist; any sheet entity/extra viewport blocks deletion. Other sheet identities/content and relative tab order remain; native tab numbers may close the gap. This operation has no guaranteed Undo/rollback and runs outside a selector transaction.":
                "Delete only this unused native symbol-table record after Database.Purge confirms eligibility. Current records, native LayerZero, dependent records, fields and persistent dependencies are rejected. Native protected/in-use records that Purge does not admit return STRUCTURE_IN_USE; no guessed Standard/Defpoints name ban. Their entities/definitions are never deleted or reassigned.";
            yield return new JsonObject{["type"]="object",["additionalProperties"]=false,["properties"]=fields,
                ["required"]=new JsonArray(fields.Select(field=>(JsonNode?)JsonValue.Create(field.Key)).ToArray()),["description"]=description};
        }
    }
}
