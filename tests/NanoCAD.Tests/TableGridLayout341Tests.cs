using NanoCAD.Contracts;
using NanoCAD.McpServer;

internal static class TableGridLayout341Tests
{
    public static async Task Run(Func<string,Func<Task>,Task> test)
    {
        string token=new('A',64);
        TableGridLayoutEdit341 Parse(object[] grids)=>TableGridLayoutValidation341.Parse(Wire.Element(new{
            operation="set_table_grid_layout",handles=new[]{"A1"},table_token=token,
            cells=new[]{new{row=1,column=0,grids}}}),"$.operations[0]",new[]{"A1"});
        void Need(bool condition,string message){if(!condition)throw new Exception(message);}
        void Reject(object[] grids){try{Parse(grids);}catch(DomainException e)when(e.Error.code=="INVALID_ARGUMENT"){return;}throw new Exception("Invalid native grid request accepted.");}
        await test("block341 sparse native grid style and dormant zero spacing keep exact intent",()=>
        {
            var parsed=Parse(new object[]{new{line="HorizontalBottom",line_style="Double",double_spacing=.6},new{line="VerticalLeft",double_spacing=0.0}});
            Need(parsed.Cells[0].Grids[0] is {LineStyle:"Double",DoubleSpacing:.6}&&parsed.Cells[0].Grids[1] is {LineStyle:null,DoubleSpacing:0},"Sparse native intent lost.");
            Reject(new object[]{new{line="HorizontalBottom"}});Reject(new object[]{new{line="All",line_style="Double"}});
            Reject(new object[]{new{line="HorizontalTop",line_style="double"}});Reject(new object[]{new{line="VerticalLeft",double_spacing=-.1}});
            Reject(new object[]{new{line="HorizontalTop",line_style="Single"},new{line="HorizontalTop",double_spacing=.2}});
            Reject(new object[]{new{line="HorizontalTop",double_spacing=1e10}});
            return Task.CompletedTask;
        });
        await test("block341 opposing partially merged native borders reject incompatible shared fields",()=>
        {
            var merged=new TableCellRangeSpec(0,0,0,4);var neighbor=new TableCellRangeSpec(1,2,1,2);var away=new TableCellRangeSpec(1,5,1,5);
            Need(TableGridLayoutValidation341.SharedBorder(merged,"HorizontalBottom",neighbor,"HorizontalTop")&&
                TableGridLayoutValidation341.SharedBorder(neighbor,"HorizontalTop",merged,"HorizontalBottom")&&
                !TableGridLayoutValidation341.SharedBorder(merged,"HorizontalBottom",away,"HorizontalTop")&&
                !TableGridLayoutValidation341.SharedBorder(merged,"HorizontalInside",neighbor,"HorizontalTop"),"Native outer/inside boundary distinction lost.");
            var a=new TableGridLineLayout341("HorizontalBottom","Double",.6);var b=new TableGridLineLayout341("HorizontalTop","Single",.6);
            Need(TableGridLayoutValidation341.Conflicting(a,b)&&!TableGridLayoutValidation341.Conflicting(a,b with{LineStyle=null})&&
                TableGridLayoutValidation341.Conflicting(a,b with{LineStyle="Double",DoubleSpacing=.8}),"Shared field conflict not detected independently of sparse request.");
            Need(!TableGridLayoutValidation341.SharedBorder(new(int.MaxValue,0,int.MaxValue,0),"HorizontalBottom",new(0,0,0,0),"HorizontalTop"),"Native index arithmetic overflowed.");
            return Task.CompletedTask;
        });
        await test("block341 token-bound sole native grid edit has no selected-cell count ceiling",()=>
        {
            var cells=Enumerable.Range(0,130).Select(row=>new{row,column=0,grids=new[]{new{line="HorizontalBottom",line_style="Double"}}}).ToArray();
            var input=Wire.Element(new{operation="set_table_grid_layout",handles=new[]{"A1"},table_token=token,cells});
            var edit=TableGridLayoutValidation341.Parse(input,"$",new[]{"A1"});Need(edit.Cells.Length==130,"Artificial selected-cell ceiling.");
            TableGridLayoutValidation341.Batch(new EditSpec[]{edit});
            try{TableGridLayoutValidation341.Batch(new EditSpec[]{edit,edit});throw new Exception("Mixed token use accepted.");}catch(DomainException e)when(e.Error.code=="INVALID_OPERATION_ORDER"){}
            var schema=ToolCatalog.Tools.Single(t=>t.Name=="nanocad.modify_entities").InputSchema.GetProperty("properties").GetProperty("operations").GetProperty("items").GetProperty("oneOf")
                .EnumerateArray().Single(v=>v.GetProperty("properties").GetProperty("operation").GetProperty("const").GetString()==TableGridLayoutValidation341.Operation);
            Need(!schema.GetProperty("properties").GetProperty("cells").TryGetProperty("maxItems",out _)&&schema.GetProperty("properties").GetProperty("table_token").GetProperty("pattern").GetString()=="^[0-9A-F]{64}$","Actual catalog/token parity differs.");
            return Task.CompletedTask;
        });
    }
}
