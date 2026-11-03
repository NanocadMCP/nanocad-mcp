using NanoCAD.Contracts;

internal static class ModelScaleTests
{
    private sealed record Item(ulong Handle);
    public static async Task Run(Func<string,Func<Task>,Task> test)
    {
        await test("model scale views accept full model scope and arbitrary explicit body sets",()=>
        {
            var edit=DrawingViewCreateTests.Edit();edit.Remove("source_handle");edit["source_scope"]="all_model_solids";
            StructureWriteArguments Parse()=>StructureWriteValidation.Parse(Wire.Element(DrawingViewCreateTests.Input(edit)));
            var all=Parse();if(((CreateDrawingViewsEdit)all.Operations.Single()).SourceScope!="all_model_solids")throw new Exception("Lost model scope.");
            edit.Remove("source_scope");edit["source_handles"]=Enumerable.Range(1,1200).Select(i=>i.ToString("X")).ToArray();
            var subset=Parse();if(((CreateDrawingViewsEdit)subset.Operations.Single()).SourceHandles!.Length!=1200)throw new Exception("Truncated bodies.");
            edit["source_handles"]=((string[])edit["source_handles"]).Reverse().ToArray();
            if(Parse().Fingerprint()!=subset.Fingerprint()||subset.Fingerprint()==all.Fingerprint())throw new Exception("Source identity mismatch.");
            foreach(var invalid in new object[]{Array.Empty<string>(),new[]{"71","0071"},new object[]{17}})
            {edit["source_handles"]=invalid;try{Parse();throw new Exception("Invalid source set accepted.");}catch(DomainException){}}
            edit["source_handles"]=new[]{"71"};edit["source_scope"]="all_model_solids";
            try{Parse();throw new Exception("Ambiguous scope accepted.");}catch(DomainException){}
            return Task.CompletedTask;
        });
        await test("model scale page cache indexes a million handles without reading a million summaries",()=>
        {
            var pages=new HandlePages<Item>();int reads=0;
            Item Read(ulong id){reads++;return new(id);}
            var page=pages.First("doc","rev",null,"filter",Enumerable.Range(1,1_000_000).Select(i=>(ulong)i),100,Read);
            if(reads!=100||page.items[0].Handle!=1||page.items[^1].Handle!=100||page.complete||page.total_count!=null)throw new Exception("First page materialized or misrepresented full drawing.");
            var next=pages.Next(page.next_cursor!,"doc","rev",null,"filter",100,Read);
            if(reads!=200||next.items[0].Handle!=101)throw new Exception("Continuation restarted scanning.");
            try{pages.Next(next.next_cursor!,"doc","changed",null,"filter",100,Read);throw new Exception("Stale cursor accepted.");}catch(DomainException e)when(e.Error.code=="STALE_DOCUMENT"){}
            return Task.CompletedTask;
        });
        await test("model scale filtered pagination yields continuations and exact final totals",()=>
        {
            var pages=new HandlePages<Item>();int reads=0;
            Item? Read(ulong id){reads++;return id%997==0?new(id):null;}
            bool Yield()=>reads%300==0;
            var page=pages.First("doc","rev","sel","filter",Enumerable.Range(1,30000).Select(i=>(ulong)i),100,Read,Yield);
            var results=new List<ulong>();int empty=0;
            while(true)
            {
                results.AddRange(page.items.Select(i=>i.Handle));if(page.items.Length==0)empty++;
                if(page.complete)break;
                page=pages.Next(page.next_cursor!,"doc","rev","sel","filter",100,Read,Yield);
            }
            if(reads!=30000||empty==0||page.total_count!=30||!results.SequenceEqual(Enumerable.Range(1,30).Select(i=>(ulong)(i*997))))throw new Exception("Lost, repeated or miscounted filtered results.");
            return Task.CompletedTask;
        });
    }
}
