using System.Text.Json;
using NanoCAD.Contracts;
using NanoCAD.McpServer;

internal static class TableCellLayout340Tests
{
    public static async Task Run(Func<string,Func<Task>,Task> test)
    {
        string token=new('A',64);
        TableCellLayoutEdit340 Parse(object properties)=>TableCellLayoutValidation340.Parse(Wire.Element(new{
            operation="set_table_cell_layout",handles=new[]{"A1"},table_token=token,
            cells=new[]{new{row=17,column=15,properties}}}),"$.operations[0]",new[]{"A1"});
        void Need(bool condition,string why){if(!condition)throw new Exception(why);}
        void Reject(object properties){try{Parse(properties);}catch(DomainException e)when(e.Error.code=="INVALID_ARGUMENT"){return;}throw new Exception("Invalid native layout accepted.");}
        await test("block340 native cell layout preserves sparse zero margins and explicit scale policy",()=>
        {
            var result=Parse(new{margins=new{left=0.0,top=.6},rotation=Math.PI/12,scale=.9,auto_scale=false,cell_style="Data",data_format=""});
            var p=result.Cells.Single().Properties;
            Need(result.TableToken==token&&result.Cells.Single().Row==17&&p.Margins is {Left:0,Top:.6,Right:null,Bottom:null}&&p.AutoScale==false&&p.DataFormat=="","Sparse intent/default format lost.");
            Reject(new{});Reject(new{margins=new{}});Reject(new{margins=new{top=-1}});Reject(new{scale=.9});Reject(new{scale=.9,auto_scale=true});Reject(new{scale=0,auto_scale=false});
            Reject(new{cell_style=" "});Reject(new{data_format="x\ny"});Reject(new{rotation=1e10});Reject(new{arbitrary=1});
            return Task.CompletedTask;
        });
        await test("block340 cell layout requires fresh complete token unique native anchors and sole operation",()=>
        {
            var item=Wire.Element(new{operation="set_table_cell_layout",handles=new[]{"A1"},table_token=token,cells=new[]{new{row=0,column=0,properties=new{auto_scale=true}},new{row=0,column=0,properties=new{auto_scale=false}}}});
            try{TableCellLayoutValidation340.Parse(item,"$",new[]{"A1"});throw new Exception("Duplicate cells accepted.");}catch(DomainException e)when(e.Error.code=="INVALID_ARGUMENT"){}
            var one=Parse(new{auto_scale=true});TableCellLayoutValidation340.Batch(new EditSpec[]{one});
            try{TableCellLayoutValidation340.Batch(new EditSpec[]{one,one});throw new Exception("Mixed lifecycle accepted.");}catch(DomainException e)when(e.Error.code=="INVALID_OPERATION_ORDER"){}
            var published=ToolCatalog.Tools.Single(t=>t.Name=="nanocad.modify_entities").InputSchema;
            var branch=published.GetProperty("properties").GetProperty("operations").GetProperty("items").GetProperty("oneOf")
                .EnumerateArray().Single(v=>v.GetProperty("properties").GetProperty("operation").GetProperty("const").GetString()=="set_table_cell_layout");
            var properties=branch.GetProperty("properties");
            Need(properties.GetProperty("handles").GetProperty("maxItems").GetInt32()==1&&!properties.GetProperty("cells").TryGetProperty("maxItems",out _),"Hidden native cell-count ceiling added.");
            Need(branch.GetProperty("required").EnumerateArray().Select(v=>v.GetString()).Contains("table_token")&&
                 branch.GetProperty("properties").GetProperty("cells").GetProperty("items").GetProperty("properties").GetProperty("properties").GetProperty("properties").TryGetProperty("margins",out _),
                 "Actual published branch does not expose the native cell-layout inputs.");
            return Task.CompletedTask;
        });
    }
}
