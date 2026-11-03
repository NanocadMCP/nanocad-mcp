using System.Text.Json;
using System.Text.Json.Nodes;
using NanoCAD.Contracts;

namespace NanoCAD.McpServer;

internal static class ModifySchema
{
    private static JsonObject Text(int max) => new() { ["type"] = "string", ["minLength"] = 1, ["maxLength"] = max };
    private static JsonObject Number(double min, double max) => new() { ["type"] = "number", ["minimum"] = min, ["maximum"] = max };
    private static JsonObject Point() => new() { ["type"] = "array", ["minItems"] = 2, ["maxItems"] = 2, ["items"] = Number(-1e9, 1e9) };
    private static JsonObject Object(JsonObject properties, params string[] required) => new()
    { ["type"] = "object", ["additionalProperties"] = false, ["properties"] = properties, ["required"] = new JsonArray(required.Select(s => (JsonNode?)JsonValue.Create(s)).ToArray()) };
    private static JsonObject Operation(string name, params (string Name, JsonObject Schema)[] fields)
    {
        var handle = Text(16); handle["pattern"] = "^[0-9A-Fa-f]{1,16}$";
        var properties = new JsonObject
        {
            ["operation"] = new JsonObject { ["type"] = "string", ["const"] = name },
            ["handles"] = new JsonObject { ["type"] = "array", ["minItems"] = 1, ["uniqueItems"] = true, ["items"] = handle }
        };
        foreach (var (field, schema) in fields) properties[field] = schema;
        return Object(properties, new[] { "operation", "handles" }.Concat(fields.Select(f => f.Name)).ToArray());
    }
    public static JsonElement Input()
    {
        JsonObject Id() { var id = Text(36); id["format"] = "uuid"; return id; }
        var revision = Text(20); revision["pattern"] = "^[0-9]{1,20}$";
        var properties = Object(new JsonObject
        {
            ["layer"] = Text(255),
            ["color_aci"] = new JsonObject { ["type"] = "integer", ["minimum"] = 0, ["maximum"] = 256, ["description"] = "0=ByBlock, 1-255=ACI color, 256=ByLayer." },
            ["linetype"] = Text(255),
            ["linetype_scale"] = new JsonObject { ["type"] = "number", ["exclusiveMinimum"] = 0, ["description"] = "Positive per-object dash/pattern scale; does not change geometry or the drawing-wide linetype scale." },
            ["visible"] = new JsonObject { ["type"] = "boolean", ["description"] = "Stored visibility of this entity, independent of layer visibility." },
            ["lineweight_hundredth_mm"] = new JsonObject { ["type"] = "integer", ["enum"] = new JsonArray(ModifyValidation.Lineweights.Select(w => (JsonNode?)JsonValue.Create(w)).ToArray()), ["description"] = "-1=ByLayer, -2=ByBlock, -3=default; nonnegative values in hundredths of a millimeter." }
        });
        properties["minProperties"] = 1;
        AppearancePropertySchema.ExtendInput(properties);
        var expectedText=Text(16384);expectedText["minLength"]=0;
        var replacement=Operation("replace_text",("expected_text",expectedText),("text",Text(4096)));
        replacement["properties"]!["handles"]!["maxItems"]=1;
        replacement["description"]="Replace the literal of native TEXT while preserving its existing justification, meaningful alignment anchors, style and format. Native Align/Fit may adjust glyph sizing to retain its two anchors. No fields or annotative contexts.";
        JsonObject CellIndex(int maximum)=>new(){["type"]="integer",["minimum"]=0,["maximum"]=maximum};
        JsonObject CellText(){var text=Text(4096);text["minLength"]=0;return text;}
        var tableReplacement=Operation("replace_table_text",("cells",new JsonObject{
            ["type"]="array",["minItems"]=1,["items"]=Object(new JsonObject{
                ["row"]=CellIndex(int.MaxValue),["column"]=CellIndex(int.MaxValue),["expected_text"]=CellText(),["text"]=CellText()
            },"row","column","expected_text","text")}));
        tableReplacement["properties"]!["handles"]!["maxItems"]=1;
        JsonObject Paragraphs()=>new(){["type"]="array",["minItems"]=1,["items"]=new JsonObject{["type"]="string",["maxLength"]=2048}};
        var mtextReplacement=Operation("replace_mtext",("expected_paragraphs",Paragraphs()),("paragraphs",Paragraphs()));
        mtextReplacement["properties"]!["handles"]!["maxItems"]=1;
        mtextReplacement["description"]="Replace literal paragraphs of native MTEXT while retaining attachment, location, background and one uniform inline formatting run. Read existing literal paragraphs first; mixed formatting runs, fields and columns require a separate owning content route.";
        JsonObject DimensionText()=>new(){["type"]="string",["maxLength"]=256};
        var dimensionReplacement=Operation("replace_dimension_text",("expected_text",DimensionText()),("text",DimensionText()));
        dimensionReplacement["properties"]!["handles"]!["maxItems"]=1;
        dimensionReplacement["description"]="Plain dimension prefix/suffix preserving measurement: text must contain exactly one <> or be empty to restore the native value. One exact automatic-placement aligned/rotated/radial Dimension handle, or a manual diametric Dimension with existing effective DIMTMOVE=2, no persistent dependencies, fields, controls or CAD formatting. Measured endpoints/style/orientation and the dimension line remain unchanged; DimLinePoint may slide along that same line; automatic native text placement may adapt; manual diametric TextPosition and its existing movement policy remain unchanged. Expected text must match after preceding edits. only set_dimension_line may share an aligned/rotated target.";
        JsonObject Sizes(int max)=>new(){["type"]="array",["items"]=Object(new JsonObject{
            ["index"]=CellIndex(int.MaxValue),["expected_size"]=Number(0.001,1e6),["size"]=Number(0.001,1e6)
        },"index","expected_size","size")};
        var tableResize=Operation("resize_table",("rows",Sizes(64)),("columns",Sizes(32)));
        tableResize["properties"]!["handles"]!["maxItems"]=1;
        tableResize["anyOf"]=new JsonArray(new JsonObject{["properties"]=new JsonObject{["rows"]=new JsonObject{["minItems"]=1}}},new JsonObject{["properties"]=new JsonObject{["columns"]=new JsonObject{["minItems"]=1}}});
        tableResize["description"]="Set sparse row heights/column widths by zero-based index; both arrays required, at least one edit. expected_size must match after prior edits; sizes0.001..1000000mm. Exact native unmerged WCS XY/Z0 Table with ordinary String/Long/Double Value slots, including empty and multiple-content cells; no fields, blocks, formulas, links, persistent root dependencies or locked formatting. Preserve native scalar type/unit/raw bits/value/order/data format, cell Flow/stacking, style/appearance/overrides, placement, owned metadata and unedited sizes. An unoverridden automatic slot scale may reflow and is reported; explicit scale and auto mode stay. Indexed resizing uses actual resident snapshots/setters; a request containing an indexed Table must use resize_table operations only. Native clamping is an error. May combine with replace_table_text within its literal-cell scope; complete batch admission by actual serialized bytes. Independent native Table placement/copy uses move/rotate/scale/copy; cell-size editing has its own scope.";
        JsonObject DimensionPoint()=>new(){["type"]="array",["minItems"]=2,["maxItems"]=2,["items"]=Number(-1e6,1e6)};
        var dimensionTextPosition=Operation("set_dimension_text_position",("expected_text_position",DimensionPoint()),("text_position",DimensionPoint()),("movement_mode",new JsonObject{["type"]="integer",["const"]=2}));
        dimensionTextPosition["properties"]!["handles"]!["maxItems"]=1;
        dimensionTextPosition["description"]="Place the label of one plain aligned/rotated/radial/diametric dimension in WCS XY. Read current geometry.text_position and supply expected_text_position. Explicit movement_mode=2 sets that object's DIMTMOVE override and manual text placement; it does not change its shared style, measured geometry, measurement or text. No implicit leader is added by mode2. Choose readable positions from engineering context and inspect a group image; placement coordinates are not measurements. No fields or persistent dependencies; cannot share the same target with other dimension edit types in one batch. Existing replace_dimension_text policy is unchanged; modes0/1 remain deferred.";
        var dimensionLine=Operation("set_dimension_line",("expected_dimline_point",DimensionPoint()),("dimline_point",DimensionPoint()));
        dimensionLine["properties"]!["handles"]!["maxItems"]=1;
        dimensionLine["description"]="Place the dimension line of one native aligned/rotated Dimension in WCSXY/Z0. expected_dimline_point identifies the current line after preceding edits; points on the same line are equivalent within1e-7mm. New point specifies line offset, native layout may move its stored point along the same line. Coordinates +/-1e6mm; nonzero measurement and at least0.001mm perpendicular distance from the first endpoint. Preserve measured endpoints, rotation, text/style and measurement. Automatic text placement only, no fields/persistent dependencies. May combine with replace_dimension_text; Independent native annotation placement/copy uses move/rotate/scale/copy in a separate target batch.";
        JsonObject HatchAngle()=>new(){["type"]="number",["minimum"]=0,["exclusiveMaximum"]=2*Math.PI};
        var hatchPattern=Operation("set_hatch_pattern",("expected_angle",HatchAngle()),("expected_scale",Number(.01,100)),("angle",HatchAngle()),("scale",Number(.01,100)));
        hatchPattern["properties"]!["handles"]!["maxItems"]=1;
        hatchPattern["description"]="Legacy ANSI31-only angle/scale edit with narrow polygon family admission. Prefer set_hatch_properties for general native stored pattern edits, SOLID conversion, local associations and OCS plane preservation. This legacy contract remains compatible: angle radians0..2pi exclusive, scale0.01..100; exact expected values; non-associative normal-style WCSXY/Z0 simple straight-vertex outer loop and polygon/two-semicircle holes; no fields/reactors/pattern_double; complete topology/observation bytes and native nonempty fill are checked; a density estimate is not a geometry acceptance oracle.";
        var leaderExpected=Text(16384);leaderExpected["minLength"]=0;
        var leaderReplacement=Operation("replace_mleader_text",("expected_text",leaderExpected),("text",Text(4096)));
        leaderReplacement["properties"]!["handles"]!["maxItems"]=1;
        leaderReplacement["description"]="Replace single-line literal text of one native straight non-annotative MText-content MLeader with all actual branches/arrows retained; no branch-count ceiling. Native glyph layout can adjust text insertion while meaningful leader connections, style and vertices remain. No fields/formatting/columns. Complete batch/source observations are admitted by actual serialized bytes. Text edits cannot share their target with other edit types in this request; independent native move/copy/rotate/positive uniform scale uses a separate request.";
        JsonObject Point3() => new(){["type"]="array",["minItems"]=3,["maxItems"]=3,["items"]=Number(-1e6,1e6)};
        JsonObject SolidTransform(string kind, params (string Name, JsonObject Schema)[] fields)
        {
            var p=new JsonObject{["kind"]=new JsonObject{["type"]="string",["const"]=kind}};
            foreach(var field in fields)p[field.Name]=field.Schema;
            return Object(p,new[]{"kind"}.Concat(fields.Select(field=>field.Name)).ToArray());
        }
        var solidEdit=Operation("transform_solid",("transform",new JsonObject{["oneOf"]=new JsonArray(
            SolidTransform("translation",("displacement",Point3())),
            SolidTransform("rotation",("axis_origin",Point3()),("axis_direction",Point3()),("angle",Number(-2*Math.PI,2*Math.PI))),
            SolidTransform("mirror",("plane_origin",Point3()),("plane_normal",Point3())),
            SolidTransform("scale",("center",Point3()),("factor",Number(1e-6,1e6))))}));
        JsonObject CloudPoint()=>new(){["type"]="array",["minItems"]=3,["maxItems"]=3,["items"]=Number(-1e9,1e9)};
        var cloudPlacement=Operation("transform_point_cloud",("transform",new JsonObject{["oneOf"]=new JsonArray(
            SolidTransform("translation",("displacement",CloudPoint())),
            SolidTransform("rotation",("axis_origin",CloudPoint()),("axis_direction",CloudPoint()),("angle",Number(-2*Math.PI,2*Math.PI))))}));
        cloudPlacement["description"]="Move or rotate existing native PointCloud insertions in Model in place, preserving their handles and shared cloud definitions. WCS XYZ displacement or arbitrary axis with nonzero length >=1e-9 and angle in radians. This transforms only drawing insertion placement; no point/source data, loading, clipping edit, copy, scale or storage rewrite. Sources may already be loaded or unloaded; drawing and layer must be writable. Re-read metadata/points with fresh revision and cursors after placement. Ordinary move/rotate also accept cloud insertions in WCS XY/about Z without workplane; use this branch for full3D placement. Native transform executes once on the resident insertion, without cloning/importing it.";
        solidEdit["description"]="Transform existing direct native Solid3d in place, preserving its handle. WCS XYZ translation, arbitrary-axis rotation in radians, plane mirror or positive uniform scale. Finite geometry/volume/centroid checked before and after. Parametric feature histories, fields and unknown persistent dependencies require the owning adapter. Healthy downstream drawing views and sections on a nonparametric body are retained and refreshed after commit; copy/deletion of their sources is a separate ownership change. Documented edits have no guaranteed rollback; inspect any post-commit verification warning. Refresh geometry/topology references after modification. Existing erase/set_properties also accept these direct solids; ordinary move/copy/rotate/scale retain their XY/Z-axis semantics.";
        JsonObject NativeId()=>new(){["type"]="string",["pattern"]="^[1-9][0-9]{0,18}$"};
        JsonObject SolidFinish(string operation)
        {
            bool shell=operation=="shell_solid",chamfer=operation=="chamfer_solid";
            var token=Text(64);token["pattern"]="^[0-9A-F]{64}$";
            var selected=new JsonObject{["type"]="array",["minItems"]=shell?0:1,["uniqueItems"]=true,["items"]=NativeId()};
            var fields=new List<(string Name,JsonObject Schema)>{("topology_token",token),(shell?"remove_faces":"edges",selected)};
            if(chamfer){fields.Add(("base_face",NativeId()));fields.Add(("base_distance",Number(.001,1e6)));fields.Add(("other_distance",Number(.001,1e6)));}
            else fields.Add((shell?"thickness":"radius",Number(.001,1e6)));
            if(shell)fields.Add(("side",new JsonObject{["enum"]=new JsonArray("inward","outward"),["default"]="inward"}));
            var result=Operation(operation,fields.ToArray());result["properties"]!["handles"]!["maxItems"]=1;
            result["description"]="Finish one existing independent native Solid3d in place, preserving its handle, layer and display properties. First inspect_solid_topology and supply its exact current token and native edge/face IDs. Fillet uses radius; chamfer uses adjacent base_face and two distances; shell uses positive thickness with optional inward(default)/outward side; empty remove_faces requests a closed shell without opening faces. Detached preview and native readback require finite nonempty solid geometry; the native kernel determines the result, including concave fillets. No body-count ceiling; a finished handle cannot have another edit in that batch. Parametric feature histories, fields and unknown dependencies are excluded. Healthy drawing views/sections on a nonparametric body are retained and refreshed after commit; inspect any post-commit verification warning. This does not create a parametric feature. ROLLBACK_NOT_GUARANTEED: a started failure can leave a changed body; inspect it and never assume Undo. Reacquire topology after success.";
            if(shell){result["required"]=new JsonArray("operation","handles","topology_token","remove_faces","thickness");result["description"]=result["description"]!.GetValue<string>()+" "+SolidStockSchema353.Scope;}
            return result;
        }
        var solidDrill=Operation("drill_solid",("start",Point3()),("direction",Point3()),("radius",Number(.001,1e6)),("depth",Number(.001,1e6)));
        solidDrill["properties"]!["handles"]!["maxItems"]=1;
        solidDrill["description"]="Drill one existing independent direct Solid3d in place, preserving its handle and display properties. WCS start and nonzero direction define a cylindrical cut of positive radius/depth; the cut runs from start along direction. Detached preview requires measurable material removal and finite nonempty geometry. Cutter-volume comparison is diagnostic, not a universal acceptance oracle. Machining operations in a batch must target different solids. No countersink, thread or parametric Hole feature. Parametric feature histories, fields and unknown dependencies are excluded. Healthy drawing views/sections on a nonparametric body are retained and refreshed after commit; inspect any post-commit verification warning. ROLLBACK_NOT_GUARANTEED: started failure may leave a changed body; inspect the result, never assume Undo. Refresh geometry/topology after success.";
        var vertexEdit=Operation("set_polyline_vertices",("vertices",new JsonObject{["type"]="array",["minItems"]=1,["items"]=Object(new JsonObject{
            ["index"]=CellIndex(int.MaxValue),["expected_position"]=Point(),["position"]=Point()
        },"index","expected_position","position")}));
        vertexEdit["properties"]!["handles"]!["maxItems"]=1;
        var vertexShape=vertexEdit["properties"]!["vertices"]!["items"]!;
        vertexShape["properties"]!["expected_bulge"]=Number(-1e6,1e6);vertexShape["properties"]!["bulge"]=Number(-1e6,1e6);
        vertexShape["dependentRequired"]=new JsonObject{["expected_bulge"]=new JsonArray("bulge"),["bulge"]=new JsonArray("expected_bulge")};
        vertexEdit["description"]="Move selected zero-based vertices of one native WCSXY/Z0 Polyline in place. expected_position must match after earlier operations. Optional paired expected_bulge/bulge changes the outgoing segment curvature (tan(signed sweep/4),0=straight); positions may remain equal for curvature-only edits. Omit both to preserve the old bulge. Last vertex of an open polyline has no outgoing segment. Distinct native zero-based indices within the current source are required; this edit does not insert or remove vertices. Preserve closure, widths and untouched vertices/segments; reject collapsed adjacent segments. Bulge changes require no fields/persistent dependencies or native model ownership; use constraints/model adapters for linked curves. Position-only edits retain prior scope. Read geometry and inspect the image after a meaningful group; rollback is not guaranteed for linked legacy vertex edits.";
        PolylineVertexSchema431.Extend(vertexEdit);
        JsonObject CurveGeometry()=>new(){["oneOf"]=new JsonArray(
            Object(new JsonObject{["type"]=new JsonObject{["const"]="Line"},["start"]=Point(),["end"]=Point()},"type","start","end"),
            Object(new JsonObject{["type"]=new JsonObject{["const"]="Circle"},["center"]=Point(),["radius"]=Number(1e-9,1e9)},"type","center","radius"),
            Object(new JsonObject{["type"]=new JsonObject{["const"]="Arc"},["center"]=Point(),["radius"]=Number(1e-9,1e9),["start_angle"]=Number(-2*Math.PI,2*Math.PI),["sweep_angle"]=Number(1e-9,2*Math.PI-1e-9)},"type","center","radius","start_angle","sweep_angle"))};
        var expectedCurve=CurveGeometry();var replacementCurve=CurveGeometry();ExtendedCurveEditSchema.Add(expectedCurve);ExtendedCurveEditSchema.Add(replacementCurve);
        var curveEdit=Operation("set_curve_geometry",("expected_geometry",expectedCurve),("geometry",replacementCurve));
        curveEdit["properties"]!["workplane"]=WorkplaneSchema.Input();
        curveEdit["properties"]!["handles"]!["maxItems"]=1;
        curveEdit["description"]="Change endpoints of an exact native Line, Circle center/radius, or Arc center/radius/start_angle/sweep_angle in WCSXY/Z0 normal+Z by default. Optional workplane makes Line/Circle/Arc/Ellipse coordinates and Spline control points local to the explicit orthonormal WCS frame, preserving that plane and the native root; independent sources only, without Fields, persistent dependencies or model ownership. A workplane edit target cannot share legacy transforms or a different workplane in the batch. It may share appearance edits. Read actual WCS points and convert into the selected frame before stating expected_geometry. For Arc/Ellipse select frame axes whose normal matches the retained native normal; angles/parameters are relative to those axes. Exact detached previews normalize only for the established expected/readback checks; the resident curve and its display/owned properties are never transformed. Spline retains the existing complete clamped NURBS input/edit scope in that explicit plane. Arc angles are radians; sweep is counterclockwise, positive and less than2*pi; crossing angle0 is supported. Expected and replacement have the same native type and handle; expected_geometry must match after earlier edits. Arc expectations compare actual endpoints and sweep, allowing equivalent angle0/2*pi. No layer/style changes. Arc replacements require no fields/persistent dependencies or native model ownership; use source dimensions or set_model_sketch_geometry for linked curves. This is direct geometry editing, not a solver or entity conversion. Ellipse: replace center, major_axis vector, radius_ratio and start_parameter/sweep_parameter in radians (ellipse parameter, not polar angle); full sweep2*pi requires start0, partial intervals may cross0. Spline: full degree1..5,at least degree+1 control_points, clamped knots over0..1, positive weights (empty for nonrational) and explicit closed; controls, degree/count/knots/weights and closure may change while retaining the Spline entity. No fit or unclamped periodic source; closed curves require repeated first/last controls. Full expected definition required; read current axes/domain or NURBS first. Independent sources only, same ownership guard as Arc. Complete request/source/result bytes bound admission. Display properties retained. Legacy Line/Circle dependencies still need readback after commit and may prevent guaranteed rollback.";
        JsonObject Boundaries()=>new(){["type"]="array",["minItems"]=1,["uniqueItems"]=true,["items"]=new JsonObject{["type"]="string",["pattern"]="^[0-9A-Fa-f]{1,16}$"}};
        var trim=Operation("trim_curve",("boundary_handles",Boundaries()),("pick_point",Point()));
        trim["properties"]!["handles"]!["maxItems"]=1;
        trim["description"]="Remove the interval containing pick_point from one native Line/Arc/Circle, cut by existing finite Line/Arc/Circle or straight zero-width Polyline boundaries. WCSXY/Z0; point must lie on the source within0.00001 mm and away from cuts. Circle needs two distinct cuts; coincident supports are ambiguous. Creates1-2 independent retained pieces and erases the source, so its old handle cannot be used later in this batch. Source fields/persistent dependencies require their adapter; generated pieces retain display properties, not associations or XData. Boundaries are unchanged, may be on locked layers, and see earlier ordered edits. Results expose source -> generated references in contour_results; no silent boundary extension. Finite coordinates/radii remain within1e6 mm; choose the affected source and boundaries for the intended drawing change.";
        var extend=Operation("extend_curve",("boundary_handles",Boundaries()),("end",new JsonObject{["enum"]=new JsonArray("start","end")}));
        extend["properties"]!["handles"]!["maxItems"]=1;
        extend["description"]="Extend the stored start or end of one native Line/Arc to the nearest finite boundary beyond that endpoint. Same finite boundary types as trim_curve; boundaries are not extended. Retains source handle, opposite endpoint, line support or circle center/radius. Arc stays counterclockwise and shorter than a full circle. No intersection, coincident supports or collapsed result are explicit errors before write-open. Linked dependents need readback; their rollback may not be guaranteed. Uses preceding ordered geometry and returns contour_results provenance.";
        var offset=Operation("offset_curve",("distance",Number(.001,1e6)),("side",new JsonObject{["enum"]=new JsonArray("left","right","inward","outward")}));
        offset["properties"]!["handles"]!["maxItems"]=1;
        offset["description"]="Create one independent parallel/concentric contour while retaining its source. Native WCSXY/Z0 Line: left/right relative to stored start -> end. Circle/Arc: inward/outward radius change, arc angular extent retained. Closed strictly convex straight zero-width Polyline (at least three vertices): inward/outward with sharp miter corners, independent of winding. Concave, collinear, self-intersecting or collapsing polygon offsets are explicit unsupported/geometry errors; no hidden simplification. Positive distance0.001-1e6mm. Copies display properties only, not XData/fields/associations. Generated refs appear in created and contour_results; copies retains its ordinary copy meaning.";
        NativeContourOffsetSchema341.Extend(offset);
        var parameterName=Operation("name_model_parameter",("variable_id",new JsonObject{["type"]="string",["pattern"]="^\\{[0-9A-Fa-f]{8}-[0-9A-Fa-f]{4}-[0-9A-Fa-f]{4}-[0-9A-Fa-f]{4}-[0-9A-Fa-f]{12}\\}$"}),
            ("expected_name",new JsonObject{["type"]="string",["minLength"]=1,["maxLength"]=128}),
            ("name",new JsonObject{["type"]="string",["minLength"]=1,["maxLength"]=64}));
        parameterName["properties"]!["handles"]!["maxItems"]=1;
        parameterName["properties"]!["scope_handles"]=new JsonObject{["type"]="array",["minItems"]=1,["uniqueItems"]=true,["items"]=new JsonObject{["type"]="string",["pattern"]="^[0-9A-Fa-f]{1,16}$"}};
        parameterName["description"]="Give an existing User driver, bound extrusion Distance, proven revolution Angle or terminal fillet/chamfer size a useful name, e.g. Depth, preserving its ID/value and rewriting native dependent formulas. Exactly one body and sole operation. Read variable_id and expected_name from get_model_variables. New name is one arithmetic identifier of up to64 characters; case-only renaming/collisions are rejected. Optional scope_handles explicitly includes the primary body and the selected model handles in one owning MCS network, allowing a shared driver rename with its formulas in those bodies. Omitting it preserves the single-body boundary. Connected related User scalars/first-Distance/degree-Angle/terminal finishing-size variables inside the selected body scope; dependent expressions must use supported arithmetic. No new variable or sketch dimension is created. Geometry, values, owners and dependency IDs are checked after native rename. ROLLBACK_NOT_GUARANTEED; unknown outcome requires inspection of affected variables and models. Result model_parameter_names lists rewritten expression IDs.";
        var userVariable=Operation("create_model_variable",("name",Text(64)),("value",Number(-1e12,1e12)));
        userVariable["properties"]!["handles"]!["maxItems"]=1;
        userVariable["description"]="Create one named scalar User variable in the selected native model owner document MCS network. The model handle chooses document context; no geometry or constraint is created. Sole operation, unique arithmetic identifier up to64 characters, finite value +/-1e12 native variable units. Bind existing Distance expressions to its name using set_model_parameter expression, then edit this driver with expression and propagate=true. References and IDs must be read again after reopening DWG. ROLLBACK_NOT_GUARANTEED; result model_variables_created identifies the new variable, created entity refs stay empty. Does not create an angular variable or solve sketch constraints.";
        var mirror2d=MirrorAnnotationSchema340.Operation();
        var operations = new JsonArray(NativeExplodeSchema386.Operation(),mirror2d,SolidSliceSchema.Separate(),SolidSliceSchema.Operation(),ModelRevolutionSchema.Operation(),ContourAssemblySchema.Operation("join_curves"),ContourAssemblySchema.Operation("split_curve"),ContourAssemblySchema.Operation("explode_polyline"),ModelPlacementSchema.Operation(false),ModelPlacementSchema.Operation(true),PolylineTopologySchema.Operation("set_polyline_geometry"),PolylineTopologySchema.Operation("set_polyline_corners"),PolylineTopologySchema.Operation("reverse_polyline"),userVariable,parameterName,ModelFeatureSchema.Operation(),ModelPatternSchema.Operation(false),ModelPatternSchema.Operation(true),ModelPatternSchema.ElementOperation(),ModelShellSchema.Operation(),ModelShellSchema.ThicknessOperation(),ModelFeatureSuppressionSchema.Operation(),ModelEdgeFeatureSchema.Operation("add_model_fillet"),ModelEdgeFeatureSchema.Operation("add_model_chamfer"),ModelSketchSchema.Operation(),solidEdit,SolidFinish("fillet_solid"),SolidFinish("chamfer_solid"),SolidFinish("shell_solid"),solidDrill,vertexEdit,curveEdit,trim,extend,offset,leaderReplacement,replacement,tableReplacement,tableResize,mtextReplacement,dimensionReplacement,dimensionLine,dimensionTextPosition,hatchPattern,
            Operation("move", ("displacement", Point())), Operation("copy", ("displacement", Point())),
            Operation("rotate", ("center", Point()), ("angle", Number(-2 * Math.PI, 2 * Math.PI))),
            Operation("scale", ("center", Point()), ("factor", Number(1e-6, 1e6))),
            Operation("erase"), Operation("set_properties", ("properties", properties)));
        foreach(var item in operations.OfType<JsonObject>())
        {
            string? kind=item["properties"]?["operation"]?["const"]?.GetValue<string>();
            if (kind is "move" or "copy") item["properties"]!["displacement"]!["description"] = "[dx,dy] in millimeters in WCS XY when workplane is omitted, or in the explicit workplane axes. This is a vector, not a destination point.";
            if (kind is "rotate" or "scale") item["properties"]!["center"]!["description"] = "[x,y] pivot in millimeters in WCS XY when workplane is omitted, or in the explicit workplane coordinates.";
            if (kind == "rotate") item["properties"]!["angle"]!["description"] = "Signed rotation in radians about the positive plane normal (WCS +Z by default, explicit workplane normal otherwise). This field is not degrees.";
            if (kind == "scale") item["properties"]!["factor"]!["description"] = "Positive dimensionless uniform scale factor; pivot coordinates retain their declared distance units and plane.";
            if(kind is "trim_curve" or "split_curve" or "extend_curve")NativeContourFinishingSchema342.Extend(item,kind);
        }
        foreach(var item in operations.OfType<JsonObject>())
            if(item["properties"]?["operation"]?["const"]?.GetValue<string>()=="erase")
                item["description"]="Delete selected supported top-level entities. For Model Space targets, first call inspect_objects with erase_assessment=true and the full intended handles together; this explicit read assesses current native erase admission without writing. That flag is not applicable to ordinary Paper erase, which retains its existing supported active-space, owner and dependency checks. Before requesting erase, make the evidence visible: identify each selected handle's actual creation provenance, current role, retained consumers and the deliverable bodies that will remain. Reuse actual creation/Boolean-result receipts; a construction-layer name, hidden display or creation during this task alone does not prove disposability. Removing the task's own proven-unused temporary construction belongs to that modeling task. An eligible assessment is technical admission for its observed context, not user authorization or an override of external review; blocked or unavailable is not clearance. Do not repeat a rejected erase until its exact concern is resolved by new evidence or narrow explicit user approval; approval does not replace missing dependency evidence. For a complete native parametric model root, erase dispatches through its owning model entity, including a Sweep body; delete_model_feature instead removes one eligible feature while retaining its part. Batch whole-model erasures separately from other edits; native roots and ordinary erase targets can share the erase-only batch. Inspect actual external consumers before removing obsolete auxiliaries: retained model dependencies and shared sketch inputs are protected. Explicitly select external included source curves only when their removal is intended; native-owned construction follows native cleanup, with any observed residual history reported. Native Table, MLeader, seven supported Dimension types, Region, curves and Hatch (associative, solid or patterned) remain supported. For Hatch select its handle, not its boundaries; unselected boundary geometry is preserved. Ordinary targets may combine erase with set_properties; whole-model erasure uses an erase-only batch. Unguaranteed Undo/rollback is a warning, never by itself a refusal or extra-permission requirement. Locked layers and actual document/space ownership checks still apply.";
        foreach(var item in operations.OfType<JsonObject>())
            if(item["properties"]?["operation"]?["const"]?.GetValue<string>()=="set_properties")
                item["description"]="Set native display properties (layer, color, linetype, lineweight, per-object linetype_scale and visible) on supported curves, text, Table, Hatch, MLeader, Dimension and Region. Does not change cell content, measured geometry, boundary geometry or feature parameters. Dedicated content-edit restrictions do not apply to display properties. Existing layer/ownership checks apply.";
        PlaneCurveTransformSchema394.Enrich(operations);
        foreach(var erase443 in operations.OfType<JsonObject>().Where(value=>value["properties"]?["operation"]?["const"]?.GetValue<string>()=="erase"))
            erase443["description"]=(erase443["description"]?.GetValue<string>()??"")+" "+ConstructionCurveSchema443.Scope;
        foreach(var operation in BasicModelingSchema.FaceOperations((JsonObject)solidEdit["properties"]!["transform"]!))operations.Add(operation);
        foreach(var operation in ModelManufacturingSchema.Operations())operations.Add(operation);
        operations.Add(cloudPlacement);
        operations.Add(DimensionFormatSchema.Edit());
        foreach(var operation in TableEditingSchema.Operations())operations.Add(operation);
        operations.Add(TableContentsSchema364.Operation());
        foreach(var operation in TableValueEditingSchema336.Operations())operations.Add(operation);
        foreach(var operation in IndependentContourCornersSchema343.Operations())operations.Add(operation);
        operations.Add(TableFormulaSchema343.Operation());
        operations.Add(TableFormulaRangeSchema350.Operation());
        operations.Add(NativeStretchSchema345.Operation());
        operations.Add(MLeaderGeometrySchema388.Operation());
        operations.Add(ModelFeatureReorderSchema345.Operation());
        operations.Add(ModelRebuildSchema419.Operation());
        operations.Add(ModelFeatureDeletionSchema378.Operation());
        operations.Add(ModelProfileRebindSchema349.Operation());
        operations.Add(ModelProfileRegionsSchema350.Operation());
        operations.Add(ModelProfileElementsSchema358.Operation());
        operations.Add(ModelExtrusionExtentsSchema351.Operation());
        operations.Add(ModelDatumPlaneSchema353.Create());
        operations.Add(ModelDatumPlaneRevisionSchema354.Operation());
        operations.Add(ModelDatumPlaneSchema353.MirrorOperation());
        operations.Add(TableCellLayoutSchema340.Operation());
        operations.Add(TableGridLayoutSchema341.Operation());
        operations.Add(ExtendedAnnotationSchema356.Edit());
        operations.Add(DimensionEditingSchema341.DisplayOperation());
        operations.Add(DimensionEditingSchema341.DefinitionOperation());
        foreach(var profileOperation in ModelProfileLifecycleSchema340.Operations())operations.Add(profileOperation!.DeepClone());
        operations.Add(ModelProfileLoftSchema446.Operation());
        foreach(var sketchOperation in ModelSketchLifecycleSchema362.Operations())operations.Add(sketchOperation!.DeepClone());
        operations.Add(SketchConstraintSchema.Operation());
        operations.Add(ModelSketchConstraintParameterSchema379.Operation());
        operations.Add(ModelSketchConstraintRemovalSchema430.Operation());
        operations.Add(ModelSketchVisibilitySchema436.Operation());
        operations.Add(MTextFormatSchema.Operation());
        operations.Add(TextFormatSchema442.Operation());
        operations.Add(MLeaderInstanceSchema442.Operation());
        operations.Add(HatchPropertiesSchema.Edit());
        foreach(var fill in HatchFillSchema377.Operations())operations.Add(fill!.DeepClone());
        foreach(var association in DimensionAssociationSchema385.Operations())operations.Add(association!.DeepClone());
        operations.Add(HatchBoundarySchema349.Operation());
        var hatchAssociation=HatchAssociationSchema358.Operation();
        foreach(var branch in hatchAssociation["oneOf"]!.AsArray())
        {
            var leaf=branch!.DeepClone().AsObject();
            leaf["description"]=hatchAssociation["description"]!.DeepClone();
            operations.Add(leaf);
        }
        operations.Add(SolidImprintSchema393.Operation());
        operations.Add(BasicModelingSchema.BooleanOperation());
        operations.Add(SolidStockSchema353.OffsetOperation());
        operations.Add(SolidReplacementSchema409.Operation());
        operations.Add(RegionBooleanSchema.Edit());
        operations.Add(ModelFeatureRevolutionSchema.Operation());
        foreach(var operation in DrawingCircleSchema.Operations())operations.Add(operation);
        foreach(var operation in DrawingProfileSchema.Operations())operations.Add(operation);
        foreach(var operation in CopyTransforms2dSchema407.Operations())operations.Add(operation);
        operations.Add(DrawOrderSchema407.Operation());
        operations.Add(PointSchema408.PositionEdit());
        operations.Add(LegacyPolylineEditingSchema408.Operation());
        operations.Add(MLeaderTopologySchema431.Operation());
        operations.Add(WipeoutSchema431.Edit());
        operations.Add(AssociativeArraySchema431.Operation(true));operations.Add(AssociativeArraySchema431.Operation(false));
        foreach(var array441 in AssociativeArraySchema441.Creates())operations.Add(array441);
        foreach(var array441 in AssociativeArraySchema441.Edits())operations.Add(array441);
        return JsonSerializer.SerializeToElement(Object(new JsonObject
        {
            ["bridge_instance_id"] = Id(), ["document_id"] = Id(), ["operation_id"] = Id(), ["expected_revision"] = revision,
            ["target_space"]=new JsonObject{["enum"]=new JsonArray("model","paper"),["default"]="model",["description"]="Default Model; explicit paper edits only top-level entities owned by the active sheet, never model objects seen through a viewport or other layouts. Paper supports ordinary placement/properties, independent curve and polyline geometry edits, trim/extend/offset, native Region Boolean, dedicated annotation edits and ordinary static BlockReference root placement/copy/erase/appearance. Block attributes use modify_structure with target_space=paper. Paper block placement requires CVPORT1. No model features, solids or contour assembly."},
            ["units"] = new JsonObject { ["type"] = "string", ["const"] = "Millimeters" },
            ["operations"] = new JsonObject { ["type"] = "array", ["minItems"] = 1, ["items"] = new JsonObject { ["oneOf"] = operations },
                ["description"] = "Ordered edits of existing handles; payload bytes and actual per-family scope apply, without a general source/body-count ceiling. Recovery is operation-specific. Copy sees prior edits; returned copied handles are available after this batch. No use after erase. Operations requiring a fresh ownership/topology/table snapshot are sole operations as described by their branches." }
        }, "bridge_instance_id", "document_id", "operation_id", "expected_revision", "units", "operations"));
    }
}





