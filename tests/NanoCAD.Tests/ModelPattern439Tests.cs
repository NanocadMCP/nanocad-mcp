using System.Text.Json.Nodes;
using NanoCAD.Contracts;
using NanoCAD.McpServer;

internal static class ModelPattern439Tests
{
    public static async Task Run(Func<string,Func<Task>,Task> test)
    {
        string session=Guid.NewGuid().ToString(),source=Guid.NewGuid().ToString("B"),pattern=Guid.NewGuid().ToString("B"),axisId=Guid.NewGuid().ToString("B");
        JsonObject Fixed()=>new(){["kind"]="fixed_axis",["origin"]=new JsonArray(100,0,0),["direction"]=new JsonArray(0,0,1)};
        JsonObject Rect(bool update=false)=>new(){["operation"]=update?"set_model_rectangular_pattern":"add_model_rectangular_pattern",["handles"]=new JsonArray("AB"),["expected_last_item_id"]=pattern,
            ["count_x"]=3,["count_y"]=2,["step_x"]=10,["step_y"]=15};
        JsonObject Circle(bool update=false)=>new(){["operation"]=update?"set_model_circular_pattern":"add_model_circular_pattern",["handles"]=new JsonArray("AB"),["expected_last_item_id"]=pattern,
            ["definition"]=new JsonObject{["count"]=4,["angle_degrees"]=-90}};
        ModifyArguments Parse(JsonObject request)=>ModifyValidation.Parse(Wire.Element(new{bridge_instance_id=session,document_id=session,expected_revision="1",operation_id=session,units="Millimeters",operations=new[]{request.DeepClone()}}));
        void Reject(JsonObject request){try{Parse(request);}catch(DomainException){return;}throw new Exception("Invalid pattern input accepted.");}
        await test("block439 native pattern selectors retain legacy fields and reject ambiguous bindings",()=>
        {
            var request=Rect();request["source_feature_id"]=source;request["direction_x"]=Fixed();request["direction_y"]=new JsonObject{["kind"]="global_axis",["axis"]="Y"};
            var edit=(ModelPatternSpec)Parse(request).Operations.Single();
            if(edit.DirectionX?.GetProperty("kind").GetString()!="fixed_axis"||edit.DirectionY?.GetProperty("axis").GetString()!="Y")throw new Exception("Selector lost.");
            request["direction_x_handle"]="10";Reject(request);request.Remove("direction_x_handle");
            request["direction_x"]!["direction"]=new JsonArray(0,0,0);Reject(request);
            request["direction_x"]=new JsonObject{["kind"]="work_axis",["axis_id"]=axisId};Parse(request);
            request["direction_x"]!["axis_id"]="invented";Reject(request);
            return Task.CompletedTask;
        });
        await test("block439 rectangular direction retarget requires the observed exact binding IDs",()=>
        {
            var request=Rect(true);request["feature_id"]=pattern;request["expected_count_x"]=3;request["expected_count_y"]=2;request["expected_step_x"]=10;request["expected_step_y"]=15;
            request["expected_source_feature_ids"]=new JsonArray(source);request["direction_x"]=Fixed();Reject(request);
            request["expected_direction_x_id"]=axisId;
            var edit=(ModelPatternSpec)Parse(request).Operations.Single();if(edit.ExpectedDirectionXId!=axisId.ToUpperInvariant())throw new Exception("Expected native ID lost.");
            request.Remove("direction_x");Reject(request);return Task.CompletedTask;
        });
        await test("block439 circular global fixed and legacy edge selectors share one compatible contract",()=>
        {
            var request=Circle();var definition=request["definition"]!.AsObject();definition["source_feature_ids"]=new JsonArray(source);definition["axis"]=Fixed();
            if(Parse(request).Operations.Single() is not ModelManufacturingEdit)throw new Exception("Legacy manufacturing route changed.");
            definition["axis"]=new JsonObject{["kind"]="global_axis",["axis"]="Z"};Parse(request);
            definition["axis_edge_id"]=axisId;definition["edge_token"]=new string('A',64);Reject(request);
            definition.Remove("axis");Parse(request);definition.Remove("edge_token");Reject(request);
            return Task.CompletedTask;
        });
        await test("block439 circular retarget guards original sources axis and formula replacement",()=>
        {
            var request=Circle(true);request["feature_id"]=pattern;var definition=request["definition"]!.AsObject();
            definition["axis"]=Fixed();Reject(request);definition["expected_axis_id"]=axisId;
            definition["source_feature_ids"]=new JsonArray(source);Reject(request);
            definition["expected_source_feature_ids"]=new JsonArray(source);definition["replace_driver_formulas"]=true;
            definition["expected_count"]=3;definition["expected_angle_degrees"]=-120;Parse(request);
            definition["source_feature_ids"]=new JsonArray(pattern);Reject(request);
            definition["source_feature_ids"]=new JsonArray(source);definition["replace_driver_formulas"]="true";Reject(request);
            return Task.CompletedTask;
        });
        await test("block439 native counts are per-axis Int32 without a Cartesian product ceiling",()=>
        {
            var request=Rect();request["source_feature_id"]=source;request["direction_x_handle"]="10";request["direction_y_handle"]="11";
            request["count_x"]=int.MaxValue;request["count_y"]=int.MaxValue;request["step_x"]=-0.00001;Parse(request);
            request["count_x"]=2147483648L;Reject(request);request["count_x"]=0;Reject(request);
            request=Circle();request["definition"]!["source_feature_ids"]=new JsonArray(source);request["definition"]!["axis"]=Fixed();
            foreach(double angle in new[]{-450d,0d,0.0000001d,360d}){request["definition"]!["angle_degrees"]=angle;Parse(request);}
            request["definition"]!["count"]=0;Reject(request);return Task.CompletedTask;
        });
        await test("block439 actual tool schema publishes selectors expected bindings and typed receipts",()=>
        {
            var tool=ToolCatalog.Tools.Single(tool=>tool.Name=="nanocad.modify_entities");
            var branches=tool.InputSchema.GetProperty("properties").GetProperty("operations").GetProperty("items").GetProperty("oneOf").EnumerateArray().ToArray();
            var rect=branches.Single(branch=>branch.GetProperty("properties").GetProperty("operation").GetProperty("const").GetString()=="set_model_rectangular_pattern");
            var circle=branches.Single(branch=>branch.GetProperty("properties").GetProperty("operation").GetProperty("const").GetString()=="set_model_circular_pattern");
            if(!rect.GetRawText().Contains("expected_direction_x_id")||!circle.GetRawText().Contains("expected_axis_id")||!circle.GetRawText().Contains("fixed_axis"))throw new Exception("Published selectors/preconditions missing.");
            if(!tool.OutputSchema!.Value.GetRawText().Contains("spacing_mode")||!tool.OutputSchema.Value.GetRawText().Contains("gs_marker"))throw new Exception("Typed native binding receipt missing.");
            return Task.CompletedTask;
        });
        await test("block439b native axis anchor release needs unchanged ordered retained history",()=>
        {
            string[] before=["body","join","cut","old-axis","pattern"],after=["body","join","cut","new-axis","pattern"];
            if(!NanoCADBridge.ModelPatternAxisOrder439.ProvesRelease("pattern","old-axis",null,["old-axis"],["new-axis"],before,after)||
                !NanoCADBridge.ModelPatternAxisOrder439.ProvesRelease("pattern","old-axis","new-axis",["old-axis"],["new-axis"],before,after))throw new Exception("Exact native anchor release was rejected.");
            if(!NanoCADBridge.ModelPatternAxisOrder439.ProvesRelease("pattern","old-axis",null,["x-axis","old-axis"],["x-axis","new-axis"],
                ["body","join","cut","x-axis","old-axis","pattern"],["body","join","cut","x-axis","new-axis","pattern"]))throw new Exception("The retained first direction was not preserved.");
            return Task.CompletedTask;
        });
        await test("block439b native anchor proof rejects source loss reordering and unrelated edges",()=>
        {
            string[] before=["body","join","cut","old-axis","pattern"];
            bool Proof(string[] after,string? prior="old-axis",string? current=null)=>NanoCADBridge.ModelPatternAxisOrder439.ProvesRelease("pattern",prior,current,["old-axis"],["new-axis"],before,after);
            string[] valid=["body","join","cut","new-axis","pattern"];
            if(Proof(["body","cut","new-axis","pattern"])||Proof(["body","cut","join","new-axis","pattern"])||
                Proof(["body","join","cut","foreign-feature","new-axis","pattern"])||Proof(valid,"cut")||Proof(valid,null)||Proof(valid,current:"cut")||
                Proof(["body","join","cut","pattern","new-axis"])||Proof(["body","join","cut","new-axis","pattern","pattern"])||
                Proof(["body","old-axis","join","cut","new-axis","pattern"]))throw new Exception("A meaningful history change was admitted as axis release.");
            if(NanoCADBridge.ModelPatternAxisOrder439.ProvesRelease("pattern","old-axis",null,["old-axis","old-axis"],["new-axis","old-axis"],before,valid))throw new Exception("An axis still bound by another direction was treated as released.");
            return Task.CompletedTask;
        });
    }
}
