using System.Text.Json;
using System.Text.Json.Nodes;
using NanoCAD.Contracts;
using NanoCAD.McpServer;

internal static class ModelSketchVisibility436Tests
{
    internal static async Task Run(Func<string,Func<Task>,Task> test)
    {
        static void Need(bool value,string message){if(!value)throw new Exception(message);}
        static void Reject(Action action,string code="INVALID_ARGUMENT")
        {
            try{action();}catch(DomainException e){if(e.Error.code==code)return;throw new Exception("Expected "+code+", got "+e.Error.code);}
            throw new Exception("Ambiguous sketch presentation request was accepted.");
        }
        const string sketch="{00000000-0000-0000-0000-00000000000a}",dimension="{00000000-0000-0000-0000-00000000000b}";
        JsonObject Operation()=>new(){["operation"]="set_model_sketch_visibility",["handles"]=new JsonArray("00a1"),["sketch_id"]=sketch,["expected_visibility_token"]=new string('a',64),["dimensions_visible"]=true};
        JsonObject Request(JsonObject operation)=>new(){["bridge_instance_id"]="00000000-0000-0000-0000-000000000001",["document_id"]="00000000-0000-0000-0000-000000000002",["operation_id"]="00000000-0000-0000-0000-000000000003",["expected_revision"]="7",["units"]="Millimeters",["operations"]=new JsonArray(operation.DeepClone())};
        ModelSketchVisibilitySpec436 Parse(JsonObject operation)=>(ModelSketchVisibilitySpec436)ModifyValidation.Parse(Wire.Element(Request(operation))).Operations.Single();
        await test("block436 sketch visibility dispatch preserves omission, false and exact native selections",()=>
        {
            var all=Parse(Operation());
            Need(all.Handles.SequenceEqual(["A1"])&&all.SketchId==sketch.ToUpperInvariant()&&all.ExpectedVisibilityToken==new string('A',64),"Current identities did not normalize.");
            Need(all.DimensionsVisible==true&&all.SketchVisible==null&&all.ConstraintIds==null,"Omitted selection must mean all dimensional controls; implicit parent prerequisite remains explicit in the receipt.");
            var selectedOperation=Operation();selectedOperation["dimensions_visible"]=false;selectedOperation["constraint_ids"]=new JsonArray(dimension);
            var selected=Parse(selectedOperation);
            Need(selected.DimensionsVisible==false&&selected.ConstraintIds!.SequenceEqual([dimension.ToUpperInvariant()]),"Explicit hide or selected dimensional controls changed.");
            var hideSketch=Operation();hideSketch.Remove("dimensions_visible");hideSketch["sketch_visible"]=false;
            Need(Parse(hideSketch) is {SketchVisible:false,DimensionsVisible:null,ConstraintIds:null},"Explicit sketch hide became an omitted request.");
            Need(ModifyValidation.Parse(Wire.Element(Request(Operation()))).Fingerprint()!=ModifyValidation.Parse(Wire.Element(Request(selectedOperation))).Fingerprint(),"Different visibility intent shares a fingerprint.");
            return Task.CompletedTask;
        });
        await test("block436 sketch visibility rejects conflicting, stale-shape and ambiguous selections",()=>
        {
            var noFlag=Operation();noFlag.Remove("dimensions_visible");Reject(()=>Parse(noFlag));
            var conflict=Operation();conflict["sketch_visible"]=false;Reject(()=>Parse(conflict));
            var orphanSelection=Operation();orphanSelection.Remove("dimensions_visible");orphanSelection["sketch_visible"]=true;orphanSelection["constraint_ids"]=new JsonArray(dimension);Reject(()=>Parse(orphanSelection));
            var empty=Operation();empty["constraint_ids"]=new JsonArray();Reject(()=>Parse(empty));
            var duplicate=Operation();duplicate["constraint_ids"]=new JsonArray(dimension,dimension.ToUpperInvariant());Reject(()=>Parse(duplicate));
            foreach(string invalid in new[]{"length","00000000-0000-0000-0000-00000000000a","{00000000-0000-0000-0000-000000000000}"})
            {var operation=Operation();operation["sketch_id"]=invalid;Reject(()=>Parse(operation));}
            var label=Operation();label["constraint_ids"]=new JsonArray("Length");Reject(()=>Parse(label));
            var token=Operation();token["expected_visibility_token"]=new string('z',64);Reject(()=>Parse(token));
            var missingToken=Operation();missingToken.Remove("expected_visibility_token");Reject(()=>Parse(missingToken));
            var nullFlag=Operation();nullFlag["dimensions_visible"]=null;Reject(()=>Parse(nullFlag));
            var twoParts=Operation();twoParts["handles"]=new JsonArray("A1","A2");Reject(()=>Parse(twoParts));
            var unknown=Operation();unknown["visible"]=true;Reject(()=>Parse(unknown));
            string duplicateField=Operation().ToJsonString().Replace("\"dimensions_visible\":true","\"dimensions_visible\":true,\"dimensions_visible\":false",StringComparison.Ordinal);
            using var duplicateJson=JsonDocument.Parse(duplicateField);
            Reject(()=>ModelSketchVisibilityValidation436.Parse(duplicateJson.RootElement,"$",["A1"]));
            return Task.CompletedTask;
        });
        await test("block436 sketch visibility requires one model-space operation",()=>
        {
            var request=Request(Operation());request["operations"]!.AsArray().Add(new JsonObject{["operation"]="move",["handles"]=new JsonArray("B1"),["displacement"]=new JsonArray(1,0)});
            Reject(()=>ModifyValidation.Parse(Wire.Element(request)),"INVALID_OPERATION_ORDER");
            var twoSketches=Request(Operation());twoSketches["operations"]!.AsArray().Add(Operation());Reject(()=>ModifyValidation.Parse(Wire.Element(twoSketches)),"INVALID_OPERATION_ORDER");
            var paper=Request(Operation());paper["target_space"]="paper";Reject(()=>ModifyValidation.Parse(Wire.Element(paper)),"OUT_OF_SCOPE");
            return Task.CompletedTask;
        });
        await test("block436 sketch visibility publishes conditional flags and bounded identity selection",()=>
        {
            var tool=ToolCatalog.Tools.Single(tool=>tool.Name=="nanocad.modify_entities");
            var branch=tool.InputSchema.GetProperty("properties").GetProperty("operations").GetProperty("items").GetProperty("oneOf").EnumerateArray().Single(branch=>branch.GetProperty("properties").GetProperty("operation").GetProperty("const").GetString()=="set_model_sketch_visibility");
            var properties=branch.GetProperty("properties");
            Need(properties.GetProperty("handles").GetProperty("maxItems").GetInt32()==1,"One selected part is not published.");
            Need(properties.GetProperty("constraint_ids").GetProperty("minItems").GetInt32()==1&&!properties.GetProperty("constraint_ids").TryGetProperty("maxItems",out _),"Selection is empty or has an arbitrary model-size ceiling.");
            Need(branch.GetProperty("anyOf").GetArrayLength()==2&&branch.GetProperty("dependentRequired").GetProperty("constraint_ids")[0].GetString()=="dimensions_visible","Conditional input intent is hidden.");
            Need(branch.GetProperty("not").GetProperty("properties").GetProperty("sketch_visible").GetProperty("const").ValueKind==JsonValueKind.False,"Conflicting show dimensions/hide sketch is published.");
            return Task.CompletedTask;
        });
        await test("block436 sketch visibility serializes unavailable annotations and parent prerequisite explicitly",()=>
        {
            var source=new EntityRef("00000000-0000-0000-0000-000000000001","00000000-0000-0000-0000-000000000002","A1");
            const string annotation="{00000000-0000-0000-0000-00000000000c}",withoutAnnotation="{00000000-0000-0000-0000-00000000000d}";
            var unavailable=new ModelSketchDimensionPresentation436(withoutAnnotation,null,null,null,"native_annotation_missing");
            var before=new ModelSketchPresentation436(sketch,new string('A',64),false,0,false,null,false,
                [new(dimension,annotation,0,false,null),unavailable],null);
            var after=new ModelSketchPresentation436(sketch,new string('B',64),false,1,true,null,false,
                [new(dimension,annotation,1,true,null),unavailable],null);
            var change=new ModelSketchVisibilityChange436(0,source,source,sketch,null,true,[dimension,withoutAnnotation],true,before,after,[dimension],[withoutAnnotation]);
            var receipt=new ModifiedEntities("00000000-0000-0000-0000-000000000003","committed",false,[source],[],[],[],"not_guaranteed",model_sketch_visibility_changes:[change]);
            var observed=Wire.Element(receipt).GetProperty("model_sketch_visibility_changes")[0];
            Need(observed.GetProperty("sketch_parent_enabled").GetBoolean()&&observed.GetProperty("before").GetProperty("native_visibility").GetDouble()==0&&observed.GetProperty("after").GetProperty("native_visibility").GetDouble()==1,"Parent prerequisite or actual numeric readback disappeared.");
            var missing=observed.GetProperty("after").GetProperty("dimensions")[1];
            Need(missing.GetProperty("dimension_id").ValueKind==JsonValueKind.Null&&missing.GetProperty("native_visibility").ValueKind==JsonValueKind.Null&&missing.GetProperty("visible").ValueKind==JsonValueKind.Null&&missing.GetProperty("unavailable_reason").GetString()=="native_annotation_missing","Missing native annotation was lost or reported visible.");
            Need(observed.GetProperty("after").GetProperty("dimensions_visible").ValueKind==JsonValueKind.Null&&!observed.GetProperty("after").GetProperty("dimensions_complete").GetBoolean()&&observed.GetProperty("unavailable_constraint_ids")[0].GetString()==withoutAnnotation,"Partial annotation availability was presented as all shown.");
            var read=ToolCatalog.Tools.Single(tool=>tool.Name=="nanocad.inspect_sketch_constraints").OutputSchema!.Value.GetProperty("properties").GetProperty("data").GetProperty("anyOf")[0];
            Need(read.GetProperty("properties").TryGetProperty("presentation",out var presentation)&&!read.GetProperty("required").EnumerateArray().Any(name=>name.GetString()=="presentation"),"Optional presentation breaks the existing constraint read contract.");
            foreach(string field in new[]{"in_edit_mode","native_visibility","sketch_visible","dimensions_visible","dimensions_complete","dimensions","unavailable_reason"})
                Need(presentation.GetProperty("properties").TryGetProperty(field,out _),"Observed presentation field is unpublished: "+field);
            var schema=ModelSketchVisibilitySchema436.Dimension();
            Need(schema["allOf"]![0]!["then"]!["properties"]!["visible"]!["type"]!.GetValue<string>()=="null","Schema permits a null native annotation to claim visibility.");
            return Task.CompletedTask;
        });
    }
}
