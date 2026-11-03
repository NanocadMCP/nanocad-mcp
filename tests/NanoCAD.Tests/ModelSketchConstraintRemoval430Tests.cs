using System.Text.Json.Nodes;
using NanoCAD.Contracts;
using NanoCAD.McpServer;

internal static class ModelSketchConstraintRemoval430Tests
{
    internal static async Task Run(Func<string,Func<Task>,Task> test)
    {
        static void Need(bool value,string message){if(!value)throw new Exception(message);}
        static void Reject(Action action){try{action();}catch(DomainException){return;}throw new Exception("An unscoped or ambiguous destructive request was accepted.");}
        const string sketch="{00000000-0000-0000-0000-000000000001}",relation="{00000000-0000-0000-0000-000000000002}";
        JsonObject Request()=>new(){["operation"]="delete_sketch_constraints",["handles"]=new JsonArray("A1"),["sketch_id"]=sketch,["expected_binding_token"]=new string('A',64),["constraint_ids"]=new JsonArray(relation)};
        await test("block430 constraint removal requires explicit native relation identities and current sketch binding",()=>
        {
            var accepted=(ModelSketchConstraintRemoval430)ModelSketchConstraintRemovalValidation430.Parse(Wire.Element(Request()),"$",["A1"]);
            Need(accepted.SketchId==sketch&&accepted.ConstraintIds.SequenceEqual([relation]),"Native selection changed.");
            var duplicate=Request();duplicate["constraint_ids"]=new JsonArray(relation,relation);Reject(()=>ModelSketchConstraintRemovalValidation430.Parse(Wire.Element(duplicate),"$",["A1"]));
            var alias=Request();alias["constraint_ids"]=new JsonArray("dia1");Reject(()=>ModelSketchConstraintRemovalValidation430.Parse(Wire.Element(alias),"$",["A1"]));
            Reject(()=>ModelSketchConstraintRemovalValidation430.Batch([accepted,new EraseSpec(["A1"])]));
            return Task.CompletedTask;
        });
        await test("block430 constraint removal publishes selection and actual unresolved-model outcome",()=>
        {
            var schema=ModelSketchConstraintRemovalSchema430.Operation();Need(schema["properties"]!["operation"]!["const"]!.GetValue<string>()=="delete_sketch_constraints","Wrong operation.");
            Need(schema["properties"]!["constraint_ids"]!["items"]!["pattern"]!=null,"Native IDs are hidden.");
            var change=ModelSketchConstraintRemovalSchema430.Change();Need(change["properties"]!["model_healthy"]!=null&&change["properties"]!["unresolved_history_ids"]!=null,"Actual result health is missing.");
            return Task.CompletedTask;
        });
    }
}
