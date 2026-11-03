using System.Text.Json.Nodes;

namespace NanoCAD.McpServer;

internal static class NativeSketchSchema385
{
    static JsonObject Text()=>new(){["type"]="string"};
    static JsonObject Id()=>new(){["type"]="string",["pattern"]="^\\{[0-9A-Fa-f]{8}-[0-9A-Fa-f]{4}-[0-9A-Fa-f]{4}-[0-9A-Fa-f]{4}-[0-9A-Fa-f]{12}\\}$"};
    static JsonObject Enum(params string[] values)=>new(){["enum"]=new JsonArray(values.Select(v=>(JsonNode?)JsonValue.Create(v)).ToArray())};
    static JsonObject Object(JsonObject fields,params string[] required)=>new(){["type"]="object",["additionalProperties"]=false,["properties"]=fields,["required"]=new JsonArray(required.Select(s=>(JsonNode?)JsonValue.Create(s)).ToArray())};
    static JsonObject Array(JsonObject value,int minimum=0)=>new(){["type"]="array",["minItems"]=minimum,["items"]=value};
    static JsonObject Nullable(JsonObject value)=>new(){["anyOf"]=new JsonArray(value,new JsonObject{["type"]="null"})};
    static JsonObject Any()=>new(){["type"]="object",["additionalProperties"]=true};
    static JsonObject Vector()=>new(){["type"]="array",["minItems"]=3,["maxItems"]=3,["items"]=new JsonObject{["type"]="number",["minimum"]=-1e6,["maximum"]=1e6}};
    internal static JsonObject Constraint(bool nativeIds=true)
    {
        JsonObject Member()=>nativeIds?Id():new(){["type"]="string",["minLength"]=1,["maxLength"]=64};
        JsonObject One(string[] kinds,bool second=false,bool firstPoint=false,bool secondPoint=false,bool value=false,bool third=false)
        {
            var fields=new JsonObject{["kind"]=Enum(kinds),["first"]=Member()};var required=new List<string>{"kind","first"};
            if(second){fields["second"]=Member();required.Add("second");}if(firstPoint){fields["first_point"]=Enum("start","end","center");required.Add("first_point");}if(secondPoint){fields["second_point"]=Enum("start","end","center");required.Add("second_point");}
            if(third){fields["third"]=Member();fields["third"]!["description"]="Actual separate native Line member used as the symmetry axis.";required.Add("third");}
            if(value){fields["value"]=new JsonObject{["type"]="number",["exclusiveMinimum"]=0};if(kinds.Length==1&&kinds[0]=="angle")fields["value"]!["exclusiveMaximum"]=Math.PI;required.Add("value");}return Object(fields,required.ToArray());
        }
        var smooth=One(["smooth"],true,true,true);
        smooth["properties"]!["first_point"]=Enum("start","end");smooth["properties"]!["second_point"]=Enum("start","end");
        smooth["description"]="Retained native Smooth at explicit start/end endpoints of at least one open Spline and an adjacent curve. Initially match endpoint positions and tangent continuation. Keep constraints independent. Drive supported shape changes through the existing native sketch controls; use current returned IDs/fresh control tokens and verify the rebuilt curve/profile/body. No G2 or universal deformation guarantee.";
        return new(){["oneOf"]=new JsonArray(One(["horizontal","vertical","fix"]),One(["parallel","perpendicular","collinear","tangent","equal","concentric"],second:true),One(["symmetric"],second:true,third:true),One(["coincident"],true,true,true),smooth,One(["fix_point"],firstPoint:true),One(["radius","diameter","length"],value:true),One(["distance","distance_x","distance_y"],true,true,true,true),One(["angle"],second:true,value:true))};
    }
    internal static JsonObject Geometry()=>Array(new JsonObject{["oneOf"]=new JsonArray(
        Object(new(){["key"]=new JsonObject{["type"]="string",["minLength"]=1,["maxLength"]=64},["kind"]=Enum("line"),["points"]=new JsonObject{["type"]="array",["minItems"]=2,["maxItems"]=2,["items"]=Vector()}},"key","kind","points"),
        Object(new(){["key"]=new JsonObject{["type"]="string",["minLength"]=1,["maxLength"]=64},["kind"]=Enum("circle"),["points"]=new JsonObject{["type"]="array",["minItems"]=1,["maxItems"]=1,["items"]=Vector()},["radius"]=new JsonObject{["type"]="number",["exclusiveMinimum"]=0,["maximum"]=1e6}},"key","kind","points","radius"),
        Object(new(){["key"]=new JsonObject{["type"]="string",["minLength"]=1,["maxLength"]=64},["kind"]=Enum("arc"),["points"]=new JsonObject{["type"]="array",["minItems"]=3,["maxItems"]=3,["items"]=Vector(),["description"]="Ordered actual native Arc start, through and end WCS points in the explicit sketch plane; three distinct noncollinear points. Radius follows those points; no radius field. Clockwise and counterclockwise arcs are supported."}},"key","kind","points"),
        NativeSketchCurveSchema445.Spline(),NativeSketchCurveSchema445.Ellipse())},1);
    internal static JsonObject InitialConstraints(bool allowReferenceIds=false)
    {
        var value=Array(Constraint(false));
        value["description"]=allowReferenceIds
            ?"Initial first/second/third operands are own native_geometry keys or exact braced source GUIDs supplied in reference_ids. A source GUID binds the real newly created same-sketch projection; never invent a projection ID, drawing handle or alias. At least one paired operand must be own geometry; projections remain immutable. fix fixes one complete owned Line/Circle/Arc without point selectors. symmetric relates two Lines or two Circles about a separate actual Line supplied as third. Point selectors use actual start/end/center geometry in the observed native sketch frame; linear controls millimeters and angles radians."
            :"Initial first/second/third operands are own native_geometry keys from this request. fix fixes one complete owned Line/Circle/Arc without point selectors. symmetric relates two Lines or two Circles about a separate actual Line supplied as third. Point selectors use actual start/end/center geometry in the observed native sketch frame; linear controls millimeters and angles radians.";
        return value;
    }
    internal static JsonObject Read()=>Object(new(){["complete"]=new JsonObject{["type"]="boolean"},["unavailable_reason"]=Nullable(Text()),["binding_token"]=Text(),["members"]=Array(Object(new(){["member_id"]=Id(),["object_id"]=Id(),["native_type"]=Text(),["segment_index"]=Nullable(new JsonObject{["type"]="integer",["minimum"]=0}),["geometry"]=Any(),["independently_owned"]=new JsonObject{["type"]="boolean"},["reference_only"]=new JsonObject{["type"]="boolean",["default"]=false,["description"]="True for a proved immutable same-sketch native projection operand. At least one relation operand must be independently owned; reference geometry/source are retained, never directly driven or rewritten by the internal solver."},["unavailable_reason"]=Nullable(Text())},"member_id","object_id","native_type","segment_index","geometry","independently_owned","unavailable_reason")),["identity_scope"]=Text(),["scope"]=Text()},"complete","unavailable_reason","binding_token","members","identity_scope","scope");
    internal static JsonObject Created()=>Array(Object(new(){["key"]=Text(),["object_id"]=Id(),["member_ids"]=Array(Id(),1)},"key","object_id","member_ids"));
    internal static JsonObject Changes()
    {
        var item=Object(new(){["sketch_id"]=Id(),["constraint_id"]=Id(),["kind"]=Text(),["first_element_id"]=Id(),["second_element_id"]=Nullable(Id()),["third_element_id"]=Id(),["variable_id"]=Nullable(Id()),["value"]=Nullable(new JsonObject{["type"]="number"})},"sketch_id","constraint_id","kind","first_element_id","second_element_id","variable_id","value");
        item["allOf"]=new JsonArray(new JsonObject{["if"]=new JsonObject{["properties"]=new JsonObject{["kind"]=new JsonObject{["const"]="symmetric"}}},["then"]=new JsonObject{["required"]=new JsonArray("third_element_id")}});
        return Array(item);
    }
}
