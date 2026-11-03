using System.Text.Json;
using NanoCAD.Contracts;
using NanoCAD.McpServer;

internal static class BasicEditing336Tests
{
    private static JsonElement Request(object[] edits,string space="model")=>Wire.Element(new{
        bridge_instance_id=Guid.NewGuid().ToString("D"),document_id=Guid.NewGuid().ToString("D"),operation_id=Guid.NewGuid().ToString("D"),
        expected_revision="1",units="Millimeters",target_space=space,operations=edits});
    public static async Task Run(Func<string,Func<Task>,Task> test)
    {
        void Need(bool value,string why){if(!value)throw new Exception(why);}
        void Reject(Action action,string code="INVALID_ARGUMENT")
        {try{action();}catch(DomainException error)when(error.Error.code==code){return;}throw new Exception("Invalid edit was accepted or gave an unrelated refusal.");}
        string token=new('A',64);
        object Numeric(object value,string type="Double")=>new{operation="set_table_cell_value",handles=new[]{"a1"},table_token=token,
            cells=new[]{new{row=2,column=1,data_type=type,unit_type="Distance",value,data_format=""}}};
        await test("block336 numeric cells retain raw typed values and native units",()=>
        {
            var value=ModifyValidation.Parse(Request([Numeric(12.375)],"paper"));
            var edit=(TableValueEditingSpec)value.Operations.Single();
            Need(edit.Cells![0].Value.GetDouble()==12.375&&edit.Cells[0].DataType=="Double"&&edit.Cells[0].UnitType=="Distance"&&
                WriteRecovery.NotGuaranteed(value),"Raw native values/units were converted or recovery was overstated.");
            ModifyValidation.Parse(Request([Numeric(int.MinValue,"Long")]));
            Reject(()=>ModifyValidation.Parse(Request([Numeric(12.5,"Long")])));
            Reject(()=>ModifyValidation.Parse(Request([Numeric("12.375 mm")])));
            Reject(()=>ModifyValidation.Parse(Request([Numeric((long)int.MaxValue+1,"Long")])));
            Reject(()=>ModifyValidation.Parse(Request([Numeric(12.0),new{operation="erase",handles=new[]{"b1"}}])),"INVALID_OPERATION_ORDER");
            return Task.CompletedTask;
        });
        await test("block336 note format binds fresh target state while independent notes batch",()=>
        {
            object Format(string handle,object properties)=>new{operation="set_mtext_format",handles=new[]{handle},mtext_token=token,properties};
            var edits=ModifyValidation.Parse(Request([Format("a1",new{width=0.0,text_height=3.0,attachment="top_left"}),
                Format("b1",new{line_spacing_style="exactly",line_spacing_factor=1.25})],"paper"));
            Need(edits.Operations.Length==2&&WriteRecovery.NotGuaranteed(edits)&&edits.Operations[0] is SetMTextFormatSpec{Properties.Width:0},
                "Valid no-wrap native note edit was rejected or independent targets forced into separate requests.");
            Reject(()=>ModifyValidation.Parse(Request([Format("a1",new{width=30.0}),new{operation="move",handles=new[]{"A1"},displacement=new[]{1.0,0.0}}])),"INVALID_OPERATION_ORDER");
            Reject(()=>ModifyValidation.Parse(Request([Format("a1",new{text="silently replace raw content"})])));
            Reject(()=>ModifyValidation.Parse(Request([Format("a1",new{direction_wcs=new[]{1.0,1.0,0.0}})])));
            return Task.CompletedTask;
        });
        await test("block336 local table style selection and note tokens are publicly usable",()=>
        {
            var style=ModifyValidation.Parse(Request([new{operation="set_table_style",handles=new[]{"a1"},table_token=token,table_style_handle="b1"}],"paper"));
            Need(style.Operations[0] is TableValueEditingSpec{TableStyleHandle:"B1"}&&WriteRecovery.NotGuaranteed(style),"Native style identity lost or recovery overstated.");
            var tool=ToolCatalog.Tools.Single(t=>t.Name=="nanocad.modify_entities");
            string input=tool.InputSchema.GetRawText(),output=tool.OutputSchema!.Value.GetRawText();
            Need(input.Contains("set_table_cell_value")&&input.Contains("native scalar")&&input.Contains("set_table_style")&&input.Contains("set_mtext_format")&&
                output.Contains("mtext_format_changes")&&output.Contains("direction_wcs"),"The user agent cannot discover native editing branches or actual outcomes.");
            var inspected=ToolCatalog.Tools.Single(t=>t.Name=="nanocad.inspect_objects").OutputSchema!.Value.GetRawText();
            Need(inspected.Contains("mtext_token")&&inspected.Contains("unavailable_reason")&&inspected.Contains("native_value")&&inspected.Contains("unit_type"),
                "Editing tokens/raw numeric cells are not included in the actual public reader.");
            return Task.CompletedTask;
        });
        await test("block336 optional formatting availability does not destroy base read contract",()=>
        {
            var id=new EntityRef(Guid.NewGuid().ToString("D"),Guid.NewGuid().ToString("D"),"A1");
            var unavailable=Wire.Element(new MTextFormatting(id,"AtLeast",1,"ByStyle","NoColumns",new(false,false,1,new{}),
                new(null,false,"Field content requires its owning adapter.")));
            var editing=unavailable.GetProperty("editing");
            Need(!editing.GetProperty("editable").GetBoolean()&&!editing.TryGetProperty("mtext_token",out _)&&
                unavailable.GetProperty("text_style_ref").GetProperty("handle").GetString()=="A1", "Unavailable editing erased otherwise readable native formatting or fabricated a token.");
            var old=Wire.Element(new MTextFormatting(id,"AtLeast",1,"ByStyle","NoColumns",new(false,false,1,new{})));
            Need(!old.TryGetProperty("editing",out _),"Existing DTO callers acquired a fake available-editing field.");
            return Task.CompletedTask;
        });
    }
}
