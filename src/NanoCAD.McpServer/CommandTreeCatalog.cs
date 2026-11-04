using System.Text.Json;
using System.Text.Json.Nodes;

namespace NanoCAD.McpServer;

// Compact navigation only: canonical tool inputs remain in ToolCatalog.
internal static class CommandTreeCatalog
{
    internal static readonly string[] SectionNames = ["documents", "2d", "direct3d", "parametric", "documentation", "point_clouds", "viewing"];
    private static readonly string[] DirectKinds = ["extrusion", "boolean", "transform_copy", "drilled_copy", "fillet_copy", "chamfer_copy", "shell_copy", "loft", "revolve", "sweep", "box", "sphere", "cylinder", "frustum", "torus", "wedge", "pyramid"];
    private static readonly string[] DirectEdits = ["separate_solid", "slice_solid", "transform_solid", "fillet_solid", "chamfer_solid", "shell_solid", "drill_solid", "offset_faces", "presspull_faces", "extrude_faces", "extrude_faces_along_path", "transform_faces", "taper_faces", "remove_faces", "imprint_solid", "boolean_solid", "offset_solid", "replace_solid"];
    private static readonly string[] CommonEdits = ["move", "copy", "rotate", "scale", "mirror", "erase", "set_properties", "explode_native"];
    private static readonly string[] AnnotationKinds = ["Text", "MText", "aligned", "rotated", "radial", "diametric", "angular_3point", "angular_2line", "ordinate", "arc_length", "jogged_radial", "projected_aligned", "Tolerance", "MLeader", "Table"];
    private static bool Model(string name) => name == "rebuild_model" || name.Contains("model_", StringComparison.Ordinal);
    private static bool InternalSketch(string name) => name is "add_sketch_constraints" or "set_sketch_constraint_parameter" or "delete_sketch_constraints" or "set_model_sketch_visibility";
    private static bool RetainedDrawingModel(string name) => name is "extrude_drawing_circle" or "extrude_drawing_profile" or "revolve_drawing_profile";
    private static bool DrawingDriver(string name) => name.Contains("drawing_", StringComparison.Ordinal);
    private static bool Sheet(string name) => name.Contains("layout", StringComparison.Ordinal) || name.Contains("viewport", StringComparison.Ordinal) || name.Contains("drawing_view", StringComparison.Ordinal) || name.Contains("section_view", StringComparison.Ordinal);
    private static bool Annotation(string name) => name.Contains("text", StringComparison.Ordinal) || name.Contains("dimension", StringComparison.Ordinal) || name.Contains("table", StringComparison.Ordinal) || name.Contains("mleader", StringComparison.Ordinal) || name == "set_tolerance";

    internal static string Read(string? section)
    {
        if (section is not null) return Category(section).ToJsonString();
        return new JsonObject
        {
            ["sections"] = new JsonArray(SectionNames.Select(name =>
            {
                var category = Category(name);
                return (JsonNode?)new JsonObject
                {
                    ["section"] = name,
                    ["title"] = category["title"]!.DeepClone(),
                    ["purpose"] = category["purpose"]!.DeepClone(),
                    ["tools"] = Strings(category["routes"]!.AsArray().Select(route => route!["tool"]!.GetValue<string>()).Distinct())
                };
            }).ToArray()),
            ["next"] = "Read get_guidance(topic=commands, section=the needed section) for its routes. For exact inputs, use topic=schema or operation with the selected tool name without nanocad. prefix; create/modify branches accept an exact variant. Read only what the task needs."
        }.ToJsonString();
    }

    private static JsonObject Category(string section)
    {
        var routes = new JsonArray(); string title; string purpose;
        void Add(string tool) => routes.Add(Route(tool));
        void Branch(string tool, Func<string, bool> selected) => routes.Add(Route(tool, Branches(tool).Where(selected)));
        switch (section)
        {
            case "documents":
                title = "Documents and context"; purpose = "Connection, intended document, units, selection and current drawing organization.";
                foreach (var tool in new[] { "get_status", "get_operation_result", "get_documents", "get_document", "get_selection", "get_structure", "manage_document" }) Add(tool);
                break;
            case "2d":
                title = "2D drawing and structure"; purpose = "Curves, source constraints, dimensions and callouts, masks, native groups, blocks and arrays within their published scope.";
                foreach (var tool in new[] { "query_objects", "inspect_objects", "analyze_geometry", "inspect_block", "inspect_constraints", "get_structure" }) Add(tool);
                Branch("create_entities", name => !Model(name) && !DirectKinds.Contains(name));
                Branch("modify_entities", name => !Model(name) && !InternalSketch(name) && !RetainedDrawingModel(name) && !DirectEdits.Contains(name) && name != "transform_point_cloud");
                Branch("modify_structure", name => !Sheet(name));
                break;
            case "direct3d":
                title = "Direct 3D"; purpose = "Solid construction, boolean and supported face/solid finishing; inspect actual topology and result.";
                foreach (var tool in new[] { "inspect_objects", "analyze_geometry", "inspect_solid_topology" }) Add(tool);
                Branch("create_entities", name => DirectKinds.Contains(name));
                Branch("modify_entities", name => DirectEdits.Contains(name) || CommonEdits.Contains(name));
                break;
            case "parametric":
                title = "Parameters and retained part features"; purpose = "Actual model history, parameters, drawing constraints, supported profiles and dependent features. Catalog routes do not promise every native combination.";
                foreach (var tool in new[] { "inspect_model_history", "get_model_variables", "inspect_sketch_constraints", "inspect_constraints", "inspect_solid_topology", "set_model_parameter" }) Add(tool);
                Branch("create_entities", Model);
                Branch("modify_entities", name => Model(name) || InternalSketch(name) || DrawingDriver(name));
                break;
            case "documentation":
                title = "Editable DWG documentation"; purpose = "Drawing views, sections, sheets, viewports and readable native annotations.";
                Add("get_drawing_views"); Add("inspect_objects");
                Branch("create_entities", name => AnnotationKinds.Contains(name));
                Branch("modify_entities", Annotation);
                Branch("modify_structure", name => Sheet(name) || Annotation(name));
                break;
            case "point_clouds":
                title = "PointCloud"; purpose = "Read cloud metadata, stored features, raw points, sections, statistics and actual indexed measurements. Cloud data remains unchanged.";
                var inspect = Route("inspect_point_cloud"); inspect["modes"] = Modes("inspect_point_cloud"); routes.Add(inspect);
                var analysis = Route("analyze_point_cloud"); analysis["modes"] = Modes("analyze_point_cloud");
                var input = ToolCatalog.InputContract("analyze_point_cloud");
                analysis["measurement_kinds"] = Strings(input.GetProperty("properties").GetProperty("measurement").GetProperty("oneOf").EnumerateArray()
                    .Select(branch => branch.GetProperty("properties").GetProperty("kind").GetProperty("const").GetString()!));
                routes.Add(analysis);
                Branch("modify_entities", name => name is "transform_point_cloud" or "move" or "rotate");
                break;
            case "viewing":
                title = "Viewing and visual checks"; purpose = "Frame the requested result and inspect a meaningful image; display changes do not edit geometry.";
                Add("set_view"); Add("capture_view");
                break;
            default: throw new InvalidOperationException("Unknown command-tree section.");
        }
        return new JsonObject
        {
            ["section"] = section,
            ["title"] = title,
            ["purpose"] = purpose,
            ["routes"] = routes,
            ["help"] = section == "point_clouds" ? "get_guidance(topic=point_clouds) explains loaded sources, units, WCS/slab frames and cursor completion. Request exact tool inputs with topic=schema."
                : "Use get_guidance(topic=operation, operation=the tool name without nanocad. prefix, variant=the needed branch) for focused operating limits and exact input fields. Tools without branches omit variant."
        };
    }

    private static JsonObject Route(string tool, IEnumerable<string>? variants = null)
    {
        if (!ToolCatalog.OperationNames.Contains(tool, StringComparer.Ordinal)) throw new InvalidOperationException("Command-tree route is not a published tool.");
        var route = new JsonObject { ["tool"] = "nanocad." + tool };
        if (variants is not null) route["variants"] = Strings(variants);
        return route;
    }
    private static IEnumerable<string> Branches(string tool)
    {
        string field = tool == "create_entities" ? "entities" : "operations";
        return ToolCatalog.InputContract(tool).GetProperty("properties").GetProperty(field).GetProperty("items").GetProperty("oneOf").EnumerateArray()
            .Select(branch =>
            {
                var props = branch.GetProperty("properties");
                string key = tool != "create_entities" ? "operation" : props.TryGetProperty("kind", out _) ? "kind" : "type";
                return props.GetProperty(key).GetProperty("const").GetString()!;
            }).Distinct();
    }
    private static JsonArray Modes(string tool) => Strings(ToolCatalog.InputContract(tool).GetProperty("properties").GetProperty("mode").GetProperty("enum").EnumerateArray().Select(item => item.GetString()!));
    private static JsonArray Strings(IEnumerable<string> values) => new(values.Select(value => (JsonNode?)JsonValue.Create(value)).ToArray());
}
