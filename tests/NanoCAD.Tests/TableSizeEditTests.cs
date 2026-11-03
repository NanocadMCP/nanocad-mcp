using System.Text.Json.Nodes;
using NanoCAD.Contracts;
using NanoCAD.McpServer;

internal static class TableSizeEditTests
{
    public static async Task Run(Func<string,Func<Task>,Task> test)
    {
        string bridge=Guid.NewGuid().ToString("D"),doc=Guid.NewGuid().ToString("D"),id=Guid.NewGuid().ToString("D");
        object Size(int index=0,double old=20,double size=30)=>new{index,expected_size=old,size};
        object Edit(object[]? rows=null,object[]? columns=null,string handle="AF")=>new{operation="resize_table",handles=new[]{handle},rows=rows??new[]{Size()},columns=columns??Array.Empty<object>()};
        ModifyArguments Parse(params object[] edits)=>ModifyValidation.Parse(Wire.Element(new{bridge_instance_id=bridge,document_id=doc,operation_id=id,expected_revision="7",units="Millimeters",operations=edits}));
        void Reject(params object[] edits){try{Parse(edits);}catch(DomainException){return;}throw new Exception("Invalid size edit accepted.");}
        await test("table size contract preserves ordered intent and rejects bounds ambiguous indices and combined budgets",()=>{
            var args=Parse(Edit());var edit=(ResizeTableSpec)args.Operations.Single();
            if(edit.Rows.Single()!=new TableSizeEdit(0,20,30)||args.Fingerprint()!=Parse(Edit(handle:"00af")).Fingerprint())throw new Exception("Size intent/identity changed.");
            foreach(var change in new[]{Size(1),Size(old:21),Size(size:31)})if(args.Fingerprint()==Parse(Edit(new[]{change})).Fingerprint())throw new Exception("Resize replay omits intent.");
            if(args.Fingerprint()==Parse(Edit(Array.Empty<object>(),new[]{Size()})).Fingerprint())throw new Exception("Row and column conflated.");
            if(Parse(Edit(),Edit(new[]{Size(old:30,size:40)})).Fingerprint()==Parse(Edit(new[]{Size(old:30,size:40)}),Edit()).Fingerprint())throw new Exception("Lost ordered edits.");
            Reject(Edit(Array.Empty<object>()));Reject(Edit(new[]{Size(),Size()}));
            foreach(var value in new[]{Size(-1),Size(size:0),Size(old:-1),Size(size:1000001)})Reject(Edit(new[]{value}));
            Parse(Edit(Array.Empty<object>(),new[]{Size(32)}));
            Parse(Edit(),new{operation="replace_table_text",handles=new[]{"B0"},cells=new[]{new{row=0,column=0,expected_text="a",text="b"}}},Edit(handle:"B1"),Edit(handle:"B2"),Edit(handle:"B3"));
            Parse(Enumerable.Range(0,3).Select(_=>Edit(Enumerable.Range(0,64).Select(i=>Size(i)).ToArray())).ToArray());
            Reject(new{operation="resize_table",handles=new[]{"AF","B0"},rows=new[]{Size()},columns=Array.Empty<object>()});
            Reject(new{operation="resize_table",handles=new[]{"AF"},rows=new[]{new{index=0,expected_size=20,size=30,extra=true}},columns=Array.Empty<object>()});
            return Task.CompletedTask;
        });
        await test("table size verification rejects clamping collateral placement formatting and stale dimensions",()=>{
            var before=Wire.Element(new{rows=2,columns=2,row_heights=new[]{20.0,25.0},column_widths=new[]{100.0,120.0},width=220.0,height=45.0,position=new[]{10,100,0},cells=new[]{new{text="Control",alignment="Left",text_height=3.0}}});
            var resize=new ResizeTableSpec(new[]{"AF"},new[]{new TableSizeEdit(1,25,35)},new[]{new TableSizeEdit(0,100,140)});
            var expected=TableSizeVerification.Expected(before,resize);
            if(expected.GetProperty("width").GetDouble()!=260||expected.GetProperty("height").GetDouble()!=55)throw new Exception("Total dimensions incorrect.");
            TableSizeVerification.Verify(expected,expected);
            foreach(Action<JsonNode> change in new Action<JsonNode>[]{n=>n["row_heights"]![1]=36,n=>n["column_widths"]![1]=125,n=>n["position"]![1]=101,n=>n["cells"]![0]!["alignment"]="Center",n=>n["cells"]![0]!["text"]="Wrong",n=>n["cells"]![0]!["text_height"]=4,n=>n["height"]=56})
            {
                var changed=JsonNode.Parse(expected.GetRawText())!;change(changed);
                try{TableSizeVerification.Verify(expected,Wire.Element(changed));}catch(DomainException e)when(e.Error.code=="NATIVE_TABLE_MISMATCH"){continue;}
                throw new Exception("Collateral size edit accepted.");
            }
            foreach(var bad in new[]{new TableSizeEdit(2,25,35),new TableSizeEdit(1,20,35)})
            {try{TableSizeVerification.Expected(before,resize with{Rows=new[]{bad}});}catch(DomainException){continue;}throw new Exception("Invalid native expectation accepted.");}
            return Task.CompletedTask;
        });
        await test("table size schema exposes bounded row and column edits and stored dimensions",()=>{
            var tool=ToolCatalog.Tools.Single(t=>t.Name=="nanocad.modify_entities");
            var schema=tool.InputSchema.GetProperty("properties").GetProperty("operations").GetProperty("items").GetProperty("oneOf").EnumerateArray().Single(s=>s.GetProperty("properties").GetProperty("operation").GetProperty("const").GetString()=="resize_table");
            var props=schema.GetProperty("properties");
            if(props.GetProperty("handles").GetProperty("maxItems").GetInt32()!=1||props.GetProperty("rows").TryGetProperty("maxItems",out _)||props.GetProperty("columns").TryGetProperty("maxItems",out _)||schema.GetProperty("anyOf").GetArrayLength()!=2)throw new Exception("Schema bounds lost.");
            var output=ToolCatalog.Tools.Single(t=>t.Name=="nanocad.inspect_objects").OutputSchema!.Value;
            var table=output.GetProperty("$defs").GetProperty("table").GetProperty("properties");
            foreach(string field in new[]{"row_heights","column_widths"})if(table.GetProperty(field).GetProperty("items").GetProperty("type").GetString()!="number")throw new Exception("Read schema missing sizes.");
            return Task.CompletedTask;
        });
    }
}
