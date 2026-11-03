using System.Text.Json;
using NanoCAD.Contracts;

internal static class SettingsEditingBlockTests
{
    static readonly string Bridge=Guid.NewGuid().ToString("D"),Document=Guid.NewGuid().ToString("D");
    static void Check(bool value,string reason){if(!value)throw new Exception(reason);}
    static void Invalid(Action action){try{action();throw new Exception("Invalid settings accepted");}catch(DomainException e){Check(e.Error.code is "INVALID_ARGUMENT" or "INVALID_OPERATION_ORDER",e.Error.code);}}
    static JsonElement Write(object operation)=>Wire.Element(new{bridge_instance_id=Bridge,document_id=Document,expected_revision="1",operation_id=Guid.NewGuid().ToString("D"),units="Millimeters",operations=new[]{operation}});
    public static async Task Run(Func<string,Func<Task>,Task> test)
    {
        await test("hatch333 stored native edits preserve explicit schema and token scope",()=>{
            string token=new('A',64);
            var input=Write(new{operation="set_hatch_properties",handles=new[]{"a1"},hatch_token=token,properties=new{pattern_name="SOLID",origin_ocs=new[]{12.5,-8.0},hatch_style="outer",pattern_double=true}});
            var parsed=ModifyValidation.Parse(input);
            Check(parsed.Operations.Single() is SetHatchPropertiesSpec{HatchToken:var observed,Properties:{PatternName:"SOLID",OriginOcs:{X:12.5,Y:-8},HatchStyle:"outer",PatternDouble:true,PatternScale:null}}&&observed==token,"Native hatch token/optional pattern fields/OCS origin were lost");
            Check(WriteRecovery.NotGuaranteed(parsed),"Unproved native hatch recovery was advertised as guaranteed");
            var paper=Wire.Element(new{bridge_instance_id=Bridge,document_id=Document,expected_revision="1",operation_id=Guid.NewGuid().ToString("D"),units="Millimeters",target_space="paper",
                operations=Enumerable.Range(1,12).Select(i=>new{operation="set_hatch_properties",handles=new[]{i.ToString("X")},hatch_token=token,properties=new{pattern_angle=.7,pattern_scale=12.0}}).ToArray()});
            Check(ModifyValidation.Parse(paper).Operations.Length==12,"Paper native hatch route inherited a legacy eight-hatch count ceiling");
            Check(ModifyValidation.Parse(Write(new{operation="set_hatch_properties",handles=new[]{"A1"},hatch_token=token,properties=new{pattern_space=3.5}})).Operations.Single() is SetHatchPropertiesSpec{Properties:{PatternSpace:3.5,PatternScale:null}},"User-defined line spacing was conflated with scale");
            Invalid(()=>ModifyValidation.Parse(Write(new{operation="set_hatch_properties",handles=new[]{"A1"},hatch_token="old",properties=new{hatch_style="normal"}})));
            Invalid(()=>ModifyValidation.Parse(Write(new{operation="set_hatch_properties",handles=new[]{"A1"},hatch_token=token,properties=new{}})));
            Invalid(()=>ModifyValidation.Parse(Write(new{operation="set_hatch_properties",handles=new[]{"A1"},hatch_token=token,properties=new{pattern_scale=0}})));
            HatchPropertiesValidation.Batch(parsed.Operations);
            Invalid(()=>HatchPropertiesValidation.Batch(new EditSpec[]{parsed.Operations.Single(),new PropertiesSpec(new[]{"A1"},new(null,3,null,null))}));
            return Task.CompletedTask;
        });
        await test("settings333 appearance inheritance quantization and strict payloads",()=>{
            var value=ModifyValidation.Parse(Write(new{operation="set_properties",handles=new[]{"a1","b1"},properties=new{color_rgb=new[]{20,80,120},transparency=new{mode="explicit",percent=37.5}}}));
            Check(value.Operations.Single() is PropertiesSpec{Properties:{ColorRgb:{Red:20,Green:80,Blue:120},Transparency:{Mode:"explicit",Percent:37.5,Alpha:159}}},"RGB/percentage/normalized references lost");
            var operation=(PropertiesSpec)value.Operations.Single();
            var second=value with{Operations=new EditSpec[]{operation with{Properties=operation.Properties with{Transparency=new("explicit",37.51)}}}};
            Check(operation.Properties.Transparency!.Alpha==((PropertiesSpec)second.Operations[0]).Properties.Transparency!.Alpha&&value.Fingerprint()!=second.Fingerprint(),"Native alpha quantization erased distinct requested payloads from the write fingerprint");
            foreach(string mode in new[]{"by_layer","by_block"})
                Check(ModifyValidation.Parse(Write(new{operation="set_properties",handles=new[]{"A1"},properties=new{transparency=new{mode}}})).Operations.Single() is PropertiesSpec{Properties:{Transparency:{Percent:null,Alpha:null}}},"Transparency inheritance became an explicit percentage");
            var layer=StructureWriteValidation.Parse(Write(new{operation="create_layer",name="Manufacturing",properties=new{color_rgb=new[]{20,80,120},description="Superficies de control — Ø",transparency=new{mode="explicit",percent=0}}}));
            Check(layer.Operations.Single() is CreateLayerEdit{Properties:{ColorAci:null,ColorRgb:not null,Description:"Superficies de control — Ø"}}&&StructureWriteValidation.IsSettingsOnly(layer.Operations),"RGB layer got default ACI or lost Unicode/setting classification");
            Invalid(()=>ModifyValidation.Parse(Write(new{operation="set_properties",handles=new[]{"A1"},properties=new{color_rgb=new[]{20,80,120},color_aci=3}})));
            Invalid(()=>ModifyValidation.Parse(Write(new{operation="set_properties",handles=new[]{"A1"},properties=new{color_rgb=new[]{20.5,80.0,120.0}}})));
            Invalid(()=>StructureWriteValidation.Parse(Write(new{operation="set_layer_properties",handle="A1",properties=new{transparency=new{mode="by_layer"}}})));
            Invalid(()=>StructureWriteValidation.Parse(Write(new{operation="set_layer_properties",handle="A1",properties=new{description="A\0B"}})));
            return Task.CompletedTask;
        });
        await test("settings333 scene framing retains all requested objects",()=>{
            var sheet=CameraViews.Create(Bridge,Document,"1",1,"A1",[0,0,0],[0,0,1],[0,0],420,297,0,0,0);
            var otherSheet=CameraViews.Create(Bridge,Document,"1",1,"B1",[0,0,0],[0,0,1],[0,0],420,297,0,0,0);
            Check(sheet.viewport_number==1&&sheet.camera_token!=otherSheet.camera_token&&!CameraChangeValidation.SameState(sheet,otherSheet),"Paper camera viewport identity was rejected or conflated across sheets");
            var bounds=Enumerable.Range(0,128).Select(i=>new CameraBounds(new(i,0,0),new(i+1,10,1))).Append(new(new(1000,20,0),new(1005,30,1))).ToArray();
            var frame=CameraFraming.Calculate(bounds,new(1,0,0),new(0,1,0),2,.1);
            Check(frame.Target.X==502.5&&frame.Width>=1005&&frame.Height==frame.Width/2,"Framing omitted the final requested object or lost viewport aspect");
            foreach(var box in bounds)
                Check(Math.Abs(box.Min.X-frame.Target.X-frame.Center.X)<=frame.Width/2&&Math.Abs(box.Max.X-frame.Target.X-frame.Center.X)<=frame.Width/2,"A requested scene object lies outside its fitted image");
            return Task.CompletedTask;
        });
        await test("settings333 native lifecycle contracts retain identities and recovery scope",()=>{
            foreach(string operation in StructureLifecycleValidation.Operations)
            {
                var fields=new Dictionary<string,object>{["operation"]=operation,["handle"]="b1",["expected_name"]="Manufacturing"};
                if(operation.StartsWith("set_current_",StringComparison.Ordinal))fields["expected_current_handle"]="a1";
                if(operation.StartsWith("rename_",StringComparison.Ordinal))fields["name"]="Inspection";
                var request=StructureWriteValidation.Parse(Write(fields));
                Check(request.Operations.Single() is StructureLifecycleEdit{Handle:"B1",ExpectedName:"Manufacturing"},"Native record identity or expected name was lost");
                Check(StructureWriteValidation.IsSettingsOnly(request.Operations),"A structure lifecycle edit incorrectly requires geometric unit conversion");
                Check(WriteRecovery.NotGuaranteed(request)==(operation is "delete_layout" or "set_current_dimension_style"),"Native manager/header recovery scope was misrepresented");
            }
            Invalid(()=>StructureWriteValidation.Parse(Write(new{operation="set_current_dimension_style",handle="B1",expected_name="Manufacturing"})));
            Invalid(()=>StructureWriteValidation.Parse(Write(new{operation="rename_text_style",handle="B1",expected_name="Manufacturing",name="manufacturing"})));
            var mixed=Wire.Element(new{bridge_instance_id=Bridge,document_id=Document,expected_revision="1",operation_id=Guid.NewGuid().ToString("D"),units="Millimeters",operations=new[]{new{operation="delete_layer",handle="B1",expected_name="Manufacturing"},new{operation="delete_layer",handle="C1",expected_name="Inspection"}}});
            Invalid(()=>StructureWriteValidation.Parse(mixed));
            return Task.CompletedTask;
        });
        await test("drawing333 aligned view groups expose edits without a model inventory ceiling",()=>{
            var operations=Enumerable.Range(1,72).Select(i=>new{operation="set_drawing_view_properties",layout_handle="A1",handle=(256+i).ToString("X"),expected_name="Projection "+i,model_scale=2.0,show_hidden_lines=false}).ToArray();
            var payload=Wire.Element(new{bridge_instance_id=Bridge,document_id=Document,expected_revision="1",operation_id=Guid.NewGuid().ToString("D"),units="Millimeters",operations});
            var group=StructureWriteValidation.Parse(payload);
            Check(group.Operations.Length==72&&group.Operations.All(edit=>edit is SetDrawingViewPropertiesEdit{ModelScale:2,ShowHiddenLines:false}),"A complete aligned view group lost members or properties");
            var mixed=Wire.Element(new{bridge_instance_id=Bridge,document_id=Document,expected_revision="1",operation_id=Guid.NewGuid().ToString("D"),units="Millimeters",
                operations=operations.Cast<object>().Append(new{operation="create_layer",name="Unexpected",properties=new{color_aci=3}}).ToArray()});
            Invalid(()=>StructureWriteValidation.Parse(mixed));
            Check(StructureWriteValidation.IsSettingsOnly(group.Operations)&&WriteRecovery.NotGuaranteed(group),"Coordinate-free native view editing lost its settings/recovery classification");
            var placement=StructureWriteValidation.Parse(Write(new{operation="set_drawing_view_properties",layout_handle="A1",handle="B1",expected_name="Front",origin=new[]{20.0,30.0}}));
            Check(!StructureWriteValidation.IsSettingsOnly(placement.Operations),"Millimeter placement bypassed geometric units");
            var deletion=StructureWriteValidation.Parse(Write(new{operation="delete_drawing_views",targets=new[]{new{layout_handle="A1",handle="B1",expected_name="Right"}}}));
            Check(deletion.Operations.Single() is DeleteDrawingViewsEdit{Targets:[{LayoutHandle:"A1",Handle:"B1"}]}&&WriteRecovery.NotGuaranteed(deletion),"Explicit native leaf identity or honest deletion recovery was lost");
            Invalid(()=>StructureWriteValidation.Parse(Write(new{operation="set_drawing_view_properties",layout_handle="A1",handle="B1",expected_name="Front"})));
            Invalid(()=>StructureWriteValidation.Parse(Write(new{operation="delete_drawing_views",targets=new[]{new{layout_handle="A1",handle="B1",expected_name="Front"},new{layout_handle="A1",handle="b1",expected_name="Front"}}})));
            return Task.CompletedTask;
        });
        await test("drawing333 projection pages retain explicit layout view and revision",()=>{
            var read=Wire.Element(new{bridge_instance_id=Bridge,document_id=Document,expected_revision="47",layout_handle="a1",geometry_view_handle="b1",geometry_start_index=1000,geometry_page_size=500});
            var page=DrawingViewsValidation.Parse(read);
            Check(page.BridgeId==Bridge&&page.DocumentId==Document&&page.ExpectedRevision=="47"&&page.Handles is ["A1"]&&page.GeometryViewHandle=="B1"&&page.GeometryStartIndex==1000&&page.GeometryPageSize==500,"Projection continuation lost source/context/page position");
            Invalid(()=>DrawingViewsValidation.Parse(Wire.Element(new{bridge_instance_id=Bridge,document_id=Document,expected_revision="47",layout_handle="A1",geometry_start_index=1})));
            Invalid(()=>DrawingViewsValidation.Parse(Wire.Element(new{bridge_instance_id=Bridge,document_id=Document,expected_revision="47",layout_handle="A1",geometry_view_handle="B1",geometry_page_size=0})));
            return Task.CompletedTask;
        });
    }
}
