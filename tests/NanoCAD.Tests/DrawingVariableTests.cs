using System.Text.Json.Nodes;
using NanoCAD.Contracts;
using NanoCAD.McpServer;

internal static class DrawingVariableTests
{
    internal static async Task Run(Func<string,Func<Task>,Task> test)
    {
        void Deny(Action action){try{action();}catch(DomainException){return;}throw new Exception("Unsafe drawing variable plan accepted");}
        ModelExpressionVariable[] Inventory()=>new[]{new ModelExpressionVariable("A","Size","10",10,new[]{"B","C"},false),new("B","Width","Size*2",20,new[]{"D"}),new("C","Radius","Size/2",5,Array.Empty<string>()),new("D","Height","Width/2",10,Array.Empty<string>())};
        await test("drawing variables propagate a shared scalar through independent dimensions",()=>{
            var vars=Inventory();DrawingVariablePlanning.VerifyConsumers(vars);var plan=ModelExpressionPlanning.Prepare(vars,"A","12",true);
            if(plan.ExpectedValues["B"]!=24||plan.ExpectedValues["C"]!=6||plan.ExpectedValues["D"]!=12||plan.AffectedIds.Length!=4)throw new Exception("Linked drawing values lost");
            Deny(()=>ModelExpressionPlanning.Prepare(vars,"A","-3",true));Deny(()=>ModelExpressionPlanning.Prepare(vars,"A","1/0",true));return Task.CompletedTask;
        });
        await test("drawing variables reject native dependency disagreement and cycles",()=>{
            var vars=Inventory();vars[0]=vars[0] with{DownstreamVariables=new[]{"B"}};Deny(()=>DrawingVariablePlanning.VerifyConsumers(vars));
            vars=Inventory();vars[0]=vars[0] with{Expression="Height"};Deny(()=>DrawingVariablePlanning.Consumers(vars));
            Deny(()=>ModelExpressionPlanning.Prepare(Inventory(),"A","Width+1",true));return Task.CompletedTask;
        });
        await test("drawing variable names require exact unambiguous references and preserve token boundaries",()=>{
            if(!DrawingVariablePlanning.IsDimensionType("User, Dim2DConstraint")||!DrawingVariablePlanning.IsDimensionType("User, Reference, Dim2DConstraint")||DrawingVariablePlanning.IsDimensionType("User")||DrawingVariablePlanning.IsDimensionType("Model3D, Dim2DConstraint"))throw new Exception("Dimensional native flags misclassified");
            var vars=Inventory();vars[2]=vars[2] with{Name="width"};Deny(()=>DrawingVariablePlanning.Consumers(vars));
            vars=Inventory();vars[1]=vars[1] with{Expression="size*2"};Deny(()=>DrawingVariablePlanning.Consumers(vars));
            string renamed=ModelArithmetic.Parse("Size+Size2+2").RenameReference("Size","BaseSize");
            if(!renamed.Contains("BaseSize")||!renamed.Contains("Size2")||renamed.Contains("BaseSize2"))throw new Exception("Rename altered an unrelated token");return Task.CompletedTask;
        });
        await test("drawing variable edits enforce exact preconditions and the sole operation boundary",()=>{
            var id=Guid.NewGuid().ToString("D");JsonObject Input(JsonObject op)=>new(){["bridge_instance_id"]=id,["document_id"]=id,["expected_revision"]="1",["operation_id"]=id,["units"]="Millimeters",["operations"]=new JsonArray(op)};
            var op=new JsonObject{["operation"]="set_drawing_expression",["handles"]=new JsonArray("A"),["variable_id"]="{"+id+"}",["expected_expression"]="20",["expected_value"]=20,["expression"]="Size*2"};
            var input=Input(op);if(ModifyValidation.Parse(Wire.Element(input)).Operations.Single() is not DrawingProfileEdit{Expression:"Size*2"})throw new Exception("Expression not parsed");
            input["operations"]!.AsArray().Add(op.DeepClone());Deny(()=>ModifyValidation.Parse(Wire.Element(input)));input["operations"]!.AsArray().RemoveAt(1);
            op.Remove("expected_value");Deny(()=>ModifyValidation.Parse(Wire.Element(input)));
            var create=Input(new(){["operation"]="create_drawing_variable",["handles"]=new JsonArray("A"),["name"]="Offset",["value"]=-5});ModifyValidation.Parse(Wire.Element(create));
            create["operations"]![0]!["name"]="A+B";Deny(()=>ModifyValidation.Parse(Wire.Element(create)));return Task.CompletedTask;
        });
        await test("drawing variable inventory is optional boolean and catalog provides the linked workflow",()=>{
            var id=Guid.NewGuid().ToString("D");var input=new JsonObject{["bridge_instance_id"]=id,["document_id"]=id,["expected_revision"]="1",["handles"]=new JsonArray("A"),["include_variables"]=true};
            if(!Validation.Parse("inspect_constraints",Wire.Element(input)).IncludeVariables)throw new Exception("Variable flag lost");input["include_variables"]="true";Deny(()=>Validation.Parse("inspect_constraints",Wire.Element(input)));
            var read=ToolCatalog.Tools.Single(t=>t.Name=="nanocad.inspect_constraints");if(!read.OutputSchema!.Value.GetRawText().Contains("variable_inventory"))throw new Exception("Missing variable output");
            var schema=ToolCatalog.Tools.Single(t=>t.Name=="nanocad.modify_entities").InputSchema.GetRawText();foreach(var name in new[]{"create_drawing_variable","name_drawing_parameter","set_drawing_expression"})if(!schema.Contains(name))throw new Exception("Missing variable operation");return Task.CompletedTask;
        });
    }
}
