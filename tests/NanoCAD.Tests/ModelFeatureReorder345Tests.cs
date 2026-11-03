using NanoCAD.Contracts;
using NanoCAD.McpServer;

internal static class ModelFeatureReorder345Tests
{
    public static async Task Run(Func<string,Func<Task>,Task> test)
    {
        static void Need(bool ok){if(!ok)throw new Exception("Native feature order345 contract assertion failed.");}
        static void Reject(Action action){try{action();}catch(DomainException){return;}throw new Exception("An invalid native order request was admitted.");}
        static string Id(int index)=>new Guid(index,0,0,new byte[8]).ToString("B").ToUpperInvariant();
        await test("345 reorder predecessor semantics preserve base and terminal through nonfeature nodes",()=>
        {
            // Native inventory order is deliberately shuffled. Sketch/profile
            // nodes sit between forming items; inventory order is not history.
            var history=new[]{new ModelSequenceLink(Id(5),Id(4)),new ModelSequenceLink(Id(2),Id(1)),new ModelSequenceLink(Id(1),null),
                new ModelSequenceLink(Id(6),Id(5)),new ModelSequenceLink(Id(4),Id(3)),new ModelSequenceLink(Id(3),Id(2))};
            var actual=ModelFeatureReorderPlanning345.FeatureOrder(history,[Id(6),Id(1),Id(4),Id(2)],Id(6));
            Need(actual.SequenceEqual([Id(1),Id(2),Id(4),Id(6)]));
            var moved=ModelFeatureReorderPlanning345.MoveAfter(actual,Id(2),Id(4));
            Need(moved.SequenceEqual([Id(1),Id(4),Id(2),Id(6)]));
            Reject(()=>ModelFeatureReorderPlanning345.MoveAfter(actual,Id(1),Id(4)));
            Reject(()=>ModelFeatureReorderPlanning345.MoveAfter(actual,Id(6),Id(2)));
            Reject(()=>ModelFeatureReorderPlanning345.MoveAfter(actual,Id(4),Id(6)));
            Reject(()=>ModelFeatureReorderPlanning345.MoveAfter(actual,Id(4),Id(2)));
            Reject(()=>ModelFeatureReorderPlanning345.FeatureOrder([new(Id(1),Id(2)),new(Id(2),Id(1))],[Id(1),Id(2)],Id(2)));
            return Task.CompletedTask;
        });
        await test("374 terminal order opt-in retains base and derives the actual resulting terminal",()=>
        {
            var before=new[]{Id(1),Id(2),Id(3)};
            // Two retained consumers may swap only by explicit opt-in. Native
            // eligibility, not this pure ordering plan, proves independence.
            var earlier=ModelFeatureReorderPlanning345.MoveAfter(before,Id(3),Id(1),allowTerminal:true);
            Need(earlier.SequenceEqual([Id(1),Id(3),Id(2)])&&earlier[^1]==Id(2));
            var later=ModelFeatureReorderPlanning345.MoveAfter(before,Id(2),Id(3),allowTerminal:true);
            Need(later.SequenceEqual([Id(1),Id(3),Id(2)])&&later[^1]==Id(2));
            Reject(()=>ModelFeatureReorderPlanning345.MoveAfter(before,Id(1),Id(3),allowTerminal:true));
            Reject(()=>ModelFeatureReorderPlanning345.MoveAfter(before,Id(3),Id(2),allowTerminal:true));
            return Task.CompletedTask;
        });
        await test("374 explicit resulting-terminal contract is current native identity and optional for old fixed-terminal requests",()=>
        {
            var value=Wire.Element(new{operation="reorder_model_feature",handles=new[]{"A1"},feature_id=Id(3),predecessor_id=Id(1),
                expected_current_predecessor_id=Id(2),expected_last_item_id=Id(3),expected_resulting_last_item_id=Id(2)});
            Need(ModelFeatureReorderValidation345.Parse(value,"$",["A1"]) is ModelFeatureReorderSpec345{ExpectedResultingLastItemId:var last}&&last==Id(2));
            var empty=Wire.Element(new{operation="reorder_model_feature",handles=new[]{"A1"},feature_id=Id(3),predecessor_id=Id(1),
                expected_current_predecessor_id=Id(2),expected_last_item_id=Id(3),expected_resulting_last_item_id="{00000000-0000-0000-0000-000000000000}"});
            Reject(()=>ModelFeatureReorderValidation345.Parse(empty,"$",["A1"]));
            var schema=ModelFeatureReorderSchema345.Operation();
            Need(schema["properties"]!["expected_resulting_last_item_id"]!=null&&
                !schema["required"]!.AsArray().Any(item=>item!.GetValue<string>()=="expected_resulting_last_item_id"));
            return Task.CompletedTask;
        });
        await test("345 complete larger native sequence and cancellation retain sparse assigned-link semantics",()=>
        {
            var links=Enumerable.Range(1,2048).Select(index=>new ModelSequenceLink(Id(index),index==1?null:Id(index-1))).Reverse().ToArray();
            var features=Enumerable.Range(1,2048).Select(Id).Reverse().ToArray();
            var order=ModelFeatureReorderPlanning345.FeatureOrder(links,features,Id(2048));
            var result=ModelFeatureReorderPlanning345.MoveAfter(order,Id(3),Id(2000));
            Need(result[0]==Id(1)&&result[^1]==Id(2048)&&Array.IndexOf(result,Id(3))==Array.IndexOf(result,Id(2000))+1&&result.ToHashSet().SetEquals(order));
            bool stopped=false;try{ModelFeatureReorderPlanning345.FeatureOrder(links,features,Id(2048),()=>throw new OperationCanceledException());}
            catch(OperationCanceledException){stopped=true;}Need(stopped);
            Reject(()=>ModelFeatureReorderPlanning345.FeatureOrder([new(Id(1),null),new(Id(2),Id(999))],[Id(1),Id(2)],Id(2)));
            return Task.CompletedTask;
        });
        await test("345 public current native order preconditions and sole operation match publication",()=>
        {
            var value=Wire.Element(new{operation="reorder_model_feature",handles=new[]{"A1"},feature_id=Id(2),predecessor_id=Id(3),expected_current_predecessor_id=Id(9),expected_last_item_id=Id(4)});
            var parsed=ModelFeatureReorderValidation345.Parse(value,"$",["A1"]);
            Need(parsed is ModelFeatureReorderSpec345{FeatureId:var id}&&id==Id(2));
            Reject(()=>ModelFeatureReorderValidation345.Parse(value,"$",["A1","A2"]));
            Reject(()=>ModelFeatureReorderValidation345.Batch([parsed,new EraseSpec(["B1"])]));
            var schema=ModelFeatureReorderSchema345.Operation();Need(schema["properties"]!["operation"]!["const"]!.GetValue<string>()=="reorder_model_feature");
            Need(ModelFeatureReorderSchema345.ChangeOutput(new(){["type"]="object"}).ToJsonString().Contains("order_changes"));
            return Task.CompletedTask;
        });
    }
}
