using System.Text.Json;
using System.Text.Json.Nodes;

namespace NanoCAD.McpServer;

internal static class CreateSchema
{
    private static JsonObject Text(int max) => new() { ["type"] = "string", ["minLength"] = 1, ["maxLength"] = max };
    private static JsonObject Number(double min = -1e9, double max = 1e9) => new() { ["type"] = "number", ["minimum"] = min, ["maximum"] = max };
    private static JsonObject Point() => new() { ["type"] = "array", ["minItems"] = 2, ["maxItems"] = 2, ["items"] = Number(), ["description"] = "Coordinate pair in millimeters. WCSXY/Z0 by default; for Line/Circle/Arc/Ellipse/Spline/Polyline with workplane, coordinates are local to that frame." };
    private static JsonObject Object(JsonObject properties, params string[] required) => new()
    { ["type"] = "object", ["additionalProperties"] = false, ["properties"] = properties, ["required"] = new JsonArray(required.Select(s => (JsonNode?)JsonValue.Create(s)).ToArray()) };
    private static JsonObject Entity(string kind, params (string Name, JsonObject Schema, bool Required)[] fields)
    {
        var layer = Text(255); layer["default"] = "0";
        var properties = new JsonObject { ["type"] = new JsonObject { ["type"] = "string", ["const"] = kind }, ["layer"] = layer };
        if(kind is "Line" or "Circle" or "Arc" or "Ellipse" or "Spline" or "Polyline")properties["workplane"]=WorkplaneSchema.Input();
        var required = new List<string> { "type" };
        foreach (var (name, schema, mandatory) in fields) { properties[name] = schema; if (mandatory) required.Add(name); }
        var result=Object(properties, required.ToArray());if(kind is "Line" or "Circle" or "Arc" or "Ellipse" or "Spline" or "Polyline")result["description"]="Planar source geometry; coordinate pairs and vectors are local to optional workplane, otherwise WCSXY/Z0. No projection or UCS change. Arc start/sweep, Ellipse parameters and polyline bulges use the frame normal; Spline retains its complete native creation grammar.";return result;
    }
    public static JsonElement Input()
    {
        JsonObject Id() { var id = Text(36); id["format"] = "uuid"; return id; }
        var start = Number(0, Math.PI * 2); start.Remove("maximum"); start["exclusiveMaximum"] = Math.PI * 2;
        var sweep = Number(1e-9, Math.PI * 2); sweep.Remove("maximum"); sweep["exclusiveMaximum"] = Math.PI * 2;
        var style = Text(255); style["default"] = "Standard";
        var revision = Text(20); revision["pattern"] = "^[0-9]{1,20}$";
        var entities = new JsonArray(
            Line(),
            Entity("Circle", ("center", Point(), true), ("radius", Number(1e-9), true)),
            Entity("Arc", ("center", Point(), true), ("radius", Number(1e-9), true), ("start_angle", start, true), ("sweep_angle", sweep, true)),
            Entity("Polyline", ("vertices", new JsonObject { ["type"] = "array", ["minItems"] = 2, 
                ["items"] = Object(new JsonObject { ["point"] = Point(), ["bulge"] = Number(-1000, 1000) }, "point") }, true),
                ("closed", new JsonObject { ["type"] = "boolean", ["default"] = false }, false)),
            Entity("Text", ("text", Text(4096), true), ("position", Point(), true), ("height", Number(1e-9), true), ("text_style", style, false), ("format",TextFormatSchema442.CreateFormat(),false)));
        entities.Add(MLeaderInstanceSchema442.Create());
        entities.Add(ConstructionCurveSchema443.Create("Ray"));entities.Add(ConstructionCurveSchema443.Create("Xline"));
        var paragraph=Text(2048);paragraph["minLength"]=0;
        var splineKnots=new JsonObject{["type"]="array",["minItems"]=4,["items"]=Number(0,1),
            ["description"]="Clamped knot vector: count=controls+degree+1, degree+1 zeros and ones, ordered internal knots with multiplicity<=degree; distinct knots at least1e-6 apart."};
        var splineWeights=new JsonObject{["type"]="array",["items"]=Number(.01,100),
            ["description"]="Empty for nonrational; otherwise exactly one positive weight per control. No automatic normalization."};
        entities.Add(Entity("Spline",("degree",new JsonObject{["type"]="integer",["minimum"]=1,["maximum"]=5},true),
            ("control_points",new JsonObject{["type"]="array",["minItems"]=2,["items"]=Point(),
                ["description"]="At least degree+1 controls. Neighboring controls and open endpoints at least1e-6 mm apart. Closed curves require at least4 controls and exactly repeated first/last control. WCS XY; no fit or unclamped periodic input."},true),
            ("knots",splineKnots,true),("weights",splineWeights,true),("closed",new JsonObject{["type"]="boolean",["default"]=false},false)));
        var ellipseAxis=Point();ellipseAxis["description"]="WCS XY major-axis vector from center, not an endpoint; length0.001..1e6 mm, minor radius at least0.001 mm.";
        var ellipseStart=Number(0,Math.PI*2);ellipseStart.Remove("maximum");ellipseStart["exclusiveMaximum"]=Math.PI*2;
        var ellipseSweep=Number(1e-6,Math.PI*2);ellipseSweep["description"]="Parametric sweep in radians, not polar angle. Full2pi requires start0; partial sweep must be at least1e-6 below2pi.";
        entities.Add(Entity("Ellipse",("center",Point(),true),("major_axis",ellipseAxis,true),("radius_ratio",Number(.01,1),true),
            ("start_parameter",ellipseStart,true),("sweep_parameter",ellipseSweep,true)));
        entities.Add(Entity("MText",("paragraphs",new JsonObject{["type"]="array",["minItems"]=1,["items"]=paragraph},true),
            ("position",Point(),true),("height",Number(0.1,10000),true),("width",Number(1,1000000),true),("text_style",Text(255),true)));
        foreach (string kind in new[] { "aligned", "rotated", "radial", "diametric" })
        {
            var fields = new List<(string, JsonObject, bool)> {
                ("kind", new JsonObject { ["type"] = "string", ["const"] = kind }, true),
                ("dimension_style", Text(255), true), ("text_override", Text(4096), false) };
            if (kind is "aligned" or "rotated")
            {
                fields.Add(("xline1", Point(), true)); fields.Add(("xline2", Point(), true)); fields.Add(("dimline_point", Point(), true));
                if (kind == "rotated") fields.Add(("rotation", new JsonObject { ["type"] = "number", ["minimum"] = 0, ["exclusiveMaximum"] = Math.PI * 2 }, true));
            }
            else
            {
                fields.Add((kind == "radial" ? "center" : "chord_point", Point(), true));
                fields.Add((kind == "radial" ? "chord_point" : "far_chord_point", Point(), true));
                fields.Add(("leader_length", Number(0), false));
            }
            fields.Add(("associations",DimensionAssociationSchema385.Bindings(kind),false));
            entities.Add(Entity("Dimension", fields.ToArray()));
        }
        entities.Add(Entity("Dimension",
            ("kind", new JsonObject { ["const"] = "projected_aligned" }, true),
            ("view_handle", new JsonObject { ["type"] = "string", ["pattern"] = "^[0-9A-Fa-f]{1,16}$" }, true),
            ("line_index", new JsonObject { ["type"] = "integer", ["minimum"] = 0, ["maximum"] = int.MaxValue,
                ["description"] = "Current visible Line index from get_drawing_views for this view." }, true),
            ("dimline_point", Point(), true)));
        JsonObject Points(int min) => new() { ["type"]="array", ["minItems"]=min, ["items"]=Point() };
        JsonObject DimensionPoint()=>new(){["type"]="array",["minItems"]=2,["maxItems"]=2,["items"]=Number(-1e6,1e6),["description"]="WCS XY millimeters, Z=0; coordinates within +/-1000000."};
        foreach(string kind in new[]{"angular_3point","angular_2line","ordinate"})
        {
            var text=Text(256);text["description"]="Optional plain prefix/suffix around one <> measurement token; default <>. No fields or formatting.";
            var fields=new List<(string,JsonObject,bool)>{("kind",new(){["const"]=kind},true),("dimension_style",Text(255),true),("text_override",text,false)};
            string[] names=kind=="angular_3point"?new[]{"center","xline1","xline2","arc_point"}:kind=="angular_2line"?new[]{"xline1_start","xline1_end","xline2_start","xline2_end","arc_point"}:new[]{"origin","defining_point","leader_end_point"};
            foreach(string name in names)fields.Add((name,DimensionPoint(),true));
            if(kind=="ordinate")fields.Add(("axis",new(){["enum"]=new JsonArray("x","y"),["description"]="Coordinate measured from explicit origin. Nonzero coordinate and perpendicular leader extension, each at least0.001 mm."},true));
            var variant=Entity("Dimension",fields.ToArray());
            if(kind.StartsWith("angular",StringComparison.Ordinal))variant["description"]="Ray/line directions are ordered counterclockwise, angle1e-5..pi-1e-5 radians. Arc point strictly inside that sector at radius0.001..1000000. Parallel, reflex and degenerate cases are rejected.";
            entities.Add(variant);
        }
        foreach(var nativeAnnotation in ExtendedAnnotationSchema356.Creates())entities.Add(nativeAnnotation);
        entities.Add(Entity("MLeader", ("vertices",Points(2),true),("text_position",Point(),true),("text",Text(4096),true),
            ("text_height",Number(1e-6),true),("text_style",Text(255),false),("mleader_style",Text(255),true)));
        entities.Add(NativeHatchCreateSchema407.Entity(false));
        entities.Add(PointSchema408.Create());
        entities.Add(FitSplineSchema408.Entity());
        foreach(var curve441 in CurveCreationSchema441.Creates())entities.Add(curve441);
        entities.Add(SmoothPolylineSchema409.Entity());
        entities.Add(CurveSplineSchema408.Entity());
        entities.Add(NativeHatchCreateSchema407.Entity(true));
        var holePolygon=new JsonObject{["type"]="object",["additionalProperties"]=false,["required"]=new JsonArray("kind","vertices"),
            ["properties"]=new JsonObject{["kind"]=new JsonObject{["const"]="polygon"},["vertices"]=Points(3)}};
        var holeCircle=new JsonObject{["type"]="object",["additionalProperties"]=false,["required"]=new JsonArray("kind","center","radius"),
            ["properties"]=new JsonObject{["kind"]=new JsonObject{["const"]="circle"},["center"]=Point(),["radius"]=Number(1e-6,1e9)}};
        entities.Add(Entity("Hatch", ("boundary",Points(3),true),
            ("holes",new JsonObject{["type"]="array",["minItems"]=1,["items"]=new JsonObject{["oneOf"]=new JsonArray(holePolygon,holeCircle)},
                ["description"]="Optional exact polygonal or circular voids strictly inside the outer polygon; disjoint, non-touching and non-nested. Complete actual geometry must fit its byte budget; circles use two exact semicircular bulges. Non-associative."},false),
            ("pattern_name",new JsonObject{["type"]="string",["enum"]=new JsonArray("SOLID","ANSI31"),["default"]="SOLID"},false),
            ("pattern_angle",new JsonObject{["type"]="number",["minimum"]=0,["exclusiveMaximum"]=Math.PI*2,["default"]=0},false),
            ("pattern_scale",Number(0.01,100),false)));
        entities.Add(Entity("Solid3d",("kind",new JsonObject { ["const"]="extrusion" },true),("profile",Points(3),true),
            ("base_z",Number(),true),("height",Number(1e-3,1e6),true)));
        JsonObject ModelXyz()=>new(){["type"]="array",["minItems"]=3,["maxItems"]=3,["items"]=Number(-1e6,1e6)};
        JsonObject ModelXy()=>new(){["type"]="array",["minItems"]=2,["maxItems"]=2,["items"]=Number(-1e6,1e6)};
        JsonObject UnitAxis()=>new(){["type"]="array",["minItems"]=3,["maxItems"]=3,["items"]=Number(-1,1),["description"]="Unit WCS vector, perpendicular to the other in-plane axis; no implicit scaling."};
        var modelExtrusion=Entity("Solid3d",("kind",new JsonObject{["const"]="model_extrusion"},true),
            ("plane",Object(new JsonObject{["origin"]=ModelXyz(),["x_axis"]=UnitAxis(),["y_axis"]=UnitAxis()},"origin","x_axis","y_axis"),true),
            ("profile",new JsonObject{["oneOf"]=new JsonArray(
                Object(new JsonObject{["kind"]=new JsonObject{["const"]="circle"},["center"]=ModelXy(),["radius"]=Number(.001,1e6)},"kind","center","radius"),
                Object(new JsonObject{["kind"]=new JsonObject{["const"]="polygon"},["vertices"]=new JsonObject{["type"]="array",["minItems"]=3,["items"]=ModelXy()}},"kind","vertices"),ModelPolycurveSchema.Variant())},true),
            ("distance",Number(.001,1e6),true),("direction",new JsonObject{["enum"]=new JsonArray("positive","negative")},true),("name",Text(64),false),("editable_profile",new JsonObject{["type"]="boolean",["description"]="Retain hidden source curves so the profile can later be reshaped with set_model_sketch_geometry. Default false keeps the prior compact profile. Neither choice creates MCS constraints."},false));
        modelExtrusion["description"]="Legacy profile branch: create one native MultiCAD Mc3dSolid history with PlanarSketch, closed profile and ExtrudeFeature. Must be the only entity in this request and requires a named Model Space millimeter drawing with MultiCAD already loaded. Plane axes are orthogonal unit WCS vectors; profile coordinates are local millimeters; direction follows x_axis cross y_axis. One circle, simple straight polygon or exact Line/Arc polycurve (vertices plus outgoing bulges), no holes/taper. name is a prefix for history items. Return native body handle, then inspect_model_history for sketch and distance-variable IDs; this model uses the history/view adapter, not direct solid editing. This legacy branch creates no sketch constraints or automatic variable formulas. ROLLBACK_NOT_GUARANTEED: failed creation can retain partial native objects; inspect the reported outcome, never assume rollback.";
        entities.Add(modelExtrusion);
        var modelSweep=(JsonObject)modelExtrusion.DeepClone();var sp=(JsonObject)modelSweep["properties"]!;
        sp["kind"]=new JsonObject{["const"]="model_sweep"};sp.Remove("distance");sp.Remove("direction");
        sp["path_handles"]=BasicModelingSchema.List(BasicModelingSchema.Handle(),1);
        sp["orientation"]=new JsonObject{["type"]="string",["enum"]=new JsonArray("normal_to_path","parallel_to_profile")};
        sp["taper_angle"]=Number(-Math.PI/2+1e-6,Math.PI/2-1e-6);
        modelSweep["required"]=new JsonArray("type","kind","plane","profile","path_handles","orientation","taper_angle");
        modelSweep["description"]="Native parametric sweep: create sketch/profile and ExtrudeAlongPathFeature with persistent references to ordered connected path_handles. Named Model document, sole entity in request. Circle, polygon or line/arc polycurve section on explicit plane. Path entities remain in the drawing. orientation selects native normal-to-path or parallel-to-original-profile; taper_angle is radians, not accumulated twist. Inspect resulting feature and dependent shape. ROLLBACK_NOT_GUARANTEED. editable_profile retains native sketch geometry; later profile reshaping depends on the supported existing-history editor and must not be assumed for a sweep.";
        entities.Add(modelSweep);
        var sectionProperties=(JsonObject)modelExtrusion["properties"]!.DeepClone();foreach(var field in new[]{"type","kind","layer","distance","direction"})sectionProperties.Remove(field);
        var sectionSchema=Object(sectionProperties,"plane","profile");
        var nullablePath=BasicModelingSchema.Handle();nullablePath["type"]=new JsonArray("string","null");
        var modelLoft=Entity("Solid3d",("kind",new JsonObject{["const"]="model_loft"},true),("sections",BasicModelingSchema.List(sectionSchema,2),true),("path_handle",nullablePath,true),("guide_handles",BasicModelingSchema.List(BasicModelingSchema.List(BasicModelingSchema.Handle(),1)),true));
        modelLoft["description"]="Native parametric loft with ordered circle/polygon/line-arc sketch sections on explicit planes. Creates retained sketches/profiles and LoftFeature. Choose null path_handle and empty guide_handles for regular loft, a centerline path, or guide chains intersecting all sections. Each guide_handles item is an ordered connected list of existing curve handles. Native links are retained; no replacement by independent direct geometry. Named Model document, sole entity. ROLLBACK_NOT_GUARANTEED. Inspect history and resulting form; free end conditions.";
        entities.Add(modelLoft);
        var controlledExtrusion=(JsonObject)modelExtrusion.DeepClone();var cp=(JsonObject)controlledExtrusion["properties"]!;
        cp.Remove("profile");cp.Remove("editable_profile");cp["native_geometry"]=NativeSketchSchema385.Geometry();
        cp["initial_constraints"]=NativeSketchSchema385.InitialConstraints();cp["region_point"]=ModelXyz();
        cp["region_point"]!["description"]="One unambiguous interior WCS point in the sketch plane, in the wanted bounded material region and outside its holes. Select the region after native constraints solve; do not infer it from input ordering.";
        controlledExtrusion["description"]="Start one native Mc3dSolid from its own internal PlanarSketch geometry, optional initial native relations/controls, one selected bounded profile and the first distance ExtrudeFeature. No pre-existing solid is needed. Explicit plane uses orthonormal WCS axes; native Line/Circle/three-point Arc, complete clamped NURBS Spline and Ellipse inputs use actual WCS points in that plane. Initial constraint operands are native_geometry keys from this request; angles radians and linear controls millimeters. region_point selects one actual bounded native area after solving, outside its holes; SDK region membership verifies complete native Line/Arc/Circle/Spline/Ellipse boundaries without faceting. The native sketch retains service geometry/relations separately from requested input identities. This branch accepts native_geometry instead of profile and does not use editable_profile. Named Millimeters Model drawing, sole entity, uncertain Undo/rollback. Returned model_sketch_creation identifies actual sketch/profile/feature/member/relation/variable IDs. Read fresh history sketch_ids/control tokens before later edits. A stored value or VarId alone does not establish an observed driving effect. When changing a driver, assess actual sketch/dependent geometry at a meaningful checkpoint; a create-only receipt does not show that later propagation was observed.";
        var extrusionProperties=(JsonObject)modelExtrusion["properties"]!;
        foreach(string field in new[]{"native_geometry","initial_constraints","region_point"})extrusionProperties[field]=cp[field]!.DeepClone();
        modelExtrusion["required"]=new JsonArray("type","kind","plane","distance","direction");
        modelExtrusion["oneOf"]=new JsonArray(
            new JsonObject{["required"]=new JsonArray("profile"),["not"]=new JsonObject{["anyOf"]=new JsonArray(new JsonObject{["required"]=new JsonArray("native_geometry")},new JsonObject{["required"]=new JsonArray("initial_constraints")},new JsonObject{["required"]=new JsonArray("region_point")})}},
            new JsonObject{["required"]=new JsonArray("native_geometry","region_point"),["not"]=new JsonObject{["anyOf"]=new JsonArray(new JsonObject{["required"]=new JsonArray("profile")},new JsonObject{["required"]=new JsonArray("editable_profile")})}});
        modelExtrusion["description"]=modelExtrusion["description"]!.GetValue<string>()+" Alternatively, "+controlledExtrusion["description"]!.GetValue<string>();

        entities.Add(Entity("Solid3d",("kind",new JsonObject { ["const"]="boolean" },true),
            ("operator",new JsonObject { ["enum"]=new JsonArray("union","intersection","subtract") },true),
            ("source_handles",new JsonObject { ["type"]="array",["minItems"]=2,["maxItems"]=2,["uniqueItems"]=true,
                ["items"]=new JsonObject { ["type"]="string",["pattern"]="^[0-9A-Fa-f]{1,16}$" } },true)));
        JsonObject Triple(bool size=false)=>new() { ["type"]="array",["minItems"]=3,["maxItems"]=3,["items"]=size?Number(1e-3,1e6):Number(),
            ["description"]=size?"WCS X/Y/Z lengths in millimeters.":"WCS XYZ center of volume in millimeters." };
        JsonObject Xyz()=>new(){["type"]="array",["minItems"]=3,["maxItems"]=3,["items"]=Number(),["description"]="WCS XYZ coordinates in millimeters."};
        JsonObject Direction()=>new(){["type"]="array",["minItems"]=3,["maxItems"]=3,["items"]=Number(),["description"]="Nonzero WCS direction vector; normalized by the bridge, magnitude at least 1e-9."};
        entities.Add(Entity("Solid3d",("kind",new JsonObject{["const"]="transform_copy"},true),
            ("source_handle",new JsonObject{["type"]="string",["pattern"]="^[0-9A-Fa-f]{1,16}$"},true),
            ("transform",new JsonObject{["oneOf"]=new JsonArray(
                Object(new JsonObject{["kind"]=new JsonObject{["const"]="translation"},["displacement"]=Xyz()},"kind","displacement"),
                Object(new JsonObject{["kind"]=new JsonObject{["const"]="rotation"},["axis_origin"]=Xyz(),["axis_direction"]=Direction(),["angle"]=Number(-Math.PI*2,Math.PI*2)},"kind","axis_origin","axis_direction","angle"),
                Object(new JsonObject{["kind"]=new JsonObject{["const"]="mirror"},["plane_origin"]=Xyz(),["plane_normal"]=Direction()},"kind","plane_origin","plane_normal"))},true)));
        entities.Add(Entity("Solid3d",("kind",new JsonObject{["const"]="drilled_copy"},true),
            ("source_handle",new JsonObject{["type"]="string",["pattern"]="^[0-9A-Fa-f]{1,16}$"},true),
            ("start",Xyz(),true),("direction",Direction(),true),("radius",Number(1e-3,1e6),true),("depth",Number(1e-3,1e6),true)));
        foreach(var finishing in new[]{"fillet_copy","chamfer_copy","shell_copy"})
        {
            JsonObject NativeId()=>new(){["type"]="string",["pattern"]="^[1-9][0-9]{0,18}$",["description"]="Canonical positive Int64 ID from the current source topology."};
            var fields=new List<(string,JsonObject,bool)>{("kind",new JsonObject{["const"]=finishing},true),
                ("source_handle",new JsonObject{["type"]="string",["pattern"]="^[0-9A-Fa-f]{1,16}$"},true),
                ("topology_token",new JsonObject{["type"]="string",["pattern"]="^[0-9A-F]{64}$"},true),
                (finishing=="shell_copy"?"remove_faces":"edges",new JsonObject{["type"]="array",["minItems"]=finishing=="shell_copy"?0:1,["uniqueItems"]=true,["items"]=NativeId()},true)};
            if(finishing=="chamfer_copy"){fields.Add(("base_face",NativeId(),true));fields.Add(("base_distance",Number(1e-3,1e6),true));fields.Add(("other_distance",Number(1e-3,1e6),true));}
            else fields.Add((finishing=="fillet_copy"?"radius":"thickness",Number(1e-3,1e6),true));
            if(finishing=="shell_copy")fields.Add(("side",new JsonObject{["enum"]=new JsonArray("inward","outward"),["default"]="inward"},false));
            entities.Add(Entity("Solid3d",fields.ToArray()));
        }
        entities.Add(Entity("Solid3d",("kind",new JsonObject{["const"]="loft"},true),
            ("interpolation",new JsonObject{["enum"]=new JsonArray("smooth","ruled"),["description"]="smooth: native smooth interpolation. ruled: native two-profile pieces united into linear center/radius transitions, with verified mass and direct face-surface observations."},true),
            ("sections",new JsonObject{["type"]="array",["minItems"]=2,
                ["items"]=Object(new JsonObject{["center"]=Xyz(),["radius"]=Number(1e-3,1e6)},"center","radius"),
                ["description"]="Circular sections parallel to WCS XY, strictly ascending Z with at least 0.001 mm separation. Smooth capped solid, no guides or twist. Total height and adjacent center distance <=1000000 mm."},true)));
        var radialProfile=Points(3);radialProfile["items"]!["description"]="Radius / axial height pair in millimeters, radius >=0; starts in the XZ meridian of axis_origin.";
        entities.Add(Entity("Solid3d",("kind",new JsonObject{["const"]="revolve"},true),("profile",radialProfile,true),("axis_origin",Xyz(),true),
            ("angle",Number(1e-3,Math.PI*2),true),("start_angle",new JsonObject{["type"]="number",["minimum"]=0,["exclusiveMaximum"]=Math.PI*2,["default"]=0},false)));
        var linePath=Object(new JsonObject{["kind"]=new JsonObject{["const"]="line"},["start"]=Xyz(),["end"]=Xyz()},"kind","start","end");
        var arcPath=Object(new JsonObject{["kind"]=new JsonObject{["const"]="arc"},["center"]=Xyz(),["radius"]=Number(1e-3,1e6),
            ["start_angle"]=new JsonObject{["type"]="number",["minimum"]=0,["exclusiveMaximum"]=Math.PI*2},["sweep_angle"]=Number(1e-3,Math.PI)},"kind","center","radius","start_angle","sweep_angle");
        entities.Add(Entity("Solid3d",("kind",new JsonObject{["const"]="sweep"},true),("radius",Number(1e-3,1e6),true),
            ("path",new JsonObject{["oneOf"]=new JsonArray(linePath,arcPath)},true)));
        foreach(string kind in new[]{"box","sphere","cylinder"})
        {
            var fields=new List<(string,JsonObject,bool)> { ("kind",new JsonObject { ["const"]=kind },true),("center",Triple(),true) };
            if(kind=="box") fields.Add(("size",Triple(true),true)); else fields.Add(("radius",Number(1e-3,1e6),true));
            if(kind=="cylinder") fields.Add(("height",Number(1e-3,1e6),true));
            entities.Add(Entity("Solid3d",fields.ToArray()));
        }
        var frustum=Entity("Solid3d",("kind",new JsonObject{["const"]="frustum"},true),
            ("center",Triple(),true),("base_radius",Number(1e-3,1e6),true),("top_radius",Number(1e-3,1e6),true),("height",Number(1e-3,1e6),true));
        frustum["description"]="Create circular tapered stock or a reducer as one direct native Solid3d in Model Space. base_radius and top_radius are positive circular end radii; height is the native axial length, all in millimeters. center is the WCS native center of volume, as for the other solid primitives, not the base center or half-height midpoint. It retains the native cylinder orientation without an extra rotation; use the existing native transform operations for another pose. Both radii must be positive; a pointed apex is outside this branch. No retained feature graph is created. Read the returned body and review its useful engineering shape.";
        entities.Add(frustum);
        var torus=Entity("Solid3d",("kind",new JsonObject{["const"]="torus"},true),
            ("center",Triple(),true),("major_radius",Number(1e-3,1e6),true),("minor_radius",Number(1e-3,1e6),true));
        torus["description"]="Create one direct native ring torus in Model Space. major_radius is the ring radius and minor_radius is the circular tube radius, in millimeters; require 0 < minor_radius < major_radius. center is the WCS native center of volume. The native initial orientation is retained without a workplane or extra rotation; use existing native transforms for another pose. No self-intersecting spindle/horn torus or retained feature graph is requested. Inspect the actual body and useful view for the intended radii, placement and orientation.";
        entities.Add(torus);
        var wedge=Entity("Solid3d",("kind",new JsonObject{["const"]="wedge"},true),("center",Triple(),true),("size",Triple(true),true));
        wedge["description"]="Create one direct native wedge stock in Model Space. size gives its native X/Y/Z lengths in millimeters. center is the WCS native center of volume, not the enclosing box midpoint. The native slope and orientation are retained without an extra rotation; use existing native transforms for another pose. No retained feature graph is created. Inspect the native faces and useful view for the intended taper direction, dimensions and placement.";
        entities.Add(wedge);
        var topRadius=new JsonObject{["oneOf"]=new JsonArray(new JsonObject{["type"]="number",["const"]=0},Number(1e-3,1e6)),
            ["description"]="Zero requests a pointed apex; a positive value requests a regular truncated pyramid, in millimeters."};
        var sides=new JsonObject{["type"]="integer",["minimum"]=3,["maximum"]=int.MaxValue,
            ["description"]="Polygon side count. At least three; maximum is the actual native Int32 parameter representation, not a model-size ceiling."};
        var pyramid=Entity("Solid3d",("kind",new JsonObject{["const"]="pyramid"},true),("center",Triple(),true),
            ("base_radius",Number(1e-3,1e6),true),("top_radius",topRadius,true),("height",Number(1e-3,1e6),true),("sides",sides,true));
        pyramid["description"]="Create one direct native regular pyramid or truncated pyramid stock in Model Space. base_radius and top_radius are the native polygon radii, height is the native axial length, all in millimeters; sides gives the polygon side count. top_radius=0 requests an apex; positive top_radius requests truncation. center is the WCS native center of volume, not the base center or half-height point. The native polygon starting orientation is retained without an extra rotation. Use existing native transforms for another pose; no retained feature graph is created. Inspect the actual end geometry, placement and useful view.";
        entities.Add(pyramid);
        var table=Entity("Table", ("position",Point(),true),("row_height",Number(1e-6),true),("column_width",Number(1e-6),true),("table_style",Text(255),true),
            ("cells",new JsonObject { ["type"]="array", ["minItems"]=1,  ["items"]=new JsonObject { ["type"]="array", ["minItems"]=1,["items"]=new JsonObject { ["type"]="string",["maxLength"]=4096 } } },true));
        table["description"]="Create an editable native nanoCAD Table. Use this branch for requested tables instead of drawing grid lines and separate text. cells is a rectangular row-major array of literal strings; rows must have equal column counts. position is WCSXY/Z0 (sheet millimeters for target_space=paper); row_height and column_width are initial uniform cell sizes in millimeters. table_style must name an existing native TableStyle from get_structure(section=table_styles). No formulas, data links or merged-cell inputs. Creation uses a nonempty rectangular grid within actual request and result byte budgets. Existing cell text uses modify_entities/replace_table_text; row heights and column widths use resize_table, for plain unmerged tables within complete observation-byte admission. If a required native table feature is unsupported, disclose the specific gap and obtain agreement before substituting linework/text. Do not infer unsupported creation from missing transform/delete support.";
        entities.Add(table);
        foreach(var schema in BasicModelingSchema.Creates())entities.Add(schema);
        foreach(var schema in SolidFaceProfileCreateSchema390.Creates())entities.Add(schema);
        var wipeout431=WipeoutSchema431.Create();wipeout431["properties"]!["workplane"]=WorkplaneSchema.Input();entities.Add(wipeout431);
        return JsonSerializer.SerializeToElement(Object(new JsonObject
        {
            ["bridge_instance_id"] = Id(), ["document_id"] = Id(), ["operation_id"] = Id(), ["expected_revision"] = revision,
            ["target_space"]=new JsonObject{["enum"]=new JsonArray("model","paper"),["default"]="model",["description"]="Explicit write destination: default Model; paper requires an active sheet outside a model viewport. Sheet coordinates are millimeters, XY/Z0, no workplane. Supports ordinary2D curves, text, dimensions, leaders, tables and non-associative hatches. Never switches tabs or writes to another layout implicitly."},
            ["units"] = new JsonObject { ["type"] = "string", ["const"] = "Millimeters" },
            ["entities"] = new JsonObject { ["type"] = "array", ["minItems"] = 1, ["items"] = new JsonObject { ["oneOf"] = entities } }
        }, "bridge_instance_id", "document_id", "operation_id", "expected_revision", "units", "entities"));
    }

    private static JsonObject Line()
    {
        var line = Entity("Line", ("start", Point(), false), ("end", Point(), false),
            ("start_snap", NativeCurveSnapSchema.Input(), false), ("end_snap", NativeCurveSnapSchema.Input(), false));
        JsonObject Required(string field) => new() { ["required"] = new JsonArray(field) };
        JsonObject Endpoint(string field) => new() { ["oneOf"] = new JsonArray(Required(field), Required(field + "_snap")) };
        line["allOf"] = new JsonArray(Endpoint("start"), Endpoint("end"), new JsonObject
        {
            ["if"] = new JsonObject { ["anyOf"] = new JsonArray(Required("start_snap"), Required("end_snap")) },
            ["then"] = new JsonObject { ["not"] = Required("workplane") }
        });
        line["description"] = "Create a native Line. Supply start or start_snap and end or end_snap. With only literal endpoints, coordinates are WCS XY/Z0 or local XY in the optional workplane. If either endpoint uses a snap, omit workplane. Snapped endpoints use resolved WCS XYZ and any literal endpoint remains WCS XY/Z0. The created Line is independent.";
        return line;
    }
}
