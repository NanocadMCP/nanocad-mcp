using System.Text.Json;
using NanoCAD.Contracts;

internal static class BasicEditingBlockTests
{
    static readonly string Bridge=Guid.NewGuid().ToString("D"),Document=Guid.NewGuid().ToString("D");
    static void Check(bool ok,string reason){if(!ok)throw new Exception(reason);}
    static void Invalid(Action action){try{action();throw new Exception("Invalid input was accepted");}catch(DomainException e){Check(e.Error.code is "INVALID_ARGUMENT" or "INVALID_OPERATION_ORDER",e.Error.code);}}
    static JsonElement Write(object operation)=>Wire.Element(new{bridge_instance_id=Bridge,document_id=Document,expected_revision="1",operation_id=Guid.NewGuid().ToString("D"),units="Millimeters",operations=new[]{operation}});
    public static async Task Run(Func<string,Func<Task>,Task> test)
    {
        await test("basic332 native table lifecycle contracts and Paper dispatch",()=>{
            string token=new('A',64);
            var insert=ModifyValidation.Parse(Write(new{operation="insert_table_rows",handles=new[]{"b1"},table_token=token,index=1,count=2,height=6.0,values=new[]{new[]{"A",""},new[]{"B",""}}}));
            Check(insert.Operations.Single() is TableEditingSpec{Targets:["B1"],Count:2,Values.Length:2},"Native table insert payload lost dimensions/literals");
            foreach(string op in new[]{"delete_table_rows","delete_table_columns"})
                Check(ModifyValidation.Parse(Write(new{operation=op,handles=new[]{"B1"},table_token=token,index=1,count=1})).Operations.Single() is TableEditingSpec,"Table axis deletion was not dispatched");
            var range=new{top_row=0,left_column=0,bottom_row=0,right_column=1};
            foreach(string op in new[]{"merge_table_cells","unmerge_table_cells"})
                Check(ModifyValidation.Parse(Write(new{operation=op,handles=new[]{"B1"},table_token=token,range})).Operations.Single() is TableEditingSpec{Range:{RightColumn:1}},"Table range fields were lost");
            Check(ModifyValidation.Parse(Write(new{operation="set_table_cell_text",handles=new[]{"B1"},table_token=token,cells=new[]{new{row=0,column=0,text="Updated title"}}})).Operations.Single() is TableEditingSpec{TextCells:[{Text:"Updated title"}]},"Token-based literal/merged-anchor edit was not dispatched");
            var paper=Wire.Element(new{bridge_instance_id=Bridge,document_id=Document,expected_revision="1",operation_id=Guid.NewGuid().ToString("D"),units="Millimeters",target_space="paper",operations=new[]{new{operation="set_table_cell_format",handles=new[]{"B1"},table_token=token,cells=new[]{new{row=0,column=0,properties=new{alignment="MiddleCenter",text_height=3.0,text_style_handle="a1",color_aci=3}}}}}});
            Check(ModifyValidation.Parse(paper).Operations.Single() is TableEditingSpec{Cells:[{Properties:{TextStyleHandle:"A1"}}]},"Paper Table format route rejected or lost exact style identity");
            Invalid(()=>ModifyValidation.Parse(Write(new{operation="insert_table_rows",handles=new[]{"B1"},table_token=token,index=0,count=2,height=6.0,values=new[]{new[]{"A"}}})));
            Invalid(()=>ModifyValidation.Parse(Write(new{operation="delete_table_rows",handles=new[]{"B1"},table_token="stale",index=0,count=1})));
            return Task.CompletedTask;
        });
        await test("basic332 format contracts preserve explicit units and reject incomplete deviations",()=>{
            var value=ModifyValidation.Parse(Write(new{operation="set_dimension_format",handles=new[]{"b1"},properties=new{tolerance_mode="deviation",upper_deviation=0.02,lower_deviation=-0.01,angular_precision=3}}));
            Check(value.Operations.Single() is DimensionFormatSpec{Targets:["B1"],Properties:{UpperDeviation:0.02,LowerDeviation:-0.01,AngularPrecision:3}},"Format payload lost fields/sign/identity");
            Invalid(()=>ModifyValidation.Parse(Write(new{operation="set_dimension_format",handles=new[]{"B1"},properties=new{tolerance_mode="deviation",upper_deviation=0.02}})));
            Invalid(()=>ModifyValidation.Parse(Write(new{operation="set_dimension_format",handles=new[]{"B1"},properties=new{linear_precision=2.5}})));
            Invalid(()=>ModifyValidation.Parse(Write(new{operation="set_dimension_format",handles=new[]{"B1","B2"},properties=new{text_height=2.5}})));
            return Task.CompletedTask;
        });
        await test("basic332 display and structure edits distinguish settings from dimensional data",()=>{
            var display=ModifyValidation.Parse(Write(new{operation="set_properties",handles=new[]{"B1"},properties=new{visible=false,linetype_scale=2.0}}));
            Check(display.Operations.Single() is PropertiesSpec{Properties:{Visible:false,LinetypeScale:2}},"Display fields lost");
            var current=StructureWriteValidation.Parse(Write(new{operation="set_current_layer",handle="B1",expected_current_handle="A1"}));
            Check(StructureWriteValidation.IsSettingsOnly(current.Operations),"Current layer incorrectly requires geometric scaling");
            var rename=StructureWriteValidation.Parse(Write(new{operation="rename_layout",handle="B1",expected_name="Layout1",name="Detail A"}));
            Check(StructureWriteValidation.IsSettingsOnly(rename.Operations),"Layout rename incorrectly requires geometric scaling");
            var viewport=StructureWriteValidation.Parse(Write(new{operation="set_viewport_view",layout_handle="B1",handle="B2",properties=new{is_on=true,frozen_layer_handles=new[]{"A1"}}}));
            Check(StructureWriteValidation.IsSettingsOnly(viewport.Operations),"Viewport display flags incorrectly treated as coordinates");
            var camera=StructureWriteValidation.Parse(Write(new{operation="set_viewport_view",layout_handle="B1",handle="B2",properties=new{view_target=new[]{0.0,0.0,0.0},view_direction=new[]{1.0,1.0,1.0}}}));
            Check(!StructureWriteValidation.IsSettingsOnly(camera.Operations),"Camera coordinate write incorrectly bypassed units");
            return Task.CompletedTask;
        });
        await test("basic332 Boolean and solid analysis use semantic operands without inventory ceilings",()=>{
            var tools=Enumerable.Range(2,12).Select(n=>n.ToString("X")).ToArray();
            var boolean=ModifyValidation.Parse(Write(new{operation="boolean_solid",handles=new[]{"1"},@operator="union",tool_handles=tools}));
            Check(boolean.Operations.Single() is SolidBooleanEditSpec{ToolHandles.Length:12},"Boolean imposed unrelated body-count ceiling");
            Invalid(()=>ModifyValidation.Parse(Write(new{operation="boolean_solid",handles=new[]{"1"},@operator="union",tool_handles=new[]{"1"}})));
            Invalid(()=>ModifyValidation.Parse(Write(new{operation="boolean_solid",handles=new[]{"1"},@operator="union",tool_handles=new[]{"2","02"}})));
            JsonElement Read(string mode,string[] handles, double[]? point=null)=>Wire.Element(new{bridge_instance_id=Bridge,document_id=Document,expected_revision="1",mode,handles,point});
            var containment=GeometryAnalysisValidation.Parse(Read("solid_containment",["1"],[1,2,3]));
            Check(containment.Mode=="solid_containment"&&containment.Point![2]==3,"Solid point analysis not dispatched");
            var interference=GeometryAnalysisValidation.Parse(Wire.Element(new{bridge_instance_id=Bridge,document_id=Document,expected_revision="1",mode="solid_interference",handles=new[]{"1","2"}}));
            Check(interference.Mode=="solid_interference","Interference branch unavailable");
            return Task.CompletedTask;
        });
    }
}
