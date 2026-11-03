using NanoCAD.Contracts;
using NanoCAD.McpServer;

internal static class BasicEditing337Tests
{
    public static async Task Run(Func<string,Func<Task>,Task> test)
    {
        void Need(bool value,string why){if(!value)throw new Exception(why);}
        void Reject(Action action,string code="INVALID_ARGUMENT")
        {try{action();}catch(DomainException e)when(e.Error.code==code){return;}throw new Exception("Invalid style edit was accepted or failed for another reason.");}
        StructureWriteArguments Request(params object[] edits)=>StructureWriteValidation.Parse(Wire.Element(new{
            bridge_instance_id=Guid.NewGuid().ToString("D"),document_id=Guid.NewGuid().ToString("D"),expected_revision="1",
            operation_id=Guid.NewGuid().ToString("D"),units="Millimeters",target_space="paper",operations=edits}));
        string token=new('A',64);
        object Table(object fields)=>new{operation="set_table_style_properties",handle="a1",expected_name="Standard",table_style_token=token,properties=fields};
        object Leader(object fields)=>new{operation="set_mleader_style_properties",handle="b1",expected_name="Standard",style_token=token,properties=fields};
        await test("block337 drawing-unit style lengths cannot bypass document-unit validation",()=>
        {
            var settings=Request(Table(new{description="Production table",rows=new[]{new{row_type="DataRow",alignment="MiddleLeft"}}}));
            Need(StructureWriteValidation.IsSettingsOnly(settings.Operations)&&WriteRecovery.NotGuaranteed(settings),"Non-geometric style edit rejected or recovery overstated.");
            Need(!StructureWriteValidation.IsSettingsOnly(Request(Table(new{horizontal_margin=1.5})).Operations)&&
                 !StructureWriteValidation.IsSettingsOnly(Request(Table(new{rows=new[]{new{row_type="DataRow",text_height=3.0}}})).Operations)&&
                 !StructureWriteValidation.IsSettingsOnly(Request(Leader(new{arrow_size=2.5})).Operations),"Native drawing lengths bypass actual document unit validation.");
            Need(StructureWriteValidation.IsSettingsOnly(Request(Leader(new{scale=1.25,leader_color_rgb=new[]{40,80,120}})).Operations),"Dimensionless/native appearance defaults falsely require drawing units.");
            return Task.CompletedTask;
        });
        await test("block337 token-bound style edits preserve identity and sparse intent",()=>
        {
            var table=Request(Table(new{vertical_margin=0.0}));
            Need(table.Operations.Single() is TableStyleEdit337{SourceHandle:"A1",SourceToken:var fresh}&&fresh==token,"Source identity or fresh token lost.");
            var leader=Request(Leader(new{enable_landing=true,leader_color_aci=256}));
            Need(leader.Operations.Single() is MLeaderStyleEdit337{SourceHandle:"B1",Properties.LeaderColorAci:256},"Native inheritance or omitted fields lost.");
            Reject(()=>Request(Table(new{rows=new[]{new{row_type="DataRow",color_aci=1,color_rgb=new[]{1,2,3}}}})));
            Reject(()=>Request(Leader(new{leader_color_aci=1,leader_color_rgb=new[]{1,2,3}})));
            Reject(()=>Request(Table(new{description="planned"}),Leader(new{scale=1.0})),"INVALID_OPERATION_ORDER");
            Reject(()=>Request(new{operation="set_mleader_style_properties",handle="B1",expected_name="Standard",style_token="stale",properties=new{scale=1.0}}));
            return Task.CompletedTask;
        });
        await test("block337 reusable style branches and availability are discoverable",()=>
        {
            var write=ToolCatalog.Tools.Single(t=>t.Name=="nanocad.modify_structure");
            string input=write.InputSchema.GetRawText(),output=write.OutputSchema!.Value.GetRawText();
            Need(new[]{"create_table_style","set_table_style_properties","create_mleader_style","set_mleader_style_properties"}.All(input.Contains)&&
                 input.Contains("base_style_token")&&input.Contains("table_style_token")&&input.Contains("style_token")&&output.Contains("table_style_changes"),
                 "User agent cannot obtain precise native style operations or outcomes.");
            string read=ToolCatalog.Tools.Single(t=>t.Name=="nanocad.get_structure").OutputSchema!.Value.GetRawText();
            Need(read.Contains("table_style_unavailable_reason")&&read.Contains("editing_unavailable_reason")&&read.Contains("horizontal_margin")&&read.Contains("leader_color"),
                 "Native style defaults/tokens or additive unavailable scope are hidden.");
            string leaders=ToolCatalog.Tools.Single(t=>t.Name=="nanocad.inspect_objects").OutputSchema!.Value.GetRawText();
            Need(leaders.Contains("effective_settings")&&leaders.Contains("style_unavailable_reason")&&leaders.Contains("complete_override_mask_available"),
                 "Leader style relationship or honest override observation boundary is unavailable.");
            return Task.CompletedTask;
        });
    }
}
