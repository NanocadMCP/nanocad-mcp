using System.Text.Json.Nodes;
using NanoCAD.Contracts;
using NanoCAD.McpServer;

internal static class DrawingCircleTests
{
    internal static async Task Run(Func<string,Func<Task>,Task> test)
    {
        var id=Guid.NewGuid().ToString("D");
        JsonObject Input(string operation)=>new(){["bridge_instance_id"]=id,["document_id"]=id,["expected_revision"]="1",["operation_id"]=id,["units"]="Millimeters",
            ["operations"]=new JsonArray(new JsonObject{["operation"]=operation,["handles"]=new JsonArray("00aB"),["expected_radius"]=10})};
        JsonObject Edit(JsonObject input)=>input["operations"]![0]!.AsObject();
        void Deny(JsonObject input){try{ModifyValidation.Parse(Wire.Element(input));}catch(DomainException){return;}throw new Exception("Invalid drawing-circle write accepted");}
        await test("drawing circle driver binds exact source radius constraint and expression",()=>{
            var add=ModifyValidation.Parse(Wire.Element(Input("add_drawing_radius")));if(add.Operations[0] is not DrawingCircleEdit {Action:"add_drawing_radius",ExpectedRadius:10} value||value.Handles.Single()!="AB")throw new Exception("Source context changed");
            var input=Input("set_drawing_radius");var edit=Edit(input);edit["constraint_id"]="{"+id+"}";edit["expected_expression"]="10";edit["radius"]=15;
            if(ModifyValidation.Parse(Wire.Element(input)).Operations[0] is not DrawingCircleEdit {Radius:15,ExpectedExpression:"10"})throw new Exception("Driver preconditions lost");
            foreach(var field in new[]{"constraint_id","expected_expression","expected_radius"}){var bad=input.DeepClone().AsObject();Edit(bad).Remove(field);Deny(bad);}
            foreach(var radius in new[]{0d,-1,1e6+1}){var bad=input.DeepClone().AsObject();Edit(bad)["radius"]=radius;Deny(bad);}
            edit["constraint_id"]="{00000000-0000-0000-0000-000000000000}";Deny(input);return Task.CompletedTask;
        });
        await test("drawing circle extrusion validates direction size and isolated operation",()=>{
            var input=Input("extrude_drawing_circle");var edit=Edit(input);edit["distance"]=20;edit["direction"]="negative";edit["name"]="Cylinder";
            if(ModifyValidation.Parse(Wire.Element(input)).Operations[0] is not DrawingCircleEdit {Distance:20,Direction:"negative",Name:"Cylinder"})throw new Exception("Extrusion shape changed");
            var bad=input.DeepClone().AsObject();Edit(bad)["handles"]=new JsonArray("A","B");Deny(bad);
            bad=input.DeepClone().AsObject();Edit(bad)["direction"]="automatic";Deny(bad);
            bad=input.DeepClone().AsObject();Edit(bad)["distance"]=0;Deny(bad);
            bad=input.DeepClone().AsObject();bad["operations"]!.AsArray().Add(Edit(input).DeepClone());Deny(bad);
            bad=Input("add_drawing_radius");Edit(bad)["radius"]=12;Deny(bad);return Task.CompletedTask;
        });
        await test("drawing circle operations are discoverable with explicit recovery scope",()=>{
            var tool=ToolCatalog.Tools.Single(t=>t.Name=="nanocad.modify_entities");var schema=tool.InputSchema.GetRawText();
            foreach(var action in new[]{"add_drawing_radius","set_drawing_radius","extrude_drawing_circle"})if(!schema.Contains(action))throw new Exception("Missing driver operation");
            if(!schema.Contains("ROLLBACK_NOT_GUARANTEED")||!tool.OutputSchema!.Value.GetRawText().Contains("drawing_circle_changes"))throw new Exception("Missing outcome metadata");
            return Task.CompletedTask;
        });
    }
}
