using NanoCAD.Contracts;
using NanoCAD.McpServer;

internal static class AnnotationTests
{
    public static async Task Run(Func<string,Func<Task>,Task> test)
    {
        EntitySpec[] Parse(object entity) => CreateValidation.ParseEntities(Wire.Element(new[] { entity }),allowDimensions:true);
        void Reject(object entity) { try { Parse(entity); } catch(DomainException) { return; } throw new Exception("Expected rejected annotation."); }
        await test("annotation creation rejects crossing polygons ragged tables and zero leader segments", () =>
        {
            var hatch=new { type="Hatch",boundary=new[] {new[] {0,0},new[] {20,0},new[] {20,10},new[] {0,10}} };
            if(Parse(hatch)[0] is not HatchSpec) throw new Exception("Hatch route lost.");
            Reject(new { type="Hatch",boundary=new[] {new[] {0,0},new[] {20,10},new[] {0,10},new[] {20,0}} });
            Reject(new { type="Table",position=new[] {0,0},table_style="Standard",row_height=10,column_width=20,cells=new[] {new[] {"A","B"},new[] {"C"}} });
            Reject(new { type="MLeader",vertices=new[] {new[] {0,0},new[] {0,0}},text_position=new[] {10,10},text="label",text_height=2.5,mleader_style="Standard" });
            var variants=BlockWriteSchema.Define().GetProperty("properties").GetProperty("entities").GetProperty("items").GetProperty("oneOf");
            if(variants.EnumerateArray().Any(v=>v.GetProperty("properties").GetProperty("type").GetProperty("const").GetString() is "Hatch" or "Table" or "MLeader")) throw new Exception("Annotations leaked into block schema.");
            return Task.CompletedTask;
        });
        await test("annotation reading exposes bounded content families without new tools", () =>
        {
            var args=Validation.Parse("query_objects",Wire.Element(new { document_id=Guid.NewGuid().ToString("D"),types=new[] {"MLeader","Table","Hatch"} }));
            if(args.Types!.Length!=3 || ToolCatalog.Tools.Count!=ToolCatalog.OperationNames.Length) throw new Exception("Read surface changed unexpectedly.");
            var defs=ToolCatalog.Tools.Single(t=>t.Name=="nanocad.inspect_objects").OutputSchema!.Value.GetProperty("$defs");
            foreach(string key in new[] {"mleader","table","hatch"}) if(!defs.TryGetProperty(key,out _)) throw new Exception("Missing annotation schema.");
            return Task.CompletedTask;
        });
    }
}

