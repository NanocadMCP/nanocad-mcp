using System.Text.Json;
using System.Text.Json.Nodes;
using NanoCAD.Contracts;
using NanoCAD.McpServer;

internal static class TableTextEditTests
{
    public static async Task Run(Func<string,Func<Task>,Task> test)
    {
        string bridge=Guid.NewGuid().ToString("D"),doc=Guid.NewGuid().ToString("D"),operation=Guid.NewGuid().ToString("D");
        object Cell(int row=0,int column=0,string old="Old",string text="New part")=>new{row,column,expected_text=old,text};
        object Edit(object[]? cells=null,string[]? handles=null)=>new{operation="replace_table_text",handles=handles??new[]{"00aF"},cells=cells??new[]{Cell()}};
        ModifyArguments Parse(params object[] edits)=>ModifyValidation.Parse(Wire.Element(new{bridge_instance_id=bridge,document_id=doc,operation_id=operation,expected_revision="7",units="Millimeters",operations=edits}));
        void Reject(params object[] edits){try{Parse(edits);}catch(DomainException){return;}throw new Exception("Invalid table edit accepted.");}
        await test("table text contract preserves cell coordinates ordered values and replay identity",()=>{
            var args=Parse(Edit());var edit=(ReplaceTableTextSpec)args.Operations.Single();
            if(edit.Handles.Single()!="AF" || edit.Cells.Single()!=new TableTextCellEdit(0,0,"Old","New part"))throw new Exception("Cell identity changed.");
            if(args.Fingerprint()!=Parse(Edit(handles:new[]{"AF"})).Fingerprint())throw new Exception("Handle normalization changed intent.");
            foreach(var change in new[]{Cell(row:1),Cell(column:1),Cell(old:"Other"),Cell(text:"Other")})
                if(args.Fingerprint()==Parse(Edit(new[]{change})).Fingerprint())throw new Exception("Cell intent missing from replay.");
            var first=Edit();var second=Edit(new[]{Cell(old:"New part",text:"")});
            if(Parse(first,second).Fingerprint()==Parse(second,first).Fingerprint())throw new Exception("Ordered edits lost.");
            return Task.CompletedTask;
        });
        await test("table text rejects ambiguous cells formatted values and per batch limits",()=>{
            Reject(Edit(handles:new[]{"AF","B0"}));Reject(Edit(Array.Empty<object>()));Reject(Edit(new[]{Cell(),Cell()}));
            foreach(var value in new[]{Cell(row:-1),Cell(text:"a\nb"),Cell(text:@"\P"),Cell(old:"%<field>%"),Cell(text:"%%c"),Cell(text:"{x}"),Cell(text:new string('x',4097))})Reject(Edit(new[]{value}));
            Parse(Edit(Enumerable.Range(0,33).Select(i=>Cell(row:i)).ToArray()));
            Parse(Enumerable.Range(0,5).Select(i=>Edit(handles:new[]{(i+16).ToString("X")})).ToArray());
            Parse(Enumerable.Range(0,5).Select(_=>Edit(Enumerable.Range(0,32).Select(i=>Cell(row:i)).ToArray())).ToArray());
            Parse(Edit(Enumerable.Range(0,9).Select(i=>Cell(row:i,old:new string('x',4096),text:new string('y',4096))).ToArray()));
            Reject(new{operation="erase",handles=new[]{"AF"}},Edit());
            return Task.CompletedTask;
        });
        await test("table text verification admits native row reflow and detects collateral content formatting placement and column changes",()=>{
            var before=Wire.Element(new{rows=1,columns=2,width=100.0,height=8.0,row_heights=new[]{8.0},column_widths=new[]{50.0,50.0},table_style="AF",position=new[]{0.0,0.0,0.0},cells=new[]{new{row=0,column=0,text="Old",alignment="Left",text_height=3.0,text_style="10"},new{row=0,column=1,text="Control",alignment="Center",text_height=4.0,text_style="11"}}});
            var expected=JsonNode.Parse(before.GetRawText())!;expected["cells"]![0]!["text"]="New";
            var edits=new[]{new TableTextCellEdit(0,0,"Old","New")};
            TableTextVerification.Verify(before,Wire.Element(expected),edits);
            var reflow=JsonNode.Parse(expected.ToJsonString())!;reflow["height"]=16.0;reflow["row_heights"]![0]=16.0;
            TableTextVerification.Verify(before,Wire.Element(reflow),edits);
            if(!TableTextVerification.Reflowed(before,Wire.Element(reflow))||TableTextVerification.Reflowed(before,Wire.Element(expected)))throw new Exception("Native reflow observation lost.");
            foreach(Action<JsonNode> mutate in new Action<JsonNode>[] {n=>n["cells"]![1]!["text"]="Wrong",n=>n["column_widths"]![0]=51.0,n=>n["table_style"]="B0",n=>n["position"]![1]=1.0,n=>n["cells"]![0]!["alignment"]="Center",n=>n["cells"]![1]!["text_height"]=5,n=>n["cells"]![1]!["text_style"]="12",n=>n["cells"]![0]!["column"]=1,n=>n["row_heights"]![0]=0.0})
            {
                var changed=JsonNode.Parse(reflow.ToJsonString())!;mutate(changed);
                try{TableTextVerification.Verify(before,Wire.Element(changed),edits);}catch(DomainException e)when(e.Error.code=="NATIVE_TABLE_MISMATCH"){continue;}
                throw new Exception("Collateral change accepted.");
            }
            return Task.CompletedTask;
        });
        await test("table text schema uses bounded typed cells with no additional tool",()=>{
            var tool=ToolCatalog.Tools.Single(t=>t.Name=="nanocad.modify_entities");
            var variant=tool.InputSchema.GetProperty("properties").GetProperty("operations").GetProperty("items").GetProperty("oneOf").EnumerateArray().Single(v=>v.GetProperty("properties").GetProperty("operation").GetProperty("const").GetString()=="replace_table_text");
            var props=variant.GetProperty("properties");var cells=props.GetProperty("cells");
            if(props.GetProperty("handles").GetProperty("maxItems").GetInt32()!=1 || cells.TryGetProperty("maxItems",out _) || cells.GetProperty("items").GetProperty("properties").GetProperty("row").GetProperty("type").GetString()!="integer" || !ToolCatalog.Tools.Select(t=>t.Name).Order().SequenceEqual(ToolCatalog.OperationNames.Append("get_guidance").Select(op=>"nanocad."+op).Order()) || tool.Annotations?.ReadOnlyHint!=false)throw new Exception("Table capability boundary changed.");
            File.WriteAllText(Path.Combine(Environment.GetEnvironmentVariable("NANOCAD_MCP_HOME")!,"tools-list.json"),JsonSerializer.Serialize(ToolCatalog.Tools,Wire.Json));return Task.CompletedTask;
        });
    }
}
