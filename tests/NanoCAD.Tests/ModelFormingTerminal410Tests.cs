using NanoCAD.Contracts;

internal static class ModelFormingTerminal410Tests
{
    public static async Task Run(Func<string,Func<Task>,Task> test)
    {
        // The full native inventory includes actual intermediate construction
        // and an unassigned construction item. Its array order has no meaning.
        ModelSequenceLink[] History()=>[new("C","PC"),new("TAIL","C"),new("SB","A"),new("SA",null),
            new("A","SA"),new("PC","SC"),new("UNASSIGNED",null),new("SC","B"),new("B","SB")];
        string[] Features()=>["C","A","B"];
        void Need(bool condition,string message){if(!condition)throw new Exception(message);}
        void Ordered(ModelSequenceLink[] history,string[] features,string last)
        {
            Need(ModelFeatureSequence.FromHistory(history,features,"A",last).SequenceEqual(new[]{"A","B","C"}),
                "Actual predecessor links did not derive the forming tip and complete base-to-tip chain.");
        }
        void Refused(ModelSequenceLink[] history,string[] features,string last)
        {
            try{ModelFeatureSequence.FromHistory(history,features,"A",last);}
            catch(DomainException error)when(error.Error.code=="MODEL_ORDER_UNAVAILABLE"){return;}
            throw new Exception("Ambiguous or broken actual predecessor history was accepted.");
        }
        await test("410 forming terminal trailing assigned sketch retains the actual forming tip",()=>
        {
            var history=History();Ordered(history,Features(),"TAIL");
            // Both arrays are independently reordered: no first/last inventory
            // item or supplied feature-ID position can define the sequence.
            Ordered(history.Reverse().ToArray(),Features().Reverse().ToArray(),"TAIL");
            var bridge=history.Append(new ModelSequenceLink("TAIL_PROFILE","TAIL")).ToArray();Ordered(bridge,Features(),"TAIL_PROFILE");
            return Task.CompletedTask;
        });
        await test("410 forming terminal unassigned known construction admits a unique explicit forming chain",()=>
        {
            Ordered(History(),Features(),"UNASSIGNED");
            var history=History().OrderBy(row=>row.Id,StringComparer.Ordinal).ToArray();
            Ordered(history,["B","C","A"],"UNASSIGNED");return Task.CompletedTask;
        });
        await test("410 forming terminal assigned terminal must match the uniquely derived tip",()=>
        {
            var history=History();Ordered(history,Features(),"C");
            Refused(history,Features(),"A");Refused(history,Features(),"B");
            var wrongAssigned=history.Select(row=>row.Id=="TAIL"?row with{Previous="B"}:row).ToArray();
            Refused(wrongAssigned,Features(),"TAIL");return Task.CompletedTask;
        });
        await test("410 forming terminal fork and missing predecessor cannot be guessed from inventory",()=>
        {
            var history=History();
            var fork=history.Select(row=>row.Id=="SC"?row with{Previous="A"}:row).ToArray();Refused(fork,Features(),"UNASSIGNED");
            var missing=history.Select(row=>row.Id=="PC"?row with{Previous="MISSING"}:row).ToArray();Refused(missing,Features(),"TAIL");
            Refused(history.Where(row=>row.Id!="B").ToArray(),Features(),"TAIL");Refused(history,Features(),"ABSENT_TERMINAL");
            return Task.CompletedTask;
        });
        await test("410 forming terminal forming construction and trailing cycles are refused",()=>
        {
            var history=History();
            var formingCycle=history.Select(row=>row.Id=="SA"?row with{Previous="C"}:row).ToArray();Refused(formingCycle,Features(),"TAIL");
            var constructionCycle=history.Select(row=>row.Id=="SB"?row with{Previous="SB_CYCLE"}:row).Append(new ModelSequenceLink("SB_CYCLE","SB")).ToArray();
            Refused(constructionCycle,Features(),"TAIL");
            var trailingCycle=history.Select(row=>row.Id=="TAIL"?row with{Previous="TAIL_CYCLE"}:row).Append(new ModelSequenceLink("TAIL_CYCLE","TAIL")).ToArray();
            Refused(trailingCycle,Features(),"TAIL");return Task.CompletedTask;
        });
    }
}
