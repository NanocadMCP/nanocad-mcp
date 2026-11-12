using System.Text.Json.Nodes;

namespace NanoCAD.McpServer;

internal static class LibraryBlockDwgSchema
{
    internal static IEnumerable<JsonObject> Operations()
    {
        JsonObject Token() => new() { ["type"] = "string", ["pattern"] = "^[0-9A-Fa-f]{64}$", ["minLength"] = 64, ["maxLength"] = 64 };
        foreach (string operation in new[] { "import_block_dwg", "redefine_block_from_dwg" })
        {
            bool import = operation == "import_block_dwg";
            var properties = new JsonObject
            {
                ["operation"] = new JsonObject { ["type"] = "string", ["const"] = operation },
                ["path"] = new JsonObject
                {
                    ["type"] = "string",
                    ["minLength"] = 4,
                    ["maxLength"] = 240,
                    ["description"] = "Absolute local drive path to a saved DWG. Network/device/relative paths and implicit unit conversion are outside this route."
                },
                ["expected_file_sha256"] = Token()
            };
            properties["expected_file_sha256"]!["description"] = "Optional expected SHA-256 of the source DWG bytes. Reject a different source file before cloning.";
            var required = new JsonArray("operation", "path");
            if (import)
            {
                properties["name"] = new JsonObject { ["type"] = "string", ["minLength"] = 1, ["maxLength"] = 255 };
                required.Add("name");
            }
            else
            {
                properties["definition_handle"] = new JsonObject { ["type"] = "string", ["pattern"] = "^[0-9A-Fa-f]{1,16}$" };
                properties["expected_definition_token"] = Token();
                properties["expected_definition_token"]!["description"] = "Current inspect_block.definition_membership.definition_token of the actual existing target definition and its consumers.";
                required.Add("definition_handle"); required.Add("expected_definition_token");
            }
            yield return new JsonObject
            {
                ["type"] = "object",
                ["additionalProperties"] = false,
                ["properties"] = properties,
                ["required"] = required,
                ["description"] = (import
                    ? "Import the saved DWG's supported ordinary static Model Space geometry as one new reusable local block definition; the name must not exist. The source INSBASE establishes the new block origin. "
                    : "Redefine the actual existing local static block from the saved DWG's supported Model Space geometry. Preserve the target definition ID and origin, its existing AttributeDefinition objects/defaults, all actual Model/Paper/nested insertions and their placement/owned attribute values. Replace only the target's non-attribute direct members; this is a shared change to every insertion. ") +
                    "Use source and target Millimeters without implicit conversion. Import can retain supported ordinary single-line root AttributeDefinitions; redefinition requires a source without root AttributeDefinitions and retains the target's existing attributes. Attribute synchronization is outside this route. Native cloning preserves supported static geometry and uses MangleName for incoming symbol conflicts rather than silently using or replacing unrelated destination records. " +
                    "Fields, constraints, dynamic/annotative/external/custom objects and persistent dependencies need their owning lifecycle; preflight retains those guards. Read the actual target and current token before redefining. One separate operation per request. The result includes the source file hash and actual native symbol mappings; source handles are scoped to that file/hash, not destination drawing references. Undo/rollback is not guaranteed. No implicit save."
            };
        }
    }
}
