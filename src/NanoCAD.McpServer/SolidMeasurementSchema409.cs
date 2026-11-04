using System.Text.Json.Nodes;

namespace NanoCAD.McpServer;

internal static class SolidMeasurementSchema409
{
    internal const string Description="Use analyze_geometry solid_distance for minimum distance between complete boundaries of two native Solid3d bodies whose faces are planar and whose finite edges are straight. The result also reports native material interference and material clearance; nested interfering bodies can have positive boundary distance and zero material clearance. face_distance measures two bounded trimmed planar straight-edged faces selected by their current topology native_id and body handle. Other faces of those bodies may be curved. wall_thickness measures one local normal material chord at a WCS point strictly inside the first selected face, between opposed parallel faces of one complete planar straight-edged body, and rejects intervening body/cavity boundaries. It is not global minimum wall thickness. All witnesses use WCS drawing units. Reacquire topology after edits. No workplane, UCS projection, tessellation or bounding-box distance is used.";
    internal const string AxisDescription = "cylinder_diameter reads the native diameter and WCS axis of one circular cylindrical face. axis_axis_distance measures the infinite axes of two such faces. axis_plane_distance measures the first cylindrical face axis against the second face's infinite supporting plane. Select current body handles and native face IDs from inspect_solid_topology. Near-parallel geometry uses the reported native vector tolerance. These reads use native analytic surfaces without fitting or classifying holes. Axis measurements do not establish finite face or material clearance.";

    internal static JsonObject Input()
    {
        JsonObject Branch(string mode)
        {
            int faceCount = mode == "cylinder_diameter" ? 1 : 2;
            var properties=new JsonObject
            {
                ["bridge_instance_id"]=new JsonObject{["type"]="string",["format"]="uuid"},
                ["document_id"]=new JsonObject{["type"]="string",["format"]="uuid"},
                ["expected_revision"]=new JsonObject{["type"]="string",["pattern"]="^[0-9]{1,20}$"},
                ["mode"]=new JsonObject{["const"]=mode},
                ["handles"]=new JsonObject{["type"]="array",["uniqueItems"]=true,["minItems"]=mode=="solid_distance"?2:1,["maxItems"]=mode=="wall_thickness"?1:faceCount,
                    ["items"]=Handle(),["description"]="Exactly the body handles used by this measurement and its face selections."}
            };
            var required=new JsonArray("bridge_instance_id","document_id","expected_revision","mode","handles");
            if(mode!="solid_distance")
            {
                properties["faces"]=new JsonObject{["type"]="array",["minItems"]=faceCount,["maxItems"]=faceCount,["uniqueItems"]=true,
                    ["description"]="Ordered native face selections from inspect_solid_topology. Use the count and face types of the selected mode. For axis_plane_distance, put the cylindrical face first and the planar face second.",
                    ["items"]=Obj(new(){["handle"]=Handle(),["native_id"]=NativeId()})};required.Add("faces");
            }
            if(mode=="wall_thickness")
            {
                var point=Triple();point["items"]=new JsonObject{["type"]="number",["minimum"]=-1e12,["maximum"]=1e12};
                point["description"]="WCS XYZ point strictly inside the first selected trimmed face. The second face must be reached through one uninterrupted material chord along the first face inward normal.";
                properties["point"]=point;required.Add("point");
            }
            return new(){["type"]="object",["additionalProperties"]=false,["properties"]=properties,["required"]=required};
        }
        return new(){["type"]="object",["description"]=Description + " " + AxisDescription,
            ["oneOf"]=new JsonArray(Branch("solid_distance"),Branch("face_distance"),Branch("wall_thickness"),
                Branch("cylinder_diameter"),Branch("axis_axis_distance"),Branch("axis_plane_distance"))};
    }

    internal static JsonObject Output()
    {
        JsonObject Common(string mode,string solver)=>new()
        {
            ["mode"]=Const(mode),["coordinate_system"]=Const("WCS"),["solver"]=Const(solver),
            ["units"]=Obj(new(){["name"]=Text(),["value"]=new JsonObject{["type"]="integer"}}),
            ["first_face"]=FaceRef(),["second_face"]=FaceRef(),["first_point"]=Triple(),["second_point"]=Triple(),
            ["accuracy"]=Obj(new(){["point_tolerance"]=Number(),["vector_tolerance"]=Number(),["finite_edge_verification_tolerance"]=Number(),["policy"]=Text()}),
            ["result_scope"]=Text(),["warning"]=Text()
        };
        JsonObject Distance(string mode)
        {
            var properties=Common(mode,"native_finite_edges_and_trimmed_planar_faces");
            properties["distance"]=Number();properties["witness_method"]=new JsonObject{["enum"]=new JsonArray("native_finite_edge_pair","vertex_to_trimmed_face","finite_edge_face_crossing")};
            properties["first_edge_native_id"]=Nullable(NativeId());properties["second_edge_native_id"]=Nullable(NativeId());
            properties["solid_interference"]=mode=="solid_distance"?new JsonObject{["type"]="boolean"}:new JsonObject{["type"]="null"};
            properties["material_clearance"]=mode=="solid_distance"?Number():new JsonObject{["type"]="null"};
            return Obj(properties);
        }
        var wall=Common("wall_thickness","native_planar_topology_material_chord");
        wall["thickness"]=Number();wall["direction"]=Triple();wall["material_chord_verified"]=new JsonObject{["const"]=true};wall["global_minimum_evaluated"]=new JsonObject{["const"]=false};
        JsonObject Cylinder() => Obj(new(){["face"]=FaceRef(),["axis_origin"]=Triple(),["axis_direction"]=Triple(),["diameter"]=Number()});
        JsonObject Analytic(string mode) => new()
        {
            ["mode"]=Const(mode),["coordinate_system"]=Const("WCS"),["solver"]=Const("native_analytic_supporting_surfaces"),
            ["units"]=Obj(new(){["name"]=Text(),["value"]=new JsonObject{["type"]="integer"}})
        };
        JsonObject AxisDistance(string mode)
        {
            var properties=Analytic(mode);
            properties["distance"]=Number();
            properties["angle_radians"]=Number();
            properties["parallel_within_native_tolerance"]=new JsonObject{["type"]="boolean"};
            properties["vector_tolerance"]=Number();
            properties["result_scope"]=Text();
            return properties;
        }
        var diameter=Analytic("cylinder_diameter");
        diameter["cylinder"]=Cylinder();
        var axes=AxisDistance("axis_axis_distance");
        axes["first_cylinder"]=Cylinder();
        axes["second_cylinder"]=Cylinder();
        var axisPlane=AxisDistance("axis_plane_distance");
        axisPlane["cylinder"]=Cylinder();
        axisPlane["plane"]=Obj(new(){["face"]=FaceRef(),["origin"]=Triple(),["normal"]=Triple()});
        return new(){["oneOf"]=new JsonArray(Distance("solid_distance"),Distance("face_distance"),Obj(wall),Obj(diameter),Obj(axes),Obj(axisPlane))};
    }

    private static JsonObject Obj(JsonObject properties)=>new(){["type"]="object",["additionalProperties"]=false,["properties"]=properties,["required"]=new JsonArray(properties.Select(p=>(JsonNode?)JsonValue.Create(p.Key)).ToArray())};
    private static JsonObject Text()=>new(){["type"]="string"};
    private static JsonObject Number()=>new(){["type"]="number",["minimum"]=0};
    private static JsonObject Triple()=>new(){["type"]="array",["minItems"]=3,["maxItems"]=3,["items"]=new JsonObject{["type"]="number"}};
    private static JsonObject Handle()=>new(){["type"]="string",["pattern"]="^[0-9A-Fa-f]{1,16}$"};
    private static JsonObject NativeId()=>new(){["type"]="string",["pattern"]="^[1-9][0-9]{0,18}$"};
    private static JsonObject Const(string value)=>new(){["type"]="string",["const"]=value};
    private static JsonObject Nullable(JsonObject value)=>new(){["anyOf"]=new JsonArray(value,new JsonObject{["type"]="null"})};
    private static JsonObject FaceRef()=>Obj(new(){["body_ref"]=new JsonObject{["$ref"]="#/$defs/ref"},["native_id"]=NativeId()});
}
