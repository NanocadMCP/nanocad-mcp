using System.Text.Json;
using System.Text.Json.Nodes;
using ModelContextProtocol.Protocol;
using NanoCAD.Contracts;
using NanoCAD.McpServer;

internal static class NativeParametric410Tests
{
    public static async Task Run(Func<string,Func<Task>,Task> test)
    {
        string context=Guid.NewGuid().ToString("D"),nativeId=Guid.NewGuid().ToString("B").ToUpperInvariant();
        JsonObject Frame()=>JsonNode.Parse("""{"origin":[10,20,30],"x_axis":[0,1,0],"y_axis":[0,0,1]}""")!.AsObject();
        JsonArray Geometry()=>JsonNode.Parse("""[{"key":"outer","kind":"circle","points":[[10,20,30]],"radius":3}]""")!.AsArray();
        JsonObject Controlled()=>new(){["type"]="Solid3d",["kind"]="model_extrusion",["plane"]=Frame(),["native_geometry"]=Geometry(),
            ["initial_constraints"]=JsonNode.Parse("""[{"kind":"fix_point","first":"outer","first_point":"center"},{"kind":"radius","first":"outer","value":3}]"""),
            ["region_point"]=JsonNode.Parse("[10,21,30]"),["distance"]=12,["direction"]="positive"};
        JsonObject Legacy()=>new(){["type"]="Solid3d",["kind"]="model_extrusion",["plane"]=Frame(),
            ["profile"]=JsonNode.Parse("""{"kind":"circle","center":[0,0],"radius":3}"""),["distance"]=12,["direction"]="positive"};
        CreateArguments Create(params JsonObject[] entities)=>CreateValidation.Parse(Wire.Element(new{bridge_instance_id=context,document_id=context,
            operation_id=context,expected_revision="9",units="Millimeters",entities}));
        ModifyArguments Modify(JsonObject operation)=>ModifyValidation.Parse(Wire.Element(new{bridge_instance_id=context,document_id=context,
            operation_id=context,expected_revision="9",units="Millimeters",operations=new[]{operation}}));
        void Need(bool condition,string message){if(!condition)throw new Exception(message);}
        void Reject(Action action){try{action();}catch(DomainException){return;}throw new Exception("Invalid caller contract accepted before native write.");}
        async Task<JsonElement> Help(string topic,string operation,string? variant=null,string? section=null)
        {
            var arguments=new Dictionary<string,JsonElement>{["topic"]=Wire.Element(topic)};
            if(operation.Length>0)arguments["operation"]=Wire.Element(operation);
            if(variant!=null)arguments["variant"]=Wire.Element(variant);
            if(section!=null)arguments["section"]=Wire.Element(section);
            var result=await new ToolService().CallAsync("get_guidance",arguments,CancellationToken.None);
            string text=result.Content.OfType<TextContentBlock>().Single().Text;
            using var document=JsonDocument.Parse(text);return document.RootElement.Clone();
        }
        await test("410 controlled first feature retains WCS inputs and explicit selection without a fake body",()=>
        {
            var input=Create(Controlled());var spec=(ModelExtrusionSpec)input.Entities.Single();
            Need(spec.NativeGeometry is {Geometry.Length:1,Constraints.Length:2}&&spec.RegionPoint==new Point3(10,21,30),"Owned geometry/constraints and WCS region selection are missing.");
            Need(spec.Profile.Kind=="native_sketch"&&!spec.EditableProfile,"Controlled initialization must not be treated as a legacy coordinate profile.");
            Need(spec.NativeGeometry!.Geometry.Single().Points.Single()==new Point3(10,20,30),"Native WCS input was silently interpreted as plane-local coordinates.");
            var changed=Controlled();changed["initial_constraints"]![1]!["value"]=4;
            Need(input.Fingerprint()!=Create(changed).Fingerprint(),"Replay identity omits the requested native control intent.");
            return Task.CompletedTask;
        });
        await test("410 legacy model profile keeps its previous shape and serialized contract",()=>
        {
            var spec=(ModelExtrusionSpec)Create(Legacy()).Entities.Single();var oracle=ModelExtrusionGeometry.Measure(spec);
            Need(spec.NativeGeometry==null&&spec.RegionPoint==null&&!spec.EditableProfile&&Math.Abs(oracle.Volume-108*Math.PI)<1e-8,"Legacy profile semantics changed.");
            var serialized=Wire.Element(spec);
            Need(!serialized.TryGetProperty("NativeGeometry",out _)&&!serialized.TryGetProperty("RegionPoint",out _),"Absent controlled fields changed legacy replay payloads.");
            return Task.CompletedTask;
        });
        await test("410 controlled create refuses ambiguous branches missing selection and foreign-plane picks",()=>
        {
            var both=Controlled();both["profile"]=Legacy()["profile"]!.DeepClone();Reject(()=>Create(both));
            var compact=Controlled();compact["editable_profile"]=false;Reject(()=>Create(compact));
            var missing=Controlled();missing.Remove("region_point");Reject(()=>Create(missing));
            var offPlane=Controlled();offPlane["region_point"]=JsonNode.Parse("[11,21,30]");Reject(()=>Create(offPlane));
            var wrongKey=Controlled();wrongKey["initial_constraints"]![1]!["first"]="unknown";Reject(()=>Create(wrongKey));
            var legacyControls=Legacy();legacyControls["initial_constraints"]=new JsonArray();Reject(()=>Create(legacyControls));
            Reject(()=>Create(Controlled(),Legacy()));return Task.CompletedTask;
        });
        await test("410 controlled inputs cannot leak into unsupported sweep loft or append definitions",()=>
        {
            var sweep=Controlled();sweep["kind"]="model_sweep";sweep.Remove("distance");sweep.Remove("direction");
            sweep["path_handles"]=new JsonArray("AB");sweep["orientation"]="normal_to_path";sweep["taper_angle"]=0;Reject(()=>Create(sweep));
            var section=Controlled();foreach(string field in new[]{"type","kind","distance","direction"})section.Remove(field);
            var loft=new JsonObject{["type"]="Solid3d",["kind"]="model_loft",["sections"]=new JsonArray(section,section.DeepClone()),["path_handle"]=null,["guide_handles"]=new JsonArray()};Reject(()=>Create(loft));
            var definition=Controlled();definition.Remove("type");definition.Remove("kind");
            Reject(()=>Modify(new(){["operation"]="add_model_extrusion",["handles"]=new JsonArray("1A"),["expected_last_item_id"]=nativeId,["part_operation"]="join",["definition"]=definition}));
            return Task.CompletedTask;
        });
        await test("410 mixed retained sketch references remain exact native IDs and own-key initialization",()=>
        {
            var operation=new JsonObject{["operation"]="create_model_sketch",["handles"]=new JsonArray("1A"),["expected_last_item_id"]=nativeId,
                ["plane"]=Frame(),["source_handles"]=new JsonArray("AB"),["reference_ids"]=new JsonArray(nativeId),["native_geometry"]=Geometry(),
                ["initial_constraints"]=JsonNode.Parse("""[{"kind":"radius","first":"outer","value":3}]""")};
            var parsed=(ModelSketchCreate362)Modify(operation).Operations.Single();
            Need(parsed.SourceHandles.SequenceEqual(new[]{"AB"})&&parsed.ReferenceIds!.SequenceEqual(new[]{nativeId})&&parsed.NativeGeometry!.Constraints.Single().First=="outer","Mixed construction lost a real source/reference or initial owned key.");
            var referencesOnly=(JsonObject)operation.DeepClone();referencesOnly.Remove("source_handles");referencesOnly.Remove("native_geometry");referencesOnly.Remove("initial_constraints");
            Need(((ModelSketchCreate362)Modify(referencesOnly).Operations.Single()).NativeGeometry==null,"A reference-only sketch was forced to invent an owned input.");
            var invented=(JsonObject)operation.DeepClone();invented["reference_ids"]=new JsonArray("edge:AB");Reject(()=>Modify(invented));
            var projectedInitial=(JsonObject)operation.DeepClone();projectedInitial["initial_constraints"]![0]!["first"]=nativeId;Reject(()=>Modify(projectedInitial));
            return Task.CompletedTask;
        });
        await test("410 complete focused create schema has one variant and both exclusive caller branches",async()=>
        {
            var tool=ToolCatalog.Tools.Single(t=>t.Name=="nanocad.create_entities");
            var choices=tool.InputSchema.GetProperty("properties").GetProperty("entities").GetProperty("items").GetProperty("oneOf").EnumerateArray()
                .Where(branch=>branch.GetProperty("properties").TryGetProperty("kind",out var kind)&&kind.GetProperty("const").GetString()=="model_extrusion").ToArray();
            Need(choices.Length==1,"Duplicate top-level variant makes focused selection ambiguous.");
            var focused=await Help("schema","create_entities","model_extrusion");
            var branch=focused.GetProperty("properties").GetProperty("entities").GetProperty("items");
            Need(branch.GetProperty("properties").TryGetProperty("profile",out _)&&branch.GetProperty("properties").TryGetProperty("native_geometry",out _)
                &&branch.GetProperty("properties").TryGetProperty("region_point",out _)&&branch.GetProperty("oneOf").GetArrayLength()==2,"Focused help hid a complete legacy or controlled input contract.");
            Need(!focused.GetRawText().Contains("\"$ref\""),"Client-visible create fields require unresolved local references.");
        });
        await test("410 internal read write routes are discoverable with typed output identities",async()=>
        {
            var reader=ToolCatalog.Tools.Single(t=>t.Name=="nanocad.inspect_sketch_constraints");
            Need(Wire.ReadOperations.Contains("inspect_sketch_constraints")&&reader.Annotations is {ReadOnlyHint:true,DestructiveHint:false},"Readonly internal relation tool is missing or unsafe.");
            var read=Validation.Parse("inspect_sketch_constraints",Wire.Element(new{bridge_instance_id=context,document_id=context,expected_revision="9",handle="1A",sketch_id=nativeId}));
            Need(read.ModelObjectId==nativeId,"Advertised reader is rejected by the IPC allowlist.");
            var tree=await Help("commands","",section:"parametric");
            Need(tree.GetProperty("routes").EnumerateArray().Any(route=>route.GetProperty("tool").GetString()==reader.Name),"Parametric command tree hides the internal reader.");
            var modify=await Help("schema","modify_entities");var variants=modify.GetProperty("variants").EnumerateArray().Select(value=>value.GetString()).ToArray();
            Need(variants.Contains("add_sketch_constraints")&&variants.Contains("set_sketch_constraint_parameter"),"Supported internal writes are missing from the real focused contract index.");
            var output=ToolCatalog.Tools.Single(t=>t.Name=="nanocad.create_entities").OutputSchema!.Value;
            Need(output.GetRawText().Contains("\"model_sketch_creation\"")&&output.GetRawText().Contains("\"native_objects\"")&&output.GetRawText().Contains("\"constraint_changes\""),"Controlled create receipt hides actual native identities.");
        });
        await test("440 duplicate internal control bindings are rejected before native writes",()=>
        {
            SketchConstraintSpec[] Parse(string json)=>SketchConstraintValidation385.Parse(JsonDocument.Parse(json).RootElement,"$.constraints",false);
            Reject(()=>Parse("""[{"kind":"radius","first":"circle","value":3},{"kind":"radius","first":"circle","value":4}]"""));
            Reject(()=>Parse("""[{"kind":"distance_x","first":"a","second":"b","first_point":"start","second_point":"end","value":3},{"kind":"distance_x","first":"a","second":"b","first_point":"start","second_point":"end","value":4}]"""));
            Reject(()=>Parse("""[{"kind":"parallel","first":"a","second":"b"},{"kind":"parallel","first":"b","second":"a"}]"""));
            Reject(()=>Parse("""[{"kind":"coincident","first":"a","second":"b","first_point":"start","second_point":"end"},{"kind":"coincident","first":"b","second":"a","first_point":"end","second_point":"start"}]"""));
            Reject(()=>Parse("""[{"kind":"symmetric","first":"a","second":"b","third":"axis"},{"kind":"symmetric","first":"b","second":"a","third":"axis"}]"""));
            var initial=Controlled();initial["initial_constraints"]!.AsArray().Add(JsonNode.Parse("""{"kind":"radius","first":"outer","value":4}"""));Reject(()=>Create(initial));
            var reference=new JsonObject{["kind"]="concentric",["first"]="outer",["second"]=nativeId};
            var normalizedRepeat=(JsonObject)reference.DeepClone();normalizedRepeat["second"]=nativeId.ToLowerInvariant();
            Reject(()=>NativeSketchValidation385.Parse(Wire.Element(Geometry()),Wire.Element(new[]{reference,normalizedRepeat}),null,"$.native_geometry",[nativeId]));
            var valid=Parse("""[{"kind":"fix_point","first":"circle","first_point":"center"},{"kind":"diameter","first":"circle","value":6},{"kind":"radius","first":"peer","value":3},{"kind":"distance_x","first":"a","second":"b","first_point":"start","second_point":"end","value":3},{"kind":"distance_x","first":"a","second":"b","first_point":"end","second_point":"start","value":4}]""");
            Need(valid.Length==5&&valid[1].Kind=="diameter"&&valid[1].Value==6,"Independent controls or valid center fixation and diameter were changed.");
            return Task.CompletedTask;
        });
        await test("414 internal whole fix and symmetry retain exact third operands and old serialization",()=>
        {
            string first=Guid.NewGuid().ToString("B").ToUpperInvariant(),second=Guid.NewGuid().ToString("B").ToUpperInvariant(),axis=Guid.NewGuid().ToString("B").ToUpperInvariant();
            var relation=new JsonObject{["kind"]="symmetric",["first"]=first,["second"]=second,["third"]=axis};
            SketchConstraintSpec Parse(JsonObject value)=>SketchConstraintValidation385.Parse(Wire.Element(new[]{value}),"$.constraints").Single();
            Need(Parse(relation).Third==axis,"Symmetry lost its exact native axis identity.");
            var missing=(JsonObject)relation.DeepClone();missing.Remove("third");Reject(()=>Parse(missing));
            var repeated=(JsonObject)relation.DeepClone();repeated["third"]=first;Reject(()=>Parse(repeated));
            var fixedObject=new JsonObject{["kind"]="fix",["first"]=axis};Need(Parse(fixedObject).FirstPoint==null,"Whole fix gained a point selector.");
            var withPoint=(JsonObject)fixedObject.DeepClone();withPoint["first_point"]="start";Reject(()=>Parse(withPoint));
            var unrelatedThird=(JsonObject)fixedObject.DeepClone();unrelatedThird["third"]=axis;Reject(()=>Parse(unrelatedThird));
            var old=Wire.Element(new SketchConstraintSpec("radius",first,null,null,null,2));Need(!old.TryGetProperty("third",out _)&&!old.TryGetProperty("Third",out _),"Absent optional axis changed old serialized request intent.");
            var receipt=Wire.Element(new SketchConstraintChange(nativeId,nativeId,"symmetric",first,second,null,null,axis));Need(receipt.GetProperty("third_element_id").GetString()==axis,"The native symmetry receipt omits its actual third axis ID.");
            return Task.CompletedTask;
        });
        await test("414 initial symmetry normalizes declared axis references and requires owned paired geometry",()=>
        {
            string axis=Guid.NewGuid().ToString("B").ToUpperInvariant(),other=Guid.NewGuid().ToString("B").ToUpperInvariant();
            var plane=new SketchPlaneSpec(new(0,0,0),new(1,0,0),new(0,1,0));
            var geometry=JsonNode.Parse("""[{"key":"left","kind":"circle","points":[[5,2,0]],"radius":2},{"key":"right","kind":"circle","points":[[5,-2,0]],"radius":2}]""")!;
            var relation=new JsonObject{["kind"]="symmetric",["first"]="left",["second"]="right",["third"]=axis.ToLowerInvariant()};
            var initial=NativeSketchValidation385.Parse(Wire.Element(geometry),Wire.Element(new[]{relation}),plane,"$.native_geometry",[axis]);
            Need(initial.Constraints.Single().Third==axis,"Initial axis source was not normalized to its exact declared GUID.");
            var invented=(JsonObject)relation.DeepClone();invented["third"]="unbound_axis";Reject(()=>NativeSketchValidation385.Parse(Wire.Element(geometry),Wire.Element(new[]{invented}),plane,"$.native_geometry",[axis]));
            var projectedPair=new JsonObject{["kind"]="symmetric",["first"]=axis,["second"]=other,["third"]="axis"};
            var axisOnly=JsonNode.Parse("""[{"key":"axis","kind":"line","points":[[0,0,0],[10,0,0]]}]""")!;
            Reject(()=>NativeSketchValidation385.Parse(Wire.Element(axisOnly),Wire.Element(new[]{projectedPair}),plane,"$.native_geometry",[axis,other]));
            return Task.CompletedTask;
        });
        await test("414 symmetry checks full reflected Lines and Circles about an oblique actual axis",()=>
        {
            var axis=new SketchConstraintCurve("axis","line",new(2,3),new(7,8));
            var first=new SketchConstraintCurve("left","circle",Center:new(3,6),Radius:2);var second=new SketchConstraintCurve("right","circle",Center:new(5,4),Radius:2);
            SketchSymmetryGeometry414.Validate(first,second,axis);
            Reject(()=>SketchSymmetryGeometry414.Validate(first,second with{Radius=3},axis));
            Reject(()=>SketchSymmetryGeometry414.Validate(first,second with{Center=new(5,5)},axis));
            var line=new SketchConstraintCurve("a","line",new(3,6),new(4,7));var mirror=new SketchConstraintCurve("b","line",new(6,5),new(5,4));
            SketchSymmetryGeometry414.Validate(line,mirror,axis);
            Reject(()=>SketchSymmetryGeometry414.Validate(line,mirror with{End=new(5,5)},axis));
            Reject(()=>SketchSymmetryGeometry414.Validate(first,second,axis with{End=axis.Start}));
            Reject(()=>SketchSymmetryGeometry414.Validate(line,second,axis));return Task.CompletedTask;
        });
        await test("410 typed native planner does not assign distance semantics to count or offset controls",()=>
        {
            var owned=new ModelExpressionVariable("offset","Offset","2",2,[],NativeOwnedControl:true);
            var plan=ModelExpressionPlanning.Prepare([owned],"offset","-3",false);
            Need(plan.ExpectedValues["offset"]==-3,"Signed native offset was rejected as a positive extrusion distance before its owner can validate it.");
            var legacy=new ModelExpressionVariable("distance","Distance","2",2,[]);
            Reject(()=>ModelExpressionPlanning.Prepare([legacy],"distance","-3",false));return Task.CompletedTask;
        });
        await test("410 numeric caller contract defers signed zero and count ranges to verified native owners",()=>
        {
            foreach(double scalar in new[]{-3d,0,1,1e7})
            {
                var parsed=ModelParameterValidation.Parse(Wire.Element(new{bridge_instance_id=context,document_id=context,expected_revision="9",operation_id=context,
                    units="Millimeters",handle="1A",variable_id=nativeId,expected_expression="2",value=scalar}));
                Need(parsed.Value==scalar&&parsed.RequestedExpression==null,"Numeric literal route imposed an unrelated distance range or rewrote input to expression.");
            }
            var value=ToolCatalog.Tools.Single(t=>t.Name=="nanocad.set_model_parameter").InputSchema.GetProperty("properties").GetProperty("value");
            Need(!value.TryGetProperty("minimum",out _)&&!value.TryGetProperty("maximum",out _),"Published numeric contract still assigns a universal distance range.");
            using var nonfinite=JsonDocument.Parse("""{"bridge_instance_id":"10000000-0000-0000-0000-000000000001","document_id":"10000000-0000-0000-0000-000000000002","operation_id":"10000000-0000-0000-0000-000000000003","expected_revision":"9","units":"Millimeters","handle":"1A","variable_id":"{10000000-0000-0000-0000-000000000004}","expected_expression":"2","value":1e999}""");
            Reject(()=>ModelParameterValidation.Parse(nonfinite.RootElement));return Task.CompletedTask;
        });
    }
}
